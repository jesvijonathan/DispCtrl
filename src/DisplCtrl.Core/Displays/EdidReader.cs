namespace DisplCtrl.Core.Displays;

/// <summary>
/// Decodes the whole base EDID block, rather than the two fields needed to
/// identify a panel.
/// </summary>
/// <remarks>
/// <see cref="Edid"/> reads what DisplCtrl needs to tell two monitors apart and
/// stops there, because it runs on the engine's rescan path and must be cheap.
/// This is the other job: everything the panel says about itself, for the report
/// and for a device record. It is called when someone asks, never on a timer.
/// <para>
/// Layout per E-EDID release A, revision 2 (the 1.4 base block). Byte offsets
/// are given against that document, because a magic number here is unreadable
/// without one.
/// </para>
/// </remarks>
public static class EdidReader
{
    /// <summary>Parses the cached EDID for a display.</summary>
    public static EdidDetails Describe(string devicePath)
    {
        byte[]? e = Edid.Raw(devicePath);
        return e is null || e.Length < 128 ? EdidDetails.None : Parse(e);
    }

    /// <summary>Parses a blob directly, for checks that supply their own.</summary>
    public static EdidDetails Parse(byte[] e)
    {
        if (e.Length < 128) return EdidDetails.None;

        (int week, int year, bool modelYear) = Made(e);
        (bool digital, int bits, string face, string analogue) = Input(e, e[18], e[19]);
        (int widthCm, int heightCm, string aspect) = Size(e);
        var descriptors = new List<string>();
        var detailed = new List<string>();

        Range range = Descriptors(e, descriptors, detailed);

        return new EdidDetails
        {
            Present = true,
            Bytes = e.Length,

            ManufacturerCode = Manufacturer(e),
            ManufacturerName = PnpNames.For(Manufacturer(e)),
            ProductCode = $"{(ushort)(e[10] | (e[11] << 8)):X4}",
            MonitorName = String(e, 0xFC),
            FreeText = String(e, 0xFE),

            Week = week,
            Year = year,
            IsModelYear = modelYear,
            Version = $"{e[18]}.{e[19]}",

            Digital = digital,
            BitsPerColour = bits,
            Interface = face,
            AnalogueSignal = analogue,

            WidthCm = widthCm,
            HeightCm = heightCm,
            AspectRatio = aspect,

            // Byte 23 is (gamma x 100) - 100; 0xFF defers it to a descriptor.
            Gamma = e[23] == 0xFF ? 0 : (e[23] + 100) / 100.0,

            Standby = (e[24] & 0x80) != 0,
            Suspend = (e[24] & 0x40) != 0,
            ActiveOff = (e[24] & 0x20) != 0,
            ColourEncodings = Encodings(e, digital),
            SrgbDefault = (e[24] & 0x04) != 0,
            PreferredTimingIsNative = (e[24] & 0x02) != 0,
            ContinuousFrequency = (e[24] & 0x01) != 0,

            Red = Point(e, 27, 6, 4),
            Green = Point(e, 29, 2, 0),
            Blue = Point(e, 31, 6, 4),
            White = Point(e, 33, 2, 0),

            EstablishedTimings = Established(e),
            StandardTimings = Standard(e),
            DetailedTimings = detailed,
            Descriptors = descriptors,

            MinVerticalHz = range.MinV,
            MaxVerticalHz = range.MaxV,
            MinHorizontalKHz = range.MinH,
            MaxHorizontalKHz = range.MaxH,
            MaxPixelClockMHz = range.Clock,

            Extensions = e[126],
            ChecksumValid = Sum(e) == 0,
        };
    }

    private readonly record struct Range(uint MinV, uint MaxV, uint MinH, uint MaxH, uint Clock);

    /// <summary>Bytes 8-9 pack three letters as five bits each, with 1 = 'A'.</summary>
    private static string Manufacturer(byte[] e)
    {
        int v = (e[8] << 8) | e[9];
        Span<char> c =
        [
            (char)('A' - 1 + ((v >> 10) & 0x1F)),
            (char)('A' - 1 + ((v >> 5) & 0x1F)),
            (char)('A' - 1 + (v & 0x1F)),
        ];

        foreach (char ch in c)
            if (ch is < 'A' or > 'Z') return "";

        return new string(c);
    }

