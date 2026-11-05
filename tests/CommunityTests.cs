using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KerbalNews;
using KerbalNews.Web;

/// <summary>Player replies, Snackboard, press photos, page actions and page layout.</summary>
public static class CommunityTests
{
    const double Day = 21600;
    static void Assert(bool c, string m) { if (!c) throw new Exception("Assertion failed: " + m); }
    static void Eq<T>(T e, T a, string m) { if (!Equals(e, a)) throw new Exception(m + ": expected <" + e + "> got <" + a + ">"); }

    static HttpResult Anthropic(string text)
    {
        var resp = new Dictionary<string, object> { { "content", new List<object> { new Dictionary<string, object> { { "type", "text" }, { "text", text } } } } };
        return new HttpResult { Status = 200, Body = Json.Write(resp) };
    }

    static string UserPrompt(HttpRequestSpec r) { return Json.Str(Json.Arr(Json.Get(Json.Parse(r.Body), "messages"))[0], "content"); }

    /// <summary>An engine with one published story (offline), ready for the player to comment on.</summary>
    static NewsEngine Published(GlobalSettings g, IHttpTransport t)
    {
        var s = new NewsState();
        s.Profile.Name = "Kerbin Space Agency";
        var eng = new NewsEngine(s, g, t, new SimpleClock(Day));
        eng.Tick(0, 0, true);
        eng.TrackLaunch("v1", CoreTests.Craft(), 10);
        eng.Ingest(CoreTests.Ev(EventTypes.Landing, 10, "v1", "Mun", "Jebediah Kerman"));
        bool online = !g.OfflineMode;
        g.OfflineMode = true;
        eng.Tick(Day * 3, 1, true);
        g.OfflineMode = !online;
        return eng;
    }

    public static void TestPlayerCommentsGetOfflineReplies()
    {
        var eng = Published(new GlobalSettings { OfflineMode = true }, null);
        var art = eng.State.Articles.First();
        var target = "a:" + art.Id;
        var mine = eng.PlayerComment(target, "We are proud of the crew.", true, "", "", Day * 3);
        Assert(mine != null && mine.Agency && mine.Author == "Kerbin Space Agency", "official comment uses the agency name");
        Eq(1, eng.State.CommunityJobs.Count, "reaction queued");
        eng.Tick(Day * 3 + 1, 2, true);
        Eq(0, eng.State.CommunityJobs.Count, "job done");
        var replies = art.Comments.Where(c => c.ReplyTo == mine.Id).ToList();
        Assert(replies.Count >= 3, "community replied to the official post");
        var threaded = Comment.Threaded(art.Comments);
        int at = threaded.FindIndex(kv => kv.Key == mine);
        Assert(threaded[at + 1].Value == 1 && threaded[at + 1].Key.ReplyTo == mine.Id, "replies sit under the player's comment");

        var anon = eng.PlayerComment(target, "Honestly the landing looked rough.", false, "@mun_watcher", "", Day * 3);
        Eq("mun_watcher", anon.Author, "anonymous handle");
        Eq("mun_watcher", eng.State.AnonHandle, "handle remembered");
        eng.Vote(target, anon.Id, -1);
        Eq(-1, anon.Likes, "own downvote replaces own upvote");

        var back = NewsState.Load(CoreSnapshot(eng.State));
        var c2 = back.Articles.First(a => a.Id == art.Id).Comments.First(c => c.Id == mine.Id);
        Assert(c2.Player && c2.Agency && c2.Text == mine.Text, "player comments saved");
    }

    static DataNode CoreSnapshot(NewsState s) { var n = new DataNode("K"); s.Save(n); return n; }

