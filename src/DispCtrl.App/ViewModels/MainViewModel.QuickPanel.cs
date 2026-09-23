using DispCtrl.Core.Settings;

namespace DispCtrl.App.ViewModels;

/// <summary>
/// The quick panel's own settings, and what the customisation page edits them
/// through.
/// </summary>
/// <remarks>
/// The panel itself does not bind to these: it reads <see cref="QuickPanel"/>
/// when it is summoned and builds its rows from it, so changing what it shows
/// takes effect the next time it opens rather than rearranging under a pointer
/// that is on it. <see cref="QuickPanelChanged"/> lets an open panel rebuild
/// anyway, for the page's own preview.
/// </remarks>
public sealed partial class MainViewModel
{
    public QuickPanelSettings QuickPanel => _settings.Global.QuickPanel;

    /// <summary>Raised after anything about the panel's composition is saved.</summary>
    public event Action? QuickPanelChanged;

    public bool QuickPanelEnabled
    {
        get => QuickPanel.Enabled;
        set { if (QuickPanel.Enabled == value) return; QuickPanel.Enabled = value; SaveQuickPanel(composition: false); }
    }

    public bool QuickPanelStayOpen
    {
        get => QuickPanel.StayOpen;
        set { if (QuickPanel.StayOpen == value) return; QuickPanel.StayOpen = value; SaveQuickPanel(composition: false); }
    }

    public bool QuickFooter
    {
        get => QuickPanel.ShowFooter;
        set { if (QuickPanel.ShowFooter == value) return; QuickPanel.ShowFooter = value; SaveQuickPanel(); }
    }

    /// <summary>See <see cref="GlobalSettings.PreloadQuickPanel"/>.</summary>
    public bool PreloadQuickPanel
    {
        get => _settings.Global.PreloadQuickPanel;
        set
        {
            if (_settings.Global.PreloadQuickPanel == value) return;
            _settings.Global.PreloadQuickPanel = value;
            Persist();
            Raise();
            // Now, not only at the next sign-in: the next click is the test.
            if (value && QuickPanel.Enabled) QuickPanelSignal.Preload();
        }
    }

    /// <summary>See <see cref="GlobalSettings.OpenWindowAtSignIn"/>.</summary>
    public bool OpenWindowAtSignIn
    {
        get => _settings.Global.OpenWindowAtSignIn;
        set
        {
            if (_settings.Global.OpenWindowAtSignIn == value) return;
            _settings.Global.OpenWindowAtSignIn = value;
            Persist();
            Raise();
        }
    }

    /// <summary>Brightness sliders only; see <see cref="QuickPanelSettings.Simple"/>.</summary>
    public bool QuickPanelSimple
    {
        get => QuickPanel.Simple;
        set
        {
            if (QuickPanel.Simple == value) return;
            QuickPanel.Simple = value;
            SaveQuickPanel(composition: true);
            Raise(nameof(QuickPanelCustomisable));
        }
    }

    /// <summary>See <see cref="QuickPanelSettings.Locked"/>.</summary>
    public bool QuickPanelLocked
    {
        get => QuickPanel.Locked;
        set { if (QuickPanel.Locked == value) return; QuickPanel.Locked = value; SaveQuickPanel(composition: false); Raise(); }
    }

    /// <summary>What only the full panel uses, greyed on the page in simple mode.</summary>
    public bool QuickPanelCustomisable => !QuickPanel.Simple;

    public bool QuickPanelAnimate
    {
        get => QuickPanel.Animate;
        set { if (QuickPanel.Animate == value) return; QuickPanel.Animate = value; SaveQuickPanel(composition: false); }
    }

    public double QuickPanelWidthMinimum => QuickPanelSettings.MinWidth;
    public double QuickPanelWidthMaximum => QuickPanelSettings.MaxWidth;

