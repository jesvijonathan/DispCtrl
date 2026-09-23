using System.Text.Json;
using DispCtrl.Core.Settings;

namespace DispCtrl.Core.Devices;

/// <summary>
/// Loads, layers and edits device definitions.
/// </summary>
/// <remarks>
/// Two places, one format. The shipped folder beside the executables holds the
/// definitions the project has reviewed (<c>devices/definitions</c> in the
/// repository). The user folder holds the ones made on this machine, and wins
/// over a shipped one for the same target, code by code - so a local correction
/// takes effect at once, and becomes everyone's once it is shared and merged.
/// </remarks>
public static class DeviceLibrary
{
    public static string ShippedFolder => Path.Combine(AppContext.BaseDirectory, "devices", "definitions");

    public static string UserFolder => Path.Combine(SettingsStore.Directory, "devices", "definitions");

    /// <summary>Overrides the folders, for checks that must not touch the real ones.</summary>
    public static (string Shipped, string User)? FoldersOverride { get; set; }

    private static string Shipped => FoldersOverride?.Shipped ?? ShippedFolder;
    private static string User => FoldersOverride?.User ?? UserFolder;

    /// <summary>Every definition in a folder, by target; unreadable files are reported, not fatal.</summary>
    public static Dictionary<string, DeviceDefinition> LoadFolder(string folder, List<string>? problems = null)
    {
        var result = new Dictionary<string, DeviceDefinition>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(folder)) return result;
        foreach (string file in Directory.EnumerateFiles(folder, "*.json"))
        {
            try
            {
                DeviceDefinition? d = JsonSerializer.Deserialize(File.ReadAllText(file), DeviceJsonContext.Default.DeviceDefinition);
                if (d is null) continue;
                List<string> wrong = DeviceDefinitions.Validate(d);
                if (wrong.Count > 0) { problems?.Add($"{Path.GetFileName(file)}: {string.Join("; ", wrong)}"); continue; }
                result[d.Target] = d;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                problems?.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        return result;
    }

    /// <summary>The definitions for one target as the two folders combine them.</summary>
    public static DeviceDefinition? Find(string target) => Layers(target).LastOrDefault().Definition;

    private static IEnumerable<(DeviceDefinition Definition, string Origin)> Layers(string target)
    {
        var shipped = LoadFolder(Shipped);
        var user = LoadFolder(User);
        if (shipped.TryGetValue(target, out var s)) yield return (s, "shipped");
        if (user.TryGetValue(target, out var u)) yield return (u, "local");
    }

    /// <summary>
    /// Every code with a definition that applies to a model, most specific last.
    /// </summary>
    /// <remarks>
    /// Order: every monitor, the manufacturer, whatever the model extends (and
    /// what those extend, each target once however the links loop), then the
    /// model itself; within a target, shipped then local. A later layer replaces
    /// an earlier one's definition of the same code whole, so a correction never
    /// half-merges with what it corrects.
    /// </remarks>
    public static Dictionary<byte, ResolvedControl> Resolve(string model)
    {
        var shipped = LoadFolder(Shipped);
        var user = LoadFolder(User);
        var result = new Dictionary<byte, ResolvedControl>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Apply(string target)
        {
            if (!visited.Add(target)) return;
            var layers = new List<(DeviceDefinition, string)>();
            if (shipped.TryGetValue(target, out var s)) layers.Add((s, "shipped " + target));
            if (user.TryGetValue(target, out var u)) layers.Add((u, "local " + target));
            foreach (var (definition, _) in layers)
                foreach (string link in definition.Extends)
                    if (!string.Equals(link, model, StringComparison.OrdinalIgnoreCase)) Apply(link);
            foreach (var (definition, origin) in layers)
                foreach (DefinedControl c in definition.Controls)
                    if (c.CodeValue is byte code) result[code] = new ResolvedControl(code, c, origin);
        }

        Apply("*");
        if (DeviceDefinitions.IsModel(model)) Apply(DeviceDefinitions.Brand(model));
        Apply(model);
        return result;
    }

    /// <summary>The local definition for a target, created empty when there is none.</summary>
    public static DeviceDefinition LoadLocal(string target)
    {
        string path = PathFor(target);
        if (File.Exists(path))
        {
            DeviceDefinition? d = JsonSerializer.Deserialize(File.ReadAllText(path), DeviceJsonContext.Default.DeviceDefinition);
            if (d is not null) return d;
        }
        return new DeviceDefinition { Target = target };
    }

    /// <summary>Validates and writes a local definition; returns its path.</summary>
    public static string SaveLocal(DeviceDefinition definition)
    {
        List<string> problems = DeviceDefinitions.Validate(definition);
        if (problems.Count > 0) throw new ArgumentException(string.Join("; ", problems));
        definition.Controls.Sort((a, b) => (a.CodeValue ?? 0).CompareTo(b.CodeValue ?? 0));
        string path = PathFor(definition.Target);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(definition, DeviceJsonContext.Default.DeviceDefinition));
        File.Move(temp, path, overwrite: true);
        return path;
    }

    /// <summary>Adds or replaces one code's definition in the local file for a target.</summary>
    public static string Map(string target, DefinedControl control, string? name = null)
    {
        DeviceDefinition d = LoadLocal(target);
        if (name is not null && d.Name is null) d.Name = name;
        d.Controls.RemoveAll(c => c.CodeValue == control.CodeValue);
        d.Controls.Add(control);
        return SaveLocal(d);
    }

    /// <summary>Removes one code from the local file for a target; false when it was not there.</summary>
    public static bool Unmap(string target, byte code)
    {
        DeviceDefinition d = LoadLocal(target);
        if (d.Controls.RemoveAll(c => c.CodeValue == code) == 0) return false;
        SaveLocal(d);
        return true;
    }

    /// <summary>Links a target to another, so it takes that one's mappings.</summary>
    public static string Link(string target, string to, bool remove = false)
    {
        DeviceDefinition d = LoadLocal(target);
        d.Extends.RemoveAll(e => string.Equals(e, to, StringComparison.OrdinalIgnoreCase));
        if (!remove) d.Extends.Add(to);
        return SaveLocal(d);
    }

    public static string PathFor(string target) =>
        Path.Combine(User, (target == "*" ? "common" : target) + ".json");

    /// <summary>Every local definition that bears on a model: its own, its brand's, common.</summary>
    public static List<DeviceDefinition> LocalFor(string model)
    {
        var result = new List<DeviceDefinition>();
        foreach (string target in new[] { "*", DeviceDefinitions.Brand(model), model }.Distinct())
            if (File.Exists(PathFor(target))) result.Add(LoadLocal(target));
        return result;
    }
}
