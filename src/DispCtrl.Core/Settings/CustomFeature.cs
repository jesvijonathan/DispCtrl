using System.Text;

namespace DispCtrl.Core.Settings;

/// <summary>
/// A named list of steps somebody put together: monitor controls, dispctrl
/// commands, programs and scripts, run in order.
/// </summary>
/// <remarks>
/// Steps are kept as the lines a person writes rather than as a structure, so
/// the settings file, the command line and the app's editor all show the same
/// text, and a feature can be copied from the documentation as it stands:
/// <code>
/// set 2 picture-mode fps
/// set 2 brightness 80
/// wait 500
/// dispctrl nightlight set --enabled off
/// run "C:\Games\launcher.exe"
/// </code>
/// </remarks>
public sealed class CustomFeature
{
    public string Name { get; set; } = "";

    /// <summary>What it is for, in the owner's words; optional.</summary>
    public string Description { get; set; } = "";

    /// <summary>One step per line; see <see cref="FeatureStep.Parse"/>.</summary>
    public List<string> Steps { get; set; } = [];

    public const int MaxSteps = 100;
    public const int MaxNameLength = 64;

    /// <summary>The steps, parsed; blank lines and comments dropped.</summary>
    /// <exception cref="FormatException">A line that is not a step, with its number.</exception>
    public List<FeatureStep> Parse()
    {
        var steps = new List<FeatureStep>();
        for (int i = 0; i < Steps.Count; i++)
        {
            try
            {
                if (FeatureStep.Parse(Steps[i]) is { } step) steps.Add(step);
            }
            catch (FormatException ex) { throw new FormatException($"Step {i + 1} of “{Name}”: {ex.Message}"); }
        }
        return steps;
    }

    /// <summary>Why a set of features cannot be saved, or null when they can.</summary>
    public static string? Problem(IReadOnlyList<CustomFeature?> features)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CustomFeature? f in features)
        {
            if (f is null || f.Name is null || f.Description is null || f.Steps is null || f.Steps.Any(s => s is null))
                return "Features cannot contain null entries.";
            string name = f.Name.Trim();
            if (name.Length == 0 || name.Length > MaxNameLength || f.Name.Any(char.IsControl)) return $"A feature's name is 1 to {MaxNameLength} characters without control characters.";
            if (!names.Add(name)) return $"Two features are called “{name}”.";
            if (f.Steps.Count > MaxSteps) return $"“{name}” has more than {MaxSteps} steps.";
            try { _ = f.Parse(); }
            catch (FormatException ex) { return ex.Message; }
        }
        return null;
    }

    /// <summary>Splits text written as one line with <c>;</c> between steps, or several lines.</summary>
    public static List<string> SplitSteps(string text)
    {
        var steps = new List<string>();
        var current = new StringBuilder();
        bool quoted = false, comment = false;
        foreach (char c in text)
        {
            if (c == '#' && current.ToString().Trim().Length == 0) comment = true;
            if (c == '"' && !comment) quoted = !quoted;
            if (c == '\n' || c == ';' && !quoted && !comment)
            {
                if (current.ToString().Trim() is { Length: > 0 } line) steps.Add(line);
                current.Clear();
                quoted = false;
                comment = false;
            }
            else if (c != '\r') current.Append(c);
        }
        if (current.ToString().Trim() is { Length: > 0 } last) steps.Add(last);
        return steps;
    }

    /// <summary>The text help and the editor show.</summary>
    public const string Grammar = """
        set MONITOR CONTROL VALUE [raw]   A monitor control: set 2 picture-mode fps, set all contrast +10,
                                          set 1 input-source next. VALUE: a value's name, a number, next,
                                          previous, +N or -N. raw writes an unmapped code (Advanced setting).
        dispctrl ARGUMENTS                Any dispctrl command: dispctrl nightlight set --enabled on
        run TARGET [ARGUMENTS]            Open a program, document or link; does not wait for it
        script PATH [ARGUMENTS]           Run a .ps1, .cmd, .bat or .exe and wait; a failure stops the feature
        wait MILLISECONDS                 Pause, up to 60000
        # text                            A comment
        """;
}

