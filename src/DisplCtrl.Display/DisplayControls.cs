using DisplCtrl.Core.Displays;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.ColorSystem;

namespace DisplCtrl.Display;

/// <summary>Which way up a display is.</summary>
public enum ScreenOrientation
{
    Landscape = 0,
    Portrait = 1,
    LandscapeFlipped = 2,
    PortraitFlipped = 3,
}

/// <summary>The ICC profile Windows associates with a display.</summary>
public static class ColorProfile
{
    /// <summary>
    /// The default ICC profile for a display, or null when it uses the system one.
    /// </summary>
    /// <remarks>
    /// Read through WCS rather than the older <c>GetICMProfile</c>, because WCS
    /// is what Windows' own Color Management dialog reads and writes — the two
    /// can disagree on a display the user has configured there.
    /// </remarks>
    public static unsafe string? ReadName(DisplayInfo display)
    {
        try
        {
            var scope = WCS_PROFILE_MANAGEMENT_SCOPE.WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER;

            // No string overload is generated for these, so the device name is
            // pinned and passed as a raw pointer.
            fixed (char* device = display.GdiName)
            {
                var deviceName = new Windows.Win32.Foundation.PCWSTR(device);

                // CsWin32 generates a friendly string overload for the size
                // call and a raw PCWSTR one for the fetch; they are not
                // interchangeable.
                if (!PInvoke.WcsGetDefaultColorProfileSize(
                        scope, display.GdiName,
                        COLORPROFILETYPE.CPT_ICC, COLORPROFILESUBTYPE.CPST_NONE, 0, out uint size)
                    || size == 0)
                    return null;

                // Heap, not stackalloc, and bounded. This runs on a thread-pool
                // thread with a small stack, and the size comes from an API
                // whose failure modes are not worth trusting with the stack —
                // an implausible value there would take the process down with
                // an access violation rather than an exception.
                const uint MaxProfileNameBytes = 64 * 1024;
                if (size > MaxProfileNameBytes) return null;

                // size is in bytes, including the terminator.
                var buffer = new char[(int)size / sizeof(char)];

                fixed (char* p = buffer)
                {
                    if (!PInvoke.WcsGetDefaultColorProfile(
                            scope, deviceName,
                            COLORPROFILETYPE.CPT_ICC, COLORPROFILESUBTYPE.CPST_NONE, 0,
                            size, new Windows.Win32.Foundation.PWSTR(p)))
                        return null;
                }

                string name = new string(buffer).TrimEnd('\0');
                return string.IsNullOrWhiteSpace(name) ? null : name;
            }
        }
        catch (Exception)
        {
            // WCS is absent or refuses on some virtual adapters.
            return null;
        }
    }

    /// <summary>Opens Windows' own Color Management dialog for this display.</summary>
    /// <remarks>
    /// Installing and associating a profile properly means enumerating installed
    /// profiles, validating them and handling per-user versus system scope —
    /// all of which Windows already does well. Handing off is better than a
    /// half-built copy that can leave a display with a broken profile.
    /// </remarks>
    public static void OpenColorManagement()
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "colorcpl.exe",
            UseShellExecute = true,
        });
    }
}

/// <summary>Orientation and primary-display changes.</summary>
public static class DisplayArrangement
{
    private const uint DmPelsWidth = 0x00080000;
    private const uint DmPelsHeight = 0x00100000;
    private const uint DmPosition = 0x00000020;
    private const uint DmDisplayOrientation = 0x00000080;

    /// <summary>
    /// Rotates a display.
    /// </summary>
    /// <remarks>
    /// Width and height are swapped when moving between landscape and portrait.
    /// The driver expects the mode in the <em>rotated</em> frame, and omitting
    /// the swap is rejected as an unsupported mode rather than silently fixed.
    /// </remarks>
    public static unsafe bool SetOrientation(DisplayInfo display, ScreenOrientation orientation)
    {
        using var stateChange = new DisplayStateChange();
        var dm = new DEVMODEW { dmSize = (ushort)sizeof(DEVMODEW) };
        if (!PInvoke.EnumDisplaySettingsEx(
                display.GdiName, ENUM_DISPLAY_SETTINGS_MODE.ENUM_CURRENT_SETTINGS, ref dm, 0))
            return false;

        uint current = (uint)dm.Anonymous1.Anonymous2.dmDisplayOrientation;
        uint target = (uint)orientation;
        if (current == target) return true;

        bool currentlyPortrait = current is 1 or 3;
        bool targetPortrait = target is 1 or 3;

        if (currentlyPortrait != targetPortrait)
            (dm.dmPelsWidth, dm.dmPelsHeight) = (dm.dmPelsHeight, dm.dmPelsWidth);

        dm.Anonymous1.Anonymous2.dmDisplayOrientation = (DEVMODE_DISPLAY_ORIENTATION)target;
        dm.dmFields = (DEVMODE_FIELD_FLAGS)(DmPelsWidth | DmPelsHeight | DmDisplayOrientation);

        return PInvoke.ChangeDisplaySettingsEx(display.GdiName, dm, CDS_TYPE.CDS_UPDATEREGISTRY, null)
            == DISP_CHANGE.DISP_CHANGE_SUCCESSFUL;
    }

