using System;
using System.Collections.Generic;
using System.Linq;

namespace KerbalNews.Web
{
    /// <summary>
    /// The top of the Settings page. "AI services": one card per service with its key, pasted once, checked for free
    /// when it is saved. "What uses what": each job picks a service that has a key, and a model.
    /// </summary>
    public partial class Sites
    {
        /// <summary>Which folds are open, which job is being changed, and what a bad paste was told. This session only.</summary>
        public bool ShowMoreServices, ShowLocalServices;
        public string OpenJob = "", OpenService = "";
        readonly Dictionary<string, string> keyErrors = new Dictionary<string, string>();
        /// <summary>What "Get the full list" or "Send a test request" under a job said.</summary>
        public string JobResult = "";
        public bool JobOk = true;
        /// <summary>
        /// Check saved keys nobody has checked yet when the Settings page opens (free requests; never while the AI is off).
        /// The game turns this on; tests leave it off so a page render sends nothing.
        /// </summary>
        public bool AutoCheckKeys;

        static readonly string[] TextJobs = { "writer", "cheap" };

        void AddServiceSettings(Dictionary<string, object> m, GlobalSettings g)
        {
            if (AutoCheckKeys) Engine.CheckServices(() => { SaveSettings(); Changed(); });
            Func<AiService, object> card = s => ServiceCard(s, g);
            // Services with a key come first, so what is set up is at the top.
            m["services"] = Services.All.Where(s => !s.Minor && !s.Local).OrderByDescending(s => g.HasKey(s.Id)).Select(card).ToList();
            m["moreServices"] = Services.All.Where(s => s.Minor && !s.Local).Select(card).ToList();
            m["localServices"] = Services.All.Where(s => s.Local).Select(card).ToList();
            m["moreNames"] = string.Join(", ", Services.All.Where(s => s.Minor && !s.Local).Select(s => s.Name).ToArray());
            // A fold with something set in it opens by itself, so a saved key is never out of sight.
            m["showMore"] = ShowMoreServices || Services.All.Any(s => s.Minor && !s.Local && g.HasKey(s.Id));
            m["showLocal"] = ShowLocalServices || Services.All.Any(s => s.Local && ServiceInUse(s, g));
            m["jobs"] = new List<object> { TextJob("writer", g), TextJob("cheap", g), LookJob(g), ImageJob(g), VoiceJob(g) };
            m["jobResult"] = JobResult;
            m["jobOk"] = JobOk;
        }

        static bool ServiceInUse(AiService s, GlobalSettings g)
        {
            return s.TextId != "" && (s.TextId == g.ProviderId || s.TextId == g.CheapServiceId) || s.ImageId != "" && s.ImageId == g.ImageProviderId;
        }

