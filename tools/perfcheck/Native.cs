using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DispCtrl.PerfCheck;

/// <summary>What a process costs, read precisely enough to tell a code change from noise.</summary>
/// <remarks>
/// <see cref="Process.TotalProcessorTime"/> advances in scheduler ticks of
/// 15.6 ms, so an idle process reads 0 or 78 ms at random: that is how an
/// earlier measurement here blamed a change for noise. Cycle counts are exact,
/// and context switches are the wake-ups that idle cost is really made of.
/// </remarks>
internal readonly record struct ProcessSample(ulong Cycles, ulong ContextSwitches, long WorkingSet, long Private,
                                              int Handles, int Threads, uint Gdi, uint User, long Taken)
{
    public double CpuMsSince(ProcessSample earlier) => (Cycles - earlier.Cycles) / Native.CyclesPerMs;
    public double SecondsSince(ProcessSample earlier) => Stopwatch.GetElapsedTime(earlier.Taken, Taken).TotalSeconds;

    /// <summary>Context switches since <paramref name="earlier"/>, or null when a thread exited between the two.</summary>
    /// <remarks>A thread that exits takes its count with it, so the total can fall; unsigned, that read as 1.8e19.</remarks>
    public long? SwitchesSince(ProcessSample earlier) => ContextSwitches >= earlier.ContextSwitches ? (long)(ContextSwitches - earlier.ContextSwitches) : null;
}

internal static partial class Native
{
    /// <summary>Reference cycles per millisecond on this machine, measured once by spinning.</summary>
    public static double CyclesPerMs { get; } = Calibrate();

    private static double Calibrate()
    {
        _ = QueryThreadCycleTime(GetCurrentThread(), out ulong c0);
        long t0 = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(t0).TotalMilliseconds < 60) { }
        _ = QueryThreadCycleTime(GetCurrentThread(), out ulong c1);
        return (c1 - c0) / Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    }

    public static ProcessSample Sample(Process process)
    {
        process.Refresh();
        (ulong cycles, ulong switches) = CyclesAndSwitches(process.Id);
        return new ProcessSample(cycles, switches, process.WorkingSet64, process.PrivateMemorySize64, process.HandleCount,
            process.Threads.Count, GetGuiResources(process.Handle, 0), GetGuiResources(process.Handle, 1), Stopwatch.GetTimestamp());
    }

    /// <summary>The process's CPU cycles and the context switches of its live threads.</summary>
    /// <remarks>
    /// One <c>NtQuerySystemInformation(SystemProcessInformation)</c> snapshot.
    /// x64 layouts: the process record is 0x100 bytes, cycle time at 0x18 and
    /// the process id at 0x50; each 0x50-byte thread record that follows keeps
    /// its context switches at 0x40.
    /// </remarks>
    public static unsafe (ulong Cycles, ulong ContextSwitches) CyclesAndSwitches(int pid)
    {
        int size = 1 << 20;
        while (true)
        {
            byte[] buffer = new byte[size];
            fixed (byte* start = buffer)
            {
                int status = NtQuerySystemInformation(5, start, size, out int needed);
                if (status == unchecked((int)0xC0000004)) { size = Math.Max(size * 2, needed + 65536); continue; }
                if (status != 0) return (0, 0);
                byte* entry = start;
                while (true)
                {
                    if ((long)*(nint*)(entry + 0x50) == pid)
                    {
                        uint threads = *(uint*)(entry + 4);
                        ulong switches = 0;
                        for (uint i = 0; i < threads; i++) switches += *(uint*)(entry + 0x100 + i * 0x50 + 0x40);
                        return (*(ulong*)(entry + 0x18), switches);
                    }
                    uint next = *(uint*)entry;
                    if (next == 0) return (0, 0);
                    entry += next;
                }
            }
        }
    }

    /// <summary>Peak working set from a process handle, which stays valid after the process exits.</summary>
    public static long PeakWorkingSet(nint process)
    {
        var counters = new MemoryCounters { Size = (uint)Marshal.SizeOf<MemoryCounters>() };
        return K32GetProcessMemoryInfo(process, ref counters, counters.Size) ? (long)counters.PeakWorkingSetSize : 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryCounters
    {
        public uint Size, PageFaultCount;
        public nuint PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
            QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage;
    }

    /// <summary>A process's exact CPU time from its cycle count; works on an exited process's handle.</summary>
    public static double CpuMs(nint process) => QueryProcessCycleTime(process, out ulong cycles) ? cycles / CyclesPerMs : double.NaN;

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryProcessCycleTime(nint process, out ulong cycles);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool K32GetProcessMemoryInfo(nint process, ref MemoryCounters counters, uint size);

    [LibraryImport("ntdll.dll")]
    private static unsafe partial int NtQuerySystemInformation(int infoClass, byte* buffer, int length, out int needed);
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryThreadCycleTime(nint thread, out ulong cycles);
    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentThread();
    [LibraryImport("user32.dll")]
    public static partial uint GetGuiResources(nint process, uint flags);
    [LibraryImport("winmm.dll")]
    public static partial uint timeBeginPeriod(uint period);
    [LibraryImport("winmm.dll")]
    public static partial uint timeEndPeriod(uint period);

    // ---------------------------------------------------------------- windows

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }

    public static Rect RectOf(nint hwnd) { _ = GetWindowRect(hwnd, out Rect r); return r; }

    public static bool IsShown(nint hwnd) => IsWindowVisible(hwnd) && !IsCloaked(hwnd);

    public static bool IsCloaked(nint hwnd) => DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    public static bool IsForeground(nint hwnd) => GetForegroundWindow() == hwnd;

    /// <summary>Top-level windows of a process with the given class, and title when one is given.</summary>
    public static List<nint> WindowsOf(int pid, string cls, string? title = null)
    {
        var found = new List<nint>();
        var name = new StringBuilder(256);
        EnumWindows((hwnd, parameter) =>
        {
            _ = GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner != pid) return true;
            name.Clear(); _ = GetClassName(hwnd, name, name.Capacity);
            if (name.ToString() != cls) return true;
            if (title is not null) { name.Clear(); _ = GetWindowText(hwnd, name, name.Capacity); if (name.ToString() != title) return true; }
            found.Add(hwnd);
            return true;
        }, 0);
        return found;
    }

    public static void Close(nint hwnd) => _ = PostMessage(hwnd, 0x0010, 0, 0); // WM_CLOSE

    private delegate bool EnumProc(nint hwnd, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc callback, nint parameter);
    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hwnd, StringBuilder name, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hwnd, StringBuilder name, int capacity);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hwnd, out Rect rect);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hwnd);
    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);
    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint hwnd, uint attribute, out int value, int size);
}
