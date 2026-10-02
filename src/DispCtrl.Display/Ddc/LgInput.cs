using DispCtrl.Core.Devices;
using DispCtrl.Core.Displays;

namespace DispCtrl.Display.Ddc;

/// <summary>Model-scoped LG input mappings; discovery never probes this write-only channel.</summary>
public static class LgInput
{
    public readonly record struct Result(bool Sent, string? Error)
    {
        public static Result Failed(string message) => new(false, message);
    }

    public static DefinedControl? Mapping(DisplayInfo display)
    {
        if (display.IsInternal || !DeviceDefinitions.IsLgModel(display.Key.Model)) return null;
        return DeviceLibrary.Resolve(display.Key.Model).TryGetValue(0x60, out var mapped)
            && mapped.Definition.DdcWrite is not null ? mapped.Definition : null;
    }

    /// <summary>A write-only input from a validated, resolved model mapping.</summary>
    public static VcpControl Control(DefinedControl mapping) => new(0x60, mapping.Name, VcpKind.Discrete,
        mapping.Values.Select(v => new VcpValue((byte)v.Number!.Value, v.Name)).ToArray())
        { WriteOnly = true, MappedWritable = mapping.Writable, MappedDefinition = mapping,
            MappingSnapshot = System.Text.Json.JsonSerializer.Serialize(mapping, DeviceJsonContext.Default.DefinedControl) };

    // Includes the source byte, not the bus destination. 0x6E is the shifted
    // DDC/CI bus address (0x37), and participates in the checksum.
    public static byte[] Packet(byte sourceAddress, byte code, ushort value)
    {
        byte[] packet = [sourceAddress, 0x84, 0x03, code, (byte)(value >> 8), (byte)value, 0];
        byte checksum = 0x6E;
        for (int i = 0; i < packet.Length - 1; i++) checksum ^= packet[i];
        packet[^1] = checksum;
        return packet;
    }

    internal static Result Write(DisplayInfo display, DefinedControl mapping, uint value)
    {
        if (!mapping.Writable || !mapping.Values.Any(v => v.Number == value) || value > 255)
            return Result.Failed("This LG input is not enabled in the model's writable input mapping.");
        return DdcChannel.Locked(display,
            () => RawDdc.Write(display, Packet(0x50, 0xF4, (ushort)value)),
            Result.Failed("DDC access is blocked or busy for this monitor."), risky: true);
    }

    internal static (DisplayInfo? Display, string? Error) ResolveOutput(DisplayInfo display, IReadOnlyList<DisplayInfo> live)
    {
        DisplayInfo? current = null;
        foreach (DisplayInfo candidate in live)
        {
            if (candidate.Key != display.Key) continue;
            if (current is not null) return (null, "The selected monitor is no longer uniquely attached.");
            current = candidate;
        }
        if (current is null) return (null, "The selected monitor is no longer attached.");
        if (current.IsInternal || string.IsNullOrEmpty(current.GdiName) || current.SharingConnector > 0
            || live.Count(d => d.GdiName.Equals(current.GdiName, StringComparison.OrdinalIgnoreCase)) != 1)
            return (null, "LG alternate input switching requires an external, unique output without cloning or shared MST connectors.");
        return (current, null);
    }
}
