namespace DisplCtrl.Core.Displays;

/// <summary>A colour primary, as the EDID records it.</summary>
/// <remarks>
/// CIE 1931 xy, stored as ten-bit fractions. Three decimal places is all the
/// precision there is; printing more would invent it.
/// </remarks>
public readonly record struct Chromaticity(double X, double Y)
{
    public bool Known => X > 0 || Y > 0;

    public override string ToString() => Known ? $"{X:0.000}, {Y:0.000}" : "not given";
}

/// <summary>
/// Everything the base EDID block says about a panel.
/// </summary>
/// <remarks>
/// The EDID is the one place a monitor describes <em>itself</em> rather than
/// being described by Windows: when it was made, what it is made of, what it
/// will accept on its cable, and which primaries its glass actually has. None of
/// it appears anywhere in Windows' own display settings, and several fields are
/// the only answer to a question that otherwise cannot be asked — a panel's real
/// colour primaries, or whether it is an old unit of a model that was revised.
/// <para>
/// Read to the letter of E-EDID 1.4 and reported as read. Where a field is
/// absent, or a monitor has written something the standard does not define, that
/// is what is said: a display database is only worth having if a blank in it
/// means the panel was blank.
/// </para>
/// </remarks>
public sealed record EdidDetails
{
    /// <summary>False when no EDID could be read at all.</summary>
    public bool Present { get; init; }

    // ------------------------------------------------------------ identity --

    /// <summary>Three-letter PNP code, e.g. <c>DEL</c>.</summary>
    public string ManufacturerCode { get; init; } = "";

    /// <summary>The company behind that code, where DisplCtrl knows it.</summary>
    /// <remarks>
    /// From a table, because the code is an allocation and nothing in the EDID
    /// expands it. The list here covers display makers; an unknown code is
    /// reported as unknown rather than guessed at.
    /// </remarks>
    public string ManufacturerName { get; init; } = "";

    /// <summary>Product code, four hex digits, e.g. <c>A234</c>.</summary>
    public string ProductCode { get; init; } = "";

    /// <summary>Descriptor 0xFC, the name the panel gives itself.</summary>
    public string? MonitorName { get; init; }

    /// <summary>Descriptor 0xFE, free text — often a panel part number.</summary>
    public string? FreeText { get; init; }

    // ----------------------------------------------------------------- age --

    /// <summary>Week of manufacture, 1-54, or 0 when not given.</summary>
    public int Week { get; init; }

    /// <summary>Year of manufacture, or the model year when <see cref="IsModelYear"/>.</summary>
    public int Year { get; init; }

    /// <summary>
    /// True when the panel gave a model year rather than a build date.
    /// </summary>
    /// <remarks>
    /// EDID 1.4 signals this with week 0xFF. The distinction is worth keeping:
    /// a model year says when the design was current, a build week says when
    /// this particular unit came off the line.
    /// </remarks>
    public bool IsModelYear { get; init; }

    public string Made => Year == 0
        ? "not given"
        : IsModelYear ? $"model year {Year}"
        : Week > 0 ? $"week {Week} of {Year}"
        : $"{Year}";

    /// <summary>EDID structure version, e.g. <c>1.4</c>.</summary>
    public string Version { get; init; } = "";

    // --------------------------------------------------------------- input --

    public bool Digital { get; init; }

    /// <summary>Bits per colour on the wire, or 0 when the panel does not say.</summary>
    public int BitsPerColour { get; init; }

    /// <summary>The digital interface the panel declares, e.g. DisplayPort.</summary>
    /// <remarks>
    /// What the <em>panel</em> says it is wired for, which is not always what it
    /// is plugged into today: a monitor with both HDMI and DisplayPort declares
    /// one of them here.
    /// </remarks>
    public string Interface { get; init; } = "";

    /// <summary>For an analogue panel, its signal level and sync support.</summary>
    public string AnalogueSignal { get; init; } = "";

    // ------------------------------------------------------------- picture --

    /// <summary>Image size in centimetres, as the basic block gives it.</summary>
    public int WidthCm { get; init; }

    public int HeightCm { get; init; }

    /// <summary>Aspect ratio, when the panel gives one instead of a size.</summary>
    public string AspectRatio { get; init; } = "";

    /// <summary>Display gamma, or 0 when deferred to a descriptor.</summary>
    public double Gamma { get; init; }

    public bool Standby { get; init; }

    public bool Suspend { get; init; }

    public bool ActiveOff { get; init; }

    /// <summary>What the panel accepts, e.g. <c>RGB 4:4:4, YCrCb 4:2:2</c>.</summary>
    public string ColourEncodings { get; init; } = "";

    /// <summary>True when sRGB is the panel's declared default colour space.</summary>
    public bool SrgbDefault { get; init; }

    /// <summary>True when the first detailed timing is the panel's native mode.</summary>
    public bool PreferredTimingIsNative { get; init; }

    /// <summary>True when the panel will accept rates it was not explicitly given.</summary>
    public bool ContinuousFrequency { get; init; }

    // -------------------------------------------------------------- colour --

    public Chromaticity Red { get; init; }

    public Chromaticity Green { get; init; }

    public Chromaticity Blue { get; init; }

    public Chromaticity White { get; init; }

    // -------------------------------------------------------------- timing --

    /// <summary>The pre-VESA modes in bytes 35-37, in words.</summary>
    public IReadOnlyList<string> EstablishedTimings { get; init; } = [];

    /// <summary>The eight standard timing slots, where filled.</summary>
    public IReadOnlyList<string> StandardTimings { get; init; } = [];

    /// <summary>Full detail for each detailed timing descriptor.</summary>
    public IReadOnlyList<string> DetailedTimings { get; init; } = [];

    /// <summary>Which of the four descriptor blocks hold what.</summary>
    public IReadOnlyList<string> Descriptors { get; init; } = [];

    /// <summary>Vertical range from descriptor 0xFD, in Hz.</summary>
    public uint MinVerticalHz { get; init; }

    public uint MaxVerticalHz { get; init; }

    /// <summary>Horizontal range from the same descriptor, in kHz.</summary>
    public uint MinHorizontalKHz { get; init; }

    public uint MaxHorizontalKHz { get; init; }

    /// <summary>Highest pixel clock the panel will take, in MHz. 0 when not given.</summary>
    public uint MaxPixelClockMHz { get; init; }

    // --------------------------------------------------------------- block --

    /// <summary>How many extension blocks follow the 128-byte base.</summary>
    /// <remarks>
    /// A CTA-861 extension is where a modern monitor keeps its HDR metadata,
    /// audio support and the video modes a television understands. Counting
    /// them says whether there is more to be had.
    /// </remarks>
    public int Extensions { get; init; }

    /// <summary>Whether the base block's bytes sum to zero, as they must.</summary>
    /// <remarks>
    /// A failing checksum does not stop anything here being read, and it is
    /// worth reporting rather than hiding: it usually means a cable or an
    /// adapter is mangling the read, which explains a great many other oddities.
    /// </remarks>
    public bool ChecksumValid { get; init; }

    /// <summary>Size of the blob Windows had cached, in bytes.</summary>
    public int Bytes { get; init; }

    public static EdidDetails None => new() { Present = false };
}
