namespace DisplCtrl.Core.Displays;

/// <summary>Integer screen rectangle in physical (DPI-aware) pixels.</summary>
public readonly record struct DisplayRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
    public override string ToString() => $"{Left},{Top} {Width}x{Height}";
}

/// <summary>How a display is physically attached.</summary>
/// <remarks>
/// Drives backend selection rather than being cosmetic: an
/// <see cref="Internal"/> panel has no DDC/CI, so its brightness has to go
/// through WMI, while everything else is controlled over DDC/CI.
/// </remarks>
public enum ConnectorKind
{
    Unknown,
    Internal,
    Hdmi,
    DisplayPort,
    Dvi,
    Vga,
    Usb,
    Virtual,
}

/// <summary>
/// A live snapshot of one attached display. Cheap to rebuild — treat every
/// instance as valid only for the current monitor layout.
/// </summary>
/// <remarks>
/// <see cref="Handle"/> is deliberately excluded from the record's equality:
/// an HMONITOR is a transient token that Windows recycles across layout
/// changes, so two snapshots of the same panel must still compare equal.
/// </remarks>
public sealed record DisplayInfo
{
    public required DisplayKey Key { get; init; }

    /// <summary>GDI name, e.g. <c>\\.\DISPLAY1</c>. Transient — never persist it.</summary>
    public required string GdiName { get; init; }

    /// <summary>e.g. "DELL U2424H". Blank on panels that report no name.</summary>
    public required string FriendlyName { get; init; }

    public required ConnectorKind Connector { get; init; }
    public bool IsInternal => Connector == ConnectorKind.Internal;
    public required bool IsPrimary { get; init; }

    public required DisplayRect Bounds { get; init; }

    /// <summary>
    /// Bounds minus whatever the shell has reserved (taskbar, appbars). DisplCtrl
    /// rewrites this per monitor when it parks a secondary taskbar.
    /// </summary>
    public required DisplayRect WorkArea { get; init; }

    public required uint RefreshHz { get; init; }

    /// <summary>
    /// How the desktop is rotated on this panel: 0, 90, 180 or 270.
    /// </summary>
    /// <remarks>
    /// Read from the mode rather than inferred from the aspect ratio, which
    /// cannot tell a rotated 16:9 panel from a native portrait one.
    /// </remarks>
    public int OrientationDegrees { get; init; }
    public required uint BitsPerPixel { get; init; }

    /// <summary>Physical panel width in millimetres; 0 when the EDID does not say.</summary>
    public int PhysicalWidthMm { get; init; }

    /// <summary>Physical panel height in millimetres; 0 when unknown.</summary>
    public int PhysicalHeightMm { get; init; }

    public bool HasPhysicalSize => PhysicalWidthMm > 0 && PhysicalHeightMm > 0;

    /// <summary>Diagonal in inches, as a monitor is normally described.</summary>
    public double DiagonalInches => HasPhysicalSize
        ? Math.Sqrt((PhysicalWidthMm * (double)PhysicalWidthMm)
                  + (PhysicalHeightMm * (double)PhysicalHeightMm)) / 25.4
        : 0;

    /// <summary>
    /// True pixel density, as opposed to the scaling factor.
    /// </summary>
    /// <remarks>
    /// <see cref="Dpi"/> is what Windows renders at; this is what the panel
    /// physically is. They are routinely very different — a 2880x1800 laptop
    /// panel is around 240 real PPI while Windows renders it at 192.
    /// </remarks>
    public double PhysicalPpi => HasPhysicalSize
        ? Bounds.Width / (PhysicalWidthMm / 25.4)
        : 0;

    /// <summary>Effective DPI; 96 is 100% scaling, 192 is 200%.</summary>
    public required uint Dpi { get; init; }
    public double Scale => Dpi / 96.0;

    /// <summary>HMONITOR. Valid only until the next display change.</summary>
    public nint Handle { get; init; }

    /// <summary>
    /// Settings key for this display, computed once.
    /// </summary>
    /// <remarks>
    /// Cached here rather than on <see cref="DisplayKey"/>: a record's
    /// generated equality compares every instance field, so a lazily-populated
    /// cache field inside the record would make two otherwise-identical keys
    /// compare unequal depending on whether the token had been asked for yet.
    /// <see cref="DisplayInfo"/> overrides equality explicitly, so it is safe here.
    /// </remarks>
    public string Token => _token ??= Key.ToToken();
    private string? _token;

    /// <summary>Display name for logs and UI, falling back when EDID has no name.</summary>
    public string Label => string.IsNullOrWhiteSpace(FriendlyName)
        ? $"{Connector} {Bounds.Width}x{Bounds.Height}"
        : FriendlyName;

    public bool Equals(DisplayInfo? other) => other is not null && Key == other.Key;
    public override int GetHashCode() => Key.GetHashCode();
}
