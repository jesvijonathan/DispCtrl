using DispCtrl.Engine.Protection;

namespace DispCtrl.Engine.Taskbar;

/// <summary>Applies opacity on the existing rescan cadence; restores Explorer's original state.</summary>
internal sealed class TaskbarAppearance : IDisposable
{
    private const long Layered = 0x80000;
    private sealed record Original(uint Process, bool WasLayered, uint Key, byte Alpha, uint Flags, byte Applied);
    private readonly Dictionary<nint, Original> _original = [];

    public void Update(int opacity)
    {
        byte alpha = (byte)Math.Round(Math.Clamp(opacity, 0, 100) * 2.55);
        if (opacity >= 100) { Dispose(); return; }

        foreach (string cls in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
        {
            nint window = 0;
            while ((window = OverlayNative.FindWindowEx(0, window, cls, null)) != 0)
            {
                OverlayNative.GetWindowThreadProcessId(window, out uint process);
                long style = OverlayNative.GetWindowLongPtr(window, -20);
                if (_original.TryGetValue(window, out Original? old) && old.Process != process)
                { _original.Remove(window); old = null; }
                if (old is null)
                {
                    bool layered = (style & Layered) != 0;
                    uint key = 0, flags = 2;
                    byte originalAlpha = 255;
                    // Leave pre-existing per-pixel composition alone if we cannot restore it.
                    if (layered && OverlayNative.GetLayeredWindowAttributes(window, out key, out originalAlpha, out flags) == 0) continue;
                    old = new(process, layered, key, originalAlpha, flags, 255);
                    _original[window] = old;
                }
                if (old.Applied == alpha && (style & Layered) != 0) continue;
                OverlayNative.SetWindowLongPtr(window, -20, (nint)(style | Layered));
                if (OverlayNative.SetLayeredWindowAttributes(window, old.Key, alpha, old.Flags | 2) != 0)
                    _original[window] = old with { Applied = alpha };
            }
        }
        foreach (nint stale in _original.Keys.Where(hwnd => OverlayNative.IsWindow(hwnd) == 0).ToArray())
            _original.Remove(stale);
    }

    public void Dispose()
    {
        foreach (var (window, old) in _original)
        {
            if (OverlayNative.IsWindow(window) == 0) continue;
            OverlayNative.GetWindowThreadProcessId(window, out uint process);
            if (process != old.Process) continue;
            if (old.WasLayered) OverlayNative.SetLayeredWindowAttributes(window, old.Key, old.Alpha, old.Flags);
            else
            {
                long style = OverlayNative.GetWindowLongPtr(window, -20);
                OverlayNative.SetLayeredWindowAttributes(window, 0, 255, 2);
                OverlayNative.SetWindowLongPtr(window, -20, (nint)(style & ~Layered));
            }
        }
        _original.Clear();
    }

}