        object ServiceCard(AiService s, GlobalSettings g)
        {
            string word = g.StatusWord(s.Id), tail = g.KeyTail(s.Id), err;
            bool set = g.HasKey(s.Id), checking = Engine.Checking(s.Id);
            ServiceStatus st;
            g.Status.TryGetValue(s.Id, out st);
            string ends = set ? (tail != "" ? "Key ends in ..." + tail + ". " : "A key is saved. ") : "";
            string msg, cls = word;
            if (keyErrors.TryGetValue(s.Id, out err)) { msg = err; cls = "fail"; }
            else if (checking) { msg = ends + "Checking..."; cls = "untested"; }
            else if (word == "none") msg = "Get a key at " + s.Where + ".";
            else if (st != null) msg = ends + st.Message;
            else if (s.Local) msg = (ServiceInUse(s, g) ? "Not checked yet. " : "Not in use. ") + s.Where + ".";
            else msg = ends + (s.FreeCheck ? "Not checked yet." : "No free check: use \"Make a test photo\" under Pictures to try it with one real picture.");
            if (s.Local && st == null && !ServiceInUse(s, g)) cls = "none";
            string v;
            var p = s.TextId != "" ? Providers.Get(s.TextId) : null;
            var ip = s.ImageId != "" ? ImageProviders.Get(s.ImageId) : null;
            string address = "", defaultAddress = "";
            if (p != null) { address = g.BaseUrls.TryGetValue(p.Id, out v) ? v ?? "" : ""; defaultAddress = p.BaseUrl; }
            else if (s.Id == Services.ImageServer) { address = g.ImageBaseUrls.TryGetValue(ip.Id, out v) ? v ?? "" : ""; defaultAddress = ip.BaseUrl; }
            return new Dictionary<string, object>
            {
                { "id", s.Id }, { "name", s.Name }, { "does", s.Does }, { "local", s.Local }, { "set", set },
                { "cls", cls }, { "word", cls == "ok" ? "Working" : cls == "fail" ? "Not working" : cls == "untested" ? (s.Local ? "Not checked" : "Saved, not checked") : s.Local ? "Not in use" : "Not set" },
                { "msg", msg },
                { "placeholder", set ? "Paste a new key to replace it" : s.Local ? "Key, if your server needs one" : "Paste your key" + (s.KeyHint != "" ? " (" + s.KeyHint + ")" : "") },
                { "canTest", s.FreeCheck && (set || !s.NeedsKey) && !checking },
                { "hasAddress", defaultAddress != "" }, { "address", address }, { "defaultAddress", defaultAddress },
                { "more", OpenService == s.Id || s.Local },
                { "canMore", defaultAddress != "" && !s.Local },
            };
        }

        Func<string, string, bool, bool, object> Chip = (label, action, on, dead) => new Dictionary<string, object> { { "label", label }, { "action", action }, { "on", on }, { "dead", dead } };

        object TextJob(string job, GlobalSettings g)
        {
            bool writer = job == "writer";
            string pick = writer ? g.ProviderId : (g.CheapProviderId != "" ? g.CheapProviderId : g.ProviderId);
            var route = writer ? g.Writer : g.Cheap;
            var m = new Dictionary<string, object>
            {
                { "id", job }, { "isText", true }, { "pickModel", false }, { "fixed", false }, { "open", OpenJob == job },
                { "label", writer ? "Big stories" : "Everything else" },
                { "note", writer ? "The writer model: major stories and historic firsts, your assistant, the engineers, the science office and Kerbopedia."
                    : "The cheap model: briefs, quiet-day features, comments, the forum, mail, crew chat, livestream chat and summaries." },
                { "current", route.Preset.Name + " · " + route.Model }, { "warn", "" },
            };
            if (!route.Ready) m["warn"] = route.Preset.Name + " has no key yet. Paste one above, or pick another service.";
            else if (!writer && pick != route.Preset.Id) m["warn"] = Providers.Get(pick).Name + " has no key, so " + route.Preset.Name + " does this job for now.";
            if (OpenJob != job) return m;
            m["picks"] = Providers.All.Where(p => p.Id == pick || !Services.Get(p.Id).Minor || g.ServiceReady(p.Id))
                .Select(p => Chip(p.Name + (g.ServiceReady(p.Id) ? "" : " (needs a key)"), "job-service/" + job + "/" + p.Id, p.Id == pick, !g.ServiceReady(p.Id) && p.Id != pick)).ToList();
            string model = writer ? g.WriterModelOf(pick) : g.CheapModelOf(pick);
            m["modelPicks"] = g.ModelChoicesFor(pick).Select(x => Chip(x, "job-model/" + job + "/" + x, x == model, false)).ToList();
            m["serviceName"] = Providers.Get(pick).Name;
            m["sees"] = Providers.CanSee(Providers.Get(pick), model) ? "This model can look at pictures." : "This model cannot look at pictures.";
            return m;
        }

