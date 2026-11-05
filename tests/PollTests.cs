using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KerbalNews;
using KerbalNews.Web;

public static class PollTests
{
    static void Assert(bool c, string m) { if (!c) throw new Exception("Assertion failed: " + m); }

    public static void TestPollsAndTownHalls()
    {
        double day = 21600;
        double now;
        var s = DemoNews.Build(new SimpleClock(day), out now);
        var eng = new NewsEngine(s, new GlobalSettings { OfflineMode = true }, null, new SimpleClock(day));
        eng.Tick(now, 0, true);
        Assert(s.Polls.Count >= 1, "a poll is taken once there is news");
        var p = s.Polls.Last();
        Assert(p.Approve > 0 && p.Approve < 100 && Math.Abs(p.Trust - s.Mood.Trust) < 4, "poll numbers follow the mood");
        Assert(s.Social.Any(x => x.Handle == "@KerbinPolls"), "the poll is posted on Kerbnet");
        int polls = s.Polls.Count;
        eng.Tick(now + day * 0.5, 1, true);
        Assert(s.Polls.Count == polls, "no poll every tick");

        string dir = Path.Combine(Directory.GetCurrentDirectory(), "GameData/KerbalNews/Sites");
        var sites = new Sites(eng, rel => { var f = Path.Combine(dir, rel); return File.Exists(f) ? File.ReadAllText(f) : null; });
        double t = now + day * 0.5;
        sites.Now = () => t;
        var page = sites.Render("kerbalnews.kb/polls");
        Assert(page.Html.Contains(p.Approve + "%") && !page.Html.Contains("{{"), "polls page shows the numbers");
        Assert(sites.Render("kerbalnews.kb").Html.Contains("Latest poll"), "portal links the poll");

        var story = s.Stories.First(x => x.Kind == "mission");
        Assert(PromptBuilder.User(story, CoveragePlan.For(story), s, null, new SimpleClock(day), t).Contains("LATEST POLL"), "writers see the poll");

        // Town hall after the Big Bertha loss.
        sites.Handle("action:post-thread/townhall", new Dictionary<string, string> { { "title", "" }, { "questions", "4" } });
        var th = s.Forum.Last();
        Assert(th.Kind == "townhall" && th.Title.Contains("Town hall") && s.CommunityJobs.Any(j => j.Mode == "townhall"), "town hall opened");
        eng.Tick(t + 1, 2, true);
        var qs = th.Comments.Where(c => !c.Player && c.ReplyTo == "").ToList();
        Assert(qs.Count == 4, "four questions asked: " + qs.Count);
        double trust = s.Mood.Trust;
        eng.PlayerComment("t:" + th.Id, "We found a weld flaw and changed suppliers.", true, "", qs[0].Id, t + 2);
        Assert(s.Mood.Trust > trust, "answering wins trust back");
        double after = s.Mood.Trust;
        eng.PlayerComment("t:" + th.Id, "Again, the weld.", true, "", qs[0].Id, t + 3);
        Assert(s.Mood.Trust == after, "answering the same question twice doesn't count");
        var html = sites.Render(Sites.ForumHost + "/t/" + th.Id).Html;
        Assert(html.Contains("TOWN HALL") && html.Contains("3 unanswered"), "thread page shows the town hall state");
        double before = s.Mood.Trust;
        eng.Tick(t + 3 * day, 3, true);
        Assert(th.Closed && s.Mood.Trust < before + 0.01, "ignoring most questions costs trust after two days");

        var node = new DataNode("NEWS");
        s.Save(node);
        var back = NewsState.Load(node);
        Assert(back.Polls.Count == s.Polls.Count && back.Forum.Any(x => x.Kind == "townhall" && x.Closed), "polls and town hall saved");
    }
}
