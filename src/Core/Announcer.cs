using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace KerbalNews
{
    /// <summary>One line the announcer said, for the caption under the video and the replay.</summary>
    public class AnnouncerLine
    {
        public double At;
        public string Text = "";
        public string Audio = "";
        public double Seconds;
    }

    /// <summary>
    /// The livestream announcer: a commentator who calls the countdown, liftoff, staging, orbit and landings the way a
    /// real launch broadcast does. Lines come from templates the moment something happens (no AI call), and on full
    /// commentary the stream's chat call also asks the AI for one line of colour. With an ElevenLabs key the lines are
    /// spoken; without one they show as captions. Short stock lines (countdown numbers) are cached so they're paid for once.
    /// </summary>
    public partial class NewsEngine
    {
        class Speech
        {
            public string Text;
            public double PlayAt, Deadline;
            public byte[] Pcm;
            public bool Ready, Count;
        }

        readonly List<Speech> speech = new List<Speech>();
        /// <summary>Host: plays 16-bit mono PCM at the given rate, at the announcer volume.</summary>
        public Action<byte[], int> PlayVoice;
        /// <summary>Host: a cache of short clips on disk, by Voice.CacheKey.</summary>
        public Func<string, byte[]> VoiceCacheGet;
        public Action<string, byte[]> VoiceCachePut;
        /// <summary>Voice off for the rest of the session after a bad key or an empty account, with the reason for the settings page.</summary>
        public bool VoiceBroken;
        public string VoiceStatus = "";
        public int VoiceCalls;
        double voiceFreeAt;
        bool lastWasCount;

        bool VoiceReady { get { return Settings.VoiceOn && !VoiceBroken && PlayVoice != null && Transport != null; } }

        /// <summary>Settings changed (new key, voice on again): give the voice another go.</summary>
        public void ResetVoice() { VoiceBroken = false; VoiceStatus = ""; }

        static readonly Dictionary<string, string[]> AnnLines = new Dictionary<string, string[]>
        {
            { "open", new[] { "Good day, and welcome to live coverage of {craft} from {agency}.", "Hello and welcome. This is {agency}, live with {craft}.", "Welcome to the {agency} broadcast. We're following {craft} today.", "This is {agency} mission coverage. {craft} is on our screens." } },
            { "count_start", new[] { "The countdown clock is running. We are at {clock}.", "And the count has begun. {clock} and counting.", "We are now counting. {clock}." } },
            { "count_min", new[] { "{clock} and counting.", "We are at {clock}. All stations report go.", "{clock}. The vehicle is looking good.", "{clock} on the countdown. Everything is proceeding normally." } },
            { "count_30", new[] { "T-minus thirty seconds.", "Thirty seconds. Guidance is internal.", "T-minus thirty seconds and counting." } },
            { "zero", new[] { "Zero.", "And zero." } },
            { "hold", new[] { "Hold, hold, hold. We are holding at {clock}.", "We've called a hold at {clock}. The team is looking at it.", "The count is holding at {clock}. Stand by." } },
            { "resume", new[] { "The count has resumed. {clock} and counting.", "And we're back in the count at {clock}.", "Hold released. Counting from {clock}." } },
            { "recycle", new[] { "We are recycling the count back to {clock}.", "The clock has been reset to {clock}. Holding there for now." } },
            { "scrub", new[] { "We have a scrub. There will be no launch today.", "That's a scrub for today. Thanks for watching, and we'll try again.", "The launch has been called off for today." } },
            { "late", new[] { "The clock has reached zero, and the vehicle is still on the pad. Stand by.", "We're past zero. No liftoff yet. The team is working the problem." } },
            { "liftoff", new[] { "And liftoff! Liftoff of {craft}!", "Liftoff! {craft} has cleared the pad.", "We have liftoff of {craft}!", "Liftoff, and {craft} is on its way!" } },
            { "liftoff_crew", new[] { "Liftoff! {craft} lifts off with {crew} aboard!", "And we have liftoff! {crew} are on their way!" } },
            { "stage", new[] { "Staging confirmed.", "And we have separation.", "Separation, and the next stage is lit.", "That's staging. The vehicle is looking good.", "Clean separation." } },
            { "onboard", new[] { "We're now looking at the view from the vehicle itself.", "Here's the onboard camera.", "You're seeing the view from the vehicle now." } },
            { "orbit", new[] { "And we are in orbit around {body}! {craft} is in orbit.", "Orbit confirmed. {craft} is circling {body}.", "That's orbit! Congratulations to the whole team." } },
            { "soi", new[] { "{craft} has entered {body}'s sphere of influence.", "We are now inside {body}'s sphere of influence." } },
            { "landing", new[] { "Touchdown! {craft} is down on {body}!", "Contact! We have touchdown on {body}.", "The vehicle is down on {body}. What a moment." } },
            { "splash", new[] { "Splashdown! {craft} is in the water.", "And splashdown. Welcome home.", "Splashdown confirmed." } },
            { "dock", new[] { "Capture confirmed. We are docked.", "Docking confirmed. Two vehicles, one.", "And we have a hard dock." } },
            { "boom", new[] { "We appear to have lost the vehicle.", "That is not what we wanted to see. We've lost the vehicle.", "Obviously a major malfunction. The vehicle is gone." } },
            { "anomaly", new[] { "We may have an anomaly there.", "We saw something come off the vehicle. Stand by.", "Looks like we've lost a piece of the vehicle." } },
            { "crew_lost", new[] { "We have lost the crew. Our thoughts are with their families.", "This is a dark day. The crew have been lost." } },
            { "milestone", new[] { "That's a first for {agency}.", "History is being made right now.", "A milestone moment for {agency}." } },
            { "telemetry", new[] { "Altitude {alt}, speed {speed} metres per second.", "{alt} up, travelling at {speed} metres per second.", "We're at {alt}. Velocity {speed} metres per second.", "Coming up on {alt}. Speed {speed}." } },
            { "coast", new[] { "The vehicle is coasting now. Apoapsis {ap}.", "Quiet on the vehicle as it coasts. Apoapsis is {ap}.", "Coasting, with an apoapsis of {ap}." } },
        };

        static readonly string[] CountWords = { "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten" };

        int annSeed;
        readonly Dictionary<string, int> annLast = new Dictionary<string, int>();

        string AnnLine(string rule, StreamSession st, Dictionary<string, string> extra = null)
        {
            string[] opts;
            if (!AnnLines.TryGetValue(rule, out opts) || opts.Length == 0) return "";
            var rng = new Random(OfflineWriter.StableHash(st.VesselId + rule) + annSeed++ * 7919);
            int last;
            int pick = rng.Next(opts.Length);
            if (opts.Length > 1 && annLast.TryGetValue(rule, out last) && last == pick) pick = (pick + 1 + rng.Next(opts.Length - 1)) % opts.Length;
            annLast[rule] = pick;
            var v = new Dictionary<string, string> { { "craft", SpokenName(st.VesselName) }, { "agency", AgencyName } };
            var tv = Tracked(st.VesselId);
            if (tv != null && tv.Craft != null && tv.Craft.Crew.Count > 0) v["crew"] = SpokenList(tv.Craft.Crew.Select(c => c.Split(' ')[0]).ToList());
            if (extra != null) foreach (var kv in extra) v[kv.Key] = kv.Value;
            Func<string, string> fill = o => { foreach (var kv in v) o = o.Replace("{" + kv.Key + "}", kv.Value); return o.Contains("{") ? "" : o; };
            // With the voice on, prefer lines already recorded (pre-recorded or said before): they play at once and cost nothing.
            if (VoiceReady && VoiceCacheGet != null && opts.Length > 1)
            {
                string mood = RuleMood(rule);
                var cached = Enumerable.Range(0, opts.Length).Where(i => i != (annLast.ContainsKey(rule + "#prev") ? annLast[rule + "#prev"] : -1))
                    .Where(i => { string t = fill(opts[i]); return t != "" && IsRecorded(t, mood); }).ToList();
                if (cached.Count > 0) pick = cached[rng.Next(cached.Count)];
            }
            annLast[rule + "#prev"] = pick;
            return fill(opts[pick]);
        }

        static string RuleMood(string rule)
        {
            return rule == "boom" || rule == "crew_lost" ? "loss"
                : rule == "hold" || rule == "scrub" || rule == "late" || rule == "anomaly" || rule == "recycle" ? "tense"
                : rule.StartsWith("liftoff") || rule == "orbit" || rule == "landing" || rule == "splash" || rule == "dock" || rule == "milestone" || rule == "soi" ? "big"
                : rule == "zero" || rule == "count_30" ? "count" : "routine";
        }

        string VoiceKeyFor(string text, string mood)
        {
            return Voice.CacheKey(Settings.VoiceId, Settings.VoiceModel + "|" + Settings.AnnouncerStyle, Voice.Spoken(text, mood, Settings.AnnouncerStyle, Settings.VoiceModel));
        }

        bool IsRecorded(string text, string mood)
        {
            if (VoiceCacheGet == null) return false;
            try { var b = VoiceCacheGet(VoiceKeyFor(text, mood)); return b != null && b.Length > 0; } catch (Exception) { return false; }
        }

        // ------------------------------------------------------------------ pre-recording

        static readonly string[] CommonBodies = { "Kerbin", "Mun", "Minmus", "Duna", "Eve" };

        /// <summary>Every announcer line that doesn't name the craft or read out live numbers, in the current voice's words:
        /// the countdown, the stock calls, orbit and landings at the usual places. These can be recorded once and reused.</summary>
        public List<KeyValuePair<string, string>> CommonLines()
        {
            var l = new List<KeyValuePair<string, string>>();
            Action<string, string> add = (t, mood) => { if (t != "" && !t.Contains("{") && !l.Any(x => x.Key == t && x.Value == mood)) l.Add(new KeyValuePair<string, string>(t, mood)); };
            for (int n = 10; n >= 1; n--) add(CountWords[n] + ".", "count");
            foreach (var kv in AnnLines)
            {
                if (kv.Key == "telemetry" || kv.Key == "coast") continue;
                string mood = RuleMood(kv.Key);
                foreach (var o in kv.Value)
                {
                    if (o.Contains("{craft}") || o.Contains("{crew}") || o.Contains("{alt}") || o.Contains("{speed}") || o.Contains("{ap}")) continue;
                    string t = o.Replace("{agency}", AgencyName);
                    if (t.Contains("{body}")) { foreach (var b in CommonBodies) add(t.Replace("{body}", b), mood); continue; }
                    if (t.Contains("{clock}"))
                    {
                        int[] mins = kv.Key == "count_min" ? new[] { 1, 2, 5, 10, 30 } : kv.Key == "count_start" ? new[] { 1, 5, 10, 30 } : kv.Key == "recycle" ? new[] { 1, 5, 10 } : new int[0];
                        foreach (var m in mins) add(t.Replace("{clock}", SpokenClock(m * 60)), mood);
                        continue;
                    }
                    add(t, mood);
                }
            }
            add(SampleLine, "big");
            return l;
        }

        public List<KeyValuePair<string, string>> LinesToRecord() { return CommonLines().Where(x => !IsRecorded(x.Key, x.Value)).ToList(); }

        public bool Prerecording { get; private set; }
        public string PrerecordStatus = "";

        /// <summary>Records the common lines one at a time (ElevenLabs limits parallel requests), saving each as it comes.</summary>
        public bool Prerecord(Action changed)
        {
            if (Prerecording || Settings.VoiceKey == "" || Transport == null || VoiceCachePut == null) return false;
            var todo = LinesToRecord();
            if (todo.Count == 0) { PrerecordStatus = "All common lines are already recorded."; return false; }
            Prerecording = true;
            int done = 0, total = todo.Count;
            string voice = Settings.VoiceId, model = Settings.VoiceModel, style = Settings.AnnouncerStyle;
            Action next = null;
            next = () =>
            {
                if (done >= todo.Count || Settings.VoiceId != voice || Settings.VoiceModel != model || Settings.AnnouncerStyle != style)
                {
                    Prerecording = false;
                    PrerecordStatus = done >= todo.Count ? "Recorded " + total + " lines. They play instantly and cost nothing from now on." : "Stopped: the voice settings changed. Press the button again for the new voice.";
                    changed();
                    return;
                }
                var item = todo[done];
                string spoken = Voice.Spoken(item.Key, item.Value, style, model);
                VoiceCalls++;
                Transport.Send(Voice.Build(Settings.VoiceKey, voice, model, spoken, style), res =>
                {
                    byte[] pcm;
                    string err;
                    if (Voice.TryExtract(res, out pcm, out err))
                    {
                        try { VoiceCachePut(Voice.CacheKey(voice, model + "|" + style, spoken), pcm); } catch (Exception) { }
                        done++;
                        PrerecordStatus = "Recording... " + done + " of " + total;
                        changed();
                        next();
                    }
                    else
                    {
                        Prerecording = false;
                        VoiceStatus = err;
                        PrerecordStatus = "Stopped after " + done + " of " + total + ": " + err;
                        changed();
                    }
                });
            };
            PrerecordStatus = "Recording... 0 of " + total;
            next();
            return true;
        }

        static string SpokenName(string name)
        {
            name = (name ?? "").Trim();
            return name == "" ? "the vehicle" : name;
        }

        static string SpokenList(List<string> names)
        {
            if (names.Count <= 1) return names.FirstOrDefault() ?? "";
            return string.Join(", ", names.Take(names.Count - 1).ToArray()) + " and " + names.Last();
        }

        /// <summary>"T-minus five minutes", "T-minus one minute thirty", "T-minus forty-five seconds".</summary>
        public static string SpokenClock(double secs)
        {
            int s = (int)Math.Round(Math.Abs(secs));
            int h = s / 3600, m = s % 3600 / 60, r = s % 60;
            string body;
            if (h > 0) body = h + " hour" + (h > 1 ? "s" : "") + (m > 0 ? " " + m + " minute" + (m > 1 ? "s" : "") : "");
            else if (m > 0) body = m + " minute" + (m > 1 ? "s" : "") + (r > 0 ? " " + r + " second" + (r > 1 ? "s" : "") : "");
            else body = r + " second" + (r != 1 ? "s" : "");
            return (secs >= 0 ? "T-minus " : "T-plus ") + body;
        }

        public static string SpokenKm(double m)
        {
            var inv = CultureInfo.InvariantCulture;
            if (Math.Abs(m) >= 10000) return (m / 1000).ToString(Math.Abs(m) >= 100000 ? "0" : "0.#", inv) + " kilometres";
            return (Math.Round(m / 10) * 10).ToString("0", inv) + " metres";
        }

        /// <summary>A moment on the stream, as the announcer calls it. Which moments get a line depends on the setting.</summary>
        void AnnounceMoment(StreamSession st, string what, double realTime)
        {
            int mode = Settings.Announcer;
            if (mode <= 0) return;
            string m = what.ToLowerInvariant();
            var x = new Dictionary<string, string>();
            string rule = null;
            if (m.StartsWith("liftoff"))
            {
                speech.RemoveAll(s => s.Count);
                var tv = Tracked(st.VesselId);
                rule = tv != null && tv.Craft != null && tv.Craft.Crew.Count > 0 ? "liftoff_crew" : "liftoff";
            }
            else if (m.StartsWith("the launch countdown started")) { rule = "count_start"; x["clock"] = SpokenClock(st.CountLeft); }
            else if (m.StartsWith("hold:")) { rule = "hold"; x["clock"] = SpokenClock(st.CountLeft); }
            else if (m.StartsWith("the countdown resumed")) { rule = "resume"; x["clock"] = SpokenClock(st.CountLeft); }
            else if (m.Contains("recycled")) { rule = "recycle"; x["clock"] = SpokenClock(st.CountLeft); }
            else if (m.StartsWith("scrub")) rule = "scrub";
            else if (m.StartsWith("t-0 was")) rule = "late";
            else if (m.StartsWith("t-0")) rule = st.FinalCountSaid ? "zero" : null;
            else if (m.StartsWith("t-10 seconds")) rule = null;   // the final count calls each number itself
            else if (m.StartsWith("t-30")) rule = "count_30";
            else if (m.StartsWith("t-") && m.Contains("minute"))
            {
                var mm = Regex.Match(m, @"t-(\d+) minute");
                rule = "count_min";
                x["clock"] = SpokenClock(mm.Success ? int.Parse(mm.Groups[1].Value, CultureInfo.InvariantCulture) * 60 : st.CountLeft);
            }
            else if (m.Contains("reached orbit")) { rule = "orbit"; x["body"] = BodyFrom(what, "orbit around "); }
            else if (m.Contains("sphere of influence")) { rule = "soi"; x["body"] = Regex.Match(what, @"entered (\w[\w ]*?)'s").Groups[1].Value; }
            else if (m.StartsWith("touchdown")) { rule = "landing"; x["body"] = BodyFrom(what, "touchdown on "); }
            else if (m.StartsWith("splashdown")) rule = "splash";
            else if (m.StartsWith("docking")) rule = "dock";
            else if (m.Contains("crew lost")) rule = "crew_lost";
            else if (m.Contains("destroyed")) rule = "boom";
            else if (m.Contains("part of the vessel exploded")) rule = st.Lifted && realTime - st.LastAnnReal > 8 ? "anomaly" : null;
            else if (m.StartsWith("milestone")) rule = "milestone";
            else if (mode >= 2 && m.StartsWith("stage ") && st.Lifted) rule = realTime - st.LastAnnReal > 12 ? "stage" : null;
            else if (mode >= 2 && m.Contains("onboard hull cameras")) rule = realTime - st.LastAnnReal > 10 ? "onboard" : null;
            if (rule == null) return;
            if (x.ContainsKey("body") && x["body"] == "") x["body"] = st.Last != null ? st.Last.Body : "Kerbin";
            string line = AnnLine(rule, st, x);
            if (line == "") return;
            bool urgent = rule == "liftoff" || rule == "liftoff_crew" || rule == "boom" || rule == "crew_lost" || rule == "hold" || rule == "scrub";
            if (urgent) speech.RemoveAll(s => !s.Count);   // nothing queued should hold up a liftoff or a loss
            Say(st, line, realTime, 0, urgent ? 6 : 12, false, RuleMood(rule));
        }

        static string BodyFrom(string what, string after)
        {
            int i = what.IndexOf(after, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return "";
            string rest = what.Substring(i + after.Length).Trim();
            int cut = rest.IndexOfAny(new[] { ' ', ',', '(' });
            return cut > 0 ? rest.Substring(0, cut) : rest;
        }

        /// <summary>Queues a line. playAt 0 means as soon as it's ready; maxWait is how stale it may get before it's dropped.</summary>
        void Say(StreamSession st, string text, double realTime, double playAt, double maxWait, bool count = false, string mood = "routine")
        {
            if (count) mood = "count";
            text = (text ?? "").Trim();
            if (Settings.Announcer <= 0 || text == "" || st == null) return;
            double at = playAt > 0 ? playAt : realTime;
            var sp = new Speech { Text = text, PlayAt = at, Deadline = at + maxWait, Count = count };
            speech.Add(sp);
            st.LastAnnReal = Math.Max(st.LastAnnReal, realTime);
            if (!VoiceReady) { sp.Ready = true; return; }
            string spoken = Voice.Spoken(text, mood, Settings.AnnouncerStyle, Settings.VoiceModel);
            string key = Voice.CacheKey(Settings.VoiceId, Settings.VoiceModel + "|" + Settings.AnnouncerStyle, spoken);
            byte[] cached = null;
            if (VoiceCacheGet != null) { try { cached = VoiceCacheGet(key); } catch (Exception) { cached = null; } }
            if (cached != null && cached.Length > 0) { sp.Pcm = cached; sp.Ready = true; return; }
            VoiceCalls++;
            bool keep = count || st.VesselName == "" || !text.Contains(st.VesselName);   // anything without the craft's name can come up again
            Transport.Send(Voice.Build(Settings.VoiceKey, Settings.VoiceId, Settings.VoiceModel, spoken, Settings.AnnouncerStyle), res =>
            {
                byte[] pcm;
                string err;
                if (Voice.TryExtract(res, out pcm, out err))
                {
                    sp.Pcm = pcm;
                    VoiceStatus = "";
                    if (keep && VoiceCachePut != null) { try { VoiceCachePut(key, pcm); } catch (Exception) { } }
                }
                else
                {
                    VoiceStatus = err;
                    if (Voice.IsFatal(res)) VoiceBroken = true;
                }
                sp.Ready = true;
            });
        }

        /// <summary>The final ten seconds, one number a second, fetched a few seconds early so each lands on time.</summary>
        void TickFinalCount(StreamSession st, double realTime)
        {
            if (Settings.Announcer <= 0 || st.Countdown != "counting" || st.FinalCountQueued) return;
            double left = CountdownLeft(realTime);
            if (double.IsNaN(left) || left > 14 || left < 1) return;
            st.FinalCountQueued = true;
            st.FinalCountSaid = true;
            speech.RemoveAll(s => !s.Count && s.Pcm == null);
            for (int n = 10; n >= 1; n--)
                if (n <= left + 0.2) Say(st, CountWords[n] + ".", realTime, realTime + (left - n), 0.7, true);
        }

        /// <summary>A hold, recycle or scrub stops the numbers that haven't been said.</summary>
        void StopFinalCount(StreamSession st)
        {
            speech.RemoveAll(s => s.Count);
            if (st != null) st.FinalCountQueued = false;
        }

        /// <summary>On full commentary, a telemetry call now and then on a quiet climb, when nothing has been said for a while.</summary>
        void TickCommentary(StreamSession st, double realTime)
        {
            if (Settings.Announcer < 2 || !st.Lifted || st.Last == null || realTime - st.LastAnnReal < 40 || speech.Count > 0) return;
            var t = st.Last;
            var inv = CultureInfo.InvariantCulture;
            string line;
            bool climbing = st.Previous != null && t.Altitude > st.Previous.Altitude + 5;
            if (t.Situation == "FLYING" || t.Situation == "SUB_ORBITAL" && climbing && t.Altitude < 70000)
                line = AnnLine("telemetry", st, new Dictionary<string, string> { { "alt", SpokenKm(t.Altitude) }, { "speed", t.Speed.ToString("N0", inv) } });
            else if (t.Situation == "SUB_ORBITAL" && t.Apoapsis > 0)
                line = AnnLine("coast", st, new Dictionary<string, string> { { "ap", SpokenKm(t.Apoapsis) } });
            else return;
            Say(st, line, realTime, 0, 10);
        }

        /// <summary>Plays the next line when it's due and the last one has finished, and puts it under the video.</summary>
        void TickVoice(StreamSession st, double realTime)
        {
            speech.RemoveAll(s => realTime > s.Deadline);
            if (speech.Count == 0) { if (st.Caption != "" && realTime > st.CaptionUntil) { st.Caption = ""; Changed(); } return; }
            var next = speech.Where(s => s.Ready && s.PlayAt <= realTime).OrderBy(s => s.PlayAt).FirstOrDefault();
            if (next == null || realTime < voiceFreeAt - (next.Count ? 0.2 : 0) && !(next.Count && lastWasCount)) { if (st.Caption != "" && realTime > st.CaptionUntil) { st.Caption = ""; Changed(); } return; }
            speech.Remove(next);
            lastWasCount = next.Count;   // the next number cuts off this one rather than falling behind the clock
            double dur = Voice.Seconds(next.Pcm);
            int words = next.Text.Split(' ').Length;
            var line = new AnnouncerLine { At = realTime - st.StartReal, Text = next.Text, Seconds = dur };
            if (next.Pcm != null && next.Pcm.Length > 0)
            {
                try { PlayVoice(next.Pcm, Voice.Rate); } catch (Exception) { }
                voiceFreeAt = realTime + dur + (next.Count ? 0.05 : 0.35);
                if (st.RecFolder != "" && SaveReplayFile != null)
                {
                    line.Audio = "v" + (st.Said.Count + 1).ToString("000", CultureInfo.InvariantCulture) + ".wav";
                    try { SaveReplayFile(st.RecFolder, line.Audio, Voice.Wav(next.Pcm)); } catch (Exception) { line.Audio = ""; }
                }
            }
            else voiceFreeAt = realTime + (next.Count ? 0.6 : 1.5);   // captions only: a short gap so lines don't flash past
            st.Said.Add(line);
            st.Caption = next.Text;
            st.CaptionUntil = realTime + Math.Max(next.Count ? 1 : 4, Math.Max(dur + 1.5, words * 0.4));
            st.LastAnnReal = realTime;
            Record(st, realTime, "ann", "x", line.Text, "v", line.Audio, "d", Math.Round(dur, 2));
            Changed();
        }

        public const string SampleLine = "T-minus ten seconds, and counting... Three, two, one... Liftoff! We have liftoff!";

        /// <summary>
        /// A sample of the chosen voice, model and style from the settings page (also when a voice is picked). The same sample
        /// is saved, so listening to a voice again costs nothing.
        /// </summary>
        public bool VoiceTest(Action<string> done)
        {
            if (Settings.VoiceKey == "") { done("Save an ElevenLabs key first, then pick a voice to hear it."); return false; }
            if (Transport == null || PlayVoice == null) { done("The voice can only play inside the game."); return false; }
            string spoken = Voice.Spoken(SampleLine, "big", Settings.AnnouncerStyle, Settings.VoiceModel);
            string key = Voice.CacheKey(Settings.VoiceId, Settings.VoiceModel + "|" + Settings.AnnouncerStyle, spoken);
            string who = VoiceLabel(Settings.VoiceId);
            byte[] cached = null;
            if (VoiceCacheGet != null) { try { cached = VoiceCacheGet(key); } catch (Exception) { cached = null; } }
            if (cached != null && cached.Length > 0)
            {
                try { PlayVoice(cached, Voice.Rate); } catch (Exception) { }
                done("Playing " + who + ".");
                return true;
            }
            VoiceCalls++;
            Transport.Send(Voice.Build(Settings.VoiceKey, Settings.VoiceId, Settings.VoiceModel, spoken, Settings.AnnouncerStyle), res =>
            {
                byte[] pcm;
                string err;
                if (Voice.TryExtract(res, out pcm, out err))
                {
                    VoiceBroken = false; VoiceStatus = "";
                    if (VoiceCachePut != null) { try { VoiceCachePut(key, pcm); } catch (Exception) { } }
                    try { PlayVoice(pcm, Voice.Rate); } catch (Exception) { }
                    done("Playing " + who + ".");
                }
                else { VoiceStatus = err; done(err); }
            });
            return true;
        }

        /// <summary>A voice's name, from the account's list if it was loaded, else the stock list.</summary>
        public string VoiceLabel(string id)
        {
            foreach (var v in Settings.FetchedVoiceList) if (v[0] == id) return v[1];
            return Voice.VoiceName(id);
        }

        /// <summary>Loads the voices on the player's ElevenLabs account into the settings list.</summary>
        public void LoadVoices(Action<string> done)
        {
            if (Settings.VoiceKey == "" || Transport == null) { done("Save an ElevenLabs key first."); return; }
            Transport.Send(Voice.ListVoices(Settings.VoiceKey), res =>
            {
                string err;
                var list = Voice.ParseVoices(res, out err);
                if (list == null) { done(err); return; }
                Settings.FetchedVoices = string.Join(";", list.ToArray());
                done("Loaded " + list.Count + " voices from your ElevenLabs account.");
            });
        }
    }
}
