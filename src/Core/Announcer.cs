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
        /// <summary>"host" (the commentator), "ctl" (the flight controller on the radio) or "stn:Prop" (a station on the loop).</summary>
        public string Speaker = "host";
        public string Audio = "";
        public double Seconds;
    }

    /// <summary>
    /// The livestream announcer: a commentator who calls the countdown, liftoff, staging, orbit and landings the way a
    /// real launch broadcast does. Lines come from templates the moment something happens (no AI call), and on full
    /// commentary the stream's chat call also asks the AI for one line of colour. With an ElevenLabs key the lines are
    /// spoken; without one they show as captions. Short stock lines (countdown numbers) are cached so they're paid for once.
    /// </summary>
    /// <summary>A line that can be recorded once: its words (Key), mood (Value) and who says it. Sound effects have
    /// Speaker "sfx", their name as Key and the prompt as Value.</summary>
    public class VoiceLine
    {
        public string Key = "", Value = "", Speaker = "host";
        public double Seconds;
        public bool Loop;
    }

    public partial class NewsEngine
    {
        class Speech
        {
            public string Text, Speaker = "host", Mood = "routine", Tag = "", Sfx = "";
            public double PlayAt, Deadline, Gap;
            public byte[] Pcm;
            public bool Ready, Count;
            public int Tries;
        }

        /// <summary>Lines waiting for ElevenLabs: a few at a time, soonest first (the free plan allows few requests at once).</summary>
        readonly List<KeyValuePair<Speech, Action>> fetches = new List<KeyValuePair<Speech, Action>>();
        int fetching;
        const int MaxFetching = 3;

        void PumpFetches()
        {
            while (fetching < MaxFetching && fetches.Count > 0)
            {
                var next = fetches.OrderBy(f => f.Key.PlayAt).First();
                fetches.Remove(next);
                if (!speech.Contains(next.Key)) continue;   // dropped while it waited
                fetching++;
                next.Value();
            }
        }

        static string SpeakerOf(string rule) { return rule.StartsWith("c_") ? "ctl" : rule.StartsWith("s_") ? "stn" : "host"; }
        /// <summary>A station's speaker for a line: "stn:Prop".</summary>
        static string StationSpeaker(string text) { return "stn:" + StationIn(text); }
        bool OnRadio(string speaker) { return speaker != "host" && Settings.ControllerOn; }
        string VoiceOf(string speaker)
        {
            if (!OnRadio(speaker)) return Settings.VoiceId;
            string v;
            if (speaker.StartsWith("stn:") && StationVoices.TryGetValue(speaker.Substring(4), out v)) return v == Settings.ControllerVoiceId ? Voice.DefaultController2 : v;
            return Settings.ControllerVoiceId;
        }
        string StyleOf(string speaker) { return OnRadio(speaker) ? "radio" : Settings.AnnouncerStyle; }

        readonly List<Speech> speech = new List<Speech>();
        /// <summary>Host: plays 16-bit mono PCM at the given rate, at the announcer volume.</summary>
        public Action<byte[], int> PlayVoice;
        /// <summary>Cuts off the clip that's playing (a loss silences the announcer mid-sentence).</summary>
        public Action StopVoice;
        /// <summary>Host: a cache of short clips on disk, by Voice.CacheKey.</summary>
        public Func<string, byte[]> VoiceCacheGet;
        public Action<string, byte[]> VoiceCachePut;
        /// <summary>Voice off for the rest of the session after a bad key or an empty account, with the reason for the settings page.</summary>
        public bool VoiceBroken;
        public string VoiceStatus = "";
        public int VoiceCalls;
        double voiceFreeAt;

        bool VoiceReady { get { return Settings.VoiceOn && !VoiceBroken && PlayVoice != null && Transport != null; } }
        /// <summary>Recorded lines can play even without a key: the ones shipped with the mod, or saved earlier.</summary>
        bool CanPlay { get { return Settings.Announcer > 0 && Settings.VoiceEnabled && PlayVoice != null; } }

        /// <summary>Settings changed (new key, voice on again): give the voice another go.</summary>
        public void ResetVoice() { VoiceBroken = false; VoiceStatus = ""; }

        static readonly Dictionary<string, string[]> AnnLines = new Dictionary<string, string[]>
        {
            { "open", new[] { "Good day, and welcome to live coverage of {craft} from {agency}.", "Hello and welcome. This is {agency}, live with {craft}.", "Welcome to the {agency} broadcast. We're following {craft} today.", "This is {agency} mission coverage. {craft} is on our screens.", "Hello, and welcome to the broadcast.", "Welcome, everyone. We are live." } },
            { "count_start", new[] { "The countdown clock is running. We are at {clock}.", "And the count has begun. {clock} and counting.", "We are now counting. {clock}." } },
            { "count_min", new[] { "{clock} and counting.", "We are at {clock}. All stations report go.", "{clock}. The vehicle is looking good.", "{clock} on the countdown. Everything is proceeding normally." } },
            { "count_30", new[] { "T-minus thirty seconds.", "Thirty seconds. Guidance is internal.", "T-minus thirty seconds and counting." } },
            { "zero", new[] { "Zero.", "And zero." } },
            { "hold", new[] { "Hold, hold, hold. We are holding at {clock}.", "We've called a hold at {clock}. The team is looking at it.", "The count is holding at {clock}. Stand by." } },
            { "resume", new[] { "The count has resumed. {clock} and counting.", "And we're back in the count at {clock}.", "Hold released. Counting from {clock}." } },
            { "recycle", new[] { "We are recycling the count back to {clock}.", "The clock has been reset to {clock}. Holding there for now." } },
            { "scrub", new[] { "We have a scrub. There will be no launch today.", "That's a scrub for today. Thanks for watching, and we'll try again.", "The launch has been called off for today." } },
            { "late", new[] { "The clock has reached zero, and the vehicle is still on the pad. Stand by.", "We're past zero. No liftoff yet. The team is working the problem." } },
            { "liftoff", new[] { "And liftoff! Liftoff of {craft}!", "Liftoff! {craft} has cleared the pad.", "We have liftoff of {craft}!", "Liftoff, and {craft} is on its way!", "And liftoff! We have liftoff!", "Liftoff! The vehicle has cleared the tower." } },
            { "liftoff_crew", new[] { "Liftoff! {craft} lifts off with {crew} aboard!", "And we have liftoff! {crew} are on their way!", "And liftoff! The crew are on their way!" } },
            { "stage", new[] { "Staging confirmed.", "And we have separation.", "Separation, and the next stage is lit.", "That's staging. The vehicle is looking good.", "Clean separation." } },
            { "onboard", new[] { "We're now looking at the view from the vehicle itself.", "Here's the onboard camera.", "You're seeing the view from the vehicle now." } },
            { "orbit", new[] { "And we are in orbit around {body}! {craft} is in orbit.", "Orbit confirmed. {craft} is circling {body}.", "That's orbit! Congratulations to the whole team." } },
            { "soi", new[] { "{craft} has entered {body}'s sphere of influence.", "We are now inside {body}'s sphere of influence." } },
            { "landing", new[] { "Touchdown! {craft} is down on {body}!", "Contact! We have touchdown on {body}.", "The vehicle is down on {body}. What a moment." } },
            { "splash", new[] { "Splashdown! {craft} is in the water.", "And splashdown. Welcome home.", "Splashdown confirmed." } },
            { "dock", new[] { "And they're docked! Two vehicles, now one.", "Docking confirmed. A beautiful piece of flying.", "And we have a hard dock." } },
            { "boom", new[] { "We appear to have lost the vehicle.", "We can now confirm that the vehicle has been lost.", "Obviously a major malfunction. The vehicle is gone." } },
            { "anomaly", new[] { "We may have an anomaly there.", "We saw something come off the vehicle. Stand by.", "Looks like we've lost a piece of the vehicle." } },
            { "crew_lost", new[] { "We have lost the crew. Our thoughts are with their families.", "This is a dark day. The crew have been lost." } },
            { "milestone", new[] { "That's a first for {agency}.", "History is being made right now.", "A milestone moment for {agency}." } },
            { "telemetry", new[] { "Altitude {alt}, speed {speed} metres per second.", "{alt} up, travelling at {speed} metres per second.", "We're at {alt}. Velocity {speed} metres per second.", "Coming up on {alt}. Speed {speed}." } },
            { "coast", new[] { "The vehicle is coasting now. Apoapsis {ap}.", "Quiet on the vehicle as it coasts. Apoapsis is {ap}.", "Coasting, with an apoapsis of {ap}." } },
            // The commentator around the flight controller's calls.
            { "poll", new[] { "We're coming up on the go, no-go poll. Let's listen in.", "The launch director is about to poll the team. Let's listen.", "Time for the go, no-go poll. Here's the loop." } },
            { "poll_go", new[] { "And the team is go for launch.", "All stations are go. We are go for launch.", "That's a go from everyone. We're go for launch." } },
            { "standby", new[] { "We're standing by for more information from the flight team.", "Stand by. We're waiting on word from mission control.", "We don't have any more information at this time. Please stand by." } },
            { "recap_loss", new[] { "The flight team will now begin looking into what happened. We'll share more as we learn it.", "An investigation will follow. We'll bring you more information when we have it." } },
            // FMRS: the broadcast follows a dropped booster down, then returns to the main vessel.
            { "b_cam", new[] { "We're going to the booster now, as it heads back for landing.", "Let's switch to the booster, on its way back down.", "Here's the booster, coming home." } },
            { "b_land", new[] { "Touchdown! The booster has landed.", "And the booster is down, standing on its legs. Beautiful.", "The booster has landed. That stage will fly again." } },
            { "b_splash", new[] { "The booster is down in the water.", "Booster splashdown confirmed." } },
            { "b_lost", new[] { "It looks like we've lost the booster. The main vehicle is unaffected.", "The booster did not survive the landing attempt. The mission continues." } },
            { "b_back", new[] { "Back now to the main vehicle, continuing its flight.", "And we return to {craft}.", "Let's get back to the main vehicle." } },
            { "signoff", new[] { "That concludes our live coverage. Thank you for watching.", "That's all from us. Thanks for joining the broadcast.", "And that wraps up today's coverage. Thanks for watching." } },
            { "lag", new[] { "The signal takes {lag} to reach us from {body}, so we're waiting on confirmation.", "It takes {lag} for the signal to get here from {body}. Stand by for confirmation." } },
            // The loop: Flight (the flight director, also the launch director in the count) and the stations. Calls follow
            // real loop etiquette: who you're calling, who you are, then the message, short; Flight acknowledges.
            { "s_tower", new[] { "Flight, Booster, tower clear.", "Tower clear, Flight." } },
            { "s_super", new[] { "Flight, FIDO, vehicle is supersonic.", "Flight, FIDO, supersonic." } },
            { "s_bucket", new[] { "Flight, Prop, throttle down.", "Flight, Prop, throttling down for max Q." } },
            { "s_maxq", new[] { "Flight, FIDO, max Q.", "Flight, FIDO, through max Q." } },
            { "s_throttle_up", new[] { "Flight, Prop, throttle up.", "Flight, Prop, engines at full throttle." } },
            { "s_meco", new[] { "Flight, Prop, MECO.", "MECO, Flight." } },
            { "s_cutoff", new[] { "Flight, Prop, engine cutoff.", "Flight, Prop, shutdown confirmed." } },
            { "s_stage", new[] { "Flight, Booster, stage sep confirmed.", "Flight, Booster, good separation." } },
            { "s_ignite", new[] { "Flight, Prop, ignition.", "Flight, Prop, good ignition." } },
            { "s_orbit", new[] { "Flight, FIDO, orbit insertion confirmed.", "Flight, FIDO, nominal orbit." } },
            { "s_touchdown", new[] { "Flight, FIDO, contact light.", "Flight, FIDO, touchdown confirmed." } },
            { "s_splash", new[] { "Flight, Recovery, splashdown confirmed.", "Flight, Recovery, we have splashdown." } },
            { "s_dock", new[] { "Flight, Guidance, capture confirmed.", "Flight, Guidance, contact and capture." } },
            { "s_anomaly", new[] { "Flight, Telemetry, we're seeing an anomaly.", "Flight, Telemetry, off-nominal readings." } },
            { "s_los", new[] { "Flight, Telemetry, loss of signal.", "Flight, Telemetry, no data." } },
            { "c_copy", new[] { "Copy.", "Flight copies.", "Roger." } },
            { "c_anomaly_ack", new[] { "Copy. All stations, Flight, stay on your data.", "Flight copies. Stations, eyes on your data." } },
            { "c_los_ack", new[] { "Copy. All stations, Flight, secure your data and stand by.", "Flight copies. All controllers, maintain your data." } },
            { "c_poll_open", new[] { "All stations, Flight, go, no-go poll for launch.", "All stations, Flight. Stand by for the go, no-go poll." } },
            { "c_poll_close", new[] { "Flight is go. We are go for launch.", "Copy all. We are go for launch." } },
        };

        /// <summary>The stations Flight polls before launch: the call, and the station's answer. Crew only flies crewed.</summary>
        static readonly string[][] PollStations =
        {
            new[] { "Booster", "Booster?", "Booster is go." }, new[] { "Prop", "Prop?", "Prop is go." }, new[] { "Guidance", "Guidance?", "Guidance, go." },
            new[] { "FIDO", "FIDO?", "FIDO is go." }, new[] { "Range", "Range?", "Range is go for launch." }, new[] { "Weather", "Weather?", "Weather is go." },
            new[] { "Recovery", "Recovery?", "Recovery is go." }, new[] { "Capcom", "Capcom?", "Capcom is go. The crew is go." },
        };

        /// <summary>Each station's own voice on the loop (ElevenLabs stock voices), so the poll sounds like a room of people.</summary>
        static readonly Dictionary<string, string> StationVoices = new Dictionary<string, string>
        {
            { "Booster", "CwhRBWXzGAHq8TQ4Fs17" }, { "Prop", "cjVigY5qzO86Huf0OWal" }, { "Guidance", "nPczCjzI2devNBz1zQrb" },
            { "FIDO", "CwhRBWXzGAHq8TQ4Fs17" }, { "Range", "cjVigY5qzO86Huf0OWal" }, { "Weather", "nPczCjzI2devNBz1zQrb" },
            { "Recovery", "CwhRBWXzGAHq8TQ4Fs17" }, { "Capcom", "cjVigY5qzO86Huf0OWal" }, { "Telemetry", "nPczCjzI2devNBz1zQrb" },
        };

        /// <summary>The station a loop call comes from: the name after "Flight, " (or before ", Flight").</summary>
        static string StationIn(string text)
        {
            var m = Regex.Match(text ?? "", @"^Flight, (\w+),");
            if (m.Success) return m.Groups[1].Value;
            var r = Regex.Match(text ?? "", @"^(\w+)( is|,| clear| Flight)");
            return r.Success && StationVoices.ContainsKey(r.Groups[1].Value) ? r.Groups[1].Value : "Booster";
        }

        public const string ControllerSample = "All stations, Flight, go, no-go poll for launch.";

        /// <summary>Sound effects, made once with ElevenLabs and shipped: name, prompt, seconds, loop or once.</summary>
        public static readonly string[][] Sounds =
        {
            new[] { "room", "Quiet mission control room ambience: low murmur of people talking, soft keyboard typing, faint radio chatter, air conditioning hum. No music.", "22", "loop" },
            new[] { "cheer", "A large crowd of spectators outdoors at a rocket launch viewing site cheering, whistling and clapping, heard from a short distance.", "9", "once" },
            new[] { "rumble", "The deep crackling roar of a large rocket launch heard from several kilometres away, rising and then slowly fading.", "14", "once" },
            new[] { "applause", "A mission control room erupts in applause and cheering, people clapping and shouting with joy, indoors.", "7", "once" },
            new[] { "gasp", "A crowd of spectators gasps in shock all at once, a few cries, then falls silent.", "4", "once" },
            new[] { "murmur", "A worried crowd murmuring quietly after something went wrong, outdoors.", "6", "once" },
        };

        static string SfxKey(string name)
        {
            var fx = Sounds.FirstOrDefault(x => x[0] == name);
            return fx == null ? "" : Voice.CacheKey("sfx:" + name, Voice.SfxModel, fx[1] + "|" + fx[2] + "|" + fx[3]);
        }

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
            if (CanPlay && VoiceCacheGet != null && opts.Length > 1)
            {
                string mood = RuleMood(rule), who = SpeakerOf(rule);
                var cached = Enumerable.Range(0, opts.Length).Where(i => i != (annLast.ContainsKey(rule + "#prev") ? annLast[rule + "#prev"] : -1))
                    .Where(i => { string t = fill(opts[i]); return t != "" && IsRecorded(t, mood, who == "stn" ? StationSpeaker(t) : who); }).ToList();
                if (cached.Count > 0) pick = cached[rng.Next(cached.Count)];
            }
            annLast[rule + "#prev"] = pick;
            return fill(opts[pick]);
        }

        static string RuleMood(string rule)
        {
            return rule == "boom" || rule == "crew_lost" || rule == "recap_loss" || rule == "s_los" || rule == "c_los_ack" ? "loss"
                : rule == "hold" || rule == "scrub" || rule == "late" || rule == "anomaly" || rule == "recycle" || rule == "standby" || rule == "lag" || rule == "s_anomaly" || rule == "c_anomaly_ack" ? "tense"
                : rule.StartsWith("liftoff") || rule == "orbit" || rule == "landing" || rule == "splash" || rule == "dock" || rule == "milestone" || rule == "soi" || rule == "poll_go" || rule == "c_poll_close" ? "big"
                : rule == "zero" || rule == "count_30" ? "count" : "routine";
        }

        string VoiceKeyFor(string text, string mood, string speaker = "host")
        {
            string style = StyleOf(speaker);
            return Voice.CacheKey(VoiceOf(speaker), Settings.VoiceModel + "|" + style, Voice.Spoken(text, mood, style, Settings.VoiceModel));
        }

        bool IsRecorded(string text, string mood, string speaker = "host")
        {
            if (VoiceCacheGet == null) return false;
            try { var b = VoiceCacheGet(VoiceKeyFor(text, mood, speaker)); return b != null && b.Length > 0; } catch (Exception) { return false; }
        }

        // ------------------------------------------------------------------ pre-recording

        static readonly string[] CommonBodies = { "Kerbin", "Mun", "Minmus", "Duna", "Eve" };

        /// <summary>Every announcer line that doesn't name the craft or read out live numbers, in the current voice's words:
        /// the countdown, the stock calls, orbit and landings at the usual places. These can be recorded once and reused.</summary>
        public List<VoiceLine> CommonLines()
        {
            var l = new List<VoiceLine>();
            var keys = new HashSet<string>();
            string who = "host";
            Action<string, string> add = (t, mood) =>
            {
                if (t == "" || t.Contains("{")) return;
                string sp = who == "stn" ? StationSpeaker(t) : who;
                if (keys.Add(VoiceKeyFor(t, mood, sp))) l.Add(new VoiceLine { Key = t, Value = mood, Speaker = sp });
            };
            for (int n = 10; n >= 1; n--) add(CountWords[n] + ".", "count");
            foreach (var kv in AnnLines)
            {
                if (kv.Key == "telemetry" || kv.Key == "coast" || kv.Key == "lag") continue;
                string mood = RuleMood(kv.Key);
                who = SpeakerOf(kv.Key);
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
            who = "host";
            add(SampleLine, "big");
            who = "ctl";
            add(ControllerSample, "routine");
            foreach (var st in PollStations) { who = "ctl"; add(st[1], "routine"); who = "stn:" + st[0]; add(st[2], "routine"); }
            foreach (var fx in Sounds)
                if (keys.Add(SfxKey(fx[0]))) l.Add(new VoiceLine { Key = fx[0], Value = fx[1], Speaker = "sfx", Seconds = double.Parse(fx[2], CultureInfo.InvariantCulture), Loop = fx[3] == "loop" });
            return l;
        }

        string LineKey(VoiceLine x) { return x.Speaker == "sfx" ? SfxKey(x.Key) : VoiceKeyFor(x.Key, x.Value, x.Speaker); }
        bool LineRecorded(VoiceLine x)
        {
            if (VoiceCacheGet == null) return false;
            try { var b = VoiceCacheGet(LineKey(x)); return b != null && b.Length > 0; } catch (Exception) { return false; }
        }

        /// <summary>The bank folder a line ships in: the commentator's, the controller's, or the sound effects.</summary>
        string BankOf(string speaker)
        {
            if (speaker == "sfx") return "sfx";
            return (VoiceLabel(VoiceOf(speaker)) + "-" + Settings.VoiceModel + "-" + StyleOf(speaker)).ToLowerInvariant().Replace(" ", "_").Replace("/", "_").Replace("\\", "_");
        }

        /// <summary>Host: copies recorded clips into a voice bank that ships with the mod (folder, then key and words for each).</summary>
        public Action<string, List<KeyValuePair<string, string>>> ExportVoiceBank;

        /// <summary>The bank's folder name for the current voice, model and style, e.g. chris-eleven_v3-calm.</summary>
        public string BankName { get { return BankOf("host"); } }
        public string ControllerBankName { get { return BankOf("ctl"); } }
        public List<string> StationBankNames { get { return StationVoices.Keys.Select(k => BankOf("stn:" + k)).Distinct().ToList(); } }
        public bool SoundRecorded(string name) { return SoundClip(name) != null; }

        /// <summary>The recorded common lines that fit every player (not the ones with this save's agency name), for shipping:
        /// one folder for the commentator, one for the controller, one for the sound effects.</summary>
        public int ExportBank()
        {
            if (ExportVoiceBank == null) return 0;
            int n = 0;
            foreach (var g in CommonLines().Where(x => !x.Key.Contains(AgencyName) && LineRecorded(x)).GroupBy(x => BankOf(x.Speaker)))
            {
                var lines = g.Select(x => new KeyValuePair<string, string>(LineKey(x), x.Speaker == "sfx" ? x.Key + ": " + x.Value : x.Key)).ToList();
                ExportVoiceBank(g.Key, lines);
                n += lines.Count;
            }
            return n;
        }

        public List<VoiceLine> LinesToRecord() { return CommonLines().Where(x => !LineRecorded(x)).ToList(); }

        public bool Prerecording { get; private set; }
        public string PrerecordStatus = "";

        /// <summary>Records the common lines one at a time (ElevenLabs limits parallel requests), saving each as it comes.</summary>
        public bool Prerecord(Action changed)
        {
            if (Prerecording || Settings.VoiceKey == "" || Transport == null || VoiceCachePut == null) return false;
            var todo = LinesToRecord();
            if (todo.Count == 0) { PrerecordStatus = "All common lines are already recorded."; return false; }
            Prerecording = true;
            int done = 0, total = todo.Count, skipped = 0;
            string voice = Settings.VoiceId, model = Settings.VoiceModel, style = Settings.AnnouncerStyle, ctl = Settings.ControllerVoiceId;
            bool ctlOn = Settings.ControllerOn;
            Action next = null;
            next = () =>
            {
                if (done >= todo.Count || Settings.VoiceId != voice || Settings.VoiceModel != model || Settings.AnnouncerStyle != style || Settings.ControllerVoiceId != ctl || Settings.ControllerOn != ctlOn)
                {
                    Prerecording = false;
                    PrerecordStatus = done >= todo.Count ? "Recorded " + total + " lines." + (skipped > 0 ? " " + skipped + " couldn't be made: " + VoiceStatus : " They play instantly and cost nothing from now on.") : "Stopped: the voice settings changed. Press the button again for the new voice.";
                    changed();
                    return;
                }
                var item = todo[done];
                string key = LineKey(item);
                HttpRequestSpec req;
                if (item.Speaker == "sfx") req = Voice.BuildSfx(Settings.VoiceKey, item.Value, item.Seconds, item.Loop);
                else
                {
                    string st = StyleOf(item.Speaker);
                    req = Voice.Build(Settings.VoiceKey, VoiceOf(item.Speaker), model, Voice.Spoken(item.Key, item.Value, st, model), st);
                }
                VoiceCalls++;
                Transport.Send(req, res =>
                {
                    byte[] pcm;
                    string err;
                    if (Voice.TryExtract(res, out pcm, out err))
                    {
                        if (item.Loop) pcm = Voice.LoopSmooth(pcm);
                        try { VoiceCachePut(key, pcm); } catch (Exception) { }
                        done++;
                        PrerecordStatus = "Recording... " + done + " of " + total;
                        changed();
                        next();
                    }
                    else if (!Voice.IsFatal(res) && skipped < 15)
                    {
                        // One bad line (a voice the account can't use, a sound the API refused) shouldn't stop the rest.
                        skipped++;
                        VoiceStatus = err;
                        todo.RemoveAt(done);
                        total--;
                        PrerecordStatus = "Recording... " + done + " of " + total + " (skipped " + skipped + ": " + err + ")";
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
            if (m.StartsWith("booster")) { AnnounceBooster(st, m, realTime); return; }
            if (st.OnBooster) return;   // the launch commentary waits for the main vessel
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
            else if (m.Contains("part of the vessel exploded")) rule = st.Lifted ? "anomaly" : null;
            else if (m.StartsWith("milestone")) rule = "milestone";
            else if (m.StartsWith("stage ") && st.Lifted) rule = "stage";
            else if (mode >= 2 && m.Contains("onboard hull cameras")) rule = realTime - st.LastAnnReal > 10 ? "onboard" : null;
            if (rule == null) return;
            if (x.ContainsKey("body") && x["body"] == "") x["body"] = st.Last != null ? st.Last.Body : "Kerbin";
            CallMoment(st, rule, x, realTime);
        }

        /// <summary>FMRS: the commentator on the cut to a dropped booster, its landing or loss, and the cut back.</summary>
        void AnnounceBooster(StreamSession st, string m, double realTime)
        {
            string rule = m.StartsWith("booster cam") ? "b_cam" : m.StartsWith("booster touchdown") ? "b_land" : m.StartsWith("booster splashdown") ? "b_splash"
                : m.StartsWith("booster lost") ? "b_lost" : m.StartsWith("booster coverage over") ? "b_back" : null;
            if (rule == null) return;
            string text = AnnLine(rule, st);
            if (text == "") return;
            if (rule == "b_land") { SurgeTo(st, 1.6, 300); Say(st, text, realTime, realTime + Rand(1.0, 1.8), 14, false, "big", "host", 0, "", "cheer"); }
            else if (rule == "b_splash") { SurgeTo(st, 1.3, 300); Say(st, text, realTime, realTime + Rand(1.0, 1.8), 14, false, "big", "host", 0, "", "applause"); }
            else if (rule == "b_lost") { SurgeTo(st, 1.5, 300); PlaySfx(st, "gasp", realTime); Say(st, text, realTime, realTime + Rand(1.4, 2.2), 16, false, "tense"); }
            else Say(st, text, realTime, realTime + 0.8, 15, false, "routine");
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
        Speech Say(StreamSession st, string text, double realTime, double playAt, double maxWait, bool count = false, string mood = "routine",
            string speaker = "host", double gap = 0, string tag = "", string sfx = "")
        {
            if (count) mood = "count";
            text = (text ?? "").Trim();
            if (Settings.Announcer <= 0 || text == "" || st == null) return null;
            if (speaker == "stn") speaker = StationSpeaker(text);
            double at = playAt > 0 ? playAt : realTime;
            var sp = new Speech { Text = text, PlayAt = at, Deadline = at + maxWait, Count = count, Speaker = speaker, Mood = mood, Gap = gap, Tag = tag, Sfx = sfx };
            speech.Add(sp);
            st.LastAnnReal = Math.Max(st.LastAnnReal, realTime);
            if (!CanPlay) { sp.Ready = true; return sp; }
            string rec = speaker;
            string style = StyleOf(rec), voice = VoiceOf(rec);
            string spoken = Voice.Spoken(text, mood, style, Settings.VoiceModel);
            string key = Voice.CacheKey(voice, Settings.VoiceModel + "|" + style, spoken);
            byte[] cached = null;
            if (VoiceCacheGet != null) { try { cached = VoiceCacheGet(key); } catch (Exception) { cached = null; } }
            if (cached != null && cached.Length > 0) { sp.Pcm = cached; sp.Ready = true; return sp; }
            if (!VoiceReady) { sp.Ready = true; return sp; }   // no key: this line is a caption
            bool keep = count || st.VesselName == "" || !text.Contains(st.VesselName);   // anything without the craft's name can come up again
            string model = Settings.VoiceModel, vkey = Settings.VoiceKey;
            Action send = null;
            send = () =>
            {
                VoiceCalls++;
                sp.Tries++;
                Transport.Send(Voice.Build(vkey, voice, model, spoken, style), res =>
                {
                    fetching = Math.Max(0, fetching - 1);
                    byte[] pcm;
                    string err;
                    if (Voice.TryExtract(res, out pcm, out err))
                    {
                        sp.Pcm = pcm;
                        VoiceStatus = "";
                        if (keep && VoiceCachePut != null) { try { VoiceCachePut(key, pcm); } catch (Exception) { } }
                    }
                    else if (res != null && res.Status == 429 && sp.Tries < 3) { fetches.Add(new KeyValuePair<Speech, Action>(sp, send)); PumpFetches(); return; }
                    else
                    {
                        VoiceStatus = err;
                        if (Voice.IsFatal(res)) VoiceBroken = true;
                    }
                    sp.Ready = true;
                    PumpFetches();
                });
            };
            fetches.Add(new KeyValuePair<Speech, Action>(sp, send));
            PumpFetches();
            return sp;
        }

        /// <summary>The final ten seconds, one number a second, fetched a few seconds early so each lands on time.</summary>
        void TickFinalCount(StreamSession st, double realTime)
        {
            if (Settings.Announcer <= 0 || st.Countdown != "counting" || st.FinalCountQueued) return;
            double left = CountdownLeft(realTime);
            if (double.IsNaN(left) || left > 14 || left < 1) return;
            st.FinalCountQueued = true;
            st.FinalCountSaid = true;
            speech.RemoveAll(s => !s.Count);   // nothing else talks over the final count
            for (int n = 10; n >= 1; n--)
                if (n <= left + 0.2) Say(st, CountWords[n] + ".", realTime, realTime + (left - n), 0.45, true);
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
            PumpFetches();
            if (speech.Count == 0) { if (st.Caption != "" && realTime > st.CaptionUntil) { st.Caption = ""; Changed(); } return; }
            var next = speech.Where(s => s.Ready && s.PlayAt <= realTime).OrderBy(s => s.PlayAt).FirstOrDefault();
            // Countdown numbers go on the second, cutting off whatever is still playing; anything else waits its turn and
            // never starts if it would run into a number.
            bool blocked = next == null
                || !next.Count && realTime < voiceFreeAt + next.Gap
                || !next.Count && speech.Any(s => s.Count && s.PlayAt < realTime + Math.Max(1.5, Voice.Seconds(next.Pcm) + 0.3));
            if (blocked) { if (st.Caption != "" && realTime > st.CaptionUntil) { st.Caption = ""; Changed(); } return; }
            speech.Remove(next);
            if (next.Pcm != null) next.Pcm = Voice.Tighten(Voice.Trim(next.Pcm, next.Count));
            if (next.Pcm != null && OnRadio(next.Speaker)) next.Pcm = Voice.Radio(Voice.Tighten(next.Pcm, 7, 4), next.Speaker == "ctl", next.Speaker.StartsWith("stn"));
            if (next.Sfx != "") PlaySfx(st, next.Sfx, realTime);
            double dur = Voice.Seconds(next.Pcm);
            int words = next.Text.Split(' ').Length;
            var line = new AnnouncerLine { At = realTime - st.StartReal, Text = next.Text, Seconds = dur, Speaker = next.Speaker };
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
            st.Caption = (next.Speaker == "host" || !Settings.ControllerOn ? "" : next.Speaker == "ctl" ? "FLIGHT: " : next.Speaker.StartsWith("stn:") ? next.Speaker.Substring(4).ToUpperInvariant() + ": " : "LOOP: ") + next.Text;
            st.CaptionUntil = realTime + Math.Max(next.Count ? 1 : 4, Math.Max(dur + 1.5, words * 0.4));
            st.LastAnnReal = realTime;
            Record(st, realTime, "ann", "x", st.Caption, "v", line.Audio, "d", Math.Round(dur, 2));
            Changed();
        }

        public const string SampleLine = "T-minus ten seconds, and counting... Three, two, one... Liftoff! We have liftoff!";

        /// <summary>
        /// A sample of the chosen voice, model and style from the settings page (also when a voice is picked). The same sample
        /// is saved, so listening to a voice again costs nothing.
        /// </summary>
        public bool VoiceTest(Action<string> done, string speaker = "host")
        {
            if (PlayVoice == null) { done("The voice can only play inside the game."); return false; }
            string style = StyleOf(speaker), voice = VoiceOf(speaker), text = speaker == "host" ? SampleLine : ControllerSample, mood = speaker == "host" ? "big" : "routine";
            bool radio = OnRadio(speaker);
            string spoken = Voice.Spoken(text, mood, style, Settings.VoiceModel);
            string key = Voice.CacheKey(voice, Settings.VoiceModel + "|" + style, spoken);
            string who = VoiceLabel(voice);
            Func<byte[], byte[]> fx = p => { p = Voice.Tighten(Voice.Trim(p, false)); return radio ? Voice.Radio(p, true, false) : p; };
            byte[] cached = null;
            if (VoiceCacheGet != null) { try { cached = VoiceCacheGet(key); } catch (Exception) { cached = null; } }
            if (cached != null && cached.Length > 0)
            {
                try { PlayVoice(fx(cached), Voice.Rate); } catch (Exception) { }
                done("Playing " + who + ".");
                return true;
            }
            if (Settings.VoiceKey == "" || Transport == null) { done("No recording of " + who + " comes with the mod. Save an ElevenLabs key to hear any voice."); return false; }
            VoiceCalls++;
            Transport.Send(Voice.Build(Settings.VoiceKey, voice, Settings.VoiceModel, spoken, style), res =>
            {
                byte[] pcm;
                string err;
                if (Voice.TryExtract(res, out pcm, out err))
                {
                    VoiceBroken = false; VoiceStatus = "";
                    if (VoiceCachePut != null) { try { VoiceCachePut(key, pcm); } catch (Exception) { } }
                    try { PlayVoice(fx(pcm), Voice.Rate); } catch (Exception) { }
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
