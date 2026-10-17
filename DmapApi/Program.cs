using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

if (builder.Configuration.GetValue<bool>("Dmap:LoadUserSecrets"))
    builder.Configuration.AddUserSecrets<Program>(optional: true);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
var isDevelopment = app.Environment.IsDevelopment();
var configuredApiKeys = builder.Configuration.GetSection("Dmap:ApiKeys").GetChildren()
    .Select(section => (Key: section["Key"], Role: section["Role"]))
    .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Role))
    .ToList();
var developmentApiKey = builder.Configuration["Dmap:ApiKey"] ?? "local-dev-key";

if (!isDevelopment && (configuredApiKeys.Count == 0 || configuredApiKeys.Any(entry => !IsKnownRole(entry.Role))))
    throw new InvalidOperationException("Configure Dmap:ApiKeys with valid role-bound API keys before running outside Development.");

var dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
Directory.CreateDirectory(dataDirectory);
var dbPath = Path.Combine(dataDirectory, "dmap.db");
EnsureDatabase(dbPath);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api"))
    {
        await next();
        return;
    }

    var suppliedApiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault();
    string? role;
    if (isDevelopment)
    {
        if (!FixedTimeEquals(suppliedApiKey, developmentApiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
            return;
        }

        role = context.Request.Headers["X-User-Role"].FirstOrDefault() ?? "viewer";
    }
    else
    {
        role = configuredApiKeys
            .Where(entry => FixedTimeEquals(suppliedApiKey, entry.Key!))
            .Select(entry => entry.Role)
            .FirstOrDefault();
    }

    if (!IsKnownRole(role))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
        return;
    }

    context.Items["DmapUserRole"] = role;
    await next();
});

app.MapGet("/api/dmap/health", () => Results.Ok(new
{
    status = "ok",
    database = dbPath,
    updatedAt = DateTime.UtcNow,
    supportedRoles = new[] { "viewer", "editor", "admin" }
}));

app.MapGet("/api/dmap/guide", (HttpContext context) =>
{
    var payload = LoadGuide(dbPath);
    context.Response.Headers["X-Guide-Version"] = payload.Version.ToString();
    return Results.Text(payload.Json, "application/json");
});

app.MapGet("/api/dmap/seasons", () => LoadGuideSeasons(dbPath));
app.MapGet("/api/dmap/campaigns", () => LoadGuideCampaigns(dbPath));
app.MapGet("/api/dmap/episodes", () => LoadGuideEpisodes(dbPath));

app.MapGet("/api/dmap/seasons/{id}", (string id) =>
{
    var season = LoadGuideSeasons(dbPath).FirstOrDefault(item => item.Id == id);
    return season is null ? Results.NotFound() : Results.Ok(season);
});

app.MapPost("/api/dmap/seasons", (HttpContext context, SeasonWriteRequest request) =>
{
    var role = GetUserRole(context);
    if (!IsAllowed(role, "edit"))
        return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Editor role required.");
    if (request.Number < 1 || string.IsNullOrWhiteSpace(request.Name) ||
        (!string.IsNullOrWhiteSpace(request.Id) && !Guid.TryParse(request.Id, out _)))
        return Results.BadRequest(new { error = "Id must be a GUID when provided, Number must be positive, and Name is required." });

    var id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString() : request.Id;
    if (SeasonExists(dbPath, id))
        return Results.Conflict(new { error = "A season with this ID already exists." });
    CreateSeason(dbPath, id, request, role);
    SetGuideVersionHeader(context, dbPath);
    var created = LoadGuideSeasons(dbPath).First(item => item.Id == id);
    return Results.Created($"/api/dmap/seasons/{id}", created);
});

app.MapDelete("/api/dmap/seasons/{id}", (HttpContext context, string id) =>
{
    var role = GetUserRole(context);
    if (!IsAllowed(role, "edit"))
        return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Editor role required.");
    if (!Guid.TryParse(id, out _))
        return Results.BadRequest(new { error = "A valid season ID is required." });
    if (!DeleteSeason(dbPath, id, role))
        return Results.NotFound();
    SetGuideVersionHeader(context, dbPath);
    return Results.NoContent();
});

app.MapGet("/api/dmap/campaigns/{id}", (string id) =>
{
    var campaign = LoadGuideCampaigns(dbPath).FirstOrDefault(item => item.Id == id);
    return campaign is null ? Results.NotFound() : Results.Ok(campaign);
});

app.MapPost("/api/dmap/campaigns", (HttpContext context, CampaignWriteRequest request) =>
{
    var role = GetUserRole(context);
    if (!IsAllowed(role, "edit"))
        return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Editor role required.");
    if (request.Number < 1 || string.IsNullOrWhiteSpace(request.Name) || !Guid.TryParse(request.SeasonId, out _) ||
        (!string.IsNullOrWhiteSpace(request.Id) && !Guid.TryParse(request.Id, out _)))
        return Results.BadRequest(new { error = "IDs must be valid GUIDs, Number must be positive, and Name is required." });
    if (!SeasonExists(dbPath, request.SeasonId))
        return Results.BadRequest(new { error = "SeasonId must identify an existing season." });

    var id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString() : request.Id;
    if (CampaignExists(dbPath, id))
        return Results.Conflict(new { error = "A campaign with this ID already exists." });
    CreateCampaign(dbPath, id, request, role);
    SetGuideVersionHeader(context, dbPath);
    var created = LoadGuideCampaigns(dbPath).First(item => item.Id == id);
    return Results.Created($"/api/dmap/campaigns/{id}", created);
});

app.MapDelete("/api/dmap/campaigns/{id}", (HttpContext context, string id) =>
{
    var role = GetUserRole(context);
    if (!IsAllowed(role, "edit"))
        return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Editor role required.");
    if (!Guid.TryParse(id, out _))
        return Results.BadRequest(new { error = "A valid campaign ID is required." });
    if (!DeleteCampaign(dbPath, id, role))
        return Results.NotFound();
    SetGuideVersionHeader(context, dbPath);
    return Results.NoContent();
});

app.MapPut("/api/dmap/seasons/{id}", (HttpContext context, string id, SeasonWriteRequest request) =>
{
    var role = GetUserRole(context);
    if (!IsAllowed(role, "edit"))
        return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Editor role required.");
    if (!Guid.TryParse(id, out _) || request.Number < 1 || string.IsNullOrWhiteSpace(request.Name))
        return Results.BadRequest(new { error = "A valid season ID, positive Number, and Name are required." });
    var currentSeason = LoadGuideSeasons(dbPath).FirstOrDefault(season => season.Id == id);
    if (currentSeason is null)
        return Results.NotFound();
    if (request.UpdatedAt.HasValue && !IsSameTimestamp(request.UpdatedAt.Value, currentSeason.UpdatedAt))
        return Results.Conflict(new { error = "Season changed on the server.", currentUpdatedAt = currentSeason.UpdatedAt });

    UpdateSeason(dbPath, id, request, role);
    SetGuideVersionHeader(context, dbPath);
    return Results.Ok(LoadGuideSeasons(dbPath).First(season => season.Id == id));
});