    /// <summary>
    /// Makes a display the primary one.
    /// </summary>
    /// <remarks>
    /// Windows defines the primary display as the one whose origin is (0,0), so
    /// this is a coordinate change rather than a flag. Every other display has
    /// to be shifted by the same delta in the same batch, or they would end up
    /// overlapping. The changes are staged with <c>CDS_NORESET</c> and then
    /// committed by a single null call, so the desktop reflows once.
    /// </remarks>
    public static unsafe bool SetPrimary(DisplayInfo target, IReadOnlyList<DisplayInfo> all)
    {
        using var stateChange = new DisplayStateChange();
        int dx = -target.Bounds.Left;
        int dy = -target.Bounds.Top;

        foreach (DisplayInfo d in all)
        {
            var dm = new DEVMODEW { dmSize = (ushort)sizeof(DEVMODEW) };
            if (!PInvoke.EnumDisplaySettingsEx(
                    d.GdiName, ENUM_DISPLAY_SETTINGS_MODE.ENUM_CURRENT_SETTINGS, ref dm, 0))
                continue;

            dm.Anonymous1.Anonymous2.dmPosition.x = d.Bounds.Left + dx;
            dm.Anonymous1.Anonymous2.dmPosition.y = d.Bounds.Top + dy;
            dm.dmFields = (DEVMODE_FIELD_FLAGS)DmPosition;

            CDS_TYPE flags = CDS_TYPE.CDS_UPDATEREGISTRY | CDS_TYPE.CDS_NORESET;
            if (d.Key == target.Key) flags |= CDS_TYPE.CDS_SET_PRIMARY;

            PInvoke.ChangeDisplaySettingsEx(d.GdiName, dm, flags, null);
        }

        // Commit everything staged above in one reflow.
        return PInvoke.ChangeDisplaySettingsEx(null, null, 0, null)
            == DISP_CHANGE.DISP_CHANGE_SUCCESSFUL;
    }

    /// <summary>
    /// Moves displays to new desktop coordinates, applied as one change.
    /// </summary>
    /// <remarks>
    /// Uses the CCD API rather than <c>ChangeDisplaySettingsEx</c>. The legacy
    /// call is a compatibility shim on modern Windows and was measured
    /// returning <c>DISP_CHANGE_FAILED</c> for <em>every</em> variation tried
    /// on this hardware — position-only, full mode description, staged and
    /// unstaged alike. <c>SetDisplayConfig</c> is what Display settings itself
    /// drives, and it takes the whole layout in one call, so there is no
    /// staging step to fail silently.
    /// </remarks>
    public static unsafe bool SetPositions(IReadOnlyDictionary<string, (int X, int Y)> positions,
                                           IReadOnlyList<DisplayInfo> all)
        => SetPositions(positions, all, out _);

