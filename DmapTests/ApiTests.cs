using DmapApi;
using DmapShared.Api;
using DmapShared.Models;
using DmapShared.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using System.Net;
using System.Text.Json;

namespace DmapTests;

public class ApiTests
{
    private static readonly Guid SeasonId = Guid.NewGuid();
    private static string Json(object record) => GuideRecords.Serialize(record);

    private static Task<SaveOutcome> PutSeason(DmapApiClient client, long baseVersion, string title = "First") =>
        client.PutRecordAsync(GuideRecords.Season, SeasonId.ToString(), null, baseVersion,
            Json(new Season { Id = SeasonId, Number = 1, Title = title }));

    [Fact]
    public async Task Requests_without_an_accepted_key_are_refused()
    {
        using var server = new TestServer();
        using var raw = server.RawClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await raw.GetAsync("/api/dmap/guide")).StatusCode);

        raw.DefaultRequestHeaders.Add(ApiHeaders.ApiKey, "not-the-key-at-all-000");
        Assert.Equal(HttpStatusCode.Unauthorized, (await raw.GetAsync("/api/dmap/guide")).StatusCode);
    }

    [Fact]
    public async Task A_viewer_key_cannot_change_the_guide_even_if_it_claims_a_role()
    {
        using var server = new TestServer();
        using var raw = server.RawClient();
        raw.DefaultRequestHeaders.Add(ApiHeaders.ApiKey, TestServer.ViewerKey);
        raw.DefaultRequestHeaders.Add("X-User-Role", "admin");

        var body = new StringContent(
            JsonSerializer.Serialize(new { baseVersion = 0, data = new { Number = 1 } }),
            System.Text.Encoding.UTF8, "application/json");
        var response = await raw.PutAsync($"/api/dmap/records/season/{Guid.NewGuid()}", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var viewer = server.Client(TestServer.ViewerKey);
        Assert.Equal("viewer", (await viewer.GetInfoAsync()).Role);
    }

    [Fact]
    public async Task Saves_must_name_the_version_they_were_edited_from()
    {
        using var server = new TestServer();
        using var client = server.Client();

        var created = await PutSeason(client, 0);
        Assert.False(created.IsConflict);
        Assert.Equal(1, created.Record!.Version);
        Assert.Equal("Alex", created.Record.UpdatedBy);

        var updated = await PutSeason(client, 1, "Second");
        Assert.Equal(2, updated.Record!.Version);

        // Someone still editing version 1 is told about version 2, and given it.
        var stale = await PutSeason(client, 1, "Stale");
        Assert.True(stale.IsConflict);
        Assert.Equal(2, stale.Record!.Version);
        Assert.Contains("Second", stale.Record.Data.GetRawText());
    }

    [Fact]
    public async Task Saving_a_page_someone_deleted_reports_it_gone()
    {
        using var server = new TestServer();
        using var client = server.Client();
        await PutSeason(client, 0);
        await client.DeleteRecordAsync(GuideRecords.Season, SeasonId.ToString());

        Assert.True((await PutSeason(client, 1)).IsGone);
        Assert.False((await PutSeason(client, 0)).IsConflict); // recreating it is allowed
    }

    [Fact]
    public async Task A_campaign_needs_an_existing_season()
    {
        using var server = new TestServer();
        using var client = server.Client();
        var campaign = new Campaign { Number = 1, Title = "Orphan" };

        var ex = await Assert.ThrowsAsync<DmapApiException>(() =>
            client.PutRecordAsync(GuideRecords.Campaign, campaign.Id.ToString(), Guid.NewGuid().ToString(), 0, Json(campaign)));
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("no longer exists", ex.Message);
    }

    [Fact]
    public async Task Deleting_a_season_deletes_its_campaigns_and_episodes_and_reports_them()
    {
        using var server = new TestServer();
        using var client = server.Client();
        await PutSeason(client, 0);
        var campaign = new Campaign { Number = 1 };
        var episode = new Episode { Number = 101 };
        await client.PutRecordAsync(GuideRecords.Campaign, campaign.Id.ToString(), SeasonId.ToString(), 0, Json(campaign));
        await client.PutRecordAsync(GuideRecords.Episode, episode.Id.ToString(), campaign.Id.ToString(), 0, Json(episode));
        long before = (await client.GetGuideAsync()).Version;

        await client.DeleteRecordAsync(GuideRecords.Season, SeasonId.ToString());

        Assert.Empty((await client.GetGuideAsync()).Records);
        var changes = await client.GetChangesAsync(before);
        Assert.Equal(3, changes.Deleted.Count);
        Assert.Contains(changes.Deleted, d => d.Id == episode.Id.ToString() && d.Kind == GuideRecords.Episode);
    }

    [Fact]
    public async Task Changes_lists_only_what_changed_since()
    {
        using var server = new TestServer();
        using var client = server.Client();
        await PutSeason(client, 0);
        long version = (await client.GetGuideAsync()).Version;

        Assert.Empty((await client.GetChangesAsync(version)).Records);
        await PutSeason(client, 1, "Renamed");
        var changes = await client.GetChangesAsync(version);
        Assert.Single(changes.Records);
        Assert.True(changes.Version > version);
    }

    [Fact]
    public async Task Pictures_can_be_uploaded_and_fetched()
    {
        using var server = new TestServer();
        using var client = server.Client();
        byte[] picture = { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };

        string stored = await client.UploadImageAsync(new MemoryStream(picture), ".png");
        Assert.StartsWith("images/", stored);

        string target = Path.Combine(TempFolder.Create("download"), "p.png");
        Assert.True(await client.DownloadImageAsync(Path.GetFileName(stored), target));
        Assert.Equal(picture, File.ReadAllBytes(target));

        await Assert.ThrowsAsync<DmapApiException>(() => client.UploadImageAsync(new MemoryStream(picture), ".exe"));
    }

    [Fact]
    public async Task Edit_locks_are_shown_to_others_and_released_only_by_their_holder()
    {
        using var server = new TestServer();
        using var client = server.Client(user: "Sam");
        string id = Guid.NewGuid().ToString();
        var session = Guid.NewGuid();

        await client.PutLockAsync(id, new EditLockMessage { User = "spoofed", Session = session, Since = DateTime.UtcNow });
        var held = await client.GetLockAsync(id);
        Assert.Equal("Sam", held!.User); // the name comes from the request, not the body

        await client.DeleteLockAsync(id, Guid.NewGuid());
        Assert.NotNull(await client.GetLockAsync(id));
        await client.DeleteLockAsync(id, session);
        Assert.Null(await client.GetLockAsync(id));
    }

    [Fact]
    public void Outside_development_the_server_refuses_to_start_without_keys()
    {
        var config = new ConfigurationBuilder().Build();
        Assert.Throws<InvalidOperationException>(() => ServerOptions.From(config, new FakeEnvironment("Production")));

        var dev = ServerOptions.From(config, new FakeEnvironment("Development"));
        Assert.Equal(("local-dev-key", "editor"), dev.Keys.Single());
    }

    [Fact]
    public async Task A_guide_from_the_first_server_version_is_brought_across()
    {
        string folder = TempFolder.Create("legacy");
        var season = Guid.NewGuid();
        var campaign = Guid.NewGuid();
        var castId = Guid.NewGuid();
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(folder, "dmap.db")};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $@"
                CREATE TABLE guide_store (id INTEGER PRIMARY KEY, guide_json TEXT NOT NULL, updated_at TEXT NOT NULL, version INTEGER NOT NULL, updated_by TEXT NOT NULL);
                CREATE TABLE seasons (id TEXT PRIMARY KEY, number INTEGER NOT NULL, title TEXT NOT NULL, year TEXT, description TEXT, updated_at TEXT NOT NULL);
                CREATE TABLE campaigns (id TEXT PRIMARY KEY, season_id TEXT NOT NULL, number INTEGER NOT NULL, title TEXT NOT NULL, dungeon_master TEXT,
                    setting TEXT, description TEXT, cover_image TEXT, filming_start TEXT, filming_end TEXT, filming_location TEXT, characters TEXT, updated_at TEXT NOT NULL);
                CREATE TABLE episodes (id TEXT PRIMARY KEY, campaign_id TEXT NOT NULL, number INTEGER NOT NULL, title TEXT NOT NULL, dungeon_master TEXT,
                    date_filmed TEXT, filming_location TEXT, runtime TEXT, synopsis TEXT, production_notes TEXT, air_dates TEXT, cast_ids TEXT,
                    guest_cast TEXT, key_moments TEXT, updated_at TEXT NOT NULL);
                INSERT INTO guide_store VALUES (1, '{{""ShowName"":""Old Show""}}', '2026-01-01', 3, 'editor');
                INSERT INTO seasons VALUES ('{season}', 1, 'Old season', '2026', '', '2026-01-01');
                INSERT INTO campaigns VALUES ('{campaign}', '{season}', 2, 'Old campaign', 'Jo', '', '', '', '2026-03-01 00:00:00', NULL, 'Studio',
                    '[{{""id"":""{castId}"",""playerName"":""Pat"",""characterName"":""Grim""}}]', '2026-01-01');
                INSERT INTO episodes VALUES ('{Guid.NewGuid()}', '{campaign}', 201, 'Old episode', 'Jo', NULL, '', '', 'It happened', '',
                    '[{{""date"":""2026-04-01T00:00:00"",""platform"":""TV""}}]', '[""{castId}""]', '[]', '[]', '2026-01-01');";
            command.ExecuteNonQuery();
        }

        using var server = new TestServer(folder);
        using var client = server.Client();
        var store = new ServerGuideStore(client, "Alex", TempFolder.Create("cache"));
        var data = (await store.LoadAsync()).Data;

        Assert.Equal("Old Show", data.ShowName);
        var c = Assert.Single(data.AllCampaigns);
        Assert.Equal("Old campaign", c.Title);
        Assert.Equal(new DateTime(2026, 3, 1), c.FilmingStart);
        Assert.Equal("Grim", Assert.Single(c.Characters).CharacterName);
        var e = Assert.Single(c.Episodes);
        Assert.Equal("It happened", e.Synopsis);
        Assert.Equal(castId, Assert.Single(e.CastIds));
        Assert.Equal("TV", Assert.Single(e.AirDates).Platform);
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "DmapApi";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