    public double QuickPanelWidth
    {
        get => QuickPanel.Width;
        set
        {
            // A slider realised before its value arrives writes its own default
            // back; a cleared box writes NaN. Neither is a request for anything.
            if (!double.IsFinite(value) || value < QuickPanelSettings.MinWidth) return;
            int v = Math.Clamp((int)(Math.Round(value / 10) * 10), QuickPanelSettings.MinWidth, QuickPanelSettings.MaxWidth);
            if (QuickPanel.Width == v) return;
            QuickPanel.Width = v;
            SaveQuickPanel();
        }
    }

    public string QuickPanelWidthText => $"{QuickPanel.Width} DIP";

    public double QuickPanelColumnsMinimum => QuickPanelSettings.MinColumns;
    public double QuickPanelColumnsMaximum => QuickPanelSettings.MaxColumns;

    public double QuickPanelColumns
    {
        get => QuickPanel.TileColumns;
        set
        {
            if (!double.IsFinite(value) || value < QuickPanelSettings.MinColumns) return;
            int v = Math.Clamp((int)Math.Round(value), QuickPanelSettings.MinColumns, QuickPanelSettings.MaxColumns);
            if (QuickPanel.TileColumns == v) return;
            QuickPanel.TileColumns = v;
            SaveQuickPanel();
        }
    }

    public string QuickPanelColumnsText => $"{QuickPanel.TileColumns} per row";

    public bool QuickPanelFixedHeight
    {
        get => QuickPanel.FixedHeight;
        set { if (QuickPanel.FixedHeight == value) return; QuickPanel.FixedHeight = value; SaveQuickPanel(); }
    }

    public double QuickPanelHeight
    {
        get => QuickPanel.Height;
        set
        {
            if (!double.IsFinite(value) || value < QuickPanelSettings.MinHeight) return;
            int v = Math.Clamp((int)(Math.Round(value / 20) * 20), QuickPanelSettings.MinHeight, QuickPanelSettings.MaxHeight);
            if (QuickPanel.Height == v) return;
            QuickPanel.Height = v;
            SaveQuickPanel();
        }
    }

    public string QuickPanelHeightText => $"{QuickPanel.Height} DIP";

    public string[] QuickPanelDensities { get; } = ["Compact", "Comfortable", "Spacious"];

    /// <remarks>
    /// Bound as an index rather than an item, because a <c>ComboBox</c> applies
    /// its selected item before its source is filled and renders blank. The
    /// index is mapped by name, not cast, so the order of the enum is free to
    /// change without moving anybody's choice.
    /// </remarks>
    public int QuickPanelDensityIndex
    {
        get => QuickPanel.Density switch
        {
            QuickPanelDensity.Compact => 0,
            QuickPanelDensity.Spacious => 2,
            _ => 1,
        };
        set
        {
            QuickPanelDensity wanted = value switch
            {
                0 => QuickPanelDensity.Compact,
                2 => QuickPanelDensity.Spacious,
                1 => QuickPanelDensity.Comfortable,
                _ => QuickPanel.Density,
            };
            if (QuickPanel.Density == wanted) return;
            QuickPanel.Density = wanted;
            SaveQuickPanel();
        }
    }

    public string[] QuickPanelIcons { get; } = ["Brightness", "Display", "DispCtrl logo"];

    public int QuickPanelIconIndex
    {
        get => QuickPanel.Icon switch
        {
            TrayIconStyle.Display => 1,
            TrayIconStyle.AppLogo => 2,
            _ => 0,
        };
        set
        {
            TrayIconStyle wanted = value switch
            {
                0 => TrayIconStyle.Brightness,
                1 => TrayIconStyle.Display,
                2 => TrayIconStyle.AppLogo,
                _ => QuickPanel.Icon,
            };
            if (QuickPanel.Icon == wanted) return;
            QuickPanel.Icon = wanted;
            SaveQuickPanel(composition: false);
        }
    }

    public string[] QuickPanelIconColours { get; } = ["Match the taskbar", "Windows accent colour"];

