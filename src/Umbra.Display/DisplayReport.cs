using System.Text;
using Umbra.Core.Displays;
using Umbra.Core.Settings;

namespace Umbra.Display;

/// <summary>
/// Writes everything Umbra can discover about every attached display to one
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

        sb.AppendLine("Umbra display report");
        sb.AppendLine($"Written {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"{displays.Count} display(s) attached");
        sb.AppendLine();

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

        return sb.ToString();
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
        Line(sb, "Built in", d.IsInternal ? "yes" : "no");
        Line(sb, "Main display", d.IsPrimary ? "yes" : "no");

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
        Line(sb, "Umbra will offer", $"{settable} of them");

        sb.AppendLine();
        sb.AppendLine("    code  set  control                                    now");
        sb.AppendLine("    ----  ---  -----------------------------------------  ---------------------------");

        foreach (VcpControl c in cap.Controls)
        {
            sb.AppendLine($"    {c.Hex}  {(c.Settable ? "yes" : "   ")}  {c.Name,-41}  {c.Display}");

            if (c.Values.Count > 0)
                sb.AppendLine($"                 choices: {string.Join(", ", c.Values)}");
        }

        sb.AppendLine();
        sb.AppendLine("    Capabilities string, verbatim:");
        sb.AppendLine($"    {cap.Raw}");
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
        Section(sb, "Controls Umbra can drive");

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
