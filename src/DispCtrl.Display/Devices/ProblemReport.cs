using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;

namespace DispCtrl.Display.Devices;

/// <summary>A bug report, scrubbed and ready for the person to submit.</summary>
/// <param name="Text">Everything the report says, exactly as it would be published.</param>
/// <param name="Url">The bug form, prefilled with as much as a link can carry.</param>
/// <param name="Paste">What did not fit, for the clipboard; null when nothing is left.</param>
public readonly record struct ProblemReport(string Text, Uri Url, string? Paste);

/// <summary>
/// Builds a bug report from what DispCtrl can see: its version, Windows, the
/// displays, the settings that matter, and the end of the engine's log.
/// </summary>
/// <remarks>
/// A bug about hardware is only fixable with the hardware described, and people
/// reporting one rarely know which details matter. So the report gathers them,
/// and the person adds only what happened. It goes through the same scrub as a
/// device record - <see cref="Redact.Scrub"/> against every attached panel's
/// serial and identity token, device paths, user paths and the account name -
/// because a log names monitors by token and files by path. It opens the
/// repository's bug form with its fields filled; nothing is sent from here.
/// </remarks>
public static class ProblemReports
{
    // Under the 8k where GitHub answers 414, with room for the browser.
    private const int MaxUrlLength = 7000;
    private const int LogLines = 40;

    public static ProblemReport Build(string? whatHappened, string? steps, string version)
    {
        List<string> identifiers = DeviceContribution.Identifiers(null);
        string Clean(string text) => Redact.Ascii(Redact.Scrub(text, identifiers)).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

        string what = Clean(whatHappened ?? "");
        string how = Clean(steps ?? "");
        string versionLine = Clean($"{version} ({InstallKind()})");
        string windows = Clean(WindowsVersion());
        string displays = Clean(Displays());
        string context = Clean(Context());
        string log = Clean(Tail(SettingsStore.LogPath, LogLines));
        string crash = File.Exists(CrashLog) && File.GetLastWriteTimeUtc(CrashLog) > DateTime.UtcNow.AddDays(-7)
            ? Clean(Tail(CrashLog, 30)) : "";
        string logs = log + (crash.Length > 0 ? "\n\n--- app-crash.log ---\n" + crash : "");

        var text = new StringBuilder();
        text.Append("### What happened\n\n").Append(what.Length > 0 ? what : "_Not described._").Append("\n\n");
        if (how.Length > 0) text.Append("### Steps\n\n").Append(how).Append("\n\n");
        text.Append("### DispCtrl\n\n").Append(versionLine).Append("\n\n");
        text.Append("### Windows\n\n").Append(windows).Append("\n\n");
        text.Append("### Displays\n\n").Append(displays).Append("\n\n");
        text.Append("### Settings that bear on it\n\n").Append(context).Append("\n\n");
        text.Append("### Logs\n\n```text\n").Append(logs).Append("\n```\n");

        // The repository's bug form, field by field: GitHub prefills an issue
        // form's fields from query parameters named for their ids.
        string title = what.Length > 0 ? what.Split('\n')[0] : "Problem report";
        if (title.Length > 80) title = title[..77] + "...";
        string displaysField = displays + "\n\n" + context;
        string Link(string logsField) => $"https://github.com/{DeviceContribution.Repository}/issues/new?template=bug.yml"
            + $"&title={WebUtility.UrlEncode(title)}&what={WebUtility.UrlEncode(what)}&steps={WebUtility.UrlEncode(how)}"
            + $"&version={WebUtility.UrlEncode(versionLine)}&windows={WebUtility.UrlEncode(windows)}"
            + $"&displays={WebUtility.UrlEncode(displaysField)}&logs={WebUtility.UrlEncode(logsField)}";

        string full = Link(logs);
        if (full.Length <= MaxUrlLength) return new ProblemReport(text.ToString(), new Uri(full), null);
        // The log is what overflows; it goes to the clipboard and the field says so.
        string placeholder = "The log was too long for the link. DispCtrl copied it: paste it here.";
        return new ProblemReport(text.ToString(), new Uri(Link(placeholder)), logs);
    }