        object LookJob(GlobalSettings g)
        {
            var cheap = g.Cheap;
            var helper = g.Looker;
            bool sees = cheap.Sees;
            string current = !g.PhotoLook ? "Off" : sees ? cheap.Preset.Name + " · " + cheap.Model + " (the Everything else model can see)"
                : helper != null ? helper.Preset.Name + " · " + helper.Model : "Nobody";
            var m = new Dictionary<string, object>
            {
                { "id", "look" }, { "isText", false }, { "pickModel", true }, { "fixed", false }, { "open", OpenJob == "look" }, { "label", "Looking at photos" },
                { "note", "Each photo is looked at once, and the stream camera at big moments. What was seen then feeds articles, comments, Kerbopedia and the stream chat at no extra cost." },
                { "current", current }, { "warn", "" }, { "modelPicks", new List<object>() }, { "serviceName", "" },
            };
            if (g.PhotoLook && !sees && helper == null)
                m["warn"] = cheap.Preset.Name + " · " + cheap.Model + " can't see pictures. Pick a service below that can, or photos go undescribed.";
            if (OpenJob != "look") return m;
            var chips = new List<object> { Chip("Off", "job-look/off", !g.PhotoLook, false) };
            if (sees) chips.Add(Chip(cheap.Preset.Name + " (the Everything else model)", "job-look/self", g.PhotoLook, false));
            else
            {
                chips.Add(Chip("Nobody", "job-look/nobody", g.PhotoLook && g.VisionProviderId == "", false));
                foreach (var p in Providers.All.Where(p => p.NeedsKey && p.Id != cheap.Preset.Id && p.Kind != ApiKind.Ollama && Providers.CanSee(p, g.CheapModelOf(p.Id))))
                    chips.Add(Chip(p.Name + (g.ServiceReady(p.Id) ? "" : " (needs a key)"), "job-look/" + p.Id, g.PhotoLook && g.VisionProviderId == p.Id, !g.ServiceReady(p.Id) && g.VisionProviderId != p.Id));
            }
            m["picks"] = chips;
            if (!sees && helper != null)
            {
                // The helper's own model: any of its models that can look at pictures.
                string hid = helper.Preset.Id;
                var models = g.ModelChoicesFor(hid).Where(x => Providers.CanSee(helper.Preset, x)).ToList();
                if (!models.Contains(helper.Model)) models.Insert(0, helper.Model);
                m["modelPicks"] = models.Select(x => Chip(x, "job-model/look/" + x, x == helper.Model, false)).ToList();
                m["serviceName"] = helper.Preset.Name;
            }
            return m;
        }

        object ImageJob(GlobalSettings g)
        {
            var p = g.ImagePreset;
            var m = new Dictionary<string, object>
            {
                { "id", "images" }, { "isText", false }, { "pickModel", true }, { "fixed", false }, { "open", OpenJob == "images" }, { "label", "Article photos" },
                { "note", "Optional. An image service draws one press photo for bigger stories: one image call per story that qualifies. Which stories qualify is under Pictures." },
                { "current", p == null ? "Off" : p.Name + " · " + g.ImageModel }, { "warn", "" }, { "serviceName", "" },
            };
            if (p != null && !g.ImagesOn) m["warn"] = Services.Get(Services.ForImage(p.Id)).Name + " has no key yet, so no photos are drawn. Paste one above.";
            if (OpenJob != "images") return m;
            var chips = new List<object> { Chip("Off", "job-images/off", p == null, false) };
            foreach (var x in ImageProviders.All)
            {
                bool ready = g.ServiceReady(Services.ForImage(x.Id)), on = p != null && p.Id == x.Id;
                chips.Add(Chip(x.Name + (ready ? "" : " (needs a key)"), "job-images/" + x.Id, on, !ready && !on));
            }
            m["picks"] = chips;
            m["modelPicks"] = new List<object>();
            if (p != null)
            {
                var models = new List<string>(p.Models);
                if (g.ImageModel != "" && !models.Contains(g.ImageModel)) models.Insert(0, g.ImageModel);
                m["modelPicks"] = models.Select(x => Chip(x, "job-model/images/" + x, x == g.ImageModel, false)).ToList();
                m["serviceName"] = p.Name;
            }
            return m;
        }

