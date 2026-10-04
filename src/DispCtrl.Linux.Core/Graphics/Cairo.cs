using System.Runtime.InteropServices;

namespace DispCtrl.Linux.Graphics;

/// <summary>cairo and pango-cairo: what the overlays draw with. Every desktop
/// that runs GTK has both, so they cost nothing to depend on.</summary>
internal static unsafe class Cairo
{
    private const string Lib = "libcairo.so.2";
    private const string PangoCairo = "libpangocairo-1.0.so.0";
    private const string Pango = "libpango-1.0.so.0";
    private const string GObject = "libgobject-2.0.so.0";

    public const int FormatArgb32 = 0;
    public const int OperatorSource = 1, OperatorOver = 2;
    public const int PangoScale = 1024;
    public const int EllipsizeEnd = 3;
    public const int WeightNormal = 400, WeightSemibold = 600;
    public const int AlignLeft = 0, AlignCenter = 1;
    public const int FilterGood = 1;

    [DllImport(Lib)] public static extern nint cairo_image_surface_create(int format, int width, int height);
    [DllImport(Lib)] public static extern nint cairo_xlib_surface_create(nint display, ulong drawable, nint visual, int width, int height);
    [DllImport(Lib)] public static extern void cairo_surface_flush(nint surface);
    [DllImport(Lib)] public static extern void cairo_surface_destroy(nint surface);
    [DllImport(Lib)] public static extern int cairo_surface_status(nint surface);
    [DllImport(Lib)] public static extern int cairo_surface_write_to_png(nint surface, [MarshalAs(UnmanagedType.LPUTF8Str)] string filename);
    [DllImport(Lib)] public static extern nint cairo_create(nint surface);
    [DllImport(Lib)] public static extern void cairo_destroy(nint cr);
    [DllImport(Lib)] public static extern void cairo_save(nint cr);
    [DllImport(Lib)] public static extern void cairo_restore(nint cr);
    [DllImport(Lib)] public static extern void cairo_set_operator(nint cr, int op);
    [DllImport(Lib)] public static extern void cairo_set_source_rgba(nint cr, double r, double g, double b, double a);
    [DllImport(Lib)] public static extern void cairo_set_source_surface(nint cr, nint surface, double x, double y);
    [DllImport(Lib)] public static extern nint cairo_get_source(nint cr);
    [DllImport(Lib)] public static extern void cairo_pattern_set_filter(nint pattern, int filter);
    [DllImport(Lib)] public static extern void cairo_paint(nint cr);
    [DllImport(Lib)] public static extern void cairo_paint_with_alpha(nint cr, double alpha);
    [DllImport(Lib)] public static extern void cairo_fill(nint cr);
    [DllImport(Lib)] public static extern void cairo_fill_preserve(nint cr);
    [DllImport(Lib)] public static extern void cairo_stroke(nint cr);
    [DllImport(Lib)] public static extern void cairo_clip(nint cr);
    [DllImport(Lib)] public static extern void cairo_set_line_width(nint cr, double width);
    [DllImport(Lib)] public static extern void cairo_new_path(nint cr);
    [DllImport(Lib)] public static extern void cairo_new_sub_path(nint cr);
    [DllImport(Lib)] public static extern void cairo_close_path(nint cr);
    [DllImport(Lib)] public static extern void cairo_rectangle(nint cr, double x, double y, double w, double h);
    [DllImport(Lib)] public static extern void cairo_arc(nint cr, double xc, double yc, double radius, double angle1, double angle2);
    [DllImport(Lib)] public static extern void cairo_move_to(nint cr, double x, double y);
    [DllImport(Lib)] public static extern void cairo_translate(nint cr, double tx, double ty);
    [DllImport(Lib)] public static extern void cairo_scale(nint cr, double sx, double sy);

