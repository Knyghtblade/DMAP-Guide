using DmapShared.Api;
using DmapShared.Models;
using DmapShared.Storage;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;

namespace DmapApi;

/// <summary>
/// The shared guide, one row per page. This is the only copy: the whole-guide view is
/// read from these rows, never kept separately.
///
///   records   one row per page: its kind, parent page, data (the page in the
///             GuideRecords format), version (goes up with each save of that page)
///             and changed_at (the guide version of its last change)
///   deleted   pages deleted, with the guide version they were deleted at, so apps
///             catching up know to remove them
///   meta      guide_version: goes up with every change to the guide
///
/// All writes go through one lock and one transaction each, so a version check and
/// the write it allows can't be split by another save.
/// </summary>
public sealed class GuideDatabase
{
    private const int MaxRecordBytes = 1_000_000;

    private readonly string _connectionString;
    private readonly object _writeLock = new();

    public GuideDatabase(ServerOptions options, ILogger<GuideDatabase> log)
    {
        Directory.CreateDirectory(options.DataFolder);
        LegacyMigration.BringOldDatabaseAcross(options, log);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = options.DatabasePath,
            Pooling = true
        }.ToString();

        using var connection = Open();
        Execute(connection, "PRAGMA journal_mode = WAL;");
        Execute(connection, @"
            CREATE TABLE IF NOT EXISTS records (
                id TEXT PRIMARY KEY,
                kind TEXT NOT NULL,
                parent_id TEXT,
                data TEXT NOT NULL,
                version INTEGER NOT NULL,
                changed_at INTEGER NOT NULL,
                updated_by TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_records_changed ON records (changed_at);
            CREATE INDEX IF NOT EXISTS ix_records_parent ON records (parent_id);

            CREATE TABLE IF NOT EXISTS deleted (
                id TEXT PRIMARY KEY,
                kind TEXT NOT NULL,
                deleted_at INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_deleted_at ON deleted (deleted_at);

            CREATE TABLE IF NOT EXISTS meta (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            INSERT OR IGNORE INTO meta (key, value) VALUES ('guide_version', '0');");

        lock (_writeLock)
        {
            using var transaction = connection.BeginTransaction();
            var imported = LegacyMigration.ImportOldTables(connection, transaction, log);
            if (imported.Count > 0)
            {
                long version = NextGuideVersion(connection, transaction);
                foreach (var (kind, id, parentId, record) in imported)
                    Upsert(connection, transaction, kind, id, parentId, record, version, 1, "Imported", DateTime.UtcNow);
            }
            transaction.Commit();
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        Execute(connection, "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;");
        return connection;
    }

    // ============================================================
    // READ
    // ============================================================
    public GuideSnapshot GetGuide()
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        return new GuideSnapshot
        {
            Version = GuideVersion(connection, transaction),
            Records = ReadRecords(connection, transaction, "SELECT * FROM records ORDER BY id;")
        };
    }

    public GuideChanges GetChanges(long since)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        var changes = new GuideChanges
        {
            Version = GuideVersion(connection, transaction),
            Records = ReadRecords(connection, transaction, "SELECT * FROM records WHERE changed_at > @since ORDER BY changed_at;", ("@since", since))
        };

        using var command = Command(connection, transaction, "SELECT id, kind FROM deleted WHERE deleted_at > @since;", ("@since", since));
        using var reader = command.ExecuteReader();
        while (reader.Read())
            changes.Deleted.Add(new DeletedRecord { Id = reader.GetString(0), Kind = reader.GetString(1) });
        return changes;
    }

    public RecordEnvelope? GetRecord(string id)
    {
        using var connection = Open();
        return ReadRecords(connection, null, "SELECT * FROM records WHERE id = @id;", ("@id", id)).FirstOrDefault();
    }

    // ============================================================
    // WRITE
    // ============================================================
    /// <summary>Save one page, if it's still at the version the editor started from.</summary>
    public PutResult Put(string kind, string id, RecordWrite write, string user)
    {
        if (!GuideRecords.IsKnownKind(kind))
            return PutResult.Invalid("Unknown kind of page.");
        if (kind == GuideRecords.Guide ? id != GuideRecords.GuideId : !Guid.TryParse(id, out _))
            return PutResult.Invalid("The page id isn't valid.");
        if (write.Data.ValueKind != JsonValueKind.Object)
            return PutResult.Invalid("The page must be a JSON object.");

        string raw = write.Data.GetRawText();
        if (raw.Length > MaxRecordBytes)
            return PutResult.Invalid("The page is too large to save.");

        object record;
        try
        {
            record = GuideRecords.Parse(kind, raw);
        }
        catch (JsonException ex)
        {
            return PutResult.Invalid($"The page couldn't be read: {ex.Message}");
        }

        string? parentKind = kind switch
        {
            GuideRecords.Campaign => GuideRecords.Season,
            GuideRecords.Episode => GuideRecords.Campaign,
            _ => null
        };
        string? parentId = parentKind == null ? null : write.ParentId;
        if (parentKind != null && !Guid.TryParse(parentId, out _))
            return PutResult.Invalid($"A {kind} needs the id of its {parentKind}.");

        // The page's own fields name it and its parent; make them agree with the request.
        switch (record)
        {
            case Season s: s.Id = Guid.Parse(id); break;
            case Campaign c: c.Id = Guid.Parse(id); c.SeasonId = Guid.Parse(parentId!); break;
            case Episode e: e.Id = Guid.Parse(id); e.CampaignId = Guid.Parse(parentId!); break;
        }

        var now = DateTime.UtcNow;
        if (record is TreeNode node)
        {
            node.LastEditedBy = user;
            node.LastEdited = now;
        }

        lock (_writeLock)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();

            var current = ReadRecords(connection, transaction, "SELECT * FROM records WHERE id = @id;", ("@id", id)).FirstOrDefault();
            if (current != null && current.Kind != kind)
                return PutResult.Invalid("That id belongs to a different kind of page.");
            if (current == null && write.BaseVersion != 0)
                return PutResult.Gone();
            if (current != null && current.Version != write.BaseVersion)
                return PutResult.Conflict(current);

            if (parentKind != null && !Exists(connection, transaction, parentKind, parentId!))
                return PutResult.Invalid($"The {parentKind} this {kind} belongs to no longer exists. Someone may have deleted it.");

            long guideVersion = NextGuideVersion(connection, transaction);
            long version = (current?.Version ?? 0) + 1;
            Upsert(connection, transaction, kind, id, parentId, record, guideVersion, version, user, now);
            using (var undelete = Command(connection, transaction, "DELETE FROM deleted WHERE id = @id;", ("@id", id)))
                undelete.ExecuteNonQuery();

            var saved = ReadRecords(connection, transaction, "SELECT * FROM records WHERE id = @id;", ("@id", id)).Single();
            transaction.Commit();
            return current == null ? PutResult.Created(saved) : PutResult.Saved(saved);
        }
    }

    /// <summary>Delete a page and the pages inside it. False if it doesn't exist.</summary>
    public DeleteResult Delete(string kind, string id)
    {
        if (kind == GuideRecords.Guide) return DeleteResult.Invalid;

        lock (_writeLock)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            if (!Exists(connection, transaction, kind, id)) return DeleteResult.NotFound;

            // The page, then everything inside it.
            var doomed = new List<(string Id, string Kind)> { (id, kind) };
            for (int i = 0; i < doomed.Count; i++)
            {
                using var children = Command(connection, transaction, "SELECT id, kind FROM records WHERE parent_id = @id;", ("@id", doomed[i].Id));
                using var reader = children.ExecuteReader();
                while (reader.Read()) doomed.Add((reader.GetString(0), reader.GetString(1)));
            }

            long guideVersion = NextGuideVersion(connection, transaction);
            foreach (var (doomedId, doomedKind) in doomed)
            {
                using (var delete = Command(connection, transaction, "DELETE FROM records WHERE id = @id;", ("@id", doomedId)))
                    delete.ExecuteNonQuery();
                using var tombstone = Command(connection, transaction,
                    "INSERT OR REPLACE INTO deleted (id, kind, deleted_at) VALUES (@id, @kind, @at);",
                    ("@id", doomedId), ("@kind", doomedKind), ("@at", guideVersion));
                tombstone.ExecuteNonQuery();
            }

            transaction.Commit();
            return DeleteResult.Deleted;
        }
    }

