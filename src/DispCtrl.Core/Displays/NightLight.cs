using Windows.Win32;
using Windows.Win32.Graphics.Gdi;

namespace DispCtrl.Core.Displays;

/// <summary>
/// Warms a display's colour by rewriting its gamma ramp.
/// </summary>
/// <remarks>
/// Windows' own night light has no public API — it is driven through an
/// undocumented CloudStore blob that changes shape between builds, and writing
/// to it is neither supportable nor Store-legal. A gamma ramp is the same
/// mechanism every third-party tool uses: it is per-display, applies instantly,
/// needs no elevation, and is undone by putting the original ramp back.
/// <para>
/// The trade-off is honest and worth stating: a ramp warms everything the GPU
/// scans out, so a screenshot taken while it is on looks normal. Windows' own
/// night light behaves the same way.
/// </para>
/// </remarks>
public static class NightLight
{
    /// <summary>Colour temperature with the warmth slider at zero.</summary>
    private const double NeutralKelvin = 6500;

    /// <summary>
    /// Colour temperature at full warmth.
    /// </summary>
    /// <remarks>
    /// Not an aesthetic choice — it is where Windows stops accepting the ramp.
    /// GDI validates gamma ramps against a default range and refuses anything
    /// further from the identity; measured on this machine, 3280K is taken and
    /// 3050K is refused on both panels. Unlocking the rest means writing
    /// <c>GdiIcmGammaRange</c> under HKLM, which needs elevation and is not
    /// something a Store app should be doing to a shared machine.
    /// <para>
    /// So the slider spans what actually works. A control whose top third
    /// silently does nothing is worse than a shorter one.
    /// </para>
    /// </remarks>
    private const double WarmestKelvin = 3300;

    /// <summary>
    /// Colour temperature at full warmth once the clamp is lifted.
    /// </summary>
    /// <remarks>
    /// 1900K is about as warm as a gamma ramp can usefully go — below it the
    /// blue channel is so compressed that banding is worse than the warmth is
    /// worth. Reachable only with <see cref="GammaRange"/> unlocked.
    /// </remarks>
    private const double WarmestUnlockedKelvin = 1900;

    /// <summary>
    /// The ramp each warmed display had before DispCtrl touched it.
    /// </summary>
    /// <remarks>
    /// Captured rather than assumed to be linear. A display with a calibration
    /// profile loaded already has a non-linear ramp, and restoring a synthetic
    /// straight line instead of the real one would quietly throw that
    /// calibration away — a much worse bug than night light not working,
    /// because nothing on screen announces it.
    /// <para>
    /// Keyed on the GDI name, not the stable token: the ramp belongs to the
    /// device context, and that is what a DC is opened by.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, ushort[]> Baselines = [];

    /// <summary>Returns the colour temperature a warmth percentage maps to.</summary>
    public static double KelvinFor(int strength)
    {
        double warmest = IsUnlocked ? WarmestUnlockedKelvin : WarmestKelvin;
        return NeutralKelvin - ((NeutralKelvin - warmest) * Math.Clamp(strength, 0, 100) / 100.0);
    }

    /// <summary>
    /// The dimmest a software-dimmed panel is allowed to go, as a percentage.
    /// </summary>
    /// <remarks>
    /// Software dimming multiplies the signal, so zero is a black screen with
    /// no way back except by feel. A floor keeps the control recoverable —
    /// exactly the reasoning behind never storing a zero warmth.
    /// </remarks>
    public const int MinimumDim = 10;

    /// <summary>
    /// How warm this machine can currently go, for the UI to say so.
    /// </summary>
    public static double WarmestAvailableKelvin => IsUnlocked ? WarmestUnlockedKelvin : WarmestKelvin;

    /// <summary>True when the gamma clamp has been lifted on this machine.</summary>
    public static bool FullRange => IsUnlocked;

