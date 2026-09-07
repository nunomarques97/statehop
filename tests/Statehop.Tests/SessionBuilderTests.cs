using Statehop.Core.Sessions;

namespace Statehop.Tests;

/// <summary>
/// Every case below is grounded in one real measured
/// day of 6 Sep 2026 (1 862 foreground events over 29 process names), not in
/// invented traffic — the numbers appear in the comments so a future change
/// can be argued against real observation.
/// </summary>
[TestClass]
public sealed class SessionBuilderTests
{
    private static readonly DateOnly Day = new(2026, 9, 6);

    private static DateTime At(int hour, int minute, int second = 0) =>
        new(2026, 9, 6, hour, minute, second, DateTimeKind.Unspecified);

    private static ForegroundSpan Fg(string name, DateTime start, DateTime end) =>
        new(name, start, end);

    private static DayTimeline Build(
        IEnumerable<ForegroundSpan> foreground,
        IEnumerable<AbsenceSpan>? absence = null,
        SessionOptions? options = null) =>
        SessionBuilder.Build(Day, foreground, absence ?? [], options);

    // ---- The plain case ----------------------------------------------------

    [TestMethod]
    public void ContiguousRunsOfOneApp_BecomeOneBlock()
    {
        var timeline = Build(
        [
            Fg("Code", At(9, 0), At(9, 30)),
            Fg("Code", At(9, 30), At(10, 0)),
        ]);

        Assert.HasCount(1, timeline.Blocks);
        Assert.AreEqual(TimeSpan.FromHours(1), timeline.Blocks[0].Duration);
        Assert.AreEqual("Visual Studio Code", timeline.Blocks[0].DisplayName);
    }

    [TestMethod]
    public void TwoApps_KeepTheirOwnBlocksAndTheirOrder()
    {
        var timeline = Build(
        [
            Fg("Code", At(9, 0), At(10, 0)),
            Fg("chrome", At(10, 0), At(10, 30)),
        ]);

        Assert.HasCount(2, timeline.Blocks);
        Assert.AreEqual("code", timeline.Blocks[0].AppKey);
        Assert.AreEqual("chrome", timeline.Blocks[1].AppKey);
        Assert.AreEqual(2, timeline.AppsWithBlocks);
    }

    [TestMethod]
    public void EmptyDay_IsEmpty_NotAnError()
    {
        var timeline = Build([]);

        Assert.IsEmpty(timeline.Blocks);
        Assert.AreEqual(TimeSpan.Zero, timeline.InUse);
        Assert.IsNull(timeline.FirstLocal);
    }

    // ---- The short-switch rule the screen promises in words -----------------

    [TestMethod]
    public void AShortSwitchIsFoldedIntoTheBlockItHappenedIn()
    {
        // The explorer case: 480 foreground changes for 20.8 minutes in one
        // day, from Alt+Tab passing through it and from taskbar clicks.
        var timeline = Build(
        [
            Fg("Code", At(9, 0), At(9, 30)),
            Fg("explorer", At(9, 30), At(9, 30, 5)),
            Fg("Code", At(9, 30, 5), At(10, 0)),
        ]);

        Assert.HasCount(1, timeline.Blocks, "a five-second flash is not a block");
        Assert.AreEqual("code", timeline.Blocks[0].AppKey);
        Assert.AreEqual(TimeSpan.FromHours(1), timeline.Blocks[0].Duration);
        Assert.AreEqual(1, timeline.Blocks[0].AbsorbedSwitches);
        Assert.AreEqual(1, timeline.AbsorbedSwitches);
    }

    [TestMethod]
    public void TheThresholdIsExactlyTheOneMinuteTheScreenAdvertises()
    {
        // "Trocas de foco com menos de um minuto ficam agrupadas no bloco onde
        // aconteceram." The wording and the behaviour have to agree, so this
        // pins the boundary rather than a comfortable value either side of it.
        var justUnder = Build(
        [
            Fg("Code", At(9, 0), At(9, 30)),
            Fg("chrome", At(9, 30), At(9, 30, 59)),
            Fg("Code", At(9, 30, 59), At(10, 0)),
        ]);

        var exactlyOne = Build(
        [
            Fg("Code", At(9, 0), At(9, 30)),
            Fg("chrome", At(9, 30), At(9, 31)),
            Fg("Code", At(9, 31), At(10, 0)),
        ]);

        Assert.HasCount(1, justUnder.Blocks, "59 seconds is less than a minute");
        Assert.HasCount(3, exactlyOne.Blocks, "a full minute earns its own block");
    }

