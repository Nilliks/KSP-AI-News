using System;
using System.Collections.Generic;
using System.Linq;
using KerbalNews;

/// <summary>The AI looking at photos and the livestream camera, once per picture, only for models that can see.</summary>
public static class VisionTests
{
    const double Day = 21600;
    static void Assert(bool c, string m) { if (!c) throw new Exception("Assertion failed: " + m); }
    static readonly byte[] Jpg = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4 };

    static HttpResult Text(string text)
    {
        var resp = new Dictionary<string, object> { { "content", new List<object> { new Dictionary<string, object> { { "type", "text" }, { "text", text } } } } };
        return new HttpResult { Status = 200, Body = Json.Write(resp) };
    }

    public static void TestPicturesGoInEachProvidersFormat()
    {
        string b64 = Convert.ToBase64String(Jpg);
        var a = Providers.Build(Providers.Get("anthropic"), "", "k", "claude-haiku-4-5", "sys", "look", 100, true, Jpg).Body;
        Assert(a.Contains("\"type\":\"image\"") && a.Contains("image/jpeg") && a.Contains(b64), "Claude image block");
        var o = Providers.Build(Providers.Get("openai"), "", "k", "gpt-5-mini", "sys", "look", 100, true, Jpg).Body;
        Assert(o.Contains("image_url") && o.Contains("data:image/jpeg;base64," + b64) && o.Contains("\"detail\":\"low\""), "OpenAI image_url, low detail");
        var g = Providers.Build(Providers.Get("gemini"), "", "k", "gemini-2.5-flash", "sys", "look", 100, true, Jpg).Body;
        Assert(g.Contains("inline_data") && g.Contains(b64), "Gemini inline data");
        var l = Providers.Build(Providers.Get("ollama"), "", "", "llava", "sys", "look", 100, true, Jpg).Body;
        Assert(l.Contains("\"images\""), "Ollama images");
        Assert(!Providers.Build(Providers.Get("openai"), "", "k", "gpt-5-mini", "sys", "look", 100, true).Body.Contains("image_url"), "no picture, plain text as before");
        Assert(!Providers.CanSee(Providers.Get("deepseek"), "deepseek-chat") && Providers.CanSee(Providers.Get("gemini"), "gemini-2.5-flash")
            && Providers.CanSee(Providers.Get("ollama"), "llava:13b") && !Providers.CanSee(Providers.Get("ollama"), "mistral-nemo"), "who can see");
    }

    static NewsEngine WithPhoto(GlobalSettings g, CoreTests.FakeTransport t, out NewsState s, out GalleryShot shot)
    {
        Providers.ClearBlind();
        s = new NewsState();
        s.Profile.Name = "Kerbin Space Agency";
        var eng = new NewsEngine(s, g, t, new SimpleClock(Day));
        eng.PhotoJpeg = f => Jpg;
        eng.Tick(0, 0, true);
        shot = new GalleryShot { Id = "g1", File = "g1.jpg", UT = 5, VesselId = "v1", VesselName = "Test Rocket v1", Caption = "On the pad", Public = true, ReleasedUT = 5 };
        s.Gallery.Add(shot);
        return eng;
    }

    public static void TestABlindWriterGetsAHelperToLookOnce()
    {
        // DeepSeek writes; Gemini looks.
        var g = new GlobalSettings { ProviderId = "deepseek", OfflineMode = false, VisionProviderId = "gemini" };
        g.Keys["deepseek"] = "k1"; g.Keys["gemini"] = "k2";
        var t = new CoreTests.FakeTransport { Responder = r => r.Url.Contains("generativelanguage")
            ? new HttpResult { Status = 200, Body = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"{\\\"look\\\":\\\"A white rocket with orange tanks stands on a misty pad.\\\"}\"}]}}]}" }
            : new HttpResult { Status = 200, Body = "{\"choices\":[{\"message\":{\"content\":\"{}\"}}]}" } };
        NewsState s; GalleryShot shot;
        var eng = WithPhoto(g, t, out s, out shot);
        for (double u = 0; u < Day; u += Day * 0.06) eng.Tick(u, u, true);
        Assert(shot.Look.Contains("orange tanks"), "the helper described it: " + shot.Look + " / " + eng.LastError);
        Assert(t.Sent.Count(r => r.Url.Contains("generativelanguage")) == 1, "looked at once");
        Assert(!t.Sent.Any(r => r.Url.Contains("deepseek") && r.Body.Contains("base64")), "DeepSeek never gets a picture");
        var back = NewsState.Load(Snap(s));
        Assert(back.Gallery[0].Look == shot.Look, "the look is saved");

        // Without a helper, a blind model just doesn't look.
        var g2 = new GlobalSettings { ProviderId = "deepseek", OfflineMode = false };
        g2.Keys["deepseek"] = "k1";
        var t2 = new CoreTests.FakeTransport { Responder = r => new HttpResult { Status = 200, Body = "{\"choices\":[{\"message\":{\"content\":\"{}\"}}]}" } };
        NewsState s2; GalleryShot shot2;
        var e2 = WithPhoto(g2, t2, out s2, out shot2);
        for (double u = 0; u < Day; u += Day * 0.06) e2.Tick(u, u, true);
        Assert(shot2.Look == "" && t2.Sent.Count == 0, "no helper, no look, no call");
        // Offline: never.
        var g3 = new GlobalSettings { OfflineMode = true };
        NewsState s3; GalleryShot shot3;
        var e3 = WithPhoto(g3, null, out s3, out shot3);
        for (double u = 0; u < Day; u += Day * 0.06) e3.Tick(u, u, true);
        Assert(shot3.Look == "", "offline sends nothing");
    }

    static DataNode Snap(NewsState s) { var n = new DataNode("K"); s.Save(n); return n; }

    public static void TestTheNewsCallSeesTheMainPhotoAndRemembersIt()
    {
        var bodies = new List<string>();
        var t = new CoreTests.FakeTransport { Responder = r =>
        {
            bodies.Add(r.Body);
            var rep = CoreTests.AnthropicReply(r);
            if (!r.Body.Contains("photo_look")) return rep;
            var outer = Json.Parse(rep.Body);
            string txt = Json.Str(Json.Arr(Json.Get(outer, "content"))[0], "text").Replace("```json", "").Replace("```", "").Trim();
            return Text(txt.Substring(0, txt.Length - 1) + ",\"photo_look\":\"A stubby rocket under grey skies, steam at its base.\"}");
        } };
        var g = CoreTests.OnlineSettings();
        g.PhotoLook = true;
        NewsState s; GalleryShot shot;
        var eng = WithPhoto(g, t, out s, out shot);
        eng.PhotoJpeg = null;   // no background look first: the news call should carry the picture
        eng.TrackLaunch("v1", CoreTests.Craft(), 10);
        eng.Ingest(CoreTests.Ev(EventTypes.Launch, 10, "v1", "Kerbin", "Jebediah Kerman"));
        eng.Ingest(CoreTests.Ev(EventTypes.Orbit, 400, "v1", "Kerbin", "Jebediah Kerman"));
        eng.PhotoJpeg = f => Jpg;
        eng.Tick(10 + Day * 1.2, 1, true);
        var news = bodies.FirstOrDefault(b => b.Contains("newsroom"));
        Assert(news != null && news.Contains("\"type\":\"image\"") && news.Contains("photo_look"), "the writer sees the photo");
        Assert(shot.Look.Contains("stubby rocket"), "and its description is kept: " + shot.Look);
        // The next story about the craft gets the words, not the picture.
        eng.Ingest(CoreTests.Ev(EventTypes.Landing, Day * 2, "v1", "Mun", "Jebediah Kerman"));
        eng.Tick(Day * 4, 2, true);
        var second = bodies.LastOrDefault(b => b.Contains("newsroom"));
        Assert(second != news && second.Contains("WHAT THE PHOTOS SHOW") && second.Contains("stubby rocket") && !second.Contains("\"type\":\"image\""), "reused as text");
        Assert(PhotoOff(), "turned off, no pictures");
    }

    static bool PhotoOff()
    {
        var t = new CoreTests.FakeTransport { Responder = CoreTests.AnthropicReply };
        var g = CoreTests.OnlineSettings();
        g.PhotoLook = false;
        NewsState s; GalleryShot shot;
        var eng = WithPhoto(g, t, out s, out shot);
        eng.TrackLaunch("v1", CoreTests.Craft(), 10);
        eng.Ingest(CoreTests.Ev(EventTypes.Launch, 10, "v1", "Kerbin", "Jebediah Kerman"));
        for (double u = 0; u < Day * 3; u += Day * 0.1) eng.Tick(u, u, true);
        return t.Sent.Count > 0 && !t.Sent.Any(r => r.Body.Contains("base64") || r.Body.Contains("\"type\":\"image\""));
    }

    public static void TestAModelThatRefusesPicturesIsAskedAgainWithout()
    {
        var bodies = new List<string>();
        var t = new CoreTests.FakeTransport { Responder = r =>
        {
            bodies.Add(r.Body);
            if (r.Body.Contains("\"type\":\"image\"")) return new HttpResult { Status = 400, Body = "{\"error\":{\"message\":\"This model does not support image input\"}}" };
            return CoreTests.AnthropicReply(r);
        } };
        var g = CoreTests.OnlineSettings();
        NewsState s; GalleryShot shot;
        var eng = WithPhoto(g, t, out s, out shot);
        eng.PhotoJpeg = null;
        eng.TrackLaunch("v1", CoreTests.Craft(), 10);
        eng.Ingest(CoreTests.Ev(EventTypes.Launch, 10, "v1", "Kerbin", "Jebediah Kerman"));
        eng.PhotoJpeg = f => Jpg;
        double rt = 0;
        for (double u = Day * 1.2; u < Day * 2; u += Day * 0.05) { rt += 30; eng.Tick(u, rt, true); }
        Assert(s.Articles.Count > 0 && s.Articles.Any(a => !a.Offline), "the story still got AI coverage");
        int tries = bodies.Count(b => b.Contains("\"type\":\"image\""));
        Assert(tries >= 1 && tries <= 2, "the picture was tried once per model, then left out: " + tries);
        Providers.ClearBlind();
    }

    public static void TestStreamChatSeesTheCameraAtBigMoments()
    {
        var bodies = new List<string>();
        var t = new CoreTests.FakeTransport { Responder = r => { bodies.Add(r.Body); return Text("{\"viewers\":100,\"messages\":[{\"author\":\"a\",\"text\":\"look at that plume\"}]}"); } };
        var g = CoreTests.OnlineSettings();
        Providers.ClearBlind();
        var eng = new NewsEngine(new NewsState(), g, t, new SimpleClock(Day));
        eng.StreamFrameJpeg = () => Jpg;
        eng.Tick(0, 0, true);
        eng.StreamInterval = 30;
        eng.StartStream("v1", "Kerbal X", 0, 0);
        eng.StreamSample(new TelemetrySample { UT = 1, Altitude = 80 }, 1);
        eng.StreamMoment("liftoff", 5);
        eng.TickStream(6, true);
        Assert(bodies.Count == 1 && bodies[0].Contains("\"type\":\"image\"") && bodies[0].Contains("live camera"), "liftoff: the chat sees the frame");
        eng.StreamMoment("stage 2 activated", 30);
        eng.TickStream(40, true);
        Assert(bodies.Count == 2 && !bodies[1].Contains("\"type\":\"image\""), "small moments, and right after a frame: text only");
    }

    public static void TestStrayChineseIsTakenOutOfEnglishText()
    {
        Assert(Providers.StrayScript("The\u7d2f\u8ba1 time, accumulated over multiple missions.") == "The time, accumulated over multiple missions.", Providers.StrayScript("The\u7d2f\u8ba1 time, accumulated over multiple missions."));
        Assert(Providers.StrayScript("A \u7d2f\u8ba1 total.") == "A total.", "spaces tidied: " + Providers.StrayScript("A \u7d2f\u8ba1 total."));
        string zh = "\u6708\u7403\u7740\u9646\u6210\u529f\u4e86\u3002\u592a\u68d2\u4e86";
        Assert(Providers.StrayScript(zh) == zh, "a reply mostly in Chinese is left alone");
        Assert(Providers.StrayScript("Plain English.") == "Plain English.", "untouched");
    }
}
