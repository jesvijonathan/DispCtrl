using System.Text.Json;
using System.Text.Json.Serialization;

namespace DispCtrl.Linux.Settings;

/// <summary>Reads and writes <c>settings.json</c>, shared by the command line,
/// the window and the engine.</summary>
/// <remarks>
/// The same rules as the Windows <c>SettingsStore</c>, which paid for each: a save
/// is a whole file renamed over the old one, so a reader never sees half of it;
/// a change is read, modified and written under a lock file, so two processes
/// changing different settings do not undo each other; properties this build
/// does not know are kept; and text that is not JSON is set aside as
/// <c>settings.json.bad</c> rather than overwritten, with defaults in its place.
/// </remarks>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary><c>$DISPCTRL_LINUX_CONFIG_DIR</c> when set (tests and scratch
    /// runs), else <c>$XDG_CONFIG_HOME/dispctrl-linux</c>, else
    /// <c>~/.config/dispctrl-linux</c>.</summary>
    public static string Directory
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable("DISPCTRL_LINUX_CONFIG_DIR");
            if (!string.IsNullOrEmpty(overridden)) return overridden;
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var root = !string.IsNullOrEmpty(xdg)
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(root, "dispctrl-linux");
        }
    }

    public static string FilePath => Path.Combine(Directory, "settings.json");

    public static LinuxSettings Load()
    {
        string text;
        try
        {
            text = File.ReadAllText(FilePath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Fresh();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<LinuxSettings>(text, Options) ?? new LinuxSettings();
            settings.Normalise();
            return settings;
        }
        catch (JsonException)
        {
            Quarantine();
            return Fresh();
        }
    }

    /// <summary>Loads, applies <paramref name="change"/> and saves, holding the
    /// lock for the whole of it.</summary>
    public static LinuxSettings Update(Action<LinuxSettings> change)
    {
        System.IO.Directory.CreateDirectory(Directory);
        using var held = AcquireLock();
        var settings = Load();
        change(settings);
        settings.Normalise();
        Save(settings);
        return settings;
    }

    private static void Save(LinuxSettings settings)
    {
        var temp = Path.Combine(Directory, $".settings.{Environment.ProcessId}.tmp");
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, FilePath, overwrite: true);
    }

    private static LinuxSettings Fresh()
    {
        var settings = new LinuxSettings();
        settings.Normalise();
        return settings;
    }

    private static void Quarantine()
    {
        try { File.Move(FilePath, FilePath + ".bad", overwrite: true); }
        catch (IOException) { }
    }

    private static FileStream AcquireLock()
    {
        var path = Path.Combine(Directory, ".settings.lock");
        var deadline = Environment.TickCount64 + 5000;
        while (true)
        {
            try
            {
                // FileShare.None is an exclusive flock on Linux: another
                // process opening it the same way fails until this is disposed.
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (Environment.TickCount64 < deadline)
            {
                Thread.Sleep(20);
            }
        }
    }
}
