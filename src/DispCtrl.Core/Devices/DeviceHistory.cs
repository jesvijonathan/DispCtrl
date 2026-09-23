using System.Text.Json;
using DispCtrl.Core.Settings;

namespace DispCtrl.Core.Devices;

/// <summary>
/// Every monitor model this machine has seen, and every code each one listed.
/// </summary>
/// <remarks>
/// Local only; nothing reads it but DispCtrl and the person who owns it. It is
/// what makes a mapping possible after the fact - a monitor unplugged last
/// month still has its codes and the values they were seen to take - and what
/// a shared record is built from. Keyed on the model, never the unit: it holds
/// no serial, no device path, and no current setting beyond the values a code
/// has been seen to take, which describe the model's range rather than a desk.
/// </remarks>
public sealed class DeviceHistory
{
    public int Schema { get; set; } = 1;

    public Dictionary<string, SeenModel> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Models the person removed from the list; not recorded again until they sync them.</summary>
    /// <remarks>
    /// Everything is learned without a click now, so removing a monitor that is
    /// still attached would otherwise last only until the next capabilities
    /// read. Removal wins over automatic learning; an explicit sync wins over
    /// removal.
    /// </remarks>
    public List<string> Forgotten { get; set; } = [];

    private bool IsForgotten(string model) => Forgotten.Contains(model, StringComparer.OrdinalIgnoreCase);

    public static string PathOnDisk => Path.Combine(SettingsStore.Directory, "devices", "history.json");

    /// <summary>For checks that must not touch the real file.</summary>
    public static string? PathOverride { get; set; }

    private static string FilePath => PathOverride ?? PathOnDisk;

    private static readonly Lock Gate = new();

    public static DeviceHistory Load() => Read(strict: false);

    private static DeviceHistory Read(bool strict)
    {
        try
        {
            if (File.Exists(FilePath))
            {
                using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return JsonSerializer.Deserialize(stream, DeviceJsonContext.Default.DeviceHistory) ?? new();
            }
        }
        catch (Exception ex) when (!strict && (ex is JsonException or IOException or UnauthorizedAccessException)) { }
        return new DeviceHistory();
    }

