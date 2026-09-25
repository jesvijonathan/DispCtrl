using System.Diagnostics;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Settings;

namespace DispCtrl.Display;

/// <summary>What a marker left on disk says about the read it marked.</summary>
public enum MarkerVerdict
{
    /// <summary>Its process is still reading: leave it.</summary>
    Live,

    /// <summary>Its process ended without clearing it, in this boot: a kill, not a crash.</summary>
    Stale,

    /// <summary>It comes from an earlier boot: Windows went down in the middle of the read.</summary>
    Crashed,
}

/// <summary>
/// Stops DispCtrl reading a monitor that has taken Windows down.
/// </summary>
/// <remarks>
/// Microsoft documents that reading the DDC/CI capabilities string of a monitor
/// with a malformed one can hit a Windows kernel bug and crash the machine -
/// PowerToys' Power Display guards the same read. Nothing in this process
/// survives that, so the evidence has to be on disk before the read starts: a
/// marker, flushed through to the drive, removed when the read returns. A
/// marker still there at the next start, from an earlier boot, is a read that
/// never came back. That monitor is then blocked (<see cref="DdcGuardSettings"/>)
/// and every DDC/CI conversation with it is refused until somebody allows it
/// again from the Displays page or <c>dispctrl ddc allow</c>.
/// <para>
/// Marked: every capabilities read, and each process's first conversation with
/// each monitor - Dxva2's high-level calls read the string themselves on a
/// handle's first use. Not every brightness write: each mark is a flushed file,
/// and a slider drag would pay it per step for a crash no report ties to them.
/// </para>
/// </remarks>
public static class DdcGuard
{
    private static readonly string Folder = Path.Combine(SettingsStore.Directory, "ddc-guard");

