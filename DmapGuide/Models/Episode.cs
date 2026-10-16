using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace DmapGuide.Models;

public enum EpisodeStatus { Planned, Filmed, Aired }

public partial class Episode : TreeNode
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private int _number;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _title = "";

    [ObservableProperty] private string _dungeonMaster = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    private DateTime? _dateFilmed;

    [ObservableProperty] private string _filmingLocation = "";
    [ObservableProperty] private string _runtime = "";
    [ObservableProperty] private string _synopsis = "";
    [ObservableProperty] private string _productionNotes = "";

    /// <summary>Which of the campaign's characters are in this episode.</summary>
    public ObservableCollection<Guid> CastIds { get; set; } = new();

    /// <summary>Older files kept cast details per episode; moved to the campaign on load.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CastMember>? Cast { get; set; }

    public ObservableCollection<CastMember> GuestCast { get; set; } = new();
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

    public void RefreshCast()
    {
        OnPropertyChanged(nameof(CastMembers));
        OnPropertyChanged(nameof(CastChoices));
        OnPropertyChanged(nameof(HasCampaignCharacters));
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

/// <summary>A character and the cast member who plays them (campaign cast list or a guest star).</summary>
public partial class CastMember : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty] private string _playerName = "";
    [ObservableProperty] private string _characterName = "";
    [ObservableProperty] private string _characterSummary = "";

    /// <summary>The character's bio.</summary>
    [ObservableProperty] private string _characterDescription = "";
    [ObservableProperty] private string _imagePath = "";

    [ObservableProperty]
    [property: JsonIgnore]
    private string _appearances = "";
}

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

public partial class KeyMoment : ObservableObject
{
    [ObservableProperty] private string _timestamp = "";
    [ObservableProperty] private string _description = "";
}

public partial class AirDate : ObservableObject
{
    [ObservableProperty] private DateTime? _date;
    [ObservableProperty] private string _platform = "";
    [ObservableProperty] private string _notes = "";
}
