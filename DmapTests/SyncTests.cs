using DmapShared.Models;
using DmapShared.Storage;

namespace DmapTests;

/// <summary>Two people with the same guide open, on the shared server and in a shared folder.</summary>
public class SyncTests
{
    private static Task<bool> Keep(bool mine) => Task.FromResult(mine);

    private static IGuideStore ServerStore(TestServer server, string user) =>
        new ServerGuideStore(server.Client(user: user), user, TempFolder.Create("cache"));

    public static IEnumerable<TheoryDataRow<string>> Kinds() => [new("server"), new("folder")];

    /// <summary>Two stores on one guide: alex's and sam's.</summary>
    private static (IGuideStore Alex, IGuideStore Sam, IDisposable Cleanup) Pair(string kind)
    {
        if (kind == "server")
        {
            var server = new TestServer();
            return (ServerStore(server, "Alex"), ServerStore(server, "Sam"), server);
        }
        string folder = TempFolder.Create("folder");
        return (new FolderGuideStore(folder, "Alex"), new FolderGuideStore(folder, "Sam"), new Cleanup(() => TempFolder.Delete(folder)));
    }

    [Theory, MemberData(nameof(Kinds))]
    public async Task A_new_guide_starts_with_the_sample_pages(string kind)
    {
        var (alex, _, cleanup) = Pair(kind);
        using var _c = cleanup;
        var data = (await alex.LoadAsync()).Data;
        Assert.Equal(3, data.AllCampaigns.Count());
        Assert.Equal(5, data.AllEpisodes.Count());
    }

    [Theory, MemberData(nameof(Kinds))]
    public async Task Changes_one_person_saves_appear_for_the_other(string kind)
    {
        var (alex, sam, cleanup) = Pair(kind);
        using var _c = cleanup;
        var alexData = (await alex.LoadAsync()).Data;
        var samData = (await sam.LoadAsync()).Data;

        var episode = alexData.AllEpisodes.First();
        episode.Title = "The Vault";
        var guest = new Episode { Number = 999, Title = "Brand new" };
        episode.Campaign!.Episodes.Add(guest);
        Assert.Equal(2, await alex.SaveAsync(alexData, (_, _) => Keep(true)));

        var result = await sam.SyncAsync(samData);
        Assert.True(result.Changed);
        var samEpisode = samData.AllEpisodes.Single(e => e.Id == episode.Id);
        Assert.Equal("The Vault", samEpisode.Title);
        Assert.Equal("Alex", samEpisode.LastEditedBy);
        Assert.Contains(samData.AllEpisodes, e => e.Id == guest.Id && e.Campaign!.Id == episode.Campaign.Id);
    }

    [Theory, MemberData(nameof(Kinds))]
    public async Task Saving_over_someone_elses_newer_save_asks_whose_to_keep(string kind)
    {
        var (alex, sam, cleanup) = Pair(kind);
        using var _c = cleanup;
        var alexData = (await alex.LoadAsync()).Data;
        var samData = (await sam.LoadAsync()).Data;
        var id = alexData.AllEpisodes.First().Id;

        alexData.AllEpisodes.First().Synopsis = "Alex's synopsis";
        await alex.SaveAsync(alexData, (_, _) => Keep(true));

        // Sam edited the same page without seeing Alex's save, and keeps theirs.
        samData.AllEpisodes.First().Synopsis = "Sam's synopsis";
        string? askedAbout = null;
        await sam.SaveAsync(samData, (_, who) => { askedAbout = who; return Keep(false); });

        Assert.Equal("Alex", askedAbout);
        Assert.Equal("Alex's synopsis", samData.AllEpisodes.First(e => e.Id == id).Synopsis);

        // Next time, Sam keeps theirs.
        samData.AllEpisodes.First().Synopsis = "Sam's synopsis again";
        await sam.SaveAsync(samData, (_, _) => Keep(true));
        await alex.SyncAsync(alexData);
        Assert.Equal("Sam's synopsis again", alexData.AllEpisodes.First(e => e.Id == id).Synopsis);
    }

