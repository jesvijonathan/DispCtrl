using System.Text;
using DispCtrl.Core.Displays;
using DispCtrl.Core.Presets;
using DispCtrl.Core.Settings;

namespace DispCtrl.Display;

/// <summary>
/// Writes everything DispCtrl can discover about every attached display to one
/// readable text file.
/// </summary>
/// <remarks>
/// The file exists to answer questions after the fact. Nearly every awkward
/// display problem — a monitor that came back at the wrong rate, a preset that
/// did less than expected, a panel that refuses a control — is a question about
/// what the hardware said it could do at the time, and by the time it is worth
/// asking, the moment has passed.
/// <para>
/// Written whole each time rather than appended. This is a snapshot of what is
/// attached now, not a history; a growing file would bury the current answer
/// under every previous one. The timestamp at the top says which moment it is.
/// </para>
/// </remarks>
public static class DisplayReport
{
    public static string Path_ => System.IO.Path.Combine(SettingsStore.Directory, "displays.log");

    /// <summary>
    /// Builds the report and writes it to <see cref="Path_"/>.
    /// </summary>
    /// <remarks>
    /// Blocking, and slow: it enumerates every mode, reads EDID, and asks each
    /// external monitor over DDC/CI what it supports. Call it off the UI thread.
    /// </remarks>
    public static string Write(IReadOnlyList<DisplayInfo> displays)
    {
        string text = Build(displays);

        System.IO.Directory.CreateDirectory(SettingsStore.Directory);

        // Temp-then-move, so a reader never catches a half-written file.
        string temp = Path_ + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, Path_, overwrite: true);

