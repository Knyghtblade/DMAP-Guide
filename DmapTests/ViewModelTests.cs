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
    public async Task A_desktop_ignores_server_settings_and_opens_its_folder()
    {
        var platform = new FakePlatform { SupportsServer = false };
        var folder = TempFolder.Create("desk");
        var vm = new MainViewModel(platform, new FakeSettings(new GuideSettings
        {
            UserName = "Alex",
            UseServer = true, // left over from testing with a server and a key
            ServerAddress = "http://127.0.0.1:9/",
            AccessKey = "stale-key",
            DataFolder = folder
        }));

        await vm.StartAsync();

        Assert.True(vm.IsConnected);
        Assert.IsType<PrologueViewModel>(vm.CurrentPage);
        Assert.Equal(folder, vm.Location);
        Assert.True(vm.CanChangePages);
        Assert.Empty(platform.Alerts);
    }

    [Fact]
    public async Task The_shared_app_refuses_to_open_when_its_guide_folder_is_missing()
    {
        var platform = new FakePlatform { SupportsServer = false };
        var elsewhere = TempFolder.Create("private");
        var appFolder = TempFolder.Create("download"); // a stray copy of the .exe, with no "guide" beside it
        var settings = new FakeSettings(new GuideSettings { UserName = "Alex", DataFolder = elsewhere });

        var vm = new MainViewModel(platform, settings) { RequiredFolder = Path.Combine(appFolder, "guide") };
        await vm.StartAsync();

        Assert.False(vm.IsConnected);
        var page = Assert.IsType<GuideUnavailablePage>(vm.CurrentPage);
        Assert.False(page.CanOpenSettings);
        Assert.Contains(appFolder, page.Message);
        Assert.True(vm.FolderIsFixed);
        Assert.False(Directory.EnumerateFileSystemEntries(elsewhere).Any()); // no private guide was opened or written

        // The folder turns up (OneDrive finished syncing): "Try again" opens it, and only it.
        Directory.CreateDirectory(Path.Combine(appFolder, "guide"));
        await vm.ReconnectCommand.ExecuteAsync(null);

        Assert.True(vm.IsConnected);
        Assert.IsType<PrologueViewModel>(vm.CurrentPage);
        Assert.Equal(Path.Combine(appFolder, "guide"), vm.Location);
        Assert.False(Directory.EnumerateFileSystemEntries(elsewhere).Any());
    }

    [Fact]
    public async Task Closing_the_shared_app_after_changing_the_guide_updates_the_web_copy_beside_it()
    {
        var appFolder = TempFolder.Create("shared");
        string guide = Path.Combine(appFolder, "guide");
        Directory.CreateDirectory(guide);
        string webCopy = Path.Combine(appFolder, MainViewModel.SharedWebCopyName);
        MainViewModel Open() => new(new FakePlatform { SupportsServer = false, SupportsWebCopy = true },
            new FakeSettings(new GuideSettings { UserName = "Alex" })) { RequiredFolder = guide };

        // Opening and closing without changing anything leaves it alone.
        var reader = Open();
        await reader.StartAsync();
        await reader.ShutdownAsync();
        Assert.False(File.Exists(webCopy));

        // A change, then closing: the web copy is written beside the app, with the change in it.
        var editor = Open();
        await editor.StartAsync();
        await editor.AddSeasonCommand.ExecuteAsync(null);
        ((Season)editor.CurrentPage!).Title = "The Shadowfell";
        await editor.ToggleEditCommand.ExecuteAsync(null);
        await editor.ShutdownAsync();

        Assert.True(File.Exists(webCopy));
        Assert.Contains("The Shadowfell", File.ReadAllText(webCopy));
        Assert.False(File.Exists(webCopy + ".partial"));
    }

    [Fact]
    public async Task A_mac_picking_the_shared_folder_opens_the_guide_inside_it_and_never_starts_an_empty_one()
    {
        // The shared folder as OneDrive holds it: the app, and the guide in a "guide" folder beside it.
        var shared = TempFolder.Create("shared");
        string guide = Path.Combine(shared, "guide");
        var seed = new MainViewModel(new FakePlatform { SupportsServer = false }, new FakeSettings(new GuideSettings { UserName = "Paul" }))
            { RequiredFolder = guide };
        Directory.CreateDirectory(guide);
        await seed.StartAsync();
        await seed.ShutdownAsync();
        Assert.True(DmapShared.Storage.FolderGuideStore.HasData(guide));

        // A Mac: no folder chosen yet, so it asks for one (and says how), rather than for a server.
        var platform = new FakePlatform { SupportsServer = false };
        var mac = new MainViewModel(platform, new FakeSettings(new GuideSettings { UserName = "Sam" }));
        await mac.StartAsync();
        var asking = Assert.IsType<GuideUnavailablePage>(mac.CurrentPage);
        Assert.Contains("Choose folder", asking.Message);

        // Picking an empty folder is refused, and nothing is written there.
        var empty = TempFolder.Create("empty");
        platform.PickedFolder = empty;
        string? problem = await mac.ChooseFolderAsync(new GuideSettings { UserName = "Sam" });
        Assert.Contains("There's no DMAP Guide in that folder", problem);
        Assert.Empty(Directory.EnumerateFileSystemEntries(empty));
        Assert.False(mac.IsConnected);

        // Picking the shared folder itself opens the guide inside it.
        platform.PickedFolder = shared;
        problem = await mac.ChooseFolderAsync(new GuideSettings { UserName = "Sam" });
        Assert.Null(problem);
        Assert.True(mac.IsConnected);
        Assert.Equal(guide, mac.Location);
    }

    [Fact]
    public async Task The_shared_app_refuses_a_guide_folder_that_fails_the_extra_check()
    {
        var platform = new FakePlatform { SupportsServer = false };
        var appFolder = TempFolder.Create("unzipped"); // the whole shared folder, downloaded rather than synced
        string guide = Path.Combine(appFolder, "guide");
        Directory.CreateDirectory(guide);
        var settings = new FakeSettings(new GuideSettings { UserName = "Alex", DataFolder = TempFolder.Create("private") });

        bool synced = false;
        var vm = new MainViewModel(platform, settings)
        {
            RequiredFolder = guide,
            RequiredFolderCheck = folder => synced ? null : $"Not synced: {folder}"
        };
        await vm.StartAsync();

        Assert.False(vm.IsConnected);
        var page = Assert.IsType<GuideUnavailablePage>(vm.CurrentPage);
        Assert.Equal("This copy of the guide isn't shared", page.Title);
        Assert.Contains(guide, page.Message);
        Assert.False(page.CanOpenSettings);
        Assert.False(Directory.EnumerateFileSystemEntries(guide).Any()); // nothing was written there

        synced = true;
        await vm.ReconnectCommand.ExecuteAsync(null);
        Assert.True(vm.IsConnected);
        Assert.Equal(guide, vm.Location);
    }

    [Fact]
    public async Task An_older_app_is_told_when_a_newer_one_has_used_the_guide()
    {
        var folder = TempFolder.Create("versions");
        MainViewModel Open(string version)
        {
            var vm = new MainViewModel(new FakePlatform { SupportsServer = false, AppVersion = version },
                new FakeSettings(new GuideSettings { UserName = "Alex", DataFolder = folder }));
            vm.StartAsync().GetAwaiter().GetResult();
            return vm;
        }
        string note = Path.Combine(folder, "app-version.txt");

        var first = Open("1.0.2");
        Assert.Equal("", first.VersionWarning);
        Assert.Equal("1.0.2", File.ReadAllText(note));

        var older = Open("1.0.1");
        Assert.Contains("1.0.1", older.VersionWarning);
        Assert.Contains("1.0.2", older.VersionWarning);
        Assert.Equal("1.0.2", File.ReadAllText(note)); // an older app never lowers the note

        var newer = Open("1.0.3");
        Assert.Equal("", newer.VersionWarning);
        Assert.Equal("1.0.3", File.ReadAllText(note));

        // The app that was fine a moment ago learns of the newer one at its next sync.
        await first.SyncNowAsync();
        Assert.Contains("1.0.3", first.VersionWarning);

        await first.ShutdownAsync();
        await older.ShutdownAsync();
        await newer.ShutdownAsync();
    }

    [Fact]
    public async Task Refresh_saves_my_changes_and_picks_up_everyone_elses()
    {
        var platform = new FakePlatform { SupportsServer = false };
        var folder = TempFolder.Create("shared");
        var mine = new MainViewModel(platform, new FakeSettings(new GuideSettings { UserName = "Alex", DataFolder = folder }));
        await mine.StartAsync();
        var theirs = new MainViewModel(platform, new FakeSettings(new GuideSettings { UserName = "Sam", DataFolder = folder }));
        await theirs.StartAsync();

        await mine.RefreshCommand.ExecuteAsync(null);
        Assert.StartsWith("Up to date", mine.StatusText);

        // Sam changes a page and presses Refresh: it's saved.
        var season = theirs.Data.Seasons[0];
        await theirs.NavigateAsync(season);
        await theirs.ToggleEditCommand.ExecuteAsync(null);
        season.Description = "Sam was here";
        await theirs.RefreshCommand.ExecuteAsync(null);
        Assert.StartsWith("Saved", theirs.StatusText);

        // Alex presses Refresh: Sam's change arrives.
        await mine.RefreshCommand.ExecuteAsync(null);
        Assert.StartsWith("Picked up changes", mine.StatusText);
        Assert.Equal("Sam was here", mine.Data.Seasons[0].Description);

        await mine.ShutdownAsync();
        await theirs.ShutdownAsync();
    }

    [Fact]
    public async Task A_view_only_folder_opens_without_edit()
    {
        var platform = new FakePlatform { SupportsServer = false };
        var folder = TempFolder.Create("view");
        var settings = new FakeSettings(new GuideSettings { UserName = "Alex", DataFolder = folder });

        var seed = new MainViewModel(platform, settings);
        await seed.StartAsync(); // writes the sample guide
        await seed.ShutdownAsync();

        string guideFile = Path.Combine(folder, "guide.json");
        File.SetAttributes(guideFile, FileAttributes.ReadOnly); // what a "Can view" OneDrive share looks like on disk
        try
        {
            var vm = new MainViewModel(platform, settings);
            await vm.StartAsync();

            Assert.True(vm.IsConnected);
            Assert.True(vm.IsReadOnly);
            Assert.False(vm.CanEdit);
            Assert.False(vm.CanChangePages);
            Assert.NotEmpty(vm.Data.Seasons);
            Assert.Contains("Read only", vm.StatusText);
            await vm.ToggleEditCommand.ExecuteAsync(null);
            Assert.False(vm.IsEditing);
            Assert.Empty(platform.Alerts);
        }
        finally
        {
            File.SetAttributes(guideFile, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task The_guide_opens_on_the_prologue()
    {
        using var server = new TestServer();
        var (vm, _) = await Connected(server);

        Assert.IsType<PrologueViewModel>(vm.CurrentPage);
        Assert.Equal("Prologue", vm.Breadcrumb);
        Assert.True(vm.IsOnPrologue);
        Assert.False(vm.CanEdit); // nothing on the Prologue is edited by hand

        // The table of contents starts showing the campaigns, with their episodes rolled up.
        Assert.All(vm.Data.Seasons, s => Assert.True(s.IsExpanded));
        Assert.All(vm.Data.AllCampaigns, c => Assert.False(c.IsExpanded));
        var episode = vm.Data.AllEpisodes.First();
        await vm.NavigateAsync(episode);
        Assert.False(vm.IsOnPrologue);
        Assert.True(episode.Campaign!.IsExpanded);
        Assert.True(episode.Campaign.Season!.IsExpanded);
    }

    [Fact]
    public async Task The_cast_is_saved_with_the_show_details_and_a_campaigns_book_with_the_campaign()
    {
        using var server = new TestServer();
        var (vm, _) = await Connected(server);
        var campaign = vm.Data.AllCampaigns.First();

        await vm.NavigateAsync(campaign);
        await vm.ToggleEditCommand.ExecuteAsync(null);
        campaign.BookTitle = "Curse of Strahd";
        await vm.ToggleEditCommand.ExecuteAsync(null);

        await vm.ShowCastCommand.ExecuteAsync(null);
        Assert.IsType<CastPageViewModel>(vm.CurrentPage);
        await vm.ToggleEditCommand.ExecuteAsync(null);
        vm.AddCastMemberCommand.Execute(null);
        vm.Data.Cast[0].PlayerName = "Sam";
        vm.Data.Cast[0].ImageOffsetY = 0.75;
        await vm.ToggleEditCommand.ExecuteAsync(null);
        await vm.SaveAsync();

        var check = new DmapShared.Storage.ServerGuideStore(server.Client(), "Check", TempFolder.Create("c"));
        var saved = (await check.LoadAsync()).Data;
        Assert.Equal("Curse of Strahd", saved.AllCampaigns.Single(c => c.Id == campaign.Id).BookTitle);
        Assert.Equal("Sam", saved.Cast.Single().PlayerName);
        Assert.Equal(0.75, saved.Cast.Single().ImageOffsetY);
    }

    [Fact]
    public async Task The_introduction_is_edited_on_its_own_and_reaches_everyone_sharing_the_folder()
    {
        var folder = TempFolder.Create("intro");
        async Task<MainViewModel> Open(string name)
        {
            var vm = new MainViewModel(new FakePlatform { SupportsServer = false },
                new FakeSettings(new GuideSettings { UserName = name, DataFolder = folder }));
            await vm.StartAsync();
            return vm;
        }
        var paul = await Open("Paul");
        var colleague = await Open("Jo");

        await paul.ShowIntroductionCommand.ExecuteAsync(null);
        Assert.IsType<IntroductionViewModel>(paul.CurrentPage);
        Assert.True(paul.IsOnIntroduction);
        Assert.Equal("Introduction", paul.Breadcrumb);
        Assert.True(paul.CanEdit);

        await paul.ToggleEditCommand.ExecuteAsync(null);
        Assert.True(paul.IsEditing);
        paul.Data.Introduction = "Dungeon Masters is a show about...\n\nSecond paragraph.";
        await paul.ToggleEditCommand.ExecuteAsync(null);
        await paul.SaveAsync();

        Assert.True(await colleague.SyncNowAsync());
        Assert.Equal("Dungeon Masters is a show about...\n\nSecond paragraph.", colleague.Data.Introduction);

        var reopened = await Open("Sam");
        Assert.Equal("Dungeon Masters is a show about...\n\nSecond paragraph.", reopened.Data.Introduction);

        // Leaving the page ends editing, like the Cast page.
        await paul.ToggleEditCommand.ExecuteAsync(null);
        await paul.ShowPrologueCommand.ExecuteAsync(null);
        Assert.False(paul.IsEditing);
        Assert.False(paul.IsOnIntroduction);
    }

    [Fact]
    public async Task Guest_stars_come_from_the_cast_page_and_are_ticked_on_episodes()
    {
        using var server = new TestServer();
        var (vm, _) = await Connected(server);
        var episode = vm.Data.AllEpisodes.First();

        await vm.ShowCastCommand.ExecuteAsync(null);
        await vm.ToggleEditCommand.ExecuteAsync(null);
        vm.AddCastMemberCommand.Execute("DungeonMaster");
        vm.Data.DungeonMasters[0].PlayerName = "Dee";
        vm.AddCastMemberCommand.Execute("Guest");
        var gina = vm.Data.Guests[0];
        gina.PlayerName = "Gina";
        await vm.ToggleEditCommand.ExecuteAsync(null);

        await vm.NavigateAsync(episode);
        Assert.True(episode.HasShowGuests);
        await vm.ToggleEditCommand.ExecuteAsync(null);
        episode.GuestChoices.Single().IsInEpisode = true;
        await vm.ToggleEditCommand.ExecuteAsync(null);
        await vm.SaveAsync();
        Assert.Equal("Gina", episode.Guests.Single().PlayerName);

        var check = new DmapShared.Storage.ServerGuideStore(server.Client(), "Check", TempFolder.Create("c"));
        var saved = (await check.LoadAsync()).Data;
        Assert.Equal("Dee", saved.DungeonMasters.Single().PlayerName);
        Assert.Equal("Gina", saved.AllEpisodes.Single(e => e.Id == episode.Id).Guests.Single().PlayerName);

        saved.RefreshCastAppearances();
        Assert.Equal($"Guest in episodes {episode.Number}", saved.Guests.Single().Appearances);
    }

    [Fact]
    public void Air_date_platforms_are_ticked_on_and_off_and_keep_anything_typed_by_hand()
    {
        var air = new AirDate { Platform = "Netflix" };

        air.PlatformChoices.Single(p => p.Name == "Disney+").IsSelected = true;
        air.PlatformChoices.Single(p => p.Name == "YouTube").IsSelected = true;
        Assert.Equal("YouTube, Disney+, Netflix", air.Platform);

        air.PlatformChoices.Single(p => p.Name == "YouTube").IsSelected = false;
        Assert.Equal("Disney+, Netflix", air.Platform);
        Assert.True(air.PlatformChoices.Single(p => p.Name == "Disney+").IsSelected);
    }

    [Fact]
    public async Task A_link_opens_in_the_browser_and_a_bad_one_is_explained()
    {
        using var server = new TestServer();
        var (vm, platform) = await Connected(server);

        await vm.OpenLinkCommand.ExecuteAsync("youtube.com/watch?v=abc");
        Assert.Equal("https://youtube.com/watch?v=abc", platform.Links.Single());

        await vm.OpenLinkCommand.ExecuteAsync("not a link at all");
        Assert.Single(platform.Links);
        Assert.Contains("doesn't look like a web address", platform.Alerts.Single());
    }

    [Fact]
    public async Task The_campaigns_dungeon_master_carries_through_to_its_episodes()
    {
        using var server = new TestServer();
        var (vm, _) = await Connected(server);
        var campaign = vm.Data.AllCampaigns.First(c => c.Episodes.Count >= 2);
        var first = campaign.Episodes[0];
        var second = campaign.Episodes[1];
        second.DungeonMaster = "Someone Else"; // this episode has its own

        await vm.NavigateAsync(campaign);
        await vm.ToggleEditCommand.ExecuteAsync(null);
        campaign.DungeonMaster = "Morgan Vale";
        await vm.ToggleEditCommand.ExecuteAsync(null);

        Assert.Equal("Morgan Vale", first.DungeonMasterShown);
        Assert.True(first.IsDungeonMasterInherited);
        Assert.Equal("Someone Else", second.DungeonMasterShown);

        // Change it again: the episode that followed the old name follows the new one.
        first.DungeonMaster = "Morgan Vale"; // as if it had been typed in by hand earlier
        await vm.ToggleEditCommand.ExecuteAsync(null);
        campaign.DungeonMaster = "Dee";
        await vm.ToggleEditCommand.ExecuteAsync(null);
        Assert.Equal("Dee", first.DungeonMasterShown);
        Assert.Equal("Someone Else", second.DungeonMasterShown);

        // A new episode follows the campaign too.
        await vm.AddEpisodeCommand.ExecuteAsync(campaign);
        var added = campaign.Episodes.Last();
        Assert.Equal("", added.DungeonMaster);
        Assert.Equal("Dee", added.DungeonMasterShown);
    }

    [Fact]
    public async Task Shoot_notes_are_saved_on_their_episode()
    {
        using var server = new TestServer();
        var (vm, _) = await Connected(server);
        var episode = vm.Data.AllEpisodes.First();

        await vm.OpenShootNotesCommand.ExecuteAsync(episode);
        Assert.Same(episode.ShootNotesPage, vm.CurrentPage);
        Assert.EndsWith("Shoot Notes", vm.Breadcrumb);
        Assert.True(episode.IsSelected); // the table of contents stays on the episode

        await vm.ToggleEditCommand.ExecuteAsync(null);
        episode.ShootNotes = "The dragon prop caught fire.";
        await vm.ToggleEditCommand.ExecuteAsync(null);
        await vm.SaveAsync();

        var check = new DmapShared.Storage.ServerGuideStore(server.Client(), "Check", TempFolder.Create("c"));
        var saved = (await check.LoadAsync()).Data;
        Assert.Equal("The dragon prop caught fire.", saved.AllEpisodes.Single(e => e.Id == episode.Id).ShootNotes);
    }

    /// <summary>Makes a small typed PDF, like a real Hot Sheets export.</summary>
    private static string MakePdf(string name, params string[] lines)
    {
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        double y = 780;
        foreach (var line in lines)
        {
            page.AddText(line, 12, new UglyToad.PdfPig.Core.PdfPoint(50, y), font);
            y -= 20;
        }
        string path = Path.Combine(TempFolder.Create("pdf"), name);
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    [Fact]
    public async Task Hot_sheets_pdf_text_is_added_to_the_episode_and_can_be_replaced_or_cleared()
    {
        using var server = new TestServer();
        var (vm, platform) = await Connected(server);
        var episode = vm.Data.AllEpisodes.First();

        await vm.OpenHotSheetsCommand.ExecuteAsync(episode);
        Assert.Same(episode.HotSheetsPage, vm.CurrentPage);
        Assert.EndsWith("Hot Sheets", vm.Breadcrumb);
        Assert.True(episode.IsSelected);

        // Only while editing.
        platform.PdfToPick = MakePdf("Ep 101 Hot Sheets.pdf", "Call time 7:00 AM", "Stage B");
        await vm.AddHotSheetsCommand.ExecuteAsync(episode);
        Assert.Equal("", episode.HotSheets);

        await vm.ToggleEditCommand.ExecuteAsync(null);
        await vm.AddHotSheetsCommand.ExecuteAsync(episode);
        Assert.Contains("Call time 7:00 AM", episode.HotSheets);
        Assert.Contains("Stage B", episode.HotSheets);
        Assert.StartsWith("Ep 101 Hot Sheets.pdf · added", episode.HotSheetsSource);

        // A second PDF: "Add underneath" keeps both.
        platform.Answer = false;
        platform.PdfToPick = MakePdf("Revised.pdf", "Wrap 6:00 PM");
        await vm.AddHotSheetsCommand.ExecuteAsync(episode);
        Assert.Contains("Call time 7:00 AM", episode.HotSheets);
        Assert.Contains("Wrap 6:00 PM", episode.HotSheets);

        // "Replace" swaps them out.
        platform.Answer = true;
        await vm.AddHotSheetsCommand.ExecuteAsync(episode);
        Assert.DoesNotContain("Call time", episode.HotSheets);
        Assert.StartsWith("Revised.pdf", episode.HotSheetsSource);

        // Edited by hand (the laid-out text), then saved for everyone, layout and all.
        Assert.True(episode.HasHotSheetsLayout);
        episode.HotSheetBlocks.First(b => b.Kind == HotSheetBlockKind.Text).Text += "\nBring the dragon.";
        await vm.ToggleEditCommand.ExecuteAsync(null);
        await vm.SaveAsync();
        var check = new DmapShared.Storage.ServerGuideStore(server.Client(), "Check", TempFolder.Create("c"));
        var saved = (await check.LoadAsync()).Data.AllEpisodes.Single(e => e.Id == episode.Id);
        Assert.Contains("Bring the dragon.", saved.HotSheets);
        Assert.Contains("Bring the dragon.", saved.HotSheetBlocks.Single().Text);
        Assert.StartsWith("Revised.pdf", saved.HotSheetsSource);

        // Cleared.
        await vm.ToggleEditCommand.ExecuteAsync(null);
        await vm.ClearHotSheetsCommand.ExecuteAsync(episode);
        Assert.Equal("", episode.HotSheets);
        Assert.Equal("", episode.HotSheetsSource);
        Assert.False(episode.HasHotSheetsLayout);
    }

    [Fact]
    public async Task Hot_sheets_from_before_the_layout_are_kept_when_a_pdf_is_added_underneath()
    {
        using var server = new TestServer();
        var (vm, platform) = await Connected(server);
        var episode = vm.Data.AllEpisodes.First();
        episode.HotSheets = "Old notes from 1.0.10";
        await vm.OpenHotSheetsCommand.ExecuteAsync(episode);
        await vm.ToggleEditCommand.ExecuteAsync(null);

        platform.Answer = false; // add underneath
        platform.PdfToPick = MakePdf("New.pdf", "Call time 8:00 AM");
        await vm.AddHotSheetsCommand.ExecuteAsync(episode);

        Assert.Equal("Old notes from 1.0.10", episode.HotSheetBlocks[0].Text);
        Assert.Equal(HotSheetBlockKind.Rule, episode.HotSheetBlocks[1].Kind);
        Assert.Contains("Old notes from 1.0.10", episode.HotSheets);
        Assert.Contains("Call time 8:00 AM", episode.HotSheets);
    }

    [Fact]
    public async Task A_pdf_with_no_words_is_explained_and_changes_nothing()
    {
        using var server = new TestServer();
        var (vm, platform) = await Connected(server);
        var episode = vm.Data.AllEpisodes.First();
        await vm.OpenHotSheetsCommand.ExecuteAsync(episode);
        await vm.ToggleEditCommand.ExecuteAsync(null);

        platform.PdfToPick = MakePdf("Scan.pdf");
        await vm.AddHotSheetsCommand.ExecuteAsync(episode);
        Assert.Equal("", episode.HotSheets);
        Assert.Contains(platform.Alerts, a => a.Contains("No words could be found"));
    }

    // ------------------------------------------------------------
    // Two episodes on one Hot Sheets PDF (502 & 503 onwards)
    // ------------------------------------------------------------

    private static HotSheetBlock Table(params string[][] rows)
    {
        var table = new HotSheetBlock { Kind = HotSheetBlockKind.Table, ColumnWidths = [0.2, 0.1, 0.7] };
        foreach (var cells in rows)
        {
            var row = new HotSheetRow();
            foreach (var text in cells) row.Cells.Add(new HotSheetCell { Text = text, Span = cells.Length == 1 ? 3 : 1 });
            table.Rows.Add(row);
        }
        return table;
    }

    /// <summary>Laid out like the real double sheets, as the PDF reader rebuilds them.</summary>
    private static List<HotSheetBlock> TwoEpisodeSheet() =>
    [
        HotSheetBlock.Paragraph("Dungeon Masters 502 & 503", HotSheetStyle.Title),
        HotSheetBlock.Paragraph("Date: 11/11/2026\nProducers: Ella, Burke", HotSheetStyle.Bold),
        HotSheetBlock.Rule(),
        HotSheetBlock.Paragraph("OVERVIEW:\n502", HotSheetStyle.Bold),
        HotSheetBlock.Paragraph("The group is guided to the dorms."),
        HotSheetBlock.Paragraph("THE PROPHET gives a speech."),
        HotSheetBlock.Paragraph("503", HotSheetStyle.Bold),
        HotSheetBlock.Paragraph("ILAFOR slips away to the Aludel."),
        HotSheetBlock.Paragraph("They arrive at the city gate."),
        HotSheetBlock.Rule(),
        Table(["Marching Orders-#502"], ["LOCATION", "CAST", "ADD’ CHARACTERS"], ["Stage #2", "Jasmine", ""], ["SYNOPSIS: 502.\nThe group prepares."]),
        Table(["10:43:25:06", "502", "start"], ["10:44:00:14", "502", "LLP retreats"], ["13:44:20:15", "502", "break lunch"]),
        // 507's box came out of the PDF in two pieces: its title alone, then LOCATION / SYNOPSIS.
        Table(["Nibenay’s Bargain- 503"]),
        Table(["LOCATION", "CAST", "ADD’ CHARACTERS"], ["Stage #3", "Jasmine", ""], ["SYNOPSIS: 503\nThe party journeys."]),
        Table(["15:57:26:21", "503", "PICKUP end of 502."], ["16:00:14:01", "503", "THO startles MARTHA"], ["18:57:35:19", "503", "end"]),
    ];

    private static string Words(IEnumerable<HotSheetBlock> blocks) => HotSheetBlock.PlainText(blocks);

    [Fact]
    public void A_two_episode_hot_sheet_keeps_only_the_chosen_episode()
    {
        Assert.Equal([502, 503], DmapShared.HotSheetEpisodes.EpisodesIn(TwoEpisodeSheet()));
        Assert.True(DmapShared.HotSheetEpisodes.CoversOtherEpisodes(TwoEpisodeSheet(), 503));
        Assert.False(DmapShared.HotSheetEpisodes.CoversOtherEpisodes(TwoEpisodeSheet(), 504));

        string only502 = Words(DmapShared.HotSheetEpisodes.KeepOnly(TwoEpisodeSheet(), 502));
        Assert.StartsWith("Dungeon Masters 502\n\nDate: 11/11/2026", only502);
        Assert.Contains("OVERVIEW:\n502", only502);
        Assert.Contains("THE PROPHET gives a speech.", only502);
        Assert.Contains("SYNOPSIS: 502.", only502);
        Assert.Contains("13:44:20:15   502   break lunch", only502);
        Assert.DoesNotContain("ILAFOR slips away", only502);
        Assert.DoesNotContain("Nibenay’s Bargain", only502);
        Assert.DoesNotContain("SYNOPSIS: 503", only502);
        Assert.DoesNotContain("   503   ", only502);
        Assert.DoesNotContain("Stage #3", only502);

        var kept503 = DmapShared.HotSheetEpisodes.KeepOnly(TwoEpisodeSheet(), 503);
        string only503 = Words(kept503);
        Assert.StartsWith("Dungeon Masters 503\n", only503);
        Assert.Contains("OVERVIEW:\n\n503", only503);   // the OVERVIEW label stays, without 502's number
        Assert.Contains("They arrive at the city gate.", only503);
        Assert.Contains("Nibenay’s Bargain- 503", only503);
        Assert.Contains("SYNOPSIS: 503", only503);
        Assert.Contains("PICKUP end of 502.", only503);   // tagged 503, so it stays (remove it by hand if wanted)
        Assert.DoesNotContain("The group is guided", only503);
        Assert.DoesNotContain("Marching Orders", only503);
        Assert.DoesNotContain("   502   ", only503);
        Assert.False(DmapShared.HotSheetEpisodes.CoversOtherEpisodes(kept503, 503)); // nothing left to take out
        Assert.DoesNotContain(kept503.Zip(kept503.Skip(1)), p => p.First.Kind == HotSheetBlockKind.Rule && p.Second.Kind == HotSheetBlockKind.Rule);

        // A single-episode sheet is left alone (its timeline is tagged "1", not "501").
        List<HotSheetBlock> single =
        [
            HotSheetBlock.Paragraph("Dungeon Masters 501", HotSheetStyle.Title),
            HotSheetBlock.Paragraph("OVERVIEW:\n501", HotSheetStyle.Bold),
            HotSheetBlock.Paragraph("Story."),
            Table(["18:44:51:05", "1", "end ep"], ["18:45:00:00", "1", "wrap"]),
        ];
        Assert.False(DmapShared.HotSheetEpisodes.CoversOtherEpisodes(single, 501));
        Assert.Equal(single, DmapShared.HotSheetEpisodes.KeepOnly(single, 501));
    }

    [Fact]
    public async Task Hot_sheets_for_two_episodes_can_be_trimmed_and_pieces_removed_by_hand()
    {
        using var server = new TestServer();
        var (vm, platform) = await Connected(server);
        var episode = vm.Data.AllEpisodes.First();
        episode.Number = 503;
        episode.HotSheetBlocks = new(TwoEpisodeSheet());
        episode.RefreshHotSheetsText();
        var page = episode.HotSheetsPage;

        await vm.OpenHotSheetsCommand.ExecuteAsync(episode);
        Assert.True(page.CoversOtherEpisodes);
        Assert.Contains("Episode 502", page.OtherEpisodesNote);
        Assert.Equal("✂ Keep only Episode 503's parts", page.KeepOnlyLabel);
        // "Remove section" is offered on each overview heading and each episode box (507's two-piece box once).
        Assert.Equal(4, episode.HotSheetBlocks.Count(b => b.StartsSection));

        // Not while reading.
        await vm.KeepOnlyThisEpisodeHotSheetsCommand.ExecuteAsync(episode);
        Assert.Contains("The group is guided", episode.HotSheets);

        await vm.ToggleEditCommand.ExecuteAsync(null);
        platform.Answer = true;
        await vm.KeepOnlyThisEpisodeHotSheetsCommand.ExecuteAsync(episode);
        Assert.DoesNotContain("The group is guided", episode.HotSheets);
        Assert.False(page.CoversOtherEpisodes);
        Assert.Equal("", page.OtherEpisodesNote);

        // ✕ on one timeline line.
        var timeline = episode.HotSheetBlocks.Single(b => b.Rows.Any(r => r.Cells.Count == 3 && r.Cells[2].Text == "end"));
        vm.RemoveHotSheetRowCommand.Execute(timeline.Rows.Single(r => r.Cells[2].Text.StartsWith("PICKUP")));
        Assert.DoesNotContain("PICKUP end of 502", episode.HotSheets);
        Assert.Equal(2, timeline.Rows.Count);

        // ✕ on a paragraph.
        vm.RemoveHotSheetBlockCommand.Execute(episode.HotSheetBlocks.Single(b => b.Text == "They arrive at the city gate."));
        Assert.DoesNotContain("city gate", episode.HotSheets);

        // "Remove section" on the overview's 503 heading takes its paragraphs and stops at the divider.
        var heading = episode.HotSheetBlocks.Single(b => b.Text == "503");
        await vm.RemoveHotSheetSectionCommand.ExecuteAsync(heading);
        Assert.DoesNotContain("ILAFOR slips away", episode.HotSheets);
        Assert.Contains("Nibenay’s Bargain- 503", episode.HotSheets);
        Assert.Contains("THO startles MARTHA", episode.HotSheets);

        // Saved for everyone, with the layout.
        await vm.ToggleEditCommand.ExecuteAsync(null);
        await vm.SaveAsync();
        var check = new DmapShared.Storage.ServerGuideStore(server.Client(), "Check", TempFolder.Create("c"));
        var saved = (await check.LoadAsync()).Data.AllEpisodes.Single(e => e.Id == episode.Id);
        Assert.Equal(episode.HotSheets, saved.HotSheets);
        Assert.Equal(episode.HotSheetBlocks.Count, saved.HotSheetBlocks.Count);
        Assert.DoesNotContain(saved.HotSheetBlocks.SelectMany(b => b.Rows), r => r.Cells.Any(c => c.Text.StartsWith("PICKUP")));
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
    public async Task While_changes_cant_be_fetched_every_page_says_so()
    {
        var server = new TestServer();
        var (vm, _) = await Connected(server);

        server.Dispose(); // the server goes away
        await vm.SyncNowAsync();
        Assert.StartsWith("⚠ Not up to date", vm.StatusText);

        await vm.NavigateAsync(vm.Data.AllEpisodes.First());
        Assert.StartsWith("⚠ Not up to date", vm.StatusText);
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
        public bool SupportsServer { get; set; } = true;
        public string CacheFolder { get; } = TempFolder.Create("platform-cache");
        public string AppVersion { get; set; } = "1.0.0";
        public bool SupportsWebCopy { get; set; }

        public Task AlertAsync(string title, string message)
        {
            Alerts.Add(message);
            return Task.CompletedTask;
        }

        public Task<bool> AskAsync(string title, string message, string yes, string no) => Task.FromResult(Answer);
        public Task<string?> PickImageAsync() => Task.FromResult<string?>(null);
        public string? PickedFolder { get; set; }
        public Task<string?> PickFolderAsync(string title, string? startIn) => Task.FromResult(PickedFolder);
        public string? PdfToPick { get; set; }
        public Task<string?> PickPdfAsync(string title, string prompt) => Task.FromResult(PdfToPick);
        public void BeginInvoke(Action action) => action();
        public List<string> Links { get; } = new();
        public void OpenLink(string url) => Links.Add(url);
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
