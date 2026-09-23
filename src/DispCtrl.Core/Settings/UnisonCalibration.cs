namespace DispCtrl.Core.Settings;

/// <summary>Process-lifetime calibration pause; a crashed UI cannot leave following disabled on disk.</summary>
public static class UnisonCalibration
{
    private static readonly string Name = @"Local\DispCtrl.UnisonCalibration." + System.Diagnostics.Process.GetCurrentProcess().SessionId;
    private static EventWaitHandle? _lease;

    public static void SetActive(bool active)
    {
        if (active) (_lease ??= new EventWaitHandle(false, EventResetMode.ManualReset, Name)).Set();
        else { _lease?.Reset(); _lease?.Dispose(); _lease = null; }
    }

    public static bool IsActive
    {
        get
        {
            try { using var signal = EventWaitHandle.OpenExisting(Name); return signal.WaitOne(0); }
            catch (WaitHandleCannotBeOpenedException) { return false; }
        }
    }
}