    public static void TestCommunityRepliesFromAi()
    {
        var t = new CoreTests.FakeTransport();
        var eng = Published(CoreTests.OnlineSettings(), t);
        var art = eng.State.Articles.First();
        var anon = eng.PlayerComment("a:" + art.Id, "I heard the engines were from the bargain bin.", false, "insider99", "", Day * 3);
        eng.Tick(Day * 3 + 1, 2, true);
        Eq(1, t.Sent.Count, "one community call");
        var prompt = UserPrompt(t.Sent[0]);
        Assert(prompt.Contains("Nobody knows this is the agency") && prompt.Contains("[" + anon.Id + "]"), "anonymous framing and ids");
        Assert(prompt.Contains("player_likes"), "schema");
        t.Pending[0](Anthropic("{\"replies\":[{\"author\":\"RetiredRocketeer_Bob\",\"text\":\"Source? Those engines passed every test.\",\"likes\":20,\"reply_to\":\"" + anon.Id + "\"},{\"author\":\"TruthOrbiter\",\"text\":\"Bob would say that.\",\"likes\":4,\"reply_to\":\"r1\"}],\"player_likes\":7}"));
        var bob = art.Comments.First(c => c.Author == "RetiredRocketeer_Bob" && c.ReplyTo == anon.Id);
        var truth = art.Comments.First(c => c.Author == "TruthOrbiter" && c.Text.StartsWith("Bob"));
        Eq(bob.Id, truth.ReplyTo, "r1 resolves to the earlier reply");
        Eq(7, anon.Likes, "player's likes");

        // Official account framing.
        eng.PlayerComment("a:" + art.Id, "The engines are certified.", true, "", bob.Id, Day * 3);
        eng.Tick(Day * 3 + 2, 100, true);
        Assert(UserPrompt(t.Sent[1]).Contains("verified official account"), "official framing");
    }

    public static void TestSnackboardAndAma()
    {
        var t = new CoreTests.FakeTransport { Responder = CoreTests.AnthropicReply };
        var s = new NewsState();
        var g = CoreTests.OnlineSettings();
        var eng = new NewsEngine(s, g, t, new SimpleClock(Day));
        eng.Tick(0, 0, true);
        eng.TrackLaunch("v1", CoreTests.Craft(), 10);
        eng.Ingest(CoreTests.Ev(EventTypes.Landing, 10, "v1", "Mun", "Jebediah Kerman"));
        // The main coverage call can carry a forum thread.
        t.Responder = req =>
        {
            var r = CoreTests.AnthropicReply(req);
            if (!UserPrompt(req).Contains("\"forum\"")) return r;
            var text = Json.Str(Json.Arr(Json.Get(Json.Parse(r.Body), "content"))[0], "text").Replace("```json", "").Replace("```", "").Trim();
            text = text.Substring(0, text.Length - 1) + ",\"forum\":{\"board\":\"missions\",\"title\":\"They actually landed on the Mun\",\"body\":\"Megathread.\",\"author\":\"MunGazer\",\"score\":900,\"replies\":[{\"author\":\"JebFan4Ever\",\"text\":\"JEB!!!\",\"likes\":50,\"reply_to\":\"\"},{\"author\":\"TaxpayerTed\",\"text\":\"At what cost?\",\"likes\":9,\"reply_to\":\"r1\"}]}}";
            return Anthropic(text);
        };
        eng.Tick(Day * 3, 1, true);
        var thread = s.Forum.Single();
        Eq("missions", thread.Board, "board");
        Eq(thread.Comments[0].Id, thread.Comments[1].ReplyTo, "threaded replies");

        // The player hosts an AMA; the community asks; the player answers.
        t.Responder = null;
        var ama = eng.PlayerThread("ama", "We just landed on the Mun. Ask us anything.", "", true, "", true, 3, Day * 3);
        eng.Tick(Day * 3 + 1, 2, true);
        var prompt = UserPrompt(t.Sent.Last());
        Assert(prompt.Contains("Ask Me Anything") && prompt.Contains("Write 3 questions"), "AMA prompt");
        t.Pending.Last()(Anthropic("{\"replies\":[{\"author\":\"FirstTimeReader\",\"text\":\"How cold is the Mun?\",\"likes\":3,\"reply_to\":\"\"},{\"author\":\"TaxpayerTed\",\"text\":\"What did it cost?\",\"likes\":8,\"reply_to\":\"\"}],\"player_likes\":300}"));
        Eq(2, ama.Comments.Count, "questions arrived");
        Eq(300, ama.Score, "AMA score");
        var q = ama.Comments[0];
        eng.PlayerComment("t:" + ama.Id, "Very cold at night.", true, "", q.Id, Day * 3);
        Assert(ama.Comments.Last().ReplyTo == q.Id, "answer under the question");

        var back = NewsState.Load(CoreSnapshot(s));
        Eq(2, back.Forum.Count, "forum saved");
        Assert(back.Forum.Any(x => x.Kind == "ama" && x.Agency) && back.CommunityJobs.Count == 1, "AMA and pending reaction saved");
    }

    // ------------------------------------------------------------ images

