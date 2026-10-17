using DmapShared.Models;
using DmapShared.Storage;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;

namespace DmapApi;

/// <summary>
/// The first version of the server kept its database next to the program
/// (bin\…\data\dmap.db) in separate seasons/campaigns/episodes tables. This brings
/// that guide across, once, into the data folder and the one-record-per-page format.
/// The old tables are left in place, untouched.
/// </summary>
internal static class LegacyMigration
{
    public static void BringOldDatabaseAcross(ServerOptions options, ILogger log)
    {
        if (File.Exists(options.DatabasePath)) return;

        var baseDir = AppContext.BaseDirectory;
        string[] candidates =
        {
            Path.Combine(baseDir, "data", "dmap.db"),
            // Before the move to .NET 10 the program ran from bin\<config>\net9.0.
            Path.GetFullPath(Path.Combine(baseDir, "..", "net9.0", "data", "dmap.db"))
        };

        var old = candidates.FirstOrDefault(File.Exists);
        if (old == null || Path.GetFullPath(old) == options.DatabasePath) return;

        // Fold any pending write-ahead log into the old file, then copy it.
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = old, Pooling = false }.ToString()))
        {
            connection.Open();
            using var checkpoint = connection.CreateCommand();
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            checkpoint.ExecuteNonQuery();
        }
        File.Copy(old, options.DatabasePath);
        log.LogInformation("Copied the guide from {Old} to {New}", old, options.DatabasePath);
    }

    /// <summary>The old guide's pages, if the old tables exist and haven't been brought across yet.</summary>
    public static List<(string Kind, string Id, string? ParentId, object Record)> ImportOldTables(
        SqliteConnection connection, SqliteTransaction transaction, ILogger log)
    {
        var none = new List<(string, string, string?, object)>();
        if (!TableExists(connection, transaction, "seasons") || Scalar(connection, transaction, "SELECT value FROM meta WHERE key = 'legacy_imported';") != null)
            return none;

        Run(connection, transaction, "INSERT INTO meta (key, value) VALUES ('legacy_imported', '1');");
        if (Convert.ToInt64(Scalar(connection, transaction, "SELECT COUNT(*) FROM records;"), CultureInfo.InvariantCulture) > 0)
            return none;

        var data = new ShowData();
        if (TableExists(connection, transaction, "guide_store") &&
            Scalar(connection, transaction, "SELECT guide_json FROM guide_store WHERE id = 1;") is string guideJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(guideJson);
                foreach (var property in doc.RootElement.EnumerateObject())
                    if (property.NameEquals("ShowName") && property.Value.GetString() is { Length: > 0 } name)
                        data.ShowName = name;
            }
            catch (JsonException) { }
        }

        var seasons = new Dictionary<string, Season>();
        foreach (var row in Rows(connection, transaction, "SELECT id, number, title, year, description FROM seasons ORDER BY number;"))
        {
            if (!Guid.TryParse(Text(row[0]), out var id)) continue;
            var season = new Season
            {
                Id = id,
                Number = Number(row[1]),
                Title = Text(row[2]),
                Year = Text(row[3]),
                Description = Text(row[4])
            };
            seasons[Text(row[0])] = season;
            data.Seasons.Add(season);
        }

        var campaigns = new Dictionary<string, Campaign>();
        foreach (var row in Rows(connection, transaction, @"SELECT id, season_id, number, title, dungeon_master, setting, description,
                cover_image, filming_start, filming_end, filming_location, characters FROM campaigns ORDER BY number;"))
        {
            if (!Guid.TryParse(Text(row[0]), out var id) || !seasons.TryGetValue(Text(row[1]), out var season)) continue;
            var campaign = new Campaign
            {
                Id = id,
                Number = Number(row[2]),
                Title = Text(row[3]),
                DungeonMaster = Text(row[4]),
                Setting = Text(row[5]),
                Description = Text(row[6]),
                CoverImage = Text(row[7]),
                FilmingStart = Date(row[8]),
                FilmingEnd = Date(row[9]),
                FilmingLocation = Text(row[10])
            };
            foreach (var member in List<OldCastMember>(row[11])) campaign.Characters.Add(member.ToCastMember());
            campaigns[Text(row[0])] = campaign;
            season.Campaigns.Add(campaign);
        }

        foreach (var row in Rows(connection, transaction, @"SELECT id, campaign_id, number, title, dungeon_master, date_filmed,
                filming_location, runtime, synopsis, production_notes, air_dates, cast_ids, guest_cast, key_moments
                FROM episodes ORDER BY number;"))
        {
            if (!Guid.TryParse(Text(row[0]), out var id) || !campaigns.TryGetValue(Text(row[1]), out var campaign)) continue;
            var episode = new Episode
            {
                Id = id,
                Number = Number(row[2]),
                Title = Text(row[3]),
                DungeonMaster = Text(row[4]),
                DateFilmed = Date(row[5]),
                FilmingLocation = Text(row[6]),
                Runtime = Text(row[7]),
                Synopsis = Text(row[8]),
                ProductionNotes = Text(row[9])
            };
            foreach (var air in List<OldAirDate>(row[10])) episode.AirDates.Add(new AirDate { Date = air.Date, Platform = air.Platform ?? "", Notes = air.Notes ?? "" });
            foreach (var castId in List<Guid>(row[11])) episode.CastIds.Add(castId);
            foreach (var guest in List<OldCastMember>(row[12])) episode.GuestCast.Add(guest.ToCastMember());
            foreach (var moment in List<OldKeyMoment>(row[13])) episode.KeyMoments.Add(new KeyMoment { Timestamp = moment.Timestamp ?? "", Description = moment.Description ?? "" });
            campaign.Episodes.Add(episode);
        }

        data.LinkParents();
        var records = GuideRecords.All(data).ToList();
        log.LogInformation("Brought {Count} pages across from the old guide tables", records.Count - 1);
        return records;
    }

    private sealed record OldCastMember(Guid Id, string? PlayerName, string? CharacterName, string? CharacterSummary, string? CharacterDescription, string? ImagePath)
    {
        public CastMember ToCastMember() => new()
        {
            Id = Id == Guid.Empty ? Guid.NewGuid() : Id,
            PlayerName = PlayerName ?? "",
            CharacterName = CharacterName ?? "",
            CharacterSummary = CharacterSummary ?? "",
            CharacterDescription = CharacterDescription ?? "",
            ImagePath = ImagePath ?? ""
        };
    }

    private sealed record OldAirDate(DateTime? Date, string? Platform, string? Notes);
    private sealed record OldKeyMoment(string? Timestamp, string? Description);

    private static List<T> List<T>(object? value)
    {
        if (value is not string json || json.Length == 0) return new();
        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, JsonSerializerOptions.Web) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    private static string Text(object? value) => value as string ?? "";

    private static int Number(object? value) => value is long n ? (int)n : 0;

    private static DateTime? Date(object? value) =>
        value is string s && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : null;

    private static bool TableExists(SqliteConnection connection, SqliteTransaction transaction, string name) =>
        Scalar(connection, transaction, $"SELECT name FROM sqlite_master WHERE type = 'table' AND name = '{name}';") != null;

    private static object? Scalar(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value is DBNull ? null : value;
    }

    private static void Run(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static List<object?[]> Rows(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<object?[]>();
        while (reader.Read())
        {
            var row = new object?[reader.FieldCount];
            for (int i = 0; i < row.Length; i++) row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }
}
