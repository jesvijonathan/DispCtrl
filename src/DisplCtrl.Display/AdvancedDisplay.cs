using System.Runtime.InteropServices;
using DisplCtrl.Core.Displays;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;

namespace DisplCtrl.Display;

/// <summary>HDR capability and state for one display.</summary>
public readonly record struct HdrState(bool Supported, bool Enabled, uint BitsPerChannel)
{
    public static HdrState Unsupported => new(false, false, 0);
}

/// <summary>Per-monitor DPI scaling, as percentages.</summary>
public readonly record struct ScalingState(
    IReadOnlyList<int> Available, int Current, int Recommended, bool Supported)
{
    public static ScalingState Unsupported => new([], 100, 100, false);
}

/// <summary>
/// Display features that sit behind the CCD device-info interface: HDR, and
/// per-monitor scaling.
/// </summary>
public static class AdvancedDisplay
{
    /// <summary>
    /// The scaling steps Windows offers, in percent.
    /// </summary>
    /// <remarks>
    /// Fixed by the shell, not by the driver. The scaling API works in offsets
    /// into this table rather than in percentages.
    /// </remarks>
    private static readonly int[] ScaleSteps =
        [100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500];

    // ------------------------------------------------------------------ HDR --

    public static HdrState ReadHdr(DisplayInfo display)
    {
        if (!TryResolvePath(display, out LUID adapter, out uint sourceId, out uint targetId))
            return HdrState.Unsupported;

        return ReadHdrCore(adapter, targetId);
    }

