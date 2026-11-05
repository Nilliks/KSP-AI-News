using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace KerbalNews.KSP
{
    /// <summary>Copies between the core's DataNode and KSP's ConfigNode.</summary>
    public static class ConfigBridge
    {
        public static ConfigNode ToConfig(DataNode d)
        {
            var c = new ConfigNode(d.Name);
            Fill(c, d);
            return c;
        }

        static void Fill(ConfigNode c, DataNode d)
        {
            foreach (var kv in d.Values) c.AddValue(kv.Key, kv.Value);
            foreach (var child in d.Nodes) Fill(c.AddNode(child.Name), child);
        }

        public static DataNode ToData(ConfigNode c)
        {
            if (c == null) return null;
            var d = new DataNode(c.name);
            foreach (ConfigNode.Value v in c.values) d.Add(v.name, v.value);
            foreach (ConfigNode child in c.nodes) d.Nodes.Add(ToData(child));
            return d;
        }
    }

    /// <summary>Kerbin calendar from the game's own formatter, so 24-hour-day settings and planet packs work.</summary>
    public class KspClock : IGameClock
    {
        public double DaySeconds
        {
            get
            {
                var f = KSPUtil.dateTimeFormatter;
                return f != null && f.Day > 0 ? f.Day : 21600;
            }
        }

        public string FormatDate(double ut)
        {
            var f = KSPUtil.dateTimeFormatter;
            double day = f != null && f.Day > 0 ? f.Day : 21600;
            double year = f != null && f.Year > 0 ? f.Year : day * 426;
            if (ut < 0) ut = 0;
            int y = (int)(ut / year) + 1;
            int d = (int)((ut % year) / day) + 1;
            return "Year " + y + ", Day " + d;
        }
    }

    /// <summary>
    /// HTTP through UnityWebRequest. Requests are started and polled on the main thread but run
    /// asynchronously inside Unity, so the game never stalls waiting for an AI provider.
    /// </summary>
    public class UnityHttpTransport : IHttpTransport
    {
        class Active
        {
            public UnityWebRequest Req;
            public Action<HttpResult> Done;
            public float Started;
            public int Timeout;
        }

        readonly List<Active> active = new List<Active>();

        public void Send(HttpRequestSpec spec, Action<HttpResult> done)
        {
            try
            {
                var req = new UnityWebRequest(spec.Url, spec.Method);
                if (spec.Body != null) req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(spec.Body));
                req.downloadHandler = new DownloadHandlerBuffer();
                foreach (var h in spec.Headers) req.SetRequestHeader(h.Key, h.Value);
                req.timeout = spec.TimeoutSeconds;
                req.SendWebRequest();
                active.Add(new Active { Req = req, Done = done, Started = Time.realtimeSinceStartup, Timeout = spec.TimeoutSeconds });
            }
            catch (Exception e)
            {
                done(new HttpResult { Error = e.Message });
            }
        }

        public void Poll()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var a = active[i];
                bool timedOut = Time.realtimeSinceStartup - a.Started > a.Timeout + 10;
                if (!a.Req.isDone && !timedOut) continue;
                active.RemoveAt(i);
                var res = new HttpResult();
                if (timedOut && !a.Req.isDone) { a.Req.Abort(); res.Error = "Timed out"; }
                else
                {
                    res.Status = (int)a.Req.responseCode;
                    res.Body = a.Req.downloadHandler != null ? a.Req.downloadHandler.text ?? "" : "";
                    if (a.Req.isNetworkError) res.Error = a.Req.error ?? "Network error";
                }
                a.Req.Dispose();
                try { a.Done(res); }
                catch (Exception e) { Debug.LogError("[KerbalNews] Response handler failed: " + e); }
            }
        }
    }

    /// <summary>Global settings live in GameData/KerbalNews/PluginData, keys in their own file.</summary>
    public static class SettingsStore
    {
        static string Dir { get { return Path.Combine(KSPUtil.ApplicationRootPath, "GameData/KerbalNews/PluginData"); } }
        static string SettingsPath { get { return Path.Combine(Dir, "settings.cfg"); } }
        static string SecretsPath { get { return Path.Combine(Dir, "secrets.cfg"); } }

        public static GlobalSettings Load()
        {
            try
            {
                ConfigNode s = File.Exists(SettingsPath) ? ConfigNode.Load(SettingsPath) : null;
                ConfigNode k = File.Exists(SecretsPath) ? ConfigNode.Load(SecretsPath) : null;
                return GlobalSettings.Load(
                    s != null ? ConfigBridge.ToData(s.GetNode("KERBALNEWS_SETTINGS")) : null,
                    k != null ? ConfigBridge.ToData(k.GetNode("KERBALNEWS_SECRETS")) : null);
            }
            catch (Exception e)
            {
                Debug.LogError("[KerbalNews] Could not read settings: " + e.Message);
                return new GlobalSettings();
            }
        }

        public static void Save(GlobalSettings g)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var s = new DataNode("KERBALNEWS_SETTINGS");
                var k = new DataNode("KERBALNEWS_SECRETS");
                g.Save(s, k);
                var sRoot = new ConfigNode();
                sRoot.AddNode(ConfigBridge.ToConfig(s));
                sRoot.Save(SettingsPath);
                var kRoot = new ConfigNode();
                kRoot.AddNode(ConfigBridge.ToConfig(k));
                kRoot.Save(SecretsPath);
            }
            catch (Exception e)
            {
                Debug.LogError("[KerbalNews] Could not save settings: " + e.Message);
            }
        }
    }
}
