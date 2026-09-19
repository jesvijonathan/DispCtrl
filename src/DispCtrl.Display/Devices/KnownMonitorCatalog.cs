namespace DispCtrl.Display.Devices;

/// <summary>Monitor models already represented by a reviewed file in devices/.</summary>
public static class KnownMonitorCatalog
{
    private static readonly Lazy<HashSet<string>> Models = new(Load);

    public static bool Contains(string model) => Models.Value.Contains(model);

    private static HashSet<string> Load()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string folder = Path.Combine(AppContext.BaseDirectory, "devices");
            foreach (string path in Directory.EnumerateFiles(folder, "*.md"))
                result.Add(Path.GetFileNameWithoutExtension(path));
        }
        catch (Exception) when (!System.Diagnostics.Debugger.IsAttached)
        {
            // A missing catalog makes every monitor eligible for review, which
            // loses no data and is safer than suppressing a new model.
        }
        return result;
    }
}
