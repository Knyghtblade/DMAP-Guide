using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmapGuide.Models;
using DmapGuide.Services;
using DmapDesktopClient;
using Microsoft.Win32;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace DmapGuide.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly DmapApiClient? _apiClient;
    private DataStore _store;
    private TreeNode? _selectedNode;
    private int? _loadedRemoteVersion;

    [ObservableProperty] private ShowData _data;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Breadcrumb))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    private object? _currentPage;

    /// <summary>Set by the window: pushes the value of the text box being typed in.</summary>
    public Action? CommitPendingEdits { get; set; }

    public bool CanEdit => CurrentPage is TreeNode;

    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _statusText = "";

    /// <summary>Raised just before the page changes (the window snapshots it for the page-turn).</summary>
    public event Action? Navigating;

    public string DataFolder => _store.Folder;

    public MainViewModel()
    {
        _settings = AppSettings.Load();

        if (!string.IsNullOrWhiteSpace(_settings.ApiBaseUrl))
        {
            try
            {
                _apiClient = new DmapApiClient(_settings.ApiBaseUrl, _settings.ApiKey, _settings.ApiRole);
            }
            catch
            {
                _apiClient = null;
            }
        }

        // "--data <folder>" opens a different guide for this run only.
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "--data");
        string folder = i >= 0 && i + 1 < args.Length ? args[i + 1] : _settings.DataFolder;

        _store = OpenStore(folder);
        _data = LoadOrSeed();
        _currentPage = new PrologueViewModel(this);

        // The folder watcher catches most changes; this catches anything it misses.
        var poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        poll.Tick += (_, _) => SyncNow();
        poll.Start();

        // Keep "you're editing this page" fresh for other people.
        var heartbeat = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        heartbeat.Tick += (_, _) => { if (_lockedId is Guid id) _store.WriteLock(id, _lockSince); };
        heartbeat.Start();
    }

    private DataStore OpenStore(string folder)
    {
        var store = new DataStore(folder, _settings.UserName);
        store.FilesChanged += () => Application.Current?.Dispatcher.BeginInvoke(SyncNow);
        store.StartWatching();
        return store;
    }

    private ShowData LoadOrSeed()
    {
        if (_apiClient != null)
        {
            var remoteData = TryLoadFromApi();
            if (remoteData != null)
            {
                StatusText = $"Using remote API: {_settings.ApiBaseUrl}";
                return remoteData;
            }
        }

        try
        {
            var data = _store.Load(out var unreadable);
            if (unreadable.Count > 0)
                MessageBox.Show(
                    $"{unreadable.Count} page(s) in {_store.Folder} could not be read and are not shown:\n\n" +
                    string.Join("\n", unreadable.Take(10)) +
                    "\n\nThose files have not been changed. OneDrive's version history can restore an earlier copy.",
                    "DMAP Guide", MessageBoxButton.OK, MessageBoxImage.Warning);
            return data;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"The guide in {_store.Folder} could not be read:\n{ex.Message}\n\n" +
                "Starting with an empty guide. Your files have not been changed.",
                "DMAP Guide", MessageBoxButton.OK, MessageBoxImage.Warning);
            _readOnlyAfterLoadError = true;
            return ShowData.CreateSeed();
        }
    }

    private ShowData? TryLoadFromApi()
    {
        try
        {
            var json = _apiClient!.GetGuideJsonAsync().GetAwaiter().GetResult();
            _loadedRemoteVersion = _apiClient.GuideVersion;
            var seasons = _apiClient.GetSeasonsAsync().GetAwaiter().GetResult();
            var campaigns = _apiClient.GetCampaignsAsync().GetAwaiter().GetResult();
            var episodes = _apiClient.GetEpisodesAsync().GetAwaiter().GetResult();

            if (seasons.Count == 0 && campaigns.Count == 0 && episodes.Count == 0)
            {
                if (string.IsNullOrWhiteSpace(json) || json == "{}")
                    return null;

                var guide = JsonSerializer.Deserialize<ShowData>(json);
                if (guide == null)
                    return null;

                guide.LinkParents();
                return guide;
            }

            var data = new ShowData { ShowName = "Dungeon Masters" };

            foreach (var seasonDto in seasons)
            {
                var season = new Season
                {
                    Id = TryParseGuid(seasonDto.Id, Guid.NewGuid()),
                    Number = seasonDto.Number,
                    Title = seasonDto.Name,
                    Year = seasonDto.Year ?? "",
                    Description = seasonDto.Description ?? "",
                    LastEdited = seasonDto.UpdatedAt,
                    IsExpanded = true
                };
                data.Seasons.Add(season);
            }

            foreach (var campaignDto in campaigns)
            {
                var season = data.Seasons.FirstOrDefault(s => s.Id == TryParseGuid(campaignDto.SeasonId, Guid.Empty));
                if (season == null)
                    continue;

                var campaign = new Campaign
                {
                    Id = TryParseGuid(campaignDto.Id, Guid.NewGuid()),
                    Number = campaignDto.Number,
                    Title = campaignDto.Name,
                    DungeonMaster = campaignDto.DungeonMaster ?? "",
                    Setting = campaignDto.Setting ?? "",
                    Description = campaignDto.Description ?? "",
                    CoverImage = campaignDto.CoverImage ?? "",
                    FilmingStart = campaignDto.FilmingStart,
                    FilmingEnd = campaignDto.FilmingEnd,
                    FilmingLocation = campaignDto.FilmingLocation ?? "",
                    Season = season,
                    SeasonId = season.Id,
                    LastEdited = campaignDto.UpdatedAt
                };
                foreach (var character in campaignDto.Characters)
                    campaign.Characters.Add(new CastMember
                    {
                        Id = character.Id,
                        PlayerName = character.PlayerName ?? "",
                        CharacterName = character.CharacterName ?? "",
                        CharacterSummary = character.CharacterSummary ?? "",
                        CharacterDescription = character.CharacterDescription ?? "",
                        ImagePath = character.ImagePath ?? ""
                    });
                season.Campaigns.Add(campaign);
            }

            foreach (var episodeDto in episodes)
            {
                var campaign = data.Seasons
                    .SelectMany(s => s.Campaigns)
                    .FirstOrDefault(c => c.Id == TryParseGuid(episodeDto.CampaignId, Guid.Empty));
                if (campaign == null)
                    continue;

                var episode = new Episode
                {
                    Id = TryParseGuid(episodeDto.Id, Guid.NewGuid()),
                    Number = episodeDto.Number,
                    Title = episodeDto.Name,
                    DungeonMaster = episodeDto.DungeonMaster ?? "",
                    DateFilmed = episodeDto.DateFilmed,
                    FilmingLocation = episodeDto.FilmingLocation ?? "",
                    Runtime = episodeDto.Runtime ?? "",
                    Synopsis = episodeDto.Summary ?? "",
                    ProductionNotes = episodeDto.ProductionNotes ?? "",
                    Campaign = campaign,
                    CampaignId = campaign.Id,
                    LastEdited = episodeDto.UpdatedAt
                };
                foreach (var castId in episodeDto.CastIds)
                    episode.CastIds.Add(castId);
                foreach (var guest in episodeDto.GuestCast)
                    episode.GuestCast.Add(new CastMember
                    {
                        Id = guest.Id,
                        PlayerName = guest.PlayerName ?? "",
                        CharacterName = guest.CharacterName ?? "",
                        CharacterSummary = guest.CharacterSummary ?? "",
                        CharacterDescription = guest.CharacterDescription ?? "",
                        ImagePath = guest.ImagePath ?? ""
                    });
                foreach (var moment in episodeDto.KeyMoments)
                    episode.KeyMoments.Add(new KeyMoment
                    {
                        Timestamp = moment.Timestamp ?? "",
                        Description = moment.Description ?? ""
                    });
                foreach (var airDate in episodeDto.AirDates)
                    episode.AirDates.Add(new AirDate
                    {
                        Date = airDate.Date,
                        Platform = airDate.Platform ?? "",
                        Notes = airDate.Notes ?? ""
                    });
                campaign.Episodes.Add(episode);
            }

            data.LinkParents();
            return data;
        }
        catch
        {
            return null;
        }
    }

    private static Guid TryParseGuid(string id, Guid fallback)
    {
        return Guid.TryParse(id, out var guid) ? guid : fallback;
    }

    // If the file couldn't be read, never overwrite it automatically.
    private bool _readOnlyAfterLoadError;

    public string Breadcrumb => CurrentPage switch
    {
        Episode e => $"{e.Campaign?.Season?.DisplayName}  ›  {e.Campaign?.DisplayName}  ›  Episode {e.Number}",
        Campaign c => $"{c.Season?.DisplayName}  ›  {c.DisplayName}",
        CastRosterPage r => $"{r.Campaign.Season?.DisplayName}  ›  {r.Campaign.DisplayName}  ›  Cast & Characters",
        Season s => s.DisplayName,
        ScheduleViewModel => "Chronicle of Dates",
        _ => "Prologue"
    };

    // ============================================================
    // NAVIGATION
    // ============================================================
    [RelayCommand]
    public void Navigate(object? target)
    {
        if (target == null || ReferenceEquals(target, CurrentPage)) return;

        if (IsEditing) Save();
        Navigating?.Invoke();

        // Cast details live on the campaign; make sure the page shows the latest.
        if (target is CastRosterPage roster) roster.RefreshAppearances();
        if (target is Episode episode) episode.RefreshCast();

        CurrentPage = target;

        // Keep the table of contents in step with the page.
        if (target is TreeNode node)
        {
            for (var p = node.ParentNode; p != null; p = p.ParentNode) p.IsExpanded = true;
            _selectedNode = node;
            node.IsSelected = true;
        }
        else
        {
            IsEditing = false;
            if (_selectedNode != null)
            {
                _selectedNode.IsSelected = false;
                _selectedNode = null;
            }
        }

        // Still in edit mode: check nobody else is editing the new page.
        if (IsEditing && !OkToEdit()) IsEditing = false;
        UpdateLock();
        ShowPageInfo();
    }

    [RelayCommand] private void ShowPrologue() => Navigate(new PrologueViewModel(this));
    [RelayCommand] private void ShowSchedule() => Navigate(new ScheduleViewModel(this));

    // ============================================================
    // EDITING / SAVING
    // ============================================================
    [RelayCommand]
    private void ToggleEdit()
    {
        if (!IsEditing && !OkToEdit()) return;
        IsEditing = !IsEditing;
    }

    partial void OnIsEditingChanged(bool value)
    {
        if (!value) Save();
        UpdateLock();
    }

    // True while saving or syncing; message boxes let timers run, so don't start another.
    private bool _busy;

    [RelayCommand]
    public void Save()
    {
        if (_readOnlyAfterLoadError)
        {
            StatusText = "Not saved - the guide file could not be read at startup";
            return;
        }
        if (_busy) return;

        CommitPendingEdits?.Invoke();

        if (_apiClient != null)
        {
            var versionBeforeSave = _apiClient.GuideVersion;
            _busy = true;
            try
            {
                var json = JsonSerializer.Serialize(Data);
                if (CurrentPage is Episode episode)
                {
                    if (!Data.AllEpisodes.Any(item => item.Id == episode.Id))
                    {
                        _apiClient.DeleteEpisodeAsync(episode.Id.ToString()).GetAwaiter().GetResult();
                        StatusText = "Deleted episode from API";
                    }
                    else
                    {
                        try
                        {
                            var savedEpisode = _apiClient.UpdateEpisodeAsync(ToEpisodeDto(episode)).GetAwaiter().GetResult();
                            episode.LastEdited = savedEpisode.UpdatedAt;
                            StatusText = $"Saved episode to API {DateTime.Now:t}";
                        }
                        catch (HttpRequestException ex) when (
                            ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.BadRequest)
                        {
                            SaveGuideToApi(json);
                            StatusText = $"Saved guide to API {DateTime.Now:t}";
                        }
                    }
                }
                else if (CurrentPage is Season season)
                {
                    if (!Data.Seasons.Any(item => item.Id == season.Id))
                    {
                        _apiClient.DeleteSeasonAsync(season.Id.ToString()).GetAwaiter().GetResult();
                        StatusText = "Deleted season from API";
                    }
                    else
                    {
                        try
                        {
                            var savedSeason = _apiClient.UpdateSeasonAsync(ToSeasonDto(season)).GetAwaiter().GetResult();
                            season.LastEdited = savedSeason.UpdatedAt;
                            StatusText = $"Saved season to API {DateTime.Now:t}";
                        }
                        catch (HttpRequestException ex) when (
                            ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.BadRequest)
                        {
                            SaveGuideToApi(json);
                            StatusText = $"Saved guide to API {DateTime.Now:t}";
                        }
                    }
                }
                else if (CurrentPage is Campaign or CastRosterPage)
                {
                    var campaign = CurrentPage is Campaign currentCampaign
                        ? currentCampaign
                        : ((CastRosterPage)CurrentPage).Campaign;
                    if (!Data.AllCampaigns.Any(item => item.Id == campaign.Id))
                    {
                        _apiClient.DeleteCampaignAsync(campaign.Id.ToString()).GetAwaiter().GetResult();
                        StatusText = "Deleted campaign from API";
                    }
                    else
                    {
                        try
                        {
                            var savedCampaign = _apiClient.UpdateCampaignAsync(ToCampaignDto(campaign)).GetAwaiter().GetResult();
                            campaign.LastEdited = savedCampaign.UpdatedAt;
                            StatusText = $"Saved campaign to API {DateTime.Now:t}";
                        }
                        catch (HttpRequestException ex) when (
                            ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.BadRequest)
                        {
                            SaveGuideToApi(json);
                            StatusText = $"Saved guide to API {DateTime.Now:t}";
                        }
                    }
                }
                else
                {
                    SaveGuideToApi(json);
                    StatusText = $"Saved guide to API {DateTime.Now:t}";
                }
                if (versionBeforeSave == _loadedRemoteVersion)
                    _loadedRemoteVersion = _apiClient.GuideVersion;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                StatusText = "Save conflict: the server has newer changes";
                MessageBox.Show(
                    "Someone saved newer changes to the guide. Your edit was not written; the guide will refresh when you finish editing.",
                    "Guide changed on server", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                StatusText = "Save failed";
                MessageBox.Show($"Could not save the guide to the API:\n{ex.Message}", "DMAP Guide", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _busy = false;
            }
            return;
        }

        _busy = true;
        bool tookTheirs = false;
        try
        {
            int written = _store.Save(Data, (page, who) =>
            {
                bool keepMine = AskKeepMine(page, who);
                tookTheirs |= !keepMine;
                return keepMine;
            });
            if (written > 0) StatusText = $"Saved {DateTime.Now:t}";
        }
        catch (Exception ex)
        {
            StatusText = "Save failed";
            MessageBox.Show($"Could not save the guide:\n{ex.Message}", "DMAP Guide", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _busy = false;
        }

        if (tookTheirs) RefreshPage();
    }

    private static EpisodeDto ToEpisodeDto(Episode episode) => new()
    {
        Id = episode.Id.ToString(),
        CampaignId = (episode.Campaign?.Id ?? episode.CampaignId).ToString(),
        Number = episode.Number,
        Name = episode.Title,
        DungeonMaster = episode.DungeonMaster,
        Summary = episode.Synopsis,
        DateFilmed = episode.DateFilmed,
        FilmingLocation = episode.FilmingLocation,
        Runtime = episode.Runtime,
        ProductionNotes = episode.ProductionNotes,
        CastIds = episode.CastIds.ToList(),
        GuestCast = episode.GuestCast.Select(member => new EpisodeCastMemberDto
        {
            Id = member.Id,
            PlayerName = member.PlayerName,
            CharacterName = member.CharacterName,
            CharacterSummary = member.CharacterSummary,
            CharacterDescription = member.CharacterDescription,
            ImagePath = member.ImagePath
        }).ToList(),
        KeyMoments = episode.KeyMoments.Select(moment => new EpisodeKeyMomentDto
        {
            Timestamp = moment.Timestamp,
            Description = moment.Description
        }).ToList(),
        AirDates = episode.AirDates.Select(airDate => new EpisodeAirDateDto
        {
            Date = airDate.Date,
            Platform = airDate.Platform,
            Notes = airDate.Notes
        }).ToList(),
        UpdatedAt = episode.LastEdited
    };

    private static SeasonDto ToSeasonDto(Season season) => new()
    {
        Id = season.Id.ToString(),
        Number = season.Number,
        Name = season.Title,
        Year = season.Year,
        Description = season.Description,
        UpdatedAt = season.LastEdited
    };

    private void CreateRemoteSeason(Season season)
    {
        var versionBeforeCreate = _apiClient!.GuideVersion;
        try
        {
            try
            {
                _apiClient!.CreateSeasonAsync(ToSeasonDto(season)).GetAwaiter().GetResult();
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                SaveGuideToApi(JsonSerializer.Serialize(Data));
            }
            if (season.LastEdited == null)
                season.LastEdited = _apiClient.GetSeasonsAsync().GetAwaiter().GetResult()
                    .FirstOrDefault(item => item.Id == season.Id.ToString())?.UpdatedAt;
            if (versionBeforeCreate == _loadedRemoteVersion)
                _loadedRemoteVersion = _apiClient.GuideVersion;
            StatusText = $"Created season in API {DateTime.Now:t}";
        }
        catch (Exception ex)
        {
            StatusText = "Season creation failed";
            MessageBox.Show($"Could not create the season in the API:\n{ex.Message}", "DMAP Guide", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CreateRemoteCampaign(Campaign campaign)
    {
        var versionBeforeCreate = _apiClient!.GuideVersion;
        try
        {
            try
            {
                _apiClient!.CreateCampaignAsync(ToCampaignDto(campaign)).GetAwaiter().GetResult();
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                SaveGuideToApi(JsonSerializer.Serialize(Data));
            }
            if (campaign.LastEdited == null)
                campaign.LastEdited = _apiClient.GetCampaignsAsync().GetAwaiter().GetResult()
                    .FirstOrDefault(item => item.Id == campaign.Id.ToString())?.UpdatedAt;
            if (versionBeforeCreate == _loadedRemoteVersion)
                _loadedRemoteVersion = _apiClient.GuideVersion;
            StatusText = $"Created campaign in API {DateTime.Now:t}";
        }
        catch (Exception ex)
        {
            StatusText = "Campaign creation failed";
            MessageBox.Show($"Could not create the campaign in the API:\n{ex.Message}", "DMAP Guide", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static CampaignDto ToCampaignDto(Campaign campaign) => new()
    {
        Id = campaign.Id.ToString(),
        SeasonId = (campaign.Season?.Id ?? campaign.SeasonId).ToString(),
        Number = campaign.Number,
        Name = campaign.Title,
        DungeonMaster = campaign.DungeonMaster,
        Setting = campaign.Setting,
        Description = campaign.Description,
        CoverImage = campaign.CoverImage,
        FilmingStart = campaign.FilmingStart,
        FilmingEnd = campaign.FilmingEnd,
        FilmingLocation = campaign.FilmingLocation,
        Characters = campaign.Characters.Select(member => new EpisodeCastMemberDto
        {
            Id = member.Id,
            PlayerName = member.PlayerName,
            CharacterName = member.CharacterName,
            CharacterSummary = member.CharacterSummary,
            CharacterDescription = member.CharacterDescription,
            ImagePath = member.ImagePath
        }).ToList(),
        UpdatedAt = campaign.LastEdited
    };

    private void SaveGuideToApi(string json)
    {
        _apiClient!.SaveGuideJsonAsync(json, _loadedRemoteVersion).GetAwaiter().GetResult();
        _loadedRemoteVersion = _apiClient.GuideVersion;

        var seasons = _apiClient.GetSeasonsAsync().GetAwaiter().GetResult();
        foreach (var season in Data.Seasons)
            season.LastEdited = seasons.FirstOrDefault(item => item.Id == season.Id.ToString())?.UpdatedAt;

        var campaigns = _apiClient.GetCampaignsAsync().GetAwaiter().GetResult();
        foreach (var campaign in Data.AllCampaigns)
            campaign.LastEdited = campaigns.FirstOrDefault(item => item.Id == campaign.Id.ToString())?.UpdatedAt;

        var episodes = _apiClient.GetEpisodesAsync().GetAwaiter().GetResult();
        foreach (var episode in Data.AllEpisodes)
            episode.LastEdited = episodes.FirstOrDefault(item => item.Id == episode.Id.ToString())?.UpdatedAt;
    }

    private static bool AskKeepMine(object page, string who)
    {
        string name = page is TreeNode n ? $"\"{n.DisplayName}\"" : "the show details";
        return MessageBox.Show(
            $"{who} saved changes to {name} while you were editing it.\n\n" +
            "Yes = keep your version (their changes to this page are replaced)\n" +
            "No = keep their version (your changes to this page are lost)",
            "Someone else changed this page", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    /// <summary>Save, and let go of the page being edited (when the window closes).</summary>
    public void Shutdown()
    {
        Save();
        if (_lockedId is Guid id) _store.ReleaseLock(id);
        _lockedId = null;
        _store.Dispose();
    }

    // ============================================================
    // SHARING: other people's changes and who's editing what
    // ============================================================
    private Guid? _lockedId;
    private DateTime _lockSince;

    /// <summary>The saved page the current page edits (the cast page belongs to its campaign).</summary>
    private TreeNode? EditTarget => CurrentPage switch
    {
        CastRosterPage r => r.Campaign,
        TreeNode n => n,
        _ => null
    };

    /// <summary>Mark the page being edited so other people get a warning; clear it when done.</summary>
    private void UpdateLock()
    {
        Guid? wanted = IsEditing ? EditTarget?.Id : null;
        if (wanted == _lockedId) return;

        if (_lockedId is Guid old) _store.ReleaseLock(old);
        _lockedId = wanted;
        _lockSince = DateTime.UtcNow;
        if (wanted is Guid id) _store.WriteLock(id, _lockSince);
    }

    private bool OkToEdit()
    {
        if (EditTarget is not TreeNode node || _store.ReadLock(node.Id) is not { IsSomeoneElse: true } other)
            return true;

        return MessageBox.Show(
            $"{other.User} is editing \"{node.DisplayName}\" right now (since {other.Since.ToLocalTime():t}).\n\n" +
            "If you both change it, whoever saves second will be asked whose version to keep.\n\nEdit it anyway?",
            "Someone else is editing", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    /// <summary>Status line: who's editing this page, or who last changed it.</summary>
    private void ShowPageInfo()
    {
        if (EditTarget is not TreeNode node) return;

        if (_store.ReadLock(node.Id) is { IsSomeoneElse: true } other)
            StatusText = $"✎ {other.User} is editing this page";
        else if (node.LastEdited is DateTime when && node.LastEditedBy.Length > 0)
            StatusText = $"Last changed by {node.LastEditedBy}, {when.ToLocalTime():g}";
        else
            StatusText = "";
    }

    /// <summary>Pick up pages other people have saved.</summary>
    private void SyncNow()
    {
        if (_busy || _readOnlyAfterLoadError) return;

        if (_apiClient != null)
        {
            SyncRemoteNow();
            return;
        }

        _busy = true;
        SyncResult result;
        try
        {
            result = _store.Sync(Data);
        }
        finally
        {
            _busy = false;
        }

        // The page being viewed was deleted by someone else.
        var gone = result.Removed.FirstOrDefault(n =>
            EditTarget is TreeNode t && (t == n || t.ParentNode == n || t.ParentNode?.ParentNode == n));
        if (gone != null)
        {
            IsEditing = false;
            _selectedNode = null;
            ShowPrologue();
            StatusText = $"\"{gone.DisplayName}\" was deleted by someone else";
            return;
        }

        if (result.Changed) RefreshPage();
        ShowPageInfo();
    }

    private void SyncRemoteNow()
    {
        _busy = true;
        try
        {
            _apiClient!.GetGuideJsonAsync().GetAwaiter().GetResult();
            var version = _apiClient.GuideVersion;
            if (!version.HasValue || version == _loadedRemoteVersion)
                return;

            if (IsEditing)
            {
                StatusText = "Remote changes detected. Finish editing to load them; a conflicting save will be rejected.";
                return;
            }

            var remoteData = TryLoadFromApi();
            if (remoteData == null)
                return;
            _loadedRemoteVersion = _apiClient.GuideVersion ?? version;

            var oldPage = CurrentPage;
            object? nextPage = oldPage switch
            {
                Season season => remoteData.Seasons.FirstOrDefault(item => item.Id == season.Id),
                Campaign campaign => remoteData.AllCampaigns.FirstOrDefault(item => item.Id == campaign.Id),
                CastRosterPage roster => remoteData.AllCampaigns.FirstOrDefault(item => item.Id == roster.Campaign.Id)?.RosterPage,
                Episode episode => remoteData.AllEpisodes.FirstOrDefault(item => item.Id == episode.Id),
                ScheduleViewModel => new ScheduleViewModel(this),
                _ => new PrologueViewModel(this)
            };

            Data = remoteData;
            _loadedRemoteVersion = version;
            Navigate(nextPage ?? new PrologueViewModel(this));
            StatusText = $"Synced from API {DateTime.Now:t}";
        }
        catch (Exception ex)
        {
            StatusText = $"API sync failed: {ex.Message}";
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Redraw pages built from the whole guide (the prologue and schedule) after changes.</summary>
    private void RefreshPage()
    {
        switch (CurrentPage)
        {
            case PrologueViewModel:
                CurrentPage = new PrologueViewModel(this);
                break;
            case ScheduleViewModel schedule:
                schedule.Rebuild();
                break;
            case CastRosterPage roster:
                roster.RefreshAppearances();
                break;
            case Episode episode:
                episode.RefreshCast();
                break;
        }
    }

    // ============================================================
    // ADD / DELETE
    // ============================================================
    [RelayCommand]
    private void AddSeason()
    {
        int number = Data.Seasons.Select(s => s.Number).DefaultIfEmpty(0).Max() + 1;
        var season = new Season { Number = number, IsExpanded = true };
        Save();
        Data.Seasons.Add(season);
        if (_apiClient != null)
            CreateRemoteSeason(season);
        else
            Save();
        Navigate(season);
        IsEditing = true;
    }

    [RelayCommand]
    private void AddCampaign()
    {
        var season = CurrentPage switch
        {
            Season s => s,
            Campaign c => c.Season,
            CastRosterPage r => r.Campaign.Season,
            Episode e => e.Campaign?.Season,
            _ => null
        } ?? Data.Seasons.LastOrDefault();

        if (season == null)
        {
            MessageBox.Show("Add a season first.", "DMAP Guide");
            return;
        }

        // Campaign numbers run across the whole show (1, 2, 3...).
        int number = Data.AllCampaigns.Select(c => c.Number).DefaultIfEmpty(0).Max() + 1;
        var campaign = new Campaign { Number = number, Season = season, IsExpanded = true };
        Save();
        season.Campaigns.Add(campaign);
        if (_apiClient != null)
            CreateRemoteCampaign(campaign);
        else
            Save();
        Navigate(campaign);
        IsEditing = true;
    }

    [RelayCommand]
    private void AddEpisode(Campaign? target)
    {
        var campaign = target ?? CurrentPage switch
        {
            Campaign c => c,
            CastRosterPage r => r.Campaign,
            Episode e => e.Campaign,
            Season s => s.Campaigns.LastOrDefault(),
            _ => null
        } ?? Data.AllCampaigns.LastOrDefault();

        if (campaign == null)
        {
            MessageBox.Show("Add a campaign first.", "DMAP Guide");
            return;
        }

        // Episodes are numbered <campaign><nn>, e.g. 301, 302 for campaign 3.
        var last = campaign.Episodes.OrderBy(e => e.Number).LastOrDefault();
        var episode = new Episode
        {
            Number = last != null ? last.Number + 1 : campaign.Number * 100 + 1,
            Campaign = campaign,
            DungeonMaster = last?.DungeonMaster is { Length: > 0 } dm ? dm : campaign.DungeonMaster,
            FilmingLocation = last?.FilmingLocation ?? ""
        };

        // The cast carries over from the previous episode; the first episode
        // starts with everyone on the campaign's cast list.
        var castIds = last != null ? last.CastIds.ToList() : campaign.Characters.Select(c => c.Id).ToList();
        foreach (var id in castIds) episode.CastIds.Add(id);

        episode.Attach();
        Save();
        campaign.Episodes.Add(episode);
        campaign.IsExpanded = true;
        if (_apiClient != null)
        {
            var versionBeforeCreate = _apiClient.GuideVersion;
            try
            {
                var savedEpisode = _apiClient.CreateEpisodeAsync(ToEpisodeDto(episode)).GetAwaiter().GetResult();
                episode.LastEdited = savedEpisode.UpdatedAt;
                if (versionBeforeCreate == _loadedRemoteVersion)
                    _loadedRemoteVersion = _apiClient.GuideVersion;
                StatusText = $"Created episode in API {DateTime.Now:t}";
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                SaveGuideToApi(JsonSerializer.Serialize(Data));
                StatusText = $"Saved guide to API {DateTime.Now:t}";
            }
            catch (Exception ex)
            {
                StatusText = "Episode creation failed";
                MessageBox.Show($"Could not create the episode in the API:\n{ex.Message}", "DMAP Guide", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            Save();
        }
        Navigate(episode);
        IsEditing = true;
    }

    [RelayCommand]
    private void DeleteCurrent()
    {
        if (CurrentPage is not TreeNode node || node is CastRosterPage)
        {
            MessageBox.Show(CurrentPage is CastRosterPage
                    ? "The Cast & Characters page belongs to its campaign and can't be deleted. To remove a character, press Edit Page and use the ✕ on their card."
                    : "Open the season, campaign or episode you want to delete first.",
                "DMAP Guide");
            return;
        }

        string extra = node switch
        {
            Season s when s.Campaigns.Count > 0 => $"\n\nThis also deletes its {s.Campaigns.Count} campaign(s) and all their episodes.",
            Campaign c when c.Episodes.Count > 0 => $"\n\nThis also deletes its {c.Episodes.Count} episode(s).",
            _ => ""
        };

        if (MessageBox.Show($"Delete \"{node.DisplayName}\"?{extra}", "Delete page",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var parent = node.ParentNode;
        switch (node)
        {
            case Season s: Data.Seasons.Remove(s); break;
            case Campaign c: c.Season?.Campaigns.Remove(c); break;
            case Episode e: e.Campaign?.Episodes.Remove(e); break;
        }

        _selectedNode = null;
        Save();
        if (parent != null) Navigate(parent);
        else ShowPrologue();
    }

    // ============================================================
    // EPISODE LISTS
    // ============================================================
    [RelayCommand]
    private void AddCharacter(Campaign campaign)
    {
        var member = new CastMember();
        campaign.Characters.Add(member);
        campaign.RosterPage.RefreshAppearances();
    }

    [RelayCommand]
    private void OpenRoster(Campaign campaign) => Navigate(campaign.RosterPage);

    [RelayCommand] private void AddGuest(Episode ep) => ep.GuestCast.Add(new CastMember());
    [RelayCommand] private void AddKeyMoment(Episode ep) => ep.KeyMoments.Add(new KeyMoment());
    [RelayCommand] private void AddAirDate(Episode ep) => ep.AirDates.Add(new AirDate { Date = DateTime.Today });

    [RelayCommand]
    private void RemoveCast(CastMember member)
    {
        switch (CurrentPage)
        {
            // On an episode: take them out of this episode (they stay on the campaign's list).
            case Episode ep:
                if (ep.CastIds.Contains(member.Id))
                {
                    ep.SetInCast(member, false);
                    ep.RefreshCast(); // untick their box too
                }
                else ep.GuestCast.Remove(member);
                break;

            // On the cast page: remove the character from the campaign and all its episodes.
            case CastRosterPage roster:
                var campaign = roster.Campaign;
                int count = campaign.Episodes.Count(e => e.CastIds.Contains(member.Id));
                string name = string.IsNullOrWhiteSpace(member.CharacterName) ? "this character" : member.CharacterName;
                string extra = count > 0 ? $"\n\nThey'll also be removed from the {count} episode(s) they appear in." : "";

                if (MessageBox.Show($"Remove {name} from {campaign.DisplayName}?{extra}", "Remove character",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;

                foreach (var e in campaign.Episodes) e.CastIds.Remove(member.Id);
                campaign.Characters.Remove(member);
                break;
        }
    }

    [RelayCommand]
    private void RemoveKeyMoment(KeyMoment moment)
    {
        if (CurrentPage is Episode ep) ep.KeyMoments.Remove(moment);
    }

    [RelayCommand]
    private void RemoveAirDate(AirDate date)
    {
        if (CurrentPage is Episode ep) ep.AirDates.Remove(date);
    }

    // ============================================================
    // PICTURES
    // ============================================================
    [RelayCommand]
    private void ChoosePortrait(CastMember member)
    {
        string? path = PickImage();
        if (path != null) member.ImagePath = path;
    }

    [RelayCommand]
    private void ChooseCover(Campaign campaign)
    {
        string? path = PickImage();
        if (path != null) campaign.CoverImage = path;
    }

    private string? PickImage()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose a picture",
            Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*"
        };
        if (dlg.ShowDialog() != true) return null;

        try
        {
            return _store.ImportImage(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not add the picture:\n{ex.Message}", "DMAP Guide");
            return null;
        }
    }

    // ============================================================
    // DATA FOLDER
    // ============================================================
    [RelayCommand]
    private void ChangeDataFolder()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Choose where the guide is kept (e.g. a shared OneDrive folder)",
            InitialDirectory = _store.Folder
        };
        if (dlg.ShowDialog() != true || dlg.FolderName == _store.Folder) return;

        string folder = dlg.FolderName;

        if (DataStore.HasData(folder))
        {
            var r = MessageBox.Show(
                "That folder already has a DMAP Guide. Open it?\n\n" +
                "Yes = switch to that guide\nNo = cancel",
                "Change data folder", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
        }
        else
        {
            var r = MessageBox.Show(
                "Copy this guide and its pictures to the new folder and use it from now on?",
                "Change data folder", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            Save();
            try { _store.CopyTo(folder, Data); }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not copy the guide:\n{ex.Message}", "DMAP Guide");
                return;
            }
        }

        _settings.DataFolder = folder;
        _settings.Save();

        IsEditing = false;
        if (_lockedId is Guid id) _store.ReleaseLock(id);
        _lockedId = null;
        _store.Dispose();
        _store = OpenStore(folder);
        _readOnlyAfterLoadError = false;
        _selectedNode = null;
        Data = LoadOrSeed();
        OnPropertyChanged(nameof(DataFolder));
        ShowPrologue();
        StatusText = $"Using {folder}";
    }
}
