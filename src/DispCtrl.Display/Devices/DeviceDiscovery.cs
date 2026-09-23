using System.Collections.Concurrent;
using DispCtrl.Core.Devices;
using DispCtrl.Core.Displays;

namespace DispCtrl.Display.Devices;

/// <summary>
/// Reads a monitor's codes the first time this PC sees its model, so the device
/// library fills itself without anybody pressing a button.
/// </summary>
/// <remarks>
/// Once per model, not per sighting: a capabilities read is seconds of DDC/CI,
/// and what it learns describes the model, not the moment. A model the person
/// removed from the list is left alone until they sync it back
/// (<see cref="DeviceHistoryEdits.Forget"/>). A monitor that does not answer
/// is tried once per process, so a panel with DDC/CI switched off in its menu
/// costs one attempt per engine start rather than one per rescan.
/// </remarks>
public static class DeviceDiscovery
{
    private static readonly ConcurrentDictionary<string, byte> Tried = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Attached models still missing their codes or public record.</summary>
    public static List<DisplayInfo> Unread(IEnumerable<DisplayInfo> displays) => displays
        .Where(d => DeviceDefinitions.IsModel(d.Key.Model))
        .GroupBy(d => d.Key.Model, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
        .Where(d => !Tried.ContainsKey(d.Key.Model) && NeedsLearning(d.Key.Model))
        .ToList();

    private static bool NeedsLearning(string model)
    {
        DeviceHistory history = DeviceHistory.Load();
        return !history.Forgotten.Contains(model, StringComparer.OrdinalIgnoreCase)
            && (!history.Models.TryGetValue(model, out SeenModel? seen) || seen.Record is null || DeviceHistoryEdits.NeedsReading(model));
    }

    /// <summary>Reads one monitor's capabilities, which records them; returns how many codes it listed.</summary>
    /// <remarks>
    /// Through the same <see cref="MonitorCapabilities.Read"/> every other
    /// client uses: it holds the monitor's named mutex, spaces its messages,
    /// retries an empty answer and records into the history as a by-product.
    /// </remarks>
    public static int Learn(DisplayInfo display)
    {
        if (!Tried.TryAdd(display.Key.Model, 0)) return 0;
        MonitorCapability capabilities = display.IsInternal ? MonitorCapability.None : MonitorCapabilities.Read(display);
        CacheRecord(display);
        return capabilities.Supported ? capabilities.Controls.Count : -1;
    }

    /// <summary>Collects the public model record outside the Share button's path.</summary>
    public static void CacheRecord(DisplayInfo display)
    {
        bool? oled = Core.Settings.SettingsStore.Load().For(display.Token).IsOled;
        string record = Redact.Ascii(Redact.Scrub(
            DeviceSubmission.Build(display, oled, includePrivateDetails: false).ToRepositoryMarkdown(),
            DeviceContribution.Identifiers([display]))).Replace("\r\n", "\n", StringComparison.Ordinal);
        DeviceHistory.Update(history =>
        {
            if (!history.Models.TryGetValue(display.Key.Model, out SeenModel? seen) || seen.Record == record) return false;
            seen.Record = record;
            return true;
        });
    }
}