    private static string CrashLog => Path.Combine(SettingsStore.Directory, "app-crash.log");

    /// <summary>How this copy was installed, from where it runs.</summary>
    private static string InstallKind()
    {
        string path = AppContext.BaseDirectory;
        if (path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)) return "Microsoft Store package";
        if (path.Contains(@"\Programs\DispCtrl", StringComparison.OrdinalIgnoreCase)) return "installer";
        if (path.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase)) return "built from source";
        return "portable";
    }

    private static string WindowsVersion()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string display = key?.GetValue("DisplayVersion") as string ?? "";
            string build = key?.GetValue("CurrentBuild") as string ?? Environment.OSVersion.Version.Build.ToString();
            string ubr = key?.GetValue("UBR")?.ToString() ?? "";
            string name = int.TryParse(build, out int b) && b >= 22000 ? "Windows 11" : "Windows 10";
            return $"{name} {display} (build {build}{(ubr.Length > 0 ? "." + ubr : "")}), {RuntimeInformation.OSArchitecture}";
        }
        catch (Exception) { return $"{Environment.OSVersion.Version}, {RuntimeInformation.OSArchitecture}"; }
    }

    /// <summary>Each display by model, never by serial: what a person reading the report needs.</summary>
    private static string Displays()
    {
        try
        {
            var lines = new List<string>();
            int n = 0;
            foreach (DisplayInfo d in DisplayRegistry.Enumerate())
            {
                n++;
                string size = d.HasPhysicalSize ? $", {d.DiagonalInches:0.0} in" : "";
                lines.Add($"{n}. {d.Label} ({d.Key.Model}), {(d.IsInternal ? "built-in" : d.Connector.ToString())}, "
                    + $"{d.Bounds.Width}x{d.Bounds.Height} @ {d.RefreshHz} Hz, {d.Scale * 100:0}% scaling{size}{(d.IsPrimary ? ", primary" : "")}");
            }
            return lines.Count == 0 ? "No displays enumerated." : string.Join("\n", lines);
        }
        catch (Exception ex) { return $"Could not enumerate the displays: {ex.Message}"; }
    }

    /// <summary>The switches most bugs turn on, and whether the engine is there to act on them.</summary>
    private static string Context()
    {
        var lines = new List<string>();
        bool engine = Process.GetProcessesByName("DispCtrl.Engine").Length > 0;
        lines.Add($"- Engine: {(engine ? "running" : "not running")}");
        try
        {
            DispCtrlSettings s = SettingsStore.Load();
            GlobalSettings g = s.Global;
            lines.Add($"- Unison brightness: {(g.UnisonBrightness ? $"on at {g.UnisonLevel}%" : "off")}{(g.UnisonCalibrated ? ", calibrated" : "")}{(g.UnisonFollowsWindows ? ", replacing Windows brightness" : "")}");
            lines.Add($"- Night light: {(g.NightLight.Enabled ? "on" : "off")}; focus: {(g.Focus.Enabled ? "on" : "off")}; OLED care: {(g.OledCare.Enabled ? "on" : "off")}");
            int hidden = s.Monitors.Values.Count(m => m.HideTaskbar);
            lines.Add($"- Taskbar hidden on {hidden} display(s)");
            HotkeyStatus? status = HotkeyStatus.Read();
            int on = s.Hotkeys.Count(h => h.Enabled && h.IsComplete);
            lines.Add($"- Hotkeys: {on} on{(status is { Refused.Count: > 0 } ? $", refused by Windows: {string.Join(", ", status.Refused)}" : "")}");
        }
        catch (Exception ex) { lines.Add($"- Settings could not be read: {ex.Message}"); }
        return string.Join("\n", lines);
    }

    private static string Tail(string path, int lines)
    {
        try
        {
            if (!File.Exists(path)) return "(no log)";
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var all = new Queue<string>();
            while (reader.ReadLine() is { } line)
            {
                all.Enqueue(line);
                if (all.Count > lines) all.Dequeue();
            }
            return string.Join("\n", all);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return $"(log unreadable: {ex.Message})"; }
    }
}