    public int QuickPanelIconColourIndex
    {
        get => QuickPanel.IconColour == TrayIconColour.Accent ? 1 : 0;
        set
        {
            TrayIconColour wanted = value == 1 ? TrayIconColour.Accent : TrayIconColour.Taskbar;
            if (value is < 0 or > 1 || QuickPanel.IconColour == wanted) return;
            QuickPanel.IconColour = wanted;
            SaveQuickPanel(composition: false);
        }
    }

    /// <summary>The icon drawn bolder while Stay active or Keep awake is on.</summary>
    public bool QuickPanelIconShowsActive
    {
        get => QuickPanel.IconShowsActive;
        set
        {
            if (QuickPanel.IconShowsActive == value) return;
            QuickPanel.IconShowsActive = value;
            SaveQuickPanel(composition: false);
        }
    }

    // ---- where Windows puts the icon ----

    /// <summary>Whether Windows has a record of the icon it could move.</summary>
    public bool QuickPanelCanPromote => TrayIconPromotion.IsPromoted(_engine.EnginePath) is not null;

    /// <summary>On the taskbar rather than behind the ^.</summary>
    public bool QuickPanelOnTaskbar
    {
        get => TrayIconPromotion.IsPromoted(_engine.EnginePath) == true;
        set
        {
            if (QuickPanelOnTaskbar == value) return;
            if (!TrayIconPromotion.SetPromoted(_engine.EnginePath, value))
                AppearanceStatus = "Windows has no record of the icon yet. Show it once, then try again.";
            Raise(nameof(QuickPanelOnTaskbar));
        }
    }

    public string QuickPanelOnTaskbarDescription => QuickPanelCanPromote
        ? "Windows 11 puts new icons behind the ^ at the end of the taskbar. This is the same switch as Settings, Personalization, Taskbar, Other system tray icons. Dragging the icon out of the ^ does the same."
        : "Windows keeps this switch per icon, and has no record of DispCtrl's until the icon has been shown once. Turn the icon on above, then come back.";

    /// <summary>Re-reads the promotion state, which Windows' own Settings can change.</summary>
    public void RefreshQuickPanelPromotion()
    {
        Raise(nameof(QuickPanelCanPromote));
        Raise(nameof(QuickPanelOnTaskbar));
        Raise(nameof(QuickPanelOnTaskbarDescription));
    }

    /// <summary>Whether the one shared night light strength is what gets applied.</summary>
    public bool QuickNightLightShared => Night.SharedApplies;

    // ---- the four lists ----

    public void SetQuickPanelItem(QuickPanelGroup group, string id, bool visible)
    {
        QuickPanelItem? item = QuickPanel.List(group).Find(i => i.Id == id);
        if (item is null || item.Visible == visible) return;
        item.Visible = visible;
        SaveQuickPanel();
    }

    public bool MoveQuickPanelItem(QuickPanelGroup group, string id, int by)
    {
        if (!QuickPanelCatalog.Move(QuickPanel.List(group), id, by)) return false;
        SaveQuickPanel();
        return true;
    }

    /// <summary>Takes a group's new order from a drag on the customisation page.</summary>
    public void ReorderQuickPanel(QuickPanelGroup group, IReadOnlyList<string> shown)
    {
        QuickPanel.Reorder(group, shown);
        SaveQuickPanel();
    }

    /// <summary>Folds or unfolds a section or a display in the panel.</summary>
    /// <remarks>
    /// Not a composition change: the panel folds the block itself, in place.
    /// Rebuilding and re-placing it for a fold would snap it back to its corner.
    /// </remarks>
    public void SetQuickPanelCollapsed(string key, bool collapsed)
    {
        if (QuickPanel.IsCollapsed(key) == collapsed) return;
        QuickPanel.SetCollapsed(key, collapsed);
        SaveQuickPanel(composition: false);
    }

    // ---- tiles somebody made ----

    public IReadOnlyList<QuickPanelCustomTile> QuickCustomTiles => QuickPanel.CustomTiles;

