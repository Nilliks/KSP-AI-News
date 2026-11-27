using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KerbalNews;
using KerbalNews.Web;

/// <summary>The "AI used" counter on the Settings page.</summary>
public static class UsageTests
{
    const double Day = 21600;
    static void Assert(bool c, string m) { if (!c) throw new Exception("Assertion failed: " + m); }

    static Sites MakeSites(NewsEngine eng)
    {
        var dir = Path.Combine(Directory.GetCurrentDirectory(), "GameData/KerbalNews/Sites");
        var sites = new Sites(eng, rel => { var p = Path.Combine(dir, rel); return File.Exists(p) ? File.ReadAllText(p) : null; });
        sites.Now = () => 0;
        return sites;
    }

    public static void TestUsageReadsTokensInEveryFormat()
    {
        long i, o;
        Assert(UsageMeter.ReadTokens("{\"choices\":[],\"usage\":{\"prompt_tokens\":1200,\"completion_tokens\":340}}", out i, out o) && i == 1200 && o == 340, "OpenAI format");
        Assert(UsageMeter.ReadTokens("{\"content\":[],\"usage\":{\"input_tokens\":900,\"output_tokens\":120}}", out i, out o) && i == 900 && o == 120, "Anthropic format");
        Assert(UsageMeter.ReadTokens("{\"candidates\":[],\"usageMetadata\":{\"promptTokenCount\":50,\"candidatesTokenCount\":7}}", out i, out o) && i == 50 && o == 7, "Gemini format");
        Assert(UsageMeter.ReadTokens(" {\"message\":{},\"prompt_eval_count\":30,\"eval_count\":9}", out i, out o) && i == 30 && o == 9, "Ollama format");
        Assert(!UsageMeter.ReadTokens("{\"choices\":[]}", out i, out o) && !UsageMeter.ReadTokens("", out i, out o) && !UsageMeter.ReadTokens("not json", out i, out o), "no usage, no tokens");
    }

    public static void TestUsageIsCountedPerServiceAndSaved()
    {
        var g = new GlobalSettings();
        Assert(UsageMeter.ServiceFor(g, "https://api.deepseek.com/chat/completions") == "deepseek", "DeepSeek by its address");
        Assert(UsageMeter.ServiceFor(g, "https://api.elevenlabs.io/v1/text-to-speech/abc") == Services.ElevenLabs, "ElevenLabs");
        Assert(UsageMeter.ServiceFor(g, "https://fal.run/fal-ai/flux/schnell") == Services.Fal, "fal pictures");
        Assert(UsageMeter.ServiceFor(g, "http://192.168.1.20:5000/v1/chat/completions") == "local", "a server on the home network");

        var t = new CoreTests.FakeTransport { Responder = r => r.Url.Contains("deepseek")
            ? new HttpResult { Status = 200, Body = "{\"choices\":[{\"message\":{\"content\":\"{}\"}}],\"usage\":{\"prompt_tokens\":1000,\"completion_tokens\":200}}" }
            : r.Url.Contains("elevenlabs") ? new HttpResult { Status = 200, Bytes = new byte[10] }
            : new HttpResult { Status = 500, Body = "{\"error\":{\"message\":\"down\"}}" } };
        var meter = new UsageMeter(t, () => g);
        Action<HttpRequestSpec> send = r => meter.Send(r, res => { });
        send(new HttpRequestSpec { Url = "https://api.deepseek.com/chat/completions", Body = "{}" });
        send(new HttpRequestSpec { Url = "https://api.deepseek.com/chat/completions", Body = "{}" });
        send(new HttpRequestSpec { Url = "https://api.deepseek.com/models", Method = "GET" });
        send(new HttpRequestSpec { Url = "https://api.elevenlabs.io/v1/text-to-speech/v1?output_format=pcm_22050", Body = "{\"text\":\"Liftoff!\",\"model_id\":\"m\"}" });
        send(new HttpRequestSpec { Url = "https://api.openai.com/v1/chat/completions", Body = "{}" });
        Assert(t.Sent.Count == 5, "every request still goes out");
        var ds = g.UsageTotal["deepseek"];
        Assert(ds.Calls == 2 && ds.InTokens == 2000 && ds.OutTokens == 400 && ds.Failed == 0, "two DeepSeek calls, tokens added up; the free model list is not counted");
        Assert(g.UsageTotal[Services.ElevenLabs].Letters == 8, "ElevenLabs counts the letters spoken");
        Assert(g.UsageTotal["openai"].Failed == 1 && g.UsageSession["openai"].Calls == 1, "a failed call is counted as failed");
        Assert(g.UsageDirty, "the settings know there is something new to write");

        var sn = new DataNode("S"); var kn = new DataNode("K");
        g.Save(sn, kn);
        Assert(!g.UsageDirty, "saving clears the flag");
        var back = GlobalSettings.Load(sn, kn);
        Assert(back.UsageTotal["deepseek"].InTokens == 2000 && back.UsageTotal[Services.ElevenLabs].Letters == 8 && back.UsageSession.Count == 0, "totals are saved; the session starts again at zero");

        back.ResetToDefaults();
        Assert(back.UsageTotal.ContainsKey("deepseek"), "resetting the settings keeps the usage counter");
    }

    public static void TestSettingsPageShowsUsageAndClearsIt()
    {
        var t = new CoreTests.FakeTransport();
        var g = new GlobalSettings();
        var eng = new NewsEngine(new NewsState(), g, t, new SimpleClock(Day));
        var sites = MakeSites(eng);
        string h = sites.Render("settings.kb").Html;
        Assert(h.Contains("AI used") && h.Contains("Nothing yet"), "an empty counter says so");

        g.CountUsage("deepseek", new UsageStats { Calls = 3, InTokens = 12345, OutTokens = 2100 });
        g.CountUsage(Services.ElevenLabs, new UsageStats { Calls = 2, Letters = 540 });
        h = sites.Render("settings.kb").Html;
        Assert(h.Contains("DeepSeek") && h.Contains("12,345") && h.Contains("2,100") && h.Contains("540 letters spoken") && h.Contains("5 requests since the game started"), "one row per service with its numbers");

        sites.Handle("usage-clear", null);
        Assert(g.UsageTotal.Count == 2 && sites.Render("settings.kb").Html.Contains("Clear every count"), "clearing asks first");
        sites.Handle("usage-clear/yes", null);
        h = sites.Render("settings.kb").Html;
        Assert(g.UsageTotal.Count == 0 && h.Contains("Nothing yet") && h.Contains("Totals since"), "cleared, with the date it started again");
    }
}
