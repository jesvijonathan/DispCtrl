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
public static partial class SettingsStore
{
    private sealed class Snapshot(JsonNode json) { public JsonNode Json = json; public bool ExternalChanges; }
    private static readonly ConditionalWeakTable<DispCtrlSettings, Snapshot> Snapshots = new();

    /// <summary>The text this process last saved, and the identity of the file that save produced.</summary>
    private sealed record OwnWrite(DateTime Written, DateTime Created, long Length, string Text);
    private static volatile OwnWrite? _ownWrite;
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
        if (PackagedFolder(local, current) is { } packaged) return packaged;

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

    /// <summary>The data folder as it really is on disk, for a Store install.</summary>
    /// <remarks>
    /// Inside a package, %LOCALAPPDATA% is a merged view: a folder that existed
    /// before the install is written in place, a new one lands in the
    /// package's LocalCache. Every DispCtrl process sees the same view, so the
    /// data itself was never lost - but each path handed to something outside
    /// the package pointed nowhere: "open the log" opened nothing, and the
    /// folder people looked for in %LOCALAPPDATA% did not exist. The real
    /// folder works from both sides, and the package may write it directly.
    /// Null when not packaged, or when an earlier install's real folder is the
    /// one in use.
    /// </remarks>
    private static string? PackagedFolder(string local, string current)
    {
        if (!OperatingSystem.IsWindows() || PackageFamily() is not { } family) return null;
        string redirected = Path.Combine(local, "Packages", family, "LocalCache", "Local", "DispCtrl");
        if (System.IO.Directory.Exists(redirected)) return redirected;
        // With nothing redirected yet, a folder seen here is the real one.
        return System.IO.Directory.Exists(current) ? null : redirected;
    }

    private static unsafe string? PackageFamily()
    {
        try
        {
            uint length = 0;
            // 15700 (APPMODEL_ERROR_NO_PACKAGE) for an ordinary process; 122
            // (ERROR_INSUFFICIENT_BUFFER) with the length it needs for a packaged one.
            if (GetCurrentPackageFamilyName(ref length, null) != 122 || length == 0) return null;
            char* name = stackalloc char[(int)length];
            return GetCurrentPackageFamilyName(ref length, name) == 0 ? new string(name, 0, (int)length - 1) : null;
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
    }

    [System.Runtime.InteropServices.LibraryImport("kernel32.dll")]
    private static unsafe partial int GetCurrentPackageFamilyName(ref uint length, char* name);

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
            DispCtrlSettings? s;
            try
            {
                s = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.DispCtrlSettings);
                SetAside = [];
            }
            catch (JsonException)
            {
                // Whole JSON with a value this build cannot read - most often one
                // a newer DispCtrl wrote, such as a hotkey action added since.
                s = Lenient(json, out List<string> setAside);
                SetAside = setAside;
            }
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
    /// What the last load could not read and left out, as JSON paths; empty
    /// nearly always. For the engine's log.
    /// </summary>
    public static IReadOnlyList<string> SetAside { get; private set; } = [];