    /// <summary>
    /// The lowest any channel may fall before Windows refuses the ramp.
    /// </summary>
    /// <remarks>
    /// Measured, not documented. Sweeping dim levels against warmth on this
    /// hardware, the boundary sits where the weakest channel reaches about half
    /// of the identity ramp: dimming alone is accepted to 50%, at 60% warmth
    /// only to 70%, and at full warmth not at all. 0.53 reproduces every one of
    /// those measurements with a little margin.
    /// <para>
    /// This is the same clamp that caps warmth at 3300K. It matters more here,
    /// because warmth and dimming come out of the same ramp and so compete: the
    /// warmer the screen, the less room is left to dim it.
    /// </para>
    /// </remarks>
    private const double LowestChannel = 0.53;

    /// <summary>The floor once the clamp is lifted. Not zero: black is not recoverable.</summary>
    private const double LowestUnlockedChannel = 0.08;

    /// <summary>
    /// Whether the clamp has been lifted, cached for the life of the process.
    /// </summary>
    /// <remarks>
    /// A registry read per gamma write would be wasteful — the engine writes a
    /// ramp every 20 seconds — and the value cannot change without an elevated
    /// prompt, which only happens through this app. <see cref="Recheck"/> is
    /// called straight after that prompt.
    /// </remarks>
    private static bool? _unlocked;

    private static bool IsUnlocked => _unlocked ??= GammaRange.Read().Unlocked;

    /// <summary>Re-reads the clamp state, after it has been changed.</summary>
    public static void Recheck() => _unlocked = null;

    private static double Ceiling => IsUnlocked ? LowestUnlockedChannel : LowestChannel;

    /// <summary>
    /// The lowest dim percentage Windows will accept at a given warmth.
    /// </summary>
    /// <remarks>
    /// Asked before the slider is drawn, so the control only offers levels that
    /// work. A refused ramp is not an error the user sees — the previous ramp
    /// simply stays — which would read as the slider doing nothing below some
    /// arbitrary point.
    /// </remarks>
    public static int LowestDim(int warmth)
    {
        (_, _, double blue) = Multipliers(KelvinFor(warmth));
        if (blue <= 0) return 100;


        double limit = Ceiling / blue;
        return (int)Math.Clamp(Math.Ceiling(limit * 100), MinimumDim, 100);
    }

    /// <summary>
    /// Applies warmth and software dimming to one display in a single ramp.
    /// </summary>
    /// <remarks>
    /// Both together, deliberately. A display has one gamma ramp; warmth and
    /// dimming are each a scale of it, and two callers each writing "their"
    /// ramp would simply overwrite one another — whichever ran last would win
    /// and the other setting would vanish. Composing them here is the only
    /// arrangement where both hold.
    /// </remarks>
    /// <param name="strength">Warmth, 0-100. Zero leaves the colour alone.</param>
    /// <param name="dim">
    /// Brightness, 10-100, for panels with no hardware control. 100 leaves the
    /// level alone.
    /// </param>
    /// <returns>False when the driver refused the ramp.</returns>
    public static bool Apply(DisplayInfo display, int strength, int dim = 100)
    {
        ushort[]? baseline = Baseline(display.GdiName);
        if (baseline is null) return false;

        (double r, double g, double b) = Multipliers(KelvinFor(strength));

        // Clamped to what Windows will actually take, so a caller asking for
        // more than the ramp allows gets the most it can have rather than a
        // silent refusal that leaves the previous ramp in place.
        double level = Math.Clamp(dim, LowestDim(strength), 100) / 100.0;
        return Write(display.GdiName, Scaled(baseline, r * level, g * level, b * level));
    }

    /// <summary>
    /// The peak of each channel as a fraction of full scale, or null when the
    /// display will not report a ramp.
    /// </summary>
    /// <remarks>
    /// The check that the warmth actually landed. A neutral display reads
    /// 1,1,1; a warmed one reads a blue below one. Worth exposing rather than
    /// trusting the write's return value, because a driver can accept a ramp
    /// and quietly not apply it.
    /// </remarks>
    public static (double R, double G, double B)? Peaks(DisplayInfo display)
    {
        ushort[]? ramp = Read(display.GdiName);
        if (ramp is null) return null;

        return (ramp[255] / 65535.0, ramp[511] / 65535.0, ramp[767] / 65535.0);
    }

