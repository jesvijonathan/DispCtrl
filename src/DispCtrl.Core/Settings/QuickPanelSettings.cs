namespace DispCtrl.Core.Settings;

/// <summary>How tightly the quick panel packs its rows.</summary>
public enum QuickPanelDensity
{
    /// <summary>Icon-only toggles and tight rows, for a panel reached for constantly.</summary>
    Compact,

    /// <summary>Toggles labelled underneath, the way Windows' quick settings are.</summary>
    Comfortable,

    /// <summary>Larger toggles, and every slider named above itself.</summary>
    Spacious,
}

/// <summary>What the notification area icon looks like.</summary>
public enum TrayIconStyle
{
    /// <summary>The sun Windows uses for its own brightness control.</summary>
    Brightness,

    /// <summary>A monitor, for a desk where the panel is more than brightness.</summary>
    Display,

    /// <summary>DispCtrl's own coloured icon, the one on the executables.</summary>
    AppLogo,
}

/// <summary>What colour the notification area glyph is drawn in.</summary>
public enum TrayIconColour
{
    /// <summary>White on a dark taskbar, black on a light one, as Windows draws its own.</summary>
    Taskbar,

    /// <summary>Windows' accent colour, so the icon stands out among the others.</summary>
    Accent,
}

/// <summary>One entry in one of the panel's ordered lists.</summary>
public sealed class QuickPanelItem
{
    public string Id { get; set; } = "";
    public bool Visible { get; set; }

    public QuickPanelItem() { }

    public QuickPanelItem(string id, bool visible)
    {
        Id = id;
        Visible = visible;
    }
}

/// <summary>What a tile somebody made for themselves does when clicked.</summary>
public enum QuickPanelCustomKind
{
    /// <summary>
    /// A <c>dispctrl</c> command line - the same words typed in a terminal.
    /// </summary>
    /// <remarks>
    /// The command line is DispCtrl's scriptable surface, so a tile that runs
    /// one can do anything a script can, and is written in the language the
    /// documentation already uses.
    /// </remarks>
    Command,

    /// <summary>Any program, script, document or URL, opened the way Explorer would.</summary>
    Program,
}

/// <summary>A tile somebody added themselves.</summary>
/// <remarks>
/// Stored in the settings file beside the built-in ones and ordered with them,
/// so a custom tile is not a second-class citizen of the panel: it can sit
/// first, be hidden, or be moved like any other. Nothing here runs except when
/// its tile is clicked.
/// </remarks>
public sealed class QuickPanelCustomTile
{
    /// <summary>Always starts with <see cref="Prefix"/>, so it can never collide with a built-in id.</summary>
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";

    /// <summary>A Segoe Fluent Icons code point, as a one-character string.</summary>
    public string Glyph { get; set; } = "\uE768";

    public QuickPanelCustomKind Kind { get; set; } = QuickPanelCustomKind.Command;

    /// <summary>The <c>dispctrl</c> arguments, or the program, script, document or URL.</summary>
    public string Target { get; set; } = "";

    /// <summary>Arguments for a <see cref="QuickPanelCustomKind.Program"/>; unused for a command.</summary>
    public string Arguments { get; set; } = "";

    public const string Prefix = "custom-";

    public static QuickPanelCustomTile Create(string label) => new()
    {
        Id = Prefix + Guid.NewGuid().ToString("N")[..8],
        Label = label,
    };

    /// <summary>What the hover text says, so a tile always explains itself.</summary>
    public string Hint => Kind == QuickPanelCustomKind.Command
        ? $"Runs: dispctrl {Target}".TrimEnd()
        : $"Opens: {Target} {Arguments}".TrimEnd();
}

/// <summary>Which of the panel's lists an item belongs to.</summary>
public enum QuickPanelGroup
{
    /// <summary>The blocks the panel is made of, top to bottom.</summary>
    Sections,

    /// <summary>The grid of small switches and actions.</summary>
    Tiles,

    /// <summary>The rows under each display.</summary>
    DisplayRows,

    /// <summary>The small switches in each display's own strip.</summary>
    DisplayTiles,
}

/// <summary>
/// Everything the quick panel can show, in one place.
/// </summary>
/// <remarks>
/// The settings file stores only ids, order and visibility. What each id is
/// called, the symbol it wears and what its hover text says live here, so the
/// panel, the page that customises it and anything else that lists them cannot
/// disagree. Ids are the file's vocabulary and are never renamed: a renamed id
/// is a hidden item somebody chose to show, silently reset.
/// </remarks>
public static class QuickPanelCatalog
{
    /// <summary>One item the panel knows how to draw.</summary>
    /// <param name="Glyph">A Segoe Fluent Icons code point.</param>
    /// <param name="Hint">The hover text, and the description on the customisation page.</param>
    public sealed record Entry(string Id, string Label, string Glyph, string Hint, bool ShownByDefault);

