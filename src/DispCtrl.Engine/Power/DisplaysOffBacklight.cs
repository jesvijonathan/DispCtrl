using DispCtrl.Core.Displays;
using DispCtrl.Display;

namespace DispCtrl.Engine.Power;

/// <summary>
/// Turns the real backlight down while the displays are off, and back afterwards.
/// </summary>
/// <remarks>
/// The overlay alone leaves the panel lit behind the black, which on an LCD is
/// glow and on any panel is power. Offered only at 90% darkness and more, where
/// "off" is what was asked for. Every write blocks - a DDC/CI round trip is tens
/// to hundreds of milliseconds - so all of it runs off the overlay's thread, one
/// display at a time.
/// </remarks>
internal static class DisplaysOffBacklight
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Dictionary<string, (DisplayInfo Display, uint Value)> Saved = new(StringComparer.Ordinal);
    private static long _quietUntil;

    /// <summary>
    /// True while a backlight is down and for a moment after it comes back. The
    /// Windows brightness bridge ignores the panel's own events meanwhile, or the
    /// laptop going to its floor would pull every display and unison down with it.
    /// </summary>
    public static bool Busy
    {
        get
        {
            lock (Saved) if (Saved.Count > 0) return true;
            return Environment.TickCount64 < Volatile.Read(ref _quietUntil);
        }
    }

    public static void Lower(IEnumerable<DisplayInfo> displays)
    {
        DisplayInfo[] list = [.. displays];
        Volatile.Write(ref _quietUntil, Environment.TickCount64 + 2000);
        _ = Task.Run(async () =>
        {
            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                foreach (DisplayInfo d in list)
                {
                    BrightnessRange range = Brightness.Read(d);
                    if (!range.Supported) continue;
                    lock (Saved) { if (!Saved.TryAdd(d.Token, (d, range.Current))) continue; }
                    if (!Brightness.Write(d, range.Min)) lock (Saved) Saved.Remove(d.Token);
                }
                int down;
                lock (Saved) down = Saved.Count;
                if (down > 0) Log.Write($"displays off: backlight down on {down} display(s)");
            }
            catch (Exception ex) { Log.Write($"displays off: backlight could not go down: {ex.Message}"); }
            finally { Gate.Release(); }
        });
    }

    /// <summary>Puts one display's backlight back, or every one when <paramref name="token"/> is null.</summary>
    public static void Restore(string? token = null, bool wait = false)
    {
        Task work = Task.Run(async () =>
        {
            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                List<(DisplayInfo Display, uint Value)> back;
                lock (Saved)
                {
                    back = token is null ? [.. Saved.Values]
                        : Saved.TryGetValue(token, out var one) ? [one] : [];
                    if (token is null) Saved.Clear(); else Saved.Remove(token);
                }
                if (back.Count == 0) return;
                Volatile.Write(ref _quietUntil, Environment.TickCount64 + 2000);
                foreach (var (display, value) in back) _ = Brightness.Write(display, value);
                Volatile.Write(ref _quietUntil, Environment.TickCount64 + 2000);
            }
            catch (Exception ex) { Log.Write($"displays off: backlight could not come back: {ex.Message}"); }
            finally { Gate.Release(); }
        });
        // On the way out of the engine: a backlight left at its floor would
        // outlive the process that remembered what it was.
        if (wait) work.Wait(TimeSpan.FromSeconds(5));
    }
}
