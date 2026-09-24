namespace DispCtrl.Core.Devices;

/// <summary>
/// Where each file of the device library lives, in the repository and beside
/// the executables.
/// </summary>
/// <remarks>
/// <code>
/// devices/
///   common.json            every monitor (target *)
///   index.json             generated; never edited, never needed by the app
///   DEL/
///     brand.json           every Dell (target DEL)
///     A234/
///       definition.json    the model's mappings (target DEL-A234)
///       record.md          what the model reports about itself
/// </code>
/// A folder per manufacturer, then per model, because the library is meant to
/// hold thousands of models. Every path follows from the key alone, so the app
/// reads the two or three files that bear on the monitor in front of it and
/// never lists the rest; a manufacturer folder stays far under the thousand
/// entries GitHub will list; and two people sharing different models touch
/// different folders, so their pull requests cannot conflict.
/// </remarks>
public static class DeviceLayout
{
    public const string CommonFile = "common.json";
    public const string BrandFile = "brand.json";
    public const string DefinitionFile = "definition.json";
    public const string RecordFile = "record.md";

    /// <summary>
    /// The issues a model was shared in: numbers only, so how many owners have
    /// reported it can be counted without a name ever entering the repository.
    /// </summary>
    /// <remarks>Not shipped with the app, like records and the index.</remarks>
    public const string ReportsFile = "reports.json";

    /// <summary>The report list for a model.</summary>
    public static string ReportsPath(string root, string model)
    {
        if (!DeviceDefinitions.IsModel(model)) throw new ArgumentException($"'{model}' is not a model key such as DEL-A234.");
        return Path.Combine(root, model[..3], model[4..], ReportsFile);
    }
    public const string IndexFile = "index.json";

    /// <summary>The definition file for a target: <c>*</c>, a brand (<c>DEL</c>) or a model (<c>DEL-A234</c>).</summary>
    public static string DefinitionPath(string root, string target)
    {
        if (target == "*") return Path.Combine(root, CommonFile);
        if (!DeviceDefinitions.IsTarget(target)) throw new ArgumentException($"'{target}' is not a device key such as DEL or DEL-A234.");
        return DeviceDefinitions.IsModel(target)
            ? Path.Combine(root, target[..3], target[4..], DefinitionFile)
            : Path.Combine(root, target, BrandFile);
    }

    /// <summary>The record for a model.</summary>
    public static string RecordPath(string root, string model)
    {
        if (!DeviceDefinitions.IsModel(model)) throw new ArgumentException($"'{model}' is not a model key such as DEL-A234.");
        return Path.Combine(root, model[..3], model[4..], RecordFile);
    }

    /// <summary>The folder a model's files live in.</summary>
    public static string ModelFolder(string root, string model) => Path.GetDirectoryName(RecordPath(root, model))!;

    /// <summary>Every model folder under the root, as keys, whether it holds a record, a definition or both.</summary>
    public static IEnumerable<string> Models(string root)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (string brand in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            string b = Path.GetFileName(brand);
            if (!DeviceDefinitions.IsTarget(b) || b.Length != 3) continue;
            foreach (string product in Directory.EnumerateDirectories(brand).Order(StringComparer.Ordinal))
            {
                string key = b + "-" + Path.GetFileName(product);
                if (DeviceDefinitions.IsModel(key)) yield return key;
            }
        }
    }

    /// <summary>Every definition file under the root with the target its place says it holds.</summary>
    public static IEnumerable<(string Target, string Path)> Definitions(string root)
    {
        string common = Path.Combine(root, CommonFile);
        if (File.Exists(common)) yield return ("*", common);
        if (!Directory.Exists(root)) yield break;
        foreach (string brand in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            string b = Path.GetFileName(brand);
            if (b.Length != 3 || !DeviceDefinitions.IsTarget(b)) continue;
            string brandFile = Path.Combine(brand, BrandFile);
            if (File.Exists(brandFile)) yield return (b, brandFile);
        }
        foreach (string model in Models(root))
        {
            string file = DefinitionPath(root, model);
            if (File.Exists(file)) yield return (model, file);
        }
    }
}
