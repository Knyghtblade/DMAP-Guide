using System.Text.RegularExpressions;
using DmapShared.Models;

namespace DmapShared;

/// <summary>
/// From 502 on, one Hot Sheets PDF covers two episodes. Laid out (see <see cref="HotSheetReader"/>) it reads:
/// the title "Dungeon Masters 502 &amp; 503", the crew, a divider, an OVERVIEW split under "502" and "503"
/// headings, a divider, then per episode a boxed table ("Marching Orders-#502" / LOCATION / SYNOPSIS: 502)
/// and a timeline table whose second column is the episode ("10:43:25:06 | 502 | start").
/// This works out which episode each paragraph, table and timeline row belongs to, so one episode's
/// parts can be kept and the rest taken out, and where each section starts (for "Remove section").
/// </summary>
public static partial class HotSheetEpisodes
{
    [GeneratedRegex(@"^\s*(\d{3,4})\s*$")] private static partial Regex BareNumber();
    [GeneratedRegex(@"(\d{3,4})(?!.*\d{3,4})", RegexOptions.Singleline)] private static partial Regex LastNumber();
    [GeneratedRegex(@"^\s*SYNOPSIS\s*:?\s*(\d{3,4})", RegexOptions.IgnoreCase)] private static partial Regex SynopsisNumber();
    [GeneratedRegex(@"^\s*\d{1,2}:\d{2}:\d{2}[:;]\d{2}\s*$")] private static partial Regex Timecode();
    [GeneratedRegex(@"\b(\d{3,4})\s*(?:&|and|\+|/|,)\s*(\d{3,4})\b", RegexOptions.IgnoreCase)] private static partial Regex EpisodePair();

    /// <summary>Who each block belongs to (null = shared, like the title and crew), and each timeline row.</summary>
    public sealed class Owners
    {
        public Dictionary<HotSheetBlock, int?> Blocks { get; } = new();
        public Dictionary<HotSheetRow, int?> Rows { get; } = new();
        public HashSet<HotSheetBlock> SectionStarts { get; } = new();
        public SortedSet<int> Episodes { get; } = new();
    }

    public static Owners Read(IEnumerable<HotSheetBlock> blocks)
    {
        var list = blocks.ToList();
        var owners = new Owners();

        // The episodes the sheet is split into, from its headings and episode boxes.
        foreach (var block in list)
            if ((HeadingEpisode(block) ?? BoxEpisode(block)) is int ep) owners.Episodes.Add(ep);

        int? current = null;
        HotSheetBlock? previous = null;
        foreach (var block in list)
        {
            if (block.Kind == HotSheetBlockKind.Rule)
            {
                current = null;   // a divider ends the section above it
                owners.Blocks[block] = null;
            }
            else if (HeadingEpisode(block) is int heading)
            {
                current = heading;
                owners.Blocks[block] = heading;
                owners.SectionStarts.Add(block);
            }
            else if (BoxEpisode(block) is int box)
            {
                // A box split in two ("Friend or Spy 507" on its own, then LOCATION/SYNOPSIS) is one section.
                if (!(current == box && previous != null && BoxEpisode(previous) == box)) owners.SectionStarts.Add(block);
                current = box;
                owners.Blocks[block] = box;
            }
            else
            {
                owners.Blocks[block] = current;
                if (block.Kind == HotSheetBlockKind.Table)
                    foreach (var row in block.Rows)
                        owners.Rows[row] = RowEpisode(row, owners.Episodes) ?? current;
            }
            previous = block;
        }
        return owners;
    }

    /// <summary>The episodes the sheet covers, e.g. 502 and 503 (one, or none, for a single-episode sheet).</summary>
    public static IReadOnlyList<int> EpisodesIn(IEnumerable<HotSheetBlock> blocks) => Read(blocks).Episodes.ToList();

    /// <summary>The sheet covers this episode and at least one other, so the others' parts can be taken out.</summary>
    public static bool CoversOtherEpisodes(IEnumerable<HotSheetBlock> blocks, int episode)
    {
        var eps = Read(blocks).Episodes;
        return eps.Count > 1 && eps.Contains(episode);
    }

