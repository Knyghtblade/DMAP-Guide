using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace DmapShared.Models;

public enum EpisodeStatus { Planned, Filmed, Aired }

public partial class Episode : TreeNode
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private int _number;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _title = "";

    /// <summary>This episode's own Dungeon Master. Blank means the campaign's (see <see cref="DungeonMasterShown"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DungeonMasterShown))]
    [NotifyPropertyChangedFor(nameof(IsDungeonMasterInherited))]
    private string _dungeonMaster = "";

    /// <summary>The Dungeon Master to show: this episode's own, or the campaign's when it has none.</summary>
    [JsonIgnore]
    public string DungeonMasterShown => IsDungeonMasterInherited ? Campaign!.DungeonMaster : DungeonMaster;

    [JsonIgnore]
    public bool IsDungeonMasterInherited =>
        string.IsNullOrWhiteSpace(DungeonMaster) && !string.IsNullOrWhiteSpace(Campaign?.DungeonMaster);

    /// <summary>The campaign's Dungeon Master changed: redraw what this episode shows.</summary>
    public void RefreshDungeonMaster()
    {
        OnPropertyChanged(nameof(DungeonMasterShown));
        OnPropertyChanged(nameof(IsDungeonMasterInherited));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    private DateTime? _dateFilmed;

    [ObservableProperty] private string _filmingLocation = "";
    [ObservableProperty] private string _runtime = "";
    [ObservableProperty] private string _synopsis = "";
    [ObservableProperty] private string _productionNotes = "";

    /// <summary>Free-form notes on what happened during filming, kept on the episode's own Shoot Notes page.</summary>
    [ObservableProperty] private string _shootNotes = "";

    /// <summary>The "Shoot Notes" page opened from the episode (not saved on its own; the notes live on the episode).</summary>
    [JsonIgnore] public ShootNotesPage ShootNotesPage { get; }

    public Episode()
    {
        ShootNotesPage = new ShootNotesPage(this);
    }

    /// <summary>Which of the campaign's characters are in this episode.</summary>
    public ObservableCollection<Guid> CastIds { get; set; } = new();

    /// <summary>Older files kept cast details per episode; moved to the campaign on load.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CastMember>? Cast { get; set; }

    /// <summary>Guest stars written straight onto the episode by earlier versions. New guests come from the Cast page (<see cref="GuestIds"/>).</summary>
    public ObservableCollection<CastMember> GuestCast { get; set; } = new();

    /// <summary>Which of the show's guest stars (the Cast page) are in this episode.</summary>
    public ObservableCollection<Guid> GuestIds { get; set; } = new();

    public ObservableCollection<KeyMoment> KeyMoments { get; set; } = new();
    public ObservableCollection<AirDate> AirDates { get; set; } = new();

    [JsonIgnore] public Campaign? Campaign { get; set; }

    /// <summary>Which campaign this belongs to (saved in the episode's own file).</summary>
    public Guid CampaignId { get; set; }

    /// <summary>The cast for this episode, in the campaign's cast-list order.</summary>
    [JsonIgnore]
    public List<CastMember> CastMembers =>
        Campaign?.Characters.Where(c => CastIds.Contains(c.Id)).ToList() ?? new();

    /// <summary>One checkbox per campaign character, for picking who's in this episode.</summary>
    [JsonIgnore]
    public List<CastChoice> CastChoices =>
        Campaign?.Characters.Select(c => new CastChoice(this, c)).ToList() ?? new();

    [JsonIgnore] public bool HasCampaignCharacters => Campaign?.Characters.Count > 0;

    public void SetInCast(CastMember member, bool inCast)
    {
        if (inCast && !CastIds.Contains(member.Id)) CastIds.Add(member.Id);
        else if (!inCast) CastIds.Remove(member.Id);
        OnPropertyChanged(nameof(CastMembers));
    }

    private IEnumerable<CastMember> ShowGuests => Campaign?.Season?.Show?.Guests ?? Enumerable.Empty<CastMember>();

    /// <summary>The guest stars in this episode, in the Cast page's order.</summary>
    [JsonIgnore]
    public List<CastMember> Guests => ShowGuests.Where(g => GuestIds.Contains(g.Id)).ToList();

    /// <summary>One checkbox per guest star on the Cast page, for picking who's in this episode.</summary>
    [JsonIgnore]
    public List<GuestChoice> GuestChoices => ShowGuests.Select(g => new GuestChoice(this, g)).ToList();

    [JsonIgnore] public bool HasShowGuests => ShowGuests.Any();

    /// <summary>Guests from the Cast page plus any written straight onto this episode.</summary>
    [JsonIgnore] public int GuestCount => Guests.Count + GuestCast.Count;

    public void SetGuest(CastMember member, bool inEpisode)
    {
        if (inEpisode && !GuestIds.Contains(member.Id)) GuestIds.Add(member.Id);
        else if (!inEpisode) GuestIds.Remove(member.Id);
        OnPropertyChanged(nameof(Guests));
        OnPropertyChanged(nameof(GuestCount));
    }

    public void RefreshCast()
    {
        OnPropertyChanged(nameof(CastMembers));
        OnPropertyChanged(nameof(CastChoices));
        OnPropertyChanged(nameof(HasCampaignCharacters));
        OnPropertyChanged(nameof(Guests));
        OnPropertyChanged(nameof(GuestChoices));
        OnPropertyChanged(nameof(HasShowGuests));
        OnPropertyChanged(nameof(GuestCount));
    }

    [JsonIgnore]
    public override string DisplayName =>
        string.IsNullOrWhiteSpace(Title) ? $"Episode {Number}" : $"{Number} · {Title}";

    [JsonIgnore] public override TreeNode? ParentNode => Campaign;

    [JsonIgnore]
    public DateTime? FirstAirDate =>
        AirDates.Where(a => a.Date.HasValue).Select(a => a.Date).OrderBy(d => d).FirstOrDefault();

    [JsonIgnore]
    public EpisodeStatus Status
    {
        get
        {
            var today = DateTime.Today;
            if (AirDates.Any(a => a.Date.HasValue && a.Date.Value.Date <= today)) return EpisodeStatus.Aired;
            if (DateFilmed.HasValue && DateFilmed.Value.Date <= today) return EpisodeStatus.Filmed;
            return EpisodeStatus.Planned;
        }
    }

    /// <summary>Watch air dates so Status / FirstAirDate stay current.</summary>
    public void Attach()
    {
        AirDates.CollectionChanged -= OnAirDatesChanged;
        AirDates.CollectionChanged += OnAirDatesChanged;
        foreach (var a in AirDates)
        {
            a.PropertyChanged -= OnAirDateChanged;
            a.PropertyChanged += OnAirDateChanged;
        }
    }

    private void OnAirDatesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (AirDate a in e.NewItems) a.PropertyChanged += OnAirDateChanged;
        if (e.OldItems != null)
            foreach (AirDate a in e.OldItems) a.PropertyChanged -= OnAirDateChanged;
        RaiseDates();
    }

    private void OnAirDateChanged(object? sender, PropertyChangedEventArgs e) => RaiseDates();

    private void RaiseDates()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(FirstAirDate));
    }
}

