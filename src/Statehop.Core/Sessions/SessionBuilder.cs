namespace Statehop.Core.Sessions;

/// <summary>
/// Thresholds for turning raw observation into blocks. Both are reader-side:
/// they change what the screen shows, never what is stored, so a wrong value
/// costs a redraw and not a day of data.
/// </summary>
public sealed record SessionOptions
{
    /// <summary>
    /// A foreground run shorter than this does not earn a block of its own.
    ///
    /// One minute is not a tuned number: it is the promise the screen makes in
    /// words — "Trocas de foco com menos de um minuto ficam agrupadas no bloco
    /// onde aconteceram." If this changes, that sentence changes with it.
    /// </summary>
    public TimeSpan ShortSwitch { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Absence shorter than this is not worth a block either. Idle events are
    /// already longer than the idle threshold by construction; this catches
    /// the small holes left by clipping and by observer restarts.
    /// </summary>
    public TimeSpan MinimumAbsence { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Floor under which a block is not a block at all. Clipping an
    /// application against absence can leave a sliver of a few hundred
    /// milliseconds; on the band that is a zero-width segment, and in the list
    /// a row reading "12:49 - 12:49". Neither tells the user anything.
    /// </summary>
    public TimeSpan MinimumBlock { get; init; } = TimeSpan.FromSeconds(1);

    public static SessionOptions Default { get; } = new();
}

/// <summary>
/// Turns a day of raw foreground and idle observation into the ordered,
/// gapless sequence of blocks the timeline draws.
///
/// Pure: no clock, no store, no UI. Everything below is decided from the
/// arguments, which is what makes the session rules testable.
/// </summary>
public static class SessionBuilder
{
    private sealed class Run
    {
        public string Key = string.Empty;
        public string Display = string.Empty;
        public AppKind Kind;
        public AbsenceReason? Absence;
        public DateTime Start;
        public DateTime End;
        public int Absorbed;

        public TimeSpan Duration => End - Start;
    }

    public static DayTimeline Build(
        DateOnly day,
        IEnumerable<ForegroundSpan> foreground,
        IEnumerable<AbsenceSpan> absence,
        SessionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(foreground);
        ArgumentNullException.ThrowIfNull(absence);

        var opts = options ?? SessionOptions.Default;
        var dayStart = day.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);

        var apps = new List<(NormalizedApp App, DateTime Start, DateTime End)>();
        var gaps = new List<Run>();
        var observed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var span in foreground)
        {
            var start = Later(span.StartLocal, dayStart);
            var end = Earlier(span.EndLocal, dayEnd);
            if (end <= start) { continue; }

            var app = AppNormalizer.Normalize(span.ProcessName);
            if (app.Kind == AppKind.LockScreen)
            {
                // A locked screen is absence. Left as an application it would
                // have been the third "app" of the measured day, at 95 minutes.
                gaps.Add(new Run { Start = start, End = end, Absence = AbsenceReason.ScreenLocked });
                continue;
            }

            if (app.Kind == AppKind.Application) { observed.Add(app.Key); }
            apps.Add((app, start, end));
        }

        foreach (var span in absence)
        {
            var start = Later(span.StartLocal, dayStart);
            var end = Earlier(span.EndLocal, dayEnd);
            if (end <= start) { continue; }
            gaps.Add(new Run { Start = start, End = end, Absence = span.Reason });
        }

        var absences = MergeAbsences(gaps, opts.MinimumAbsence);
        var atoms = ClipAgainstAbsence(apps, absences);
        atoms.AddRange(absences);
        atoms.Sort(static (a, b) => a.Start.CompareTo(b.Start));

        var sequence = Cover(atoms, opts.MinimumAbsence);
        Collapse(sequence, opts.ShortSwitch);
        DropSlivers(sequence, opts.MinimumBlock);
        TrimEdges(sequence);

        return Summarise(day, sequence, observed.Count);
    }

    /// <summary>
    /// Overlapping absence becomes one absence. A locked screen wins over mere
    /// lack of input, because it says more; both win over time we never saw.
    /// </summary>
    private static List<Run> MergeAbsences(List<Run> gaps, TimeSpan minimum)
    {
        gaps.Sort(static (a, b) => a.Start.CompareTo(b.Start));

        var merged = new List<Run>();
        foreach (var gap in gaps)
        {
            if (merged.Count > 0 && gap.Start <= merged[^1].End)
            {
                var last = merged[^1];
                if (gap.End > last.End) { last.End = gap.End; }
                if (Rank(gap.Absence) > Rank(last.Absence)) { last.Absence = gap.Absence; }
                continue;
            }

            merged.Add(new Run { Start = gap.Start, End = gap.End, Absence = gap.Absence });
        }

        merged.RemoveAll(x => x.Duration < minimum);
        return merged;

        static int Rank(AbsenceReason? reason) => reason switch
        {
            AbsenceReason.ScreenLocked => 2,
            AbsenceReason.NoInput => 1,
            _ => 0,
        };
    }