    /// <summary>When this boot began, from the tick count, which includes sleep and hibernation.</summary>
    private static DateTimeOffset BootUtc => DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);

    private static readonly long ProcessStartTicks = StartOf(Environment.ProcessId) ?? 0;
    private static int _recovered;

    /// <summary>
    /// Judges one marker. Pure, so the rule is checked in presetverify.
    /// </summary>
    /// <param name="markerBoot">When the boot that wrote it began.</param>
    /// <param name="currentBoot">When this boot began.</param>
    /// <param name="writerAlive">Whether the process that wrote it is still running.</param>
    /// <remarks>
    /// Boot times computed from the tick count wander by a second or two, and a
    /// clock corrected by time sync moves both; two minutes apart is a different
    /// boot beyond doubt, and nobody reboots faster than that and hits the read.
    /// </remarks>
    public static MarkerVerdict Judge(DateTimeOffset markerBoot, DateTimeOffset currentBoot, bool writerAlive)
    {
        if ((currentBoot - markerBoot).Duration() > TimeSpan.FromMinutes(2)) return MarkerVerdict.Crashed;
        return writerAlive ? MarkerVerdict.Live : MarkerVerdict.Stale;
    }

    // ----------------------------------------------------------- blocking --

    private static readonly Lock CacheGate = new();
    private static (DateTime Written, long Length) _stamp;
    private static long _checkedAt = long.MinValue;
    private static HashSet<string> _blocked = new(StringComparer.OrdinalIgnoreCase);
    private static bool _enabled = true;

    /// <summary>Whether DDC/CI to this monitor is refused.</summary>
    /// <remarks>
    /// On every conversation, so it reads the settings file only when its
    /// write time or length has moved, and looks at those at most once a second.
    /// </remarks>
    public static bool IsBlocked(DisplayInfo display)
    {
        lock (CacheGate)
        {
            long now = Environment.TickCount64;
            // Compared with the sentinel first: now - long.MinValue overflows to
            // a negative number, which read as "checked a moment ago" forever,
            // and the block list was never loaded at all.
            if (_checkedAt == long.MinValue || now - _checkedAt >= 1000)
            {
                _checkedAt = now;
                try
                {
                    var file = new FileInfo(SettingsStore.Path_);
                    var stamp = file.Exists ? (file.LastWriteTimeUtc, file.Length) : default;
                    if (stamp != _stamp)
                    {
                        _stamp = stamp;
                        DdcGuardSettings guard = SettingsStore.Load().Global.DdcGuard;
                        _enabled = guard.Enabled;
                        _blocked = guard.Blocked.Select(b => b.Token).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch (Exception) { /* keep the last answer */ }
            }
            return _enabled && _blocked.Contains(display.Token);
        }
    }

    /// <summary>Forgets the cached block list, so a change made in this process applies at once.</summary>
    public static void Invalidate()
    {
        lock (CacheGate) { _checkedAt = long.MinValue; _stamp = default; }
    }

    /// <summary>Lets DispCtrl talk to a blocked monitor again.</summary>
    /// <returns>False when nothing by that token or model was blocked.</returns>
    public static bool Allow(string tokenOrModel)
    {
        bool removed = SettingsStore.WithWriteLock(() =>
        {
            DispCtrlSettings settings = SettingsStore.Load();
            int count = settings.Global.DdcGuard.Blocked.RemoveAll(b =>
                string.Equals(b.Token, tokenOrModel, StringComparison.OrdinalIgnoreCase)
                || string.Equals(b.Model, tokenOrModel, StringComparison.OrdinalIgnoreCase));
            if (count > 0) SaveUnlocked(settings);
            return count > 0;
        });
        Invalidate();
        return removed;
    }

    // --------------------------------------------------------------- marks --

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> Vetted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether this process has already had a conversation with this monitor.</summary>
    internal static bool FirstConversation(DisplayInfo display) => Vetted.TryAdd(display.Key.DevicePath, 0);

    /// <summary>Marks a read on disk until the returned handle is disposed; null when guarding is off.</summary>
    internal static IDisposable? Enter(DisplayInfo display)
    {
        Recover();
        if (!GuardEnabled()) return null;
        try
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, $"{Environment.ProcessId}-{Hash(display.Key.DevicePath)}.mark");
            string text = string.Join('\n',
                display.Token, display.Key.Model, display.Label.Replace('\n', ' '),
                Environment.ProcessId, ProcessStartTicks, BootUtc.UtcTicks, DateTimeOffset.UtcNow.UtcTicks);
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(text);
                writer.Flush();
                // Through to the drive, not the cache: the whole point is that
                // the mark outlives Windows going down a moment later.
                stream.Flush(flushToDisk: true);
            }
            return new Mark(path);
        }
        catch (Exception)
        {
            // A read with no mark is what every DispCtrl before this did; a
            // guard that failed must not stop the monitor being read.
            return null;
        }
    }

    private sealed class Mark(string path) : IDisposable
    {
        public void Dispose()
        {
            try { File.Delete(path); } catch (Exception) { }
        }
    }

    private static bool GuardEnabled()
    {
        lock (CacheGate)
        {
            if (_checkedAt == long.MinValue)
            {
                try { _enabled = SettingsStore.Load().Global.DdcGuard.Enabled; } catch (Exception) { }
            }
            return _enabled;
        }
    }

    /// <summary>
    /// Looks for marks left behind, once per process: blocks the monitor of any
    /// that outlived a boot, and removes those a killed process left.
    /// </summary>
    /// <returns>The monitors blocked by this call; usually none.</returns>
    public static IReadOnlyList<DdcBlock> Recover()
    {
        if (Interlocked.Exchange(ref _recovered, 1) != 0) return [];
        var blocked = new List<DdcBlock>();
        try
        {
            if (!Directory.Exists(Folder)) return blocked;
            DateTimeOffset boot = BootUtc;
            foreach (string path in Directory.GetFiles(Folder, "*.mark"))
            {
                string[] lines;
                try { lines = File.ReadAllText(path).Split('\n'); }
                catch (Exception) { continue; }
                if (lines.Length < 7 || !int.TryParse(lines[3], out int pid) || !long.TryParse(lines[4], out long started)
                    || !long.TryParse(lines[5], out long bootTicks) || !long.TryParse(lines[6], out long readTicks))
                {
                    TryDelete(path);
                    continue;
                }
                bool alive = pid == Environment.ProcessId || StartOf(pid) == started;
                switch (Judge(new DateTimeOffset(bootTicks, TimeSpan.Zero), boot, alive))
                {
                    case MarkerVerdict.Live: break;
                    case MarkerVerdict.Stale: TryDelete(path); break;
                    case MarkerVerdict.Crashed:
                        blocked.Add(new DdcBlock
                        {
                            Token = lines[0], Model = lines[1], Label = lines[2],
                            SinceUtc = DateTimeOffset.UtcNow,
                            Reason = $"Windows stopped while DispCtrl was reading this monitor's capabilities on "
                                + $"{new DateTimeOffset(readTicks, TimeSpan.Zero).ToLocalTime():d MMM yyyy, HH:mm}. A monitor with a malformed "
                                + "capabilities string can trigger a Windows bug that crashes the computer, so DispCtrl no longer talks to it over DDC/CI.",
                        });
                        TryDelete(path);
                        break;
                }
            }
            if (blocked.Count > 0)
            {
                SettingsStore.WithWriteLock(() =>
                {
                    DispCtrlSettings settings = SettingsStore.Load();
                    foreach (DdcBlock block in blocked)
                        if (!settings.Global.DdcGuard.Blocked.Any(b => string.Equals(b.Token, block.Token, StringComparison.OrdinalIgnoreCase)))
                            settings.Global.DdcGuard.Blocked.Add(block);
                    SaveUnlocked(settings);
                    return true;
                });
                Invalidate();
            }
        }
        catch (Exception) { }
        return blocked;
    }

    /// <summary>Saves inside a write lock already held: <see cref="SettingsStore.Save"/> takes it itself, and it is reentrant.</summary>
    private static void SaveUnlocked(DispCtrlSettings settings) => SettingsStore.Save(settings);

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception) { }
    }

    /// <summary>A process's start time in ticks, or null when it is not running.</summary>
    /// <remarks>With the id, so a later process that reused the id is not taken for the writer.</remarks>
    private static long? StartOf(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.StartTime.ToUniversalTime().Ticks;
        }
        catch (Exception) { return null; }
    }

    private static string Hash(string text)
    {
        ulong hash = 14695981039346656037;
        foreach (char c in text.ToUpperInvariant()) { hash ^= c; hash *= 1099511628211; }
        return hash.ToString("x16");
    }
}
