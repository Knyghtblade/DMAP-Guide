using DmapShared.Models;
using DmapShared.ViewModels;
using System.Globalization;
using System.Net;
using System.Text;

namespace DmapShared.Export;

/// <summary>
/// The whole guide as one read-only web page: the table of contents down the side, one page
/// at a time on the right, pictures inside the file. Anyone can open it in a browser (on a
/// Mac, a phone, anywhere) without the app, OneDrive or any settings. It is a snapshot: it
/// shows the guide as it was when it was made.
/// </summary>
public static class WebCopy
{
    /// <summary>
    /// Build the page. <paramref name="picture"/> turns a stored picture (a portrait or cover
    /// art) into something a web page can show inline (a data: address), or null to leave it out.
    /// </summary>
    public static string Build(ShowData data, string appVersion, DateTime made, Func<string, string?> picture)
    {
        var pictures = new Dictionary<string, string?>();
        string? Picture(string stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return null;
            if (!pictures.TryGetValue(stored, out var src)) pictures[stored] = src = picture(stored);
            return src;
        }

        data.RefreshCastAppearances();
        foreach (var c in data.AllCampaigns) c.RosterPage.RefreshAppearances();

        var w = new Writer(Picture, data);
        string show = string.IsNullOrWhiteSpace(data.ShowName) ? "Dungeon Masters" : data.ShowName.Trim();

        string logo = Art("logo.png", "image/png");
        string artwork = ":root{--hide:url(" + Art("dragon-scales.jpg", "image/jpeg") + ");--logo:url(" + logo + ")}";
        var current = data.CurrentCampaign;
        string asOf = made.ToString("MMMM d, yyyy 'at' h:mm tt", CultureInfo.CurrentCulture);

        w.Raw($"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{H(show)} Production Guide</title>
            <link rel="preconnect" href="https://fonts.googleapis.com">
            <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
            <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Cinzel:wght@400;700&family=Cinzel+Decorative:wght@400;700&family=EB+Garamond:ital,wght@0,400;0,600;1,400&display=swap">
            <style>{artwork}</style>
            <style>{Css}</style>
            </head>
            <body>
            <div class="candles" aria-hidden="true"><i class="candle c1"></i><i class="candle c2"></i></div>
            <header class="bar">
              <button class="menu" type="button" aria-expanded="false" aria-controls="toc">☰ Contents</button>
              <span class="bar-title">{H(show)} · Production Guide</span>
            </header>
            <div class="book">
            <i class="gilt tl"></i><i class="gilt tr"></i><i class="gilt bl"></i><i class="gilt br"></i>
            <div class="pages">
            <nav id="toc" class="leaf left" aria-label="Table of contents">
              <div class="title-block">
                <div class="logo" role="img" aria-label="{H(show)}"></div>
                <div class="toc-sub">The Production Guide</div>
                <div class="flourish">— ❦ —</div>
              </div>
              <a class="nav" href="#front"><span class="glyph">❧</span>Prologue</a>
              <a class="nav" href="#schedule"><span class="glyph">☾</span>Chronicle of Dates</a>
              <a class="nav" href="#cast"><span class="glyph">♞</span>Cast</a>
              <div class="toc-rule">Table of Contents</div>
            """);

        // Seasons and campaigns fold open and shut like the app's table of contents.
        foreach (var season in data.Seasons)
        {
            w.Raw($"""<details class="t-season" open><summary><a href="#{Id(season)}">{H(season.DisplayName)}</a></summary>""");
            foreach (var campaign in season.Campaigns)
            {
                w.Raw($"""<details class="t-campaign"{(campaign == current ? " open" : "")}><summary><a href="#{Id(campaign)}">{H(campaign.DisplayName)}</a></summary>""");
                foreach (var episode in campaign.Episodes)
                    w.Raw($"""<a class="t-episode" href="#{Id(episode)}">{H(episode.DisplayName)}</a>""");
                w.Raw("</details>");
            }
            w.Raw("</details>");
        }
        if (data.Seasons.Count == 0) w.Raw("""<p class="empty">No seasons yet.</p>""");

        w.Raw($"""
              <p class="made">Read-only copy of the guide as it was on {H(asOf)}, made by DMAP Guide {H(appVersion)}. Changes are made in the app.</p>
            </nav>
            <div class="leaf right">
            <main>
            """);

        Front(w, data, show, asOf);
        Schedule(w, data);
        Cast(w, data, show);
        foreach (var season in data.Seasons)
        {
            SeasonPage(w, season);
            foreach (var campaign in season.Campaigns)
            {
                CampaignPage(w, campaign);
                foreach (var episode in campaign.Episodes) EpisodePage(w, episode, data);
            }
        }

        w.Raw($"""
            </main>
            <div class="turn" aria-hidden="true"><div class="turn-page"></div><i class="turn-shade"></i></div>
            </div>
            </div>
            <i class="ribbon" aria-hidden="true"></i>
            <div class="cover" role="button" tabindex="0" aria-label="Open the guide">
              <div class="door back"><i class="emboss"></i></div>
              <div class="door front">
                <div class="plate"><div class="logo" role="img" aria-label="{H(show)}"></div></div>
                <div class="cover-title">The Production Guide</div>
                <div class="cover-hint"><span class="click">click</span><span class="tap">tap</span> to open the tome</div>
              </div>
              <i class="seam"></i>
            </div>
            </div>
            <div class="sparkles" aria-hidden="true"></div>
            <style>{w.PortraitStyles()}</style>
            <script>{Script}</script>
            </body>
            </html>
            """);
        return w.ToString();
    }

    // ============================================================
    // PAGES
    // ============================================================
    // The app's Prologue: the logo, the counts, what's current, what's coming up.
    private static void Front(Writer w, ShowData data, string show, string made)
    {
        w.Raw($"""
            <section class="page prologue" id="s-front">
            <div class="logo big" role="img" aria-label="{H(show)}"></div>
            <div class="prologue-title">The Production Guide</div>
            <div class="flourish">— ❦ —</div>
            """);

        int seasons = data.Seasons.Count, campaigns = data.AllCampaigns.Count(), episodes = data.AllEpisodes.Count();
        w.Raw($"""
            <div class="counts">
              <div><b>{seasons}</b><span>{(seasons == 1 ? "Season" : "Seasons")}</span></div>
              <div><b>{campaigns}</b><span>{(campaigns == 1 ? "Campaign" : "Campaigns")}</span></div>
              <div><b>{episodes}</b><span>{(episodes == 1 ? "Episode" : "Episodes")}</span></div>
            </div>
            """);

        var eps = data.AllEpisodes.ToList();
        var latest = data.LatestAiredEpisode ?? eps.LastOrDefault(e => e.Status != EpisodeStatus.Planned) ?? eps.LastOrDefault();
        w.Raw("""<div class="highlights">""");
        if (data.CurrentCampaign is Campaign current)
            w.Raw($"""<a class="highlight" href="#{Id(current)}"><span class="label">⚔ Current Campaign</span><b>{H(current.DisplayName)}</b></a>""");
        if (latest != null)
            w.Raw($"""<a class="highlight" href="#{Id(latest)}"><span class="label">✧ Latest Episode</span><b>{H(latest.DisplayName)}</b></a>""");
        w.Raw("</div>");

        w.Section("Coming Up");
        var upcoming = ScheduleViewModel.AllEvents(data.AllCampaigns).Where(e => !e.IsPast).OrderBy(e => e.Date).Take(6).ToList();
        if (upcoming.Count == 0) w.Empty("No upcoming dates yet.");
        foreach (var e in upcoming) Event(w, e);

        w.Section("Using this Tome");
        w.Raw($"""
            <p>Choose a season, campaign or episode from the Table of Contents to open its page. Click a character or a cast member to see who plays whom.</p>
            <p class="note">This is a read-only copy of the guide, as it was on {H(made)}. Changes are made in the DMAP Guide app, and this copy is brought up to date when someone who made changes closes the app.</p>
            """);
        w.Close();
    }

    private static void Schedule(Writer w, ShowData data)
    {
        w.Open("schedule", "Filming and air dates", "Chronicle of Dates");
        var all = ScheduleViewModel.AllEvents(data.AllCampaigns)
            .OrderBy(e => e.Date).ThenBy(e => e.Kind).ThenBy(e => e.Episode?.Number).ToList();
        if (all.Count == 0) w.Empty("No dates set yet.");

        foreach (var month in all.GroupBy(e => new DateTime(e.Date.Year, e.Date.Month, 1)))
        {
            w.Section(month.Key.ToString("MMMM yyyy", CultureInfo.CurrentCulture));
            foreach (var e in month) Event(w, e);
        }

        var unscheduled = data.AllEpisodes.Where(e => e.DateFilmed == null && !e.AirDates.Any(a => a.Date.HasValue)).OrderBy(e => e.Number).ToList();
        if (unscheduled.Count > 0)
        {
            w.Section("Not Yet Scheduled");
            w.Raw("<ul class=\"plain\">");
            foreach (var e in unscheduled) w.Raw($"<li>{Link(e)}</li>");
            w.Raw("</ul>");
        }
        w.Close();
    }

    private static void Event(Writer w, ScheduleEvent e)
    {
        string kind = e.IsFilming ? "Filming" : "Airs";
        string target = e.Episode != null ? Id(e.Episode) : Id(e.Campaign);
        w.Raw($"""
            <a class="event{(e.IsPast ? " past" : "")}{(e.IsFilming ? " filming" : " airing")}" href="#{target}">
              <span class="day">{H(e.Date.ToString("MMM", CultureInfo.CurrentCulture))}<b>{H(e.Day)}</b>{H(e.Weekday)}</span>
              <span class="what"><span class="kind">{kind}</span> <b>{H(e.Heading)}</b><br><i>{H(e.Subline)}</i></span>
            </a>
            """);
    }

    private static void Cast(Writer w, ShowData data, string show)
    {
        w.Open("cast", show, "Cast");
        w.Raw("""<p class="note">The people who make the show, across every campaign. Click a character to see them in their campaign.</p>""");

        // As the app's Cast page: the campaigns a Dungeon Master runs, the characters anyone
        // plays (guest stars too), and the episodes a guest star is in.
        People(w, "Dungeon Masters", data.DungeonMasters, "No Dungeon Masters listed.");
        People(w, "The Company", data.Cast, "No one on the cast list yet.");
        People(w, "Guest Stars", data.Guests, "No guest stars listed.", p =>
            data.AllEpisodes.Where(e => e.GuestIds.Contains(p.Id))
                .Select(e => w.Chip("#" + Id(e), null, "☾", e.DisplayName, e.Campaign?.DisplayName ?? "")));
        w.Close();
    }

    private static void People(Writer w, string title, IEnumerable<CastMember> people, string empty,
        Func<CastMember, IEnumerable<string>>? episodes = null)
    {
        w.Section(title);
        var list = people.ToList();
        if (list.Count == 0) w.Empty(empty);
        foreach (var p in list)
        {
            string Row(string heading, string style, IEnumerable<string> links)
            {
                var all = links.ToList();
                return all.Count == 0 ? "" : $"""<div class="label plays-label">{H(heading)}</div><div class="plays {style}">{string.Concat(all)}</div>""";
            }
            string extra =
                Row("Runs", "tiles", p.Runs.Select(c => w.CampaignTile(c))) +
                Row("Plays", "tiles", p.Roles.Select(r => w.Tile($"#{Id(r.Campaign)}/{MemberId(r.Character)}", r.Character, r.Character.CharacterName, r.CampaignName))) +
                Row("Guests in", "", episodes?.Invoke(p) ?? []);
            w.Card(p, p.PlayerName, p.CharacterSummary, p.CharacterDescription, extra.Length > 0 ? null : p.Appearances,
                extra: extra.Length > 0 ? extra : null);
        }
    }

    private static void SeasonPage(Writer w, Season s)
    {
        w.Open(Id(s), "Dungeon Masters", string.IsNullOrWhiteSpace(s.Title) ? $"Season {s.Number}" : $"Season {s.Number} · {s.Title}");
        w.Raw("""<div class="fields">""");
        w.Field("Year", H(s.Year));
        w.Raw("</div>");
        w.Text("About this Season", s.Description);

        w.Section("Campaigns");
        if (s.Campaigns.Count == 0) w.Empty("No campaigns yet.");
        w.Raw("<ul class=\"plain\">");
        foreach (var c in s.Campaigns)
            w.Raw($"<li>⚔ {Link(c)} <i>· {c.Episodes.Count} {Plural(c.Episodes.Count, "episode")}</i></li>");
        w.Raw("</ul>");
        w.Close();
    }

    private static void CampaignPage(Writer w, Campaign c)
    {
        w.Open(Id(c), c.Season?.DisplayName ?? "", $"Campaign {c.Number}" + (string.IsNullOrWhiteSpace(c.Title) ? "" : $" · {c.Title}"));

        w.Raw("""<div class="split"><div class="fields">""");
        w.Field("Dungeon Master", w.PersonLink(c.DungeonMaster));
        w.Field("Setting / Game System", H(c.Setting));
        w.Field("Filming starts", H(Date(c.FilmingStart)));
        w.Field("Filming ends", H(Date(c.FilmingEnd)));
        w.Field("Filming location", H(c.FilmingLocation));
        w.Raw("</div>");
        string? cover = w.Picture(c.CoverImage);
        if (cover != null || !string.IsNullOrWhiteSpace(c.BookTitle))
        {
            w.Raw("""<figure class="cover-art">""");
            if (cover != null) w.Raw($"""<img src="{cover}" alt="Cover art">""");
            if (!string.IsNullOrWhiteSpace(c.BookTitle)) w.Raw($"<figcaption>{H(c.BookTitle)}</figcaption>");
            w.Raw("</figure>");
        }
        w.Raw("</div>");

        w.Text("About this Campaign", c.Description);

        w.Section("Characters");
        if (c.Characters.Count == 0) w.Empty("No characters yet.");
        foreach (var m in c.Characters)
            w.Card(m, m.CharacterName, m.CharacterSummary,
                m.CharacterDescription, m.Appearances, playedBy: m.PlayerName, guest: m.IsGuest);

        w.Section("Episodes");
        if (c.Episodes.Count == 0) w.Empty("No episodes yet.");
        else
        {
            w.Raw("""<div class="scroll"><table><thead><tr><th>No.</th><th>Title</th><th>Filmed</th><th>First aired</th><th>Status</th></tr></thead><tbody>""");
            foreach (var e in c.Episodes)
                w.Raw($"""<tr><td>{e.Number}</td><td>{Link(e)}</td><td>{H(Date(e.DateFilmed, "MMM d, yyyy"))}</td><td>{H(Date(e.FirstAirDate, "MMM d, yyyy"))}</td><td>{Status(e.Status)}</td></tr>""");
            w.Raw("</tbody></table></div>");
        }
        w.Close();
    }

    private static void EpisodePage(Writer w, Episode e, ShowData data)
    {
        w.Open(Id(e), e.Campaign?.DisplayName ?? "", $"Episode {e.Number}" + (string.IsNullOrWhiteSpace(e.Title) ? "" : $" · {e.Title}"), Status(e.Status));

        w.Raw("""<div class="fields">""");
        w.Field("Dungeon Master", w.PersonLink(e.DungeonMasterShown) + (e.IsDungeonMasterInherited ? " <i>(from the campaign)</i>" : ""));
        w.Field("Date filmed", H(Date(e.DateFilmed)));
        w.Field("Filming location", H(e.FilmingLocation));
        w.Field("Runtime", H(e.Runtime));
        w.Raw("</div>");

        w.Text("Synopsis", e.Synopsis);

        w.Section("The Adventuring Party");
        if (e.CastMembers.Count == 0) w.Empty("No cast chosen for this episode yet.");
        foreach (var m in e.CastMembers)
            w.Card(m, m.CharacterName, m.CharacterSummary, m.CharacterDescription, null, playedBy: m.PlayerName, small: true,
                nameLink: e.Campaign != null ? $"#{Id(e.Campaign)}/{MemberId(m)}" : null);

        if (e.GuestCount > 0)
        {
            w.Section("Guest Stars");
            foreach (var g in e.Guests) w.Card(g, g.PlayerName, g.CharacterSummary, null, null, small: true, nameLink: $"#cast/{MemberId(g)}");
            foreach (var g in e.GuestCast) w.Card(g, g.CharacterName, g.CharacterSummary, g.CharacterDescription, null, playedBy: g.PlayerName, small: true, guest: true);
        }

        w.Section("Key Moments");
        if (e.KeyMoments.Count == 0) w.Empty("No key moments recorded.");
        foreach (var k in e.KeyMoments)
            w.Raw($"""<div class="moment"><span class="time">✧ {H(k.Timestamp)}</span><div>{Paras(k.Description)}</div></div>""");

        w.Section("Air Dates");
        if (e.AirDates.Count == 0) w.Empty("No air dates set.");
        foreach (var a in e.AirDates)
        {
            string link = SafeLink(a.Link) is string url ? $"""<a class="watch" href="{H(url)}" target="_blank" rel="noopener">Watch ↗</a>""" : "";
            w.Raw($"""<div class="airdate"><b>☾ {H(Date(a.Date))}</b> <span>{H(a.Platform)}</span> <i>{H(a.Notes)}</i> {link}</div>""");
        }

        w.Text("Production Notes", e.ProductionNotes);
        w.Text("Shoot Notes", e.ShootNotes);
        w.Close();
    }

    // ============================================================
    // PIECES
    // ============================================================
    private sealed class Writer(Func<string, string?> picture, ShowData data)
    {
        private readonly StringBuilder _sb = new();
        private readonly Dictionary<string, string> _portraits = new();

        public string? Picture(string stored) => picture(stored);

        /// <summary>
        /// A portrait's style name. A character shows on many pages, so each picture is
        /// written once (see <see cref="PortraitStyles"/>) and every card refers to it.
        /// </summary>
        public string? Portrait(string stored)
        {
            if (Picture(stored) is not string src) return null;
            if (!_portraits.TryGetValue(src, out var css)) _portraits[src] = css = "pic" + _portraits.Count;
            return css;
        }

        public string PortraitStyles() =>
            string.Concat(_portraits.Select(p => $".{p.Value}{{background-image:url({p.Key})}}\n"));

        public void Raw(string html) => _sb.Append(html).Append('\n');

        public void Open(string id, string kicker, string title, string? badge = null)
        {
            Raw($"""<section class="page" id="s-{id}">""");
            if (!string.IsNullOrWhiteSpace(kicker)) Raw($"""<div class="kicker">{H(kicker)}</div>""");
            Raw($"""<h1>{H(title)}{(badge != null ? " " + badge : "")}</h1><div class="flourish">❦</div>""");
        }

        public void Close() => Raw("</section>");

        public void Section(string title) => Raw($"<h2>{H(title)}</h2>");

        public void Empty(string text) => Raw($"""<p class="empty">{H(text)}</p>""");

        /// <summary>A labelled value; left out when there's nothing to show.</summary>
        public void Field(string label, string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return;
            Raw($"""<div class="field"><div class="label">{H(label)}</div><div class="value">{html}</div></div>""");
        }

        /// <summary>A heading and its text; left out when the text is blank.</summary>
        public void Text(string title, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            Section(title);
            Raw($"""<div class="prose">{Paras(text)}</div>""");
        }

        /// <summary>A portrait filling its circle (placed as it is in the app), or <paramref name="glyph"/> without one.</summary>
        public string Pic(CastMember? m, string glyph = "✦") =>
            m != null && Portrait(m.ImagePath) is string css
                ? $"""<i class="pic {css}" style="background-position:{Pos(m.ImageOffsetX)}% {Pos(m.ImageOffsetY)}%"></i>"""
                : $"<span>{glyph}</span>";

        /// <summary>The person on the Cast page with this name, matched as the app matches them (by name).</summary>
        public CastMember? Person(string name) =>
            data.DungeonMasters.Concat(data.Cast).Concat(data.Guests).FirstOrDefault(p => SameName(p.PlayerName, name));

        /// <summary>A name with their photo, opening their card on the Cast page; plain when they aren't on it.</summary>
        public string PersonLink(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            return Person(name) is CastMember p
                ? $"""<a class="who-link" href="#cast/{MemberId(p)}"><span class="mini">{Pic(p)}</span>{H(name.Trim())}</a>"""
                : $"<b>{H(name.Trim())}</b>";
        }

        /// <summary>A small round picture with a name under it, linking somewhere (a character, a campaign, an episode).</summary>
        public string Chip(string href, CastMember? picture, string glyph, string title, string sub) =>
            $"""<a class="chip" href="{href}"><span class="chip-pic">{Pic(picture, glyph)}</span><span class="chip-text"><b>{H(title)}</b><i>{H(sub)}</i></span></a>""";

        /// <summary>A character in a person's gallery on the Cast page: their artwork, name and campaign.</summary>
        public string Tile(string href, CastMember character, string name, string sub) =>
            $"""<a class="tile" href="{href}"><span class="tile-pic">{Pic(character)}</span><b>{H(name)}</b><i>{H(sub)}</i></a>""";

        /// <summary>A campaign a Dungeon Master runs: its cover art, title and number, opening its page.</summary>
        public string CampaignTile(Campaign c)
        {
            string cover = Portrait(c.CoverImage) is string css ? $"""<i class="pic {css}"></i>""" : "<span>⚔</span>";
            string title = string.IsNullOrWhiteSpace(c.Title) ? c.DisplayName : c.Title;
            return $"""<a class="tile campaign" href="#{Id(c)}"><span class="tile-cover">{cover}</span><b>{H(title)}</b><i>Campaign {c.Number}</i></a>""";
        }

        /// <summary>A person or character card with their portrait.</summary>
        public void Card(CastMember m, string name, string summary, string? bio, string? appearances,
            string? playedBy = null, bool small = false, bool guest = false, string? nameLink = null, string? extra = null)
        {
            Raw($"""<div class="card{(small ? " small" : "")}" data-m="{MemberId(m)}"><div class="portrait">{Pic(m)}</div><div class="who">""");
            string badge = guest ? """ <span class="badge">Guest character</span>""" : "";
            string shown = H(string.IsNullOrWhiteSpace(name) ? "Unnamed" : name);
            if (nameLink != null) shown = $"""<a href="{nameLink}">{shown}</a>""";
            Raw($"""<div class="name">{shown}{badge}</div>""");
            if (!string.IsNullOrWhiteSpace(playedBy)) Raw($"""<div class="played">played by {PersonLink(playedBy)}</div>""");
            if (!string.IsNullOrWhiteSpace(summary)) Raw($"""<div class="summary">{H(summary)}</div>""");
            if (!string.IsNullOrWhiteSpace(bio)) Raw($"""<div class="bio">{Paras(bio)}</div>""");
            if (!string.IsNullOrWhiteSpace(appearances)) Raw($"""<div class="appears">{H(appearances)}</div>""");
            if (extra != null) Raw(extra);
            Raw("</div></div>");
        }

        public override string ToString() => _sb.ToString();
    }

    /// <summary>One of the tome's pictures built into this library, as a data: address.</summary>
    private static string Art(string name, string type)
    {
        using var stream = typeof(WebCopy).Assembly.GetManifestResourceStream("WebCopy." + name)
                           ?? throw new InvalidOperationException($"The web copy's {name} is missing from the app.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return $"data:{type};base64,{Convert.ToBase64String(bytes.ToArray())}";
    }

    private static string Id(TreeNode node) => "p" + node.Id.ToString("N");

    /// <summary>Marks a person's or character's card, so a link can open the page and point at them.</summary>
    private static string MemberId(CastMember m) => "m" + m.Id.ToString("N");

    // As the app matches a character's player to a person on the Cast page.
    private static bool SameName(string a, string b) =>
        a.Trim().Length > 0 && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Link(TreeNode node) => $"""<a href="#{Id(node)}">{H(node.DisplayName)}</a>""";

    private static string H(string? text) => WebUtility.HtmlEncode(text ?? "");

    /// <summary>Blank lines start a new paragraph; single line breaks are kept.</summary>
    private static string Paras(string? text) =>
        string.Concat((text ?? "").Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => $"<p>{H(p).Replace("\n", "<br>")}</p>"));

    private static string Date(DateTime? d, string format = "ddd, MMM d, yyyy") =>
        d?.ToString(format, CultureInfo.CurrentCulture) ?? "";

    private static string Status(EpisodeStatus s) => $"""<span class="status {s.ToString().ToLowerInvariant()}">{s}</span>""";

    private static string Plural(int n, string word) => n == 1 ? word : word + "s";

    // The app's portrait offset (-1…1, 1 = left/top edge) as a CSS object-position percentage.
    private static string Pos(double offset) =>
        (50 - 50 * Math.Clamp(offset, -1, 1)).ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>An air date's link as a web address, or null unless it's http(s) (as the app's link button does).</summary>
    internal static string? SafeLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        string address = url.Trim();
        if (!address.Contains("://")) address = "https://" + address;
        return Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Contains('.')
            ? uri.ToString() : null;
    }

    // ============================================================
    // LOOK AND BEHAVIOUR
    // ============================================================
    // The tome on the desk, as the app draws it: dragon-hide binding with gilt corners, two
    // parchment leaves with stacked page edges, a ribbon, candlelight, and the cover that
    // swings open like two doors (with sparkles from the spine) the first time it's opened.
    private const string Css = """
        :root{--ink:#2E1F14;--soft:#5A4431;--faded:#8C7A63;--rubric:#9E141E;--gold:#C9A24A;--gold-light:#E8CF8A;--arcane:#5B3E8C;
          --card:rgba(201,168,106,.15);--card-border:rgba(135,106,69,.33);--parch:#F7ECD2;--leather:#55101E;
          --paper:radial-gradient(ellipse at 45% 40%,#F7ECD2,#EEDDB6 55%,#DCC293 90%,#C9AA76);
          --grain:url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='256' height='256'%3E%3Cfilter id='n'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='.9' numOctaves='2' stitchTiles='stitch'/%3E%3CfeColorMatrix values='0 0 0 0 .23 0 0 0 0 .14 0 0 0 0 .06 .7 0 0 0 -.36'/%3E%3C/filter%3E%3Crect width='256' height='256' filter='url(%23n)'/%3E%3C/svg%3E");
          --shade:radial-gradient(ellipse at 50% 42%,rgba(0,0,0,.2),rgba(0,0,0,.47) 70%,rgba(0,0,0,.73));
          --corner:url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='-1 -1 36 36'%3E%3Cpath d='M0 34V0h34M6 34V6h28M12 12l6 6' fill='none' stroke='%23C9A24A' stroke-width='1.5'/%3E%3C/svg%3E")}
        *{box-sizing:border-box}
        html{background:#0E0905}
        body{margin:0;height:100vh;overflow:hidden;color:var(--ink);font:18px/1.5 "EB Garamond",Garamond,"Palatino Linotype",Georgia,serif;
          background:radial-gradient(ellipse at 50% 45%,#3B2717,#1E140C 70%,#0E0905)}
        a{color:var(--rubric)}
        ::selection{background:rgba(201,162,74,.45)}

        /* Candlelight on the desk */
        .candle{position:fixed;border-radius:50%;pointer-events:none}
        .c1{width:1100px;height:1100px;left:-480px;top:-480px;background:radial-gradient(closest-side,rgba(255,179,71,.4),rgba(255,154,46,.13) 45%,rgba(255,154,46,0));animation:flicker1 6.1s ease-in-out infinite alternate}
        .c2{width:900px;height:900px;right:-420px;bottom:-420px;background:radial-gradient(closest-side,rgba(255,179,71,.33),rgba(255,154,46,0));animation:flicker2 4.7s ease-in-out infinite alternate}
        @keyframes flicker1{0%{opacity:.6}9%{opacity:.74}17%{opacity:.58}30%{opacity:.8}41%{opacity:.63}55%{opacity:.77}63%{opacity:.56}78%{opacity:.75}88%{opacity:.62}100%{opacity:.7}}
        @keyframes flicker2{0%{opacity:.4}12%{opacity:.52}26%{opacity:.37}39%{opacity:.55}51%{opacity:.42}67%{opacity:.5}80%{opacity:.36}100%{opacity:.48}}

        /* The book: dragon-hide binding */
        .bar{display:none}
        .book{position:fixed;top:28px;bottom:52px;left:40px;right:40px;max-width:1800px;margin:0 auto;border-radius:16px;border:2px solid #1A0408;
          background:var(--shade),var(--hide) -100px -800px/1500px auto;box-shadow:0 16px 45px rgba(0,0,0,.8)}
        .book::before{content:"";position:absolute;inset:8px;border:1.2px solid rgba(201,162,74,.5);border-radius:12px;pointer-events:none}
        .gilt{position:absolute;width:36px;height:36px;background:var(--corner);opacity:.8;pointer-events:none}
        .tl{top:4px;left:4px}.tr{top:4px;right:4px;transform:rotate(90deg)}.br{bottom:4px;right:4px;transform:rotate(180deg)}.bl{bottom:4px;left:4px;transform:rotate(270deg)}
        .pages{position:absolute;inset:20px 24px;display:flex}
        .leaf{position:relative;background:var(--grain),var(--paper)}
        .leaf.left{width:400px;flex:none;border-radius:6px 0 0 6px;padding:22px 34px 18px 26px;overflow-y:auto;
          box-shadow:-3px 2px 0 #D8BF91,-7px 5px 0 #BFA273;
          background:linear-gradient(to right,transparent calc(100% - 44px),rgba(58,36,16,.16) calc(100% - 17px),rgba(58,36,16,.4)),var(--grain),var(--paper)}
        .leaf.right{flex:1;min-width:0;display:flex;flex-direction:column;border-radius:0 6px 6px 0;overflow:hidden;
          box-shadow:3px 2px 0 #D8BF91,7px 5px 0 #BFA273;
          background:linear-gradient(to right,rgba(58,36,16,.47),rgba(58,36,16,.16) 19px,transparent 54px),var(--grain),var(--paper)}
        .leaf.right::after{content:"— ❦ —";text-align:center;color:var(--gold);opacity:.8;padding:2px 0 10px}
        .ribbon{position:absolute;left:470px;bottom:-50px;width:22px;height:74px;pointer-events:none;
          background:linear-gradient(to right,#7A1418,#A8222B 50%,#6A1014);clip-path:polygon(0 0,100% 0,100% 100%,50% 84%,0 100%)}

        /* Left leaf: title and contents */
        .title-block{text-align:center}
        .logo{width:280px;max-width:100%;aspect-ratio:901/351;margin:0 auto;background:var(--logo) center/contain no-repeat;filter:drop-shadow(0 2px 3px rgba(0,0,0,.35))}
        .toc-sub{font:700 17px "Cinzel Decorative","Palatino Linotype",serif;color:var(--rubric);margin-top:6px}
        .flourish{color:var(--gold);margin:4px 0 12px}
        #toc a{display:block;border-radius:4px;text-decoration:none;color:var(--ink);transition:background .15s,color .15s}
        #toc a:hover{background:rgba(201,162,74,.2)}
        #toc a.on{background:rgba(201,162,74,.33);color:var(--rubric)}
        .nav{font:700 15px Cinzel,serif;padding:6px 8px}
        .glyph{color:var(--gold);display:inline-block;width:26px}
        .toc-rule{font:12px Cinzel,serif;letter-spacing:.15em;text-transform:uppercase;color:var(--faded);margin:16px 6px 6px;padding-top:10px;border-top:1px solid var(--gold)}
        details{margin:0}
        summary{list-style:none;display:flex;align-items:baseline;cursor:pointer}
        summary::-webkit-details-marker{display:none}
        summary::before{content:"▸";color:var(--gold);width:16px;flex:none;font-size:13px;transition:transform .2s;display:inline-block}
        details[open]>summary::before{transform:rotate(90deg)}
        summary a{flex:1;padding:3px 6px}
        .t-season>summary{font:700 15px Cinzel,serif;margin-top:6px}
        .t-campaign{margin-left:14px}
        .t-campaign>summary{font:700 13.5px Cinzel,serif;margin-top:2px}
        .t-episode{margin-left:16px;padding:2px 8px;font-size:16px}
        .made{font-size:13px;font-style:italic;color:var(--faded);margin-top:24px;border-top:1px solid var(--gold);padding-top:10px}

        /* Right leaf: the page */
        main{flex:1;overflow-y:auto;padding:24px 32px 24px 58px}
        .js .page{display:none}.js .page.current{display:block}
        .page{max-width:960px}
        .kicker{font:12px Cinzel,serif;letter-spacing:.15em;text-transform:uppercase;color:var(--faded)}
        h1{font:700 34px/1.2 "Cinzel Decorative","Palatino Linotype",serif;color:var(--rubric);margin:4px 0 0}
        h2{font:700 19px Cinzel,serif;color:var(--soft);border-bottom:1px solid var(--gold);padding-bottom:3px;margin:28px 0 12px}
        h2::before{content:"✦ ";color:var(--gold);font-size:14px}
        .lead{font-size:20px;font-style:italic;color:var(--soft)}
        .prologue{text-align:center;padding-top:6px}
        .prologue h2,.prologue p,.prologue .event{text-align:left}
        .logo.big{width:460px;filter:drop-shadow(0 3px 4px rgba(0,0,0,.35))}
        .prologue-title{font:700 26px "Cinzel Decorative","Palatino Linotype",serif;color:var(--rubric);margin-top:10px}
        .counts{display:grid;grid-template-columns:repeat(3,1fr);margin:4px 0 14px}
        .counts b{display:block;font:700 38px/1.1 "Cinzel Decorative","Palatino Linotype",serif;color:var(--rubric)}
        .counts span{font:11px Cinzel,serif;letter-spacing:.12em;text-transform:uppercase;color:var(--faded)}
        .highlights{display:grid;grid-template-columns:1fr 1fr;gap:14px;text-align:left}
        .highlight{display:block;text-decoration:none;color:var(--ink);background:var(--card);border:1px solid var(--card-border);border-radius:8px;padding:14px 16px;transition:transform .2s,box-shadow .2s}
        .highlight:hover{transform:translateY(-2px);box-shadow:0 6px 16px rgba(58,36,16,.18)}
        .highlight b{display:block;font:700 18px Cinzel,serif;color:var(--rubric);margin-top:4px}
        .note,.empty{font-style:italic;color:var(--faded)}
        .fields{display:grid;grid-template-columns:repeat(auto-fill,minmax(200px,1fr));gap:12px 24px;flex:1}
        .label{font:11px Cinzel,serif;letter-spacing:.12em;text-transform:uppercase;color:var(--faded)}
        .value{font-size:18px}
        .split{display:flex;gap:28px;align-items:flex-start}
        .cover-art{margin:0;flex:none;width:220px;text-align:center}
        .cover-art img{width:100%;display:block;border:6px solid var(--gold);outline:1px solid #876A45;outline-offset:-3px;border-radius:3px;box-shadow:0 4px 14px rgba(0,0,0,.4)}
        .cover-art figcaption{font:16px Cinzel,serif;color:var(--soft);margin-top:8px}
        .prose p{margin:0 0 10px}
        .card{display:flex;gap:16px;background:var(--card);border:1px solid var(--card-border);border-radius:8px;padding:14px;margin-bottom:12px;transition:transform .2s,box-shadow .2s}
        .card:hover{transform:translateY(-2px);box-shadow:0 6px 16px rgba(58,36,16,.18)}
        .portrait{position:relative;flex:none;width:120px;height:120px;border-radius:50%;border:3px solid var(--gold);overflow:hidden;display:flex;align-items:center;justify-content:center;background:rgba(135,106,69,.13);color:var(--gold);font-size:30px}
        .portrait::after{content:"";position:absolute;inset:4px;border-radius:50%;border:.8px solid rgba(90,68,49,.6);pointer-events:none}
        .pic{display:block;width:100%;height:100%;background-size:cover;background-repeat:no-repeat}
        .card.small .portrait{width:76px;height:76px;font-size:22px}
        .name{font:700 20px Cinzel,serif;color:var(--rubric)}
        .name a{color:inherit;text-decoration:none;border-bottom:1px dotted rgba(158,20,30,.5)}
        .name a:hover{border-bottom-style:solid}
        .who-link{color:var(--ink);font-weight:600;text-decoration:none;border-bottom:1px dotted var(--gold);white-space:nowrap}
        .who-link:hover{color:var(--rubric);border-bottom-style:solid}
        .mini{display:inline-flex;align-items:center;justify-content:center;width:24px;height:24px;border-radius:50%;overflow:hidden;
          vertical-align:-6px;margin-right:5px;border:1.5px solid var(--gold);background:rgba(135,106,69,.2);color:var(--gold);font-size:11px}
        .plays{display:flex;flex-wrap:wrap;gap:8px 12px;margin-top:12px}
        .chip{display:flex;align-items:center;gap:9px;text-decoration:none;color:var(--ink);padding:4px 14px 4px 4px;border-radius:26px;
          background:rgba(201,168,106,.2);border:1px solid var(--card-border);transition:background .15s,transform .15s,box-shadow .15s}
        .chip:hover{background:rgba(201,162,74,.38);transform:translateY(-2px);box-shadow:0 4px 10px rgba(58,36,16,.18)}
        .chip-pic{width:44px;height:44px;flex:none;border-radius:50%;overflow:hidden;border:1.5px solid var(--gold);background:rgba(135,106,69,.2);
          display:flex;align-items:center;justify-content:center;color:var(--gold);font-size:18px}
        .chip-text{display:flex;flex-direction:column;line-height:1.25;text-align:left}
        .chip-text b{font:700 14px Cinzel,serif;color:var(--rubric)}
        .chip-text i{font-size:14px;color:var(--faded)}
        .plays-label{margin-top:14px}
        .plays.tiles{gap:14px 18px;margin-top:6px}
        .tile{display:flex;flex-direction:column;align-items:center;width:118px;text-align:center;text-decoration:none;color:var(--ink);transition:transform .2s}
        .tile:hover{transform:translateY(-3px)}
        .tile-pic{position:relative;width:92px;height:92px;border-radius:50%;overflow:hidden;border:3px solid var(--gold);background:rgba(135,106,69,.2);
          display:flex;align-items:center;justify-content:center;color:var(--gold);font-size:26px;box-shadow:0 3px 8px rgba(58,36,16,.25);transition:box-shadow .2s}
        .tile:hover .tile-pic{box-shadow:0 0 0 3px rgba(232,207,138,.8),0 6px 14px rgba(58,36,16,.3)}
        .tile b{font:700 14px/1.2 Cinzel,serif;color:var(--rubric);margin-top:6px}
        .tile i{font-size:13px;line-height:1.25;color:var(--faded)}
        .tile.campaign{width:150px}
        .tile-cover{width:132px;height:92px;border-radius:4px;overflow:hidden;border:3px solid var(--gold);background:rgba(135,106,69,.2);
          display:flex;align-items:center;justify-content:center;color:var(--gold);font-size:26px;box-shadow:0 3px 8px rgba(58,36,16,.25);transition:box-shadow .2s}
        .tile:hover .tile-cover{box-shadow:0 0 0 3px rgba(232,207,138,.8),0 6px 14px rgba(58,36,16,.3)}
        .tile-cover .pic{background-position:center}
        .card.flash{animation:flash 1.8s ease-out}
        @keyframes flash{0%,30%{box-shadow:0 0 0 3px var(--gold),0 0 24px rgba(201,162,74,.9);background:rgba(232,207,138,.45)}100%{box-shadow:none}}
        .played{color:var(--soft)}
        .summary{font-style:italic;color:var(--soft)}
        .bio{margin-top:6px}.bio p{margin:0 0 6px}
        .appears{font-style:italic;color:var(--faded);margin-top:6px;font-size:16px}
        .badge{font:700 10px Cinzel,serif;color:var(--parch);background:var(--rubric);border-radius:3px;padding:2px 6px;vertical-align:middle}
        .status{font:700 11px Cinzel,serif;color:#fff;border-radius:3px;padding:2px 7px;vertical-align:middle;letter-spacing:.05em}
        .status.planned{background:#8C7A63}.status.filmed{background:#C08A1E}.status.aired{background:#4F7F3A}
        h1 .status{font-size:13px}
        .scroll{overflow-x:auto}
        table{border-collapse:collapse;width:100%}
        th{font:11px Cinzel,serif;letter-spacing:.1em;text-align:left;color:var(--faded);border-bottom:1px solid var(--gold);padding:4px 8px}
        td{padding:5px 8px;border-bottom:1px solid var(--card-border)}
        tbody tr:hover{background:var(--card)}
        .moment{display:flex;gap:12px;margin-bottom:8px}.moment .time{font:700 14px Cinzel,serif;color:var(--soft);flex:none;min-width:80px}.moment p{margin:0}
        .airdate{margin-bottom:6px}.airdate span{color:var(--soft)}.airdate i{color:var(--faded)}
        .watch{font:700 12px Cinzel,serif;border:1px solid var(--rubric);border-radius:4px;padding:1px 8px;text-decoration:none;margin-left:6px}
        .watch:hover{background:var(--rubric);color:var(--parch)}
        .event{display:flex;gap:14px;align-items:center;text-decoration:none;color:var(--ink);padding:8px 10px;border-radius:6px;margin-bottom:6px;background:var(--card);border-left:4px solid var(--gold);transition:background .15s,transform .15s}
        .event.airing{border-left-color:#4F7F3A}.event.past{opacity:.55}
        .event:hover{background:rgba(201,168,106,.3);transform:translateX(3px)}
        .day{flex:none;width:48px;text-align:center;font:12px Cinzel,serif;color:var(--faded)}.day b{display:block;font-size:22px;color:var(--rubric)}
        .kind{font:700 11px Cinzel,serif;color:var(--faded);text-transform:uppercase;letter-spacing:.1em}
        .what i{color:var(--faded);font-size:16px}
        ul.plain{list-style:none;padding:0}ul.plain li{margin-bottom:4px}

        /* Page turn: the old page folds away toward the spine */
        .turn{position:absolute;inset:0;display:none;pointer-events:none;transform-origin:left center;overflow:hidden;z-index:3;border-radius:0 6px 6px 0;
          background:linear-gradient(to right,rgba(58,36,16,.47),rgba(58,36,16,.16) 19px,transparent 54px),var(--grain),var(--paper)}
        .turn-page{padding:24px 32px 24px 58px}
        .turn-shade{position:absolute;inset:0;opacity:0;background:linear-gradient(to right,transparent,rgba(48,26,8,.6))}

        /* The front cover */
        .cover{position:absolute;inset:0;display:flex;cursor:pointer;z-index:5;outline:none}
        .door{position:relative;height:100%;background:var(--shade),var(--hide) -100px -800px/1500px auto;border:2px solid #1A0408}
        .door.back{width:424px;flex:none;border-radius:16px 0 0 16px;border-right:0;transform-origin:left center}
        .door.front{flex:1;min-width:0;border-radius:0 16px 16px 0;border-left:0;transform-origin:right center;display:flex;flex-direction:column;align-items:center;justify-content:center;text-align:center;padding:20px}
        .door.back::before{content:"";position:absolute;inset:10px 0 10px 10px;border:1.5px solid rgba(201,162,74,.7);border-right:0;border-radius:12px 0 0 12px}
        .door.front::before{content:"";position:absolute;inset:10px 10px 10px 0;border:1.5px solid rgba(201,162,74,.7);border-left:0;border-radius:0 12px 12px 0}
        .door.back::after{content:"";position:absolute;top:0;bottom:0;right:0;width:30px;background:linear-gradient(to right,transparent,rgba(0,0,0,.53))}
        .door.front::after{content:"";position:absolute;top:0;bottom:0;left:0;width:30px;background:linear-gradient(to right,rgba(0,0,0,.53),transparent)}
        .emboss{position:absolute;left:50%;top:50%;width:150px;height:157px;margin:-78px 0 0 -75px;opacity:.3;background:var(--logo) 0 0/1073px auto no-repeat}
        .plate{padding:26px 34px;border-radius:10px;background:rgba(20,4,8,.75);border:1.5px solid var(--gold);box-shadow:0 0 40px rgba(255,42,42,.55);max-width:100%}
        .plate .logo{width:460px;max-width:100%;filter:none}
        .cover-title{font:700 34px "Cinzel Decorative","Palatino Linotype",serif;color:var(--gold-light);margin-top:26px;text-shadow:0 2px 8px #000}
        .cover-hint{font:italic 18px "EB Garamond",Georgia,serif;color:var(--gold-light);margin-top:30px;text-shadow:0 1px 4px #000;animation:beckon 2.8s ease-in-out infinite}
        @keyframes beckon{0%,100%{opacity:.55}50%{opacity:1}}
        .tap{display:none}
        @media (hover:none){.click{display:none}.tap{display:inline}}
        .cover:focus-visible .plate{box-shadow:0 0 0 3px var(--gold-light),0 0 40px rgba(255,42,42,.55)}
        .seam{position:absolute;left:394px;top:50%;width:60px;height:700px;margin-top:-350px;border-radius:50%;opacity:0;pointer-events:none;
          background:radial-gradient(closest-side,#FFF2C4,rgba(255,210,122,.53) 40%,rgba(255,210,122,0))}
        .cover.opening{pointer-events:none}
        .cover.opening .back{animation:swing-back 1.3s cubic-bezier(.65,0,.35,1) forwards}
        .cover.opening .front{animation:swing-front 1.3s cubic-bezier(.65,0,.35,1) forwards}
        .cover.opening .seam{animation:seam 1.3s cubic-bezier(.65,0,.35,1) forwards}
        @keyframes swing-back{to{transform:scaleX(0) skewY(-12deg)}}
        @keyframes swing-front{to{transform:scaleX(0) skewY(12deg)}}
        @keyframes seam{0%{opacity:0;transform:scaleX(.5)}25%{opacity:1}100%{opacity:0;transform:scaleX(14)}}
        .sparkles{position:fixed;inset:0;pointer-events:none;z-index:10}
        .sparkles i{position:absolute;border-radius:50%;box-shadow:0 0 6px rgba(255,230,154,.9)}

        /* Phones and narrow windows: one page at a time, contents in a drawer */
        @media (max-width:800px){
          body{height:auto;overflow:auto;font-size:17px;background:#F7ECD2}
          .candles,.gilt,.ribbon,.leaf.right::after{display:none}
          .bar{display:flex;align-items:center;gap:12px;position:sticky;top:0;z-index:4;padding:8px 16px;font:14px Cinzel,serif;color:var(--gold-light);
            background:var(--shade),var(--hide) 0 0/700px auto;border-bottom:2px solid var(--gold)}
          .bar-title{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
          .menu{font:700 13px Cinzel,serif;background:rgba(20,4,8,.5);color:var(--gold-light);border:1px solid var(--gold);border-radius:4px;padding:5px 10px;flex:none}
          .book{position:static;margin:0;border:0;border-radius:0;box-shadow:none;background:none}
          .book::before{display:none}
          .pages{position:static;display:block}
          .leaf{box-shadow:none!important;border-radius:0!important}
          .leaf.left{display:none;width:auto;padding:18px 16px;border-bottom:2px solid var(--gold);background:var(--grain),var(--paper)}
          .menu-open .leaf.left{display:block}
          .leaf.right{display:block;overflow:visible;background:var(--grain),var(--paper)}
          main{overflow:visible;padding:18px 16px 48px}
          h1{font-size:26px}
          .highlights{grid-template-columns:1fr}.counts b{font-size:30px}
          .split{display:block}.cover-art{width:180px;margin:16px auto 0}
          .card{flex-direction:column;align-items:center;text-align:center}
          .plays{justify-content:center}
          .moment{flex-direction:column;gap:0}
          .cover{position:fixed;z-index:6}
          .door.back{width:22%}
          .seam{left:calc(22% - 30px)}
          .plate{padding:14px 16px}
          .cover-title{font-size:22px;margin-top:18px}
          .emboss{width:60px;height:63px;margin:-31px 0 0 -30px;background-size:429px auto}
        }
        @media (prefers-reduced-motion:reduce){.candle,.cover-hint{animation:none}.card:hover,.event:hover{transform:none}}
        @media print{body{height:auto;overflow:visible;background:#fff}.candles,.bar,.leaf.left,.cover,.ribbon,.gilt,.sparkles{display:none}
          .book,.pages{position:static;border:0;box-shadow:none;background:none}.leaf.right{background:none;overflow:visible}main{overflow:visible}
          .js .page{display:block;page-break-after:always}}
        """;

    private const string Script = """
        (function(){
          var root=document.documentElement;root.classList.add('js');
          var still=window.matchMedia&&matchMedia('(prefers-reduced-motion: reduce)').matches;
          var main=document.querySelector('main'),turn=document.querySelector('.turn'),turnPage=turn.querySelector('.turn-page');
          var cover=document.querySelector('.cover'),menu=document.querySelector('.menu');
          var shown=null,opened=false;
          function narrow(){return root.clientWidth<=800;}

          // "#page" opens a page; "#page/person" also points at that person's or character's card.
          function pointAt(card){
            if(!card)return;
            card.scrollIntoView({block:'center',behavior:still?'auto':'smooth'});
            card.classList.remove('flash');void card.offsetWidth;card.classList.add('flash');
          }

          function showPage(){
            var parts=decodeURIComponent(location.hash.slice(1)).split('/'),id=parts[0]||'front',who=parts[1];
            var page=document.getElementById('s-'+id);
            if(!page){id='front';page=document.getElementById('s-front');}
            var card=who?page.querySelector('[data-m="'+who+'"]'):null;
            if(page===shown){pointAt(card);return;}
            if(shown&&opened&&!still&&!narrow())turnAway(shown);
            document.querySelectorAll('.page').forEach(function(p){p.classList.toggle('current',p===page);});
            document.querySelectorAll('#toc a').forEach(function(a){
              var on=a.getAttribute('href')==='#'+id;
              a.classList.toggle('on',on);
              if(on)for(var d=a.parentElement;d;d=d.parentElement)if(d.tagName==='DETAILS')d.open=true;
            });
            document.body.classList.remove('menu-open');menu.setAttribute('aria-expanded','false');
            var h=page.querySelector('h1');document.title=(h?h.textContent+' · ':'')+'Production Guide';
            main.scrollTop=0;window.scrollTo(0,0);
            shown=page;
            if(card)setTimeout(function(){pointAt(card);},still||narrow()?0:450);
          }

          // The page being left folds away toward the spine while the new one fades up.
          function turnAway(old){
            if(!turn.animate)return;
            var copy=old.cloneNode(true);copy.removeAttribute('id');copy.classList.add('current');
            turnPage.innerHTML='';turnPage.appendChild(copy);
            turnPage.style.transform='translateY('+(-main.scrollTop)+'px)';
            turn.style.display='block';
            var fold=turn.animate([{transform:'none'},{transform:'scaleX(0) skewY(-9deg)'}],{duration:560,easing:'cubic-bezier(.55,.06,.68,.19)'});
            turn.querySelector('.turn-shade').animate([{opacity:0},{opacity:.8}],{duration:560});
            var fade=main.animate([{opacity:.25},{opacity:1}],{duration:1120,easing:'ease-out'});
            // Done (or stalled, say in a background tab): never leave the old page over the new one.
            var done=function(){turn.style.display='none';turnPage.innerHTML='';};
            fold.onfinish=fold.oncancel=done;
            setTimeout(function(){done();fade.finish();},1300);
          }

          // Clicking a season or campaign opens its page; clicking it again folds it shut.
          document.querySelectorAll('#toc summary a').forEach(function(a){
            a.addEventListener('click',function(e){
              e.preventDefault();
              var d=a.parentElement.parentElement,href=a.getAttribute('href');
              if(location.hash===href)d.open=!d.open;else{d.open=true;location.hash=href;}
            });
          });

          menu.addEventListener('click',function(){
            var open=document.body.classList.toggle('menu-open');
            menu.setAttribute('aria-expanded',open?'true':'false');
          });

          // The cover: swings open like two doors, light from the seam, sparkles from the spine.
          function burst(){
            var box=document.querySelector('.sparkles'),r=cover.getBoundingClientRect(),x=cover.querySelector('.back').getBoundingClientRect().right;
            var colors=['#FFE69A','#FFF6D8','#C9A24A','#C8B4FF'];
            for(var i=0;i<70;i++){
              var s=2+Math.random()*5,dot=document.createElement('i');
              dot.style.cssText='left:'+x+'px;top:'+(r.top+r.height/2+(Math.random()-.5)*r.height*.8)+'px;width:'+s+'px;height:'+s+'px;background:'+colors[i%4];
              box.appendChild(dot);
              var a=Math.random()*Math.PI*2,dist=120+Math.random()*520;
              var anim=dot.animate([{transform:'translate(0,0)',opacity:1},{opacity:1,offset:.1},
                {transform:'translate('+Math.cos(a)*dist+'px,'+(Math.sin(a)*dist*.6-60)+'px)',opacity:0}],
                {duration:900+Math.random()*1100,easing:'cubic-bezier(.25,.46,.45,.94)'});
              anim.onfinish=function(){this.effect.target.remove();};
            }
          }
          function openCover(){
            if(opened)return;opened=true;
            try{sessionStorage.setItem('dmap-guide-open','1');}catch(e){}
            if(still||!cover.animate){cover.remove();return;}
            cover.classList.add('opening');burst();
            setTimeout(function(){cover.remove();},1350);
          }
          cover.addEventListener('click',openCover);
          cover.addEventListener('keydown',function(e){if(e.key==='Enter'||e.key===' '){e.preventDefault();openCover();}});
          var seen=false;try{seen=sessionStorage.getItem('dmap-guide-open')==='1';}catch(e){}
          if(seen){opened=true;cover.remove();}

          window.addEventListener('hashchange',showPage);
          showPage();
        })();
        """;
}
