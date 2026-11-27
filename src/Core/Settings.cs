using System;
using System.Collections.Generic;
using System.Linq;

namespace KerbalNews
{
    /// <summary>
    /// Settings shared by every save: which AI provider to use and its keys.
    /// Stored in GameData/KerbalNews/PluginData (keys in their own secrets file),
    /// never in the save, so saves can be shared without leaking keys.
    /// </summary>
    public class GlobalSettings
    {
        /// <summary>The service that writes the big stories (the writer model). Also the fallback for every other text job.</summary>
        public string ProviderId = "deepseek";
        /// <summary>The service that does everything else (the cheap model). "" = the same one as the writer.</summary>
        public string CheapProviderId = "";
        public bool OfflineMode = true; // no key yet: templates until the player picks a provider
        /// <summary>Press photographers take long-lens shots of launches from a separate camera.</summary>
        public bool PressPhotos = true;
        /// <summary>The personal assistant chat on the agency site, and whether it asks before spending money.</summary>
        public bool AssistantOn = true;
        public bool AssistantConfirm = true;
        /// <summary>The AI looks at photos and the livestream camera (only models that can see; never offline).</summary>
        public bool PhotoLook = true;
        /// <summary>When the cheap model can't see: another service that looks at photos, with that service's key. "" = none.</summary>
        public string VisionProviderId = "";
        /// <summary>Talking to your crew on the Kerbonauts site, and KTV segments written with each story.</summary>
        public bool CrewChatOn = true;
        /// <summary>The livestream announcer: 0 off, 1 big moments (countdown, liftoff, orbit, landings, trouble), 2 full commentary.</summary>
        public int Announcer = 1;
        /// <summary>The announcer speaks through ElevenLabs when its key is saved and this is on; otherwise captions only.</summary>
        public bool VoiceEnabled = true;
        public string VoiceId = Voice.DefaultVoice;
        public string VoiceModel = Voice.DefaultModel;
        public double VoiceVolume = 0.8;
        /// <summary>"calm" (mission control) or "excited" (sportscaster): the delivery directions and voice settings.</summary>
        public string AnnouncerStyle = "calm";
        /// <summary>The flight controller: a second, calm voice on the radio loop for the callouts and the go/no-go poll.</summary>
        public bool ControllerOn = true;
        public string ControllerVoiceId = Voice.DefaultController;
        /// <summary>Crowd, applause and control-room sound under the stream: 0 off, up to 1.</summary>
        public double SfxVolume = 0.6;
        /// <summary>The stream player's own volume slider: scales the announcer and the sounds, live and in replays.</summary>
        public double StreamVolume = 1;
        /// <summary>Voices loaded from the player's ElevenLabs account: "id|name|description;..." (not secret).</summary>
        public string FetchedVoices = "";
        public List<string[]> FetchedVoiceList
        {
            get
            {
                return FetchedVoices.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split('|')).Where(p => p.Length >= 2 && p[0] != "")
                    .Select(p => new[] { p[0], p[1], p.Length > 2 ? p[2] : "" }).ToList();
            }
        }
        public const string VoiceKeyId = Services.ElevenLabs;
        public string VoiceKey { get { return KeyOf(VoiceKeyId); } }
        public bool VoiceOn { get { return VoiceEnabled && Announcer > 0 && VoiceKey != ""; } }
        /// <summary>Stream replays: 0 off, 1 normal (a frame a second), 2 smooth (three a second). Oldest are deleted past ReplayMaxGb.</summary>
        public int ReplayQuality = 1;
        public int ReplayMaxGb = 3;
        public double ReplayFps { get { return ReplayQuality >= 2 ? 3 : ReplayQuality == 1 ? 1 : 0; } }
        public bool KtvOn = true;
        public bool EngineeringOn = true;
        /// <summary>News and public outcry sometimes post a real contract (career only).</summary>
        public bool ContractTieIns = true;

