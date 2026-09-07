using Microsoft.Data.Sqlite;
using Statehop.Core.Abstractions;
using Statehop.Core.Model;
using Statehop.Core.Sessions;
using Statehop.Storage;

namespace Statehop.Tests;

/// <summary>
/// The retention policy, and the read path the timeline will sit on.
///
/// Retention deletes observation, which cannot be undone, so the preview and
/// the real run are held to the same numbers here — that is the property the
/// user's trust actually rests on.
/// </summary>
[TestClass]
public sealed class RetentionTests
{
    private string _databasePath = string.Empty;

    [TestInitialize]
    public void CreateTempDatabase() =>
        _databasePath = Path.Combine(Path.GetTempPath(), $"statehop-retention-{Guid.NewGuid():N}.db");

    [TestCleanup]
    public void DeleteTempDatabase()
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var file = _databasePath + suffix;
            try
            {
                if (File.Exists(file)) { File.Delete(file); }
            }
            catch (IOException)
            {
                // A temp file still held open must not turn an assertion
                // failure into a cleanup failure and hide the real message.
            }
        }
    }

    private SqliteActivityStore Open() => new(_databasePath);

    private static ProcessIdentity App(string name) =>
        new(name, name + ".exe", ProcessAccessState.Accessible);

    /// <summary>Local wall-clock time, converted the way the store stores it.</summary>
    private static DateTime Utc(int daysAgo, int hour, int minute = 0) =>
        new DateTime(2026, 9, 6, hour, minute, 0, DateTimeKind.Unspecified)
            .AddDays(-daysAgo)
            .ToUniversalTime();

    private static readonly DateTime Now = Utc(0, 23, 0);

    /// <summary>
    /// Durations are computed in SQL with julianday(), which is float
    /// arithmetic and lands a millisecond or two either side. Pinning an exact
    /// value would pin a rounding artefact, not the behaviour under test.
    /// </summary>
    private void AssertDuration(TimeSpan expected, string sql, string because)
    {
        var actual = Count(sql);
        Assert.IsLessThanOrEqualTo(5, Math.Abs(expected.TotalMilliseconds - actual), because);
    }

    private long Count(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    // ---- The window --------------------------------------------------------

    [TestMethod]
    public void NothingOlderThanTheWindow_MeansNothingIsTouched()
    {
        using var store = Open();
        store.RecordForeground(new ForegroundChange(1, App("Code"), Utc(1, 9)));
        store.RecordForeground(new ForegroundChange(2, App("chrome"), Utc(1, 10)));

        var outcome = store.ApplyRetention(RetentionPolicy.Default, Now, apply: true);

        Assert.AreEqual(0, outcome.TotalRowsRemoved);
        Assert.AreEqual(2, Count("SELECT COUNT(*) FROM foreground_event;"));
    }

    [TestMethod]
    public void TheCutoffIsTheWindowCountedBackFromToday()
    {
        using var store = Open();
        var outcome = store.ApplyRetention(new RetentionPolicy { RawDays = 90 }, Now, apply: false);

        Assert.AreEqual(
            DateOnly.FromDateTime(Now.ToLocalTime().Date.AddDays(-90)),
            outcome.Cutoff);
    }

    // ---- Preview and run agree ---------------------------------------------

    [TestMethod]
    public void APreviewReportsTheRealNumbersAndDeletesNothing()
    {
        using var store = Open();
        SeedOldDay(store);

        var preview = store.ApplyRetention(RetentionPolicy.Default, Now, apply: false);

        Assert.IsFalse(preview.Applied);
        Assert.IsGreaterThan(0, preview.ForegroundRowsRemoved);
        Assert.AreEqual(3, Count("SELECT COUNT(*) FROM foreground_event;"), "a preview must not delete");
        Assert.AreEqual(0, Count("SELECT COUNT(*) FROM daily_app_usage;"), "nor write");

        var applied = store.ApplyRetention(RetentionPolicy.Default, Now, apply: true);

        Assert.IsTrue(applied.Applied);
        Assert.AreEqual(preview.ForegroundRowsRemoved, applied.ForegroundRowsRemoved);
        Assert.AreEqual(preview.IdleRowsRemoved, applied.IdleRowsRemoved);
        Assert.AreEqual(preview.LifetimeRowsRemoved, applied.LifetimeRowsRemoved);
        Assert.AreEqual(preview.Cutoff, applied.Cutoff);
    }

    // ---- What the roll-up keeps, and what it loses --------------------------

    [TestMethod]
    public void OldRawEventsBecomeDailyTotals()
    {
        using var store = Open();
        SeedOldDay(store);

        store.ApplyRetention(RetentionPolicy.Default, Now, apply: true);

        Assert.AreEqual(0, Count("SELECT COUNT(*) FROM foreground_event;"));
        Assert.AreEqual(3, Count("SELECT COUNT(*) FROM daily_app_usage;"),
            "one row per app per day: two that held the foreground, and node, which only ran");
        AssertDuration(
            TimeSpan.FromHours(2),
            "SELECT SUM(foreground_ms) FROM daily_app_usage;",
            "the total time of the day survives the raw rows");
        Assert.AreEqual(3, Count("SELECT SUM(switches) FROM daily_app_usage;"));
    }

    [TestMethod]
    public void IdleIsRolledUpToo_SoAbsenceIsNotLostWithTheRawRows()
    {
        using var store = Open();
        SeedOldDay(store);

        store.ApplyRetention(RetentionPolicy.Default, Now, apply: true);

        Assert.AreEqual(0, Count("SELECT COUNT(*) FROM idle_event;"));
        Assert.AreEqual(1, Count("SELECT COUNT(*) FROM daily_absence;"));
        AssertDuration(
            TimeSpan.FromMinutes(30),
            "SELECT SUM(idle_ms) FROM daily_absence;",
            "the absence of the day survives too");
    }

    [TestMethod]
    public void ProcessLifetimeIsRolledUpToStartsAndExits()
    {
        using var store = Open();
        SeedOldDay(store);

        store.ApplyRetention(RetentionPolicy.Default, Now, apply: true);

        Assert.AreEqual(0, Count("SELECT COUNT(*) FROM process_lifetime_event;"));
        Assert.AreEqual(1, Count("SELECT SUM(starts) FROM daily_app_usage;"));
        Assert.AreEqual(1, Count("SELECT SUM(exits) FROM daily_app_usage;"));
    }

    [TestMethod]
    public void RecentDaysAreLeftAlone()
    {
        using var store = Open();
        SeedOldDay(store);
        store.RecordForeground(new ForegroundChange(9, App("Code"), Utc(2, 9)));
        store.RecordForeground(new ForegroundChange(9, App("Code"), Utc(2, 10)));

        store.ApplyRetention(RetentionPolicy.Default, Now, apply: true);

        Assert.AreEqual(2, Count("SELECT COUNT(*) FROM foreground_event;"), "two days ago is inside the window");
    }

    [TestMethod]
    public void RunningItTwice_DoesNotDoubleCountTheTotals()
    {
        using var store = Open();
        SeedOldDay(store);

        store.ApplyRetention(RetentionPolicy.Default, Now, apply: true);
        var second = store.ApplyRetention(RetentionPolicy.Default, Now, apply: true);

        Assert.AreEqual(0, second.TotalRowsRemoved);
        AssertDuration(
            TimeSpan.FromHours(2),
            "SELECT SUM(foreground_ms) FROM daily_app_usage;",
            "a second pass has nothing left to add");
    }

    [TestMethod]
    public void IdentitiesSurviveTheirRawEvents()
    {
        // The roll-up points at process_identity, and Phase 3 still needs to
        // know what the application was. Identity rows are never deleted.
        using var store = Open();
        SeedOldDay(store);
        var before = store.GetStats().DistinctProcesses;

        store.ApplyRetention(RetentionPolicy.Default, Now, apply: true);

        Assert.AreEqual(before, store.GetStats().DistinctProcesses);
    }

    // ---- The read path the timeline sits on --------------------------------

    [TestMethod]
    public void ReadDay_ReturnsLocalSpansForThatDayOnly()
    {
        using var store = Open();
        store.RecordForeground(new ForegroundChange(1, App("Code"), Utc(1, 9)));
        store.RecordForeground(new ForegroundChange(2, App("chrome"), Utc(1, 10)));
        store.RecordForeground(new ForegroundChange(3, App("Code"), Utc(0, 9)));
        store.RecordForeground(new ForegroundChange(4, App("Code"), Utc(0, 10)));

        var yesterday = DateOnly.FromDateTime(Utc(1, 9).ToLocalTime());
        var observations = store.ReadDay(yesterday, Now);

        Assert.HasCount(2, observations.Foreground);
        Assert.AreEqual("Code", observations.Foreground[0].ProcessName);
        Assert.AreEqual(9, observations.Foreground[0].StartLocal.Hour, "spans come back in local time");
    }

    [TestMethod]
    public void ReadDay_ClosesTheStillOpenSpanWithTheCallersClock()
    {
        using var store = Open();
        store.RecordForeground(new ForegroundChange(1, App("Code"), Utc(0, 9)));

        var today = DateOnly.FromDateTime(Utc(0, 9).ToLocalTime());
        var observations = store.ReadDay(today, Utc(0, 11));

        Assert.HasCount(1, observations.Foreground);
        Assert.AreEqual(
            TimeSpan.FromHours(2),
            observations.Foreground[0].EndLocal - observations.Foreground[0].StartLocal,
            "the store must not reach for a clock of its own");
    }

    [TestMethod]
    public void ReadDay_ReportsIdleAsAbsence()
    {
        using var store = Open();
        store.RecordForeground(new ForegroundChange(1, App("Code"), Utc(0, 9)));
        store.RecordIdle(new IdleChange(true, Utc(0, 10)));
        store.RecordIdle(new IdleChange(false, Utc(0, 11)));

        var today = DateOnly.FromDateTime(Utc(0, 9).ToLocalTime());
        var observations = store.ReadDay(today, Now);

        Assert.HasCount(1, observations.Absence);
        Assert.AreEqual(AbsenceReason.NoInput, observations.Absence[0].Reason);
    }

    [TestMethod]
    public void ReadDayFeedsTheSessionBuilderEndToEnd()
    {
        // The one test that crosses every layer: written through the real
        // store, read back through the real query, interpreted by the real
        // builder. Everything else here is one layer at a time.
        using var store = Open();
        store.RecordForeground(new ForegroundChange(1, App("Code"), Utc(0, 9)));
        store.RecordIdle(new IdleChange(true, Utc(0, 10)));
        store.RecordIdle(new IdleChange(false, Utc(0, 11)));
        store.RecordForeground(new ForegroundChange(2, App("chrome"), Utc(0, 12)));

        var today = DateOnly.FromDateTime(Utc(0, 9).ToLocalTime());
        var observations = store.ReadDay(today, Utc(0, 13));
        var timeline = SessionBuilder.Build(today, observations.Foreground, observations.Absence);

        // The editor never lost the foreground, so the break cuts it in two
        // rather than ending it. That is the honest reading of the day.
        Assert.HasCount(4, timeline.Blocks);
        Assert.AreEqual("code", timeline.Blocks[0].AppKey);
        Assert.AreEqual(AbsenceReason.NoInput, timeline.Blocks[1].Absence);
        Assert.AreEqual("code", timeline.Blocks[2].AppKey);
        Assert.AreEqual("chrome", timeline.Blocks[3].AppKey);
        Assert.AreEqual(TimeSpan.FromHours(3), timeline.InUse);
        Assert.AreEqual(TimeSpan.FromHours(4), timeline.AtComputer);
    }

    /// <summary>
    /// One day well outside any sane retention window: two applications, an
    /// idle stretch, and a process that started and exited.
    /// </summary>
    private void SeedOldDay(SqliteActivityStore store)
    {
        store.RecordForeground(new ForegroundChange(1, App("Code"), Utc(400, 9)));
        store.RecordForeground(new ForegroundChange(2, App("chrome"), Utc(400, 10)));
        store.RecordForeground(new ForegroundChange(3, App("Code"), Utc(400, 11)));

        store.RecordIdle(new IdleChange(true, Utc(400, 12)));
        store.RecordIdle(new IdleChange(false, Utc(400, 12, 30)));

        store.RecordLifetime(new ProcessLifetimeChange(
            50, App("node"), ProcessLifetimeKind.Started, Utc(400, 9, 5), null));
        store.RecordLifetime(new ProcessLifetimeChange(
            50, App("node"), ProcessLifetimeKind.Exited, Utc(400, 9, 30), null));
    }
}