    [TestMethod]
    public void AbsorbingExtendsTheEnd_NeverMovesAStartTime()
    {
        // Block start times are what the screen prints in the left column, so
        // they stay true; only the end of the earlier block stretches.
        var timeline = Build(
        [
            Fg("Code", At(9, 0), At(9, 30)),
            Fg("explorer", At(9, 30), At(9, 30, 10)),
            Fg("chrome", At(9, 30, 10), At(10, 0)),
        ]);

        Assert.HasCount(2, timeline.Blocks);
        Assert.AreEqual(At(9, 0), timeline.Blocks[0].StartLocal);
        Assert.AreEqual(At(9, 30, 10), timeline.Blocks[1].StartLocal, "Chrome still starts when Chrome started");
        Assert.AreEqual(At(9, 30, 10), timeline.Blocks[0].EndLocal);
    }

    [TestMethod]
    public void ManyConsecutiveShortSwitches_CollapseToOneBlock()
    {
        // SnippingTool: 157 foreground changes for 3.8 minutes total.
        var spans = new List<ForegroundSpan> { Fg("Code", At(9, 0), At(9, 10)) };
        var cursor = At(9, 10);
        for (var i = 0; i < 40; i++)
        {
            spans.Add(Fg("SnippingTool", cursor, cursor.AddSeconds(3)));
            spans.Add(Fg("Code", cursor.AddSeconds(3), cursor.AddSeconds(30)));
            cursor = cursor.AddSeconds(30);
        }

        var timeline = Build(spans);

        Assert.HasCount(1, timeline.Blocks);
        Assert.AreEqual(40, timeline.Blocks[0].AbsorbedSwitches);
        Assert.AreEqual(2, timeline.AppsObserved, "both applications were still seen");
        Assert.AreEqual(1, timeline.AppsWithBlocks, "only one of them earned a block");
    }

    [TestMethod]
    public void AShortBlockSurvivesWhenItIsTheOnlyThingInTheStretch()
    {
        // Twenty seconds of Notepad between two breaks really is twenty
        // seconds of Notepad. The band draws it as a sliver, which is correct.
        var timeline = Build(
            [Fg("Notepad", At(9, 0), At(9, 0, 20))],
            [new AbsenceSpan(At(8, 0), At(9, 0), AbsenceReason.NoInput),
             new AbsenceSpan(At(9, 0, 20), At(10, 0), AbsenceReason.NoInput)]);

        Assert.HasCount(1, timeline.Blocks);
        Assert.AreEqual(TimeSpan.FromSeconds(20), timeline.Blocks[0].Duration);
    }

    [TestMethod]
    public void ShortSwitchesAreNeverFoldedAcrossAbsence()
    {
        // Folding across a break would claim the user worked through it.
        var timeline = Build(
        [
            Fg("Code", At(9, 0), At(9, 30)),
            Fg("chrome", At(10, 0), At(10, 0, 20)),
        ],
        [new AbsenceSpan(At(9, 30), At(10, 0), AbsenceReason.NoInput)]);

        Assert.HasCount(3, timeline.Blocks);
        Assert.AreEqual("code", timeline.Blocks[0].AppKey);
        Assert.IsTrue(timeline.Blocks[1].IsAbsence);
        Assert.AreEqual("chrome", timeline.Blocks[2].AppKey);
    }

    // ---- Absence -----------------------------------------------------------

    [TestMethod]
    public void ALockedScreenIsAbsence_NotTheThirdAppOfTheDay()
    {
        // LockApp held the foreground for 95.3 minutes in a single event on
        // the measured day. As an application it would have outranked every
        // real tool except two.
        var timeline = Build(
        [
            Fg("Code", At(9, 0), At(10, 0)),
            Fg("LockApp", At(10, 0), At(11, 35)),
            Fg("Code", At(11, 35), At(12, 0)),
        ]);

        Assert.HasCount(3, timeline.Blocks);
        Assert.AreEqual(AbsenceReason.ScreenLocked, timeline.Blocks[1].Absence);
        Assert.AreEqual("Sem utilização", timeline.Blocks[1].DisplayName);
        Assert.AreEqual(1, timeline.AppsWithBlocks, "the lock screen is not an app");
        Assert.AreEqual(1, timeline.AppsObserved);
    }

    [TestMethod]
    public void IdleIsCutOutOfTheAppItWasSittingIn()
    {
        // This is what makes "com utilização" honest: an editor left in front
        // while nobody touches the keyboard is not an hour of use.
        var timeline = Build(
            [Fg("Code", At(9, 0), At(11, 0))],
            [new AbsenceSpan(At(9, 30), At(10, 30), AbsenceReason.NoInput)]);

        Assert.HasCount(3, timeline.Blocks);
        Assert.AreEqual(TimeSpan.FromHours(1), timeline.InUse);
        Assert.AreEqual(TimeSpan.FromHours(2), timeline.AtComputer);
    }

