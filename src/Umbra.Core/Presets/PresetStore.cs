using System.Text.Json;
using System.Text.Json.Serialization;
using Umbra.Core.Settings;

namespace Umbra.Core.Presets;

/// <summary>Source-generated JSON for presets. See <c>SettingsJsonContext</c>.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Preset))]
public partial class PresetJsonContext : JsonSerializerContext;

/// <summary>
/// Presets on disk, one file each.
/// </summary>
/// <remarks>
/// A flat folder of readable JSON, deliberately. It means a preset can be
/// mailed, put in a dotfiles repo or dropped in by hand, and it means a user
/// who wants to know what a preset will do can simply read it — neither of
/// which is true of a database or a blob in the registry.
/// </remarks>
public static class PresetStore
{
    public static string Directory { get; } = Path.Combine(SettingsStore.Directory, "presets");

    /// <summary>Every preset on disk, by name.</summary>
    /// <remarks>
    /// A file that will not parse is skipped rather than throwing: one bad
    /// preset — hand-edited, or from someone else's machine — must not take the
    /// whole list down with it.
    /// </remarks>
    public static List<Preset> Load()
    {
        var result = new List<Preset>();
        if (!System.IO.Directory.Exists(Directory)) return result;

        foreach (string file in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            Preset? p = Read(file);
            if (p is not null) result.Add(p);
        }

        result.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    /// <summary>Reads one preset file, or null when it will not parse.</summary>
    public static Preset? Read(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            Preset? p = JsonSerializer.Deserialize(json, PresetJsonContext.Default.Preset);
            if (p is null) return null;

            // A file renamed on disk should win over the name inside it, so
            // what the folder shows and what the app shows never disagree.
            string stem = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrWhiteSpace(stem)) p.Name = stem;

            return p;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string PathFor(string name) => Path.Combine(Directory, FileName(name));

    public static void Save(Preset preset)
    {
        System.IO.Directory.CreateDirectory(Directory);
        preset.SavedUtc = DateTimeOffset.UtcNow;

        string json = JsonSerializer.Serialize(preset, PresetJsonContext.Default.Preset);

        // Written to a temporary file and moved into place, so a crash or a
        // full disk mid-write leaves the previous preset intact rather than a
        // truncated file that will not parse.
        string target = PathFor(preset.Name);
        string temp = target + ".tmp";

        File.WriteAllText(temp, json);
        File.Move(temp, target, overwrite: true);
    }

    public static void Delete(string name)
    {
        string path = PathFor(name);
        if (File.Exists(path)) File.Delete(path);
    }

    public static void Rename(string from, string to)
    {
        Preset? p = Read(PathFor(from));
        if (p is null) return;

        p.Name = to;
        Save(p);
        Delete(from);
    }

    /// <summary>Copies a preset out to a path the user chose.</summary>
    public static void Export(Preset preset, string destination)
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? ".");
        File.WriteAllText(destination, JsonSerializer.Serialize(preset, PresetJsonContext.Default.Preset));
    }

    /// <summary>
    /// Brings a preset file into the folder, returning the name it landed under.
    /// </summary>
    /// <remarks>
    /// Never overwrites: an import that silently replaced a preset of the same
    /// name would destroy the user's own work on the strength of a filename
    /// chosen by someone else. A clashing name gets a numeric suffix instead.
    /// </remarks>
    public static string? Import(string source)
    {
        Preset? p = Read(source);
        if (p is null) return null;

        string name = p.Name;
        for (int i = 2; File.Exists(PathFor(name)); i++) name = $"{p.Name} ({i})";

        p.Name = name;
        Save(p);
        return name;
    }

    /// <summary>
    /// Turns a preset name into a file name it is safe to write.
    /// </summary>
    /// <remarks>
    /// The name is the file stem, so it has to survive the filesystem. Invalid
    /// characters become underscores rather than being dropped, which keeps two
    /// names that differ only in punctuation from collapsing onto one file.
    /// </remarks>
    private static string FileName(string name)
    {
        char[] chars = name.Trim().ToCharArray();
        char[] invalid = Path.GetInvalidFileNameChars();

        for (int i = 0; i < chars.Length; i++)
            if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';

        string stem = new string(chars).Trim();
        return (stem.Length == 0 ? "Untitled" : stem) + ".json";
    }
}
