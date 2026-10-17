using DmapDesktopClient;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Controls.Shapes;
using System.Globalization;
using System.Net.Http;

namespace DmapAndroid;

public sealed class TomePage : ContentPage
{
    private const string Desk = "#1E140C";
    private const string Leather = "#55101E";
    private const string Gold = "#C9A24A";
    private const string GoldLight = "#E8CF8A";
    private const string Paper = "#EEDDB6";
    private const string Ink = "#2E1F14";
    private const string Rubric = "#9E141E";
    private const string SoftInk = "#5A4431";

    private readonly Entry _apiAddress = new() { Placeholder = "Private API address", Keyboard = Keyboard.Url };
    private readonly Entry _apiKey = new() { Placeholder = "Access key", IsPassword = true };
    private readonly Entry _episodeTitle = new() { Placeholder = "Episode title" };
    private readonly Entry _episodeDm = new() { Placeholder = "Dungeon Master" };
    private readonly Entry _episodeLocation = new() { Placeholder = "Filming location" };
    private readonly Editor _episodeSynopsis = new() { Placeholder = "Synopsis", AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 90 };

    private DmapApiClient? _client;
    private IReadOnlyList<SeasonDto> _seasons = [];
    private IReadOnlyList<CampaignDto> _campaigns = [];
    private IReadOnlyList<EpisodeDto> _episodes = [];
    private SeasonDto? _selectedSeason;
    private CampaignDto? _selectedCampaign;
    private EpisodeDto? _selectedEpisode;
    private ContentView? _rightPage;
    private Grid? _leftDoor;
    private Grid? _rightDoor;
    private bool _coverOpening;
    private bool _bookOpen;
    private bool _editingEpisode;
    private string _status = "";

    public TomePage()
    {
        Title = "DMAP Guide";
        BackgroundColor = Color.FromArgb(Desk);
        ShowCover();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var isEmulator = DeviceInfo.Current.DeviceType == DeviceType.Virtual;
        _apiAddress.Text = Preferences.Default.Get("dmap-api-address", isEmulator ? "http://10.0.2.2:5080/" : string.Empty);
        try
        {
            _apiKey.Text = await SecureStorage.Default.GetAsync("dmap-api-key") ?? (isEmulator ? "local-dev-key" : string.Empty);
        }
        catch
        {
            _status = "Enter the access key to connect.";
        }
    }

    private void ShowCover()
    {
        _leftDoor = CoverDoor();
        _rightDoor = CoverDoor();
        _leftDoor.Children.Add(new Image { Source = LogoImage(), WidthRequest = 118, HeightRequest = 118, Opacity = 0.32, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center });

        var openButton = ActionButton("Open the tome", OpenTomeAsync);
        var titlePlate = new Border
        {
            BackgroundColor = Color.FromArgb("#CC140408"),
            Stroke = Color.FromArgb(Gold),
            StrokeThickness = 1.5,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(14, 12),
            Content = new VerticalStackLayout
            {
                Spacing = 9,
                HorizontalOptions = LayoutOptions.Center,
                Children =
                {
                    new Image { Source = LogoImage(), WidthRequest = 270, HeightRequest = 110, Aspect = Aspect.AspectFit },
                    new Label { Text = "The Producer's Guide", FontFamily = "DmapDisplay", FontSize = 27, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(GoldLight), HorizontalTextAlignment = TextAlignment.Center },
                    Flourish(Gold),
                    new Label { Text = "DUNGEONS & DRAGONS · PRODUCTION", FontFamily = "DmapHeading", FontSize = 11, TextColor = Color.FromArgb("#DCC293"), HorizontalTextAlignment = TextAlignment.Center },
                    openButton
                }
            }
        };
        _rightDoor.Children.Add(titlePlate);

        var cover = new Grid
        {
            Padding = new Thickness(16, 20),
            BackgroundColor = Color.FromArgb(Desk),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            Children = { _leftDoor, _rightDoor }
        };
        Grid.SetColumn(_rightDoor, 1);
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OpenTomeAsync();
        cover.GestureRecognizers.Add(tap);

        Content = new Grid
        {
            BackgroundColor = Color.FromArgb(Desk),
            Children =
            {
                new Border
                {
                    Margin = new Thickness(16, 19),
                    BackgroundColor = Color.FromArgb(Leather),
                    Stroke = Color.FromArgb("#1A0408"),
                    StrokeThickness = 2,
                    StrokeShape = new RoundRectangle { CornerRadius = 15 },
                    Padding = 8,
                    Content = new Border
                    {
                        Stroke = Color.FromArgb(Gold),
                        StrokeThickness = 1,
                        StrokeShape = new RoundRectangle { CornerRadius = 11 },
                        Padding = 8,
                        Content = cover
                    }
                }
            }
        };
    }

