using System;
using System.IO;
using System.Linq;
using KerbalNews;
using KerbalNews.Web;

public static class TabloidTests
{
    static void Assert(bool c, string m) { if (!c) throw new Exception("Assertion failed: " + m); }

    static Sites DemoSites(out NewsState s)
    {
        double now;
        s = DemoNews.Build(new SimpleClock(21600), out now);
        var eng = new NewsEngine(s, new GlobalSettings { OfflineMode = true }, null, new SimpleClock(21600));
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "GameData/KerbalNews/Sites");
        var sites = new Sites(eng, rel => { var p = Path.Combine(dir, rel); return File.Exists(p) ? File.ReadAllText(p) : null; });
        double t = now + 10;
        sites.Now = () => t;
        return sites;
    }

    public static void TestTabloidCoversAndGetsCited()
    {
        NewsState s;
        var sites = DemoSites(out s);
        var rumor = s.Articles.FirstOrDefault(a => a.Outlet == "kraken" && s.Forum.Any(t => t.Body.Contains("[[" + a.Id + "]]")));
        Assert(rumor != null, "the Daily Kraken wrote something in the demo");
        var home = sites.Render("dailykraken.kb");
        Assert(home.Title.Contains("Daily Kraken") && home.Html.Contains(Sites.ArticleUrl(rumor)), "tabloid front page lists its rumor");
        var thread = s.Forum.First(t => t.Body.Contains("[[" + rumor.Id + "]]"));
        var page = sites.Render(Sites.ForumHost + "/t/" + thread.Id).Html;
        Assert(page.Contains("href=\"" + Sites.ArticleUrl(rumor) + "\"") && page.Contains("class=\"cite\""), "Snackboard thread links the cited article");
        Assert(!page.Contains("[["), "no raw citation markers");
        var herald = s.Articles.First(a => a.Comments.Any(c => c.Text.Contains("[[" + rumor.Id + "]]")));
        Assert(sites.Render(Sites.ArticleUrl(herald)).Html.Contains("href=\"" + Sites.ArticleUrl(rumor) + "\""), "comment links the cited article");
    }

    public static void TestCitationsAreSafe()
    {
        NewsState s;
        var sites = DemoSites(out s);
        string html = sites.Linked("<b>hi</b> see [[a99999]] and [[t1]]");
        Assert(html.StartsWith("&lt;b&gt;"), "text is escaped");
        Assert(!html.Contains("a99999") && !html.Contains("[["), "unknown citations are dropped");
    }

    public static void TestPromptOffersCitationsAndLeaks()
    {
        double now;
        var s = DemoNews.Build(new SimpleClock(21600), out now);
        var story = s.Stories.First(x => x.Kind == "mission");
        var plan = CoveragePlan.For(story);
        string p = PromptBuilder.User(story, plan, s, null, new SimpleClock(21600), now);
        Assert(p.Contains("ids in brackets") && p.Contains("[" + s.Articles.Where(a => a.PublishedUT <= now).OrderByDescending(a => a.PublishedUT).First().Id + "]"), "recent headlines carry ids and the citation rule");

        int leaks = 0;
        for (int i = 0; i < 30; i++)
        {
            var st = new Story { Id = "s" + i, Silent = true, Craft = new CraftSnapshot { Parts = 30, MassTons = 12, Crew = { "Bill Kerman" } } };
            st.Events.Add(new NewsEvent { Type = EventTypes.Soi, Body = "Duna" });
            var leak = PromptBuilder.LeakFact(st);
            if (leak != null) { leaks++; Assert(leak.Contains("Bill Kerman") || leak.Contains("Duna"), "a leak is a true detail"); }
        }
        Assert(leaks > 3 && leaks < 20, "about a third of silent stories leak: " + leaks);
        Assert(PromptBuilder.LeakFact(new Story { Id = "s0", Craft = new CraftSnapshot() }) == null, "only silent stories leak");
    }
}
