using Statehop.Core.Abstractions;
using Statehop.Core.Activity;
using Statehop.Core.Model;

namespace Statehop.Core.Observation;

/// <summary>
/// The Observation layer of the product, wired to the store.
///
/// This is deliberately the only layer that exists so far. The architecture is
/// Observation → Inference → Decision → Execution (docs/PRODUCT.md), and
/// nothing here infers, decides or acts: it records what happened and stops.
///
/// It lives in Core, not in the WinUI project, and it takes its sources as
/// interfaces. That is what makes it testable, and it is also what the
/// separation above actually requires — a presentation project owning the
/// observation layer was a quiet violation of it.
/// </summary>
public sealed class ObservationService : IDisposable
{
    /// <summary>How long without input before the user counts as idle.</summary>
    public static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(2);

    /// <summary>How often the process list is enumerated and diffed.</summary>
    public static readonly TimeSpan ProcessPollInterval = TimeSpan.FromSeconds(30);

    /// <summary>How often the B0.2 window-owner measurement runs.</summary>
    public static readonly TimeSpan AccessProbeInterval = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How often the ephemeral classification is recomputed. Deliberately
    /// slow: it is a whole-table pass, it changes nothing a user sees within
    /// the minute, and this app has to stay cheap all day.
    /// </summary>
    public static readonly TimeSpan ReclassifyInterval = TimeSpan.FromMinutes(30);

    private readonly IActivityStore _store;
    private readonly IForegroundSource _foreground;
    private readonly IIdleSource _idle;
    private readonly IProcessSource _processes;
    private readonly IWindowOwnerSource _accessProbe;
    private readonly ActivityFeed _feed;
    private readonly Timer _reclassifyTimer;

    private bool _disposed;

    /// <summary>Raised whenever displayed state changed, on a background thread.</summary>
    public event EventHandler? Updated;

    public IActivityStore Store => _store;

    /// <summary>The recent-activity buffer, for a view to render incrementally.</summary>
    public ActivityFeed Feed => _feed;

    /// <summary>Process name currently in the foreground, for display only.</summary>
    public string CurrentForeground { get; private set; } = "(ainda não observado)";

    public bool IsIdle => _idle.IsIdle;

    public DateTime StartedAtUtc { get; } = DateTime.UtcNow;

    /// <summary>Processes in the most recent enumeration snapshot.</summary>
    public int ProcessesInLastSnapshot => _processes.LastSnapshotCount;

    /// <summary>
    /// Exceptions swallowed while recording. Non-zero here is a reliability
    /// signal for the Phase 0 gate, so it is counted rather than ignored.
    /// </summary>
    public int RecordingErrors { get; private set; }

    public string? LastErrorMessage { get; private set; }

    /// <summary>
    /// Takes ownership of everything passed in: <see cref="Dispose"/> disposes
    /// the sources and, if it is disposable, the store.
    /// </summary>
    public ObservationService(
        IActivityStore store,
        IForegroundSource foreground,
        IIdleSource idle,
        IProcessSource processes,
        IWindowOwnerSource accessProbe,
        ActivityFeed? feed = null)
    {
        _store = store;
        _foreground = foreground;
        _idle = idle;
        _processes = processes;
        _accessProbe = accessProbe;
        _feed = feed ?? new ActivityFeed();

        _reclassifyTimer = new Timer(_ => Reclassify(), null, Timeout.Infinite, Timeout.Infinite);

        _foreground.ForegroundChanged += OnForegroundChanged;
        _idle.IdleChanged += OnIdleChanged;
        _processes.LifetimeChanged += OnLifetimeChanged;
        _accessProbe.Probed += OnProbed;
    }

    /// <summary>
    /// Starts observing. Call from the UI thread: the foreground hook delivers
    /// its callbacks through that thread's message queue.
    /// </summary>
    public void Start()
    {
        _foreground.Start();
        _idle.Start();
        _processes.Start();
        _accessProbe.Start();
        _reclassifyTimer.Change(TimeSpan.Zero, ReclassifyInterval);
    }

    /// <summary>The whole feed, newest first. For the first fill of a view.</summary>
    public IReadOnlyList<ActivityLine> RecentActivity() => _feed.Recent();

    /// <summary>Lines newer than a watermark, oldest first.</summary>
    public IReadOnlyList<ActivityLine> ActivitySince(long afterSequence) => _feed.Since(afterSequence);

    /// <summary>
    /// Re-derives the ephemeral flags. Never throws out of the timer: a failed
    /// classification is a cosmetic loss, and taking down an app that is meant
    /// to run all day over it would not be.
    /// </summary>
    public void Reclassify()
    {
        try
        {
            _store.ReclassifyEphemeral();
            Updated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            RecordingErrors++;
            LastErrorMessage = ex.Message;
        }
    }

    private void OnForegroundChanged(object? sender, ForegroundChange change)
    {
        CurrentForeground = change.Identity.Name;
        Record(
            () => _store.RecordForeground(change),
            "Foreground",
            change.Identity.AccessState == ProcessAccessState.Denied
                ? $"{change.Identity.Name} (identidade inacessível)"
                : change.Identity.Name);
    }

    private void OnIdleChanged(object? sender, IdleChange change)
    {
        Record(
            () => _store.RecordIdle(change),
            "Idle",
            change.IsIdle
                ? $"sem input do utilizador há mais de {IdleThreshold.TotalMinutes:0} min"
                : "input do utilizador retomado");
    }

    private void OnLifetimeChanged(object? sender, ProcessLifetimeChange change)
    {
        Record(
            () => _store.RecordLifetime(change),
            "Processo",
            $"{change.Identity.Name} {(change.Kind == ProcessLifetimeKind.Started ? "arrancou" : "terminou")}");
    }

    private void OnProbed(object? sender, IReadOnlyList<WindowOwnerProbe> probes)
    {
        try
        {
            _store.RecordWindowOwnerProbe(probes);
            Updated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            RecordingErrors++;
            LastErrorMessage = ex.Message;
        }
    }

    private void Record(Action write, string kind, string description)
    {
        try
        {
            write();
        }
        catch (Exception ex)
        {
            // A failed write must never take down an app that is meant to run
            // all day; it is counted and surfaced instead. The line still goes
            // into the feed, because it did happen — the recording failed, the
            // observation did not.
            RecordingErrors++;
            LastErrorMessage = ex.Message;
        }

        _feed.Add(DateTime.Now, kind, description);
        Updated?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _reclassifyTimer.Dispose();
        _accessProbe.Dispose();
        _processes.Dispose();
        _idle.Dispose();
        _foreground.Dispose();
        (_store as IDisposable)?.Dispose();
    }
}