    /// <summary>Changes the history and writes it only when something changed.</summary>
    /// <remarks>
    /// A named mutex, because the engine, the app and the CLI all record into
    /// it. Reads of an unchanged monitor write nothing, so the file is not
    /// rewritten on every capabilities read.
    /// </remarks>
    public static void Update(Func<DeviceHistory, bool> change)
    {
        lock (Gate)
        {
            using var mutex = new Mutex(false, @"Local\DispCtrl.DeviceHistory");
            bool held;
            try { held = mutex.WaitOne(3000); }
            catch (AbandonedMutexException) { held = true; }
            if (!held) return;
            try
            {
                // A sharing violation must not turn an existing library into
                // an empty one that this update would then overwrite.
                DeviceHistory history = Read(strict: true);
                if (!change(history)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(history, DeviceJsonContext.Default.DeviceHistory));
                File.Move(temp, FilePath, overwrite: true);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
            finally { mutex.ReleaseMutex(); }
        }
    }

    /// <summary>Records that a model is attached now.</summary>
    public static void Seen(string model, string name, string connector, bool builtIn, int widthMm, int heightMm)
    {
        if (!DeviceDefinitions.IsModel(model)) return;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Update(h =>
        {
            if (h.IsForgotten(model)) return false;
            bool fresh = !h.Models.TryGetValue(model, out SeenModel? m);
            m ??= h.Models[model] = new SeenModel { Key = model, FirstSeen = now };
            // A sighting a day, not a write per rescan.
            bool changed = fresh || m.Name != name || m.Connector != connector || m.BuiltIn != builtIn
                || m.WidthMm != widthMm || m.HeightMm != heightMm || (now - m.LastSeen).TotalHours >= 24;
            if (!changed) return false;
            m.Name = name; m.Connector = connector; m.BuiltIn = builtIn;
            m.WidthMm = widthMm; m.HeightMm = heightMm;
            m.LastSeen = now; m.Sightings++;
            return true;
        });
    }

    /// <summary>Records the codes a model listed and the values they were read at.</summary>
    public static void Listed(string model, string capabilities, IEnumerable<SeenReading> readings)
    {
        if (!DeviceDefinitions.IsModel(model)) return;
        Update(h =>
        {
            if (h.IsForgotten(model)) return false;
            if (!h.Models.TryGetValue(model, out SeenModel? m))
                m = h.Models[model] = new SeenModel { Key = model, FirstSeen = DateTimeOffset.UtcNow, LastSeen = DateTimeOffset.UtcNow, Sightings = 1 };
            bool changed = m.Capabilities != capabilities;
            m.Capabilities = capabilities;
            foreach (SeenReading r in readings)
            {
                string code = DeviceDefinitions.FormatCode(r.Code);
                if (!m.Codes.TryGetValue(code, out SeenCode? c)) { c = m.Codes[code] = new SeenCode(); changed = true; }
                if (c.Name != r.Name || c.Kind != r.Kind) { c.Name = r.Name; c.Kind = r.Kind; changed = true; }
                if (!c.ListedValues.SequenceEqual(r.ListedValues)) { c.ListedValues = [.. r.ListedValues]; changed = true; }
                if (r.Current is int value && !c.Observed.Contains(value))
                {
                    c.Observed.Add(value);
                    c.Observed.Sort();
                    // A handful is a range's shape; hundreds would be a log.
                    if (c.Observed.Count > 32) c.Observed.RemoveAt(c.Observed.Count / 2);
                    changed = true;
                }
            }
            return changed;
        });
    }
}

public static class DeviceHistoryEdits
{
    /// <summary>Removes a model from the list and stops it being recorded again by itself.</summary>
    /// <returns>False when there was nothing to remove.</returns>
    public static bool Forget(string model)
    {
        bool removed = false;
        DeviceHistory.Update(h =>
        {
            removed = h.Models.Remove(model);
            if (!h.Forgotten.Contains(model, StringComparer.OrdinalIgnoreCase)) { h.Forgotten.Add(model.ToUpperInvariant()); return true; }
            return removed;
        });
        return removed;
    }

    /// <summary>Lets a forgotten model be recorded again: what an explicit sync does.</summary>
    public static void Remember(IEnumerable<string> models)
    {
        var list = models.ToList();
        DeviceHistory.Update(h => h.Forgotten.RemoveAll(f => list.Contains(f, StringComparer.OrdinalIgnoreCase)) > 0);
    }

    /// <summary>Attached models whose codes have never been read here, and are not forgotten.</summary>
    public static bool NeedsReading(string model)
    {
        DeviceHistory h = DeviceHistory.Load();
        if (h.Forgotten.Contains(model, StringComparer.OrdinalIgnoreCase)) return false;
        return !h.Models.TryGetValue(model, out SeenModel? m) || (!m.BuiltIn && m.Capabilities is null);
    }
}

public sealed class SeenModel
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Connector { get; set; } = "";
    public bool BuiltIn { get; set; }
    public int WidthMm { get; set; }
    public int HeightMm { get; set; }
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public int Sightings { get; set; }

    /// <summary>The MCCS capabilities string, verbatim.</summary>
    public string? Capabilities { get; set; }

    /// <summary>The scrubbed public model record collected in the background, never a private diagnostic report.</summary>
    public string? Record { get; set; }

    public Dictionary<string, SeenCode> Codes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SeenCode
{
    /// <summary>The standard's name, or how the capabilities parser describes an unknown code.</summary>
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";

    /// <summary>The values the monitor listed for it.</summary>
    public List<int> ListedValues { get; set; } = [];

    /// <summary>The distinct values it has been read at.</summary>
    public List<int> Observed { get; set; } = [];
}

/// <summary>One code as read, for <see cref="DeviceHistory.Listed"/>.</summary>
public sealed record SeenReading(byte Code, string Name, string Kind, IReadOnlyList<int> ListedValues, int? Current);
