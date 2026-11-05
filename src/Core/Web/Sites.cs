using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KerbalNews.Web
{
    public class PageResult
    {
        public string Url;
        public string Title;
        public string Html;
        public string Css;
        /// <summary>Re-render this page when news state changes (front pages, feeds).</summary>
        public bool Live;
    }

    /// <summary>
    /// Routes in-game URLs to site templates and fills them with data from the save.
    /// Templates and stylesheets are plain files under GameData/KerbalNews/Sites/&lt;site&gt;/,
    /// so sites can be restyled without recompiling.
    /// </summary>
    public partial class Sites
    {
        public const string Home = "kerbalnews.kb";
        readonly Func<string, string> read;   // reads "site/file.html"; null if missing
        public NewsEngine Engine;
        public Func<WorldDigest> Digest = () => null;
        public Func<double> Now = () => 0;
        public Func<float> UiScale = () => 1;
        /// <summary>The vessel the player is flying (persistentId and name), or nulls outside flight.</summary>
        public Func<string> ActiveVesselId = () => null;
        public Func<string> ActiveVesselName = () => null;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public Sites(NewsEngine engine, Func<string, string> readFile)
        {
            Engine = engine;
            read = readFile;
        }

        NewsState State { get { return Engine.State; } }
        IGameClock Clock { get { return Engine.Clock; } }

        public static string SiteForOutlet(string outletId) { return outletId; }

        public static string DomainOf(string outletId)
        {
            var o = Outlets.Get(outletId);
            return o != null ? o.Domain : outletId + ".kb";
        }

        public static string ArticleUrl(Article a) { return DomainOf(a.Outlet) + "/article/" + a.Id; }

        static readonly Dictionary<string, string> SpecialSites = new Dictionary<string, string>
        {
            { "kerbnet.kb", "kerbnet" }, { "tracker.kb", "tracker" }, { "agency.kb", "agency" }, { "settings.kb", "settings" }, { "telemetry.kb", "telemetry" },
        };

        /// <summary>Splits "host/path?query" into parts. Accepts "http(s)://" prefixes.</summary>
        public static void Split(string url, out string host, out string path, out Dictionary<string, string> query)
        {
            url = (url ?? "").Trim();
            if (url.StartsWith("http://")) url = url.Substring(7);
            if (url.StartsWith("https://")) url = url.Substring(8);
            query = new Dictionary<string, string>();
            int q = url.IndexOf('?');
            if (q >= 0)
            {
                foreach (var kv in url.Substring(q + 1).Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq > 0) query[Uri.UnescapeDataString(kv.Substring(0, eq))] = Uri.UnescapeDataString(kv.Substring(eq + 1).Replace('+', ' '));
                }
                url = url.Substring(0, q);
            }
            int slash = url.IndexOf('/');
            host = (slash < 0 ? url : url.Substring(0, slash)).ToLowerInvariant();
            path = slash < 0 ? "" : url.Substring(slash + 1).Trim('/');
            if (host == "" || host == "www." + Home) host = Home;
        }

        public PageResult Render(string url)
        {
            string host, path;
            Dictionary<string, string> query;
            Split(url, out host, out path, out query);
            try
            {
                var outlet = Outlets.All.FirstOrDefault(o => o.Domain == host);
                if (outlet != null)
                {
                    if (path.StartsWith("article/"))
                    {
                        var a = State.Articles.FirstOrDefault(x => x.Id == path.Substring(8) && x.PublishedUT <= Now());
                        if (a == null) return NotFound(url);
                        if (a.Removed) return new PageResult { Url = url, Title = "Removed - " + outlet.Name, Html = "<body><div style=\"padding: 60px 40px\"><h1>" + Html.Escape(outlet.Name) + "</h1><p>" + Html.Escape(a.RemovedNote != "" ? a.RemovedNote : "This article is no longer available.") + "</p><p><a href=\"" + outlet.Domain + "\">Back to the front page</a></p></div></body>", Css = "body { background: #ffffff; color: #222222; } h1 { font-size: 30px; } p { font-size: 18px; }" };
                        a.Read = true;
                        return Page(url, outlet.Id, "article.html", ArticleModel(a, true), a.Headline + " - " + outlet.Name, false);
                    }
                    return Page(url, outlet.Id, "index.html", OutletModel(outlet), outlet.Name, true);
                }
                string site;
                if (SpecialSites.TryGetValue(host, out site))
                {
                    switch (site)
                    {
                        case "kerbnet": return Page(url, "kerbnet", "index.html", KerbnetModel(path.StartsWith("story/") ? path.Substring(6) : null), "Kerbnet", true);
                        case "tracker": return Page(url, "tracker", "index.html", TrackerModel(), "Universe tracker", true);
                        case "agency":
                            if (path == "assistant") return Page(url, "agency", "assistant.html", AssistantModel(), "Assistant", false);
                            if (path == "ledger") return Page(url, "agency", "ledger.html", LedgerModel(), "Paper trail", false);
                            if (path == "mods") return Page(url, "agency", "mods.html", ModsModel(), "Mods", false);
                            return Page(url, "agency", "index.html", AgencyModel(), "Agency profile", false);
                        case "settings": return Page(url, "settings", "index.html", SettingsModel(), "Settings", false);
                        case "telemetry": return Page(url, "telemetry", "index.html", TelemetryModel(), "Telemetry sharing", false);
                    }
                }
                if (host == ForumHost) return Snackboard(url, path, query);
                if (host == PressHost) return PressRoom(url);
                if (host == GalleryHost) return GalleryPage(url, path);
                if (host == LiveHost) return LivePage(url, path);
                if (host == Home)
                {
                    if (path == "polls") return Page(url, "portal", "polls.html", PollsModel(), "Polls", true);
                    if (path == "search") return Page(url, "portal", "search.html", SearchModel(query.ContainsKey("q") ? query["q"] : ""), "Search", false);
                    return Page(url, "portal", "index.html", PortalModel(), "Kerbal News Network", true);
                }
                return NotFound(url);
            }
            catch (Exception e)
            {
                return new PageResult { Url = url, Title = "Error", Html = "<body><h1>Page error</h1><p>" + Html.Escape(e.Message) + "</p></body>", Css = "body { padding: 20px; }" };
            }
        }

        PageResult NotFound(string url)
        {
            return new PageResult
            {
                Url = url, Title = "Not found",
                Html = "<body><div class=\"nf\"><h1>404</h1><p>No such page on the Kerbin network: " + Html.Escape(url) + "</p><p><a href=\"" + Home + "\">Back to Kerbal News Network</a></p></div></body>",
                Css = "body { background: #f4f1ea; padding: 40px; } .nf { max-width: 600px; }",
            };
        }

        PageResult Page(string url, string site, string file, Dictionary<string, object> model, string title, bool live)
        {
            string tpl = read(site + "/" + file);
            if (tpl == null) return new PageResult { Url = url, Title = title, Html = "<body><h1>Missing template</h1><p>Sites/" + Html.Escape(site + "/" + file) + " was not found. Reinstall the mod.</p></body>", Css = "body { padding: 20px; background: #ffffff; }" };
            Func<string, string> partials = name => read("_shared/" + name + ".html") ?? read(site + "/" + name + ".html") ?? "";
            AddCommon(model, url);
            string html = Template.Render(tpl, model, partials);
            string css = (read("_shared/base.css") ?? "") + "\n" + (read(site + "/style.css") ?? "");
            return new PageResult { Url = url, Title = title, Html = html, Css = css, Live = live };
        }

        // ------------------------------------------------------------------ models

        static Dictionary<string, object> M() { return new Dictionary<string, object>(); }
        string Date(double ut) { return Clock.FormatDate(ut); }

        void AddCommon(Dictionary<string, object> m, string url)
        {
            double now = Now();
            m["today"] = Date(now);
            m["url"] = url;
            m["home"] = Home;
            m["agency"] = State.Profile.Name;
            m["agencyShort"] = State.Profile.ShortName;
            m["unread"] = Engine.UnreadCount(now);
            m["aiOff"] = !Engine.Settings.CanCallAi;
            m["offline"] = Engine.Settings.OfflineMode;
            m["providerName"] = Engine.Settings.Preset.Name;
            m["status"] = Engine.Status;
            m["error"] = Engine.LastError;
            m["pending"] = Engine.PendingCount;
            m["mood"] = MoodModel();
            m["outlets"] = Outlets.All.Select(o => (object)new Dictionary<string, object>
            {
                { "id", o.Id }, { "name", o.Name }, { "domain", o.Domain }, { "color", o.Color },
            }).ToList();
        }

        Dictionary<string, object> MoodModel()
        {
            var md = State.Mood;
            var m = M();
            m["excitement"] = (int)Math.Round(md.Excitement);
            m["trust"] = (int)Math.Round(md.Trust);
            m["excitementWord"] = Mood.Word(md.Excitement);
            m["trustWord"] = Mood.Word(md.Trust);
            m["excitementBar"] = BarWidth(md.Excitement);
            m["trustBar"] = BarWidth(md.Trust);
            m["concerns"] = md.Concerns.Where(kv => kv.Value >= 10).OrderByDescending(kv => kv.Value).Take(4)
                .Select(kv => (object)new Dictionary<string, object> { { "name", kv.Key }, { "level", Mood.Word(kv.Value) } }).ToList();
            var poll = State.Polls.LastOrDefault(p => p.UT <= Now());
            if (poll != null) m["poll"] = new Dictionary<string, object> { { "approve", poll.Approve }, { "url", Home + "/polls" } };
            return m;
        }

        Dictionary<string, object> PollsModel()
        {
            var m = M();
            var list = State.Polls.Where(p => p.UT <= Now()).ToList();
            Func<Poll, Dictionary<string, object>> model = p => new Dictionary<string, object>
            {
                { "date", Date(p.UT) }, { "pollster", p.Pollster }, { "approve", p.Approve }, { "trust", p.Trust }, { "funding", p.Funding }, { "interest", p.Interest },
                { "margin", p.Margin }, { "concern", p.TopConcern }, { "approveBar", BarWidth(p.Approve) }, { "trustBar", BarWidth(p.Trust) }, { "fundingBar", BarWidth(p.Funding) },
                { "interestBar", BarWidth(p.Interest) }, { "h", (4 + p.Approve * 1.2).ToString("0", Inv) + "px" },
            };
            var last = list.LastOrDefault();
            if (last != null)
            {
                var lm = model(last);
                var prev = list.Count > 1 ? list[list.Count - 2] : null;
                if (prev != null) lm["change"] = (last.Approve - prev.Approve >= 0 ? "+" : "") + (last.Approve - prev.Approve) + " since " + Date(prev.UT);
                m["latest"] = lm;
            }
            m["trend"] = list.Skip(Math.Max(0, list.Count - 16)).Select(p => (object)model(p)).ToList();
            m["history"] = list.AsEnumerable().Reverse().Take(12).Select(p => (object)model(p)).ToList();
            m["empty"] = list.Count == 0;
            return m;
        }

        static string BarWidth(double v) { return ((int)Math.Round(Math.Max(2, Math.Min(100, v)))).ToString(Inv) + "%"; }

        static string KindLabel(string k)
        {
            switch (k)
            {
                case "analysis": return "Analysis";
                case "opinion": return "Opinion";
                case "feature": return "Feature";
                case "expert": return "Expert view";
                case "statement": return "Official statement";
                case "rumor": return "Exclusive";
                default: return "News";
            }
        }

        static string Tier(double sig)
        {
            if (sig >= 80) return "historic";
            if (sig >= 60) return "major";
            if (sig >= 35) return "story";
            return "brief";
        }

        Dictionary<string, object> ArticleModel(Article a, bool full)
        {
            var o = Outlets.Get(a.Outlet);
            var paras = a.Body.Split(new[] { "\n\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => p != "").ToList();
            var m = M();
            m["id"] = a.Id;
            m["storyId"] = a.StoryId;
            m["url"] = ArticleUrl(a);
            m["outletId"] = a.Outlet;
            m["outletName"] = o != null ? o.Name : a.Outlet;
            m["outletDomain"] = DomainOf(a.Outlet);
            m["outletColor"] = o != null ? o.Color : "#333333";
            m["headline"] = a.Headline;
            m["subhead"] = a.Subhead;
            m["reporter"] = a.Reporter;
            m["date"] = Date(a.PublishedUT);
            m["kind"] = a.Kind;
            m["kindLabel"] = KindLabel(a.Kind);
            m["tier"] = Tier(a.Significance);
            m["unread"] = !a.Read;
            m["lead"] = paras.Count > 0 ? Clip(paras[0], 240) : "";
            m["commentCount"] = a.Comments.Count;
            m["thumb"] = ImageUrl(a.Image);
            m["tags"] = a.Tags.Select(t => (object)t).ToList();
            m["model"] = a.Model != "" ? a.Model : (a.Offline ? "offline templates" : "");
            m["offline"] = a.Offline;
            if (full)
            {
                m["paragraphs"] = paras.Select((p, i) => (object)new Dictionary<string, object> { { "text", p }, { "html", Linked(p) }, { "first", i == 0 } }).ToList();
                m["comments"] = CommentsModel("a:" + a.Id, a.Comments);
                m["reply"] = ReplyBox("a:" + a.Id, a.Comments);
                m["image"] = ImageUrl(a.Image);
                m["photoCredit"] = PhotoCredit(a);
                double now = Now();
                m["related"] = State.Articles.Where(x => x.StoryId == a.StoryId && x.Id != a.Id && x.PublishedUT <= now)
                    .Select(x => (object)ArticleModel(x, false)).ToList();
                var social = State.Social.Where(p => p.StoryId == a.StoryId && p.UT <= now).ToList();
                m["socialCount"] = social.Count;
                m["socialUrl"] = "kerbnet.kb/story/" + a.StoryId;
                var story = State.Stories.FirstOrDefault(s => s.Id == a.StoryId);
                m["silent"] = story != null && story.Silent;
                if (story != null && !story.Silent && story.Craft != null && story.Craft.Parts > 0)
                {
                    var c = story.Craft;
                    m["craft"] = new Dictionary<string, object>
                    {
                        { "name", c.VesselName }, { "parts", c.Parts }, { "mass", c.MassTons.ToString("0.#", Inv) }, { "height", c.HeightM.ToString("0", Inv) },
                        { "stages", c.Stages }, { "dv", c.DeltaV > 0 ? c.DeltaV.ToString("N0", Inv) : "" }, { "engines", c.Engines }, { "crewCap", c.CrewCapacity },
                        { "crew", string.Join(", ", c.Crew.ToArray()) }, { "site", c.LaunchSite }, { "desc", c.Description },
                    };
                }
            }
            return m;
        }

        static string Clip(string s, int n) { return s.Length <= n ? s : s.Substring(0, s.LastIndexOf(' ', n) > n / 2 ? s.LastIndexOf(' ', n) : n) + "…"; }

        List<Article> Visible() { return State.PublishedArticles(Now()).ToList(); }

        Dictionary<string, object> OutletModel(OutletDef o)
        {
            var list = Visible().Where(a => a.Outlet == o.Id).Take(40).ToList();
            var m = M();
            m["outlet"] = new Dictionary<string, object> { { "id", o.Id }, { "name", o.Name }, { "domain", o.Domain }, { "style", o.Style }, { "covers", o.Covers } };
            var top = list.OrderByDescending(a => a.Significance).ThenByDescending(a => a.PublishedUT).FirstOrDefault(a => Now() - a.PublishedUT < 4 * Clock.DaySeconds) ?? list.FirstOrDefault();
            m["top"] = top != null ? ArticleModel(top, false) : null;
            m["articles"] = list.Where(a => a != top).Select(a => (object)ArticleModel(a, false)).ToList();
            m["secondary"] = list.Where(a => a != top).Take(2).Select(a => (object)ArticleModel(a, false)).ToList();
            m["rest"] = list.Where(a => a != top).Skip(2).Select(a => (object)ArticleModel(a, false)).ToList();
            m["empty"] = list.Count == 0;
            m["reporters"] = State.Reporters.ContainsKey(o.Id) ? string.Join(", ", State.Reporters[o.Id].ToArray()) : "";
            return m;
        }

        Dictionary<string, object> PortalModel()
        {
            var all = Visible();
            var m = M();
            var top = all.Where(a => Now() - a.PublishedUT < 3 * Clock.DaySeconds).OrderByDescending(a => a.Significance).ThenByDescending(a => a.PublishedUT).FirstOrDefault() ?? all.FirstOrDefault();
            m["top"] = top != null ? ArticleModel(top, false) : null;
            m["latest"] = all.Where(a => a != top).Take(30).Select(a => (object)ArticleModel(a, false)).ToList();
            m["empty"] = all.Count == 0;
            m["sections"] = Outlets.All.Select(o =>
            {
                var items = all.Where(a => a.Outlet == o.Id).Take(3).Select(a => (object)ArticleModel(a, false)).ToList();
                return (object)new Dictionary<string, object> { { "id", o.Id }, { "name", o.Name }, { "domain", o.Domain }, { "color", o.Color }, { "items", items }, { "hasItems", items.Count > 0 } };
            }).ToList();
            m["trending"] = State.Social.Where(p => p.UT <= Now()).OrderByDescending(p => p.UT).Take(40).OrderByDescending(p => p.Likes).Take(5).Select(p => (object)PostModel(p)).ToList();
            return m;
        }

        Dictionary<string, object> PostModel(SocialPost p)
        {
            var art = State.Articles.FirstOrDefault(a => a.StoryId == p.StoryId && a.PublishedUT <= Now());
            return new Dictionary<string, object>
            {
                { "handle", p.Handle }, { "persona", p.Persona }, { "text", p.Text }, { "html", Linked(p.Text) }, { "date", Date(p.UT) },
                { "likes", Compact(p.Likes) }, { "reshares", Compact(p.Reshares) },
                { "initial", p.Handle.TrimStart('@').Length > 0 ? p.Handle.TrimStart('@').Substring(0, 1).ToUpperInvariant() : "?" },
                { "avatarColor", AvatarColor(p.Handle) },
                { "storyHeadline", art != null ? art.Headline : "" }, { "storyUrl", art != null ? ArticleUrl(art) : "" },
            };
        }

        static readonly string[] Palette = { "#e57373", "#64b5f6", "#81c784", "#ffb74d", "#ba68c8", "#4db6ac", "#f06292", "#a1887f", "#90a4ae", "#dce775" };
        static string AvatarColor(string h) { return Palette[Math.Abs(OfflineWriter.StableHash(h)) % Palette.Length]; }

        public static string Compact(int n)
        {
            if (n >= 1000000) return (n / 1000000.0).ToString("0.#", Inv) + "M";
            if (n >= 1000) return (n / 1000.0).ToString("0.#", Inv) + "k";
            return n.ToString(Inv);
        }

        Dictionary<string, object> KerbnetModel(string storyId)
        {
            var posts = State.Social.Where(p => p.UT <= Now() && (storyId == null || p.StoryId == storyId)).OrderByDescending(p => p.UT).Take(120).ToList();
            var m = M();
            m["posts"] = posts.Select(p => (object)PostModel(p)).ToList();
            m["empty"] = posts.Count == 0;
            m["filtered"] = storyId != null;
            var art = storyId != null ? State.Articles.FirstOrDefault(a => a.StoryId == storyId) : null;
            m["filterHeadline"] = art != null ? art.Headline : "";
            // Trending topics: the stories with the most chatter.
            m["topics"] = State.Social.Where(p => p.UT <= Now()).GroupBy(p => p.StoryId).OrderByDescending(g => g.Sum(p => p.Likes)).Take(6)
                .Select(g =>
                {
                    var a = State.Articles.FirstOrDefault(x => x.StoryId == g.Key);
                    return (object)new Dictionary<string, object> { { "headline", a != null ? a.Headline : "Untitled" }, { "url", "kerbnet.kb/story/" + g.Key }, { "posts", g.Count() } };
                }).ToList();
            return m;
        }

        Dictionary<string, object> TrackerModel()
        {
            var d = Digest() ?? new WorldDigest();
            var m = M();
            m["stats"] = new List<object>
            {
                Stat("Satellites", d.Satellites), Stat("Stations", d.Stations), Stat("Surface bases", d.Bases),
                Stat("Kerbals in space", d.CrewInSpace), Stat("Tracked debris", d.Debris), Stat("Lost crew", d.CrewLost),
            };
            m["crewNames"] = string.Join(", ", d.CrewInSpaceNames.ToArray());
            m["debrisByBody"] = d.DebrisByBody.OrderByDescending(kv => kv.Value).Select(kv => (object)new Dictionary<string, object> { { "body", kv.Key }, { "count", kv.Value } }).ToList();
            m["records"] = State.Records.Select(r => (object)r).ToList();
            m["memory"] = State.MemorySummary;
            var hist = State.DigestHistory.Skip(Math.Max(0, State.DigestHistory.Count - 30)).ToList();
            double maxDebris = Math.Max(1, hist.Count > 0 ? hist.Max(h => h.Debris) : 1);
            double maxSats = Math.Max(1, hist.Count > 0 ? hist.Max(h => h.Satellites + h.Stations) : 1);
            m["debrisHistory"] = hist.Select(h => (object)new Dictionary<string, object> { { "h", BarWidth(h.Debris / maxDebris * 100) }, { "v", h.Debris }, { "date", Date(h.UT) } }).ToList();
            m["satHistory"] = hist.Select(h => (object)new Dictionary<string, object> { { "h", BarWidth((h.Satellites + h.Stations) / maxSats * 100) }, { "v", h.Satellites + h.Stations }, { "date", Date(h.UT) } }).ToList();
            m["hasHistory"] = hist.Count > 1;
            return m;
        }

        static object Stat(string label, int v) { return new Dictionary<string, object> { { "label", label }, { "value", v } }; }

        Dictionary<string, object> AgencyModel()
        {
            var p = State.Profile;
            var m = M();
            m["profile"] = new Dictionary<string, object>
            {
                { "name", p.Name }, { "short", p.ShortName }, { "founding", p.Founding }, { "mission", p.Mission }, { "politics", p.Politics },
                { "reputation", p.Reputation }, { "people", p.KeyPeople }, { "world", p.WorldLore }, { "rules", p.HouseRules },
            };
            m["silliness"] = State.Silliness;
            m["sillinessText"] = PromptBuilder.SillinessText(State.Silliness);
            m["sillyLevels"] = Enumerable.Range(0, 11).Select(i => (object)new Dictionary<string, object> { { "n", i }, { "on", i == State.Silliness } }).ToList();
            m["lore"] = State.Lore.OrderBy(l => l.UT).Select((l, i) => (object)new Dictionary<string, object> { { "date", Date(l.UT) }, { "text", l.Text }, { "index", State.Lore.IndexOf(l) } }).ToList();
            return m;
        }

        Dictionary<string, object> SettingsModel()
        {
            var g = Engine.Settings;
            var m = M();
            m["providers"] = Providers.All.Select(p => (object)new Dictionary<string, object> { { "id", p.Id }, { "name", p.Name }, { "on", p.Id == g.ProviderId } }).ToList();
            var pr = g.Preset;
            string v;
            m["provider"] = new Dictionary<string, object>
            {
                { "id", pr.Id }, { "name", pr.Name }, { "needsKey", pr.NeedsKey }, { "keyHint", pr.KeyHint }, { "hasKey", g.Key != "" },
                { "baseUrl", g.BaseUrls.TryGetValue(pr.Id, out v) ? v : "" }, { "defaultBaseUrl", pr.BaseUrl },
                { "writer", g.WriterModels.TryGetValue(pr.Id, out v) ? v : "" }, { "defaultWriter", pr.WriterModel },
                { "cheap", g.CheapModels.TryGetValue(pr.Id, out v) ? v : "" }, { "defaultCheap", pr.CheapModel },
            };
            m["writerChoices"] = g.ModelChoices.Select(x => (object)new Dictionary<string, object> { { "id", x }, { "on", x == g.WriterModel } }).ToList();
            m["cheapChoices"] = g.ModelChoices.Select(x => (object)new Dictionary<string, object> { { "id", x }, { "on", x == g.CheapModel } }).ToList();
            m["writerModel"] = g.WriterModel;
            m["cheapModel"] = g.CheapModel;
            m["fetchResult"] = FetchResult;
            m["maxTokens"] = g.MaxTokens;
            m["notify"] = g.NotifyOnPublish;
            m["warp"] = g.GenerateDuringWarp;
            m["pressPhotos"] = g.PressPhotos;
            m["assistantOn"] = g.AssistantOn;
            m["assistantConfirm"] = g.AssistantConfirm;
            m["scale"] = g.UiScale > 0.4f ? g.UiScale.ToString("0.0", Inv) + "x" : "auto (" + UiScale().ToString("0.0", Inv) + "x)";
            m["testResult"] = TestResult;
            m["testOk"] = TestOk;
            AddImageSettings(m, g);
            return m;
        }

        public string FetchResult = "";
        public string TestResult = "";
        public bool TestOk;

        Dictionary<string, object> TelemetryModel()
        {
            var m = M();
            string active = ActiveVesselId();
            m["silentDefault"] = State.SilentByDefault;
            if (!string.IsNullOrEmpty(active))
                m["active"] = new Dictionary<string, object> { { "id", active }, { "name", ActiveVesselName() ?? "" }, { "silent", State.IsSilent(active) } };
            m["vessels"] = State.Tracked.Values.Where(t => t.VesselId != active).OrderByDescending(t => t.LaunchUT).Take(60).Select(t => (object)new Dictionary<string, object>
            {
                { "id", t.VesselId }, { "name", t.Craft != null && t.Craft.VesselName != "" ? t.Craft.VesselName : t.VesselId },
                { "launched", Date(t.LaunchUT) }, { "silent", State.IsSilent(t.VesselId) },
            }).ToList();
            return m;
        }

        Dictionary<string, object> SearchModel(string q)
        {
            var m = M();
            m["q"] = q;
            var ql = q.Trim().ToLowerInvariant();
            var list = Visible().Where(a => ql == "" || a.Headline.ToLowerInvariant().Contains(ql) || a.Body.ToLowerInvariant().Contains(ql)
                || a.Tags.Any(t => t.Contains(ql)) || Outlets.NameOf(a.Outlet).ToLowerInvariant().Contains(ql)).Take(80).ToList();
            m["results"] = list.Select(a => (object)ArticleModel(a, false)).ToList();
            m["count"] = list.Count;
            return m;
        }
    }
}
