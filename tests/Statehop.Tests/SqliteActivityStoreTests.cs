using Statehop.Core.Model;
using Statehop.Storage;

namespace Statehop.Tests;

/// <summary>
/// Covers the store against a real SQLite file, because the things worth
/// checking here — the unique index on identity, the sticky denial in the
/// B0.2 upsert, closing an open foreground span — all live in SQL.
/// </summary>
[TestClass]
public sealed class SqliteActivityStoreTests
{
    private string _databasePath = string.Empty;

    [TestInitialize]
    public void CreateTempDatabase() =>
        _databasePath = Path.Combine(Path.GetTempPath(), $"statehop-test-{Guid.NewGuid():N}.db");

    [TestCleanup]
    public void DeleteTempDatabase()
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var file = _databasePath + suffix;
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    private SqliteActivityStore Open() => new(_databasePath);

    [TestMethod]
    public void RepeatedForegroundChanges_ReuseOneIdentityRow()
    {
        using var store = Open();
        var identity = new ProcessIdentity("devenv", @"C:\vs\devenv.exe", ProcessAccessState.Accessible);

        store.RecordForeground(new ForegroundChange(100, identity, DateTime.UtcNow));
        store.RecordForeground(new ForegroundChange(200, identity, DateTime.UtcNow.AddSeconds(5)));

        var stats = store.GetStats();
        Assert.AreEqual(2, stats.ForegroundEvents);
        Assert.AreEqual(1, stats.DistinctProcesses, "the same app must not create a second identity row");
    }

    [TestMethod]
    public void SameNameDifferentPath_AreDifferentIdentities()
    {
        using var store = Open();

        // Both paths are inside install directories, so both survive whole and
        // the two installs stay distinguishable. Outside those directories the
        // path policy collapses them on purpose — see StoreCorrectionsTests.
        store.RecordForeground(new ForegroundChange(
            1,
            new ProcessIdentity("chrome", @"C:\Program Files\A\chrome.exe", ProcessAccessState.Accessible),
            DateTime.UtcNow));
        store.RecordForeground(new ForegroundChange(
            2,
            new ProcessIdentity("chrome", @"C:\Program Files\B\chrome.exe", ProcessAccessState.Accessible),
            DateTime.UtcNow));

        Assert.AreEqual(2, store.GetStats().DistinctProcesses);
    }

    [TestMethod]
    public void IdentityWithoutPath_IsStoredOnce()
    {
        using var store = Open();
        var denied = ProcessIdentity.Denied("elevated-thing");

        store.RecordForeground(new ForegroundChange(1, denied, DateTime.UtcNow));
        store.RecordForeground(new ForegroundChange(2, denied, DateTime.UtcNow.AddSeconds(1)));

        Assert.AreEqual(1, store.GetStats().DistinctProcesses);
    }

    [TestMethod]
    public void IdleSpan_IsOpenedAndClosed()
    {
        using var store = Open();
        var start = DateTime.UtcNow;

        store.RecordIdle(new IdleChange(true, start));
        store.RecordIdle(new IdleChange(false, start.AddMinutes(3)));

        Assert.AreEqual(1, store.GetStats().IdleEvents, "resuming input must close the span, not open a new row");
    }

    [TestMethod]
    public void ResumingWithoutGoingIdle_RecordsNothing()
    {
        using var store = Open();

        store.RecordIdle(new IdleChange(false, DateTime.UtcNow));

        Assert.AreEqual(0, store.GetStats().IdleEvents);
    }

    [TestMethod]
    public void ElevatedReport_ExcludesSystemProcessesAndKeepsDenialSticky()
    {
        using var store = Open();

        store.RecordWindowOwnerProbe(
        [
            new WindowOwnerProbe("notepad", ProcessAccessState.Accessible, false),
            new WindowOwnerProbe("taskmgr", ProcessAccessState.Denied, false),
            new WindowOwnerProbe("csrss", ProcessAccessState.Denied, true),
        ]);

        // A later pass that happens to succeed must not erase the denial.
        store.RecordWindowOwnerProbe(
        [
            new WindowOwnerProbe("taskmgr", ProcessAccessState.Accessible, false),
        ]);

        var report = store.GetElevatedAccessReport();

        Assert.AreEqual(2, report.WindowOwnersObserved);
        Assert.AreEqual(1, report.Denied);
        CollectionAssert.AreEqual(new[] { "taskmgr" }, report.DeniedNames.ToArray());
        Assert.AreEqual(1, report.SystemProtectedExcluded);
        Assert.AreEqual(50.0, report.DeniedPercent, 0.001);
    }

    [TestMethod]
    public void EmptyReport_DoesNotDivideByZero()
    {
        using var store = Open();

        var report = store.GetElevatedAccessReport();

        Assert.AreEqual(0, report.WindowOwnersObserved);
        Assert.AreEqual(0, report.DeniedPercent);
    }

    [TestMethod]
    public void DataSurvivesReopening()
    {
        using (var store = Open())
        {
            store.RecordLifetime(new ProcessLifetimeChange(
                1,
                new ProcessIdentity("docker", @"C:\docker.exe", ProcessAccessState.Accessible),
                ProcessLifetimeKind.Started,
                DateTime.UtcNow,
                DateTime.UtcNow.AddSeconds(-2)));
        }

        using var reopened = Open();
        var stats = reopened.GetStats();

        Assert.AreEqual(1, stats.LifetimeEvents);
        Assert.IsGreaterThan(0, stats.DatabaseBytes);
    }
}

/// <summary>
/// The list that decides which processes are excluded from the B0.2 headline
/// number, so it is worth a test of its own.
/// </summary>
[TestClass]
public sealed class SystemProcessesTests
{
    [TestMethod]
    public void KnownProtectedProcesses_AreRecognised()
    {
        Assert.IsTrue(SystemProcesses.IsProtected("csrss"));
        Assert.IsTrue(SystemProcesses.IsProtected("Registry"));
        Assert.IsTrue(SystemProcesses.IsProtected("WININIT"), "matching must be case-insensitive");
    }

    [TestMethod]
    public void OrdinaryApps_AreNotProtected()
    {
        Assert.IsFalse(SystemProcesses.IsProtected("devenv"));
        Assert.IsFalse(SystemProcesses.IsProtected("chrome"));
    }
}
