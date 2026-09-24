using System.Diagnostics;
using System.Runtime.InteropServices;
using DispCtrl.Core.Settings;

namespace DispCtrl.Engine.Taskbar;

/// <summary>
/// Owns the small Explorer XAML bridge used for compositor-backed taskbar
/// blur. The bridge is injected only into Explorer and receives numeric
/// settings; no screen capture or managed code runs in Explorer.
/// </summary>
internal sealed class TaskbarGlassController : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int AttachDelegate(uint explorerPid);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int UpdateDelegate(uint explorerPid, uint ownerPid, uint config);

    private nint _module;
    private AttachDelegate? _attach;
    private UpdateDelegate? _update;
    private uint _explorerPid;
    private int _lastConfig;
    private bool _missingLogged;
    private string? _lastStatus;
    private long _retryAt;

    // Adaptive cadence for the native check. It is a cross-process message into
    // Explorer's taskbar thread, and a once-a-second one kept Explorer waking to
    // confirm a brush it already had. While nothing changes it steps back to
    // every few seconds; anything that can replace the brush - a new
    // configuration, a new result, the primary bar moving through Windows'
    // auto-hide - brings it straight back to every rescan.
    private const int QuietChecksBeforeBackoff = 5;
    private const long BackoffMs = 5000;
    private int _quietChecks;
    private int _lastResult = -1;
    private long _nextCheckAt;
    private Protection.OverlayNative.Rect _primaryRect;

    public void Update(GlobalSettings settings)
    {
        if (!settings.TaskbarGlassEnabled)
        {
            if (_update is not null && _lastConfig != 0)
            {
                _ = InvokeUpdate(_explorerPid, 0);
                _lastConfig = 0;
            }
            WriteStatus("off");
            return;
        }

        if (!TryLoad()) return;

        nint taskbar = Protection.OverlayNative.FindWindowEx(0, 0, "Shell_TrayWnd", null);
        if (taskbar == 0) { WriteStatus("Explorer taskbar is not running"); return; }
        long now = Environment.TickCount64;
        // Explorer swaps the bar's brush when its auto-hide state changes, and
        // the primary bar moving is how that shows from outside.
        bool moved = Protection.OverlayNative.GetWindowRect(taskbar, out Protection.OverlayNative.Rect rect) != 0
            && (rect.Left != _primaryRect.Left || rect.Top != _primaryRect.Top
                || rect.Right != _primaryRect.Right || rect.Bottom != _primaryRect.Bottom);
        if (moved) { _primaryRect = rect; _quietChecks = 0; }

        int radius = Math.Clamp(settings.TaskbarGlassRadius, 0, 100);
        int tint = Math.Clamp(settings.TaskbarGlassTint, 0, 100);
        int config = unchecked((int)(0x01000000u | (uint)radius | ((uint)tint << 8)));
        if (!moved && config == _lastConfig && now < _nextCheckAt) return;

        Protection.OverlayNative.GetWindowThreadProcessId(taskbar, out uint pid);
        if (_explorerPid != pid)
        {
            if (Environment.TickCount64 < _retryAt) return;
            int hr = _attach!(pid);
            _lastConfig = 0;
            if (hr < 0)
            {
                Log.Write($"taskbar glass: Explorer attach failed (0x{hr:X8})");
                _retryAt = Environment.TickCount64 + 10000;
                WriteStatus(hr == unchecked((int)0x8007051A)
                    ? "Explorer has an older glass helper loaded. Restart Windows Explorer to load this build."
                    : $"Explorer integration failed (0x{hr:X8})");
                return;
            }
            _explorerPid = pid;
            Log.Write($"taskbar glass: attached to Explorer {pid}");
            WriteStatus("Connected to Explorer; waiting for the taskbar surface");
        }

        // New taskbar XAML threads and Explorer's own visual-state changes
        // can arrive after a successful application, so it is re-checked on
        // the cadence above rather than applied once.
        int result = InvokeUpdate(pid, unchecked((uint)config));
        if (result < 0)
        {
            Log.Write($"taskbar glass: Explorer update failed (0x{result:X8})");
            WriteStatus($"Explorer update failed (0x{result:X8})");
            return;
        }
        // XAML can be injected before Explorer has created the taskbar
        // rectangles. Keep retrying until the callback reports a real target.
        if (config != _lastConfig && result > 0)
            Log.Write($"taskbar glass: applied to {result} taskbar background(s), radius {radius}, tint {tint}");
        _quietChecks = result > 0 && config == _lastConfig && result == _lastResult ? _quietChecks + 1 : 0;
        _lastResult = result;
        _lastConfig = result > 0 ? config : 0;
        _nextCheckAt = _quietChecks >= QuietChecksBeforeBackoff ? now + BackoffMs : 0;
        WriteStatus(result == 0
            ? "Connected to Explorer; waiting for the taskbar surface"
            : $"Applied to {result} taskbar surface(s) · blur {radius}px · tint {tint}%");
    }

    private int InvokeUpdate(uint explorerPid, uint config)
    {
        try { return _update!(explorerPid, unchecked((uint)Environment.ProcessId), config); }
        catch (Exception ex) { Log.Write($"taskbar glass: bridge call failed: {ex.Message}"); return -1; }
    }

    private bool TryLoad()
    {
        if (_update is not null) return true;
        string directory = Path.Combine(AppContext.BaseDirectory, "taskbar-glass");
        string versionFile = Path.Combine(directory, "TaskbarGlass.version");
        try
        {
            string name = File.ReadAllText(versionFile).Trim();
            string path = Path.Combine(directory, name);
            if (!File.Exists(path)) throw new FileNotFoundException("native helper is missing", path);
            path = ExplorerLoadable(path, name);
            _module = NativeLibrary.Load(path);
            _attach = Marshal.GetDelegateForFunctionPointer<AttachDelegate>(NativeLibrary.GetExport(_module, "GlassAttach"));
            _update = Marshal.GetDelegateForFunctionPointer<UpdateDelegate>(NativeLibrary.GetExport(_module, "GlassUpdate"));
            return true;
        }
        catch (Exception ex)
        {
            if (!_missingLogged)
            {
                Log.Write($"taskbar glass: native bridge unavailable: {ex.Message}");
                _missingLogged = true;
            }
            WriteStatus("Native Explorer bridge is unavailable");
            return false;
        }
    }

    /// <summary>A path to the helper that Explorer is allowed to load.</summary>
    /// <remarks>
    /// Explorer loads the helper itself, from the path the engine hands it. In
    /// a Store install that path is under WindowsApps, where a conditional ACE
    /// grants execute only to processes whose SYSAPPID is the package's own:
    /// Explorer may read the file but not map it as an image, so the glass did
    /// nothing at all there while the installer and the zips worked. A copy in
    /// the package's LocalCache - its real path, not the redirected
    /// %LOCALAPPDATA% view - has the user's own ACL. The name carries the
    /// revision, so an existing copy is the same bytes and is never rewritten:
    /// Explorer may have it mapped.
    /// </remarks>
    private static string ExplorerLoadable(string path, string name)
    {
        string cache;
        try { _ = Windows.ApplicationModel.Package.Current.Id; cache = Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path; }
        catch (InvalidOperationException) { return path; }
        string directory = Path.Combine(cache, "TaskbarGlass");
        string copy = Path.Combine(directory, name);
        if (!File.Exists(copy))
        {
            Directory.CreateDirectory(directory);
            File.Copy(path, copy + ".tmp", true);
            File.Move(copy + ".tmp", copy, true);
            Log.Write($"taskbar glass: helper copied out of the package to {copy}");
        }
        // Earlier revisions go when nothing holds them; one Explorer still has
        // mapped stays until it restarts, and is tried again next time.
        foreach (string old in Directory.EnumerateFiles(directory, "DispCtrl.TaskbarGlass.*.dll"))
            if (!string.Equals(old, copy, StringComparison.OrdinalIgnoreCase))
                try { File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return copy;
    }

    public void Dispose()
    {
        if (_update is not null && _explorerPid != 0)
            _ = InvokeUpdate(_explorerPid, 0);
        WriteStatus("off");
        _update = null;
        _attach = null;
        // The DLL is intentionally left loaded until engine exit. Explorer may
        // still hold the versioned mapping, and unloading a COM callback DLL
        // while XAML is unwinding is unsafe.
    }

    private void WriteStatus(string status)
    {
        if (string.Equals(_lastStatus, status, StringComparison.Ordinal)) return;
        try
        {
            Directory.CreateDirectory(SettingsStore.Directory);
            File.WriteAllText(Path.Combine(SettingsStore.Directory, "taskbar-glass.status"), status);
            _lastStatus = status;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
