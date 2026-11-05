using System;
using System.IO;
using System.Linq;
using KerbalNews;
using KerbalNews.Web;

public static class PressPhotoTests
{
    static void Assert(bool c, string m) { if (!c) throw new Exception("Assertion failed: " + m); }

    public static void TestPressShotsBecomeNewsPhotos()
    {
        double day = 21600;
        var s = new NewsState();
        var clock = new SimpleClock(day);
        var eng = new NewsEngine(s, new GlobalSettings { OfflineMode = true }, null, clock);
        var removed = new System.Collections.Generic.List<string>();
        eng.PressShotRemoved += f => removed.Add(f);
        eng.Tick(0, 0, true);
        eng.TrackLaunch("v1", new CraftSnapshot { VesselName = "Big Bertha", Parts = 120, MassTons = 210, HeightM = 48, Stages = 4 }, 100);
        s.SetSilent("v1", true);
        var pad = eng.AddPressShot("press-pad.jpg", 50, "v1", "Big Bertha", "Kerbin", "PRELAUNCH", "pad", "Gus Kerman for Kerbin Daily Herald, 1200 mm from the bay causeway, about 3.2 km away");
        Assert(pad.Public && pad.IsPress && pad.Caption.Contains("launch pad"), "press shot is public with a caption");
        var thread = s.Forum.FirstOrDefault(t => t.Image == "press-pad.jpg");
        Assert(thread != null && thread.Title.Contains("isn't saying"), "a spotter posts the silent craft on Snackboard");
        Assert(s.CommunityJobs.Any(j => j.Target == "t:" + thread.Id), "the post draws replies");
        eng.AddPressShot("press-pad2.jpg", 60, "v1", "Big Bertha", "Kerbin", "PRELAUNCH", "pad", "x");
        Assert(s.Forum.Count(t => t.Image != "") == 1, "one spotter post per moment");
        Assert(eng.HasPressShot("v1", "pad") && !eng.HasPressShot("v1", "liftoff"), "HasPressShot");

        eng.Ingest(new NewsEvent { Type = EventTypes.Launch, UT = 100, VesselId = "v1", VesselName = "Big Bertha", Body = "Kerbin" });
        eng.Ingest(new NewsEvent { Type = EventTypes.Failure, UT = 150, VesselId = "v1", VesselName = "Big Bertha", Body = "Kerbin", Detail = "exploded" });
        eng.AddPressShot("press-lift.jpg", 102, "v1", "Big Bertha", "Kerbin", "FLYING", "liftoff", "y");
        eng.AddPressShot("press-boom.jpg", 151, "v1", "Big Bertha", "Kerbin", "FLYING", "explosion", "z");
        for (int i = 0; i < 8; i++) eng.AddPressShot("press-asc" + i + ".jpg", 120 + i, "v1", "Big Bertha", "Kerbin", "FLYING", "ascent", "w");
        Assert(s.Gallery.Count(g => g.IsPress && g.VesselId == "v1") == NewsEngine.MaxPressShotsPerVessel, "press shots are capped per vessel");
        Assert(removed.Count > 0 && removed.All(f => f.StartsWith("press-asc") || f == "press-pad2.jpg"), "ascent shots go first: " + string.Join(",", removed.ToArray()));
        Assert(s.Gallery.Any(g => g.File == "press-boom.jpg") && s.Gallery.Any(g => g.File == "press-pad.jpg"), "key moments are kept");

        var story = s.Stories.First(x => x.VesselId == "v1");
        string prompt = PromptBuilder.User(story, CoveragePlan.For(story), s, null, clock, day * 2);
        Assert(prompt.Contains("Press photographers got pictures") && prompt.Contains("breaks apart"), "writers know about the photos");
        eng.Tick(day * 3, 1, true);
        var arts = s.Articles.Where(a => a.StoryId == story.Id).ToList();
        Assert(arts.Count > 0 && arts.All(a => a.Image == "gallery:press-boom.jpg"), "failure coverage runs the explosion photo: " + string.Join(",", arts.Select(a => a.Image).ToArray()));

        string dir = Path.Combine(Directory.GetCurrentDirectory(), "GameData/KerbalNews/Sites");
        var sites = new Sites(eng, rel => { var p = Path.Combine(dir, rel); return File.Exists(p) ? File.ReadAllText(p) : null; });
        double now = day * 3;
        sites.Now = () => now;
        var art = sites.Render(Sites.ArticleUrl(arts[0])).Html;
        Assert(art.Contains("kbshot:press-boom.jpg") && art.Contains("Photo: z"), "article shows the press photo and its credit");
        Assert(sites.Render(Sites.ForumHost + "/t/" + thread.Id).Html.Contains("kbshot:press-pad.jpg"), "spotter thread shows the photo");
        var gal = sites.Render(Sites.GalleryHost).Html;
        Assert(gal.Contains("Seen by the press") && gal.Contains("kbshot:press-lift.jpg"), "gallery lists press photos");

        var node = new DataNode("NEWS");
        s.Save(node);
        var back = NewsState.Load(node);
        var g2 = back.Gallery.First(g => g.File == "press-boom.jpg");
        Assert(g2.IsPress && g2.Kind == "explosion" && g2.Credit == "z", "press shot fields saved");
        Assert(back.Forum.Any(t => t.Image == "press-pad.jpg"), "thread image saved");

        var gs = new GlobalSettings { PressPhotos = false };
        var sn = new DataNode("S"); var sec = new DataNode("K");
        gs.Save(sn, sec);
        Assert(!GlobalSettings.Load(sn, sec).PressPhotos, "press photo setting saved");
    }
}
