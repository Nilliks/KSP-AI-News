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
        g.Keys["openai"] = "img-key";
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
        sites.Handle("svc-save/openai", new Dictionary<string, string> { { "key-openai", " sk-test \n" } });
        sites.Handle("job-model/writer/gpt-5-mini", null);
        sites.Handle("save-settings", new Dictionary<string, string> { { "maxTokens", "9000" } });
        Assert(g.ProviderId == "openai" && g.Key == "sk-test" && !g.OfflineMode && g.WriterModel == "gpt-5-mini" && g.MaxTokens == 9000 && saves >= 2, "a first key becomes the writer and turns the AI on");
        sites.Handle("action:job-images/gemini", null);
        Assert(g.ImageProviderId == "", "an image service without a key can't be picked");
        sites.Handle("svc-save/gemini", new Dictionary<string, string> { { "key-gemini", "AIza-x" } });
        sites.Handle("action:job-images/gemini", null);
        Assert(g.ImagesOn && g.ImageKey == "AIza-x" && g.Key == "sk-test" && g.ProviderId == "openai", "one Google key, used for pictures; the writer stays put");
        sites.Handle("action:job-images/openai", null);
        Assert(g.ImagesOn && g.ImageKey == "sk-test", "the OpenAI key writes and draws: no second key");
        sites.Handle("svc-save/openai", new Dictionary<string, string> { { "key-openai", "sk two keys" } });
        Assert(g.Key == "sk-test" && sites.Render("settings.kb").Html.Contains("An API key has no spaces in it"), "a paste with spaces is refused and the saved key kept");

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

    public static void TestKerbopediaPagesFromTheGame()
    {
        var g = new GlobalSettings { OfflineMode = true };
        var eng = Published(g, null);
        var w = eng.State.Wiki;
        var craft = eng.WikiGet("Test Rocket v1");
        Assert(craft != null && craft.Kind == "craft" && craft.Flights.Count == 1 && craft.Craft != null, "craft page with its flight and specs");
        Eq("Landed on Mun", craft.Flights[0].Outcome, "flight outcome");
        Assert(craft.Flights[0].Crew.Contains("Jebediah Kerman"), "flight crew");
        Assert(eng.WikiGet("Jebediah Kerman") != null && eng.WikiGet("Mun") != null && w.Values.Any(p => p.Kind == "agency"), "crew, place and agency pages");
        Assert(craft.Refs.Count > 0, "press coverage cited");

        var sites = MakeSites(eng);
        var home = sites.Render("kerbopedia.kb");
        Assert(home.Html.Contains("Welcome to Kerbopedia") && home.Html.Contains("Test Rocket v1") && !home.Html.Contains("{{"), "main page");
        var page = sites.Render(Sites.WikiUrl(craft));
        Assert(page.Html.Contains("Specifications") && page.Html.Contains(">42<") && page.Html.Contains("Landed on Mun") && !page.Html.Contains("{{"), "craft page infobox and flights");
        Assert(page.Html.Contains("href=\"kerbopedia.kb/wiki/jebediah_kerman\""), "crew name links to their page");
        Assert(page.Html.Contains("/article/"), "references link to the articles");
        Assert(page.Html.Contains("stub"), "template text is marked as a stub");
        foreach (var u in new[] { "kerbopedia.kb/wiki/mun", "kerbopedia.kb/wiki/jebediah_kerman", "kerbopedia.kb/list/craft", "kerbopedia.kb/timeline", "kerbopedia.kb/search?q=mun", Sites.WikiUrl(craft) + "/history", "kerbopedia.kb/wiki/nothing_here", "kerbopedia.kb/random" })
        {
            var r = sites.Render(u);
            Assert(!r.Html.Contains("{{") && !r.Title.StartsWith("Error") && r.Html.Contains("KERBOPEDIA"), "renders " + u);
        }
        Assert(sites.Render("kerbopedia.kb/search?q=mun").Html.Contains("Test Rocket v1"), "search finds the craft by what it did");
        Eq("kerbopedia.kb/search?q=jeb", sites.Handle("wiki-search", new Dictionary<string, string> { { "wq", " jeb " } }).Navigate, "search box");

        // The director edits a page through the assistant.
        string res = eng.Execute("edit_wiki", new Dictionary<string, object>
        {
            { "page", "test rocket v1" }, { "lead", "Test Rocket v1 is the agency's first Mun lander." },
            { "sections", new List<object> { new Dictionary<string, object> { { "heading", "Legacy" }, { "text", "It proved the agency could reach the Mun." } } } },
            { "remember", "Always call it the agency's workhorse." }, { "note", "Better opening" },
        }, Day * 3);
        Assert(res.Contains("Edited") && craft.Lead.StartsWith("Test Rocket v1 is the agency's first") && craft.Sections.Any(x => x.Heading == "Legacy") && craft.Notes.Count == 1, "edit_wiki: " + res);
        Assert(craft.History.Last().By.StartsWith("An account at"), "history shows the agency made the edit");
        Assert(sites.Render(Sites.WikiUrl(craft) + "/history").Html.Contains("Better opening"), "history page");
        Assert(eng.WikiLookup("Test Rocket v1").Contains("Legacy") && eng.WikiLookup("").Contains("test_rocket_v1"), "wiki lookup");
        Assert(eng.Execute("edit_wiki", new Dictionary<string, object> { { "page", "Kerbin Space Program history" }, { "lead", "A short history." } }, Day * 3).Contains("Edited") && eng.WikiGet("Kerbin Space Program history").Kind == "topic", "a new topic page");

        // A save from before Kerbopedia fills it in from the news it already has.
        var old = NewsState.Load(CoreSnapshot(eng.State));
        old.Wiki.Clear(); old.WikiSeeded = false;
        var eng2 = new NewsEngine(old, g, null, new SimpleClock(Day));
        eng2.Tick(Day * 3, 1, true);
        Assert(eng2.WikiGet("Test Rocket v1") != null && eng2.WikiGet("Test Rocket v1").Flights.Count == 1 && eng2.WikiGet("Mun") != null, "old saves get their pages");

        var back = NewsState.Load(CoreSnapshot(eng.State));
        var c2 = back.Wiki["test_rocket_v1"];
        Assert(c2.Lead == craft.Lead && c2.Flights.Count == 1 && c2.Flights[0].Outcome == "Landed on Mun" && c2.Craft.Parts == 42 && c2.Notes.Count == 1 && c2.History.Count == craft.History.Count && c2.Refs.Count == craft.Refs.Count, "saved and loaded");
    }

    public static void TestKerbopediaAiWritesAfterTheNewsSettles()
    {
        var t = new CoreTests.FakeTransport();
        var eng = Published(CoreTests.OnlineSettings(), t);
        var craft = eng.WikiGet("Test Rocket v1");
        int before = t.Sent.Count;
        eng.Tick(Day * 3.2, 5, true);
        Assert(!t.Sent.Skip(before).Any(r => UserPrompt(r).Contains("PAGE test_rocket_v1")), "no wiki call while the news is fresh");
        eng.Tick(Day * 4.5, 10, true);
        var req = t.Sent.Skip(before).FirstOrDefault(r => UserPrompt(r).Contains("PAGE test_rocket_v1"));
        Assert(req != null, "the wiki writes once the news settles");
        string up = UserPrompt(req);
        Assert(up.Contains("42 parts") && up.Contains("landed on Mun") && up.Contains("[1]"), "facts, specs and cites in the prompt");
        int idx = t.Sent.IndexOf(req);
        t.Pending[t.Pending.Count - (t.Sent.Count - idx)](Anthropic("{\"pages\":[{\"page\":\"test_rocket_v1\",\"lead\":\"Test Rocket v1 is a crewed Mun lander flown by Jebediah Kerman [1].\",\"sections\":[{\"heading\":\"Flight history\",\"text\":\"It landed on the Mun.\"}],\"change\":\"First full article\"}]}"));
        Assert(craft.WrittenUT > 0 && craft.Sections.Count == 1 && craft.Pending == 0, "the AI text is stored");
        var html = MakeSites(eng).Render(Sites.WikiUrl(craft)).Html;
        Assert(html.Contains("class=\"cite\"") && !html.Contains("stub"), "cite linked, no stub note");
        Assert(craft.History.Any(h => h.Note == "Expanded from a stub into a full article") && craft.History.Count(h => h.Note.StartsWith("Page created")) == 1, "revision noted once, after the creation entry");
        int n = t.Sent.Count;
        eng.Tick(Day * 6, 20, true);
        Assert(!t.Sent.Skip(n).Any(r => UserPrompt(r).Contains("PAGE test_rocket_v1")), "no rewrite without new events");
    }

    /// <summary>A+ shrinks the layout width; a squeezed box once handed TextMeshPro a zero-width text, which froze the game.</summary>
    public static void TestBigZoomNeverZeroWidthText()
    {
        var g = new GlobalSettings { OfflineMode = true };
        var eng = Published(g, null);
        var sites = MakeSites(eng);
        var urls = new List<string> { "kerbalnews.kb", "agency.kb", "engineering.kb", "snackboard.kb", "settings.kb", "live.kb", "tracker.kb", "kerbopedia.kb", "kerbopedia.kb/wiki/test_rocket_v1", Sites.ArticleUrl(eng.State.Articles.First()) };
        float narrowest = float.MaxValue; string where = "";
        foreach (var u in urls)
        {
            var p = sites.Render(u);
            foreach (float vw in new[] { 320f, 640f, 900f })
                foreach (float z in new[] { 1f, 1.5f, 2f, 2.5f })
                {
                    var root = Html.Parse(p.Html);
                    Css.Apply(root, Css.Parse(p.Css));
                    float least = float.MaxValue;
                    var l = Layout.Run(root, vw / z, (TextItem t, float mw, out float w, out float h) => { least = Math.Min(least, mw); FakeMeasure(t, mw, out w, out h); });
                    foreach (var t in l.Texts) if (t.W / t.FontSize < narrowest) { narrowest = t.W / t.FontSize; where = u + " at " + vw + "/" + z; }
                    if (least < 8) { narrowest = 0; where = u + " measured at " + least; }
                }
        }
        Assert(narrowest >= 1.99f, "no text box narrower than two letters at any zoom (narrowest " + narrowest.ToString("0.00") + "em, " + where + ")");
    }

    /// <summary>Rough text metrics: 0.5em per character, greedy word wrap.</summary>
    public static void FakeMeasure(TextItem t, float maxW, out float w, out float h)
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
        var urls = new List<string> { "kerbalnews.kb", "kerbnet.kb", "tracker.kb", "agency.kb", "settings.kb", "telemetry.kb", "pressroom.kb", "gallery.kb", "snackboard.kb", "snackboard.kb/b/general", "snackboard.kb/submit", "snackboard.kb?sort=new", Sites.ArticleUrl(art) };
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

