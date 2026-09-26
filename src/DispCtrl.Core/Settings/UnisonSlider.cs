namespace DispCtrl.Core.Settings;

/// <summary>
/// Says, across processes, that the app's unison slider is being moved - so the
/// engine does not take the built-in panel's movement for Windows' own slider.
/// </summary>
/// <remarks>
/// A drag moves the built-in panel, and every step raises the same WMI event
/// Windows' slider and brightness keys do. The engine's bridge compares that
/// event with the level saved on disk, and a drag saves at most every 300 ms:
/// in between, each step read as a brightness key. The bridge then saved its
/// own level, drove the other displays alongside the app, and the app pulled
/// that level back into the slider mid-drag - the slider and the brightness
/// jumped about, while a single click, one step, was smooth. Held from each
/// step to <see cref="HoldMs"/> after the last, which outlasts the event and
/// the app's final save; a crashed app takes the event with it.
/// </remarks>
public static class UnisonSlider
{
    /// <summary>How long after the last step the slider still counts as moving.</summary>
    public const int HoldMs = 800;

    private static readonly string Name = @"Local\DispCtrl.UnisonSlider." + Session.Id;
    private static readonly Lock Gate = new();
    private static EventWaitHandle? _moving;
    private static Timer? _release;

    /// <summary>The slider took a step: moving now, and until <see cref="HoldMs"/> passes without another.</summary>
    public static void Step()
    {
        lock (Gate)
        {
            (_moving ??= new EventWaitHandle(false, EventResetMode.ManualReset, Name)).Set();
            (_release ??= new Timer(_ => { lock (Gate) _moving?.Reset(); })).Change(HoldMs, Timeout.Infinite);
        }
    }

    /// <summary>Whether any process's unison slider is moving.</summary>
    public static bool IsMoving
    {
        get
        {
            try { using var signal = EventWaitHandle.OpenExisting(Name); return signal.WaitOne(0); }
            catch (WaitHandleCannotBeOpenedException) { return false; }
        }
    }
}