    private static Grid CoverDoor()
    {
        var door = new Grid { BackgroundColor = Color.FromArgb(Leather), AnchorY = 0.5 };
        door.Children.Add(new Image { Source = ScalesImage(), Aspect = Aspect.AspectFill, Opacity = 0.44 });
        door.Children.Add(new BoxView { Color = Color.FromArgb("#66000000") });
        door.Children.Add(new Border { Stroke = Color.FromArgb(Gold), StrokeThickness = 1.5, Margin = new Thickness(7), StrokeShape = new RoundRectangle { CornerRadius = 8 }, InputTransparent = true });
        return door;
    }

    private async Task OpenTomeAsync()
    {
        if (_coverOpening || _bookOpen || _leftDoor == null || _rightDoor == null)
            return;
        _coverOpening = true;
        _leftDoor.AnchorX = 0;
        _rightDoor.AnchorX = 1;
        await Task.WhenAll(_leftDoor.ScaleXToAsync(0.01, 820, Easing.CubicInOut), _rightDoor.ScaleXToAsync(0.01, 820, Easing.CubicInOut));
        _bookOpen = true;
        BuildOpenBook();
        if (!string.IsNullOrWhiteSpace(_apiAddress.Text) && !string.IsNullOrWhiteSpace(_apiKey.Text))
            await ConnectAsync();
        else
            ShowConnectionPage();
    }

