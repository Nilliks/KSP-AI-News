using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KerbalNews;
using KerbalNews.Web;

/// <summary>One key per service, checked when saved, and each job on the service picked for it.</summary>
public static class ServiceTests
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

    public static void TestOldSecretsFilesMoveToOneKeyPerService()
    {
        var kn = new DataNode("K");
        Action<string, string> key = (id, k) => { var n = kn.AddNode("KEY"); n.Add("id", id); n.AddText("key", k); };
        key("deepseek", "sk-deepseek-0001");
        key("voice:elevenlabs", "sk_eleven_0002");
        key("img:gemini", "AIza-pictures-0003");      // no text key for Google: the picture key becomes Google's key
        key("openai", "sk-openai-text-0004");
        key("img:openai", "sk-openai-text-0004");     // the same key pasted twice: one is kept
        key("xai", "xai-text-0005");
        key("img:xai", "xai-other-account-0006");     // a different key for pictures: both are kept
        key("img:fal", "fal-0007");
        var g = GlobalSettings.Load(new DataNode("S"), kn);
        Assert(g.VoiceKey == "sk_eleven_0002" && g.KeyOf("elevenlabs") == "sk_eleven_0002" && !g.Keys.ContainsKey("voice:elevenlabs"), "the announcer's key moved to ElevenLabs");
        Assert(g.KeyOf("gemini") == "AIza-pictures-0003" && !g.Keys.ContainsKey("img:gemini"), "a picture-only Google key is now the Google key");
        Assert(g.KeyOf("openai") == "sk-openai-text-0004" && !g.Keys.ContainsKey("img:openai"), "a key pasted twice is kept once");
        Assert(g.KeyOf("fal") == "fal-0007", "fal's key moved");
        g.ImageProviderId = "xai";
        Assert(g.KeyOf("xai") == "xai-text-0005" && g.ImageKey == "xai-other-account-0006", "a different picture key is not lost and still draws the pictures");
        var sn2 = new DataNode("S"); var kn2 = new DataNode("K");
        g.Save(sn2, kn2);
        var back = GlobalSettings.Load(sn2, kn2);
        back.ImageProviderId = "xai";
        Assert(back.ImageKey == "xai-other-account-0006" && back.KeyOf("gemini") == "AIza-pictures-0003" && back.VoiceKey == "sk_eleven_0002", "and it survives a save and load");
        string err;
        Assert(back.SetKey("xai", "xai-new-0008", out err) && back.ImageKey == "xai-new-0008" && back.Keys.Keys.All(k => !k.StartsWith("img:")), "saving the service's key again leaves one key");
        Assert(!sn2.ToString().Contains("0001") && !sn2.ToString().Contains("0003"), "no key goes in settings.cfg");
    }

    public static void TestPastedKeysAreCheckedBeforeTheyAreSaved()
    {
        var g = new GlobalSettings();
        string err;
        Assert(g.SetKey("deepseek", "  \"sk-abcdef123456\"\r\n", out err) && g.KeyOf("deepseek") == "sk-abcdef123456", "spaces, line ends and quotes around a key are trimmed");
        Assert(g.KeyTail("deepseek") == "3456", "the tail is the last four characters");
        Assert(!g.SetKey("deepseek", "sk-abc sk-def", out err) && err.Contains("no spaces") && g.KeyOf("deepseek") == "sk-abcdef123456", "two things pasted at once are refused; the saved key stays");
        Assert(!g.SetKey("deepseek", new string('k', 400), out err) && err.Contains("too long"), "far too long is refused");
        Assert(g.SetKey("deepseek", "   ", out err) && g.KeyOf("deepseek") == "sk-abcdef123456", "a blank field keeps the saved key");
        g.Status["deepseek"] = new ServiceStatus { Ok = true, Message = "The key works." };
        g.SetKey("deepseek", "sk-abcdef123456", out err);
        Assert(g.StatusWord("deepseek") == "ok", "saving the same key keeps its mark");
        g.SetKey("deepseek", "sk-another-key-9999", out err);
        Assert(g.StatusWord("deepseek") == "untested", "a new key is unchecked again");
        g.ForgetKey("deepseek");
        Assert(g.StatusWord("deepseek") == "none" && !g.HasKey("deepseek"), "removed");
        Assert(Services.Tail("short") == "", "a very short key shows no tail");
        Assert(Services.All.Select(s => s.Id).Distinct().Count() == Services.All.Count && Providers.All.All(p => Services.Get(p.Id) != null && Services.Get(p.Id).Where != ""), "every provider is a service with a place to get its key");
        Assert(ImageProviders.All.All(p => Services.ForImage(p.Id) != ""), "every image preset belongs to a service");
    }

    public static void TestSavingAKeyChecksItForFree()
    {
        var t = new CoreTests.FakeTransport();
        var g = new GlobalSettings();
        var eng = new NewsEngine(new NewsState(), g, t, new SimpleClock(Day));
        var sites = MakeSites(eng);
        int changed = 0;
        sites.Changed = () => changed++;
        sites.Render("settings.kb");
        Assert(t.Sent.Count == 0, "opening Settings sends nothing by itself in tests");

        sites.Handle("svc-save/deepseek", new Dictionary<string, string> { { "key-deepseek", "sk-good-key-1234" } });
        Assert(t.Sent.Count == 1 && t.Sent[0].Method == "GET" && t.Sent[0].Url == "https://api.deepseek.com/models" && t.Sent[0].Headers["Authorization"] == "Bearer sk-good-key-1234", "the check is the free model list");
        string h = sites.Render("settings.kb").Html;
        Assert(h.Contains("Checking...") && h.Contains("Saved, not checked"), "the card says it is being checked");
        t.Pending[0](new HttpResult { Status = 200, Body = "{\"data\":[{\"id\":\"deepseek-chat\"},{\"id\":\"deepseek-v9\"}]}" });
        h = sites.Render("settings.kb").Html;
        Assert(g.StatusWord("deepseek") == "ok" && changed == 1 && h.Contains("Working") && h.Contains("Key ends in ...1234. The key works. 2 models available."), "Working, with the key's tail");
        Assert(g.ModelChoicesFor("deepseek").Contains("deepseek-v9"), "the check's model list is kept, so no second request is needed for it");
        Assert(!h.Contains("sk-good-key"), "the key itself is never on the page");

        sites.Handle("svc-save/deepseek", new Dictionary<string, string> { { "key-deepseek", "sk-wrong-key-5678" } });
        t.Pending[1](new HttpResult { Status = 401, Body = "{\"error\":{\"message\":\"bad key\"}}" });
        h = sites.Render("settings.kb").Html;
        Assert(g.StatusWord("deepseek") == "fail" && h.Contains("Not working") && h.Contains("The key was refused (401)"), "a refused key is marked Not working");

        sites.Handle("svc-test/deepseek", null);
        t.Pending[2](new HttpResult { Error = "Cannot resolve host" });
        h = sites.Render("settings.kb").Html;
        Assert(g.StatusWord("deepseek") == "untested" && h.Contains("Could not connect: Cannot resolve host"), "no network says nothing about the key");

        // ElevenLabs: a limited key that may not read the account page still counts.
        sites.Handle("svc-save/elevenlabs", new Dictionary<string, string> { { "key-elevenlabs", "sk_eleven_limited_1" } });
        var last = t.Sent[t.Sent.Count - 1];
        Assert(last.Url == "https://api.elevenlabs.io/v1/user" && last.Headers["xi-api-key"] == "sk_eleven_limited_1", "ElevenLabs is checked on its account page");
        t.Pending[3](new HttpResult { Status = 401, Body = "{\"detail\":{\"status\":\"missing_permissions\"}}" });
        Assert(g.StatusWord("elevenlabs") == "ok" && g.VoiceOn, "a limited ElevenLabs key works");

        // fal has no free check: nothing is sent, and the card says so.
        int before = t.Sent.Count;
        sites.Handle("svc-fold/more", null);
        sites.Handle("svc-save/fal", new Dictionary<string, string> { { "key-fal", "fal-key-000000001" } });
        Assert(t.Sent.Count == before && sites.Render("settings.kb").Html.Contains("No free check"), "no free check for fal");

        // With the game's switch on, opening Settings checks keys nobody has checked, once.
        g.Status.Clear();
        sites.AutoCheckKeys = true;
        sites.Render("settings.kb");
        int sent = t.Sent.Count;
        Assert(sent == before + 2, "deepseek and elevenlabs are checked when Settings opens");
        sites.Render("settings.kb");
        foreach (var p in t.Pending.Skip(4).ToList()) p(new HttpResult { Error = "offline" });
        sites.Render("settings.kb");
        Assert(t.Sent.Count == sent, "and not again on every redraw, even when the network swallowed them");
        g.Status.Clear(); g.OfflineMode = true;
        g.SetKey("gemini", "AIza-unchecked-00001", out h);
        sites.Render("settings.kb");
        Assert(t.Sent.Count == sent, "nothing is checked while the AI is turned off");
    }

    public static void TestModelListsRefreshAtStartAndTheDropDown()
    {
        var t = new CoreTests.FakeTransport();
        var g = new GlobalSettings { ProviderId = "openrouter", OfflineMode = false };
        g.Keys["openrouter"] = "sk-or-key-000001";
        g.FetchedModels["openrouter"] = "old/model-1";
        var eng = new NewsEngine(new NewsState(), g, t, new SimpleClock(Day));
        var sites = MakeSites(eng);

        // What the game does once when it starts.
        int saved = 0;
        Assert(eng.CheckServices(() => saved++) == 1 && t.Sent[t.Sent.Count - 1].Url == "https://openrouter.ai/api/v1/models", "the service with a key is asked for its model list");
        var ids = new List<string>();
        for (int i = 0; i < 20; i++) { ids.Add("openai/gpt-" + i); ids.Add("google/gemini-" + i); }
        ids.Add("newlab/brand-new-model");
        t.Pending[t.Pending.Count - 1](new HttpResult { Status = 200, Body = "{\"data\":[" + string.Join(",", ids.Select(x => "{\"id\":\"" + x + "\"}").ToArray()) + "]}" });
        Assert(saved == 1 && g.FetchedModels["openrouter"].Contains("newlab/brand-new-model") && !g.FetchedModels["openrouter"].Contains("old/model-1"), "the fresh list replaces the old one and is saved");
        Assert(eng.CheckServices(null) == 0, "once per game start");

        sites.Handle("job-open/writer", null);
        string h = sites.Render("settings.kb").Html;
        Assert(h.Contains("action:job-models/writer") && !h.Contains("action:job-model/writer/"), "the drop-down is closed until asked for");
        sites.Handle("job-models/writer", null);
        h = sites.Render("settings.kb").Html;
        Assert(h.Contains("action:job-group/writer/newlab") && h.Contains("action:job-group/writer/openai"), "a long list is cut down by maker");
        Assert(h.Contains("action:job-model/writer/anthropic/claude-opus") || h.Contains("action:job-model/writer/anthropic/"), "the maker of the model in use is shown first");
        Assert(!h.Contains("action:job-model/writer/newlab/brand-new-model"), "other makers' models stay folded away");
        sites.Handle("job-group/writer/newlab", null);
        h = sites.Render("settings.kb").Html;
        Assert(h.Contains("action:job-model/writer/newlab/brand-new-model") && !h.Contains("action:job-model/writer/openai/gpt-1\""), "picking a maker shows its models");
        sites.Handle("job-model/writer/newlab/brand-new-model", null);
        h = sites.Render("settings.kb").Html;
        Assert(g.Writer.Model == "newlab/brand-new-model" && !h.Contains("action:job-model/writer/"), "picking a model uses it and closes the list");

        g.OfflineMode = true; g.Status.Clear();
        var eng2 = new NewsEngine(new NewsState(), g, t, new SimpleClock(Day));
        int sent = t.Sent.Count;
        Assert(eng2.CheckServices(null) == 0 && t.Sent.Count == sent, "nothing is asked while the AI is turned off");
    }

    public static void TestCostEstimatesComeFromARefreshedPriceList()
    {
        Pricing.Clear();
        var t = new CoreTests.FakeTransport();
        var g = new GlobalSettings { ProviderId = "deepseek", OfflineMode = false };
        g.Keys["deepseek"] = "sk-deepseek-key1";
        var eng = new NewsEngine(new NewsState(), g, t, new SimpleClock(Day));
        eng.Today = () => "2030-01-02";
        var sites = MakeSites(eng);
        Assert(Pricing.Count > 100 && Pricing.AsOf != "", "a price list ships with the mod");

        int saved = 0;
        Assert(eng.RefreshPrices(() => saved++) && t.Sent[t.Sent.Count - 1].Url == Pricing.Url && t.Sent[t.Sent.Count - 1].Headers.Count == 0, "the price list is fetched at game start, with no key");
        t.Pending[t.Pending.Count - 1](new HttpResult { Status = 200, Body = "{\"sample_spec\":{\"mode\":\"chat\"},"
            + "\"deepseek/deepseek-chat\":{\"litellm_provider\":\"deepseek\",\"mode\":\"chat\",\"input_cost_per_token\":1e-06,\"output_cost_per_token\":2e-06},"
            + "\"deepseek/deepseek-embed\":{\"litellm_provider\":\"deepseek\",\"mode\":\"embedding\",\"input_cost_per_token\":1e-06,\"output_cost_per_token\":0},"
            + "\"someone-else/model\":{\"litellm_provider\":\"bedrock\",\"mode\":\"chat\",\"input_cost_per_token\":1e-06,\"output_cost_per_token\":2e-06}}" });
        double a, b;
        Assert(saved == 1 && Pricing.AsOf == "2030-01-02" && Pricing.TryGet(Providers.Get("deepseek"), "deepseek-chat", out a, out b) && a == 1 && b == 2, "new prices replace the old ones and are saved");
        Assert(Pricing.TryGet(Providers.Get("deepseek"), "deepseek-chat-2030-01-01", out a, out b) && a == 1, "a dated name takes the price of the name it starts with");
        Assert(!Pricing.TryGet(Providers.Get("deepseek"), "deepseek-embed", out a, out b) && !Pricing.TryGet(Providers.Get("deepseek"), "no-such-model", out a, out b), "models that don't write, and unknown ones, have no price");
        Assert(!eng.RefreshPrices(null), "once per game start");
        var eng2 = new NewsEngine(new NewsState(), g, t, new SimpleClock(Day));
        eng2.Today = () => "2030-01-02";
        Assert(!eng2.RefreshPrices(null), "and not again on a day it was already read");
        eng2.Today = () => "2030-01-03";
        g.OfflineMode = true;
        Assert(!eng2.RefreshPrices(null), "nothing is fetched while the AI is turned off");
        g.OfflineMode = false;

        // 6000 in and 3500 out at $1 and $2 per million: $0.013.
        g.WriterModels["deepseek"] = "deepseek-chat";
        string h = sites.Render("settings.kb").Html;
        Assert(h.Contains("About $0.01 a big story ($1.00 in, $2.00 out per million tokens)."), "the job shows what a big story costs");
        sites.Handle("job-open/writer", null);
        sites.Handle("job-models/writer", null);
        h = sites.Render("settings.kb").Html;
        Assert(h.Contains("about $0.01 a big story") && h.Contains("as of 2030-01-02"), "each model in the drop-down carries its estimate, and the page says how fresh the prices are");
        Assert(Pricing.Line(Providers.Get("ollama"), "qwen2.5:14b", Pricing.BigStory, "a big story").StartsWith("free"), "a local model is free");

        DataNode sn, kn;
        g.Save(sn = new DataNode("S"), kn = new DataNode("K"));
        Pricing.Clear();
        GlobalSettings.Load(sn, kn);
        Assert(Pricing.AsOf == "2030-01-02" && Pricing.TryGet(Providers.Get("deepseek"), "deepseek-chat", out a, out b) && a == 1, "prices are kept between sessions");

        Assert(Pricing.ReadOpenRouter("{\"data\":[{\"id\":\"lab/model-x\",\"pricing\":{\"prompt\":\"0.000003\",\"completion\":\"0.000015\"}}]}") == 1
            && Pricing.TryGet(Providers.Get("openrouter"), "lab/model-x", out a, out b) && Math.Abs(a - 3) < 1e-9 && Math.Abs(b - 15) < 1e-9, "OpenRouter's own list gives its prices");
        Pricing.Clear();
    }

    public static void TestSpendingIsCountedFromWhatRepliesReport()
    {
        Pricing.Clear();
        Pricing.Load("# 2030-01-02\ndeepseek|deepseek-chat 1 2\n");
        var t = new CoreTests.FakeTransport();
        var g = new GlobalSettings { ProviderId = "deepseek", OfflineMode = false };
        g.Keys["deepseek"] = "sk-deepseek-key1";
        g.WriterModels["deepseek"] = "deepseek-chat"; g.CheapModels["deepseek"] = "deepseek-chat";
        var state = new NewsState();
        var eng = new NewsEngine(state, g, t, new SimpleClock(Day));
        var sites = MakeSites(eng);
        Assert(sites.Render("settings.kb").Html.Contains("Nothing yet. Rows appear here after the first AI request."), "the table starts empty");

        // A free request (a model list) is not counted; a paid one is, under the feature that sent it.
        eng.FetchModels("deepseek", (ok, msg) => { });
        t.Pending[t.Pending.Count - 1](new HttpResult { Status = 200, Body = "{\"data\":[{\"id\":\"deepseek-chat\"}]}" });
        Assert(g.UsageAll.Total.Calls == 0, "listing models is free and not counted");
        string reply = "{\"choices\":[{\"message\":{\"content\":\"{\\\"ok\\\":true}\"}}],\"usage\":{\"prompt_tokens\":100000,\"completion_tokens\":50000}}";
        eng.TestConnection(g.Writer, (ok, msg) => { });
        Assert(t.Sent[t.Sent.Count - 1].Feature == "Test requests" && t.Sent[t.Sent.Count - 1].Model == "deepseek-chat", "a request carries the feature and model that sent it");
        t.Pending[t.Pending.Count - 1](new HttpResult { Status = 200, Body = reply });
        // 100,000 in at $1 and 50,000 out at $2 per million: $0.20.
        foreach (var b in new[] { g.UsageSession, g.UsageAll, state.Usage })
        {
            var u = b.Of("Test requests");
            Assert(u.Calls == 1 && u.InTokens == 100000 && u.OutTokens == 50000 && Math.Abs(u.Cost - 0.2) < 1e-9 && u.Unpriced == 0, "session, save and all time each count the request and its cost");
        }
        eng.TestConnection(g.Writer, (ok, msg) => { });
        t.Pending[t.Pending.Count - 1](new HttpResult { Status = 500, Body = "{}" });
        Assert(g.UsageAll.Total.Calls == 2 && g.UsageAll.Total.Failed == 1 && Math.Abs(g.UsageAll.Total.Cost - 0.2) < 1e-9, "a failed request is counted and costs nothing");
        g.WriterModels["deepseek"] = "mystery-model";
        eng.TestConnection(g.Writer, (ok, msg) => { });
        t.Pending[t.Pending.Count - 1](new HttpResult { Status = 200, Body = reply });
        Assert(g.UsageAll.Total.Unpriced == 1 && Math.Abs(g.UsageAll.Total.Cost - 0.2) < 1e-9, "a model with no listed price is counted apart, not guessed");

        long i, o;
        Assert(UsageMeter.ReadTokens("{\"usage\":{\"input_tokens\":10,\"output_tokens\":5,\"cache_read_input_tokens\":90}}", out i, out o) && i == 100 && o == 5, "Anthropic's cached input is part of the input");
        Assert(UsageMeter.ReadTokens("{\"usageMetadata\":{\"promptTokenCount\":7,\"candidatesTokenCount\":3,\"thoughtsTokenCount\":20}}", out i, out o) && i == 7 && o == 23, "Gemini's thinking is part of the output");
        Assert(UsageMeter.ReadTokens("{\"prompt_eval_count\":4,\"eval_count\":6}", out i, out o) && i == 4 && o == 6 && !UsageMeter.ReadTokens("audio bytes", out i, out o), "Ollama's counts are read; a reply with none gives none");

        // Half an hour of play and two launches.
        for (int k = 0; k <= 1800; k++) eng.Tick(Day + k, 1000 + k, false);
        eng.Tick(Day + 1801, 99999, false);
        eng.Ingest(CoreTests.Ev(EventTypes.Launch, Day + 10, "u1"));
        eng.Ingest(CoreTests.Ev(EventTypes.Launch, Day + 20, "u2"));
        Assert(Math.Abs(g.UsageAll.PlaySeconds - 1800) < 1 && state.Usage.Launches == 2, "play time and launches are counted; a long gap is not play");
        string h = sites.Render("settings.kb").Html;
        Assert(h.Contains("Test requests") && h.Contains("$0.20 · 3") && h.Contains("All time: $0.20 for 3 requests over 0.5 hours of play and 2 launches. About $0.40 an hour of play, $0.10 a launch."), "the page shows the totals and what they come to per hour and per launch");
        Assert(h.Contains("1 request on a model with no listed price") && h.Contains("1 request failed and cost nothing."), "what is not in the dollars is said");

        DataNode sn, kn;
        g.Save(sn = new DataNode("S"), kn = new DataNode("K"));
        var g2 = GlobalSettings.Load(sn, kn);
        var saved = new DataNode("N"); state.Save(saved);
        var state2 = NewsState.Load(saved);
        Assert(Math.Abs(g2.UsageAll.Total.Cost - 0.2) < 1e-9 && g2.UsageAll.Launches == 2 && g2.UsageSession.Total.Calls == 0, "the all-time count is saved with the settings; the session count is not");
        Assert(state2.Usage.Total.Calls == 3 && Math.Abs(state2.Usage.PlaySeconds - 1800) < 1, "this save's count is saved with the save");
        g.ResetToDefaults();
        Assert(g.UsageAll.Total.Calls == 3, "resetting the settings keeps the count");

        sites.Handle("usage-clear", null);
        Assert(g.UsageAll.Total.Calls == 3 && sites.Render("settings.kb").Html.Contains("action:usage-clear/yes"), "clearing asks first");
        sites.Handle("usage-clear/yes", null);
        Assert(g.UsageAll.Total.Calls == 0 && state.Usage.Total.Calls == 0 && g.UsageAll.Since != "", "then every count starts again");
        Pricing.Clear();
    }

    public static void TestEachJobUsesTheServicePickedForIt()
    {
        var t = new CoreTests.FakeTransport();
        var g = new GlobalSettings { ProviderId = "anthropic", OfflineMode = false };
        g.Keys["anthropic"] = "sk-ant-key";
        var eng = new NewsEngine(new NewsState(), g, t, new SimpleClock(Day));
        var sites = MakeSites(eng);
        Assert(g.Cheap.Preset.Id == "anthropic" && g.Cheap.Model == "claude-haiku-5-5" && g.Writer.Model == "claude-sonnet-5-5", "by default one service does both jobs");

        sites.Handle("job-open/cheap", null);
        string h = sites.Render("settings.kb").Html;
        Assert(h.Contains("DeepSeek (needs a key)") && !h.Contains("action:job-service/cheap/deepseek"), "a service without a key can't be picked");
        sites.Handle("job-service/cheap/deepseek", null);
        Assert(g.CheapProviderId == "", "and asking anyway does nothing");

        string err;
        g.SetKey("deepseek", "sk-deepseek-key", out err);
        sites.Handle("job-service/cheap/deepseek", null);
        sites.Handle("job-use/cheap", new Dictionary<string, string> { { "model-cheap", " deepseek-v4-flash " } });
        Assert(g.ProviderId == "anthropic" && g.Cheap.Preset.Id == "deepseek" && g.Cheap.Model == "deepseek-v4-flash" && g.Cheap.Key == "sk-deepseek-key", "the cheap model is on DeepSeek");
        Assert(g.Writer.Preset.Id == "anthropic" && g.Writer.Key == "sk-ant-key" && g.WriterModel == "claude-sonnet-5-5", "the writer stays on Anthropic");
        h = sites.Render("settings.kb").Html;
        Assert(h.Contains("DeepSeek · deepseek-v4-flash") && h.Contains("Anthropic (Claude) · claude-sonnet-5-5"), "both show under What uses what");

        // The real requests go where the job says.
        sites.Handle("job-test/cheap", null);
        var c = t.Sent[t.Sent.Count - 1];
        Assert(c.Url == "https://api.deepseek.com/chat/completions" && c.Headers["Authorization"] == "Bearer sk-deepseek-key" && c.Body.Contains("deepseek-v4-flash"), "cheap request to DeepSeek with DeepSeek's key");
        t.Pending[t.Pending.Count - 1](new HttpResult { Status = 200, Body = "{\"choices\":[{\"message\":{\"content\":\"{\\\"ok\\\":true}\"}}]}" });
        Assert(sites.JobOk && sites.JobResult.Contains("Connected to DeepSeek") && g.StatusWord("deepseek") == "ok", "a real answer also marks the key Working");
        sites.Handle("job-test/writer", null);
        var w = t.Sent[t.Sent.Count - 1];
        Assert(w.Url == "https://api.anthropic.com/v1/messages" && w.Headers["x-api-key"] == "sk-ant-key" && !w.Headers.ContainsKey("Authorization"), "writer request to Anthropic with Anthropic's key, and no other key rides along");
        t.Pending[t.Pending.Count - 1](new HttpResult { Status = 401, Body = "{\"error\":{\"message\":\"invalid x-api-key\"}}" });
        Assert(!sites.JobOk && g.StatusWord("anthropic") == "fail", "a refused real request marks the key Not working");

        // Settings round trip, and the fallback when the cheap service loses its key.
        var sn = new DataNode("S"); var kn = new DataNode("K");
        g.Save(sn, kn);
        var back = GlobalSettings.Load(sn, kn);
        Assert(back.CheapProviderId == "deepseek" && back.Cheap.Model == "deepseek-v4-flash", "the pick is saved");
        sites.Handle("svc-remove/deepseek", null);
        Assert(g.CheapProviderId == "deepseek" && g.Cheap.Preset.Id == "anthropic" && g.Cheap.Model == "claude-haiku-5-5", "without its key the job falls back to the writer's service");
        Assert(sites.Render("settings.kb").Html.Contains("DeepSeek has no key, so Anthropic (Claude) does this job for now."), "and the page says so");

        // Picking the writer's own service for the cheap job is the same as no pick.
        g.SetKey("deepseek", "sk-deepseek-key", out err);
        sites.Handle("job-service/cheap/anthropic", null);
        Assert(g.CheapProviderId == "", "same service as the writer = no separate pick");
        sites.Handle("job-service/writer/deepseek", null);
        Assert(g.ProviderId == "deepseek" && g.Cheap.Preset.Id == "deepseek", "moving the writer moves both when nothing else is picked");
    }

    public static void TestLocalServersHaveAnAddressInsteadOfAKey()
    {
        var t = new CoreTests.FakeTransport();
        var g = new GlobalSettings();
        var eng = new NewsEngine(new NewsState(), g, t, new SimpleClock(Day));
        var sites = MakeSites(eng);
        string h = sites.Render("settings.kb").Html;
        Assert(!h.Contains("svc-save/ollama") && h.Contains("svc-fold/local"), "local servers are folded away until asked for");
        sites.Handle("svc-fold/local", null);
        h = sites.Render("settings.kb").Html;
        Assert(h.Contains("svc-save/ollama") && h.Contains("name=\"addr-ollama\"") && h.Contains("Not in use"), "each has an address field");
        sites.Handle("svc-save/ollama", new Dictionary<string, string> { { "key-ollama", "" }, { "addr-ollama", "localhost:11434" } });
        Assert(sites.Render("settings.kb").Html.Contains("The address must start with http:// or https://"), "an address needs its scheme");
        sites.Handle("svc-save/ollama", new Dictionary<string, string> { { "key-ollama", "" }, { "addr-ollama", "http://192.168.1.20:11434/" } });
        Assert(g.BaseUrlOf("ollama") == "http://192.168.1.20:11434" && t.Sent.Last().Url == "http://192.168.1.20:11434/api/tags", "the address is saved and the server asked for its models");
        t.Pending.Last()(new HttpResult { Error = "Connection refused" });
        Assert(g.StatusWord("ollama") == "fail" && sites.Render("settings.kb").Html.Contains("Nothing answered at that address. Start Ollama before the game."), "a server that isn't running is told plainly");
        sites.Handle("svc-test/ollama", null);
        t.Pending.Last()(new HttpResult { Status = 200, Body = "{\"models\":[{\"name\":\"qwen2.5:14b\"}]}" });
        Assert(g.StatusWord("ollama") == "ok", "and Working once it answers");
        sites.Handle("job-service/writer/ollama", null);
        Assert(g.ProviderId == "ollama" && g.Writer.Ready && g.LocalRoot == null, "it can be picked without a key; nothing is local while the AI is off");
        g.OfflineMode = false;
        Assert(g.LocalRoot == "http://192.168.1.20:11434", "its requests queue one at a time");
    }

    public static void TestGalleryKeyResetAndThePhotoHelpersModel()
    {
        var g = new GlobalSettings { ProviderId = "deepseek", OfflineMode = false };
        string err;
        g.SetKey("deepseek", "sk-deepseek-key-0001", out err);
        g.SetKey("gemini", "AIza-gemini-key-0002", out err);
        var eng = new NewsEngine(new NewsState(), g, null, new SimpleClock(Day));
        var sites = MakeSites(eng);

        // The gallery key.
        Assert(g.GalleryKey == "F8" && sites.Render("gallery.kb").Html.Contains("Press F8"), "F8 unless changed");
        sites.Handle("gallery-key/F6", null);
        Assert(g.GalleryKey == "F6" && sites.Render("gallery.kb").Html.Contains("Press F6") && !sites.Render("gallery.kb").Html.Contains("F8"), "the pages name the chosen key");
        sites.Handle("gallery-key/F9", null);
        Assert(g.GalleryKey == "F6", "a key stock KSP uses can't be picked");
        sites.Handle("gallery-key/off", null);
        var sn = new DataNode("S"); var kn = new DataNode("K");
        g.Save(sn, kn);
        Assert(g.GalleryKey == "" && GlobalSettings.Load(sn, kn).GalleryKey == "" && GlobalSettings.Load(new DataNode("S"), null).GalleryKey == "F8", "off is saved as off; a file without it means F8");

        // The photo helper's model.
        sites.Handle("job-look/gemini", null);
        Assert(g.Looker != null && g.Looker.Model == "gemini-2.5-flash" && eng.CanLook, "the helper starts on its service's cheap model");
        sites.Handle("job-open/look", null);
        sites.Handle("job-models/look", null);
        Assert(sites.Render("settings.kb").Html.Contains("action:job-model/look/gemini-2.5-pro"), "its models that can see are offered");
        sites.Handle("job-model/look/gemini-2.5-pro", null);
        g.Save(sn = new DataNode("S"), kn = new DataNode("K"));
        Assert(g.Looker.Model == "gemini-2.5-pro" && GlobalSettings.Load(sn, kn).Looker.Model == "gemini-2.5-pro", "the pick is used and saved");
        Assert(g.CheapModelOf("gemini") == "gemini-2.5-flash", "and it does not change Google's cheap model for writing");

        // Reset: every switch and number goes back; keys and what-uses-what stay.
        g.GalleryKey = "F7"; g.MaxTokens = 9000; g.KtvOn = false; g.AiAmount = 1; g.SitesOff.Add("kmail"); g.CheapProviderId = "gemini";
        sites.Handle("reset-settings", null);
        Assert(g.MaxTokens == 9000 && sites.ConfirmReset && sites.Render("settings.kb").Html.Contains("reset-settings/yes"), "the first click only asks");
        sites.Handle("reset-settings/cancel", null);
        Assert(!sites.ConfirmReset && g.MaxTokens == 9000, "cancel leaves everything");
        sites.Handle("reset-settings", null);
        sites.Handle("reset-settings/yes", null);
        Assert(g.GalleryKey == "F8" && g.MaxTokens == 6000 && g.KtvOn && g.AiAmount == 3 && g.SiteOn("kmail"), "switches, numbers and sites are back to default");
        Assert(g.KeyOf("deepseek") == "sk-deepseek-key-0001" && g.KeyOf("gemini") == "AIza-gemini-key-0002" && g.ProviderId == "deepseek" && g.CheapProviderId == "gemini"
            && g.VisionProviderId == "gemini" && g.VisionModel == "gemini-2.5-pro" && !g.OfflineMode, "keys, picks and the AI switch are kept");
    }
}
