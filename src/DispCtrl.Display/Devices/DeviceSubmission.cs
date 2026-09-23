using System.Text;
using System.Text.RegularExpressions;
using DispCtrl.Core.Displays;

namespace DispCtrl.Display.Devices;

/// <summary>
/// What a monitor is, with everything personal removed.
/// </summary>
/// <remarks>
/// Built for publishing, which is what separates it from <c>DisplayReport</c>.
/// The report is written for the person looking at their own machine and holds
/// things that must never leave it: the monitor's serial number, a device path
/// carrying a machine-specific instance id, and wallpaper paths containing the
/// user's name. The public record contains model capabilities and a separately
/// labelled observation of signal timing and scaling on the tested connection.
/// <para>
/// Controls are recorded by range, not the user's chosen value. The observed
/// signal and DPI describe the tested setup and are not advertised as universal
/// model properties. Serial numbers, paths and personal settings stay private.
/// </para>
/// </remarks>
public sealed record DeviceSubmission
{
    /// <summary>Three-letter EDID manufacturer code, e.g. DEL.</summary>
    public required string Manufacturer { get; init; }

    /// <summary>Model as the monitor reports it over DDC/CI, or from EDID.</summary>
    public required string Model { get; init; }

    /// <summary>EDID product code, the four hex digits after the manufacturer.</summary>
    public required string Product { get; init; }

    public required string Connector { get; init; }
    public required string PanelTechnology { get; init; }

    /// <summary>
    /// The machine a built-in panel is part of, e.g. <c>ASUSTeK Vivobook M7400QC</c>.
    /// </summary>
    /// <remarks>
    /// Empty for an external monitor, which is attached to a PC rather than
    /// built into one. For a laptop screen it is the most useful line in the
    /// record: the panel reports no name of its own, so the machine's model is
    /// the only thing anyone would search for.
    /// </remarks>
    public string BuiltInto { get; init; } = "";

    public int WidthMm { get; init; }
    public int HeightMm { get; init; }
    public double DiagonalInches { get; init; }

    public uint NativeWidth { get; init; }
    public uint NativeHeight { get; init; }
    public uint HighestRefreshHz { get; init; }

    /// <summary>Every resolution the driver reports, with all of its rates.</summary>
    /// <remarks>
    /// The whole list, not a summary. Which rates a model offers at which
    /// resolutions is exactly the kind of thing someone reads a device record to
    /// find out, and it is identical on every unit of the model.
    /// </remarks>
    public IReadOnlyList<string> Modes { get; init; } = [];

    /// <summary>
    /// The panel's own EDID, decoded.
    /// </summary>
    /// <remarks>
    /// <see cref="EdidDetails"/> is publishable by construction: it has no field
    /// for a serial number, so there is none to forget to remove. That is
    /// deliberate — the serial lives in <see cref="DisplayKey"/>, where the
    /// identity code that must never be published already is.
    /// </remarks>
    public EdidDetails Edid { get; init; } = EdidDetails.None;

    /// <summary>Low-level DDC/CI commands the monitor says it accepts.</summary>
    public IReadOnlyList<string> Commands { get; init; } = [];

    /// <summary>
    /// This display's whole report section, verbatim.
    /// </summary>
    /// <remarks>
    /// Everything DispCtrl can read, including the parts that describe a desk
    /// rather than a model: where the display sits, what it is set to right now,
    /// what each of its controls currently reads. Carried because the owner of
    /// this project asked for records to hold everything available rather than a
    /// chosen subset.
    /// <para>
    /// <b>It is <see cref="Redact.Scrub"/> that makes this publishable, not the
    /// choice of fields.</b> That inverts the rule the rest of this type was
    /// built on, and it is the whole safety margin now: the scrub removes the
    /// serials of attached panels, device paths, any path under a user profile,
    /// bare GUIDs and the account name, and nothing else is standing between
    /// this text and a public issue. A field of a shape none of those catch
    /// would go out.
    /// </para>
    /// </remarks>
    public string FullReport { get; init; } = "";