app.MapPut("/api/dmap/campaigns/{id}", (HttpContext context, string id, CampaignWriteRequest request) =>
{
    var role = GetUserRole(context);
    if (!IsAllowed(role, "edit"))
        return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Editor role required.");
    if (!Guid.TryParse(id, out _) || request.Number < 1 || string.IsNullOrWhiteSpace(request.Name) ||
        !Guid.TryParse(request.SeasonId, out _))
        return Results.BadRequest(new { error = "A valid campaign ID, SeasonId, positive Number, and Name are required." });
    var currentCampaign = LoadGuideCampaigns(dbPath).FirstOrDefault(campaign => campaign.Id == id);
    if (currentCampaign is null)
        return Results.NotFound();
    if (!SeasonExists(dbPath, request.SeasonId))
        return Results.BadRequest(new { error = "SeasonId must identify an existing season." });
    if (request.UpdatedAt.HasValue && !IsSameTimestamp(request.UpdatedAt.Value, currentCampaign.UpdatedAt))
        return Results.Conflict(new { error = "Campaign changed on the server.", currentUpdatedAt = currentCampaign.UpdatedAt });

    UpdateCampaign(dbPath, id, request, role);
    SetGuideVersionHeader(context, dbPath);
    return Results.Ok(LoadGuideCampaigns(dbPath).First(campaign => campaign.Id == id));
});

app.MapGet("/api/dmap/episodes/{id}", (string id) =>
{
    var episode = LoadGuideEpisode(dbPath, id);
    return episode is null ? Results.NotFound() : Results.Ok(episode);
});

app.MapPost("/api/dmap/episodes", (HttpContext context, EpisodeWriteRequest request) =>
{
    var role = GetUserRole(context);
    if (!IsAllowed(role, "edit"))
        return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Editor role required.");
    if (!IsValidEpisodeRequest(dbPath, request))
        return Results.BadRequest(new { error = "CampaignId must identify an existing campaign, Number must be positive, and Name is required." });

    if (!string.IsNullOrWhiteSpace(request.Id) && !Guid.TryParse(request.Id, out _))
        return Results.BadRequest(new { error = "Id must be a valid GUID when provided." });

    var id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString() : request.Id;
    if (LoadGuideEpisode(dbPath, id) is not null)
        return Results.Conflict(new { error = "An episode with this ID already exists." });
    SaveEpisode(dbPath, id, request, role, insert: true);
    SetGuideVersionHeader(context, dbPath);
    var episode = LoadGuideEpisode(dbPath, id)!;
    return Results.Created($"/api/dmap/episodes/{id}", episode);
});

app.MapPut("/api/dmap/episodes/{id}", (HttpContext context, string id, EpisodeWriteRequest request) =>
{
    var role = GetUserRole(context);
    if (!IsAllowed(role, "edit"))
        return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Editor role required.");
    if (!Guid.TryParse(id, out _) || request.Number < 1 || string.IsNullOrWhiteSpace(request.Name))
        return Results.BadRequest(new { error = "A valid episode ID, existing CampaignId, positive Number, and Name are required." });
    var currentEpisode = LoadGuideEpisode(dbPath, id);
    if (currentEpisode is null)
        return Results.NotFound();
    if (request.UpdatedAt.HasValue && !IsSameTimestamp(request.UpdatedAt.Value, currentEpisode.UpdatedAt))
        return Results.Conflict(new { error = "Episode changed on the server.", currentUpdatedAt = currentEpisode.UpdatedAt });
    if (!IsValidEpisodeRequest(dbPath, request))
        return Results.BadRequest(new { error = "CampaignId must identify an existing campaign." });

    SaveEpisode(dbPath, id, request, role, insert: false);
    SetGuideVersionHeader(context, dbPath);
    return Results.Ok(LoadGuideEpisode(dbPath, id));
});

app.MapDelete("/api/dmap/episodes/{id}", (HttpContext context, string id) =>
{
    var role = GetUserRole(context);
    if (!IsAllowed(role, "edit"))
        return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Editor role required.");
    if (!Guid.TryParse(id, out _))
        return Results.BadRequest(new { error = "A valid episode ID is required." });

    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    using var transaction = connection.BeginTransaction();
    using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = "DELETE FROM episodes WHERE id = @id;";
    command.Parameters.AddWithValue("@id", id);
    if (command.ExecuteNonQuery() == 0)
        return Results.NotFound();

    SyncEpisodeGuideJson(connection, transaction, id, request: null, delete: true, role);
    transaction.Commit();
    SetGuideVersionHeader(context, dbPath);
    return Results.NoContent();
});

app.MapPut("/api/dmap/guide", async (HttpContext context) =>
{
    var role = GetUserRole(context);
    if (!IsAllowed(role, "edit"))
    {
        return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Editor role required.");
    }

    var requestedVersion = context.Request.Headers["X-Guide-Version"].FirstOrDefault();
    var current = LoadGuide(dbPath);
    if (!string.IsNullOrWhiteSpace(requestedVersion) && int.TryParse(requestedVersion, out var clientVersion) && clientVersion != current.Version)
    {
        return Results.Conflict(new
        {
            error = "Guide changed on the server.",
            currentVersion = current.Version,
            clientVersion = clientVersion
        });
    }

    using var reader = new StreamReader(context.Request.Body);
    var json = await reader.ReadToEndAsync();

    if (string.IsNullOrWhiteSpace(json))
        return Results.BadRequest(new { error = "Request body cannot be empty." });

    var updated = SaveGuideJson(dbPath, json, role);
    context.Response.Headers["X-Guide-Version"] = updated.Version.ToString();
    return Results.Ok(new { saved = true, version = updated.Version, updatedAt = updated.UpdatedAt });
});

app.Run();

static bool IsAllowed(string role, string action)
{
    var normalized = (role ?? "viewer").Trim().ToLowerInvariant();
    return action switch
    {
        "edit" => normalized is "editor" or "admin",
        "read" => normalized is "viewer" or "editor" or "admin",
        _ => false
    };
}

static bool IsKnownRole(string? role) =>
    role?.Trim().ToLowerInvariant() is "viewer" or "editor" or "admin";

static string GetUserRole(HttpContext context) => context.Items["DmapUserRole"] as string ?? "viewer";

static bool FixedTimeEquals(string? supplied, string expected)
{
    if (string.IsNullOrEmpty(supplied))
        return false;

    return CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(supplied),
        Encoding.UTF8.GetBytes(expected));
}

