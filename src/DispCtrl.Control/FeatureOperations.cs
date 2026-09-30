using System.Diagnostics;
using System.Text.Json.Nodes;
using DispCtrl.Core.Settings;

namespace DispCtrl.Control;

/// <summary>
/// Custom features: named lists of steps somebody put together, run in order
/// from the command line, a hotkey, a quick-panel tile or the app.
/// </summary>
/// <remarks>
/// Every step that touches DispCtrl goes back through <see cref="Execute"/>, so
/// a feature can do nothing a command could not, is checked the same way, and
/// takes the same lock one step at a time. A feature is not itself a mutation:
/// holding the lock across a wait or a script would block every other command.
/// </remarks>
public sealed partial class ControlService
{
    /// <summary>How deep features may run features, before it is taken as a loop.</summary>
    private const int MaxFeatureDepth = 4;

    [ThreadStatic] private static int _featureDepth;
    [ThreadStatic] private static int _featureSteps;

    /// <summary>Where a script step's relative path is looked for; the same folder <c>scripts list</c> shows.</summary>
    public static string ScriptsFolder => Path.Combine(SettingsStore.Directory, "scripts");

    private JsonNode FeaturesCommand(string action, JsonObject args)
    {
        bool dryRun = Flag(args, "dryRun");
        DispCtrlSettings settings = SettingsStore.Load();
        switch (action)
        {
            case "list":
            case "get":
            {
                if (args.Count > 0) throw new ArgumentException("features list takes no options.");
                var list = new JsonArray();
                foreach (CustomFeature f in settings.Features)
                    list.Add((JsonNode)new JsonObject
                    {
                        ["name"] = f.Name, ["description"] = f.Description,
                        ["steps"] = new JsonArray(f.Steps.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
                    });
                return new JsonObject { ["features"] = list, ["grammar"] = CustomFeature.Grammar };
            }
            case "add":
            case "set":
            {
                if (args.Any(p => p.Key is not ("name" or "steps" or "description" or "rename" or "dryRun")))
                    throw new ArgumentException("features add|set takes --name, --steps \"step; step\", --description and --rename.");
                string name = (Text(args, "name") ?? throw new ArgumentException("Name the feature: --name Gaming.")).Trim();
                CustomFeature? existing = settings.Features.FirstOrDefault(f => f.Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
                if (action == "add" && existing is not null) throw new ArgumentException($"“{existing.Name}” already exists; use features set to change it.");
                if (existing is null && !args.ContainsKey("steps")) throw new ArgumentException("A new feature needs --steps, as --steps \"set 2 picture-mode fps; wait 500\".");
                var feature = existing ?? new CustomFeature { Name = name };
                var candidate = new CustomFeature
                {
                    Name = Text(args, "rename")?.Trim() ?? feature.Name,
                    Description = Text(args, "description") ?? feature.Description,
                    Steps = Text(args, "steps") is { } steps ? CustomFeature.SplitSteps(steps) : [.. feature.Steps],
                };
                var all = settings.Features.Where(f => !ReferenceEquals(f, existing)).Append(candidate).ToList();
                if (CustomFeature.Problem(all) is { } problem) throw new ArgumentException(problem);
                if (dryRun) return new JsonObject { ["state"] = "validated", ["name"] = candidate.Name, ["steps"] = candidate.Parse().Count };
                if (existing is null) settings.Features.Add(candidate);
                else { existing.Name = candidate.Name; existing.Description = candidate.Description; existing.Steps = candidate.Steps; }
                SettingsStore.Save(settings);
                return new JsonObject { ["state"] = existing is null ? "added" : "saved", ["name"] = candidate.Name, ["steps"] = candidate.Steps.Count };
            }
            case "remove":
            {
                if (args.Any(p => p.Key is not ("name" or "dryRun"))) throw new ArgumentException("features remove takes --name.");
                CustomFeature feature = Find(settings, Text(args, "name"));
                if (dryRun) return new JsonObject { ["state"] = "validated", ["name"] = feature.Name };
                settings.Features.Remove(feature);
                SettingsStore.Save(settings);
                return new JsonObject { ["state"] = "removed", ["name"] = feature.Name };
            }
            case "run":
            {
                if (args.Any(p => p.Key is not ("name" or "dryRun"))) throw new ArgumentException("features run takes --name.");
                return RunFeature(Find(settings, Text(args, "name")), dryRun);
            }
            default:
                throw new ArgumentException("features list|add|set|remove|run");
        }
    }

    private static CustomFeature Find(DispCtrlSettings settings, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name the feature: --name Gaming. See features list.");
        return settings.Features.FirstOrDefault(f => f.Name.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"No feature is called “{name}”. Features: {string.Join(", ", settings.Features.Select(f => f.Name))}.");
    }

    /// <summary>Runs a feature's steps in order, stopping at the first that fails.</summary>
    /// <remarks>A dry run validates steps without mutations. Read commands may still query current state.</remarks>
    public JsonObject RunFeature(CustomFeature feature, bool dryRun = false)
    {
        if (CustomFeature.Problem([feature]) is { } problem) throw new ArgumentException(problem);
        if (_featureDepth >= MaxFeatureDepth) throw new InvalidOperationException($"“{feature.Name}” runs features inside features too deeply; is it running itself?");
        using var runGate = new Mutex(false, @"Local\DispCtrl.Control.FeatureRun");
        bool held = false;
        if (_featureDepth == 0)
        {
            _featureSteps = 0;
            if (!dryRun)
            {
                try { held = runGate.WaitOne(0); }
                catch (AbandonedMutexException) { held = true; }
                if (!held) throw new InvalidOperationException("A custom feature is already running. Wait for it to finish.");
            }
        }
        List<FeatureStep> steps = feature.Parse();
        var results = new JsonArray();
        bool failed = false;
        _featureDepth++;
        try
        {
            int number = 0;
            foreach (FeatureStep step in steps)
            {
                number++;
                var entry = new JsonObject { ["step"] = number, ["does"] = FeatureStep.Join([step.Kind.ToString().ToLowerInvariant(), .. step.Words]) };
                try
                {
                    if (++_featureSteps > 1000) throw new InvalidOperationException("A feature and its nested features may run at most 1000 steps.");
                    entry["result"] = RunStep(step, dryRun);
                    entry["ok"] = true;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    entry["ok"] = false;
                    entry["error"] = ex.Message;
                    failed = true;
                }
                results.Add((JsonNode)entry);
                if (failed) break;
            }
        }
        finally
        {
            _featureDepth--;
            if (held) runGate.ReleaseMutex();
        }
        return new JsonObject
        {
            ["state"] = failed ? "partial" : dryRun ? "validated" : "applied",
            ["name"] = feature.Name, ["steps"] = results,
        };
    }

    private JsonNode? RunStep(FeatureStep step, bool dryRun)
    {
        switch (step.Kind)
        {
            case FeatureStepKind.Set:
            {
                var args = new JsonObject { ["monitor"] = step.Monitor, ["name"] = step.Control, ["value"] = step.Value };
                if (step.Raw) args["raw"] = true;
                if (dryRun) args["dryRun"] = true;
                return Nested(new JsonObject { ["command"] = "display.control", ["args"] = args });
            }
            case FeatureStepKind.Command:
            {
                JsonObject request = ControlTerminal.RequestFor(step.Words);
                string name = request["command"]!.GetValue<string>();
                bool readOnly = name.EndsWith(".get", StringComparison.Ordinal) || name.EndsWith(".list", StringComparison.Ordinal)
                    || name is "status" or "commands" or "diagnostics" or "display.controls" or "display.capabilities" or "display.modes"
                        or "settings.schema" or "devices.show" or "devices.definitions";
                if (dryRun && !readOnly) ((JsonObject)request["args"]!)["dryRun"] = true;
                return Nested(request);
            }
            case FeatureStepKind.Wait:
                if (!dryRun) Thread.Sleep(step.Milliseconds);
                return JsonValue.Create(step.Milliseconds);
            case FeatureStepKind.Run:
            {
                if (dryRun) return JsonValue.Create("not run in a dry run");
                var info = new ProcessStartInfo(step.Words[0]) { UseShellExecute = true };
                foreach (string argument in step.Words.Skip(1)) info.ArgumentList.Add(argument);
                using var process = Process.Start(info) ?? throw new InvalidOperationException("The target could not be opened.");
                return JsonValue.Create("opened");
            }
            case FeatureStepKind.Script:
            {
                string path = step.Words[0];
                if (!Path.IsPathFullyQualified(path)) path = Path.Combine(ScriptsFolder, path);
                if (!File.Exists(path)) throw new FileNotFoundException($"The script {step.Words[0]} was not found (relative paths are looked for in {ScriptsFolder}).");
                var info = ScriptStartInfo(Path.GetFullPath(path), step.Words.Skip(1).ToList());
                if (dryRun) return JsonValue.Create("found; not run in a dry run");
                return JsonValue.Create(RunScriptStep(info));
            }
            default:
                throw new ArgumentException("Unknown step.");
        }
    }

    /// <summary>One DispCtrl request from inside a feature; its failure is the step's failure.</summary>
    private JsonNode? Nested(JsonObject request)
    {
        JsonObject result = Execute(request);
        if (result["ok"]?.GetValue<bool>() != true)
            throw new InvalidOperationException(result["error"]?["message"]?.GetValue<string>()
                ?? result["data"]?["controls"]?.AsArray().FirstOrDefault(c => c?["error"] is not null)?["error"]?.GetValue<string>()
                ?? $"{request["command"]} did not complete.");
        return result["data"]?.DeepClone();
    }

    /// <summary>Runs a script and waits up to five minutes; a non-zero exit fails the step.</summary>
    private static ProcessStartInfo ScriptStartInfo(string path, List<string> arguments)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        ProcessStartInfo info;
        if (extension == ".ps1")
        {
            info = new ProcessStartInfo("powershell.exe");
            foreach (string a in (string[])["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", path]) info.ArgumentList.Add(a);
        }
        else if (extension is ".cmd" or ".bat")
        {
            if (arguments.Prepend(path).Any(a => a.IndexOfAny(['"', '%', '!', '^', '&', '|', '<', '>', '\r', '\n']) >= 0))
                throw new ArgumentException("Batch paths and arguments cannot contain shell operators, quotes, percent signs or exclamation marks. Use a .ps1 or .exe script for those arguments.");
            info = new ProcessStartInfo("cmd.exe");
            info.Arguments = "/d /v:off /s /c \"" + string.Join(" ", arguments.Prepend(path).Select(a => "\"" + a + "\"")) + "\"";
        }
        else if (extension == ".exe") info = new ProcessStartInfo(path);
        else throw new ArgumentException("A script must be a .ps1, .cmd, .bat or .exe file.");
        if (extension is not (".cmd" or ".bat"))
            foreach (string a in arguments) info.ArgumentList.Add(a);
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.WorkingDirectory = Path.GetDirectoryName(path)!;
        return info;
    }

    private static string RunScriptStep(ProcessStartInfo info)
    {
        using var process = Process.Start(info) ?? throw new InvalidOperationException("The script could not be started.");
        Task<string> output = ReadScriptOutput(process.StandardOutput);
        Task<string> error = ReadScriptOutput(process.StandardError);
        if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException("The script was still running after five minutes and was stopped.");
        }
        Task.WaitAll([output, error], TimeSpan.FromSeconds(2));
        string stderr = error.IsCompletedSuccessfully ? error.Result.Trim() : "";
        if (process.ExitCode != 0) throw new InvalidOperationException($"The script exited with code {process.ExitCode}. {stderr}".Trim());
        return output.IsCompletedSuccessfully && output.Result.Length > 0 ? output.Result.TrimEnd() : "finished";
    }

    private static async Task<string> ReadScriptOutput(StreamReader reader)
    {
        var text = new System.Text.StringBuilder();
        var buffer = new char[1024];
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
                if (text.Length < 4096) text.Append(buffer, 0, Math.Min(count, 4096 - text.Length));
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
        return text.ToString();
    }
}