    /// <summary>Restores one display's neutral ramp.</summary>
    public static bool Clear(DisplayInfo display) => Clear(display.GdiName);

    /// <summary>True when a display needs a ramp at all.</summary>
    /// <remarks>
    /// Neutral warmth and full brightness are the identity ramp, and writing
    /// the identity is indistinguishable from leaving the display alone — but
    /// it still costs a gamma write per tick, and it would hold a baseline
    /// captured for no reason.
    /// </remarks>
    public static bool NeedsRamp(int strength, int dim) => strength > 0 || dim < 100;

    /// <summary>Restores every display this process has warmed.</summary>
    public static void ClearAll()
    {
        // Copied first: Clear mutates the dictionary as it goes.
        string[] names = [.. Baselines.Keys];
        foreach (string name in names) Clear(name);
    }

    private static bool Clear(string gdiName)
    {
        if (!Baselines.Remove(gdiName, out ushort[]? baseline)) return true;
        return Write(gdiName, baseline);
    }

    /// <summary>
    /// How far below red the blue peak may sit before a ramp is judged to be
    /// somebody's warmth rather than the display's own calibration.
    /// </summary>
    /// <remarks>
    /// A real calibration profile tints a little; it does not take 10% off the
    /// blue peak. Anything past this is almost certainly a warm ramp left
    /// behind — by a crashed engine, or by an earlier build — and adopting it
    /// as the baseline is how warmth compounds and can never be undone.
    /// </remarks>
    private const double PollutedBlueRatio = 0.90;

    /// <summary>The display's untouched ramp, read once and remembered.</summary>
    private static ushort[]? Baseline(string gdiName)
    {
        if (Baselines.TryGetValue(gdiName, out ushort[]? cached)) return cached;

        ushort[]? read = Read(gdiName);
        if (read is null) return null;

        // Refuse to adopt a ramp that is already warm — see PollutedBlueRatio.
        // Falling back to the identity loses a calibration in that one case,
        // which is the better failure: a display stuck progressively warmer
        // every restart has no way out at all.
        if (LooksWarmed(read)) read = Identity();

        Baselines[gdiName] = read;
        return read;
    }

    /// <summary>
    /// True when a display is carrying warmth nobody in this process applied.
    /// </summary>
    /// <remarks>
    /// The recovery signal after an engine that was killed rather than stopped:
    /// its ramps outlive it, and with no baseline anywhere there is nothing to
    /// put them back. See <see cref="PollutedBlueRatio"/> for what counts.
    /// </remarks>
    public static bool LooksWarmed(DisplayInfo display)
    {
        ushort[]? ramp = Read(display.GdiName);
        return ramp is not null && LooksWarmed(ramp);
    }

    private static bool LooksWarmed(ushort[] ramp)
    {
        double red = ramp[255];
        if (red <= 0) return true;

        // Dimming scales every channel equally, so a tinted ramp is not the
        // only kind left behind: a uniformly low ramp is a dimmed one, and an
        // abandoned dim needs clearing just as much as an abandoned warmth.
        if (red / 65535.0 < 0.97) return true;

        return ramp[767] / red < PollutedBlueRatio;
    }

    private static ushort[] Identity()
    {
        var ramp = new ushort[3 * 256];
        for (int i = 0; i < 256; i++)
        {
            ushort v = (ushort)Math.Clamp(Math.Round(i / 255.0 * 65535.0), 0, 65535);
            ramp[i] = v;
            ramp[256 + i] = v;
            ramp[512 + i] = v;
        }

        return ramp;
    }

    /// <summary>
    /// Forces a display back to an untinted ramp, discarding any baseline.
    /// </summary>
    /// <remarks>
    /// The way out when a ramp has been left behind by something that is no
    /// longer running, so there is no baseline to restore. Blunt on purpose.
    /// </remarks>
    public static bool ResetToNeutral(DisplayInfo display)
    {
        Baselines.Remove(display.GdiName);
        return Write(display.GdiName, Identity());
    }

