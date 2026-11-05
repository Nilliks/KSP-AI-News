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
        public Dictionary<string, string> BaseUrls = new Dictionary<string, string>();
        public Dictionary<string, string> WriterModels = new Dictionary<string, string>();
        public Dictionary<string, string> CheapModels = new Dictionary<string, string>();
        public Dictionary<string, string> Keys = new Dictionary<string, string>();
        public int MaxTokens = 6000;
        public int TimeoutSeconds = 120;
        public bool NotifyOnPublish = true;
        public bool GenerateDuringWarp = false;
        /// <summary>News window scale; 0 means automatic from screen size.</summary>
        public float UiScale = 0;

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
            settings.Add("uiScale", (double)UiScale);
            foreach (var p in Providers.All)
            {
                var n = settings.AddNode("PROVIDER");
                n.Add("id", p.Id);
                n.AddText("baseUrl", Lookup(BaseUrls, p.Id, ""));
                n.AddText("writer", Lookup(WriterModels, p.Id, ""));
                n.AddText("cheap", Lookup(CheapModels, p.Id, ""));
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
                g.UiScale = (float)settings.GetDouble("uiScale", 0);
                foreach (var n in settings.GetNodes("PROVIDER"))
                {
                    string id = n.Get("id", "");
                    if (id == "") continue;
                    g.BaseUrls[id] = n.GetText("baseUrl");
                    g.WriterModels[id] = n.GetText("writer");
                    g.CheapModels[id] = n.GetText("cheap");
                }
            }
            if (secrets != null)
                foreach (var k in secrets.GetNodes("KEY"))
                    g.Keys[k.Get("id", "")] = k.GetText("key");
            return g;
        }
    }
}
