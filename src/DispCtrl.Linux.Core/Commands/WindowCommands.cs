using System.Globalization;
using DispCtrl.Linux.Settings;
using DispCtrl.Linux.Snap;
using DispCtrl.Linux.X11;

namespace DispCtrl.Linux.Commands;

public static partial class CommandRunner
{
    private static int WindowsCommand(string[] args, CommandContext c)
    {
        if (args.Length > 0) return Wrong(c, "windows takes no arguments yet");
        using var x = XConnection.Open();
        if (x is null) return Refuse(c, NoDesktop());
        var monitors = Desktop.Monitors(x);
        var active = Desktop.Active(x);
        foreach (var w in Desktop.Windows(x))
        {
            var monitor = Desktop.MonitorOf(monitors, w.Frame);
            string flags = string.Join(",", new[]
            {
                w.Id == active ? "active" : null, w.Minimized ? "minimized" : null, w.Maximized ? "maximized" : null,
                w.Fullscreen ? "fullscreen" : null, w.Above ? "on top" : null,
            }.Where(f => f is not null));
            c.Out.WriteLine($"0x{w.Id:x8}  {monitor.Name,-8} {w.Frame,-22} {(flags.Length > 0 ? $"[{flags}] " : "")}{w.AppClass}: {w.Title}");
        }
        return Done;
    }

