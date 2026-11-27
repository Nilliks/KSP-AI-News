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
