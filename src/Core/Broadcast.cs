using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KerbalNews
{
    /// <summary>What the launch broadcast has called so far: callouts, the poll, a loss, the outcome, and the audience.</summary>
    public class BroadcastState
    {
        public bool Lost, Tower, Super, Bucket, MaxQ, ThrottleUp, Meco, Thrusting, PollDone, RecapSaid, Orbited;
        public double LiftoffReal = -1, LiftAlt, PeakThrottle, BucketThrottle, QPeak;
        public double CutSince = -1, ThrustSince = -1, BurnStart = -1, LastStageReal = -1000, LastStageCall = -1000, LastCutCall = -1000, LastIgniteCall = -1000;
        public double LostReal = -1, AnomalyReal = -1000, OutcomeReal = -1;
        public string Outcome = "", OutcomeBody = "";
        /// <summary>The audience: a base from the chat model, times a surge that jumps at big moments and fades.</summary>
        public double ViewerBase, Surge = 1, SurgeTarget = 1, SurgeDecay = 240, ViewerTick, LastViewerReal = -1, Wobble;
    }

    /// <summary>
    /// The parts of a real launch broadcast beyond the commentator: a flight controller on the radio loop (callouts such as
    /// supersonic, max Q and MECO, the go/no-go poll, confirmations that arrive a moment after the event, or after the
    /// signal delay from far away), the slow ladder from "anomaly" to "we've lost the vehicle", crowd and control-room
    /// sound, a recap at the end, and an audience that floods in at big moments and drifts off during holds.
    /// </summary>
    public partial class NewsEngine
    {
        /// <summary>Host: plays a sound on a channel ("room" loops under the stream, "crowd" and "rumble" play once) at a
        /// volume from 0 to 1; StopSound(null) stops them all.</summary>
        public Action<string, byte[], bool, double> PlaySound;
        public Action<string> StopSound;
        readonly Random bcRng = new Random(1234);

        double Rand(double a, double b) { return a + bcRng.NextDouble() * (b - a); }

        static readonly Dictionary<string, double> SoundLevels = new Dictionary<string, double>
        {
            { "room", 0.22 }, { "cheer", 0.8 }, { "rumble", 0.75 }, { "applause", 0.7 }, { "gasp", 0.9 }, { "murmur", 0.5 },
        };

        byte[] SoundClip(string name)
        {
            if (VoiceCacheGet == null) return null;
            try { var b = VoiceCacheGet(SfxKey(name)); return b != null && b.Length > 0 ? b : null; } catch (Exception) { return null; }
        }

        /// <summary>Plays a recorded sound effect (never fetched mid-stream: they are made once from the settings page).</summary>
        bool PlaySfx(StreamSession st, string name, double realTime)
        {
            if (PlaySound == null || Settings.SfxVolume <= 0) return false;
            var pcm = SoundClip(name);
            if (pcm == null) return false;
            double level;
            if (!SoundLevels.TryGetValue(name, out level)) level = 0.7;
            string channel = name == "room" ? "room" : name == "rumble" ? "rumble" : "crowd";
            try { PlaySound(channel, pcm, name == "room", level * Settings.SfxVolume); } catch (Exception) { }
            if (st != null) Record(st, realTime, "sfx", "x", name);
            return true;
        }

        void StopSounds(string channel = null) { if (StopSound != null) { try { StopSound(channel); } catch (Exception) { } } }

        /// <summary>A replay passing a sound in its timeline.</summary>
        void ReplaySfx(string name) { PlaySfx(null, name, 0); }

        // ------------------------------------------------------------------ moments

        void CallMoment(StreamSession st, string rule, Dictionary<string, string> x, double realTime)
        {
            var bc = st.Bc;
            int mode = Settings.Announcer;
            Func<string, string> line = r => AnnLine(r, st, x);
            switch (rule)
            {
                case "liftoff":
                case "liftoff_crew":
                    speech.RemoveAll(s => !s.Count);
                    bc.LiftoffReal = realTime;
                    bc.LiftAlt = st.Last != null ? st.Last.Altitude : 0;
                    PlaySfx(st, "rumble", realTime);
                    PlaySfx(st, "cheer", realTime);
                    SurgeTo(st, 1.7, 300);
                    Say(st, line(rule), realTime, 0, 6, false, "big");
                    return;
                case "stage":
                    bc.LastStageReal = realTime;
                    if (bc.Lost || realTime - bc.LastStageCall < 6) return;
                    bc.LastStageCall = realTime;
                    bool quiet = realTime - st.LastAnnReal > 20;
                    // The confirmation comes a beat after the event, once the data shows it.
                    Say(st, line("c_stage"), realTime, realTime + Rand(1.1, 1.9), 6, false, "routine", "ctl");
                    if (mode >= 2 && Settings.ControllerOn && quiet) Say(st, line("stage"), realTime, realTime + 2, 8, false, "routine", "host", 0.8);
                    return;
                case "anomaly":
                    if (bc.Lost || realTime - bc.AnomalyReal < 15) return;
                    bc.AnomalyReal = realTime;
                    PlaySfx(st, "gasp", realTime);
                    Say(st, line("c_anomaly"), realTime, realTime + Rand(0.6, 1.1), 6, false, "tense", "ctl");
                    Say(st, line("anomaly"), realTime, realTime + 1.2, 10, false, "tense", "host", 0.8);
                    return;
                case "boom":
                {
                    if (bc.Lost) return;
                    bc.Lost = true; bc.LostReal = realTime; bc.Outcome = "lost";
                    // A real commentator goes quiet for a moment before finding the words: stop mid-sentence, a beat of
                    // silence, the controller reports no data, "stand by", and only then the confirmation.
                    if (realTime < voiceFreeAt && StopVoice != null) { try { StopVoice(); } catch (Exception) { } }
                    speech.RemoveAll(s => !s.Count);
                    voiceFreeAt = Math.Min(voiceFreeAt, realTime);
                    if (st.Caption != "") { st.Caption = ""; Changed(); }
                    if (realTime - bc.AnomalyReal > 4) PlaySfx(st, "gasp", realTime);
                    double beat = Rand(1.6, 2.5);
                    Say(st, line("c_los"), realTime, realTime + beat, 20, false, "loss", "ctl");
                    Say(st, line("standby"), realTime, realTime + beat + 0.01, 25, false, "tense", "host", 1.4, "", "murmur");
                    Say(st, line("boom"), realTime, realTime + beat + 0.02, 30, false, "loss", "host", 2.6);
                    SurgeTo(st, 2.2, 700);
                    return;
                }
                case "crew_lost":
                {
                    double at = realTime;
                    if (!bc.Lost)
                    {
                        bc.Lost = true; bc.LostReal = realTime;
                        if (realTime < voiceFreeAt && StopVoice != null) { try { StopVoice(); } catch (Exception) { } }
                        speech.RemoveAll(s => !s.Count);
                        at = realTime + Rand(1.6, 2.5);
                    }
                    bc.Outcome = "lost";
                    Say(st, line("crew_lost"), realTime, at + 0.03, 45, false, "loss", "host", 2.6);
                    return;
                }
                case "orbit":
                case "landing":
                case "splash":
                case "dock":
                {
                    if (bc.Lost) return;
                    double lag = SignalLag(st);
                    string body = x.ContainsKey("body") ? x["body"] : st.Last != null ? st.Last.Body : "Kerbin";
                    if (lag > 5)
                        Say(st, AnnLine("lag", st, new Dictionary<string, string> { { "lag", SpokenSeconds(lag) }, { "body", body } }), realTime, realTime + 1, 10, false, "tense");
                    double at = realTime + lag + Rand(1.2, 2.2);
                    string ctl = rule == "orbit" ? "c_orbit" : rule == "landing" ? "c_touchdown" : rule == "splash" ? "c_splash" : "c_dock";
                    Say(st, line(ctl), realTime, at, 12, false, RuleMood(ctl), "ctl");
                    Say(st, line(rule), realTime, at + 0.01, 16, false, "big", "host", 0.5, "", rule == "dock" ? "applause" : rule == "orbit" ? "applause" : "cheer");
                    if (rule == "orbit") bc.Orbited = true;
                    if (rule != "dock") { bc.Outcome = rule == "orbit" ? "orbit" : rule; bc.OutcomeBody = body; bc.OutcomeReal = realTime + lag; }
                    SurgeTo(st, rule == "landing" ? 1.9 : 1.4, 400);
                    return;
                }
                case "hold":
                case "recycle":
                case "scrub":
                    if (speech.RemoveAll(s => s.Tag == "poll") > 0) bc.PollDone = false;   // the poll starts over on resume
                    SurgeTo(st, rule == "scrub" ? 0.35 : 0.9, rule == "scrub" ? 900 : 300);
                    if (rule == "scrub") PlaySfx(st, "murmur", realTime);
                    break;
                case "count_30":
                    SurgeTo(st, 1.25, 200);
                    break;
            }
            string text = line(rule);
            if (text == "") return;
            bool urgent = rule == "hold" || rule == "scrub";
            if (urgent) speech.RemoveAll(s => !s.Count);
            Say(st, text, realTime, 0, urgent ? 6 : 12, false, RuleMood(rule));
        }

        /// <summary>Seconds for a signal to reach Kerbin from the craft, capped so nobody waits minutes on a stream.</summary>
        static double SignalLag(StreamSession st) { return st.Last == null ? 0 : Math.Max(0, Math.Min(45, st.Last.SignalDelay)); }

        static string SpokenSeconds(double s)
        {
            int n = (int)Math.Round(s);
            return n < 60 ? "about " + n + " seconds" : "about " + (n / 60) + " minute" + (n >= 120 ? "s" : "");
        }

        // ------------------------------------------------------------------ the flight controller's callouts

        /// <summary>
        /// Host, a few times a second while streaming: the craft's numbers for the controller's callouts. Calls each one
        /// once, at the moment it happens: tower cleared, supersonic, throttle down for max Q, max Q, throttle up, engine
        /// cutoff (MECO for the first), and ignition of the next stage.
        /// </summary>
        public void StreamFlight(LiveNumbers n, double realTime)
        {
            var st = Stream;
            if (st == null || n == null) return;
            RecordOverlay(st, n, realTime);
            if (Settings.Announcer <= 0 || !st.Lifted || st.Bc.Lost) return;
            var bc = st.Bc;
            bool inAir = n.AtmosphereTop > 0 && n.Altitude < n.AtmosphereTop;
            bool ascent = bc.LiftoffReal > 0 && realTime - bc.LiftoffReal < 600;
            Action<string, string> call = (rule, mood) => Say(st, AnnLine(rule, st), realTime, realTime + Rand(0.2, 0.6), 4, false, mood, "ctl");

            if (!bc.Tower && ascent && bc.LiftoffReal > 0 && realTime - bc.LiftoffReal < 25 && n.Altitude - bc.LiftAlt > 70) { bc.Tower = true; call("c_tower", "routine"); }
            if (!bc.Super && ascent && inAir && n.Mach >= 1.0) { bc.Super = true; call("c_super", "routine"); }

            bool thrusting = n.Thrust > 0.5;
            if (thrusting) bc.PeakThrottle = Math.Max(bc.PeakThrottle, n.Throttle);
            if (ascent && inAir && n.Q > bc.QPeak) bc.QPeak = n.Q;
            if (!bc.Bucket && !bc.MaxQ && ascent && inAir && thrusting && bc.PeakThrottle > 0.5 && n.Throttle < bc.PeakThrottle - 0.15 && n.Q > 2)
            { bc.Bucket = true; bc.BucketThrottle = n.Throttle; call("c_bucket", "routine"); }
            if (!bc.MaxQ && ascent && inAir && bc.QPeak > 4 && n.Q < bc.QPeak * 0.9) { bc.MaxQ = true; call("c_maxq", "routine"); }
            if (bc.Bucket && !bc.ThrottleUp && thrusting && n.Throttle > bc.BucketThrottle + 0.15) { bc.ThrottleUp = true; call("c_throttle_up", "routine"); }

            // Cutoff: the engines stay quiet for a moment and it wasn't the stage falling away (that has its own call).
            if (thrusting)
            {
                if (!bc.Thrusting)
                {
                    bc.Thrusting = true;
                    bc.BurnStart = realTime;
                    bool afterQuiet = bc.CutSince > 0 && realTime - bc.CutSince > 0.5;
                    if (afterQuiet && n.Altitude - bc.LiftAlt > 500 && realTime - bc.LastIgniteCall > 20 && realTime - bc.LiftoffReal > 5)
                    { bc.LastIgniteCall = realTime; call("c_ignite", "routine"); }
                }
                bc.CutSince = -1;
            }
            else
            {
                if (bc.Thrusting) { bc.Thrusting = false; bc.CutSince = realTime; }
                if (bc.CutSince > 0 && realTime - bc.CutSince > 0.75 && bc.BurnStart > 0)
                {
                    bool staged = bc.LastStageReal > bc.CutSince - 1.5;
                    bool longBurn = bc.CutSince - bc.BurnStart > 3;
                    if (!staged && longBurn && realTime - bc.LastCutCall > 20)
                    {
                        bc.LastCutCall = realTime;
                        call(bc.Meco ? "c_cutoff" : "c_meco", "routine");
                        bc.Meco = true;
                    }
                    if (staged) bc.Meco = true;   // the first stage burned out and fell away: no MECO later
                    bc.BurnStart = -1;
                }
            }
        }

        // ------------------------------------------------------------------ the go/no-go poll

        void StartPoll(StreamSession st, double realTime)
        {
            st.Bc.PollDone = true;
            var tv = Tracked(st.VesselId);
            bool crewed = tv != null && tv.Craft != null && tv.Craft.Crew.Count > 0 || st.Last != null && st.Last.Crew.Count > 0;
            double t = realTime + 0.5, k = 0;
            Func<double> at = () => t + (k += 0.001);
            Say(st, AnnLine("poll", st), realTime, at(), 60, false, "routine", "host", 0, "poll");
            Say(st, AnnLine("c_poll_open", st), realTime, at(), 60, false, "routine", "ctl", 0.7, "poll");
            foreach (var s in PollStations)
            {
                if (s[0] == "Crew support." && !crewed) continue;
                Say(st, s[0], realTime, at(), 60, false, "routine", "ctl", 0.6, "poll");
                Say(st, AnnLine("s_go", st), realTime, at(), 60, false, "routine", "stn", 0.2, "poll");
            }
            Say(st, AnnLine("c_poll_close", st), realTime, at(), 60, false, "big", "ctl", 0.7, "poll");
            Say(st, AnnLine("poll_go", st), realTime, at(), 60, false, "big", "host", 0.8, "poll");
        }

        // ------------------------------------------------------------------ every frame

        void TickBroadcast(StreamSession st, double realTime)
        {
            var bc = st.Bc;
            TickViewers(st, realTime);
            if (Settings.Announcer <= 0) return;
            if (st.Countdown == "counting" && !bc.PollDone)
            {
                double left = CountdownLeft(realTime);
                if (left < 45 || !Settings.ControllerOn) bc.PollDone = true;   // one voice polling itself would sound silly
                else if (left <= 300 && realTime - st.CountMark >= 3) StartPoll(st, realTime);
            }
            if (!bc.RecapSaid && speech.Count == 0 && realTime > voiceFreeAt + 2)
            {
                bool lostDone = bc.Outcome == "lost" && realTime - bc.LostReal > 14;
                bool down = (bc.Outcome == "landing" || bc.Outcome == "splash") && bc.OutcomeReal > 0 && realTime - bc.OutcomeReal > 20
                    && st.Last != null && st.Last.Speed < 2 && (st.Last.Situation == "LANDED" || st.Last.Situation == "SPLASHED");
                if (lostDone || down) Recap(st, realTime);
            }
        }

        /// <summary>The closing summary, the way a broadcast wraps up before the press conference.</summary>
        void Recap(StreamSession st, double realTime)
        {
            var bc = st.Bc;
            bc.RecapSaid = true;
            var inv = CultureInfo.InvariantCulture;
            string craft = SpokenName(st.VesselName);
            string text = "To recap: " + craft + " lifted off and climbed to " + SpokenKm(st.Max.Altitude) + ", at up to " + Math.Round(st.Max.Speed).ToString("N0", inv) + " metres per second.";
            if (bc.Outcome == "lost" && bc.LiftoffReal > 0)
                text += bc.LostReal - bc.LiftoffReal < 15 ? " The vehicle was lost moments after liftoff." : " The vehicle was lost " + SpokenSeconds(bc.LostReal - bc.LiftoffReal).Replace("about ", "") + " into the flight.";
            else if (bc.Outcome == "landing") text += (bc.Orbited ? " It reached orbit, then" : " It") + " touched down safely on " + (bc.OutcomeBody != "" ? bc.OutcomeBody : "the surface") + ".";
            else if (bc.Outcome == "splash") text += (bc.Orbited ? " It reached orbit, then" : " It") + " splashed down safely.";
            Say(st, text, realTime, realTime, 20, false, bc.Outcome == "lost" ? "loss" : "routine");
            Say(st, AnnLine(bc.Outcome == "lost" ? "recap_loss" : "signoff", st), realTime, realTime + 0.01, 30, false, bc.Outcome == "lost" ? "loss" : "routine", "host", 0.6);
        }

        // ------------------------------------------------------------------ the audience

        void SurgeTo(StreamSession st, double mult, double fade)
        {
            st.Bc.SurgeTarget = mult;
            st.Bc.SurgeDecay = fade;
        }

        void TickViewers(StreamSession st, double realTime)
        {
            var bc = st.Bc;
            if (realTime < bc.ViewerTick) return;
            double dt = bc.LastViewerReal < 0 ? 1 : Math.Max(0, Math.Min(5, realTime - bc.LastViewerReal));
            bc.LastViewerReal = realTime;
            bc.ViewerTick = realTime + 1;
            // News of a big moment spreads in seconds; people leave more slowly.
            double k = 1 - Math.Exp(-dt / (bc.SurgeTarget > bc.Surge ? 6 : 25));
            bc.Surge += (bc.SurgeTarget - bc.Surge) * k;
            bc.SurgeTarget += (1 - bc.SurgeTarget) * (1 - Math.Exp(-dt / Math.Max(30, bc.SurgeDecay)));
            if (st.Countdown == "hold") bc.ViewerBase *= Math.Exp(-dt / 1500);   // a long hold bleeds viewers
            bc.Wobble = Math.Max(-0.04, Math.Min(0.04, bc.Wobble + (bcRng.NextDouble() - 0.5) * 0.012 * dt));
            ApplyViewers(st);
        }

        void ApplyViewers(StreamSession st)
        {
            var bc = st.Bc;
            int v = Math.Max(10, (int)(bc.ViewerBase * bc.Surge * (1 + bc.Wobble)));
            if (v == st.Viewers) return;
            st.Viewers = v;
            st.PeakViewers = Math.Max(st.PeakViewers, v);
            Changed();
        }
    }
}