static void EnsureDatabase(string dbPath)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();

    using var command = connection.CreateCommand();
    command.CommandText = @"
        CREATE TABLE IF NOT EXISTS guide_store (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            guide_json TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            version INTEGER NOT NULL DEFAULT 1,
            updated_by TEXT NOT NULL DEFAULT 'system'
        );

        CREATE TABLE IF NOT EXISTS seasons (
            id TEXT PRIMARY KEY,
            number INTEGER NOT NULL,
            title TEXT NOT NULL,
            year TEXT,
            description TEXT,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS campaigns (
            id TEXT PRIMARY KEY,
            season_id TEXT NOT NULL,
            number INTEGER NOT NULL,
            title TEXT NOT NULL,
            dungeon_master TEXT,
            setting TEXT,
            description TEXT,
            cover_image TEXT,
            filming_start TEXT,
            filming_end TEXT,
            filming_location TEXT,
            characters TEXT,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS episodes (
            id TEXT PRIMARY KEY,
            campaign_id TEXT NOT NULL,
            number INTEGER NOT NULL,
            title TEXT NOT NULL,
            dungeon_master TEXT,
            date_filmed TEXT,
            filming_location TEXT,
            runtime TEXT,
            synopsis TEXT,
            production_notes TEXT,
            air_dates TEXT,
            cast_ids TEXT,
            guest_cast TEXT,
            key_moments TEXT,
            updated_at TEXT NOT NULL
        );
    ";
    command.ExecuteNonQuery();
    EnsureColumn(connection, "campaigns", "characters", "TEXT");
    EnsureColumn(connection, "episodes", "guest_cast", "TEXT");
    EnsureColumn(connection, "episodes", "key_moments", "TEXT");
    BackfillCampaignCharacters(connection);
}

static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string declaration)
{
    bool exists;
    using (var check = connection.CreateCommand())
    {
        check.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = check.ExecuteReader();
        exists = false;
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }
    }

    if (!exists)
    {
        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {declaration};";
        alter.ExecuteNonQuery();
    }
}

static void BackfillCampaignCharacters(SqliteConnection connection)
{
    string? guideJson;
    using (var read = connection.CreateCommand())
    {
        read.CommandText = "SELECT guide_json FROM guide_store WHERE id = 1;";
        guideJson = read.ExecuteScalar() as string;
    }

    if (string.IsNullOrWhiteSpace(guideJson))
        return;

    using var document = JsonDocument.Parse(guideJson);
    var seasons = GetArrayProperty(document.RootElement, "Seasons");
    if (!seasons.HasValue)
        return;

    using var transaction = connection.BeginTransaction();
    foreach (var season in seasons.Value.EnumerateArray())
    {
        var campaigns = GetArrayProperty(season, "Campaigns");
        if (!campaigns.HasValue)
            continue;

        foreach (var campaign in campaigns.Value.EnumerateArray())
        {
            var id = GetStringValue(campaign, "Id");
            var characters = GetArrayProperty(campaign, "Characters");
            if (id is null || !characters.HasValue)
                continue;

            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE campaigns SET characters = @characters WHERE id = @id AND characters IS NULL;";
            update.Parameters.AddWithValue("@characters", characters.Value.GetRawText());
            update.Parameters.AddWithValue("@id", id);
            update.ExecuteNonQuery();
        }
    }
    transaction.Commit();
}

static GuideRecord LoadGuide(string dbPath)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();

    using var command = connection.CreateCommand();
    command.CommandText = "SELECT guide_json, updated_at, version FROM guide_store WHERE id = 1;";
    using var reader = command.ExecuteReader();
    if (!reader.Read())
        return new GuideRecord("{}", DateTime.UtcNow, 0);

    var json = reader.GetString(0);
    var updatedAt = reader.GetDateTime(1);
    var version = reader.GetInt32(2);
    return new GuideRecord(json, updatedAt, version);
}

static void SetGuideVersionHeader(HttpContext context, string dbPath) =>
    context.Response.Headers["X-Guide-Version"] = LoadGuide(dbPath).Version.ToString();

static List<GuideSeasonRecord> LoadGuideSeasons(string dbPath)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();

    using var command = connection.CreateCommand();
    command.CommandText = "SELECT id, number, title, year, description, updated_at FROM seasons ORDER BY number;";
    using var reader = command.ExecuteReader();

    var rows = new List<GuideSeasonRecord>();
    while (reader.Read())
    {
        rows.Add(new GuideSeasonRecord(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetDateTime(5))
        );
    }

    return rows;
}

static List<GuideCampaignRecord> LoadGuideCampaigns(string dbPath)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();

    using var command = connection.CreateCommand();
    command.CommandText = @"SELECT id, season_id, number, title, dungeon_master, setting, description,
        cover_image, filming_start, filming_end, filming_location, characters, updated_at
        FROM campaigns ORDER BY number;";
    using var reader = command.ExecuteReader();

    var rows = new List<GuideCampaignRecord>();
    while (reader.Read())
    {
        rows.Add(new GuideCampaignRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            ReadNullableDateTime(reader, 8),
            ReadNullableDateTime(reader, 9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            DeserializeList<EpisodeCastMemberRecord>(reader, 11),
            reader.GetDateTime(12))
        );
    }

    return rows;
}

static List<GuideEpisodeRecord> LoadGuideEpisodes(string dbPath)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();

    using var command = connection.CreateCommand();
    command.CommandText = @"SELECT id, campaign_id, number, title, dungeon_master, date_filmed,
        filming_location, runtime, synopsis, production_notes, air_dates, cast_ids,
        guest_cast, key_moments, updated_at FROM episodes ORDER BY number;";
    using var reader = command.ExecuteReader();

    var rows = new List<GuideEpisodeRecord>();
    while (reader.Read())
        rows.Add(ReadGuideEpisode(reader));

    return rows;
}

static GuideEpisodeRecord? LoadGuideEpisode(string dbPath, string id)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();

    using var command = connection.CreateCommand();
    command.CommandText = @"SELECT id, campaign_id, number, title, dungeon_master, date_filmed,
        filming_location, runtime, synopsis, production_notes, air_dates, cast_ids,
        guest_cast, key_moments, updated_at FROM episodes WHERE id = @id;";
    command.Parameters.AddWithValue("@id", id);
    using var reader = command.ExecuteReader();
    if (!reader.Read())
        return null;

    return ReadGuideEpisode(reader);
}

static GuideEpisodeRecord ReadGuideEpisode(SqliteDataReader reader)
{
    return new GuideEpisodeRecord(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetInt32(2),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        ReadNullableDateTime(reader, 5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetString(9),
        DeserializeList<EpisodeAirDateRecord>(reader, 10),
        DeserializeList<Guid>(reader, 11),
        DeserializeList<EpisodeCastMemberRecord>(reader, 12),
        DeserializeList<EpisodeKeyMomentRecord>(reader, 13),
        reader.GetDateTime(14));
}

static DateTime? ReadNullableDateTime(SqliteDataReader reader, int ordinal) =>
    reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);

static bool IsSameTimestamp(DateTime expected, DateTime actual) =>
    expected.ToUniversalTime().Ticks == actual.ToUniversalTime().Ticks;

static List<T> DeserializeList<T>(SqliteDataReader reader, int ordinal)
{
    if (reader.IsDBNull(ordinal))
        return [];

    return JsonSerializer.Deserialize<List<T>>(reader.GetString(ordinal), JsonSerializerOptions.Web) ?? [];
}

static bool IsValidEpisodeRequest(string dbPath, EpisodeWriteRequest request)
{
    if (request.Number < 1 || string.IsNullOrWhiteSpace(request.Name) || !Guid.TryParse(request.CampaignId, out _))
        return false;

    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = "SELECT EXISTS(SELECT 1 FROM campaigns WHERE id = @campaignId);";
    command.Parameters.AddWithValue("@campaignId", request.CampaignId);
    return Convert.ToInt32(command.ExecuteScalar()) == 1;
}

