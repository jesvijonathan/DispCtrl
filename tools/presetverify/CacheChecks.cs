using DispCtrl.Core.Caching;
using DispCtrl.Core.Presets;

internal static class CacheChecks
{
    public static void Run(Action<bool, string> check)
    {
        var cache = new BoundedCache<string, int>(2);
        int reads = 0;
        int Read() => Interlocked.Increment(ref reads);
        Parallel.For(0, 32, _ => cache.Get("shared", TimeSpan.FromMinutes(1), Read));
        check(reads == 1, "concurrent cache misses share one read");
        cache.Remove("shared");
        check(cache.Get("shared", TimeSpan.FromMinutes(1), Read) == 2, "explicit invalidation fetches fresh data");
        cache.Get("second", TimeSpan.FromMinutes(1), Read);
        cache.Get("shared", TimeSpan.FromMinutes(1), Read);
        cache.Get("third", TimeSpan.FromMinutes(1), Read);
        int before = reads;
        cache.Get("shared", TimeSpan.FromMinutes(1), Read);
        check(reads == before, "LRU keeps recently used entries");
        cache.Get("second", TimeSpan.FromMinutes(1), Read);
        check(reads == before + 1, "LRU evicts the oldest entry at capacity");
        before = reads;
        cache.Get("second", TimeSpan.Zero, Read);
        check(reads == before + 1, "expired entries are re-read");
        try { cache.Get("failure", TimeSpan.FromMinutes(1), () => throw new IOException("Transient")); }
        catch (IOException) { }
        check(cache.Get("failure", TimeSpan.FromMinutes(1), () => 42) == 42, "failed reads are not cached");

        using var started = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        var pending = Task.Run(() => cache.Get("inflight", TimeSpan.FromMinutes(1), () =>
        {
            started.Set();
            if (!finish.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return 1;
        }));
        if (!started.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
        cache.Remove("inflight");
        int newer = cache.Get("inflight", TimeSpan.FromMinutes(1), () => 2);
        finish.Set();
        pending.GetAwaiter().GetResult();
        check(newer == 2 && cache.Get("inflight", TimeSpan.FromMinutes(1), () => 3) == 2,
            "an invalidated in-flight read cannot replace newer data");

        string path = Path.Combine(Path.GetTempPath(), $"DispCtrl-cache-check-{Guid.NewGuid():N}.json");
        try
        {
            var preset = new Preset
            {
                Global = new() { Taskbar = new() }, CaptureNotes = ["original"],
                Monitors = new() { ["panel"] = new() { Brightness = 30, MonitorControls = new() { ["0x12"] = 70 } } },
            };
            File.WriteAllText(path, PresetStore.ToJson(preset));
            var first = PresetStore.Read(path)!;
            first.Monitors["panel"].Brightness = 90;
            first.Monitors["panel"].MonitorControls["0x12"] = 10;
            first.Global.Taskbar!.HideDelayMs = 999;
            first.CaptureNotes.Clear();
            var second = PresetStore.Read(path)!;
            check(second.Monitors["panel"].Brightness == 30 && second.Monitors["panel"].MonitorControls["0x12"] == 70
                && second.Global.Taskbar!.HideDelayMs == 350 && second.CaptureNotes.Count == 1,
                "preset cache isolates all mutable nested state");
            preset.Description = "An external editor changed this file.";
            File.WriteAllText(path, PresetStore.ToJson(preset));
            check(PresetStore.Read(path)!.Description == preset.Description, "external file edits invalidate preset reads");
            File.Delete(path);
            check(PresetStore.Read(path) is null, "deleted files cannot return cached presets");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
