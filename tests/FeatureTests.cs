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
}
