using DispCtrl.Core.Settings;

namespace DispCtrl.Core.Presets;

/// <summary>
/// Export-only snapshot of everything DispCtrl knows about the current desk.
/// </summary>
/// <remarks>
/// The hardware snapshot records live layout, modes, monitor serials and VCP
/// values. Settings carries every shared, taskbar, protection, shortcut and
/// per-monitor value. Used only by the preview page of a build with presets
/// switched off (<c>FeatureFlags.Presets</c>), where it is export only.
/// </remarks>
public sealed class CurrentConfigurationExport
{
    public int Version { get; set; } = 1;
    public DateTimeOffset ExportedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DispCtrlSettings Settings { get; set; } = new();
    public Preset CurrentDesk { get; set; } = new() { Name = "Current configuration" };
}