    /// <summary>Every preset saved on this machine, in full.</summary>
    /// <remarks>
    /// Asked for alongside the report: a preset says how a desk is actually
    /// used, which the rest of a record cannot. Carries wallpaper paths and
    /// per-monitor serials, so the same scrub applies.
    /// </remarks>
    public string Presets { get; init; } = "";

    public string BitDepth { get; init; } = "";
    public string ColorFormat { get; init; } = "";
    public string ActiveSignalMode { get; init; } = "";
    public string PixelClock { get; init; } = "";
    public string ControllerType { get; init; } = "";
    public double PixelDensity { get; init; }
    public uint EffectiveDpi { get; init; }
    public bool HdrSupported { get; init; }
    public uint VrrMinHz { get; init; }
    public uint VrrMaxHz { get; init; }

    public bool DdcWorks { get; init; }
    public bool BrightnessOverDdc { get; init; }
    public string MccsVersion { get; init; } = "";

    /// <summary>The MCCS capabilities string, verbatim. The valuable part.</summary>
    public string Capabilities { get; init; } = "";

    /// <summary>Each control the monitor lists, with its range — never its current value.</summary>
    public IReadOnlyList<string> Controls { get; init; } = [];

    /// <summary>How many of those DispCtrl is willing to drive.</summary>
    public int SettableCount { get; init; }

    /// <summary>The identifier a device record is filed under.</summary>
    /// <remarks>
    /// Manufacturer and product code, which is what every unit of a model
    /// carries in its EDID. Deliberately not the token DispCtrl uses internally:
    /// that ends in the serial number, which is the whole point of it and
    /// exactly what must not be published.
    /// </remarks>
    public string Key => Product.Length > 0 ? $"{Manufacturer}-{Product}" : Manufacturer;

    /// <summary>
    /// Everything about one display that is safe to publish.
    /// </summary>
    /// <remarks>
    /// Slow — a full capabilities sweep plus mode enumeration, several seconds
    /// on a DDC/CI monitor. Call it off the UI thread.
    /// <para>
    /// Every read is guarded individually. A submission built from a monitor
    /// that answers only half the questions is still worth having; in fact it is
    /// the most worth having, because a panel that fails a read here is exactly
    /// the kind this database exists to describe.
    /// </para>
    /// </remarks>
    /// <param name="isOled">
    /// What the owner has marked the panel as, used only when the monitor does
    /// not report its own technology. Most internal panels report nothing over
    /// DDC/CI, so without this every laptop record would read "not reported" -
    /// and whether a model is OLED is exactly the sort of thing this database
    /// exists to answer.
    /// </param>
    public static DeviceSubmission Build(DisplayInfo display, bool? isOled = null, bool includePrivateDetails = true)
    {
        MonitorCapability capability = Safely(() => MonitorCapabilities.Read(display), MonitorCapability.None);
        DisplayDetail detail = Safely(() => DisplayDetails.Read(display), new DisplayDetail());
        VrrState vrr = Safely(() => VariableRefreshRate.Read(display), VrrState.Unsupported);
        HdrState hdr = Safely(() => AdvancedDisplay.ReadHdr(display), HdrState.Unsupported);
        BrightnessRange brightness = Safely(() => Brightness.Read(display), BrightnessRange.Unsupported);

        (uint width, uint height, uint refresh, List<string> modes) = ReadModes(display);

        var controls = new List<string>();
        int settable = 0;

        foreach (VcpControl c in capability.Controls)
        {
            string kind = c.Kind switch
            {
                VcpKind.Continuous => "range",
                VcpKind.Discrete => "list",
                _ => "read-only",
            };

            var line = new StringBuilder($"`{c.Hex}` {c.Name} ({kind})");

            if (c.Values.Count > 0)
            {
                var values = new List<string>(c.Values.Count);
                foreach (VcpValue v in c.Values) values.Add($"0x{v.Value:X2} {v.Name}");

                line.Append(": ").Append(string.Join(", ", values));
            }
            else if (c.Maximum > 0)
            {
                line.Append(": 0 to ").Append(c.Maximum);
            }

            if (c.Settable) { line.Append(" **(DispCtrl writes this)**"); settable++; }
            else if (c.Kind != VcpKind.Information && !VcpControl.IsAllowed(c.Code))
                line.Append(" _(recorded, never written)_");

            controls.Add(line.ToString());
        }

        // The monitor's own name for itself is the marketing one; the EDID
        // descriptor is the next best; the product code is the last resort.
        string model = First(capability.Model, display.FriendlyName, display.Key.Model);

        return new DeviceSubmission
        {
            Manufacturer = Redact.Manufacturer(display.Key.Model),
            Model = model,
            Product = Redact.Product(display.Key.Model),
            Connector = display.Connector.ToString(),
            BuiltInto = display.IsInternal && MachineInfo.Read() is { Present: true } machine
                ? machine.Label
                : "",
            PanelTechnology = ReadPanelTechnology(capability, isOled),
            WidthMm = display.PhysicalWidthMm,
            HeightMm = display.PhysicalHeightMm,
            DiagonalInches = display.DiagonalInches,
            NativeWidth = width,
            NativeHeight = height,
            HighestRefreshHz = refresh,
            Modes = modes,
            BitDepth = Known(detail.BitDepth),
            ColorFormat = Known(detail.ColorFormat),
            ActiveSignalMode = Known(detail.ActiveSignalMode),
            PixelClock = Known(detail.PixelClock),
            ControllerType = capability.Controls.FirstOrDefault(control => control.Code == 0xC8) is { Current: >= 0 } controller
                ? $"0x{controller.Current & 0xFF:X2} (raw 0x{controller.Current:X4})" : "",
            PixelDensity = display.PhysicalPpi,
            EffectiveDpi = display.Dpi,
            HdrSupported = hdr.Supported,
            VrrMinHz = vrr.Capable ? vrr.MinHz : 0,
            VrrMaxHz = vrr.Capable ? vrr.MaxHz : 0,
            DdcWorks = capability.Supported,
            BrightnessOverDdc = brightness.Supported && !display.IsInternal,
            MccsVersion = capability.MccsVersion ?? "",
            Capabilities = capability.Raw,
            Controls = controls,
            Commands = capability.Commands,
            FullReport = includePrivateDetails ? Safely(() => DisplayReport.For(display), "") : "",
            Presets = includePrivateDetails ? Safely(DisplayReport.Presets, "") : "",
            Edid = Safely(() => EdidReader.Describe(display.Key.DevicePath), EdidDetails.None),
            SettableCount = settable,
        };
    }

