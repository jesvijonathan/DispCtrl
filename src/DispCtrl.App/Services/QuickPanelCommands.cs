using System.Diagnostics;
using DispCtrl.Core.Settings;

namespace DispCtrl.App.Services;

/// <summary>
/// Runs a tile somebody made: a <c>dispctrl</c> command, or a program, script,
/// document or URL.
/// </summary>
/// <remarks>
/// Commands go through the command-line executable rather than being parsed
/// here. The command line is DispCtrl's scriptable surface and owns its own
/// grammar; a second parser in the app would be a second place for the same
/// words to mean something slightly different.
/// </remarks>
public static class QuickPanelCommands
{
    /// <summary>Longest a command may take before the tile stops waiting for it.</summary>
    /// <remarks>
    /// The command itself is not stopped - it may be halfway through a DDC/CI
    /// write, and killing it there is worse than letting it finish unwatched.
    /// </remarks>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>Runs the tile and says, in a few words, how it went.</summary>
    public static async Task<string> RunAsync(QuickPanelCustomTile tile)
    {
        if (string.IsNullOrWhiteSpace(tile.Target))
            return $"{tile.Label}: nothing to run - edit the tile to give it a command.";

        try
        {
            return tile.Kind == QuickPanelCustomKind.Command
                ? await RunCommandAsync(tile)
                : Open(tile);
        }
        catch (Exception ex)
        {
            return $"{tile.Label}: {ex.Message}";
        }
    }

    private static string Open(QuickPanelCustomTile tile)
    {
        // Shell-executed, as Explorer would: a URL opens in the browser, a
        // document in its program, ms-settings: in Settings.
        Process.Start(new ProcessStartInfo(tile.Target.Trim(), tile.Arguments ?? "")
        {
            UseShellExecute = true,
        });
        return $"{tile.Label}: opened.";
    }

    private static async Task<string> RunCommandAsync(QuickPanelCustomTile tile)
    {
        string? exe = LocateCli();
        if (exe is null)
            return $"{tile.Label}: dispctrl.exe was not found next to DispCtrl, in the build tree, or on PATH.";

        var start = new ProcessStartInfo(exe, tile.Target.Trim())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("dispctrl did not start.");

        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();

        using var cancel = new CancellationTokenSource(Patience);
        try
        {
            await process.WaitForExitAsync(cancel.Token);
        }
        catch (OperationCanceledException)
        {
            return $"{tile.Label}: still running after {Patience.TotalSeconds:0} seconds.";
        }

        // Exit codes as the command line documents them: 0 done, 1 refused,
        // 2 asked wrongly.
        string said = FirstLine(await error) ?? FirstLine(await output) ?? "";
        return process.ExitCode switch
        {
            0 => $"{tile.Label}: done.",
            2 => $"{tile.Label}: the command was not understood. {said}".TrimEnd(),
            _ => $"{tile.Label}: refused. {said}".TrimEnd(),
        };
    }

    private static string? FirstLine(string text)
    {
        foreach (string line in text.Split('\n'))
            if (line.Trim().Length > 0) return line.Trim();
        return null;
    }

    /// <summary>
    /// Finds <c>dispctrl.exe</c>: beside the app when installed, in the build
    /// tree during development, or on <c>PATH</c>.
    /// </summary>
    /// <remarks>
    /// The same order the engine is found in, for the same reason: a packaged
    /// install keeps the executables together, and a development checkout keeps
    /// each in its own project's <c>bin</c>.
    /// </remarks>
    public static string? LocateCli()
    {
        string appDir = AppContext.BaseDirectory;

        string beside = Path.Combine(appDir, "dispctrl.exe");
        if (File.Exists(beside)) return beside;

        var dir = new DirectoryInfo(appDir);
        for (int up = 0; up < 6 && dir is not null; up++, dir = dir.Parent)
        {
            if (!string.Equals(dir.Name, "DispCtrl.App", StringComparison.OrdinalIgnoreCase)) continue;

            string relative = Path.GetRelativePath(dir.FullName, appDir);
            string candidate = Path.Combine(dir.Parent!.FullName, "DispCtrl.Cli", relative, "dispctrl.exe");
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            break;
        }

        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                string candidate = Path.Combine(folder.Trim(), "dispctrl.exe");
                if (folder.Length > 0 && File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry. Skip it.
            }
        }

        return null;
    }

    /// <summary>
    /// A glyph as typed: the character itself, or its code point in hex.
    /// </summary>
    /// <remarks>
    /// A settings file edited by hand will usually say <c>E768</c> rather than
    /// the private-use character it stands for, and both should work.
    /// </remarks>
    public static string Glyph(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "\uE768";
        string t = text.Trim();
        if (t.Length == 1) return t;

        if (t.StartsWith("\\u", StringComparison.OrdinalIgnoreCase) || t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t[2..];
        if (t.StartsWith("&#x", StringComparison.OrdinalIgnoreCase)) t = t[3..].TrimEnd(';');

        return int.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out int code) && code is > 0 and <= 0xFFFF
            ? ((char)code).ToString()
            : "\uE768";
    }
}