    /// <summary>Bytes 16-17: week, and year less 1990.</summary>
    private static (int Week, int Year, bool ModelYear) Made(byte[] e)
    {
        int year = e[17] == 0 ? 0 : e[17] + 1990;

        // Week 0xFF means byte 17 is a model year, not a build year.
        if (e[16] == 0xFF) return (0, year, true);

        return (e[16] is >= 1 and <= 54 ? e[16] : 0, year, false);
    }

    /// <summary>
    /// Byte 20, whose meaning depends on its top bit <em>and</em> on the version.
    /// </summary>
    /// <remarks>
    /// The bit depth and interface sub-fields only exist from EDID 1.4. Reading
    /// them off a 1.3 panel yields whatever the reserved bits happen to hold,
    /// which is how this Dell first came out as "0-bit, not declared" — a claim
    /// about the monitor that the monitor never made.
    /// </remarks>
    private static (bool Digital, int Bits, string Interface, string Analogue) Input(byte[] e, int major, int minor)
    {
        byte b = e[20];
        bool describesLink = major > 1 || (major == 1 && minor >= 4);

        if ((b & 0x80) == 0)
        {
            // Analogue. The levels are the signal swing either side of blank.
            string levels = ((b >> 5) & 0x03) switch
            {
                0 => "0.700 / 0.300 V",
                1 => "0.714 / 0.286 V",
                2 => "1.000 / 0.400 V",
                _ => "0.700 / 0.000 V",
            };

            var syncs = new List<string>();
            if ((b & 0x10) != 0) syncs.Add("blank-to-black setup");
            if ((b & 0x08) != 0) syncs.Add("separate sync");
            if ((b & 0x04) != 0) syncs.Add("composite sync");
            if ((b & 0x02) != 0) syncs.Add("sync on green");
            if ((b & 0x01) != 0) syncs.Add("serration");

            string sync = syncs.Count > 0 ? $", {string.Join(", ", syncs)}" : "";
            return (false, 0, "", $"{levels}{sync}");
        }

        // EDID 1.3 says only "digital", and adds one bit for DFP 1.x.
        if (!describesLink)
            return (true, 0, (b & 0x01) != 0 ? "DFP 1.x compatible" : "", "");

        int bits = ((b >> 4) & 0x07) switch
        {
            1 => 6, 2 => 8, 3 => 10, 4 => 12, 5 => 14, 6 => 16, _ => 0,
        };

        string face = (b & 0x0F) switch
        {
            1 => "DVI",
            2 => "HDMI-a",
            3 => "HDMI-b",
            4 => "MDDI",
            5 => "DisplayPort",
            _ => "not declared",
        };

        return (true, bits, face, "");
    }

    /// <summary>
    /// Bytes 21-22, which are a size in centimetres <em>or</em> an aspect ratio.
    /// </summary>
    /// <remarks>
    /// EDID 1.4 reuses the pair: one of them zero means the other is a landscape
    /// or portrait aspect, and both zero means the panel declines to say. A
    /// projector is the usual reason.
    /// </remarks>
    private static (int WidthCm, int HeightCm, string Aspect) Size(byte[] e)
    {
        int w = e[21], h = e[22];

        if (w > 0 && h > 0) return (w, h, "");
        if (w == 0 && h == 0) return (0, 0, "");

        // The stored byte is (ratio x 100) - 99, landscape in 21, portrait in 22.
        double ratio = ((w > 0 ? w : h) + 99) / 100.0;
        string shape = w > 0 ? $"{ratio:0.00} : 1" : $"1 : {ratio:0.00}";

        return (0, 0, shape);
    }

