using Statehop.Core.Model;
using Statehop.Observation.Interop;

namespace Statehop.Observation.Watchers;

/// <summary>
/// The B0.2 measurement.
///
/// The question is not "how many processes exist" but "how many of the apps
/// the user actually uses are invisible to an unelevated observer". So the
/// probe enumerates top-level visible windows, maps each to its owning
/// process, and records whether identity could be read. Processes with no
/// window are irrelevant here, and system/protected processes are flagged so
/// they can be excluded from the headline number — docs/adr/001-packaging.md
/// already showed those are unreachable in every packaging mode.
/// </summary>
public sealed class WindowOwnerProbeRunner : IDisposable
{
    private readonly TimeSpan _interval;
    private readonly Timer _timer;
    private bool _disposed;

    public event EventHandler<IReadOnlyList<Core.Model.WindowOwnerProbe>>? Probed;

    public WindowOwnerProbeRunner(TimeSpan interval)
    {
        _interval = interval;
        _timer = new Timer(_ => Probed?.Invoke(this, Run()), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start() => _timer.Change(TimeSpan.Zero, _interval);

    /// <summary>Runs one pass immediately and returns the result.</summary>
    public static IReadOnlyList<Core.Model.WindowOwnerProbe> Run()
    {
        var pids = new HashSet<int>();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            // Visible, unowned top-level windows: a good approximation of
            // "windows a person sees in the taskbar and alt-tab".
            if (NativeMethods.IsWindowVisible(hWnd)
                && NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER) == IntPtr.Zero)
            {
                NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
                if (pid != 0)
                {
                    pids.Add((int)pid);
                }
            }

            return true;
        }, IntPtr.Zero);

        var results = new List<Core.Model.WindowOwnerProbe>(pids.Count);
        foreach (var pid in pids)
        {
            var identity = ProcessInspector.Resolve(pid);
            if (identity.AccessState == ProcessAccessState.Exited)
            {
                continue;
            }

            results.Add(new Core.Model.WindowOwnerProbe(
                identity.Name,
                identity.AccessState,
                SystemProcesses.IsProtected(identity.Name)));
        }

        return results;
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
