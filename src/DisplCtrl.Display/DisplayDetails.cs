using DisplCtrl.Core.Displays;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace DisplCtrl.Display;

/// <summary>
/// Everything Windows shows under "Advanced display settings", for one display.
/// </summary>
/// <remarks>
/// All of it comes from data <c>QueryDisplayConfig</c> already returns — the
/// signal info hanging off the target mode, which most code throws away after
/// reading the resolution.
/// </remarks>
public sealed record DisplayDetail
{
    /// <summary>Signal actually on the cable, e.g. "2880 × 1800 @ 90.001 Hz".</summary>
    public string ActiveSignalMode { get; init; } = "—";

    /// <summary>What the desktop is composed at, which scaling can make differ.</summary>
    public string DesktopMode { get; init; } = "—";

    public string ColorFormat { get; init; } = "—";
    public string ColorSpace { get; init; } = "—";
    public string BitDepth { get; init; } = "—";
    public string HdrStatus { get; init; } = "—";
    public string Orientation { get; init; } = "—";
    public string ScanLineOrdering { get; init; } = "—";

    /// <summary>Pixel clock, which is what actually limits resolution × rate on a cable.</summary>
    public string PixelClock { get; init; } = "—";

    public string ColorProfile { get; init; } = "—";
}

public static class DisplayDetails
{
    /// <summary>Gathers the advanced detail for one display.</summary>
    public static unsafe DisplayDetail Read(DisplayInfo display)
    {
        var detail = new DisplayDetail();

        uint pathCount, modeCount;
        if (PInvoke.GetDisplayConfigBufferSizes(
                QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS,
                &pathCount, &modeCount) != WIN32_ERROR.ERROR_SUCCESS)
            return detail;

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

        fixed (DISPLAYCONFIG_PATH_INFO* pPaths = paths)
        fixed (DISPLAYCONFIG_MODE_INFO* pModes = modes)
        {
            if (PInvoke.QueryDisplayConfig(
                    QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS,
                    &pathCount, pPaths, &modeCount, pModes, null) != WIN32_ERROR.ERROR_SUCCESS)
                return detail;
        }

        for (uint i = 0; i < pathCount; i++)
        {
            DISPLAYCONFIG_PATH_INFO p = paths[i];
            if (!MatchesDisplay(p, display)) continue;

            return Build(p, modes, display);
        }

        return detail;
    }

