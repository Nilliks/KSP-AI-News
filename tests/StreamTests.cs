using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KerbalNews;
using KerbalNews.Web;

public static class StreamTests
{
    static void Assert(bool c, string m) { if (!c) throw new Exception("Assertion failed: " + m); }

    static HttpResult ChatReply(HttpRequestSpec req)
    {
        var inner = Json.Write(new Dictionary<string, object> {
            { "viewers", 5000.0 },
            { "messages", new List<object> {
                new Dictionary<string, object> { { "author", "@munwatcher" }, { "text", "clean staging!" } },
                new Dictionary<string, object> { { "author", "taxpayer_ted" }, { "text", "how much did that booster cost" } },
            } },
        });
        var resp = new Dictionary<string, object> { { "content", new List<object> { new Dictionary<string, object> { { "type", "text" }, { "text", inner } } } } };
        return new HttpResult { Status = 200, Body = Json.Write(resp) };
    }

    static TelemetrySample Sample(double ut, double alt) { return new TelemetrySample { UT = ut, Altitude = alt, Speed = alt / 10, Apoapsis = alt * 2, Periapsis = -500000, Stage = 3, FuelPct = 70, G = 1.8, Situation = "FLYING", Body = "Kerbin" }; }

    public static void TestLivestreamOffline()
    {
        var s = new NewsState();
        var eng = new NewsEngine(s, new GlobalSettings { OfflineMode = true }, null, new SimpleClock(21600));
        eng.Tick(0, 0, true);
        eng.TrackLaunch("v1", CoreTests.Craft(), 10);
        var st = eng.StartStream("v1", "Test Rocket v1", 10, 100);
        Assert(st.Viewers > 0 && st.Chat.Count == 1, "stream starts with a welcome");
        eng.StreamSample(Sample(12, 100), 101);
        eng.Ingest(CoreTests.Ev(EventTypes.Launch, 12));
        Assert(!st.Moments.Contains("liftoff"), "a launch event alone is not liftoff; the host reports it when the craft moves");
        eng.Ingest(CoreTests.Ev(EventTypes.Orbit, 13));
        Assert(st.Moments.Any(m => m.Contains("orbit")), "an ingested event becomes a stream moment");
        eng.StreamMoment("liftoff", 102);
        eng.SetRealTime(105);
        eng.TickStream(105, true);
        Assert(st.Batches == 1 && st.Queue.Count > 0, "offline chat batch queued");
        eng.TickStream(200, true);
        Assert(st.Chat.Count > 5 && st.Chat.Any(c => { var x = c.Text.ToLowerInvariant(); return x.Contains("liftoff") || x.Contains("go go") || x.Contains("goosebumps") || x.Contains("plume") || x.Contains("lets go") || x.Contains("look at it go") || x.Contains("tower") || x.Contains("godspeed") || x.Contains("send it") || x.Contains("shaking") || x.Contains("fire") || x.Contains("wow") || x.Contains("cheering") || x.Contains("crying") || x.Contains("believe") || x.Contains("going") || x.Contains("sound"); }), "chat plays out and reacts to liftoff");
        eng.StreamSay("Crew reports all good", true, "", 201);
        Assert(st.Chat.Last().Agency && st.Chat.Last().Player, "player message shows as official");
        eng.StreamSample(Sample(60, 9000), 230);
        eng.SetRealTime(240);
        eng.TickStream(240, true);
        eng.TickStream(400, true);
        Assert(st.Chat.Any(c => c.Text.Contains("crew") || c.Text.Contains("official") || c.Text.Contains("delta-v") || c.Text.Contains("streaming")), "chat answers the agency");
        int stories = s.Stories.Count;
        var arch = eng.EndStream(70);
        Assert(eng.Stream == null && arch != null && s.Streams.Count == 1 && arch.Highlights.Count > 0, "stream archived");
        Assert(s.Stories.Any(x => x.Events.Any(e => e.Type == EventTypes.Livestream)), "the press hears about the stream");

        var node = new DataNode("NEWS");
        s.Save(node);
        var back = NewsState.Load(node);
        Assert(back.Streams.Count == 1 && back.Streams[0].Highlights.Count == arch.Highlights.Count && back.Streams[0].Moments.Contains("liftoff"), "archive saved");
    }

    public static void TestLivestreamAiAndPage()
    {
        var s = new NewsState();
        var t = new CoreTests.FakeTransport { Responder = ChatReply };
        var eng = new NewsEngine(s, CoreTests.OnlineSettings(), t, new SimpleClock(21600));
        eng.Tick(0, 0, true);
        eng.TrackLaunch("v1", CoreTests.Craft(), 10);
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "GameData/KerbalNews/Sites");
        var sites = new Sites(eng, rel => { var p = Path.Combine(dir, rel); return File.Exists(p) ? File.ReadAllText(p) : null; });
        double now = 20;
        sites.Now = () => now;
        sites.ActiveVesselId = () => "v1";
        sites.ActiveVesselName = () => "Test Rocket v1";
        var idle = sites.Render("live.kb");
        Assert(idle.Html.Contains("action:stream-start") && !idle.Html.Contains("{{"), "idle page offers Go live");
        eng.SetRealTime(50);
        sites.Handle("action:stream-start", null);
        Assert(eng.Stream != null && eng.Stream.VesselId == "v1", "Go live starts the stream");
        eng.StreamSample(Sample(20, 3000), 51);
        eng.StreamMoment("stage 2 separated", 52);
        eng.SetRealTime(60);
        eng.TickStream(60, true);
        Assert(t.Sent.Count == 1, "one cheap-model call per batch");
        string user = Json.Str(Json.Arr(Json.Get(Json.Parse(t.Sent[0].Body), "messages"))[0], "content");
        Assert(user.Contains("stage 2 separated") && user.Contains("Telemetry now") && user.Contains("viewers"), "chat prompt carries telemetry and moments");
        eng.TickStream(61, true);
        Assert(t.Sent.Count == 1, "no second call before the interval");
        eng.TickStream(89, true);
        Assert(eng.Stream.Chat.Any(c => c.Author == "munwatcher" && c.Text == "clean staging!"), "AI chat plays out, handles cleaned");
        Assert(eng.Stream.Viewers > 1000, "viewers move toward the estimate");
        var live = sites.Render("live.kb");
        Assert(live.Html.Contains("clean staging!") && live.Html.Contains("kblive:frame") && live.Html.Contains("Altitude") && !live.Html.Contains("{{"), "live page shows chat, video and telemetry");
        var root = Html.Parse(live.Html);
        Css.Apply(root, Css.Parse(live.Css));
        var lay = Layout.Run(root, 1100, (TextItem ti, float w, out float mw, out float mh) => { mw = Math.Min(w, ti.Plain.Length * ti.FontSize * 0.5f); mh = ti.FontSize * 1.3f; });
        Assert(lay.Inputs.Count >= 2 && lay.Images.Any(i => i.Src == "kblive:frame"), "live page lays out with chat inputs and the video");
        sites.Handle("action:stream-say/anon", new Dictionary<string, string> { { "chat", "is that a new engine?" }, { "handle", "curious_kerbal" } });
        Assert(eng.Stream.Chat.Last().Author == "curious_kerbal" && !eng.Stream.Chat.Last().Agency, "anonymous chat");
        sites.Handle("action:stream-end", null);
        Assert(eng.Stream == null && sites.Render("live.kb").Html.Contains("Past streams"), "ending lists the replay");
        var replay = sites.Render("live.kb/a/" + s.Streams[0].Id);
        Assert(replay.Title.Contains("replay") && !replay.Html.Contains("{{"), "replay page renders");
    }
}
