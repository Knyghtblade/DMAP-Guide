using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json.Serialization;

namespace DmapShared.Models;

public partial class Campaign : TreeNode
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private int _number;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _title = "";

    [ObservableProperty] private string _dungeonMaster = "";
    [ObservableProperty] private string _setting = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _coverImage = "";

    /// <summary>When the campaign is filmed (usually one block for all its episodes).</summary>
    [ObservableProperty] private DateTime? _filmingStart;
    [ObservableProperty] private DateTime? _filmingEnd;
    [ObservableProperty] private string _filmingLocation = "";

    /// <summary>The campaign's cast list. Episodes refer to these by Id.</summary>
    public ObservableCollection<CastMember> Characters { get; set; } = new();

    private ObservableCollection<Episode> _episodes = new();
    public ObservableCollection<Episode> Episodes
    {
        get => _episodes;
        set
        {
            _episodes.CollectionChanged -= OnEpisodesChanged;
            _episodes = value;
            _episodes.CollectionChanged += OnEpisodesChanged;
            RebuildChildren();
        }
    }

    [JsonIgnore] public Season? Season { get; set; }

    /// <summary>Which season this belongs to (saved in the campaign's own file).</summary>
    public Guid SeasonId { get; set; }

    /// <summary>The "Cast &amp; Characters" page shown in the table of contents.</summary>
    [JsonIgnore] public CastRosterPage RosterPage { get; }

    /// <summary>What the table of contents shows under the campaign: the cast page, then episodes.</summary>
    [JsonIgnore] public ObservableCollection<TreeNode> Children { get; } = new();

    public Campaign()
    {
        RosterPage = new CastRosterPage(this);
        _episodes.CollectionChanged += OnEpisodesChanged;
        RebuildChildren();
    }

    private void OnEpisodesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildChildren();

    private void RebuildChildren()
    {
        Children.Clear();
        Children.Add(RosterPage);
        foreach (var ep in _episodes) Children.Add(ep);
    }

    [JsonIgnore]
    public override string DisplayName =>
        string.IsNullOrWhiteSpace(Title) ? $"Campaign {Number}" : $"Campaign {Number} · {Title}";

    [JsonIgnore] public override TreeNode? ParentNode => Season;

    /// <summary>A picture finished downloading: redraw the cover and portraits.</summary>
    public void RefreshImages()
    {
        OnPropertyChanged(nameof(CoverImage));
        foreach (var member in Characters) member.RefreshImage();
    }
}

/// <summary>Tree/page node for a campaign's cast list (not saved; it belongs to the campaign).</summary>
public class CastRosterPage : TreeNode
{
    public Campaign Campaign { get; }

    public CastRosterPage(Campaign campaign) => Campaign = campaign;

    public override string DisplayName => "Cast & Characters";
    public override TreeNode? ParentNode => Campaign;

    /// <summary>Fill in each character's "Appears in" line (called when the page opens).</summary>
    public void RefreshAppearances()
    {
        foreach (var member in Campaign.Characters)
        {
            var eps = Campaign.Episodes.Where(e => e.CastIds.Contains(member.Id)).Select(e => e.Number.ToString()).ToList();
            member.Appearances = eps.Count == 0 ? "Not in any episodes yet" : "Appears in episodes " + string.Join(", ", eps);
        }
    }
}
