namespace Statehop.Storage;

/// <summary>
/// The Phase 0 schema. Deliberately minimal — it does not have
/// to be the final shape, and Phase 1 will revisit it along with the retention
/// policy.
///
/// PRIVACY: there is no column anywhere for a window
/// title, and there must not be one. Titles can carry client names, file
/// names, URLs and email subjects. What is stored is process identity and
/// timestamps.
/// </summary>
internal static class Schema
{
    internal const int Version = 1;

    internal const string CreateSql = """
        CREATE TABLE IF NOT EXISTS schema_version (
            version INTEGER NOT NULL
        );

        -- One row per distinct application, not per run.
        CREATE TABLE IF NOT EXISTS process_identity (
            id              INTEGER PRIMARY KEY,
            name            TEXT NOT NULL,
            executable_path TEXT,
            access_state    TEXT NOT NULL,
            first_seen_utc  TEXT NOT NULL,
            last_seen_utc   TEXT NOT NULL
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
}
