using System.Text;
using System.Text.RegularExpressions;
using DisplCtrl.Core.Displays;

namespace DisplCtrl.Display.Devices;

/// <summary>
/// What a monitor is, with everything personal removed.
/// </summary>
/// <remarks>
/// Built for publishing, which is what separates it from <c>DisplayReport</c>.
/// The report is written for the person looking at their own machine and holds
/// things that must never leave it: the monitor's serial number, a device path
/// carrying a machine-specific instance id, and wallpaper paths containing the
/// user's name. This carries what is true of the <em>model</em> and nothing
/// true of the person.
/// <para>
/// The test for every field is: would this be identical on someone else's
/// monitor of the same model? If not, it does not belong here. Current settings
/// fail that test — brightness 62 says what this desk is like this evening — so
/// controls are recorded by range and not by where they happen to be set.
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

    public int WidthMm { get; init; }
    public int HeightMm { get; init; }
    public double DiagonalInches { get; init; }

    public uint NativeWidth { get; init; }
    public uint NativeHeight { get; init; }
    public uint HighestRefreshHz { get; init; }

    /// <summary>Distinct resolutions the panel offers, with the best refresh of each.</summary>
    public IReadOnlyList<string> Modes { get; init; } = [];

    public string BitDepth { get; init; } = "";
    public string ColorFormat { get; init; } = "";
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

    /// <summary>How many of those DisplCtrl is willing to drive.</summary>
    public int SettableCount { get; init; }

    /// <summary>The identifier a device record is filed under.</summary>
    /// <remarks>
    /// Manufacturer and product code, which is what every unit of a model
    /// carries in its EDID. Deliberately not the token DisplCtrl uses internally:
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
    public static DeviceSubmission Build(DisplayInfo display, bool? isOled = null)
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
            var line = new StringBuilder($"`{c.Hex}` {c.Name}");

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

            if (c.Settable) { line.Append(" **(settable)**"); settable++; }

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
            HdrSupported = hdr.Supported,
            VrrMinHz = vrr.Capable ? vrr.MinHz : 0,
            VrrMaxHz = vrr.Capable ? vrr.MaxHz : 0,
            DdcWorks = capability.Supported,
            BrightnessOverDdc = brightness.Supported && !display.IsInternal,
            MccsVersion = capability.MccsVersion ?? "",
            Capabilities = capability.Raw,
            Controls = controls,
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
        var best = new Dictionary<(uint Width, uint Height), uint>();

        try
        {
            foreach (DisplayMode m in DisplayModes.Available(display.GdiName))
            {
                if ((long)m.Width * m.Height > (long)w * h) (w, h) = (m.Width, m.Height);
                if (m.RefreshHz > hz) hz = m.RefreshHz;

                if (!best.TryGetValue((m.Width, m.Height), out uint top) || m.RefreshHz > top)
                    best[(m.Width, m.Height)] = m.RefreshHz;
            }
        }
        catch (Exception)
        {
            // The current mode is a reasonable answer on its own.
        }

        var sizes = new List<(uint Width, uint Height)>(best.Keys);
        sizes.Sort(static (a, b) => ((long)b.Width * b.Height).CompareTo((long)a.Width * a.Height));

        var lines = new List<string>(sizes.Count);
        foreach ((uint mw, uint mh) in sizes) lines.Add($"{mw} x {mh} @ {best[(mw, mh)]} Hz");

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
    /// Plain ASCII throughout, which makes this the one place in DisplCtrl without
    /// proper typography. The body travels as a percent-encoded query string,
    /// where an em dash costs nine characters and a multiplication sign six;
    /// spelling them out in ASCII is the difference between a prefilled issue
    /// and asking the person to paste a file.
    /// </para>
    /// </remarks>
    public string ToMarkdown()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"### {IssueTitle}");
        sb.AppendLine();
        sb.AppendLine($"Device key: `{Key}`");
        sb.AppendLine();
        sb.AppendLine("| | |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Connector | {Connector} |");
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
            sb.AppendLine($"#### Controls it lists ({Controls.Count}, {SettableCount} DisplCtrl will drive)");
            sb.AppendLine();
            foreach (string c in Controls) sb.AppendLine($"- {c}");
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

        if (Modes.Count > 0)
        {
            sb.AppendLine("<details><summary>Modes it offers</summary>");
            sb.AppendLine();
            foreach (string m in Modes) sb.AppendLine($"- {m}");
            sb.AppendLine();
            sb.AppendLine("</details>");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("Submitted from DisplCtrl. Serial number, device path, file paths, user name and "
            + "current settings are not collected - only what is true of every unit of this model.");

        return sb.ToString();
    }

    /// <summary>
    /// How the device is named in the issue title.
    /// </summary>
    /// <remarks>
    /// The manufacturer is dropped when the model already opens with it, which
    /// happens on panels that report no name of their own and fall back to the
    /// EDID key: "SDC SDC-4154" reads like a mistake.
    /// </remarks>
    public string IssueTitle =>
        Model.StartsWith(Manufacturer, StringComparison.OrdinalIgnoreCase)
            ? Model
            : $"{Manufacturer} {Model}";
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
    /// Everything in a submission is chosen field by field, so in principle
    /// nothing sensitive can reach here. It runs anyway, over the finished text,
    /// against the four things known to be dangerous: the serials of the
    /// attached panels, Windows device instance paths, any path under a user
    /// profile — which carries the account name, very often the person's real
    /// name — and the account name itself.
    /// <para>
    /// The point is that a field added later cannot quietly reintroduce a leak.
    /// It would have to be sensitive <em>and</em> of a shape none of these
    /// catch before anything escaped.
    /// </para>
    /// </remarks>
    public static string Scrub(string text, IEnumerable<string>? serials = null)
    {
        foreach (string serial in serials ?? [])
            if (serial.Length >= 4)
                text = text.Replace(serial, Removed, StringComparison.OrdinalIgnoreCase);

        text = DevicePath().Replace(text, Removed);
        text = UserPath().Replace(text, Removed);
        text = Guid().Replace(text, Removed);

        // Short account names are skipped: a two-letter name would match inside
        // ordinary words and redact the report into uselessness.
        string user = Environment.UserName;
        if (user.Length >= 3) text = text.Replace(user, Removed, StringComparison.OrdinalIgnoreCase);

        return text;
    }

    /// <summary>True when nothing sensitive survived into the text.</summary>
    public static bool IsClean(string text, IEnumerable<string>? serials = null) =>
        Scrub(text, serials) == text;

    private const string Removed = "[removed]";

    // \\?\DISPLAY#SDC4154#5&1af48b2f&0&UID256#{guid}
    [GeneratedRegex(@"\\\\[?.]\\[A-Za-z0-9#&{}\-]+", RegexOptions.None, 200)]
    private static partial Regex DevicePath();

    // Any path under a user profile, which carries the account name.
    [GeneratedRegex(@"[A-Za-z]:\\Users\\[^\s""'|)\]]*", RegexOptions.IgnoreCase, 200)]
    private static partial Regex UserPath();

    [GeneratedRegex(@"\{?[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}?",
        RegexOptions.None, 200)]
    private static partial Regex Guid();
}
