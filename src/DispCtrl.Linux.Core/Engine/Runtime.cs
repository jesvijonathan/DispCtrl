namespace DispCtrl.Linux.Engine;

/// <summary>Where the engine's socket and session state live.</summary>
public static class Runtime
{
    /// <summary>The longest path a Unix socket address holds (sun_path, 108
    /// bytes with its terminator). Longer, and binding threw out of the engine
    /// before it had said anything useful.</summary>
    public const int MaxSocketPath = 107;

    /// <summary><c>$XDG_RUNTIME_DIR</c> - per user, mode 0700, cleared at logout,
    /// which is also when X forgets its ramps. Without it, a 0700 folder under
    /// <c>~/.cache</c>: never <c>/tmp</c>, where another user could reach the
    /// socket or plant one first.</summary>
    public static string Directory
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (!string.IsNullOrEmpty(xdg) && System.IO.Directory.Exists(xdg)) return xdg;
            var cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (string.IsNullOrEmpty(cache))
                cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
            var dir = Path.Combine(cache, "dispctrl-linux");
            System.IO.Directory.CreateDirectory(dir);
            File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return dir;
        }
    }

    public static string SocketPath => Path.Combine(Directory, "dispctrl-linux.sock");

    public static string OwnedRampsPath => Path.Combine(Directory, "dispctrl-linux.ramps");

    public static HashSet<string> LoadOwnedRamps()
    {
        try
        {
            return new HashSet<string>(
                File.ReadAllLines(OwnedRampsPath).Where(l => l.Length > 0),
                StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    public static void SaveOwnedRamps(IEnumerable<string> owned)
    {
        try
        {
            var list = owned.ToList();
            if (list.Count == 0) File.Delete(OwnedRampsPath);
            else File.WriteAllLines(OwnedRampsPath, list);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
