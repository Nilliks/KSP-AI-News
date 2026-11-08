using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KerbalNews
{
    /// <summary>A saved stream, loaded for watching: the timeline of chat, announcer lines, telemetry, camera cuts and frames.</summary>
    public class ReplayData
    {
        public string Folder = "", Vessel = "", Agency = "";
        public double Length, Fps;
        public int Frames;
        public readonly List<Dictionary<string, object>> Events = new List<Dictionary<string, object>>();

        public static double T(Dictionary<string, object> e) { return Json.Num(e, "t", 0); }
        public static string K(Dictionary<string, object> e) { return Json.Str(e, "k", ""); }

        public static ReplayData Parse(string folder, string json)
        {
            object root;
            if (string.IsNullOrEmpty(json) || !Json.TryParse(json, out root) || Json.Obj(root) == null) return null;
            var d = new ReplayData
            {
                Folder = folder, Vessel = Json.Str(root, "vessel", ""), Agency = Json.Str(root, "agency", ""),
                Length = Json.Num(root, "length", 0), Fps = Json.Num(root, "fps", 0), Frames = (int)Json.Num(root, "frames", 0),
            };
            foreach (var e in Json.Arr(Json.Get(root, "events")) ?? new List<object>())
            {
                var o = Json.Obj(e);
                if (o != null) d.Events.Add(o);
            }
            d.Events.Sort((a, b) => T(a).CompareTo(T(b)));
            if (d.Events.Count > 0) d.Length = Math.Max(d.Length, T(d.Events[d.Events.Count - 1]));
            return d;
        }

        /// <summary>The last event of a kind at or before t, or null.</summary>
        public Dictionary<string, object> Last(string kind, double t)
        {
            Dictionary<string, object> found = null;
            foreach (var e in Events)
            {
                if (T(e) > t) break;
                if (K(e) == kind) found = e;
            }
            return found;
        }

        public List<Dictionary<string, object>> Upto(string kind, double t, int take)
        {
            var l = new List<Dictionary<string, object>>();
            foreach (var e in Events)
            {
                if (T(e) > t) break;
                if (K(e) == kind) l.Add(e);
            }
            return l.Skip(Math.Max(0, l.Count - take)).ToList();
        }
    }

    /// <summary>Someone watching a replay: where they are in it, and whether it's playing.</summary>
    public class ReplayPlayer
    {
        public ReplayData Data;
        public double Pos, Speed = 1, LastReal = -1;
        public bool Playing;
        public int Shown = -1;
        public string StreamId = "";
    }

    /// <summary>
    /// Stream replays. While a stream is live, everything on it goes into a timeline (chat, announcer lines with their
    /// audio, telemetry, countdown clock, camera cuts) and the host saves video frames beside it, under
    /// PluginData/Replays/&lt;save&gt;/&lt;folder&gt;. The live page's archive then plays it back with the video, chat and voice in step.
    /// </summary>
    public partial class NewsEngine
    {
        /// <summary>Host: saves one file of a replay (a frame, an announcer clip) and the replay's index.</summary>
        public Action<string, string, byte[]> SaveReplayFile;
        public Action<string, string> SaveReplayIndex;
        /// <summary>Host: reads a replay's index, or null if it's gone. And: plays a saved announcer clip.</summary>
        public Func<string, string> LoadReplayIndex;
        public Action<string, string> PlayReplayVoice;
        /// <summary>Host: stops a replay's voice mid-line (pause, seek, leaving the page).</summary>
        public Action StopReplayVoice;
        public ReplayPlayer Replay { get; private set; }
        /// <summary>Host: the replay folders saved beside this save, so a stream still shows if the game was never saved after it.</summary>
        public Func<List<string>> ReplayFolders;
        readonly Dictionary<string, StreamArchive> orphanCache = new Dictionary<string, StreamArchive>();

        /// <summary>
        /// Every stream to list: the ones in the save, plus replays on disk the save doesn't know about (the game was quit or
        /// reloaded before it saved the archive). Those are rebuilt from their index.
        /// </summary>
        public List<StreamArchive> AllStreams()
        {
            var list = new List<StreamArchive>(State.Streams);
            if (ReplayFolders == null || LoadReplayIndex == null) return list;
            List<string> folders;
            try { folders = ReplayFolders() ?? new List<string>(); } catch (Exception) { return list; }
            var known = new HashSet<string>(State.Streams.Where(a => a.Replay != "").Select(a => a.Replay));
            if (Stream != null && Stream.RecFolder != "") known.Add(Stream.RecFolder);   // still being recorded
            foreach (var f in folders)
            {
                if (known.Contains(f)) continue;
                StreamArchive a;
                if (!orphanCache.TryGetValue(f, out a))
                {
                    string json = null;
                    try { json = LoadReplayIndex(f); } catch (Exception) { json = null; }
                    a = ArchiveFromIndex(f, json);
                    orphanCache[f] = a;
                }
                if (a != null) list.Add(a);
            }
            return list.OrderBy(a => a.StartUT).ToList();
        }

        public StreamArchive FindStream(string id) { return AllStreams().FirstOrDefault(a => a.Id == id); }

        static StreamArchive ArchiveFromIndex(string folder, string json)
        {
            var d = ReplayData.Parse(folder, json);
            if (d == null) return null;
            object root;
            Json.TryParse(json, out root);
            double start = Json.Num(root, "startUT", 0);
            var tels = d.Events.Where(e => ReplayData.K(e) == "tel").ToList();
            var a = new StreamArchive
            {
                Id = "r-" + folder, VesselId = Json.Str(root, "vesselId", ""), VesselName = d.Vessel != "" ? d.Vessel : "Stream",
                StartUT = start, EndUT = start + d.Length, Replay = folder,
                PeakViewers = (int)Json.Num(root, "peak", tels.Count > 0 ? tels.Max(e => Json.Num(e, "vw", 0)) : 0),
                MaxAltitude = Json.Num(root, "maxAlt", tels.Count > 0 ? tels.Max(e => Json.Num(e, "alt", 0)) : 0),
                MaxSpeed = Json.Num(root, "maxSpeed", tels.Count > 0 ? tels.Max(e => Json.Num(e, "spd", 0)) : 0),
            };
            a.Moments.AddRange(d.Events.Where(e => ReplayData.K(e) == "mom").Select(e => Json.Str(e, "x", "")).Take(20));
            return a;
        }
        /// <summary>Host: bytes used by all saved replays, for the settings page.</summary>
        public Func<long> ReplayDiskUse;

        void StartRecording(StreamSession st)
        {
            if (Settings.ReplayQuality <= 0 || SaveReplayIndex == null) return;
            st.RecFolder = State.NewId("rec") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            st.RecFps = Settings.ReplayFps;
        }

        void Record(StreamSession st, double realTime, string kind, params object[] kv)
        {
            if (st == null || st.RecFolder == "") return;
            if (st.Rec.Count > 60000) return;
            var e = new Dictionary<string, object> { { "t", Math.Round(Math.Max(0, realTime - st.StartReal), 2) }, { "k", kind } };
            for (int i = 0; i + 1 < kv.Length; i += 2)
            {
                var v = kv[i + 1];
                if (v == null || v is string && (string)v == "" || v is bool && !(bool)v) continue;
                e[(string)kv[i]] = v;
            }
            st.Rec.Add(e);
        }

        void RecordChat(StreamSession st, ChatMessage c, double realTime)
        {
            Record(st, realTime, "chat", "a", c.Author, "x", c.Text, "o", c.Agency, "p", c.Player, "s", c.System);
        }

        void RecordSample(StreamSession st, TelemetrySample s, double realTime)
        {
            if (st.RecFolder == "") return;
            double met = st.Lifted && st.Overlay.LiftoffUT >= 0 ? s.UT - st.Overlay.LiftoffUT : double.NaN;
            Record(st, realTime, "tel", "alt", Math.Round(s.Altitude), "spd", Math.Round(s.Speed), "ap", Math.Round(s.Apoapsis), "pe", Math.Round(s.Periapsis),
                "stg", StreamOverlay.StageText(st, s.Stage), "fuel", Math.Round(s.FuelPct), "g", Math.Round(s.G, 1), "sit", s.Situation, "body", s.Body,
                "met", double.IsNaN(met) ? null : (object)Math.Round(met, 1), "vw", st.Viewers);
        }

        /// <summary>Host: the frame number to save now, or -1 when no frame is due (not recording, or too soon).</summary>
        public int ReplayFrameDue(double realTime)
        {
            var st = Stream;
            if (st == null || st.RecFolder == "" || st.RecFps <= 0 || realTime < st.RecNextFrame) return -1;
            if (st.RecFrames >= 30000) return -1;
            st.RecNextFrame = Math.Max(realTime, st.RecNextFrame) + 1.0 / st.RecFps;
            if (st.RecNextFrame < realTime) st.RecNextFrame = realTime + 1.0 / st.RecFps;
            int n = ++st.RecFrames;
            Record(st, realTime, "frm", "i", n);
            return n;
        }

        public string RecordingFolder { get { return Stream != null ? Stream.RecFolder : ""; } }

        public static string FrameFile(int n) { return "f" + n.ToString("00000", CultureInfo.InvariantCulture) + ".jpg"; }

        string ReplayIndexJson(StreamSession st, double realTime)
        {
            return Json.Write(new Dictionary<string, object>
            {
                { "vessel", st.VesselName }, { "vesselId", st.VesselId }, { "agency", AgencyName }, { "startUT", st.StartUT }, { "length", Math.Round(realTime - st.StartReal, 1) },
                { "peak", st.PeakViewers }, { "maxAlt", Math.Round(st.Max.Altitude) }, { "maxSpeed", Math.Round(st.Max.Speed) },
                { "fps", st.RecFps }, { "frames", st.RecFrames }, { "events", st.Rec.Cast<object>().ToList() },
            });
        }

        /// <summary>Writes the index now and then during a stream, so a crash still leaves a watchable replay.</summary>
        void TickRecording(StreamSession st, double realTime)
        {
            if (st.RecFolder == "" || realTime < st.RecIndexAt) return;
            st.RecIndexAt = realTime + 60;
            try { SaveReplayIndex(st.RecFolder, ReplayIndexJson(st, realTime)); } catch (Exception) { }
        }

        void FinishRecording(StreamSession st, StreamArchive arch)
        {
            if (st.RecFolder == "") return;
            double now = lastRealTime > 0 ? lastRealTime : st.StartReal;
            Record(st, now, "end");
            try { SaveReplayIndex(st.RecFolder, ReplayIndexJson(st, now)); arch.Replay = st.RecFolder; }
            catch (Exception) { arch.Replay = ""; }
        }

        // ------------------------------------------------------------------ watching

        /// <summary>Opens a replay for the archive page; keeps the one already open.</summary>
        public ReplayPlayer OpenReplay(StreamArchive a)
        {
            if (a == null || a.Replay == "" || LoadReplayIndex == null) return null;
            if (Replay != null && Replay.StreamId == a.Id && Replay.Data != null) return Replay;
            string json = null;
            try { json = LoadReplayIndex(a.Replay); } catch (Exception) { json = null; }
            var d = ReplayData.Parse(a.Replay, json);
            if (d == null) return null;
            Replay = new ReplayPlayer { Data = d, StreamId = a.Id };
            return Replay;
        }

        public void CloseReplay() { if (Replay != null) { Replay.Playing = false; StopVoiceNow(); } }
        void StopVoiceNow() { if (StopReplayVoice != null) { try { StopReplayVoice(); } catch (Exception) { } } }

        public void ReplayPlayPause()
        {
            var r = Replay;
            if (r == null) return;
            if (!r.Playing && r.Pos >= r.Data.Length - 0.5) r.Pos = 0;
            r.Playing = !r.Playing;
            r.LastReal = -1;
            if (!r.Playing) StopVoiceNow();
            Changed();
        }

        public void ReplaySeek(double t)
        {
            var r = Replay;
            if (r == null) return;
            r.Pos = Math.Max(0, Math.Min(r.Data.Length, t));
            r.LastReal = -1;
            StopVoiceNow();
            Changed();
        }

        public void ReplaySpeed(double s)
        {
            var r = Replay;
            if (r == null) return;
            r.Speed = s == 2 || s == 4 ? s : 1;
            if (r.Speed != 1) StopVoiceNow();
            Changed();
        }

        /// <summary>Host, every frame: moves the replay along, plays announcer clips as it passes them (at normal speed), and
        /// redraws the page once a second.</summary>
        public void TickReplay(double realTime)
        {
            var r = Replay;
            if (r == null || !r.Playing) return;
            if (r.LastReal < 0) { r.LastReal = realTime; return; }
            double dt = Math.Max(0, Math.Min(1, realTime - r.LastReal)) * r.Speed;
            r.LastReal = realTime;
            double from = r.Pos, to = Math.Min(r.Data.Length, r.Pos + dt);
            r.Pos = to;
            if (r.Speed == 1 && PlayReplayVoice != null)
                foreach (var e in r.Data.Events)
                {
                    double t = ReplayData.T(e);
                    if (t <= from) continue;
                    if (t > to) break;
                    string v = ReplayData.K(e) == "ann" ? Json.Str(e, "v", "") : "";
                    if (v != "") { try { PlayReplayVoice(r.Data.Folder, v); } catch (Exception) { } }
                }
            if (to >= r.Data.Length) { r.Playing = false; Changed(); }
            else if ((int)from != (int)to) Changed();
        }

        /// <summary>The frame to show at the replay's position, or 0 for none yet.</summary>
        public int ReplayFrameNow()
        {
            var r = Replay;
            if (r == null) return 0;
            var f = r.Data.Last("frm", r.Pos);
            return f == null ? 0 : (int)Json.Num(f, "i", 0);
        }
    }
}
