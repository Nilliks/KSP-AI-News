using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KerbalNews.Web
{
    /// <summary>live.kb: the agency's livestream page, with telemetry, a viewer count and the chat on the right.</summary>
    public partial class Sites
    {
        public const string LiveHost = "live.kb";
        public string LiveResult = "";
        static readonly CultureInfo LiveInv = CultureInfo.InvariantCulture;

        PageResult LivePage(string url, string path)
        {
            var m = M();
            if (path.StartsWith("a/"))
            {
                var a = State.Streams.FirstOrDefault(x => x.Id == path.Substring(2));
                if (a == null) return NotFound(url);
                m["archive"] = ArchiveModel(a, true);
                return Page(url, "live", "archive.html", m, a.VesselName + " stream replay", true);
            }
            var st = Engine.Stream;
            m["result"] = LiveResult;
            m["archives"] = State.Streams.AsEnumerable().Reverse().Take(12).Select(a => (object)ArchiveModel(a, false)).ToList();
            if (st == null)
            {
                string active = ActiveVesselId();
                m["idle"] = true;
                m["canStart"] = !string.IsNullOrEmpty(active);
                m["vessel"] = ActiveVesselName() ?? "";
                m["silent"] = !string.IsNullOrEmpty(active) && State.IsSilent(active);
                return Page(url, "live", "index.html", m, "Live", true);
            }
            var t = st.Last;
            m["live"] = true;
            m["vessel"] = st.VesselName;
            m["viewers"] = st.Viewers.ToString("N0", LiveInv);
            m["peak"] = st.PeakViewers.ToString("N0", LiveInv);
            m["camera"] = st.Camera;
            double met = t != null ? t.UT - st.StartUT : 0;
            m["met"] = "T+" + TimeSpan.FromSeconds(Math.Max(0, met)).ToString(@"hh\:mm\:ss");
            if (t != null)
            {
                m["tele"] = new List<object>
                {
                    Stat("Altitude", TelemetrySample.Km(t.Altitude)), Stat("Speed", t.Speed.ToString("N0", LiveInv) + " m/s"),
                    Stat("Apoapsis", t.Altitude > 15000 || t.Situation == "ORBITING" ? TelemetrySample.Km(t.Apoapsis) : "-"),
                    Stat("Periapsis", t.Altitude > 15000 || t.Situation == "ORBITING" ? TelemetrySample.Km(t.Periapsis) : "-"),
                    Stat("Stage", t.Stage.ToString(LiveInv)), Stat("Fuel", t.FuelPct >= 0 ? t.FuelPct.ToString("0", LiveInv) + "%" : "-"),
                    Stat("G-force", t.G.ToString("0.0", LiveInv) + " g"), Stat("Status", t.Situation.ToLowerInvariant().Replace("_", " ") + " · " + t.Body),
                };
                double max = Math.Max(1, st.AltitudeTrace.DefaultIfEmpty(0).Max());
                m["trace"] = st.AltitudeTrace.Skip(Math.Max(0, st.AltitudeTrace.Count - 60)).Select(a => (object)new Dictionary<string, object> { { "h", (2 + Math.Round(a / max * 70)).ToString(LiveInv) + "px" } }).ToList();
            }
            m["moments"] = st.Moments.AsEnumerable().Reverse().Take(6).Select(x => (object)x).ToList();
            var chat = st.Chat.Skip(Math.Max(0, st.Chat.Count - 30)).ToList();
            m["chat"] = chat.Select(c => (object)new Dictionary<string, object>
            {
                { "author", c.Author }, { "text", c.Text }, { "system", c.System }, { "agency", c.Agency }, { "mine", c.Player },
                { "color", HandleColor(c.Author) },
            }).ToList();
            m["handle"] = State.AnonHandle != "" ? State.AnonHandle : "";
            m["agencyName"] = Engine.AgencyName;
            return Page(url, "live", "index.html", m, "LIVE: " + st.VesselName, true);
        }

        static Dictionary<string, object> Stat(string label, string value) { return new Dictionary<string, object> { { "label", label }, { "value", value } }; }

        /// <summary>Each chatter keeps one name color, like real stream chats.</summary>
        static string HandleColor(string name)
        {
            string[] colors = { "#ff7b72", "#79c0ff", "#7ee787", "#d2a8ff", "#ffa657", "#f2cc60", "#56d4dd", "#ff9bce" };
            return colors[(OfflineWriter.StableHash(name ?? "") & 0x7fffffff) % colors.Length];
        }

        Dictionary<string, object> ArchiveModel(StreamArchive a, bool full)
        {
            var m = new Dictionary<string, object>
            {
                { "id", a.Id }, { "url", LiveHost + "/a/" + a.Id }, { "vessel", a.VesselName }, { "date", Date(a.StartUT) },
                { "peak", a.PeakViewers.ToString("N0", LiveInv) }, { "length", TimeSpan.FromSeconds(Math.Max(0, a.EndUT - a.StartUT)).ToString(@"hh\:mm\:ss") },
                { "maxAlt", TelemetrySample.Km(a.MaxAltitude) },
            };
            if (full)
            {
                m["moments"] = a.Moments.Select(x => (object)x).ToList();
                m["chat"] = a.Highlights.Select(c => (object)new Dictionary<string, object> { { "author", c.Author }, { "text", c.Text }, { "agency", c.Agency }, { "color", HandleColor(c.Author) } }).ToList();
            }
            return m;
        }

        ActionResult HandleLive(string verb, string arg, IDictionary<string, string> form)
        {
            var r = new ActionResult();
            switch (verb)
            {
                case "stream-start":
                {
                    string id = ActiveVesselId();
                    if (string.IsNullOrEmpty(id)) { LiveResult = "Go to a flight to start a livestream."; return r; }
                    Engine.StartStream(id, ActiveVesselName() ?? "", Now(), Engine.RealTime);
                    LiveResult = "";
                    return r;
                }
                case "stream-end":
                {
                    var a = Engine.EndStream(Now());
                    LiveResult = a != null ? "Stream ended. " + a.PeakViewers.ToString("N0", LiveInv) + " people watched at the peak." : "";
                    return r;
                }
                case "stream-say":
                {
                    string text = Get(form, "chat");
                    string handle = Get(form, "handle");
                    if (handle != "") State.AnonHandle = handle;
                    Engine.StreamSay(text, arg == "agency", handle, Engine.RealTime);
                    return r;
                }
            }
            return null;
        }
    }
}