    private void BuildOpenBook()
    {
        var leftHeader = new VerticalStackLayout
        {
            Spacing = 3,
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 2,
                    HorizontalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Image { Source = LogoImage(), WidthRequest = 150, HeightRequest = 64, Aspect = Aspect.AspectFit },
                        new Label { Text = "The Producer's Guide", FontFamily = "DmapDisplay", FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(Rubric), HorizontalTextAlignment = TextAlignment.Center },
                        Flourish(Gold)
                    }
                },
                TocButton("❧  Prologue", ShowPrologue, Rubric, true),
                TocButton("☾  Chronicle of Dates", ShowSchedule, SoftInk, true),
                TocButton("⚙  Connection", ShowConnectionPage, SoftInk),
                FieldLabel("TABLE OF CONTENTS"),
                new BoxView { HeightRequest = 1, Color = Color.FromArgb(Gold), Opacity = 0.7 }
            }
        };

        var leftPage = new Grid
        {
            BackgroundColor = Color.FromArgb(Paper),
            Padding = new Thickness(13, 10, 10, 8)
        };
        leftPage.Add(new ScrollView { Content = new VerticalStackLayout { Spacing = 4, Children = { leftHeader, CreateContentsTree(), CreatePageActions() } } });

        _rightPage = new ContentView();
        ShowPrologue();

        var spread = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(new GridLength(0.40, GridUnitType.Star)), new ColumnDefinition(new GridLength(7)), new ColumnDefinition(new GridLength(0.60, GridUnitType.Star)) },
            BackgroundColor = Color.FromArgb("#BFA273")
        };
        spread.Add(leftPage, 0, 0);
        spread.Add(new BoxView { Color = Color.FromArgb("#633A2410") }, 1, 0);
        spread.Add(_rightPage, 2, 0);

        Content = new Grid
        {
            Padding = new Thickness(14, 15),
            BackgroundColor = Color.FromArgb(Desk),
            Children =
            {
                new Border
                {
                    BackgroundColor = Color.FromArgb(Leather),
                    Stroke = Color.FromArgb("#1A0408"),
                    StrokeThickness = 2,
                    StrokeShape = new RoundRectangle { CornerRadius = 14 },
                    Padding = 8,
                    Content = new Border
                    {
                        Stroke = Color.FromArgb(Gold),
                        StrokeThickness = 1,
                        StrokeShape = new RoundRectangle { CornerRadius = 8 },
                        Padding = 6,
                        Content = spread
                    }
                }
            }
        };
    }

    private View CreateContentsTree()
    {
        var contents = new VerticalStackLayout { Spacing = 1 };
        foreach (var season in _seasons.OrderBy(item => item.Number))
        {
            contents.Children.Add(TocButton($"❖  {SeasonName(season)}", () => ShowSeason(season), Rubric, true));
            foreach (var campaign in _campaigns.Where(item => item.SeasonId == season.Id).OrderBy(item => item.Number))
            {
                contents.Children.Add(TocButton($"⚔  {CampaignName(campaign)}", () => ShowCampaign(campaign), SoftInk, true, 12));
                contents.Children.Add(TocButton("♛  Cast & Characters", () => ShowCast(campaign), Rubric, false, 23));
                foreach (var episode in _episodes.Where(item => item.CampaignId == campaign.Id).OrderBy(item => item.Number))
                    contents.Children.Add(TocButton($"•  {EpisodeName(episode)}", () => ShowEpisode(episode), Ink, false, 23));
            }
        }
        if (contents.Children.Count == 0)
            contents.Children.Add(EmptyNote("No seasons yet."));
        return contents;
    }

    private View CreatePageActions()
    {
        var actions = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
            ColumnSpacing = 4,
            RowSpacing = 2
        };
        actions.Add(ActionButton("+ Season", AddSeasonAsync), 0, 0);
        actions.Add(ActionButton("+ Campaign", AddCampaignAsync), 1, 0);
        actions.Add(ActionButton("+ Episode", AddEpisodeAsync), 0, 1);
        actions.Add(ActionButton("Delete", DeleteCurrentAsync), 1, 1);
        return new VerticalStackLayout { Spacing = 4, Children = { new BoxView { HeightRequest = 1, Color = Color.FromArgb(Gold), Opacity = 0.7 }, actions } };
    }

    private void ShowPrologue()
    {
        var body = new VerticalStackLayout { Spacing = 9 };
        body.Children.Add(Body("The production guide for the show."));
        body.Children.Add(Section("In this guide"));
        body.Children.Add(Body($"{_seasons.Count} seasons · {_campaigns.Count} campaigns · {_episodes.Count} episodes"));
        var latest = _episodes.LastOrDefault();
        if (latest != null)
        {
            body.Children.Add(Section("Latest episode"));
            body.Children.Add(TocButton(EpisodeName(latest), () => ShowEpisode(latest), Rubric, true));
        }
        var upcoming = _episodes.SelectMany(episode => episode.AirDates
                .Where(date => date.Date.HasValue && date.Date.Value.Date >= DateTime.Today)
                .Select(date => (Episode: episode, Date: date.Date!.Value)))
            .OrderBy(item => item.Date).Take(6).ToList();
        body.Children.Add(Section("Coming up"));
        if (upcoming.Count == 0) body.Children.Add(EmptyNote("No upcoming dates are listed."));
        foreach (var item in upcoming)
            body.Children.Add(TocButton($"{item.Date.ToString("MMM d", CultureInfo.CurrentCulture)} · {EpisodeName(item.Episode)}", () => ShowEpisode(item.Episode), Ink));
        ShowRightPage("PROLOGUE", "The Guide", body);
    }

    private void ShowSchedule()
    {
        var body = new VerticalStackLayout { Spacing = 7 };
        var events = _episodes.SelectMany(episode => episode.AirDates.Where(date => date.Date.HasValue)
                .Select(date => (Date: date.Date!.Value, Kind: "Airing", Place: date.Platform, Episode: episode)))
            .Concat(_episodes.Where(episode => episode.DateFilmed.HasValue)
                .Select(episode => (Date: episode.DateFilmed!.Value, Kind: "Filming", Place: episode.FilmingLocation, Episode: episode)))
            .OrderBy(item => item.Date).ToList();
        body.Children.Add(Section("Chronicle of Dates"));
        if (events.Count == 0) body.Children.Add(EmptyNote("No filming or airing dates are listed."));
        foreach (var item in events)
            body.Children.Add(TocButton($"{item.Date.ToString("ddd, MMM d", CultureInfo.CurrentCulture)} · {item.Kind} · {EpisodeName(item.Episode)} {item.Place}", () => ShowEpisode(item.Episode), Ink));
        ShowRightPage("CHRONICLE", "Chronicle of Dates", body);
    }

    private void ShowSeason(SeasonDto season)
    {
        _selectedSeason = season;
        _selectedCampaign = null;
        _selectedEpisode = null;
        var body = new VerticalStackLayout { Spacing = 9 };
        if (!string.IsNullOrWhiteSpace(season.Year)) body.Children.Add(Detail("YEAR", season.Year));
        if (!string.IsNullOrWhiteSpace(season.Description)) body.Children.Add(Body(season.Description));
        body.Children.Add(Section("Campaigns"));
        foreach (var campaign in _campaigns.Where(item => item.SeasonId == season.Id).OrderBy(item => item.Number))
            body.Children.Add(TocButton(CampaignName(campaign), () => ShowCampaign(campaign), Rubric, true));
        ShowRightPage($"SEASON {season.Number}", season.Name, body);
    }

    private void ShowCampaign(CampaignDto campaign)
    {
        _selectedSeason = null;
        _selectedCampaign = campaign;
        _selectedEpisode = null;
        var body = new VerticalStackLayout { Spacing = 8 };
        if (!string.IsNullOrWhiteSpace(campaign.DungeonMaster)) body.Children.Add(Detail("DUNGEON MASTER", campaign.DungeonMaster));
        if (!string.IsNullOrWhiteSpace(campaign.Setting)) body.Children.Add(Detail("SETTING", campaign.Setting));
        if (!string.IsNullOrWhiteSpace(campaign.Description)) body.Children.Add(Body(campaign.Description));
        body.Children.Add(Section("Cast & Characters"));
        foreach (var member in campaign.Characters)
            body.Children.Add(Body($"{member.CharacterName} · {member.PlayerName} · {member.CharacterSummary}"));
        body.Children.Add(Section("Episodes"));
        foreach (var episode in _episodes.Where(item => item.CampaignId == campaign.Id).OrderBy(item => item.Number))
            body.Children.Add(TocButton(EpisodeName(episode), () => ShowEpisode(episode), Ink));
        ShowRightPage($"CAMPAIGN {campaign.Number}", campaign.Name, body);
    }

    private void ShowCast(CampaignDto campaign)
    {
        _selectedSeason = null;
        _selectedCampaign = campaign;
        _selectedEpisode = null;
        var body = new VerticalStackLayout { Spacing = 8 };
        if (campaign.Characters.Count == 0) body.Children.Add(EmptyNote("No characters are listed."));
        foreach (var member in campaign.Characters)
        {
            body.Children.Add(new Label { Text = member.CharacterName, FontFamily = "DmapHeading", FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(Rubric) });
            if (!string.IsNullOrWhiteSpace(member.PlayerName)) body.Children.Add(Detail("PLAYED BY", member.PlayerName));
            if (!string.IsNullOrWhiteSpace(member.CharacterSummary)) body.Children.Add(Body(member.CharacterSummary));
            if (!string.IsNullOrWhiteSpace(member.CharacterDescription)) body.Children.Add(Body(member.CharacterDescription));
            body.Children.Add(Flourish("#A9823C"));
        }
        ShowRightPage($"CAMPAIGN {campaign.Number}", "Cast & Characters", body);
    }

    private void ShowEpisode(EpisodeDto episode)
    {
        _selectedSeason = null;
        _selectedCampaign = _campaigns.FirstOrDefault(item => item.Id == episode.CampaignId);
        _selectedEpisode = episode;
        var season = _selectedCampaign is null ? null : _seasons.FirstOrDefault(item => item.Id == _selectedCampaign.SeasonId);
        var kicker = season is null ? $"EPISODE {episode.Number}" : $"SEASON {season.Number} · CAMPAIGN {_selectedCampaign!.Number} · EPISODE {episode.Number}";
        var body = new VerticalStackLayout { Spacing = 9 };
        if (_editingEpisode)
        {
            body.Children.Add(FieldLabel("TITLE")); body.Children.Add(StyleEntry(_episodeTitle));
            body.Children.Add(FieldLabel("DUNGEON MASTER")); body.Children.Add(StyleEntry(_episodeDm));
            body.Children.Add(FieldLabel("FILMING LOCATION")); body.Children.Add(StyleEntry(_episodeLocation));
            body.Children.Add(FieldLabel("SYNOPSIS")); body.Children.Add(StyleEditor(_episodeSynopsis));
            body.Children.Add(ActionButton("Save page", SaveEpisodeAsync));
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(episode.DungeonMaster)) body.Children.Add(Detail("DUNGEON MASTER", episode.DungeonMaster));
            if (!string.IsNullOrWhiteSpace(episode.FilmingLocation)) body.Children.Add(Detail("FILMING LOCATION", episode.FilmingLocation));
            if (!string.IsNullOrWhiteSpace(episode.Runtime)) body.Children.Add(Detail("RUNTIME", episode.Runtime));
            if (!string.IsNullOrWhiteSpace(episode.Summary)) body.Children.Add(Body(episode.Summary));
            if (episode.AirDates.Count > 0)
            {
                body.Children.Add(Section("Air dates"));
                foreach (var date in episode.AirDates) body.Children.Add(Body($"{date.Date?.ToString("d", CultureInfo.CurrentCulture) ?? "Date TBD"} · {date.Platform}"));
            }
            if (episode.KeyMoments.Count > 0)
            {
                body.Children.Add(Section("Key moments"));
                foreach (var moment in episode.KeyMoments) body.Children.Add(Body($"{moment.Timestamp} · {moment.Description}"));
            }
            body.Children.Add(ActionButton("Edit page", () => { ToggleEdit(); return Task.CompletedTask; }));
        }
        ShowRightPage(kicker, episode.Name, body, () =>
        {
            _editingEpisode = false;
            if (_selectedCampaign != null) ShowCampaign(_selectedCampaign); else ShowPrologue();
        });
    }

    private void ToggleEdit()
    {
        if (_selectedEpisode == null) return;
        if (!_editingEpisode)
        {
            _episodeTitle.Text = _selectedEpisode.Name;
            _episodeDm.Text = _selectedEpisode.DungeonMaster;
            _episodeLocation.Text = _selectedEpisode.FilmingLocation;
            _episodeSynopsis.Text = _selectedEpisode.Summary;
        }
        _editingEpisode = !_editingEpisode;
        ShowEpisode(_selectedEpisode);
    }

    private async Task SaveEpisodeAsync()
    {
        if (_client == null || _selectedEpisode == null) return;
        _selectedEpisode.Name = _episodeTitle.Text?.Trim() ?? string.Empty;
        _selectedEpisode.DungeonMaster = _episodeDm.Text?.Trim();
        _selectedEpisode.FilmingLocation = _episodeLocation.Text?.Trim();
        _selectedEpisode.Summary = _episodeSynopsis.Text;
        SetStatus("Saving page...");
        try
        {
            _selectedEpisode = await _client.UpdateEpisodeAsync(_selectedEpisode);
            _editingEpisode = false;
            _status = "Saved to the shared guide.";
            await RefreshGuideAsync();
            ShowEpisode(_episodes.FirstOrDefault(item => item.Id == _selectedEpisode.Id) ?? _selectedEpisode);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            SetStatus("This page changed on another copy. Reopen it from the contents and reapply your edit.");
        }
        catch (Exception ex) { SetStatus($"Save failed: {ex.Message}"); }
    }

    private async Task ConnectAsync()
    {
        var address = _apiAddress.Text?.Trim();
        var key = _apiKey.Text?.Trim();
        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(key)) { SetStatus("Enter the API address and access key."); return; }
        if (!address.EndsWith('/')) address += "/";
        SetStatus("Connecting...");
        try
        {
            var client = new DmapApiClient(address, key, "editor");
            var seasons = await client.GetSeasonsAsync();
            var campaigns = await client.GetCampaignsAsync();
            var episodes = await client.GetEpisodesAsync();
            _client?.Dispose();
            _client = client;
            _seasons = seasons;
            _campaigns = campaigns;
            _episodes = episodes;
            Preferences.Default.Set("dmap-api-address", address);
            await SecureStorage.Default.SetAsync("dmap-api-key", key);
            _status = $"Connected · {seasons.Count} seasons · {campaigns.Count} campaigns · {episodes.Count} episodes";
            ShowPrologue();
        }
        catch (HttpRequestException ex) { SetStatus($"Could not connect. Check Tailscale and the API address. {ex.Message}"); ShowConnectionPage(); }
        catch (Exception ex) { SetStatus($"Connection failed: {ex.Message}"); ShowConnectionPage(); }
    }

    private async Task RefreshGuideAsync()
    {
        if (_client == null) return;
        _seasons = await _client.GetSeasonsAsync();
        _campaigns = await _client.GetCampaignsAsync();
        _episodes = await _client.GetEpisodesAsync();
    }

    private async Task AddSeasonAsync()
    {
        if (_client == null) { SetStatus("Connect to the guide first."); return; }
        var title = await DisplayPromptAsync("New season", "Season title");
        if (string.IsNullOrWhiteSpace(title)) return;
        var season = new SeasonDto { Id = Guid.NewGuid().ToString(), Number = _seasons.Select(item => item.Number).DefaultIfEmpty().Max() + 1, Name = title };
        try { await _client.CreateSeasonAsync(season); await RefreshGuideAsync(); ShowSeason(_seasons.First(item => item.Id == season.Id)); }
        catch (Exception ex) { SetStatus($"Could not add season: {ex.Message}"); }
    }

    private async Task AddCampaignAsync()
    {
        if (_client == null || _seasons.Count == 0) { SetStatus("Add a season first."); return; }
        var title = await DisplayPromptAsync("New campaign", "Campaign title");
        if (string.IsNullOrWhiteSpace(title)) return;
        var names = _seasons.Select(SeasonName).ToArray();
        var chosen = await DisplayActionSheetAsync("Choose season", "Cancel", null, names);
        var index = Array.IndexOf(names, chosen);
        if (index < 0) return;
        var campaign = new CampaignDto { Id = Guid.NewGuid().ToString(), SeasonId = _seasons[index].Id, Number = _campaigns.Select(item => item.Number).DefaultIfEmpty().Max() + 1, Name = title };
        try { await _client.CreateCampaignAsync(campaign); await RefreshGuideAsync(); ShowCampaign(_campaigns.First(item => item.Id == campaign.Id)); }
        catch (Exception ex) { SetStatus($"Could not add campaign: {ex.Message}"); }
    }

    private async Task AddEpisodeAsync()
    {
        if (_client == null || _campaigns.Count == 0) { SetStatus("Add a campaign first."); return; }
        var title = await DisplayPromptAsync("New episode", "Episode title");
        if (string.IsNullOrWhiteSpace(title)) return;
        var names = _campaigns.Select(CampaignName).ToArray();
        var chosen = await DisplayActionSheetAsync("Choose campaign", "Cancel", null, names);
        var index = Array.IndexOf(names, chosen);
        if (index < 0) return;
        var campaign = _campaigns[index];
        var number = _episodes.Where(item => item.CampaignId == campaign.Id).Select(item => item.Number).DefaultIfEmpty(campaign.Number * 100).Max() + 1;
        var episode = new EpisodeDto { Id = Guid.NewGuid().ToString(), CampaignId = campaign.Id, Number = number, Name = title };
        try
        {
            await _client.CreateEpisodeAsync(episode);
            await RefreshGuideAsync();
            var created = _episodes.First(item => item.Id == episode.Id);
            ShowEpisode(created);
            ToggleEdit();
        }
        catch (Exception ex) { SetStatus($"Could not add episode: {ex.Message}"); }
    }

    private async Task DeleteCurrentAsync()
    {
        if (_client == null) { SetStatus("Connect to the guide first."); return; }
        if (_selectedEpisode == null && _selectedCampaign == null && _selectedSeason == null) { SetStatus("Open a season, campaign, or episode first."); return; }
        if (!await DisplayAlertAsync("Delete page?", "This also removes child pages.", "Delete", "Cancel")) return;
        try
        {
            if (_selectedEpisode != null) await _client.DeleteEpisodeAsync(_selectedEpisode.Id);
            else if (_selectedCampaign != null) await _client.DeleteCampaignAsync(_selectedCampaign.Id);
            else if (_selectedSeason != null) await _client.DeleteSeasonAsync(_selectedSeason.Id);
            _selectedEpisode = null; _selectedCampaign = null; _selectedSeason = null;
            await RefreshGuideAsync();
            ShowPrologue();
        }
        catch (Exception ex) { SetStatus($"Delete failed: {ex.Message}"); }
    }

    private void ShowConnectionPage()
    {
        StyleEntry(_apiAddress);
        StyleEntry(_apiKey);
        var body = new VerticalStackLayout { Spacing = 9, Children = { Body("Connect to the shared production guide."), FieldLabel("API ADDRESS"), _apiAddress, FieldLabel("ACCESS KEY"), _apiKey, ActionButton("Connect / Refresh", ConnectAsync) } };
        ShowRightPage("CONNECTION", "Open the shared guide", body);
    }

    private void ShowRightPage(string kicker, string? title, View body, Action? back = null)
    {
        if (_rightPage == null) return;
        var header = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 4,
            Children =
            {
                new Label { Text = kicker, FontFamily = "DmapHeading", FontSize = 10, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(SoftInk), VerticalOptions = LayoutOptions.Center },
                _selectedEpisode is null ? new Label() : HeaderButton(_editingEpisode ? "Reading" : "Edit page", ToggleEdit)
            }
        };
        header.SetColumn(header.Children[1], 1);
        var page = new VerticalStackLayout { Spacing = 8, Children = { header, new Label { Text = string.IsNullOrWhiteSpace(title) ? "The Guide" : title, FontFamily = "DmapDisplay", FontSize = 25, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(Rubric), LineBreakMode = LineBreakMode.WordWrap }, Flourish("#A9823C"), body, new Label { Text = _status, FontFamily = "DmapBodyItalic", FontSize = 13, TextColor = Color.FromArgb(SoftInk), Margin = new Thickness(0, 7, 0, 0) } } };
        var parchment = new Border { Background = ParchmentBrush(), Stroke = Color.FromArgb(Gold), StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 3 }, Padding = new Thickness(15, 13), Content = page };
        _rightPage.Content = new ScrollView { Content = back == null ? parchment : new VerticalStackLayout { Spacing = 4, Children = { parchment, HeaderButton("‹ Back", back) } } };
    }

    private void SetStatus(string status)
    {
        _status = status;
        if (_rightPage != null)
            ShowRightPage("STATUS", "The Guide", Body(status));
    }

    private static string SeasonName(SeasonDto item) => string.IsNullOrWhiteSpace(item.Name) ? $"Season {item.Number}" : $"Season {item.Number} · {item.Name}";
    private static string CampaignName(CampaignDto item) => string.IsNullOrWhiteSpace(item.Name) ? $"Campaign {item.Number}" : $"Campaign {item.Number} · {item.Name}";
    private static string EpisodeName(EpisodeDto item) => string.IsNullOrWhiteSpace(item.Name) ? $"Episode {item.Number}" : $"{item.Number} · {item.Name}";
    private static ImageSource LogoImage() => ImageSource.FromResource("DmapAndroid.Resources.dmaplogo.png", typeof(TomePage).Assembly);
    private static ImageSource ScalesImage() => ImageSource.FromResource("DmapAndroid.Resources.dragonscales.jpg", typeof(TomePage).Assembly);
    private static Button TocButton(string text, Action action, string color = Ink, bool bold = false, double indent = 0)
    {
        var button = new Button { Text = text, HorizontalOptions = LayoutOptions.Fill, BackgroundColor = Colors.Transparent, TextColor = Color.FromArgb(color), FontFamily = bold ? "DmapHeading" : "DmapBody", FontSize = bold ? 14 : 15, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None, Padding = new Thickness(3, 4), Margin = new Thickness(indent, 0, 0, 0) };
        button.Clicked += (_, _) => action();
        return button;
    }
    private static Button ActionButton(string text, Func<Task> action)
    {
        var button = new Button { Text = text, BackgroundColor = Color.FromArgb(Leather), TextColor = Color.FromArgb(GoldLight), BorderColor = Color.FromArgb(Gold), BorderWidth = 1, CornerRadius = 4, FontFamily = "DmapHeading", FontSize = 11, Padding = new Thickness(6, 5), Margin = new Thickness(1) };
        button.Clicked += async (_, _) => await action();
        return button;
    }
    private static Button HeaderButton(string text, Action action)
    {
        var button = new Button { Text = text, BackgroundColor = Colors.Transparent, TextColor = Color.FromArgb(GoldLight), FontFamily = "DmapHeading", FontSize = 10, Padding = new Thickness(4), MinimumWidthRequest = 0 };
        button.Clicked += (_, _) => action();
        return button;
    }
    private static Label Section(string text) => new() { Text = text, FontFamily = "DmapHeading", FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(Rubric), Margin = new Thickness(0, 7, 0, 1) };
    private static Label FieldLabel(string text) => new() { Text = text, FontFamily = "DmapHeading", FontSize = 10, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#8C7A63"), Margin = new Thickness(2, 3, 0, -4) };
    private static Label Body(string text) => new() { Text = text, FontFamily = "DmapBody", FontSize = 16, TextColor = Color.FromArgb(Ink), LineBreakMode = LineBreakMode.WordWrap };
    private static Label EmptyNote(string text) => new() { Text = text, FontFamily = "DmapBodyItalic", FontSize = 15, TextColor = Color.FromArgb("#8C7A63") };
    private static View Detail(string label, string value) => new VerticalStackLayout { Spacing = 0, Children = { FieldLabel(label), Body(value) } };
    private static Entry StyleEntry(Entry entry) { entry.FontFamily = "DmapBody"; entry.FontSize = 16; entry.BackgroundColor = Color.FromArgb("#FFF9EF"); entry.TextColor = Color.FromArgb(Ink); entry.PlaceholderColor = Color.FromArgb("#8C7A63"); return entry; }
    private static Editor StyleEditor(Editor editor) { editor.FontFamily = "DmapBody"; editor.FontSize = 16; editor.BackgroundColor = Color.FromArgb("#FFF9EF"); editor.TextColor = Color.FromArgb(Ink); editor.PlaceholderColor = Color.FromArgb("#8C7A63"); return editor; }
    private static LinearGradientBrush ParchmentBrush() => new([new GradientStop(Color.FromArgb("#F7ECD2"), 0), new GradientStop(Color.FromArgb(Paper), 0.65f), new GradientStop(Color.FromArgb("#DCC293"), 1)], new Point(0, 0), new Point(1, 1));
    private static Label Flourish(string color) => new() { Text = "────── ❦ ──────", FontFamily = "DmapBody", FontSize = 13, TextColor = Color.FromArgb(color), HorizontalTextAlignment = TextAlignment.Center };
}