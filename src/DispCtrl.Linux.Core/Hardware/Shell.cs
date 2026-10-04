using System.Diagnostics;

namespace DispCtrl.Linux.Hardware;

/// <summary>The outcome of one external tool run. <see cref="ExitCode"/> is -1
/// when the tool could not be started and -2 when it timed out.</summary>
public readonly record struct ShellResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>Runs the external tools the Linux client wraps (ddcutil, xrandr).</summary>
/// <remarks>
/// Arguments go through <see cref="ProcessStartInfo.ArgumentList"/>, never a
/// joined string: an output name or a value typed by a person reached the
/// command line verbatim before, so <c>"DP-1 --off"</c> would have been two
/// xrandr arguments. Every run has a timeout, because a DDC/CI channel that
/// stops answering can hold ddcutil for a long time, and the engine serves
/// every client through one gate.
/// </remarks>
public static class Shell
{
    public const int DefaultTimeoutMs = 20_000;

    /// <summary>Whether <paramref name="exe"/> is on PATH, looked up directly
    /// rather than by starting <c>which</c>, which a minimal or confined system
    /// may not have.</summary>
    public static bool TryWhich(string exe) => Which(exe) is not null;

    public static string? Which(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        foreach (var dir in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, exe);
            if (File.Exists(candidate)
                && (File.GetUnixFileMode(candidate) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0)
            {
                return candidate;
            }
        }
        return null;
    }

    public static ShellResult Run(string fileName, IEnumerable<string> arguments, int timeoutMs = DefaultTimeoutMs)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new ShellResult(-1, string.Empty, $"{fileName}: not found");
        }

        // Both streams are drained concurrently: reading one to its end first
        // deadlocks once the other fills its pipe buffer.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return new ShellResult(-2, string.Empty, $"{fileName}: no answer within {timeoutMs / 1000} s");
        }
        process.WaitForExit();
        return new ShellResult(process.ExitCode, stdout.Result, stderr.Result);
    }
}