/// <summary>Page node for an episode's shoot notes (not saved; the notes live on the episode).</summary>
public class ShootNotesPage : TreeNode
{
    public Episode Episode { get; }

    public ShootNotesPage(Episode episode) => Episode = episode;

    public override string DisplayName => "Shoot Notes";
    public override TreeNode? ParentNode => Episode;
}

/// <summary>A character and the cast member who plays them (campaign cast list, a guest star, or the show's cast).</summary>
public partial class CastMember : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty] private string _playerName = "";
    [ObservableProperty] private string _characterName = "";
    [ObservableProperty] private string _characterSummary = "";

    /// <summary>The character's bio.</summary>
    [ObservableProperty] private string _characterDescription = "";

    /// <summary>A guest character (campaign characters only): not a regular member of the party.</summary>
    [ObservableProperty] private bool _isGuest;
    [ObservableProperty] private string _imagePath = "";

    /// <summary>
    /// Where the picture sits inside its circle, from -1 to 1 on each axis: 0 is centred,
    /// 1 shows the picture's top (or left) edge, -1 its bottom (or right) edge.
    /// Only the axis the picture overflows on has any effect.
    /// </summary>
    [ObservableProperty] private double _imageOffsetX;
    [ObservableProperty] private double _imageOffsetY;

    [ObservableProperty]
    [property: JsonIgnore]
    private string _appearances = "";

    /// <summary>Cast page: the characters this person plays, and in which campaigns (not saved).</summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private List<CharacterRole> _roles = new();

    /// <summary>Characters page: the person on the Cast page who plays this character, if listed there (not saved).</summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private CastMember? _player;

    /// <summary>The picture finished downloading: redraw the portrait.</summary>
    public void RefreshImage() => OnPropertyChanged(nameof(ImagePath));

    /// <summary>Names match ignoring case and spaces at either end (a blank name never matches).</summary>
    public static bool SameName(string a, string b) =>
        a.Trim().Length > 0 && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>A character a cast member plays, and the campaign it's in (a link on the Cast page).</summary>