        // ---- API usage. "balanced" is how the mod has always behaved; "saver" and "full" move every knob at once.
        public string Usage = "balanced";
        /// <summary>Days of quiet before a feature or opinion piece is written; 0 turns them off.</summary>
        public double QuietDays = 3;
        /// <summary>Scales article length, comment counts and social posts (output tokens).</summary>
        public double AmountScale = 1;
        /// <summary>Most outlets that cover one story; 0 means no limit.</summary>
        public int MaxOutlets = 0;
        /// <summary>Smallest story tier written by the writer model: "story" (default), "major" (saves money) or "brief" (everything).</summary>
        public string WriterFrom = "story";
        /// <summary>Seconds between livestream chat updates.</summary>
        public double StreamSeconds = 30;
        /// <summary>Livestream chat messages in a quiet ten-minute stretch. Big moments (liftoff, explosions, landings) go well above it.</summary>
        public int StreamChatPer10Min = 120;
        /// <summary>Anniversary retrospectives and the yearly review.</summary>
        public bool Retrospectives = true;
        /// <summary>Background world news (asteroids, windows, crew milestones...): 0 off, 1 a little, 2 normal, 3 lots.</summary>
        public int WorldNews = 2;
        /// <summary>
        /// How much of the generated text the AI writes, on every site (the rest comes from templates):
        /// 0 nothing, 1 big news only, 2 most, 3 almost everything (default), 4 everything, small events included.
        /// Things the player asks for directly (assistant, crew chat, engineers, replies to mail) always use the AI when it is on.
        /// </summary>
        public int AiAmount = 3;
        public const int AiBig = 1, AiMost = 2, AiAlmostAll = 3, AiAll = 4;
        /// <summary>True when the AI is on and the slider is at or above this stop.</summary>
        public bool AiWrites(int stop) { return CanCallAi && AiAmount >= stop; }
        /// <summary>Whether news of this coverage tier is AI-written at the current slider stop.</summary>
        public bool AiWritesTier(string tier)
        {
            if (!CanCallAi) return false;
            if (AiAmount >= AiAlmostAll) return true;
            if (AiAmount == AiMost) return tier != "brief" && tier != "feature";
            if (AiAmount == AiBig) return tier == "major" || tier == "historic";
            return false;
        }
        /// <summary>Kmail volume: 0 off, 1 less, 2 normal, 3 more.</summary>
        public int MailLevel = 2;
        /// <summary>Invented color around the game facts (weather, politics, culture), and rival programs (Kerbin orbit only).</summary>
        public bool WorldFlavor = true;
        public bool WorldRivals = true;

        public static readonly string[] UsagePresets = { "saver", "balanced", "full" };

        /// <summary>The "How much the AI writes" stop also sets the spend knobs: Nothing/Big news = Saver, Most/Almost everything = Balanced, Everything = Full. Feature switches (KTV, anniversaries) are left alone.</summary>
        public void ApplyAiAmount(int stop)
        {
            AiAmount = Math.Max(0, Math.Min(4, stop));
            bool ktv = KtvOn, retro = Retrospectives;
            ApplyUsage(AiAmount <= AiBig ? "saver" : AiAmount >= AiAll ? "full" : "balanced");
            KtvOn = ktv; Retrospectives = retro;
        }

        /// <summary>Kmail pop-ups in flight, separate from the news message.</summary>
        public bool MailPopups = true;
        /// <summary>How soon news comes out after an event: 0.35 fast, 1 normal, 2.5 slow (times the outlet's usual delay).</summary>
        public double NewsDelay = 1;

        public void ApplyUsage(string preset)
        {
            Usage = preset;
            switch (preset)
            {
                case "saver": QuietDays = 0; AmountScale = 0.6; MaxOutlets = 3; WriterFrom = "major"; StreamSeconds = 60; Retrospectives = false; KtvOn = false; WorldNews = 1; break;
                case "full": QuietDays = 2; AmountScale = 1.3; MaxOutlets = 0; WriterFrom = "brief"; StreamSeconds = 20; Retrospectives = true; KtvOn = true; WorldNews = 3; break;
                default: Usage = "balanced"; QuietDays = 3; AmountScale = 1; MaxOutlets = 0; WriterFrom = "story"; StreamSeconds = 30; Retrospectives = true; KtvOn = true; WorldNews = 2; break;
            }
        }