    public static IReadOnlyList<Entry> Sections { get; } =
    [
        new("unison", "Unison brightness", "\uE793",
            "One slider that moves every display together. The symbol beside it switches unison on and off.", true),
        new("tiles", "Quick toggles", "\uE8A9",
            "A grid of small switches and actions, like Windows' quick settings.", true),
        new("displays", "Displays", "\uE7F4",
            "A block for each display, with the rows chosen under Each display.", true),
        new("nightLight", "Night light", "\uF08C",
            "Night light's switch and strength. Kept in step with Windows' own.", true),
        new("taskbar", "Taskbar", "\uE75A",
            "Transparency, the taskbar's glass and opacity, and auto-hide, in a block you can fold away.", true),
        new("focus", "Focus", "\uE890",
            "Focus mode's switch, and how far it dims everything but the window in use.", true),
        new("oledCare", "OLED care", "\uE7EF",
            "OLED idle protection's switch, how dim it goes and how soon.", true),
        new("presets", "Presets", "\uE768",
            "Every saved preset as a button that applies it. Presets are still being built, so for now this is a placeholder.", true),
        new("displayMode", "Display mode", "\uEBC6",
            "Extend, duplicate, or one screen only - the choices Win+P offers, one click each.", false),
    ];

    public static IReadOnlyList<Entry> Tiles { get; } =
    [
        new("nightLight", "Night light", "\uF08C", "Warm every display for the evening.", true),
        new("darkMode", "Dark mode", "\uE708", "Windows' app and system theme together.", true),
        new("focus", "Focus", "\uE890", "Dim everything except the window you are working in.", true),
        new("awake", "Keep awake", "\uE916", "Stop the computer from sleeping. Click to switch; the arrow, or a right-click, chooses for how long.", true),
        new("stayActive", "Stay active", "\uE962", "Keep the screen on and never show as Away: a tiny pointer nudge after a minute idle. On until you switch it off.", true),
        new("taskbar", "Taskbar", "\uE75A", "Taskbar glass on and off. The arrow opens transparency, glass, opacity and auto-hide together.", true),
        new("oledIdle", "OLED care", "\uE7EF", "Dim OLED displays after a spell of inactivity.", true),
        new("project", "Project", "\uEBC6", "Windows' Project flyout. The arrow switches extend, duplicate or one screen only directly.", true),
        new("displaysOff", "Displays off", "\uE7E8", "Black the displays out, as if switched off, while the computer keeps running. A touch brings them back; the arrow chooses how dark, which displays and what wakes them.", false),
        new("detect", "Detect", "\uE72C", "Look for displays again, including one that is connected but switched off.", false),
        new("unison", "Unison", "\uE793", "Move every display's brightness with one slider.", false),
        new("identify", "Identify", "\uE7C4", "Show each display's number on it for three seconds. The arrow picks one display, or looks for displays again.", false),
        new("cast", "Cast", "\uE7F7", "Connect to a wireless display.", false),
        new("restAll", "Rest OLED", "\uEA14", "Rest every OLED display under the idle-protection dimming until the computer is next used.", false),
        new("engine", "Engine", "\uE9F5", "DispCtrl's background engine, which runs taskbar hiding, night light, focus and shortcuts.", false),
    ];

    public static IReadOnlyList<Entry> DisplayRows { get; } =
    [
        new("brightness", "Brightness", "\uE706", "The display's own backlight.", true),
        new("toggles", "Switches", "\uE8A9", "A strip of small switches for this display, chosen under Switches on each display.", true),
        new("resolution", "Resolution", "\uE9A6", "The display's resolution.", true),
        new("scaling", "Scale", "\uE8A9", "How large Windows draws text and apps on this display.", true),
        new("refresh", "Refresh rate", "\uE823", "How many times a second the display redraws.", true),
        new("dimming", "Software dimming", "\uEC8A", "Dims below what the backlight can reach.", true),
        new("orientation", "Orientation", "\uE8EB", "Landscape, portrait, or either flipped.", true),
        new("controls", "Monitor controls", "\uE9E9", "Everything else the monitor lets you change over DDC/CI: contrast, volume, colour presets.", true),
        new("warmth", "Warmth", "\uF08C", "This display's own night light strength. Only shown while night light runs per display, because in unison or through Windows it would change nothing.", false),
        new("input", "Input source", "\uE8CB", "The monitor's input. Appears once the monitor has said what it supports.", false),
    ];

