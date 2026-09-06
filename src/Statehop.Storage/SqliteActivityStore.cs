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
        Migrate();
    }

    // ---- Migration ---------------------------------------------------------

    /// <summary>
    /// Brings an existing database up to <see cref="Schema.Version"/>.
    /// CreateSql is written with IF NOT EXISTS, so it builds a new database at
    /// the current version but does nothing to an old one; that is what this
    /// handles.
    /// </summary>
    private void Migrate()
    {
        using var versionCheck = _connection.CreateCommand();
        versionCheck.CommandText = "SELECT version FROM schema_version LIMIT 1;";
        var existing = versionCheck.ExecuteScalar();

        if (existing is null)
        {
            // Fresh database: CreateSql already produced the current shape.
            Execute($"INSERT INTO schema_version (version) VALUES ({Schema.Version});");
            return;
        }

        var version = Convert.ToInt32(existing);
        if (version >= Schema.Version)
        {
            return;
        }

        if (version < 2)
        {
            MigrateToV2();
        }

        Execute($"UPDATE schema_version SET version = {Schema.Version};");
    }

    /// <summary>
    /// Version 2:
    ///
    /// 1. adds the ephemeral-classification columns, and
    /// 2. applies the executable-path policy to rows already written.
    ///
    /// Step 2 can collapse several identities into one — three
    /// "build-script-build.exe" rows under different project folders become a
    /// single "build-script-build.exe" — so the events that pointed at the
    /// duplicates have to be repointed at the survivor before it is deleted.
    /// Losing those events would be worse than the leak being fixed.
    /// </summary>
    private void MigrateToV2()
    {
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var info = _connection.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info(process_identity);";
            using var reader = info.ExecuteReader();
            while (reader.Read())
            {
                existingColumns.Add(reader.GetString(1));
            }
        }

        foreach (var (column, definition) in Schema.V2IdentityColumns)
        {
            if (!existingColumns.Contains(column))
            {
                Execute($"ALTER TABLE process_identity ADD COLUMN {column} {definition};");
            }
        }

        var rows = new List<(long Id, string Name, string? Path, string State, string First, string Last)>();
        using (var read = _connection.CreateCommand())
        {
            read.CommandText =
                "SELECT id, name, executable_path, access_state, first_seen_utc, last_seen_utc " +
                "FROM process_identity ORDER BY id;";
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5)));
            }
        }

        // The key must match the unique index, (name, IFNULL(executable_path,
        // '')), which uses SQLite's default BINARY collation.
        var survivors = new Dictionary<string, long>(StringComparer.Ordinal);
        var merges = new List<(long From, long Into)>();
        var rewrites = new Dictionary<long, (string? Path, string First, string Last, string State)>();

        foreach (var row in rows)
        {
            var newPath = ExecutablePathPolicy.Apply(row.Path);
            var key = row.Name + "\0" + (newPath ?? string.Empty);

            if (survivors.TryGetValue(key, out var survivorId))
            {
                merges.Add((row.Id, survivorId));
                var current = rewrites[survivorId];
                rewrites[survivorId] = (
                    current.Path,
                    string.CompareOrdinal(row.First, current.First) < 0 ? row.First : current.First,
                    string.CompareOrdinal(row.Last, current.Last) > 0 ? row.Last : current.Last,
                    // Having been readable once is the honest answer for the
                    // merged row; a later Exited must not overwrite it.
                    current.State == nameof(ProcessAccessState.Accessible) ? current.State : row.State);
                continue;
            }

            survivors[key] = row.Id;
            rewrites[row.Id] = (newPath, row.First, row.Last, row.State);
        }

        using var transaction = _connection.BeginTransaction();

        foreach (var (from, into) in merges)
        {
            Repoint(transaction, "foreground_event", from, into);
            Repoint(transaction, "process_lifetime_event", from, into);
        }

        // Duplicates go before the survivors are rewritten, or the rewrite
        // would collide with the unique index it is converging on.
        foreach (var (from, _) in merges)
        {
            using var delete = _connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM process_identity WHERE id = $id;";
            delete.Parameters.AddWithValue("$id", from);
            delete.ExecuteNonQuery();
        }

        foreach (var (id, value) in rewrites)
        {
            using var update = _connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText =
                "UPDATE process_identity SET executable_path = $path, first_seen_utc = $first, " +
                "last_seen_utc = $last, access_state = $state WHERE id = $id;";
            update.Parameters.AddWithValue("$path", (object?)value.Path ?? DBNull.Value);
            update.Parameters.AddWithValue("$first", value.First);
            update.Parameters.AddWithValue("$last", value.Last);
            update.Parameters.AddWithValue("$state", value.State);
            update.Parameters.AddWithValue("$id", id);
            update.ExecuteNonQuery();
        }

        transaction.Commit();

        MigratedIdentitiesMerged = merges.Count;
        MigratedIdentitiesKept = rewrites.Count;
    }

    /// <summary>Identity rows merged away by the v2 path policy, for the session log.</summary>
    public int MigratedIdentitiesMerged { get; private set; }

    /// <summary>Identity rows that survived the v2 migration.</summary>
    public int MigratedIdentitiesKept { get; private set; }

    private void Repoint(SqliteTransaction transaction, string table, long from, long into)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"UPDATE {table} SET identity_id = $into WHERE identity_id = $from;";
        command.Parameters.AddWithValue("$from", from);
        command.Parameters.AddWithValue("$into", into);
        command.ExecuteNonQuery();
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
                DatabaseSizeBytes(),
                Count("process_identity", "is_ephemeral = 1"),
                Scalar(
                    "SELECT COUNT(*) FROM process_lifetime_event e " +
                    "JOIN process_identity i ON i.id = e.identity_id " +
                    "WHERE i.is_ephemeral = 1;"));
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

    /// <summary>
    /// Recomputes which identities are ephemeral. This is a classification, not
    /// a filter: no row is ever deleted or refused.
    /// Discarding on write is irreversible, and a wrong rule would lose
    /// signal for good.
    ///
    /// An identity is ephemeral when all three hold:
    ///
    ///   * it never owned a window — no foreground event, and its name never
    ///     appeared in the B0.2 window-owner probe;
    ///   * there are at least <see cref="MinimumLifetimeSamples"/> observed
    ///     start/exit pairs, so the verdict rests on evidence rather than on
    ///     one sighting;
    ///   * the median observed lifetime is under
    ///     <see cref="EphemeralLifetimeThreshold"/>.
    ///
    /// The evidence (owned_window, lifetime_samples, median_lifetime_ms) is
    /// stored next to the verdict on purpose. The Phase 0 sample is biased —
    /// bash at 1 239 starts and sleep at 668 come from developer tooling a
    /// normal user never runs — so a later reader must be able to re-judge
    /// without this threshold being the only thing that survived.
    /// </summary>
    public void ReclassifyEphemeral()
    {
        lock (_gate)
        {
            Execute("""
                UPDATE process_identity SET owned_window =
                    CASE WHEN EXISTS (
                             SELECT 1 FROM foreground_event f
                              WHERE f.identity_id = process_identity.id)
                          OR EXISTS (
                             SELECT 1 FROM window_owner_access w
                              WHERE w.name = process_identity.name COLLATE NOCASE)
                         THEN 1 ELSE 0 END;
                """);

            // Lifetime evidence is keyed by process NAME, not by identity id,
            // and that is not a shortcut. A process exits under a different
            // identity row than it started under: ProcessWatcher cannot read a
            // path out of a process that is already gone, so the exit lands on
            // the null-path row of the same name. Keying by id would leave
            // every exit row — half the volume — permanently unclassified.
            var samples = new Dictionary<string, List<long>>(StringComparer.OrdinalIgnoreCase);
            using (var read = _connection.CreateCommand())
            {
                read.CommandText = """
                    SELECT si.name,
                           CAST((julianday((
                               SELECT MIN(x.observed_utc)
                                 FROM process_lifetime_event x
                                 JOIN process_identity xi ON xi.id = x.identity_id
                                WHERE x.pid = s.pid
                                  AND x.kind = 'Exited'
                                  AND x.observed_utc > s.observed_utc
                                  AND xi.name = si.name
                           )) - julianday(s.observed_utc)) * 86400000 AS INTEGER) AS lifetime_ms
                      FROM process_lifetime_event s
                      JOIN process_identity si ON si.id = s.identity_id
                     WHERE s.kind = 'Started';
                    """;
                using var reader = read.ExecuteReader();
                while (reader.Read())
                {
                    if (reader.IsDBNull(1))
                    {
                        // Still running, or its exit was never observed.
                        continue;
                    }

                    var name = reader.GetString(0);
                    if (!samples.TryGetValue(name, out var list))
                    {
                        list = [];
                        samples[name] = list;
                    }

                    list.Add(reader.GetInt64(1));
                }
            }

            using var transaction = _connection.BeginTransaction();

            using (var reset = _connection.CreateCommand())
            {
                reset.Transaction = transaction;
                reset.CommandText =
                    "UPDATE process_identity SET lifetime_samples = 0, " +
                    "median_lifetime_ms = NULL, is_ephemeral = 0;";
                reset.ExecuteNonQuery();
            }

            using var update = _connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText =
                "UPDATE process_identity SET lifetime_samples = $samples, " +
                "median_lifetime_ms = $median, " +
                // owned_window is still read per row, so an app that owned a
                // window is spared even when a namesake did not.
                "is_ephemeral = CASE WHEN owned_window = 0 AND $samples >= $minSamples " +
                "                     AND $median < $threshold THEN 1 ELSE 0 END " +
                "WHERE name = $name COLLATE NOCASE;";
            var sampleCount = update.Parameters.Add("$samples", SqliteType.Integer);
            var median = update.Parameters.Add("$median", SqliteType.Integer);
            var nameParameter = update.Parameters.Add("$name", SqliteType.Text);
            update.Parameters.AddWithValue("$minSamples", MinimumLifetimeSamples);
            update.Parameters.AddWithValue("$threshold", (long)EphemeralLifetimeThreshold.TotalMilliseconds);

            foreach (var (processName, values) in samples)
            {
                values.Sort();
                sampleCount.Value = values.Count;
                median.Value = values[values.Count / 2];
                nameParameter.Value = processName;
                update.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    /// <summary>
    /// Below this median observed lifetime a process counts as short-lived.
    /// Two minutes is four process-poll intervals: coarse on purpose, because
    /// the poll is 30 s and a tighter number would be measuring the poll rather
    /// than the process.
    /// </summary>
    public static readonly TimeSpan EphemeralLifetimeThreshold = TimeSpan.FromMinutes(2);

    /// <summary>How many start/exit pairs are needed before the verdict is trusted.</summary>
    public const int MinimumLifetimeSamples = 3;

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
        // Applied again here, not only at capture. This is the last line
        // before disk, so it is the one place where the guarantee "no
        // directory outside an install root is ever persisted" can actually be
        // made — an identity built anywhere else cannot slip past it.
        var executablePath = ExecutablePathPolicy.Apply(identity.ExecutablePath);

        var key = identity.Name + "|" + (executablePath ?? string.Empty);
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
        command.Parameters.AddWithValue("$path", (object?)executablePath ?? DBNull.Value);
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

    private long Count(string table, string? where = null)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM " + table
            + (where is null ? ";" : " WHERE " + where + ";");
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private long Scalar(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
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
