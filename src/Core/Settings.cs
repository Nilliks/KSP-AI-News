using System;
using System.Collections.Generic;

namespace KerbalNews
{
    /// <summary>
    /// Settings shared by every save: which AI provider to use and its keys.
    /// Stored in GameData/KerbalNews/PluginData (keys in their own secrets file),
    /// never in the save, so saves can be shared without leaking keys.
    /// </summary>
    public class GlobalSettings
    {
        public string ProviderId = "deepseek";
        public bool OfflineMode = true; // no key yet: templates until the player picks a provider
        /// <summary>Press photographers take long-lens shots of launches from a separate camera.</summary>
        public bool PressPhotos = true;
        /// <summary>The personal assistant chat on the agency site, and whether it asks before spending money.</summary>
        public bool AssistantOn = true;
        public bool AssistantConfirm = true;
        /// <summary>Talking to your crew on the Kerbonauts site, and KTV segments written with each story.</summary>
        public bool CrewChatOn = true;
        public bool KtvOn = true;
        public Dictionary<string, string> BaseUrls = new Dictionary<string, string>();
        public Dictionary<string, string> WriterModels = new Dictionary<string, string>();
        public Dictionary<string, string> CheapModels = new Dictionary<string, string>();
        public Dictionary<string, string> Keys = new Dictionary<string, string>();
        /// <summary>Models fetched live from each provider (space-separated), kept between sessions.</summary>
        public Dictionary<string, string> FetchedModels = new Dictionary<string, string>();
        public int MaxTokens = 6000;
        public int TimeoutSeconds = 120;
        public bool NotifyOnPublish = true;
        public bool GenerateDuringWarp = false;
        /// <summary>News window scale; 0 means automatic from screen size.</summary>
        public float UiScale = 0;
        /// <summary>Image generation for article photos: "" means off. Uses its own key, stored as "img:&lt;id&gt;".</summary>
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
        public string ImageKey { get { return ImageProviderId == "" ? "" : Lookup(Keys, "img:" + ImageProviderId, ""); } }
        public bool ImagesOn { get { return ImagePreset != null && (ImageKey != "" || ImageProviderId == "custom") && ImageModel != ""; } }

        public ProviderPreset Preset { get { return Providers.Get(ProviderId); } }

        static string Lookup(Dictionary<string, string> d, string id, string def)
        {
            string v;
            return d.TryGetValue(id, out v) && !string.IsNullOrEmpty(v) ? v : def;
        }

        public string BaseUrl { get { return Lookup(BaseUrls, ProviderId, Preset.BaseUrl); } }
        public string WriterModel { get { return Lookup(WriterModels, ProviderId, Preset.WriterModel); } }
        public string CheapModel { get { return Lookup(CheapModels, ProviderId, Preset.CheapModel); } }
        public string Key { get { return Lookup(Keys, ProviderId, ""); } }

        /// <summary>Suggested plus fetched models for the current provider, without duplicates.</summary>
        public List<string> ModelChoices
        {
            get
            {
                var l = new List<string>(Preset.Models);
                foreach (var m in Lookup(FetchedModels, ProviderId, "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    if (!l.Contains(m)) l.Add(m);
                if (!l.Contains(WriterModel)) l.Insert(0, WriterModel);
                if (!l.Contains(CheapModel)) l.Insert(0, CheapModel);
                return l;
            }
        }

        public bool CanCallAi
        {
            get { return !OfflineMode && (!Preset.NeedsKey || Key != ""); }
        }

        public void Save(DataNode settings, DataNode secrets)
        {
            settings.Add("provider", ProviderId);
            settings.Add("offline", OfflineMode);
            settings.Add("maxTokens", MaxTokens);
            settings.Add("timeout", TimeoutSeconds);
            settings.Add("notify", NotifyOnPublish);
            settings.Add("warpGeneration", GenerateDuringWarp);
            settings.Add("pressPhotos", PressPhotos);
            settings.Add("assistant", AssistantOn);
            settings.Add("assistantConfirm", AssistantConfirm);
            settings.Add("crewChat", CrewChatOn);
            settings.Add("ktv", KtvOn);
            settings.Add("uiScale", (double)UiScale);
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
                g.OfflineMode = settings.GetBool("offline", true);
                g.MaxTokens = Math.Max(1000, settings.GetInt("maxTokens", g.MaxTokens));
                g.TimeoutSeconds = Math.Max(10, settings.GetInt("timeout", g.TimeoutSeconds));
                g.NotifyOnPublish = settings.GetBool("notify", true);
                g.GenerateDuringWarp = settings.GetBool("warpGeneration", false);
                g.PressPhotos = settings.GetBool("pressPhotos", true);
                g.AssistantOn = settings.GetBool("assistant", true);
                g.AssistantConfirm = settings.GetBool("assistantConfirm", true);
                g.CrewChatOn = settings.GetBool("crewChat", true);
                g.KtvOn = settings.GetBool("ktv", true);
                g.UiScale = (float)settings.GetDouble("uiScale", 0);
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
            return g;
        }
    }
}
