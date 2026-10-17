using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace DmapShared.Models;

/// <summary>Root of the guide. Its own record holds the show-wide details; each page is saved separately.</summary>
public partial class ShowData : ObservableObject
{
    [ObservableProperty] private string _showName = "Dungeon Masters";

    public ObservableCollection<Season> Seasons { get; set; } = new();

    [JsonIgnore] public IEnumerable<Campaign> AllCampaigns => Seasons.SelectMany(s => s.Campaigns);
    [JsonIgnore] public IEnumerable<Episode> AllEpisodes => AllCampaigns.SelectMany(c => c.Episodes);

    /// <summary>The most recently aired episode (highest number on a shared air date).</summary>
    [JsonIgnore]
    public Episode? LatestAiredEpisode =>
        AllEpisodes
            .Select(e => (Episode: e, Aired: e.AirDates.Where(a => a.Date?.Date <= DateTime.Today).Max(a => a.Date)))
            .Where(x => x.Aired.HasValue)
            .OrderBy(x => x.Aired).ThenBy(x => x.Episode.Number)
            .Select(x => x.Episode)
            .LastOrDefault();

    /// <summary>The campaign currently on air: the one whose episode aired most recently.</summary>
    [JsonIgnore]
    public Campaign? CurrentCampaign => LatestAiredEpisode?.Campaign ?? AllCampaigns.LastOrDefault();

    /// <summary>Restore parent links after loading (they aren't saved).</summary>
    public void LinkParents()
    {
        foreach (var s in Seasons)
            foreach (var c in s.Campaigns)
            {
                c.Season = s;
                foreach (var e in c.Episodes)
                {
                    e.Campaign = c;
                    e.Attach();
                    MoveEpisodeCastToCampaign(c, e);
                }
            }
    }

    // Earlier versions stored full cast details on each episode. Fold them into
    // the campaign's cast list (matching by player + character) and keep links.
    private static void MoveEpisodeCastToCampaign(Campaign c, Episode e)
    {
        if (e.Cast == null) return;

        foreach (var old in e.Cast)
        {
            var match = c.Characters.FirstOrDefault(x =>
                string.Equals(x.PlayerName.Trim(), old.PlayerName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.CharacterName.Trim(), old.CharacterName.Trim(), StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                c.Characters.Add(old);
                match = old;
            }

            if (!e.CastIds.Contains(match.Id)) e.CastIds.Add(match.Id);
        }

        e.Cast = null;
    }

    /// <summary>Pictures finished downloading: redraw every cover and portrait.</summary>
    public void RefreshImages()
    {
        foreach (var c in AllCampaigns)
        {
            c.RefreshImages();
            foreach (var e in c.Episodes)
                foreach (var guest in e.GuestCast) guest.RefreshImage();
        }
    }

    public static ShowData CreateSeed()
    {
        var data = new ShowData();
        var s1 = new Season { Number = 1, IsExpanded = true };
        data.Seasons.Add(s1);

        s1.Campaigns.Add(new Campaign { Number = 1, Title = "Ravenloft" });
        s1.Campaigns.Add(new Campaign { Number = 2, Title = "Arcana Unleashed" });

        var wow = new Campaign { Number = 3, Title = "World of Warcraft", IsExpanded = true };
        for (int i = 1; i <= 5; i++)
            wow.Episodes.Add(new Episode { Number = 300 + i });
        s1.Campaigns.Add(wow);

        data.LinkParents();
        return data;
    }
}