static bool SeasonExists(string dbPath, string id) => RecordExists(dbPath, "seasons", id);
static bool CampaignExists(string dbPath, string id) => RecordExists(dbPath, "campaigns", id);

static bool RecordExists(string dbPath, string tableName, string id)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = $"SELECT EXISTS(SELECT 1 FROM {tableName} WHERE id = @id);";
    command.Parameters.AddWithValue("@id", id);
    return Convert.ToInt32(command.ExecuteScalar()) == 1;
}

static void CreateSeason(string dbPath, string id, SeasonWriteRequest request, string role)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    using var transaction = connection.BeginTransaction();

    using (var command = connection.CreateCommand())
    {
        command.Transaction = transaction;
        command.CommandText = @"INSERT INTO seasons (id, number, title, year, description, updated_at)
            VALUES (@id, @number, @title, @year, @description, @updatedAt);";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@number", request.Number);
        command.Parameters.AddWithValue("@title", request.Name.Trim());
        command.Parameters.AddWithValue("@year", (object?)request.Year ?? DBNull.Value);
        command.Parameters.AddWithValue("@description", (object?)request.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    var root = LoadGuideRoot(connection, transaction) ?? new JsonObject
    {
        ["ShowName"] = "Dungeon Masters",
        ["Seasons"] = new JsonArray()
    };
    GetOrCreateArray(root, "Seasons").Add(new JsonObject
    {
        ["Id"] = id,
        ["Number"] = request.Number,
        ["Title"] = request.Name.Trim(),
        ["Year"] = request.Year,
        ["Description"] = request.Description,
        ["Campaigns"] = new JsonArray()
    });
    SaveGuideRoot(connection, transaction, root, role);
    transaction.Commit();
}

static void CreateCampaign(string dbPath, string id, CampaignWriteRequest request, string role)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    using var transaction = connection.BeginTransaction();

    using (var command = connection.CreateCommand())
    {
        command.Transaction = transaction;
        command.CommandText = @"INSERT INTO campaigns (
            id, season_id, number, title, dungeon_master, setting, description, cover_image,
            filming_start, filming_end, filming_location, characters, updated_at)
            VALUES (
            @id, @seasonId, @number, @title, @dungeonMaster, @setting, @description, @coverImage,
            @filmingStart, @filmingEnd, @filmingLocation, @characters, @updatedAt);";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@seasonId", request.SeasonId);
        command.Parameters.AddWithValue("@number", request.Number);
        command.Parameters.AddWithValue("@title", request.Name.Trim());
        command.Parameters.AddWithValue("@dungeonMaster", (object?)request.DungeonMaster ?? DBNull.Value);
        command.Parameters.AddWithValue("@setting", (object?)request.Setting ?? DBNull.Value);
        command.Parameters.AddWithValue("@description", (object?)request.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("@coverImage", (object?)request.CoverImage ?? DBNull.Value);
        command.Parameters.AddWithValue("@filmingStart", request.FilmingStart?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@filmingEnd", request.FilmingEnd?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@filmingLocation", (object?)request.FilmingLocation ?? DBNull.Value);
        command.Parameters.AddWithValue("@characters", JsonSerializer.Serialize(request.Characters));
        command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    var root = LoadGuideRoot(connection, transaction);
    var season = root is null ? null : FindSeason(root, request.SeasonId);
    if (root == null || season == null)
        throw new InvalidOperationException("The campaign's season was not found in the stored guide JSON.");

    GetOrCreateArray(season, "Campaigns").Add(CreateCampaignJson(id, request));
    SaveGuideRoot(connection, transaction, root, role);
    transaction.Commit();
}

static bool DeleteSeason(string dbPath, string id, string role)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    using var transaction = connection.BeginTransaction();
    if (!ExistsInTransaction(connection, transaction, "seasons", id))
        return false;

    using (var deleteEpisodes = connection.CreateCommand())
    {
        deleteEpisodes.Transaction = transaction;
        deleteEpisodes.CommandText = @"DELETE FROM episodes WHERE campaign_id IN
            (SELECT id FROM campaigns WHERE season_id = @id);";
        deleteEpisodes.Parameters.AddWithValue("@id", id);
        deleteEpisodes.ExecuteNonQuery();
    }
    using (var deleteCampaigns = connection.CreateCommand())
    {
        deleteCampaigns.Transaction = transaction;
        deleteCampaigns.CommandText = "DELETE FROM campaigns WHERE season_id = @id;";
        deleteCampaigns.Parameters.AddWithValue("@id", id);
        deleteCampaigns.ExecuteNonQuery();
    }
    using (var deleteSeason = connection.CreateCommand())
    {
        deleteSeason.Transaction = transaction;
        deleteSeason.CommandText = "DELETE FROM seasons WHERE id = @id;";
        deleteSeason.Parameters.AddWithValue("@id", id);
        deleteSeason.ExecuteNonQuery();
    }

    var root = LoadGuideRoot(connection, transaction);
    var seasons = root is null ? null : FindArray(root, "Seasons");
    var removed = seasons?.OfType<JsonObject>()
        .FirstOrDefault(item => string.Equals(GetNodeString(item, "Id"), id, StringComparison.OrdinalIgnoreCase));
    if (removed != null)
    {
        seasons!.Remove(removed);
        SaveGuideRoot(connection, transaction, root!, role);
    }

    transaction.Commit();
    return true;
}

static bool DeleteCampaign(string dbPath, string id, string role)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    using var transaction = connection.BeginTransaction();
    if (!ExistsInTransaction(connection, transaction, "campaigns", id))
        return false;

    using (var deleteEpisodes = connection.CreateCommand())
    {
        deleteEpisodes.Transaction = transaction;
        deleteEpisodes.CommandText = "DELETE FROM episodes WHERE campaign_id = @id;";
        deleteEpisodes.Parameters.AddWithValue("@id", id);
        deleteEpisodes.ExecuteNonQuery();
    }
    using (var deleteCampaign = connection.CreateCommand())
    {
        deleteCampaign.Transaction = transaction;
        deleteCampaign.CommandText = "DELETE FROM campaigns WHERE id = @id;";
        deleteCampaign.Parameters.AddWithValue("@id", id);
        deleteCampaign.ExecuteNonQuery();
    }

    var root = LoadGuideRoot(connection, transaction);
    var removed = false;
    var seasons = root is null ? null : FindArray(root, "Seasons");
    foreach (var season in seasons?.OfType<JsonObject>() ?? [])
    {
        var campaigns = FindArray(season, "Campaigns");
        var campaign = campaigns?.OfType<JsonObject>()
            .FirstOrDefault(item => string.Equals(GetNodeString(item, "Id"), id, StringComparison.OrdinalIgnoreCase));
        if (campaign == null)
            continue;

        campaigns!.Remove(campaign);
        removed = true;
        break;
    }
    if (removed)
        SaveGuideRoot(connection, transaction, root!, role);

    transaction.Commit();
    return true;
}

