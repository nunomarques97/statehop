namespace Statehop.Core.Sessions;

/// <summary>
/// Everything one local day needs, in the shape <see cref="SessionBuilder"/>
/// consumes. Reading and interpreting are kept apart on purpose: the builder
/// has no database, and the store has no opinion about blocks.
/// </summary>
public sealed record DayObservations(
    DateOnly Day,
    IReadOnlyList<ForegroundSpan> Foreground,
    IReadOnlyList<AbsenceSpan> Absence)
{
    public static DayObservations Empty(DateOnly day) => new(day, [], []);
}