    private static string Encodings(byte[] e, bool digital)
    {
        // Bits 4-3 of byte 24 mean different things either side of the divide,
        // and reading the analogue meaning off a digital panel is how a monitor
        // ends up described as monochrome.
        if (!digital)
        {
            return ((e[24] >> 3) & 0x03) switch
            {
                0 => "monochrome or greyscale",
                1 => "RGB colour",
                2 => "non-RGB colour",
                _ => "not defined",
            };
        }

        var kinds = new List<string> { "RGB 4:4:4" };
        if ((e[24] & 0x10) != 0) kinds.Add("YCrCb 4:4:4");
        if ((e[24] & 0x08) != 0) kinds.Add("YCrCb 4:2:2");

        return string.Join(", ", kinds);
    }

    /// <summary>
    /// One CIE primary out of bytes 25-34.
    /// </summary>
    /// <remarks>
    /// Ten bits each, split awkwardly: the top eight live in their own byte and
    /// the bottom two are packed two-per-primary into bytes 25 and 26.
    /// </remarks>
    private static Chromaticity Point(byte[] e, int high, int xShift, int yShift)
    {
        int packed = high < 31 ? e[25] : e[26];

        int x = (e[high] << 2) | ((packed >> xShift) & 0x03);
        int y = (e[high + 1] << 2) | ((packed >> yShift) & 0x03);

        return new Chromaticity(x / 1024.0, y / 1024.0);
    }

    /// <summary>Bytes 35-37, a bitmap of modes that predate the standard timings.</summary>
    private static List<string> Established(byte[] e)
    {
        var modes = new List<string>();

        void Bit(int offset, int bit, string mode)
        {
            if ((e[offset] & (1 << bit)) != 0) modes.Add(mode);
        }

        Bit(35, 7, "720 x 400 @ 70 Hz");
        Bit(35, 6, "720 x 400 @ 88 Hz");
        Bit(35, 5, "640 x 480 @ 60 Hz");
        Bit(35, 4, "640 x 480 @ 67 Hz");
        Bit(35, 3, "640 x 480 @ 72 Hz");
        Bit(35, 2, "640 x 480 @ 75 Hz");
        Bit(35, 1, "800 x 600 @ 56 Hz");
        Bit(35, 0, "800 x 600 @ 60 Hz");

        Bit(36, 7, "800 x 600 @ 72 Hz");
        Bit(36, 6, "800 x 600 @ 75 Hz");
        Bit(36, 5, "832 x 624 @ 75 Hz");
        Bit(36, 4, "1024 x 768 @ 87 Hz interlaced");
        Bit(36, 3, "1024 x 768 @ 60 Hz");
        Bit(36, 2, "1024 x 768 @ 70 Hz");
        Bit(36, 1, "1024 x 768 @ 75 Hz");
        Bit(36, 0, "1280 x 1024 @ 75 Hz");

        Bit(37, 7, "1152 x 870 @ 75 Hz");

        return modes;
    }

    /// <summary>Bytes 38-53: eight two-byte slots, unused ones filled with 0x01.</summary>
    private static List<string> Standard(byte[] e)
    {
        var modes = new List<string>();

        for (int off = 38; off <= 52; off += 2)
        {
            if (e[off] == 0x01 && e[off + 1] == 0x01) continue;
            if (e[off] == 0x00) continue;

            int width = (e[off] + 31) * 8;
            int refresh = (e[off + 1] & 0x3F) + 60;

            // The ratio is stored, not the height, so the height is derived.
            (int num, int den, string shape) = ((e[off + 1] >> 6) & 0x03) switch
            {
                0 => (16, 10, "16:10"),
                1 => (4, 3, "4:3"),
                2 => (5, 4, "5:4"),
                _ => (16, 9, "16:9"),
            };

            modes.Add($"{width} x {width * den / num} @ {refresh} Hz  ({shape})");
        }

        return modes;
    }

