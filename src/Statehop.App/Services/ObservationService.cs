using Statehop.Core.Abstractions;
using Statehop.Core.Model;
using Statehop.Observation.Interop;
using Statehop.Observation.Watchers;
using Statehop.Storage;

namespace Statehop.App.Services;

/// <summary>One line of the in-memory activity feed shown in the main window.</summary>
/// <param name="AtLocal">When it happened, in local time.</param>
/// <param name="Kind">Foreground, Idle or Process.</param>
/// <param name="Description">Process name and state — never a window title.</param>
public sealed record ActivityLine(DateTime AtLocal, string Kind, string Description)
{
    /// <summary>Pre-formatted for display, so the XAML needs no converter.</summary>
    public string TimeText => AtLocal.ToString("HH:mm:ss");
}

/// <summary>
/// The Observation layer of the product, wired to the store.
///
/// This is deliberately the only layer that exists in Phase 0. The
/// architecture is Observation → Inference → Decision → Execution
///, and nothing here infers,
/// decides or acts: it records what happened and stops.
/// </summary>
public sealed class ObservationService : IDisposable
{
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ProcessPollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AccessProbeInterval = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How often the ephemeral classification is recomputed. Deliberately slow:
    /// it is a whole-table pass, it changes nothing a user sees within the
    /// minute, and this app has to stay cheap all day.
    /// </summary>
    private static readonly TimeSpan ReclassifyInterval = TimeSpan.FromMinutes(30);

    private const int FeedCapacity = 200;

    private readonly SqliteActivityStore _store;
    private readonly ForegroundWatcher _foreground;
    private readonly IdleWatcher _idle;
    private readonly ProcessWatcher _processes;
    private readonly WindowOwnerProbeRunner _accessProbe;
    private readonly Timer _reclassifyTimer;
    private readonly LinkedList<ActivityLine> _feed = new();
    private readonly object _feedGate = new();

    private bool _disposed;

    /// <summary>Raised whenever displayed state changed, on a background thread.</summary>
    public event EventHandler? Updated;

    public IActivityStore Store => _store;

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

    public ObservationService(string databasePath)
    {
        _store = new SqliteActivityStore(databasePath);
        _foreground = new ForegroundWatcher();
        _idle = new IdleWatcher(IdleThreshold);
        _processes = new ProcessWatcher(ProcessPollInterval);
        _accessProbe = new WindowOwnerProbeRunner(AccessProbeInterval);
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

    /// <summary>
    /// Re-derives the ephemeral flags. Never throws out of the timer: a failed
    /// classification is a cosmetic loss, and taking down an app that is meant
    /// to run all day over it would not be.
    /// </summary>
    private void Reclassify()
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

    public IReadOnlyList<ActivityLine> RecentActivity()
    {
        lock (_feedGate)
        {
            return _feed.ToList();
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
            // all day; it is counted and surfaced instead.
            RecordingErrors++;
            LastErrorMessage = ex.Message;
        }

        lock (_feedGate)
        {
            _feed.AddFirst(new ActivityLine(DateTime.Now, kind, description));
            while (_feed.Count > FeedCapacity)
            {
                _feed.RemoveLast();
            }
        }

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
        _store.Dispose();
    }
}