    public static void TestImageFormats()
    {
        var png = Convert.ToBase64String(Enumerable.Range(0, 200).Select(i => (byte)i).ToArray());
        byte[] b; string err;
        Assert(ImageProviders.TryExtract(ImageApiKind.OpenAIImages, new HttpResult { Status = 200, Body = "{\"data\":[{\"b64_json\":\"" + png + "\"}]}" }, out b, out err) && b.Length == 200, "openai");
        Assert(ImageProviders.TryExtract(ImageApiKind.Gemini, new HttpResult { Status = 200, Body = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"hi\"},{\"inlineData\":{\"mimeType\":\"image/png\",\"data\":\"" + png + "\"}}]}}]}" }, out b, out err), "gemini");
        Assert(ImageProviders.TryExtract(ImageApiKind.Fal, new HttpResult { Status = 200, Body = "{\"images\":[{\"url\":\"data:image/jpeg;base64," + png + "\"}]}" }, out b, out err), "fal data uri");
        Assert(!ImageProviders.TryExtract(ImageApiKind.OpenAIImages, new HttpResult { Status = 400, Body = "{\"error\":{\"message\":\"safety system\"}}" }, out b, out err) && err.Contains("safety"), "error message");

        var req = ImageProviders.Build(ImageProviders.Get("openai"), null, "gpt-image-1", "k", "a rocket");
        Assert(req.Url.EndsWith("/images/generations") && req.Headers["Authorization"] == "Bearer k" && req.Body.Contains("1536x1024") && !req.Body.Contains("response_format"), "openai request");
        var gem = ImageProviders.Build(ImageProviders.Get("gemini"), null, null, "k", "x");
        Assert(gem.Url.Contains("gemini-2.5-flash-image:generateContent") && gem.Body.Contains("IMAGE"), "gemini request");
        var fal = ImageProviders.Build(ImageProviders.Get("fal"), null, null, "k", "x");
        Assert(fal.Headers["Authorization"] == "Key k" && fal.Body.Contains("sync_mode"), "fal request");
        Assert(ImageProviders.TierQualifies("historic", "major") && !ImageProviders.TierQualifies("story", "major"), "tiers");
    }

    public static void TestPressPhotoForBigStory()
    {
        var png = Convert.ToBase64String(Enumerable.Range(0, 300).Select(i => (byte)(i * 7)).ToArray());
        var t = new CoreTests.FakeTransport();
        var g = CoreTests.OnlineSettings();
        g.ImageProviderId = "openai";
        g.Keys["img:openai"] = "img-key";
        Assert(g.ImagesOn, "images on with a key");
        var s = new NewsState();
        var eng = new NewsEngine(s, g, t, new SimpleClock(Day));
        var saved = new Dictionary<string, byte[]>();
        eng.SaveImage = (f, bytes) => saved[f] = bytes;
        eng.Tick(0, 0, true);
        eng.TrackLaunch("v1", CoreTests.Craft(), 10);
        var e = CoreTests.Ev(EventTypes.Landing, 10, "v1", "Mun", "Jebediah Kerman");
        eng.Ingest(e);
        eng.Ingest(new NewsEvent { Type = EventTypes.Milestone, UT = 11, VesselId = "v1", VesselName = "Test Rocket v1", Body = "Mun", Detail = "First crewed landing on the Mun" });
        eng.Tick(Day * 3, 1, true);
        Assert(UserPrompt(t.Sent[0]).Contains("image_prompt"), "writer is asked for a photo description");
        t.Pending[0](CoreTests.AnthropicReply(t.Sent[0]));
        eng.Tick(Day * 3 + 1, 2, true);
        var imgReq = t.Sent.Last();
        Assert(imgReq.Url == "https://api.openai.com/v1/images/generations" && imgReq.Headers["Authorization"] == "Bearer img-key", "photo uses its own key");
        t.Pending.Last()(new HttpResult { Status = 200, Body = "{\"data\":[{\"b64_json\":\"" + png + "\"}]}" });
        Eq(1, saved.Count, "photo written");
        Assert(s.Articles.All(a => a.Image == saved.Keys.First()), "every outlet runs the pool photo");
        var back = NewsState.Load(CoreSnapshot(s));
        Assert(back.Articles.All(a => a.Image != ""), "photo saved with the news");

        // Small news gets no photo.
        int before = t.Sent.Count;
        eng.Ingest(new NewsEvent { Type = EventTypes.ContractDone, UT = Day * 4, Detail = "Test a part" });
        for (int i = 0; i < 6; i++) { eng.Tick(Day * (5 + i), 10 + i * 100, true); while (t.Pending.Count > 0) { var p = t.Pending[0]; t.Pending.RemoveAt(0); p(CoreTests.AnthropicReply(t.Sent[t.Sent.Count - 1])); } }
        Assert(!t.Sent.Skip(before).Any(r => r.Url.Contains("images")), "no photo for small news");
    }

