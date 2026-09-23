using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DispCtrl.Engine.Shell;

/// <summary>
/// Draws a tray icon from a font glyph, the way Windows draws its own.
/// </summary>
/// <remarks>
/// Windows' notification area icons - network, volume, battery - are single
/// glyphs from Segoe Fluent Icons in one colour, white on a dark taskbar and
/// black on a light one. An .ico cannot do that: it is one picture for every
/// theme, and at 16 pixels a coloured logo among monochrome glyphs is the icon
/// that looks installed rather than built in. So the icon is drawn here, at the
/// size the shell asks for and in the colour the taskbar needs, and drawn again
/// when either changes.
/// <para>
/// GDI draws text into a bitmap but does not write alpha, so the glyph is drawn
/// white on black with greyscale antialiasing and the grey level is taken as the
/// coverage. That coverage becomes the alpha of a premultiplied 32-bit icon,
/// which is what gives the edges the same softness as the shell's own glyphs.
/// Greyscale rather than ClearType, because ClearType's colour fringes are
/// tuned for text on an opaque background and turn into coloured halos once
/// the icon is composited onto the taskbar.
/// </para>
/// </remarks>
internal static class TrayGlyph
{
    /// <summary>Windows' own brightness glyph, as used in quick settings.</summary>
    public const char Brightness = '\uE706';

    /// <summary>A monitor.</summary>
    public const char Display = '\uE7F4';

    /// <summary>
    /// The size the notification area draws at, in pixels.
    /// </summary>
    /// <remarks>
    /// The small-icon metric at the system DPI, which is the DPI the shell draws
    /// the notification area at. An icon handed over at any other size is
    /// resampled, and a resampled 16-pixel glyph is a blurred one.
    /// </remarks>
    public static int Size => Math.Max(16, PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSMICON));

    /// <summary>Draws <paramref name="glyph"/> as an icon, or returns null when nothing drew.</summary>
    /// <param name="covered">How many pixels the glyph touched, for the log.</param>
    public static HICON Draw(char glyph, int size, bool darkTaskbar, out int covered)
    {
        // Segoe Fluent Icons is Windows 11's; MDL2 Assets carries the same code
        // points on Windows 10. GDI substitutes a missing face silently, so the
        // fallback is decided by whether anything was drawn at all.
        HICON icon = Draw(glyph, size, darkTaskbar, "Segoe Fluent Icons", out covered);
        if (covered > 0) return icon;

        if (!icon.IsNull) PInvoke.DestroyIcon(icon);
        return Draw(glyph, size, darkTaskbar, "Segoe MDL2 Assets", out covered);
    }

    private static unsafe HICON Draw(char glyph, int size, bool darkTaskbar, string face, out int covered)
    {
        covered = 0;

        HDC dc = PInvoke.CreateCompatibleDC(default);
        if (dc.IsNull) return default;

        HBITMAP colour = default, mask = default;
        HFONT font = default;

        try
        {
            BITMAPINFO info = default;
            info.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
            info.bmiHeader.biWidth = size;
            info.bmiHeader.biHeight = -size; // top-down, so row 0 is the top
            info.bmiHeader.biPlanes = 1;
            info.bmiHeader.biBitCount = 32;
            info.bmiHeader.biCompression = (uint)BI_COMPRESSION.BI_RGB;

            void* bits;
            colour = PInvoke.CreateDIBSection(dc, &info, DIB_USAGE.DIB_RGB_COLORS, &bits, default, 0);
            if (colour.IsNull || bits is null) return default;

            // A DIB section starts zeroed: black, which is "no coverage".
            HGDIOBJ previousBitmap = PInvoke.SelectObject(dc, (HGDIOBJ)colour.Value);

            // CreateFontIndirect, not CreateFont. CsWin32 declares CreateFont's
            // byte-sized charset with a four-byte marshalling, which the runtime
            // refuses the first time it is called: the tray thread died on it,
            // and took the icon with it. A struct has no marshalling to get wrong.
            LOGFONTW description = new()
            {
                // Negative height asks for the em size, which is how icon fonts
                // are designed: the glyph fills the em box at its intended size.
                lfHeight = -size,
                lfWeight = 400,
                lfCharSet = FONT_CHARSET.DEFAULT_CHARSET,
                lfOutPrecision = FONT_OUTPUT_PRECISION.OUT_TT_PRECIS,
                lfClipPrecision = FONT_CLIP_PRECISION.CLIP_DEFAULT_PRECIS,
                lfQuality = FONT_QUALITY.ANTIALIASED_QUALITY,
                lfFaceName = face,
            };

            font = PInvoke.CreateFontIndirect(&description);

            if (font.IsNull) return default;

            HGDIOBJ previousFont = PInvoke.SelectObject(dc, (HGDIOBJ)font.Value);
            _ = PInvoke.SetBkMode(dc, BACKGROUND_MODE.TRANSPARENT);
            _ = PInvoke.SetTextColor(dc, new COLORREF(0x00FFFFFF));

            RECT area = new() { left = 0, top = 0, right = size, bottom = size };
            char text = glyph;
            _ = PInvoke.DrawText(dc, &text, 1, &area,
                DRAW_TEXT_FORMAT.DT_CENTER | DRAW_TEXT_FORMAT.DT_VCENTER
                | DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_NOPREFIX);

            PInvoke.SelectObject(dc, previousFont);
            PInvoke.SelectObject(dc, previousBitmap);

            // Coverage to premultiplied BGRA in the taskbar's colour.
            byte tone = darkTaskbar ? (byte)0xFF : (byte)0x00;
            uint* pixel = (uint*)bits;
            for (int i = 0; i < size * size; i++)
            {
                uint p = pixel[i];
                byte b = (byte)p, g = (byte)(p >> 8), r = (byte)(p >> 16);
                byte a = Math.Max(r, Math.Max(g, b));

                if (a == 0) { pixel[i] = 0; continue; }
                covered++;

                byte c = (byte)(tone * a / 255);
                pixel[i] = ((uint)a << 24) | ((uint)c << 16) | ((uint)c << 8) | c;
            }

            // A 32-bit icon with alpha ignores its mask, but must still have one.
            // Zeroed, which means "draw the colour bitmap" everywhere.
            int stride = ((size + 15) / 16) * 2;
            byte[] zeros = new byte[stride * size];
            fixed (byte* m = zeros) mask = PInvoke.CreateBitmap(size, size, 1, 1, m);
            if (mask.IsNull) return default;

            ICONINFO icon = new() { fIcon = true, hbmMask = mask, hbmColor = colour };
            return PInvoke.CreateIconIndirect(&icon);
        }
        finally
        {
            // The icon holds its own copies of both bitmaps.
            if (!font.IsNull) PInvoke.DeleteObject((HGDIOBJ)font.Value);
            if (!colour.IsNull) PInvoke.DeleteObject((HGDIOBJ)colour.Value);
            if (!mask.IsNull) PInvoke.DeleteObject((HGDIOBJ)mask.Value);
            PInvoke.DeleteDC(dc);
        }
    }
}
