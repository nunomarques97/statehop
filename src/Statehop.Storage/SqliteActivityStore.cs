using Microsoft.Data.Sqlite;
using Statehop.Core.Abstractions;
using Statehop.Core.Model;

namespace Statehop.Storage;

/// <summary>
/// SQLite-backed activity store.
///
/// Microsoft.Data.Sqlite is used directly, with no ORM: the write volume is
/// high and the schema is trivial, so an ORM would be dead weight.
///
/// One connection is held open for the life of the app and every write is
/// serialised behind a lock, because events arrive on three different threads
/// (foreground on the UI thread, process and idle on timer threads). WAL is
/// enabled so the periodic writes do not block readers.
/// </summary>
public sealed class SqliteActivityStore : IActivityStore, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _gate = new();
    private readonly Dictionary<string, long> _identityCache = new(StringComparer.OrdinalIgnoreCase);

    private long? _openForegroundEventId;
    private long? _openIdleEventId;
    private bool _disposed;

    public string DatabasePath { get; }

    public SqliteActivityStore(string databasePath)
    {
        DatabasePath = databasePath;

        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());

        _connection.Open();
        Execute("PRAGMA journal_mode=WAL;");
        Execute("PRAGMA synchronous=NORMAL;");
        Execute(Schema.CreateSql);

        using var versionCheck = _connection.CreateCommand();
        versionCheck.CommandText = "SELECT COUNT(*) FROM schema_version;";
        if (Convert.ToInt64(versionCheck.ExecuteScalar()) == 0)
        {
            Execute($"INSERT INTO schema_version (version) VALUES ({Schema.Version});");
        }
    }

    // ---- Writes ------------------------------------------------------------

    public void RecordForeground(ForegroundChange change)
    {
        lock (_gate)
        {
            CloseOpenForegroundEvent(change.AtUtc);

            var identityId = ResolveIdentityId(change.Identity, change.AtUtc);

            using var command = _connection.CreateCommand();
            command.CommandText =
                "INSERT INTO foreground_event (identity_id, pid, started_utc) " +
                "VALUES ($identityId, $pid, $started); " +
                "SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$identityId", identityId);
            command.Parameters.AddWithValue("$pid", change.Pid);
            command.Parameters.AddWithValue("$started", Iso(change.AtUtc));
            _openForegroundEventId = Convert.ToInt64(command.ExecuteScalar());
        }
    }

    public void RecordIdle(IdleChange change)
    {
        lock (_gate)
        {
            if (change.IsIdle)
            {
                using var open = _connection.CreateCommand();
                open.CommandText =
                    "INSERT INTO idle_event (started_utc) VALUES ($started); " +
                    "SELECT last_insert_rowid();";
                open.Parameters.AddWithValue("$started", Iso(change.AtUtc));
                _openIdleEventId = Convert.ToInt64(open.ExecuteScalar());
                return;
            }

            if (_openIdleEventId is not { } openId)
            {
                return;
            }

            using var close = _connection.CreateCommand();
            close.CommandText =
                "UPDATE idle_event SET ended_utc = $ended, " +
                "duration_ms = CAST((julianday($ended) - julianday(started_utc)) * 86400000 AS INTEGER) " +
                "WHERE id = $id;";
            close.Parameters.AddWithValue("$ended", Iso(change.AtUtc));
            close.Parameters.AddWithValue("$id", openId);
            close.ExecuteNonQuery();
            _openIdleEventId = null;
        }
    }

    public void RecordLifetime(ProcessLifetimeChange change)
    {
        lock (_gate)
        {
            var identityId = ResolveIdentityId(change.Identity, change.ObservedUtc);

            using var command = _connection.CreateCommand();
            command.CommandText =
                "INSERT INTO process_lifetime_event " +
                "(identity_id, pid, kind, observed_utc, process_start_utc) " +
                "VALUES ($identityId, $pid, $kind, $observed, $start);";
            command.Parameters.AddWithValue("$identityId", identityId);
            command.Parameters.AddWithValue("$pid", change.Pid);
            command.Parameters.AddWithValue("$kind", change.Kind.ToString());
            command.Parameters.AddWithValue("$observed", Iso(change.ObservedUtc));
            command.Parameters.AddWithValue(
                "$start",
                change.ProcessStartUtc is { } start ? Iso(start) : (object)DBNull.Value);
            command.ExecuteNonQuery();
        }
    }

    public void RecordWindowOwnerProbe(IReadOnlyList<WindowOwnerProbe> probes)
    {
        if (probes.Count == 0)
        {
            return;
        }

        var now = Iso(DateTime.UtcNow);

        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;

            // Sticky denial: one denied observation is enough to call an app
            // invisible to this unelevated observer, even if a later pass
            // happened to succeed.
            command.CommandText =
                "INSERT INTO window_owner_access " +
                "(name, denied, is_system_protected, observations, denied_observations, " +
                " first_seen_utc, last_seen_utc) " +
                "VALUES ($name, $denied, $protected, 1, $denied, $now, $now) " +
                "ON CONFLICT(name) DO UPDATE SET " +
                "  observations        = observations + 1, " +
                "  denied_observations = denied_observations + $denied, " +
                "  denied              = MAX(denied, $denied), " +
                "  last_seen_utc       = $now;";

            var name = command.Parameters.Add("$name", SqliteType.Text);
            var denied = command.Parameters.Add("$denied", SqliteType.Integer);
            var isProtected = command.Parameters.Add("$protected", SqliteType.Integer);
            command.Parameters.AddWithValue("$now", now);

            foreach (var probe in probes)
            {
                name.Value = probe.Name;
                denied.Value = probe.AccessState == ProcessAccessState.Denied ? 1 : 0;
                isProtected.Value = probe.IsSystemProtected ? 1 : 0;
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    // ---- Reads -------------------------------------------------------------

    public StoreStats GetStats()
    {
        lock (_gate)
        {
            return new StoreStats(
                Count("foreground_event"),
                Count("idle_event"),
                Count("process_lifetime_event"),
                Count("process_identity"),
                DatabaseSizeBytes());
        }
    }

    public ElevatedAccessReport GetElevatedAccessReport()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                "SELECT name, denied, is_system_protected FROM window_owner_access " +
                "ORDER BY name COLLATE NOCASE;";

            var observed = 0;
            var deniedNames = new List<string>();
            var protectedCount = 0;

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                var denied = reader.GetInt64(1) != 0;
                var isProtected = reader.GetInt64(2) != 0;

                if (isProtected)
                {
                    protectedCount++;
                    continue;
                }

                observed++;
                if (denied)
                {
                    deniedNames.Add(name);
                }
            }

            return new ElevatedAccessReport(observed, deniedNames.Count, deniedNames, protectedCount);
        }
    }

    // ---- Internals ---------------------------------------------------------

    private void CloseOpenForegroundEvent(DateTime atUtc)
    {
        if (_openForegroundEventId is not { } openId)
        {
            return;
        }

        using var command = _connection.CreateCommand();
        command.CommandText =
            "UPDATE foreground_event SET ended_utc = $ended, " +
            "duration_ms = CAST((julianday($ended) - julianday(started_utc)) * 86400000 AS INTEGER) " +
            "WHERE id = $id;";
        command.Parameters.AddWithValue("$ended", Iso(atUtc));
        command.Parameters.AddWithValue("$id", openId);
        command.ExecuteNonQuery();
        _openForegroundEventId = null;
    }

    private long ResolveIdentityId(ProcessIdentity identity, DateTime seenUtc)
    {
        var key = identity.Name + "|" + (identity.ExecutablePath ?? string.Empty);
        if (_identityCache.TryGetValue(key, out var cached))
        {
            Touch(cached, seenUtc);
            return cached;
        }

        using var command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO process_identity " +
            "(name, executable_path, access_state, first_seen_utc, last_seen_utc) " +
            "VALUES ($name, $path, $state, $seen, $seen) " +
            "ON CONFLICT(name, IFNULL(executable_path, '')) DO UPDATE SET " +
            "  last_seen_utc = $seen, access_state = $state; " +
            "SELECT id FROM process_identity " +
            " WHERE name = $name AND IFNULL(executable_path, '') = IFNULL($path, '');";
        command.Parameters.AddWithValue("$name", identity.Name);
        command.Parameters.AddWithValue("$path", (object?)identity.ExecutablePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$state", identity.AccessState.ToString());
        command.Parameters.AddWithValue("$seen", Iso(seenUtc));

        var id = Convert.ToInt64(command.ExecuteScalar());
        _identityCache[key] = id;
        return id;
    }

    private void Touch(long identityId, DateTime seenUtc)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "UPDATE process_identity SET last_seen_utc = $seen WHERE id = $id;";
        command.Parameters.AddWithValue("$seen", Iso(seenUtc));
        command.Parameters.AddWithValue("$id", identityId);
        command.ExecuteNonQuery();
    }

    private long Count(string table)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM " + table + ";";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private long DatabaseSizeBytes()
    {
        long total = 0;
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var file = new FileInfo(DatabasePath + suffix);
            if (file.Exists)
            {
                total += file.Length;
            }
        }

        return total;
    }

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Iso(DateTime utc) =>
        utc.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_gate)
        {
            // Leaving the last foreground span open would make a day of data
            // end in a row with no duration.
            try
            {
                CloseOpenForegroundEvent(DateTime.UtcNow);
                Execute("PRAGMA wal_checkpoint(TRUNCATE);");
            }
            catch (SqliteException)
            {
                // Shutting down; a failed checkpoint is not worth crashing for.
            }

            _connection.Dispose();
        }
    }
}