    public static IReadOnlyList<Entry> DisplayTiles { get; } =
    [
        new("hideTaskbar", "Hide taskbar", "\uE75A", "Hide this display's taskbar until the pointer reaches the edge. On the main display this is Windows' own auto-hide.", true),
        new("hdr", "HDR", "\uE7A1", "High dynamic range, where the display supports it.", true),
        new("primary", "Make main", "\uE80F", "Make this the main display. Lit on the one that already is.", true),
        new("focusDimming", "Focus dimming", "\uE890", "Whether focus mode may dim this display.", true),
        new("oledProtection", "OLED care", "\uE7EF", "Idle protection for this display. OLED displays only.", true),
        new("rest", "Rest now", "\uEA14", "Rest this OLED display under the idle-protection dimming until the computer is next used. OLED displays only.", true),
        new("sleep", "Monitor sleep", "\uE7E8", "Switch this monitor off through its own power control after inactivity. External monitors only.", true),
        new("vrr", "Variable refresh", "\uE9D9", "Variable refresh rate, where the display supports it.", true),
        new("adaptive", "Adaptive brightness", "\uE9F3", "Windows' automatic brightness, where the display has a sensor.", true),
        new("rotation", "Auto-rotate", "\uE7AD", "Rotate with the device, where it can sense which way up it is.", true),
        new("identify", "Identify", "\uE7C4", "Show this display's number on it.", false),
    ];

    public static IReadOnlyList<Entry> For(QuickPanelGroup group) => group switch
    {
        QuickPanelGroup.Sections => Sections,
        QuickPanelGroup.Tiles => Tiles,
        QuickPanelGroup.DisplayRows => DisplayRows,
        _ => DisplayTiles,
    };

    public static Entry? Find(QuickPanelGroup group, string id)
    {
        foreach (Entry e in For(group)) if (e.Id == id) return e;
        return null;
    }

    /// <summary>The shipped order and visibility for a group.</summary>
    public static List<QuickPanelItem> Defaults(QuickPanelGroup group) =>
        For(group).Select(e => new QuickPanelItem(e.Id, e.ShownByDefault)).ToList();

    /// <summary>
    /// A saved list made whole: unknown ids dropped, duplicates dropped, and
    /// anything added to the catalogue since appended, hidden.
    /// </summary>
    /// <remarks>
    /// Appended hidden rather than shown, because a panel somebody has pared
    /// down to two sliders should not grow a row on its own after an update.
    /// Appended at the end rather than at its catalogue position, because the
    /// saved order is theirs and there is no right place to splice into it.
    /// </remarks>
    /// <param name="extra">
    /// Ids that exist beyond the catalogue - custom tiles. New ones are
    /// appended shown, because somebody who has just made a tile wants to see it.
    /// </param>
    public static List<QuickPanelItem> Normalise(
        QuickPanelGroup group, List<QuickPanelItem>? saved, IReadOnlyCollection<string>? extra = null)
    {
        extra ??= [];
        if ((saved is null || saved.Count == 0) && extra.Count == 0) return Defaults(group);
        saved ??= [];
        if (saved.Count == 0) saved = Defaults(group);

        var known = new HashSet<string>(For(group).Select(e => e.Id).Concat(extra));
        var seen = new HashSet<string>();
        var result = new List<QuickPanelItem>();

        foreach (QuickPanelItem item in saved)
        {
            if (item is null || !known.Contains(item.Id) || !seen.Add(item.Id)) continue;
            result.Add(new QuickPanelItem(item.Id, item.Visible));
        }

        foreach (Entry e in For(group))
            if (seen.Add(e.Id)) result.Add(new QuickPanelItem(e.Id, false));

        foreach (string id in extra)
            if (seen.Add(id)) result.Add(new QuickPanelItem(id, true));

        return result;
    }

    /// <summary>Moves one item up (negative) or down (positive), clamped at the ends.</summary>
    public static bool Move(List<QuickPanelItem> items, string id, int by)
    {
        int from = items.FindIndex(i => i.Id == id);
        if (from < 0) return false;

        int to = Math.Clamp(from + by, 0, items.Count - 1);
        if (to == from) return false;

        QuickPanelItem moving = items[from];
        items.RemoveAt(from);
        items.Insert(to, moving);
        return true;
    }
}