    [DllImport(PangoCairo)] public static extern nint pango_cairo_create_layout(nint cr);
    [DllImport(PangoCairo)] public static extern void pango_cairo_show_layout(nint cr, nint layout);
    [DllImport(Pango)] public static extern nint pango_font_description_from_string([MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    [DllImport(Pango)] public static extern void pango_font_description_free(nint desc);
    [DllImport(Pango)] public static extern void pango_font_description_set_weight(nint desc, int weight);
    [DllImport(Pango)] public static extern void pango_font_description_set_absolute_size(nint desc, double size);
    [DllImport(Pango)] public static extern void pango_layout_set_font_description(nint layout, nint desc);
    [DllImport(Pango)] public static extern void pango_layout_set_text(nint layout, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, int length);
    [DllImport(Pango)] public static extern void pango_layout_set_width(nint layout, int width);
    [DllImport(Pango)] public static extern void pango_layout_set_ellipsize(nint layout, int mode);
    [DllImport(Pango)] public static extern void pango_layout_set_alignment(nint layout, int alignment);
    [DllImport(Pango)] public static extern void pango_layout_get_pixel_size(nint layout, out int width, out int height);
    [DllImport(GObject)] public static extern void g_object_unref(nint obj);
}

/// <summary>A cairo context with the few shapes the overlays use.</summary>
public sealed class Canvas : IDisposable
{
    private readonly nint _cr;
    private readonly nint _surface;
    private readonly bool _ownsSurface;

    private Canvas(nint surface, bool ownsSurface)
    {
        _surface = surface;
        _ownsSurface = ownsSurface;
        _cr = Cairo.cairo_create(surface);
    }

    internal nint Handle => _cr;

    /// <summary>An offscreen ARGB image: for tests and for rendering checks.</summary>
    public static Canvas Image(int width, int height) =>
        new(Cairo.cairo_image_surface_create(Cairo.FormatArgb32, width, height), true);

    internal static Canvas OnXlib(nint display, ulong drawable, nint visual, int width, int height) =>
        new(Cairo.cairo_xlib_surface_create(display, drawable, visual, width, height), true);

    /// <summary>Copies <paramref name="source"/> over this canvas in one paint:
    /// frames are drawn offscreen first, so a window never shows half of one.</summary>
    public void Blit(Canvas source)
    {
        Cairo.cairo_surface_flush(source._surface);
        Cairo.cairo_save(_cr);
        Cairo.cairo_set_operator(_cr, Cairo.OperatorSource);
        Cairo.cairo_set_source_surface(_cr, source._surface, 0, 0);
        Cairo.cairo_paint(_cr);
        Cairo.cairo_restore(_cr);
        Cairo.cairo_surface_flush(_surface);
    }

    public void SavePng(string path)
    {
        Cairo.cairo_surface_flush(_surface);
        Cairo.cairo_surface_write_to_png(_surface, path);
    }

    /// <summary>Everything transparent: the starting point of every frame.</summary>
    public void Clear()
    {
        Cairo.cairo_save(_cr);
        Cairo.cairo_set_operator(_cr, Cairo.OperatorSource);
        Cairo.cairo_set_source_rgba(_cr, 0, 0, 0, 0);
        Cairo.cairo_paint(_cr);
        Cairo.cairo_restore(_cr);
    }

    public void RoundedRect(double x, double y, double w, double h, double radius)
    {
        radius = Math.Max(0, Math.Min(radius, Math.Min(w, h) / 2));
        Cairo.cairo_new_sub_path(_cr);
        Cairo.cairo_arc(_cr, x + w - radius, y + radius, radius, -Math.PI / 2, 0);
        Cairo.cairo_arc(_cr, x + w - radius, y + h - radius, radius, 0, Math.PI / 2);
        Cairo.cairo_arc(_cr, x + radius, y + h - radius, radius, Math.PI / 2, Math.PI);
        Cairo.cairo_arc(_cr, x + radius, y + radius, radius, Math.PI, 3 * Math.PI / 2);
        Cairo.cairo_close_path(_cr);
    }

    public void Fill(Rgba c)
    {
        Cairo.cairo_set_source_rgba(_cr, c.R, c.G, c.B, c.A);
        Cairo.cairo_fill(_cr);
    }

    public void FillPreserve(Rgba c)
    {
        Cairo.cairo_set_source_rgba(_cr, c.R, c.G, c.B, c.A);
        Cairo.cairo_fill_preserve(_cr);
    }

    public void Stroke(Rgba c, double width)
    {
        Cairo.cairo_set_source_rgba(_cr, c.R, c.G, c.B, c.A);
        Cairo.cairo_set_line_width(_cr, width);
        Cairo.cairo_stroke(_cr);
    }

    /// <summary>A soft shadow under a rounded rectangle, from stacked translucent
    /// outlines: cairo has no blur, and a few layers read as one at this size.</summary>
    public void Shadow(double x, double y, double w, double h, double radius, double spread, double alpha)
    {
        int steps = (int)Math.Max(1, spread);
        for (int i = steps; i >= 1; i--)
        {
            RoundedRect(x - i, y - i + spread / 3, w + 2 * i, h + 2 * i, radius + i);
            Fill(new Rgba(0, 0, 0, alpha / steps));
        }
    }

    /// <summary>Text in one line, ellipsized to <paramref name="maxWidth"/>.
    /// Returns its height.</summary>
    public int Text(string text, double x, double y, double maxWidth, Rgba colour, string font, bool semibold = false, bool centre = false)
    {
        var layout = Cairo.pango_cairo_create_layout(_cr);
        var desc = Cairo.pango_font_description_from_string(font);
        try
        {
            if (semibold) Cairo.pango_font_description_set_weight(desc, Cairo.WeightSemibold);
            Cairo.pango_layout_set_font_description(layout, desc);
            Cairo.pango_layout_set_text(layout, text, -1);
            Cairo.pango_layout_set_width(layout, (int)(maxWidth * Cairo.PangoScale));
            Cairo.pango_layout_set_ellipsize(layout, Cairo.EllipsizeEnd);
            Cairo.pango_layout_set_alignment(layout, centre ? Cairo.AlignCenter : Cairo.AlignLeft);
            Cairo.pango_layout_get_pixel_size(layout, out _, out int height);
            Cairo.cairo_set_source_rgba(_cr, colour.R, colour.G, colour.B, colour.A);
            Cairo.cairo_move_to(_cr, x, y);
            Cairo.pango_cairo_show_layout(_cr, layout);
            return height;
        }
        finally
        {
            Cairo.pango_font_description_free(desc);
            Cairo.g_object_unref(layout);
        }
    }

    /// <summary>Paints another surface (a window's picture) scaled into a rounded
    /// rectangle.</summary>
    internal void Picture(nint surface, double srcX, double srcY, double srcW, double srcH, double x, double y, double w, double h, double radius)
    {
        Cairo.cairo_save(_cr);
        RoundedRect(x, y, w, h, radius);
        Cairo.cairo_clip(_cr);
        double scale = Math.Min(w / srcW, h / srcH);
        double dw = srcW * scale, dh = srcH * scale;
        Cairo.cairo_translate(_cr, x + (w - dw) / 2, y + (h - dh) / 2);
        Cairo.cairo_scale(_cr, scale, scale);
        Cairo.cairo_set_source_surface(_cr, surface, -srcX, -srcY);
        Cairo.cairo_pattern_set_filter(Cairo.cairo_get_source(_cr), Cairo.FilterGood);
        Cairo.cairo_paint(_cr);
        Cairo.cairo_restore(_cr);
    }

    public void Dispose()
    {
        Cairo.cairo_destroy(_cr);
        if (_ownsSurface) Cairo.cairo_surface_destroy(_surface);
    }
}

/// <summary>A colour with alpha, components 0 to 1.</summary>
public readonly record struct Rgba(double R, double G, double B, double A = 1)
{
    public static Rgba Hex(string hex, double alpha = 1)
    {
        hex = hex.TrimStart('#');
        int v = Convert.ToInt32(hex, 16);
        return new Rgba(((v >> 16) & 0xFF) / 255.0, ((v >> 8) & 0xFF) / 255.0, (v & 0xFF) / 255.0, alpha);
    }

    public Rgba WithAlpha(double alpha) => this with { A = alpha };
}