public enum FeatureStepKind { Set, Command, Run, Script, Wait }

/// <summary>One parsed step of a <see cref="CustomFeature"/>.</summary>
public sealed record FeatureStep(FeatureStepKind Kind, IReadOnlyList<string> Words)
{
    /// <summary>For <see cref="FeatureStepKind.Set"/>: which display, as a number, alias, token or <c>all</c>.</summary>
    public string Monitor => Words[0];
    public string Control => Words[1];
    public string Value => Words[2];
    public bool Raw => Kind == FeatureStepKind.Set && Words.Count == 4;
    public int Milliseconds => Kind == FeatureStepKind.Wait ? int.Parse(Words[0], System.Globalization.CultureInfo.InvariantCulture) : 0;

    /// <summary>A step from one line, or null for a blank line or a comment.</summary>
    /// <exception cref="FormatException">The line is not a step this grammar knows.</exception>
    public static FeatureStep? Parse(string line)
    {
        string text = line.Trim();
        if (text.Length == 0 || text.StartsWith('#')) return null;
        List<string> words = Tokenize(text);
        string verb = words[0].ToLowerInvariant();
        List<string> rest = words.Skip(1).ToList();
        switch (verb)
        {
            case "set":
                if (rest.Count is not (3 or 4) || rest.Count == 4 && !rest[3].Equals("raw", StringComparison.OrdinalIgnoreCase))
                    throw new FormatException("set MONITOR CONTROL VALUE [raw], as: set 2 picture-mode fps");
                return new FeatureStep(FeatureStepKind.Set, rest);
            case "dispctrl":
                if (rest.Count == 0) throw new FormatException("dispctrl needs a command, as: dispctrl topology set --mode extend");
                if (rest[0] is "watch" or "scripts" or "engine" or "request")
                    throw new FormatException($"dispctrl {rest[0]} cannot run inside a feature.");
                return new FeatureStep(FeatureStepKind.Command, rest);
            case "run":
            case "open":
                if (rest.Count == 0 || string.IsNullOrWhiteSpace(rest[0])) throw new FormatException("run needs something to open.");
                return new FeatureStep(FeatureStepKind.Run, rest);
            case "script":
                if (rest.Count == 0 || string.IsNullOrWhiteSpace(rest[0])) throw new FormatException("script needs a file to run.");
                return new FeatureStep(FeatureStepKind.Script, rest);
            case "wait":
            case "delay":
                if (rest.Count != 1 || !int.TryParse(rest[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int ms) || ms > 60000)
                    throw new FormatException("wait takes milliseconds, 0 to 60000.");
                return new FeatureStep(FeatureStepKind.Wait, rest);
            default:
                throw new FormatException($"'{words[0]}' is not a step. Steps: set, dispctrl, run, script, wait.");
        }
    }

    /// <summary>Words separated by spaces; double quotes keep spaces in one word.</summary>
    public static List<string> Tokenize(string text)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        bool quoted = false, any = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"') { current.Append('"'); i++; }
                else quoted = !quoted;
                any = true;
                continue;
            }
            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any) { words.Add(current.ToString()); current.Clear(); any = false; }
                continue;
            }
            current.Append(c);
            any = true;
        }
        if (quoted) throw new FormatException("A quote is not closed.");
        if (any) words.Add(current.ToString());
        return words;
    }

    /// <summary>Words back into one argument string, quoting what needs it.</summary>
    public static string Join(IEnumerable<string> words) =>
        string.Join(' ', words.Select(w => w.Length == 0 || w.Any(c => char.IsWhiteSpace(c) || c is '"' or ';')
            ? "\"" + w.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : w));

    /// <summary>Quotes arguments for a Windows executable, rather than for the feature step grammar.</summary>
    public static string JoinWindowsArguments(IEnumerable<string> words) => string.Join(' ', words.Select(word =>
    {
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in word)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            result.Append(c);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }));
}
