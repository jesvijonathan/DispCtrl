namespace DispCtrl.Core.Settings;

/// <summary>When the window asks, once in a while, for a star or a donation.</summary>
/// <remarks>
/// Asked of somebody who has come back to DispCtrl, not on a first run: the
/// window has opened <see cref="OpensBefore"/> times over at least
/// <see cref="Settle"/>. A star or a donation ends it; closing it without
/// either asks once more after <see cref="Again"/>, and a second close ends it.
/// Never shown again after that, and a reset of preferences does not start it over.
/// </remarks>
public sealed class SupportPrompt
{
    public const int OpensBefore = 5;
    public static readonly TimeSpan Settle = TimeSpan.FromDays(3);
    public static readonly TimeSpan Again = TimeSpan.FromDays(90);

    /// <summary>When the window first opened; bookkeeping.</summary>
    public DateTimeOffset? FirstSeenUtc { get; set; }

    /// <summary>How many times the window has opened; bookkeeping.</summary>
    public int WindowOpens { get; set; }

    /// <summary>How many times the request was closed without a star or a donation.</summary>
    public int Declined { get; set; }

    /// <summary>When it was last closed that way.</summary>
    public DateTimeOffset? DeclinedUtc { get; set; }

    /// <summary>Never ask again: somebody starred or donated, or closed it twice.</summary>
    public bool Done { get; set; }

    public void CountOpen(DateTimeOffset now)
    {
        FirstSeenUtc ??= now;
        if (WindowOpens < int.MaxValue) WindowOpens++;
    }

    public bool Due(DateTimeOffset now) =>
        !Done && WindowOpens >= OpensBefore && FirstSeenUtc is { } first && now - first >= Settle
        && (DeclinedUtc is not { } last || now - last >= Again || last > now);

    public void Acted() => Done = true;

    public void Decline(DateTimeOffset now)
    {
        Declined++;
        DeclinedUtc = now;
        if (Declined >= 2) Done = true;
    }
}