    /// <param name="error">
    /// What Windows said when it refused. Worth surfacing: SetDisplayConfig
    /// rejects a whole arrangement with one code and no indication of which
    /// display is at fault, so without it a failure is indistinguishable from
    /// the call never having been made.
    /// </param>
    public static unsafe bool SetPositions(IReadOnlyDictionary<string, (int X, int Y)> positions,
                                           IReadOnlyList<DisplayInfo> all,
                                           out string? error)
    {
        using var stateChange = new DisplayStateChange();
        error = null;
        uint pathCount, modeCount;
        if (PInvoke.GetDisplayConfigBufferSizes(
                QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS,
                &pathCount, &modeCount) != WIN32_ERROR.ERROR_SUCCESS)
        {
            error = "could not read the current display configuration";
            return false;
        }

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

        fixed (DISPLAYCONFIG_PATH_INFO* pPaths = paths)
        fixed (DISPLAYCONFIG_MODE_INFO* pModes = modes)
        {
            if (PInvoke.QueryDisplayConfig(
                    QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS,
                    &pathCount, pPaths, &modeCount, pModes, null) != WIN32_ERROR.ERROR_SUCCESS)
            {
                error = "could not read the current display configuration";
                return false;
            }
        }

        // Map each display's device path to the position it should take.
        bool changed = false;

        for (uint i = 0; i < pathCount; i++)
        {
            string? devicePath = TargetDevicePath(paths[i]);
            if (devicePath is null) continue;

            DisplayInfo? display = null;
            foreach (DisplayInfo d in all)
            {
                if (!string.Equals(d.Key.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase)) continue;
                display = d;
                break;
            }

            if (display is null) continue;
            if (!positions.TryGetValue(display.Token, out (int X, int Y) p)) continue;

            uint sourceIndex = paths[i].sourceInfo.Anonymous.modeInfoIdx;
            if (sourceIndex >= modes.Length) continue;
            if (modes[sourceIndex].infoType != DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
                continue;

            modes[sourceIndex].Anonymous.sourceMode.position.x = p.X;
            modes[sourceIndex].Anonymous.sourceMode.position.y = p.Y;
            changed = true;
        }

        if (!changed)
        {
            error = "none of the displays on screen matched the live configuration";
            return false;
        }

        // SDC_USE_SUPPLIED_DISPLAY_CONFIG: use exactly these paths and modes.
        // SDC_ALLOW_CHANGES lets Windows reconcile anything it must;
        // SDC_SAVE_TO_DATABASE makes the arrangement stick across replugs.
        const uint SdcUseSupplied = 0x00000020;
        const uint SdcApply = 0x00000080;
        const uint SdcSaveToDatabase = 0x00000200;
        const uint SdcAllowChanges = 0x00000400;

        fixed (DISPLAYCONFIG_PATH_INFO* pPaths = paths)
        fixed (DISPLAYCONFIG_MODE_INFO* pModes = modes)
        {
            var result = (WIN32_ERROR)PInvoke.SetDisplayConfig(
                pathCount, pPaths, modeCount, pModes,
                (SET_DISPLAY_CONFIG_FLAGS)(SdcUseSupplied | SdcApply | SdcSaveToDatabase | SdcAllowChanges));

            if (result == WIN32_ERROR.ERROR_SUCCESS) return true;

            error = result switch
            {
                WIN32_ERROR.ERROR_INVALID_PARAMETER =>
                    "Windows rejected the arrangement as invalid — a display is not flush against its neighbours",
                WIN32_ERROR.ERROR_NOT_SUPPORTED =>
                    "the graphics driver does not support setting this arrangement",
                WIN32_ERROR.ERROR_ACCESS_DENIED =>
                    "another process is already changing the display configuration",
                WIN32_ERROR.ERROR_GEN_FAILURE =>
                    "the graphics driver failed the request",
                _ => $"Windows returned error {(int)result}",
            };

            return false;
        }
    }

    /// <summary>
    /// Changes a display's refresh rate through the CCD API.
    /// </summary>
    /// <remarks>
    /// <c>ChangeDisplaySettingsEx</c> refuses every mode change on this
    /// hardware, so the legacy path is unusable. Here the target's desired rate
    /// is set and its mode index invalidated, which asks Windows to find a mode
    /// matching that rate rather than describing the timing by hand — the
    /// pixel clock and blanking intervals are the driver's business.
    /// </remarks>
    public static unsafe bool SetRefreshRate(DisplayInfo display, uint hz)
    {
        using var stateChange = new DisplayStateChange();
        const uint PathModeIdxInvalid = 0xffffffff;
        const uint SdcUseSupplied = 0x00000020;
        const uint SdcApply = 0x00000080;
        const uint SdcSaveToDatabase = 0x00000200;
        const uint SdcAllowChanges = 0x00000400;

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

        bool found = false;
        for (uint i = 0; i < pathCount; i++)
        {
            string? devicePath = TargetDevicePath(paths[i]);
            if (!string.Equals(devicePath, display.Key.DevicePath, StringComparison.OrdinalIgnoreCase))
                continue;

            paths[i].targetInfo.refreshRate = new DISPLAYCONFIG_RATIONAL { Numerator = hz, Denominator = 1 };
            paths[i].targetInfo.Anonymous.modeInfoIdx = PathModeIdxInvalid;
            found = true;
            break;
        }

        if (!found) return false;

        fixed (DISPLAYCONFIG_PATH_INFO* pPaths = paths)
        fixed (DISPLAYCONFIG_MODE_INFO* pModes = modes)
        {
            var result = (WIN32_ERROR)PInvoke.SetDisplayConfig(
                pathCount, pPaths, modeCount, pModes,
                (SET_DISPLAY_CONFIG_FLAGS)(SdcUseSupplied | SdcApply | SdcSaveToDatabase | SdcAllowChanges));

            return result == WIN32_ERROR.ERROR_SUCCESS;
        }
    }

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

}