/// <summary>
/// What the quick panel shows, and how.
/// </summary>
/// <remarks>
/// Four ordered lists - the sections, the toggle grid, the rows under each
/// display and the switches in each display's strip - so the panel can be one
/// brightness slider per display for somebody who wants that, or everything
/// DispCtrl does in whatever order somebody prefers. The lists hold ids;
/// <see cref="QuickPanelCatalog"/> says what each id is.
/// </remarks>
public sealed class QuickPanelSettings
{
    /// <summary>Whether the icon is in the notification area at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The icon in the notification area.</summary>
    /// <remarks>
    /// A glyph by default, drawn the way Windows draws its own tray icons: one
    /// colour, following the taskbar's theme. A coloured logo among Windows'
    /// own monochrome icons is the one that looks like it was installed.
    /// </remarks>
    public TrayIconStyle Icon { get; set; } = TrayIconStyle.Brightness;

    /// <summary>The glyph's colour: the taskbar's own white or black, or Windows' accent colour.</summary>
    public TrayIconColour IconColour { get; set; } = TrayIconColour.Taskbar;

    /// <summary>Draws the glyph bolder while Stay active or Keep awake is on, so it can be seen at a glance.</summary>
    public bool IconShowsActive { get; set; } = true;

    public QuickPanelDensity Density { get; set; } = QuickPanelDensity.Comfortable;

    /// <summary>Panel width in DIP.</summary>
    /// <remarks>
    /// 360 is what Windows' own quick settings uses, which is the width people
    /// already expect something summoned from the tray to be.
    /// </remarks>
    public int Width { get; set; } = 360;

    public const int MinWidth = 280, MaxWidth = 560;

    /// <summary>How many toggles sit side by side in the grid.</summary>
    /// <remarks>Four at the default width leaves each wide enough for a two-word label.</remarks>
    public int TileColumns { get; set; } = 4;

    public const int MinColumns = 3, MaxColumns = 8;

    /// <summary>
    /// Keep the panel one height and scroll inside it, rather than growing to fit.
    /// </summary>
    /// <remarks>
    /// Growing to fit is Windows' own behaviour and the default. A fixed height
    /// is for a panel with a lot in it: it opens the same size every time, and
    /// what does not fit scrolls, instead of the panel reaching the top of the
    /// screen.
    /// </remarks>
    public bool FixedHeight { get; set; } = true;

    /// <summary>The height when <see cref="FixedHeight"/> is on, in DIP.</summary>
    public int Height { get; set; } = 500;

    public const int MinHeight = 240, MaxHeight = 1200;

    /// <summary>Keep the panel open when it loses focus.</summary>
    /// <remarks>
    /// Off by default because a flyout that outstays its welcome is a window,
    /// not a flyout. On while somebody is working through several settings and
    /// does not want it closing every time they click a display.
    /// </remarks>
    public bool StayOpen { get; set; }

    /// <summary>
    /// Brightness sliders only: all displays together, then one per display.
    /// </summary>
    /// <remarks>
    /// A mode, not a preset of the lists: the sections, tiles and rows are left
    /// as they were, so switching it off brings the full panel back unchanged.
    /// On by default: a first opening shows what the panel is for, and the dot
    /// in its title bar opens up the rest.
    /// </remarks>
    public bool Simple { get; set; } = true;

    /// <summary>Keep the panel where it opens: its title is not a drag handle.</summary>
    public bool Locked { get; set; }

    /// <summary>Animate the panel unless Windows has disabled animation effects.</summary>
    public bool Animate { get; set; } = true;

    /// <summary>The row that opens the full app, at the bottom.</summary>
    public bool ShowFooter { get; set; }

    /// <summary>Tiles somebody made, run from the panel. See <see cref="QuickPanelCustomTile"/>.</summary>
    public List<QuickPanelCustomTile> CustomTiles { get; set; } = [];

    /// <summary>
    /// Sections and displays folded away in the panel, by id - a section's id,
    /// or <c>display:</c> and a display's identity token.
    /// </summary>
    /// <remarks>Remembered, so a block folded once stays folded.</remarks>
    public List<string> Collapsed { get; set; } = [];

    /// <summary>Sections folded by default that somebody has opened.</summary>
    public List<string> Expanded { get; set; } = [];

    /// <summary>
    /// Sections that start folded until they are opened: the ones that are
    /// detail, not the everyday controls.
    /// </summary>
    /// <remarks>
    /// Added to the panel open, each of these made it half a screen taller with
    /// options set once and then left; folded, they are one line that says what
    /// is there. A separate list of opened ones, rather than a default written
    /// into <see cref="Collapsed"/>, so a section added later still starts folded.
    /// </remarks>
    public static readonly string[] FoldedByDefault = ["oledCare", "focus", "displayMode", "taskbar", "nightLight", "presets"];

    public bool IsCollapsed(string key) =>
        Collapsed.Contains(key) || (FoldedByDefault.Contains(key) && !Expanded.Contains(key));

