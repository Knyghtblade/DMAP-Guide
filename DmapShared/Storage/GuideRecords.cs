using DmapShared.Models;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DmapShared.Storage;

/// <summary>
/// The guide is saved one record per page: the show-wide details, and one record
/// for each season, campaign and episode. Child pages are their own records, so
/// they're left out of their parent's record. The shared folder keeps each record
/// as a file and the shared server keeps each as a row; both use this format.
/// </summary>
public static class GuideRecords
{
    public const string Guide = "guide";
    public const string Season = "season";
    public const string Campaign = "campaign";
    public const string Episode = "episode";

    /// <summary>The show-wide record's id (seasons, campaigns and episodes use their GUID).</summary>
    public const string GuideId = "guide";

    /// <summary>
    /// The id used for "someone is editing" warnings on the Cast page, which edits the
    /// show-wide record; that record has no GUID of its own.
    /// </summary>
    public static readonly Guid GuideLockId = new("00000000-0000-0000-0000-00000000da7a");

    /// <summary>Child lists live in their own records.</summary>
    private static readonly string[] ChildLists = { "Seasons", "Campaigns", "Episodes" };

    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static bool IsKnownKind(string? kind) => kind is Guide or Season or Campaign or Episode;

    /// <summary>Parents come before children (guide, season, campaign, episode).</summary>
    public static int Rank(string kind) => kind switch
    {
        Guide => 0,
        Season => 1,
        Campaign => 2,
        _ => 3
    };

    public static Type TypeFor(string kind) => kind switch
    {
        Guide => typeof(ShowData),
        Season => typeof(Season),
        Campaign => typeof(Campaign),
        Episode => typeof(Episode),
        _ => throw new ArgumentException($"Unknown record kind '{kind}'.", nameof(kind))
    };

    public static string KindOf(object record) => record switch
    {
        ShowData => Guide,
        Season => Season,
        Campaign => Campaign,
        Episode => Episode,
        _ => throw new ArgumentException("Not a guide record.", nameof(record))
    };

    public static string IdOf(object record) => record is TreeNode node ? IdText(node.Id) : GuideId;

    public static string IdText(Guid id) => id.ToString("D");

    /// <summary>Every page in the guide, parents first, with the id of the page it belongs to.</summary>
    public static IEnumerable<(string Kind, string Id, string? ParentId, object Record)> All(ShowData data)
    {
        yield return (Guide, GuideId, null, data);
        foreach (var s in data.Seasons)
        {
            yield return (Season, IdText(s.Id), null, s);
            foreach (var c in s.Campaigns)
            {
                c.SeasonId = s.Id;
                yield return (Campaign, IdText(c.Id), IdText(s.Id), c);
                foreach (var e in c.Episodes)
                {
                    e.CampaignId = c.Id;
                    yield return (Episode, IdText(e.Id), IdText(c.Id), e);
                }
            }
        }
    }

    /// <summary>A page's own saved fields, as JSON (child pages left out).</summary>
    public static string Serialize(object record)
    {
        var node = JsonSerializer.SerializeToNode(record, record.GetType(), JsonOptions)!.AsObject();
        foreach (var list in ChildLists) node.Remove(list);
        return node.ToJsonString(JsonOptions);
    }

    /// <summary>
    /// The record exactly as it was sent, with the "last edited" stamp the server gave it. Used to
    /// remember what the server holds without re-reading the live page, which may have been
    /// edited again while the save was on its way.
    /// </summary>
    public static string WithStamp(string json, string editedBy, DateTime? editedAt)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        node[nameof(TreeNode.LastEditedBy)] = editedBy;
        node[nameof(TreeNode.LastEdited)] = editedAt is DateTime at ? JsonValue.Create(at) : null;
        return node.ToJsonString(JsonOptions);
    }

    /// <summary>Read a page's record. Throws <see cref="JsonException"/> if it isn't a valid record.</summary>
    public static object Parse(string kind, string json)
    {
        var record = JsonSerializer.Deserialize(json, TypeFor(kind), JsonOptions) ?? throw new JsonException("Empty record");
        if (JsonNode.Parse(json) is not JsonObject)
            throw new JsonException("A record must be a JSON object");
        return record;
    }
}