static bool ExistsInTransaction(SqliteConnection connection, SqliteTransaction transaction, string tableName, string id)
{
    using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = $"SELECT EXISTS(SELECT 1 FROM {tableName} WHERE id = @id);";
    command.Parameters.AddWithValue("@id", id);
    return Convert.ToInt32(command.ExecuteScalar()) == 1;
}

static JsonObject? FindSeason(JsonObject root, string id) => FindArray(root, "Seasons")?.OfType<JsonObject>()
    .FirstOrDefault(item => string.Equals(GetNodeString(item, "Id"), id, StringComparison.OrdinalIgnoreCase));

static JsonArray GetOrCreateArray(JsonObject parent, string propertyName)
{
    var array = FindArray(parent, propertyName);
    if (array != null)
        return array;

    array = [];
    parent[FindPropertyName(parent, propertyName) ?? propertyName] = array;
    return array;
}

static JsonObject CreateCampaignJson(string id, CampaignWriteRequest request) => new()
{
    ["Id"] = id,
    ["SeasonId"] = request.SeasonId,
    ["Number"] = request.Number,
    ["Title"] = request.Name.Trim(),
    ["DungeonMaster"] = request.DungeonMaster,
    ["Setting"] = request.Setting,
    ["Description"] = request.Description,
    ["CoverImage"] = request.CoverImage,
    ["FilmingStart"] = JsonSerializer.SerializeToNode(request.FilmingStart),
    ["FilmingEnd"] = JsonSerializer.SerializeToNode(request.FilmingEnd),
    ["FilmingLocation"] = request.FilmingLocation,
    ["Characters"] = JsonSerializer.SerializeToNode(request.Characters),
    ["Episodes"] = new JsonArray()
};

static void UpdateSeason(string dbPath, string id, SeasonWriteRequest request, string role)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    using var transaction = connection.BeginTransaction();

    using (var command = connection.CreateCommand())
    {
        command.Transaction = transaction;
        command.CommandText = @"UPDATE seasons SET number = @number, title = @title, year = @year,
            description = @description, updated_at = @updatedAt WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@number", request.Number);
        command.Parameters.AddWithValue("@title", request.Name.Trim());
        command.Parameters.AddWithValue("@year", (object?)request.Year ?? DBNull.Value);
        command.Parameters.AddWithValue("@description", (object?)request.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    var root = LoadGuideRoot(connection, transaction);
    if (root != null)
    {
        var seasons = FindArray(root, "Seasons");
        var season = seasons?.OfType<JsonObject>()
            .FirstOrDefault(item => string.Equals(GetNodeString(item, "Id"), id, StringComparison.OrdinalIgnoreCase));
        if (season == null)
            throw new InvalidOperationException("The season was not found in the stored guide JSON.");

        SetJsonValue(season, "Number", JsonValue.Create(request.Number));
        SetJsonValue(season, "Title", JsonValue.Create(request.Name.Trim()));
        SetJsonValue(season, "Year", JsonValue.Create(request.Year));
        SetJsonValue(season, "Description", JsonValue.Create(request.Description));
        SaveGuideRoot(connection, transaction, root, role);
    }

    transaction.Commit();
}

static void UpdateCampaign(string dbPath, string id, CampaignWriteRequest request, string role)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    using var transaction = connection.BeginTransaction();

    using (var command = connection.CreateCommand())
    {
        command.Transaction = transaction;
        command.CommandText = @"UPDATE campaigns SET season_id = @seasonId, number = @number, title = @title,
            dungeon_master = @dungeonMaster, setting = @setting, description = @description,
            cover_image = @coverImage, filming_start = @filmingStart, filming_end = @filmingEnd,
            filming_location = @filmingLocation, characters = @characters, updated_at = @updatedAt
            WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@seasonId", request.SeasonId);
        command.Parameters.AddWithValue("@number", request.Number);
        command.Parameters.AddWithValue("@title", request.Name.Trim());
        command.Parameters.AddWithValue("@dungeonMaster", (object?)request.DungeonMaster ?? DBNull.Value);
        command.Parameters.AddWithValue("@setting", (object?)request.Setting ?? DBNull.Value);
        command.Parameters.AddWithValue("@description", (object?)request.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("@coverImage", (object?)request.CoverImage ?? DBNull.Value);
        command.Parameters.AddWithValue("@filmingStart", request.FilmingStart?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@filmingEnd", request.FilmingEnd?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@filmingLocation", (object?)request.FilmingLocation ?? DBNull.Value);
        command.Parameters.AddWithValue("@characters", JsonSerializer.Serialize(request.Characters));
        command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    var root = LoadGuideRoot(connection, transaction);
    if (root != null)
    {
        var seasons = FindArray(root, "Seasons");
        JsonObject? originalCampaign = null;
        JsonArray? originalCampaigns = null;
        foreach (var season in seasons?.OfType<JsonObject>() ?? [])
        {
            var campaigns = FindArray(season, "Campaigns");
            var match = campaigns?.OfType<JsonObject>()
                .FirstOrDefault(item => string.Equals(GetNodeString(item, "Id"), id, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                originalCampaign = match;
                originalCampaigns = campaigns;
                break;
            }
        }

        var targetSeason = seasons?.OfType<JsonObject>()
            .FirstOrDefault(item => string.Equals(GetNodeString(item, "Id"), request.SeasonId, StringComparison.OrdinalIgnoreCase));
        if (originalCampaign == null || originalCampaigns == null || targetSeason == null)
            throw new InvalidOperationException("The campaign or its season was not found in the stored guide JSON.");

        originalCampaigns.Remove(originalCampaign);
        SetJsonValue(originalCampaign, "SeasonId", JsonValue.Create(request.SeasonId));
        SetJsonValue(originalCampaign, "Number", JsonValue.Create(request.Number));
        SetJsonValue(originalCampaign, "Title", JsonValue.Create(request.Name.Trim()));
        SetJsonValue(originalCampaign, "DungeonMaster", JsonValue.Create(request.DungeonMaster));
        SetJsonValue(originalCampaign, "Setting", JsonValue.Create(request.Setting));
        SetJsonValue(originalCampaign, "Description", JsonValue.Create(request.Description));
        SetJsonValue(originalCampaign, "CoverImage", JsonValue.Create(request.CoverImage));
        SetJsonValue(originalCampaign, "FilmingStart", JsonValue.Create(request.FilmingStart));
        SetJsonValue(originalCampaign, "FilmingEnd", JsonValue.Create(request.FilmingEnd));
        SetJsonValue(originalCampaign, "FilmingLocation", JsonValue.Create(request.FilmingLocation));
        SetJsonValue(originalCampaign, "Characters", JsonSerializer.SerializeToNode(request.Characters));

        var targetCampaigns = FindArray(targetSeason, "Campaigns");
        if (targetCampaigns == null)
        {
            targetCampaigns = [];
            targetSeason[FindPropertyName(targetSeason, "Campaigns") ?? "Campaigns"] = targetCampaigns;
        }
        targetCampaigns.Add(originalCampaign);
        SaveGuideRoot(connection, transaction, root, role);
    }

    transaction.Commit();
}

static JsonObject? LoadGuideRoot(SqliteConnection connection, SqliteTransaction transaction)
{
    using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = "SELECT guide_json FROM guide_store WHERE id = 1;";
    var json = command.ExecuteScalar() as string;
    return string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json) as JsonObject;
}

static void SaveGuideRoot(SqliteConnection connection, SqliteTransaction transaction, JsonObject root, string role)
{
    using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = @"INSERT INTO guide_store (id, guide_json, updated_at, version, updated_by)
        VALUES (1, @json, @updatedAt, 1, @updatedBy)
        ON CONFLICT(id) DO UPDATE SET
            guide_json = excluded.guide_json,
            updated_at = excluded.updated_at,
            version = guide_store.version + 1,
            updated_by = excluded.updated_by;";
    command.Parameters.AddWithValue("@json", root.ToJsonString());
    command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
    command.Parameters.AddWithValue("@updatedBy", role);
    command.ExecuteNonQuery();
}

static void SetJsonValue(JsonObject parent, string propertyName, JsonNode? value)
{
    parent[FindPropertyName(parent, propertyName) ?? propertyName] = value;
}

static void SaveEpisode(string dbPath, string id, EpisodeWriteRequest request, string role, bool insert)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    using var transaction = connection.BeginTransaction();

    using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = insert
        ? @"INSERT INTO episodes (
                id, campaign_id, number, title, dungeon_master, date_filmed, filming_location,
                runtime, synopsis, production_notes, air_dates, cast_ids, guest_cast, key_moments, updated_at)
            VALUES (
                @id, @campaignId, @number, @title, @dungeonMaster, @dateFilmed, @filmingLocation,
                @runtime, @synopsis, @productionNotes, @airDates, @castIds, @guestCast, @keyMoments, @updatedAt);"
        : @"UPDATE episodes SET campaign_id = @campaignId, number = @number, title = @title,
            dungeon_master = @dungeonMaster, date_filmed = @dateFilmed, filming_location = @filmingLocation,
            runtime = @runtime, synopsis = @synopsis, production_notes = @productionNotes,
            air_dates = @airDates, cast_ids = @castIds, guest_cast = @guestCast, key_moments = @keyMoments,
            updated_at = @updatedAt
            WHERE id = @id;";
    command.Parameters.AddWithValue("@id", id);
    command.Parameters.AddWithValue("@campaignId", request.CampaignId);
    command.Parameters.AddWithValue("@number", request.Number);
    command.Parameters.AddWithValue("@title", request.Name.Trim());
    command.Parameters.AddWithValue("@dungeonMaster", (object?)request.DungeonMaster ?? DBNull.Value);
    command.Parameters.AddWithValue("@dateFilmed", request.DateFilmed?.ToString("O") ?? (object)DBNull.Value);
    command.Parameters.AddWithValue("@filmingLocation", (object?)request.FilmingLocation ?? DBNull.Value);
    command.Parameters.AddWithValue("@runtime", (object?)request.Runtime ?? DBNull.Value);
    command.Parameters.AddWithValue("@synopsis", (object?)request.Summary ?? DBNull.Value);
    command.Parameters.AddWithValue("@productionNotes", (object?)request.ProductionNotes ?? DBNull.Value);
    command.Parameters.AddWithValue("@airDates", JsonSerializer.Serialize(request.AirDates));
    command.Parameters.AddWithValue("@castIds", JsonSerializer.Serialize(request.CastIds));
    command.Parameters.AddWithValue("@guestCast", JsonSerializer.Serialize(request.GuestCast));
    command.Parameters.AddWithValue("@keyMoments", JsonSerializer.Serialize(request.KeyMoments));
    command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
    command.ExecuteNonQuery();

    SyncEpisodeGuideJson(connection, transaction, id, request, delete: false, role);
    transaction.Commit();
}

