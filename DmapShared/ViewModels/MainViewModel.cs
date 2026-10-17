using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmapShared.Api;
using DmapShared.Models;
using DmapShared.Storage;
using System.Text.Json;

namespace DmapShared.ViewModels;

/// <summary>
/// Everything the tome does, for both the desktop and the phone app: which page is
/// open, reading and editing, saving, picking up other people's changes, and adding
/// and deleting pages. Each app only draws the pages.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IPlatformServices _platform;
    private readonly ISettingsStore _settingsStore;
    private readonly string? _folderOverride;
    private GuideSettings _settings = new();
    private IGuideStore? _store;
    private TreeNode? _selectedNode;

    /// <summary>One save or sync at a time; message boxes let timers run, so they wait or skip.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    [ObservableProperty] private ShowData _data = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Breadcrumb))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    private object? _currentPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanChangePages))]
    private bool _isConnected;

    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _statusText = "";

    /// <summary>Set by the window: pushes the value of the text box being typed in.</summary>
    public Action? CommitPendingEdits { get; set; }

    /// <summary>Raised just before the page changes (the window snapshots it for the page-turn).</summary>
    public event Action? Navigating;

    public IPlatformServices Platform => _platform;

    /// <summary>Where the guide is kept, as shown to the user.</summary>
    public string Location => _store?.Location ?? "";

    public bool UsesServer => _store is ServerGuideStore;

    public bool CanEdit => IsConnected && !_readOnlyAfterLoadError && CurrentPage is TreeNode;

    /// <summary>Adding and deleting pages.</summary>
    public bool CanChangePages => IsConnected && !_readOnlyAfterLoadError;

    public string UserName => _settings.UserName;

    // If the guide couldn't be read, never overwrite it automatically.
    private bool _readOnlyAfterLoadError;

    /// <param name="folderOverride">Open this folder for this run only (desktop "--data folder").</param>
    public MainViewModel(IPlatformServices platform, ISettingsStore settingsStore, string? folderOverride = null)
    {
        _platform = platform;
        _settingsStore = settingsStore;
        _folderOverride = folderOverride;
        _currentPage = new PrologueViewModel(this);
    }

    /// <summary>Open the guide named in the settings and start checking for other people's changes.</summary>
    public async Task StartAsync()
    {
        _settings = await _settingsStore.LoadAsync();

        // Pick up other people's changes; the folder watcher also catches most of them as they happen.
        _platform.StartTimer(TimeSpan.FromSeconds(30), () => _ = SyncNowAsync());

        // Keep "you're editing this page" fresh for other people.
        _platform.StartTimer(TimeSpan.FromMinutes(2), () =>
        {
            if (_lockedId is Guid id && _store != null) _ = _store.WriteLockAsync(id, _lockSince);
        });

        bool nothingChosenYet = _folderOverride == null && (_settings.UseServer
            ? string.IsNullOrWhiteSpace(_settings.ServerAddress)
            : !_platform.SupportsFolders || string.IsNullOrWhiteSpace(_settings.DataFolder));
        if (nothingChosenYet)
        {
            ShowUnavailable("Connect to the shared guide",
                "Enter the shared guide's server address and access key in Settings. The person who runs the shared guide can give you both.");
            return;
        }

        await OpenAsync(_settings, _folderOverride);
    }

    // ============================================================
    // OPENING A GUIDE
    // ============================================================
    /// <summary>Tests: the client to open the next server guide with.</summary>
    internal DmapApiClient? TestClient { get; set; }

    private IGuideStore CreateStore(GuideSettings settings, string? folderOverride)
    {
        if (settings.UseServer && folderOverride == null)
        {
            var client = TestClient ?? new DmapApiClient(settings.ServerAddress, settings.AccessKey, settings.UserName);
            TestClient = null;
            string cache = Path.Combine(_platform.CacheFolder, "images", SafeName(client.BaseAddress.Authority));
            return new ServerGuideStore(client, settings.UserName, cache);
        }
        return new FolderGuideStore(folderOverride ?? settings.DataFolder, settings.UserName);
    }

    private static string SafeName(string text) =>
        string.Concat(text.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_'));

    /// <summary>Open a guide. If it can't be reached, a page saying so is shown instead and false is returned.</summary>
    private async Task<bool> OpenAsync(GuideSettings settings, string? folderOverride = null)
    {
        IGuideStore store;
        try
        {
            store = CreateStore(settings, folderOverride);
        }
        catch (DmapApiException ex)
        {
            ShowUnavailable("The shared guide couldn't be opened", ex.Message);
            return false;
        }

        StatusText = store is ServerGuideStore ? "Opening the shared guide…" : "";
        bool readOnly = false;
        GuideLoadResult loaded;
        try
        {
            loaded = await store.LoadAsync();
        }
        catch (Exception ex) when (store is ServerGuideStore)
        {
            store.Dispose();
            ShowUnavailable("The shared guide couldn't be reached", Friendly(ex));
            return false;
        }
        catch (Exception ex)
        {
            await _platform.AlertAsync("DMAP Guide",
                $"The guide in {store.Location} could not be read:\n{ex.Message}\n\n" +
                "Starting with an empty guide. Your files have not been changed.");
            loaded = new GuideLoadResult(ShowData.CreateSeed(), new());
            readOnly = true;
        }

        await _gate.WaitAsync();
        try
        {
            await CloseStoreAsync();
            _store = store;
            GuideImages.Store = store;
            store.ChangesWaiting += () => _platform.BeginInvoke(() => _ = SyncNowAsync());
            store.ImagesArrived += () => _platform.BeginInvoke(() =>
            {
                Data.RefreshImages();
                GuideImages.RaiseArrived();
            });
            store.StartWatching();

            _settings = settings;
            _readOnlyAfterLoadError = readOnly;
            _selectedNode = null;
            IsEditing = false;
            Data = loaded.Data;
            OpenAtCurrentCampaign();
            IsConnected = true;
        }
        finally
        {
            _gate.Release();
        }

        OnPropertyChanged(nameof(Location));
        OnPropertyChanged(nameof(UsesServer));
        OnPropertyChanged(nameof(CanChangePages));
        Navigating?.Invoke();
        CurrentPage = new PrologueViewModel(this);
        StatusText = store is ServerGuideStore ? "Connected to the shared guide" : "";

        if (loaded.Unreadable.Count > 0)
            await _platform.AlertAsync("DMAP Guide",
                $"{loaded.Unreadable.Count} page(s) in {store.Location} could not be read and are not shown:\n\n" +
                string.Join("\n", loaded.Unreadable.Take(10)) +
                (store is FolderGuideStore
                    ? "\n\nThose files have not been changed. OneDrive's version history can restore an earlier copy."
                    : "\n\nThey have not been changed on the server."));
        return true;
    }

    /// <summary>Open the table of contents at the campaign that's on air.</summary>
    private void OpenAtCurrentCampaign()
    {
        if (Data.CurrentCampaign is Campaign current)
        {
            current.IsExpanded = true;
            if (current.Season != null) current.Season.IsExpanded = true;
        }
    }

    private async Task CloseStoreAsync()
    {
        if (_store == null) return;
        if (_lockedId is Guid id) await _store.ReleaseLockAsync(id);
        _lockedId = null;
        _store.Dispose();
        _store = null;
        GuideImages.Store = null;
    }

    private void ShowUnavailable(string title, string message)
    {
        IsConnected = false;
        IsEditing = false;
        _selectedNode = null;
        Data = new ShowData();
        Navigating?.Invoke();
        CurrentPage = new GuideUnavailablePage(this, title, message);
        StatusText = "";
    }

    [RelayCommand]
    private async Task ReconnectAsync()
    {
        _settings = await _settingsStore.LoadAsync();
        await OpenAsync(_settings, _folderOverride);
    }

    public static string Friendly(Exception ex) => ex switch
    {
        DmapApiException api => api.Message,
        _ => ex.Message
    };

    public string Breadcrumb => CurrentPage switch
    {
        Episode e => $"{e.Campaign?.Season?.DisplayName}  ›  {e.Campaign?.DisplayName}  ›  Episode {e.Number}",
        Campaign c => $"{c.Season?.DisplayName}  ›  {c.DisplayName}",
        CastRosterPage r => $"{r.Campaign.Season?.DisplayName}  ›  {r.Campaign.DisplayName}  ›  Cast & Characters",
        Season s => s.DisplayName,
        ScheduleViewModel => "Chronicle of Dates",
        SettingsViewModel => "Settings",
        GuideUnavailablePage => "",
        _ => "Prologue"
    };

    // ============================================================
    // NAVIGATION
    // ============================================================
    [RelayCommand]
    public async Task NavigateAsync(object? target)
    {
        if (target == null || ReferenceEquals(target, CurrentPage)) return;

        if (IsEditing) await SaveAsync();
        Navigating?.Invoke();

        // Cast details live on the campaign; make sure the page shows the latest.
        if (target is CastRosterPage roster) roster.RefreshAppearances();
        if (target is Episode episode) episode.RefreshCast();

        CurrentPage = target;

        // Keep the table of contents in step with the page.
        if (target is TreeNode node)
        {
            for (var p = node.ParentNode; p != null; p = p.ParentNode) p.IsExpanded = true;
            if (_selectedNode != null && _selectedNode != node) _selectedNode.IsSelected = false;
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
        if (IsEditing && !await OkToEditAsync()) IsEditing = false;
        await UpdateLockAsync();
        await ShowPageInfoAsync();
    }

    [RelayCommand] private Task ShowPrologueAsync() => NavigateAsync(new PrologueViewModel(this));
    [RelayCommand] private Task ShowScheduleAsync() => NavigateAsync(new ScheduleViewModel(this));
    [RelayCommand] private Task ShowSettingsAsync() => NavigateAsync(new SettingsViewModel(this, _settings.Clone()));

    // ============================================================
    // EDITING / SAVING
    // ============================================================
    [RelayCommand]
    private async Task ToggleEditAsync()
    {
        if (!CanEdit) return;
        if (!IsEditing && !await OkToEditAsync()) return;
        IsEditing = !IsEditing;
    }

    partial void OnIsEditingChanged(bool value)
    {
        if (!value) _ = SaveAsync();
        _ = UpdateLockAsync();
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (_store == null || !IsConnected) return;
        if (_readOnlyAfterLoadError)
        {
            StatusText = "Not saved - the guide could not be read at startup";
            return;
        }

        CommitPendingEdits?.Invoke();

        await _gate.WaitAsync();
        bool tookTheirs = false;
        try
        {
            int written = await _store.SaveAsync(Data, async (page, who) =>
            {
                bool keepMine = await AskKeepMineAsync(page, who);
                tookTheirs |= !keepMine;
                return keepMine;
            });
            if (written > 0) StatusText = $"Saved {DateTime.Now:t}";
        }
        catch (Exception ex)
        {
            StatusText = "Not saved";
            await _platform.AlertAsync("DMAP Guide",
                $"Could not save the guide:\n{Friendly(ex)}\n\n" +
                "Your changes are still here, and will be saved the next time you press Done or Save.");
        }
        finally
        {
            _gate.Release();
        }

        if (tookTheirs) RefreshPage();
    }

    private Task<bool> AskKeepMineAsync(object page, string who)
    {
        string name = page is TreeNode n ? $"\"{n.DisplayName}\"" : "the show details";
        return _platform.AskAsync("Someone else changed this page",
            $"{who} saved changes to {name} while you were editing it.\n\n" +
            "Keep your version (their changes to this page are replaced), or keep theirs (your changes to this page are lost)?",
            "Keep mine", "Keep theirs");
    }

    /// <summary>Save, and let go of the page being edited (when the app closes).</summary>
    public async Task ShutdownAsync()
    {
        await SaveAsync();
        await _gate.WaitAsync();
        try
        {
            await CloseStoreAsync();
        }
        finally
        {
            _gate.Release();
        }
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
    private async Task UpdateLockAsync()
    {
        if (_store == null) return;
        Guid? wanted = IsEditing ? EditTarget?.Id : null;
        if (wanted == _lockedId) return;

        var old = _lockedId;
        _lockedId = wanted;
        _lockSince = DateTime.UtcNow;
        if (old is Guid previous) await _store.ReleaseLockAsync(previous);
        if (wanted is Guid id) await _store.WriteLockAsync(id, _lockSince);
    }

    private async Task<bool> OkToEditAsync()
    {
        if (_store == null || EditTarget is not TreeNode node ||
            await _store.ReadLockAsync(node.Id) is not EditLock other || !other.IsSomeoneElse(UserName))
            return true;

        return await _platform.AskAsync("Someone else is editing",
            $"{other.User} is editing \"{node.DisplayName}\" right now (since {other.Since.ToLocalTime():t}).\n\n" +
            "If you both change it, whoever saves second will be asked whose version to keep.\n\nEdit it anyway?",
            "Edit anyway", "Cancel");
    }

    /// <summary>Status line: who's editing this page, or who last changed it.</summary>
    private async Task ShowPageInfoAsync()
    {
        if (_store == null || EditTarget is not TreeNode node) return;

        if (await _store.ReadLockAsync(node.Id) is EditLock other && other.IsSomeoneElse(UserName))
            StatusText = $"✎ {other.User} is editing this page";
        else if (node.LastEdited is DateTime when && node.LastEditedBy.Length > 0)
            StatusText = $"Last changed by {node.LastEditedBy}, {when.ToLocalTime():g}";
        else
            StatusText = "";
    }

    private bool _syncFailing;

    /// <summary>Pick up pages other people have saved.</summary>
    public async Task SyncNowAsync()
    {
        if (_store == null || !IsConnected || _readOnlyAfterLoadError) return;
        if (!await _gate.WaitAsync(0)) return;

        SyncResult result;
        try
        {
            result = await _store.SyncAsync(Data);
            if (_syncFailing) StatusText = "Connected to the shared guide again";
            _syncFailing = false;
        }
        catch (Exception ex)
        {
            if (!_syncFailing) StatusText = $"Can't check for other people's changes right now: {Friendly(ex)}";
            _syncFailing = true;
            return;
        }
        finally
        {
            _gate.Release();
        }

        // The page being viewed was deleted by someone else.
        var gone = result.Removed.FirstOrDefault(n =>
            EditTarget is TreeNode t && (t == n || t.ParentNode == n || t.ParentNode?.ParentNode == n));
        if (gone != null)
        {
            IsEditing = false;
            _selectedNode = null;
            await ShowPrologueAsync();
            StatusText = $"\"{gone.DisplayName}\" was deleted by someone else";
            return;
        }

        if (result.Changed) RefreshPage();
        await ShowPageInfoAsync();
    }

    /// <summary>Raised when pages built from the whole guide need redrawing (the phone's contents list listens).</summary>
    public event Action? GuideChanged;

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
        GuideChanged?.Invoke();
    }

    // ============================================================
    // ADD / DELETE
    // ============================================================
    [RelayCommand]
    private async Task AddSeasonAsync()
    {
        if (!CanChangePages) return;
        int number = Data.Seasons.Select(s => s.Number).DefaultIfEmpty(0).Max() + 1;
        var season = new Season { Number = number, IsExpanded = true };
        await SaveAsync();
        Data.Seasons.Add(season);
        await SaveAsync();
        GuideChanged?.Invoke();
        await NavigateAsync(season);
        IsEditing = true;
    }

    [RelayCommand]
    private async Task AddCampaignAsync()
    {
        if (!CanChangePages) return;
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
            await _platform.AlertAsync("DMAP Guide", "Add a season first.");
            return;
        }

        // Campaign numbers run across the whole show (1, 2, 3...).
        int number = Data.AllCampaigns.Select(c => c.Number).DefaultIfEmpty(0).Max() + 1;
        var campaign = new Campaign { Number = number, Season = season, IsExpanded = true };
        await SaveAsync();
        season.Campaigns.Add(campaign);
        season.IsExpanded = true;
        await SaveAsync();
        GuideChanged?.Invoke();
        await NavigateAsync(campaign);
        IsEditing = true;
    }

    [RelayCommand]
    private async Task AddEpisodeAsync(Campaign? target)
    {
        if (!CanChangePages) return;
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
            await _platform.AlertAsync("DMAP Guide", "Add a campaign first.");
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
        await SaveAsync();
        campaign.Episodes.Add(episode);
        campaign.IsExpanded = true;
        if (campaign.Season != null) campaign.Season.IsExpanded = true;
        await SaveAsync();
        GuideChanged?.Invoke();
        await NavigateAsync(episode);
        IsEditing = true;
    }

    [RelayCommand]
    private async Task DeleteCurrentAsync()
    {
        if (!CanChangePages) return;
        if (CurrentPage is not TreeNode node || node is CastRosterPage)
        {
            await _platform.AlertAsync("DMAP Guide", CurrentPage is CastRosterPage
                ? "The Cast & Characters page belongs to its campaign and can't be deleted. To remove a character, press Edit Page and use the ✕ on their card."
                : "Open the season, campaign or episode you want to delete first.");
            return;
        }

        string extra = node switch
        {
            Season s when s.Campaigns.Count > 0 => $"\n\nThis also deletes its {s.Campaigns.Count} campaign(s) and all their episodes.",
            Campaign c when c.Episodes.Count > 0 => $"\n\nThis also deletes its {c.Episodes.Count} episode(s).",
            _ => ""
        };

        if (!await _platform.AskAsync("Delete page", $"Delete \"{node.DisplayName}\"?{extra}", "Delete", "Cancel"))
            return;

        var parent = node.ParentNode;
        switch (node)
        {
            case Season s: Data.Seasons.Remove(s); break;
            case Campaign c: c.Season?.Campaigns.Remove(c); break;
            case Episode e:
                e.Campaign?.Episodes.Remove(e);
                e.Campaign?.RosterPage.RefreshAppearances();
                break;
        }

        _selectedNode = null;
        await SaveAsync();
        GuideChanged?.Invoke();
        if (parent != null) await NavigateAsync(parent);
        else await ShowPrologueAsync();
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
    private Task OpenRosterAsync(Campaign campaign) => NavigateAsync(campaign.RosterPage);

    [RelayCommand] private void AddGuest(Episode ep) => ep.GuestCast.Add(new CastMember());
    [RelayCommand] private void AddKeyMoment(Episode ep) => ep.KeyMoments.Add(new KeyMoment());
    [RelayCommand] private void AddAirDate(Episode ep) => ep.AirDates.Add(new AirDate { Date = DateTime.Today });

    [RelayCommand]
    private async Task RemoveCastAsync(CastMember member)
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

                if (!await _platform.AskAsync("Remove character", $"Remove {name} from {campaign.DisplayName}?{extra}", "Remove", "Cancel"))
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
    private async Task ChoosePortraitAsync(CastMember member)
    {
        if (await PickImageAsync() is string path) member.ImagePath = path;
    }

    [RelayCommand]
    private async Task ChooseCoverAsync(Campaign campaign)
    {
        if (await PickImageAsync() is string path) campaign.CoverImage = path;
    }

    private async Task<string?> PickImageAsync()
    {
        if (_store == null || !IsEditing) return null;
        string? file = await _platform.PickImageAsync();
        if (file == null) return null;

        try
        {
            StatusText = "Adding the picture…";
            string stored = await _store.ImportImageAsync(file);
            StatusText = "";
            return stored;
        }
        catch (Exception ex)
        {
            StatusText = "";
            await _platform.AlertAsync("DMAP Guide", $"Could not add the picture:\n{Friendly(ex)}");
            return null;
        }
    }

    // ============================================================
    // SETTINGS: where the guide lives
    // ============================================================
    /// <summary>Switch to the guide named in <paramref name="next"/>. Returns a problem to show, or null when done.</summary>
    internal async Task<string?> ApplySettingsAsync(GuideSettings next)
    {
        next.UserName = next.UserName.Trim();
        if (next.UserName.Length == 0) return "Enter your name. It's shown on the pages you change.";

        if (next.UseServer)
        {
            if (!DmapApiClient.TryNormalizeAddress(next.ServerAddress, out var address))
                return "The server address isn't valid. It should look like https://name.tailnet.ts.net/";
            if (string.IsNullOrWhiteSpace(next.AccessKey)) return "Enter the access key.";
            next.ServerAddress = address.ToString();
            next.AccessKey = next.AccessKey.Trim();
        }
        else if (string.IsNullOrWhiteSpace(next.DataFolder))
        {
            return "Choose the folder the guide is kept in.";
        }

        if (IsEditing) IsEditing = false;
        await SaveAsync();

        if (next.UseServer)
        {
            // Check the address and key work before leaving the guide that's open.
            using var client = new DmapApiClient(next.ServerAddress, next.AccessKey, next.UserName);
            GuideSnapshot snapshot;
            try
            {
                await client.GetInfoAsync();
                snapshot = await client.GetGuideAsync();
            }
            catch (DmapApiException ex)
            {
                return ex.Message;
            }

            if (snapshot.Records.Count == 0 && _store is FolderGuideStore folder && IsConnected && !_readOnlyAfterLoadError &&
                await _platform.AskAsync("The shared guide is empty",
                    "The shared server doesn't have a guide yet. Copy the guide on this computer to it, so everyone can use it?",
                    "Copy it", "Start a new guide"))
            {
                try
                {
                    StatusText = "Copying the guide to the shared server…";
                    await CopyGuideToServerAsync(folder, next);
                }
                catch (Exception ex)
                {
                    StatusText = "";
                    return $"The guide couldn't be copied to the server: {Friendly(ex)}";
                }
            }
        }

        if (!await OpenAsync(next)) return null; // the page saying why is already showing
        await _settingsStore.SaveAsync(next);
        _settings = next;
        OnPropertyChanged(nameof(UserName));
        return null;
    }

    /// <summary>Upload the guide open from a folder, with its pictures, to an empty server.</summary>
    private async Task CopyGuideToServerAsync(FolderGuideStore folder, GuideSettings target)
    {
        // Work on a copy, so the folder's guide is untouched if anything goes wrong.
        var copy = JsonSerializer.Deserialize<ShowData>(JsonSerializer.Serialize(Data))!;
        copy.LinkParents();

        var client = new DmapApiClient(target.ServerAddress, target.AccessKey, target.UserName);
        using var server = new ServerGuideStore(client, target.UserName,
            Path.Combine(_platform.CacheFolder, "images", SafeName(client.BaseAddress.Authority)));

        var uploaded = new Dictionary<string, string>();
        async Task<string> Upload(string stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return stored;
            if (uploaded.TryGetValue(stored, out var done)) return done;
            if (folder.ResolveImage(stored) is not string file) return stored;
            return uploaded[stored] = await server.ImportImageAsync(file);
        }

        foreach (var c in copy.AllCampaigns)
        {
            c.CoverImage = await Upload(c.CoverImage);
            foreach (var m in c.Characters) m.ImagePath = await Upload(m.ImagePath);
            foreach (var e in c.Episodes)
                foreach (var g in e.GuestCast) g.ImagePath = await Upload(g.ImagePath);
        }

        await server.SaveAsync(copy, (_, _) => Task.FromResult(true));
    }

    /// <summary>Desktop: move to (or open) a different folder, as chosen on the Settings page.</summary>
    internal async Task<string?> ChooseFolderAsync(GuideSettings next)
    {
        string? folder = await _platform.PickFolderAsync(
            "Choose where the guide is kept (e.g. a shared OneDrive folder)",
            _store is FolderGuideStore current ? current.Folder : next.DataFolder);
        if (folder == null) return null;

        if (FolderGuideStore.HasData(folder))
        {
            if (!await _platform.AskAsync("Change data folder", "That folder already has a DMAP Guide. Open it?", "Open it", "Cancel"))
                return null;
        }
        else if (_store is FolderGuideStore store && IsConnected)
        {
            if (!await _platform.AskAsync("Change data folder",
                    "Copy this guide and its pictures to the new folder and use it from now on?", "Copy it", "Cancel"))
                return null;

            await SaveAsync();
            try
            {
                store.CopyTo(folder, Data);
            }
            catch (Exception ex)
            {
                return $"Could not copy the guide: {ex.Message}";
            }
        }

        next.UseServer = false;
        next.DataFolder = folder;
        return await ApplySettingsAsync(next);
    }
}

/// <summary>Shown instead of the guide when it can't be opened (e.g. the server can't be reached).</summary>
public partial class GuideUnavailablePage
{
    public MainViewModel Main { get; }
    public string Title { get; }
    public string Message { get; }

    public GuideUnavailablePage(MainViewModel main, string title, string message)
    {
        Main = main;
        Title = title;
        Message = message;
    }
}
