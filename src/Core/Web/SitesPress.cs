using System;
using System.Collections.Generic;
using System.Linq;

namespace KerbalNews.Web
{
    /// <summary>The press room (pressroom.kb) and the photo gallery (gallery.kb).</summary>
    public partial class Sites
    {
        public const string PressHost = "pressroom.kb", GalleryHost = "gallery.kb";
        public string PressType = "announcement", PressVessel = "";
        public readonly HashSet<string> PressShots = new HashSet<string>();
        public string PressResult = "";
        /// <summary>Copies the newest stock F1 screenshots into the gallery; returns how many. Set by the game.</summary>
        public Func<int> ImportScreenshots = () => 0;
        public string GalleryResult = "";

        /// <summary>Article and gallery pictures: "gallery:file" lives in the save folder, anything else in PluginData/Images.</summary>
        public static string ImageUrl(string image)
        {
            if (string.IsNullOrEmpty(image)) return "";
            return image.StartsWith("gallery:") ? "kbshot:" + image.Substring(8) : "kbimg:" + image;
        }

        PageResult PressRoom(string url)
        {
            var m = M();
            m["types"] = NewsEngine.ReleaseTypes.Select(t => (object)new Dictionary<string, object>
            {
                { "id", t }, { "on", t == PressType }, { "url", "action:press-type/" + t },
                { "label", t == "incident" ? "Incident statement" : t == "update" ? "Mission update" : t == "teaser" ? "Teaser" : "Announcement" },
            }).ToList();
            m["typeHint"] = PressType == "incident" ? "Own what happened. A clear, honest statement softens the coverage; spin that contradicts what people saw gets called out."
                : PressType == "teaser" ? "Hint at what's coming without saying it. Expect speculation."
                : PressType == "update" ? "Progress on a mission in flight. Routine updates draw modest coverage."
                : "News you want to make: a new program, a crew assignment, a milestone.";
            m["vessels"] = State.Tracked.Values.OrderByDescending(t => t.LaunchUT).Take(12).Select(t => (object)new Dictionary<string, object>
            {
                { "id", t.VesselId }, { "name", t.Craft != null && t.Craft.VesselName != "" ? t.Craft.VesselName : t.VesselId }, { "on", t.VesselId == PressVessel },
                { "url", "action:press-vessel/" + (t.VesselId == PressVessel ? "" : t.VesselId) }, { "silent", State.IsSilent(t.VesselId) },
            }).ToList();
            m["shots"] = State.Gallery.OrderByDescending(g => g.UT).Take(12).Select(g => (object)new Dictionary<string, object>
            {
                { "id", g.Id }, { "src", "kbshot:" + g.File }, { "on", PressShots.Contains(g.Id) }, { "url", "action:press-shot/" + g.Id }, { "public", g.Public },
                { "caption", g.Caption != "" ? g.Caption : g.VesselName },
            }).ToList();
            m["hasShots"] = State.Gallery.Count > 0;
            m["result"] = PressResult;
            double now = Now();
            m["releases"] = State.Releases.OrderByDescending(r => r.UT).Select(r =>
            {
                var coverage = State.Articles.Where(a => a.StoryId == r.StoryId && a.PublishedUT <= now).ToList();
                var story = State.Stories.FirstOrDefault(s => s.Id == r.StoryId);
                return (object)new Dictionary<string, object>
                {
                    { "title", r.Title }, { "date", Date(r.UT) }, { "type", r.Type }, { "body", Clip(r.Body.Replace("\n", " "), 260) },
                    { "vessel", r.VesselName }, { "pending", coverage.Count == 0 },
                    { "coverage", coverage.Select(a => (object)new Dictionary<string, object> { { "outlet", Outlets.NameOf(a.Outlet) }, { "headline", a.Headline }, { "url", ArticleUrl(a) } }).ToList() },
                    { "waiting", coverage.Count == 0 && story != null && story.Status == StoryStatus.Pending ? "The press will react around " + Date(story.PublishUT + Clock.DaySeconds * 0.4) + "." : "" },
                };
            }).ToList();
            return Page(url, "pressroom", "index.html", m, "Press room", true);
        }

        PageResult GalleryPage(string url, string path)
        {
            if (path.StartsWith("p/"))
            {
                var g = State.Gallery.FirstOrDefault(x => x.Id == path.Substring(2));
                if (g == null) return NotFound(url);
                var m = M();
                m["shot"] = ShotModel(g);
                if (g.Public)
                {
                    m["comments"] = CommentsModel("g:" + g.Id, g.Comments);
                    m["reply"] = ReplyBox("g:" + g.Id, g.Comments);
                    m["commentCount"] = g.Comments.Count;
                }
                return Page(url, "gallery", "photo.html", m, (g.Caption != "" ? g.Caption : "Photo") + " - Gallery", true);
            }
            var model = M();
            model["public"] = State.Gallery.Where(g => g.Public).OrderByDescending(g => g.ReleasedUT).Select(g => (object)ShotModel(g)).ToList();
            model["private"] = State.Gallery.Where(g => !g.Public).OrderByDescending(g => g.UT).Select(g => (object)ShotModel(g)).ToList();
            model["empty"] = State.Gallery.Count == 0;
            model["result"] = GalleryResult;
            return Page(url, "gallery", "index.html", model, "Gallery", true);
        }

        Dictionary<string, object> ShotModel(GalleryShot g)
        {
            return new Dictionary<string, object>
            {
                { "id", g.Id }, { "src", "kbshot:" + g.File }, { "url", GalleryHost + "/p/" + g.Id }, { "caption", g.Caption }, { "public", g.Public },
                { "vessel", g.VesselName }, { "body", g.Body }, { "situation", g.Situation.ToLowerInvariant().Replace("_", " ") }, { "date", Date(g.UT) },
                { "released", g.Public ? Date(g.ReleasedUT) : "" }, { "comments", g.Comments.Count }, { "releaseUrl", "action:shot-release/" + g.Id },
            };
        }

        ActionResult HandlePress(string verb, string arg, IDictionary<string, string> form)
        {
            var r = new ActionResult();
            switch (verb)
            {
                case "press-type": if (Array.IndexOf(NewsEngine.ReleaseTypes, arg) >= 0) PressType = arg; return r;
                case "press-vessel": PressVessel = arg; return r;
                case "press-shot": if (!PressShots.Remove(arg)) PressShots.Add(arg); return r;
                case "press-publish":
                {
                    var rel = Engine.PublishRelease(PressType, Get(form, "title"), Get(form, "body"), PressVessel, PressShots.ToList(), Now());
                    if (rel == null) { PressResult = "Write a title or some text first."; return r; }
                    PressResult = "Released. The press will react within about a day.";
                    PressShots.Clear();
                    PressVessel = "";
                    return r;
                }
                case "shot-release":
                    Engine.ReleaseShot(arg, Get(form, "caption", null), Now());
                    return r;
                case "import-shots":
                    int n = ImportScreenshots();
                    GalleryResult = n > 0 ? "Imported " + n + " screenshot" + (n == 1 ? "" : "s") + " from the stock Screenshots folder." : "No new stock screenshots found.";
                    return r;
            }
            return null;
        }
    }
}