        return Path_;
    }

    public static string Build(IReadOnlyList<DisplayInfo> displays)
    {
        var sb = new StringBuilder();

        sb.AppendLine("DispCtrl display report");
        sb.AppendLine($"Written {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"{displays.Count} display(s) attached");
        sb.AppendLine();

        MachineInfo machine = MachineInfo.Read();
        if (machine.Present)
        {
            sb.AppendLine("This machine");
            Line(sb, "Maker", machine.Manufacturer);
            Line(sb, "Model", machine.Model);
            Line(sb, "Product name", machine.ProductName);
            if (machine.Family.Length > 0) Line(sb, "Family", machine.Family);
            if (machine.Sku.Length > 0) Line(sb, "SKU", machine.Sku);
            if (machine.BaseBoard.Length > 0) Line(sb, "Board", machine.BaseBoard);
            Line(sb, "BIOS", $"{machine.BiosVendor} {machine.BiosVersion} ({machine.BiosDate})");
            sb.AppendLine();
        }

        var inactive = DisplayRegistry.InactiveDisplays();
        if (inactive.Count > 0)
        {
            sb.AppendLine("Connected but not part of the desktop:");
            foreach (string name in inactive) sb.AppendLine($"  {name}");
            sb.AppendLine();
        }

        for (int i = 0; i < displays.Count; i++)
        {
            Describe(sb, displays[i], i + 1);
            sb.AppendLine();
        }

        if (DispCtrl.Core.FeatureFlags.Presets)
        {
            sb.AppendLine(new string('=', 78));
            sb.AppendLine("Presets");
            sb.AppendLine(new string('=', 78));
            sb.AppendLine();
            sb.Append(Presets());
        }

        return sb.ToString();
    }

    /// <summary>
    /// One display's whole report section, as text.
    /// </summary>
    /// <remarks>
    /// For a device record that carries the full report rather than a summary of
    /// it. One display's section rather than the whole file, because a record
    /// describes one monitor and an issue about the Dell has no business
    /// carrying the laptop's.
    /// <para>
    /// <b>The text is not publishable as it stands.</b> It holds the serial, the
    /// device path and wallpaper paths with the account name in them.
    /// <c>Redact.Scrub</c> has to run over it, as it does over every submission.
    /// </para>
    /// </remarks>
    public static string For(DisplayInfo display, int number = 1)
    {
        var sb = new StringBuilder();
        Describe(sb, display, number);
        return sb.ToString();
    }

    /// <summary>Every preset saved on this machine, in full.</summary>
    /// <remarks>
    /// Presets are the answer to "what was this desk set to", which is the
    /// question the report exists for. Wallpaper paths and per-monitor serials
    /// inside them are why this too has to be scrubbed before it goes anywhere.
    /// </remarks>
    public static string Presets()
    {
        if (!DispCtrl.Core.FeatureFlags.Presets) return "";
        try
        {
            return PresetText.DescribeAll(PresetStore.Load());
        }
        catch (Exception ex)
        {
            return $"The presets could not be read: {ex.Message}";
        }
    }

    private static void Describe(StringBuilder sb, DisplayInfo d, int number)
    {
        sb.AppendLine(new string('=', 78));
        sb.AppendLine($"[{number}] {d.Label}");
        sb.AppendLine(new string('=', 78));

        Section(sb, "Identity");
        Line(sb, "Token", d.Token);
        Line(sb, "Device path", d.Key.DevicePath);
        Line(sb, "Model", string.IsNullOrWhiteSpace(d.Key.Model) ? "(EDID gives none)" : d.Key.Model);
        Line(sb, "Serial", string.IsNullOrWhiteSpace(d.Key.Serial) ? "(EDID gives none)" : d.Key.Serial);
        Line(sb, "GDI name", $"{d.GdiName}   (transient — changes across replugs)");
        Line(sb, "Connector", d.Connector.ToString());
        Line(sb, "Built in", d.IsInternal
            ? MachineInfo.Read() is { Present: true } m ? $"yes, into a {m.Label}" : "yes"
            : "no");
        Line(sb, "Main display", d.IsPrimary ? "yes" : "no");

        DescribeEdid(sb, d);

        Section(sb, "Geometry");
        Line(sb, "Desktop bounds", d.Bounds.ToString());
        Line(sb, "Work area", d.WorkArea.ToString());
        Line(sb, "Orientation", $"{d.OrientationDegrees} degrees");
        Line(sb, "Rendered DPI", $"{d.Dpi}  ({d.Scale * 100:0}% scaling)");

        if (d.HasPhysicalSize)
        {
            Line(sb, "Panel size", $"{d.PhysicalWidthMm} x {d.PhysicalHeightMm} mm  ({d.DiagonalInches:0.0}\")");
            Line(sb, "True density", $"{d.PhysicalPpi:0} PPI  (what the glass is, not what Windows renders at)");
        }
        else
        {
            Line(sb, "Panel size", "the EDID does not report one");
        }

        Section(sb, "Current mode");
        Line(sb, "Resolution", $"{d.Bounds.Width} x {d.Bounds.Height}");
        Line(sb, "Refresh rate", $"{d.RefreshHz} Hz");
        Line(sb, "Colour depth", $"{d.BitsPerPixel} bits per pixel");

        DescribeSignal(sb, d);
        DescribeModes(sb, d);
        DescribeControls(sb, d);
        DescribeMonitorControls(sb, d);
    }

    /// <summary>
    /// Everything in the panel's own EDID, decoded.
    /// </summary>
    /// <remarks>
    /// The only place a monitor describes itself rather than being described by
    /// Windows: when it was built, what its glass actually is, what it will take
    /// on its cable, and which modes it was shipped knowing. Windows exposes
    /// almost none of it.
    /// </remarks>
    private static void DescribeEdid(StringBuilder sb, DisplayInfo d)
    {
        EdidDetails e = EdidReader.Describe(d.Key.DevicePath);

        Section(sb, "What the panel says about itself (EDID)");

        if (!e.Present)
        {
            sb.AppendLine("    Windows has no EDID cached for this display.");
            return;
        }

        Line(sb, "Manufacturer", e.ManufacturerName.Length > 0
            ? $"{e.ManufacturerName}  ({e.ManufacturerCode})"
            : $"{e.ManufacturerCode}  (not a code DispCtrl has a name for)");

        // The PNP registry records a country of registration; the EDID does not
        // carry one, and inventing it from the code would be a guess about the
        // panel in front of you.
        Line(sb, "Product code", e.ProductCode);
        Line(sb, "Monitor name", e.MonitorName ?? "(no 0xFC descriptor)");
        Line(sb, "Free text", e.FreeText ?? "(no 0xFE descriptor)");
        Line(sb, "Made", e.Made);
        Line(sb, "EDID version", e.Version);
        Line(sb, "Blob size", $"{e.Bytes} bytes, {e.Extensions} extension block(s) declared");
        Line(sb, "Checksum", e.ChecksumValid ? "valid" : "WRONG - suspect the cable or an adapter");

        Line(sb, "Signal", e.Digital
            ? "digital"
              + (e.BitsPerColour > 0 ? $", {e.BitsPerColour} bits per colour" : "")
              + (e.Interface.Length > 0 ? $", {e.Interface}" : "")
              + (e.BitsPerColour == 0 && e.Interface.Length == 0
                 ? " (EDID 1.3 records no depth or interface)" : "")
            : $"analogue, {e.AnalogueSignal}");

        Line(sb, "Colour encodings", e.ColourEncodings);
        Line(sb, "Declared size", e.WidthCm > 0
            ? $"{e.WidthCm} x {e.HeightCm} cm"
            : e.AspectRatio.Length > 0 ? $"aspect {e.AspectRatio}, no size given" : "none given");

        Line(sb, "Gamma", e.Gamma > 0 ? $"{e.Gamma:0.00}" : "deferred to a descriptor");
        Line(sb, "sRGB is the default", e.SrgbDefault ? "yes" : "no");
        Line(sb, "Preferred timing", e.PreferredTimingIsNative
            ? "is the native pixel format and rate" : "is not marked native");
        Line(sb, "Continuous frequency", e.ContinuousFrequency
            ? "yes - accepts rates it was not given" : "no - only the listed modes");

        var power = new List<string>();
        if (e.Standby) power.Add("standby");
        if (e.Suspend) power.Add("suspend");
        if (e.ActiveOff) power.Add("active off");
        Line(sb, "DPMS", power.Count > 0 ? string.Join(", ", power) : "none advertised");

        Line(sb, "Red primary", e.Red.ToString());
        Line(sb, "Green primary", e.Green.ToString());
        Line(sb, "Blue primary", e.Blue.ToString());
        Line(sb, "White point", e.White.ToString());

        if (e.MaxVerticalHz > 0)
        {
            Line(sb, "Vertical range", $"{e.MinVerticalHz}-{e.MaxVerticalHz} Hz");
            Line(sb, "Horizontal range", $"{e.MinHorizontalKHz}-{e.MaxHorizontalKHz} kHz");
            Line(sb, "Max pixel clock", e.MaxPixelClockMHz > 0 ? $"{e.MaxPixelClockMHz} MHz" : "not given");
        }

        Line(sb, "Descriptor blocks", string.Join(", ", e.Descriptors));

        if (e.DetailedTimings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("    Detailed timings, as the panel gives them:");
            foreach (string t in e.DetailedTimings) sb.AppendLine($"      {t}");
        }

        if (e.StandardTimings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("    Standard timings:");
            foreach (string t in e.StandardTimings) sb.AppendLine($"      {t}");
        }

        if (e.EstablishedTimings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("    Established timings:");
            foreach (string t in e.EstablishedTimings) sb.AppendLine($"      {t}");
        }
    }

    /// <summary>
    /// What the monitor itself says it can be told to do.
    /// </summary>
    /// <remarks>
    /// The point of the whole report. Everything above is what Windows knows
    /// about the display; this is what the panel knows about itself, and it is
    /// routinely far more — contrast, colour presets, RGB gains, sharpness,
    /// which input it is showing, its own OSD language, its power state. None
    /// of it appears anywhere in Windows.
    /// <para>
    /// Manufacturer-specific codes are recorded and never written. Their
    /// meaning is undocumented and differs between models, so the report is the
    /// place to notice them, not the place to start experimenting.
    /// </para>
    /// </remarks>
    private static void DescribeMonitorControls(StringBuilder sb, DisplayInfo d)
    {
        Section(sb, "What the monitor reports over DDC/CI");

        if (d.IsInternal)
        {
            sb.AppendLine("    A built-in panel has no DDC/CI channel.");
            return;
        }

        MonitorCapability cap;
        try
        {
            cap = MonitorCapabilities.Read(d);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"    Read failed: {ex.Message}");
            return;
        }

        if (!cap.Supported)
        {
            sb.AppendLine("    This monitor answered no capabilities string.");
            return;
        }

        Line(sb, "Model (per the monitor)", cap.Model ?? "(not given)");
        Line(sb, "Panel type", cap.Type ?? "(not given)");
        Line(sb, "MCCS version", cap.MccsVersion ?? "(not given)");
        Line(sb, "Commands accepted", cap.Commands.Count > 0 ? string.Join(" ", cap.Commands) : "(none listed)");
        Line(sb, "Controls reported", cap.Controls.Count.ToString());

        int settable = 0;
        foreach (VcpControl c in cap.Controls) if (c.Settable) settable++;
        Line(sb, "DispCtrl will offer", $"{settable} of them");

        sb.AppendLine();
        sb.AppendLine("    Every code this monitor lists. \"Set\" means DispCtrl will write it;");
        sb.AppendLine("    a blank there is a code that is read and reported, never written.");
        sb.AppendLine();
        sb.AppendLine("    code  set  control                                    kind         now");
        sb.AppendLine("    ----  ---  -----------------------------------------  -----------  ------------------");

        foreach (VcpControl c in cap.Controls)
        {
            string kind = c.Kind switch
            {
                VcpKind.Continuous => "range",
                VcpKind.Discrete => "list",
                _ => "read-only",
            };

            sb.AppendLine($"    {c.Hex}  {(c.Settable ? "yes" : "   ")}  {c.Name,-41}  {kind,-11}  {c.Display}");

            if (c.Kind == VcpKind.Continuous && c.Maximum > 0)
                sb.AppendLine($"                 accepts 0 to {c.Maximum}");

            if (c.Values.Count > 0)
            {
                var choices = new List<string>(c.Values.Count);
                foreach (VcpValue v in c.Values) choices.Add($"0x{v.Value:X2} {v.Name}");
                sb.AppendLine($"                 accepts: {string.Join(", ", choices)}");
            }

            if (!c.Settable && c.Kind != VcpKind.Information)
                sb.AppendLine($"                 not written: {WhyNotSettable(c)}");

            if (c.Settable) sb.AppendLine($"                 dispctrl vcp {c.Hex} <value> --display <n>");
        }

        sb.AppendLine();
        sb.AppendLine("    Capabilities string, verbatim:");
        sb.AppendLine($"    {cap.Raw}");
    }

    /// <summary>Why a control the monitor lists is not one DispCtrl will write.</summary>
    /// <remarks>
    /// Worth saying rather than leaving the column blank. Both reasons are
    /// deliberate, and one of them is a fact about the monitor rather than about
    /// DispCtrl: a panel that cannot say which of its own settings it is on has
    /// contradicted its own capabilities string, and writing a listed value
    /// would be acting on an assumption it has just disproved.
    /// </remarks>
    private static string WhyNotSettable(VcpControl c)
    {
        if (!VcpControl.IsAllowed(c.Code))
            return "manufacturer-specific or undocumented; recorded, never written";

        return c.Current < 0
            ? "the monitor would not answer a read of it"
            : $"it answered 0x{c.CurrentValue:X2}, which is not one of the values it listed";
    }

    private static void DescribeSignal(StringBuilder sb, DisplayInfo d)
    {
        DisplayDetail detail;
        try
        {
            detail = DisplayDetails.Read(d);
        }
        catch (Exception ex)
        {
            Section(sb, "Signal");
            Line(sb, "Error", ex.Message);
            return;
        }

        Section(sb, "Signal");
        Line(sb, "Active signal mode", detail.ActiveSignalMode);
        Line(sb, "Desktop mode", detail.DesktopMode);
        Line(sb, "Bit depth", detail.BitDepth);
        Line(sb, "Colour format", detail.ColorFormat);
        Line(sb, "Colour space", detail.ColorSpace);
        Line(sb, "Scan type", detail.ScanLineOrdering);
        Line(sb, "Pixel clock", detail.PixelClock);
    }

    private static void DescribeModes(StringBuilder sb, DisplayInfo d)
    {
        List<DisplayMode> modes;
        try
        {
            modes = DisplayModes.Available(d.GdiName);
        }
        catch (Exception ex)
        {
            Section(sb, "Modes");
            Line(sb, "Error", ex.Message);
            return;
        }

        Section(sb, $"Modes the driver reports ({modes.Count})");

        // Grouped by resolution rather than listed flat: a monitor reporting
        // 163 modes is mostly the same dozen resolutions at different rates,
        // and the useful question is "what can this resolution run at".
        var byResolution = new Dictionary<(uint W, uint H), SortedSet<uint>>();
        uint bestRate = 0;
        (uint W, uint H) bestSize = (0, 0);

        foreach (DisplayMode m in modes)
        {
            if (!byResolution.TryGetValue((m.Width, m.Height), out SortedSet<uint>? rates))
            {
                rates = [];
                byResolution[(m.Width, m.Height)] = rates;
            }

            rates.Add(m.RefreshHz);

            if (m.RefreshHz > bestRate) bestRate = m.RefreshHz;
            if (m.Width * (long)m.Height > bestSize.W * (long)bestSize.H) bestSize = (m.Width, m.Height);
        }

        Line(sb, "Highest resolution", $"{bestSize.W} x {bestSize.H}");
        Line(sb, "Highest refresh rate", $"{bestRate} Hz (at some resolution)");

        if (byResolution.TryGetValue(bestSize, out SortedSet<uint>? nativeRates))
            Line(sb, "Rates at that resolution", string.Join(", ", nativeRates.Reverse()) + " Hz");

        sb.AppendLine();

        var sizes = new List<(uint W, uint H)>(byResolution.Keys);
        sizes.Sort((a, b) => (b.W * (long)b.H).CompareTo(a.W * (long)a.H));

        foreach ((uint w, uint h) in sizes)
            sb.AppendLine($"    {w,5} x {h,-5}  {string.Join(", ", byResolution[(w, h)].Reverse())} Hz");
    }

    private static void DescribeControls(StringBuilder sb, DisplayInfo d)
    {
        Section(sb, "Controls DispCtrl can drive");

        try
        {
            BrightnessRange brightness = Brightness.Read(d);
            Line(sb, "Brightness", brightness.Supported
                ? $"yes, {brightness.Current}% now (range {brightness.Min}-{brightness.Max}), "
                  + (d.IsInternal ? "over WMI" : "over DDC/CI")
                : "not reported by this panel");
        }
        catch (Exception ex)
        {
            Line(sb, "Brightness", $"read failed: {ex.Message}");
        }

        try
        {
            HdrState hdr = AdvancedDisplay.ReadHdr(d);
            Line(sb, "HDR", hdr.Supported
                ? $"supported, currently {(hdr.Enabled ? "on" : "off")}, {hdr.BitsPerChannel} bits per channel"
                : "not supported");
        }
        catch (Exception ex)
        {
            Line(sb, "HDR", $"read failed: {ex.Message}");
        }

        try
        {
            ScalingState scaling = AdvancedDisplay.ReadScaling(d);
            Line(sb, "Scaling", scaling.Supported
                ? $"{scaling.Current}% now, recommended {scaling.Recommended}%, "
                  + $"offered: {string.Join(", ", scaling.Available)}"
                : "not adjustable through the undocumented CCD path");
        }
        catch (Exception ex)
        {
            Line(sb, "Scaling", $"read failed: {ex.Message}");
        }

        try
        {
            VrrState vrr = VariableRefreshRate.Read(d);
            Line(sb, "Variable refresh", vrr.Capable
                ? $"panel advertises {vrr.MinHz}-{vrr.MaxHz} Hz; Windows' setting is currently "
                  + $"{(vrr.Enabled ? "on" : "off")} (it is global, not per display)"
                : "the EDID advertises no variable range");
        }
        catch (Exception ex)
        {
            Line(sb, "Variable refresh", $"read failed: {ex.Message}");
        }

        try
        {
            Line(sb, "Colour profile", ColorProfile.ReadName(d) ?? "system default");
        }
        catch (Exception ex)
        {
            Line(sb, "Colour profile", $"read failed: {ex.Message}");
        }

        try
        {
            Line(sb, "Night light", NightLight.Peaks(d) is { } p
                ? $"gamma control works; ramp peaks now R {p.R:P1} G {p.G:P1} B {p.B:P1}"
                : "this driver will not report a gamma ramp");
        }
        catch (Exception ex)
        {
            Line(sb, "Night light", $"read failed: {ex.Message}");
        }

        try
        {
            string? wallpaper = Wallpaper.Read(d);
            Line(sb, "Wallpaper", wallpaper ?? "(none reported)");
            Line(sb, "Per-monitor wallpaper", Wallpaper.SupportsPerMonitor ? "yes" : "no — changes apply to every display");
        }
        catch (Exception ex)
        {
            Line(sb, "Wallpaper", $"read failed: {ex.Message}");
        }

        Line(sb, "Taskbar hiding", d.IsPrimary
            ? "no — Explorer restores the primary bar immediately; Windows' own auto-hide is used instead"
            : "yes");
    }

    private static void Section(StringBuilder sb, string title)
    {
        sb.AppendLine();
        sb.AppendLine($"  {title}");
        sb.AppendLine($"  {new string('-', title.Length)}");
    }

    private static void Line(StringBuilder sb, string label, string value) =>
        sb.AppendLine($"    {label,-24} {value}");
}
