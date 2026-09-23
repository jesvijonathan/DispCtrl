using System.Text;
using System.Diagnostics;
using DispCtrl.Core.Caching;
using DispCtrl.Core.Displays;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace DispCtrl.Display;

/// <summary>How a VCP control behaves, and therefore what control to draw for it.</summary>
public enum VcpKind
{
    /// <summary>A value anywhere between zero and a maximum. A slider.</summary>
    Continuous,

    /// <summary>A fixed set of values the monitor named. A list.</summary>
    Discrete,

    /// <summary>Readable but not worth setting — hours used, firmware level.</summary>
    Information,
}

/// <summary>One control a monitor says it supports.</summary>
/// <param name="Code">The MCCS VCP code, e.g. 0x12 for contrast.</param>
/// <param name="Name">What it is, in words.</param>
/// <param name="Kind">Which sort of control to offer.</param>
/// <param name="Values">
/// For a discrete control, the values the monitor listed, with names where the
/// standard gives them.
/// </param>
public sealed record VcpControl(byte Code, string Name, VcpKind Kind, IReadOnlyList<VcpValue> Values)
{
    /// <summary>Current value, once read. -1 when it has not been.</summary>
    public int Current { get; set; } = -1;

    /// <summary>Maximum for a continuous control. -1 when unknown.</summary>
    public int Maximum { get; set; } = -1;

    public string Hex => $"0x{Code:X2}";

    /// <summary>
    /// The current value reduced to the byte that actually carries it.
    /// </summary>
    /// <remarks>
    /// A VCP reply is 16 bits, and for a discrete control the meaning is in the
    /// low byte — the high byte carries whatever the monitor felt like. This
    /// Dell answers 0x1111 for "input source is HDMI 1", where only the 0x11
    /// means anything. Matching the raw word against the listed values finds
    /// nothing and the control reads as being on a setting it does not have.
    /// </remarks>
    public int CurrentValue => Current < 0 ? -1 : Current & 0xFF;

    /// <summary>The listed value the control is currently on, if any.</summary>
    public VcpValue? CurrentOption
    {
        get
        {
            foreach (VcpValue v in Values)
                if (v.Value == CurrentValue) return v;

            return null;
        }
    }

    /// <summary>Whether DispCtrl will offer to change this control.</summary>
    /// <remarks>
    /// Two conditions, and the second matters as much as the first. A discrete
    /// control is only offered when the monitor's current reading is one of the
    /// values it listed — because a panel that cannot say which of its own
    /// settings it is on is not implementing the code, and writing one of the
    /// listed values would be acting on an assumption the hardware has just
    /// contradicted.
    /// <para>
    /// This Dell is the case in point: it advertises 0x66, the ambient light
    /// sensor, listing 0x0F and 0x02 — then answers 0xA1, which is neither, and
    /// never changes. The code is in its capabilities string and is not a
    /// working control.
    /// </para>
    /// </remarks>
    public bool Settable =>
        Kind != VcpKind.Information
        && VcpControl.Settables.Contains(Code)
        && (Kind == VcpKind.Continuous || CurrentOption is not null);

    /// <summary>
    /// The codes DispCtrl is willing to write.
    /// </summary>
    /// <remarks>
    /// An allow list, not a block list, and deliberately so. A monitor's
    /// capabilities string includes manufacturer-specific codes whose meaning
    /// is undocumented and differs between models; writing one to find out what
    /// it does is how a panel ends up in a state its own OSD cannot undo. They
    /// are reported, never written.
    /// </remarks>
    /// <summary>Whether a code is one DispCtrl is willing to write at all.</summary>
    /// <remarks>
    /// Separate from <see cref="Settable"/>, which also asks whether this
    /// particular monitor is answering sensibly. The report needs to tell the
    /// two apart to say why a control is not offered.
    /// </remarks>
    public static bool IsAllowed(byte code) => Settables.Contains(code);

