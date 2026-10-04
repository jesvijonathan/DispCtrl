using DispCtrl.Core.Displays;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;

namespace DispCtrl.Display.Topology;

/// <summary>The four arrangements Windows' own Display settings offers.</summary>
public enum DesktopArrangement
{
    /// <summary>Desktop spread across every display.</summary>
    Extend,

    /// <summary>The same image on every display.</summary>
    Duplicate,

    /// <summary>Built-in panel only; external displays switched off.</summary>
    InternalOnly,

    /// <summary>External displays only; built-in panel switched off.</summary>
    ExternalOnly,
}

/// <summary>
/// Switches the desktop between extend, duplicate and single-display layouts.
/// </summary>
/// <remarks>
/// This is the same operation as <c>Win</c>+<c>P</c>, done through the
/// documented topology flags rather than by rewriting paths by hand. Letting
/// Windows pick the paths means it reuses the arrangement the user last had for
/// that combination of monitors, instead of resetting positions every time.
/// </remarks>
public static class DesktopLayout
{
    // SetDisplayConfig topology flags. CsWin32 exposes the enum, but the
    // combinations are spelled out here so the intent is readable.
    private const uint SdcApply = 0x00000080;
    private const uint SdcTopologyInternal = 0x00000001;
    private const uint SdcTopologyClone = 0x00000002;
    private const uint SdcTopologyExtend = 0x00000004;
    private const uint SdcTopologyExternal = 0x00000008;

    /// <summary>
    /// Applies an arrangement.
    /// </summary>
    /// <remarks>
    /// Blocks while the display stack reconfigures — expect a black flash and
    /// up to a few seconds. Must not be called on the UI thread.
    /// </remarks>
    public static bool Apply(DesktopArrangement arrangement)
    {
        uint topology = arrangement switch
        {
            DesktopArrangement.Extend => SdcTopologyExtend,
            DesktopArrangement.Duplicate => SdcTopologyClone,
            DesktopArrangement.InternalOnly => SdcTopologyInternal,
            DesktopArrangement.ExternalOnly => SdcTopologyExternal,
            _ => SdcTopologyExtend,
        };

        WIN32_ERROR result = (WIN32_ERROR)PInvoke.SetDisplayConfig(
            null, null, (SET_DISPLAY_CONFIG_FLAGS)(SdcApply | topology));

        return result == WIN32_ERROR.ERROR_SUCCESS;
    }

    /// <summary>What the desktop is doing now, in Win+P's terms, or null for a mix of duplicated and extended.</summary>
    /// <param name="onlyDisplayIsInternal">
    /// Asked only when one display is on and Windows has no record of which
    /// choice put it there: then a built-in panel means "PC screen only".
    /// </param>
    /// <remarks>
    /// Duplicate and Extend are read from the paths themselves, which is
    /// right however the desk was arranged. With one display on, Windows'
    /// own record decides between the two single-screen choices; see
    /// <see cref="DisplayRegistry.Recorded"/>.
    /// </remarks>
    public static DesktopArrangement? Current(Func<bool> onlyDisplayIsInternal) =>
        DisplayRegistry.Topology() switch
        {
            DisplayRegistry.DisplayTopology.Extended => DesktopArrangement.Extend,
            DisplayRegistry.DisplayTopology.Duplicated => DesktopArrangement.Duplicate,
            DisplayRegistry.DisplayTopology.Single => DisplayRegistry.Recorded() switch
            {
                DisplayRegistry.RecordedChoice.Internal => DesktopArrangement.InternalOnly,
                DisplayRegistry.RecordedChoice.External => DesktopArrangement.ExternalOnly,
                _ => onlyDisplayIsInternal() ? DesktopArrangement.InternalOnly : DesktopArrangement.ExternalOnly,
            },
            _ => null,
        };
}