    /// <summary>Adds a custom tile, shown, at the end of the quick toggles.</summary>
    public void AddQuickCustomTile(QuickPanelCustomTile tile)
    {
        QuickPanel.CustomTiles.Add(tile);
        QuickPanel.List(QuickPanelGroup.Tiles);
        SaveQuickPanel();
    }

    /// <summary>Saves a custom tile after it was edited in place.</summary>
    public void UpdateQuickCustomTile(QuickPanelCustomTile tile)
    {
        if (QuickPanel.Custom(tile.Id) is null) return;
        SaveQuickPanel();
    }

    public void RemoveQuickCustomTile(string id)
    {
        if (QuickPanel.CustomTiles.RemoveAll(t => t.Id == id) == 0) return;
        QuickPanel.List(QuickPanelGroup.Tiles);
        SaveQuickPanel();
    }

    /// <summary>Whether this display appears in the panel.</summary>
    public bool ShowsInQuickPanel(string token) => QuickPanel.Shows(token);

    /// <summary>Adds or removes a display from the panel.</summary>
    public void SetShowsInQuickPanel(string token, bool shows)
    {
        if (QuickPanel.Shows(token) == shows) return;

        if (shows) QuickPanel.HiddenDisplays.Remove(token);
        else QuickPanel.HiddenDisplays.Add(token);

        SaveQuickPanel();
    }

    /// <summary>What the panel will look like, in a sentence.</summary>
    /// <remarks>
    /// Counts rather than a list: with everything on it is dozens of entries,
    /// and the lists below already say which. What is worth saying on the card
    /// is whether the panel has anything in it at all, because a panel with
    /// every section off opens empty and looks broken.
    /// </remarks>
    public string QuickPanelSummary
    {
        get
        {
            if (!QuickPanel.Enabled) return "The icon is hidden, so the panel cannot be reached from the taskbar.";

            int sections = QuickPanel.Shown(QuickPanelGroup.Sections).Count();
            if (sections == 0) return "Every section is switched off, so the panel would open empty.";

            int tiles = QuickPanel.IsShown(QuickPanelGroup.Sections, "tiles") ? QuickPanel.Shown(QuickPanelGroup.Tiles).Count() : 0;
            int rows = QuickPanel.IsShown(QuickPanelGroup.Sections, "displays") ? QuickPanel.Shown(QuickPanelGroup.DisplayRows).Count() : 0;

            int displays = 0;
            foreach (DisplayViewModel d in Displays)
                if (QuickPanel.Shows(d.Token)) displays++;

            return $"{sections} section{Plural(sections)}, {tiles} quick toggle{Plural(tiles)}, "
                 + $"and {rows} row{Plural(rows)} on each of {displays} display{Plural(displays)}.";
        }
    }

    private static string Plural(int n) => n == 1 ? "" : "s";

    public void ResetQuickPanel()
    {
        QuickPanel.ResetToDefaults();
        SaveQuickPanel();
    }

    /// <param name="composition">
    /// Whether what the panel shows changed. The pin and the tray icon do not,
    /// and rebuilding for them would snap a panel that had been dragged aside
    /// back to its corner.
    /// </param>
    private void SaveQuickPanel(bool composition = true)
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
        if (composition) QuickPanelChanged?.Invoke();
    }

    private void RaiseQuickPanel()
    {
        foreach (string name in new[]
        {
            nameof(QuickPanelEnabled), nameof(QuickPanelStayOpen), nameof(QuickPanelAnimate), nameof(QuickFooter),
            nameof(QuickPanelSimple), nameof(QuickPanelCustomisable), nameof(QuickPanelLocked),
            nameof(QuickPanelWidth), nameof(QuickPanelWidthText),
            nameof(QuickPanelColumns), nameof(QuickPanelColumnsText),
            nameof(QuickPanelDensityIndex), nameof(QuickPanelIconIndex), nameof(QuickPanelIconColourIndex), nameof(QuickPanelIconShowsActive), nameof(QuickPanelSummary),
        })
        {
            Raise(name);
        }
    }
}
