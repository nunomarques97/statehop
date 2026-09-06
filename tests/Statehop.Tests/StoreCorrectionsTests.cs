using Microsoft.Data.Sqlite;
using Statehop.Core.Model;
using Statehop.Storage;

namespace Statehop.Tests;

/// <summary>
/// The privacy corrections, against a real SQLite file: the executable-path
/// policy at the write boundary, the version 1 → 2 migration of a database
/// that already exists, and the ephemeral classification.
/// </summary>
[TestClass]
public sealed class StoreCorrectionsTests
{
    private string _databasePath = string.Empty;

    [TestInitialize]
    public void CreateTempDatabase() =>
        _databasePath = Path.Combine(Path.GetTempPath(), $"statehop-test-{Guid.NewGuid():N}.db");

    [TestCleanup]
    public void DeleteTempDatabase()
    {
        SqliteConnection.ClearAllPools();
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

    // ---- Item 1: the executable-path policy --------------------------------

    [TestMethod]
    public void PathOutsideInstallDirectories_IsNeverPersistedWhole()
    {
        const string directory = @"C:\Users\alex\Documents\Clients\Acme Confidential";
        const string fullPath = directory + @"\reconciliation-tool.exe";

        using (var store = Open())
        {
            // Every write path, not just one, because the guarantee has to hold
            // for all of them.
            var identity = new ProcessIdentity(
                "reconciliation-tool", fullPath, ProcessAccessState.Accessible);

            store.RecordForeground(new ForegroundChange(1, identity, DateTime.UtcNow));
            store.RecordLifetime(new ProcessLifetimeChange(
                1, identity, ProcessLifetimeKind.Started, DateTime.UtcNow, null));
        }

        Assert.AreEqual(
            "reconciliation-tool.exe",
            SingleValue("SELECT executable_path FROM process_identity;"),
            "only the file name may survive");

        foreach (var value in AllTextValues())
        {
            Assert.IsFalse(
                value.Contains(directory, StringComparison.OrdinalIgnoreCase),
                $"the full directory reached disk in: {value}");
            Assert.IsFalse(
                value.Contains("Clients", StringComparison.OrdinalIgnoreCase),
                $"a directory name outside the install roots reached disk in: {value}");
        }
    }

    [TestMethod]
    public void PathInsideInstallDirectories_IsKeptWhole()
    {
        var fullPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Google", "Chrome", "Application", "chrome.exe");

        using (var store = Open())
        {
            store.RecordForeground(new ForegroundChange(
                1,
                new ProcessIdentity("chrome", fullPath, ProcessAccessState.Accessible),
                DateTime.UtcNow));
        }

        Assert.AreEqual(fullPath, SingleValue("SELECT executable_path FROM process_identity;"));
    }

    [TestMethod]
    public void SamePathOutsideInstallDirectories_CollapsesToOneIdentity()
    {
        using var store = Open();

        // Three build-script-build.exe under three project folders — the exact
        // shape that filled the Phase 0 database.
        foreach (var folder in new[] { "alpha", "beta", "gamma" })
        {
            store.RecordLifetime(new ProcessLifetimeChange(
                1,
                new ProcessIdentity(
                    "build-script-build",
                    $@"C:\Users\alex\{folder}\target\build-script-build.exe",
                    ProcessAccessState.Accessible),
                ProcessLifetimeKind.Started,
                DateTime.UtcNow,
                null));
        }

        Assert.AreEqual(1, store.GetStats().DistinctProcesses);
        Assert.AreEqual(3, store.GetStats().LifetimeEvents, "no event may be lost to the collapse");
    }

    // ---- Item 1: migrating a database that already exists -------------------

    [TestMethod]
    public void OpeningAVersion1Database_MigratesItInPlace()
    {
        CreateVersion1Database();

        using (var store = Open())
        {
            Assert.AreEqual(2, store.MigratedIdentitiesMerged, "the two duplicate rows must merge into one");
            Assert.AreEqual(2, store.MigratedIdentitiesKept);
        }

        Assert.AreEqual("2", SingleValue("SELECT version FROM schema_version;"));

        // The three project folders collapsed to one identity...
        Assert.AreEqual(
            "build-script-build.exe",
            SingleValue("SELECT executable_path FROM process_identity WHERE name = 'build-script-build';"));

        // ...and the events that pointed at the rows now gone were repointed,
        // not orphaned or deleted.
        Assert.AreEqual(
            "3",
            SingleValue("SELECT COUNT(*) FROM process_lifetime_event;"));
        Assert.AreEqual(
            "0",
            SingleValue(
                "SELECT COUNT(*) FROM process_lifetime_event e " +
                "WHERE NOT EXISTS (SELECT 1 FROM process_identity i WHERE i.id = e.identity_id);"),
            "no event may be left pointing at a deleted identity");

        // The system path was left alone.
        Assert.AreEqual(
            @"C:\Windows\System32\notepad.exe",
            SingleValue("SELECT executable_path FROM process_identity WHERE name = 'notepad';"));

        // First/last seen span the whole merged group.
        Assert.AreEqual(
            "2026-09-06 08:00:00.000",
            SingleValue("SELECT first_seen_utc FROM process_identity WHERE name = 'build-script-build';"));
        Assert.AreEqual(
            "2026-09-06 10:00:00.000",
            SingleValue("SELECT last_seen_utc FROM process_identity WHERE name = 'build-script-build';"));

        foreach (var value in AllTextValues())
        {
            Assert.IsFalse(
                value.Contains("sample-app", StringComparison.OrdinalIgnoreCase),
                $"the migration left a project folder behind in: {value}");
        }
    }

    [TestMethod]
    public void MigrationIsIdempotent()
    {
        CreateVersion1Database();

        using (var first = Open())
        {
            Assert.AreEqual(2, first.MigratedIdentitiesMerged);
        }

        using var second = Open();
        Assert.AreEqual(0, second.MigratedIdentitiesMerged, "a migrated database must not be migrated again");
        Assert.AreEqual("2", SingleValue("SELECT version FROM schema_version;"));
    }

    /// <summary>
    /// The version 1 schema, written out literally rather than imported. A test
    /// of a migration has to pin the shape being migrated from, or it stops
    /// testing anything the day the current schema changes.
    /// </summary>
    private void CreateVersion1Database()
    {
        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();

        using (var create = connection.CreateCommand())
        {
            create.CommandText = """
                CREATE TABLE schema_version (version INTEGER NOT NULL);
                CREATE TABLE process_identity (
                    id              INTEGER PRIMARY KEY,
                    name            TEXT NOT NULL,
                    executable_path TEXT,
                    access_state    TEXT NOT NULL,
                    first_seen_utc  TEXT NOT NULL,
                    last_seen_utc   TEXT NOT NULL
                );
                CREATE UNIQUE INDEX ix_process_identity_key
                    ON process_identity (name, IFNULL(executable_path, ''));
                CREATE TABLE foreground_event (
                    id           INTEGER PRIMARY KEY,
                    identity_id  INTEGER NOT NULL REFERENCES process_identity(id),
                    pid          INTEGER NOT NULL,
                    started_utc  TEXT NOT NULL,
                    ended_utc    TEXT,
                    duration_ms  INTEGER
                );
                CREATE TABLE idle_event (
                    id INTEGER PRIMARY KEY, started_utc TEXT NOT NULL,
                    ended_utc TEXT, duration_ms INTEGER
                );
                CREATE TABLE process_lifetime_event (
                    id                INTEGER PRIMARY KEY,
                    identity_id       INTEGER NOT NULL REFERENCES process_identity(id),
                    pid               INTEGER NOT NULL,
                    kind              TEXT NOT NULL,
                    observed_utc      TEXT NOT NULL,
                    process_start_utc TEXT
                );
                CREATE TABLE window_owner_access (
                    name TEXT PRIMARY KEY, denied INTEGER NOT NULL,
                    is_system_protected INTEGER NOT NULL, observations INTEGER NOT NULL,
                    denied_observations INTEGER NOT NULL,
                    first_seen_utc TEXT NOT NULL, last_seen_utc TEXT NOT NULL
                );

                INSERT INTO schema_version (version) VALUES (1);

                INSERT INTO process_identity
                    (id, name, executable_path, access_state, first_seen_utc, last_seen_utc)
                VALUES
                    (1, 'build-script-build',
                        'C:\Users\alex\source\repos\sample-app\src-tauri\target\release\build\a\build-script-build.exe',
                        'Accessible', '2026-09-06 09:00:00.000', '2026-09-06 09:10:00.000'),
                    (2, 'build-script-build',
                        'C:\Users\alex\source\repos\sample-app\src-tauri\target\debug\build\b\build-script-build.exe',
                        'Exited', '2026-09-06 08:00:00.000', '2026-09-06 08:30:00.000'),
                    (3, 'build-script-build',
                        'C:\Users\alex\source\repos\sample-app\src-tauri\target\release\build\c\build-script-build.exe',
                        'Accessible', '2026-09-06 09:30:00.000', '2026-09-06 10:00:00.000'),
                    (4, 'notepad', 'C:\Windows\System32\notepad.exe',
                        'Accessible', '2026-09-06 09:00:00.000', '2026-09-06 09:05:00.000');

                INSERT INTO process_lifetime_event (identity_id, pid, kind, observed_utc)
                VALUES (1, 10, 'Started', '2026-09-06 09:00:00.000'),
                       (2, 11, 'Started', '2026-09-06 08:00:00.000'),
                       (3, 12, 'Started', '2026-09-06 09:30:00.000');
                """;
            create.ExecuteNonQuery();
        }
    }

    // ---- Item 2: the ephemeral classification -------------------------------

    [TestMethod]
    public void ShortLivedProcessWithoutAWindow_IsClassifiedEphemeral()
    {
        using var store = Open();
        var sleep = new ProcessIdentity("sleep", @"C:\Program Files\Git\usr\bin\sleep.exe", ProcessAccessState.Accessible);
        var start = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);

        // Four runs, each seen alive for one 30 s poll and gone by the next.
        for (var i = 0; i < 4; i++)
        {
            var at = start.AddMinutes(i);
            store.RecordLifetime(new ProcessLifetimeChange(
                1000 + i, sleep, ProcessLifetimeKind.Started, at, null));
            store.RecordLifetime(new ProcessLifetimeChange(
                1000 + i, new ProcessIdentity("sleep", null, ProcessAccessState.Exited),
                ProcessLifetimeKind.Exited, at.AddSeconds(30), null));
        }

        store.ReclassifyEphemeral();

        Assert.AreEqual("1", SingleValue(
            "SELECT is_ephemeral FROM process_identity WHERE name = 'sleep' AND executable_path IS NOT NULL;"));
        Assert.AreEqual("4", SingleValue(
            "SELECT lifetime_samples FROM process_identity WHERE name = 'sleep' AND executable_path IS NOT NULL;"));
        // Durations come from julianday() arithmetic, which is a float and
        // lands a millisecond either side. The threshold is two minutes, so
        // the tolerance costs nothing; asserting an exact 30000 would only be
        // pinning a rounding artefact.
        var median = long.Parse(SingleValue(
            "SELECT median_lifetime_ms FROM process_identity WHERE name = 'sleep' AND executable_path IS NOT NULL;"));
        Assert.IsLessThanOrEqualTo(
            2,
            Math.Abs(median - 30_000),
            $"expected a median near 30 000 ms, got {median}");

        // Nothing was thrown away — that is the whole point of classifying
        // rather than filtering on write.
        var stats = store.GetStats();
        Assert.AreEqual(8, stats.LifetimeEvents);
        Assert.AreEqual(8, stats.EphemeralLifetimeEvents);
    }

