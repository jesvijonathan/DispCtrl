using System.Collections.Concurrent;
using Microsoft.Win32;

namespace DispCtrl.Core.Displays;

/// <summary>The graphics adapter a display hangs off, as Windows records it.</summary>
/// <param name="InstanceId">The adapter's device instance, as Device Manager shows it (<c>PCI\VEN_10DE...</c>, <c>ROOT\DISPLAY\0000</c>).</param>
/// <param name="Name">What the driver calls the adapter.</param>
/// <param name="Provider">Who wrote the driver.</param>
/// <param name="DriverVersion">The driver's version.</param>
/// <param name="Software">
/// No hardware under it: a root-enumerated adapter, which is how a virtual
/// display driver (Parsec, spacedesk, an IddCx driver) appears. A GPU sits on a
/// bus (PCI); nothing physical is enumerated from the root.
/// </param>
/// <remarks>
/// The display's own name and connector are whatever its driver reports, and
/// a virtual display can claim to be any monitor on any connector: the one on
/// this desk says HDMI. The adapter is the part it cannot dress up, so it is
/// what "virtual" is decided by. Read from the registry, no rights needed, and
/// cached per adapter: it describes the installed driver, not the moment.
/// </remarks>
public sealed record GraphicsAdapter(string InstanceId, string Name, string Provider, string DriverVersion, bool Software)
{
    public static readonly GraphicsAdapter Unknown = new("", "", "", "", false);

    private static readonly ConcurrentDictionary<string, GraphicsAdapter> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The adapter behind an adapter device path such as <c>\\?\PCI#VEN_10DE&amp;...#{guid}</c>.</summary>
    public static GraphicsAdapter For(string devicePath) =>
        string.IsNullOrEmpty(devicePath) ? Unknown : Cache.GetOrAdd(devicePath, Read);

    /// <summary>The device instance from an interface path: <c>\\?\ROOT#DISPLAY#0000#{guid}</c> is <c>ROOT\DISPLAY\0000</c>.</summary>
    public static string InstanceFromPath(string devicePath)
    {
        string p = devicePath.StartsWith(@"\\?\", StringComparison.Ordinal) ? devicePath[4..] : devicePath;
        int guid = p.LastIndexOf("#{", StringComparison.Ordinal);
        if (guid > 0) p = p[..guid];
        return p.Replace('#', '\\');
    }

    private static GraphicsAdapter Read(string devicePath)
    {
        string instance = InstanceFromPath(devicePath);
        bool software = instance.StartsWith(@"ROOT\", StringComparison.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows()) return new(instance, "", "", "", software);
        try
        {
            using RegistryKey? device = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + instance);
            string name = Plain(device?.GetValue("FriendlyName") as string) is { Length: > 0 } friendly ? friendly : Plain(device?.GetValue("DeviceDesc") as string);
            string provider = "", version = "";
            if (device?.GetValue("Driver") is string driver)
            {
                using RegistryKey? cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\" + driver);
                provider = cls?.GetValue("ProviderName") as string ?? "";
                version = cls?.GetValue("DriverVersion") as string ?? "";
                if (name.Length == 0) name = cls?.GetValue("DriverDesc") as string ?? "";
            }
            return new(instance, name, provider, version, software);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return new(instance, "", "", "", software);
        }
    }

    /// <summary>"@oem12.inf,%devicename%;Virtual Display Driver" is "Virtual Display Driver".</summary>
    internal static string Plain(string? value) =>
        value is null ? "" : value.LastIndexOf(';') is int at and >= 0 ? value[(at + 1)..].Trim() : value.Trim();
}
