using System.Diagnostics;
using Statehop.Core.Model;
using Statehop.Observation.Interop;
using Statehop.Core.Abstractions;

namespace Statehop.Observation.Watchers;

/// <summary>
/// Enumerates running processes on an interval and reports what appeared and
/// what disappeared between snapshots.
///
/// Windows has no cheap unprivileged notification for process start/stop (ETW
/// and WMI both cost more than they are worth here), so this polls. The
/// interval is deliberately coarse: the product needs to know that Docker was
/// running this afternoon, not the exact second it started. Start times come
/// from GetProcessTimes, which is precise even when the poll that noticed the
/// process was late.
/// </summary>
public sealed class ProcessWatcher : IProcessSource
{
    private readonly TimeSpan _interval;
    private readonly Timer _timer;
    private readonly Dictionary<int, string> _known = new();

    private bool _disposed;

    public event EventHandler<ProcessLifetimeChange>? LifetimeChanged;

    /// <summary>Number of processes seen in the most recent snapshot.</summary>
    public int LastSnapshotCount { get; private set; }

    public ProcessWatcher(TimeSpan interval)
    {
        _interval = interval;
        _timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start() => _timer.Change(TimeSpan.Zero, _interval);

    private void Poll()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return;
        }

        var now = DateTime.UtcNow;
        var current = new Dictionary<int, string>(processes.Length);

        try
        {
            foreach (var process in processes)
            {
                try
                {
                    current[process.Id] = process.ProcessName;
                }
                catch
                {
                    // The process ended between enumeration and read; ignore.
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        LastSnapshotCount = current.Count;

        // First snapshot establishes the baseline. Reporting every process
        // already running as "started" would be a lie about when they started.
        var isFirst = _known.Count == 0;

        foreach (var (pid, name) in current)
        {
            if (_known.ContainsKey(pid))
            {
                continue;
            }

            if (!isFirst)
            {
                LifetimeChanged?.Invoke(this, new ProcessLifetimeChange(
                    pid,
                    ProcessInspector.Resolve(pid, name),
                    ProcessLifetimeKind.Started,
                    now,
                    ProcessInspector.TryGetStartTimeUtc(pid)));
            }
        }

        foreach (var (pid, name) in _known)
        {
            if (current.ContainsKey(pid))
            {
                continue;
            }

            LifetimeChanged?.Invoke(this, new ProcessLifetimeChange(
                pid,
                new ProcessIdentity(name, null, ProcessAccessState.Exited),
                ProcessLifetimeKind.Exited,
                now,
                null));
        }

        _known.Clear();
        foreach (var (pid, name) in current)
        {
            _known[pid] = name;
        }
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