static void SyncEpisodeGuideJson(
    SqliteConnection connection,
    SqliteTransaction transaction,
    string id,
    EpisodeWriteRequest? request,
    bool delete,
    string role)
{
    string? json;
    using (var read = connection.CreateCommand())
    {
        read.Transaction = transaction;
        read.CommandText = "SELECT guide_json FROM guide_store WHERE id = 1;";
        json = read.ExecuteScalar() as string;
    }

    if (string.IsNullOrWhiteSpace(json))
        return;

    if (JsonNode.Parse(json) is not JsonObject root || FindArray(root, "Seasons") is not { } seasons)
        throw new InvalidOperationException("The stored guide JSON is not a valid guide document.");

    JsonObject? targetCampaign = null;
    var foundExisting = false;
    foreach (var season in seasons.OfType<JsonObject>())
    {
        if (FindArray(season, "Campaigns") is not { } campaigns)
            continue;

        foreach (var campaign in campaigns.OfType<JsonObject>())
        {
            if (!delete && string.Equals(
                    GetNodeString(campaign, "Id"), request!.CampaignId, StringComparison.OrdinalIgnoreCase))
                targetCampaign = campaign;

            if (FindArray(campaign, "Episodes") is not { } campaignEpisodes)
                continue;

            for (var index = campaignEpisodes.Count - 1; index >= 0; index--)
            {
                if (campaignEpisodes[index] is JsonObject episode &&
                    string.Equals(GetNodeString(episode, "Id"), id, StringComparison.OrdinalIgnoreCase))
                {
                    campaignEpisodes.RemoveAt(index);
                    foundExisting = true;
                }
            }
        }
    }

    if (delete)
    {
        if (!foundExisting)
            return;
    }
    else
    {
        if (targetCampaign == null)
            throw new InvalidOperationException("The episode's campaign was not found in the stored guide JSON.");

        var episodeArray = FindArray(targetCampaign, "Episodes");
        if (episodeArray == null)
        {
            episodeArray = [];
            targetCampaign[FindPropertyName(targetCampaign, "Episodes") ?? "Episodes"] = episodeArray;
        }

        var episodeNode = JsonSerializer.SerializeToNode(new
        {
            Id = id,
            CampaignId = request!.CampaignId,
            Number = request.Number,
            Title = request.Name,
            DungeonMaster = request.DungeonMaster,
            DateFilmed = request.DateFilmed,
            FilmingLocation = request.FilmingLocation,
            Runtime = request.Runtime,
            Synopsis = request.Summary,
            ProductionNotes = request.ProductionNotes,
            CastIds = request.CastIds,
            GuestCast = request.GuestCast,
            KeyMoments = request.KeyMoments,
            AirDates = request.AirDates
        });
        episodeArray.Add(episodeNode);
    }

    using var update = connection.CreateCommand();
    update.Transaction = transaction;
    update.CommandText = @"UPDATE guide_store
        SET guide_json = @json, updated_at = @updatedAt, version = version + 1, updated_by = @updatedBy
        WHERE id = 1;";
    update.Parameters.AddWithValue("@json", root.ToJsonString());
    update.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
    update.Parameters.AddWithValue("@updatedBy", role);
    update.ExecuteNonQuery();
}

static JsonArray? FindArray(JsonObject parent, string propertyName)
{
    foreach (var property in parent)
        if (string.Equals(property.Key, propertyName, StringComparison.OrdinalIgnoreCase))
            return property.Value as JsonArray;
    return null;
}