    // ------------------------------------------------------------ actions and layout

    static Sites MakeSites(NewsEngine eng)
    {
        var dir = Path.Combine(Directory.GetCurrentDirectory(), "GameData/KerbalNews/Sites");
        var sites = new Sites(eng, rel => { var p = Path.Combine(dir, rel); return File.Exists(p) ? File.ReadAllText(p) : null; });
        sites.Now = () => Day * 3;
        return sites;
    }

    public static void TestPageActions()
    {
        var g = new GlobalSettings { OfflineMode = true };
        var eng = Published(g, null);
        var sites = MakeSites(eng);
        int saves = 0;
        sites.SaveSettings = () => saves++;
        Eq("kerbalnews.kb/search?q=mun%20arrow", sites.Handle("search", new Dictionary<string, string> { { "q", " mun arrow " } }).Navigate, "search");
        sites.Handle("action:provider/openai", null);
        sites.Handle("save-settings", new Dictionary<string, string> { { "key", "sk-test" }, { "writer", "gpt-5-mini" }, { "maxTokens", "9000" } });
        Assert(g.ProviderId == "openai" && g.Key == "sk-test" && !g.OfflineMode && g.WriterModel == "gpt-5-mini" && g.MaxTokens == 9000 && saves >= 2, "settings form");
        sites.Handle("action:img-provider/gemini", null);
        sites.Handle("save-images", new Dictionary<string, string> { { "imgKey", "AIza-x" } });
        Assert(g.ImagesOn && g.Keys["img:gemini"] == "AIza-x" && g.Key == "sk-test", "image key kept apart");

        var art = eng.State.Articles.First();
        string target = "a:" + art.Id;
        var first = art.Comments[0];
        sites.Handle("action:reply-to/" + target + "/" + first.Id, null);
        Assert(sites.Render(Sites.ArticleUrl(art)).Html.Contains("Replying to"), "reply banner");
        sites.Handle("action:comment/" + target + "/anon", new Dictionary<string, string> { { "comment", "Nice!" }, { "handle", "lurker" } });
        Assert(art.Comments.Last().ReplyTo == first.Id && art.Comments.Last().Author == "lurker", "reply posted under the chosen comment");
        var r = sites.Handle("action:post-thread/agency", new Dictionary<string, string> { { "title", "Mission update" }, { "body", "All good." } });
        Assert(r.Navigate.StartsWith("snackboard.kb/t/"), "new thread opens");
        Assert(!sites.Render(r.Navigate).Html.Contains("{{"), "thread page renders");
        sites.Handle("action:privacy/v1/silent", null);
        Assert(eng.State.IsSilent("v1"), "privacy action");
        Assert(!sites.Handle("nonsense", null).Refresh, "unknown action ignored");
    }

    /// <summary>Rough text metrics: 0.5em per character, greedy word wrap.</summary>
    static void FakeMeasure(TextItem t, float maxW, out float w, out float h)
    {
        float cw = t.FontSize * 0.5f, lineW = 0, maxLine = 0;
        int lines = 1;
        foreach (var para in t.Plain.Split('\n'))
        {
            if (lineW > 0) { lines++; lineW = 0; }
            foreach (var word in para.Split(' '))
            {
                float ww = (word.Length + 1) * cw;
                if (lineW > 0 && lineW + ww > maxW) { lines++; maxLine = Math.Max(maxLine, lineW); lineW = 0; }
                lineW += ww;
            }
        }
        maxLine = Math.Max(maxLine, lineW);
        w = Math.Min(maxW, maxLine);
        h = lines * t.FontSize * t.LineHeight;
    }

    static PageLayout Lay(string html, string css, float width = 800)
    {
        var root = Html.Parse(html);
        Css.Apply(root, Css.Parse(css));
        return Layout.Run(root, width, FakeMeasure);
    }

