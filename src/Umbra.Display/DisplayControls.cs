using Umbra.Core.Displays;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.ColorSystem;

namespace Umbra.Display;

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

    /// <summary>Moves displays to new desktop coordinates, applied as one change.</summary>
    /// <remarks>
    /// Used by the drag-to-arrange canvas. Positions are staged per display and
    /// committed once, so the desktop does not reflow between each move.
    /// </remarks>
    public static unsafe bool SetPositions(IReadOnlyDictionary<string, (int X, int Y)> positions,
                                           IReadOnlyList<DisplayInfo> all)
    {
        bool staged = false;

        foreach (DisplayInfo d in all)
        {
            if (!positions.TryGetValue(d.Token, out (int X, int Y) p)) continue;

            var dm = new DEVMODEW { dmSize = (ushort)sizeof(DEVMODEW) };
            if (!PInvoke.EnumDisplaySettingsEx(
                    d.GdiName, ENUM_DISPLAY_SETTINGS_MODE.ENUM_CURRENT_SETTINGS, ref dm, 0))
                continue;

            dm.Anonymous1.Anonymous2.dmPosition.x = p.X;
            dm.Anonymous1.Anonymous2.dmPosition.y = p.Y;
            dm.dmFields = (DEVMODE_FIELD_FLAGS)DmPosition;

            PInvoke.ChangeDisplaySettingsEx(
                d.GdiName, dm, CDS_TYPE.CDS_UPDATEREGISTRY | CDS_TYPE.CDS_NORESET, null);
            staged = true;
        }

        if (!staged) return false;

        return PInvoke.ChangeDisplaySettingsEx(null, null, 0, null)
            == DISP_CHANGE.DISP_CHANGE_SUCCESSFUL;
    }
}
