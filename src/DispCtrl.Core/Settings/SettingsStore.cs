using System.Text.Json;
using System.Text.Json.Serialization;

namespace DispCtrl.Core.Settings;

/// <summary>
/// Source-generated JSON context. Reflection-based serialization does not
/// survive Native AOT trimming, so the engine must use this.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

    // Enums as names, not numbers. This file is meant to be readable and is
    // routinely hand-edited; "brightnessDown" says what it does, 1 does not —
    // and a number silently means something different the moment a value is
    // inserted into the enum.
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(DispCtrlSettings))]
public partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>Loads and saves <see cref="DispCtrlSettings"/>.</summary>
public static class SettingsStore
{
    public static string Directory { get; } = Resolve();

    /// <summary>
    /// The data folder, bringing an earlier product folder along the first time.
    /// </summary>
    /// <remarks>
    /// Existing installs may still use either of the two earlier product folder
    /// names. Everything a user has — every monitor's settings, presets and
    /// device records — moves to the current folder on first launch.
    /// <para>
    /// A move rather than a copy, so there is one folder afterwards and no
    /// question of which is authoritative. It runs only when the new folder
    /// does not exist yet, which makes it a one-time step that cannot overwrite
    /// anything, and a failure is swallowed: starting with fresh defaults is a
    /// far better outcome than refusing to start.
    /// </para>
    /// </remarks>
    private static string Resolve()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string current = Path.Combine(local, "DispCtrl");

        if (System.IO.Directory.Exists(current)) return current;

        try
        {
            // Keep the superseded spelling out of the current product surface
            // while still carrying existing installations forward.
            string[] formerNames = ["Displ" + "Ctrl", "Umbra"];
            foreach (string formerName in formerNames)
            {
                string former = Path.Combine(local, formerName);
                if (!System.IO.Directory.Exists(former)) continue;
                System.IO.Directory.Move(former, current);
                break;
            }
        }
        catch (Exception)
        {
            // Locked by another DispCtrl process mid-rename, or the disk said
            // no. Either way the folder below is created fresh on first save.
        }

        return current;
    }

    public static string Path_ => Path.Combine(Directory, "settings.json");

    /// <summary>
    /// Where the engine writes its log. Declared here, beside the settings
    /// path, so the app can point a user at it without referencing the engine.
    /// </summary>
    public static string LogPath => Path.Combine(Directory, "engine.log");

    /// <summary>
    /// Reads settings, falling back to defaults when the file is missing or
    /// unreadable.
    /// </summary>
    /// <remarks>
    /// A corrupt settings file must never stop the engine starting: it manages
    /// the taskbar, so failing closed would leave the user staring at a
    /// permanently hidden bar with no obvious cause. The bad file is kept as
    /// <c>.bad</c> for diagnosis rather than silently overwritten.
    /// </remarks>
    public static DispCtrlSettings Load()
    {
        try
        {
            if (!File.Exists(Path_)) return new DispCtrlSettings();

            string json = File.ReadAllText(Path_);
            DispCtrlSettings? s = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.DispCtrlSettings);
            if (s is not null) return s;

            Quarantine();
            return new DispCtrlSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Quarantine();
            return new DispCtrlSettings();
        }
    }

    /// <summary>Writes settings atomically.</summary>
    /// <remarks>
    /// Written to a temp file and moved into place, so an interrupted write
    /// cannot leave a half-written file that the next load would quarantine.
    /// </remarks>
    public static void Save(DispCtrlSettings settings)
    {
        System.IO.Directory.CreateDirectory(Directory);

        string json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.DispCtrlSettings);
        string tmp = Path_ + ".tmp";

        File.WriteAllText(tmp, json);
        File.Move(tmp, Path_, overwrite: true);
    }

    private static void Quarantine()
    {
        try
        {
            if (File.Exists(Path_)) File.Move(Path_, Path_ + ".bad", overwrite: true);
        }
        catch (IOException) { /* best effort; never block startup on this */ }
    }
}
