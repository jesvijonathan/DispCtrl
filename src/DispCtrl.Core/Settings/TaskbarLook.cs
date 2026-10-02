using System.Globalization;

namespace DispCtrl.Core.Settings;

/// <summary>What the taskbar's surface is when taskbar glass is on.</summary>
/// <remarks>Stored by name; the numbers are what the Explorer helper is sent.</remarks>
public enum TaskbarLook
{
    /// <summary>What is behind, blurred, under the tint.</summary>
    Blur = 0,
    /// <summary>What is behind, sharp, under the tint.</summary>
    Clear = 1,
    /// <summary>The tint's colour, solid.</summary>
    Opaque = 2,
    /// <summary>Blurred and more saturated, as Windows' own acrylic material.</summary>
    Acrylic = 3,
}

/// <summary>The one number the Explorer helper is configured with.</summary>
/// <remarks>
/// Bits 0-7 blur radius, 8-15 tint opacity, 24 on, 25-27 the look, 28 hide
/// the top border, 29 a colour follows, 32-55 the colour as 0xRRGGBB. The
/// helper reads exactly this; changing it changes the helper's revision too.
/// </remarks>
public static class TaskbarGlass
{
    public static ulong Pack(int radius, int tint, TaskbarLook look, bool border, int? rgb)
    {
        ulong config = 0x01000000ul | (ulong)Math.Clamp(radius, 0, 100) | ((ulong)Math.Clamp(tint, 0, 100) << 8)
            | ((ulong)((int)look & 0x7) << 25);
        if (!border) config |= 1ul << 28;
        if (rgb is int colour) config |= (1ul << 29) | ((ulong)(colour & 0xFFFFFF) << 32);
        return config;
    }

    /// <summary>A colour written #RRGGBB or RRGGBB, as 0xRRGGBB; null when it is not one.</summary>
    public static int? ParseColour(string? text)
    {
        string t = (text ?? "").Trim().TrimStart('#');
        return t.Length == 6 && int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb) ? rgb : null;
    }

    /// <summary>Windows' accent colour, as 0xRRGGBB; null when it cannot be read.</summary>
    /// <remarks>DWM keeps it as 0xAABBGGRR.</remarks>
    public static int? AccentColour()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is not int abgr) return null;
            int r = abgr & 0xFF, g = (abgr >> 8) & 0xFF, b = (abgr >> 16) & 0xFF;
            return (r << 16) | (g << 8) | b;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }
}