    /// <summary>
    /// The sheet with only this episode's parts: the title, date and crew stay (the title's "502 &amp; 503"
    /// becomes "502"), and the other episodes' overview, box and timeline rows go.
    /// </summary>
    public static List<HotSheetBlock> KeepOnly(IEnumerable<HotSheetBlock> blocks, int episode)
    {
        var list = blocks.ToList();
        var owners = Read(list);
        if (!(owners.Episodes.Count > 1 && owners.Episodes.Contains(episode))) return list;

        var kept = new List<HotSheetBlock>();
        bool seenEpisode = false;
        foreach (var block in list)
        {
            int? owner = owners.Blocks[block];
            if (owner != null) seenEpisode = true;

            if (owner != null && owner != episode)
            {
                // "OVERVIEW:" sits on the same line block as the first episode's number: keep the label.
                if (HeadingEpisode(block) != null && Lines(block.Text).Count > 1)
                {
                    var rest = Lines(block.Text).Take(Lines(block.Text).Count - 1);
                    kept.Add(HotSheetBlock.Paragraph(string.Join("\n", rest), block.Style));
                }
                continue;
            }

            if (block.Kind == HotSheetBlockKind.Table)
            {
                // Timeline rows tagged with another episode go, wherever the table sits.
                foreach (var row in block.Rows.Where(r => owners.Rows.TryGetValue(r, out var o) && o != null && o != episode).ToList())
                    block.Rows.Remove(row);
                if (block.Rows.Count == 0) continue;
            }

            // The title (and anything else before the first episode's part) names this episode only.
            if (!seenEpisode && block.Kind == HotSheetBlockKind.Text)
                block.Text = EpisodePair().Replace(block.Text, m =>
                    m.Groups[1].Value == episode.ToString() || m.Groups[2].Value == episode.ToString() ? episode.ToString() : m.Value);

            kept.Add(block);
        }

        // No two dividers in a row, and none at the very end.
        var tidy = new List<HotSheetBlock>();
        foreach (var block in kept)
        {
            if (block.Kind == HotSheetBlockKind.Rule && (tidy.Count == 0 || tidy[^1].Kind == HotSheetBlockKind.Rule)) continue;
            tidy.Add(block);
        }
        while (tidy.Count > 0 && tidy[^1].Kind == HotSheetBlockKind.Rule) tidy.RemoveAt(tidy.Count - 1);
        return tidy;
    }

    /// <summary>What "Remove section" on a heading or episode box takes out: it and everything up to the next section or divider.</summary>
    public static List<HotSheetBlock> SectionOf(IList<HotSheetBlock> blocks, HotSheetBlock start)
    {
        var owners = Read(blocks);
        int from = blocks.IndexOf(start);
        if (from < 0) return new();
        int to = from + 1;
        while (to < blocks.Count && !owners.SectionStarts.Contains(blocks[to]) && blocks[to].Kind != HotSheetBlockKind.Rule) to++;
        return blocks.Skip(from).Take(to - from).ToList();
    }

    /// <summary>Marks the blocks that start a section, for the page's "Remove section" buttons.</summary>
    public static void MarkSections(IEnumerable<HotSheetBlock> blocks)
    {
        var list = blocks.ToList();
        var starts = Read(list).SectionStarts;
        foreach (var block in list) block.StartsSection = starts.Contains(block);
    }

    /// <summary>A paragraph whose last line is just an episode number: "502", or "OVERVIEW:" then "502".</summary>
    private static int? HeadingEpisode(HotSheetBlock block)
    {
        if (block.Kind != HotSheetBlockKind.Text) return null;
        var lines = Lines(block.Text);
        if (lines.Count == 0 || lines.Count > 3) return null;
        return BareNumber().Match(lines[^1]) is { Success: true } m ? int.Parse(m.Groups[1].Value) : null;
    }

    /// <summary>
    /// An episode's box: a table whose first row is one cell naming the episode ("Marching Orders-#502",
    /// "Friend or Spy 507"), or a LOCATION / CAST table whose SYNOPSIS names it.
    /// </summary>
    private static int? BoxEpisode(HotSheetBlock block)
    {
        if (block.Kind != HotSheetBlockKind.Table || block.Rows.Count == 0) return null;
        bool hasLocation = block.Rows.Any(r => r.Cells.FirstOrDefault()?.Text.TrimStart().StartsWith("LOCATION", StringComparison.OrdinalIgnoreCase) == true);
        var first = block.Rows[0];
        bool titleRow = first.Cells.Count == 1 && first.Cells[0].Text.Trim().Length is > 0 and <= 80;

        if (titleRow && (hasLocation || block.Rows.Count == 1) && LastNumber().Match(first.Cells[0].Text) is { Success: true } title)
            return int.Parse(title.Groups[1].Value);
        if (hasLocation)
            foreach (var row in block.Rows)
                foreach (var cell in row.Cells)
                    if (SynopsisNumber().Match(cell.Text) is { Success: true } synopsis)
                        return int.Parse(synopsis.Groups[1].Value);
        return null;
    }

    /// <summary>A timeline row ("10:43:25:06 | 502 | start"): the episode in its second column. A short tag ("1") matches the one episode ending in it.</summary>
    private static int? RowEpisode(HotSheetRow row, SortedSet<int> episodes)
    {
        if (row.Cells.Count < 2 || !Timecode().IsMatch(row.Cells[0].Text)) return null;
        string tag = row.Cells[1].Text.Trim();
        if (!int.TryParse(tag, out int n)) return null;
        if (episodes.Contains(n)) return n;
        var match = episodes.Where(e => e.ToString().EndsWith(tag)).ToList();
        return match.Count == 1 ? match[0] : null;
    }

    private static List<string> Lines(string text) =>
        text.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
}
