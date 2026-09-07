using Statehop.Core.Model;

namespace Statehop.Core.Abstractions;

/// <summary>
/// The four things Statehop watches, as interfaces rather than as concrete
/// Win32 watchers.
///
/// This is what lets <see cref="Observation.ObservationService"/> live in Core
/// and be tested without Windows. The implementations in
/// <c>Statehop.Observation</c> are hooks and timers over user32/kernel32; the
/// service only ever sees the events they raise, which is the
/// Observation → Inference → Decision → Execution separation the product rule
/// in docs/PRODUCT.md asks for.
///
/// Every source starts inert. Nothing observes until <c>Start</c> is called,
/// so constructing one is always safe.
/// </summary>
public interface IObservationSource : IDisposable
{
    /// <summary>Begins observing. Safe to call once.</summary>
    void Start();
}

/// <summary>Raises an event whenever the foreground application changes.</summary>
public interface IForegroundSource : IObservationSource
{
    event EventHandler<ForegroundChange>? ForegroundChanged;
}

/// <summary>
/// Raises an event when the user stops or resumes giving input.
///
/// "Idle" is absence of keyboard and mouse input, never "this application is
/// doing nothing" — see the UX rule in docs/PRODUCT.md.
/// </summary>
public interface IIdleSource : IObservationSource
{
    event EventHandler<IdleChange>? IdleChanged;

    bool IsIdle { get; }
}

/// <summary>Raises an event when a process appears or disappears.</summary>
public interface IProcessSource : IObservationSource
{
    event EventHandler<ProcessLifetimeChange>? LifetimeChanged;

    /// <summary>Processes seen in the most recent snapshot, for display.</summary>
    int LastSnapshotCount { get; }
}

/// <summary>Raises the periodic B0.2 measurement: who owns a visible window, and could we read them.</summary>
public interface IWindowOwnerSource : IObservationSource
{
    event EventHandler<IReadOnlyList<WindowOwnerProbe>>? Probed;
}