static string? FindPropertyName(JsonObject parent, string propertyName)
{
    foreach (var property in parent)
        if (string.Equals(property.Key, propertyName, StringComparison.OrdinalIgnoreCase))
            return property.Key;
    return null;
}

static string? GetNodeString(JsonObject parent, string propertyName)
{
    foreach (var property in parent)
        if (string.Equals(property.Key, propertyName, StringComparison.OrdinalIgnoreCase))
            return property.Value?.GetValue<string>();
    return null;
}

static GuideRecord SaveGuideJson(string dbPath, string json, string role)
{
    var root = JsonDocument.Parse(json).RootElement;
    SaveGuideStructure(dbPath, root);

    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();

    var updatedAt = DateTime.UtcNow;
    var version = 1;
    using (var existing = connection.CreateCommand())
    {
        existing.CommandText = "SELECT version FROM guide_store WHERE id = 1;";
        var currentVersion = existing.ExecuteScalar();
        if (currentVersion != null && currentVersion is long l)
            version = (int)l + 1;
    }

    using var command = connection.CreateCommand();
    command.CommandText = @"
        INSERT INTO guide_store (id, guide_json, updated_at, version, updated_by)
        VALUES (1, @json, @updatedAt, @version, @updatedBy)
        ON CONFLICT(id) DO UPDATE SET
            guide_json = excluded.guide_json,
            updated_at = excluded.updated_at,
            version = excluded.version,
            updated_by = excluded.updated_by;
    ";
    command.Parameters.AddWithValue("@json", json);
    command.Parameters.AddWithValue("@updatedAt", updatedAt.ToString("O"));
    command.Parameters.AddWithValue("@version", version);
    command.Parameters.AddWithValue("@updatedBy", role);
    command.ExecuteNonQuery();

    return new GuideRecord(json, updatedAt, version);
}

static void SaveGuideStructure(string dbPath, JsonElement root)
{
    var seasons = GetArrayProperty(root, "Seasons");

    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    using var transaction = connection.BeginTransaction();

    using (var clear = connection.CreateCommand())
    {
        clear.Transaction = transaction;
        clear.CommandText = "DELETE FROM episodes; DELETE FROM campaigns; DELETE FROM seasons;";
        clear.ExecuteNonQuery();
    }

    if (seasons.HasValue)
    {
        foreach (var seasonElement in seasons.Value.EnumerateArray())
        {
            var seasonId = GetStringValue(seasonElement, "Id") ?? Guid.NewGuid().ToString();
            var seasonNumber = GetIntValue(seasonElement, "Number") ?? 0;
            var seasonTitle = GetStringValue(seasonElement, "Title") ?? $"Season {seasonNumber}";
            var seasonYear = GetStringValue(seasonElement, "Year");
            var seasonDescription = GetStringValue(seasonElement, "Description");

            using (var insertSeason = connection.CreateCommand())
            {
                insertSeason.Transaction = transaction;
                insertSeason.CommandText = @"
                    INSERT INTO seasons (id, number, title, year, description, updated_at)
                    VALUES (@id, @number, @title, @year, @description, @updatedAt);
                ";
                insertSeason.Parameters.AddWithValue("@id", seasonId);
                insertSeason.Parameters.AddWithValue("@number", seasonNumber);
                insertSeason.Parameters.AddWithValue("@title", seasonTitle);
                insertSeason.Parameters.AddWithValue("@year", seasonYear ?? (object)DBNull.Value);
                insertSeason.Parameters.AddWithValue("@description", seasonDescription ?? (object)DBNull.Value);
                insertSeason.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
                insertSeason.ExecuteNonQuery();
            }

            var campaigns = GetArrayProperty(seasonElement, "Campaigns");
            if (!campaigns.HasValue) continue;

            foreach (var campaignElement in campaigns.Value.EnumerateArray())
            {
                var campaignId = GetStringValue(campaignElement, "Id") ?? Guid.NewGuid().ToString();
                var campaignNumber = GetIntValue(campaignElement, "Number") ?? 0;
                var campaignTitle = GetStringValue(campaignElement, "Title") ?? $"Campaign {campaignNumber}";
                var dungeonMaster = GetStringValue(campaignElement, "DungeonMaster");
                var setting = GetStringValue(campaignElement, "Setting");
                var description = GetStringValue(campaignElement, "Description");
                var coverImage = GetStringValue(campaignElement, "CoverImage");
                var filmingStart = GetDateTimeString(campaignElement, "FilmingStart");
                var filmingEnd = GetDateTimeString(campaignElement, "FilmingEnd");
                var filmingLocation = GetStringValue(campaignElement, "FilmingLocation");
                var characters = GetArrayProperty(campaignElement, "Characters");

                using (var insertCampaign = connection.CreateCommand())
                {
                    insertCampaign.Transaction = transaction;
                    insertCampaign.CommandText = @"
                        INSERT INTO campaigns (
                            id, season_id, number, title, dungeon_master, setting, description,
                            cover_image, filming_start, filming_end, filming_location, characters, updated_at)
                        VALUES (
                            @id, @seasonId, @number, @title, @dungeonMaster, @setting, @description,
                            @coverImage, @filmingStart, @filmingEnd, @filmingLocation, @characters, @updatedAt);
                    ";
                    insertCampaign.Parameters.AddWithValue("@id", campaignId);
                    insertCampaign.Parameters.AddWithValue("@seasonId", seasonId);
                    insertCampaign.Parameters.AddWithValue("@number", campaignNumber);
                    insertCampaign.Parameters.AddWithValue("@title", campaignTitle);
                    insertCampaign.Parameters.AddWithValue("@dungeonMaster", dungeonMaster ?? (object)DBNull.Value);
                    insertCampaign.Parameters.AddWithValue("@setting", setting ?? (object)DBNull.Value);
                    insertCampaign.Parameters.AddWithValue("@description", description ?? (object)DBNull.Value);
                    insertCampaign.Parameters.AddWithValue("@coverImage", coverImage ?? (object)DBNull.Value);
                    insertCampaign.Parameters.AddWithValue("@filmingStart", filmingStart ?? (object)DBNull.Value);
                    insertCampaign.Parameters.AddWithValue("@filmingEnd", filmingEnd ?? (object)DBNull.Value);
                    insertCampaign.Parameters.AddWithValue("@filmingLocation", filmingLocation ?? (object)DBNull.Value);
                    insertCampaign.Parameters.AddWithValue("@characters", characters.HasValue ? characters.Value.GetRawText() : (object)DBNull.Value);
                    insertCampaign.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
                    insertCampaign.ExecuteNonQuery();
                }

                var episodes = GetArrayProperty(campaignElement, "Episodes");
                if (!episodes.HasValue) continue;

                foreach (var episodeElement in episodes.Value.EnumerateArray())
                {
                    var episodeId = GetStringValue(episodeElement, "Id") ?? Guid.NewGuid().ToString();
                    var episodeNumber = GetIntValue(episodeElement, "Number") ?? 0;
                    var episodeTitle = GetStringValue(episodeElement, "Title") ?? $"Episode {episodeNumber}";
                    var episodeDungeonMaster = GetStringValue(episodeElement, "DungeonMaster");
                    var dateFilmed = GetDateTimeString(episodeElement, "DateFilmed");
                    var episodeFilmingLocation = GetStringValue(episodeElement, "FilmingLocation");
                    var runtime = GetStringValue(episodeElement, "Runtime");
                    var synopsis = GetStringValue(episodeElement, "Synopsis");
                    var productionNotes = GetStringValue(episodeElement, "ProductionNotes");
                    var airDates = GetArrayProperty(episodeElement, "AirDates");
                    var castIds = GetArrayProperty(episodeElement, "CastIds");
                    var guestCast = GetArrayProperty(episodeElement, "GuestCast");
                    var keyMoments = GetArrayProperty(episodeElement, "KeyMoments");

                    using (var insertEpisode = connection.CreateCommand())
                    {
                        insertEpisode.Transaction = transaction;
                        insertEpisode.CommandText = @"
                            INSERT INTO episodes (
                                id, campaign_id, number, title, dungeon_master, date_filmed,
                                filming_location, runtime, synopsis, production_notes, air_dates, cast_ids,
                                guest_cast, key_moments, updated_at)
                            VALUES (
                                @id, @campaignId, @number, @title, @dungeonMaster, @dateFilmed,
                                @filmingLocation, @runtime, @synopsis, @productionNotes, @airDates, @castIds,
                                @guestCast, @keyMoments, @updatedAt);
                        ";
                        insertEpisode.Parameters.AddWithValue("@id", episodeId);
                        insertEpisode.Parameters.AddWithValue("@campaignId", campaignId);
                        insertEpisode.Parameters.AddWithValue("@number", episodeNumber);
                        insertEpisode.Parameters.AddWithValue("@title", episodeTitle);
                        insertEpisode.Parameters.AddWithValue("@dungeonMaster", episodeDungeonMaster ?? (object)DBNull.Value);
                        insertEpisode.Parameters.AddWithValue("@dateFilmed", dateFilmed ?? (object)DBNull.Value);
                        insertEpisode.Parameters.AddWithValue("@filmingLocation", episodeFilmingLocation ?? (object)DBNull.Value);
                        insertEpisode.Parameters.AddWithValue("@runtime", runtime ?? (object)DBNull.Value);
                        insertEpisode.Parameters.AddWithValue("@synopsis", synopsis ?? (object)DBNull.Value);
                        insertEpisode.Parameters.AddWithValue("@productionNotes", productionNotes ?? (object)DBNull.Value);
                        insertEpisode.Parameters.AddWithValue("@airDates", airDates.HasValue ? airDates.Value.GetRawText() : (object)DBNull.Value);
                        insertEpisode.Parameters.AddWithValue("@castIds", castIds.HasValue ? castIds.Value.GetRawText() : (object)DBNull.Value);
                        insertEpisode.Parameters.AddWithValue("@guestCast", guestCast.HasValue ? guestCast.Value.GetRawText() : (object)DBNull.Value);
                        insertEpisode.Parameters.AddWithValue("@keyMoments", keyMoments.HasValue ? keyMoments.Value.GetRawText() : (object)DBNull.Value);
                        insertEpisode.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
                        insertEpisode.ExecuteNonQuery();
                    }
                }
            }
        }
    }

    transaction.Commit();
}

