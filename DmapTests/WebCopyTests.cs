using DmapShared.Export;
using DmapShared.Models;

namespace DmapTests;

/// <summary>The read-only web page made from the guide.</summary>
public class WebCopyTests
{
    private static ShowData Guide()
    {
        var data = ShowData.CreateSeed();
        var wow = data.AllCampaigns.Last();
        var hero = new CastMember { CharacterName = "Thrall <the Bold>", PlayerName = "Sam", ImagePath = "images/thrall.jpg" };
        wow.Characters.Add(hero);
        var first = wow.Episodes[0];
        first.Title = "Into the Barrens";
        first.Synopsis = "Line one\nline two\n\nA new paragraph";
        first.CastIds.Add(hero.Id);
        first.AirDates.Add(new AirDate { Date = new DateTime(2026, 11, 3), Platform = "YouTube", Link = "youtube.com/watch?v=abc" });
        first.AirDates.Add(new AirDate { Date = new DateTime(2026, 11, 4), Link = "javascript:alert(1)" });
        return data;
    }

    [Fact]
    public void Every_season_campaign_and_episode_gets_a_page_and_a_contents_line()
    {
        var data = Guide();
        string html = WebCopy.Build(data, "1.0.4", new DateTime(2026, 10, 27), _ => null);

        foreach (TreeNode node in data.Seasons.Cast<TreeNode>().Concat(data.AllCampaigns).Concat(data.AllEpisodes))
        {
            string id = "p" + node.Id.ToString("N");
            Assert.Contains($"""<section class="page" id="s-{id}">""", html);
            Assert.Contains($"""href="#{id}">""", html);
        }
        Assert.Contains("""id="s-front">""", html);
        Assert.Contains("""id="s-schedule">""", html);
        Assert.Contains("""id="s-cast">""", html);
        Assert.Contains("DMAP Guide 1.0.4", html);
    }

    [Fact]
    public void Text_is_escaped_and_kept_in_paragraphs()
    {
        string html = WebCopy.Build(Guide(), "1.0.4", DateTime.Now, _ => null);

        Assert.Contains("Thrall &lt;the Bold&gt;", html);
        Assert.DoesNotContain("<the Bold>", html);
        Assert.Contains("<p>Line one<br>line two</p><p>A new paragraph</p>", html);
    }

    [Fact]
    public void The_cast_and_the_characters_they_play_link_to_each_other()
    {
        var data = Guide();
        var sam = new CastMember { PlayerName = "sam " }; // names match as the app matches them
        data.Cast.Add(sam);
        var wow = data.AllCampaigns.Last();
        var thrall = wow.Characters.Single();
        string html = WebCopy.Build(data, "1.0.4", DateTime.Now, _ => null);

        string character = $"#p{wow.Id:N}/m{thrall.Id:N}";
        string person = $"#cast/m{sam.Id:N}";
        Assert.Contains($"""<a class="chip" href="{character}">""", html);        // Cast page → the character
        Assert.Contains($"""<a class="who-link" href="{person}">""", html);       // "played by" → the person
        Assert.Contains($"""<a href="{character}">Thrall &lt;the Bold&gt;</a>""", html); // episode → the full character card
        Assert.Contains($"""data-m="m{thrall.Id:N}" """.TrimEnd(), html);
        Assert.Contains($"""data-m="m{sam.Id:N}" """.TrimEnd(), html);
    }

    [Fact]
    public void Only_web_addresses_become_watch_links()
    {
        string html = WebCopy.Build(Guide(), "1.0.4", DateTime.Now, _ => null);

        Assert.Contains("""href="https://youtube.com/watch?v=abc" """.TrimEnd(), html);
        Assert.DoesNotContain("javascript:", html);
    }

    [Fact]
    public void Each_picture_is_made_and_written_once_however_many_pages_show_it()
    {
        var asked = new List<string>();
        string html = WebCopy.Build(Guide(), "1.0.4", DateTime.Now, stored =>
        {
            asked.Add(stored);
            return "data:image/jpeg;base64,AAAA";
        });

        // Thrall is on the campaign page and on episode 301.
        Assert.Equal(new[] { "images/thrall.jpg" }, asked);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "base64,AAAA"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, """class="pic pic0" """.TrimEnd()).Count);
    }
}
