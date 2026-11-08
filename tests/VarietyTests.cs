using System;
using System.Collections.Generic;
using System.Linq;
using KerbalNews;
using KerbalNews.Web;

/// <summary>How much the AI writes (the five-stop slider), and how varied the offline templates are.</summary>
public static class VarietyTests
{
    const double Day = 21600;
    static void Assert(bool c, string m) { if (!c) throw new Exception("Assertion failed: " + m); }

    static int CallsFor(int stop, Action<NewsEngine> fly, out NewsState s)
    {
        s = new NewsState();
        var t = new CoreTests.FakeTransport { Responder = CoreTests.AnthropicReply };
        var g = CoreTests.OnlineSettings();
        g.AiAmount = stop;
        var eng = new NewsEngine(s, g, t, new SimpleClock(Day));
        eng.Tick(0, 0, true);
        fly(eng);
        return t.Sent.Count;
    }

    static void LaunchToOrbit(NewsEngine eng)
    {
        eng.TrackLaunch("v1", CoreTests.Craft(), 10);
        eng.Ingest(CoreTests.Ev(EventTypes.Launch, 10, "v1", "Kerbin", "Jebediah Kerman"));
        eng.Ingest(CoreTests.Ev(EventTypes.Orbit, 400, "v1", "Kerbin", "Jebediah Kerman"));
        eng.Tick(10 + Day * 3, 1, true);
    }

    /// <summary>Many plain uncrewed launches: after the first few they are too routine for a story at the usual stops.</summary>
    static void ManyLaunches(NewsEngine eng)
    {
        for (int i = 0; i < 12; i++)
        {
            double ut = 10 + i * Day * 2;
            eng.TrackLaunch("p" + i, CoreTests.Craft(), ut);
            eng.Ingest(CoreTests.Ev(EventTypes.Launch, ut, "p" + i, "Kerbin"));
            eng.Tick(ut + Day * 1.5, 1 + i, true);
        }
        eng.Tick(Day * 30, 50, true);
    }

    public static void TestAiAmountSliderGatesTheWriting()
    {
        NewsState s;
        Assert(CallsFor(0, LaunchToOrbit, out s) == 0 && s.Articles.Count > 0 && s.Articles.All(a => a.Offline), "Nothing: templates write the story");
        int normal = CallsFor(3, LaunchToOrbit, out s);
        Assert(normal >= 1 && s.Articles.Any(a => !a.Offline), "Almost everything (default): the AI writes it");
        NewsState most, all;
        int atAlmost = CallsFor(3, ManyLaunches, out most);
        int atAll = CallsFor(4, ManyLaunches, out all);
        Assert(atAll > atAlmost, "Everything: routine launches get AI briefs too (" + atAlmost + " vs " + atAll + ")");
        NewsState big;
        int atBig = CallsFor(1, ManyLaunches, out big);
        Assert(atBig < atAlmost && big.Articles.Count > 0, "Big news only: routine stories fall back to templates (" + atBig + " vs " + atAlmost + ")");
    }

    public static void TestAiAmountSettingSavesAndHasAButton()
    {
        var g = new GlobalSettings();
        Assert(g.AiAmount == 3, "default is Almost everything");
        g.AiAmount = 4;
        var n = new DataNode("S"); var sec = new DataNode("X"); g.Save(n, sec); var back = GlobalSettings.Load(n, sec);
        Assert(back.AiAmount == 4, "saved: " + back.AiAmount);
        Assert(!g.AiWrites(GlobalSettings.AiBig) || g.CanCallAi, "offline never writes with the AI");
        var on = CoreTests.OnlineSettings();
        on.AiAmount = 1;
        Assert(on.AiWritesTier("major") && !on.AiWritesTier("normal") && !on.AiWrites(GlobalSettings.AiMost), "Big news only");
        on.AiAmount = 2;
        Assert(on.AiWritesTier("normal") && !on.AiWritesTier("brief") && on.AiWrites(GlobalSettings.AiMost) && !on.AiWrites(GlobalSettings.AiAlmostAll), "Most");
    }


    public static void TestTemplatesRarelyRepeat()
    {
        foreach (var rule in new[] { "hl_good", "open_good", "open_fail", "quote_good", "c_good", "s_good", "c_release", "c_tip" })
            Assert(Phrases.Variety(rule) >= 20, rule + " has only " + Phrases.Variety(rule) + " versions");
        var v = new Dictionary<string, string> { { "agency", "KSC" }, { "vessel", "Rover 3" }, { "what", "Rover 3 lands on Duna" }, { "what_l", "Rover 3 lands on Duna" }, { "what_up", "ROVER 3 LANDS ON DUNA" }, { "body", "Duna" }, { "date", "Year 1, Day 9" }, { "mood", "" } };
        var rng = new Random(7);
        var heads = Enumerable.Range(0, 40).Select(i => Phrases.Rule("open_good", v, rng)).ToList();
        Assert(heads.Distinct().Count() >= 25, "40 openings, " + heads.Distinct().Count() + " different");
        foreach (var h in heads) Assert(!h.Contains("{") && !h.Contains("[") && !h.Contains("  ") && !h.Contains(".."), "clean: " + h);
        // Pieces that can land mid-sentence are capitalised where a sentence starts.
        var q = Enumerable.Range(0, 30).Select(i => Phrases.Cap(Phrases.Rule("quote_good", v, rng))).ToList();
        Assert(q.All(x => char.IsUpper(x[0]) || x[0] == '"'), "quotes start a sentence properly");
        var c = Enumerable.Range(0, 60).Select(i => Phrases.Rule("c_good", null, rng)).ToList();
        Assert(!c.Any(x => System.Text.RegularExpressions.Regex.IsMatch(x, ", [A-Z][a-z]")), "no 'Honestly, Best' comments");
    }

    public static void TestOfflineCoverageVariesAndCommentsDontRepeat()
    {
        var heads = new List<string>();
        for (int run = 0; run < 6; run++)
        {
            var s = new NewsState();
            s.Profile.Name = "Kerbin Space Agency";
            var eng = new NewsEngine(s, new GlobalSettings { OfflineMode = true }, null, new SimpleClock(Day));
            eng.Tick(0, 0, true);
            eng.TrackLaunch("v1", CoreTests.Craft(), 10);
            eng.Ingest(CoreTests.Ev(run % 2 == 1 ? EventTypes.Failure : EventTypes.Landing, 10, "v1", "Mun", "Jebediah Kerman"));
            eng.Tick(Day * 3, 1, true);
            foreach (var a in s.Articles)
            {
                heads.Add(a.Headline);
                var texts = a.Comments.Select(x => x.Text).ToList();
                Assert(texts.Count == texts.Distinct().Count(), "no repeated comment under one article: " + string.Join(" / ", texts.ToArray()));
                if (run % 2 == 1) Assert(!a.Body.Contains("WILD") && !a.Body.Contains("tear"), "no cheering on a failure: " + a.Outlet);
                Assert(!a.Body.Contains(" are said") && !a.Subhead.Contains(" are said"), "one kerbal is not 'are'");
            }
        }
        Assert(heads.Distinct().Count() >= heads.Count * 0.8, heads.Distinct().Count() + " of " + heads.Count + " headlines different");
    }
}
