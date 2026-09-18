using System.Globalization;
using DisplCtrl.Core.Settings;

namespace DisplCtrl.Engine;

/// <summary>
/// Append-only rolling log.
/// </summary>
/// <remarks>
/// Exists because of a specific failure: the predecessor to this engine exited
/// with code 1 and nothing recorded why, so the cause was unrecoverable after
/// the fact. Logging is the difference between "it broke again" and a
/// diagnosis. It must therefore never be the thing that breaks — every failure
/// here is swallowed.
/// </remarks>
internal static class Log
{
    private const long MaxBytes = 256 * 1024;
    private static readonly Lock Gate = new();
    private static bool _enabled = true;
    private static bool _echo;

    public static string Path => System.IO.Path.Combine(SettingsStore.Directory, "engine.log");

    /// <summary><paramref name="echo"/> also writes to stdout, for foreground runs.</summary>
    public static void Configure(bool enabled, bool echo)
    {
        _enabled = enabled;
        _echo = echo;
    }

    public static void Write(string message)
    {
        string line = string.Create(CultureInfo.InvariantCulture,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}");

        if (_echo) Console.WriteLine(line);
        if (!_enabled) return;

        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(SettingsStore.Directory);
                Roll();
                File.AppendAllText(Path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Deliberately unconditional: a full disk, a locked file or a
            // permissions change must not take the engine down with it.
        }
    }

    private static void Roll()
    {
        var fi = new FileInfo(Path);
        if (!fi.Exists || fi.Length <= MaxBytes) return;

        string old = Path + ".1";
        if (File.Exists(old)) File.Delete(old);
        File.Move(Path, old);
    }
}
