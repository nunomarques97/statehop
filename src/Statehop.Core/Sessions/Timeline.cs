namespace Statehop.Core.Sessions;

/// <summary>What a raw foreground observation actually is, once normalised.</summary>
public enum AppKind
{
    /// <summary>A real application the user switched to.</summary>
    Application,

    /// <summary>
    /// A shell surface: Start menu, search flyout, file picker. These take the
    /// foreground without the user having switched application, so they are
    /// never a block of their own regardless of how long they held it.
    /// </summary>
    ShellSurface,

    /// <summary>The lock or logon screen. Absence, not an application.</summary>
    LockScreen,
}

/// <summary>Why a stretch of the day carries no activity.</summary>
public enum AbsenceReason
{
    /// <summary>No keyboard or mouse input for longer than the idle threshold.</summary>
    NoInput,

    /// <summary>The workstation was locked.</summary>
    ScreenLocked,

    /// <summary>
    /// Nothing was recorded. The observer was not running, or the machine was
    /// off. Deliberately distinct from the other two: we cannot claim the
    /// user was at the computer during time we did not observe.
    /// </summary>
    NotObserved,
}

/// <summary>An application held the foreground over this interval. Local time.</summary>
public sealed record ForegroundSpan(string ProcessName, DateTime StartLocal, DateTime EndLocal);

/// <summary>A stretch with no user activity. Local time.</summary>
public sealed record AbsenceSpan(DateTime StartLocal, DateTime EndLocal, AbsenceReason Reason);

/// <summary>One segment of the band, and one row of the block list.</summary>
/// <param name="AppKey">
/// Stable key of the application, or the empty string for absence. This is
/// what the colour is assigned from — see the palette rule in DESIGN.md.
/// </param>
/// <param name="DisplayName">What the screen shows.</param>
/// <param name="Absence">Set for an absence block, null for an application.</param>
/// <param name="AbsorbedSwitches">
/// How many short foreground switches were folded into this block. The screen
/// promises this behaviour in words, so the number has to be recoverable.
/// </param>
public sealed record TimelineBlock(
    string AppKey,
    string DisplayName,
    DateTime StartLocal,
    DateTime EndLocal,
    AbsenceReason? Absence,
    int AbsorbedSwitches)
{
    public TimeSpan Duration => EndLocal - StartLocal;

    public bool IsAbsence => Absence is not null;
}

/// <summary>
/// A day, as the timeline screen reads it: an ordered, gapless sequence of
/// blocks plus the three figures in the header.
/// </summary>
/// <param name="AtComputer">
/// Application time plus absence that we actually observed. Time we did not
/// observe (<see cref="AbsenceReason.NotObserved"/>) is excluded on purpose.
/// </param>
/// <param name="InUse">
/// Application time only. The distinction from <paramref name="AtComputer"/>
/// is a product rule: giving only the first would inflate the day.
/// </param>
/// <param name="AppsObserved">
/// Distinct applications seen at all, including the ones whose switches were
/// too short to earn a block. This is the header's "apps" figure.
/// </param>
/// <param name="AppsWithBlocks">
/// Distinct applications that own at least one block. This is the set that
/// needs a distinguishable colour, and it is much smaller.
/// </param>
public sealed record DayTimeline(
    DateOnly Day,
    IReadOnlyList<TimelineBlock> Blocks,
    TimeSpan AtComputer,
    TimeSpan InUse,
    int AppsObserved,
    int AppsWithBlocks,
    int AbsorbedSwitches)
{
    public static DayTimeline Empty(DateOnly day) =>
        new(day, [], TimeSpan.Zero, TimeSpan.Zero, 0, 0, 0);

    public DateTime? FirstLocal => Blocks.Count == 0 ? null : Blocks[0].StartLocal;

    public DateTime? LastLocal => Blocks.Count == 0 ? null : Blocks[^1].EndLocal;
}