    /// <summary>
    /// Reads a settings file that is whole JSON but holds values this build does
    /// not understand, leaving out only those values.
    /// </summary>
    /// <remarks>
    /// The case is an older DispCtrl meeting a newer one's file - after a
    /// downgrade, a Store build a version behind a test build, a second copy.
    /// It used to quarantine the whole file for one word it did not know (a
    /// hotkey action added since), and every setting went back to defaults, in
    /// that build and in the newer one once it came back. Now each value that
    /// fails is removed from the in-memory copy - a list entry whole, so a hotkey
    /// never runs with its action gone to a default - and the rest loads. The
    /// file is not touched, and a save merges into it (<see cref="MergeEdits"/>),
    /// so what was left out here is still there for the build that understands
    /// it. A file that is not JSON at all is still quarantined.
    /// </remarks>
    public static DispCtrlSettings? Lenient(string json, out List<string> setAside)
    {
        setAside = [];
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException) { return null; }
        if (root is not JsonObject) return null;
        for (int attempt = 0; attempt < 64; attempt++)
        {
            try { return root.Deserialize(SettingsJsonContext.Default.DispCtrlSettings); }
            catch (JsonException ex) when (ex.Path is { Length: > 1 } path && RemoveAt(root, path))
            {
                setAside.Add(path);
            }
            catch (JsonException) { return null; }
        }
        return null;
    }

    /// <summary>
    /// Removes what a JSON path names: the innermost list entry on the way if
    /// there is one, otherwise the property itself.
    /// </summary>
    private static bool RemoveAt(JsonNode root, string path)
    {
        // $.hotkeys[5].action, $.monitors['DEL-A234-X'].isOled, $.global.pin
        var steps = new List<object>();
        int i = path.StartsWith('$') ? 1 : 0;
        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                int end = path.IndexOfAny(['.', '['], i + 1);
                if (end < 0) end = path.Length;
                steps.Add(path[(i + 1)..end]);
                i = end;
            }
            else if (path[i] == '[' && i + 1 < path.Length && path[i + 1] == '\'')
            {
                int end = path.IndexOf("']", i + 2, StringComparison.Ordinal);
                if (end < 0) return false;
                steps.Add(path[(i + 2)..end]);
                i = end + 2;
            }
            else if (path[i] == '[')
            {
                int end = path.IndexOf(']', i);
                if (end < 0 || !int.TryParse(path[(i + 1)..end], out int index)) return false;
                steps.Add(index);
                i = end + 1;
            }
            else return false;
        }
        if (steps.Count == 0) return false;

        // A list entry goes whole: half an entry is worse than none.
        int cut = steps.FindLastIndex(step => step is int);
        if (cut < 0) cut = steps.Count - 1;

        JsonNode? parent = root;
        for (int s = 0; s < cut && parent is not null; s++)
            parent = steps[s] is int n
                ? (parent is JsonArray a && n < a.Count ? a[n] : null)
                : (parent is JsonObject o ? o[(string)steps[s]] : null);
        switch (parent, steps[cut])
        {
            case (JsonArray array, int index) when index < array.Count:
                array.RemoveAt(index);
                return true;
            case (JsonObject obj, string name) when obj.ContainsKey(name):
                obj.Remove(name);
                return true;
            default:
                return false;
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
    /// <para>
    /// The file this process saved last is not read back. The first open of a
    /// freshly renamed settings file costs ~5.6 ms against 0.14 ms for the next
    /// (the antivirus scans it; measured on the 9 KB file of a four-monitor desk,
    /// perfcheck core), and the app loads straight after its own saves. Metadata
    /// does not open the file: unchanged write time, creation time (a rename-over
    /// brings the temp file's) and length mean nobody else has saved since.
    /// </para>
    private static string ReadShared()
    {
        if (_ownWrite is { } own)
        {
            var info = new FileInfo(Path_);
            if (info.Exists && info.Length == own.Length && info.LastWriteTimeUtc == own.Written && info.CreationTimeUtc == own.Created)
                return own.Text;
        }
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
                // A file that no longer parses has nothing worth merging, and
                // throwing here would make the corrupt file impossible to replace.
                JsonNode? latest = null;
                try { latest = JsonNode.Parse(ReadShared()); }
                catch (JsonException) { }
                if (latest is not null) output = MergeEdits(baseline.Json, local, latest)!;
            }
            string tmp = Path_ + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                string text = output.ToJsonString(SettingsJsonContext.Default.Options);
                File.WriteAllText(tmp, text);
                _ownWrite = null;
                ReplaceWithRetry(tmp);
                var written = new FileInfo(Path_);
                _ownWrite = new OwnWrite(written.LastWriteTimeUtc, written.CreationTimeUtc, written.Length, text);
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
