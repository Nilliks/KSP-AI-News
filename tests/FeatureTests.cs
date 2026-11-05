using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KerbalNews;
using KerbalNews.Web;

public static class FeatureTests
{
    static void Assert(bool c, string m) { if (!c) throw new Exception("Assertion failed: " + m); }

    public static void TestModScanListsNewModsAndFeedsTheAi()
    {
        string gd = Path.Combine(Path.GetTempPath(), "knn-gamedata-" + Guid.NewGuid().ToString("N"));
        foreach (var d in new[] { "Squad", "KerbalNews", "000_Toolbar", "ModuleManager", "Kerbalism", "MechJeb2", "SpaceY-Lifters" }) Directory.CreateDirectory(Path.Combine(gd, d));
        File.WriteAllText(Path.Combine(gd, "MechJeb2", "MechJeb2.version"), "{\"NAME\":\"MechJeb 2\",\"VERSION\":{\"MAJOR\":2,\"MINOR\":14,\"PATCH\":3}}");
        try
        {
            var g = new GlobalSettings();
            var first = ModCatalog.Refresh(g, gd, "2026-11-05");
            Assert(first.Count == 5 && g.Mods.Count == 5, "five mods, game and KNN skipped: " + g.Mods.Count);
            Assert(!g.Mods.Any(m => m.IsNew), "the first scan marks nothing new");
            var mj = g.Mods.First(m => m.Folder == "MechJeb2");
            Assert(mj.Name == "MechJeb 2" && mj.Version == "2.14.3", "name and version from the .version file: " + mj.Name + " " + mj.Version);
            Assert(g.Mods.First(m => m.Folder == "000_Toolbar").Library && !g.Mods.First(m => m.Folder == "000_Toolbar").Include, "libraries start hidden");
            Assert(g.Mods.First(m => m.Folder == "SpaceY-Lifters").Name == "SpaceY Lifters", "pretty name");
            Assert(g.ModsText.Contains("Kerbalism") && !g.ModsText.Contains("Toolbar"), "summary: " + g.ModsText);

            g.Mods.First(m => m.Folder == "Kerbalism").Note = "life support: kerbals need food and oxygen";
            Directory.CreateDirectory(Path.Combine(gd, "Trajectories"));
            Directory.Delete(Path.Combine(gd, "SpaceY-Lifters"));
            var added = ModCatalog.Refresh(g, gd, "2026-11-06");
            Assert(added.Count == 1 && added[0].IsNew && added[0].Folder == "Trajectories", "a new mod is added and marked new");
            Assert(!g.Mods.First(m => m.Folder == "SpaceY-Lifters").Installed, "a removed mod stays listed, not installed");
            Assert(g.ModsText.StartsWith("Kerbalism (life support"), "notes come first: " + g.ModsText);
            Assert(!g.ModsText.Contains("SpaceY"), "removed mods are not sent");

            var settingsNode = new DataNode("S"); var secrets = new DataNode("K");
            g.Save(settingsNode, secrets);
            var back = GlobalSettings.Load(settingsNode, secrets);
            Assert(back.Mods.Count == 6 && back.ModsScanned && back.Mods.First(m => m.Folder == "Kerbalism").Note.Contains("oxygen"), "mods survive a save");

            var many = new List<ModInfo>();
            for (int i = 0; i < 140; i++) many.Add(new ModInfo { Folder = "Mod" + i, Name = "Some Long Mod Name " + i });
            string sum = ModCatalog.Summary(many);
            Assert(sum.Length < 1500 && sum.Contains("more"), "140 mods stay compact: " + sum.Length);

            var s = new NewsState();
            var eng = new NewsEngine(s, back, new CoreTests.FakeTransport { Responder = CoreTests.AnthropicReply }, new SimpleClock(21600));
            Assert(PromptBuilder.System(s, back.ModsText).Contains("INSTALLED MODS"), "the system prompt lists mods");
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "GameData/KerbalNews/Sites");
            var sites = new Sites(eng, rel => { var f = Path.Combine(dir, rel); return File.Exists(f) ? File.ReadAllText(f) : null; });
            var page = sites.Render("agency.kb/mods").Html;
            Assert(page.Contains("Trajectories") && page.Contains("NEW") && page.Contains("mod-toggle/Kerbalism"), "the mods tab lists them");
            Assert(!sites.Render("agency.kb/mods").Html.Contains(">NEW<"), "NEW shows once");
            sites.Handle("action:mod-toggle/Kerbalism", null);
            Assert(!back.ModsText.Contains("Kerbalism"), "toggling hides a mod");
            int idx = back.Mods.FindIndex(m => m.Folder == "MechJeb2");
            sites.Handle("save-mod-notes", new Dictionary<string, string> { { "note" + idx, "autopilot the press calls 'the robot pilot'" } });
            Assert(back.ModsText.Contains("robot pilot"), "notes save from the page");
            sites.Handle("toggle-mods-context", null);
            Assert(back.ModsText == "", "the whole list can be left out");
        }
        finally { try { Directory.Delete(gd, true); } catch (Exception) { } }
    }

    const double Day = 21600, Year = Day * 426;

    public static void TestCrewFameGrowsAndMakesBiggerNews()
    {
        var s = new NewsState();
        var t = new CoreTests.FakeTransport { Responder = CoreTests.AnthropicReply };
        var eng = new NewsEngine(s, CoreTests.OnlineSettings(), t, new SimpleClock(Day));
        eng.Tick(0, 0, true);
        double rookie = 0;
        for (int i = 0; i < 6; i++)
        {
            string v = "v" + i; double ut = 10 + i * Day * 3;
            eng.Ingest(CoreTests.Ev(EventTypes.Launch, ut, v, "Kerbin", "Jebediah Kerman"));
            var orbit = CoreTests.Ev(EventTypes.Orbit, ut + 300, v, "Kerbin", "Jebediah Kerman");
            eng.Ingest(orbit);
            if (i == 0) rookie = orbit.Significance;
        }
        var jeb = s.Crew["Jebediah Kerman"];
        Assert(jeb.Flights == 6 && jeb.Fame > 15, "six flights build fame: " + jeb.Fame + " " + jeb.Flights);
        Assert(jeb.Highlights.Any(h => h.StartsWith("5th flight")), "a fifth-flight highlight");
        var vetMun = CoreTests.Ev(EventTypes.Landing, 30 * Day, "v9", "Mun", "Jebediah Kerman");
        var rookieMun = CoreTests.Ev(EventTypes.Landing, 30 * Day, "v8", "Mun", "Bob Kerman");
        eng.Ingest(vetMun); eng.Ingest(rookieMun);
        Assert(vetMun.Significance > rookieMun.Significance, "a famous crew makes bigger news: " + vetMun.Significance + " vs " + rookieMun.Significance);
        var story = s.Stories.First(x => x.VesselId == "v9");
        string text = NewsEngine.CrewFameText(story, s, new SimpleClock(Day));
        Assert(text.Contains("Jebediah Kerman (") && text.Contains("6th flight"), "fact sheet line: " + text);
        eng.Ingest(CoreTests.Ev(EventTypes.CrewKilled, 31 * Day, "v9", "Mun", "Jebediah Kerman"));
        Assert(jeb.Dead && jeb.DiedUT == 31 * Day, "the lost are remembered");
        var n = new DataNode("K"); s.Save(n);
        var back = NewsState.Load(n);
        Assert(back.Crew["Jebediah Kerman"].Dead && back.Crew["Jebediah Kerman"].Flights == jeb.Flights && back.Crew["Bob Kerman"].Fame > 0, "fame survives a save");
    }

    public static void TestAnniversariesAndYearInReview()
    {
        var s = new NewsState();
        var t = new CoreTests.FakeTransport { Responder = CoreTests.AnthropicReply };
        var eng = new NewsEngine(s, CoreTests.OnlineSettings(), t, new SimpleClock(Day));
        eng.Tick(0, 0, true);
        eng.TrackLaunch("v1", CoreTests.Craft(), 10);
        eng.Ingest(CoreTests.Ev(EventTypes.Launch, 10, "v1", "Kerbin", "Jebediah Kerman"));
        var first = CoreTests.Ev(EventTypes.Milestone, 400, "v1", "Kerbin", "Jebediah Kerman");
        first.Detail = "First kerbal in orbit";
        eng.Ingest(first);
        for (double ut = Day; ut < Day * 6; ut += Day / 2) eng.Tick(ut, ut, true);
        Assert(s.Stories.Any(x => x.Kind == "mission" && x.Status == StoryStatus.Published), "the first got covered");
        // Fast-forward to just past one year: the year review and the anniversary each queue once.
        double r = 100;
        for (double ut = Year - Day; ut < Year + Day * 4; ut += Day / 2) eng.Tick(ut, r += 1, true);
        var review = s.Stories.FirstOrDefault(x => x.Special == "review");
        var anniv = s.Stories.FirstOrDefault(x => x.Special == "retrospective");
        Assert(review != null && review.Topic.Contains("Year 1"), "year 1 in review was queued");
        Assert(anniv != null && anniv.Topic.Contains("one year since First kerbal in orbit"), "first-orbit anniversary: " + (anniv == null ? "none" : anniv.Topic));
        for (double ut = Year + Day * 4; ut < Year + Day * 12; ut += Day / 2) eng.Tick(ut, r += 1, true);
        Assert(s.Stories.Count(x => x.Special == "retrospective") == 1 && s.Stories.Count(x => x.Special == "review") == 1, "each happens once");
        Assert(t.Sent.Any(x => x.Body.Contains("SPECIAL RETROSPECTIVE")), "the retrospective was written with its own brief");
    }

    public static void TestScienceIsRecordedForTheJournal()
    {
        string e, b, si, bi;
        ScienceResult.ParseSubject("surfaceSample@MunSrfLandedFarsideCrater", new[] { "Kerbin", "Mun", "Minmus" }, out e, out b, out si, out bi);
        Assert(e == "surfaceSample" && b == "Mun" && si == "SrfLanded" && bi == "FarsideCrater", "parsed: " + e + "|" + b + "|" + si + "|" + bi);
        ScienceResult.ParseSubject("crewReport@KerbinFlyingLow", new[] { "Kerbin", "Mun" }, out e, out b, out si, out bi);
        Assert(b == "Kerbin" && si == "FlyingLow" && bi == "", "no biome");
        var s = new NewsState();
        var eng = new NewsEngine(s, CoreTests.OnlineSettings(), new CoreTests.FakeTransport { Responder = CoreTests.AnthropicReply }, new SimpleClock(Day));
        eng.RecordScience(new ScienceResult { UT = 100, SubjectId = "x@MunSrfLanded", Amount = 10, VesselId = "v1" });
        eng.RecordScience(new ScienceResult { UT = 150, SubjectId = "x@MunSrfLanded", Amount = 5, VesselId = "v1" });
        eng.RecordScience(new ScienceResult { UT = 150, SubjectId = "y@MunSrfLanded", Amount = 0, VesselId = "v1" });
        Assert(s.Science.Count == 1 && s.Science[0].Amount == 15, "repeats merge, zero ignored");
        var n = new DataNode("K"); s.Save(n);
        Assert(NewsState.Load(n).Science.Count == 1, "science survives a save");
    }

    static HttpResult Reply(string text)
    {
        var resp = new Dictionary<string, object> { { "content", new List<object> { new Dictionary<string, object> { { "type", "text" }, { "text", text } } } } };
        return new HttpResult { Status = 200, Body = Json.Write(resp) };
    }

    public static void TestTalkToCrewWithMemoryAndPersonality()
    {
        var s = new NewsState();
        string lastSystem = "", lastUser = "";
        int calls = 0, compress = 0;
        var t = new CoreTests.FakeTransport { Responder = r =>
        {
            if (r.Body.Contains("condense a kerbal")) { compress++; return Reply("{\"summary\":\"The director promised me the first Duna seat. I trust them.\"}"); }
            if (!r.Body.Contains("chatting privately")) return CoreTests.AnthropicReply(r);
            calls++;
            lastSystem = r.Body; lastUser = r.Body;
            return Reply("{\"reply\":\"Ha, the Mun was a blast, boss!\",\"remember\":\"The director promised me the first Duna seat, number " + calls + ".\"}");
        } };
        var eng = new NewsEngine(s, CoreTests.OnlineSettings(), t, new SimpleClock(Day));
        eng.Tick(0, 0, true);
        eng.SyncCrew(new CrewProfile { Name = "Jebediah Kerman", Trait = "Pilot", Level = 3, Courage = 0.5, Stupidity = 0.5, Badass = true, Veteran = true });
        eng.SyncCrew(new CrewProfile { Name = "Bob Kerman", Trait = "Scientist", Level = 1, Courage = 0.1, Stupidity = 0.1 });
        eng.Ingest(CoreTests.Ev(EventTypes.Launch, 10, "v1", "Kerbin", "Jebediah Kerman", "Gusemy Kerman"));
        eng.Ingest(CoreTests.Ev(EventTypes.Landing, 5000, "v1", "Mun", "Jebediah Kerman", "Gusemy Kerman"));
        eng.Ingest(CoreTests.Ev(EventTypes.CrewKilled, 9000, "v1", "Mun", "Gusemy Kerman"));
        var jeb = s.Crew["Jebediah Kerman"];
        Assert(jeb.Log.Any(l => l.Contains("landed on Mun")) && jeb.Log.Any(l => l.Contains("lost former crewmate Gusemy Kerman")), "life log: " + string.Join(" | ", jeb.Log.ToArray()));
        Assert(NewsEngine.Personality(s.Crew["Bob Kerman"]).Contains("anxious") && NewsEngine.Personality(s.Crew["Bob Kerman"]).Contains("sharp"), "stats shape personality");

        Assert(eng.CrewSend("Jebediah Kerman", "How was the Mun?", Day), "message sent");
        Assert(jeb.Chat.Count == 2 && jeb.Chat[1].Role == "crew" && jeb.Chat[1].Text.Contains("blast"), "Jeb answered");
        Assert(lastSystem.Contains("badass") && lastUser.Contains("landed on Mun") && lastUser.Contains("How was the Mun?"), "the prompt carries stats, memories and the chat");
        Assert(jeb.Notes.Count == 1 && jeb.Notes[0].Contains("Duna seat"), "a memory was noted");
        eng.CrewSend("Jebediah Kerman", "Remember what I promised?", Day * 2);
        Assert(lastUser.Contains("Things you noted") && lastUser.Contains("Duna seat"), "memories come back next time");

        for (int i = 0; i < 12; i++) jeb.Notes.Add("Y1: a long note about many things that happened and were said, padded out to take up room in memory, then padded some more so that the budget fills " + i);
        eng.CrewSend("Jebediah Kerman", "Anything else?", Day * 3);
        Assert(compress == 1 && jeb.MemorySummary.Contains("Duna") && jeb.Notes.Count <= 5, "old memories were condensed: " + jeb.Notes.Count);
        Assert(!eng.CrewSend("Gusemy Kerman", "Hello?", Day * 3), "the dead do not answer");

        var n = new DataNode("K"); s.Save(n);
        var back = NewsState.Load(n).Crew["Jebediah Kerman"];
        Assert(back.Chat.Count == jeb.Chat.Count && back.MemorySummary == jeb.MemorySummary && back.Badass && back.Trait == "Pilot" && back.Log.Count == jeb.Log.Count, "chat and memories are saved in the save");

        var off = CoreTests.OnlineSettings(); off.OfflineMode = true;
        var eng2 = new NewsEngine(s, off, t, new SimpleClock(Day));
        int before = calls;
        eng2.CrewSend("Bob Kerman", "Hi Bob", Day * 4);
        Assert(calls == before && s.Crew["Bob Kerman"].Chat.Last().Text.Contains("Turn the AI on"), "offline: no call, a polite note");
    }

    public static void TestScheduleCrewInterviewAndCoachThem()
    {
        var s = new NewsState();
        string lastCrewPrompt = "";
        var t = new CoreTests.FakeTransport { Responder = r =>
        {
            if (r.Body.Contains("personal assistant and chief of staff"))
                return Reply("{\"thinking\":\"Book Jeb.\",\"lookups\":[],\"actions\":[{\"cmd\":\"schedule_appearance\",\"args\":{\"kerbal\":\"Jeb\",\"kind\":\"interview\",\"outlet\":\"the Herald\",\"when\":\"in 3 days\",\"topic\":\"the Mun landing\"}}],\"reply\":\"Booked.\"}");
            if (r.Body.Contains("chatting privately"))
            {
                lastCrewPrompt = r.Body;
                return Reply("{\"reply\":\"Got it boss, mum's the word on the fuel leak.\",\"remember\":\"\",\"coaching\":\"Do not mention the fuel leak on the lander.\"}");
            }
            return CoreTests.AnthropicReply(r);
        } };
        var eng = new NewsEngine(s, CoreTests.OnlineSettings(), t, new SimpleClock(Day));
        eng.Tick(0, 0, true);
        eng.SyncCrew(new CrewProfile { Name = "Jebediah Kerman", Trait = "Pilot", Level = 3, Courage = 0.5, Stupidity = 0.5, Badass = true });
        eng.AssistantSend("Set Jeb up with the Herald for an interview in 3 days about the Mun landing", 100);
        var ap = s.Appearances.Single();
        Assert(ap.Kerbal == "Jebediah Kerman" && ap.Outlets == "herald" && Math.Abs(ap.AtUT - (100 + 3 * Day)) < 1, "booked: " + ap.Kerbal + " " + ap.Outlets + " " + ap.AtUT);
        var jeb = s.Crew["Jebediah Kerman"];
        Assert(jeb.Chat.Any(m => m.Role == "note" && m.Text.StartsWith("Scheduled")), "Jeb's chat shows the booking");

        eng.CrewSend("Jebediah Kerman", "Whatever you do, don't mention the fuel leak.", 200);
        Assert(lastCrewPrompt.Contains("COMING UP: an interview with Kerbin Daily Herald") || lastCrewPrompt.Contains("COMING UP: an interview"), "Jeb knows about the interview");
        Assert(ap.Coaching.Count == 1 && ap.Coaching[0].Contains("fuel leak"), "coaching saved");

        for (double ut = Day; ut < 5 * Day; ut += Day / 4) eng.Tick(ut, ut, true);
        Assert(ap.Status == "done" && ap.Outcome != "", "the interview happened: " + ap.Status);
        var story = s.Stories.First(x => x.Kind == "tip" && x.DealId == ap.DealId);
        Assert(story.Topic.Contains("fuel leak") && story.Topic.Contains("badass"), "the writer gets the coaching and personality");
        Assert(story.Topic.Contains(ap.Slipped ? "slips up" : "sticks to the agency's line"), "the dice decided: slipped " + ap.Slipped);
        Assert(jeb.Log.Any(l => l.Contains("interview with")), "it goes in Jeb's life log");
        Assert(s.Articles.Any(a => a.StoryId == story.Id && a.Outlet == "herald"), "the Herald ran it");

        var dim = new CrewFame { Stupidity = 1, Courage = 0.1 };
        var sharp = new CrewFame { Stupidity = 0, Courage = 0.9, Veteran = true, Level = 5 };
        Assert(NewsEngine.SlipChance(dim) > 0.45 && NewsEngine.SlipChance(sharp) < 0.06, "dim kerbals slip, sharp veterans don't: " + NewsEngine.SlipChance(dim) + " " + NewsEngine.SlipChance(sharp));
        Assert(Math.Abs(eng.ParseWhen("tomorrow", 0) - Day) < 1 && Math.Abs(eng.ParseWhen("in 6 hours", 0) - 6 * 3600) < 1 && eng.ParseWhen("day 10", 0) > 8 * Day, "times parse");

        var n = new DataNode("K"); s.Save(n);
        Assert(NewsState.Load(n).Appearances.Single().Coaching.Count == 1, "appearances survive a save");
    }

    public static void TestArchiveKeepsEveryHeadline()
    {
        var s = new NewsState();
        for (int i = 0; i < NewsState.MaxArticles + 50; i++)
            s.Articles.Add(new Article { Id = "a" + i, Outlet = "herald", Headline = "Story number " + i + (i == 3 ? " about Zebulon" : ""), Body = "First paragraph " + i + ".\n\nSecond paragraph that is dropped.", PublishedUT = i * 10, Significance = 10, Comments = { new Comment { Text = "hi" } } });
        var n = new DataNode("K"); s.Save(n);
        var back = NewsState.Load(n);
        Assert(back.Articles.Count == NewsState.MaxArticles && back.Archive.Count == 50, "50 moved to the archive: " + back.Archive.Count);
        var stub = back.Archive.First(a => a.Id == "a3");
        Assert(stub.Archived && stub.Comments.Count == 0 && !stub.Body.Contains("Second"), "stubs are small");
        var eng = new NewsEngine(back, CoreTests.OnlineSettings(), new CoreTests.FakeTransport { Responder = CoreTests.AnthropicReply }, new SimpleClock(Day));
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "GameData/KerbalNews/Sites");
        var sites = new Sites(eng, rel => { var f = Path.Combine(dir, rel); return File.Exists(f) ? File.ReadAllText(f) : null; });
        sites.Now = () => 1e9;
        Assert(sites.Render("kerbalnews.kb/search?q=zebulon").Html.Contains("about Zebulon"), "search finds archived stories");
        Assert(sites.Render("herald.kb/article/a3").Html.Contains("From the archive"), "the archived page opens");
    }

    public static void TestStoryCallAlsoWritesKtvSegment()
    {
        var s = new NewsState();
        string prompt = "";
        var t = new CoreTests.FakeTransport { Responder = r =>
        {
            prompt = r.Body;
            var baseReply = CoreTests.AnthropicReply(r);
            // Add a tv field to the canned coverage.
            object root; Json.TryParse(baseReply.Body, out root);
            var content = (List<object>)Json.Get(root, "content");
            string text = Json.Str(content[0], "text");
            text = text.Substring(0, text.LastIndexOf('}')) + ",\"tv\":{\"headline\":\"Jeb reaches orbit\",\"script\":\"Good evening. Jebediah Kerman is in orbit tonight.\",\"ticker\":\"Kerbal X in orbit with Jeb aboard\"}}";
            return Reply(text);
        } };
        var eng = new NewsEngine(s, CoreTests.OnlineSettings(), t, new SimpleClock(Day));
        eng.Tick(0, 0, true);
        eng.TrackLaunch("v1", CoreTests.Craft(), 10);
        eng.Ingest(CoreTests.Ev(EventTypes.Launch, 10, "v1", "Kerbin", "Jebediah Kerman"));
        eng.Ingest(CoreTests.Ev(EventTypes.Orbit, 400, "v1", "Kerbin", "Jebediah Kerman"));
        eng.Tick(10 + Day + 1, 2, true);
        Assert(prompt.Contains("KTV News television segment"), "the story call asks for a segment");
        Assert(t.Sent.Count == 1, "no extra call");
        var seg = s.Tv.Single();
        Assert(seg.Headline == "Jeb reaches orbit" && seg.Ticker.Contains("Kerbal X") && seg.StoryId == s.Stories[0].Id, "segment saved");
        var n = new DataNode("K"); s.Save(n);
        Assert(NewsState.Load(n).Tv.Count == 1, "segments survive a save");
    }

    public static void TestApiUsageSettings()
    {
        var g = CoreTests.OnlineSettings();
        g.ApplyUsage("saver");
        var s = new NewsState();
        var t = new CoreTests.FakeTransport { Responder = CoreTests.AnthropicReply };
        var eng = new NewsEngine(s, g, t, new SimpleClock(Day));
        eng.Tick(0, 0, true);
        // A big story with many outlets, written on the cheap model under Saver.
        eng.TrackLaunch("v1", CoreTests.Craft(), 10);
        eng.Ingest(CoreTests.Ev(EventTypes.Launch, 10, "v1", "Kerbin", "Jebediah Kerman"));
        eng.Ingest(CoreTests.Ev(EventTypes.Landing, 400, "v1", "Mun", "Jebediah Kerman"));
        eng.Tick(10 + Day * 3, 2, true);
        Assert(t.Sent.Count == 1, "one call");
        var outlets = s.Articles.Select(a => a.Outlet).Distinct().Count();
        Assert(outlets <= 3, "saver caps outlets: " + outlets);
        Assert(!t.Sent[0].Body.Contains("KTV News television segment"), "saver turns KTV off");
        // Quiet days: no filler under Saver.
        for (double ut = Day * 4; ut < Day * 20; ut += Day / 2) eng.Tick(ut, ut, true);
        Assert(!s.Stories.Any(x => x.Kind == "filler"), "no quiet-day features under saver");
        Assert(g.UsageMatch == "saver", "matches saver");

        string dir = Path.Combine(Directory.GetCurrentDirectory(), "GameData/KerbalNews/Sites");
        var sites = new Sites(eng, rel => { var f = Path.Combine(dir, rel); return File.Exists(f) ? File.ReadAllText(f) : null; });
        Assert(sites.Render("settings.kb").Html.Contains("API usage") && sites.Render("settings.kb").Html.Contains("usage-outlets/2"), "settings shows the knobs");
        sites.Handle("action:usage-outlets/5", null);
        Assert(g.MaxOutlets == 5 && g.Usage == "custom", "a knob makes it custom");
        sites.Handle("action:usage/balanced", null);
        Assert(g.MaxOutlets == 0 && g.QuietDays == 3 && g.Usage == "balanced", "balanced restores the defaults");
        g.ApplyUsage("full");
        var n = new DataNode("S"); var k = new DataNode("K"); g.Save(n, k);
        var back = GlobalSettings.Load(n, k);
        Assert(back.UsageMatch == "full" && back.WriterFrom == "brief" && back.StreamSeconds == 20, "usage settings survive a save");
    }

    public static void TestEngineeringTeamUsesLiveCraftData()
    {
        // The math the team quotes: LKO (80 km) to the Mun.
        double mu = 3.5316e12, r1 = 680000, r2 = 12000000, dv1, dv2;
        EngMath.Hohmann(mu, r1, r2, out dv1, out dv2);
        Assert(Math.Abs(dv1 - 860) < 20, "Mun transfer about 860 m/s: " + dv1);
        double phase = EngMath.TransferPhase(r1, r2);
        Assert(Math.Abs(phase - 111) < 2, "Mun phase angle about 111: " + phase);
        double wait = EngMath.TimeToWindow(mu, r1, r2, phase + 10, phase);
        Assert(wait > 0 && wait < 1800, "window soon after passing 10 degrees early: " + wait);
        Assert(EngMath.TimeToWindow(mu, r1, r2, phase - 1, phase) > wait, "just missed means a full lap");

        var s = new NewsState();
        string body = "";
        int calls = 0;
        var t = new CoreTests.FakeTransport { Responder = r =>
        {
            if (!r.Body.Contains("engineering team of")) return CoreTests.AnthropicReply(r);
            calls++; body = r.Body;
            return Reply("{\"replies\":[{\"who\":\"linus\",\"text\":\"You are 260 m/s short for a landing and return.\"},{\"who\":\"Wernher\",\"text\":\"A Terrier upper stage fixes that.\"}],\"remember\":\"Director wants to land on the Mun with Gull II.\"}");
        } };
        var g = CoreTests.OnlineSettings();
        g.Usage = "saver"; g.ApplyUsage("saver");
        var eng = new NewsEngine(s, g, t, new SimpleClock(Day));
        var snap = new EngSnapshot
        {
            Scene = "map", Vessel = "Gull II", Situation = "orbiting Kerbin", Crew = 3, CrewCapacity = 3, Parts = 42, Mass = 18.4,
            Body = "Kerbin", BodyMu = mu, BodyRadius = 600000, BodyGravity = 9.81, BodyAtmosphere = 70000, BodySoi = 84159286,
            HasOrbit = true, ApA = 92100, PeA = 81700, Inclination = 0.4, Eccentricity = 0.007, Period = 1920, Sma = 686900, Altitude = 85000, Speed = 2270,
            Target = "Mun", PhaseAngle = 130, RelInclination = 0.4, TransferMu = mu, TransferR1 = 686900, TransferR2 = 12000000, TransferParent = "Kerbin",
            DeltaVReady = true, TotalDvVac = 2240, TotalDvAsl = 1900, TotalDvActual = 2240, CurrentStage = 2,
        };
        snap.Stages.Add(new EngStage { Stage = 2, DvVac = 1240, TwrVac = 1.4, StartMass = 18.4, EndMass = 9 });
        snap.Stages.Add(new EngStage { Stage = 1, DvVac = 760, TwrVac = 1.9 });
        eng.EngineeringData = () => snap;
        Assert(eng.EngSend("Can we land on the Mun and get home?", Day), "sent");
        Assert(calls == 1, "one call even on the Saver preset");
        Assert(body.Contains("Gull II") && body.Contains("1240 m/s vacuum") && body.Contains("Ideal phase angle") && body.Contains("Mun landing 580"), "prompt carries live data, computed numbers and the dv map");
        Assert(s.EngChat.Count == 3 && s.EngChat[1].Role == "linus" && s.EngChat[2].Role == "wernher", "two specialists answered: " + string.Join(",", s.EngChat.Select(m => m.Role).ToArray()));
        eng.EngSend("And the transfer window?", Day);
        Assert(body.Contains("260 m/s short") && !body.Contains("remember"), "chat history comes back, no memory notes");

        string dir = Path.Combine(Directory.GetCurrentDirectory(), "GameData/KerbalNews/Sites");
        var sites = new Sites(eng, rel => { var f = Path.Combine(dir, rel); return File.Exists(f) ? File.ReadAllText(f) : null; });
        sites.Now = () => Day;
        var page = sites.Render("engineering.kb").Html;
        Assert(page.Contains("Gull II") && page.Contains("Flight · Map view") && page.Contains("Stage 2") && page.Contains("Terrier upper stage") && page.Contains("transfer window to Mun"), "page shows live data, chat and quick asks");
        Assert(sites.Render("kerbalnews.kb").Html.Contains("engineering.kb"), "netbar links the team");
        sites.Handle("action:eng-ask/" + Uri.EscapeDataString("Review my design"), null);
        Assert(body.Contains("Review my design"), "quick ask sends");
        snap.Scene = "vab"; snap.Stages[0].TwrAsl = 1.05;
        page = sites.Render("engineering.kb").Html;
        Assert(page.Contains("Vehicle Assembly Building") && page.Contains("Is my TWR good for launch?") && page.Contains("too low to lift off"), "editor view: launch TWR warning");
        snap.Scene = "map";

        snap.StockSystem = false;
        eng.EngSend("What about here?", Day);
        Assert(!body.Contains("Mun landing 580") && body.Contains("planet pack"), "no stock map with a planet pack");

        var n = new DataNode("K"); s.Save(n);
        var back = NewsState.Load(n);
        Assert(back.EngChat.Count == s.EngChat.Count && back.EngChat[1].Role == "linus", "team chat is saved");

        var off = CoreTests.OnlineSettings(); off.OfflineMode = true;
        var eng2 = new NewsEngine(s, off, t, new SimpleClock(Day));
        eng2.EngineeringData = () => snap;
        int before = calls;
        eng2.EngSend("Window?", Day);
        Assert(calls == before && s.EngChat.Last().Text.Contains("phase angle"), "offline still gives the computed numbers");
        g.EngineeringOn = false;
        Assert(!eng.EngSend("hi", Day), "switch turns the team off");
    }

    public static void TestEngineeringReviewPlanNodeAndInvestigation()
    {
        var s = new NewsState();
        string body = "";
        int inv = 0;
        var t = new CoreTests.FakeTransport { Responder = r =>
        {
            if (r.Body.Contains("failure investigation"))
            {
                inv++; body = r.Body;
                return Reply("{\"cause\":\"The rocket flipped because the fins were too far forward.\",\"findings\":\"Speed rose to 480 m/s at 9 km with a sharp g spike.\",\"fixes\":\"Move fins to the bottom; throttle down in the lower atmosphere.\",\"public_summary\":\"Lark I was lost to an aerodynamic upset. No one was hurt.\"}");
            }
            if (r.Body.Contains("engineering team of")) { body = r.Body; return Reply("{\"replies\":[{\"who\":\"gene\",\"text\":\"Here is the plan.\"}]}"); }
            return CoreTests.AnthropicReply(r);
        } };
        var g = CoreTests.OnlineSettings();
        g.Mods.Add(new ModInfo { Folder = "MechJeb2", Name = "MechJeb 2" });
        g.Mods.Add(new ModInfo { Folder = "UmbraSpaceIndustries", Name = "USI Life Support" });
        g.Mods.Add(new ModInfo { Folder = "DeadlyReentry", Name = "Deadly Reentry" });
        var eng = new NewsEngine(s, g, t, new SimpleClock(Day));
        eng.Tick(0, 0, true);

        // Design review and mission plan in the VAB.
        var snap = new EngSnapshot { Scene = "vab", Vessel = "Minmus Hopper", Situation = "being built in the VAB", Parts = 30, Mass = 22, CrewCapacity = 1, DeltaVReady = true, TotalDvVac = 5400 };
        snap.Modules["ModuleCommand"] = 1;
        snap.ResourceMax["ElectricCharge"] = 150;
        snap.Stages.Add(new EngStage { Stage = 3, DvVac = 2200, DvAsl = 1900, TwrAsl = 1.4, TwrVac = 1.7 });
        snap.Stages.Add(new EngStage { Stage = 2, DvVac = 1800, TwrVac = 1.1 });
        snap.Stages.Add(new EngStage { Stage = 1, DvVac = 1400, TwrVac = 0.25, BurnTime = 400 });
        eng.EngineeringData = () => snap;
        Assert(eng.EngPlanMission("land on Minmus and come back", Day), "plan sent");
        var plan = eng.EngCurrentPlan(snap);
        Assert(plan.Known && plan.Land && plan.Return && plan.Legs.Count == 6 && Math.Abs(plan.Total - 5010) < 1, "legs: " + string.Join(" | ", plan.Legs.Select(l => l.Name + " " + l.Dv).ToArray()));
        Assert(plan.Legs[0].Stages.StartsWith("stage 3") && plan.Legs.Last().Stages.Contains("1"), "stages pay for legs in order: " + string.Join(" | ", plan.Legs.Select(l => l.Stages).ToArray()));
        Assert(body.Contains("Mission plan for") && body.Contains("DESIGN REVIEW"), "the team gets the plan and the review");
        var review = eng.EngReview(snap, plan);
        Func<string, bool> has = k => review.Any(x => x.Contains(k));
        Assert(has("No parachutes") && has("No antenna") && has("No solar panels") && has("upper-stage TWR") && has("Supplies") && has("heat shield") && has("landing legs"), "review: " + string.Join(" | ", review.ToArray()));
        Assert(body.Contains("MechJeb is installed") && body.Contains("USI Life Support"), "mod-aware advice");
        snap.Modules["ModuleParachute"] = 2; snap.Modules["ModuleDataTransmitter"] = 1; snap.Modules["ModuleDeployableSolarPanel"] = 2; snap.Modules["ModuleAblator"] = 1; snap.ResourceMax["Supplies"] = 300;
        review = eng.EngReview(snap, plan);
        Assert(!review.Any(x => x.Contains("parachutes") || x.Contains("antenna") || x.Contains("Supplies")), "fixed parts clear the warnings");

        // A maneuver node that cannot be flown.
        snap.Scene = "map"; snap.Body = "Kerbin"; snap.BodyRadius = 600000; snap.BodyAtmosphere = 70000; snap.HasOrbit = true; snap.TotalDvActual = 800;
        snap.Stages.Clear(); snap.Stages.Add(new EngStage { Stage = 1, DvVac = 800, DvActual = 800, ThrustVac = 60, StartMass = 5, IspVac = 345 });
        snap.Node = new EngNode { Dv = 900, Prograde = 900, TimeTo = 30, AfterBody = "Kerbin", EncounterBody = "Mun", EncounterPeA = -20000 };
        var nc = NewsEngine.EngNodeCheck(snap);
        Assert(nc.Any(x => x.StartsWith("!") && x.Contains("only has 800")) && nc.Any(x => x.Contains("impact course")) && nc.Any(x => x.Contains("too close")), "node check: " + string.Join(" | ", nc.ToArray()));
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "GameData/KerbalNews/Sites");
        var sites = new Sites(eng, rel => { var f = Path.Combine(dir, rel); return File.Exists(f) ? File.ReadAllText(f) : null; });
        sites.Now = () => Day;
        var page = sites.Render("engineering.kb").Html;
        Assert(page.Contains("Maneuver node check") && page.Contains("Check my maneuver node") && page.Contains("Land on Minmus"), "page shows the node check and the plan");

        // A crash, recorded flight data, an investigation and a press release.
        snap.Node = null; snap.VesselId = "v9"; snap.Vessel = "Lark I"; snap.Situation = "flying in Kerbin's atmosphere"; snap.Altitude = 9000; snap.Speed = 480;
        eng.EngRecord(snap, Day + 10);
        eng.EngRecord(snap, Day + 14);
        var e = CoreTests.Ev(EventTypes.Failure, Day + 15, "v9", "Kerbin"); e.VesselName = "Lark I"; e.Detail = "The vessel was destroyed in flight at about 9 km.";
        eng.Ingest(e);
        Assert(s.Incidents.Count == 1 && s.Incidents[0].Telemetry.Count == 2 && s.Incidents[0].Status == "open" && inv == 0, "incident opened without an AI call");
        Assert(sites.Render("engineering.kb").Html.Contains("Investigate"), "incident listed");
        sites.Handle("action:eng-investigate/" + s.Incidents[0].Id, null);
        Assert(inv == 1 && body.Contains("480 m/s") && s.Incidents[0].Cause.Contains("fins"), "investigation used the flight data");
        Assert(eng.IncidentText().Contains("internal"), "assistant sees internal findings");
        sites.Handle("action:eng-share/" + s.Incidents[0].Id, null);
        Assert(s.Releases.Count == 1 && s.Releases[0].Type == "incident" && s.Releases[0].Body.Contains("aerodynamic upset") && s.Incidents[0].ReleaseId != "", "findings shared as a press release");
        var n = new DataNode("K"); s.Save(n);
        var back = NewsState.Load(n);
        Assert(back.Incidents.Count == 1 && back.Incidents[0].Cause == s.Incidents[0].Cause && back.Incidents[0].Telemetry.Count == 2 && back.EngMission == "land on Minmus and come back", "incidents and mission are saved");
    }
}
