using DmapShared.Models;
using DmapShared.ViewModels;

namespace DmapTests;

/// <summary>The behaviour both apps share, driven the way the pages drive it.</summary>
public class ViewModelTests
{
    [Fact]
    public async Task An_unreachable_server_shows_a_page_saying_so_instead_of_another_guide()
    {
        var platform = new FakePlatform();
        var folder = TempFolder.Create("local");
        var settings = new FakeSettings(new GuideSettings
        {
            UserName = "Alex",
            UseServer = true,
            ServerAddress = "http://127.0.0.1:9/", // nothing listens here
            AccessKey = "some-key-0000000000",
            DataFolder = folder
        });

        var vm = new MainViewModel(platform, settings);
        await vm.StartAsync();

        Assert.False(vm.IsConnected);
        Assert.IsType<GuideUnavailablePage>(vm.CurrentPage);
        Assert.Empty(vm.Data.Seasons);
        Assert.False(vm.CanEdit);
        Assert.False(Directory.EnumerateFileSystemEntries(folder).Any()); // the folder guide wasn't opened or written
    }

    [Fact]
    public async Task Delete_on_a_page_that_is_not_a_season_campaign_or_episode_deletes_nothing()
    {
        using var server = new TestServer();
        var (vm, platform) = await Connected(server);
        var episode = vm.Data.AllEpisodes.First();

        await vm.NavigateAsync(episode);
        await vm.ShowPrologueCommand.ExecuteAsync(null);
        await vm.DeleteCurrentCommand.ExecuteAsync(null);

        Assert.Contains(vm.Data.AllEpisodes, e => e.Id == episode.Id);
        Assert.Contains("Open the season, campaign or episode", platform.Alerts.Single());
    }

    [Fact]
    public async Task Edits_are_saved_to_the_page_they_were_made_on()
    {
        using var server = new TestServer();
        var (vm, _) = await Connected(server);
        var first = vm.Data.AllEpisodes.First();
        var second = vm.Data.AllEpisodes.Skip(1).First();

        await vm.NavigateAsync(first);
        await vm.ToggleEditCommand.ExecuteAsync(null);
        first.Title = "First's title";
        await vm.NavigateAsync(second); // still editing: the first page is saved on the way
        second.Title = "Second's title";
        await vm.ToggleEditCommand.ExecuteAsync(null);
        await vm.SaveAsync();

        var check = new DmapShared.Storage.ServerGuideStore(server.Client(), "Check", TempFolder.Create("c"));
        var saved = (await check.LoadAsync()).Data;
        Assert.Equal("First's title", saved.AllEpisodes.Single(e => e.Id == first.Id).Title);
        Assert.Equal("Second's title", saved.AllEpisodes.Single(e => e.Id == second.Id).Title);
    }

    [Fact]
    public async Task Leaving_for_a_non_page_ends_editing()
    {
        using var server = new TestServer();
        var (vm, _) = await Connected(server);

        await vm.NavigateAsync(vm.Data.AllEpisodes.First());
        await vm.ToggleEditCommand.ExecuteAsync(null);
        Assert.True(vm.IsEditing);
        await vm.ShowScheduleCommand.ExecuteAsync(null);
        Assert.False(vm.IsEditing);
        Assert.False(vm.CanEdit);
    }

    [Fact]
    public async Task A_failed_save_keeps_the_edit_and_says_so()
    {
        var server = new TestServer();
        var (vm, platform) = await Connected(server);
        var episode = vm.Data.AllEpisodes.First();
        await vm.NavigateAsync(episode);
        await vm.ToggleEditCommand.ExecuteAsync(null);
        episode.Synopsis = "Important notes";

        server.Dispose(); // the server goes away
        await vm.SaveAsync();

        Assert.Equal("Important notes", episode.Synopsis);
        Assert.Contains("Your changes are still here", platform.Alerts.Last());
    }

    [Fact]
    public async Task Someone_elses_save_is_picked_up_without_leaving_the_page()
    {
        using var server = new TestServer();
        var (vm, _) = await Connected(server);
        var (other, _) = await Connected(server, "Sam");
        var page = vm.Data.Seasons[0];
        await vm.NavigateAsync(page);

        other.Data.Seasons[0].Description = "Sam was here";
        await other.NavigateAsync(other.Data.Seasons[0]);
        await other.ToggleEditCommand.ExecuteAsync(null);
        await other.ToggleEditCommand.ExecuteAsync(null);
        await other.SaveAsync();

        await vm.SyncNowAsync();
        Assert.Same(page, vm.CurrentPage);
        Assert.Equal("Sam was here", page.Description);
    }

    private static async Task<(MainViewModel, FakePlatform)> Connected(TestServer server, string user = "Alex")
    {
        var platform = new FakePlatform();
        var vm = new MainViewModel(platform, new FakeSettings(new GuideSettings { UserName = user, UseServer = true, ServerAddress = "http://localhost/", AccessKey = TestServer.EditorKey }));
        vm.TestClient = server.Client(user: user);
        await vm.StartAsync();
        Assert.True(vm.IsConnected, string.Join("\n", platform.Alerts));
        return (vm, platform);
    }

    internal sealed class FakePlatform : IPlatformServices
    {
        public List<string> Alerts { get; } = new();
        public bool Answer { get; set; } = true;

        public bool SupportsFolders => true;
        public string CacheFolder { get; } = TempFolder.Create("platform-cache");

        public Task AlertAsync(string title, string message)
        {
            Alerts.Add(message);
            return Task.CompletedTask;
        }

        public Task<bool> AskAsync(string title, string message, string yes, string no) => Task.FromResult(Answer);
        public Task<string?> PickImageAsync() => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title, string? startIn) => Task.FromResult<string?>(null);
        public void BeginInvoke(Action action) => action();
        public void StartTimer(TimeSpan interval, Action tick) { }
    }

    internal sealed class FakeSettings(GuideSettings settings) : ISettingsStore
    {
        public GuideSettings Saved { get; private set; } = settings;
        public Task<GuideSettings> LoadAsync() => Task.FromResult(Saved.Clone());
        public Task SaveAsync(GuideSettings s)
        {
            Saved = s.Clone();
            return Task.CompletedTask;
        }
    }
}
