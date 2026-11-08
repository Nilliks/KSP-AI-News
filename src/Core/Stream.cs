using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>One telemetry snapshot of the streamed vessel.</summary>
    public class TelemetrySample
    {
        public double UT, Altitude, Speed, Apoapsis, Periapsis, G, FuelPct = -1;
        /// <summary>Seconds for a radio signal from the craft to reach Kerbin.</summary>
        public double SignalDelay;
        public int Stage;
        public string Situation = "", Body = "";
        public List<string> Crew = new List<string>();
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Km(double m) { return Math.Abs(m) >= 10000 ? (m / 1000).ToString("N1", Inv) + " km" : m.ToString("N0", Inv) + " m"; }

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append(Situation.ToLowerInvariant().Replace("_", " ") + " at " + Body + ", altitude " + Km(Altitude) + ", speed " + Speed.ToString("N0", Inv) + " m/s");
            if (Situation == "ORBITING" || Situation == "SUB_ORBITAL" || Situation == "ESCAPING" || Altitude > 20000) sb.Append(", apoapsis " + Km(Apoapsis) + ", periapsis " + Km(Periapsis));
            sb.Append(", stage " + Stage);
            if (FuelPct >= 0) sb.Append(", fuel " + FuelPct.ToString("0", Inv) + "%");
            if (G > 0.05) sb.Append(", " + G.ToString("0.0", Inv) + " g");
            sb.Append(Crew.Count > 0 ? ", crew aboard: " + string.Join(", ", Crew.ToArray()) : ", UNCREWED (no kerbals aboard)");
            return sb.ToString();
        }
    }

    public class ChatMessage
    {
        public string Author = "", Text = "";
        public bool Player, Agency, System;
    }

    /// <summary>A livestream in progress. Not saved: loading a save or leaving flight ends it.</summary>
    public class StreamSession
    {
        public string VesselId = "", VesselName = "";
        public double StartUT, StartReal;
        public int Viewers, PeakViewers;
        public TelemetrySample Last, Previous, Max = new TelemetrySample();
        public readonly List<ChatMessage> Chat = new List<ChatMessage>();
        public readonly List<KeyValuePair<double, ChatMessage>> Queue = new List<KeyValuePair<double, ChatMessage>>();
        public readonly List<string> Moments = new List<string>();
        public readonly List<string> NewMoments = new List<string>();
        public readonly List<ChatMessage> PlayerSaid = new List<ChatMessage>();
        public double NextBatchReal;
        public int Batches;
        /// <summary>Quiet batches in a row (nothing happened, nobody typed), for spacing out AI calls on a long coast.</summary>
        public int QuietRun;
        /// <summary>Not saved: when a camera frame last went to the AI, a helper's words for the latest frame, and whether it's looking.</summary>
        public double LastFrameReal = -1000;
        public string CameraLook = "";
        public bool LookBusy;
        /// <summary>How busy the chat is right now: drifts at random between lulls and busy stretches.</summary>
        public double Heat = 1;
        /// <summary>Excitement left over from the last big moment; it fades over the next batches.</summary>
        public double Hype;
        public string Frame = "";      // newest frame file under PluginData/Live
        /// <summary>The camera the broadcast is on now, such as "Pad camera" or "Hull camera: looking aft".</summary>
        public string Camera = "";
        public bool WentOnboard;
        /// <summary>Launch countdown: "" (none), "counting", "hold", "zero" (T-0 reached), "scrubbed" or "launched".</summary>
        public string Countdown = "";
        public double CountLeft;        // seconds left at CountMark
        public double CountMark;        // real time CountLeft was measured
        public int NextCall;            // index into the countdown calls not yet announced
        public bool ZeroLate;           // the "still on the pad after T-0" moment was sent
        public int CountShown = int.MinValue;
        public int Holds, Recycles;
        public readonly List<double> AltitudeTrace = new List<double>();
        /// <summary>What the stats overlay has seen (liftoff time, stages at liftoff, max Q...). One clock and one stage count for the page and the overlay.</summary>
        public readonly OverlayMemory Overlay = new OverlayMemory();
        public bool Lifted { get { return Moments.Any(x => x.StartsWith("liftoff", StringComparison.Ordinal)); } }
        /// <summary>The announcer: lines said so far, the caption under the video, and the final-count state.</summary>
        public readonly List<AnnouncerLine> Said = new List<AnnouncerLine>();
        public string Caption = "";
        public double CaptionUntil, LastAnnReal = -1000;
        public bool FinalCountQueued, FinalCountSaid;
        /// <summary>The replay being recorded: its folder ("" = not recording), timeline, frames saved and when the next is due.</summary>
        public string RecFolder = "";
        public readonly List<Dictionary<string, object>> Rec = new List<Dictionary<string, object>>();
        public int RecFrames;
        public double RecFps, RecNextFrame, RecIndexAt, RecOverlayAt;
        /// <summary>The stream's entry in the save, made when it starts so any save during or after the stream keeps it.</summary>
        public StreamArchive Archive;
        public double ArchiveSyncAt;
        public readonly BroadcastState Bc = new BroadcastState();
    }

    /// <summary>A finished stream, kept in the save for the archive and for the press.</summary>
    public class StreamArchive
    {
        public string Id = "", VesselId = "", VesselName = "";
        public double StartUT, EndUT, MaxAltitude, MaxSpeed;
        public int PeakViewers;
        public List<string> Moments = new List<string>();
        public List<ChatMessage> Highlights = new List<ChatMessage>();
        /// <summary>The replay's folder under PluginData/Replays/&lt;save&gt;, or "" when none was recorded.</summary>
        public string Replay = "";

        public void Save(DataNode n)
        {
            n.Add("id", Id);
            if (Replay != "") n.Add("replay", Replay); n.Add("vessel", VesselId); n.AddText("vesselName", VesselName); n.Add("start", StartUT); n.Add("end", EndUT);
            n.Add("maxAlt", MaxAltitude); n.Add("maxSpeed", MaxSpeed); n.Add("peak", PeakViewers);
            foreach (var m in Moments) n.AddText("moment", m);
            foreach (var c in Highlights) { var cn = n.AddNode("CHAT"); cn.AddText("author", c.Author); cn.AddText("text", c.Text); cn.Add("agency", c.Agency); }
        }

        public static StreamArchive Load(DataNode n)
        {
            var a = new StreamArchive
            {
                Id = n.Get("id", ""), VesselId = n.Get("vessel", ""), VesselName = n.GetText("vesselName"), StartUT = n.GetDouble("start"), EndUT = n.GetDouble("end"),
                MaxAltitude = n.GetDouble("maxAlt"), MaxSpeed = n.GetDouble("maxSpeed"), PeakViewers = n.GetInt("peak"), Replay = n.Get("replay", ""),
            };
            foreach (var m in n.GetAll("moment")) a.Moments.Add(TextCodec.Decode(m));
            foreach (var cn in n.GetNodes("CHAT")) a.Highlights.Add(new ChatMessage { Author = cn.GetText("author"), Text = cn.GetText("text"), Agency = cn.GetBool("agency") });
            return a;
        }
    }

    /// <summary>
    /// The simulated livestream: every StreamInterval seconds of real time a telemetry snapshot goes to the cheap model,
    /// which writes a batch of viewer messages that play out one by one until the next batch. Big moments
    /// (staging, explosions, touchdown) ask for a batch right away. Nothing leaves the game.
    /// </summary>
    public partial class NewsEngine
    {
        static readonly CultureInfo StreamInv = CultureInfo.InvariantCulture;
        public StreamSession Stream { get; private set; }
        public double StreamInterval = 30;
        public double StreamMinGap = 8;
        bool streamBusy;
        public event Action StreamChanged;
        /// <summary>A stream ended and was archived (the host tidies old replays).</summary>
        public event Action<StreamArchive> StreamEnded;

        public StreamSession StartStream(string vesselId, string vesselName, double ut, double realTime)
        {
            if (Stream != null) return Stream;
            if (realTime < lastRealTime) realTime = lastRealTime;   // never start the clock in the past
            lastRealTime = realTime;
            var crewed = Tracked(vesselId) != null && Tracked(vesselId).Craft != null && Tracked(vesselId).Craft.Crew.Count > 0;
            Stream = new StreamSession { VesselId = vesselId ?? "", VesselName = vesselName ?? "", StartUT = ut, StartReal = realTime, NextBatchReal = realTime + 3 };
            Stream.Viewers = Stream.PeakViewers = (int)((200 + State.Mood.Excitement * 60) * (crewed ? 2 : 1));
            Stream.Bc.ViewerBase = Stream.Viewers;
            Stream.Chat.Add(new ChatMessage { System = true, Text = "Welcome to the " + AgencyName + " livestream of " + vesselName + "." });
            speech.Clear();
            voiceFreeAt = 0;
            StartRecording(Stream);
            Stream.Archive = new StreamArchive { Id = State.NewId("ls"), VesselId = Stream.VesselId, VesselName = Stream.VesselName, StartUT = ut, EndUT = ut, Replay = Stream.RecFolder };
            SyncArchive(Stream, ut);
            RecordChat(Stream, Stream.Chat[0], realTime);
            Say(Stream, AnnLine("open", Stream), realTime, realTime + 1.5, 15);
            fetches.Clear();
            fetching = 0;
            PlaySfx(Stream, "room", realTime);
            Changed();
            return Stream;
        }

        void Changed() { if (StreamChanged != null) StreamChanged(); }

        /// <summary>Brings the stream's saved entry up to date, and puts it back if a load replaced the news state mid-stream.</summary>
        StreamArchive SyncArchive(StreamSession st, double ut)
        {
            var a = st.Archive;
            a.EndUT = Math.Max(a.StartUT, ut);
            a.MaxAltitude = st.Max.Altitude; a.MaxSpeed = st.Max.Speed; a.PeakViewers = st.PeakViewers;
            a.Moments.Clear();
            a.Moments.AddRange(st.Moments.Take(20));
            if (!State.Streams.Contains(a))
            {
                State.Streams.RemoveAll(x => x.Id == a.Id);
                State.Streams.Add(a);
                if (State.Streams.Count > 30) State.Streams.RemoveAt(0);
            }
            return a;
        }

        // ------------------------------------------------------------------ launch countdown

        static readonly int[] CountCalls = { 600, 300, 120, 60, 30, 10 };

        /// <summary>Seconds left on the countdown right now (negative after T-0), or NaN with no countdown.</summary>
        public double CountdownLeft(double realTime)
        {
            var st = Stream;
            if (st == null || st.Countdown == "" || st.Countdown == "scrubbed" || st.Countdown == "launched") return double.NaN;
            return st.Countdown == "hold" ? st.CountLeft : st.CountLeft - (realTime - st.CountMark);
        }

        static string ClockText(double secs)
        {
            var t = TimeSpan.FromSeconds(Math.Abs(Math.Round(secs)));
            return (t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss"));
        }

        public static string CountClock(double secs) { return (secs >= 0 ? "T-" : "T+") + ClockText(secs); }

        bool CanCount(StreamSession st) { return st != null && st.Countdown != "launched" && !st.Moments.Any(m => m.StartsWith("liftoff")); }

        public bool CountdownStart(double seconds, double realTime)
        {
            var st = Stream;
            if (!CanCount(st) || (st.Countdown == "counting")) return false;
            st.Countdown = "counting"; st.CountLeft = Math.Max(10, seconds); st.CountMark = realTime;
            st.NextCall = CountCalls.Count(c => c >= st.CountLeft); st.ZeroLate = false;
            StreamMoment("the launch countdown started at " + CountClock(st.CountLeft), realTime);
            Changed();
            return true;
        }

        public bool CountdownHold(double realTime)
        {
            var st = Stream;
            if (st == null || st.Countdown != "counting" && st.Countdown != "zero") return false;
            st.CountLeft = Math.Max(0, CountdownLeft(realTime)); st.CountMark = realTime; st.Countdown = "hold"; st.Holds++;
            StopFinalCount(st);
            StreamMoment("HOLD: the countdown was stopped at " + CountClock(st.CountLeft) + (st.CountLeft < 60 ? ", in the final minute" : "") + (st.Holds > 1 ? " (hold number " + st.Holds + " today)" : ""), realTime);
            Changed();
            return true;
        }

        public bool CountdownResume(double realTime)
        {
            var st = Stream;
            if (st == null || st.Countdown != "hold") return false;
            st.Countdown = "counting"; st.CountMark = realTime;
            st.NextCall = CountCalls.Count(c => c >= st.CountLeft);
            StreamMoment("the countdown resumed from " + CountClock(st.CountLeft), realTime);
            Changed();
            return true;
        }

        /// <summary>Turns the clock back to an earlier point and holds there, as a real count does after a problem.</summary>
        public bool CountdownRecycle(double seconds, double realTime)
        {
            var st = Stream;
            if (st == null || (st.Countdown != "hold" && st.Countdown != "counting" && st.Countdown != "zero")) return false;
            double from = CountdownLeft(realTime);
            st.Countdown = "hold"; st.CountLeft = Math.Max(10, seconds); st.CountMark = realTime; st.Recycles++; st.ZeroLate = false;
            StopFinalCount(st);
            StreamMoment("the countdown was recycled from " + CountClock(from) + " back to " + CountClock(st.CountLeft) + " and is holding" + (st.Recycles > 1 ? " (recycle number " + st.Recycles + ")" : ""), realTime);
            Changed();
            return true;
        }

        public bool CountdownScrub(double realTime)
        {
            var st = Stream;
            if (st == null || st.Countdown == "" || st.Countdown == "scrubbed" || st.Countdown == "launched") return false;
            double left = CountdownLeft(realTime);
            st.Countdown = "scrubbed";
            StopFinalCount(st);
            StreamMoment("SCRUB: the launch was called off for today at " + CountClock(left), realTime);
            Changed();
            return true;
        }

        /// <summary>Announces the calls (T-1 minute, final ten seconds...), T-0, and a rocket still sitting on the pad after T-0.</summary>
        void TickCountdown(StreamSession st, double realTime)
        {
            if (st.Countdown != "counting" && st.Countdown != "zero") { ShowClock(st, realTime); return; }
            double left = CountdownLeft(realTime);
            while (st.Countdown == "counting" && st.NextCall < CountCalls.Length && left <= CountCalls[st.NextCall])
            {
                int c = CountCalls[st.NextCall++];
                StreamMoment(c == 10 ? "T-10 seconds: the final countdown" : c >= 60 ? "T-" + (c / 60) + " minute" + (c >= 120 ? "s" : "") + " on the countdown" : "T-" + c + " seconds", realTime);
            }
            if (st.Countdown == "counting" && left <= 0)
            {
                st.Countdown = "zero";
                StreamMoment("T-0: the countdown reached zero", realTime);
            }
            if (st.Countdown == "zero" && !st.ZeroLate && left < -20)
            {
                st.ZeroLate = true;
                StreamMoment("T-0 was " + Math.Round(-left) + " seconds ago and the rocket is still sitting on the pad", realTime);
            }
            ShowClock(st, realTime);
        }

        void ShowClock(StreamSession st, double realTime)
        {
            double left = CountdownLeft(realTime);
            int shown = double.IsNaN(left) ? int.MinValue : (int)Math.Round(left);
            if (shown != st.CountShown)
            {
                st.CountShown = shown;
                if (!double.IsNaN(left)) Record(st, realTime, "clk", "x", CountClock(left), "h", st.Countdown == "hold");
                Changed();
            }
        }

        /// <summary>Liftoff with a countdown running: says whether it went on time, early or late.</summary>
        string LiftoffWithCount(StreamSession st, double realTime)
        {
            double left = CountdownLeft(realTime);
            string state = st.Countdown;
            st.Countdown = "launched";
            if (double.IsNaN(left)) return state == "scrubbed" ? "liftoff, even though the launch had been scrubbed" : "liftoff";
            if (state == "hold") return "liftoff during a countdown hold at " + CountClock(left) + " (nobody expected it)";
            if (Math.Abs(left) <= 3) return "liftoff right on time at T-0";
            if (left > 0) return "liftoff " + Math.Round(left) + " seconds early, with the countdown still at " + CountClock(left);
            return "liftoff " + Math.Round(-left) + " seconds after T-0";
        }

        /// <summary>The broadcast cut to another camera. The first cut to the rocket's own cameras is a moment the chat notices.</summary>
        public void StreamCamera(string name, bool onboard, double realTime)
        {
            var st = Stream;
            if (st == null || name == null || st.Camera == name) return;
            st.Camera = name;
            Record(st, realTime, "cam", "x", name);
            if (onboard && !st.WentOnboard)
            {
                st.WentOnboard = true;
                StreamMoment("the broadcast cut to the onboard hull cameras", realTime);
            }
            Changed();
        }

        /// <summary>Feeds the latest telemetry. A moment (staging, explosion...) asks for chat right away.</summary>
        public void StreamSample(TelemetrySample s, double realTime, string moment = null)
        {
            var st = Stream;
            if (st == null || s == null) return;
            st.Previous = st.Last;
            st.Last = s;
            st.Max.Altitude = Math.Max(st.Max.Altitude, s.Altitude);
            st.Max.Speed = Math.Max(st.Max.Speed, s.Speed);
            st.AltitudeTrace.Add(s.Altitude);
            if (st.Lifted)
            {
                if (st.Overlay.LiftoffUT < 0) st.Overlay.LiftoffUT = s.UT;
                if (st.Overlay.StagesAtLiftoff < 0) st.Overlay.StagesAtLiftoff = s.Stage;
            }
            if (st.AltitudeTrace.Count > 120) st.AltitudeTrace.RemoveAt(0);
            RecordSample(st, s, realTime);
            if (moment != null) StreamMoment(moment, realTime);
        }

        public void StreamMoment(string what, double realTime)
        {
            var st = Stream;
            if (st == null || string.IsNullOrEmpty(what)) return;
            if (what == "liftoff" && st.Countdown != "" && st.Countdown != "launched") what = LiftoffWithCount(st, realTime);
            st.Moments.Add(what);
            st.NewMoments.Add(what);
            Record(st, realTime, "mom", "x", what);
            AnnounceMoment(st, what, realTime);
            st.NextBatchReal = Math.Min(st.NextBatchReal, Math.Max(realTime + 1, LastBatchReal + StreamMinGap));
        }

        double LastBatchReal;

        /// <summary>The player types into the chat, officially or anonymously. The next batch answers.</summary>
        public void StreamSay(string text, bool asAgency, string handle, double realTime)
        {
            var st = Stream;
            text = (text ?? "").Trim();
            if (st == null || text == "") return;
            var m = new ChatMessage { Author = asAgency ? AgencyName : AnonName(handle), Text = ResponseParser.Clip(text, 300), Player = true, Agency = asAgency };
            st.Chat.Add(m);
            RecordChat(st, m, realTime);
            st.PlayerSaid.Add(m);
            st.NextBatchReal = Math.Min(st.NextBatchReal, Math.Max(realTime + 2, LastBatchReal + StreamMinGap));
            Changed();
        }

        /// <summary>Call every frame: plays queued messages out and asks for the next batch when it's due.</summary>
        public void TickStream(double realTime, bool allowCalls)
        {
            var st = Stream;
            if (st == null) return;
            bool changed = false;
            while (st.Queue.Count > 0 && st.Queue[0].Key <= realTime)
            {
                st.Chat.Add(st.Queue[0].Value);
                RecordChat(st, st.Queue[0].Value, realTime);
                st.Queue.RemoveAt(0);
                changed = true;
            }
            if (st.Chat.Count > 300) st.Chat.RemoveRange(0, st.Chat.Count - 300);
            if (changed) Changed();
            TickCountdown(st, realTime);
            TickFinalCount(st, realTime);
            TickCommentary(st, realTime);
            TickBroadcast(st, realTime);
            TickVoice(st, realTime);
            TickRecording(st, realTime);
            if (realTime >= st.ArchiveSyncAt) { st.ArchiveSyncAt = realTime + 20; SyncArchive(st, st.Last != null ? st.Last.UT : st.StartUT); }
            if (streamBusy || realTime < st.NextBatchReal || st.Last == null) return;
            LastBatchReal = realTime;
            st.NextBatchReal = realTime + StreamInterval;
            st.Batches++;
            var moments = st.NewMoments.ToList();
            var said = st.PlayerSaid.ToList();
            st.NewMoments.Clear();
            st.PlayerSaid.Clear();
            // How many messages: the player's rate for a quiet stretch, a random drift between lulls and busy spells,
            // and a burst when something big just happened.
            var rng = new Random(st.Batches * 7919 + (int)(realTime * 13));
            st.Heat = Math.Max(0.35, Math.Min(1.8, st.Heat * (0.75 + rng.NextDouble() * 0.5) + (1 - st.Heat) * 0.15));
            st.Hype = st.Hype * 0.45 + moments.Sum(m => MomentWeight(m));
            int n = ChatCount(st, rng, said.Count);
            double hype = st.Hype;
            // On a quiet stretch (no new moment, nobody typed, the excitement has died down) only every third batch goes
            // to the AI; the ones between come from the chat templates. "Everything" sends them all.
            bool quiet = moments.Count == 0 && said.Count == 0 && hype < 1;
            st.QuietRun = quiet ? st.QuietRun + 1 : 0;
            bool skipAi = quiet && st.QuietRun % 3 != 0 && !Settings.AiWrites(GlobalSettings.AiAll);
            if (!Settings.AiWrites(GlobalSettings.AiAlmostAll) || !allowCalls || skipAi)
            {
                PlayOut(st, Fit(OfflineWriter.StreamChat(this, st, moments, said), hype >= 2 ? n : Math.Min(n, 12), rng), null, realTime, hype);
                return;
            }
            streamBusy = true;
            var frame = StreamFrameFor(st, hype, realTime, Settings.CheapModel);
            string streamUser = PromptBuilder.StreamUser(this, st, moments, said, n, hype);
            if (frame != null) streamUser += "\nA frame from the live camera right now is attached. Some viewers react to what they can actually see in it (the craft, the plume, the view, the colours), as people watching a real launch stream do.";
            else if (st.CameraLook != "") { streamUser += "\nTHE CAMERA SHOWS: " + st.CameraLook + " Some viewers react to what they see."; st.CameraLook = ""; }
            else StreamHelperLook(st, hype, realTime);
            bool wantAnn = Settings.Announcer >= 2 && st.Lifted && !st.FinalCountQueued;
            if (wantAnn) streamUser += "\nANNOUNCER: also write \"announcer\": one line (at most 25 words) the agency's professional launch commentator says next, in the calm, precise style of real launch broadcasts, about what the telemetry shows right now (altitude, speed, staging, the view). Use spoken numbers sparingly. No hype words, no chat slang. \"\" if nothing needs saying.";
            AskJson(Settings.CheapModel, PromptBuilder.StreamSystem(State, AgencyName), streamUser, Math.Min(4000, 600 + n * 45), 40, frame, (root, err, text, res) =>
            {
                streamBusy = false;
                if (Stream != st) return;
                List<ChatMessage> msgs = null;
                int viewers = -1;
                if (root != null)
                {
                    msgs = new List<ChatMessage>();
                    foreach (var m in Json.Arr(Json.Get(root, "messages")) ?? new List<object>())
                    {
                        string t = ResponseParser.Clean(Json.Str(m, "text", ""));
                        if (t == "" || ResponseParser.ContainsForbidden(t)) continue;
                        msgs.Add(new ChatMessage { Author = ResponseParser.Clip(ResponseParser.Clean(Json.Str(m, "author", "viewer")), 30).TrimStart('@'), Text = ResponseParser.Clip(t, 240) });
                    }
                    viewers = (int)Json.Num(root, "viewers", -1);
                    string ann = ResponseParser.Clean(Json.Str(root, "announcer", "") ?? "");
                    if (wantAnn && ann != "" && !ResponseParser.ContainsForbidden(ann) && lastRealTime - st.LastAnnReal > 8 && !st.FinalCountQueued)
                        Say(st, ResponseParser.Clip(ann, 220), lastRealTime, 0, 12);
                }
                if (msgs == null || msgs.Count == 0) { LastError = err ?? "Chat reply was empty"; msgs = Fit(OfflineWriter.StreamChat(this, st, moments, said), n, rng); }
                PlayOut(st, msgs, viewers > 0 ? (int?)viewers : null, lastRealTime, hype);
            });
        }

        double lastRealTime;
        /// <summary>The host's real clock, for spreading chat out when a reply arrives.</summary>
        public void SetRealTime(double t) { lastRealTime = t; }
        public double RealTime { get { return lastRealTime; } }

        static string StreamMomentText(NewsEvent e)
        {
            switch (e.Type)
            {
                case EventTypes.Launch: return null; // the host reports liftoff once the craft really leaves the pad
                case EventTypes.Orbit: return "the vessel reached orbit around " + e.Body;
                case EventTypes.Soi: return "the vessel entered " + e.Body + "'s sphere of influence";
                case EventTypes.Landing: return "touchdown on " + e.Body;
                case EventTypes.Splashdown: return "splashdown";
                case EventTypes.Docking: return "docking";
                case EventTypes.Failure: return "the vessel was destroyed (" + e.Detail + ")";
                case EventTypes.CrewKilled: return "crew lost: " + string.Join(", ", e.Crew.ToArray());
                case EventTypes.Milestone: return "milestone: " + e.Detail;
                default: return e.Type;
            }
        }

        /// <summary>How exciting a moment is for the chat: liftoff and explosions set it off, staging gets a cheer.</summary>
        public static double MomentWeight(string m)
        {
            m = (m ?? "").ToLowerInvariant();
            if (m.Contains("crew lost")) return 5;
            if (m.StartsWith("scrub")) return 3.5;
            if (m.Contains("final countdown")) return 3.5;
            if (m.StartsWith("t-0")) return 3;
            if (m.StartsWith("hold")) return 2.5;
            if (m.Contains("recycled")) return 2;
            if (m.Contains("countdown resumed") || m.Contains("countdown started")) return 1.5;
            if (m.StartsWith("t-")) return 1;
            if (m.Contains("destroyed") || m.Contains("exploded")) return 4.5;
            if (m.Contains("liftoff")) return 4;
            if (m.Contains("touchdown") || m.Contains("splashdown")) return 3.5;
            if (m.Contains("orbit")) return 3;
            if (m.Contains("docking") || m.Contains("milestone")) return 2.5;
            if (m.Contains("sphere of influence")) return 2;
            if (m.Contains("stage")) return 1.2;
            return 1;
        }

        /// <summary>Messages for the next batch.</summary>
        int ChatCount(StreamSession st, Random rng, int playerLines)
        {
            double perBatch = Settings.StreamChatPer10Min * StreamInterval / 600.0;
            double n = perBatch * st.Heat * (0.6 + rng.NextDouble() * 0.8);
            n *= 1 + Math.Min(6, st.Hype) * 0.9;   // liftoff roughly quadruples it
            n += playerLines * 2;
            return Math.Max(1, Math.Min(60, (int)Math.Round(n)));
        }

        /// <summary>Trims or repeats-with-variety a template batch to the wanted size.</summary>
        static List<ChatMessage> Fit(List<ChatMessage> msgs, int n, Random rng)
        {
            if (msgs.Count == 0) return msgs;
            if (msgs.Count > n) return msgs.Select((m, i) => new { m, i }).OrderBy(x => rng.Next()).Take(n).OrderBy(x => x.i).Select(x => x.m).ToList();   // a random few, still in order
            // A burst: people echo each other, as a real chat does when something big happens.
            var outList = new List<ChatMessage>(msgs);
            var handles = msgs.Select(m => m.Author).Distinct().ToList();
            while (outList.Count < n && msgs.Count >= 3)
            {
                var m = msgs[rng.Next(msgs.Count)];
                outList.Insert(rng.Next(outList.Count + 1), new ChatMessage { Author = handles[rng.Next(handles.Count)] + (rng.Next(3) == 0 ? rng.Next(10, 99).ToString() : ""), Text = rng.Next(2) == 0 ? m.Text.ToUpperInvariant() : m.Text });
            }
            return outList;
        }

        /// <summary>When each message appears, in seconds after the batch arrives. Quiet chat comes in uneven gaps with the odd
        /// quick back-and-forth; after a big moment most of the batch floods in during the first seconds.</summary>
        public static List<double> ChatTimes(int count, double span, double hype, Random rng)
        {
            var times = new List<double>();
            if (count <= 0) return times;
            if (hype >= 2)
            {
                double rush = Math.Min(8, span * 0.3);
                for (int i = 0; i < count; i++)
                {
                    double u = rng.NextDouble();
                    times.Add(i < count * 0.65 ? 0.2 + rush * u * u : 0.2 + rush + (span - rush) * u);
                }
            }
            else
            {
                double t = 0;
                for (int i = 0; i < count; i++)
                {
                    double gap = -Math.Log(1 - rng.NextDouble() * 0.999);   // exponential: mostly short gaps, some long silences
                    if (rng.NextDouble() < 0.25) gap *= 0.15;               // a quick reply right after the last one
                    t += gap;
                    times.Add(t);
                }
                double scale = (span - 0.5) / Math.Max(times[times.Count - 1], 1e-3) * (0.75 + rng.NextDouble() * 0.25);
                for (int i = 0; i < times.Count; i++) times[i] = 0.3 + times[i] * scale;
            }
            times.Sort();
            return times;
        }

        void PlayOut(StreamSession st, List<ChatMessage> msgs, int? viewers, double realTime, double hype = 0)
        {
            if (realTime <= 0) realTime = lastRealTime;
            double span = Math.Max(5, StreamInterval - 1);
            var rng = new Random(st.Batches * 31 + msgs.Count);
            var times = ChatTimes(msgs.Count, span, hype, rng);
            for (int i = 0; i < msgs.Count; i++)
            {
                msgs[i].Text = StreamChatStyle.Emotes(msgs[i].Text);
                st.Queue.Add(new KeyValuePair<double, ChatMessage>(realTime + times[i], msgs[i]));
            }
            st.Queue.Sort((a, b) => a.Key.CompareTo(b.Key));
            // The audience drifts toward the model's estimate; the surge at big moments rides on top (TickViewers).
            if (viewers.HasValue) st.Bc.ViewerBase = Math.Max(10, st.Bc.ViewerBase * 0.5 + viewers.Value / Math.Max(1, st.Bc.Surge) * 0.5);
            ApplyViewers(st);
            Changed();
        }

        /// <summary>Ends the stream, archives the highlights, and lets the press know if many people watched.</summary>
        public StreamArchive EndStream(double ut)
        {
            var st = Stream;
            if (st == null) return null;
            Stream = null;
            var rng = new Random(OfflineWriter.StableHash(st.VesselId + st.StartUT.ToString("R", StreamInv)));
            var arch = SyncArchive(st, ut);
            FinishRecording(st, arch);
            speech.Clear();
            fetches.Clear();
            StopSounds();
            var pool = st.Chat.Where(c => !c.System).ToList();
            arch.Highlights.Clear();
            arch.Highlights.AddRange(pool.Where(c => c.Player).Take(5));
            arch.Highlights.AddRange(pool.Where(c => !c.Player).OrderBy(c => rng.Next()).Take(25));
            if (StreamEnded != null) { try { StreamEnded(arch); } catch (Exception) { } }
            if (st.Batches >= 2 && st.VesselId != "" && !State.IsSilent(st.VesselId))
                Ingest(new NewsEvent
                {
                    Type = EventTypes.Livestream, UT = ut, VesselId = st.VesselId, VesselName = st.VesselName,
                    Detail = "about " + st.PeakViewers.ToString("N0", StreamInv) + " people watched the agency's livestream of the flight" + (st.Moments.Count > 0 ? ", which showed: " + string.Join("; ", st.Moments.Take(5).ToArray()) : ""),
                });
            Changed();
            return arch;
        }
    }
}
