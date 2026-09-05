using Statehop.Core.Model;
using Statehop.Observation.Interop;

namespace Statehop.Observation.Watchers;

/// <summary>
/// Tracks which application is in the foreground.
///
/// Uses SetWinEventHook(EVENT_SYSTEM_FOREGROUND) instead of polling: Windows
/// calls back only when the foreground actually changes, which is what keeps
/// the CPU cost of a full working day near zero (the Phase 0 gate). A slow
/// reconciliation timer covers the rare transitions the hook can miss, for
/// example a foreground change while the hook was being installed.
///
/// PRIVACY: the window title is never read or stored. Only the owning process
/// identity and the timestamp are recorded.
/// </summary>
public sealed class ForegroundWatcher : IDisposable
{
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(15);

    // Rooted for the lifetime of the hook: Windows holds a raw function
    // pointer, so letting this be collected would crash the process.
    private readonly NativeMethods.WinEventProc _callback;
    private readonly Timer _reconcileTimer;

    private IntPtr _hook;
    private int _lastPid = -1;
    private bool _disposed;

    public event EventHandler<ForegroundChange>? ForegroundChanged;

    public ForegroundWatcher()
    {
        _callback = OnWinEvent;
        _reconcileTimer = new Timer(_ => Reconcile(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Installs the hook. Must be called on a thread that pumps messages —
    /// WINEVENT_OUTOFCONTEXT delivers callbacks through that thread's queue.
    /// </summary>
    public void Start()
    {
        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _callback,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        if (_hook == IntPtr.Zero)
        {
            throw new InvalidOperationException("SetWinEventHook failed for EVENT_SYSTEM_FOREGROUND.");
        }

        Reconcile();
        _reconcileTimer.Change(ReconcileInterval, ReconcileInterval);
    }

    private void OnWinEvent(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND)
        {
            Publish(hwnd);
        }
    }

    private void Reconcile() => Publish(NativeMethods.GetForegroundWindow());

    private void Publish(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
        {
            return;
        }

        var current = (int)pid;
        if (Interlocked.Exchange(ref _lastPid, current) == current)
        {
            return;
        }

        var identity = ProcessInspector.Resolve(current);
        ForegroundChanged?.Invoke(this, new ForegroundChange(current, identity, DateTime.UtcNow));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _reconcileTimer.Dispose();
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
