using System;
using System.Collections.Generic;
using System.Linq;

namespace KerbalNews.Web
{
    /// <summary>Comment sections, Snackboard (the community forum), and the image settings.</summary>
    public partial class Sites
    {
        public const string ForumHost = "snackboard.kb";

        /// <summary>Which comment the player is replying to, per discussion ("a:id" or "t:id").</summary>
        public readonly Dictionary<string, string> ReplyTarget = new Dictionary<string, string>();
        public string SubmitBoard = "general";
        public string ImageTestResult = "", ImageTestFile = "";
        public bool ImageTestOk;

        static readonly Dictionary<string, string> BoardInfo = new Dictionary<string, string>
        {
            { "general", "Anything about the space program" }, { "launches", "Launch day threads and pad sightings" },
            { "missions", "Ongoing missions, orbits and landings" }, { "science", "Data, papers and the people who read them" },
            { "policy", "Budgets, the Council and who pays for what" }, { "memes", "Low effort, high orbit" },
            { "ama", "Ask the agency (or anyone) anything" },
        };

        // ------------------------------------------------------------ comments

        List<object> CommentsModel(string target, List<Comment> list)
        {
            Engine.EnsureIds(list);
            string replying;
            ReplyTarget.TryGetValue(target, out replying);
            return Comment.Threaded(list).Select(kv =>
            {
                var c = kv.Key;
                return (object)new Dictionary<string, object>
                {
                    { "id", c.Id }, { "author", c.Author }, { "text", c.Text }, { "html", Linked(c.Text) }, { "likes", c.Likes },
                    { "initial", c.Author.Length > 0 ? c.Author.Substring(0, 1).ToUpperInvariant() : "?" },
                    { "depth", kv.Value }, { "indent", (kv.Value * 26).ToString(Inv) + "px" }, { "isReply", kv.Value > 0 },
                    { "agency", c.Player && c.Agency }, { "mine", c.Player && !c.Agency },
                    { "up", c.Vote > 0 }, { "down", c.Vote < 0 },
                    { "replyUrl", "action:reply-to/" + target + "/" + c.Id },
                    { "upUrl", "action:vote/" + target + "/" + c.Id + "/up" }, { "downUrl", "action:vote/" + target + "/" + c.Id + "/down" },
                    { "replying", replying == c.Id },
                };
            }).ToList();
        }

        Dictionary<string, object> ReplyBox(string target, List<Comment> list)
        {
            string replying;
            ReplyTarget.TryGetValue(target, out replying);
            var to = replying != null ? list.FirstOrDefault(c => c.Id == replying) : null;
            bool waiting = State.CommunityJobs.Any(j => j.Target == target);
            return new Dictionary<string, object>
            {
                { "target", target }, { "replyingTo", to != null ? to.Author : "" }, { "cancelUrl", "action:reply-to/" + target + "/" },
                { "anonHandle", State.AnonHandle != "" ? State.AnonHandle : "orbital_onlooker" }, { "agencyName", Engine.AgencyName },
                { "agencyUrl", "action:comment/" + target + "/agency" }, { "anonUrl", "action:comment/" + target + "/anon" },
                { "status", waiting ? Engine.CommunityStatus : "" },
            };
        }

        // ------------------------------------------------------------ Snackboard