public static class DemoTests
{
    public static void TestDemoNewsBuilds()
    {
        double now;
        var s = DemoNews.Build(new SimpleClock(21600), out now);
        if (s.Articles.Count < 8 || s.Forum.Count < 2 || !s.Articles.Any(a => a.Comments.Any(c => c.Player && c.Agency))) throw new Exception("demo news incomplete");
        if (s.Forum.First(t => t.Kind == "ama").Comments.Count != 4) throw new Exception("AMA questions");
    }
}

public static class PressTests
{
    static void Assert(bool c, string m) { if (!c) throw new Exception("Assertion failed: " + m); }

    public static void TestPressReleaseCoverage()
    {
        var t = new CoreTests.FakeTransport { Responder = CoreTests.AnthropicReply };
        var s = new NewsState();
        var eng = new NewsEngine(s, CoreTests.OnlineSettings(), t, new SimpleClock(21600));
        eng.Tick(0, 0, true);
        eng.TrackLaunch("v1", CoreTests.Craft(), 10);
        eng.Ingest(CoreTests.Ev(EventTypes.Launch, 10));
        eng.Ingest(CoreTests.Ev(EventTypes.Failure, 60));
        eng.Tick(21600 * 3, 1, true);
        int before = t.Sent.Count;
        var rel = eng.PublishRelease("announcement", "Test flight a complete success", "Everything went to plan.", "v1", null, 21600 * 3);
        Assert(rel != null && s.Stories.Any(x => x.Kind == "release" && x.ReleaseId == rel.Id), "release becomes a story");
        eng.Tick(21600 * 3.2, 2, true);
        Assert(t.Sent.Count == before, "press waits before reacting");
        double trust = s.Mood.Trust;
        eng.Tick(21600 * 4, 3, true);
        var body = Json.Str(Json.Arr(Json.Get(Json.Parse(t.Sent.Last().Body), "messages"))[0], "content");
        Assert(body.Contains("Test flight a complete success") && body.Contains("contradicts facts") && body.Contains("press already reported"), "prompt carries the release and the record");
        Assert(s.Articles.Any(a => a.StoryId == rel.StoryId), "outlets covered the release");

        var shot = eng.AddShot("x.jpg", 100, "v1", "Test Rocket v1", "Kerbin", "FLYING");
        Assert(eng.CommentsOf("g:" + shot.Id) == null, "private shots have no comments");
        eng.ReleaseShot(shot.Id, "Liftoff", 21600 * 4);
        Assert(shot.Public && s.CommunityJobs.Any(j => j.Target == "g:" + shot.Id), "released shot draws comments");
        eng.Tick(21600 * 4 + 1, 100, true);
        Assert(UserPrompt(t.Sent.Last()).Contains("public photo gallery"), "photo comment prompt");
        var back = NewsState.Load(Snap(s));
        Assert(back.Releases.Count == 1 && back.Gallery.Count == 1 && back.Gallery[0].Public, "press room and gallery saved");
    }

