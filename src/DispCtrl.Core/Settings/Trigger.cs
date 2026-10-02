using System.Globalization;

namespace DispCtrl.Core.Settings;

/// <summary>Something that happens, which a <see cref="Trigger"/> answers by running a feature.</summary>
/// <remarks>Stored by name, as every enum in the settings file is; new ones are appended.</remarks>
public enum TriggerEvent
{
    /// <summary>A display arrives; <see cref="Trigger.Match"/>, when given, names which.</summary>
    DisplayConnected,
    /// <summary>A display leaves.</summary>
    DisplayDisconnected,
    /// <summary>An app comes to the front; <see cref="Trigger.Match"/> is its executable.</summary>
    AppInFront,
    /// <summary>That app stops being in front.</summary>
    AppLeft,
    /// <summary>Nobody has touched the computer for <see cref="Trigger.Minutes"/>.</summary>
    Idle,
    /// <summary>Somebody is back after that.</summary>
    Back,
    Locked,
    Unlocked,
    /// <summary>Unplugged from the mains.</summary>
    OnBattery,
    /// <summary>Plugged in again.</summary>
    OnPower,
    /// <summary>The computer wakes from sleep.</summary>
    Resumed,
    /// <summary>Every day at <see cref="Trigger.Match"/>, a time written 20:00.</summary>
    AtTime,
}

/// <summary>An event that runs a custom feature: when this happens, do that.</summary>
/// <remarks>
/// A feature is already any list of steps - a dispctrl command, a program, a
/// wait - so a trigger needs to say only when. Fired on the change, not while
/// a state lasts: an app in front runs its feature once when it comes to the
/// front, not every second it stays there.
/// </remarks>
public sealed class Trigger
{
    public bool Enabled { get; set; } = true;
    public TriggerEvent Event { get; set; }

    /// <summary>What the event is about: an executable, a display (number, name, model or token), a time; empty for any.</summary>
    public string Match { get; set; } = "";

    /// <summary>For <see cref="TriggerEvent.Idle"/> and <see cref="TriggerEvent.Back"/>: how long counts as away.</summary>
    public int Minutes { get; set; } = 10;

    /// <summary>The custom feature to run, by name.</summary>
    public string Feature { get; set; } = "";

    public bool NeedsMatch => Event is TriggerEvent.AppInFront or TriggerEvent.AppLeft or TriggerEvent.AtTime;

    /// <summary>What is wrong with this trigger, or null when it is ready to run.</summary>
    public string? Problem(IReadOnlyList<CustomFeature> features)
    {
        if (!Enum.IsDefined(Event)) return "That is not an event DispCtrl knows.";
        if (string.IsNullOrWhiteSpace(Feature)) return "Choose the feature it runs.";
        if (!features.Any(f => f.Name.Trim().Equals(Feature.Trim(), StringComparison.OrdinalIgnoreCase)))
            return $"There is no feature called “{Feature}”.";
        if (NeedsMatch && string.IsNullOrWhiteSpace(Match))
            return Event == TriggerEvent.AtTime ? "Give the time, as 20:00." : "Give the app's executable, as vlc.exe.";
        if (Event == TriggerEvent.AtTime && TimeOfDay is null) return "The time is written 20:00.";
        if (Event is TriggerEvent.Idle or TriggerEvent.Back && Minutes is < 1 or > 1440) return "Away is 1 to 1440 minutes.";
        return null;
    }

    /// <summary>The time of an <see cref="TriggerEvent.AtTime"/> trigger, or null when it is not one.</summary>
    public TimeSpan? TimeOfDay => TimeSpan.TryParseExact(Match.Trim(), @"h\:mm", CultureInfo.InvariantCulture, out TimeSpan at)
        && at.TotalHours < 24 ? at : null;

    /// <summary>Whether an executable is the one this trigger is about, with or without .exe, in any case.</summary>
    public bool MatchesApp(string? image)
    {
        if (string.IsNullOrEmpty(image)) return false;
        return AppRule.ProgramName(image).Equals(AppRule.ProgramName(Match), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether a display is the one this trigger is about: any, when it names none.</summary>
    /// <param name="number">Its number as the app shows it, or 0 when it has left and has none.</param>
    public bool MatchesDisplay(string token, string label, int number)
    {
        string m = Match.Trim();
        if (m.Length == 0) return true;
        if (number > 0 && m == number.ToString(CultureInfo.InvariantCulture)) return true;
        return token.StartsWith(m, StringComparison.OrdinalIgnoreCase) || label.Contains(m, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether a daily time fell between two moments: the last look and this one.</summary>
    /// <remarks>Between, not at: the engine looks seldom, and a look a second late must not miss the minute.</remarks>
    public bool TimeBetween(DateTime after, DateTime upTo)
    {
        if (TimeOfDay is not { } at || upTo <= after) return false;
        for (DateTime day = after.Date; day <= upTo.Date; day = day.AddDays(1))
        {
            DateTime moment = day + at;
            if (moment > after && moment <= upTo) return true;
        }
        return false;
    }

    public string Describe() => Event switch
    {
        TriggerEvent.DisplayConnected => Match.Length == 0 ? "When a display is connected" : $"When {Match} is connected",
        TriggerEvent.DisplayDisconnected => Match.Length == 0 ? "When a display is disconnected" : $"When {Match} is disconnected",
        TriggerEvent.AppInFront => $"When {Match} comes to the front",
        TriggerEvent.AppLeft => $"When {Match} is no longer in front",
        TriggerEvent.Idle => $"After {Minutes} minute{(Minutes == 1 ? "" : "s")} away",
        TriggerEvent.Back => $"On coming back after {Minutes} minute{(Minutes == 1 ? "" : "s")} away",
        TriggerEvent.Locked => "When the computer locks",
        TriggerEvent.Unlocked => "When the computer is unlocked",
        TriggerEvent.OnBattery => "When unplugged from power",
        TriggerEvent.OnPower => "When plugged in",
        TriggerEvent.Resumed => "When the computer wakes",
        TriggerEvent.AtTime => $"Every day at {Match}",
        _ => Event.ToString(),
    } + $", run “{Feature}”";
}
