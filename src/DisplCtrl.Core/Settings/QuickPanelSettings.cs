namespace DisplCtrl.Core.Settings;

/// <summary>How tightly the quick panel packs its rows.</summary>
public enum QuickPanelDensity
{
    /// <summary>Full rows with descriptions, like the pages in the app.</summary>
    Comfortable,

    /// <summary>Labels and controls only, for a panel that is reached at constantly.</summary>
    Compact,
}

/// <summary>
/// What the quick panel shows, and how.
/// </summary>
/// <remarks>
/// Deliberately a flat set of switches rather than a layout description. The
/// panel has to be able to be two brightness sliders for somebody who wants two
/// brightness sliders, and everything DisplCtrl does for somebody who wants
/// that, and a list of booleans expresses both without inventing a format that
/// has to be edited by hand to be understood.
/// <para>
/// Every section defaults to what a first run should look like: the brightness
/// sliders that are the reason to open it at all, and little else. Turning more
/// on is a deliberate act, which is the right way round for a panel that is
/// meant to open, be used and close.
/// </para>
/// </remarks>
public sealed class QuickPanelSettings
{
    /// <summary>Whether the panel can be summoned at all.</summary>
    public bool Enabled { get; set; } = true;

    public QuickPanelDensity Density { get; set; } = QuickPanelDensity.Comfortable;

    /// <summary>
    /// Panel width in DIP.
    /// </summary>
    /// <remarks>
    /// 360 is what Windows' own quick settings uses, which is the width people
    /// already expect something summoned from the tray to be.
    /// </remarks>
    public int Width { get; set; } = 360;

    /// <summary>
    /// Keep the panel open when it loses focus.
    /// </summary>
    /// <remarks>
    /// Off by default because a flyout that outstays its welcome is a window,
    /// not a flyout. On while somebody is working through several settings and
    /// does not want it closing every time they click a display.
    /// </remarks>
    public bool StayOpen { get; set; }

    // ---- per display ----

    public bool ShowBrightness { get; set; } = true;
    public bool ShowSoftwareDimming { get; set; }
    public bool ShowPerDisplayWarmth { get; set; }
    public bool ShowInputSource { get; set; }
    public bool ShowHideTaskbar { get; set; }
    public bool ShowScreenRest { get; set; }

    // ---- the whole desk ----

    public bool ShowUnison { get; set; } = true;
    public bool ShowNightLight { get; set; } = true;
    public bool ShowDarkMode { get; set; }
    public bool ShowFocusMode { get; set; }
    public bool ShowOledIdle { get; set; }
    public bool ShowArrangement { get; set; }
    public bool ShowPresets { get; set; }

    // ---- ends ----

    public bool ShowIdentify { get; set; } = true;

    /// <summary>The row that opens the full app, at the bottom.</summary>
    public bool ShowFooter { get; set; } = true;

    /// <summary>
    /// Identity tokens of displays the panel leaves out.
    /// </summary>
    /// <remarks>
    /// By token rather than by number, so a display that is unplugged and put
    /// back on another port stays hidden instead of hiding whichever display
    /// inherited its position.
    /// </remarks>
    public List<string> HiddenDisplays { get; set; } = [];

    /// <summary>True when this display should appear in the panel.</summary>
    public bool Shows(string token) => !HiddenDisplays.Contains(token);

    /// <summary>Whether anything at all is turned on for one display's block.</summary>
    public bool AnyPerDisplay =>
        ShowBrightness || ShowSoftwareDimming || ShowPerDisplayWarmth
        || ShowInputSource || ShowHideTaskbar || ShowScreenRest;

    /// <summary>Restores the shipped defaults.</summary>
    public void ResetToDefaults()
    {
        var fresh = new QuickPanelSettings();

        Density = fresh.Density;
        Width = fresh.Width;
        StayOpen = fresh.StayOpen;
        ShowBrightness = fresh.ShowBrightness;
        ShowSoftwareDimming = fresh.ShowSoftwareDimming;
        ShowPerDisplayWarmth = fresh.ShowPerDisplayWarmth;
        ShowInputSource = fresh.ShowInputSource;
        ShowHideTaskbar = fresh.ShowHideTaskbar;
        ShowScreenRest = fresh.ShowScreenRest;
        ShowUnison = fresh.ShowUnison;
        ShowNightLight = fresh.ShowNightLight;
        ShowDarkMode = fresh.ShowDarkMode;
        ShowFocusMode = fresh.ShowFocusMode;
        ShowOledIdle = fresh.ShowOledIdle;
        ShowArrangement = fresh.ShowArrangement;
        ShowPresets = fresh.ShowPresets;
        ShowIdentify = fresh.ShowIdentify;
        ShowFooter = fresh.ShowFooter;

        // Which displays are hidden is a statement about this desk, not a
        // preference about the panel, so a reset of the panel leaves it alone.
    }
}
