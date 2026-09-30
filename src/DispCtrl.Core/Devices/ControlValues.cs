using System.Globalization;

namespace DispCtrl.Core.Devices;

/// <summary>
/// What a request for a control's value means: an exact value, the next or
/// previous choice, or a step up or down a range.
/// </summary>
/// <remarks>
/// Shared by <c>display control --value</c>, hotkeys and custom features so
/// that <c>next</c> and <c>+10</c> mean one thing everywhere.
/// </remarks>
public static class ControlValues
{
    public enum Request { Exact, Next, Previous, Relative }

    /// <summary>How to read a requested value; <paramref name="delta"/> is set for a relative one.</summary>
    public static Request Classify(string text, out int delta)
    {
        delta = 0;
        string t = text.Trim();
        if (t.Equals("next", StringComparison.OrdinalIgnoreCase)) return Request.Next;
        if (t.Equals("previous", StringComparison.OrdinalIgnoreCase) || t.Equals("prev", StringComparison.OrdinalIgnoreCase)) return Request.Previous;
        if (t.Length > 1 && t[0] is '+' or '-'
            && int.TryParse(t.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int step) && step <= 0xFFFF)
        {
            delta = t[0] == '-' ? -step : step;
            return Request.Relative;
        }
        return Request.Exact;
    }

    /// <summary>
    /// The value after (or before) the current one, wrapping round; the first
    /// (or last) when the current value is unknown or not among them.
    /// </summary>
    /// <remarks>Match the full value first; standard byte-sized choices may use the reply's low byte.</remarks>
    public static uint Cycle(IReadOnlyList<uint> values, int? current, bool forward)
    {
        if (values.Count == 0) throw new ArgumentException("This control has no values to move between.");
        int index = -1;
        if (current is int now)
        {
            for (int i = 0; i < values.Count; i++)
                if (values[i] == (uint)now) { index = i; break; }
            if (index < 0 && values.All(v => v <= 0xFF))
                for (int i = 0; i < values.Count; i++)
                    if (values[i] == (uint)(now & 0xFF)) { index = i; break; }
        }
        if (index < 0) return forward ? values[0] : values[^1];
        return values[((forward ? index + 1 : index - 1) + values.Count) % values.Count];
    }

    /// <summary>A range's current value moved by <paramref name="delta"/>, kept within 0 and its maximum.</summary>
    public static uint Step(int? current, int delta, int maximum)
    {
        if (current is not int now || now < 0) throw new InvalidOperationException("The monitor did not say where this control is now, so it cannot be stepped.");
        int top = maximum >= 0 ? Math.Min(maximum, 0xFFFF) : 0xFFFF;
        return (uint)Math.Clamp((long)now + delta, 0, top);
    }
}
