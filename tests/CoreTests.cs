using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using KerbalNews;

/// <summary>
/// Plain test runner (no NUnit in KSP's toolchain). Run: mono tests.exe
/// Every public static void method named Test* is a test.
/// </summary>
public static class CoreTests
{
    const double Day = 21600;

    public static int Main()
    {
        int pass = 0, fail = 0;
        foreach (var m in typeof(CoreTests).GetMethods().Concat(typeof(WebTests).GetMethods()).Concat(typeof(CommunityTests).GetMethods()).Concat(typeof(DemoTests).GetMethods()).Concat(typeof(PressTests).GetMethods()).Concat(typeof(TabloidTests).GetMethods()).Concat(typeof(PressPhotoTests).GetMethods()).Concat(typeof(StreamTests).GetMethods()).Concat(typeof(PollTests).GetMethods()).Concat(typeof(AssistantTests).GetMethods()).Concat(typeof(FeatureTests).GetMethods()).Concat(typeof(MailTests).GetMethods()).Concat(typeof(VarietyTests).GetMethods()).Concat(typeof(VisionTests).GetMethods()).Concat(typeof(AnnouncerTests).GetMethods()).Concat(typeof(EngineeringSheetTests).GetMethods()).Where(x => x.IsStatic && x.Name.StartsWith("Test")).OrderBy(x => x.Name))
        {
            try { m.Invoke(null, null); pass++; Console.WriteLine("PASS " + m.Name); }
            catch (Exception e)
            {
                fail++;
                var inner = e.InnerException ?? e;
                Console.WriteLine("FAIL " + m.Name + ": " + inner.Message);
                Console.WriteLine(inner.StackTrace);
            }
        }
        Console.WriteLine(pass + " passed, " + fail + " failed");
        return fail == 0 ? 0 : 1;
    }

    static void Assert(bool cond, string msg) { if (!cond) throw new Exception("Assertion failed: " + msg); }
    static void Eq<T>(T expected, T actual, string msg) { if (!Equals(expected, actual)) throw new Exception(msg + ": expected <" + expected + "> got <" + actual + ">"); }

    // ---------------------------------------------------------------- fakes

    public class FakeTransport : IHttpTransport
    {
        public List<HttpRequestSpec> Sent = new List<HttpRequestSpec>();
        public List<Action<HttpResult>> Pending = new List<Action<HttpResult>>();
        public Func<HttpRequestSpec, HttpResult> Responder; // null: hold the callback
        public void Send(HttpRequestSpec r, Action<HttpResult> done)
        {
            Sent.Add(r);
            if (Responder != null) done(Responder(r));
            else Pending.Add(done);
        }
    }

    public static GlobalSettings OnlineSettings()
    {
        var g = new GlobalSettings { ProviderId = "anthropic", OfflineMode = false };
        g.Keys["anthropic"] = "test-key";
        return g;
    }

    /// <summary>Makes a valid Anthropic-shaped response that covers every outlet the prompt asks for.</summary>
    public static HttpResult AnthropicReply(HttpRequestSpec req)
    {
        var body = Json.Parse(req.Body);
        var content = Json.Get(Json.Arr(Json.Get(body, "messages"))[0], "content");
        string user = content as string ?? string.Concat((Json.Arr(content) ?? new List<object>()).Select(c => Json.Str(c, "text", "")).ToArray());
        var outlets = new List<object>();
        foreach (var o in Outlets.All)
            if (user.Contains("outlet \"" + o.Id + "\""))
                outlets.Add(new Dictionary<string, object> {
                    { "outlet", o.Id }, { "kind", "news" }, { "headline", o.Name + " covers it" },
                    { "subhead", "A sub" }, { "reporter", "Bobak Kerman" },
                    { "body", "First paragraph with enough words to pass validation.\n\nSecond paragraph." },
                    { "tags", new List<object> { "launch" } },
                    { "comments", new List<object> { new Dictionary<string, object> { { "author", "JebFan4Ever" }, { "text", "GO JEB" }, { "likes", 5.0 } } } },
                });
        var inner = Json.Write(new Dictionary<string, object> {
            { "articles", outlets },
            { "social", new List<object> { new Dictionary<string, object> { { "handle", "skywatcher" }, { "persona", "enthusiast" }, { "text", "wow" }, { "likes", 3.0 }, { "reshares", 1.0 } } } },
        });
        var resp = new Dictionary<string, object> { { "content", new List<object> { new Dictionary<string, object> { { "type", "text" }, { "text", "```json\n" + inner + "\n```" } } } } };
        return new HttpResult { Status = 200, Body = Json.Write(resp) };
    }

    public static NewsEvent Ev(string type, double ut, string vessel = "v1", string body = "Kerbin", params string[] crew)
    {
        var e = new NewsEvent { Type = type, UT = ut, VesselId = vessel, VesselName = "Test Rocket " + vessel, Body = body };
        e.Crew.AddRange(crew);
        return e;
    }

    public static CraftSnapshot Craft()
    {
        return new CraftSnapshot { VesselName = "Test Rocket v1", VesselType = "Ship", Parts = 42, MassTons = 61.3, HeightM = 24, Stages = 3, DeltaV = 4100, Engines = "LV-T45 x1, Swivel x1", CrewCapacity = 1, LaunchSite = "LaunchPad" };
    }

