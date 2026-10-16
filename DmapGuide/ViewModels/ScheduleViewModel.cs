using CommunityToolkit.Mvvm.ComponentModel;
using DmapGuide.Models;
using System.Globalization;

namespace DmapGuide.ViewModels;

public enum ScheduleEventKind { Filming, Airing }

/// <summary>
/// One date in the chronicle: an episode's filming or airing, or a campaign's
/// filming block (Episode is null, EndDate is the last filming day).
/// </summary>
public record ScheduleEvent(DateTime Date, ScheduleEventKind Kind, Campaign Campaign, Episode? Episode, string Where, DateTime? EndDate = null)
{
    private DateTime LastDay => (EndDate ?? Date).Date;

    public bool IsFilming => Kind == ScheduleEventKind.Filming;
    public bool IsPast => LastDay < DateTime.Today;
    public bool IsToday => Date.Date <= DateTime.Today && DateTime.Today <= LastDay;
    public string Day => Date.Day.ToString(CultureInfo.CurrentCulture);
    public string Weekday => Date.ToString("ddd", CultureInfo.CurrentCulture);
    public string CampaignName => Campaign.DisplayName;

    /// <summary>Page to open when clicked.</summary>
    public object Target => (object?)Episode ?? Campaign;

    public string Heading => Episode == null
        ? $"{(string.IsNullOrWhiteSpace(Campaign.Title) ? Campaign.DisplayName : Campaign.Title)} filming block"
        : string.IsNullOrWhiteSpace(Episode.Title) ? $"Episode {Episode.Number}" : $"Episode {Episode.Number} · {Episode.Title}";

    private string DateRange => LastDay != Date.Date
        ? $"{Date.ToString("MMM d", CultureInfo.CurrentCulture)} – {LastDay.ToString("MMM d", CultureInfo.CurrentCulture)}"
        : "";

    public string LongDate => LastDay != Date.Date
        ? $"{Date.ToString("MMMM d", CultureInfo.CurrentCulture)} – {LastDay.ToString("MMMM d", CultureInfo.CurrentCulture)}"
        : Date.ToString("dddd, MMMM d", CultureInfo.CurrentCulture);

    public string Subline => string.Join("  ·  ",
        new[] { CampaignName, DateRange, Where }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

public record MonthGroup(string Name, List<ScheduleEvent> Events);

public record CampaignFilter(string Name, Campaign? Campaign);

/// <summary>"Chronicle of Dates": filming schedule and air dates, grouped by month.</summary>
public partial class ScheduleViewModel : ObservableObject
{
    public MainViewModel Main { get; }

    public List<CampaignFilter> Campaigns { get; }

    [ObservableProperty] private CampaignFilter _selectedCampaign;
    [ObservableProperty] private bool _showPast;
    [ObservableProperty] private bool _showFilming = true;
    [ObservableProperty] private bool _showAiring = true;

    [ObservableProperty] private List<MonthGroup> _months = new();
    [ObservableProperty] private ScheduleEvent? _nextFilming;
    [ObservableProperty] private ScheduleEvent? _nextAiring;
    [ObservableProperty] private List<Episode> _unscheduled = new();

    public ScheduleViewModel(MainViewModel main)
    {
        Main = main;
        Campaigns = new List<CampaignFilter> { new("All campaigns", null) };
        Campaigns.AddRange(main.Data.AllCampaigns.Select(c => new CampaignFilter(c.DisplayName, c)));
        _selectedCampaign = Campaigns[0];
        Rebuild();
    }

    partial void OnSelectedCampaignChanged(CampaignFilter value) => Rebuild();
    partial void OnShowPastChanged(bool value) => Rebuild();
    partial void OnShowFilmingChanged(bool value) => Rebuild();
    partial void OnShowAiringChanged(bool value) => Rebuild();

    public static IEnumerable<ScheduleEvent> AllEvents(IEnumerable<Campaign> campaigns)
    {
        foreach (var c in campaigns)
        {
            if (c.FilmingStart is DateTime start)
                yield return new ScheduleEvent(start, ScheduleEventKind.Filming, c, null, c.FilmingLocation,
                    c.FilmingEnd is DateTime end && end > start ? end : null);

            foreach (var ep in c.Episodes)
            {
                if (ep.DateFilmed is DateTime f)
                    yield return new ScheduleEvent(f, ScheduleEventKind.Filming, c, ep, ep.FilmingLocation);

                foreach (var a in ep.AirDates)
                    if (a.Date is DateTime d)
                        yield return new ScheduleEvent(d, ScheduleEventKind.Airing, c, ep, a.Platform);
            }
        }
    }

    public void Rebuild()
    {
        var campaigns = SelectedCampaign?.Campaign is Campaign c
            ? new[] { c }
            : Main.Data.AllCampaigns.ToArray();
        var episodes = campaigns.SelectMany(x => x.Episodes);

        var all = AllEvents(campaigns).OrderBy(e => e.Date).ThenBy(e => e.Kind).ThenBy(e => e.Episode?.Number).ToList();

        NextFilming = all.FirstOrDefault(e => e.IsFilming && !e.IsPast);
        NextAiring = all.FirstOrDefault(e => !e.IsFilming && !e.IsPast);

        Months = all
            .Where(e => ShowPast || !e.IsPast)
            .Where(e => e.IsFilming ? ShowFilming : ShowAiring)
            .GroupBy(e => new DateTime(e.Date.Year, e.Date.Month, 1))
            .Select(g => new MonthGroup(g.Key.ToString("MMMM yyyy", CultureInfo.CurrentCulture), g.ToList()))
            .ToList();

        Unscheduled = episodes
            .Where(e => e.DateFilmed == null && !e.AirDates.Any(a => a.Date.HasValue))
            .OrderBy(e => e.Number)
            .ToList();
    }
}