    private static T Safely<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    /// <summary>The first of these that says anything.</summary>
    private static string First(params string?[] candidates)
    {
        foreach (string? c in candidates)
            if (!string.IsNullOrWhiteSpace(c)) return c.Trim();

        return "unknown";
    }

    /// <summary>Turns the report's em-dash placeholder back into nothing.</summary>
    private static string Known(string? value) =>
        string.IsNullOrWhiteSpace(value) || value == "—" ? "" : value;

    private static string ReadPanelTechnology(MonitorCapability capability, bool? isOled)
    {
        // 0xB6 is the display technology code, and the only place a monitor
        // states outright whether it is LCD, OLED or something else.
        foreach (VcpControl c in capability.Controls)
            if (c.Code == 0xB6 && c.Current >= 0) return c.Display;

        string type = Known(capability.Type);
        if (type.Length > 0) return type;

        // Marked as reported rather than measured, so a reader knows how far to
        // trust it.
        return isOled switch
        {
            true => "OLED (reported by owner)",
            false => "not OLED (reported by owner)",
            null => "not reported",
        };
    }

    private static (uint Width, uint Height, uint Refresh, List<string> Modes) ReadModes(DisplayInfo display)
    {
        uint w = (uint)display.Bounds.Width, h = (uint)display.Bounds.Height, hz = display.RefreshHz;
        var rates = new Dictionary<(uint Width, uint Height), SortedSet<uint>>();

        try
        {
            foreach (DisplayMode m in DisplayModes.Available(display.GdiName))
            {
                if ((long)m.Width * m.Height > (long)w * h) (w, h) = (m.Width, m.Height);
                if (m.RefreshHz > hz) hz = m.RefreshHz;

                if (!rates.TryGetValue((m.Width, m.Height), out SortedSet<uint>? set))
                {
                    set = [];
                    rates[(m.Width, m.Height)] = set;
                }

                set.Add(m.RefreshHz);
            }
        }
        catch (Exception)
        {
            // The current mode is a reasonable answer on its own.
        }

        var sizes = new List<(uint Width, uint Height)>(rates.Keys);
        sizes.Sort(static (a, b) => ((long)b.Width * b.Height).CompareTo((long)a.Width * a.Height));

        // Grouped by resolution and carrying every rate. A monitor reporting 163
        // modes is a dozen resolutions at several rates each, and "what can this
        // resolution run at" is the question a record is read to answer.
        var lines = new List<string>(sizes.Count);
        foreach ((uint mw, uint mh) in sizes)
            lines.Add($"{mw} x {mh} @ {string.Join(", ", rates[(mw, mh)].Reverse())} Hz");

        return (w, h, hz, lines);
    }