    static DataNode Snapshot(NewsState s) { var n = new DataNode("KERBALNEWS"); s.Save(n); return n; }

    // ---------------------------------------------------------------- json / codec

    public static void TestJsonRoundTrip()
    {
        string src = "{\"a\":[1,2.5,-3e2,true,false,null],\"b\":{\"c\":\"q\\\"uote\\n\\u00e9\"}}";
        var v = Json.Parse(src);
        Eq(-300.0, (double)Json.Arr(Json.Get(v, "a"))[2], "number");
        Eq("q\"uote\né", Json.Path(v, "b.c") as string, "string escapes");
        var again = Json.Parse(Json.Write(v));
        Eq(Json.Write(v), Json.Write(again), "write/parse stable");
        object bad;
        Assert(!Json.TryParse("{\"a\":", out bad), "truncated JSON rejected");
    }

    public static void TestTextCodec()
    {
        foreach (var s in new[] { "", "plain", "multi\nline { braces } // comment = equals", "Ünïcödé ✓", new string('x', 5000) + "\n" })
        {
            var enc = TextCodec.Encode(s);
            Assert(!enc.Contains("\n") && !enc.Contains("{") && !enc.Contains("//"), "encoded is ConfigNode-safe");
            Eq(s, TextCodec.Decode(enc), "codec round trip");
        }
        Assert(TextCodec.Encode(new string('y', 5000)).StartsWith("uz:"), "long text is gzipped");
        // KSP's ConfigNode drops everything after "//" in a value, so stored text must never contain a slash.
        var rnd = new Random(7);
        for (int i = 0; i < 300; i++)
        {
            var b = new byte[rnd.Next(1, 900)];
            rnd.NextBytes(b);
            string text = Convert.ToBase64String(b) + " ?>?>?> \u00ff\u00fe";
            string enc = TextCodec.Encode(text);
            Assert(!enc.Contains("/"), "no slash in stored text");
            Eq(text, TextCodec.Decode(enc), "round trip " + i);
        }
        // Old saves: values cut at "//" by KSP still show what they can, never base64.
        string longText = string.Join(" ", Enumerable.Range(0, 200).Select(i => "Sentence number " + i + " about the Mun landing.").ToArray());
        string oldGz;
        using (var ms = new MemoryStream())
        {
            using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Compress, true)) { var raw = System.Text.Encoding.UTF8.GetBytes(longText); gz.Write(raw, 0, raw.Length); }
            oldGz = "gz:" + Convert.ToBase64String(ms.ToArray());
        }
        Eq(longText, TextCodec.Decode(oldGz), "old gz values still load");
        string cut = TextCodec.Decode(oldGz.Substring(0, oldGz.Length * 2 / 3 + 1));
        Assert(cut.StartsWith("Sentence number 0") && cut.EndsWith("[...]") && !cut.Contains("H4sI"), "a cut value keeps its readable start: " + cut.Substring(Math.Max(0, cut.Length - 60)));
        Eq(TextCodec.Damaged, TextCodec.Decode("gz:H4sIAAAAAAAAC21W"), "an unreadable value says so instead of showing base64");
        Eq("hi", TextCodec.Decode("b64:aGk="), "old b64 values still load");
        Assert(TextCodec.Encode(new string('y', 5000)).Length < 500, "gzip shrinks repetitive text");
    }

    // ---------------------------------------------------------------- persistence

    public static void TestStateRoundTrip()
    {
        var s = new NewsState { Silliness = 4 };
        s.Profile.Name = "Kerbal Orbital Agency"; s.Profile.ShortName = "KOA"; s.Profile.HouseRules = "Jeb is a national hero.\nNever mention Earth.";
        s.Lore.Add(new LoreEntry { UT = 100, Text = "Year 2: the Mun race begins" });
        s.Mood.Trust = 33; s.Mood.AddConcern("debris", 40);
        s.Tracked["123"] = new TrackedVessel { VesselId = "123", LaunchUT = 5, Craft = Craft() };
        s.Tracked["123"].History.Add("Year 1, Day 1: launch");
        var story = new Story { Id = "s1", VesselId = "123", CreatedUT = 5, PublishUT = 5 + Day, Craft = Craft() };
        story.Events.Add(Ev(EventTypes.Launch, 5, "123", "Kerbin", "Jebediah Kerman"));
        s.Stories.Add(story);
        var a = new Article { Id = "a1", StoryId = "s1", Outlet = "herald", Headline = "Hello {world}", Body = "Para 1\n\nPara 2 // not a comment", PublishedUT = 99 };
        a.Tags.Add("launch"); a.Comments.Add(new Comment { Author = "TaxpayerTed", Text = "Cost?", Likes = 3 });
        s.Articles.Add(a);
        s.Social.Add(new SocialPost { StoryId = "s1", Handle = "@x", Text = "hi", UT = 7 });
        s.Novelty["launch:crewed"] = 2;

        var back = NewsState.Load(Snapshot(s));
        Eq(4, back.Silliness, "silliness");
        Eq("KOA", back.Profile.ShortName, "profile");
        Eq(s.Profile.HouseRules, back.Profile.HouseRules, "multiline rules");
        Eq(1, back.Lore.Count, "lore");
        Eq(33.0, back.Mood.Trust, "trust");
        Eq(40.0, back.Mood.Concerns["debris"], "concern");
        Eq(42, back.Tracked["123"].Craft.Parts, "tracked craft");
        Eq("Jebediah Kerman", back.Stories[0].Events[0].Crew[0], "event crew");
        Eq("Para 1\n\nPara 2 // not a comment", back.Articles[0].Body, "article body");
        Eq("Hello {world}", back.Articles[0].Headline, "headline braces");
        Eq(3, back.Articles[0].Comments[0].Likes, "comment");
        Eq(1, back.Social.Count, "social");
        Eq(2.0, back.Novelty["launch:crewed"], "novelty");
        Eq(s.NextId, back.NextId, "id counter");
    }

    // ---------------------------------------------------------------- significance

    public static void TestReactionScalesWithEvent()
    {
        var accepted = new Story { Id = "x" };
        accepted.Events.Add(new NewsEvent { Type = EventTypes.ContractAccepted, Detail = "Put a satellite in orbit", Significance = Significance.Score(new NewsEvent { Type = EventTypes.ContractAccepted }, 0) });
        accepted.Significance = Significance.StoryScore(accepted);
        Assert(CoveragePlan.For(accepted).IsEmpty, "accepting a contract gets no coverage (" + accepted.Significance + ")");

        var duna = new Story { Id = "d" };
        var soi = Ev(EventTypes.Soi, 0, "v", "Duna");
        soi.Significance = Significance.Score(soi, 0);
        duna.Events.Add(soi);
        duna.Significance = Significance.StoryScore(duna);
        var dplan = CoveragePlan.For(duna);
        Assert(dplan.Outlets.Count >= 3, "first Duna arrival gets wide coverage (" + duna.Significance + ", " + dplan.Tier + ")");
        Assert(dplan.Outlets.Any(o => o.OutletId == "science"), "scientists weigh in on Duna");

        var launch = new Story { Id = "l" };
        var le = Ev(EventTypes.Launch, 0);
        le.Significance = Significance.Score(le, 0);
        launch.Events.Add(le);
        launch.Significance = Significance.StoryScore(launch);
        var lplan = CoveragePlan.For(launch);
        Eq("brief", lplan.Tier, "uncrewed launch is a brief");
        Eq(1, lplan.Outlets.Count, "brief goes to one outlet");
        Assert(dplan.SocialPosts > lplan.SocialPosts, "bigger news, more social chatter");

        double first = Significance.Score(Ev(EventTypes.Orbit, 0, "v", "Mun"), 0);
        double fifth = Significance.Score(Ev(EventTypes.Orbit, 0, "v", "Mun"), 4);
        Assert(fifth < first * 0.5, "novelty fades with repetition");

        var death = new Story { Id = "k" };
        var ke = Ev(EventTypes.CrewKilled, 0, "v", "Kerbin", "Bill Kerman");
        ke.Significance = Significance.Score(ke, 5);
        death.Events.Add(ke);
        death.Significance = Significance.StoryScore(death);
        var kplan = CoveragePlan.For(death);
        Eq("historic", kplan.Tier, "crew loss is always historic");
        Assert(kplan.Outlets.Any(o => o.OutletId == "council"), "government statement on loss of crew");
        Assert(!kplan.Outlets.Any(o => o.OutletId == "munmonthly") || kplan.Outlets.Count > 3, "plan sane");
    }

    public static void TestMoodReactsToOutcomes()
    {
        var m = new Mood();
        double trust0 = m.Trust;
        var fail = new Story { Id = "f", Significance = 50 };
        fail.Events.Add(Ev(EventTypes.Failure, 0));
        m.ApplyStory(fail, 0);
        double after1 = m.Trust;
        m.ApplyStory(fail, 0);
        Assert(trust0 - after1 < after1 - m.Trust, "second failure in a row hurts more");
        var killed = new Story { Id = "k", Significance = 92 };
        killed.Events.Add(Ev(EventTypes.CrewKilled, 0, "v", "Kerbin", "Bob Kerman"));
        double before = m.Trust;
        m.ApplyStory(killed, 0);
        Assert(before - m.Trust >= 15, "crew death drops trust hard");
        Assert(m.Concerns["crew safety"] > 30, "safety concern");
        double e0 = m.Excitement;
        var rescue = new Story { Id = "r", Significance = 70 };
        rescue.Events.Add(Ev(EventTypes.Failure, 0, "v", "Kerbin", "Val Kerman"));
        rescue.Events.Add(Ev(EventTypes.Splashdown, 1, "v", "Kerbin", "Val Kerman"));
        m.ApplyStory(rescue, 0);
        Assert(m.Excitement > e0 + 10, "successful failure boosts excitement");
        m.Decay(100);
        Assert(Math.Abs(m.Excitement - 35) < 5, "excitement fades toward resting level");
    }

    // ---------------------------------------------------------------- engine

    static NewsEngine Engine(NewsState s, FakeTransport t, GlobalSettings g = null)
    {
        return new NewsEngine(s, g ?? OnlineSettings(), t, new SimpleClock(Day));
    }

    public static void TestLaunchToHeadline()
    {
        var s = new NewsState();
        var t = new FakeTransport { Responder = AnthropicReply };
        var eng = Engine(s, t);
        var published = new List<Article>();
        eng.ArticlesPublished += l => published.AddRange(l);
        eng.Tick(0, 0, true);
        eng.TrackLaunch("v1", Craft(), 10);
        eng.Ingest(Ev(EventTypes.Launch, 10, "v1", "Kerbin", "Jebediah Kerman"));
        eng.Ingest(Ev(EventTypes.Orbit, 400, "v1", "Kerbin", "Jebediah Kerman"));
        Eq(1, s.Stories.Count, "launch and orbit grouped into one story");
        eng.Tick(Day * 0.5, 1, true);
        Eq(0, t.Sent.Count, "nothing written before a day has passed");
        eng.Tick(10 + Day + 1, 2, true);
        Eq(1, t.Sent.Count, "one AI call for the story");
        Assert(t.Sent[0].Body.Contains("cache_control"), "system prompt cached");
        Assert(t.Sent[0].Body.Contains("Jebediah Kerman") && t.Sent[0].Body.Contains("42 parts"), "fact sheet has crew and specs");
        Assert(s.Articles.Count >= 1, "articles saved");
        Assert(s.Social.Count >= 1, "social posts saved");
        Assert(s.Articles.All(a => a.PublishedUT >= 10 + Day * (a.Outlet == "kraken" ? 0.5 : 0.9)), "published about a day after (the rumor site a little sooner)");
        eng.Tick(10 + Day * 3, 3, true);
        Eq(s.Articles.Count, published.Count, "visibility event fired for each article");
        Eq(StoryStatus.Published, s.Stories[0].Status, "story published");
        Assert(s.Tracked["v1"].History.Count == 2, "vessel history remembered");
    }

    public static void TestRollbackForgetsUnsavedNews()
    {
        var s = new NewsState();
        var t = new FakeTransport { Responder = AnthropicReply };
        var eng = Engine(s, t);
        eng.Tick(0, 0, true);
        var quicksave = Snapshot(s);                       // saved before launch
        eng.TrackLaunch("v1", Craft(), 10);
        eng.Ingest(Ev(EventTypes.Launch, 10, "v1", "Kerbin", "Jebediah Kerman"));
        var afterLaunch = Snapshot(s);                     // saved with a pending story
        eng.Tick(10 + Day * 2, 1, true);
        Assert(s.Articles.Count > 0, "news written");

        var restored = NewsState.Load(quicksave);
        Eq(0, restored.Articles.Count + restored.Stories.Count, "loading the pre-launch save forgets the launch");
        Assert(!restored.Tracked.ContainsKey("v1"), "and the vessel");

        var crashed = NewsState.Load(afterLaunch);
        Eq(0, crashed.Articles.Count, "crash before save: no article yet");
        Eq(StoryStatus.Pending, crashed.Stories[0].Status, "story still pending");
        var eng2 = Engine(crashed, t);
        eng2.Tick(10 + Day * 2, 100, true);
        Assert(crashed.Articles.Count > 0, "pending story regenerates after reload");
    }

    public static void TestStaleResponseAfterReloadIsDropped()
    {
        var s = new NewsState();
        var t = new FakeTransport(); // hold callbacks
        var eng = Engine(s, t);
        eng.Tick(0, 0, true);
        eng.TrackLaunch("v1", Craft(), 10);
        eng.Ingest(Ev(EventTypes.Launch, 10, "v1", "Kerbin", "Jebediah Kerman"));
        var save = Snapshot(s);
        eng.Tick(10 + Day * 2, 1, true);
        Eq(1, t.Pending.Count, "request in flight");
        var reloaded = NewsState.Load(save);
        var eng2 = Engine(reloaded, new FakeTransport { Responder = r => new HttpResult { Status = 500, Body = "{}" } });
        t.Pending[0](AnthropicReply(t.Sent[0]));             // old response arrives late
        Eq(0, reloaded.Articles.Count, "late response does not leak into the reloaded save");
    }

    public static void TestRetryThenOfflineFallback()
    {
        var s = new NewsState();
        int calls = 0;
        var t = new FakeTransport { Responder = r => { calls++; return new HttpResult { Status = 529, Body = "{\"error\":{\"message\":\"overloaded\"}}" }; } };
        var eng = Engine(s, t);
        eng.Tick(0, 0, true);
        eng.Ingest(Ev(EventTypes.Landing, 10, "v1", "Mun", "Jebediah Kerman"));
        eng.Tick(Day * 3, 1, true);
        Eq(1, calls, "first attempt");
        Assert(eng.LastError.Contains("overloaded"), "error surfaced: " + eng.LastError);
        eng.Tick(Day * 3, 5, true);
        Eq(1, calls, "backs off before retrying");
        eng.Tick(Day * 3, 100, true);
        eng.Tick(Day * 3, 1000, true);
        Eq(3, calls, "three attempts");
        Assert(s.Articles.Count > 0 && s.Articles.All(a => a.Offline), "falls back to offline templates");
    }

    public static void TestBadKeyGoesOfflineImmediately()
    {
        var s = new NewsState();
        var t = new FakeTransport { Responder = r => new HttpResult { Status = 401, Body = "{\"error\":{\"message\":\"invalid x-api-key\"}}" } };
        var eng = Engine(s, t);
        eng.Tick(0, 0, true);
        eng.Ingest(Ev(EventTypes.Launch, 10, "v1", "Kerbin", "Jebediah Kerman"));
        eng.Tick(Day * 3, 1, true);
        Eq(1, t.Sent.Count, "one call");
        Assert(s.Articles.Count > 0 && s.Articles[0].Offline, "offline article after 401");
    }

    public static void TestNoCallsDuringWarp()
    {
        var s = new NewsState();
        var t = new FakeTransport { Responder = AnthropicReply };
        var eng = Engine(s, t);
        eng.Tick(0, 0, true);
        eng.Ingest(Ev(EventTypes.Launch, 10, "v1", "Kerbin", "Jebediah Kerman"));
        eng.Tick(Day * 3, 1, false);
        Eq(0, t.Sent.Count, "warp: no calls");
        eng.Tick(Day * 3, 2, true);
        Eq(0, t.Sent.Count, "warp just ended: waits a few seconds in case of another warp");
        eng.Tick(Day * 3, 2 + NewsEngine.WarpSettleSeconds + 1, true);
        Eq(1, t.Sent.Count, "warp ended: call made");
    }

    public static void TestOfflineModeWritesWithoutNetwork()
    {
        var s = new NewsState();
        var t = new FakeTransport { Responder = AnthropicReply };
        var eng = Engine(s, t, new GlobalSettings { OfflineMode = true });
        eng.Tick(0, 0, true);
        eng.TrackLaunch("v1", Craft(), 10);
        eng.Ingest(Ev(EventTypes.Failure, 10, "v1", "Kerbin", "Jebediah Kerman"));
        eng.Ingest(Ev(EventTypes.CrewKilled, 11, "v1", "Kerbin", "Jebediah Kerman"));
        eng.Tick(Day * 3, 1, true);
        Eq(0, t.Sent.Count, "no network in offline mode");
        Assert(s.Articles.Count >= 4, "crew loss gets wide coverage offline too (" + s.Articles.Count + ")");
        Assert(s.Articles.Any(a => a.Outlet == "council"), "official statement");
        Assert(s.Articles.All(a => a.Tags.Contains("crew loss")), "tagged for memory framing");
        Assert(s.Mood.Trust < 50, "trust fell");
    }

    public static void TestOfflinePostsNeverLeaveABlankCraftName()
    {
        var s = new NewsState();
        var eng = Engine(s, new FakeTransport { Responder = AnthropicReply }, new GlobalSettings { OfflineMode = true });
        eng.Tick(0, 0, true);
        var c = Craft(); c.VesselName = "";
        eng.TrackLaunch("v1", c, 10);
        foreach (var t in new[] { EventTypes.Launch, EventTypes.Failure, EventTypes.CrewKilled })
        {
            var e = Ev(t, 10, "v1", "Kerbin", "Jebediah Kerman"); e.VesselName = "";
            eng.Ingest(e);
        }
        eng.Tick(Day * 3, 1, true);
        Assert(s.Social.Count > 0, "still posts something");
        foreach (var p in s.Social)
            Assert(!p.Text.Contains(" ?") && !p.Text.StartsWith(" ") && !p.Text.Contains("  ") && !p.Text.Contains("the  "), "no blank name: '" + p.Text + "'");
        foreach (var f in s.Forum)
            Assert(!f.Title.StartsWith(" ") && !f.Title.Contains(" ?") && !f.Title.Contains(": "), "forum title reads: '" + f.Title + "'");
    }

    public static void TestSmallEventsStayQuiet()
    {
        var s = new NewsState();
        var t = new FakeTransport { Responder = AnthropicReply };
        var eng = Engine(s, t);
        eng.Tick(0, 0, true);
        eng.Ingest(new NewsEvent { Type = EventTypes.ContractAccepted, UT = 10, Detail = "Launch a satellite" });
        eng.Tick(Day * 2, 1, true);
        Eq(0, t.Sent.Count, "no AI call for an accepted contract");
        Eq(0, s.Articles.Count, "no article");
    }

    public static void TestQuietPeriodFiller()
    {
        var s = new NewsState();
        var t = new FakeTransport { Responder = AnthropicReply };
        var eng = Engine(s, t);
        eng.Digest = () => new WorldDigest { Satellites = 3, Debris = 12 };
        eng.Tick(0, 0, true);
        eng.Tick(Day * 2, 1, true);
        Eq(0, t.Sent.Count, "not quiet long enough");
        eng.Tick(Day * 3.5, 2, true); // queues filler
        eng.Tick(Day * 3.5, 3, true); // writes it
        Eq(1, t.Sent.Count, "one filler call");
        Assert(t.Sent[0].Body.Contains("feature or opinion piece"), "filler prompt");
        eng.Tick(Day * 3.8, 4, true);
        eng.Tick(Day * 3.9, 5, true);
        Eq(1, t.Sent.Count, "filler is rate limited");
        eng.Tick(Day * 5, 6, true);
        eng.Tick(Day * 5, 7, true);
        Eq(1, t.Sent.Count, "not every day in a long lull");
        eng.Tick(Day * 5.6, 8, true);
        eng.Tick(Day * 5.6, 9, true);
        Eq(2, t.Sent.Count, "next filler after two days");
    }

    // ---------------------------------------------------------------- providers

    public static void TestProviderRequestsAndResponses()
    {
        var oa = Providers.Build(Providers.Get("openai"), null, "k", "gpt-x", "sys", "usr", 1000, true);
        Eq("https://api.openai.com/v1/chat/completions", oa.Url, "openai url");
        Eq("Bearer k", oa.Headers["Authorization"], "bearer");
        Assert(oa.Body.Contains("max_completion_tokens") && oa.Body.Contains("json_object"), "openai body");
        var ds = Providers.Build(Providers.Get("deepseek"), null, "k", "deepseek-chat", "sys", "usr", 1000, true);
        Assert(ds.Body.Contains("\"max_tokens\""), "compatible providers use max_tokens");
        var ge = Providers.Build(Providers.Get("gemini"), null, "k", "gemini-2.5-flash", "sys", "usr", 1000, true);
        Eq("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent", ge.Url, "gemini url");
        Eq("k", ge.Headers["x-goog-api-key"], "gemini key header");
        var lo = Providers.Build(Providers.Get("lmstudio"), "http://127.0.0.1:1234/v1/", "", "llama", "sys", "usr", 1000, false);
        Eq("http://127.0.0.1:1234/v1/chat/completions", lo.Url, "base url override");
        Assert(!lo.Headers.ContainsKey("Authorization"), "no auth header without key");

        string text, err;
        Assert(Providers.TryExtractText(ApiKind.OpenAICompatible, new HttpResult { Status = 200, Body = "{\"choices\":[{\"message\":{\"content\":\"hi\"}}]}" }, out text, out err) && text == "hi", "openai parse");
        Assert(Providers.TryExtractText(ApiKind.Gemini, new HttpResult { Status = 200, Body = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"a\"},{\"text\":\"b\"}]}}]}" }, out text, out err) && text == "ab", "gemini parse");
        Assert(Providers.TryExtractText(ApiKind.Anthropic, new HttpResult { Status = 200, Body = "{\"content\":[{\"type\":\"text\",\"text\":\"c\"}]}" }, out text, out err) && text == "c", "anthropic parse");
        Assert(!Providers.TryExtractText(ApiKind.Anthropic, new HttpResult { Status = 401, Body = "{\"error\":{\"message\":\"bad key\"}}" }, out text, out err) && err.Contains("bad key") && err.Contains("401"), "error message");
        Assert(!Providers.TryExtractText(ApiKind.Gemini, new HttpResult { Error = "timeout" }, out text, out err) && err.Contains("timeout"), "network error");
    }

    public static void TestResponseValidation()
    {
        var plan = new CoveragePlan { Tier = "story" };
        plan.Outlets.Add(new OutletAssignment { OutletId = "herald", Kind = "news" });
        GeneratedCoverage cov; string err;
        string good = "Here you go:\n```json\n{\"articles\":[{\"outlet\":\"herald\",\"headline\":\"Rocket flies\",\"body\":\"A long enough body for the validator to accept it.\"},{\"outlet\":\"fakenews\",\"headline\":\"x\",\"body\":\"y\"}]}\n```";
        Assert(ResponseParser.TryParse(good, plan, out cov, out err), "fenced JSON accepted: " + err);
        Eq(1, cov.Articles.Count, "unknown outlet dropped");
        string meta = "{\"articles\":[{\"outlet\":\"herald\",\"headline\":\"Rocket flies\",\"body\":\"The player reverted the flight after loading a quicksave.\"}]}";
        Assert(!ResponseParser.TryParse(meta, plan, out cov, out err), "fourth-wall breaks rejected");
        Assert(!ResponseParser.TryParse("Sorry, I can't help", plan, out cov, out err), "non-JSON rejected");
    }

    public static void TestSettingsRoundTripKeepsKeysOutOfSettingsFile()
    {
        var g = OnlineSettings();
        g.WriterModels["anthropic"] = "my-model";
        var sn = new DataNode("SETTINGS"); var kn = new DataNode("SECRETS");
        g.Save(sn, kn);
        Assert(!Flatten(sn).Contains("test-key"), "key not in settings file");
        var back = GlobalSettings.Load(sn, kn);
        Eq("test-key", back.Key, "key restored");
        Eq("my-model", back.WriterModel, "model override");
        Eq(Providers.Get("anthropic").CheapModel, back.CheapModel, "default cheap model");
        Assert(back.CanCallAi, "can call");
    }

    static string Flatten(DataNode n)
    {
        return string.Join(";", n.Values.Select(v => v.Key + "=" + TextCodec.Decode(v.Value)).ToArray()) + string.Join("", n.Nodes.Select(Flatten).ToArray());
    }

    public static void TestSavePruning()
    {
        var s = new NewsState();
        for (int i = 0; i < 800; i++)
        {
            var a = new Article { Id = "a" + i, StoryId = "s", PublishedUT = i, Significance = i % 100 };
            for (int c = 0; c < 100; c++) a.Comments.Add(new Comment { Text = "c" });
            s.Articles.Add(a);
        }
        s.Prune();
        Eq(NewsState.MaxArticles, s.Articles.Count, "article cap");
        Assert(s.Articles.All(a => a.Comments.Count <= NewsState.MaxCommentsKept), "comment cap");
        Assert(s.Articles.Any(a => a.PublishedUT == 799), "newest kept");
    }

    // ---------------------------------------------------------------- deepseek, compression, rewrite

    static HttpResult OpenAiReply(string content)
    {
        var resp = new Dictionary<string, object> { { "choices", new List<object> { new Dictionary<string, object> { { "message", new Dictionary<string, object> { { "content", content } } } } } } };
        return new HttpResult { Status = 200, Body = Json.Write(resp) };
    }

    public static void TestDeepSeekIsDefaultAndWorks()
    {
        var g = new GlobalSettings { OfflineMode = false };
        Eq("deepseek", g.ProviderId, "default provider");
        g.Keys["deepseek"] = "sk-ds";
        var s = new NewsState();
        var t = new FakeTransport { Responder = r =>
        {
            // Reuse the Anthropic fake's article builder by re-wrapping its text in OpenAI format.
            var body = Json.Parse(r.Body);
            string user = Json.Str(Json.Arr(Json.Get(body, "messages"))[1], "content");
            var fake = new HttpRequestSpec { Body = Json.Write(new Dictionary<string, object> { { "messages", new List<object> { new Dictionary<string, object> { { "role", "user" }, { "content", user } } } } }) };
            string text = Json.Str(Json.Arr(Json.Get(Json.Parse(AnthropicReply(fake).Body), "content"))[0], "text");
            return OpenAiReply(text);
        } };
        var eng = Engine(s, t, g);
        eng.Tick(0, 0, true);
        eng.Ingest(Ev(EventTypes.Landing, 10, "v1", "Mun", "Jebediah Kerman"));
        eng.Tick(Day * 3, 1, true);
        Eq("https://api.deepseek.com/chat/completions", t.Sent[0].Url, "deepseek endpoint");
        Eq("Bearer sk-ds", t.Sent[0].Headers["Authorization"], "deepseek key");
        Assert(t.Sent[0].Body.Contains("json_object") && t.Sent[0].Body.Contains("deepseek-chat"), "json mode and model");
        Assert(s.Articles.Count >= 2 && s.Articles.All(a => !a.Offline), "deepseek articles parsed");
        Assert(s.Articles[0].Model.StartsWith("deepseek / "), "model recorded for comparison: " + s.Articles[0].Model);
    }

    public static void TestArchiveCompression()
    {
        var s = new NewsState();
        for (int i = 0; i < 30; i++)
            s.Articles.Add(new Article { Id = "a" + i, StoryId = "s" + i, Outlet = "herald", Headline = "Headline " + i, PublishedUT = 100 + i * Day, Significance = 30 });
        s.Lore.Add(new LoreEntry { UT = 50, Text = "The Duna Union announced its own space program." });
        double now = 100 + 40 * Day;
        Assert(Compressor.MemoryNeedsCompression(s, now), "30 stories exceed the budget");
        string sentUser = null;
        var t = new FakeTransport { Responder = r => { sentUser = r.Body; return OpenAiReply("{\"summary\":\"Thirty launches so far. The Duna Union has a rival program.\"}"); } };
        var g = new GlobalSettings { OfflineMode = false }; g.Keys["deepseek"] = "k";
        var eng = Engine(s, t, g);
        eng.Tick(now, 0, true);
        Assert(sentUser.Contains("archivist") && sentUser.Contains("Duna Union") && sentUser.Contains("Headline 0"), "old items sent for summary");
        Assert(sentUser.Contains("deepseek-chat"), "cheap model used");
        Assert(s.MemorySummary.Contains("rival program"), "summary stored");
        Eq(Compressor.KeepRecentStories, Compressor.FreshStories(s, now).Count, "recent stories kept verbatim");
        Assert(!Compressor.MemoryNeedsCompression(s, now), "within budget now");
        var back = NewsState.Load(Snapshot(s));
        Eq(s.MemorySummary, back.MemorySummary, "summary saved");
        Eq(s.MemoryUpToUT, back.MemoryUpToUT, "cut point saved");
        var user = PromptBuilder.User(new Story { Id = "x", Kind = "filler", Topic = "t" }, CoveragePlan.ForFiller("herald"), s, null, new SimpleClock(), now);
        Assert(user.Contains("ARCHIVE SUMMARY") && !user.Contains("Duna Union announced"), "prompt uses summary instead of raw lore");
    }

    public static void TestOfflineCompressionAndProfileSummary()
    {
        var s = new NewsState();
        for (int i = 0; i < 300; i++)
            s.Articles.Add(new Article { Id = "a" + i, StoryId = "s" + i, Outlet = "herald", Headline = "Headline number " + i + " " + new string('x', 40), PublishedUT = 100 + i * Day, Significance = 30 });
        var eng = Engine(s, new FakeTransport(), new GlobalSettings { OfflineMode = true });
        eng.Tick(100 + 400 * Day, 0, true);
        Assert(s.MemorySummary.Length > 0 && s.MemorySummary.Length <= Compressor.SummaryMaxChars, "offline summary trimmed: " + s.MemorySummary.Length);
        Assert(s.MemorySummary.Contains("Headline number 294"), "keeps the newest of the folded items");

        var s2 = new NewsState();
        s2.Profile.WorldLore = new string('w', 7000);
        s2.Profile.HouseRules = "Never mention Earth.";
        var t = new FakeTransport { Responder = r => OpenAiReply("{\"summary\":\"Kerbin has three nations and a long history.\"}") };
        var g = new GlobalSettings { OfflineMode = false }; g.Keys["deepseek"] = "k";
        var eng2 = Engine(s2, t, g);
        eng2.Tick(0, 0, true);
        Eq(1, t.Sent.Count, "profile summarized once");
        string sys = PromptBuilder.System(s2);
        Assert(sys.Contains("three nations") && sys.Contains("Never mention Earth.") && !sys.Contains("wwwww"), "summary replaces long lore, house rules kept verbatim");
        eng2.Tick(10, 1, true);
        Eq(1, t.Sent.Count, "cached until edited");
        s2.Profile.WorldLore += " more";
        eng2.Tick(20, 2, true);
        Eq(2, t.Sent.Count, "re-summarized after an edit");
    }

    public static void TestRewriteForModelComparison()
    {
        var s = new NewsState();
        var t = new FakeTransport { Responder = AnthropicReply };
        var eng = Engine(s, t);
        eng.Tick(0, 0, true);
        eng.Ingest(Ev(EventTypes.Landing, 10, "v1", "Mun", "Jebediah Kerman"));
        eng.Tick(Day * 3, 1, true);
        int n = s.Articles.Count;
        double trust = s.Mood.Trust, exc = s.Mood.Excitement;
        Assert(eng.Rewrite(s.Stories[0].Id, Day * 3), "rewrite accepted");
        Eq(0, s.Articles.Count, "old coverage removed");
        eng.Tick(Day * 3, 2, true);
        Eq(n, s.Articles.Count, "coverage rewritten");
        Eq(2, t.Sent.Count, "second call");
        Eq(exc, s.Mood.Excitement, "mood not applied twice");
        Eq(2, s.Tracked.Count == 0 ? 2 : s.Tracked["v1"].History.Count, "history not duplicated");
    }

    public static void TestInFlightSurvivesSceneChangeButNotRevert()
    {
        var s = new NewsState();
        var t = new FakeTransport();
        var eng = Engine(s, t);
        eng.Tick(0, 0, true);
        var beforeLaunch = Snapshot(s);
        eng.TrackLaunch("v1", Craft(), 10);
        eng.Ingest(Ev(EventTypes.Launch, 10, "v1", "Kerbin", "Jebediah Kerman"));
        eng.Tick(Day * 2, 1, true);
        Eq(1, t.Pending.Count, "in flight");
        // Scene change: the same save is loaded into a new state object while the request is out.
        var sameSave = NewsState.Load(Snapshot(s));
        eng.SetState(sameSave);
        eng.Tick(Day * 2, 2, true);
        Eq(1, t.Sent.Count, "no duplicate request while one is in flight");
        t.Pending[0](AnthropicReply(t.Sent[0]));
        Assert(sameSave.Articles.Count > 0, "response applied to the reloaded copy of the story");

        // Revert: the story does not exist in the restored state, so a late reply is dropped.
        var s2 = new NewsState();
        var t2 = new FakeTransport();
        var eng2 = Engine(s2, t2);
        eng2.Tick(0, 0, true);
        var pre = Snapshot(s2);
        eng2.TrackLaunch("v1", Craft(), 10);
        eng2.Ingest(Ev(EventTypes.Launch, 10, "v1", "Kerbin", "Jebediah Kerman"));
        eng2.Tick(Day * 2, 1, true);
        var reverted = NewsState.Load(pre);
        eng2.SetState(reverted);
        t2.Pending[0](AnthropicReply(t2.Sent[0]));
        Eq(0, reverted.Articles.Count, "reverted flight never makes the news");
    }

    public static void TestSkipDelayForTesting()
    {
        var s = new NewsState();
        var t = new FakeTransport { Responder = AnthropicReply };
        var eng = Engine(s, t);
        eng.Tick(0, 0, true);
        eng.Ingest(Ev(EventTypes.Landing, 100, "v1", "Duna", "Jebediah Kerman"));
        Eq(1, eng.SkipDelay(200), "one story moved");
        eng.Tick(200, 0.5, true);
        Assert(s.Articles.Count >= 3, "multi-outlet story");
        eng.Tick(200, 1, true);
        Assert(s.Articles.Count > 0 && s.Articles.All(a => a.PublishedUT <= 200), "every outlet published right away");
    }

    public static void TestFirstLaunchToOrbitIsBigButNotHistoric()
    {
        var s = new NewsState();
        var eng = Engine(s, new FakeTransport(), new GlobalSettings { OfflineMode = true });
        eng.Tick(0, 0, true);
        eng.TrackLaunch("v1", Craft(), 10);
        eng.Ingest(Ev(EventTypes.Launch, 10, "v1", "Kerbin", "Jebediah Kerman"));
        eng.Ingest(new NewsEvent { Type = EventTypes.Milestone, UT = 11, VesselId = "v1", VesselName = "Test Rocket v1", Body = "Kerbin", Detail = "first launch", Crew = { "Jebediah Kerman" } });
        eng.Ingest(new NewsEvent { Type = EventTypes.Milestone, UT = 60, VesselId = "v1", VesselName = "Test Rocket v1", Body = "Kerbin", Detail = "first flight to reach space", Crew = { "Jebediah Kerman" } });
        var story = s.Stories[0];
        var plan = CoveragePlan.For(story);
        Assert(plan.Tier == "story" || plan.Tier == "major", "first suborbital crewed flight is a solid story, got " + plan.Tier + " (" + story.Significance + ")");
        Assert(!plan.Outlets.Any(o => o.OutletId == "council" || o.OutletId == "science"), "no official statement or science review for a hop");
        eng.Tick(Day * 3, 1, true);
        var heads = s.Articles.Select(a => a.Headline).ToList();
        Eq(heads.Count, heads.Distinct().Count(), "offline headlines differ by outlet: " + string.Join(" | ", heads.ToArray()));
    }
}