    /// <summary>Remembers a fold, in whichever list records a departure from the default.</summary>
    public void SetCollapsed(string key, bool collapsed)
    {
        Collapsed.Remove(key);
        Expanded.Remove(key);
        bool byDefault = FoldedByDefault.Contains(key);
        if (collapsed && !byDefault) Collapsed.Add(key);
        if (!collapsed && byDefault) Expanded.Add(key);
    }

    public List<QuickPanelItem> Sections { get; set; } = QuickPanelCatalog.Defaults(QuickPanelGroup.Sections);
    public List<QuickPanelItem> Tiles { get; set; } = QuickPanelCatalog.Defaults(QuickPanelGroup.Tiles);
    public List<QuickPanelItem> DisplayRows { get; set; } = QuickPanelCatalog.Defaults(QuickPanelGroup.DisplayRows);
    public List<QuickPanelItem> DisplayTiles { get; set; } = QuickPanelCatalog.Defaults(QuickPanelGroup.DisplayTiles);

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

    /// <summary>The list for one group, made whole and written back.</summary>
    /// <remarks>
    /// Normalised on the way out, so every caller sees a list that names every
    /// catalogue item exactly once, whatever an older file or a hand edit left.
    /// </remarks>
    public List<QuickPanelItem> List(QuickPanelGroup group)
    {
        IReadOnlyCollection<string>? extra = group == QuickPanelGroup.Tiles
            ? CustomTiles.Select(t => t.Id).ToList()
            : null;

        List<QuickPanelItem> whole = QuickPanelCatalog.Normalise(group, Raw(group), extra);
        switch (group)
        {
            case QuickPanelGroup.Sections: Sections = whole; break;
            case QuickPanelGroup.Tiles: Tiles = whole; break;
            case QuickPanelGroup.DisplayRows: DisplayRows = whole; break;
            default: DisplayTiles = whole; break;
        }

        return whole;
    }

    private List<QuickPanelItem>? Raw(QuickPanelGroup group) => group switch
    {
        QuickPanelGroup.Sections => Sections,
        QuickPanelGroup.Tiles => Tiles,
        QuickPanelGroup.DisplayRows => DisplayRows,
        _ => DisplayTiles,
    };

    /// <summary>The visible ids of a group, in order.</summary>
    public IEnumerable<string> Shown(QuickPanelGroup group) =>
        List(group).Where(i => i.Visible).Select(i => i.Id).ToList();

    public bool IsShown(QuickPanelGroup group, string id) =>
        List(group).Any(i => i.Id == id && i.Visible);

    public QuickPanelCustomTile? Custom(string id) => CustomTiles.Find(t => t.Id == id);

    /// <summary>
    /// Replaces a group's order with the shown items given, keeping the hidden
    /// ones after them in the order they had.
    /// </summary>
    /// <remarks>
    /// What the customisation page hands back after a drag: it shows only what
    /// is switched on, so the hidden items are not its to reorder.
    /// </remarks>
    public void Reorder(QuickPanelGroup group, IReadOnlyList<string> shown)
    {
        List<QuickPanelItem> list = List(group);
        var byId = list.ToDictionary(i => i.Id);

        var result = new List<QuickPanelItem>();
        foreach (string id in shown)
            if (byId.Remove(id, out QuickPanelItem? item)) { item.Visible = true; result.Add(item); }

        // Everything not handed back is hidden: leaving an item out is how the
        // page takes it off the panel. Keeping its old visibility made the
        // cross beside every row do nothing.
        foreach (QuickPanelItem rest in list)
            if (byId.ContainsKey(rest.Id)) { rest.Visible = false; result.Add(rest); }

        list.Clear();
        list.AddRange(result);
    }

    /// <summary>Restores the shipped defaults.</summary>
    public void ResetToDefaults()
    {
        var fresh = new QuickPanelSettings();

        Icon = fresh.Icon;
        Density = fresh.Density;
        Width = fresh.Width;
        TileColumns = fresh.TileColumns;
        FixedHeight = fresh.FixedHeight;
        Height = fresh.Height;
        StayOpen = fresh.StayOpen;
        Simple = fresh.Simple;
        Locked = fresh.Locked;
        Animate = fresh.Animate;
        ShowFooter = fresh.ShowFooter;
        Sections = fresh.Sections;
        Tiles = fresh.Tiles;
        DisplayRows = fresh.DisplayRows;
        DisplayTiles = fresh.DisplayTiles;

        // Which displays are hidden is a statement about this desk, not a
        // preference about the panel, so a reset of the panel leaves it alone.
        // Whether the icon is shown at all is left alone for the same reason.
    }
}
