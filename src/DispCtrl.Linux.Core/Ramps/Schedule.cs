using System.Globalization;

namespace DispCtrl.Linux.Ramps;

/// <summary>A daily window, such as 20:00 to 07:00, which may cross midnight.</summary>
public static class Schedule
{
    public static bool TryParse(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text, ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    public static string Format(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Whether <paramref name="now"/> falls in [from, to). Equal ends
    /// are an empty window; the commands refuse to store one.</summary>
    public static bool IsActive(TimeOnly now, TimeOnly from, TimeOnly to)
    {
        if (from == to) return false;
        return from < to
            ? now >= from && now < to
            : now >= from || now < to;
    }

    /// <summary>The next time the window opens or closes after <paramref name="now"/>,
    /// so the engine can wake for it rather than poll for it.</summary>
    public static DateTime NextBoundary(DateTime now, TimeOnly from, TimeOnly to)
    {
        DateTime Next(TimeOnly t)
        {
            var candidate = now.Date + t.ToTimeSpan();
            return candidate > now ? candidate : candidate.AddDays(1);
        }
        var a = Next(from);
        var b = Next(to);
        return a < b ? a : b;
    }
}
