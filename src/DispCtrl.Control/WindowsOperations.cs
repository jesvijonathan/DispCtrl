using System.Text.Json.Nodes;
using DispCtrl.Core.Settings;
using DispCtrl.Display;

namespace DispCtrl.Control;

public sealed partial class ControlService
{
    private static JsonObject WindowsCommand(string action, JsonObject args)
    {
        if (action == "get") return new JsonObject
        {
            ["autoHide"] = GlobalTaskbar.IsAutoHide, ["transparency"] = WindowsTaskbarAppearance.Transparency,
            ["smallButtonsSupported"] = WindowsTaskbarAppearance.SmallButtonsSupported,
            ["smallButtons"] = WindowsTaskbarAppearance.SmallButtons, ["alignment"] = WindowsTaskbarAppearance.Alignment,
            ["combineButtons"] = WindowsTaskbarAppearance.CombineButtons,
            ["combineOtherDisplays"] = WindowsTaskbarAppearance.CombineButtonsOtherDisplays,
            ["showTaskView"] = WindowsTaskbarAppearance.ShowTaskView, ["showWidgets"] = WindowsTaskbarAppearance.ShowWidgets,
            ["showBadges"] = WindowsTaskbarAppearance.ShowBadges, ["allowFlashing"] = WindowsTaskbarAppearance.AllowFlashing,
            ["showDesktopCorner"] = WindowsTaskbarAppearance.ShowDesktopCorner, ["variableRefresh"] = VariableRefreshRate.IsEnabled(),
            ["adaptiveBrightness"] = AdaptiveBrightness.Read().Enabled, ["adaptiveBrightnessSupported"] = AdaptiveBrightness.Read().Supported,
            ["autoRotation"] = AutoRotation.Read().Enabled, ["autoRotationSupported"] = AutoRotation.Read().Supported,
            ["darkMode"] = WindowsTheme.IsDark,
            ["wallpaperFit"] = Wallpaper.ReadFit().ToString().ToLowerInvariant(),
            ["rememberWindowLocations"] = Display.Placement.WindowsWindowMemory.Remember,
            ["minimizeOnDisconnect"] = Display.Placement.WindowsWindowMemory.MinimizeOnDisconnect,
        };
        if (action == "open") return OpenWindowsPage(args);
        var steps = new List<Step>();
        foreach (var pair in args)
        {
            if (pair.Key == "dryRun") continue;
            Func<bool> run;
            switch (pair.Key)
            {
                case "smallButtons":
                    int small = Integer(args, pair.Key, 0, 2);
                    if (!WindowsTaskbarAppearance.SmallButtonsSupported) throw new ArgumentException("Small taskbar buttons are unavailable on this Windows version.");
                    run = () => WindowsTaskbarAppearance.SetSmallButtons(small); break;
                case "alignment": int alignment = Integer(args, pair.Key, 0, 1); run = () => WindowsTaskbarAppearance.SetAlignment(alignment); break;
                case "combineButtons": int combine = Integer(args, pair.Key, 0, 2); run = () => WindowsTaskbarAppearance.SetCombineButtons(combine); break;
                case "combineOtherDisplays": int other = Integer(args, pair.Key, 0, 2); run = () => WindowsTaskbarAppearance.SetCombineButtonsOtherDisplays(other); break;
                case "wallpaperFit":
                    WallpaperFit fit = Enum.TryParse(Text(args, pair.Key), ignoreCase: true, out WallpaperFit parsed)
                        ? parsed : throw new ArgumentException("Wallpaper fit: " + string.Join(", ", Enum.GetNames<WallpaperFit>().Select(n => n.ToLowerInvariant())) + ".");
                    run = () => Wallpaper.WriteFit(fit); break;
                default:
                    bool enabled = pair.Value?.GetValue<bool>() ?? throw new ArgumentException("Missing Boolean value for " + pair.Key);
                    run = pair.Key switch
                    {
                        "transparency" => () => WindowsTaskbarAppearance.SetTransparency(enabled),
                        "autoHide" => () => { GlobalTaskbar.SetAutoHide(enabled); return GlobalTaskbar.IsAutoHide == enabled; },
                        "showTaskView" => () => WindowsTaskbarAppearance.SetShowTaskView(enabled),
                        "showWidgets" => () => WindowsTaskbarAppearance.SetShowWidgets(enabled),
                        "showBadges" => () => WindowsTaskbarAppearance.SetShowBadges(enabled),
                        "allowFlashing" => () => WindowsTaskbarAppearance.SetAllowFlashing(enabled),
                        "showDesktopCorner" => () => WindowsTaskbarAppearance.SetShowDesktopCorner(enabled),
                        "variableRefresh" => () => VariableRefreshRate.SetEnabled(enabled),
                        "adaptiveBrightness" => () => AdaptiveBrightness.Write(enabled),
                        "autoRotation" => () => AutoRotation.Write(enabled),
                        "darkMode" => () => WindowsTheme.SetDark(enabled),
                        "rememberWindowLocations" => () => { Display.Placement.WindowsWindowMemory.Remember = enabled; return Display.Placement.WindowsWindowMemory.Remember == enabled; },
                        "minimizeOnDisconnect" => () => { Display.Placement.WindowsWindowMemory.MinimizeOnDisconnect = enabled; return Display.Placement.WindowsWindowMemory.MinimizeOnDisconnect == enabled; },
                        _ => throw new ArgumentException("Unknown Windows option: " + pair.Key),
                    };
                    break;
            }
            steps.Add(new(60, pair.Key, null, run));
        }
        if (steps.Count == 0) throw new ArgumentException("No Windows settings requested.");
        return RunSteps(steps, Flag(args, "dryRun"));
    }

    /// <summary>The Windows pages the app's buttons open, for scripts that want them too.</summary>
    private static readonly Dictionary<string, string> WindowsPages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["display"] = "ms-settings:display", ["advanced"] = "ms-settings:display-advanced", ["hdr"] = "ms-settings:display-advancedgraphics",
        ["nightlight"] = "ms-settings:nightlight", ["colors"] = "ms-settings:colors", ["taskbar"] = "ms-settings:taskbar",
        ["startup"] = "ms-settings:startupapps", ["power"] = "ms-settings:powersleep", ["cast"] = "ms-settings-connectabledevices:devicediscovery",
        ["colormanagement"] = "colorcpl.exe",
    };

    private static JsonObject OpenWindowsPage(JsonObject args)
    {
        string page = Text(args, "page") ?? throw new ArgumentException("windows open --page " + string.Join("|", WindowsPages.Keys));
        if (!WindowsPages.TryGetValue(page, out string? target)) throw new ArgumentException("Pages: " + string.Join(", ", WindowsPages.Keys));
        if (Flag(args, "dryRun")) return new JsonObject { ["state"] = "validated", ["target"] = target };
        using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
        return new JsonObject { ["state"] = "opened", ["target"] = target };
    }
}
