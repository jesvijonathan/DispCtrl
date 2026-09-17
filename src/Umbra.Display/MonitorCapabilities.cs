using System.Text;
using Umbra.Core.Displays;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Umbra.Display;

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

    /// <summary>Whether Umbra will offer to change this control. See the allow list.</summary>
    public bool Settable => Kind != VcpKind.Information && Settables.Contains(Code);

    /// <summary>
    /// The codes Umbra is willing to write.
    /// </summary>
    /// <remarks>
    /// An allow list, not a block list, and deliberately so. A monitor's
    /// capabilities string includes manufacturer-specific codes whose meaning
    /// is undocumented and differs between models; writing one to find out what
    /// it does is how a panel ends up in a state its own OSD cannot undo. They
    /// are reported, never written.
    /// </remarks>
    private static readonly HashSet<byte> Settables =
        [0x0C, 0x10, 0x12, 0x14, 0x16, 0x18, 0x1A, 0x60, 0x62, 0x6C, 0x6E, 0x70,
         0x87, 0x8D, 0xCA, 0xCC, 0xD6];

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
/// Controls offered to the user are drawn from this list, so Umbra never shows
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
        [0xDF] = ("MCCS version", VcpKind.Information),
    };

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
    public static MonitorCapability Read(DisplayInfo display, bool readValues = true)
    {
        if (display.IsInternal) return MonitorCapability.None;

        string? raw = ReadString(display);
        if (string.IsNullOrWhiteSpace(raw)) return MonitorCapability.None;

        MonitorCapability parsed = Parse(raw);
        if (!readValues || parsed.Controls.Count == 0) return parsed;

        ReadCurrentValues(display, parsed.Controls);
        return parsed;
    }

    // ------------------------------------------------------------ reading --

    private static unsafe string? ReadString(DisplayInfo display) =>
        WithPhysicalMonitor<string?>(display, handle =>
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

    private static unsafe void ReadCurrentValues(DisplayInfo display, IReadOnlyList<VcpControl> controls)
    {
        _ = WithPhysicalMonitor<bool>(display, handle =>
        {
            foreach (VcpControl c in controls)
            {
                uint current = 0, max = 0;
                MC_VCP_CODE_TYPE type = default;

                // One handle for the whole sweep: opening and closing the
                // channel per control is both slower and, on some monitors,
                // enough to make them start refusing requests.
                if (PInvoke.GetVCPFeatureAndVCPFeatureReply(handle, c.Code, &type, &current, &max) == 0)
                    continue;

                c.Current = (int)current;
                c.Maximum = (int)max;
            }

            return true;
        }, false);
    }

    private static unsafe T WithPhysicalMonitor<T>(DisplayInfo display, Func<HANDLE, T> work, T fallback)
    {
        var hmon = new HMONITOR((void*)display.Handle);
        if (hmon.IsNull) return fallback;

        if (!PInvoke.GetNumberOfPhysicalMonitorsFromHMONITOR(hmon, out uint count) || count == 0)
            return fallback;

        var monitors = new PHYSICAL_MONITOR[count];
        if (!PInvoke.GetPhysicalMonitorsFromHMONITOR(hmon, monitors)) return fallback;

        try
        {
            return work(monitors[0].hPhysicalMonitor);
        }
        catch (Exception)
        {
            return fallback;
        }
        finally
        {
            // Leaking the handle holds the DDC/CI channel open, after which the
            // monitor refuses later requests.
            foreach (PHYSICAL_MONITOR m in monitors)
                PInvoke.DestroyPhysicalMonitor(m.hPhysicalMonitor);
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
                        if (byte.TryParse(token, System.Globalization.NumberStyles.HexNumber,
                                null, out byte value))
                        {
                            values.Add(new VcpValue(value, NameFor(code, value)));
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
    public static bool Write(DisplayInfo display, byte code, uint value) =>
        WithPhysicalMonitor(display, handle => PInvoke.SetVCPFeature(handle, code, value) != 0, false);
}
