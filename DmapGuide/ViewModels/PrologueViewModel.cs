using DmapGuide.Models;

namespace DmapGuide.ViewModels;

/// <summary>The opening page: show overview, what's current, what's coming up.</summary>
public class PrologueViewModel
{
    public MainViewModel Main { get; }

    public string ShowName => Main.Data.ShowName;
    public int SeasonCount => Main.Data.Seasons.Count;
    public int CampaignCount => Main.Data.AllCampaigns.Count();
    public int EpisodeCount => Main.Data.AllEpisodes.Count();

    public Campaign? CurrentCampaign { get; }
    public Episode? LatestEpisode { get; }
    public List<ScheduleEvent> Upcoming { get; }

    public PrologueViewModel(MainViewModel main)
    {
        Main = main;

        CurrentCampaign = main.Data.CurrentCampaign;

        // Latest = most recently aired episode, else the newest filmed one, else the newest.
        var eps = main.Data.AllEpisodes.ToList();
        LatestEpisode = main.Data.LatestAiredEpisode
                        ?? eps.LastOrDefault(e => e.Status != EpisodeStatus.Planned)
                        ?? eps.LastOrDefault();

        Upcoming = ScheduleViewModel.AllEvents(main.Data.AllCampaigns)
            .Where(e => !e.IsPast)
            .OrderBy(e => e.Date)
            .Take(6)
            .ToList();
    }
}
