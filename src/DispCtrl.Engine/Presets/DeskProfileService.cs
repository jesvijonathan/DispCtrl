using DispCtrl.Core.Displays;
using DispCtrl.Core.Presets;
using DispCtrl.Core.Settings;
using DispCtrl.Display.Presets;

namespace DispCtrl.Engine.Presets;

/// <summary>
/// Applies a desk profile when its desk arrives: the preset marked
/// <see cref="Preset.ApplyWhenConnected"/> whose displays are exactly the ones
/// attached.
/// </summary>
/// <remarks>
/// Heard on the engine's settled display change, once the set of displays has
/// stopped moving, and at start when the desk differs from the last one seen
/// (<see cref="GlobalSettings.LastDesk"/>), so signing in at a different desk
/// counts and restarting the engine on the same one does not. Mode changes on
/// the same desk raise nothing: the set, not the layout, is the desk.
/// <para>
/// Applying a preset changes the display stack, which settles again. That
/// change is the same desk and is ignored; a topology the preset itself
/// switches to (one screen only) is a different set, and is only recorded for
/// <see cref="QuietAfterApply"/>, so two profiles cannot chase each other.
/// </para>
/// </remarks>
internal sealed class DeskProfileService : IDisposable
{
    private static readonly TimeSpan QuietAfterApply = TimeSpan.FromSeconds(20);

    // Settings are read fresh for each change: the desk and its presets are
    // looked at seldom, and the file is what the panel and CLI change.
    private readonly Lock _gate = new();
    private long _quietUntil;
    private int _working;
    private bool _disposed;

    public DeskProfileService(IReadOnlyList<DisplayInfo> attached)
    {
        Color.DisplayChanges.Settled += OnSettled;
        _ = Task.Run(() => Consider(attached, "start"));
    }

    private void OnSettled(Color.DisplayChange change)
    {
        if (change.Arrived.Count == 0 && change.Departed.Count == 0) return;
        _ = Task.Run(() => Consider(change.Displays, "displays changed"));
    }

    private void Consider(IReadOnlyList<DisplayInfo> displays, string why)
    {
        if (Interlocked.Exchange(ref _working, 1) != 0) return;
        try
        {
            lock (_gate) if (_disposed) return;
            var attached = displays.Select(d => d.Token).ToHashSet(StringComparer.Ordinal);
            string desk = DeskProfiles.Fingerprint(attached);

            DispCtrlSettings settings = SettingsStore.Load();
            if (settings.Global.LastDesk == desk) return;
            settings.Global.LastDesk = desk;
            SettingsStore.Save(settings);
            if (Environment.TickCount64 < Volatile.Read(ref _quietUntil)) return;

            Preset? due = DeskProfiles.Due(PresetStore.Load(), attached, settings);
            if (due is null) return;

            Log.Write($"desk profile: '{due.Name}' belongs to the desk now attached ({why}); applying it");
            PresetResult result = PresetService.Apply(due, DisplayRegistry.Enumerate(), settings);
            Volatile.Write(ref _quietUntil, Environment.TickCount64 + (long)QuietAfterApply.TotalMilliseconds);
            foreach (string note in result.Notes) Log.Write($"  {note}");
            if (!result.Attempted) return;
            SettingsStore.Save(PresetSettings.Merge(due, settings, SettingsStore.Load()));
            Log.Write($"desk profile: '{due.Name}' {(result.Ok ? "applied" : "not fully applied")}");
        }
        catch (Exception ex) { Log.Write($"desk profile failed: {ex.Message}"); }
        finally { Volatile.Write(ref _working, 0); }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        Color.DisplayChanges.Settled -= OnSettled;
    }
}