        /// <summary>The preset the knobs match, or "custom".</summary>
        public string UsageMatch
        {
            get
            {
                foreach (var p in UsagePresets)
                {
                    var t = new GlobalSettings(); t.ApplyUsage(p);
                    if (t.QuietDays == QuietDays && Math.Abs(t.AmountScale - AmountScale) < 0.01 && t.MaxOutlets == MaxOutlets && t.WriterFrom == WriterFrom
                        && t.StreamSeconds == StreamSeconds && t.Retrospectives == Retrospectives && t.KtvOn == KtvOn && t.WorldNews == WorldNews) return p;
                }
                return "custom";
            }
        }
        public Dictionary<string, string> BaseUrls = new Dictionary<string, string>();
        public Dictionary<string, string> WriterModels = new Dictionary<string, string>();
        public Dictionary<string, string> CheapModels = new Dictionary<string, string>();
        /// <summary>
        /// One key per service (Services), by service id. Read with KeyOf and change with SetKey and ForgetKey, which
        /// check a pasted key and reset the service's status.
        /// </summary>
        public Dictionary<string, string> Keys = new Dictionary<string, string>();
        /// <summary>What the last check said about each service's key. This session only; never saved.</summary>
        public Dictionary<string, ServiceStatus> Status = new Dictionary<string, ServiceStatus>();
        /// <summary>Models fetched live from each provider (space-separated), kept between sessions.</summary>
        public Dictionary<string, string> FetchedModels = new Dictionary<string, string>();
        public int MaxTokens = 6000;
        public int TimeoutSeconds = 120;
        /// <summary>Local model servers: context window asked of Ollama (tokens), and the longer time limit per request (seconds).</summary>
        public int LocalContext = 16384;
        public int LocalTimeoutSeconds = 600;
        public bool NotifyOnPublish = true;
        public bool GenerateDuringWarp = false;
        /// <summary>News window scale; 0 means automatic from screen size.</summary>
        public float UiScale = 0;
        /// <summary>Page zoom from the toolbar's A− and A+: the page's text and pictures grow, the window and toolbar don't.</summary>
        public float PageZoom = 1;
        /// <summary>The browser window: x, y, width, height in canvas units, or null for the default.</summary>
        public float[] BrowserRect;
        /// <summary>Websites the player turned off (SiteCatalog ids). The engineering page uses EngineeringOn instead.</summary>
        public HashSet<string> SitesOff = new HashSet<string>();
        public bool SiteOn(string id) { return id == "engineering" ? EngineeringOn : !SitesOff.Contains(id); }
        public void SetSite(string id, bool on)
        {
            if (id == "engineering") { EngineeringOn = on; return; }
            if (on) SitesOff.Remove(id); else SitesOff.Add(id);
        }
        /// <summary>Image generation for article photos: "" means off. Uses the key of the service that preset belongs to.</summary>
        public string ImageProviderId = "";
        public Dictionary<string, string> ImageModels = new Dictionary<string, string>();
        public Dictionary<string, string> ImageBaseUrls = new Dictionary<string, string>();
        /// <summary>Smallest story tier that gets a photo: story, major or historic.</summary>
        public string ImageMinTier = "major";
        /// <summary>Installed mods, found by scanning GameData at startup; the AI is told about the included ones.</summary>
        public List<ModInfo> Mods = new List<ModInfo>();
        public bool ModsScanned;
        /// <summary>Whether the AI hears about installed mods at all.</summary>
        public bool ModsInContext = true;
        public string ModsText { get { return ModsInContext ? ModCatalog.Summary(Mods) : ""; } }