    /// <summary>
    /// The submission as a GitHub issue body.
    /// </summary>
    /// <remarks>
    /// Markdown rather than JSON, because a person reads this before pressing
    /// Submit and has to be able to check what they are publishing at a glance.
    /// A fenced JSON blob would be easier to parse on the other end and far
    /// harder to read on this one, and the person deciding matters more.
    /// <para>
    /// Plain ASCII throughout, which makes this the one place in DispCtrl without
    /// proper typography. The body travels as a percent-encoded query string,
    /// where an em dash costs nine characters and a multiplication sign six;
    /// spelling them out in ASCII is the difference between a prefilled issue
    /// and asking the person to paste a file.
    /// </para>
    /// </remarks>
    public string ToRepositoryMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### {IssueTitle}");
        sb.AppendLine();
        sb.AppendLine($"Device key: `{Key}`");
        sb.AppendLine();
        sb.AppendLine("| | |");
        sb.AppendLine("|---|---|");
        Row(sb, "Model", Model);
        Row(sb, "Manufacturer", Edid.ManufacturerName.Length > 0 ? $"{Edid.ManufacturerName} ({Manufacturer})" : Manufacturer);
        Row(sb, "Manufacturer and product", Key);
        Row(sb, "Controller type", ControllerType);
        sb.AppendLine($"| Connector | {Connector} |");
        if (BuiltInto.Length > 0) sb.AppendLine($"| Built into | {BuiltInto} |");
        sb.AppendLine($"| Panel technology | {PanelTechnology} |");
        if (WidthMm > 0) sb.AppendLine($"| Physical size | {WidthMm} x {HeightMm} mm ({DiagonalInches:0.0} in) |");
        sb.AppendLine($"| Highest mode | {NativeWidth} x {NativeHeight} @ {HighestRefreshHz} Hz |");
        if (BitDepth.Length > 0) sb.AppendLine($"| Bit depth | {BitDepth} |");
        if (ColorFormat.Length > 0) sb.AppendLine($"| Colour format | {ColorFormat} |");
        sb.AppendLine($"| HDR | {(HdrSupported ? "supported" : "not supported")} |");
        sb.AppendLine($"| Variable refresh | {(VrrMaxHz > 0 ? $"{VrrMinHz}-{VrrMaxHz} Hz" : "not advertised")} |");
        sb.AppendLine($"| DDC/CI | {(DdcWorks ? "answers" : "no answer")} |");
        sb.AppendLine($"| Brightness over DDC/CI | {(BrightnessOverDdc ? "yes" : "no")} |");
        if (MccsVersion.Length > 0) sb.AppendLine($"| MCCS version | {MccsVersion} |");
        if (Edid.Present)
        {
            Row(sb, "EDID manufacturer", Edid.ManufacturerName.Length > 0
                ? $"{Edid.ManufacturerName} ({Edid.ManufacturerCode})" : Edid.ManufacturerCode);
            Row(sb, "EDID product", Edid.ProductCode);
            Row(sb, "EDID name", Edid.MonitorName);
            Row(sb, "Made", Edid.Made);
            Row(sb, "EDID version", Edid.Version);
        }
        sb.AppendLine();