    private static unsafe DisplayDetail Build(
        DISPLAYCONFIG_PATH_INFO path, DISPLAYCONFIG_MODE_INFO[] modes, DisplayInfo display)
    {
        string signal = "—", desktop = "—", scan = "—", clock = "—";

        uint targetIndex = path.targetInfo.Anonymous.modeInfoIdx;
        if (targetIndex < modes.Length
            && modes[targetIndex].infoType == DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_TARGET)
        {
            DISPLAYCONFIG_VIDEO_SIGNAL_INFO v = modes[targetIndex].Anonymous.targetMode.targetVideoSignalInfo;

            // vSyncFreq is a rational, and the exact value is the point: a
            // panel reporting 90.001 Hz rather than 90 is normal, and rounding
            // it away hides the difference between 59.94 and 60.
            double hz = v.vSyncFreq.Denominator == 0
                ? 0
                : (double)v.vSyncFreq.Numerator / v.vSyncFreq.Denominator;

            signal = $"{v.activeSize.cx} × {v.activeSize.cy} @ {hz:0.###} Hz";
            clock = v.pixelRate >= 1_000_000
                ? $"{v.pixelRate / 1_000_000.0:0.##} MHz"
                : $"{v.pixelRate} Hz";
            scan = Describe(v.scanLineOrdering);
        }

        uint sourceIndex = path.sourceInfo.Anonymous.modeInfoIdx;
        if (sourceIndex < modes.Length
            && modes[sourceIndex].infoType == DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
        {
            DISPLAYCONFIG_SOURCE_MODE s = modes[sourceIndex].Anonymous.sourceMode;
            desktop = $"{s.width} × {s.height}";
        }

        (string format, string space, string bits, string hdr) = ReadColor(path);

        return new DisplayDetail
        {
            ActiveSignalMode = signal,
            DesktopMode = desktop,
            ScanLineOrdering = scan,
            PixelClock = clock,
            ColorFormat = format,
            ColorSpace = space,
            BitDepth = bits,
            HdrStatus = hdr,
            Orientation = Describe(path.targetInfo.rotation),
            ColorProfile = ColorProfile.ReadName(display) ?? "System default",
        };
    }

    private static unsafe (string Format, string Space, string Bits, string Hdr) ReadColor(
        DISPLAYCONFIG_PATH_INFO path)
    {
        var info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                size = (uint)sizeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO),
                adapterId = path.targetInfo.adapterId,
                id = path.targetInfo.id,
            },
        };

        if (PInvoke.DisplayConfigGetDeviceInfo(&info.header) != (int)WIN32_ERROR.ERROR_SUCCESS)
            return ("—", "—", "—", "Not supported");

        bool supported = info.Anonymous.Anonymous.advancedColorSupported;
        bool enabled = info.Anonymous.Anonymous.advancedColorEnabled;
        bool wide = info.Anonymous.Anonymous.wideColorEnforced;

        string hdr = !supported ? "Not supported"
            : enabled ? "On"
            : "Supported, off";

        // Windows reports wide colour separately from HDR: a panel can be in
        // wide gamut without HDR being engaged.
        string space = enabled ? "HDR (scRGB / Rec.2020)"
            : wide ? "Wide colour gamut"
            : "SDR (sRGB)";

        return (Describe(info.colorEncoding), space, $"{info.bitsPerColorChannel}-bit per channel", hdr);
    }

    private static bool MatchesDisplay(DISPLAYCONFIG_PATH_INFO path, DisplayInfo display) =>
        TargetDevicePath(path) is { } devicePath
        && string.Equals(devicePath, display.Key.DevicePath, StringComparison.OrdinalIgnoreCase);

    private static unsafe string? TargetDevicePath(DISPLAYCONFIG_PATH_INFO path)
    {
        var name = new DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                size = (uint)sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME),
                adapterId = path.targetInfo.adapterId,
                id = path.targetInfo.id,
            },
        };

        return PInvoke.DisplayConfigGetDeviceInfo(&name.header) == (int)WIN32_ERROR.ERROR_SUCCESS
            ? name.monitorDevicePath.ToString()
            : null;
    }

    private static string Describe(DISPLAYCONFIG_COLOR_ENCODING encoding) => encoding switch
    {
        DISPLAYCONFIG_COLOR_ENCODING.DISPLAYCONFIG_COLOR_ENCODING_RGB => "RGB",
        DISPLAYCONFIG_COLOR_ENCODING.DISPLAYCONFIG_COLOR_ENCODING_YCBCR444 => "YCbCr 4:4:4",
        DISPLAYCONFIG_COLOR_ENCODING.DISPLAYCONFIG_COLOR_ENCODING_YCBCR422 => "YCbCr 4:2:2",
        DISPLAYCONFIG_COLOR_ENCODING.DISPLAYCONFIG_COLOR_ENCODING_YCBCR420 => "YCbCr 4:2:0",
        DISPLAYCONFIG_COLOR_ENCODING.DISPLAYCONFIG_COLOR_ENCODING_INTENSITY => "Intensity",
        _ => "—",
    };

    private static string Describe(DISPLAYCONFIG_SCANLINE_ORDERING ordering) => ordering switch
    {
        DISPLAYCONFIG_SCANLINE_ORDERING.DISPLAYCONFIG_SCANLINE_ORDERING_PROGRESSIVE => "Progressive",
        DISPLAYCONFIG_SCANLINE_ORDERING.DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED_UPPERFIELDFIRST
            => "Interlaced (upper first)",
        DISPLAYCONFIG_SCANLINE_ORDERING.DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED_LOWERFIELDFIRST
            => "Interlaced (lower first)",
        _ => "—",
    };

    private static string Describe(DISPLAYCONFIG_ROTATION rotation) => rotation switch
    {
        DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_IDENTITY => "Landscape",
        DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_ROTATE90 => "Portrait",
        DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_ROTATE180 => "Landscape (flipped)",
        DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_ROTATE270 => "Portrait (flipped)",
        _ => "—",
    };
}
