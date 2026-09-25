using System.Text.Json.Nodes;
using DispCtrl.Core;
using DispCtrl.Core.Settings;
using DispCtrl.Display;

namespace DispCtrl.Control;

public sealed partial class ControlService
{
    /// <summary>update get|check|set|skip|reset: the opt-in check for a new release.</summary>
    /// <remarks>
    /// Only <c>check</c> reaches the network, and only because it was asked to.
    /// The command line prints the link; it never opens a browser.
    /// </remarks>
    private static JsonNode UpdateCommand(string action, JsonObject args)
    {
        if (args.ContainsKey("monitor")) throw new ArgumentException("Updates are for DispCtrl, not a display.");
        bool dryRun = Flag(args, "dryRun");
        switch (action)
        {
            case "get":
            {
                UpdateSettings updates = SettingsStore.Load().Global.Updates;
                return new JsonObject
                {
                    ["value"] = SettingsDocument.Get(SettingsDocument.Read(), "/global/updates")?.DeepClone(),
                    ["current"] = BuildInfo.Version,
                    ["channel"] = BuildInfo.Channel,
                    ["store"] = StartupIntegration.IsPackaged,
                    ["available"] = UpdateCheck.Known(updates) is { } known ? Describe(known) : null,
                };
            }
            case "check":
            {
                if (args.Any(p => p.Key != "dryRun")) throw new ArgumentException("update check takes no options.");
                if (dryRun) return new JsonObject { ["state"] = "validated" };
                UpdateResult result;
                try { result = UpdateCheck.CheckAsync().GetAwaiter().GetResult(); }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
                {
                    throw new InvalidOperationException($"Could not reach GitHub to check ({ex.Message}). The releases are at {UpdateCheck.ReleasesPage}");
                }
                JsonObject described = Describe(result);
                described["state"] = result.Store ? "store" : result.Newer ? "available" : "current";
                described["message"] = result.Store ? "The Microsoft Store keeps this install up to date."
                    : result.Newer ? $"DispCtrl {result.Latest} is available; this is {result.Current}. {result.Url}"
                    : $"This is the latest version ({result.Current}).";
                return described;
            }
            case "set":
            {
                if (!args.ContainsKey("checkAutomatically") || args.Any(p => p.Key is not ("checkAutomatically" or "dryRun")))
                    throw new ArgumentException("update set takes --check-automatically on|off.");
                bool on = Flag(args, "checkAutomatically");
                return SettingsDocument.Update(d => d["global"]!["updates"]!["checkAutomatically"] = on, dryRun);
            }
            case "skip":
            {
                // "Not now": this release is not announced again; a later one is.
                if (args.Any(p => p.Key != "dryRun")) throw new ArgumentException("update skip takes no options.");
                string latest = SettingsStore.Load().Global.Updates.LatestVersion;
                if (latest.Length == 0) throw new InvalidOperationException("No release has been found yet; run update check first.");
                return SettingsDocument.Update(d => d["global"]!["updates"]!["skippedVersion"] = latest, dryRun);
            }
            case "reset":
                // Back to asking nothing, and forgetting what was found.
                return SettingsDocument.Update(d => d["global"]!["updates"] = SettingsDocument.Encode(new DispCtrlSettings())["global"]!["updates"]!.DeepClone(), dryRun);
            default:
                throw new ArgumentException("update get|check|set|skip|reset.");
        }
    }

    private static JsonObject Describe(UpdateResult r) => new()
    {
        ["current"] = r.Current, ["latest"] = r.Latest, ["newer"] = r.Newer, ["url"] = r.Url, ["store"] = r.Store,
    };
}