    private static int SnapCommand(string[] args, CommandContext c)
    {
        switch (args)
        {
            case [] or ["status"]:
                return SnapStatus(c);
            case ["on" or "true"] or ["off" or "false"]:
                bool on = args[0] is "on" or "true";
                SettingsStore.Update(s => s.Snap.Enabled = on);
                c.Out.WriteLine(on ? "Snap layouts on." : "Snap layouts off.");
                return EngineNote(c);
            case ["drag", var v]:
                if (!TryOnOff(v, out bool drag)) return Wrong(c, "snap drag takes on or off");
                SettingsStore.Update(s => s.Snap.DragToTop = drag);
                c.Out.WriteLine(drag ? "Dragging a window to the top opens the layouts." : "Dragging no longer opens the layouts.");
                return EngineNote(c);
            case ["assist", var v]:
                if (!TryOnOff(v, out bool assist)) return Wrong(c, "snap assist takes on or off");
                SettingsStore.Update(s => s.Snap.Assist = assist);
                c.Out.WriteLine(assist ? "Snap Assist on." : "Snap Assist off.");
                return EngineNote(c);
            case ["shortcut", .. var keys] when keys.Length > 0:
                string keysText = string.Join(' ', keys);
                if (keysText is "none" or "off") keysText = "";
                else if (X11.Shortcut.Parse(keysText) is null)
                    return Wrong(c, $"'{keysText}' is not a shortcut: a modifier (Super, Ctrl, Alt, Shift) and a key, e.g. Super+Z");
                SettingsStore.Update(s => s.Snap.Shortcut = keysText);
                c.Out.WriteLine(keysText.Length == 0 ? "No snap shortcut." : $"Snap shortcut: {keysText}.");
                return EngineNote(c);
            case ["gap", var g]:
                if (!int.TryParse(g, NumberStyles.Integer, CultureInfo.InvariantCulture, out int gap) || gap is < 0 or > 32)
                    return Wrong(c, "snap gap takes 0 to 32 pixels");
                SettingsStore.Update(s => s.Snap.Gap = gap);
                c.Out.WriteLine($"Snap gap: {gap} px.");
                return Done;
            case ["pick"]:
                if (c.Engine is null) return Refuse(c, "snap pick needs the engine: systemctl --user start dispctrl-linux-engine");
                if (!c.Engine.SnapPick(out var why)) return Refuse(c, why ?? "the layouts could not open");
                return Done;
        }

        using var x = XConnection.Open();
        if (x is null) return Refuse(c, NoDesktop());
        var monitors = Desktop.Monitors(x);

        if (args.Length > 0 && args[0] == "preview") return SnapPreview(args[1..], c, monitors);
        if (args is ["zones"])
        {
            // Where each zone of the snap panel is drawn, in desktop coordinates:
            // what a script (or a test) aims at.
            foreach (var m in monitors)
            {
                var panel = SnapPanel.For(m);
                c.Out.WriteLine($"{m.Name} handle {panel.Handle.CenterX},{panel.Handle.CenterY}");
                foreach (var z in panel.AllZones())
                {
                    var r = panel.ZoneInTile(z);
                    c.Out.WriteLine($"{m.Name} {panel.Layouts[z.Layout].Id} {z.Zone + 1} {r.CenterX},{r.CenterY}");
                }
            }
            return Done;
        }
        if (args is ["layouts"])
        {
            foreach (var m in monitors)
                c.Out.WriteLine($"{m.Name,-8} {m.WorkArea,-20} {string.Join(", ", SnapLayouts.For(m.WorkArea).Select(l => l.Id))}");
            return Done;
        }
        if (args.Length < 2) return Wrong(c, "snap needs a layout and a zone number, e.g. snap halves 1 (snap layouts lists them)");
        var layout = SnapLayouts.Find(args[0]);
        if (layout is null) return Wrong(c, $"no layout '{args[0]}' ({string.Join(", ", SnapLayouts.All.Select(l => l.Id))})");
        if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int zone) || zone < 1 || zone > layout.Zones.Count)
            return Wrong(c, $"{layout.Id} has zones 1 to {layout.Zones.Count}");
        if (!TryFlags(args[2..], c, out var flags)) return AskedWrongly;

        ulong id;
        if (flags.TryGetValue("--window", out var text))
        {
            if (!TryWindowId(text, out id)) return Wrong(c, $"'{text}' is not a window id (dispctrl-linux windows lists them)");
        }
        else if (Desktop.Active(x) is { } active) id = active;
        else return Refuse(c, "no window is active");

        var window = Desktop.Describe(x, id);
        if (window is null) return Refuse(c, $"0x{id:x} is not an application window");
        var monitor = Desktop.MonitorOf(monitors, window.Frame);
        var target = layout.Zones[zone - 1].On(monitor.WorkArea, SettingsGap());
        Desktop.Place(x, id, target);
        c.Out.WriteLine($"0x{id:x8} -> {layout.Id} zone {zone} on {monitor.Name}: {target}");
        return Done;
    }

    /// <summary>Renders each overlay state to PNG, offscreen, for every monitor:
    /// how the design is checked without drawing over somebody's desktop.</summary>
    private static int SnapPreview(string[] args, CommandContext c, IReadOnlyList<MonitorInfo> monitors)
    {
        if (!TryFlags(args, c, out var flags) || !flags.TryGetValue("--out", out var dir))
            return Wrong(c, "snap preview needs --out <folder>");
        System.IO.Directory.CreateDirectory(dir);
        string? themeName = flags.GetValueOrDefault("--theme");
        var theme = Graphics.DesktopTheme.Current();
        if (themeName == "light") theme = theme with { Dark = false };
        else if (themeName == "dark") theme = theme with { Dark = true };

        List<AssistCandidate> candidates = new()
        {
            new(1, "Inbox - Mail", "Thunderbird", default), new(2, "README.md - Visual Studio Code", "Code", default),
            new(3, "Terminal", "gnome-terminal", default), new(4, "Files", "Nautilus", default),
        };
        // --windows PREFIX: Assist's cards from the real windows whose titles
        // start with PREFIX, with their live pictures - only those, so a preview
        // never captures anything else on the desktop.
        WindowPictures? pictures = null;
        using var live = flags.TryGetValue("--windows", out var prefix) ? XConnection.Open() : null;
        if (live is not null)
        {
            candidates = Desktop.Windows(live).Where(w => w.Title.StartsWith(prefix!, StringComparison.Ordinal))
                .Select(w => new AssistCandidate(w.Id, w.Title, w.AppClass, w.Frame)).ToList();
            pictures = new WindowPictures(live);
            foreach (var w in candidates) pictures.Capture(w.Id);
        }
        using var _ = pictures;

        foreach (var m in monitors)
        {
            var panel = SnapPanel.For(m);
            void Save(string name, Action<Graphics.Canvas> draw)
            {
                using var canvas = Graphics.Canvas.Image(m.Bounds.Width, m.Bounds.Height);
                canvas.RoundedRect(0, 0, m.Bounds.Width, m.Bounds.Height, 0);
                canvas.Fill(theme.Dark ? Graphics.Rgba.Hex("#1d2a3a") : Graphics.Rgba.Hex("#c9d6e3"));
                draw(canvas);
                var path = Path.Combine(dir, $"snap-{m.Name}-{name}.png");
                canvas.SavePng(path);
                c.Out.WriteLine(path);
            }
            Save("handle", cv => SnapPainter.Handle(cv, panel, theme, m.Bounds));
            Save("panel", cv => SnapPainter.Panel(cv, panel, new ZoneRef(1, 0), Gap(), theme, m.Bounds));
            var free = SnapLayouts.Halves.Zones[1].On(m.WorkArea, Gap());
            Save("assist", cv => SnapPainter.Assist(cv, free, candidates, 1, theme, m.Bounds, pictures is null ? null : pictures.Draw));
        }
        return Done;

        static int Gap() => SettingsGap();
    }

    private static int SnapStatus(CommandContext c)
    {
        var s = SettingsStore.Load().Snap;
        c.Out.WriteLine($"snap         {(s.Enabled ? "on" : "off")}");
        c.Out.WriteLine($"drag to top  {(s.DragToTop ? "on" : "off")}");
        c.Out.WriteLine($"shortcut     {(s.Shortcut.Length > 0 ? s.Shortcut : "none")}");
        c.Out.WriteLine($"assist       {(s.Assist ? "on" : "off")}");
        c.Out.WriteLine($"gap          {s.Gap} px");
        if (c.Engine is not null) c.Out.WriteLine($"engine       {c.Engine.SnapStatus()}");
        else c.Out.WriteLine("engine       not running: nothing watches for drags or holds the shortcut");
        if (Hardware.GammaRamp.OnWayland) c.Out.WriteLine("note         this is a Wayland session; snap layouts need X11");
        return Done;
    }

    private static int EngineNote(CommandContext c)
    {
        if (!c.InEngine) c.Out.WriteLine("The engine applies it; it is not running (systemctl --user start dispctrl-linux-engine).");
        return Done;
    }

    private static bool TryOnOff(string value, out bool on)
    {
        on = value is "on" or "true";
        return on || value is "off" or "false";
    }

    private static int SettingsGap() => Settings.SettingsStore.Load().Snap.Gap;

    internal static bool TryWindowId(string text, out ulong id) =>
        text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ulong.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id)
            : ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);

    private static string NoDesktop() =>
        Hardware.GammaRamp.OnWayland
            ? "window tools need an X11 session: on Wayland only the compositor may move windows"
            : "no X display ($DISPLAY is not set, or the X server did not answer)";
}