    /// <summary>
    /// Walks the four 18-byte descriptor blocks at bytes 54-125.
    /// </summary>
    /// <remarks>
    /// A block is a detailed timing when its first two bytes are a non-zero
    /// pixel clock, and a display descriptor otherwise — in which case byte 3 is
    /// the tag saying which. Both are recorded: the timings are what the panel
    /// really runs at, and which tags are present is itself informative.
    /// </remarks>
    private static Range Descriptors(byte[] e, List<string> descriptors, List<string> detailed)
    {
        var range = new Range();

        for (int off = 54; off + 18 <= 126; off += 18)
        {
            bool isTiming = e[off] != 0 || e[off + 1] != 0;

            if (isTiming)
            {
                descriptors.Add("detailed timing");
                detailed.Add(Timing(e, off, detailed.Count == 0));
                continue;
            }

            byte tag = e[off + 3];

            descriptors.Add(tag switch
            {
                0xFF => "serial number (0xFF)",
                0xFE => "free text (0xFE)",
                0xFD => "range limits (0xFD)",
                0xFC => "monitor name (0xFC)",
                0xFB => "additional white point (0xFB)",
                0xFA => "more standard timings (0xFA)",
                0xF9 => "colour management data (0xF9)",
                0xF8 => "CVT timing codes (0xF8)",
                0xF7 => "established timings III (0xF7)",
                0x10 => "unused",
                _ => $"manufacturer defined (0x{tag:X2})",
            });

            if (tag != 0xFD) continue;

            byte flags = e[off + 4];

            uint minV = e[off + 5];
            uint maxV = e[off + 6];
            uint minH = e[off + 7];
            uint maxH = e[off + 8];

            // Byte 4 flags offsets that carry the range past 255.
            if ((flags & 0x01) != 0) maxV += 255;
            if ((flags & 0x02) != 0) { minV += 255; maxV += 255; }
            if ((flags & 0x04) != 0) maxH += 255;
            if ((flags & 0x08) != 0) { minH += 255; maxH += 255; }

            range = new Range(minV, maxV, minH, maxH, (uint)(e[off + 9] * 10));
        }

        return range;
    }

    /// <summary>One detailed timing descriptor, in full.</summary>
    /// <remarks>
    /// The first one is the preferred mode, and on a modern panel that is its
    /// native resolution and rate — the single most useful line in the whole
    /// block.
    /// </remarks>
    private static string Timing(byte[] e, int off, bool preferred)
    {
        double clock = ((e[off + 1] << 8) | e[off]) / 100.0;

        int hActive = ((e[off + 4] & 0xF0) << 4) | e[off + 2];
        int hBlank = ((e[off + 4] & 0x0F) << 8) | e[off + 3];
        int vActive = ((e[off + 7] & 0xF0) << 4) | e[off + 5];
        int vBlank = ((e[off + 7] & 0x0F) << 8) | e[off + 6];

        int widthMm = ((e[off + 14] & 0xF0) << 4) | e[off + 12];
        int heightMm = ((e[off + 14] & 0x0F) << 8) | e[off + 13];

        bool interlaced = (e[off + 17] & 0x80) != 0;

        long total = (long)(hActive + hBlank) * (vActive + vBlank);
        double hz = total > 0 ? clock * 1_000_000 / total : 0;

        var text = new System.Text.StringBuilder();
        text.Append($"{hActive} x {vActive}");
        if (hz > 0) text.Append($" @ {hz:0.##} Hz");
        if (interlaced) text.Append(" interlaced");
        text.Append($", {clock:0.##} MHz pixel clock");
        if (widthMm > 0 && heightMm > 0) text.Append($", {widthMm} x {heightMm} mm");
        if (preferred) text.Append("  (preferred)");

        return text.ToString();
    }

    private static string? String(byte[] e, byte tag)
    {
        Span<char> buf = stackalloc char[13];

        for (int off = 54; off + 18 <= 126; off += 18)
        {
            if (e[off] != 0 || e[off + 1] != 0 || e[off + 2] != 0) continue;
            if (e[off + 3] != tag) continue;

            int len = 0;
            for (int i = 0; i < 13; i++)
            {
                byte b = e[off + 5 + i];
                if (b is 0x0A or 0x00) break;
                if (b is < 0x20 or > 0x7E) continue;
                buf[len++] = (char)b;
            }

            string s = new string(buf[..len]).Trim();
            if (s.Length > 0) return s;
        }

        return null;
    }

    /// <summary>The 128 base bytes must sum to zero, modulo 256.</summary>
    private static byte Sum(byte[] e)
    {
        byte total = 0;
        for (int i = 0; i < 128; i++) total += e[i];
        return total;
    }
}