    public static void TestOfflineReleaseAndPhotoInArticles()
    {
        double now;
        var s = DemoNews.Build(new SimpleClock(21600), out now);
        var rel = s.Releases.Single();
        var cov = s.Articles.Where(a => a.StoryId == rel.StoryId).ToList();
        Assert(cov.Count >= 2 && cov.Any(a => a.Outlet == "inquirer"), "incident statement covered, tabloid included");
        Assert(s.Gallery.Any(g => g.Public && g.Comments.Count > 0), "released photo got comments");
        var sites = new Sites(new NewsEngine(s, new GlobalSettings { OfflineMode = true }, null, new SimpleClock(21600)), rel2 => { var p = System.IO.Path.Combine("GameData/KerbalNews/Sites", rel2); return System.IO.File.Exists(p) ? System.IO.File.ReadAllText(p) : null; });
        sites.Now = () => now;
        foreach (var u in new[] { "pressroom.kb", "gallery.kb", "gallery.kb/p/" + s.Gallery[0].Id, "gallery.kb/p/" + s.Gallery[1].Id })
        {
            var pg = sites.Render(u);
            Assert(pg.Title != "Not found" && !pg.Html.Contains("{{") && !pg.Html.Contains("Missing template"), "renders " + u);
        }
        Assert(sites.Render("pressroom.kb").Html.Contains("Statement on the loss of Big Bertha"), "release listed");
        Assert(sites.Render("gallery.kb/p/" + s.Gallery[0].Id).Html.Contains("kbshot:demo-mun-arrow.jpg"), "photo shown");
        var r = sites.Handle("action:press-type/teaser", null);
        sites.Handle("press-publish", new Dictionary<string, string> { { "title", "Something big is coming" }, { "body", "" } });
        Assert(s.Releases.Count == 2 && s.Releases[1].Type == "teaser", "publish from the page");
    }

    static string UserPrompt(HttpRequestSpec r) { return Json.Str(Json.Arr(Json.Get(Json.Parse(r.Body), "messages"))[0], "content"); }
    static DataNode Snap(NewsState s) { var n = new DataNode("K"); s.Save(n); return n; }
}
