using System.Diagnostics;

namespace Umbra.App.Services;

/// <summary>Whether the engine is running, and what it costs.</summary>
public readonly record struct EngineStatus(bool Running, int ProcessId, double MemoryMb)
{
    public static EngineStatus Stopped => new(false, 0, 0);
}

/// <summary>
/// Starts, stops and observes the background engine.
/// </summary>
/// <remarks>
/// The two processes are deliberately loosely coupled: configuration passes
/// through the settings file, which the engine watches, so this class only has
/// to handle lifetime. That keeps the app from being on the engine's critical
/// path — the taskbar keeps working whether or not this window is open, or
/// crashes.
/// </remarks>
public sealed class EngineController
{
    private const string ProcessName = "Umbra.Engine";
    private const string StopEventName = @"Local\Umbra.Engine.Stop";

    public string? EnginePath { get; }

    public EngineController() => EnginePath = LocateEngine();

    public static EngineStatus Query()
    {
        Process[] found = Process.GetProcessesByName(ProcessName);
        try
        {
            if (found.Length == 0) return EngineStatus.Stopped;

            Process p = found[0];
            return new EngineStatus(true, p.Id, p.WorkingSet64 / (1024.0 * 1024.0));
        }
        catch (InvalidOperationException)
        {
            // Exited between the enumeration and the read.
            return EngineStatus.Stopped;
        }
        finally
        {
            foreach (Process p in found) p.Dispose();
        }
    }

    public bool Start()
    {
        if (EnginePath is null || Query().Running) return false;

        var psi = new ProcessStartInfo(EnginePath, "run")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(EnginePath)!,
        };

        using Process? p = Process.Start(psi);
        return p is not null;
    }

    /// <summary>
    /// Asks the engine to restore the taskbars and exit.
    /// </summary>
    /// <remarks>
    /// Signals the engine's own shutdown event rather than killing the process.
    /// A kill would skip the restore path and leave a taskbar parked off-screen,
    /// recoverable only by restarting explorer.
    /// </remarks>
    public static bool Stop()
    {
        if (!EventWaitHandle.TryOpenExisting(StopEventName, out EventWaitHandle? stop))
            return false;

        using (stop) stop.Set();
        return true;
    }

    /// <summary>
    /// Finds the engine next to the app, falling back to the sibling build
    /// output so the app is runnable straight from a dev build.
    /// </summary>
    private static string? LocateEngine()
    {
        string appDir = AppContext.BaseDirectory;

        // Packaged layout: both executables sit in the same folder.
        string beside = Path.Combine(appDir, "Umbra.Engine.exe");
        if (File.Exists(beside)) return beside;

        // Dev layout: src/Umbra.App/bin/<cfg>/<tfm>/<rid>/ -> src/Umbra.Engine/bin/...
        var dir = new DirectoryInfo(appDir);
        for (int up = 0; up < 6 && dir is not null; up++, dir = dir.Parent)
        {
            if (!string.Equals(dir.Name, "Umbra.App", StringComparison.OrdinalIgnoreCase)) continue;

            string relative = Path.GetRelativePath(dir.FullName, appDir);
            string candidate = Path.Combine(dir.Parent!.FullName, "Umbra.Engine", relative, "Umbra.Engine.exe");
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            break;
        }

        return null;
    }
}