    private static unsafe HdrState ReadHdrCore(LUID adapter, uint targetId)
    {
        var info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                size = (uint)sizeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO),
                adapterId = adapter,
                id = targetId,
            },
        };

        if (PInvoke.DisplayConfigGetDeviceInfo(&info.header) != (int)WIN32_ERROR.ERROR_SUCCESS)
            return HdrState.Unsupported;

        return new HdrState(
            info.Anonymous.Anonymous.advancedColorSupported,
            info.Anonymous.Anonymous.advancedColorEnabled,
            info.bitsPerColorChannel);
    }

    public static unsafe bool WriteHdr(DisplayInfo display, bool enable)
    {
        using var stateChange = new DisplayStateChange();
        if (!TryResolvePath(display, out LUID adapter, out uint sourceId, out uint targetId))
            return false;

        var state = new DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE,
                size = (uint)sizeof(DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE),
                adapterId = adapter,
                id = targetId,
            },
        };
        state.Anonymous.Anonymous._bitfield = enable ? 1u : 0u;   // enableAdvancedColor : 1

        return PInvoke.DisplayConfigSetDeviceInfo(&state.header) == (int)WIN32_ERROR.ERROR_SUCCESS;
    }

    // -------------------------------------------------------------- scaling --

    /// <summary>
    /// <c>DISPLAYCONFIG_DEVICE_INFO_GET_DPI_SCALE</c>. Undocumented — see
    /// <see cref="ReadScaling"/>.
    /// </summary>
    private const int GetDpiScale = -3;

    /// <summary><c>DISPLAYCONFIG_DEVICE_INFO_SET_DPI_SCALE</c>. Undocumented.</summary>
    private const int SetDpiScale = -4;

    [StructLayout(LayoutKind.Sequential)]
    private struct DpiScaleGet
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;

        /// <summary>Steps below the recommended scale, as a negative number.</summary>
        public int minScaleRel;

        /// <summary>Current scale, as an offset from the recommended one.</summary>
        public int curScaleRel;

        /// <summary>Steps above the recommended scale.</summary>
        public int maxScaleRel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DpiScaleSet
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public int scaleRel;
    }

    /// <summary>
    /// Reads the scaling steps available for a display.
    /// </summary>
    /// <remarks>
    /// <b>This uses an undocumented interface.</b> Windows exposes no public API
    /// for reading or setting per-monitor scaling; the shell does it through
    /// <c>DisplayConfigGetDeviceInfo</c> with two negative, undocumented type
    /// codes. Every third-party tool that offers this does the same thing.
    /// <para>
    /// Two consequences worth knowing. It can break in a future Windows update
    /// with no warning, so every failure path here returns
    /// <see cref="ScalingState.Unsupported"/> rather than throwing. And
    /// undocumented API use is a documented reason for Microsoft Store
    /// certification to reject an app — this is the one feature in DisplCtrl that
    /// may have to be sideload-only.
    /// </para>
    /// <para>
    /// The API works in offsets from the <em>recommended</em> scale rather than
    /// in percentages, so the absolute value is
    /// <c>ScaleSteps[|minScaleRel| + curScaleRel]</c>.
    /// </para>
    /// </remarks>
    public static unsafe ScalingState ReadScaling(DisplayInfo display)
    {
        if (!TryResolvePath(display, out LUID adapter, out uint sourceId, out uint targetId))
            return ScalingState.Unsupported;

        var request = new DpiScaleGet
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = (DISPLAYCONFIG_DEVICE_INFO_TYPE)GetDpiScale,
                size = (uint)sizeof(DpiScaleGet),
                adapterId = adapter,
                id = sourceId,
            },
        };

        if (PInvoke.DisplayConfigGetDeviceInfo(&request.header) != (int)WIN32_ERROR.ERROR_SUCCESS)
            return ScalingState.Unsupported;

        int minAbs = Math.Abs(request.minScaleRel);
        int count = minAbs + request.maxScaleRel + 1;

        // A range that does not fit the known table means the undocumented
        // contract has shifted. Report unsupported rather than guess.
        if (count <= 0 || count > ScaleSteps.Length) return ScalingState.Unsupported;

        int currentIndex = minAbs + request.curScaleRel;
        if (currentIndex < 0 || currentIndex >= ScaleSteps.Length) return ScalingState.Unsupported;

        return new ScalingState(
            ScaleSteps[..count],
            ScaleSteps[currentIndex],
            ScaleSteps[minAbs],
            true);
    }

    /// <summary>Sets scaling to <paramref name="percent"/>, which must be one of the reported steps.</summary>
    public static unsafe bool WriteScaling(DisplayInfo display, int percent)
    {
        using var stateChange = new DisplayStateChange();
        if (!TryResolvePath(display, out LUID adapter, out uint sourceId, out uint targetId))
            return false;

        ScalingState state = ReadScaling(display);
        if (!state.Supported) return false;

        int targetIndex = Array.IndexOf(ScaleSteps, percent);
        int recommendedIndex = Array.IndexOf(ScaleSteps, state.Recommended);
        if (targetIndex < 0 || recommendedIndex < 0) return false;

        var request = new DpiScaleSet
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = (DISPLAYCONFIG_DEVICE_INFO_TYPE)SetDpiScale,
                size = (uint)sizeof(DpiScaleSet),
                adapterId = adapter,
                id = sourceId,
            },
            scaleRel = targetIndex - recommendedIndex,
        };

        return PInvoke.DisplayConfigSetDeviceInfo(&request.header) == (int)WIN32_ERROR.ERROR_SUCCESS;
    }

    // -------------------------------------------------------------- plumbing --

    /// <summary>
    /// Finds the adapter, source and target ids for a display.
    /// </summary>
    /// <remarks>
    /// Matched on the monitor device path rather than on index: path order is
    /// not stable across calls, and using it would silently address the wrong
    /// monitor after a replug.
    /// </remarks>
    private static unsafe bool TryResolvePath(
        DisplayInfo display, out LUID adapter, out uint sourceId, out uint targetId)
    {
        adapter = default;
        sourceId = 0;
        targetId = 0;

        uint pathCount, modeCount;
        if (PInvoke.GetDisplayConfigBufferSizes(
                QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS,
                &pathCount, &modeCount) != WIN32_ERROR.ERROR_SUCCESS)
            return false;

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

        fixed (DISPLAYCONFIG_PATH_INFO* pPaths = paths)
        fixed (DISPLAYCONFIG_MODE_INFO* pModes = modes)
        {
            if (PInvoke.QueryDisplayConfig(
                    QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS,
                    &pathCount, pPaths, &modeCount, pModes, null) != WIN32_ERROR.ERROR_SUCCESS)
                return false;
        }

        for (uint i = 0; i < pathCount; i++)
        {
            DISPLAYCONFIG_PATH_INFO p = paths[i];

            var name = new DISPLAYCONFIG_TARGET_DEVICE_NAME
            {
                header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                    size = (uint)sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME),
                    adapterId = p.targetInfo.adapterId,
                    id = p.targetInfo.id,
                },
            };

            if (PInvoke.DisplayConfigGetDeviceInfo(&name.header) != (int)WIN32_ERROR.ERROR_SUCCESS)
                continue;

            if (!string.Equals(name.monitorDevicePath.ToString(), display.Key.DevicePath,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            adapter = p.targetInfo.adapterId;
            targetId = p.targetInfo.id;
            sourceId = p.sourceInfo.id;
            return true;
        }

        return false;
    }
}
