using Windows.Win32;
using Windows.Win32.Graphics.Gdi;

namespace DispCtrl.Display;

/// <summary>One mode a display can run in.</summary>
public readonly record struct DisplayMode(uint Width, uint Height, uint RefreshHz, uint Bpp)
{
    public string Resolution => $"{Width} × {Height}";
    public override string ToString() => $"{Width} × {Height} @ {RefreshHz} Hz";
}

/// <summary>Outcome of a mode change, so the UI can say what happened.</summary>
public enum ModeChangeResult
{
    Applied,
    AppliedSeamlessly,
    NotSupported,
    NeedsRestart,
    Failed,
}

/// <summary>Enumerates and applies display modes.</summary>
public static class DisplayModes
{
    /// <summary>Every distinct mode the driver reports, newest-first by size then rate.</summary>
    public static List<DisplayMode> Available(string gdiName)
    {
        var seen = new HashSet<DisplayMode>();
        var modes = new List<DisplayMode>();

        for (uint i = 0; ; i++)
        {
            if (!TryEnum(gdiName, i, out DEVMODEW dm)) break;

            // Anything below 32bpp is a legacy mode nobody wants offered.
            if (dm.dmBitsPerPel < 32) continue;

            var mode = new DisplayMode(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency, dm.dmBitsPerPel);
            if (seen.Add(mode)) modes.Add(mode);
        }

        modes.Sort((a, b) =>
        {
            int byPixels = (b.Width * b.Height).CompareTo(a.Width * a.Height);
            return byPixels != 0 ? byPixels : b.RefreshHz.CompareTo(a.RefreshHz);
        });

        return modes;
    }

    public static DisplayMode? Current(string gdiName) =>
        TryEnumCurrent(gdiName, out DEVMODEW dm)
            ? new DisplayMode(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency, dm.dmBitsPerPel)
            : null;

    /// <summary>Refresh rates available at one resolution, ascending.</summary>
    public static List<uint> RefreshRatesAt(string gdiName, uint width, uint height)
    {
        var rates = new SortedSet<uint>();
        foreach (DisplayMode m in Available(gdiName))
            if (m.Width == width && m.Height == height)
                rates.Add(m.RefreshHz);
        return [.. rates];
    }

    /// <summary>Distinct resolutions, largest first.</summary>
    public static List<(uint Width, uint Height)> Resolutions(string gdiName)
    {
        var seen = new HashSet<(uint, uint)>();
        var list = new List<(uint, uint)>();

        foreach (DisplayMode m in Available(gdiName))
            if (seen.Add((m.Width, m.Height)))
                list.Add((m.Width, m.Height));

        return list;
    }

    /// <summary>
    /// Switches a display to <paramref name="target"/>.
    /// </summary>
    /// <remarks>
    /// Changing only the refresh rate is submitted with only
    /// <c>DM_DISPLAYFREQUENCY</c> set, leaving the resolution fields untouched.
    /// That is what lets a driver treat it as a timing change on the existing
    /// mode and switch without blanking the panel — sending a full mode
    /// description forces a complete modeset and a black flash, even when only
    /// the rate differs.
    /// <para>
    /// A test pass runs first, so an unsupported combination is reported
    /// instead of being attempted.
    /// </para>
    /// </remarks>
    public static unsafe ModeChangeResult Apply(string gdiName, DisplayMode target)
    {
        using var stateChange = new DisplayStateChange();
        if (!TryEnumCurrent(gdiName, out DEVMODEW dm)) return ModeChangeResult.Failed;

        bool rateOnly = dm.dmPelsWidth == target.Width && dm.dmPelsHeight == target.Height;

        DEVMODEW request = dm;
        if (rateOnly)
        {
            request.dmDisplayFrequency = target.RefreshHz;
            request.dmFields = (DEVMODE_FIELD_FLAGS)0x00400000;   // DM_DISPLAYFREQUENCY
        }
        else
        {
            request.dmPelsWidth = target.Width;
            request.dmPelsHeight = target.Height;
            request.dmDisplayFrequency = target.RefreshHz;
            request.dmFields = (DEVMODE_FIELD_FLAGS)(0x00080000 | 0x00100000 | 0x00400000);
            // DM_BITSPERPEL is deliberately not set: the desktop is 32bpp and
            // asking for it again only adds a reason for the driver to refuse.
        }

        DISP_CHANGE test = PInvoke.ChangeDisplaySettingsEx(
            gdiName, request, CDS_TYPE.CDS_TEST, null);

        if (test != DISP_CHANGE.DISP_CHANGE_SUCCESSFUL)
            return ModeChangeResult.NotSupported;

        DISP_CHANGE applied = PInvoke.ChangeDisplaySettingsEx(
            gdiName, request, CDS_TYPE.CDS_UPDATEREGISTRY, null);

        return applied switch
        {
            DISP_CHANGE.DISP_CHANGE_SUCCESSFUL =>
                rateOnly ? ModeChangeResult.AppliedSeamlessly : ModeChangeResult.Applied,
            DISP_CHANGE.DISP_CHANGE_RESTART => ModeChangeResult.NeedsRestart,
            _ => ModeChangeResult.Failed,
        };
    }

    // ------------------------------------------------------------ helpers --

    private static unsafe bool TryEnum(string gdiName, uint index, out DEVMODEW dm)
    {
        dm = new DEVMODEW { dmSize = (ushort)sizeof(DEVMODEW) };
        return PInvoke.EnumDisplaySettingsEx(
            gdiName, (ENUM_DISPLAY_SETTINGS_MODE)index, ref dm, 0);
    }

    private static unsafe bool TryEnumCurrent(string gdiName, out DEVMODEW dm)
    {
        dm = new DEVMODEW { dmSize = (ushort)sizeof(DEVMODEW) };
        return PInvoke.EnumDisplaySettingsEx(
            gdiName, ENUM_DISPLAY_SETTINGS_MODE.ENUM_CURRENT_SETTINGS, ref dm, 0);
    }
}
