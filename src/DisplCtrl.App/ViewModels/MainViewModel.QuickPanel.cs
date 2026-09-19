using DisplCtrl.Core.Settings;

namespace DisplCtrl.App.ViewModels;

/// <summary>
/// The quick panel's own settings, and the switches the customisation page
/// edits them through.
/// </summary>
/// <remarks>
/// Every switch here is a plain bool that saves on change, because that is all
/// the panel's composition is. The panel itself does not bind to these: it
/// reads <see cref="QuickPanel"/> once when it is summoned and builds its rows
/// from it, so changing what it shows takes effect the next time it opens
/// rather than rearranging under a pointer that is on it.
/// </remarks>
public sealed partial class MainViewModel
{
    public QuickPanelSettings QuickPanel => _settings.Global.QuickPanel;

    public bool QuickPanelEnabled
    {
        get => QuickPanel.Enabled;
        set { if (QuickPanel.Enabled == value) return; QuickPanel.Enabled = value; SaveQuickPanel(); }
    }

    public bool QuickPanelStayOpen
    {
        get => QuickPanel.StayOpen;
        set { if (QuickPanel.StayOpen == value) return; QuickPanel.StayOpen = value; SaveQuickPanel(); }
    }

    public double QuickPanelWidth
    {
        get => QuickPanel.Width;
        set
        {
            // 280 is about the narrowest a slider with a label and a value can
            // be without the value wrapping; 520 is where it stops reading as a
            // flyout and starts reading as a window.
            int v = Number(value, 280, 520);
            if (QuickPanel.Width == v) return;
            QuickPanel.Width = v;
            SaveQuickPanel();
        }
    }

    public string[] QuickPanelDensities { get; } = ["Comfortable", "Compact"];

    /// <remarks>
    /// Bound as an index rather than an item, because a <c>ComboBox</c> applies
    /// its selected item before its source is filled and renders blank.
    /// </remarks>
    public int QuickPanelDensityIndex
    {
        get => QuickPanel.Density == QuickPanelDensity.Compact ? 1 : 0;
        set
        {
            QuickPanelDensity wanted = value == 1 ? QuickPanelDensity.Compact : QuickPanelDensity.Comfortable;
            if (QuickPanel.Density == wanted) return;
            QuickPanel.Density = wanted;
            SaveQuickPanel();
        }
    }

    // ---- per display ----

    public bool QuickBrightness
    {
        get => QuickPanel.ShowBrightness;
        set { if (QuickPanel.ShowBrightness == value) return; QuickPanel.ShowBrightness = value; SaveQuickPanel(); }
    }

    public bool QuickSoftwareDimming
    {
        get => QuickPanel.ShowSoftwareDimming;
        set { if (QuickPanel.ShowSoftwareDimming == value) return; QuickPanel.ShowSoftwareDimming = value; SaveQuickPanel(); }
    }

    public bool QuickWarmth
    {
        get => QuickPanel.ShowPerDisplayWarmth;
        set { if (QuickPanel.ShowPerDisplayWarmth == value) return; QuickPanel.ShowPerDisplayWarmth = value; SaveQuickPanel(); }
    }

    public bool QuickInputSource
    {
        get => QuickPanel.ShowInputSource;
        set { if (QuickPanel.ShowInputSource == value) return; QuickPanel.ShowInputSource = value; SaveQuickPanel(); }
    }

    public bool QuickHideTaskbar
    {
        get => QuickPanel.ShowHideTaskbar;
        set { if (QuickPanel.ShowHideTaskbar == value) return; QuickPanel.ShowHideTaskbar = value; SaveQuickPanel(); }
    }

    public bool QuickScreenRest
    {
        get => QuickPanel.ShowScreenRest;
        set { if (QuickPanel.ShowScreenRest == value) return; QuickPanel.ShowScreenRest = value; SaveQuickPanel(); }
    }

    // ---- the whole desk ----

    public bool QuickUnison
    {
        get => QuickPanel.ShowUnison;
        set { if (QuickPanel.ShowUnison == value) return; QuickPanel.ShowUnison = value; SaveQuickPanel(); }
    }

    public bool QuickNightLight
    {
        get => QuickPanel.ShowNightLight;
        set { if (QuickPanel.ShowNightLight == value) return; QuickPanel.ShowNightLight = value; SaveQuickPanel(); }
    }

    public bool QuickDarkMode
    {
        get => QuickPanel.ShowDarkMode;
        set { if (QuickPanel.ShowDarkMode == value) return; QuickPanel.ShowDarkMode = value; SaveQuickPanel(); }
    }

    public bool QuickFocusMode
    {
        get => QuickPanel.ShowFocusMode;
        set { if (QuickPanel.ShowFocusMode == value) return; QuickPanel.ShowFocusMode = value; SaveQuickPanel(); }
    }