        if (Controls.Count > 0)
        {
            sb.AppendLine($"#### Controls it lists ({Controls.Count}, {SettableCount} DispCtrl will drive)");
            sb.AppendLine();
            foreach (string control in Controls) sb.AppendLine($"- {control}");
            sb.AppendLine();
        }

        if (Commands.Count > 0)
        {
            sb.AppendLine($"Low-level commands it accepts: {string.Join(" ", Commands)}");
            sb.AppendLine();
        }

        if (Capabilities.Length > 0)
        {
            sb.AppendLine("#### Capabilities string");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(Capabilities);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        Fold(sb, "Modes the driver reports", Modes);
        sb.AppendLine("#### Observed connection (may differ between setups)");
        sb.AppendLine();
        sb.AppendLine("| | |");
        sb.AppendLine("|---|---|");
        Row(sb, "Active signal mode", ActiveSignalMode);
        Row(sb, "Pixel clock", PixelClock);
        if (PixelDensity > 0) Row(sb, "Pixel density at current resolution", $"{PixelDensity:0} PPI");
        if (EffectiveDpi > 0) Row(sb, "Windows rendering", $"{EffectiveDpi} DPI ({EffectiveDpi / 96.0 * 100:0}% scaling)");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("Submitted from DispCtrl. Serial number, device path, file paths, user name "
            + "are not included. Model capabilities and the observed signal/scaling are included; brightness, wallpaper and app settings are not.");
        return sb.ToString();
    }