    // ============================================================
    // HELPERS
    // ============================================================
    private static void Upsert(SqliteConnection connection, SqliteTransaction transaction, string kind, string id, string? parentId,
        object record, long changedAt, long version, string user, DateTime when)
    {
        using var command = Command(connection, transaction, @"
            INSERT INTO records (id, kind, parent_id, data, version, changed_at, updated_by, updated_at)
            VALUES (@id, @kind, @parent, @data, @version, @changed, @by, @at)
            ON CONFLICT (id) DO UPDATE SET
                parent_id = excluded.parent_id, data = excluded.data, version = excluded.version,
                changed_at = excluded.changed_at, updated_by = excluded.updated_by, updated_at = excluded.updated_at;",
            ("@id", id), ("@kind", kind), ("@parent", parentId), ("@data", GuideRecords.Serialize(record)),
            ("@version", version), ("@changed", changedAt), ("@by", user), ("@at", when.ToString("O", CultureInfo.InvariantCulture)));
        command.ExecuteNonQuery();
    }

    private static bool Exists(SqliteConnection connection, SqliteTransaction transaction, string kind, string id)
    {
        using var command = Command(connection, transaction, "SELECT EXISTS (SELECT 1 FROM records WHERE id = @id AND kind = @kind);",
            ("@id", id), ("@kind", kind));
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }

    private static long GuideVersion(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = Command(connection, transaction, "SELECT value FROM meta WHERE key = 'guide_version';");
        return long.Parse((string)command.ExecuteScalar()!, CultureInfo.InvariantCulture);
    }

    private static long NextGuideVersion(SqliteConnection connection, SqliteTransaction transaction)
    {
        long next = GuideVersion(connection, transaction) + 1;
        using var command = Command(connection, transaction, "UPDATE meta SET value = @v WHERE key = 'guide_version';",
            ("@v", next.ToString(CultureInfo.InvariantCulture)));
        command.ExecuteNonQuery();
        return next;
    }

    private static List<RecordEnvelope> ReadRecords(SqliteConnection connection, SqliteTransaction? transaction, string sql,
        params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, transaction, sql, parameters);
        using var reader = command.ExecuteReader();
        var rows = new List<RecordEnvelope>();
        while (reader.Read())
        {
            using var data = JsonDocument.Parse(reader.GetString(reader.GetOrdinal("data")));
            rows.Add(new RecordEnvelope
            {
                Id = reader.GetString(reader.GetOrdinal("id")),
                Kind = reader.GetString(reader.GetOrdinal("kind")),
                ParentId = reader.IsDBNull(reader.GetOrdinal("parent_id")) ? null : reader.GetString(reader.GetOrdinal("parent_id")),
                Version = reader.GetInt64(reader.GetOrdinal("version")),
                UpdatedBy = reader.GetString(reader.GetOrdinal("updated_by")),
                UpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("updated_at")), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                Data = data.RootElement.Clone()
            });
        }
        return rows;
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql,
        params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}

public sealed class PutResult
{
    public RecordEnvelope? Record { get; private init; }
    public string? Problem { get; private init; }
    public bool IsCreated { get; private init; }
    public bool IsConflict { get; private init; }
    public bool IsGone { get; private init; }

    public static PutResult Saved(RecordEnvelope r) => new() { Record = r };
    public static PutResult Created(RecordEnvelope r) => new() { Record = r, IsCreated = true };
    public static PutResult Conflict(RecordEnvelope current) => new() { Record = current, IsConflict = true };
    public static PutResult Gone() => new() { IsGone = true };
    public static PutResult Invalid(string problem) => new() { Problem = problem };
}

public enum DeleteResult { Deleted, NotFound, Invalid }