    [TestMethod]
    public void LockedScreenBeatsMereIdle_WhenTheyOverlap()
    {
        var timeline = Build(
        [
            Fg("Code", At(9, 0), At(9, 30)),
            Fg("LockApp", At(9, 30), At(10, 30)),
            Fg("Code", At(10, 30), At(11, 0)),
        ],
        [new AbsenceSpan(At(9, 28), At(10, 32), AbsenceReason.NoInput)]);

        var absence = timeline.Blocks.Single(b => b.IsAbsence);
        Assert.AreEqual(AbsenceReason.ScreenLocked, absence.Absence, "the more specific reason wins");
    }

    [TestMethod]
    public void TimeWeNeverObserved_IsNotTimeAtTheComputer()
    {
        // The observer was not running. We cannot claim the user was there.
        var timeline = Build(
        [
            Fg("Code", At(9, 0), At(9, 30)),
            Fg("Code", At(14, 0), At(14, 30)),
        ]);

        Assert.HasCount(3, timeline.Blocks);
        Assert.AreEqual(AbsenceReason.NotObserved, timeline.Blocks[1].Absence);
        Assert.AreEqual(TimeSpan.FromHours(1), timeline.AtComputer);
        Assert.AreEqual(TimeSpan.FromHours(1), timeline.InUse);
    }

    [TestMethod]
    public void AbsenceAtTheEdgesOfTheDayIsTrimmed()
    {
        // The day starts when something happened, not when idle did.
        var timeline = Build(
            [Fg("Code", At(9, 0), At(10, 0))],
            [new AbsenceSpan(At(6, 0), At(9, 0), AbsenceReason.NoInput),
             new AbsenceSpan(At(10, 0), At(23, 0), AbsenceReason.NoInput)]);

        Assert.HasCount(1, timeline.Blocks);
        Assert.AreEqual(At(9, 0), timeline.FirstLocal);
        Assert.AreEqual(At(10, 0), timeline.LastLocal);
    }

    [TestMethod]
    public void AbsenceShorterThanTheMinimum_DoesNotEarnABlock()
    {
        var timeline = Build(
            [Fg("Code", At(9, 0), At(10, 0))],
            [new AbsenceSpan(At(9, 30), At(9, 30, 20), AbsenceReason.NoInput)]);

        Assert.HasCount(1, timeline.Blocks);
        Assert.AreEqual(TimeSpan.FromHours(1), timeline.Blocks[0].Duration, "the hole is closed, not shown");
    }

    // ---- Shell surfaces ----------------------------------------------------

    [TestMethod]
    public void ShellSurfacesAreAbsorbedHoweverLongTheyHeldTheForeground()
    {
        // Opening the Start menu is not switching application, so this one is
        // not a duration rule: a search box left open for ten minutes is still
        // not an app the user was using.
        var timeline = Build(
        [
            Fg("Code", At(9, 0), At(9, 30)),
            Fg("SearchHost", At(9, 30), At(9, 40)),
            Fg("Code", At(9, 40), At(10, 0)),
        ]);

        Assert.HasCount(1, timeline.Blocks);
        Assert.AreEqual("code", timeline.Blocks[0].AppKey);
        Assert.AreEqual(TimeSpan.FromHours(1), timeline.Blocks[0].Duration);
    }

    [TestMethod]
    public void ExplorerIsARealApp_AndKeepsABlockWhenItEarnsOne()
    {
        // The tempting fix for 480 daily switches is a name list. It would be
        // wrong: File Explorer is an application the user uses, and the
        // list would have to grow forever. Duration decides, not identity.
        var timeline = Build(
        [
            Fg("Code", At(9, 0), At(9, 30)),
            Fg("explorer", At(9, 30), At(9, 45)),
            Fg("Code", At(9, 45), At(10, 0)),
        ]);

        Assert.HasCount(3, timeline.Blocks);
        Assert.AreEqual("explorer", timeline.Blocks[1].AppKey);
        Assert.AreEqual("Explorador de Ficheiros", timeline.Blocks[1].DisplayName);
    }

    // ---- The three header figures ------------------------------------------

    [TestMethod]
    public void AtTheComputerAndInUseAreDifferentNumbers()
    {
        // Product rule: giving only the first would inflate the day.
        var timeline = Build(
        [
            Fg("Code", At(9, 0), At(10, 0)),
            Fg("LockApp", At(10, 0), At(10, 30)),
            Fg("chrome", At(10, 30), At(11, 0)),
        ]);

        Assert.AreEqual(TimeSpan.FromMinutes(90), timeline.InUse);
        Assert.AreEqual(TimeSpan.FromHours(2), timeline.AtComputer);
        Assert.IsGreaterThan(timeline.InUse, timeline.AtComputer);
    }

