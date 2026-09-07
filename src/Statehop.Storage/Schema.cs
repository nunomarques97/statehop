namespace Statehop.Storage;

/// <summary>
/// The Phase 0 schema, plus the privacy corrections. Deliberately minimal —
/// it does not have to be the final shape, and Phase 1 will
/// revisit it along with the retention policy.
///
/// PRIVACY: there is no column anywhere for a window
/// title, and there must not be one. Titles can carry client names, file
/// names, URLs and email subjects. What is stored is process identity and
/// timestamps. Since version 2, `executable_path` is also narrowed by
/// <see cref="Statehop.Core.Model.ExecutablePathPolicy"/>: the directory only
/// survives for executables under a system install directory.
///
/// Version history:
///   1 — Phase 0 spike.
///   2 — Executable-path policy applied to existing rows, plus the
///       ephemeral-process classification columns.
///   3 — Daily roll-up tables for the retention policy.
///       Additive only: no existing column changes.
/// </summary>
internal static class Schema
{
    internal const int Version = 3;

    internal const string CreateSql = """
        CREATE TABLE IF NOT EXISTS schema_version (
            version INTEGER NOT NULL
        );

        -- One row per distinct application, not per run.
        --
        -- The four trailing columns are a *classification*, derived from the
        -- event tables by ReclassifyEphemeral(). Nothing is discarded on write
        -- (by design): a wrong rule would lose signal for good, so
        -- readers filter instead.
        CREATE TABLE IF NOT EXISTS process_identity (
            id                 INTEGER PRIMARY KEY,
            name               TEXT NOT NULL,
            executable_path    TEXT,
            access_state       TEXT NOT NULL,
            first_seen_utc     TEXT NOT NULL,
            last_seen_utc      TEXT NOT NULL,
            owned_window       INTEGER NOT NULL DEFAULT 0,
            lifetime_samples   INTEGER NOT NULL DEFAULT 0,
            median_lifetime_ms INTEGER,
            is_ephemeral       INTEGER NOT NULL DEFAULT 0
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ix_process_identity_key
            ON process_identity (name, IFNULL(executable_path, ''));

        -- Which application was in front, and for how long.
        CREATE TABLE IF NOT EXISTS foreground_event (
            id           INTEGER PRIMARY KEY,
            identity_id  INTEGER NOT NULL REFERENCES process_identity(id),
            pid          INTEGER NOT NULL,
            started_utc  TEXT NOT NULL,
            ended_utc    TEXT,
            duration_ms  INTEGER
        );

        CREATE INDEX IF NOT EXISTS ix_foreground_started
            ON foreground_event (started_utc);

        CREATE INDEX IF NOT EXISTS ix_foreground_identity
            ON foreground_event (identity_id);

        -- Absence of user input. NOT "the app was doing nothing".
        CREATE TABLE IF NOT EXISTS idle_event (
            id           INTEGER PRIMARY KEY,
            started_utc  TEXT NOT NULL,
            ended_utc    TEXT,
            duration_ms  INTEGER
        );

        CREATE TABLE IF NOT EXISTS process_lifetime_event (
            id                INTEGER PRIMARY KEY,
            identity_id       INTEGER NOT NULL REFERENCES process_identity(id),
            pid               INTEGER NOT NULL,
            kind              TEXT NOT NULL,
            observed_utc      TEXT NOT NULL,
            process_start_utc TEXT
        );

        CREATE INDEX IF NOT EXISTS ix_lifetime_observed
            ON process_lifetime_event (observed_utc);

        -- Pairing a Started with its Exited is the only way to measure how
        -- long a process lived, and that pairing is by pid.
        CREATE INDEX IF NOT EXISTS ix_lifetime_pid
            ON process_lifetime_event (pid, kind, observed_utc);

        CREATE INDEX IF NOT EXISTS ix_lifetime_identity
            ON process_lifetime_event (identity_id);

        -- Retention. Raw events older than the policy
        -- window are summarised here and then dropped. One row per local day
        -- per application: ~30 rows a day, so a year of history costs about a
        -- megabyte, against ~18 MB for a single month of raw events.
        --
        -- What a roll-up cannot answer is which applications were open AT THE
        -- SAME TIME. Co-occurrence is the whole of Phase 3, so the raw window
        -- has to stay wide enough to hold the evidence.
        CREATE TABLE IF NOT EXISTS daily_app_usage (
            day           TEXT NOT NULL,
            identity_id   INTEGER NOT NULL REFERENCES process_identity(id),
            foreground_ms INTEGER NOT NULL,
            switches      INTEGER NOT NULL,
            starts        INTEGER NOT NULL DEFAULT 0,
            exits         INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (day, identity_id)
        );

        CREATE TABLE IF NOT EXISTS daily_absence (
            day         TEXT NOT NULL PRIMARY KEY,
            idle_ms     INTEGER NOT NULL,
            idle_events INTEGER NOT NULL
        );

        -- B0.2: per application that owns a visible window,
        -- whether this unelevated app could read its identity.
        CREATE TABLE IF NOT EXISTS window_owner_access (
            name                TEXT PRIMARY KEY,
            denied              INTEGER NOT NULL,
            is_system_protected INTEGER NOT NULL,
            observations        INTEGER NOT NULL,
            denied_observations INTEGER NOT NULL,
            first_seen_utc      TEXT NOT NULL,
            last_seen_utc       TEXT NOT NULL
        );
        """;

    /// <summary>
    /// Columns added in version 2. Applied with ALTER TABLE for databases that
    /// already exist; <see cref="CreateSql"/> already contains them for new ones.
    /// </summary>
    internal static readonly (string Column, string Definition)[] V2IdentityColumns =
    [
        ("owned_window", "INTEGER NOT NULL DEFAULT 0"),
        ("lifetime_samples", "INTEGER NOT NULL DEFAULT 0"),
        ("median_lifetime_ms", "INTEGER"),
        ("is_ephemeral", "INTEGER NOT NULL DEFAULT 0"),
    ];
}
