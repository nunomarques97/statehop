using Statehop.Core.Model;
using Statehop.Observation.Interop;

namespace Statehop.Observation.Watchers;

/// <summary>
/// Detects whether the user is providing input, via GetLastInputInfo.
///
/// IMPORTANT — what this does and does not mean. GetLastInputInfo reports the
/// time of the last keyboard or mouse input in the session. So "idle" here is
/// strictly "the user has not touched the machine for longer than the
/// threshold". It says nothing about whether an application is doing useful
/// work, and must never be read as "this app is inactive, therefore it is not
/// needed" — see the UX rule in docs/PRODUCT.md. A build, a download or a
/// container can be the most important thing running while input is idle.
/// </summary>
public sealed class IdleWatcher : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly TimeSpan _threshold;
    private readonly Timer _timer;

    private bool _isIdle;
    private bool _disposed;

    public event EventHandler<IdleChange>? IdleChanged;

    /// <summary>Current idle state, for display.</summary>
    public bool IsIdle => _isIdle;

    /// <param name="threshold">How long without input counts as idle.</param>
    public IdleWatcher(TimeSpan threshold)
    {
        _threshold = threshold;
        _timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start() => _timer.Change(TimeSpan.Zero, PollInterval);

    /// <summary>Time since the last keyboard or mouse input.</summary>
    public static TimeSpan GetIdleTime()
    {
        var info = new NativeMethods.LASTINPUTINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.LASTINPUTINFO>(),
        };

        if (!NativeMethods.GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        // Both values are 32-bit millisecond tick counts that wrap roughly
        // every 49 days; the unchecked subtraction stays correct across a wrap.
        var elapsed = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(elapsed);
    }

    private void Poll()
    {
        var idleNow = GetIdleTime() >= _threshold;
        if (idleNow == _isIdle)
        {
            return;
        }

        _isIdle = idleNow;
        IdleChanged?.Invoke(this, new IdleChange(idleNow, DateTime.UtcNow));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
    }
}