        PageResult Snackboard(string url, string path, Dictionary<string, string> query)
        {
            if (path.StartsWith("t/"))
            {
                var t = State.Forum.FirstOrDefault(x => x.Id == path.Substring(2));
                if (t == null) return NotFound(url);
                return Page(url, "snackboard", "thread.html", ThreadPage(t), t.Title + " - Snackboard", true);
            }
            if (path == "submit")
            {
                var m = BoardsModel(SubmitBoard);
                m["anonHandle"] = State.AnonHandle != "" ? State.AnonHandle : "orbital_onlooker";
                m["setback"] = State.Mood.LastFailureSummary;
                m["agencyName"] = Engine.AgencyName;
                m["board"] = SubmitBoard;
                return Page(url, "snackboard", "submit.html", m, "New post - Snackboard", false);
            }
            string board = path.StartsWith("b/") ? path.Substring(2) : "";
            if (board != "" && !BoardInfo.ContainsKey(board)) return NotFound(url);
            string sort = query.ContainsKey("sort") ? query["sort"] : "hot";
            var model = BoardsModel(board);
            double now = Now();
            var threads = State.Forum.Where(t => t.UT <= now && (board == "" || t.Board == board));
            threads = sort == "new" ? threads.OrderByDescending(t => t.UT)
                : sort == "top" ? threads.OrderByDescending(t => t.Score)
                : threads.OrderByDescending(t => Hot(t, now));
            var list = threads.Take(60).ToList();
            model["threads"] = list.Select(t => (object)ThreadSummary(t)).ToList();
            model["empty"] = list.Count == 0;
            model["sort"] = sort;
            model["sortHot"] = sort == "hot"; model["sortNew"] = sort == "new"; model["sortTop"] = sort == "top";
            model["base"] = board == "" ? ForumHost : ForumHost + "/b/" + board;
            model["boardTitle"] = board == "" ? "Front page" : "b/" + board;
            model["boardAbout"] = board == "" ? "The most talked-about threads across Kerbin." : BoardInfo[board];
            return Page(url, "snackboard", "index.html", model, (board == "" ? "Snackboard" : "b/" + board + " - Snackboard"), true);
        }

        double Hot(ForumThread t, double now)
        {
            // Score decays with age, like any busy forum's front page.
            double ageDays = Math.Max(0, (now - t.UT) / Clock.DaySeconds);
            return (t.Score + 2 * t.Comments.Count) / Math.Pow(ageDays + 1, 1.5);
        }

        static string Plural(int n, string word) { return n + " " + word + (n == 1 ? "" : "s"); }

        Dictionary<string, object> BoardsModel(string current)
        {
            var m = M();
            m["boards"] = BoardInfo.Select(kv => (object)new Dictionary<string, object>
            {
                { "name", kv.Key }, { "about", kv.Value }, { "url", ForumHost + "/b/" + kv.Key }, { "on", kv.Key == current },
                { "count", State.Forum.Count(t => t.Board == kv.Key) }, { "countText", Plural(State.Forum.Count(t => t.Board == kv.Key), "thread") }, { "pickUrl", "action:submit-board/" + kv.Key },
            }).ToList();
            m["current"] = current;
            return m;
        }

        Dictionary<string, object> ThreadSummary(ForumThread t)
        {
            var story = t.StoryId != "" ? State.Articles.FirstOrDefault(a => a.StoryId == t.StoryId) : null;
            return new Dictionary<string, object>
            {
                { "id", t.Id }, { "title", t.Title }, { "url", ForumHost + "/t/" + t.Id }, { "board", t.Board }, { "boardUrl", ForumHost + "/b/" + t.Board },
                { "author", t.Author }, { "agency", t.Player && t.Agency }, { "ama", t.Kind == "ama" }, { "townhall", t.Kind == "townhall" }, { "score", Compact(t.Score) },
                { "comments", t.Comments.Count }, { "date", Date(t.UT) }, { "up", t.Vote > 0 }, { "down", t.Vote < 0 },
                { "upUrl", "action:vote/t:" + t.Id + "//up" }, { "downUrl", "action:vote/t:" + t.Id + "//down" },
                { "news", story != null ? story.Headline : "" }, { "newsUrl", story != null ? ArticleUrl(story) : "" },
                { "excerpt", Clip(Unlinked(State, t.Body).Replace("\n", " "), 200) },
                { "image", t.Image != "" ? "kbshot:" + t.Image : "" },
            };
        }

