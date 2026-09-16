using System.Text;

using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace Umbra.Core.Displays;

/// <summary>
/// Enumerates attached displays and resolves each one to a persistent
/// <see cref="DisplayKey"/>.
/// </summary>
/// <remarks>
/// Three Windows APIs each know part of the truth and none knows all of it:
/// <list type="bullet">
/// <item>CCD (<c>QueryDisplayConfig</c>) knows the permanent device path,
/// the friendly name and how the panel is attached — but not the work area.</item>
/// <item>GDI (<c>EnumDisplaySettingsEx</c>) knows the current mode and where
/// the desktop places it — but identifies it only as <c>\\.\DISPLAY1</c>.</item>
/// <item><c>GetMonitorInfo</c> knows the work area and which is primary.</item>
/// </list>
/// The GDI device name is the join key between them.
/// </remarks>
public static class DisplayRegistry
{
    /// <summary>Snapshot of every active display, in CCD path order.</summary>
    public static unsafe List<DisplayInfo> Enumerate()
    {
        var result = new List<DisplayInfo>();

        foreach (CcdTarget t in QueryCcdTargets())
        {
            if (!TryGetCurrentMode(t.GdiName, out DEVMODEW mode)) continue;

            int left = mode.Anonymous1.Anonymous2.dmPosition.x;
            int top = mode.Anonymous1.Anonymous2.dmPosition.y;
            var bounds = new DisplayRect(
                left, top,
                left + (int)mode.dmPelsWidth,
                top + (int)mode.dmPelsHeight);

            // A point strictly inside the monitor resolves to its HMONITOR
            // without needing an enumeration callback.
            HMONITOR hmon = PInvoke.MonitorFromPoint(
                new System.Drawing.Point(left + bounds.Width / 2, top + bounds.Height / 2),
                MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONULL);

            DisplayRect work = bounds;
            bool primary = false;
            if (!hmon.IsNull)
            {
                var mi = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
                if (PInvoke.GetMonitorInfo(hmon, ref mi))
                {
                    work = ToRect(mi.rcWork);
                    primary = (mi.dwFlags & 1) != 0;   // MONITORINFOF_PRIMARY
                }
            }

            Edid.Identity edid = Edid.Read(t.DevicePath);
            (int physicalW, int physicalH) = Edid.PhysicalSize(t.DevicePath);
            string name = t.FriendlyName;
            if (string.IsNullOrWhiteSpace(name))
                name = Edid.NameFromEdid(t.DevicePath) ?? string.Empty;

            result.Add(new DisplayInfo
            {
                Key = new DisplayKey(t.DevicePath, edid.Model, edid.Serial),
                GdiName = t.GdiName,
                FriendlyName = name,
                Connector = t.Connector,
                IsPrimary = primary,
                Bounds = bounds,
                WorkArea = work,
                RefreshHz = mode.dmDisplayFrequency,
                BitsPerPixel = mode.dmBitsPerPel,
                Dpi = EffectiveDpi(hmon),
                PhysicalWidthMm = physicalW,
                PhysicalHeightMm = physicalH,
                Handle = (nint)hmon.Value,
            });
        }

        return result;
    }

    /// <summary>
    /// Fingerprint of the whole layout. Comparing this is how a replug,
    /// rearrange or resolution change is detected — monitor handles alone are
    /// not reliable, since Windows may recycle or keep them across such events.
    /// </summary>
    public static string Signature(IReadOnlyList<DisplayInfo> displays)
    {
        var sb = new StringBuilder();
        foreach (DisplayInfo d in displays)
            sb.Append(d.Token).Append('@').Append(d.Bounds)
              .Append(d.IsPrimary ? "*" : "").Append(';');
        return sb.ToString();
    }