        public ImagePreset ImagePreset { get { return ImageProviders.Get(ImageProviderId); } }
        public string ImageModel { get { var p = ImagePreset; return p == null ? "" : Lookup(ImageModels, p.Id, p.Model); } }
        public string ImageBaseUrl { get { var p = ImagePreset; return p == null ? "" : Lookup(ImageBaseUrls, p.Id, p.BaseUrl); } }
        public string ImageKey { get { return ImageProviderId == "" ? "" : ImageKeyOf(ImageProviderId); } }
        /// <summary>
        /// The key an image preset uses: its service's key. A file from before keys were saved once per service may hold
        /// a second, different key for pictures ("img:id"); that one is still used for pictures until the service's key is saved again.
        /// </summary>
        public string ImageKeyOf(string imageId) { return Lookup(Keys, LegacyImagePrefix + imageId, KeyOf(Services.ForImage(imageId))); }
        const string LegacyImagePrefix = "img:";
        public bool ImagesOn { get { return ImagePreset != null && (ImageKey != "" || ImageProviderId == "custom") && ImageModel != ""; } }

        public ProviderPreset Preset { get { return Providers.Get(ProviderId); } }

        static string Lookup(Dictionary<string, string> d, string id, string def)
        {
            string v;
            return d.TryGetValue(id, out v) && !string.IsNullOrEmpty(v) ? v : def;
        }

        // ---- keys: one per service

        public string KeyOf(string serviceId) { return Lookup(Keys, serviceId ?? "", ""); }
        public bool HasKey(string serviceId) { return KeyOf(serviceId) != ""; }
        /// <summary>The last four characters of a saved key, for "Key ends in ...".</summary>
        public string KeyTail(string serviceId) { return Services.Tail(KeyOf(serviceId)); }

        /// <summary>
        /// Saves a pasted key for a service. A blank field keeps the saved key. False with a reason when the text
        /// cannot be a key; nothing is changed then.
        /// </summary>
        public bool SetKey(string serviceId, string raw, out string error)
        {
            string key;
            if (!Services.CleanKey(raw, out key, out error)) return false;
            if (key == "" || string.IsNullOrEmpty(serviceId)) return true;
            if (KeyOf(serviceId) != key) Status.Remove(serviceId);
            Keys[serviceId] = key;
            var svc = Services.Get(serviceId);
            if (svc != null && svc.ImageId != "") Keys.Remove(LegacyImagePrefix + svc.ImageId);
            return true;
        }

        public void ForgetKey(string serviceId)
        {
            if (string.IsNullOrEmpty(serviceId)) return;
            Keys.Remove(serviceId);
            Status.Remove(serviceId);
            var svc = Services.Get(serviceId);
            if (svc != null && svc.ImageId != "") Keys.Remove(LegacyImagePrefix + svc.ImageId);
        }

        /// <summary>A service is ready when it has its key, or needs none.</summary>
        public bool ServiceReady(string serviceId)
        {
            var svc = Services.Get(serviceId);
            return svc != null && (!svc.NeedsKey || HasKey(serviceId));
        }

        /// <summary>"ok", "fail", "untested" (a key nobody has checked) or "none" (no key).</summary>
        public string StatusWord(string serviceId)
        {
            var svc = Services.Get(serviceId);
            ServiceStatus st;
            if (Status.TryGetValue(serviceId ?? "", out st)) return st.Ok ? "ok" : "fail";
            if (svc != null && svc.NeedsKey && !HasKey(serviceId)) return "none";
            return "untested";
        }

