using System.Text.Json;
using System.Text.Json.Serialization;

namespace Umbra.Core.Settings;

/// <summary>
/// Source-generated JSON context. Reflection-based serialization does not
/// survive Native AOT trimming, so the engine must use this.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(UmbraSettings))]
public partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>Loads and saves <see cref="UmbraSettings"/>.</summary>
public static class SettingsStore
{
    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Umbra");

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
    public static UmbraSettings Load()
    {
        try
        {
            if (!File.Exists(Path_)) return new UmbraSettings();

            string json = File.ReadAllText(Path_);
            UmbraSettings? s = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.UmbraSettings);
            if (s is not null) return s;

            Quarantine();
            return new UmbraSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Quarantine();
            return new UmbraSettings();
        }
    }

    /// <summary>Writes settings atomically.</summary>
    /// <remarks>
    /// Written to a temp file and moved into place, so an interrupted write
    /// cannot leave a half-written file that the next load would quarantine.
    /// </remarks>
    public static void Save(UmbraSettings settings)
    {
        System.IO.Directory.CreateDirectory(Directory);

        string json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.UmbraSettings);
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