    public string ToMarkdown(bool includeReport = true)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"### {IssueTitle}");
        sb.AppendLine();
        sb.AppendLine($"Device key: `{Key}`");
        sb.AppendLine();
        sb.AppendLine("| | |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Connector | {Connector} |");

        if (BuiltInto.Length > 0) sb.AppendLine($"| Built into | {BuiltInto} |");
        sb.AppendLine($"| Panel technology | {PanelTechnology} |");

        if (WidthMm > 0)
            sb.AppendLine($"| Physical size | {WidthMm} x {HeightMm} mm ({DiagonalInches:0.0} in) |");

        sb.AppendLine($"| Highest mode | {NativeWidth} x {NativeHeight} @ {HighestRefreshHz} Hz |");

        if (BitDepth.Length > 0) sb.AppendLine($"| Bit depth | {BitDepth} |");
        if (ColorFormat.Length > 0) sb.AppendLine($"| Colour format | {ColorFormat} |");

        sb.AppendLine($"| HDR | {(HdrSupported ? "supported" : "not supported")} |");
        sb.AppendLine($"| Variable refresh | {(VrrMaxHz > 0 ? $"{VrrMinHz}-{VrrMaxHz} Hz" : "not advertised")} |");
        sb.AppendLine($"| DDC/CI | {(DdcWorks ? "answers" : "no answer")} |");
        sb.AppendLine($"| Brightness over DDC/CI | {(BrightnessOverDdc ? "yes" : "no")} |");

        if (MccsVersion.Length > 0) sb.AppendLine($"| MCCS version | {MccsVersion} |");

        sb.AppendLine();

        if (Controls.Count > 0)
        {
            sb.AppendLine($"#### Controls it lists ({Controls.Count}, {SettableCount} DispCtrl will drive)");
            sb.AppendLine();
            foreach (string c in Controls) sb.AppendLine($"- {c}");
            sb.AppendLine();
        }

        if (Commands.Count > 0)
        {
            sb.AppendLine($"Low-level commands it accepts: {string.Join(" ", Commands)}");
            sb.AppendLine();
        }

        AppendEdid(sb);

        if (Capabilities.Length > 0)
        {
            sb.AppendLine("#### Capabilities string");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(Capabilities);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (includeReport)
        {
            FoldText(sb, "The full report for this display, as DispCtrl writes it locally", FullReport);
            if (DispCtrl.Core.FeatureFlags.Presets)
                FoldText(sb, "Presets saved on this machine", Presets);
        }

        Fold(sb, "Modes the driver reports", Modes);
        Fold(sb, "Detailed timings, as the panel gives them", Edid.DetailedTimings);
        Fold(sb, "Standard timings", Edid.StandardTimings);
        Fold(sb, "Established timings", Edid.EstablishedTimings);

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("Submitted from DispCtrl. This is everything DispCtrl can read about this "
            + "display, including its current settings"
            + (DispCtrl.Core.FeatureFlags.Presets ? " and the presets on this machine" : "") + ". Removed before "
            + "sending: monitor serial numbers, Windows device paths, anything under a user profile "
            + "folder, bare GUIDs and the account name.");

        return sb.ToString();
    }

    /// <summary>The EDID, in full, minus the parts that identify a unit.</summary>
    /// <remarks>
    /// A table rather than a hex dump. The raw blob would be more complete still
    /// and must never be published: bytes 12-15 and descriptor 0xFF carry the
    /// serial number, and a hex dump of them matches none of the patterns
    /// <see cref="Redact.Scrub"/> looks for.
    /// </remarks>
    private void AppendEdid(StringBuilder sb)
    {
        if (!Edid.Present) return;

        sb.AppendLine("#### What the panel says about itself (EDID)");
        sb.AppendLine();
        sb.AppendLine("| | |");
        sb.AppendLine("|---|---|");

        Row(sb, "Manufacturer", Edid.ManufacturerName.Length > 0
            ? $"{Edid.ManufacturerName} ({Edid.ManufacturerCode})"
            : Edid.ManufacturerCode);

        Row(sb, "Product code", Edid.ProductCode);
        Row(sb, "Monitor name", Edid.MonitorName);
        Row(sb, "Free text", Edid.FreeText);
        Row(sb, "Made", Edid.Made);
        Row(sb, "EDID version", Edid.Version);
        Row(sb, "Extension blocks", Edid.Extensions.ToString());
        Row(sb, "Checksum", Edid.ChecksumValid ? "valid" : "does not add up");

        Row(sb, "Signal", Edid.Digital
            ? "digital"
              + (Edid.BitsPerColour > 0 ? $", {Edid.BitsPerColour} bits per colour" : "")
              + (Edid.Interface.Length > 0 ? $", {Edid.Interface}" : "")
              + (Edid.BitsPerColour == 0 && Edid.Interface.Length == 0
                 ? " (EDID 1.3 records no depth or interface)" : "")
            : $"analogue, {Edid.AnalogueSignal}");

        Row(sb, "Colour encodings", Edid.ColourEncodings);
        Row(sb, "Declared size", Edid.WidthCm > 0
            ? $"{Edid.WidthCm} x {Edid.HeightCm} cm"
            : Edid.AspectRatio.Length > 0 ? $"aspect {Edid.AspectRatio}" : null);

        Row(sb, "Gamma", Edid.Gamma > 0 ? $"{Edid.Gamma:0.00}" : "deferred to a descriptor");
        Row(sb, "sRGB is the default", Edid.SrgbDefault ? "yes" : "no");
        Row(sb, "Preferred timing is native", Edid.PreferredTimingIsNative ? "yes" : "no");
        Row(sb, "Continuous frequency", Edid.ContinuousFrequency ? "yes" : "no");

        var power = new List<string>();
        if (Edid.Standby) power.Add("standby");
        if (Edid.Suspend) power.Add("suspend");
        if (Edid.ActiveOff) power.Add("active off");
        Row(sb, "DPMS", power.Count > 0 ? string.Join(", ", power) : "none advertised");

        Row(sb, "Red primary", Edid.Red.ToString());
        Row(sb, "Green primary", Edid.Green.ToString());
        Row(sb, "Blue primary", Edid.Blue.ToString());
        Row(sb, "White point", Edid.White.ToString());

        if (Edid.MaxVerticalHz > 0)
        {
            Row(sb, "Vertical range", $"{Edid.MinVerticalHz}-{Edid.MaxVerticalHz} Hz");
            Row(sb, "Horizontal range", $"{Edid.MinHorizontalKHz}-{Edid.MaxHorizontalKHz} kHz");
            Row(sb, "Max pixel clock", Edid.MaxPixelClockMHz > 0 ? $"{Edid.MaxPixelClockMHz} MHz" : null);
        }

        Row(sb, "Descriptor blocks", string.Join(", ", Edid.Descriptors));

        sb.AppendLine();
    }

    private static void Row(StringBuilder sb, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) sb.AppendLine($"| {label} | {value} |");
    }

    /// <summary>A block of preformatted text, folded away.</summary>
    /// <remarks>
    /// Fenced as well as folded. The report is column-aligned text, and GitHub
    /// would otherwise reflow it into one paragraph and lose the alignment that
    /// makes it readable.
    /// </remarks>
    private static void FoldText(StringBuilder sb, string title, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        sb.AppendLine($"<details><summary>{title}</summary>");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine(text.TrimEnd());
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("</details>");
        sb.AppendLine();
    }

    /// <summary>A long list, folded away so the issue stays readable.</summary>
    private static void Fold(StringBuilder sb, string title, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return;

        sb.AppendLine($"<details><summary>{title} ({lines.Count})</summary>");
        sb.AppendLine();
        foreach (string line in lines) sb.AppendLine($"- {line}");
        sb.AppendLine();
        sb.AppendLine("</details>");
        sb.AppendLine();
    }

    /// <summary>
    /// How the device is named in the issue title.
    /// </summary>
    /// <remarks>
    /// The manufacturer is dropped when the model already opens with it, which
    /// happens on panels that report no name of their own and fall back to the
    /// EDID key: "SDC SDC-4154" reads like a mistake.
    /// </remarks>
    public string IssueTitle
    {
        get
        {
            string name = Model.StartsWith(Manufacturer, StringComparison.OrdinalIgnoreCase)
                ? Model
                : $"{Manufacturer} {Model}";

            // A laptop panel's own name is usually nothing at all, so the
            // machine goes in the title where it can be searched for.
            return BuiltInto.Length > 0 ? $"{name} in {BuiltInto}" : name;
        }
    }
}

