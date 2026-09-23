using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

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
    private sealed class Snapshot(JsonNode json) { public JsonNode Json = json; public bool ExternalChanges; }
    private static readonly ConditionalWeakTable<DispCtrlSettings, Snapshot> Snapshots = new();
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
        string? configured = Environment.GetEnvironmentVariable("DISPCTRL_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Path.IsPathFullyQualified(configured)) throw new ArgumentException("DISPCTRL_DATA_DIR must be an absolute path.");
            return Path.GetFullPath(configured);
        }
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
            if (!File.Exists(Path_)) return Track(new DispCtrlSettings());

            string json = ReadShared();
            DispCtrlSettings? s = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.DispCtrlSettings);
            if (s is not null) return Track(s);

            Quarantine();
            return Track(new DispCtrlSettings());
        }
        catch (JsonException)
        {
            Quarantine();
            return Track(new DispCtrlSettings());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable is not corrupt. This used to quarantine too, so a read
            // that lost a race with a save moved a good file aside and every
            // client fell back to defaults.
            return Track(new DispCtrlSettings());
        }
    }

    /// <summary>
    /// Reads the file without blocking a save that replaces it meanwhile.
    /// </summary>
    /// <remarks>
    /// <c>File.ReadAllText</c> opens without delete sharing, and a save is a
    /// rename over the file: while the engine, the CLI or the panel held it open
    /// to read, the rename failed with access denied, which took the app down
    /// in the middle of a slider drag. Sharing delete lets the rename go ahead;
    /// the reader keeps the version it opened. A sharing violation from a writer
    /// that does not share is retried briefly rather than reported.
    /// </remarks>
    private static string ReadShared()
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(Path_, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch (IOException) when (attempt < 10 && File.Exists(Path_))
            {
                Thread.Sleep(15);
            }
        }
    }

    /// <summary>Writes settings atomically.</summary>
    /// <remarks>
    /// Written to a temp file and moved into place, so an interrupted write
    /// cannot leave a half-written file that the next load would quarantine.
    /// </remarks>
    public static void Save(DispCtrlSettings settings)
    {
        WithWriteLock(() =>
        {
            System.IO.Directory.CreateDirectory(Directory);
            JsonNode local = JsonSerializer.SerializeToNode(settings, SettingsJsonContext.Default.DispCtrlSettings)!;
            JsonNode output = local;
            if (Snapshots.TryGetValue(settings, out Snapshot? baseline) && File.Exists(Path_))
            {
                JsonNode latest = JsonNode.Parse(ReadShared())!;
                output = MergeEdits(baseline.Json, local, latest)!;
            }
            string tmp = Path_ + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tmp, output.ToJsonString(SettingsJsonContext.Default.Options));
                ReplaceWithRetry(tmp);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
            Snapshots.Remove(settings);
            Snapshots.Add(settings, new(local.DeepClone()) { ExternalChanges = !JsonNode.DeepEquals(local, output) });
            return true;
        });
    }

    /// <summary>
    /// Renames the new file over the old one, waiting out a reader that has it
    /// open without delete sharing - an editor, an antivirus scan, an older build.
    /// </summary>
    private static void ReplaceWithRetry(string tmp)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { File.Move(tmp, Path_, overwrite: true); return; }
            catch (Exception ex) when (attempt < 20 && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(10 + attempt * 5);
            }
        }
    }

    /// <summary>A save merged newer disk values that this client has not adopted yet.</summary>
    public static bool HasExternalChanges(DispCtrlSettings settings) =>
        Snapshots.TryGetValue(settings, out var snapshot) && snapshot.ExternalChanges;

    private static DispCtrlSettings Track(DispCtrlSettings settings)
    {
        Snapshots.Add(settings, new(JsonSerializer.SerializeToNode(settings, SettingsJsonContext.Default.DispCtrlSettings)!));
        return settings;
    }

    /// <summary>Adopt a fresh snapshot without replacing monitor objects held by UI bindings.</summary>
    public static void RefreshInPlace(DispCtrlSettings target, DispCtrlSettings source)
    {
        Copy(source.Global, target.Global, SettingsJsonContext.Default.GlobalSettings);
        foreach (var pair in source.Monitors)
            Copy(pair.Value, target.For(pair.Key), SettingsJsonContext.Default.MonitorSettings);
        foreach (string key in target.Monitors.Keys.Except(source.Monitors.Keys).ToArray()) target.Monitors.Remove(key);
        target.Hotkeys = source.Hotkeys;
        target.AppRules = source.AppRules;
        target.Version = source.Version;
        Snapshots.Remove(target);
        Track(target);
    }

    private static void Copy<T>(T source, T target, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) where T : class
    {
        foreach (var property in info.Properties)
            if (property.Get is not null && property.Set is not null) property.Set(target, property.Get(source));
    }

    /// <summary>One read-modify-write transaction shared by the UI, engine and CLI.</summary>
    public static T WithWriteLock<T>(Func<T> operation)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path_.ToUpperInvariant())))[..24];
        using var gate = new Mutex(false, @"Local\DispCtrl.Settings." + key);
        bool held;
        try { held = gate.WaitOne(TimeSpan.FromSeconds(10)); }
        catch (AbandonedMutexException) { held = true; }
        if (!held) throw new TimeoutException("Settings are busy; no changes were saved.");
        try { return operation(); }
        finally { gate.ReleaseMutex(); }
    }

    /// <summary>Preserves fields changed by other clients since this object was loaded.</summary>
    public static JsonNode? MergeEdits(JsonNode? baseline, JsonNode? edited, JsonNode? latest)
    {
        if (JsonNode.DeepEquals(baseline, edited)) return latest?.DeepClone();
        if (baseline is JsonObject before && edited is JsonObject after && latest is JsonObject current)
        {
            JsonObject result = (JsonObject)current.DeepClone();
            foreach (var property in before)
                if (!after.ContainsKey(property.Key)) result.Remove(property.Key);
            foreach (var property in after)
            {
                before.TryGetPropertyValue(property.Key, out JsonNode? old);
                current.TryGetPropertyValue(property.Key, out JsonNode? live);
                if (!before.ContainsKey(property.Key) || !JsonNode.DeepEquals(old, property.Value))
                    result[property.Key] = MergeEdits(old, property.Value, live);
            }
            return result;
        }
        return edited?.DeepClone();
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