        object VoiceJob(GlobalSettings g)
        {
            bool key = g.VoiceKey != "";
            var m = new Dictionary<string, object>
            {
                { "id", "voice" }, { "isText", false }, { "pickModel", false }, { "fixed", true }, { "open", false }, { "label", "Announcer voice" },
                { "note", "The livestream announcer and the flight controller. The voices, style and volume are under Livestream announcer below." },
                { "current", g.Announcer == 0 ? "Off" : !key ? "Stock lines and captions only" : !g.VoiceEnabled ? "Captions only (speaking is turned off)" : "ElevenLabs · " + Engine.VoiceLabel(g.VoiceId) + " (" + g.VoiceModel + ")" }, { "warn", "" },
            };
            if (g.Announcer > 0 && !key) m["warn"] = "With an ElevenLabs key above, the announcer speaks every line. Without one it speaks the stock lines that come with the mod.";
            else if (g.Announcer > 0 && g.StatusWord(Services.ElevenLabs) == "fail") m["warn"] = "The ElevenLabs key isn't working, so only the stock lines play.";
            return m;
        }

        /// <summary>Service card and job actions. Returns null when the verb isn't one of these.</summary>
        ActionResult HandleServices(string verb, string arg, IDictionary<string, string> form)
        {
            var g = Engine.Settings;
            var r = new ActionResult();
            string a = arg, b = "";
            int slash = arg.IndexOf('/');
            if (slash >= 0) { a = arg.Substring(0, slash); b = arg.Substring(slash + 1); }
            switch (verb)
            {
                case "svc-fold":
                    if (arg == "more") ShowMoreServices = !ShowMoreServices; else ShowLocalServices = !ShowLocalServices;
                    return r;
                case "svc-more": OpenService = OpenService == arg ? "" : arg; return r;
                case "svc-save":
                {
                    var s = Services.Get(arg);
                    if (s == null) return r;
                    SaveService(s, g, form);
                    return r;
                }
                case "svc-test":
                {
                    var s = Services.Get(arg);
                    if (s == null) return r;
                    keyErrors.Remove(s.Id);
                    g.Status.Remove(s.Id);
                    Engine.CheckService(s.Id, (ok, msg) => { if (!ok && !g.Status.ContainsKey(s.Id)) keyErrors[s.Id] = msg; SaveSettings(); Changed(); });
                    return r;
                }
                case "svc-remove":
                    g.ForgetKey(arg);
                    keyErrors.Remove(arg);
                    if (arg == Services.ElevenLabs) Engine.ResetVoice();
                    SaveSettings();
                    return r;
                case "job-open": OpenJob = OpenJob == arg ? "" : arg; JobResult = ""; return r;
                case "job-service":
                    if (!TextJobs.Contains(a) || !Providers.All.Any(p => p.Id == b) || !g.ServiceReady(b)) return r;
                    if (a == "writer") g.ProviderId = b; else g.CheapProviderId = b == g.ProviderId ? "" : b;
                    if (g.CheapProviderId == g.ProviderId) g.CheapProviderId = "";
                    JobResult = ""; TestResult = "";
                    SaveSettings();
                    return r;
                case "job-model": SetJobModel(g, a, b); return r;
                case "job-use": SetJobModel(g, arg, Get(form, "model-" + arg).Trim()); return r;
                case "job-fetch":
                {
                    if (!TextJobs.Contains(arg) || Busy) return r;
                    string pid = arg == "writer" ? g.ProviderId : (g.CheapProviderId != "" ? g.CheapProviderId : g.ProviderId);
                    Busy = true; JobOk = true;
                    JobResult = "Asking " + Providers.Get(pid).Name + "...";
                    Engine.FetchModels(pid, (ok, msg) => { Busy = false; JobOk = ok; JobResult = msg; if (ok) SaveSettings(); Changed(); });
                    return r;
                }
                case "job-test":
                {
                    if (!TextJobs.Contains(arg) || Busy) return r;
                    var route = arg == "writer" ? g.Writer : g.Cheap;
                    Busy = true; JobOk = true;
                    JobResult = "Sending one small request to " + route.Preset.Name + " · " + route.Model + "...";
                    Engine.TestConnection(route, (ok, msg) => { Busy = false; JobOk = ok; JobResult = msg; Changed(); });
                    return r;
                }
                case "job-look":
                    if (arg == "off") g.PhotoLook = false;
                    else if (arg == "self") g.PhotoLook = true;
                    else if (arg == "nobody") { g.PhotoLook = true; g.VisionProviderId = ""; }
                    else if (Providers.All.Any(p => p.Id == arg) && g.ServiceReady(arg)) { g.PhotoLook = true; if (g.VisionProviderId != arg) g.VisionModel = ""; g.VisionProviderId = arg; }
                    SaveSettings();
                    return r;
                case "job-images":
                    if (arg == "off") g.ImageProviderId = "";
                    else if (ImageProviders.Get(arg) != null && g.ServiceReady(Services.ForImage(arg))) g.ImageProviderId = arg;
                    ImageTestResult = ""; ImageTestFile = "";
                    SaveSettings();
                    return r;
            }
            return null;
        }

