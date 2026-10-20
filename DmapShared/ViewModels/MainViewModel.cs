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
    [NotifyPropertyChangedFor(nameof(IsOnPrologue))]
    [NotifyPropertyChangedFor(nameof(IsOnSchedule))]
    [NotifyPropertyChangedFor(nameof(IsOnCast))]
    [NotifyPropertyChangedFor(nameof(IsOnSettings))]
    private object? _currentPage;

    // Which of the fixed pages is open (for highlighting its link in the menu).
    public bool IsOnPrologue => CurrentPage is PrologueViewModel;
    public bool IsOnSchedule => CurrentPage is ScheduleViewModel;
    public bool IsOnCast => CurrentPage is CastPageViewModel;
    public bool IsOnSettings => CurrentPage is SettingsViewModel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanChangePages))]
    private bool _isConnected;

    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _statusText = "";

    /// <summary>Set while a newer DMAP Guide than this one has been used with the shared guide.</summary>
    [ObservableProperty] private string _versionWarning = "";

    public string AppVersion => _platform.AppVersion;

    /// <summary>Set by the window: pushes the value of the text box being typed in.</summary>
    public Action? CommitPendingEdits { get; set; }

    /// <summary>Raised just before the page changes (the window snapshots it for the page-turn).</summary>
    public event Action? Navigating;

    public IPlatformServices Platform => _platform;

    /// <summary>Where the guide is kept, as shown to the user.</summary>
    public string Location => _store?.Location ?? "";

    public bool UsesServer => _store is ServerGuideStore;

    /// <summary>Season, campaign, episode, Characters and Shoot Notes pages, plus the Cast page (part of the show-wide details).</summary>
    public bool CanEdit => IsConnected && !IsReadOnly && CurrentPage is TreeNode or CastPageViewModel;

    /// <summary>Adding and deleting pages.</summary>
    public bool CanChangePages => IsConnected && !IsReadOnly;

    public string UserName => _settings.UserName;

    // If the guide couldn't be read, never overwrite it automatically.
    private bool _readOnlyAfterLoadError;

    /// <summary>The guide folder can be read but not written to (e.g. shared from OneDrive with "Can view").</summary>
    private bool _folderReadOnly;

    public bool IsReadOnly => _readOnlyAfterLoadError || _folderReadOnly;

    /// <summary>The guide was opened from a fixed folder (beside the app, or "--data"), so Settings can't move it.</summary>
    public bool FolderIsFixed => _folderOverride != null || RequiredFolder != null;

    /// <summary>
    /// The shared app (desktop, published as one file): the "guide" folder that must sit beside
    /// DMAP Guide.exe. Only that folder is ever opened. If it's missing, which happens when the
    /// .exe was opened from a download or a stray copy instead of the synced shared folder, the
    /// app says so rather than quietly opening some other guide.
    /// </summary>
    public string? RequiredFolder { get; set; }

    /// <summary>
    /// A further check on <see cref="RequiredFolder"/> once it exists (the desktop checks that
    /// OneDrive syncs it). Returns why the folder can't be used, or null when it can.
    /// </summary>
    public Func<string, string?>? RequiredFolderCheck { get; set; }

    /// <summary>The desktop only opens folders; the phone only opens the shared server.</summary>
    private bool ServerChosen(GuideSettings settings) => settings.UseServer && _platform.SupportsServer;

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

        await OpenChosenGuideAsync();
    }

    /// <summary>Open the guide this app is meant to open: the fixed folder if there is one, else the one in Settings.</summary>
    private async Task OpenChosenGuideAsync()
    {
        if (_folderOverride == null && RequiredFolder != null && !Directory.Exists(RequiredFolder))
        {
            string appFolder = Path.GetDirectoryName(RequiredFolder) ?? RequiredFolder;
            ShowUnavailable("The guide isn't beside the app",
                "DMAP Guide looked for a folder called \"guide\" next to DMAP Guide.exe and didn't find one, so the shared guide can't be opened.\n\n" +
                "This usually means the app was opened from a download or a copy, not from the shared folder that OneDrive syncs to this PC. " +
                "Open File Explorer, go to OneDrive, open the shared \"DMAP Guide\" folder, and check that \"DMAP Guide.exe\" and the \"guide\" folder " +
                "sit side by side with green ticks. Then double-click \"DMAP Guide.exe\" there.\n\n" +
                $"The app was opened from:\n{appFolder}", canOpenSettings: false);
            return;
        }
        if (_folderOverride == null && RequiredFolder != null && RequiredFolderCheck?.Invoke(RequiredFolder) is string problem)
        {
            ShowUnavailable("This copy of the guide isn't shared", problem, canOpenSettings: false);
            return;
        }

        string? fixedFolder = _folderOverride ?? RequiredFolder;
        bool nothingChosenYet = fixedFolder == null && (ServerChosen(_settings)
            ? string.IsNullOrWhiteSpace(_settings.ServerAddress)
            : !_platform.SupportsFolders || string.IsNullOrWhiteSpace(_settings.DataFolder));
        if (nothingChosenYet)
        {
            ShowUnavailable("Connect to the shared guide",
                "Enter the shared guide's server address and access key in Settings. The person who runs the shared guide can give you both.");
            return;
        }

        await OpenAsync(_settings, fixedFolder);
    }

    // ============================================================
    // OPENING A GUIDE
    // ============================================================
    /// <summary>Tests: the client to open the next server guide with.</summary>
    internal DmapApiClient? TestClient { get; set; }

    private IGuideStore CreateStore(GuideSettings settings, string? folderOverride)
    {
        if (ServerChosen(settings) && folderOverride == null)
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

        // A folder shared with "Can view" opens fine but can't be written to: show the guide without Edit.
        bool folderReadOnly = !readOnly && store is FolderGuideStore folderStore && !folderStore.CanWrite();

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
            _syncProblem = null;
            _readOnlyAfterLoadError = readOnly;
            _folderReadOnly = folderReadOnly;
            _selectedNode = null;
            IsEditing = false;
            Data = loaded.Data;
            RollUpContents();
            IsConnected = true;
        }
        finally
        {
            _gate.Release();
        }

        OnPropertyChanged(nameof(Location));
        OnPropertyChanged(nameof(UsesServer));
        OnPropertyChanged(nameof(IsReadOnly));
        OnPropertyChanged(nameof(CanChangePages));
        Navigating?.Invoke();
        CurrentPage = new PrologueViewModel(this);
        StatusText = store is ServerGuideStore ? "Connected to the shared guide" : folderReadOnly ? ReadOnlyStatus : "";
        CheckAppVersion();

        if (loaded.Unreadable.Count > 0)
            await _platform.AlertAsync("DMAP Guide",
                $"{loaded.Unreadable.Count} page(s) in {store.Location} could not be read and are not shown:\n\n" +
                string.Join("\n", loaded.Unreadable.Take(10)) +
                (store is FolderGuideStore
                    ? "\n\nThose files have not been changed. OneDrive's version history can restore an earlier copy."
                    : "\n\nThey have not been changed on the server."));
        return true;
    }

    /// <summary>Start with the seasons open to show their campaigns, and the campaigns rolled up (episodes hidden).</summary>
    private void RollUpContents()
    {
        foreach (var season in Data.Seasons)
        {
            season.IsExpanded = true;
            foreach (var campaign in season.Campaigns) campaign.IsExpanded = false;
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

    private void ShowUnavailable(string title, string message, bool canOpenSettings = true)
    {
        IsConnected = false;
        IsEditing = false;
        _selectedNode = null;
        Data = new ShowData();
        Navigating?.Invoke();
        CurrentPage = new GuideUnavailablePage(this, title, message, canOpenSettings);
        StatusText = "";
    }

    [RelayCommand]
    private async Task ReconnectAsync()
    {
        _settings = await _settingsStore.LoadAsync();
        await OpenChosenGuideAsync();
    }

    public static string Friendly(Exception ex) => ex switch
    {
        DmapApiException api => api.Message,
        _ => ex.Message
    };

    public string Breadcrumb => CurrentPage switch
    {
        Episode e => $"{e.Campaign?.Season?.DisplayName}  ›  {e.Campaign?.DisplayName}  ›  Episode {e.Number}",
        ShootNotesPage n => $"{n.Episode.Campaign?.Season?.DisplayName}  ›  {n.Episode.Campaign?.DisplayName}  ›  Episode {n.Episode.Number}  ›  Shoot Notes",
        Campaign c => $"{c.Season?.DisplayName}  ›  {c.DisplayName}",
        CastRosterPage r => $"{r.Campaign.Season?.DisplayName}  ›  {r.Campaign.DisplayName}  ›  Characters",
        Season s => s.DisplayName,
        ScheduleViewModel => "Chronicle of Dates",
        CastPageViewModel => "Cast",
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

        if (IsEditing)
        {
            FinishCampaignEdit();
            await SaveAsync();
        }
        Navigating?.Invoke();

        // Cast details live on the campaign; make sure the page shows the latest.
        if (target is CastRosterPage roster) roster.RefreshAppearances();
        if (target is Episode episode) episode.RefreshCast();
        if (target is CastPageViewModel cast) cast.RefreshAppearances();

        // The Prologue and Cast pages are edited on their own; leaving them ends editing.
        if (IsEditing && CurrentPage is not TreeNode) IsEditing = false;

        CurrentPage = target;

        // Keep the table of contents in step with the page (Shoot Notes highlight their episode).
        if (target is TreeNode node)
        {
            var shown = node is ShootNotesPage notes ? notes.Episode : node;
            for (var p = shown.ParentNode; p != null; p = p.ParentNode) p.IsExpanded = true;
            if (_selectedNode != null && _selectedNode != shown) _selectedNode.IsSelected = false;
            _selectedNode = shown;
            shown.IsSelected = true;
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
        if (IsEditing) StartWatchingCampaignEdit();
        await UpdateLockAsync();
        await ShowPageInfoAsync();
    }

    [RelayCommand] private Task ShowPrologueAsync() => NavigateAsync(new PrologueViewModel(this));
    [RelayCommand] private Task ShowScheduleAsync() => NavigateAsync(new ScheduleViewModel(this));
    [RelayCommand] private Task ShowCastAsync() => NavigateAsync(new CastPageViewModel(this));
    [RelayCommand] private Task OpenShootNotesAsync(Episode episode) => NavigateAsync(episode.ShootNotesPage);
    [RelayCommand] private Task CloseShootNotesAsync(ShootNotesPage page) => NavigateAsync(page.Episode);
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
        if (value) StartWatchingCampaignEdit();
        else
        {
            FinishCampaignEdit();
            _ = SaveAsync();
        }
        _ = UpdateLockAsync();
    }

    // ------------------------------------------------------------
    // The campaign's Dungeon Master carries through to its episodes
    // ------------------------------------------------------------
    private Campaign? _editedCampaign;
    private string _dungeonMasterBefore = "";

    private void StartWatchingCampaignEdit()
    {
        _editedCampaign = CurrentPage as Campaign;
        _dungeonMasterBefore = _editedCampaign?.DungeonMaster ?? "";
    }

    /// <summary>
    /// Editing a campaign ended. If its Dungeon Master changed, episodes that were following
    /// the old name (or had none) now follow the campaign; episodes with a different Dungeon
    /// Master of their own are left alone.
    /// </summary>
    private void FinishCampaignEdit()
    {
        if (_editedCampaign is not Campaign campaign) return;
        _editedCampaign = null;

        string before = _dungeonMasterBefore.Trim(), after = campaign.DungeonMaster.Trim();
        if (string.Equals(before, after, StringComparison.OrdinalIgnoreCase)) return;

        foreach (var episode in campaign.Episodes)
        {
            string own = episode.DungeonMaster.Trim();
            if (own.Length == 0 || string.Equals(own, before, StringComparison.OrdinalIgnoreCase))
                episode.DungeonMaster = "";
        }
    }

    /// <summary>Whether the last save actually wrote anything (Refresh words its status from this).</summary>
    private bool _lastSaveWrote;

    [RelayCommand]
    public async Task SaveAsync()
    {
        _lastSaveWrote = false;
        if (_store == null || !IsConnected) return;
        if (_readOnlyAfterLoadError)
        {
            StatusText = "Not saved - the guide could not be read at startup";
            return;
        }
        if (_folderReadOnly)
        {
            StatusText = ReadOnlyStatus;
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
            _lastSaveWrote = written > 0;
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

    /// <summary>
    /// The saved page the current page edits (the Characters page belongs to its campaign,
    /// Shoot Notes to their episode). Null for the Prologue and Cast pages, which edit the
    /// show-wide record.
    /// </summary>
    private TreeNode? EditTarget => CurrentPage switch
    {
        CastRosterPage r => r.Campaign,
        ShootNotesPage n => n.Episode,
        TreeNode n => n,
        _ => null
    };

    /// <summary>What "someone is editing" warnings are keyed on for the current page, and what to call it.</summary>
    private (Guid Id, string Name)? LockTarget => CurrentPage switch
    {
        CastPageViewModel => (GuideRecords.GuideLockId, "the Cast page"),
        _ when EditTarget is TreeNode n => (n.Id, $"\"{n.DisplayName}\""),
        _ => null
    };

    /// <summary>Mark the page being edited so other people get a warning; clear it when done.</summary>
    private async Task UpdateLockAsync()
    {
        if (_store == null) return;
        Guid? wanted = IsEditing ? LockTarget?.Id : null;
        if (wanted == _lockedId) return;

        var old = _lockedId;
        _lockedId = wanted;
        _lockSince = DateTime.UtcNow;
        if (old is Guid previous) await _store.ReleaseLockAsync(previous);
        if (wanted is Guid id) await _store.WriteLockAsync(id, _lockSince);
    }

    private async Task<bool> OkToEditAsync()
    {
        if (_store == null || LockTarget is not (Guid id, string name) ||
            await _store.ReadLockAsync(id) is not EditLock other || !other.IsSomeoneElse(UserName))
            return true;

        return await _platform.AskAsync("Someone else is editing",
            $"{other.User} is editing {name} right now (since {other.Since.ToLocalTime():t}).\n\n" +
            "If you both change it, whoever saves second will be asked whose version to keep.\n\nEdit it anyway?",
            "Edit anyway", "Cancel");
    }

    /// <summary>Status line: who's editing this page, or who last changed it.</summary>
    private async Task ShowPageInfoAsync()
    {
        // While other people's changes can't be fetched, say so on every page rather than look up to date.
        if (_syncProblem != null)
        {
            StatusText = _syncProblem;
            return;
        }
        if (_store == null || LockTarget is not (Guid id, _)) return;

        if (await _store.ReadLockAsync(id) is EditLock other && other.IsSomeoneElse(UserName))
            StatusText = $"✎ {other.User} is editing this page";
        else if (EditTarget is { LastEdited: DateTime when, LastEditedBy.Length: > 0 } node)
            StatusText = $"Last changed by {node.LastEditedBy}, {when.ToLocalTime():g}";
        else
            StatusText = _folderReadOnly ? ReadOnlyStatus : "";
    }

    private const string ReadOnlyStatus = "Read only: you can read this guide but not change it";

    /// <summary>Why other people's changes can't be fetched right now (null when all is well).</summary>
    private string? _syncProblem;

    /// <summary>
    /// The Refresh button: save what's been changed here, then pick up everyone else's changes,
    /// without closing and reopening the app. The status line says what happened.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_store == null || !IsConnected) return;

        await SaveAsync();
        bool saved = _lastSaveWrote;
        bool pickedUp = await SyncNowAsync();
        if (_syncProblem != null || !IsConnected) return;

        string when = DateTime.Now.ToString("t");
        StatusText = (saved, pickedUp) switch
        {
            (true, true) => $"Saved and picked up changes {when}",
            (true, false) => $"Saved. Nothing new from anyone else {when}",
            (false, true) => $"Picked up changes {when}",
            _ => $"Up to date {when}"
        };
    }

    /// <summary>Pick up pages other people have saved. True when something changed.</summary>
    public async Task<bool> SyncNowAsync()
    {
        if (_store == null || !IsConnected || _readOnlyAfterLoadError) return false;
        if (!await _gate.WaitAsync(0)) return false;

        SyncResult result;
        try
        {
            result = await _store.SyncAsync(Data);
            if (_syncProblem != null) StatusText = "Connected to the shared guide again";
            _syncProblem = null;
        }
        catch (Exception ex)
        {
            _syncProblem = ex is DmapApiException { StatusCode: System.Net.HttpStatusCode.Unauthorized }
                ? "⚠ Not up to date: the access key isn't accepted any more. Enter the current key in Settings."
                : $"⚠ Not up to date: {Friendly(ex)}";
            StatusText = _syncProblem;
            return false;
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
            return true;
        }

        if (result.Changed) RefreshPage();
        await ShowPageInfoAsync();
        CheckAppVersion();
        return result.Changed;
    }

    // ============================================================
    // VERSIONS: is everyone on the same DMAP Guide?
    // ============================================================
    /// <summary>A note in the shared folder: the newest DMAP Guide that has opened this guide.</summary>
    private const string VersionFile = "app-version.txt";

    /// <summary>
    /// Record this app's version in the shared folder if it's the newest seen, or warn if a
    /// newer one has been used: whoever sees the warning is still on an old DMAP Guide.exe.
    /// </summary>
    private void CheckAppVersion()
    {
        if (_store is not FolderGuideStore folder) return;
        try
        {
            string path = Path.Combine(folder.Folder, VersionFile);
            Version? mine = ParseVersion(AppVersion);
            Version? newest = File.Exists(path) ? ParseVersion(File.ReadAllText(path)) : null;
            if (mine == null) return;

            if (newest != null && newest > mine)
            {
                VersionWarning = $"⚠ You're on DMAP Guide {mine}, but {newest} has been used with this guide. " +
                                 "Close the app and open the latest DMAP Guide.exe from the shared folder.";
                return;
            }
            VersionWarning = "";
            if ((newest == null || mine > newest) && !IsReadOnly)
                File.WriteAllText(path, mine.ToString());
        }
        catch (Exception)
        {
            // Not being able to read or write the note is no reason to interrupt anyone.
        }
    }

    private static Version? ParseVersion(string text) =>
        Version.TryParse(text.Trim(), out var v) ? v : null;

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
            case CastPageViewModel cast:
                cast.RefreshAppearances();
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
            ShootNotesPage n => n.Episode.Campaign?.Season,
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
            ShootNotesPage n => n.Episode.Campaign,
            Season s => s.Campaigns.LastOrDefault(),
            _ => null
        } ?? Data.AllCampaigns.LastOrDefault();

        if (campaign == null)
        {
            await _platform.AlertAsync("DMAP Guide", "Add a campaign first.");
            return;
        }

        // Episodes are numbered <campaign><nn>, e.g. 301, 302 for campaign 3.
        // The Dungeon Master comes from the campaign unless the previous episode had its own.
        var last = campaign.Episodes.OrderBy(e => e.Number).LastOrDefault();
        var episode = new Episode
        {
            Number = last != null ? last.Number + 1 : campaign.Number * 100 + 1,
            Campaign = campaign,
            DungeonMaster = last?.DungeonMaster is { Length: > 0 } dm &&
                            !string.Equals(dm.Trim(), campaign.DungeonMaster.Trim(), StringComparison.OrdinalIgnoreCase) ? dm : "",
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
        if (CurrentPage is not TreeNode node || node is CastRosterPage or ShootNotesPage)
        {
            await _platform.AlertAsync("DMAP Guide", CurrentPage switch
            {
                CastRosterPage => "The Characters page belongs to its campaign and can't be deleted. To remove a character, press Edit Page and use the ✕ on their card.",
                ShootNotesPage => "Shoot Notes belong to their episode and can't be deleted on their own. Open the episode to delete it.",
                _ => "Open the season, campaign or episode you want to delete first."
            });
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

    /// <summary>Open a campaign's Characters page: given the campaign, or one of its characters.</summary>
    [RelayCommand]
    private Task OpenRosterAsync(object? target)
    {
        var campaign = target switch
        {
            Campaign c => c,
            CastMember m => Data.AllCampaigns.FirstOrDefault(c => c.Characters.Contains(m)),
            _ => null
        };
        return campaign == null ? Task.CompletedTask : NavigateAsync(campaign.RosterPage);
    }

    /// <summary>Add a person to the Cast page: a "DungeonMaster", a "Guest", or (anything else) a cast member.</summary>
    [RelayCommand]
    private void AddCastMember(string? kind)
    {
        if (!CanEdit || CurrentPage is not CastPageViewModel) return;
        var list = kind switch
        {
            "DungeonMaster" => Data.DungeonMasters,
            "Guest" => Data.Guests,
            _ => Data.Cast
        };
        list.Add(new CastMember());
    }

    [RelayCommand] private void AddGuest(Episode ep) => ep.GuestCast.Add(new CastMember());
    [RelayCommand] private void AddKeyMoment(Episode ep) => ep.KeyMoments.Add(new KeyMoment());
    [RelayCommand] private void AddAirDate(Episode ep) => ep.AirDates.Add(new AirDate { Date = DateTime.Today });

    [RelayCommand]
    private async Task RemoveCastAsync(CastMember member)
    {
        switch (CurrentPage)
        {
            // On an episode: take them out of this episode (they stay on the campaign's list or the Cast page).
            case Episode ep:
                if (ep.CastIds.Contains(member.Id)) ep.SetInCast(member, false);
                else if (ep.GuestIds.Contains(member.Id)) ep.SetGuest(member, false);
                else ep.GuestCast.Remove(member);
                ep.RefreshCast(); // untick their box too
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

            // On the Cast page: take the person off the Cast page (their characters stay on their campaigns).
            case CastPageViewModel cast:
                string who = string.IsNullOrWhiteSpace(member.PlayerName) ? "this person" : member.PlayerName;
                int guestIn = Data.AllEpisodes.Count(e => e.GuestIds.Contains(member.Id));
                string also = guestIn > 0 ? $"\n\nThey'll also be removed from the {guestIn} episode(s) they guest in." : "";
                if (!await _platform.AskAsync("Remove from the cast", $"Remove {who} from the Cast page?{also}", "Remove", "Cancel"))
                    return;
                foreach (var e in Data.AllEpisodes) e.GuestIds.Remove(member.Id);
                if (!Data.DungeonMasters.Remove(member) && !Data.Cast.Remove(member)) Data.Guests.Remove(member);
                cast.RefreshAppearances();
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

    /// <summary>Open a web address (an air date's link) in the browser; "youtube.com/…" is fine without https://.</summary>
    [RelayCommand]
    private async Task OpenLinkAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        string address = url.Trim();
        if (!address.Contains("://")) address = "https://" + address;

        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            !uri.Host.Contains('.'))
        {
            await _platform.AlertAsync("DMAP Guide", $"That doesn't look like a web address:\n{url}\n\nIt should look like https://www.youtube.com/watch?v=…");
            return;
        }

        try
        {
            _platform.OpenLink(uri.ToString());
        }
        catch (Exception ex)
        {
            await _platform.AlertAsync("DMAP Guide", $"The link couldn't be opened:\n{Friendly(ex)}");
        }
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

    [RelayCommand]
    private void RemoveCover(Campaign campaign)
    {
        if (IsEditing) campaign.CoverImage = "";
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
        next.UseServer = ServerChosen(next);

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

        // Opened from a fixed folder (beside the app): only the name can change.
        if (!await OpenAsync(next, _folderOverride)) return null; // the page saying why is already showing
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

    /// <summary>False when Settings can't help (the shared app was opened away from its guide folder).</summary>
    public bool CanOpenSettings { get; }

    public GuideUnavailablePage(MainViewModel main, string title, string message, bool canOpenSettings = true)
    {
        Main = main;
        Title = title;
        Message = message;
        CanOpenSettings = canOpenSettings;
    }
}
