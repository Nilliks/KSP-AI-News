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
                var a = Engine.FindStream(path.Substring(2));
                if (a == null) return NotFound(url);
                var am = ArchiveModel(a, true);
                var player = Engine.OpenReplay(a);
                if (player != null) am["player"] = ReplayModel(player);
                else if (a.Replay != "") am["replayGone"] = true;
                m["archive"] = am;
                return Page(url, "live", "archive.html", m, a.VesselName + " stream replay", true);
            }
            var st = Engine.Stream;
            m["result"] = LiveResult;
            m["archives"] = Engine.AllStreams().AsEnumerable().Reverse().Take(12).Select(a => (object)ArchiveModel(a, false)).ToList();
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
            m["caption"] = st.Caption;
            m["rec"] = st.RecFolder != "";
            m["met"] = StreamOverlay.Clock(st, t != null ? t.UT : st.StartUT, double.NaN);
            // Launch countdown: the clock replaces the mission time until liftoff, with the controls for the player.
            double left = Engine.CountdownLeft(Engine.RealTime);
            string cd = st.Countdown;
            bool launched = cd == "launched" || st.Moments.Any(x => x.StartsWith("liftoff"));
            if (!double.IsNaN(left)) m["met"] = NewsEngine.CountClock(left);
            m["countHold"] = cd == "hold";
            m["countScrub"] = cd == "scrubbed";
            m["countZero"] = cd == "zero";
            m["countIdle"] = !launched && (cd == "" || cd == "scrubbed");
            m["countRunning"] = cd == "counting" || cd == "zero";
            m["countHolding"] = cd == "hold";
            if (t != null)
            {
                m["tele"] = new List<object>
                {
                    Stat("Altitude", TelemetrySample.Km(t.Altitude)), Stat("Speed", t.Speed.ToString("N0", LiveInv) + " m/s"),
                    Stat("Apoapsis", t.Situation != "PRELAUNCH" && t.Situation != "LANDED" && t.Situation != "SPLASHED" && t.Apoapsis > 0 ? TelemetrySample.Km(t.Apoapsis) : "-"),
                    Stat("Periapsis", t.Situation == "PRELAUNCH" || t.Situation == "LANDED" || t.Situation == "SPLASHED" ? "-" : t.Periapsis < 0 ? "suborbital" : TelemetrySample.Km(t.Periapsis)),
                    Stat("Stage", StreamOverlay.StageText(st, t.Stage)), Stat("Fuel", t.FuelPct >= 0 ? t.FuelPct.ToString("0", LiveInv) + "%" : "-"),
                    Stat("G-force", t.G.ToString("0.0", LiveInv) + " g"), Stat("Status", t.Situation.ToLowerInvariant().Replace("_", " ") + " · " + t.Body),
                };
                double max = Math.Max(2000, st.AltitudeTrace.DefaultIfEmpty(0).Max());   // a rocket sitting on the pad stays a flat line near the bottom
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
                { "maxAlt", TelemetrySample.Km(a.MaxAltitude) }, { "hasReplay", a.Replay != "" },
            };
            if (full)
            {
                m["moments"] = a.Moments.Select(x => (object)x).ToList();
                m["chat"] = a.Highlights.Select(c => (object)new Dictionary<string, object> { { "author", c.Author }, { "text", c.Text }, { "agency", c.Agency }, { "color", HandleColor(c.Author) } }).ToList();
            }
            return m;
        }

        static string Mmss(double s)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, Math.Floor(s)));
            return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
        }

        /// <summary>The replay player: the frame, the controls, a seek bar, and the chat, announcer and telemetry as they were at this point.</summary>
        Dictionary<string, object> ReplayModel(ReplayPlayer p)
        {
            var d = p.Data;
            double pos = p.Pos;
            var m = new Dictionary<string, object>
            {
                { "playing", p.Playing }, { "pos", Mmss(pos) }, { "len", Mmss(d.Length) }, { "hasVideo", d.Frames > 0 },
                { "back", (Math.Max(0, pos - 15)).ToString("0", LiveInv) }, { "fwd", Math.Min(d.Length, pos + 30).ToString("0", LiveInv) },
                { "speeds", new List<object> { 1, 2, 4 }.Select(x => (object)new Dictionary<string, object> { { "x", x }, { "on", Math.Abs(p.Speed - (int)x) < 0.01 } }).ToList() },
            };
            const int segs = 48;
            m["seek"] = Enumerable.Range(0, segs).Select(i => (object)new Dictionary<string, object>
            {
                { "t", (d.Length * i / segs).ToString("0", LiveInv) }, { "done", d.Length > 0 && d.Length * (i + 0.5) / segs <= pos },
            }).ToList();
            // Clock: the countdown as it was, or mission time counted on from the last telemetry.
            var tel = d.Last("tel", pos);
            var clk = d.Last("clk", pos);
            string clock = "ON THE PAD";
            double metAt = tel != null && Json.Get(tel, "met") != null ? Json.Num(tel, "met", 0) + (pos - ReplayData.T(tel)) : double.NaN;
            if (!double.IsNaN(metAt)) clock = "T+" + StreamOverlay.Hms(metAt);
            else if (clk != null) clock = Json.Str(clk, "x", clock);
            m["clock"] = clock;
            var cam = d.Last("cam", pos);
            m["camera"] = cam != null ? Json.Str(cam, "x", "") : "";
            var ann = d.Last("ann", pos);
            m["caption"] = ann != null && pos - ReplayData.T(ann) < Math.Max(5, Json.Num(ann, "d", 0) + 2) ? Json.Str(ann, "x", "") : "";
            if (tel != null)
            {
                string sit = Json.Str(tel, "sit", "");
                bool ground = sit == "PRELAUNCH" || sit == "LANDED" || sit == "SPLASHED";
                double ap = Json.Num(tel, "ap", 0), pe = Json.Num(tel, "pe", 0), fuel = Json.Num(tel, "fuel", -1);
                m["tele"] = new List<object>
                {
                    Stat("Altitude", TelemetrySample.Km(Json.Num(tel, "alt", 0))), Stat("Speed", Json.Num(tel, "spd", 0).ToString("N0", LiveInv) + " m/s"),
                    Stat("Apoapsis", !ground && ap > 0 ? TelemetrySample.Km(ap) : "-"), Stat("Periapsis", ground ? "-" : pe < 0 ? "suborbital" : TelemetrySample.Km(pe)),
                    Stat("Stage", Json.Str(tel, "stg", "-")), Stat("Fuel", fuel >= 0 ? fuel.ToString("0", LiveInv) + "%" : "-"),
                    Stat("G-force", Json.Num(tel, "g", 0).ToString("0.0", LiveInv) + " g"), Stat("Status", sit.ToLowerInvariant().Replace("_", " ") + " · " + Json.Str(tel, "body", "")),
                };
                m["viewers"] = ((int)Json.Num(tel, "vw", 0)).ToString("N0", LiveInv);
            }
            m["moments"] = d.Upto("mom", pos, 6).AsEnumerable().Reverse().Select(e => (object)Json.Str(e, "x", "")).ToList();
            m["chat"] = d.Upto("chat", pos, 30).Select(e => (object)new Dictionary<string, object>
            {
                { "author", Json.Str(e, "a", "") }, { "text", Json.Str(e, "x", "") }, { "system", Json.Get(e, "s") is bool && (bool)Json.Get(e, "s") },
                { "agency", Json.Get(e, "o") is bool && (bool)Json.Get(e, "o") }, { "color", HandleColor(Json.Str(e, "a", "")) },
            }).ToList();
            return m;
        }

        ActionResult HandleLive(string verb, string arg, IDictionary<string, string> form)
        {
            var r = new ActionResult();
            switch (verb)
            {
                case "replay-play": Engine.ReplayPlayPause(); return r;
                case "replay-seek":
                {
                    double t;
                    if (double.TryParse(arg, NumberStyles.Float, LiveInv, out t)) Engine.ReplaySeek(t);
                    return r;
                }
                case "replay-speed":
                {
                    double x;
                    if (double.TryParse(arg, NumberStyles.Float, LiveInv, out x)) Engine.ReplaySpeed(x);
                    return r;
                }
                case "stream-start":
                {
                    string id = ActiveVesselId();
                    if (string.IsNullOrEmpty(id)) { LiveResult = "Go to a flight to start a livestream."; return r; }
                    Engine.StartStream(id, ActiveVesselName() ?? "", Now(), Engine.RealTime);
                    LiveResult = "";
                    return r;
                }
                case "count-start":
                {
                    int mins;
                    Engine.CountdownStart(int.TryParse(arg, out mins) ? mins * 60 : 600, Engine.RealTime);
                    return r;
                }
                case "count-hold": Engine.CountdownHold(Engine.RealTime); return r;
                case "count-resume": Engine.CountdownResume(Engine.RealTime); return r;
                case "count-recycle":
                {
                    int mins;
                    Engine.CountdownRecycle(int.TryParse(arg, out mins) ? mins * 60 : 600, Engine.RealTime);
                    return r;
                }
                case "count-scrub": Engine.CountdownScrub(Engine.RealTime); return r;
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