    [TestMethod]
    public void AppsObservedCountsMoreThanAppsWithBlocks()
    {
        // The header says "apps"; the palette only has to colour the ones that
        // own a block. Those are two different, and very differently sized,
        // numbers — which is what makes the colour problem tractable.
        var timeline = Build(
        [
            Fg("Code", At(9, 0), At(9, 30)),
            Fg("chrome", At(9, 30), At(9, 30, 4)),
            Fg("mintty", At(9, 30, 4), At(9, 30, 9)),
            Fg("python", At(9, 30, 9), At(9, 30, 12)),
            Fg("Code", At(9, 30, 12), At(10, 0)),
        ]);

        Assert.AreEqual(4, timeline.AppsObserved);
        Assert.AreEqual(1, timeline.AppsWithBlocks);
    }

    [TestMethod]
    public void ASliverLeftByClipping_IsNotABlock()
    {
        // The real day opened with one of these: a foreground span cut to
        // nothing by an absence that started on top of it. On the band it is a
        // zero-width segment; in the list it is a row reading "12:49 - 12:49".
        var timeline = Build(
        [
            Fg("explorer", At(12, 49), At(12, 49, 0)),
            Fg("Code", At(12, 51), At(13, 30)),
        ],
        [new AbsenceSpan(At(12, 49), At(12, 51), AbsenceReason.NoInput)]);

        Assert.HasCount(1, timeline.Blocks);
        Assert.AreEqual("code", timeline.Blocks[0].AppKey);
    }

    [TestMethod]
    public void TwoAbsencesLeftAdjacent_BecomeOne()
    {
        // Dropping a sliver between two breaks must not leave the band with
        // two touching absence segments, which would draw a seam in the middle
        // of one break.
        var timeline = Build(
        [
            Fg("Code", At(9, 0), At(9, 30)),
            Fg("chrome", At(10, 0), At(10, 0, 0)),
            Fg("Code", At(11, 0), At(11, 30)),
        ],
        [new AbsenceSpan(At(9, 30), At(10, 0), AbsenceReason.NoInput),
         new AbsenceSpan(At(10, 0), At(11, 0), AbsenceReason.NoInput)]);

        Assert.HasCount(3, timeline.Blocks);
        Assert.IsTrue(timeline.Blocks[1].IsAbsence);
        Assert.AreEqual(TimeSpan.FromMinutes(90), timeline.Blocks[1].Duration);
    }

    // ---- Boundaries --------------------------------------------------------

    [TestMethod]
    public void SpansAreClippedToTheDayAsked()
    {
        var timeline = SessionBuilder.Build(
            Day,
            [new ForegroundSpan("Code", new DateTime(2026, 9, 5, 23, 0, 0), At(1, 0))],
            []);

        Assert.HasCount(1, timeline.Blocks);
        Assert.AreEqual(Day.ToDateTime(TimeOnly.MinValue), timeline.Blocks[0].StartLocal);
        Assert.AreEqual(TimeSpan.FromHours(1), timeline.InUse);
    }

    [TestMethod]
    public void TheSequenceIsAlwaysOrderedAndGapless()
    {
        var timeline = Build(
        [
            Fg("Code", At(9, 0), At(9, 30)),
            Fg("chrome", At(9, 40), At(10, 0)),
            Fg("LockApp", At(10, 0), At(10, 30)),
            Fg("Code", At(10, 30), At(11, 0)),
        ]);

        for (var i = 1; i < timeline.Blocks.Count; i++)
        {
            Assert.AreEqual(
                timeline.Blocks[i - 1].EndLocal,
                timeline.Blocks[i].StartLocal,
                "the band is drawn from this sequence: a hole would be a lie about the day");
        }
    }

    [TestMethod]
    public void OverlappingSpans_DoNotDoubleCountTime()
    {
        var timeline = Build(
        [
            Fg("Code", At(9, 0), At(9, 40)),
            Fg("chrome", At(9, 30), At(10, 0)),
        ]);

        Assert.AreEqual(TimeSpan.FromHours(1), timeline.InUse);
    }

    [TestMethod]
    public void AnUnknownProcessIsAnApplicationUnderItsOwnName()
    {
        var timeline = Build([Fg("sample-app", At(9, 0), At(10, 0))]);

        Assert.AreEqual("sample-app", timeline.Blocks[0].DisplayName);
        Assert.AreEqual("sample-app", timeline.Blocks[0].AppKey);
    }

    [TestMethod]
    public void TheKeyIsCaseInsensitive_SoOneAppIsOneApp()
    {
        var timeline = Build(
        [
            Fg("Code", At(9, 0), At(9, 30)),
            Fg("code", At(9, 30), At(10, 0)),
        ]);

        Assert.HasCount(1, timeline.Blocks);
    }
}
