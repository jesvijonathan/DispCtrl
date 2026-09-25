using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;

namespace DispCtrl.PerfCheck;

/// <summary>One measured quantity: its distribution, its budget, and whether it kept to it.</summary>
internal sealed record Row(string Suite, string Name, string Unit, double Median, double P95, double Worst,
                           int Samples, double? Budget, string? Note = null)
{
    /// <summary>Budgets hold the median, not the worst: one descheduled sample is the desk, not the code.</summary>
    public bool OverBudget => Budget is double b && Median > b;

    public JsonObject ToJson() => new()
    {
        ["suite"] = Suite, ["name"] = Name, ["unit"] = Unit, ["median"] = Math.Round(Median, 4), ["p95"] = Math.Round(P95, 4),
        ["worst"] = Math.Round(Worst, 4), ["samples"] = Samples, ["budget"] = Budget, ["note"] = Note,
    };

    public static Row FromJson(JsonNode n) => new(
        n["suite"]!.GetValue<string>(), n["name"]!.GetValue<string>(), n["unit"]!.GetValue<string>(),
        n["median"]!.GetValue<double>(), n["p95"]!.GetValue<double>(), n["worst"]!.GetValue<double>(),
        n["samples"]!.GetValue<int>(), n["budget"]?.GetValue<double>(), n["note"]?.GetValue<string>());
}

/// <summary>Collects rows and prints each as it lands, so a long run shows progress.</summary>
internal sealed class Report
{
    public List<Row> Rows { get; } = [];
    public List<string> Notes { get; } = [];
    private string? _suite;

    /// <summary>A child process prints rows as JSON lines for its parent to collect instead.</summary>
    public bool AsJsonLines { get; init; }

    public Row Add(Row row)
    {
        Rows.Add(row);
        if (AsJsonLines) { Console.WriteLine("ROW " + row.ToJson().ToJsonString()); return row; }
        if (row.Suite != _suite)
        {
            _suite = row.Suite;
            Console.WriteLine();
            Console.WriteLine($"== {row.Suite}");
        }
        string status = row.Budget is null ? "" : row.OverBudget ? "  OVER" : "  ok";
        string budget = row.Budget is double b ? "<= " + Format(b, row.Unit) : "";
        string spread = row.Samples > 1 ? $"p95 {Format(row.P95, row.Unit),-10} worst {Format(row.Worst, row.Unit),-10} n={row.Samples,-3}" : new string(' ', 42);
        Console.ForegroundColor = row.OverBudget ? ConsoleColor.Red : Console.ForegroundColor;
        Console.WriteLine($"  {row.Name,-52} {Format(row.Median, row.Unit),12}  {spread} {budget,-12}{status}{(row.Note is null ? "" : "  " + row.Note)}");
        Console.ResetColor();
        return row;
    }

    public void Note(string suite, string text)
    {
        Notes.Add(suite + ": " + text);
        if (AsJsonLines) { Console.WriteLine("NOTE " + suite + ": " + text); return; }
        if (suite != _suite) { _suite = suite; Console.WriteLine(); Console.WriteLine($"== {suite}"); }
        Console.WriteLine($"  -- {text}");
    }

    public static string Format(double value, string unit) => unit switch
    {
        "ms" when value < 1 => (value * 1000).ToString("0.0", CultureInfo.InvariantCulture) + " us",
        "ms" => value.ToString(value < 100 ? "0.00" : "0", CultureInfo.InvariantCulture) + " ms",
        _ => value.ToString(value < 10 && value != Math.Floor(value) ? "0.00" : "0.#", CultureInfo.InvariantCulture) + " " + unit,
    };
}

internal static class Stats
{
    public static (double Median, double P95, double Worst) Of(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0) return (double.NaN, double.NaN, double.NaN);
        double[] s = [.. values.Order()];
        return (Percentile(s, 0.5), Percentile(s, 0.95), s[^1]);
    }

    private static double Percentile(double[] sorted, double p)
    {
        double rank = p * (sorted.Length - 1);
        int lo = (int)Math.Floor(rank), hi = (int)Math.Ceiling(rank);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
    }

    public static Row Row(string suite, string name, string unit, IReadOnlyCollection<double> values, double? budget, string? note = null)
    {
        var (median, p95, worst) = Of(values);
        return new Row(suite, name, unit, median, p95, worst, values.Count, budget, note);
    }
}

internal static class Bench
{
    /// <summary>Wall time of <paramref name="action"/>, after <paramref name="warmup"/> unmeasured runs.</summary>
    public static Row Time(string suite, string name, int samples, int warmup, double? budgetMs, Action action, string? note = null)
    {
        for (int i = 0; i < warmup; i++) action();
        var times = new double[samples];
        for (int i = 0; i < samples; i++)
        {
            long start = Stopwatch.GetTimestamp();
            action();
            times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        return Stats.Row(suite, name, "ms", times, budgetMs, note);
    }

    /// <summary>Managed bytes allocated by one call, averaged: what the GC is handed per operation.</summary>
    public static Row Allocations(string suite, string name, int samples, double? budgetKb, Action action)
    {
        action();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < samples; i++) action();
        double kb = (GC.GetAllocatedBytesForCurrentThread() - before) / 1024.0 / samples;
        return new Row(suite, name, "KB", kb, kb, kb, samples, budgetKb);
    }

    /// <summary>Waits until <paramref name="condition"/> holds, polling at ~1 ms; the elapsed ms, or null on timeout.</summary>
    public static double? Until(Func<bool> condition, int timeoutMs, long? since = null)
    {
        long start = since ?? Stopwatch.GetTimestamp();
        while (true)
        {
            if (condition()) return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (Stopwatch.GetElapsedTime(start).TotalMilliseconds > timeoutMs) return null;
            Thread.Sleep(1);
        }
    }
}