    [TestMethod]
    public void ShortLivedProcessThatOwnedAWindow_IsNotEphemeral()
    {
        using var store = Open();
        var identity = new ProcessIdentity("SnippingTool", @"C:\Windows\System32\SnippingTool.exe", ProcessAccessState.Accessible);
        var start = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 4; i++)
        {
            var at = start.AddMinutes(i);
            store.RecordLifetime(new ProcessLifetimeChange(
                2000 + i, identity, ProcessLifetimeKind.Started, at, null));
            store.RecordLifetime(new ProcessLifetimeChange(
                2000 + i, new ProcessIdentity("SnippingTool", null, ProcessAccessState.Exited),
                ProcessLifetimeKind.Exited, at.AddSeconds(30), null));
        }

        // The B0.2 probe saw it own a visible window.
        store.RecordWindowOwnerProbe([new WindowOwnerProbe("SnippingTool", ProcessAccessState.Accessible, false)]);
        store.ReclassifyEphemeral();

        Assert.AreEqual("0", SingleValue(
            "SELECT is_ephemeral FROM process_identity " +
            "WHERE name = 'SnippingTool' AND executable_path IS NOT NULL;"),
            "a process that owned a window is never ephemeral, however short-lived");
        Assert.AreEqual(0, store.GetStats().EphemeralProcesses);
    }

    [TestMethod]
    public void LongLivedProcess_IsNotEphemeral()
    {
        using var store = Open();
        var identity = new ProcessIdentity("someservice", @"C:\Windows\System32\someservice.exe", ProcessAccessState.Accessible);
        var start = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 4; i++)
        {
            var at = start.AddHours(i);
            store.RecordLifetime(new ProcessLifetimeChange(
                3000 + i, identity, ProcessLifetimeKind.Started, at, null));
            store.RecordLifetime(new ProcessLifetimeChange(
                3000 + i, new ProcessIdentity("someservice", null, ProcessAccessState.Exited),
                ProcessLifetimeKind.Exited, at.AddMinutes(45), null));
        }

        store.ReclassifyEphemeral();

        Assert.AreEqual("0", SingleValue(
            "SELECT is_ephemeral FROM process_identity " +
            "WHERE name = 'someservice' AND executable_path IS NOT NULL;"));
    }

    [TestMethod]
    public void TooFewSamples_LeaveTheVerdictUnmade()
    {
        using var store = Open();
        var identity = new ProcessIdentity("rare", @"C:\Windows\rare.exe", ProcessAccessState.Accessible);
        var at = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);

        store.RecordLifetime(new ProcessLifetimeChange(4000, identity, ProcessLifetimeKind.Started, at, null));
        store.RecordLifetime(new ProcessLifetimeChange(
            4000, new ProcessIdentity("rare", null, ProcessAccessState.Exited),
            ProcessLifetimeKind.Exited, at.AddSeconds(30), null));

        store.ReclassifyEphemeral();

        Assert.AreEqual("0", SingleValue(
            "SELECT is_ephemeral FROM process_identity WHERE name = 'rare' AND executable_path IS NOT NULL;"),
            "one sighting is not evidence");
        Assert.AreEqual("1", SingleValue(
            "SELECT lifetime_samples FROM process_identity WHERE name = 'rare' AND executable_path IS NOT NULL;"));
    }

    [TestMethod]
    public void ReclassifyingTwice_GivesTheSameAnswer()
    {
        using var store = Open();
        var sleep = new ProcessIdentity("sleep", @"C:\Program Files\Git\usr\bin\sleep.exe", ProcessAccessState.Accessible);
        var start = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 4; i++)
        {
            var at = start.AddMinutes(i);
            store.RecordLifetime(new ProcessLifetimeChange(5000 + i, sleep, ProcessLifetimeKind.Started, at, null));
            store.RecordLifetime(new ProcessLifetimeChange(
                5000 + i, new ProcessIdentity("sleep", null, ProcessAccessState.Exited),
                ProcessLifetimeKind.Exited, at.AddSeconds(30), null));
        }

        store.ReclassifyEphemeral();
        var first = store.GetStats();
        store.ReclassifyEphemeral();

        Assert.AreEqual(first.EphemeralProcesses, store.GetStats().EphemeralProcesses);
        Assert.AreEqual("4", SingleValue(
            "SELECT lifetime_samples FROM process_identity WHERE name = 'sleep' AND executable_path IS NOT NULL;"),
            "samples must be recomputed, not accumulated");
    }

    // ---- Helpers ------------------------------------------------------------

    private string SingleValue(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Every text value in every column of every table. The privacy assertion
    /// has to cover the whole file, not the one column it expects to be wrong.
    /// </summary>
    private List<string> AllTextValues()
    {
        var values = new List<string>();

        using var connection = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly");
        connection.Open();

        var tables = new List<string>();
        using (var list = connection.CreateCommand())
        {
            list.CommandText =
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
            using var reader = list.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }

        foreach (var table in tables)
        {
            using var select = connection.CreateCommand();
            select.CommandText = $"SELECT * FROM \"{table}\";";
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    if (!reader.IsDBNull(i) && reader.GetFieldType(i) == typeof(string))
                    {
                        values.Add(reader.GetString(i));
                    }
                }
            }
        }

        return values;
    }
}