static JsonElement? GetArrayProperty(JsonElement parent, string name)
{
    foreach (var property in parent.EnumerateObject())
    {
        if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.Array)
            return property.Value;
    }

    return null;
}

static string? GetStringValue(JsonElement element, string name)
{
    foreach (var property in element.EnumerateObject())
    {
        if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
    }

    return null;
}

static int? GetIntValue(JsonElement element, string name)
{
    foreach (var property in element.EnumerateObject())
    {
        if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var value))
                return value;
            return int.TryParse(property.Value.ToString(), out var parsed) ? parsed : null;
        }
    }

    return null;
}

static string? GetDateTimeString(JsonElement element, string name)
{
    foreach (var property in element.EnumerateObject())
    {
        if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
            return property.Value.GetString();
    }

    return null;
}

record GuideRecord(string Json, DateTime UpdatedAt, int Version);
record GuideSeasonRecord(string Id, int Number, string Name, string? Year, string? Description, DateTime UpdatedAt);
record GuideCampaignRecord(
    string Id,
    string SeasonId,
    int Number,
    string Name,
    string? DungeonMaster,
    string? Setting,
    string? Description,
    string? CoverImage,
    DateTime? FilmingStart,
    DateTime? FilmingEnd,
    string? FilmingLocation,
    List<EpisodeCastMemberRecord> Characters,
    DateTime UpdatedAt);
sealed record SeasonWriteRequest
{
    public string? Id { get; init; }
    public int Number { get; init; }
    public required string Name { get; init; }
    public string? Year { get; init; }
    public string? Description { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
sealed record CampaignWriteRequest
{
    public string? Id { get; init; }
    public required string SeasonId { get; init; }
    public int Number { get; init; }
    public required string Name { get; init; }
    public string? DungeonMaster { get; init; }
    public string? Setting { get; init; }
    public string? Description { get; init; }
    public string? CoverImage { get; init; }
    public DateTime? FilmingStart { get; init; }
    public DateTime? FilmingEnd { get; init; }
    public string? FilmingLocation { get; init; }
    public List<EpisodeCastMemberRecord> Characters { get; init; } = [];
    public DateTime? UpdatedAt { get; init; }
}
record GuideEpisodeRecord(
    string Id,
    string CampaignId,
    int Number,
    string Name,
    string? DungeonMaster,
    DateTime? DateFilmed,
    string? FilmingLocation,
    string? Runtime,
    string? Summary,
    string? ProductionNotes,
    List<EpisodeAirDateRecord> AirDates,
    List<Guid> CastIds,
    List<EpisodeCastMemberRecord> GuestCast,
    List<EpisodeKeyMomentRecord> KeyMoments,
    DateTime UpdatedAt);

sealed record EpisodeWriteRequest
{
    public string? Id { get; init; }
    public required string CampaignId { get; init; }
    public int Number { get; init; }
    public required string Name { get; init; }
    public string? DungeonMaster { get; init; }
    public DateTime? DateFilmed { get; init; }
    public string? FilmingLocation { get; init; }
    public string? Runtime { get; init; }
    public string? Summary { get; init; }
    public string? ProductionNotes { get; init; }
    public List<EpisodeAirDateRecord> AirDates { get; init; } = [];
    public List<Guid> CastIds { get; init; } = [];
    public List<EpisodeCastMemberRecord> GuestCast { get; init; } = [];
    public List<EpisodeKeyMomentRecord> KeyMoments { get; init; } = [];
    public DateTime? UpdatedAt { get; init; }
}

record EpisodeAirDateRecord(DateTime? Date, string? Platform, string? Notes);
record EpisodeCastMemberRecord(Guid Id, string? PlayerName, string? CharacterName, string? CharacterSummary, string? CharacterDescription, string? ImagePath);
record EpisodeKeyMomentRecord(string? Timestamp, string? Description);
