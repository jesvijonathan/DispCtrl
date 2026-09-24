using DispCtrl.Core.Devices;
using DispCtrl.Core.Displays;

namespace DispCtrl.Display.Devices;

/// <summary>
/// Feeds the local device history from what the rest of DispCtrl already reads.
/// </summary>
/// <remarks>
/// No reads of its own: a capabilities sweep is seconds of DDC/CI, and the app,
/// the CLI and the engine already make them. Recorded on the calling thread -
/// a background task was lost whenever a short-lived CLI process exited first -
/// and never allowed to fail a read: the history is a by-product. It only
/// writes when something is new, so the cost is a file read.
/// </remarks>
public static class DeviceObserver
{
    /// <summary>Records which models are attached, from what enumeration already knows.</summary>
    public static void Attached(IEnumerable<DisplayInfo> displays)
    {
        foreach (DisplayInfo d in displays)
        {
            try { DeviceHistory.Seen(d.Key.Model, d.Label, d.Connector.ToString(), d.IsInternal, d.PhysicalWidthMm, d.PhysicalHeightMm); }
            catch (Exception) { }
        }
    }

    /// <summary>Records the codes a monitor listed and the values they were read at.</summary>
    public static void Listed(DisplayInfo display, string capabilities, IEnumerable<VcpControl> controls)
    {
        SeenReading[] readings = controls.Select(c => new SeenReading(
            c.Code, c.Name, c.Kind.ToString(),
            c.Values.Select(v => (int)v.Value).ToArray(),
            c.Current < 0 ? null : c.Kind == VcpKind.Discrete ? c.CurrentValue : c.Current)).ToArray();
        try { DeviceHistory.Listed(display.Key.Model, capabilities, readings); }
        catch (Exception) { }
    }
}