/// <summary>
/// Removes everything that identifies a machine or a person.
/// </summary>
/// <remarks>
/// Small, separate, and covered directly by checks, because this is the part
/// that has to be right. A leak here is not a bug that shows up as a crash — it
/// is a serial number on the public internet that cannot be recalled.
/// </remarks>
public static partial class Redact
{
    /// <summary>The three-letter manufacturer code from a "DEL-A234" style key.</summary>
    public static string Manufacturer(string model)
    {
        int dash = model.IndexOf('-');
        return dash > 0 ? model[..dash] : model;
    }

    /// <summary>The product code from a "DEL-A234" style key.</summary>
    public static string Product(string model)
    {
        int dash = model.IndexOf('-');
        return dash > 0 && dash + 1 < model.Length ? model[(dash + 1)..] : "";
    }

    /// <summary>
    /// The last line of defence over text about to be published.
    /// </summary>
    /// <remarks>
    /// A submission used to be chosen field by field, so in principle nothing
    /// sensitive could reach here and this was a second line. It is now the
    /// first: a record carries the whole report and the presets, so what makes
    /// it publishable <em>is</em> this. It runs over the finished text,
    /// against the things known to be dangerous: the serials <em>and identity
    /// tokens</em> of every attached panel, Windows device instance paths, any
    /// path under a user profile — which carries the account name, very often
    /// the person's real name — bare GUIDs, and the account name itself.
    /// <para>
    /// The point is that a field added later cannot quietly reintroduce a leak.
    /// It would have to be sensitive <em>and</em> of a shape none of these
    /// catch before anything escaped.
    /// </para>
    /// </remarks>
    public static string Scrub(string text, IEnumerable<string>? identifiers = null)
    {
        foreach (string id in identifiers ?? [])
            if (id.Length >= 4)
                text = text.Replace(id, Removed, StringComparison.OrdinalIgnoreCase);

        // Whole paths first, so a profile path goes in one piece rather than
        // leaving the tail of itself behind.
        text = UserPath().Replace(text, Removed);

        // Then the profile directory by name, for a profile that does not live
        // under C:\Users at all - a redirected or roamed one.
        try
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (profile.Length >= 4)
                text = text.Replace(profile, Removed, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // The patterns either side still apply.
        }

        text = DevicePath().Replace(text, Removed);
        text = InstanceId().Replace(text, Removed);
        text = Guid().Replace(text, Removed);

        // Short account names are skipped: a two-letter name would match inside
        // ordinary words and redact the report into uselessness.
        string user = Environment.UserName;
        if (user.Length >= 3) text = text.Replace(user, Removed, StringComparison.OrdinalIgnoreCase);

        return text;
    }