    public bool QuickOledIdle
    {
        get => QuickPanel.ShowOledIdle;
        set { if (QuickPanel.ShowOledIdle == value) return; QuickPanel.ShowOledIdle = value; SaveQuickPanel(); }
    }

    public bool QuickArrangement
    {
        get => QuickPanel.ShowArrangement;
        set { if (QuickPanel.ShowArrangement == value) return; QuickPanel.ShowArrangement = value; SaveQuickPanel(); }
    }

    public bool QuickPresets
    {
        get => QuickPanel.ShowPresets;
        set { if (QuickPanel.ShowPresets == value) return; QuickPanel.ShowPresets = value; SaveQuickPanel(); }
    }

    public bool QuickIdentify
    {
        get => QuickPanel.ShowIdentify;
        set { if (QuickPanel.ShowIdentify == value) return; QuickPanel.ShowIdentify = value; SaveQuickPanel(); }
    }

    public bool QuickFooter
    {
        get => QuickPanel.ShowFooter;
        set { if (QuickPanel.ShowFooter == value) return; QuickPanel.ShowFooter = value; SaveQuickPanel(); }
    }

    /// <summary>What the panel will look like, in a sentence.</summary>
    /// <remarks>
    /// A count rather than a list: with everything on it is a dozen entries,
    /// and the switches above already say which. What is worth saying on the
    /// card is whether the panel has anything in it at all, because a panel
    /// with every section off opens empty and looks broken.
    /// </remarks>
    public string QuickPanelSummary
    {
        get
        {
            if (!QuickPanel.Enabled) return "The tray icon is hidden, so the panel cannot be reached.";

            int perDisplay = Count(
                QuickPanel.ShowBrightness, QuickPanel.ShowSoftwareDimming, QuickPanel.ShowPerDisplayWarmth,
                QuickPanel.ShowInputSource, QuickPanel.ShowHideTaskbar, QuickPanel.ShowScreenRest);

            int desk = Count(
                QuickPanel.ShowUnison, QuickPanel.ShowNightLight, QuickPanel.ShowDarkMode,
                QuickPanel.ShowFocusMode, QuickPanel.ShowOledIdle, QuickPanel.ShowArrangement,
                QuickPanel.ShowPresets, QuickPanel.ShowIdentify);

            if (perDisplay == 0 && desk == 0)
                return "Nothing is turned on, so the panel would open empty.";

            int shown = 0;
            foreach (DisplayViewModel d in Displays)
                if (QuickPanel.Shows(d.Token)) shown++;

            string displays = perDisplay == 0
                ? "nothing per display"
                : $"{perDisplay} control{(perDisplay == 1 ? "" : "s")} on each of {shown} display{(shown == 1 ? "" : "s")}";

            return $"{displays}, and {desk} for the whole desk.";
        }
    }

    private static int Count(params bool[] flags)
    {
        int n = 0;
        foreach (bool flag in flags) if (flag) n++;
        return n;
    }

    /// <summary>Whether this display appears in the panel.</summary>
    public bool ShowsInQuickPanel(string token) => QuickPanel.Shows(token);

    /// <summary>Adds or removes a display from the panel.</summary>
    public void SetShowsInQuickPanel(string token, bool shows)
    {
        bool was = QuickPanel.Shows(token);
        if (was == shows) return;

        if (shows) QuickPanel.HiddenDisplays.Remove(token);
        else QuickPanel.HiddenDisplays.Add(token);

        SaveQuickPanel();
    }

    public void ResetQuickPanel()
    {
        QuickPanel.ResetToDefaults();
        SaveQuickPanel();
    }

    private void SaveQuickPanel()
    {
        Persist();

        // The icon lives in the engine, so turning it on with no engine running
        // would leave the switch on and the notification area empty.
        if (QuickPanel.Enabled)
        {
            try { _engine.Start(); }
            catch (Exception ex) { AppearanceStatus = $"Could not start the engine: {ex.Message}"; }
        }

        RaiseQuickPanel();
    }

    private void RaiseQuickPanel()
    {
        foreach (string name in new[]
        {
            nameof(QuickPanelEnabled), nameof(QuickPanelStayOpen), nameof(QuickPanelWidth),
            nameof(QuickPanelDensityIndex), nameof(QuickBrightness), nameof(QuickSoftwareDimming),
            nameof(QuickWarmth), nameof(QuickInputSource), nameof(QuickHideTaskbar),
            nameof(QuickScreenRest), nameof(QuickUnison), nameof(QuickNightLight),
            nameof(QuickDarkMode), nameof(QuickFocusMode), nameof(QuickOledIdle),
            nameof(QuickArrangement), nameof(QuickPresets), nameof(QuickIdentify),
            nameof(QuickFooter), nameof(QuickPanelSummary),
        })
        {
            Raise(name);
        }
    }
}