public record CharacterRole(CastMember Character, Campaign Campaign);

/// <summary>A campaign character with a checkbox for "in this episode".</summary>
public class CastChoice
{
    private readonly Episode _episode;

    public CastChoice(Episode episode, CastMember member)
    {
        _episode = episode;
        Member = member;
    }

    public CastMember Member { get; }

    public bool IsInEpisode
    {
        get => _episode.CastIds.Contains(Member.Id);
        set => _episode.SetInCast(Member, value);
    }
}

/// <summary>A guest star from the Cast page with a checkbox for "in this episode".</summary>
public class GuestChoice
{
    private readonly Episode _episode;

    public GuestChoice(Episode episode, CastMember member)
    {
        _episode = episode;
        Member = member;
    }

    public CastMember Member { get; }

    public bool IsInEpisode
    {
        get => _episode.GuestIds.Contains(Member.Id);
        set => _episode.SetGuest(Member, value);
    }
}

public partial class KeyMoment : ObservableObject
{
    [ObservableProperty] private string _timestamp = "";
    [ObservableProperty] private string _description = "";
}

public partial class AirDate : ObservableObject
{
    /// <summary>The platforms offered in the tick-list. Anything else typed earlier is kept as well.</summary>
    public static readonly string[] KnownPlatforms = { "YouTube", "Twitch", "Amazon Prime", "Disney+" };

    [ObservableProperty] private DateTime? _date;

    /// <summary>Where it airs, as "YouTube, Twitch" (one or more platforms).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlatformChoices))]
    private string _platform = "";

    [ObservableProperty] private string _notes = "";

    /// <summary>A web address for this airing (the episode's page on the platform, say).</summary>
    [ObservableProperty] private string _link = "";

    [JsonIgnore]
    public IEnumerable<string> Platforms =>
        Platform.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>One tick box per known platform.</summary>
    [JsonIgnore]
    public List<PlatformChoice> PlatformChoices => KnownPlatforms.Select(p => new PlatformChoice(this, p)).ToList();

    public bool HasPlatform(string name) => Platforms.Contains(name, StringComparer.OrdinalIgnoreCase);

    public void SetPlatform(string name, bool on)
    {
        var chosen = Platforms.Where(p => !string.Equals(p, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (on) chosen.Add(name);

        // Known platforms in their usual order, then anything typed by hand.
        var known = KnownPlatforms.Where(k => chosen.Contains(k, StringComparer.OrdinalIgnoreCase));
        var other = chosen.Where(p => !KnownPlatforms.Contains(p, StringComparer.OrdinalIgnoreCase));
        Platform = string.Join(", ", known.Concat(other));
    }
}

/// <summary>A platform with a tick box for "airs here".</summary>
public class PlatformChoice
{
    private readonly AirDate _airDate;

    public PlatformChoice(AirDate airDate, string name)
    {
        _airDate = airDate;
        Name = name;
    }

    public string Name { get; }

    public bool IsSelected
    {
        get => _airDate.HasPlatform(Name);
        set => _airDate.SetPlatform(Name, value);
    }
}
