using DmapShared.Models;
using System.Collections;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json.Serialization;

namespace DmapShared.Storage;

/// <summary>Puts pages that arrive from other people into the guide on screen, in place.</summary>
public static class GuideTree
{
    /// <summary>
    /// Put a page that arrived into the guide, updating the copy already there if any.
    /// Returns false when its season or campaign hasn't arrived yet.
    /// </summary>
    public static bool Place(ShowData data, object incoming, object? existing, Dictionary<Guid, TreeNode> index)
    {
        switch (incoming)
        {
            case ShowData:
                CopyInto(incoming, data);
                return true;

            case Season s:
            {
                var season = existing as Season ?? s;
                if (existing != null) CopyInto(s, season);
                else index[s.Id] = s;
                PlaceSorted(data.Seasons, season, x => x.Number);
                return true;
            }

            case Campaign c:
            {
                if (index.GetValueOrDefault(c.SeasonId) is not Season parent) return false;
                var campaign = existing as Campaign ?? c;
                if (existing != null) CopyInto(c, campaign);
                else index[c.Id] = c;

                if (campaign.Season != parent)
                {
                    campaign.Season?.Campaigns.Remove(campaign);
                    campaign.Season = parent;
                }
                PlaceSorted(parent.Campaigns, campaign, x => x.Number);

                campaign.RosterPage.RefreshAppearances();
                foreach (var ep in campaign.Episodes) ep.RefreshCast();
                return true;
            }

            case Episode e:
            {
                if (index.GetValueOrDefault(e.CampaignId) is not Campaign owner) return false;
                var episode = existing as Episode ?? e;
                if (existing != null) CopyInto(e, episode);
                else index[e.Id] = e;

                if (episode.Campaign != owner)
                {
                    episode.Campaign?.Episodes.Remove(episode);
                    episode.Campaign = owner;
                }
                PlaceSorted(owner.Episodes, episode, x => x.Number);

                episode.Attach();
                episode.RefreshCast();
                owner.RosterPage.RefreshAppearances();
                return true;
            }
        }
        return false;
    }

    public static void Remove(ShowData data, TreeNode node)
    {
        switch (node)
        {
            case Season s: data.Seasons.Remove(s); break;
            case Campaign c: c.Season?.Campaigns.Remove(c); break;
            case Episode e:
                e.Campaign?.Episodes.Remove(e);
                e.Campaign?.RosterPage.RefreshAppearances();
                break;
        }
    }

    /// <summary>Insert or move <paramref name="item"/> so the list stays in number order.</summary>
    public static void PlaceSorted<T>(ObservableCollection<T> list, T item, Func<T, int> key)
    {
        int from = list.IndexOf(item);
        int to = list.Count(x => !ReferenceEquals(x, item) && key(x) <= key(item));
        if (from < 0) list.Insert(to, item);
        else if (from != to) list.Move(from, to);
    }

    public static Dictionary<Guid, TreeNode> Index(ShowData data)
    {
        var index = new Dictionary<Guid, TreeNode>();
        foreach (var s in data.Seasons)
        {
            index[s.Id] = s;
            foreach (var c in s.Campaigns)
            {
                index[c.Id] = c;
                foreach (var e in c.Episodes) index[e.Id] = e;
            }
        }
        return index;
    }

    private static readonly HashSet<string> NotCopied = new() { nameof(TreeNode.Id), "Seasons", "Campaigns", "Episodes" };

    /// <summary>Copy a page's saved fields onto the copy already on screen, so the tree and page update in place.</summary>
    public static void CopyInto(object from, object to)
    {
        foreach (var p in to.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || !p.CanWrite || p.GetIndexParameters().Length > 0 || NotCopied.Contains(p.Name)) continue;
            if (p.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always }) continue;

            var value = p.GetValue(from);
            if (p.GetValue(to) is IList list && value is IList items)
            {
                list.Clear();
                foreach (var item in items) list.Add(item);
            }
            else if (!Equals(p.GetValue(to), value))
            {
                p.SetValue(to, value);
            }
        }
    }
}
