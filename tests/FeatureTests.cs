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
}
