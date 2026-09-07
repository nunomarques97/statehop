using Statehop.Core.Model;
using Statehop.Core.Sessions;

namespace Statehop.Core.Abstractions;

/// <summary>
/// Snapshot of what has been recorded, for display and for the Phase 0 gate
/// report. Counts only; no activity content leaves the store.
/// </summary>
/// <param name="ForegroundEvents">Foreground changes recorded.</param>
/// <param name="IdleEvents">Idle transitions recorded.</param>
/// <param name="LifetimeEvents">Process start/exit events recorded.</param>
/// <param name="DistinctProcesses">Distinct process identities seen.</param>
/// <param name="DatabaseBytes">Size on disk — input for the retention policy.</param>
/// <param name="EphemeralProcesses">
/// How many of those identities are classified ephemeral — short-lived and
/// never the owner of a window. Nothing
/// is discarded; this is what a reader would filter out.
/// </param>
/// <param name="EphemeralLifetimeEvents">
/// Lifetime rows belonging to those identities. This is the number that says
/// how much of the store is toolchain churn.
/// </param>
public sealed record StoreStats(
    long ForegroundEvents,
    long IdleEvents,
    long LifetimeEvents,
    long DistinctProcesses,
    long DatabaseBytes,
    long EphemeralProcesses,
    long EphemeralLifetimeEvents);

/// <summary>
/// The B0.2 answer, aggregated over everything observed so far.
/// </summary>
/// <param name="WindowOwnersObserved">
/// Distinct processes owning a visible top-level window, excluding
/// system/protected ones.
/// </param>
/// <param name="Denied">How many of those denied identity access.</param>
/// <param name="DeniedNames">Which ones, by name.</param>
/// <param name="SystemProtectedExcluded">
/// Protected processes seen and deliberately left out of the numbers above.
/// </param>
public sealed record ElevatedAccessReport(
    int WindowOwnersObserved,
    int Denied,
    IReadOnlyList<string> DeniedNames,
    int SystemProtectedExcluded)
{
    public double DeniedPercent =>
        WindowOwnersObserved == 0 ? 0 : 100.0 * Denied / WindowOwnersObserved;
}

/// <summary>
/// Local, append-only store of observed activity.
///
/// Implementations must be safe to call from several threads: foreground
/// changes arrive on the UI thread, process and idle polling on timer threads.
///
/// PRIVACY: implementations must never persist window titles.
/// </summary>
public interface IActivityStore
{
    void RecordForeground(ForegroundChange change);

    void RecordIdle(IdleChange change);

    void RecordLifetime(ProcessLifetimeChange change);

    /// <summary>Merges one B0.2 probe pass into the accumulated measurement.</summary>
    void RecordWindowOwnerProbe(IReadOnlyList<WindowOwnerProbe> probes);

    StoreStats GetStats();

    ElevatedAccessReport GetElevatedAccessReport();

    /// <summary>
    /// Recomputes the ephemeral classification from the recorded events. Cheap
    /// enough to run on a slow timer; it reads and updates only
    /// <c>process_identity</c> and never deletes anything.
    /// </summary>
    void ReclassifyEphemeral();

    /// <summary>
    /// Everything recorded for one local day, in the shape the session builder
    /// consumes. <paramref name="nowUtc"/> closes the span that is still open,
    /// so the store never has to reach for a clock of its own.
    /// </summary>
    DayObservations ReadDay(DateOnly localDay, DateTime nowUtc);

    /// <summary>
    /// Rolls raw events older than the policy window into
    /// daily totals and drops the raw rows.
    ///
    /// Deleting observation is irreversible, so this never runs on its own:
    /// pass <paramref name="apply"/> false to get exactly the same numbers
    /// back without writing or deleting anything.
    /// </summary>
    RetentionOutcome ApplyRetention(RetentionPolicy policy, DateTime nowUtc, bool apply);

    /// <summary>Absolute path of the database file, for display.</summary>
    string DatabasePath { get; }
}
