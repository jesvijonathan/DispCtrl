using Microsoft.UI.Xaml;
using DispCtrl.Core.Settings;

namespace DispCtrl.App.ViewModels;

public sealed partial class MainViewModel
{
    private AwakeSettings Awake => _settings.Global.Awake;
    private bool _awakeModeReady;

    public string[] AwakeModes { get; } =
    [
        "Use the selected power plan",
        "Keep awake indefinitely",
        "Keep awake for a time interval",
        "Keep awake until expiration",
    ];

    public int AwakeModeIndex
    {
        get { _awakeModeReady = true; return (int)Awake.Mode; }
        set
        {
            if (!_awakeModeReady || value < 0 || value > 3 || value == (int)Awake.Mode) return;
            Awake.Mode = (AwakeMode)value;
            if (Awake.Mode == AwakeMode.Timed) ResetAwakeInterval();
            SaveAwake();
        }
    }

    public bool AwakeKeepDisplaysOn
    {
        get => Awake.KeepDisplaysOn;
        set { if (Awake.KeepDisplaysOn == value) return; Awake.KeepDisplaysOn = value; SaveAwake(); }
    }

    /// <summary>Screen on and the session looking attended, until switched off. No timer.</summary>
    public bool AwakeStayActive
    {
        get => Awake.StayActive;
        set { if (Awake.StayActive == value) return; Awake.StayActive = value; SaveAwake(); }
    }

    // ---- turn off displays ----

    /// <summary>"Turn off displays": on from the moment it is asked for until every display it turned off is woken.</summary>
    /// <remarks>
    /// A request the engine carries out after <see cref="DisplaysOffDelay"/> and
    /// clears itself, so this reads as off again once they are all back; the
    /// settings watcher raises it when that happens.
    /// </remarks>
    public bool DisplaysOff
    {
        get => Awake.DisplaysOffUtc is not null;
        set
        {
            if ((Awake.DisplaysOffUtc is not null) == value) return;
            Awake.DisplaysOffUtc = value ? DateTimeOffset.UtcNow : null;
            SaveAwake();
        }
    }

    public double DisplaysOffLevel { get => Awake.DisplaysOffPercent; set { int v = Number(value, 50, 100); if (Awake.DisplaysOffPercent == v) return; Awake.DisplaysOffPercent = v; PersistSoon(); Raise(); Raise(nameof(DisplaysOffBacklightAvailable)); } }
    public double DisplaysOffDelay { get => Awake.DisplaysOffDelaySeconds; set { int v = Number(value, 0, 30); if (Awake.DisplaysOffDelaySeconds == v) return; Awake.DisplaysOffDelaySeconds = v; PersistSoon(); Raise(); } }
    public bool DisplaysOffWakeOnPointer { get => Awake.DisplaysOffWakeOnPointer; set { if (Awake.DisplaysOffWakeOnPointer == value) return; Awake.DisplaysOffWakeOnPointer = value; SaveAwake(); } }
    public bool DisplaysOffHidePointer { get => Awake.DisplaysOffHidePointer; set { if (Awake.DisplaysOffHidePointer == value) return; Awake.DisplaysOffHidePointer = value; SaveAwake(); } }
    public bool DisplaysOffLockOnWake { get => Awake.DisplaysOffLockOnWake; set { if (Awake.DisplaysOffLockOnWake == value) return; Awake.DisplaysOffLockOnWake = value; SaveAwake(); } }
    public bool DisplaysOffBacklight { get => Awake.DisplaysOffBacklight; set { if (Awake.DisplaysOffBacklight == value) return; Awake.DisplaysOffBacklight = value; SaveAwake(); } }

    /// <summary>The backlight switch only means something when "off" is dark enough to be off.</summary>
    public bool DisplaysOffBacklightAvailable => Awake.DisplaysOffPercent >= AwakeSettings.BacklightFromPercent;

    /// <summary>The choices for <see cref="DisplaysOffTargetName"/>, in <see cref="DisplaysOffTarget"/> order.</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> DisplaysOffTargets { get; } =
    [
        "Every display",
        "All but the main display",
        "All but the one with the pointer",
        "All but the active window's",
        "Only the main display",
    ];

    public string? DisplaysOffTargetName
    {
        get => DisplaysOffTargets[Math.Clamp((int)Awake.DisplaysOffTarget, 0, DisplaysOffTargets.Count - 1)];
        set
        {
            int i = value is null ? -1 : DisplaysOffTargets.IndexOf(value);
            if (i < 0 || (int)Awake.DisplaysOffTarget == i) return;
            Awake.DisplaysOffTarget = (DisplaysOffTarget)i;
            SaveAwake();
        }
    }

    public int DisplaysOffTargetIndex
    {
        get => (int)Awake.DisplaysOffTarget;
        set => DisplaysOffTargetName = value >= 0 && value < DisplaysOffTargets.Count ? DisplaysOffTargets[value] : null;
    }

    /// <summary>The shortcut that turns the displays off, as bound on the Hotkeys page, if one is.</summary>
    public string DisplaysOffShortcut
    {
        get
        {
            Hotkey? key = _settings.Hotkeys.FirstOrDefault(h => h.Action == HotkeyAction.DisplaysOffToggle && h.Enabled && h.Key != 0);
            return key is null
                ? "No shortcut: add one for Turn the displays off on the Hotkeys page."
                : $"{key.Describe()} turns them off, and again back on.";
        }
    }

    public double AwakeHours
    {
        get => Awake.IntervalHours;
        set { int v = Number(value, 0, 168); if (Awake.IntervalHours == v) return; Awake.IntervalHours = v; ResetAwakeInterval(); SaveAwake(); }
    }

