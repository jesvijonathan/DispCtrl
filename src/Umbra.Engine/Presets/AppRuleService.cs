using System.Diagnostics;
using Umbra.Core.Displays;
using Umbra.Core.Presets;
using Umbra.Core.Settings;
using Umbra.Display.Presets;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Umbra.Engine.Presets;

/// <summary>
/// Switches presets as apps come to the foreground.
/// </summary>
/// <remarks>
/// Polled rather than hooked. A foreground-change hook means either a global
/// WinEvent hook — which puts this process in the message path of every
/// activation on the machine — or a hook DLL. Reading the foreground window
/// once a second costs two syscalls and cannot slow anything else down.
/// <para>
/// The engine, not the panel, because the point is that it happens while you
/// are using the app rather than while you are looking at settings.
/// </para>
/// </remarks>
internal sealed class AppRuleService : IDisposable
{
    /// <summary>
    /// How often the foreground app is checked.
    /// </summary>
    /// <remarks>
    /// A second is under the threshold where a switch feels like a delayed
    /// reaction, and is nowhere near often enough to matter: the poll is
    /// GetForegroundWindow plus a process-id lookup.
    /// </remarks>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long an app must hold the foreground before its preset is applied.
    /// </summary>
    /// <remarks>
    /// Applying a preset can mean a mode change, which blanks the screen for a
    /// moment. Doing that while somebody alt-tabs through four windows would be
    /// unusable, so a rule only fires once its app has actually been settled on.
    /// </remarks>
    private static readonly TimeSpan Dwell = TimeSpan.FromSeconds(2);

    private readonly Lock _gate = new();
    private readonly Timer _timer;

    private UmbraSettings _settings;

    /// <summary>
    /// Saves the settings object it is handed, never one captured earlier.
    /// </summary>
    /// <remarks>
    /// Takes an argument for a reason. A closure over the settings instance the
    /// engine started with goes stale the moment the watcher reloads the file,
    /// and persisting that stale object wrote the engine's old view back over
    /// whatever the user had just changed — silently undoing edits made from
    /// the panel. Saving the same object the apply ran against is the only
    /// version that is correct.
    /// </remarks>
    private readonly Action<UmbraSettings> _persist;

    private string? _foreground;
    private DateTime _foregroundSince = DateTime.UtcNow;

    /// <summary>
    /// The rule currently in force, held by value rather than by reference.
    /// </summary>
    /// <remarks>
    /// Identity is the trap here. Applying a preset writes settings, the
    /// watcher reloads the file, and the reload builds a whole new set of
    /// AppRule objects — so a reference comparison never matched, the rule was
    /// re-applied every tick, and each apply triggered the next reload. It ran
    /// as a loop that pegged the brightness and never reverted.
    /// </remarks>
    private string? _activeProcess;
    private string? _activePreset;
    private string? _activeRevertTo;

    private bool _disposed;

    public AppRuleService(UmbraSettings settings, Action<UmbraSettings> persist)
    {
        _settings = settings;
        _persist = persist;

        LogRuleCount(settings);
        _timer = new Timer(_ => Tick(), null, Interval, Interval);
    }

    public void Update(UmbraSettings settings)
    {
        int before;
        lock (_gate)
        {
            before = _settings.AppRules.Count;
            _settings = settings;
        }

        if (settings.AppRules.Count != before) LogRuleCount(settings);
    }

    /// <remarks>
    /// Worth a line: without it, a rule that never fires is indistinguishable
    /// from rules never having been loaded, and those need very different fixes.
    /// </remarks>
    private static void LogRuleCount(UmbraSettings settings)
    {
        int live = 0;
        foreach (AppRule r in settings.AppRules)
            if (r.Enabled && r.IsComplete) live++;

        Log.Write($"watching {live} app rule(s) of {settings.AppRules.Count}");
    }

    private void Tick()
    {
        try
        {
            UmbraSettings settings;
            lock (_gate)
            {
                if (_disposed) return;
                settings = _settings;
            }

            if (settings.AppRules.Count == 0) return;

            string? image = ForegroundImageName();
            if (image is null) return;

            if (!string.Equals(image, _foreground, StringComparison.OrdinalIgnoreCase))
            {
                _foreground = image;
                _foregroundSince = DateTime.UtcNow;
                return;
            }

            if (DateTime.UtcNow - _foregroundSince < Dwell) return;

            AppRule? match = null;
            foreach (AppRule rule in settings.AppRules)
            {
                if (!rule.IsComplete || !rule.Matches(image)) continue;
                match = rule;
                break;
            }

            if (match is null)
            {
                if (_activeProcess is null) return;

                string left = _activeProcess;
                string? revertTo = _activeRevertTo;

                // Cleared before applying, so a revert that itself triggers a
                // settings reload cannot be mistaken for a second departure.
                _activeProcess = null;
                _activePreset = null;
                _activeRevertTo = null;

                if (!string.IsNullOrWhiteSpace(revertTo))
                    ApplyNamed(revertTo, settings, $"{left} lost focus");

                return;
            }

            if (string.Equals(match.Process, _activeProcess, StringComparison.OrdinalIgnoreCase)
                && string.Equals(match.Preset, _activePreset, StringComparison.Ordinal))
            {
                return;
            }

            _activeProcess = match.Process;
            _activePreset = match.Preset;
            _activeRevertTo = match.RevertTo;

            ApplyNamed(match.Preset, settings, $"{match.Process} came to the front");
        }
        catch (Exception ex)
        {
            // A rule that throws must not take the engine down. Taskbar work
            // matters more than a preset switch does.
            Log.Write($"app rule tick failed: {ex.Message}");
        }
    }

    private void ApplyNamed(string name, UmbraSettings settings, string why)
    {
        Preset? preset = PresetStore.Read(PresetStore.PathFor(name));
        if (preset is null)
        {
            Log.Write($"app rule wanted preset '{name}', which is not on disk");
            return;
        }

        List<DisplayInfo> displays = DisplayRegistry.Enumerate();
        PresetResult result = PresetService.Apply(preset, displays, settings);

        // Applying writes into settings as well as to the hardware, so it has
        // to be persisted or the next reload would undo half of it.
        _persist(settings);

        Log.Write($"applied preset '{name}' — {why}");
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
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _timer.Dispose();
    }
}
