using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmapGuide.Models;
using DmapGuide.Services;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Threading;

namespace DmapGuide.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private DataStore _store;
    private TreeNode? _selectedNode;

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
        Data.Seasons.Add(season);
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
        season.Campaigns.Add(campaign);
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
        campaign.Episodes.Add(episode);
        campaign.IsExpanded = true;
        Save();
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
