using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Devices.Display;
using Windows.Win32.System.Registry;

namespace DisplCtrl.Display;

/// <summary>Whether Windows adjusts brightness to the room, and whether it can.</summary>
/// <param name="Supported">The active power scheme offers the setting at all.</param>
/// <param name="OnBattery">On while running from the battery.</param>
/// <param name="PluggedIn">On while plugged in.</param>
public readonly record struct AdaptiveBrightnessState(bool Supported, bool OnBattery, bool PluggedIn)
{
    public static AdaptiveBrightnessState Unsupported => new(false, false, false);

    /// <summary>True when it is on for the power source in use right now.</summary>
    public bool Enabled => OnBattery || PluggedIn;
}

/// <summary>
/// Windows' own auto-brightness, which is a power setting and not a monitor one.
/// </summary>
/// <remarks>
/// Worth having precisely because of where people look for it. "Auto
/// brightness" sounds like something the monitor does, so the first place to
/// check is the monitor's own DDC/CI controls — and it is not there, because
/// Windows drives it from the ambient light sensor through the power scheme.
/// A panel that lists every control the monitor has, and then does not have
/// this one, is answering a question the user did not ask.
/// <para>
/// Separate AC and DC values because the power scheme keeps them separate, and
/// wanting it on unplugged but off at the desk is the normal case rather than
/// an edge one.
/// </para>
/// </remarks>
public static class AdaptiveBrightness
{
    /// <summary><c>GUID_VIDEO_SUBGROUP</c>.</summary>
    private static readonly Guid VideoSubgroup = new("7516b95f-f776-4464-8c53-06167f40cc99");

    /// <summary><c>GUID_VIDEO_ADAPTIVE_DISPLAY_BRIGHTNESS</c>.</summary>
    private static readonly Guid AdaptiveSetting = new("fbd9aa66-9553-4097-ba44-ed6e9d65eab8");

    public static unsafe AdaptiveBrightnessState Read()
    {
        Guid* scheme = null;
        try
        {
            if (PInvoke.PowerGetActiveScheme(default(HKEY), &scheme) != WIN32_ERROR.NO_ERROR || scheme is null)
                return AdaptiveBrightnessState.Unsupported;

            Guid sub = VideoSubgroup, setting = AdaptiveSetting;

            uint ac = 0, dc = 0;
            bool okAc = PInvoke.PowerReadACValueIndex(default(HKEY), scheme, &sub, &setting, &ac) == WIN32_ERROR.NO_ERROR;
            bool okDc = PInvoke.PowerReadDCValueIndex(default(HKEY), scheme, &sub, &setting, &dc) == (uint)WIN32_ERROR.NO_ERROR;

            // A scheme without the setting is a machine that cannot do this —
            // a desktop with no sensor, typically. Reporting it unsupported is
            // what keeps a dead toggle off the screen.
            if (!okAc && !okDc) return AdaptiveBrightnessState.Unsupported;

            return new AdaptiveBrightnessState(true, dc != 0, ac != 0);
        }
        finally
        {
            if (scheme is not null) _ = PInvoke.LocalFree((HLOCAL)(nint)scheme);
        }
    }

    /// <summary>Sets it for both power sources.</summary>
    /// <remarks>
    /// The active scheme has to be re-applied afterwards. Writing the value
    /// alone updates the stored scheme without telling the power manager, so
    /// nothing changes until something else happens to reapply it — which looks
    /// exactly like the setting not working.
    /// </remarks>
    public static unsafe bool Write(bool enabled)
    {
        Guid* scheme = null;
        try
        {
            if (PInvoke.PowerGetActiveScheme(default(HKEY), &scheme) != WIN32_ERROR.NO_ERROR || scheme is null)
                return false;

            Guid sub = VideoSubgroup, setting = AdaptiveSetting;
            uint value = enabled ? 1u : 0u;

            bool ac = PInvoke.PowerWriteACValueIndex(default(HKEY), scheme, &sub, &setting, value) == WIN32_ERROR.NO_ERROR;
            bool dc = PInvoke.PowerWriteDCValueIndex(default(HKEY), scheme, &sub, &setting, value) == (uint)WIN32_ERROR.NO_ERROR;

            if (!ac && !dc) return false;

            return PInvoke.PowerSetActiveScheme(default(HKEY), scheme) == WIN32_ERROR.NO_ERROR;
        }
        finally
        {
            if (scheme is not null) _ = PInvoke.LocalFree((HLOCAL)(nint)scheme);
        }
    }
}

