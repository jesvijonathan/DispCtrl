using System.Diagnostics;
using System.Runtime.InteropServices;
using DisplCtrl.Core.Settings;

namespace DisplCtrl.Engine.Taskbar;

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

        Process? explorer = Process.GetProcessesByName("explorer").FirstOrDefault();
        if (explorer is null) { Log.Write("taskbar glass: Explorer is not running"); WriteStatus("Explorer is not running"); return; }
        uint pid = unchecked((uint)explorer.Id);
        if (_explorerPid != pid)
        {
            int hr = _attach!(pid);
            _explorerPid = pid;
            _lastConfig = 0;
            if (hr < 0)
            {
                Log.Write($"taskbar glass: Explorer attach failed (0x{hr:X8})");
                WriteStatus($"Explorer integration failed (0x{hr:X8})");
                return;
            }
            Log.Write($"taskbar glass: attached to Explorer {pid}");
            WriteStatus("Connected to Explorer; waiting for the taskbar surface");
        }

        int radius = Math.Clamp(settings.TaskbarGlassRadius, 0, 100);
        int tint = Math.Clamp(settings.TaskbarGlassTint, 0, 100);
        int config = unchecked((int)(0x01000000u | (uint)radius | ((uint)tint << 8)));
        if (config == _lastConfig) return;
        int result = InvokeUpdate(pid, unchecked((uint)config));
        if (result < 0)
        {
            Log.Write($"taskbar glass: Explorer update failed (0x{result:X8})");
            WriteStatus($"Explorer update failed (0x{result:X8})");
            return;
        }
        // XAML can be injected before Explorer has created the taskbar
        // rectangles. Keep retrying until the callback reports a real target.
        _lastConfig = result > 0 ? config : 0;
        if (result == 0) Log.Write("taskbar glass: waiting for Explorer taskbar XAML");
        else Log.Write($"taskbar glass: applied to {result} taskbar background(s), radius {radius}, tint {tint}");
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