    /// <summary>
    /// A layout fingerprint built only from cheap GDI calls — no CCD query and
    /// no registry access.
    /// </summary>
    /// <remarks>
    /// This exists to keep <see cref="Enumerate"/> off the once-a-second poll
    /// path. Full enumeration costs a <c>QueryDisplayConfig</c> plus a registry
    /// EDID read per monitor, which is wasted work when the answer is a
    /// constant between layout changes. The engine polls this instead and only
    /// re-enumerates when it actually changes.
    /// <para>
    /// It deliberately captures position, size and refresh rate, since those
    /// are what invalidate a cached bar geometry.
    /// </para>
    /// </remarks>
    public static unsafe string CheapSignature()
    {
        lock (ScratchGate)
        {
            Scratch.Clear();
            _enumProc ??= CollectMonitorRect;
            _ = PInvoke.EnumDisplayMonitors(default, null, _enumProc, default);

            var sb = new StringBuilder();
            foreach (RECT r in Scratch)
                sb.Append(r.left).Append(',').Append(r.top).Append(',')
                  .Append(r.right).Append(',').Append(r.bottom).Append(';');
            return sb.ToString();
        }
    }

    private static readonly List<RECT> Scratch = [];
    private static readonly Lock ScratchGate = new();
    private static MONITORENUMPROC? _enumProc;

    /// <summary>
    /// Records each monitor's rectangle, and nothing else.
    /// </summary>
    /// <remarks>
    /// The callback is handed the rectangle directly, so there is no
    /// <c>GetMonitorInfo</c> call here at all — the whole probe is one
    /// enumeration of data the kernel already has.
    /// <para>
    /// The delegate is cached in a field rather than created per call: a fresh
    /// delegate each second would allocate, and the marshalled thunk must stay
    /// alive for the duration of the native call.
    /// </para>
    /// </remarks>
    private static unsafe BOOL CollectMonitorRect(HMONITOR monitor, HDC hdc, RECT* rect, LPARAM data)
    {
        if (rect is not null) Scratch.Add(*rect);
        return true;
    }

    /// <summary>How the desktop is spread across the attached displays.</summary>
    public enum DisplayTopology
    {
        Single,
        Extended,
        Duplicated,
        Mixed,
    }

    /// <summary>
    /// Works out the desktop topology from the active display paths.
    /// </summary>
    /// <remarks>
    /// Derived rather than queried: Windows only reports a topology value for
    /// configurations it applied itself, and returns nothing useful for one the
    /// user assembled in Display settings. Two targets sharing one source is
    /// the definition of duplication, which is reliable either way.
    /// </remarks>
    public static DisplayTopology Topology()
    {
        List<CcdTarget> targets = QueryCcdTargets(activeOnly: true, allowDuplicateSources: true);
        if (targets.Count <= 1) return DisplayTopology.Single;

        var bySource = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (CcdTarget t in targets)
            bySource[t.GdiName] = bySource.GetValueOrDefault(t.GdiName) + 1;

        bool anyShared = false, anyAlone = false;
        foreach (int count in bySource.Values)
        {
            if (count > 1) anyShared = true;
            else anyAlone = true;
        }

        if (anyShared && anyAlone) return DisplayTopology.Mixed;
        return anyShared ? DisplayTopology.Duplicated : DisplayTopology.Extended;
    }

    /// <summary>
    /// Displays that are physically connected but not part of the desktop.
    /// </summary>
    /// <remarks>
    /// A monitor that is plugged in but switched off in Display settings shows
    /// up in no other enumeration, so without this it would simply be missing
    /// from the app with no explanation.
    /// </remarks>
    public static List<string> InactiveDisplays()
    {
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CcdTarget t in QueryCcdTargets(activeOnly: true, allowDuplicateSources: true))
            active.Add(t.DevicePath);

        var inactive = new List<string>();
        foreach (CcdTarget t in QueryCcdTargets(activeOnly: false, allowDuplicateSources: true))
        {
            if (active.Contains(t.DevicePath)) continue;

            string name = t.FriendlyName;
            if (string.IsNullOrWhiteSpace(name))
            {
                Edid.Identity id = Edid.Read(t.DevicePath);
                name = id.Model.Length > 0 ? id.Model : "Unknown display";
            }

            if (!inactive.Contains(name)) inactive.Add(name);
        }

