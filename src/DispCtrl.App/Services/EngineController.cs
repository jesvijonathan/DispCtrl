using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DispCtrl.App.Services;

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
public sealed partial class EngineController
{
    private const string ProcessName = "DispCtrl.Engine";
    private const string StopEventName = @"Local\DispCtrl.Engine.Stop";
    private const string InstanceMutexName = @"Local\DispCtrl.Engine";
    private static readonly string PreviousProcessName = "Displ" + "Ctrl.Engine";
    private static readonly string PreviousStopEventName = @"Local\Displ" + "Ctrl.Engine.Stop";

    public string? EnginePath { get; }

    public EngineController() => EnginePath = LocateEngine();

    /// <summary>Whether the engine is running, its process id and working set.</summary>
    /// <remarks>
    /// Asked every two seconds while the window is open and on every quick panel
    /// summons, so it has to be cheap. <c>Process.GetProcessesByName</c>
    /// snapshots every process on the machine: measured at 3.7 ms a call, on the
    /// UI thread. The engine's single-instance mutex is taken before anything
    /// else and released after the taskbars are restored, so it exists exactly
    /// as long as the engine does, and opens in microseconds. The process itself
    /// is looked up once per engine lifetime and then read through a handle.
    /// </remarks>
    public static EngineStatus Query()
    {
        if (!Mutex.TryOpenExisting(InstanceMutexName, out Mutex? instance)) { Forget(); return EngineStatus.Stopped; }
        instance.Dispose();

        lock (Gate)
        {
            if (_handle == 0 || WaitForSingleObject(_handle, 0) == 0) { Forget(); Find(); }
            if (_handle == 0) return new EngineStatus(true, 0, 0);
            var counters = new MemoryCounters { Size = (uint)Marshal.SizeOf<MemoryCounters>() };
            double mb = GetProcessMemoryInfo(_handle, ref counters, counters.Size) ? counters.WorkingSetSize / (1024.0 * 1024.0) : 0;
            return new EngineStatus(true, _pid, mb);
        }
    }

    private static readonly Lock Gate = new();
    private static nint _handle;
    private static int _pid;

    private static void Find()
    {
        Process[] found = Process.GetProcessesByName(ProcessName);
        try
        {
            if (found.Length == 0) return;
            _pid = found[0].Id;
            _handle = OpenProcess(QueryLimitedInformation | Synchronize, false, (uint)_pid);
            if (_handle == 0) _pid = 0;
        }
        finally
        {
            foreach (Process p in found) p.Dispose();
        }
    }

    private static void Forget()
    {
        lock (Gate)
        {
            if (_handle != 0) CloseHandle(_handle);
            _handle = 0;
            _pid = 0;
        }
    }

    private const uint QueryLimitedInformation = 0x1000, Synchronize = 0x00100000;

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryCounters
    {
        public uint Size, PageFaultCount;
        public nuint PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
            QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage;
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(nint handle, uint ms);

    [LibraryImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessMemoryInfo(nint process, ref MemoryCounters counters, uint size);

    public bool Start()
    {
        if (EnginePath is null || Query().Running) return false;

        // An in-place upgrade can leave the earlier engine alive until it is
        // asked to restore the taskbars. Use its public shutdown event and wait
        // briefly so two independently named engines never manage Explorer.
        Signal(PreviousStopEventName);
        for (int attempt = 0; attempt < 20 && IsRunning(PreviousProcessName); attempt++)
            Thread.Sleep(100);

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
        bool current = Signal(StopEventName);
        bool previous = Signal(PreviousStopEventName);
        return current || previous;
    }

    private static bool Signal(string eventName)
    {
        if (!EventWaitHandle.TryOpenExisting(eventName, out EventWaitHandle? stop)) return false;
        using (stop) stop.Set();
        return true;
    }

    private static bool IsRunning(string processName)
    {
        Process[] processes = Process.GetProcessesByName(processName);
        foreach (Process process in processes) process.Dispose();
        return processes.Length > 0;
    }

    /// <summary>
    /// Finds the engine next to the app, falling back to the sibling build
    /// output so the app is runnable straight from a dev build.
    /// </summary>
    private static string? LocateEngine()
    {
        string appDir = AppContext.BaseDirectory;

        // Packaged layout: both executables sit in the same folder.
        string beside = Path.Combine(appDir, "DispCtrl.Engine.exe");
        if (File.Exists(beside)) return beside;

        // Dev layout: src/DispCtrl.App/bin/<cfg>/<tfm>/<rid>/ -> src/DispCtrl.Engine/bin/...
        var dir = new DirectoryInfo(appDir);
        for (int up = 0; up < 6 && dir is not null; up++, dir = dir.Parent)
        {
            if (!string.Equals(dir.Name, "DispCtrl.App", StringComparison.OrdinalIgnoreCase)) continue;

            string relative = Path.GetRelativePath(dir.FullName, appDir);
            string candidate = Path.Combine(dir.Parent!.FullName, "DispCtrl.Engine", relative, "DispCtrl.Engine.exe");
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            break;
        }

        return null;
    }
}