    internal static readonly HashSet<byte> Settables =
        [0x0C, 0x10, 0x12, 0x14, 0x16, 0x18, 0x1A, 0x60, 0x62, 0x66, 0x6C, 0x6E, 0x70,
         0x72, 0x87, 0x8D, 0xCA, 0xCC, 0xD6, 0xDC];

    /// <summary>A human reading of the current value.</summary>
    public string Display
    {
        get
        {
            if (Current < 0) return "could not be read";
            if (CurrentOption is { } option) return option.Name;

            return Kind switch
            {
                VcpKind.Continuous when Maximum > 0 => $"{Current} of {Maximum}",
                VcpKind.Discrete => $"0x{CurrentValue:X2} (not one of the values it listed)",
                _ => Decode(Code, Current),
            };
        }
    }

    /// <summary>Turns the handful of packed information codes into words.</summary>
    private static string Decode(byte code, int value) => code switch
    {
        // Both halves are meaningful: major.minor for firmware, and a
        // manufacturer/type pair for the controller.
        0xC9 => $"{value >> 8}.{value & 0xFF}",
        0xDF => $"MCCS {value >> 8}.{value & 0xFF}",
        0xC8 => $"type 0x{value & 0xFF:X2}",
        0xC0 => $"{value} hours",
        0xB6 => (value & 0xFF) switch
        {
            1 => "CRT", 2 => "CRT", 3 => "LCD (TFT)", 4 => "LCos",
            5 => "Plasma", 6 => "OLED", 7 => "EL", 8 => "MEM",
            _ => $"0x{value & 0xFF:X2}",
        },
        0xB2 => (value & 0xFF) switch
        {
            1 => "RGB vertical stripe", 2 => "RGB vertical stripe with blue",
            3 => "Red/blue vertical stripe", 4 => "Delta triad",
            5 => "Mosaic", _ => $"0x{value & 0xFF:X2}",
        },
        _ => $"{value}",
    };
}