    public static void TestLayoutBoxes()
    {
        var l = Lay("<body><div class=\"row\"><div class=\"a\">left</div><div class=\"b\">grow</div><div class=\"c\">x</div></div><p>Hello <a href=\"herald.kb\">link</a> there</p><button data-action=\"go\">Go</button></body>",
            "body { font-size: 20px; background: #eeeeee; } .row { display: flex; gap: 10px; padding: 5px; background: #ff0000; } .a { width: 100px; background: #00ff00; } .b { flex: 1; background: #0000ff; } .c { background: #000000; padding: 0 4px; } p { margin-top: 10px; }");
        Eq("#EEEEEEFF", l.Background, "body background is the page");
        var row = l.Rects.First(r => r.Color == "#FF0000FF");
        Eq(800f, row.W, "block fills width");
        var a = l.Rects.First(r => r.Color == "#00FF00FF");
        var b = l.Rects.First(r => r.Color == "#0000FFFF");
        var c = l.Rects.First(r => r.Color == "#000000FF");
        Eq(5f, a.X, "padding");
        Eq(100f, a.W, "fixed width");
        Eq(115f, b.X, "gap");
        Assert(Math.Abs(c.X + c.W - 795) < 0.01f, "last item ends at the padding edge");
        Assert(Math.Abs(b.W - (790 - 100 - 20 - c.W)) < 0.01f, "flex:1 takes the rest");
        Eq(a.H, b.H, "stretch");
        var para = l.Texts.First(t => t.Plain.StartsWith("Hello"));
        Eq("Hello link there", para.Plain, "whitespace collapsed");
        Assert(para.Y >= row.Y + row.H + 10 - 0.01f, "margin below the row");
        var link = para.Runs.First(rn => rn.Text == "link").Link;
        Eq("herald.kb", l.Links[link].Href, "inline link target");
        Assert(para.Build(-1).Contains("<link=\"" + link + "\">"), "rich text link tag");
        var btn = l.Regions.Single();
        Eq("go", l.Links[btn.Link].Action, "button action");
        Assert(btn.W < 100, "button shrinks to fit");
        Eq(btn.Link, l.RegionAt(btn.X + 1, btn.Y + 1), "hit test");

        var wrap = Lay("<body><div class=\"w\"><span class=\"i\">aaaa</span><span class=\"i\">bbbb</span><span class=\"i\">cccc</span></div></body>",
            ".w { display: flex; flex-wrap: wrap; gap: 4px; width: 100px; } .i { width: 40px; background: #123456; }");
        var items = wrap.Rects.Where(r => r.Color == "#123456FF").ToList();
        Eq(3, items.Count, "three items");
        Assert(items[0].Y == items[1].Y && items[2].Y > items[0].Y, "third item wraps");
        var esc = new TextItem { Runs = { new Run { Text = "<b>not bold</b>" } } };
        Assert(!esc.Build().StartsWith("<b>"), "model text can't inject tags");
    }

    public static void TestEverySiteLaysOut()
    {
        var eng = Published(new GlobalSettings { OfflineMode = true }, null);
        var sites = MakeSites(eng);
        var art = eng.State.Articles.First();
        eng.PlayerThread("general", "Hello Kerbin", "First post.", false, "me", false, 0, Day * 3);
        eng.Tick(Day * 3 + 1, 2, true);
        var urls = new List<string> { "kerbalnews.kb", "kerbnet.kb", "tracker.kb", "agency.kb", "settings.kb", "telemetry.kb", "snackboard.kb", "snackboard.kb/b/general", "snackboard.kb/submit", "snackboard.kb?sort=new", Sites.ArticleUrl(art) };
        urls.AddRange(eng.State.Forum.Select(t => "snackboard.kb/t/" + t.Id));
        foreach (var o in Outlets.All) urls.Add(o.Domain);
        foreach (var u in urls)
        {
            var page = sites.Render(u);
            Assert(page.Title != "Not found" && page.Title != "Error" && !page.Html.Contains("{{") && !page.Html.Contains("Missing template"), "renders: " + u + " " + page.Title);
            var root = Html.Parse(page.Html);
            Css.Apply(root, Css.Parse(page.Css));
            var l = Layout.Run(root, 1000, FakeMeasure);
            Assert(l.Height > 100 && l.Texts.Count > 3, "has content: " + u);
            Assert(l.Texts.All(t => t.X >= -0.01f && t.X + t.W <= 1000.01f), "text inside the page: " + u);
            Assert(l.Links.Count > 5, "links: " + u);
        }
        var thread = sites.Render("snackboard.kb/t/" + eng.State.Forum.First().Id);
        var tl = Layout.Run(Lay2(thread), 1000, FakeMeasure);
        Assert(tl.Inputs.Any(i => i.Name == "comment" && i.Multiline) && tl.Inputs.Any(i => i.Name == "handle"), "reply form fields");
    }

    static Node Lay2(PageResult p) { var r = Html.Parse(p.Html); Css.Apply(r, Css.Parse(p.Css)); return r; }
}
