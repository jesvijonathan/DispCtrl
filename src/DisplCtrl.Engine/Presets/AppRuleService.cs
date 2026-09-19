using System.Diagnostics;
using DisplCtrl.Core.Displays;
using DisplCtrl.Core.Presets;
using DisplCtrl.Core.Settings;
using DisplCtrl.Display.Presets;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace DisplCtrl.Engine.Presets;

/// <summary>Serial foreground transitions with scoped, in-memory restoration.</summary>
internal sealed class AppRuleService : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Timer _timer;
    private readonly Action<DisplCtrlSettings> _persist;
    private DisplCtrlSettings _settings;
    private string? _foreground;
    private long _foregroundSince = Stopwatch.GetTimestamp();
    private string? _activeIdentity;
    private Preset? _previous;
    private string? _revertTo;
    private int _ticking;
    private bool _disposed;
    private long _retryAfter;

    public AppRuleService(DisplCtrlSettings settings, Action<DisplCtrlSettings> persist)
    {
        _settings = settings;
        _persist = persist;
        _timer = new Timer(_ => Tick(), null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
    }

    public void Update(DisplCtrlSettings settings)
    {
        lock (_gate) _settings = settings;
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _ticking, 1) != 0) return;
        try
        {
            DisplCtrlSettings settings;
            lock (_gate)
            {
                if (_disposed) return;
                settings = _settings;
            }
            string? image = ForegroundImageName();
            if (image is null) return;
            if (!string.Equals(image, _foreground, StringComparison.OrdinalIgnoreCase))
            {
                _foreground = image;
                _foregroundSince = Stopwatch.GetTimestamp();
                _retryAfter = 0;
                return;
            }
            AppRule? match = settings.AppRules.FirstOrDefault(rule => rule.IsComplete && rule.Matches(image));
            double dwell = match?.DwellSeconds ?? 2;
            if (!double.IsFinite(dwell)) dwell = 2;
            if (Stopwatch.GetElapsedTime(_foregroundSince).TotalSeconds < Math.Clamp(dwell, 0.5, 60)) return;
            string? identity = match is null ? null : $"{match.Process.ToUpperInvariant()}|{match.Preset.ToUpperInvariant()}|{match.RestorePrevious}|{match.RevertTo}";
            if (identity == _activeIdentity) return;
            if (Stopwatch.GetTimestamp() < _retryAfter) return;

            // Release A before capturing B: B must not restore A's temporary settings.
            if (!Restore(settings)) return;
            if (match is null) return;
            Preset? target = PresetStore.Read(PresetStore.PathFor(match.Preset));
            if (target is null)
            {
                Log.Write($"app rule: preset '{match.Preset}' is missing or invalid");
                _retryAfter = Stopwatch.GetTimestamp() + 30 * Stopwatch.Frequency;
                return;
            }
            var displays = DisplayRegistry.Enumerate();
            Preset? previous = match.RestorePrevious
                ? PresetValidation.RetainScope(PresetService.Capture("Before app rule", displays, settings), target) : null;
            PresetResult result = PresetService.Apply(target, displays, settings);
            LogResult(target.Name, result);
            if (!result.Attempted)
            {
                _retryAfter = Stopwatch.GetTimestamp() + 5 * Stopwatch.Frequency;
                return;
            }
            // Even a partial apply must be reversible, and must not repeat every tick.
            _previous = previous;
            _revertTo = match.RestorePrevious ? null : match.RevertTo;
            _activeIdentity = identity;
            _persist(PresetSettings.Merge(target, settings, SettingsStore.Load()));
        }
        catch (Exception ex)
        {
            Log.Write($"app rule failed: {ex.Message}");
            _retryAfter = Stopwatch.GetTimestamp() + 30 * Stopwatch.Frequency;
        }
        finally { Volatile.Write(ref _ticking, 0); }
    }

    private bool Restore(DisplCtrlSettings settings)
    {
        if (_activeIdentity is null) return true;
        Preset? restore = _previous;
        if (restore is null && !string.IsNullOrWhiteSpace(_revertTo))
            restore = PresetStore.Read(PresetStore.PathFor(_revertTo));
        if (restore is not null)
        {
            PresetResult result = PresetService.Apply(restore, DisplayRegistry.Enumerate(), settings);
            LogResult(restore.Name, result);
            if (!result.Attempted)
            {
                _retryAfter = Stopwatch.GetTimestamp() + 5 * Stopwatch.Frequency;
                return false;
            }
            _persist(PresetSettings.Merge(restore, settings, SettingsStore.Load()));
        }
        _activeIdentity = null;
        _previous = null;
        _revertTo = null;
        return true;
    }

    private static void LogResult(string name, PresetResult result)
    {
        Log.Write($"preset '{name}': {(result.Ok ? "restored" : "not fully restored")}");
        foreach (string note in result.Notes) Log.Write($"  {note}");
    }

    /// <summary>The image name of whatever owns the foreground window.</summary>
    /// <remarks>
    /// Null when there is no foreground window, or when the owning process
    /// cannot be opened — which is normal and expected for anything running at
    /// a higher integrity level, and is not worth logging every second.
    /// </remarks>
    private static unsafe string? ForegroundImageName()
    {
        HWND hwnd = PInvoke.GetForegroundWindow();
        if (hwnd.IsNull) return null;

        uint pid = 0;
        _ = PInvoke.GetWindowThreadProcessId(hwnd, &pid);
        if (pid == 0) return null;

        try
        {
            using Process process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        // Wait for any in-flight hardware operation before shutdown completes.
        using var finished = new ManualResetEvent(false);
        if (_timer.Dispose(finished)) finished.WaitOne();
        DisplCtrlSettings settings;
        lock (_gate) settings = _settings;
        try { Restore(settings); }
        catch (Exception ex) { Log.Write($"app rule restoration on shutdown failed: {ex.Message}"); }
    }
}
