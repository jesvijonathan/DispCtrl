using System.Text.Json.Nodes;
using DispCtrl.Core.Machine;
using DispCtrl.Core.Settings;

namespace DispCtrl.Control;

public sealed partial class ControlService
{
    /// <summary>machine get|set|undo: sign-in and lock screen switches (<see cref="MachinePolicies"/>).</summary>
    /// <remarks>
    /// The first change to a switch records what Windows had, in
    /// <see cref="GlobalSettings.MachineBefore"/>, so <c>undo</c> puts that back
    /// rather than a default that may not be what the machine came with.
    /// </remarks>
    private static JsonNode MachineCommand(string action, JsonObject args)
    {
        if (args.ContainsKey("monitor")) throw new ArgumentException("These switches belong to the computer, not a display.");
        bool dryRun = Flag(args, "dryRun");
        switch (action)
        {
            case "get":
            {
                if (args.Any(p => p.Key != "dryRun")) throw new ArgumentException("machine get takes no options.");
                var list = new JsonArray();
                Dictionary<string, string?> before = SettingsStore.Load().Global.MachineBefore;
                foreach (MachinePolicy p in MachinePolicies.All)
                {
                    PolicyState state = MachinePolicies.State(p);
                    list.Add((JsonNode)new JsonObject
                    {
                        ["id"] = p.Id, ["label"] = p.Label, ["hint"] = p.Hint,
                        ["kind"] = p.Kind.ToString().ToLowerInvariant(), ["needsAdmin"] = p.NeedsAdmin,
                        ["value"] = state.Value, ["set"] = state.Set,
                        ["canUndo"] = p.Values.Any(v => before.ContainsKey(RegistryName(v))),
                    });
                }
                return new JsonObject { ["switches"] = list, ["managed"] = MachinePolicies.Managed() };
            }
            case "set":
            {
                if (args.Any(p => p.Key is not ("switch" or "value" or "dryRun")))
                    throw new ArgumentException("machine set takes a switch and a value: machine set no-ctrl-alt-del on.");
                MachinePolicy policy = MachinePolicies.Find(Text(args, "switch") ?? throw new ArgumentException("Name the switch."));
                string wanted = args["value"] switch
                {
                    JsonValue v when v.TryGetValue(out bool b) => b ? "on" : "off",
                    JsonValue v => v.ToString(),
                    _ => throw new ArgumentException("Give the value: on, off, minutes, or a picture's path."),
                };
                var plan = MachinePolicies.Plan(policy, wanted);
                if (dryRun) return new JsonObject { ["state"] = "validated", ["switch"] = policy.Id, ["needsAdmin"] = policy.NeedsAdmin };
                Remember(policy);
                if (!MachinePolicies.Apply(plan, out string? error)) throw new InvalidOperationException(error ?? "The change was not made.");
                PolicyState now = MachinePolicies.State(policy);
                return new JsonObject { ["state"] = "applied", ["switch"] = policy.Id, ["value"] = now.Value, ["managed"] = MachinePolicies.Managed() };
            }
            case "undo":
            {
                if (args.Any(p => p.Key is not ("switch" or "dryRun"))) throw new ArgumentException("machine undo takes a switch.");
                MachinePolicy policy = MachinePolicies.Find(Text(args, "switch") ?? throw new ArgumentException("Name the switch."));
                Dictionary<string, string?> before = SettingsStore.Load().Global.MachineBefore;
                if (!policy.Values.Any(v => before.ContainsKey(RegistryName(v))))
                    throw new InvalidOperationException($"{policy.Label} has not been changed by DispCtrl, so there is nothing to put back.");
                var plan = policy.Values.Select(v => (v, before.TryGetValue(RegistryName(v), out string? was) ? Decode(was) : MachinePolicies.Before(v))).ToList();
                if (dryRun) return new JsonObject { ["state"] = "validated", ["switch"] = policy.Id };
                if (!MachinePolicies.Apply(plan, out string? error)) throw new InvalidOperationException(error ?? "The change was not made.");
                SettingsDocument.Update(d =>
                {
                    JsonObject record = d["global"]!["machineBefore"]!.AsObject();
                    foreach (PolicyValue v in policy.Values) record.Remove(RegistryName(v));
                }, false);
                return new JsonObject { ["state"] = "restored", ["switch"] = policy.Id, ["value"] = MachinePolicies.State(policy).Value };
            }
            default:
                throw new ArgumentException("machine get, set or undo.");
        }
    }

    /// <summary>Records each value as Windows had it, the first time only.</summary>
    private static void Remember(MachinePolicy policy) => SettingsDocument.Update(d =>
    {
        JsonObject global = d["global"]!.AsObject();
        if (global["machineBefore"] is not JsonObject record) global["machineBefore"] = record = new JsonObject();
        foreach (PolicyValue v in policy.Values)
            if (!record.ContainsKey(RegistryName(v))) record[RegistryName(v)] = Encode(MachinePolicies.Before(v));
    }, false);

    private static string RegistryName(PolicyValue v) => $"{(v.Machine ? "HKLM" : "HKCU")}\\{v.Key}\\{v.Name}";

    /// <summary>A recorded value: <c>dword:1</c>, <c>sz:text</c>, or null for "not there".</summary>
    private static string? Encode(object? value) => value switch
    {
        null => null,
        int i => "dword:" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => "sz:" + value,
    };

    private static object? Decode(string? recorded) => recorded switch
    {
        null => null,
        _ when recorded.StartsWith("dword:", StringComparison.Ordinal) => int.Parse(recorded[6..], System.Globalization.CultureInfo.InvariantCulture),
        _ when recorded.StartsWith("sz:", StringComparison.Ordinal) => recorded[3..],
        _ => null,
    };
}