    /// <summary>
    /// Folds this project's typography down to ASCII.
    /// </summary>
    /// <remarks>
    /// A submission is plain ASCII and the rest of DispCtrl is not. The report
    /// and the preset text are written to be read on this machine, with em
    /// dashes and multiplication signs in them; a record built out of them
    /// inherits those, and the body is the one place in DispCtrl that cannot
    /// have them.
    /// <para>
    /// It began as a URL-length rule — an em dash costs nine characters
    /// percent-encoded and <c>x</c> costs one — and a full record is far past
    /// prefilling now whatever it is spelled with. It is kept because a device
    /// record is read and diffed by whoever maintains the folder, and a file
    /// that is ASCII everywhere is one they can grep.
    /// </para>
    /// <para>
    /// Only the characters this project actually emits are mapped. Anything else
    /// is left alone rather than replaced: mangling an accented monitor name into
    /// a question mark would be a worse answer than carrying it, and
    /// <c>presetcheck</c> asserting the result is ASCII is what would report it.
    /// </para>
    /// </remarks>
    public static string Ascii(string text)
    {
        var sb = new StringBuilder(text.Length);

        foreach (char c in text)
        {
            switch (c)
            {
                case '—':                 // em dash
                case '–':                 // en dash
                case '·':                 // middle dot
                case '•': sb.Append('-'); break;
                case '×': sb.Append('x'); break;
                case '…': sb.Append("..."); break;
                case '°': sb.Append(" deg"); break;
                case '‘':
                case '’': sb.Append('\''); break;
                case '“':
                case '”': sb.Append('"'); break;
                case ' ': sb.Append(' '); break;   // non-breaking space
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }

    /// <summary>True when nothing sensitive survived into the text.</summary>
    public static bool IsClean(string text, IEnumerable<string>? identifiers = null) =>
        Scrub(text, identifiers) == text;

    private const string Removed = "[removed]";

    // \\?\DISPLAY#SDC4154#5&3c9e07d1&0&UID256#{guid}
    [GeneratedRegex(@"\\\\[?.]\\[A-Za-z0-9#&{}\-]+", RegexOptions.None, 200)]
    private static partial Regex DevicePath();

    /// <summary>
    /// Any path under a user profile, which carries the account name.
    /// </summary>
    /// <remarks>
    /// Spaces are consumed, not treated as the end. Stopping at whitespace is
    /// what let a wallpaper path under <c>C:\Users\Ada Lovelace</c> through: the
    /// match ended at "Ada", and the surname, the whole folder tree and a device
    /// instance id baked into the file name all survived into a record. Paths
    /// contain spaces; the terminators are a quote, a pipe, an angle bracket or
    /// the end of the line, and a value sits last on its line in both the report
    /// and a markdown table cell.
    /// </remarks>
    [GeneratedRegex(@"[A-Za-z]:\\Users\\[^\r\n""'|<>]*", RegexOptions.IgnoreCase, 200)]
    private static partial Regex UserPath();

    /// <summary>
    /// A Windows device instance id, with or without its path prefix.
    /// </summary>
    /// <remarks>
    /// <c>5&amp;3c9e07d1&amp;0&amp;UID256</c> identifies one panel on one port of one
    /// machine. It normally arrives inside a <c>\\?\DISPLAY#...</c> path that
    /// <see cref="DevicePath"/> catches, but this laptop's wallpaper tool bakes
    /// it into a file name, where nothing was looking for it.
    /// </remarks>
    [GeneratedRegex(@"[0-9a-f]+&[0-9a-f]{4,}&[0-9a-f]+&UID[0-9]+", RegexOptions.IgnoreCase, 200)]
    private static partial Regex InstanceId();

    [GeneratedRegex(@"\{?[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}?",
        RegexOptions.None, 200)]
    private static partial Regex Guid();
}