        Dictionary<string, object> ThreadPage(ForumThread t)
        {
            var m = BoardsModel(t.Board);
            m["thread"] = ThreadSummary(t);
            m["paragraphs"] = t.Body.Split(new[] { "\n\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(p => (object)new Dictionary<string, object> { { "text", p.Trim() }, { "html", Linked(p.Trim()) } }).ToList();
            m["comments"] = CommentsModel("t:" + t.Id, t.Comments);
            m["reply"] = ReplyBox("t:" + t.Id, t.Comments);
            m["commentCount"] = t.Comments.Count;
            m["ama"] = (t.Kind == "ama" || t.Kind == "townhall") && t.Player;
            m["townhall"] = t.Kind == "townhall";
            m["unanswered"] = t.Kind == "ama" || t.Kind == "townhall" ? t.Comments.Count(c => !c.Player && c.ReplyTo == "" && !t.Comments.Any(r => r.Player && r.ReplyTo == c.Id)) : 0;
            return m;
        }

        // ------------------------------------------------------------ image settings

        void AddImageSettings(Dictionary<string, object> m, GlobalSettings g)
        {
            var p = g.ImagePreset;
            m["imagesOn"] = g.ImagesOn;
            m["imageOff"] = p == null;
            if (p != null) m["image"] = new Dictionary<string, object> { { "name", p.Name }, { "model", g.ImageModel }, { "ready", g.ImagesOn } };
            m["imageTiers"] = new[] { "story", "major", "historic" }.Select(t => (object)new Dictionary<string, object>
            {
                { "id", t }, { "on", g.ImageMinTier == t }, { "label", t == "story" ? "Every real story" : t == "major" ? "Major news" : "Historic firsts only" },
            }).ToList();
            m["imageStatus"] = Engine.ImageStatus;
            m["imageTestResult"] = ImageTestResult;
            m["imageTestOk"] = ImageTestOk;
            m["imageTest"] = ImageTestFile != "" ? "kbimg:" + ImageTestFile : "";
        }

        // ------------------------------------------------------------ actions

        /// <summary>Community and image actions. Returns null when the verb isn't one of these.</summary>
        ActionResult HandleCommunity(string verb, string arg, IDictionary<string, string> form)
        {
            var r = new ActionResult();
            var g = Engine.Settings;
            switch (verb)
            {
                case "comment":
                {
                    // arg: <target>/agency|anon
                    int s = arg.LastIndexOf('/');
                    if (s <= 0) return r;
                    string target = arg.Substring(0, s);
                    bool agency = arg.Substring(s + 1) == "agency";
                    string replyTo;
                    ReplyTarget.TryGetValue(target, out replyTo);
                    if (Engine.PlayerComment(target, Get(form, "comment"), agency, Get(form, "handle"), replyTo ?? "", Now()) != null)
                        ReplyTarget.Remove(target);
                    return r;
                }
                case "reply-to":
                {
                    int s = arg.LastIndexOf('/');
                    if (s <= 0) return r;
                    string target = arg.Substring(0, s), id = arg.Substring(s + 1);
                    if (id == "") ReplyTarget.Remove(target); else ReplyTarget[target] = id;
                    return r;
                }
                case "vote":
                {
                    // arg: <target>/<commentId or empty>/up|down
                    var parts = arg.Split('/');
                    if (parts.Length < 3) return r;
                    Engine.Vote(parts[0], parts[1], parts[2] == "up" ? 1 : -1);
                    return r;
                }
                case "submit-board":
                    if (BoardInfo.ContainsKey(arg)) SubmitBoard = arg;
                    return r;
                case "post-thread":
                {
                    bool agency = arg == "agency" || arg == "ama" || arg == "townhall";
                    int q;
                    if (!int.TryParse(Get(form, "questions", "5").Trim(), out q)) q = 5;
                    var t = arg == "townhall" ? Engine.TownHall(Get(form, "title"), Get(form, "body"), q, Now()) : Engine.PlayerThread(arg == "ama" ? "ama" : SubmitBoard, Get(form, "title"), Get(form, "body"), agency, Get(form, "handle"), arg == "ama", q, Now());
                    if (t != null) r.Navigate = ForumHost + "/t/" + t.Id;
                    return r;
                }
                case "img-tier":
                    if (arg == "story" || arg == "major" || arg == "historic") { g.ImageMinTier = arg; SaveSettings(); }
                    return r;
                case "test-image":
                {
                    if (Busy) return r;
                    Busy = true;
                    ImageTestResult = "Generating a sample photo (this can take up to a minute)...";
                    ImageTestOk = true;
                    string imgService = g.ImagePreset != null ? Services.ForImage(g.ImagePreset.Id) : "";
                    Engine.TestImage((ok, msg, file) =>
                    {
                        Busy = false; ImageTestOk = ok; ImageTestResult = msg;
                        if (ok) { ImageTestFile = file; if (imgService != "") g.Status[imgService] = new ServiceStatus { Ok = true, Message = "It drew a test photo." }; }
                        Changed();
                    });
                    return r;
                }
            }
            return null;
        }
    }
}