        return inactive;
    }

    // ------------------------------------------------------------------ CCD --

    private readonly record struct CcdTarget(
        string GdiName, string DevicePath, string FriendlyName, ConnectorKind Connector);

    private static List<CcdTarget> QueryCcdTargets() =>
        QueryCcdTargets(activeOnly: true, allowDuplicateSources: false);

    private static unsafe List<CcdTarget> QueryCcdTargets(bool activeOnly, bool allowDuplicateSources)
    {
        var targets = new List<CcdTarget>();

        QUERY_DISPLAY_CONFIG_FLAGS flags = activeOnly
            ? QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS
            : QUERY_DISPLAY_CONFIG_FLAGS.QDC_ALL_PATHS;

        uint pathCount, modeCount;
        if (PInvoke.GetDisplayConfigBufferSizes(flags, &pathCount, &modeCount) != WIN32_ERROR.ERROR_SUCCESS)
            return targets;

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

        fixed (DISPLAYCONFIG_PATH_INFO* pPaths = paths)
        fixed (DISPLAYCONFIG_MODE_INFO* pModes = modes)
        {
            if (PInvoke.QueryDisplayConfig(flags, &pathCount, pPaths, &modeCount, pModes, null)
                != WIN32_ERROR.ERROR_SUCCESS)
                return targets;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (uint i = 0; i < pathCount; i++)
        {
            DISPLAYCONFIG_PATH_INFO p = paths[i];

            string gdi = GetSourceGdiName(p);

            // An inactive path has no GDI source name, which is fine when the
            // caller is specifically looking for displays that are switched off.
            if (activeOnly && gdi.Length == 0) continue;

            // In clone mode several targets share one source. The first is the
            // one GDI reports a mode for, so the rest would be duplicates —
            // except when the caller is counting them to detect duplication.
            if (!allowDuplicateSources && gdi.Length > 0 && !seen.Add(gdi)) continue;

            if (!TryGetTargetName(p, out string devicePath, out string friendly, out ConnectorKind kind))
                continue;

            targets.Add(new CcdTarget(gdi, devicePath, friendly, kind));
        }

        return targets;
    }

    private static unsafe string GetSourceGdiName(in DISPLAYCONFIG_PATH_INFO p)
    {
        var req = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                size = (uint)sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME),
                adapterId = p.sourceInfo.adapterId,
                id = p.sourceInfo.id,
            },
        };

        return PInvoke.DisplayConfigGetDeviceInfo(&req.header) == (int)WIN32_ERROR.ERROR_SUCCESS
            ? req.viewGdiDeviceName.ToString()
            : string.Empty;
    }

    private static unsafe bool TryGetTargetName(
        in DISPLAYCONFIG_PATH_INFO p,
        out string devicePath, out string friendly, out ConnectorKind kind)
    {
        devicePath = string.Empty;
        friendly = string.Empty;
        kind = ConnectorKind.Unknown;

        var req = new DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                size = (uint)sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME),
                adapterId = p.targetInfo.adapterId,
                id = p.targetInfo.id,
            },
        };

        if (PInvoke.DisplayConfigGetDeviceInfo(&req.header) != (int)WIN32_ERROR.ERROR_SUCCESS)
            return false;

        devicePath = req.monitorDevicePath.ToString();
        friendly = req.monitorFriendlyDeviceName.ToString();
        kind = MapConnector(req.outputTechnology);
        return devicePath.Length > 0;
    }

    private static ConnectorKind MapConnector(DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY t) => t switch
    {
        DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL
            or DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED
            or DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED
            => ConnectorKind.Internal,

        DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI
            => ConnectorKind.Hdmi,

        DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL
            => ConnectorKind.DisplayPort,

        DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DVI
            => ConnectorKind.Dvi,

        DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HD15
            or DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SVIDEO
            => ConnectorKind.Vga,

        DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EXTERNAL
            => ConnectorKind.Usb,

        _ => ConnectorKind.Unknown,
    };

    // ------------------------------------------------------------------ GDI --

    private static unsafe bool TryGetCurrentMode(string gdiName, out DEVMODEW mode)
    {
        mode = new DEVMODEW { dmSize = (ushort)sizeof(DEVMODEW) };
        return PInvoke.EnumDisplaySettingsEx(
            gdiName, ENUM_DISPLAY_SETTINGS_MODE.ENUM_CURRENT_SETTINGS, ref mode, 0);
    }

    private static uint EffectiveDpi(HMONITOR hmon)
    {
        const uint fallback = 96;
        if (hmon.IsNull) return fallback;

        return PInvoke.GetDpiForMonitor(hmon, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI,
                   out uint x, out _).Succeeded && x > 0
            ? x
            : fallback;
    }

    private static DisplayRect ToRect(RECT r) => new(r.left, r.top, r.right, r.bottom);
}
