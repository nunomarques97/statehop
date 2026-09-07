namespace Statehop.Core.Abstractions;

/// <summary>
/// How long raw observation is kept before it is rolled up
/// into daily totals.
///
/// The measured growth is ~18 MB/month at 8 h/day and ~73 MB/month with the
/// machine on around the clock. Disk is not the
/// problem at that rate; what matters is that Phase 3 needs weeks of real raw
/// events, so the window has to be generous.
/// </summary>
public sealed record RetentionPolicy
{
    /// <summary>
    /// Days of raw events kept in full. Ninety covers a quarter, which is more
    /// than the "weeks of observation" Phase 3 asks for, and costs ~55 MB at
    /// the measured rate.
    ///
    /// It applies equally to foreground and to process lifetime events. That
    /// is deliberate: the open Phase 1 question is whether co-occurrence comes
    /// from one table, the other, or both, and shortening either window would
    /// answer that question by deleting the evidence.
    /// </summary>
    public int RawDays { get; init; } = 90;

    public static RetentionPolicy Default { get; } = new();
}

/// <summary>What a retention pass did, or would do when asked to preview.</summary>
/// <param name="Cutoff">Local day before which raw events are rolled up.</param>
/// <param name="DaysRolledUp">Distinct local days summarised.</param>
/// <param name="Applied">False for a preview: nothing was written or deleted.</param>
public sealed record RetentionOutcome(
    DateOnly Cutoff,
    int DaysRolledUp,
    long ForegroundRowsRemoved,
    long IdleRowsRemoved,
    long LifetimeRowsRemoved,
    bool Applied)
{
    public long TotalRowsRemoved =>
        ForegroundRowsRemoved + IdleRowsRemoved + LifetimeRowsRemoved;
}
