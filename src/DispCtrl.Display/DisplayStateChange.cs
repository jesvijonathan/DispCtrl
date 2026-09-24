using DispCtrl.Display.Presets;

namespace DispCtrl.Display;

/// <summary>Invalidate both before and after writes, including failed or partial writes.</summary>
internal sealed class DisplayStateChange : IDisposable
{
    public DisplayStateChange() => PresetService.InvalidateHardware();
    public void Dispose() => PresetService.NotifyHardwareChanged();
}
