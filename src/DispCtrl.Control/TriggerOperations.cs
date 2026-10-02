using System.Text.Json.Nodes;
using DispCtrl.Core.Settings;

namespace DispCtrl.Control;

public sealed partial class ControlService
{
    /// <summary>triggers list|add|set|remove: when this happens, run that feature (<see cref="Trigger"/>).</summary>
    /// <remarks>
    /// A trigger is checked against the features it names when it is saved, so
    /// one that cannot run is refused here rather than found silent later. The
    /// engine runs them; it reads the list from the file like everything else.
    /// </remarks>
    private static JsonNode TriggersCommand(string action, JsonObject args)
    {
        bool dryRun = Flag(args, "dryRun");
        DispCtrlSettings settings = SettingsStore.Load();
        switch (action)
        {
            case "list":
            case "get":
            {
                if (args.Any(p => p.Key != "dryRun")) throw new ArgumentException("triggers list takes no options.");
                var list = new JsonArray();
                for (int i = 0; i < settings.Triggers.Count; i++)
                {
                    Trigger t = settings.Triggers[i];
                    list.Add((JsonNode)new JsonObject
                    {
                        ["index"] = i + 1, ["enabled"] = t.Enabled, ["event"] = EventName(t.Event), ["match"] = t.Match,
                        ["minutes"] = t.Minutes, ["feature"] = t.Feature, ["does"] = t.Describe(), ["problem"] = t.Problem(settings.Features),
                    });
                }
                return new JsonObject
                {
                    ["triggers"] = list,
                    ["events"] = new JsonArray(Enum.GetValues<TriggerEvent>().Select(e => (JsonNode?)JsonValue.Create(EventName(e))).ToArray()),
                };
            }
            case "add":
            case "set":
            {
                if (args.Any(p => p.Key is not ("index" or "event" or "match" or "minutes" or "feature" or "enabled" or "dryRun")))
                    throw new ArgumentException("triggers add takes --event, --feature, and --match or --minutes where the event needs them; set takes --index too.");
                Trigger t;
                if (action == "add") t = new Trigger();
                else
                {
                    int index = Integer(args, "index", 1, Math.Max(1, settings.Triggers.Count));
                    if (index > settings.Triggers.Count) throw new ArgumentException("There is no such trigger. See triggers list.");
                    Trigger was = settings.Triggers[index - 1];
                    t = new Trigger { Enabled = was.Enabled, Event = was.Event, Match = was.Match, Minutes = was.Minutes, Feature = was.Feature };
                }
                if (Text(args, "event") is { } e) t.Event = ParseEvent(e);
                else if (action == "add") throw new ArgumentException("Which event? --event " + string.Join("|", Enum.GetValues<TriggerEvent>().Select(EventName)));
                if (args.ContainsKey("match")) t.Match = args["match"]?.ToString()?.Trim() ?? "";
                if (args.ContainsKey("minutes")) t.Minutes = Integer(args, "minutes", 1, 1440);
                if (Text(args, "feature") is { } f) t.Feature = f.Trim();
                if (args.ContainsKey("enabled")) t.Enabled = Flag(args, "enabled");
                if (t.Problem(settings.Features) is { } problem) throw new ArgumentException(problem);
                if (dryRun) return new JsonObject { ["state"] = "validated", ["does"] = t.Describe() };
                if (action == "add") settings.Triggers.Add(t);
                else settings.Triggers[Integer(args, "index", 1, settings.Triggers.Count) - 1] = t;
                SettingsStore.Save(settings);
                return new JsonObject { ["state"] = action == "add" ? "added" : "saved", ["index"] = settings.Triggers.IndexOf(t) + 1, ["does"] = t.Describe() };
            }
            case "remove":
            {
                if (args.Any(p => p.Key is not ("index" or "dryRun"))) throw new ArgumentException("triggers remove takes --index.");
                if (settings.Triggers.Count == 0) throw new ArgumentException("There are no triggers.");
                int index = Integer(args, "index", 1, settings.Triggers.Count);
                string does = settings.Triggers[index - 1].Describe();
                if (dryRun) return new JsonObject { ["state"] = "validated", ["does"] = does };
                settings.Triggers.RemoveAt(index - 1);
                SettingsStore.Save(settings);
                return new JsonObject { ["state"] = "removed", ["does"] = does };
            }
            default:
                throw new ArgumentException("triggers list|add|set|remove.");
        }
    }

    private static string EventName(TriggerEvent e) => Core.Devices.DeviceDefinitions.KeyFor(
        string.Concat(e.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString())));

    private static TriggerEvent ParseEvent(string text)
    {
        foreach (TriggerEvent e in Enum.GetValues<TriggerEvent>())
            if (EventName(e) == Core.Devices.DeviceDefinitions.KeyFor(text) || e.ToString().Equals(text, StringComparison.OrdinalIgnoreCase)) return e;
        throw new ArgumentException("Events: " + string.Join(", ", Enum.GetValues<TriggerEvent>().Select(EventName)));
    }
}