    /// <summary>
    /// Cuts absence out of the application spans. This is what makes "com
    /// utilização" honest: an editor left in front for two hours while nobody
    /// touched the keyboard is not two hours of use.
    /// </summary>
    private static List<Run> ClipAgainstAbsence(
        List<(NormalizedApp App, DateTime Start, DateTime End)> apps,
        List<Run> absences)
    {
        var result = new List<Run>();

        foreach (var (app, start, end) in apps)
        {
            var pieces = new List<(DateTime Start, DateTime End)> { (start, end) };

            foreach (var absent in absences)
            {
                var next = new List<(DateTime Start, DateTime End)>();
                foreach (var (pieceStart, pieceEnd) in pieces)
                {
                    if (absent.End <= pieceStart || absent.Start >= pieceEnd)
                    {
                        next.Add((pieceStart, pieceEnd));
                        continue;
                    }

                    if (absent.Start > pieceStart) { next.Add((pieceStart, absent.Start)); }
                    if (absent.End < pieceEnd) { next.Add((absent.End, pieceEnd)); }
                }

                pieces = next;
            }

            foreach (var (pieceStart, pieceEnd) in pieces)
            {
                result.Add(new Run
                {
                    Key = app.Key,
                    Display = app.DisplayName,
                    Kind = app.Kind,
                    Start = pieceStart,
                    End = pieceEnd,
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Makes the sequence gapless. A hole long enough to matter becomes absence
    /// we did not observe; anything smaller is closed by stretching the block
    /// before it, which is cheaper than showing the user a seam they cannot
    /// act on.
    /// </summary>
    private static List<Run> Cover(List<Run> atoms, TimeSpan minimumAbsence)
    {
        var covered = new List<Run>();

        foreach (var atom in atoms)
        {
            if (covered.Count == 0) { covered.Add(atom); continue; }

            var previous = covered[^1];
            if (atom.Start < previous.End)
            {
                // Overlap: the last foreground event of a run is still open
                // while the next already started. Time belongs to one block.
                atom.Start = previous.End;
                if (atom.End <= atom.Start) { continue; }
            }

            var hole = atom.Start - previous.End;
            if (hole >= minimumAbsence)
            {
                covered.Add(new Run
                {
                    Start = previous.End,
                    End = atom.Start,
                    Absence = AbsenceReason.NotObserved,
                });
            }
            else if (hole > TimeSpan.Zero)
            {
                previous.End = atom.Start;
            }

            covered.Add(atom);
        }

        return covered;
    }

    /// <summary>
    /// Applies the short-switch rule inside each stretch of activity. Absence
    /// is a wall: a switch is never folded across it, because that would claim
    /// the user was working through a break.
    /// </summary>
    private static void Collapse(List<Run> sequence, TimeSpan shortSwitch)
    {
        var start = 0;
        while (start < sequence.Count)
        {
            if (sequence[start].Absence is not null) { start++; continue; }

            var end = start;
            while (end < sequence.Count && sequence[end].Absence is null) { end++; }

            var group = sequence.GetRange(start, end - start);
            CollapseGroup(group, shortSwitch);
            sequence.RemoveRange(start, end - start);
            sequence.InsertRange(start, group);
            start += group.Count;
        }
    }

    private static void CollapseGroup(List<Run> group, TimeSpan shortSwitch)
    {
        MergeSameApp(group);

        while (group.Count > 1)
        {
            var index = PickAbsorbable(group, shortSwitch);
            if (index < 0) { break; }

            Absorb(group, index);
            MergeSameApp(group);
        }
    }

    private static int PickAbsorbable(List<Run> group, TimeSpan shortSwitch)
    {
        // Shell surfaces go first whatever their length: opening the Start menu
        // or a file dialog is not switching application.
        for (var i = 0; i < group.Count; i++)
        {
            if (group[i].Kind == AppKind.ShellSurface) { return i; }
        }

        var pick = -1;
        for (var i = 0; i < group.Count; i++)
        {
            if (group[i].Duration >= shortSwitch) { continue; }
            if (pick < 0 || group[i].Duration < group[pick].Duration) { pick = i; }
        }

        return pick;
    }

    private static void Absorb(List<Run> group, int index)
    {
        if (index > 0)
        {
            // Into the block before it, so block start times stay true: a block
            // starts when the user actually arrived, and only its end
            // stretches. Start times are what the screen puts in the left
            // column, so those are the ones worth keeping honest.
            group[index - 1].End = group[index].End;
            group[index - 1].Absorbed += 1 + group[index].Absorbed;
        }
        else
        {
            group[1].Start = group[0].Start;
            group[1].Absorbed += 1 + group[0].Absorbed;
        }

        group.RemoveAt(index);
    }

    private static void MergeSameApp(List<Run> group)
    {
        for (var i = group.Count - 1; i > 0; i--)
        {
            if (!string.Equals(group[i].Key, group[i - 1].Key, StringComparison.Ordinal)) { continue; }

            group[i - 1].End = group[i].End;
            group[i - 1].Absorbed += group[i].Absorbed;
            group.RemoveAt(i);
        }
    }

    /// <summary>
    /// Removes application blocks too short to draw, and merges whatever ends
    /// up adjacent afterwards. Runs after collapsing, because a sliver is
    /// usually what is left when absence cuts across an application, not
    /// something the short-switch rule could have caught.
    /// </summary>
    private static void DropSlivers(List<Run> sequence, TimeSpan minimum)
    {
        var changed = true;
        while (changed && sequence.Count > 1)
        {
            changed = false;
            for (var i = 0; i < sequence.Count; i++)
            {
                if (sequence[i].Absence is not null || sequence[i].Duration >= minimum) { continue; }

                if (i > 0) { sequence[i - 1].End = sequence[i].End; }
                else { sequence[1].Start = sequence[0].Start; }

                sequence.RemoveAt(i);
                changed = true;
                break;
            }
        }

        for (var i = sequence.Count - 1; i > 0; i--)
        {
            var current = sequence[i];
            var previous = sequence[i - 1];

            var sameApp = current.Absence is null && previous.Absence is null
                && string.Equals(current.Key, previous.Key, StringComparison.Ordinal);
            var bothAbsent = current.Absence is not null && previous.Absence is not null;
            if (!sameApp && !bothAbsent) { continue; }

            previous.End = current.End;
            previous.Absorbed += current.Absorbed;
            if (bothAbsent && current.Absence == AbsenceReason.ScreenLocked)
            {
                previous.Absence = AbsenceReason.ScreenLocked;
            }

            sequence.RemoveAt(i);
        }
    }

    /// <summary>The day starts when something happened, not when idle did.</summary>
    private static void TrimEdges(List<Run> sequence)
    {
        while (sequence.Count > 0 && sequence[0].Absence is not null) { sequence.RemoveAt(0); }
        while (sequence.Count > 0 && sequence[^1].Absence is not null) { sequence.RemoveAt(sequence.Count - 1); }
    }

    private static DayTimeline Summarise(DateOnly day, List<Run> sequence, int observed)
    {
        if (sequence.Count == 0) { return DayTimeline.Empty(day); }

        var blocks = new List<TimelineBlock>(sequence.Count);
        var inUse = TimeSpan.Zero;
        var atComputer = TimeSpan.Zero;
        var absorbed = 0;
        var withBlocks = new HashSet<string>(StringComparer.Ordinal);

        foreach (var run in sequence)
        {
            if (run.Absence is null)
            {
                inUse += run.Duration;
                atComputer += run.Duration;
                withBlocks.Add(run.Key);
            }
            else if (run.Absence != AbsenceReason.NotObserved)
            {
                // Time we did not observe is not time at the computer. We
                // cannot claim presence for a stretch we never saw.
                atComputer += run.Duration;
            }

            absorbed += run.Absorbed;
            blocks.Add(new TimelineBlock(
                run.Absence is null ? run.Key : string.Empty,
                run.Absence is null ? run.Display : "Sem utilização",
                run.Start,
                run.End,
                run.Absence,
                run.Absorbed));
        }

        return new DayTimeline(day, blocks, atComputer, inUse, observed, withBlocks.Count, absorbed);
    }

    private static DateTime Later(DateTime a, DateTime b) => a > b ? a : b;

    private static DateTime Earlier(DateTime a, DateTime b) => a < b ? a : b;
}
