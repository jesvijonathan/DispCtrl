using System.Text.Json;
using DispCtrl.Core.Devices;
using DispCtrl.Core.Displays;

namespace DispCtrl.Display.Ddc;

/// <summary>Applies the same reviewed/local mappings to every brand's controls.</summary>
public static class DeviceControls
{
    public static MonitorCapability Apply(DisplayInfo display, MonitorCapability capability, IReadOnlyDictionary<byte, ResolvedControl>? mappings = null)
    {
        if (display.IsInternal) return capability;
        mappings ??= DeviceLibrary.Resolve(display.Key.Model);
        if (mappings.Count == 0) return capability;
        var controls = capability.Controls.Select(c => mappings.TryGetValue(c.Code, out var m)
            && m.Definition.DdcWrite is null ? Apply(c, m.Definition, m.Origin,
                m.Origin.EndsWith(" " + display.Key.Model, StringComparison.OrdinalIgnoreCase)) : c).ToList();
        if (DeviceDefinitions.IsLgModel(display.Key.Model) && mappings.TryGetValue(0x60, out var input)
            && input.Definition.DdcWrite is not null)
        {
            controls.RemoveAll(c => c.Code == 0x60);
            controls.Add(LgInput.Control(input.Definition) with { MappingOrigin = input.Origin });
            controls.Sort((a, b) => a.Code.CompareTo(b.Code));
        }
        return capability with { Supported = capability.Supported || controls.Any(c => c.WriteOnly), Controls = controls };
    }

    public static VcpControl Apply(VcpControl reported, DefinedControl mapping, string? origin = null, bool ownModel = false)
    {
        // One rule for which values are offered, shared with the library's checks.
        VcpValue[] values = mapping.Kind is DefinedKinds.Range or DefinedKinds.Action ? []
            : DeviceDefinitions.OfferedValues(reported.Values.Select(v => (v.Value, v.Name)).ToList(), mapping, ownModel)
                .Select(v => new VcpValue(v.Value, v.Name)).ToArray();
        return reported with
        {
            Name = mapping.Name,
            Kind = mapping.Kind switch
            {
                DefinedKinds.Range => VcpKind.Continuous,
                DefinedKinds.Choice => VcpKind.Discrete,
                _ => VcpKind.Information,
            },
            Values = values,
            Maximum = mapping.Maximum is int max
                ? reported.Maximum >= 0 ? Math.Min(max, reported.Maximum) : max : reported.Maximum,
            MappedDefinition = mapping, MappingOrigin = origin, FullValue = values.Any(v => v.Value > 0xFF),
            MappedWritable = mapping.Writable,
            MappingSnapshot = JsonSerializer.Serialize(mapping, DeviceJsonContext.Default.DefinedControl),
        };
    }

    /// <summary>Rechecks an already displayed/validated mapping before any hardware write.</summary>
    public static string? ValidateWrite(VcpControl expected, DefinedControl? current, uint value)
    {
        if (expected.WriteOnly != (current?.DdcWrite is not null)) return "The input mapping changed. Refresh the controls before switching input.";
        bool mapped = expected.MappedDefinition is not null || expected.MappedWritable || expected.MappingSnapshot is not null;
        if (mapped && current is null)
            return "This mapping was removed. Refresh the controls before using it.";
        if (!mapped && current is not null)
            return "This control's mapping changed. Refresh the controls before using it.";
        if (expected.Code == 0x04) return "Factory reset requires the dedicated confirmation command.";
        if (value > ushort.MaxValue) return "Monitor values must be between 0 and 65535.";
        if (current is null) return expected.Settable ? null : "This control is read-only.";
        if (!current.Writable || current.Kind == DefinedKinds.Information) return "This mapping is read-only. Enable it after confirming what the control does.";
        if (expected.MappingSnapshot is { } snapshot
            && JsonSerializer.Serialize(current, DeviceJsonContext.Default.DefinedControl) != snapshot)
            return "The control mapping changed. Refresh the controls before writing.";
        if (expected.MappedDefinition is { } before && before.Kind != current.Kind)
            return "This mapping's control type changed. Refresh the controls before using it.";
        // The snapshot above ties the offered values to this mapping; they also
        // keep listed values nobody has named, by number.
        if (current.Kind == DefinedKinds.Choice && !expected.Values.Any(v => v.Value == value))
            return "This monitor did not offer that value. Refresh the controls.";
        if (current.Kind == DefinedKinds.Range && (current.Maximum is int max && value > max
            || expected.Maximum >= 0 && value > expected.Maximum)) return "The value exceeds this control's maximum.";
        return null;
    }
}
