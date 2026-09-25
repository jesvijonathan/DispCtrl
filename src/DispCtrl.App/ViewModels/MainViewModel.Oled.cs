using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DispCtrl.Core.Settings;
using Microsoft.UI.Xaml;

namespace DispCtrl.App.ViewModels;

/// <summary>One monitor in OLED care's list: connected or remembered.</summary>
/// <remarks>
/// A connected monitor's switches go through its display's own view model, so
/// the card lower on the page and this list never disagree; one that is not
/// connected is written in the settings directly, and applies when it returns.
/// </remarks>
public sealed class OledMonitorItem : INotifyPropertyChanged
{
    private readonly MonitorSettings settings;
    private readonly DisplayViewModel? display;
    private readonly Action persist;

    public OledMonitorItem(string token, MonitorSettings settings, DisplayViewModel? display, Action persist)
    {
        Token = token;
        this.settings = settings;
        this.display = display;
        this.persist = persist;
        // The display's own card can change the same two switches.
        if (display is not null) display.PropertyChanged += OnDisplayChanged;
    }

    private void OnDisplayChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DisplayViewModel.IsOled) or "" or null) { Raise(nameof(IsOled)); Raise(nameof(Protected)); }
        else if (e.PropertyName == nameof(DisplayViewModel.OledProtectionEnabled)) Raise(nameof(Protected));
    }

    /// <summary>Lets go of the display: the list is rebuilt, the display outlives it.</summary>
    public void Detach()
    {
        if (display is not null) display.PropertyChanged -= OnDisplayChanged;
    }

    public string Token { get; }
    public string Name => display?.Name ?? (string.IsNullOrWhiteSpace(settings.Label) ? Token : settings.Label);

    public string Status => display is not null
        ? $"Display {display.Number}  ·  connected"
        : settings.LastSeenUtc is { } seen ? $"Not connected  ·  last seen {Ago(seen)}" : "Not connected";

    public bool IsOled
    {
        get => display?.IsOled ?? settings.TreatAsOled;
        set
        {
            if (IsOled == value) return;
            if (display is not null) display.IsOled = value;
            else { settings.IsOled = value; persist(); }
            Raise();
            Raise(nameof(Protected));
        }
    }

    /// <summary>Whether OLED care reaches this monitor: marked OLED and its own protection on.</summary>
    public bool Protected
    {
        get => display?.OledProtectionEnabled ?? settings.OledProtection;
        set
        {
            if (Protected == value) return;
            if (display is not null) display.OledProtectionEnabled = value;
            else { settings.OledProtection = value; persist(); }
            Raise();
        }
    }

    public string OledAutomationName => $"OledMonitor {Name}";
    public string ProtectAutomationName => $"OledProtect {Name}";

    private static string Ago(DateTimeOffset when)
    {
        TimeSpan age = DateTimeOffset.UtcNow - when;
        if (age < TimeSpan.FromMinutes(2)) return "just now";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} minutes ago";
        if (age < TimeSpan.FromDays(1)) return $"{(int)age.TotalHours} hour{((int)age.TotalHours == 1 ? "" : "s")} ago";
        if (age < TimeSpan.FromDays(60)) return $"{(int)age.TotalDays} day{((int)age.TotalDays == 1 ? "" : "s")} ago";
        return when.ToLocalTime().ToString("d MMM yyyy");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed partial class MainViewModel
{
    /// <summary>Every monitor OLED care could reach: connected first, then by when each was last seen.</summary>
    public ObservableCollection<OledMonitorItem> OledMonitors { get; } = [];

    public void RefreshOledMonitors()
    {
        foreach (OledMonitorItem old in OledMonitors) old.Detach();
        OledMonitors.Clear();
        foreach (DisplayViewModel d in Displays)
            OledMonitors.Add(new OledMonitorItem(d.Token, _settings.For(d.Token), d, Persist));
        var connected = Displays.Select(d => d.Token).ToHashSet(StringComparer.Ordinal);
        foreach (var (token, monitor) in _settings.Monitors
                     .Where(p => !connected.Contains(p.Key))
                     .OrderByDescending(p => p.Value.LastSeenUtc ?? DateTimeOffset.MinValue))
            OledMonitors.Add(new OledMonitorItem(token, monitor, null, Persist));
        Raise(nameof(OledCoverage));
    }

    public bool OledPerDisplayActivity
    {
        get => Care.PerDisplayActivity;
        set { if (Care.PerDisplayActivity == value) return; Care.PerDisplayActivity = value; SaveProtection(); }
    }

    public string OledExcludedApps
    {
        get => Care.ExcludedApps;
        set { if (Care.ExcludedApps == value) return; Care.ExcludedApps = value ?? ""; SaveProtection(); }
    }

    // ---- focus mode: which windows stay clear, as one choice ----

    public string[] FocusClearChoices { get; } =
        ["The focused window", "The window under the pointer", "Both the focused window and the one under the pointer"];

    private bool _focusClearReady;

    public int FocusClearIndex
    {
        get { _focusClearReady = true; return (int)Focus.Clear; }
        set
        {
            if (!_focusClearReady || value is < 0 or > 2 || (int)Focus.Clear == value) return;
            Focus.Clear = (FocusClear)value;
            SaveProtection();
        }
    }
}