        /// <summary>
        /// Secrets files from before keys were saved once per service kept the announcer's key as "voice:elevenlabs" and a
        /// second key per company for pictures as "img:id". Each moves to its service. A picture key that differs from
        /// the service's own key is kept as it was, so no key is lost.
        /// </summary>
        static void MigrateKeys(Dictionary<string, string> keys)
        {
            string v;
            if (keys.TryGetValue("voice:" + Services.ElevenLabs, out v))
            {
                if (Lookup(keys, Services.ElevenLabs, "") == "") keys[Services.ElevenLabs] = v;
                keys.Remove("voice:" + Services.ElevenLabs);
            }
            foreach (var id in keys.Keys.Where(k => k.StartsWith(LegacyImagePrefix)).ToList())
            {
                string svc = Services.ForImage(id.Substring(LegacyImagePrefix.Length)), key = keys[id];
                if (svc == "" || string.IsNullOrEmpty(key)) { keys.Remove(id); continue; }
                string own = Lookup(keys, svc, "");
                if (own == "") keys[svc] = key;
                if (own == "" || own == key) keys.Remove(id);
            }
            keys.Remove("");
        }

        // ---- what uses what: the service and model of each text job

        public string BaseUrlOf(string providerId) { return Lookup(BaseUrls, providerId, Providers.Get(providerId).BaseUrl); }
        public string WriterModelOf(string providerId) { return Lookup(WriterModels, providerId, Providers.Get(providerId).WriterModel); }
        public string CheapModelOf(string providerId) { return Lookup(CheapModels, providerId, Providers.Get(providerId).CheapModel); }

        AiRoute RouteTo(string providerId, string model)
        {
            var p = Providers.Get(providerId);
            return new AiRoute { Preset = p, BaseUrl = BaseUrlOf(p.Id), Key = KeyOf(Services.ForText(p.Id)), Model = model };
        }

        /// <summary>Big stories, the assistant, the engineers: the writer model.</summary>
        public AiRoute Writer { get { return RouteTo(ProviderId, WriterModelOf(ProviderId)); } }

        /// <summary>
        /// Everything else: the cheap model, on its own service when one is picked. A pick whose service has no key
        /// falls back to the writer's service, so the job still runs.
        /// </summary>
        public AiRoute Cheap
        {
            get
            {
                string id = CheapServiceId;
                return RouteTo(id, CheapModelOf(id));
            }
        }

        /// <summary>
        /// The helper that looks at photos when the cheap model can't see: the picked service with its own key and its
        /// cheap model. Null when nobody is picked, or the pick has no key or can't see.
        /// </summary>
        public AiRoute Looker
        {
            get
            {
                if (VisionProviderId == "" || !Providers.All.Any(p => p.Id == VisionProviderId)) return null;
                var r = RouteTo(VisionProviderId, CheapModelOf(VisionProviderId));
                return r.Ready && r.Sees ? r : null;
            }
        }

        /// <summary>The service the cheap model really runs on: the pick when it is ready, else the writer's.</summary>
        public string CheapServiceId
        {
            get { return CheapProviderId != "" && CheapProviderId != ProviderId && Providers.All.Any(p => p.Id == CheapProviderId) && ServiceReady(CheapProviderId) ? CheapProviderId : ProviderId; }
        }

        public string BaseUrl { get { return BaseUrlOf(ProviderId); } }
        public string WriterModel { get { return WriterModelOf(ProviderId); } }
        public string CheapModel { get { return Cheap.Model; } }
        public string Key { get { return KeyOf(Services.ForText(ProviderId)); } }

        /// <summary>The addresses of the local model servers in use, for queueing their requests one at a time.</summary>
        public string LocalRoot
        {
            get
            {
                if (!CanCallAi) return null;
                if (Providers.IsLocal(BaseUrl)) return BaseUrl;
                string c = Cheap.BaseUrl;
                return Providers.IsLocal(c) ? c : null;
            }
        }

        /// <summary>Suggested plus fetched models for the writer's provider, without duplicates.</summary>
        public List<string> ModelChoices { get { return ModelChoicesFor(ProviderId); } }