    /// <summary>
    /// True once any display has been warmed, so a caller knows there is
    /// something to undo.
    /// </summary>
    public static bool AnyApplied => Baselines.Count > 0;

    /// <summary>
    /// Approximates the RGB of a black body at <paramref name="kelvin"/>.
    /// </summary>
    /// <remarks>
    /// Tanner Helland's curve fit, normalised so the neutral point is exactly
    /// 1,1,1. Without that normalisation the fit leaves a faint tint at 6500K
    /// and switching night light off would not quite return the screen to where
    /// it started.
    /// </remarks>
    private static (double R, double G, double B) Multipliers(double kelvin)
    {
        (double nr, double ng, double nb) = Raw(NeutralKelvin);
        (double r, double g, double b) = Raw(kelvin);

        return (r / nr, g / ng, b / nb);
    }

    private static (double R, double G, double B) Raw(double kelvin)
    {
        double t = Math.Clamp(kelvin, 1000, 40000) / 100.0;

        double r = t <= 66
            ? 255
            : 329.698727446 * Math.Pow(t - 60, -0.1332047592);

        double g = t <= 66
            ? (99.4708025861 * Math.Log(t)) - 161.1195681661
            : 288.1221695283 * Math.Pow(t - 60, -0.0755148492);

        double b = t >= 66
            ? 255
            : t <= 19
                ? 0
                : (138.5177312231 * Math.Log(t - 10)) - 305.0447927307;

        return (Math.Clamp(r, 1, 255) / 255.0,
                Math.Clamp(g, 1, 255) / 255.0,
                Math.Clamp(b, 1, 255) / 255.0);
    }

    /// <summary>
    /// Scales a ramp's three channels, preserving whatever shape it already had.
    /// </summary>
    /// <remarks>
    /// GDI's layout is one flat array of 3x256 entries: red, then green, then
    /// blue.
    /// </remarks>
    private static ushort[] Scaled(ushort[] baseline, double r, double g, double b)
    {
        var ramp = new ushort[3 * 256];

        for (int i = 0; i < 256; i++)
        {
            ramp[i] = Clamp(baseline[i] * r);
            ramp[256 + i] = Clamp(baseline[256 + i] * g);
            ramp[512 + i] = Clamp(baseline[512 + i] * b);
        }

        return ramp;
    }

    private static ushort Clamp(double value) =>
        (ushort)Math.Clamp(Math.Round(value), 0, 65535);

    /// <summary>
    /// Opens a DC for the display device itself.
    /// </summary>
    /// <remarks>
    /// The device, not a window: the ramp belongs to the whole output, and a
    /// window DC would warm only what happened to be under that window.
    /// </remarks>
    private static unsafe HDC Open(string gdiName) => PInvoke.CreateDCW(null, gdiName, null, null);

    private static unsafe ushort[]? Read(string gdiName)
    {
        HDC dc = Open(gdiName);
        if (dc.IsNull) return null;

        try
        {
            var ramp = new ushort[3 * 256];
            fixed (ushort* p = ramp)
            {
                return PInvoke.GetDeviceGammaRamp(dc, p) ? ramp : null;
            }
        }
        finally
        {
            _ = PInvoke.DeleteDC(dc);
        }
    }

    private static unsafe bool Write(string gdiName, ushort[] ramp)
    {
        HDC dc = Open(gdiName);
        if (dc.IsNull) return false;

        try
        {
            fixed (ushort* p = ramp)
            {
                // Windows validates ramps and refuses ones it judges too far
                // from the identity. Everything here is a straight scale of the
                // display's own ramp, which stays inside that window; the
                // return is still checked, because a driver can refuse gamma
                // control outright.
                return PInvoke.SetDeviceGammaRamp(dc, p);
            }
        }
        finally
        {
            _ = PInvoke.DeleteDC(dc);
        }
    }
}
