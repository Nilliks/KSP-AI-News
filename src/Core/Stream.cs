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
            if (Crew.Count > 0) sb.Append(", crew " + string.Join(", ", Crew.ToArray()));
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
        public string Frame = "";      // newest frame file under PluginData/Live
        public readonly List<double> AltitudeTrace = new List<double>();
    }

    /// <summary>A finished stream, kept in the save for the archive and for the press.</summary>
    public class StreamArchive
    {
        public string Id = "", VesselId = "", VesselName = "";
        public double StartUT, EndUT, MaxAltitude, MaxSpeed;
        public int PeakViewers;
        public List<string> Moments = new List<string>();
        public List<ChatMessage> Highlights = new List<ChatMessage>();

        public void Save(DataNode n)
        {
            n.Add("id", Id); n.Add("vessel", VesselId); n.AddText("vesselName", VesselName); n.Add("start", StartUT); n.Add("end", EndUT);
            n.Add("maxAlt", MaxAltitude); n.Add("maxSpeed", MaxSpeed); n.Add("peak", PeakViewers);
            foreach (var m in Moments) n.AddText("moment", m);
            foreach (var c in Highlights) { var cn = n.AddNode("CHAT"); cn.AddText("author", c.Author); cn.AddText("text", c.Text); cn.Add("agency", c.Agency); }
        }

        public static StreamArchive Load(DataNode n)
        {
            var a = new StreamArchive
            {
                Id = n.Get("id", ""), VesselId = n.Get("vessel", ""), VesselName = n.GetText("vesselName"), StartUT = n.GetDouble("start"), EndUT = n.GetDouble("end"),
                MaxAltitude = n.GetDouble("maxAlt"), MaxSpeed = n.GetDouble("maxSpeed"), PeakViewers = n.GetInt("peak"),
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

        public StreamSession StartStream(string vesselId, string vesselName, double ut, double realTime)
        {
            if (Stream != null) return Stream;
            var crewed = Tracked(vesselId) != null && Tracked(vesselId).Craft != null && Tracked(vesselId).Craft.Crew.Count > 0;
            Stream = new StreamSession { VesselId = vesselId ?? "", VesselName = vesselName ?? "", StartUT = ut, StartReal = realTime, NextBatchReal = realTime + 3 };
            Stream.Viewers = Stream.PeakViewers = (int)((200 + State.Mood.Excitement * 60) * (crewed ? 2 : 1));
            Stream.Chat.Add(new ChatMessage { System = true, Text = "Welcome to the " + AgencyName + " livestream of " + vesselName + "." });
            Changed();
            return Stream;
        }

        void Changed() { if (StreamChanged != null) StreamChanged(); }

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
            if (st.AltitudeTrace.Count > 120) st.AltitudeTrace.RemoveAt(0);
            if (moment != null) StreamMoment(moment, realTime);
        }

        public void StreamMoment(string what, double realTime)
        {
            var st = Stream;
            if (st == null || string.IsNullOrEmpty(what)) return;
            st.Moments.Add(what);
            st.NewMoments.Add(what);
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
                st.Queue.RemoveAt(0);
                changed = true;
            }
            if (st.Chat.Count > 300) st.Chat.RemoveRange(0, st.Chat.Count - 300);
            if (changed) Changed();
            if (streamBusy || realTime < st.NextBatchReal || st.Last == null) return;
            LastBatchReal = realTime;
            st.NextBatchReal = realTime + StreamInterval;
            st.Batches++;
            var moments = st.NewMoments.ToList();
            var said = st.PlayerSaid.ToList();
            st.NewMoments.Clear();
            st.PlayerSaid.Clear();
            if (!Settings.CanCallAi || !allowCalls)
            {
                PlayOut(st, OfflineWriter.StreamChat(this, st, moments, said), null, realTime);
                return;
            }
            streamBusy = true;
            var preset = Settings.Preset;
            var req = Providers.Build(preset, Settings.BaseUrl, Settings.Key, Settings.CheapModel, PromptBuilder.StreamSystem(State, AgencyName), PromptBuilder.StreamUser(this, st, moments, said), 2000, preset.JsonMode);
            req.TimeoutSeconds = 40;
            Transport.Send(req, res =>
            {
                streamBusy = false;
                if (Stream != st) return;
                string text, err;
                List<ChatMessage> msgs = null;
                int viewers = -1;
                if (Providers.TryExtractText(preset.Kind, res, out text, out err))
                {
                    object root;
                    var json = ResponseParser.ExtractJson(text);
                    if (json != null && Json.TryParse(json, out root))
                    {
                        msgs = new List<ChatMessage>();
                        foreach (var m in Json.Arr(Json.Get(root, "messages")) ?? new List<object>())
                        {
                            string t = ResponseParser.Clean(Json.Str(m, "text", ""));
                            if (t == "" || ResponseParser.ContainsForbidden(t)) continue;
                            msgs.Add(new ChatMessage { Author = ResponseParser.Clip(ResponseParser.Clean(Json.Str(m, "author", "viewer")), 30).TrimStart('@'), Text = ResponseParser.Clip(t, 240) });
                        }
                        viewers = (int)Json.Num(root, "viewers", -1);
                    }
                }
                if (msgs == null || msgs.Count == 0) { LastError = err ?? "Chat reply was empty"; msgs = OfflineWriter.StreamChat(this, st, moments, said); }
                PlayOut(st, msgs, viewers > 0 ? (int?)viewers : null, lastRealTime);
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
                case EventTypes.Launch: return "liftoff";
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

        void PlayOut(StreamSession st, List<ChatMessage> msgs, int? viewers, double realTime)
        {
            if (realTime <= 0) realTime = lastRealTime;
            double span = Math.Max(5, StreamInterval - 3);
            for (int i = 0; i < msgs.Count; i++)
                st.Queue.Add(new KeyValuePair<double, ChatMessage>(realTime + 0.5 + span * i / Math.Max(1, msgs.Count), msgs[i]));
            st.Queue.Sort((a, b) => a.Key.CompareTo(b.Key));
            int target = viewers.HasValue ? viewers.Value : st.Viewers;
            // Viewers drift toward the model's estimate, with bumps for big moments.
            st.Viewers = Math.Max(10, (int)(st.Viewers * 0.5 + target * 0.5));
            st.PeakViewers = Math.Max(st.PeakViewers, st.Viewers);
            Changed();
        }

        /// <summary>Ends the stream, archives the highlights, and lets the press know if many people watched.</summary>
        public StreamArchive EndStream(double ut)
        {
            var st = Stream;
            if (st == null) return null;
            Stream = null;
            var rng = new Random(OfflineWriter.StableHash(st.VesselId + st.StartUT.ToString("R", StreamInv)));
            var arch = new StreamArchive
            {
                Id = State.NewId("ls"), VesselId = st.VesselId, VesselName = st.VesselName, StartUT = st.StartUT, EndUT = ut,
                MaxAltitude = st.Max.Altitude, MaxSpeed = st.Max.Speed, PeakViewers = st.PeakViewers,
            };
            arch.Moments.AddRange(st.Moments.Take(20));
            var pool = st.Chat.Where(c => !c.System).ToList();
            arch.Highlights.AddRange(pool.Where(c => c.Player).Take(5));
            arch.Highlights.AddRange(pool.Where(c => !c.Player).OrderBy(c => rng.Next()).Take(25));
            State.Streams.Add(arch);
            if (State.Streams.Count > 30) State.Streams.RemoveAt(0);
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