/// <param name="Value">The raw value to write.</param>
/// <param name="Name">What it means, where the standard says.</param>
public readonly record struct VcpValue(byte Value, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Everything one monitor said about itself over DDC/CI.</summary>
public sealed record MonitorCapability
{
    public bool Supported { get; init; }

    /// <summary>The raw capabilities string, kept verbatim.</summary>
    /// <remarks>
    /// Worth keeping whole. Parsing is best-effort against a standard monitors
    /// follow loosely, so when something here looks wrong the original string
    /// is the only way to tell a parsing bug from a monitor's own oddity.
    /// </remarks>
    public string Raw { get; init; } = "";

    public string? Model { get; init; }
    public string? Type { get; init; }
    public string? MccsVersion { get; init; }

    /// <summary>Low-level commands the monitor accepts, as hex.</summary>
    public IReadOnlyList<string> Commands { get; init; } = [];

    public IReadOnlyList<VcpControl> Controls { get; init; } = [];

    public static MonitorCapability None => new() { Supported = false };
}

/// <summary>
/// Asks a monitor what it can do, rather than assuming.
/// </summary>
/// <remarks>
/// Every DDC/CI monitor publishes an MCCS capabilities string listing the VCP
/// codes it implements and, for the discrete ones, the values it will accept.
/// It is the only honest way to know whether a given panel can change its input
/// source, its colour preset or its volume — the alternative is writing codes
/// speculatively and watching what happens, which on some monitors means
/// writing to a manufacturer-specific code that does something unwelcome.
/// <para>
/// Controls offered to the user are drawn from this list, so DispCtrl never shows
/// a control the panel in front of you does not have.
/// </para>
/// </remarks>
public static class MonitorCapabilities
{
    /// <summary>
    /// The codes worth naming, from the MCCS standard.
    /// </summary>
    /// <remarks>
    /// Deliberately not exhaustive. A code the standard defines but which no
    /// consumer monitor implements adds noise to the report; a code left
    /// unnamed still appears, as its hex, so nothing is hidden.
    /// </remarks>
    private static readonly Dictionary<byte, (string Name, VcpKind Kind)> Known = new()
    {
        [0x02] = ("New control value", VcpKind.Information),
        [0x04] = ("Restore factory defaults", VcpKind.Information),
        [0x05] = ("Restore factory brightness and contrast", VcpKind.Information),
        [0x06] = ("Restore factory geometry", VcpKind.Information),
        [0x08] = ("Restore factory colour defaults", VcpKind.Information),
        [0x0B] = ("Colour temperature increment", VcpKind.Information),
        [0x0C] = ("Colour temperature request", VcpKind.Continuous),
        [0x10] = ("Brightness", VcpKind.Continuous),
        [0x12] = ("Contrast", VcpKind.Continuous),
        [0x14] = ("Colour preset", VcpKind.Discrete),
        [0x16] = ("Red gain", VcpKind.Continuous),
        [0x18] = ("Green gain", VcpKind.Continuous),
        [0x1A] = ("Blue gain", VcpKind.Continuous),
        [0x1E] = ("Auto setup", VcpKind.Information),
        [0x52] = ("Active control", VcpKind.Information),
        [0x66] = ("Ambient light sensor", VcpKind.Discrete),
        [0x72] = ("Gamma", VcpKind.Continuous),
        [0x60] = ("Input source", VcpKind.Discrete),
        [0x62] = ("Speaker volume", VcpKind.Continuous),
        [0x6C] = ("Red black level", VcpKind.Continuous),
        [0x6E] = ("Green black level", VcpKind.Continuous),
        [0x70] = ("Blue black level", VcpKind.Continuous),
        [0x87] = ("Sharpness", VcpKind.Continuous),
        [0x8D] = ("Audio mute", VcpKind.Discrete),
        [0xAA] = ("Screen orientation", VcpKind.Information),
        [0xAC] = ("Horizontal frequency", VcpKind.Information),
        [0xAE] = ("Vertical frequency", VcpKind.Information),
        [0xB2] = ("Flat panel sub-pixel layout", VcpKind.Information),
        [0xB6] = ("Display technology", VcpKind.Information),
        [0xC0] = ("Hours in use", VcpKind.Information),
        [0xC6] = ("Application enable key", VcpKind.Information),
        [0xC8] = ("Display controller type", VcpKind.Information),
        [0xC9] = ("Firmware level", VcpKind.Information),
        [0xCA] = ("OSD and power button lock", VcpKind.Discrete),
        [0xCC] = ("OSD language", VcpKind.Discrete),
        [0xD6] = ("Power mode", VcpKind.Discrete),
        [0xDC] = ("Picture mode", VcpKind.Discrete),
        [0xDF] = ("MCCS version", VcpKind.Information),
    };

    /// <summary>
    /// Gap between consecutive VCP reads on one monitor, in milliseconds.
    /// </summary>
    /// <remarks>
    /// The MCCS standard asks for 40ms between messages. Skipping it mostly
    /// works, which is what makes it dangerous: the failure is not an error but
    /// a reply belonging to the previous question.
    /// </remarks>
    private const int InterMessageMs = 40;

    /// <summary>Named values for the discrete controls worth offering.</summary>
    private static readonly Dictionary<byte, Dictionary<byte, string>> ValueNames = new()
    {
        [0x14] = new()
        {
            [0x01] = "sRGB", [0x02] = "Display native", [0x03] = "4000 K", [0x04] = "5000 K",
            [0x05] = "6500 K", [0x06] = "7500 K", [0x07] = "8200 K", [0x08] = "9300 K",
            [0x09] = "10000 K", [0x0A] = "11500 K", [0x0B] = "User 1", [0x0C] = "User 2",
            [0x0D] = "User 3",
        },
        [0x60] = new()
        {
            [0x01] = "VGA 1", [0x02] = "VGA 2", [0x03] = "DVI 1", [0x04] = "DVI 2",
            [0x05] = "Composite 1", [0x06] = "Composite 2", [0x07] = "S-Video 1",
            [0x08] = "S-Video 2", [0x09] = "Tuner 1", [0x0A] = "Tuner 2", [0x0B] = "Tuner 3",
            [0x0C] = "Component 1", [0x0D] = "Component 2", [0x0E] = "Component 3",
            [0x0F] = "DisplayPort 1", [0x10] = "DisplayPort 2", [0x11] = "HDMI 1",
            [0x12] = "HDMI 2", [0x13] = "USB-C",
        },
        [0xD6] = new()
        {
            [0x01] = "On", [0x02] = "Standby", [0x03] = "Suspend", [0x04] = "Off (soft)",
            [0x05] = "Off (hard)",
        },
        [0xCA] = new()
        {
            [0x01] = "Unlocked", [0x02] = "Locked",
        },
        [0x8D] = new()
        {
            [0x01] = "Mute", [0x02] = "Unmute",
        },
        [0x66] = new()
        {
            [0x01] = "Off", [0x02] = "On",
        },

        // MCCS calls this "display application"; every monitor OSD calls it
        // picture or preset mode, so that is what it is called here. The
        // "with ALS" variants are the monitor's own ambient light handling.
        [0xDC] = new()
        {
            [0x00] = "Standard", [0x01] = "Productivity", [0x02] = "Mixed",
            [0x03] = "Movie", [0x04] = "User", [0x05] = "Games", [0x06] = "Sports",
            [0x07] = "Professional",
            [0x08] = "Standard, auto brightness", [0x09] = "Productivity, auto brightness",
            [0x0A] = "Mixed, auto brightness", [0x0B] = "Movie, auto brightness",
            [0x0C] = "User, auto brightness", [0x0D] = "Games, auto brightness",
            [0x0E] = "Sports, auto brightness", [0x0F] = "Professional, auto brightness",
        },

        [0xCC] = new()
        {
            [0x01] = "Chinese (traditional)", [0x02] = "English", [0x03] = "French",
            [0x04] = "German", [0x05] = "Italian", [0x06] = "Japanese", [0x07] = "Korean",
            [0x08] = "Portuguese", [0x09] = "Russian", [0x0A] = "Spanish",
            [0x0B] = "Swedish", [0x0C] = "Turkish", [0x0D] = "Chinese (simplified)",
            [0x0E] = "Portuguese (Brazil)", [0x0F] = "Arabic", [0x10] = "Bulgarian",
            [0x11] = "Croatian", [0x12] = "Czech", [0x13] = "Danish", [0x14] = "Dutch",
            [0x15] = "Estonian", [0x16] = "Finnish", [0x17] = "Greek", [0x18] = "Hebrew",
            [0x19] = "Hindi", [0x1A] = "Hungarian", [0x1B] = "Latvian",
            [0x1C] = "Lithuanian", [0x1D] = "Norwegian", [0x1E] = "Polish",
            [0x1F] = "Romanian", [0x20] = "Serbian", [0x21] = "Slovak",
            [0x22] = "Slovenian", [0x23] = "Thai", [0x24] = "Ukrainian",
            [0x25] = "Vietnamese",
        },
    };

    /// <summary>
    /// Values for a discrete control the monitor named without listing any.
    /// </summary>
    /// <remarks>
    /// Plenty of monitors list a discrete code bare — this Dell lists
    /// <c>CA</c>, the OSD lock, with no values at all — leaving nothing to
    /// match the current reading against, so it reads as being on a setting it
    /// never offered. Falling back to what the standard defines for that code
    /// is better than showing a bare hex byte.
    /// </remarks>
    private static IReadOnlyList<VcpValue> StandardValues(byte code)
    {
        if (!ValueNames.TryGetValue(code, out Dictionary<byte, string>? names)) return [];

        var values = new List<VcpValue>(names.Count);
        foreach ((byte value, string name) in names) values.Add(new VcpValue(value, name));

        return values;
    }

    /// <summary>
    /// Asks the monitor what it supports, and reads the current value of each
    /// control it names.
    /// </summary>
    /// <remarks>
    /// Blocking, and not briefly: the capabilities string alone is several
    /// DDC/CI round trips, and every control read is another. Budget hundreds
    /// of milliseconds per monitor and never call this on the UI thread.
    /// <para>
    /// Internal panels have no DDC/CI at all, so they are skipped rather than
    /// made to time out.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Capabilities strings already read, by device path.
    /// </summary>
    /// <remarks>
    /// Cached for the life of the process because the string is a property of
    /// the monitor, not of its current state — it does not change while the
    /// panel is plugged in. Re-reading it is several DDC/CI round trips, and it
    /// was being paid on every drift check.
    /// </remarks>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> Strings = new();

    private sealed class ControlReadings
    {
        public readonly Lock Gate = new();
        public readonly Dictionary<byte, (int Current, int Maximum, long At)> Values = [];
    }
    private static readonly BoundedCache<(string Path, nint Handle), ControlReadings> Readings = new(32);
    private static readonly BoundedCache<string, MonitorCapability> Parsed = new(32);
    private static readonly TimeSpan ReadingLifetime = TimeSpan.FromSeconds(5);

    private static MonitorCapability Template(string raw)
    {
        MonitorCapability template = Parsed.Get(raw, TimeSpan.FromHours(1), () => Parse(raw));
        return template with { Controls = template.Controls.Select(control => control with { }).ToArray() };
    }

    /// <summary>Reads UI controls plus the four informational values actually shown.</summary>
    public static MonitorCapability ReadForUi(DisplayInfo display)
    {
        if (display.IsInternal) return MonitorCapability.None;
        string? raw = Capabilities(display);
        if (string.IsNullOrWhiteSpace(raw)) return MonitorCapability.None;
        MonitorCapability result = Template(raw);
        var visible = result.Controls.Where(control =>
            (VcpControl.IsAllowed(control.Code) && control.Code != 0x10)
            || control.Code is 0xB6 or 0xC9 or 0xC0 or 0xC8).ToList();
        ReadCurrentValues(display, visible, useCache: true);
        Devices.DeviceObserver.Listed(display, raw, visible);
        return result;
    }

    internal static void InvalidateAllValues() => Readings.Clear();

    /// <summary>Whether the MCCS standard names this code; everything else is left to the manufacturer.</summary>
    public static bool IsNamed(byte code) => Known.ContainsKey(code);

    public static void InvalidateValues(DisplayInfo display) => Readings.Remove((display.Key.DevicePath, display.Handle));

    public static MonitorCapability Read(DisplayInfo display, bool readValues = true)
    {
        if (display.IsInternal) return MonitorCapability.None;

        string? raw = Capabilities(display);
        if (string.IsNullOrWhiteSpace(raw)) return MonitorCapability.None;

        MonitorCapability parsed = Template(raw);
        if (!readValues || parsed.Controls.Count == 0) return parsed;

        ReadCurrentValues(display, parsed.Controls);
        Devices.DeviceObserver.Listed(display, raw, parsed.Controls);
        return parsed;
    }

    /// <summary>
    /// Reads these controls' values afresh, bypassing the cache.
    /// </summary>
    /// <remarks>
    /// For probing: someone changes a setting in the monitor's own menu and
    /// wants to see which code moved, which a cached reading would hide.
    /// </remarks>
    public static void ReadValues(DisplayInfo display, IReadOnlyList<VcpControl> controls)
    {
        foreach (VcpControl c in controls) c.Current = -1;
        ReadCurrentValues(display, controls, useCache: false);
    }

    /// <summary>
    /// Reads only the controls DispCtrl is willing to set.
    /// </summary>
    /// <remarks>
    /// A third of the round trips of a full read, and everything the preset
    /// machinery actually needs: a control that will never be written does not
    /// need its value captured. On the Dell this is 11 reads instead of 37.
    /// </remarks>
    public static MonitorCapability ReadSettable(DisplayInfo display, bool useCache = false)
    {
        if (display.IsInternal) return MonitorCapability.None;

        string? raw = Capabilities(display);
        if (string.IsNullOrWhiteSpace(raw)) return MonitorCapability.None;

        MonitorCapability parsed = Template(raw);

        // Settable is partly decided by the value read, so the candidates are
        // filtered on the allow list here and judged fully afterwards.
        var wanted = new List<VcpControl>();
        foreach (VcpControl c in parsed.Controls)
            if (c.Code != 0x10 && c.Kind != VcpKind.Information && VcpControl.Settables.Contains(c.Code)) wanted.Add(c);

        if (wanted.Count > 0) ReadCurrentValues(display, wanted, useCache);
        Devices.DeviceObserver.Listed(display, raw, wanted);

        return parsed;
    }

    /// <summary>
    /// The monitor's capabilities string, retried and cached only on success.
    /// </summary>
    /// <remarks>
    /// DDC/CI is a lossy channel over a wire that was not designed for it, and
    /// a capabilities read is the longest conversation DispCtrl ever has with a
    /// monitor — around a hundred round trips for a string of any size. Roughly
    /// one attempt in three on this Dell comes back with nothing, which is
    /// normal for the protocol and the reason ddcutil retries by default.
    /// <para>
    /// Not retrying showed up as a monitor that intermittently reported no
    /// controls at all: a device record claiming the panel answers nothing, a
    /// preset capturing none of the monitor's own settings, and the controls
    /// list on the Displays page simply empty. All three are silent wrong
    /// answers rather than visible failures, which is the worst shape for a bug
    /// like this to take.
    /// </para>
    /// <para>
    /// Only a successful read is cached. <c>GetOrAdd</c> stored the failure too,
    /// so within one process a single unlucky attempt meant the monitor was
    /// treated as mute until it was replugged.
    /// </para>
    /// </remarks>
    private static string? Capabilities(DisplayInfo display)
    {
        if (Strings.TryGetValue(display.Key.DevicePath, out string? cached)
            && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        for (int attempt = 1; attempt <= Attempts; attempt++)
        {
            string? raw = ReadString(display);

            if (!string.IsNullOrWhiteSpace(raw))
            {
                Strings[display.Key.DevicePath] = raw;
                return raw;
            }

            // Long enough for the monitor to finish whatever it was doing, and
            // short enough that three attempts still feel like one operation.
            if (attempt < Attempts) Thread.Sleep(RetryMs);
        }

        return null;
    }

    /// <summary>How many times a capabilities read is attempted before giving up.</summary>
    private const int Attempts = 3;

    private const int RetryMs = 150;

    /// <summary>Forgets the cached string, so a replugged monitor is asked afresh.</summary>
    public static void Forget(DisplayInfo display)
    {
        Strings.TryRemove(display.Key.DevicePath, out _);
        InvalidateValues(display);
    }

    // ------------------------------------------------------------ reading --

    private static unsafe string? ReadString(DisplayInfo display) =>
        DdcChannel.With<string?>(display, handle =>
        {
            // The raw overloads, which take the HANDLE directly and report
            // success as a non-zero int rather than a bool.
            uint length = 0;
            if (PInvoke.GetCapabilitiesStringLength(handle, &length) == 0 || length == 0) return null;

            // The monitor is allowed to be wrong about its own string length,
            // and a few are. Capping keeps a bad answer from asking for a
            // gigabyte, while still allowing the longest real string by far.
            if (length > 64 * 1024) return null;

            var buffer = new byte[length];
            fixed (byte* p = buffer)
            {
                if (PInvoke.CapabilitiesRequestAndCapabilitiesReply(handle, new PSTR(p), length) == 0)
                    return null;
            }

            int end = Array.IndexOf(buffer, (byte)0);
            if (end < 0) end = buffer.Length;

            // ASCII by specification, and treating it as such avoids a stray
            // high byte from a chatty monitor throwing mid-decode.
            return Encoding.ASCII.GetString(buffer, 0, end);
        }, null);

    private static unsafe void ReadCurrentValues(DisplayInfo display, IReadOnlyList<VcpControl> controls, bool useCache = false)
    {
        ControlReadings readings = Readings.Get((display.Key.DevicePath, display.Handle), TimeSpan.FromMinutes(10), () => new());
        // Share concurrent reads of the same panel, keeping separate panels independent.
        lock (readings.Gate)
        {
            var pending = new List<VcpControl>();
            foreach (VcpControl control in controls)
            {
                if (useCache && readings.Values.TryGetValue(control.Code, out var value)
                    && Stopwatch.GetElapsedTime(value.At) < ReadingLifetime)
                { control.Current = value.Current; control.Maximum = value.Maximum; }
                else pending.Add(control);
            }
            if (pending.Count == 0) return;
            _ = DdcChannel.With(display, handle =>
            {
                bool first = true;
                foreach (VcpControl control in pending)
                {
                    // Preserve the protocol gap; removing it produces incorrect replies.
                    if (!first) Thread.Sleep(InterMessageMs);
                    first = false;
                    uint current = 0, maximum = 0;
                    MC_VCP_CODE_TYPE type = default;
                    if (PInvoke.GetVCPFeatureAndVCPFeatureReply(handle, control.Code, &type, &current, &maximum) == 0) continue;
                    control.Current = (int)current;
                    control.Maximum = (int)maximum;
                }
                return true;
            }, false);
            long completed = Stopwatch.GetTimestamp();
            foreach (VcpControl control in pending)
                if (control.Current >= 0) readings.Values[control.Code] = (control.Current, control.Maximum, completed);
        }
    }

    // ------------------------------------------------------------ parsing --

    /// <summary>
    /// Pulls the parenthesised sections out of a capabilities string.
    /// </summary>
    /// <remarks>
    /// The format is nested parentheses — <c>(prot(monitor)vcp(10 12 14(01 05
    /// 08))…)</c> — with no escaping and no length prefixes, so it is walked
    /// by depth rather than matched with a pattern. Monitors are loose about
    /// whitespace and casing, and a few omit sections entirely, so everything
    /// here is best-effort and missing pieces are left null.
    /// </remarks>
    public static MonitorCapability Parse(string raw)
    {
        string? model = Section(raw, "model");
        string? type = Section(raw, "type");
        string? mccs = Section(raw, "mccs_ver");
        string? cmds = Section(raw, "cmds");
        string? vcp = Section(raw, "vcp") ?? Section(raw, "VCP");

        var commands = new List<string>();
        if (cmds is not null)
            foreach (string token in cmds.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                commands.Add("0x" + token.Trim().ToUpperInvariant());

        return new MonitorCapability
        {
            Supported = true,
            Raw = raw,
            Model = model,
            Type = type,
            MccsVersion = mccs,
            Commands = commands,
            Controls = vcp is null ? [] : ParseVcp(vcp),
        };
    }

    /// <summary>The contents of <c>name(...)</c>, or null when absent.</summary>
    private static string? Section(string raw, string name)
    {
        int at = 0;
        while (true)
        {
            at = raw.IndexOf(name + "(", at, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return null;

            // "vcp(" must not match the tail of "mvcp(" — a section name starts
            // at the beginning of the string or right after a delimiter.
            if (at == 0 || raw[at - 1] is '(' or ')' or ' ')
            {
                int open = at + name.Length;
                int close = MatchingParen(raw, open);
                if (close > open) return raw[(open + 1)..close].Trim();
            }

            at += name.Length;
        }
    }

    private static int MatchingParen(string raw, int open)
    {
        int depth = 0;
        for (int i = open; i < raw.Length; i++)
        {
            if (raw[i] == '(') depth++;
            else if (raw[i] == ')' && --depth == 0) return i;
        }

        return -1;
    }

    private static List<VcpControl> ParseVcp(string vcp)
    {
        var result = new List<VcpControl>();

        for (int i = 0; i < vcp.Length;)
        {
            if (!Uri.IsHexDigit(vcp[i])) { i++; continue; }

            int start = i;
            while (i < vcp.Length && Uri.IsHexDigit(vcp[i])) i++;

            // A code is two hex digits. Anything else is a monitor being
            // creative, and is skipped rather than guessed at.
            if (i - start is not 2 || !byte.TryParse(vcp.AsSpan(start, 2),
                    System.Globalization.NumberStyles.HexNumber, null, out byte code))
            {
                continue;
            }

            var values = new List<VcpValue>();
            if (i < vcp.Length && vcp[i] == '(')
            {
                int close = MatchingParen(vcp, i);
                if (close > i)
                {
                    foreach (string token in vcp[(i + 1)..close]
                                 .Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        // A token is normally one two-digit value, but monitors
                        // run them together: this Dell writes 66(0F02) meaning
                        // 0F and 02. Parsing the token whole overflows a byte
                        // and the values were dropped silently, which made a
                        // control the monitor does have look like one it does
                        // not. Even-length tokens are split into pairs.
                        for (int at = 0; at + 1 < token.Length || at < token.Length; at += 2)
                        {
                            int take = Math.Min(2, token.Length - at);
                            if (take <= 0) break;

                            if (byte.TryParse(token.AsSpan(at, take),
                                    System.Globalization.NumberStyles.HexNumber,
                                    null, out byte value))
                            {
                                values.Add(new VcpValue(value, NameFor(code, value)));
                            }
                        }
                    }

                    i = close + 1;
                }
            }

            (string name, VcpKind kind) = Known.TryGetValue(code, out var k)
                ? k
                : ($"Manufacturer-specific control {code:X2}", VcpKind.Information);

            // A code the standard calls continuous but which listed values is
            // discrete on this panel. The monitor's own answer wins.
            if (values.Count > 0 && kind == VcpKind.Continuous) kind = VcpKind.Discrete;

            // Discrete but listed bare — see StandardValues.
            IReadOnlyList<VcpValue> options =
                values.Count == 0 && kind == VcpKind.Discrete ? StandardValues(code) : values;

            result.Add(new VcpControl(code, name, kind, options));
        }

        return result;
    }

    private static string NameFor(byte code, byte value) =>
        ValueNames.TryGetValue(code, out Dictionary<byte, string>? names)
        && names.TryGetValue(value, out string? name)
            ? name
            : $"0x{value:X2}";

    // ------------------------------------------------------------ writing --

    /// <summary>Sets one VCP control on a monitor.</summary>
    /// <remarks>
    /// Only ever called with a code the monitor listed in its own capabilities
    /// string. Writing a speculative code is how a manufacturer-specific
    /// feature gets triggered by accident.
    /// </remarks>
    public static bool Write(DisplayInfo display, byte code, uint value)
    {
        using var stateChange = new DisplayStateChange();
        InvalidateValues(display);
        try { return DdcChannel.With(display, handle => PInvoke.SetVCPFeature(handle, code, value) != 0, false); }
        finally { InvalidateValues(display); }
    }

    /// <summary>
    /// Asks the monitor to restore its own factory settings.
    /// </summary>
    /// <remarks>
    /// The standard gives three codes for this and monitors implement different
    /// subsets: 04 restores everything, 05 just brightness and contrast, 08 just
    /// colour. Whichever the panel advertises is used, widest first, and one is
    /// enough — 04 covers what 05 and 08 do.
    /// <para>
    /// These are write-only triggers: the value is ignored, the act of writing
    /// is the command. Only sent when the monitor listed the code, like every
    /// other write here.
    /// </para>
    /// </remarks>
    public static bool RestoreFactory(DisplayInfo display)
    {
        if (display.IsInternal) return false;

        var available = new HashSet<byte>();
        try
        {
            foreach (VcpControl c in Read(display, readValues: false).Controls) available.Add(c.Code);
        }
        catch (Exception)
        {
            return false;
        }

        if (available.Contains(0x04)) return Write(display, 0x04, 1);

        bool any = false;
        if (available.Contains(0x05)) any |= Write(display, 0x05, 1);
        if (available.Contains(0x08)) any |= Write(display, 0x08, 1);

        return any;
    }
}
