using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DispCtrl.Core.Settings;

/// <summary>The Windows session this process runs in, for names scoped to it.</summary>
public static partial class Session
{
    /// <remarks>
    /// Not <c>Process.GetCurrentProcess().SessionId</c>: .NET answers that by
    /// snapshotting every process on the machine to read one number: 7.9 ms
    /// against 0.28 ms for this, warm, in every process that names a pipe or
    /// a mutex - each <c>dispctrl</c> command, app and engine start. (A first
    /// run after a build reads far higher; that is the antivirus scanning new
    /// DLLs, not this. Measure warm.)
    /// </remarks>
    public static int Id { get; } = Current();

    private static int Current()
    {
        if (ProcessIdToSessionId((uint)Environment.ProcessId, out uint id)) return (int)id;
        using var process = Process.GetCurrentProcess();
        return process.SessionId;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ProcessIdToSessionId(uint processId, out uint sessionId);
}
