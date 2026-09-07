namespace Statehop.Core.Activity;

/// <summary>One line of the in-memory activity feed shown in the window.</summary>
/// <param name="Sequence">
/// Monotonic id, newest highest. It exists so a view can add only the lines it
/// has not seen instead of rebuilding its whole list. That rebuild was 80 % of
/// the app's CPU before it was
/// removed.
/// </param>
/// <param name="AtLocal">When it happened, in local time.</param>
/// <param name="Kind">Foreground, Idle or Process.</param>
/// <param name="Description">Process name and state — never a window title.</param>
public sealed record ActivityLine(long Sequence, DateTime AtLocal, string Kind, string Description)
{
    /// <summary>Pre-formatted for display, so the view needs no converter.</summary>
    public string TimeText => AtLocal.ToString("HH:mm:ss");
}

/// <summary>
/// A bounded, newest-first buffer of recent activity, safe to write from any
/// thread. Observation events arrive on three different threads; the window
/// reads on the UI thread.
///
/// This is deliberately in Core and deliberately free of any view type. The
/// Phase 1 timeline needs the same incremental read, and the logic was
/// untestable while it lived inside the WinUI project.
/// </summary>
public sealed class ActivityFeed
{
    /// <summary>Lines kept in memory. Older lines fall off the end.</summary>
    public const int DefaultCapacity = 200;

    private readonly LinkedList<ActivityLine> _lines = new();
    private readonly object _gate = new();
    private readonly int _capacity;

    private long _sequence;

    public ActivityFeed(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    public int Capacity => _capacity;

    /// <summary>Lines currently held.</summary>
    public int Count
    {
        get { lock (_gate) { return _lines.Count; } }
    }

    /// <summary>Highest sequence issued so far. Zero before the first line.</summary>
    public long LastSequence
    {
        get { lock (_gate) { return _sequence; } }
    }

    /// <summary>Adds a line at the front and returns it, with its new sequence.</summary>
    public ActivityLine Add(DateTime atLocal, string kind, string description)
    {
        lock (_gate)
        {
            var line = new ActivityLine(++_sequence, atLocal, kind, description);
            _lines.AddFirst(line);

            while (_lines.Count > _capacity)
            {
                _lines.RemoveLast();
            }

            return line;
        }
    }

    /// <summary>Everything held, newest first. For the first fill of a view.</summary>
    public IReadOnlyList<ActivityLine> Recent()
    {
        lock (_gate)
        {
            return _lines.ToList();
        }
    }

    /// <summary>
    /// Only the lines newer than <paramref name="afterSequence"/>, oldest
    /// first, so a caller can insert them at the top in order.
    ///
    /// A view that has fallen far behind gets at most a full buffer back:
    /// lines that already fell off the end are gone, and that is correct —
    /// they are no longer part of "recent activity".
    /// </summary>
    public IReadOnlyList<ActivityLine> Since(long afterSequence)
    {
        lock (_gate)
        {
            var added = new List<ActivityLine>();
            foreach (var line in _lines)
            {
                // Newest first, so the first line at or below the watermark
                // ends the walk.
                if (line.Sequence <= afterSequence)
                {
                    break;
                }

                added.Add(line);
            }

            added.Reverse();
            return added;
        }
    }
}
