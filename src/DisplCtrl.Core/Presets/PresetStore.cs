using System.Text.Json;
using DisplCtrl.Core.Caching;
using System.Text.Json.Serialization;
using DisplCtrl.Core.Settings;

namespace DisplCtrl.Core.Presets;

/// <summary>Source-generated JSON for presets. See <c>SettingsJsonContext</c>.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
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
    private static readonly BoundedCache<(string Path, long Modified, long Length, long Created), Preset> Cache = new(128);

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
            var file = new FileInfo(path);
            if (!file.Exists) return null;
            var key = (file.FullName.ToUpperInvariant(), file.LastWriteTimeUtc.Ticks, file.Length, file.CreationTimeUtc.Ticks);
            Preset cached = Cache.Get(key, TimeSpan.FromSeconds(2), () =>
            {
                Preset p = Parse(File.ReadAllText(file.FullName));
                p.Name = Path.GetFileNameWithoutExtension(file.FullName);
                return p;
            });
            // The store's cached graph is never exposed to mutable callers.
            return cached.Copy();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string ToJson(Preset preset) => JsonSerializer.Serialize(preset, PresetJsonContext.Default.Preset);

    public static Preset Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("monitors", out _))
            throw new FormatException("A preset must contain a monitors object.");
        Preset p = document.RootElement.Deserialize(PresetJsonContext.Default.Preset)
            ?? throw new FormatException("The preset is empty.");
        PresetValidation.Validate(p);
        return p;
    }

    public static string PathFor(string name) => Path.Combine(Directory, FileName(name));

    /// <summary>
    /// True when a preset would land on a file that already exists.
    /// </summary>
    /// <remarks>
    /// Asked in terms of the file, not the name. Two different names can
    /// sanitise to one file — "Work/Home" and "Work_Home" both become
    /// <c>Work_Home.json</c> — so a caller that only compares display names
    /// will happily overwrite a preset it did not mean to touch.
    /// </remarks>
    public static bool Exists(string name) => File.Exists(PathFor(name));

    /// <summary>True when two names resolve to the same file on disk.</summary>
    public static bool SameFile(string a, string b) =>
        string.Equals(FileName(a), FileName(b), StringComparison.OrdinalIgnoreCase);

    public static void Save(Preset preset)
    {
        PresetValidation.Validate(preset);
        System.IO.Directory.CreateDirectory(Directory);
        preset.SavedUtc = DateTimeOffset.UtcNow;

        string json = JsonSerializer.Serialize(preset, PresetJsonContext.Default.Preset);

        // Written to a temporary file and moved into place, so a crash or a
        // full disk mid-write leaves the previous preset intact rather than a
        // truncated file that will not parse.
        string target = PathFor(preset.Name);
        string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";

        File.WriteAllText(temp, json);
        File.Move(temp, target, overwrite: true);
        Cache.Clear();
    }

    public static void Delete(string name)
    {
        string path = PathFor(name);
        if (File.Exists(path)) File.Delete(path);
        Cache.Clear();
    }

    /// <remarks>
    /// The delete is skipped when both names resolve to the same file, which is
    /// not a hypothetical: anything differing only in characters the filesystem
    /// rejects — or in trailing whitespace — sanitises to one stem. Deleting
    /// unconditionally destroyed the file that had just been written.
    /// </remarks>
    public static void Rename(string from, string to)
    {
        Preset? p = Read(PathFor(from));
        if (p is null) return;

        if (!SameFile(from, to) && Exists(to)) throw new IOException("A preset with that name already exists.");
        p.Name = to;
        Save(p);

        if (!SameFile(from, to)) Delete(from);
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

        // Bounded: a pathological name that always sanitises to the same stem
        // would otherwise spin here forever.
        for (int i = 2; Exists(name) && i < 1000; i++) name = $"{p.Name} ({i})";
        if (Exists(name)) return null;

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