/// <summary>Whether Windows will rotate the screen with the device.</summary>
/// <param name="Supported">The machine has the sensor and the capability.</param>
/// <param name="Enabled">Rotation is currently allowed to follow the device.</param>
/// <param name="Reason">Why it is unavailable, when it is.</param>
public readonly record struct AutoRotationState(bool Supported, bool Enabled, string Reason)
{
    public static AutoRotationState Unsupported(string why) => new(false, false, why);
}

/// <summary>
/// Windows' auto-rotation, for machines that can rotate.
/// </summary>
/// <remarks>
/// Reading it is documented; setting it is not. <c>GetAutoRotationState</c> is
/// a public API, but the only way to change the setting is the registry value
/// the Settings app writes, which lives under HKLM and would mean asking for
/// elevation for a toggle. So this reports, and hands off to Settings for the
/// change — the same bargain already made for colour profiles.
/// </remarks>
public static class AutoRotation
{
    // AR_STATE flags. CsWin32 surfaces the enum, but the meanings are worth
    // spelling out where they are used.
    private const uint Enabled = 0x0;
    private const uint Disabled = 0x1;
    private const uint Unsupported = 0x2;
    private const uint NoSensor = 0x4;
    private const uint NotSupportedByDevice = 0x8;
    private const uint Docked = 0x10;
    private const uint LaptopMode = 0x20;

    public static AutoRotationState Read()
    {
        if (!PInvoke.GetAutoRotationState(out AR_STATE state))
            return AutoRotationState.Unsupported("Windows would not answer.");

        uint flags = (uint)state;

        if ((flags & NotSupportedByDevice) != 0)
            return AutoRotationState.Unsupported("This device cannot rotate.");

        if ((flags & NoSensor) != 0)
            return AutoRotationState.Unsupported("There is no rotation sensor.");

        if ((flags & Unsupported) != 0)
            return AutoRotationState.Unsupported("Not supported on this machine.");

        // Docked and laptop mode are temporary: the machine can rotate, it just
        // is not going to right now. Worth saying rather than hiding.
        if ((flags & Docked) != 0)
            return new AutoRotationState(true, false, "Held off while docked.");

        if ((flags & LaptopMode) != 0)
            return new AutoRotationState(true, false, "Held off while the keyboard is open.");

        return new AutoRotationState(true, (flags & Disabled) == 0,
            (flags & Disabled) == 0 ? "On." : "Off.");
    }

    /// <summary>
    /// Turns auto-rotation on or off, asking for elevation once.
    /// </summary>
    /// <remarks>
    /// The setting lives in HKLM and there is no public API to change it —
    /// <c>SetAutoRotation</c> is not exported from user32 on this build, and the
    /// key rejects a write from a normal token. So this shells out to Windows'
    /// own <c>reg.exe</c> with the <c>runas</c> verb, which raises the UAC
    /// prompt for that one write and nothing else.
    /// <para>
    /// Deliberately not "make the whole app elevated". Everything else DisplCtrl
    /// does — taskbars, DDC/CI, modes, gamma — works from a normal token, and a
    /// packaged app cannot request elevation at all, so running the panel as
    /// administrator would trade the Store for one toggle.
    /// </para>
    /// <para>
    /// Returns false when the user dismisses the prompt, which is a normal
    /// answer and not an error.
    /// </para>
    /// </remarks>
    public static bool Write(bool enabled)
    {
        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "reg.exe",
            Arguments = "add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\AutoRotation\" "
                      + $"/v Enable /t REG_DWORD /d {(enabled ? 1 : 0)} /f",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
        };

        try
        {
            using System.Diagnostics.Process? process = System.Diagnostics.Process.Start(start);
            if (process is null) return false;

            process.WaitForExit(10_000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Exception)
        {
            // The prompt was dismissed, or the policy forbids elevation.
            return false;
        }
    }
}