        void SetJobModel(GlobalSettings g, string job, string model)
        {
            if (model == "") return;
            if (job == "writer") g.WriterModels[g.ProviderId] = model;
            else if (job == "cheap") g.CheapModels[g.CheapProviderId != "" ? g.CheapProviderId : g.ProviderId] = model;
            else if (job == "images" && g.ImagePreset != null) g.ImageModels[g.ImagePreset.Id] = model;
            else if (job == "look" && g.VisionProviderId != "") g.VisionModel = model;
            else return;
            JobResult = "";
            SaveSettings();
        }

        /// <summary>Saves a card: the pasted key (checked first), and the address when the card has one. A good key is then checked for free.</summary>
        void SaveService(AiService s, GlobalSettings g, IDictionary<string, string> form)
        {
            keyErrors.Remove(s.Id);
            string err, before = g.KeyOf(s.Id);
            if (!g.SetKey(s.Id, Get(form, "key-" + s.Id), out err)) { keyErrors[s.Id] = err; return; }
            if (form.ContainsKey("addr-" + s.Id))
            {
                string url = form["addr-" + s.Id].Trim().TrimEnd('/');
                if (url != "" && !(url.StartsWith("http://") || url.StartsWith("https://")) || url.Any(char.IsWhiteSpace)) { keyErrors[s.Id] = "The address must start with http:// or https://."; return; }
                var addresses = s.TextId != "" ? g.BaseUrls : g.ImageBaseUrls;
                string id = s.TextId != "" ? s.TextId : s.ImageId, old;
                addresses.TryGetValue(id, out old);
                if ((old ?? "") != url) { addresses[id] = url; g.Status.Remove(s.Id); }
            }
            bool newKey = g.KeyOf(s.Id) != before;
            if (newKey && s.TextId != "")
            {
                // A first text key means "use the AI": it becomes the writer when the current one has no key.
                if (!g.Writer.Ready) g.ProviderId = s.TextId;
                g.OfflineMode = false;
            }
            if (newKey && s.Voice) { g.VoiceEnabled = true; Engine.ResetVoice(); }
            SaveSettings();
            if (s.FreeCheck && (g.HasKey(s.Id) || !s.NeedsKey) && !g.Status.ContainsKey(s.Id))
                Engine.CheckService(s.Id, (ok, msg) => { SaveSettings(); Changed(); });
        }
    }
}
