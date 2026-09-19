namespace DispCtrl.Core.Settings;

/// <summary>How the resident engine temporarily overrides system sleep.</summary>
public enum AwakeMode
{
    PowerPlan,
    Indefinite,
    Timed,
    Expiration,
}

/// <summary>
/// PowerToys Awake-style power requests. These do not rewrite the active
/// Windows power plan; they only remain in force while the engine is running.
/// </summary>
public sealed class AwakeSettings
{
    public AwakeMode Mode { get; set; }
    public bool KeepDisplaysOn { get; set; }
    public int IntervalHours { get; set; } = 1;
    public int IntervalMinutes { get; set; }
    public DateTimeOffset TimedUntilUtc { get; set; }
    public DateTimeOffset ExpirationUtc { get; set; } = DateTimeOffset.Now.AddHours(1);

    public bool ActiveAt(DateTimeOffset now) => Mode switch
    {
        AwakeMode.Indefinite => true,
        AwakeMode.Timed => TimedUntilUtc > now,
        AwakeMode.Expiration => ExpirationUtc > now,
        _ => false,
    };
}