    public double AwakeMinutes
    {
        get => Awake.IntervalMinutes;
        set { int v = Number(value, 0, 59); if (Awake.IntervalMinutes == v) return; Awake.IntervalMinutes = v; ResetAwakeInterval(); SaveAwake(); }
    }

    public DateTimeOffset AwakeExpirationDate
    {
        get => Awake.ExpirationUtc.ToLocalTime();
        set
        {
            DateTimeOffset local = Awake.ExpirationUtc.ToLocalTime();

            // Unchanged is not a change. See AwakeExpirationTime: without this
            // the picker and the setter answered each other until the stack ran out.
            if (value.Date == local.Date) return;

            Awake.ExpirationUtc = new DateTimeOffset(value.Year, value.Month, value.Day,
                local.Hour, local.Minute, 0, TimeZoneInfo.Local.GetUtcOffset(value.Date)).ToUniversalTime();
            SaveAwake();
        }
    }

    public TimeSpan AwakeExpirationTime
    {
        get => Awake.ExpirationUtc.ToLocalTime().TimeOfDay;
        set
        {
            DateTimeOffset local = Awake.ExpirationUtc.ToLocalTime();

            // Compared at the picker's own resolution. A TimePicker holds whole
            // minutes and the stored expiry carries seconds, so every value the
            // picker wrote back differed from the one just raised to it: the
            // setter saved and raised again, the picker wrote back again, and a
            // reload with the Displays page open recursed 3,430 frames deep and
            // killed the process.
            if ((int)local.TimeOfDay.TotalMinutes == (int)value.TotalMinutes) return;

            DateTime date = local.Date.Add(value);
            Awake.ExpirationUtc = new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date)).ToUniversalTime();
            SaveAwake();
        }
    }

    public Visibility AwakeTimedVisibility => Awake.Mode == AwakeMode.Timed ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AwakeExpirationVisibility => Awake.Mode == AwakeMode.Expiration ? Visibility.Visible : Visibility.Collapsed;
    public bool AwakeOptionsEnabled => Awake.Mode != AwakeMode.PowerPlan;

    public string AwakeStatus
    {
        get
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (Awake.StayActive) return "Staying active: the screen stays on and you never show as Away.";
            return Awake.Mode switch
            {
                AwakeMode.PowerPlan => "Windows is using the selected power plan.",
                AwakeMode.Indefinite => Awake.KeepDisplaysOn ? "Keeping the PC and displays awake." : "Keeping the PC awake; displays may turn off.",
                AwakeMode.Timed when Awake.TimedUntilUtc <= now => "The keep-awake interval has ended.",
                AwakeMode.Timed => $"Keeping awake for {Remaining(Awake.TimedUntilUtc - now)} more.",
                AwakeMode.Expiration when Awake.ExpirationUtc <= now => "The expiration time has passed.",
                _ => $"Keeping awake until {Awake.ExpirationUtc.ToLocalTime():g}.",
            };
        }
    }

    private void ResetAwakeInterval()
    {
        int minutes = Math.Max(1, (Awake.IntervalHours * 60) + Awake.IntervalMinutes);
        Awake.TimedUntilUtc = DateTimeOffset.UtcNow.AddMinutes(minutes);
    }

    private static string Remaining(TimeSpan value) => value.TotalHours >= 1
        ? $"{(int)value.TotalHours}h {value.Minutes}m"
        : $"{Math.Max(1, value.Minutes)}m";

    private void SaveAwake()
    {
        Persist();
        if (Awake.ActiveAt(DateTimeOffset.UtcNow) || Awake.StayActive || Awake.DisplaysOffUtc is not null) _engine.Start();
        foreach (string name in new[] { nameof(AwakeModeIndex), nameof(AwakeKeepDisplaysOn), nameof(AwakeStayActive), nameof(AwakeHours),
            nameof(DisplaysOff), nameof(DisplaysOffLevel), nameof(DisplaysOffDelay), nameof(DisplaysOffWakeOnPointer),
            nameof(DisplaysOffHidePointer), nameof(DisplaysOffLockOnWake), nameof(DisplaysOffBacklight), nameof(DisplaysOffBacklightAvailable), nameof(DisplaysOffTargetName), nameof(DisplaysOffTargetIndex), nameof(DisplaysOffShortcut),
            nameof(AwakeMinutes), nameof(AwakeExpirationDate), nameof(AwakeExpirationTime), nameof(AwakeTimedVisibility),
            nameof(AwakeExpirationVisibility), nameof(AwakeOptionsEnabled), nameof(AwakeStatus) }) Raise(name);
    }

    public void RaiseAwakeSettings()
    {
        foreach (string name in new[] { nameof(AwakeModeIndex), nameof(AwakeKeepDisplaysOn), nameof(AwakeStayActive), nameof(AwakeHours),
            nameof(DisplaysOff), nameof(DisplaysOffLevel), nameof(DisplaysOffDelay), nameof(DisplaysOffWakeOnPointer),
            nameof(DisplaysOffHidePointer), nameof(DisplaysOffLockOnWake), nameof(DisplaysOffBacklight), nameof(DisplaysOffBacklightAvailable), nameof(DisplaysOffTargetName), nameof(DisplaysOffTargetIndex), nameof(DisplaysOffShortcut),
            nameof(AwakeMinutes), nameof(AwakeExpirationDate), nameof(AwakeExpirationTime), nameof(AwakeTimedVisibility),
            nameof(AwakeExpirationVisibility), nameof(AwakeOptionsEnabled), nameof(AwakeStatus) }) Raise(name);
    }

    public void RaiseAwakeStatus() => Raise(nameof(AwakeStatus));

    public void ResetAwakeSettings()
    {
        _settings.Global.Awake = new AwakeSettings();
        SaveAwake();
    }
}
