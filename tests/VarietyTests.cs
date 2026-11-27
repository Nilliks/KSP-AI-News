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

    public static void TestOfflineAssistantUnderstandsShortCommands()
    {
        var s = new NewsState();
        s.Profile.Name = "Kerbin Space Agency";
        var g = new GlobalSettings { OfflineMode = true, AssistantConfirm = false };
        var eng = new NewsEngine(s, g, null, new SimpleClock(Day));
        double funds = 50000;
        eng.FundsAvailable = () => funds;
        eng.ChangeFunds = (d, why) => funds += d;
        eng.Tick(0, 0, true);
        eng.AssistantSend("help", 10);
        Assert(s.AssistantChat.Last().Text.Contains("release:"), "help lists the commands");
        eng.AssistantSend("finances", 20);
        Assert(s.AssistantChat.Last().Role == "assistant" && s.AssistantChat.Last().Text.Length > 10, "a lookup answers: " + s.AssistantChat.Last().Text);
        eng.AssistantSend("release: New engine test | We fired the new engine for 40 seconds today.", 30);
        Assert(s.Releases.Any(r => r.Title == "New engine test"), "release published");
        eng.AssistantSend("donate 2k to the Mun Valley school publicly", 40);
        Assert(funds == 48000, "donation paid: " + funds);
        eng.AssistantSend("post: Clear skies over the pad today.", 50);
        Assert(s.Social.Any(p => p.Text.Contains("Clear skies")), "kerbnet post");
        eng.AssistantSend("make it rain", 60);
        Assert(s.AssistantChat.Last().Text.Contains("didn't catch"), "unknown asks get the help");
        g.AssistantConfirm = true;
        eng.AssistantSend("donate 1000 to the museum", 70);
        Assert(funds == 48000 && s.AssistantChat.Any(m => m.Role == "proposal" && m.State == "pending"), "spending still waits for approval when that's on");
    }

    public static void TestFewerCallsOnQuietStretches()
    {
        // Livestream: a long coast with nothing happening sends only every third batch to the AI.
        var t = new CoreTests.FakeTransport { Responder = r => CoreTests.AnthropicReply(r) };
        var g = CoreTests.OnlineSettings();
        var eng = new NewsEngine(new NewsState(), g, t, new SimpleClock(Day));
        eng.Tick(0, 0, true);
        eng.StreamInterval = 30;
        eng.StartStream("v1", "Kerbal X", 0, 0);
        eng.StreamSample(new TelemetrySample { UT = 1, Altitude = 80000 }, 1);
        double now = 5;
        for (int i = 0; i < 9; i++) { eng.TickStream(now, true); now += 31; }
        Assert(t.Sent.Count <= 4, "quiet coast: " + t.Sent.Count + " calls for 9 batches");

        // Kerbopedia: a reply that isn't usable is not retried every few hours.
        var s = new NewsState();
        var bad = new CoreTests.FakeTransport { Responder = r => r.Body.Contains("Kerbopedia") ? Text("no json") : CoreTests.AnthropicReply(r) };
        var e2 = new NewsEngine(s, CoreTests.OnlineSettings(), bad, new SimpleClock(Day));
        e2.SyncCrew(new CrewProfile { Name = "Jebediah Kerman", Trait = "Pilot", Level = 2 });
        e2.Tick(0, 0, true);
        for (int i = 0; i < 6; i++)
        {
            double l = 10 + i * Day;
            e2.TrackLaunch("v" + i, CoreTests.Craft(), l);
            e2.Ingest(CoreTests.Ev(EventTypes.Launch, l, "v" + i, "Kerbin", "Jebediah Kerman"));
            e2.Ingest(CoreTests.Ev(EventTypes.Orbit, l + 400, "v" + i, "Kerbin", "Jebediah Kerman"));
        }
        for (double u = 0; u < Day * 20; u += Day * 0.1) e2.Tick(u, u, true);
        int wiki = bad.Sent.Count(r => r.Body.Contains("Kerbopedia"));
        Assert(wiki >= 1 && wiki <= 6, "Kerbopedia backs off after bad replies: " + wiki + " calls in 20 days");

        // News: a model that keeps answering in the wrong shape stops getting retries.
        var junk = new CoreTests.FakeTransport { Responder = r => Text("I'd love to help!") };
        var s3 = new NewsState();
        var e3 = new NewsEngine(s3, CoreTests.OnlineSettings(), junk, new SimpleClock(Day));
        e3.Tick(0, 0, true);
        for (int i = 0; i < 5; i++)
        {
            double l = 10 + i * Day * 2;
            e3.TrackLaunch("v" + i, CoreTests.Craft(), l);
            e3.Ingest(CoreTests.Ev(EventTypes.Launch, l, "v" + i, "Kerbin", "Jebediah Kerman"));
            e3.Ingest(CoreTests.Ev(EventTypes.Orbit, l + 400, "v" + i, "Kerbin", "Jebediah Kerman"));
        }
        double rt = 0;
        for (double u = 0; u < Day * 14; u += Day * 0.05) { rt += 120; e3.Tick(u, rt, true); }
        int news = junk.Sent.Count(r => r.Body.Contains("newsroom"));
        Assert(s3.Articles.Count > 0 && s3.Articles.All(a => a.Offline), "templates step in");
        Assert(news <= 5 * 2, "news calls: " + news + " for 5 stories");
    }

    public static void TestStoryMailRidesInTheNewsCall()
    {
        var s = new NewsState();
        s.Profile.Name = "Kerbin Space Agency";
        var t = new CoreTests.FakeTransport { Responder = r =>
        {
            var rep = CoreTests.AnthropicReply(r);
            if (!r.Body.Contains("KMAIL")) return rep;
            var outer = Json.Parse(rep.Body);
            string txt = Json.Str(Json.Arr(Json.Get(outer, "content"))[0], "text").Replace("```json", "").Replace("```", "").Trim();
            txt = txt.Substring(0, txt.Length - 1) + ",\"emails\":[{\"n\":1,\"subject\":\"Congratulations from us\",\"body\":\"Dear Director, well done. Lisa Kerman\"},{\"n\":2,\"subject\":\"Second\",\"body\":\"Hello there. Bob\"},{\"n\":3,\"subject\":\"Third\",\"body\":\"Hi. Val\"},{\"n\":4,\"subject\":\"Fourth\",\"body\":\"Hey. Bill\"}]}";
            return Text(txt);
        } };
        var g = CoreTests.OnlineSettings();
        g.MailLevel = 3;
        var eng = new NewsEngine(s, g, t, new SimpleClock(Day));
        eng.SyncCrew(new CrewProfile { Name = "Jebediah Kerman", Trait = "Pilot", Level = 2 });
        eng.Tick(0, 0, true);
        eng.TrackLaunch("v1", CoreTests.Craft(), 10);
        eng.Ingest(CoreTests.Ev(EventTypes.Landing, 10, "v1", "Mun", "Jebediah Kerman"));
        double rt = 0;
        for (double u = 0; u < Day * 4; u += Day * 0.05) { rt += 60; eng.Tick(u, rt, true); }
        Assert(t.Sent.Any(r => r.Body.Contains("KMAIL")), "the news call asks for the mail");
        Assert(!t.Sent.Any(r => r.Body.Contains("You write emails sent to the director")), "no separate mail call");
        Assert(s.Mail.Any(m => m.Subject == "Congratulations from us") && s.Mail.All(m => m.UT > 10), "mail arrives, after the news");
    }

    static HttpResult Text(string text)
    {
        var resp = new Dictionary<string, object> { { "content", new List<object> { new Dictionary<string, object> { { "type", "text" }, { "text", text } } } } };
        return new HttpResult { Status = 200, Body = Json.Write(resp) };
    }

    public static void TestSettingsPageAfterTheReorganisation()
    {
        var g = new GlobalSettings { ProviderId = "deepseek", OfflineMode = false };
        g.Keys["deepseek"] = "k";
        var s = new NewsState();
        var eng = new NewsEngine(s, g, null, new SimpleClock(Day));
        var dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "GameData/KerbalNews/Sites");
        var sites = new Sites(eng, rel => { var p = System.IO.Path.Combine(dir, rel); return System.IO.File.Exists(p) ? System.IO.File.ReadAllText(p) : null; });
        sites.Now = () => 0;
        Func<string> page = () => sites.Render("settings.kb").Html;
        string h = page();
        Assert(!h.Contains("{{") && !h.Contains("Missing template"), "renders cleanly");
        foreach (var part in new[] { "AI services", "What uses what", "How much AI", "Pictures", "Features", "Websites", "Notifications and display", "Advanced" }) Assert(h.Contains(part), "section " + part);
        Assert(!h.Contains("API usage") && !h.Contains("usage/saver"), "the old preset buttons are gone");
        Assert(h.Split(new[] { "toggle-ktv" }, StringSplitOptions.None).Length == 2, "KTV switch appears once");
        Assert(h.Contains("can't see pictures. Pick a service below that can"), "DeepSeek can't see: the photos job says so");
        Assert(h.Contains("svc-remove/deepseek") && !h.Contains("svc-remove/gemini"), "only a saved key can be removed");
        sites.Handle("job-open/look", new Dictionary<string, string>());
        h = page();
        Assert(h.Contains("Google (Gemini) (needs a key)") && !h.Contains("action:job-look/gemini"), "a helper without a key can't be picked");

        g.KtvOn = true;
        sites.Handle("ai-amount/0", new Dictionary<string, string>());
        Assert(g.AiAmount == 0 && g.MaxOutlets == 3 && g.QuietDays == 0 && g.KtvOn, "the slider sets the spend knobs, not the features");
        sites.Handle("ai-amount/4", new Dictionary<string, string>());
        Assert(g.WriterFrom == "brief" && g.WorldNews == 3, "Everything = full knobs");

        sites.Handle("job-look/gemini", new Dictionary<string, string>());
        Assert(g.VisionProviderId == "" && !eng.CanLook, "picking a helper without a key does nothing");
        sites.Handle("svc-save/gemini", new Dictionary<string, string> { { "key-gemini", "AIzaSyTestKey0000gk" } });
        Assert(page().Contains("action:job-look/gemini"), "with a key the helper can be picked");
        sites.Handle("job-look/gemini", new Dictionary<string, string>());
        sites.Handle("save-settings", new Dictionary<string, string> { { "timeout", "200" } });
        Assert(g.KeyOf("gemini") == "AIzaSyTestKey0000gk" && g.ProviderId == "deepseek" && g.TimeoutSeconds == 200 && eng.CanLook, "helper key saved and usable");
        Assert(page().Contains("Key ends in ...00gk") && !page().Contains("AIzaSyTestKey"), "the page shows only the key's last four characters");
        sites.Handle("svc-remove/gemini", new Dictionary<string, string>());
        Assert(!g.HasKey("gemini") && !eng.CanLook, "helper key forgotten");

        sites.Handle("toggle-mailpopups", new Dictionary<string, string>());
        sites.Handle("news-delay/0.35", new Dictionary<string, string>());
        sites.Handle("toggle-mail-auto", new Dictionary<string, string>());
        var n = new DataNode("S"); var sec = new DataNode("X"); g.Save(n, sec);
        var back = GlobalSettings.Load(n, sec);
        Assert(!back.MailPopups && Math.Abs(back.NewsDelay - 0.35) < 0.001 && s.MailAuto, "new switches saved");

        // Fast news really comes out sooner.
        var t = new CoreTests.FakeTransport { Responder = CoreTests.AnthropicReply };
        var on = CoreTests.OnlineSettings();
        on.NewsDelay = 0.35;
        var e2 = new NewsEngine(new NewsState(), on, t, new SimpleClock(Day));
        e2.Tick(0, 0, true);
        e2.TrackLaunch("v1", CoreTests.Craft(), 10);
        e2.Ingest(CoreTests.Ev(EventTypes.Launch, 10, "v1", "Kerbin", "Jebediah Kerman"));
        e2.Ingest(CoreTests.Ev(EventTypes.Orbit, 400, "v1", "Kerbin", "Jebediah Kerman"));
        e2.Tick(Day * 0.5, 1, true);
        Assert(t.Sent.Count == 1 && e2.State.Articles.All(a => a.PublishedUT < Day * 0.6), "fast: out within half a day");

        // Local providers show their own knobs.
        g.ProviderId = "ollama";
        Assert(page().Contains("Local model context size"), "local knobs for local models");
    }
}