    [Theory, MemberData(nameof(Kinds))]
    public async Task Unsaved_edits_are_not_overwritten_by_other_peoples_saves(string kind)
    {
        var (alex, sam, cleanup) = Pair(kind);
        using var _c = cleanup;
        var alexData = (await alex.LoadAsync()).Data;
        var samData = (await sam.LoadAsync()).Data;

        alexData.Seasons[0].Description = "Alex's words";
        await alex.SaveAsync(alexData, (_, _) => Keep(true));

        samData.Seasons[0].Description = "Sam is still typing";
        await sam.SyncAsync(samData);
        Assert.Equal("Sam is still typing", samData.Seasons[0].Description);
    }

    [Theory, MemberData(nameof(Kinds))]
    public async Task Pages_deleted_by_someone_else_disappear(string kind)
    {
        var (alex, sam, cleanup) = Pair(kind);
        using var _c = cleanup;
        var alexData = (await alex.LoadAsync()).Data;
        var samData = (await sam.LoadAsync()).Data;

        var campaign = alexData.AllCampaigns.Last();
        alexData.Seasons[0].Campaigns.Remove(campaign);
        await alex.SaveAsync(alexData, (_, _) => Keep(true));

        var result = await sam.SyncAsync(samData);
        Assert.DoesNotContain(samData.AllCampaigns, c => c.Id == campaign.Id);
        Assert.DoesNotContain(samData.AllEpisodes, e => e.CampaignId == campaign.Id);
        Assert.Contains(result.Removed, n => n.Id == campaign.Id);
    }

    [Fact]
    public async Task A_page_deleted_while_someone_edits_it_comes_back_when_they_save()
    {
        using var server = new TestServer();
        var alex = ServerStore(server, "Alex");
        var sam = ServerStore(server, "Sam");
        var alexData = (await alex.LoadAsync()).Data;
        var samData = (await sam.LoadAsync()).Data;

        var episode = alexData.AllEpisodes.First();
        episode.Campaign!.Episodes.Remove(episode);
        await alex.SaveAsync(alexData, (_, _) => Keep(true));

        var samEpisode = samData.AllEpisodes.First(e => e.Id == episode.Id);
        samEpisode.Synopsis = "Still writing this";
        await sam.SyncAsync(samData);
        Assert.Contains(samData.AllEpisodes, e => e.Id == episode.Id);

        await sam.SaveAsync(samData, (_, _) => Keep(true));
        await alex.SyncAsync(alexData);
        Assert.Equal("Still writing this", alexData.AllEpisodes.Single(e => e.Id == episode.Id).Synopsis);
    }

    [Fact]
    public async Task Pictures_added_by_one_person_are_downloaded_for_the_other()
    {
        using var server = new TestServer();
        var alex = ServerStore(server, "Alex");
        var sam = ServerStore(server, "Sam");
        await alex.LoadAsync();
        await sam.LoadAsync();

        string file = Path.Combine(TempFolder.Create("pic"), "portrait.jpg");
        File.WriteAllBytes(file, [1, 2, 3, 4]);
        string stored = await alex.ImportImageAsync(file);
        Assert.NotNull(alex.ResolveImage(stored));

        var arrived = new TaskCompletionSource();
        sam.ImagesArrived += () => arrived.TrySetResult();
        Assert.Null(sam.ResolveImage(stored)); // starts the download
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(sam.ResolveImage(stored)!));
    }

    [Theory, MemberData(nameof(Kinds))]
    public async Task Edit_locks_warn_other_people_but_not_yourself(string kind)
    {
        var (alex, sam, cleanup) = Pair(kind);
        using var _c = cleanup;
        await alex.LoadAsync();
        var page = Guid.NewGuid();

        await alex.WriteLockAsync(page, DateTime.UtcNow);
        var held = await sam.ReadLockAsync(page);
        Assert.NotNull(held);
        Assert.Equal("Alex", held!.User);
        // Both run in this one test process (one session), so this session's own lock isn't "someone else".
        Assert.False(held.IsSomeoneElse("Sam"));
        Assert.True((held with { Session = Guid.NewGuid() }).IsSomeoneElse("Sam"));

        await alex.ReleaseLockAsync(page);
        Assert.Null(await sam.ReadLockAsync(page));
    }

    private sealed class Cleanup(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