        /// <summary>Suggested plus fetched models for a provider, with the ones in use first, without duplicates.</summary>
        public List<string> ModelChoicesFor(string providerId)
        {
            var p = Providers.Get(providerId);
            var l = new List<string>(p.Models);
            foreach (var m in Lookup(FetchedModels, p.Id, "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (!l.Contains(m)) l.Add(m);
            string w = WriterModelOf(p.Id), c = CheapModelOf(p.Id);
            if (!l.Contains(w)) l.Insert(0, w);
            if (!l.Contains(c)) l.Insert(0, c);
            return l;
        }

        public bool CanCallAi
        {
            get { return !OfflineMode && (!Preset.NeedsKey || Key != ""); }
        }

        public void Save(DataNode settings, DataNode secrets)
        {
            settings.Add("provider", ProviderId);
            settings.Add("cheapProvider", CheapProviderId);
            settings.Add("offline", OfflineMode);
            settings.Add("maxTokens", MaxTokens);
            settings.Add("timeout", TimeoutSeconds);
            settings.Add("localContext", LocalContext);
            settings.Add("localTimeout", LocalTimeoutSeconds);
            settings.Add("notify", NotifyOnPublish);
            settings.Add("warpGeneration", GenerateDuringWarp);
            settings.Add("pressPhotos", PressPhotos);
            settings.Add("assistant", AssistantOn);
            settings.Add("assistantConfirm", AssistantConfirm);
            settings.Add("photoLook", PhotoLook);
            settings.Add("mailPopups", MailPopups);
            settings.Add("newsDelay", NewsDelay);
            settings.Add("visionProvider", VisionProviderId);
            settings.Add("crewChat", CrewChatOn);
            settings.Add("announcer", Announcer);
            settings.Add("voiceOn", VoiceEnabled);
            settings.Add("voiceId", VoiceId);
            settings.Add("voiceModel", VoiceModel);
            settings.Add("voiceVolume", VoiceVolume);
            settings.Add("announcerStyle", AnnouncerStyle);
            settings.Add("controllerOn", ControllerOn);
            settings.Add("controllerVoice", ControllerVoiceId);
            settings.Add("sfxVolume", SfxVolume);
            settings.Add("streamVolume", StreamVolume);
            settings.Add("voiceDefaults2", true);
            settings.AddText("voices", FetchedVoices);
            settings.Add("replays", ReplayQuality);
            settings.Add("replayMaxGb", ReplayMaxGb);
            settings.Add("ktv", KtvOn);
            settings.Add("engineering", EngineeringOn);
            settings.Add("contractTieIns", ContractTieIns);
            settings.Add("usage", Usage);
            settings.Add("quietDays", QuietDays);
            settings.Add("amountScale", AmountScale);
            settings.Add("maxOutlets", MaxOutlets);
            settings.Add("writerFrom", WriterFrom);
            settings.Add("streamSeconds", StreamSeconds);
            settings.Add("streamChat", StreamChatPer10Min);
            settings.Add("retrospectives", Retrospectives);
            settings.Add("worldNews", WorldNews);
            settings.Add("mailLevel", MailLevel);
            settings.Add("aiAmount", AiAmount);
            settings.Add("worldFlavor", WorldFlavor);
            settings.Add("worldRivals", WorldRivals);
            settings.Add("uiScale", (double)UiScale);
            settings.Add("pageZoom", (double)PageZoom);
            settings.Add("sitesOff", string.Join(",", SitesOff.OrderBy(x => x).ToArray()));
            if (BrowserRect != null && BrowserRect.Length == 4) settings.Add("browserRect", string.Join(",", BrowserRect.Select(v => v.ToString("0", System.Globalization.CultureInfo.InvariantCulture)).ToArray()));
            settings.Add("imageProvider", ImageProviderId);
            settings.Add("imageMinTier", ImageMinTier);
            settings.Add("modsScanned", ModsScanned);
            settings.Add("modsInContext", ModsInContext);
            foreach (var m in Mods) m.Save(settings.AddNode("MOD"));
            foreach (var p in ImageProviders.All)
            {
                var n = settings.AddNode("IMAGEPROVIDER");
                n.Add("id", p.Id);
                n.AddText("model", Lookup(ImageModels, p.Id, ""));
                n.AddText("baseUrl", Lookup(ImageBaseUrls, p.Id, ""));
            }
            foreach (var p in Providers.All)
            {
                var n = settings.AddNode("PROVIDER");
                n.Add("id", p.Id);
                n.AddText("baseUrl", Lookup(BaseUrls, p.Id, ""));
                n.AddText("writer", Lookup(WriterModels, p.Id, ""));
                n.AddText("cheap", Lookup(CheapModels, p.Id, ""));
                n.AddText("fetched", Lookup(FetchedModels, p.Id, ""));
            }
            foreach (var kv in Keys)
                if (!string.IsNullOrEmpty(kv.Value)) { var k = secrets.AddNode("KEY"); k.Add("id", kv.Key); k.AddText("key", kv.Value); }
        }

        public static GlobalSettings Load(DataNode settings, DataNode secrets)
        {
            var g = new GlobalSettings();
            if (settings != null)
            {
                g.ProviderId = settings.Get("provider", g.ProviderId);
                g.CheapProviderId = settings.Get("cheapProvider", "");
                g.OfflineMode = settings.GetBool("offline", true);
                g.MaxTokens = Math.Max(1000, settings.GetInt("maxTokens", g.MaxTokens));
                g.LocalContext = Math.Max(2048, Math.Min(262144, settings.GetInt("localContext", g.LocalContext)));
                g.LocalTimeoutSeconds = Math.Max(60, Math.Min(3600, settings.GetInt("localTimeout", g.LocalTimeoutSeconds)));
                g.TimeoutSeconds = Math.Max(10, settings.GetInt("timeout", g.TimeoutSeconds));
                g.NotifyOnPublish = settings.GetBool("notify", true);
                g.GenerateDuringWarp = settings.GetBool("warpGeneration", false);
                g.PressPhotos = settings.GetBool("pressPhotos", true);
                g.AssistantOn = settings.GetBool("assistant", true);
                g.AssistantConfirm = settings.GetBool("assistantConfirm", true);
                g.PhotoLook = settings.GetBool("photoLook", true);
                g.MailPopups = settings.GetBool("mailPopups", true);
                g.NewsDelay = Math.Max(0.2, Math.Min(4, settings.GetDouble("newsDelay", 1)));
                g.VisionProviderId = settings.Get("visionProvider", "");
                g.CrewChatOn = settings.GetBool("crewChat", true);
                g.Announcer = Math.Max(0, Math.Min(2, settings.GetInt("announcer", 1)));
                g.VoiceEnabled = settings.GetBool("voiceOn", true);
                g.VoiceId = settings.Get("voiceId", Voice.DefaultVoice);
                if (g.VoiceId == "") g.VoiceId = Voice.DefaultVoice;
                g.VoiceModel = settings.Get("voiceModel", Voice.DefaultModel);
                if (g.VoiceModel == "") g.VoiceModel = Voice.DefaultModel;
                // 2026-11-08: Chris on v3 became the default; move players still on the first defaults (Adam, Flash) over once.
                if (!settings.GetBool("voiceDefaults2") && g.VoiceId == "pNInz6obpgDQGcFmaJgB" && g.VoiceModel == "eleven_flash_v2_5") { g.VoiceId = Voice.DefaultVoice; g.VoiceModel = Voice.DefaultModel; }
                g.VoiceVolume = Math.Max(0, Math.Min(1, settings.GetDouble("voiceVolume", 0.8)));
                g.AnnouncerStyle = settings.Get("announcerStyle", "calm") == "excited" ? "excited" : "calm";
                g.ControllerOn = settings.GetBool("controllerOn", true);
                g.ControllerVoiceId = settings.Get("controllerVoice", Voice.DefaultController);
                if (g.ControllerVoiceId == "") g.ControllerVoiceId = Voice.DefaultController;
                g.SfxVolume = Math.Max(0, Math.Min(1, settings.GetDouble("sfxVolume", 0.6)));
                g.StreamVolume = Math.Max(0, Math.Min(1, settings.GetDouble("streamVolume", 1)));
                g.FetchedVoices = settings.GetText("voices") ?? "";
                g.ReplayQuality = Math.Max(0, Math.Min(2, settings.GetInt("replays", 1)));
                g.ReplayMaxGb = Math.Max(1, Math.Min(50, settings.GetInt("replayMaxGb", 3)));
                g.KtvOn = settings.GetBool("ktv", true);
                g.EngineeringOn = settings.GetBool("engineering", true);
                g.ContractTieIns = settings.GetBool("contractTieIns", true);
                g.Usage = settings.Get("usage", "balanced");
                g.QuietDays = Math.Max(0, settings.GetDouble("quietDays", 3));
                g.AmountScale = Math.Max(0.3, Math.Min(2, settings.GetDouble("amountScale", 1)));
                g.MaxOutlets = Math.Max(0, settings.GetInt("maxOutlets", 0));
                g.WriterFrom = settings.Get("writerFrom", "story");
                g.StreamSeconds = Math.Max(10, settings.GetDouble("streamSeconds", 30));
                g.StreamChatPer10Min = Math.Max(10, Math.Min(600, settings.GetInt("streamChat", 120)));
                g.Retrospectives = settings.GetBool("retrospectives", true);
                g.WorldNews = Math.Max(0, Math.Min(3, settings.GetInt("worldNews", 2)));
                g.MailLevel = Math.Max(0, Math.Min(3, settings.GetInt("mailLevel", 2)));
                g.AiAmount = Math.Max(0, Math.Min(4, settings.GetInt("aiAmount", 3)));
                g.WorldFlavor = settings.GetBool("worldFlavor", true);
                g.WorldRivals = settings.GetBool("worldRivals", true);
                g.UiScale = (float)settings.GetDouble("uiScale", 0);
                g.PageZoom = (float)Math.Max(0.6, Math.Min(2.5, settings.GetDouble("pageZoom", 1)));
                foreach (var id in settings.Get("sitesOff", "").Split(',')) if (id.Trim() != "") g.SitesOff.Add(id.Trim());
                var br = settings.Get("browserRect", "").Split(',');
                float bx, by, bw, bh;
                if (br.Length == 4 && float.TryParse(br[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out bx)
                    && float.TryParse(br[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out by)
                    && float.TryParse(br[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out bw)
                    && float.TryParse(br[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out bh))
                    g.BrowserRect = new[] { bx, by, bw, bh };
                g.ImageProviderId = settings.Get("imageProvider", "");
                g.ImageMinTier = settings.Get("imageMinTier", "major");
                g.ModsScanned = settings.GetBool("modsScanned");
                g.ModsInContext = settings.GetBool("modsInContext", true);
                foreach (var n in settings.GetNodes("MOD")) { var m = ModInfo.Load(n); if (m.Folder != "") g.Mods.Add(m); }
                foreach (var n in settings.GetNodes("IMAGEPROVIDER"))
                {
                    string id = n.Get("id", "");
                    if (id == "") continue;
                    g.ImageModels[id] = n.GetText("model");
                    g.ImageBaseUrls[id] = n.GetText("baseUrl");
                }
                foreach (var n in settings.GetNodes("PROVIDER"))
                {
                    string id = n.Get("id", "");
                    if (id == "") continue;
                    g.BaseUrls[id] = n.GetText("baseUrl");
                    g.WriterModels[id] = n.GetText("writer");
                    g.CheapModels[id] = n.GetText("cheap");
                    g.FetchedModels[id] = n.GetText("fetched");
                }
            }
            if (secrets != null)
                foreach (var k in secrets.GetNodes("KEY"))
                    g.Keys[k.Get("id", "")] = k.GetText("key");
            MigrateKeys(g.Keys);
            return g;
        }
    }
}
