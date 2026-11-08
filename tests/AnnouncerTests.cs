using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KerbalNews;
using KerbalNews.Web;

public static class AnnouncerTests
{
    const double Day = 21600;
    static void Assert(bool c, string m) { if (!c) throw new Exception("Assertion failed: " + m); }

    static HttpResult Pcm(double seconds) { return new HttpResult { Status = 200, Bytes = new byte[(int)(Voice.Rate * seconds) * 2] }; }

    static HttpResult ChatText(string inner)
    {
        var resp = new Dictionary<string, object> { { "content", new List<object> { new Dictionary<string, object> { { "type", "text" }, { "text", inner } } } } };
        return new HttpResult { Status = 200, Body = Json.Write(resp) };
    }

    class Rig
    {
        public NewsEngine Eng;
        public CoreTests.FakeTransport T;
        public List<string> Played = new List<string>();
        public List<string> Spoken = new List<string>();   // texts sent to ElevenLabs
        public Dictionary<string, byte[]> Cache = new Dictionary<string, byte[]>();
        public Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>();
        public Dictionary<string, string> Index = new Dictionary<string, string>();
        public List<string> ReplayVoice = new List<string>();
    }

    static Rig Make(GlobalSettings g, string chatJson = null)
    {
        var rig = new Rig();
        rig.T = new CoreTests.FakeTransport
        {
            Responder = r =>
            {
                if (r.Url.Contains("elevenlabs"))
                {
                    var text = Json.Str(Json.Parse(r.Body), "text", "");
                    rig.Spoken.Add(text);
                    string words = System.Text.RegularExpressions.Regex.Replace(text, @"^\[[^\]]*\] ", "");
                    return Pcm(words.Length < 8 ? 0.4 : 2);
                }
                return ChatText(chatJson ?? "{\"viewers\":300,\"messages\":[{\"author\":\"a\",\"text\":\"go go go\"}]}");
            }
        };
        rig.Eng = new NewsEngine(new NewsState(), g, rig.T, new SimpleClock(Day));
        rig.Eng.PlayVoice = (pcm, rate) => rig.Played.Add(pcm.Length + "@" + rate);
        rig.Eng.VoiceCacheGet = k => rig.Cache.ContainsKey(k) ? rig.Cache[k] : null;
        rig.Eng.VoiceCachePut = (k, b) => rig.Cache[k] = b;
        rig.Eng.SaveReplayFile = (f, n, b) => rig.Files[f + "/" + n] = b;
        rig.Eng.SaveReplayIndex = (f, j) => rig.Index[f] = j;
        rig.Eng.LoadReplayIndex = f => rig.Index.ContainsKey(f) ? rig.Index[f] : null;
        rig.Eng.PlayReplayVoice = (f, n) => rig.ReplayVoice.Add(n);
        rig.Eng.Tick(0, 0, true);
        return rig;
    }

    static GlobalSettings VoiceSettings(bool aiOn = false)
    {
        var g = aiOn ? CoreTests.OnlineSettings() : new GlobalSettings { OfflineMode = true };
        g.Keys[GlobalSettings.VoiceKeyId] = "el-test";
        g.VoiceModel = "eleven_flash_v2_5";   // plain text in these tests; v3 directions have their own test
        return g;
    }

    static void Run(NewsEngine eng, double from, double to, double step = 0.1)
    {
        for (double t = from; t <= to + 1e-9; t += step) { eng.SetRealTime(t); eng.TickStream(t, true); }
    }

    public static void TestElevenLabsRequestAndErrors()
    {
        var req = Voice.Build("k1", "abc", "eleven_flash_v2_5", "Liftoff!");
        Assert(req.Url == "https://api.elevenlabs.io/v1/text-to-speech/abc?output_format=pcm_22050", req.Url);
        Assert(req.Headers["xi-api-key"] == "k1" && req.Method == "POST", "key header");
        Assert(Json.Str(Json.Parse(req.Body), "text", "") == "Liftoff!" && Json.Str(Json.Parse(req.Body), "model_id", "") == "eleven_flash_v2_5", "body");
        byte[] pcm; string err;
        Assert(Voice.TryExtract(Pcm(1), out pcm, out err) && pcm.Length == Voice.Rate * 2, "raw pcm comes back");
        var bad = new HttpResult { Status = 401, Body = "{\"detail\":{\"status\":\"invalid_api_key\",\"message\":\"Invalid API key\"}}" };
        Assert(!Voice.TryExtract(bad, out pcm, out err) && err.Contains("refused the key") && err.Contains("Invalid API key") && Voice.IsFatal(bad), err);
        var quota = new HttpResult { Status = 401, Body = "{\"detail\":{\"status\":\"quota_exceeded\",\"message\":\"You have 3 credits remaining\"}}" };
        Assert(Voice.IsFatal(quota), "out of credits stops the voice");
        var flaky = new HttpResult { Status = 500, Body = "oops" };
        Assert(!Voice.TryExtract(flaky, out pcm, out err) && !Voice.IsFatal(flaky), "a server hiccup isn't fatal");
        var wav = Voice.Wav(new byte[] { 1, 2, 3, 4 });
        Assert(wav.Length == 48 && wav[0] == 'R' && Voice.FromWav(wav).Length == 4 && Voice.FromWav(wav)[3] == 4, "wav round trip");
        Assert(NewsEngine.SpokenClock(300) == "T-minus 5 minutes" && NewsEngine.SpokenClock(90) == "T-minus 1 minute 30 seconds" && NewsEngine.SpokenClock(45) == "T-minus 45 seconds", NewsEngine.SpokenClock(90));
    }

    public static void TestAnnouncerSpeaksBigMomentsAndSkipsSmallOnes()
    {
        var rig = Make(VoiceSettings());
        var eng = rig.Eng;
        var st = eng.StartStream("v1", "Kerbal X", 0, 0);
        Run(eng, 0, 4);
        Assert(rig.Played.Count == 1 && rig.Spoken[0].Contains("Kerbal X"), "a welcome line: " + string.Join(" | ", rig.Spoken.ToArray()));
        eng.StreamSample(new TelemetrySample { UT = 1, Altitude = 80, Situation = "FLYING", Body = "Kerbin" }, 5);
        eng.StreamMoment("liftoff", 5);
        Run(eng, 5, 6);
        Assert(rig.Played.Count == 2 && st.Caption.ToLowerInvariant().Contains("liftoff"), "liftoff is called at once: " + st.Caption);
        eng.StreamMoment("stage 2 activated", 30);
        Run(eng, 30, 32);
        Assert(rig.Played.Count == 3 && st.Said.Last().Speaker == "ctl" && st.Said.Last().At > 31, "big moments: the controller confirms the staging a beat later: " + st.Said.Last().Text);
        eng.StreamMoment("the vessel reached orbit around Kerbin", 60);
        Run(eng, 60, 66);
        Assert(rig.Played.Count == 5 && st.Said[3].Speaker == "ctl" && st.Said[4].Text.ToLowerInvariant().Contains("orbit"), "orbit is confirmed, then celebrated: " + string.Join(" | ", st.Said.Select(l => l.Speaker + ": " + l.Text).ToArray()));
        Assert(st.Said.Count == 5 && eng.VoiceCalls == 5, "five lines, five voice calls");

        eng.Settings.Announcer = 2;
        eng.StreamMoment("stage 1 activated", 90);
        Run(eng, 90, 100);
        Assert(rig.Spoken.Count == 7, "full commentary adds the commentator on staging: " + rig.Spoken.Count);
        eng.Settings.Announcer = 0;
        eng.StreamMoment("touchdown on Mun", 120);
        Run(eng, 120, 122);
        Assert(rig.Spoken.Count == 7, "announcer off: silent");
    }

    public static void TestFinalCountdownLandsOnEachSecondAndIsPaidForOnce()
    {
        var rig = Make(VoiceSettings());
        var eng = rig.Eng;
        var st = eng.StartStream("v1", "Kerbal X", 0, 0);
        Run(eng, 0, 3);
        int before = rig.Spoken.Count;
        eng.CountdownStart(60, 3);
        Run(eng, 3, 64);
        var numbers = rig.Spoken.Skip(before).Where(x => x.Length < 10).ToList();
        Assert(numbers.Count >= 11 && numbers.Contains("Ten.") && numbers.Contains("One.") && (numbers.Contains("Zero.") || numbers.Contains("And zero.")), "ten to one, then zero: " + string.Join(" ", numbers.ToArray()));
        var said = st.Said.Where(l => l.Text == "Five.").ToList();
        Assert(said.Count == 1 && Math.Abs(said[0].At - (3 + 60 - 5)) < 0.25, "five is said at T-5: " + (said.Count > 0 ? said[0].At.ToString() : "none"));
        int calls = eng.VoiceCalls;
        eng.EndStream(10);

        // Next launch: the numbers come from the cache.
        st = eng.StartStream("v1", "Kerbal X", 100, 100);
        eng.CountdownStart(30, 101);
        Run(eng, 101, 133);
        Assert(st.Said.Count(l => l.Text == "One.") == 1, "one is said again");
        Assert(eng.VoiceCalls - calls <= 3, "numbers aren't paid for twice: " + (eng.VoiceCalls - calls) + " new calls");

        // A hold inside the last ten seconds stops the count.
        eng.EndStream(140);
        st = eng.StartStream("v1", "Kerbal X", 200, 200);
        eng.CountdownStart(20, 200);
        Run(eng, 200, 214);   // T-6
        eng.CountdownHold(214);
        Run(eng, 214, 225);
        Assert(!st.Said.Any(l => l.Text == "Three." || l.Text == "Two."), "no numbers after the hold");
        Assert(st.Said.Any(l => l.Text.ToLowerInvariant().Contains("hold")), "the hold is called");
    }

    static HttpResult PaddedPcm(double silence, double sound)
    {
        int a = (int)(Voice.Rate * silence), b = (int)(Voice.Rate * sound);
        var bytes = new byte[(a + b) * 2];
        for (int i = a; i < a + b; i++) { short v = (short)((i % 20) < 10 ? 8000 : -8000); bytes[2 * i] = (byte)v; bytes[2 * i + 1] = (byte)(v >> 8); }
        return new HttpResult { Status = 200, Bytes = bytes };
    }

    public static void TestCountNumbersStayOnTheSecondWithSlowClips()
    {
        // v3 clips start with silence and run long; other lines run long too. The numbers must still land on each second.
        var rig = Make(VoiceSettings());
        var lens = new List<int>();
        rig.Eng.PlayVoice = (pcm, rate) => { rig.Played.Add(pcm.Length + "@" + rate); lens.Add(pcm.Length); };
        rig.T.Responder = r =>
        {
            var text = Json.Str(Json.Parse(r.Body), "text", "");
            if (!r.Url.Contains("elevenlabs")) return ChatText("{\"viewers\":300,\"messages\":[]}");
            rig.Spoken.Add(text);
            return text.Length < 10 ? PaddedPcm(0.5, 1.8) : PaddedPcm(0.4, 4.5);
        };
        var eng = rig.Eng;
        var st = eng.StartStream("v1", "Kerbal X", 0, 0);
        Run(eng, 0, 2);
        eng.CountdownStart(25, 2);   // T-0 at 27
        Run(eng, 2, 29, 0.05);
        string[] words = { "Ten.", "Nine.", "Eight.", "Seven.", "Six.", "Five.", "Four.", "Three.", "Two.", "One." };
        for (int i = 0; i < 10; i++)
        {
            var l = st.Said.Where(x => x.Text == words[i]).ToList();
            double want = 27 - (10 - i);
            Assert(l.Count == 1 && Math.Abs(l[0].At - want) < 0.25, words[i] + " at " + want + ": " + string.Join(" | ", st.Said.Select(x => x.At.ToString("0.00") + " " + x.Text).ToArray()));
        }
        Assert(!st.Said.Any(x => x.Text.Length >= 10 && x.At > 16.5 && x.At < 26.9), "nothing else is said over the final count");
        Assert(lens.Count > 0 && lens.Min() < (int)(Voice.Rate * 2.3) * 2, "the silence at the start of a clip is cut");
    }

    public static void TestLossIsCalledAfterAStunnedPause()
    {
        var rig = Make(VoiceSettings());
        int stops = 0;
        rig.Eng.StopVoice = () => stops++;
        var eng = rig.Eng;
        var st = eng.StartStream("v1", "Kerbal X", 0, 0);
        Run(eng, 0, 4);
        eng.StreamSample(new TelemetrySample { UT = 1, Altitude = 80, Situation = "FLYING", Body = "Kerbin" }, 5);
        eng.StreamMoment("liftoff", 5);
        Run(eng, 5, 5.5);   // the liftoff call is still playing
        eng.StreamMoment("Kerbal X was destroyed", 5.5);
        Run(eng, 5.5, 25, 0.05);
        var after = st.Said.Where(l => l.At > 5.5).ToList();
        string all = string.Join(" | ", st.Said.Select(l => l.At.ToString("0.00") + " " + l.Speaker + ": " + l.Text).ToArray());
        Assert(after.Count >= 3 && after[0].Speaker == "ctl" && after[0].At >= 5.5 + 1.5 && after[0].At <= 5.5 + 2.6, "a stunned pause, then the controller: " + all);
        Assert(after[1].Speaker == "host" && after[1].At >= after[0].At + after[0].Seconds + 1.3, "stand by, after a gap: " + all);
        Assert(after[2].At >= after[1].At + after[1].Seconds + 2.5 && (after[2].Text.Contains("vehicle") || after[2].Text.Contains("malfunction")), "and only then the loss: " + all);
        Assert(stops == 1, "the liftoff call is cut off: " + stops);
        Assert(after.Count == 5 && after[3].Text.StartsWith("To recap") && after[3].Text.Contains("moments after liftoff"), "a recap once it's quiet: " + all);
    }

    static void Fly(NewsEngine eng, double t, double alt, double mach, double q, double thr, double thrust)
    {
        eng.SetRealTime(t);
        eng.StreamFlight(new LiveNumbers { Altitude = alt, Mach = mach, Q = q, Throttle = thr, Thrust = thrust, AtmosphereTop = 70000, Situation = "FLYING" }, t);
        eng.TickStream(t, true);
    }

    public static void TestControllerCallsTheClimb()
    {
        var rig = Make(VoiceSettings());
        var eng = rig.Eng;
        var st = eng.StartStream("v1", "Kerbal X", 0, 0);
        Run(eng, 0, 4);
        eng.StreamSample(new TelemetrySample { UT = 1, Altitude = 80, Situation = "FLYING", Body = "Kerbin" }, 5);
        eng.StreamMoment("liftoff", 5);
        double t = 5;
        Action<double, double, double, double, double, double> fly = (until, alt, mach, q, thr, thrust) => { for (; t < until; t += 0.2) Fly(eng, t, alt, mach, q, thr, thrust); };
        fly(9, 100, 0.2, 1, 1, 200);          // clears the tower
        fly(20, 5000, 0.8, 12, 1, 200);
        fly(30, 9000, 1.05, 18, 0.7, 150);    // supersonic, throttled down in the bucket
        fly(40, 14000, 1.6, 12, 0.7, 150);    // past the peak
        fly(50, 20000, 2.5, 6, 1, 200);       // throttle back up
        fly(56, 35000, 4, 1, 1, 0);           // burnout without staging: MECO
        fly(70, 40000, 4, 0.5, 1, 60);        // the next engine lights
        Run(eng, 70, 80);
        var calls = st.Said.Where(l => l.Speaker == "ctl").Select(l => l.Text).ToList();
        string all = string.Join(" | ", st.Said.Select(l => l.At.ToString("0.0") + " " + l.Speaker + ": " + l.Text).ToArray());
        Func<string, bool> has = w => calls.Count(c => c.ToLowerInvariant().Contains(w)) == 1;
        Assert(has("tower") && has("supersonic") && has("throttling down") && has("max q") && (has("back up") || has("full throttle")) && (has("meco") || has("main engine")) && has("ignition"), "every callout once: " + all);
        Assert(calls.Count == 7, "and nothing twice: " + all);
        var ctlClip = rig.Played.Count;
        Assert(ctlClip >= 9, "they're heard: " + ctlClip);
    }

    public static void TestGoNoGoPoll()
    {
        var rig = Make(VoiceSettings());
        var eng = rig.Eng;
        var st = eng.StartStream("v1", "Kerbal X", 0, 0);
        Run(eng, 0, 2);
        eng.CountdownStart(330, 2);   // T-5:00 at 32
        Run(eng, 2, 31);
        Assert(!st.Said.Any(l => l.Speaker == "stn"), "no poll before T-5");
        Run(eng, 31, 100);
        string all = string.Join(" | ", st.Said.Select(l => l.At.ToString("0.0") + " " + l.Speaker + ": " + l.Text).ToArray());
        var answers = st.Said.Where(l => l.Speaker == "stn").ToList();
        Assert(answers.Count == 5 && answers.All(a => a.Text.StartsWith("Go") || a.Text.StartsWith("We're go")), "five stations answer go (uncrewed): " + all);
        int i = st.Said.FindIndex(l => l.Text == "Booster.");
        Assert(i > 0 && st.Said[i + 1].Speaker == "stn" && st.Said[i].Speaker == "ctl", "each station is called, then answers: " + all);
        Assert(st.Said.Last(l => l.Speaker == "host").Text.Contains("go for launch") && st.Said.Last().At < 100, "and the commentator says the team is go: " + all);

        // A hold during the poll stops it; resuming runs it again.
        eng.EndStream(200);
        st = eng.StartStream("v1", "Kerbal X", 300, 300);
        eng.CountdownStart(290, 300);
        Run(eng, 300, 309);
        eng.CountdownHold(309);
        Run(eng, 309, 340);
        Assert(!st.Said.Any(l => l.Text.Contains("go for launch")), "the hold stops the poll");
        eng.CountdownResume(340);
        Run(eng, 340, 420);
        Assert(st.Said.Count(l => l.Speaker == "stn") >= 5 && st.Said.Any(l => l.Text.Contains("go for launch")), "and it runs again on resume");
    }

    public static void TestRadioSoundsLikeARadio()
    {
        var clean = PaddedPcm(0, 1).Bytes;
        var radio = Voice.Radio(clean, true, false);
        double secs = Voice.Seconds(radio) - Voice.Seconds(clean);
        Assert(secs > 0.6 && secs < 0.9, "Quindar tones and a squelch around the call: " + secs);
        int n = (int)(Voice.Rate * 0.2), crossings = 0;
        for (int i = 1; i < n; i++) { short a = (short)(radio[2 * i - 2] | radio[2 * i - 1] << 8), b = (short)(radio[2 * i] | radio[2 * i + 1] << 8); if (a < 0 != b < 0) crossings++; }
        double hz = crossings / 2.0 / 0.2;
        Assert(Math.Abs(hz - 2525) < 60, "the opening tone is 2525 Hz: " + hz);
        var station = Voice.Radio(clean, false, true);
        Assert(Voice.Seconds(station) > 1.05 && Voice.Seconds(station) < Voice.Seconds(radio), "a station has no tones and a lower, slower voice");
    }

    public static void TestAudienceAndSignalDelay()
    {
        var rig = Make(VoiceSettings());
        var eng = rig.Eng;
        var sounds = new List<string>();
        eng.PlaySound = (ch, pcm, loop, vol) => sounds.Add(ch + ":" + (loop ? "loop" : "once"));
        var st = eng.StartStream("v1", "Kerbal X", 0, 0);
        Assert(sounds.Count == 0, "no sounds before they're recorded");
        foreach (var fx in NewsEngine.Sounds) rig.Cache[CacheKeyOfSound(eng, fx[0])] = new byte[4000];
        eng.EndStream(1);
        st = eng.StartStream("v1", "Kerbal X", 10, 10);
        Assert(sounds.Contains("room:loop"), "the control room murmurs under the stream: " + string.Join(",", sounds.ToArray()));
        Run(eng, 10, 40);
        int before = st.Viewers;
        eng.StreamSample(new TelemetrySample { UT = 11, Altitude = 80, Situation = "FLYING", Body = "Kerbin" }, 40);
        eng.StreamMoment("liftoff", 40);
        Run(eng, 40, 70);
        Assert(sounds.Contains("crowd:once") && sounds.Contains("rumble:once"), "the crowd cheers and the roar rolls in");
        Assert(st.Viewers > before * 1.4, "people flood in at liftoff: " + before + " -> " + st.Viewers);
        int peak = st.Viewers;
        Run(eng, 70, 900, 1);
        Assert(st.Viewers < peak * 0.8, "and drift off later: " + peak + " -> " + st.Viewers);

        // A landing on Duna: the confirmation waits for the signal.
        eng.StreamSample(new TelemetrySample { UT = 500, Altitude = 3, Situation = "LANDED", Body = "Duna", SignalDelay = 20 }, 900);
        eng.StreamMoment("touchdown on Duna", 900);
        Run(eng, 900, 940);
        string all = string.Join(" | ", st.Said.Where(l => l.At + st.StartReal > 880).Select(l => l.At.ToString("0.0") + " " + l.Speaker + ": " + l.Text).ToArray());
        var lag = st.Said.FirstOrDefault(l => l.Text.Contains("signal"));
        var conf = st.Said.FirstOrDefault(l => l.Speaker == "ctl" && (l.Text.Contains("Contact") || l.Text.Contains("Touchdown")));
        Assert(lag != null && lag.At + st.StartReal < 905 && conf != null && conf.At + st.StartReal >= 900 + 21, "the signal delay is explained, then the touchdown confirmed: " + all);
    }

    static string CacheKeyOfSound(NewsEngine eng, string name)
    {
        var fx = NewsEngine.Sounds.First(x => x[0] == name);
        return Voice.CacheKey("sfx:" + name, Voice.SfxModel, fx[1] + "|" + fx[2] + "|" + fx[3]);
    }

    public static void TestNoKeyMeansCaptionsOnly()
    {
        var rig = Make(new GlobalSettings { OfflineMode = true });
        var eng = rig.Eng;
        var st = eng.StartStream("v1", "Kerbal X", 0, 0);
        Run(eng, 0, 4.5);
        eng.StreamMoment("liftoff", 5);
        Run(eng, 5, 6);
        Assert(rig.Spoken.Count == 0 && rig.Played.Count == 0, "no key: no voice calls");
        Assert(st.Said.Count == 2 && st.Caption != "", "captions still show: " + st.Said.Count + " [" + st.Caption + "] " + string.Join("|", st.Said.Select(l => l.At + ":" + l.Text).ToArray()));
    }

    public static void TestFullCommentaryTakesOneAiLinePerChatCall()
    {
        var g = VoiceSettings(true);
        g.Announcer = 2;
        var rig = Make(g, "{\"viewers\":300,\"announcer\":\"Vehicle is pitching downrange, passing ten kilometres.\",\"messages\":[{\"author\":\"a\",\"text\":\"go go go\"}]}");
        var eng = rig.Eng;
        var st = eng.StartStream("v1", "Kerbal X", 0, 0);
        eng.StreamSample(new TelemetrySample { UT = 1, Altitude = 80, Situation = "FLYING", Body = "Kerbin" }, 1);
        eng.StreamMoment("liftoff", 2);
        Run(eng, 0, 40, 0.25);
        var chatCalls = rig.T.Sent.Where(r => !r.Url.Contains("elevenlabs")).ToList();
        Assert(chatCalls.Count >= 1 && chatCalls.All(r => r.Body.Contains("ANNOUNCER")), "the chat call asks for a line");
        Assert(rig.Spoken.Any(x => x.Contains("pitching downrange")), "the AI line is spoken: " + string.Join(" | ", rig.Spoken.ToArray()));
    }

    public static void TestStreamIsRecordedAndPlaysBack()
    {
        var rig = Make(VoiceSettings());
        var eng = rig.Eng;
        var st = eng.StartStream("v1", "Kerbal X", 0, 0);
        Assert(st.RecFolder != "", "recording by default");
        eng.StreamCamera("Pad camera", false, 0.5);
        for (int i = 0; i <= 40; i++)
        {
            double t = i * 0.5;
            eng.SetRealTime(t);
            if (i % 6 == 0) eng.StreamSample(new TelemetrySample { UT = t, Altitude = 80 + t * 100, Speed = t * 20, Situation = t > 5 ? "FLYING" : "PRELAUNCH", Body = "Kerbin" }, t);
            if (i == 10) eng.StreamMoment("liftoff", t);
            if (i == 12) eng.StreamSay("hello from the agency", true, "", t);
            eng.TickStream(t, true);
            int n = eng.ReplayFrameDue(t);
            if (n > 0) rig.Files[st.RecFolder + "/" + NewsEngine.FrameFile(n)] = new byte[] { 1 };
        }
        var arch = eng.EndStream(30);
        Assert(arch.Replay == st.RecFolder && rig.Index.ContainsKey(arch.Replay), "the index is saved and the archive points at it");
        Assert(st.RecFrames >= 19 && st.RecFrames <= 22, "a frame a second: " + st.RecFrames);
        Assert(rig.Files.Keys.Any(k => k.EndsWith(".wav")), "announcer audio saved with the replay");

        // The archive survives a save and load.
        var n2 = new DataNode("S"); arch.Save(n2);
        Assert(StreamArchive.Load(n2).Replay == arch.Replay, "replay folder saved");

        var dir = Path.Combine(Directory.GetCurrentDirectory(), "GameData/KerbalNews/Sites");
        var sites = new Sites(eng, rel => { var p = Path.Combine(dir, rel); return File.Exists(p) ? File.ReadAllText(p) : null; });
        sites.Now = () => 30;
        string list = sites.Render("live.kb").Html;
        Assert(list.Contains("REPLAY"), "the list marks streams with a replay");
        Func<string> page = () => sites.Render("live.kb/a/" + arch.Id).Html;
        string h = page();
        Assert(!h.Contains("{{") && h.Contains("kbreplay:frame") && h.Contains("replay-play") && h.Contains("00:00 / 00:"), "player shows: " + h.Length);
        Assert(!h.Contains("hello from the agency"), "nothing from later in the stream yet");
        sites.Handle("replay-play", new Dictionary<string, string>());
        Assert(eng.Replay.Playing, "playing");
        for (double t = 1000; t <= 1008; t += 0.1) eng.TickReplay(t);
        Assert(eng.Replay.Pos > 7 && eng.ReplayFrameNow() >= 6, "moves along with frames: " + eng.Replay.Pos + " frame " + eng.ReplayFrameNow());
        h = page();
        Assert(h.Contains("hello from the agency") && h.Contains("ANNOUNCER"), "chat and announcer as they were");
        var pr = sites.Render("live.kb/a/" + arch.Id);
        var root = Html.Parse(pr.Html);
        Css.Apply(root, Css.Parse(pr.Css));
        var lay = Layout.Run(root, 1000, CommunityTests.FakeMeasure);
        var seekRegions = lay.Regions.Where(r => r.Link >= 0 && (lay.Links[r.Link].Href + lay.Links[r.Link].Action).Contains("replay-seek")).ToList();
        seekRegions = seekRegions.Where(r => r.H < 20).ToList();
        Assert(seekRegions.Count == 48 && seekRegions.All(r => r.W > 3 && r.H >= 6 && r.H < 30), "the seek bar is a row of clickable blocks: " + seekRegions.Count + " " + string.Join(",", seekRegions.Take(3).Select(r => r.W + "x" + r.H).ToArray()));
        Assert(lay.Images.Any(i => i.Src == "kbreplay:frame" && i.W > 300), "the video is on the page");
        Assert(rig.ReplayVoice.Count >= 1, "the liftoff call plays during the replay");
        sites.Handle("replay-seek/0", new Dictionary<string, string>());
        Assert(eng.Replay.Pos == 0 && !page().Contains("hello from the agency"), "seek back");
        sites.Handle("replay-speed/4", new Dictionary<string, string>());
        Assert(eng.Replay.Speed == 4, "4x");
        for (double t = 2000; t <= 2010; t += 0.1) eng.TickReplay(t);
        Assert(!eng.Replay.Playing && Math.Abs(eng.Replay.Pos - eng.Replay.Data.Length) < 0.01, "stops at the end");

        // The game wasn't saved after the stream: the replay on disk still shows and plays.
        eng.State.Streams.Clear();
        eng.ReplayFolders = () => rig.Index.Keys.ToList();
        string list2 = sites.Render("live.kb").Html;
        Assert(list2.Contains("REPLAY") && list2.Contains("Kerbal X") && list2.Contains("live.kb/a/r-" + arch.Replay), "an unsaved stream is still listed");
        string h2 = sites.Render("live.kb/a/r-" + arch.Replay).Html;
        Assert(h2.Contains("kbreplay:frame") && h2.Contains("replay-play"), "and plays");

        // Replays off: nothing recorded.
        eng.Settings.ReplayQuality = 0;
        var st2 = eng.StartStream("v1", "Kerbal X", 50, 50);
        Assert(st2.RecFolder == "" && eng.ReplayFrameDue(51) == -1, "off means off");
        Assert(eng.EndStream(60).Replay == "", "no replay in the archive");
    }

    public static void TestStreamIsInTheSaveFromTheStart()
    {
        var rig = Make(new GlobalSettings { OfflineMode = true });
        var eng = rig.Eng;
        var st = eng.StartStream("v1", "Kerbal X", 0, 0);
        Assert(eng.State.Streams.Count == 1 && eng.State.Streams[0].Replay == st.RecFolder, "the entry exists while live, so a save mid-stream keeps it");
        var dir = Path.Combine(Directory.GetCurrentDirectory(), "GameData/KerbalNews/Sites");
        var sites = new Sites(eng, rel => { var p = Path.Combine(dir, rel); return File.Exists(p) ? File.ReadAllText(p) : null; });
        sites.Now = () => 0;
        Assert(!sites.Render("live.kb").Html.Contains("Past streams"), "but it isn't listed as past while on air");
        // A scene change loads the save again before the stream is ended: the entry comes back with it.
        eng.SetState(new NewsState());
        var arch = eng.EndStream(100);
        Assert(eng.State.Streams.Count == 1 && eng.State.Streams[0] == arch && arch.EndUT == 100, "ending puts it into the new state");
    }

    public static void TestVoiceSamplesDirectionsAndAccountVoices()
    {
        var g = VoiceSettings();
        var rig = Make(g);
        rig.T.Responder = r =>
        {
            if (r.Url.EndsWith("/voices")) return new HttpResult { Status = 200, Body = "{\"voices\":[{\"voice_id\":\"ann1\",\"name\":\"Big Tony - Sports Announcer\",\"labels\":{\"accent\":\"american\",\"use_case\":\"sports_commentary\"}}]}" };
            rig.Spoken.Add(Json.Str(Json.Parse(r.Body), "text", ""));
            return Pcm(2);
        };
        var eng = rig.Eng;
        var dir = Path.Combine(Directory.GetCurrentDirectory(), "GameData/KerbalNews/Sites");
        var sites = new Sites(eng, rel => { var p = Path.Combine(dir, rel); return File.Exists(p) ? File.ReadAllText(p) : null; });
        sites.Now = () => 0;
        sites.Handle("voice/onwK4e9ZLuTAKqWW03F9", new Dictionary<string, string>());
        Assert(g.VoiceId == "onwK4e9ZLuTAKqWW03F9" && rig.Played.Count == 1 && rig.Spoken.Count == 1, "picking a voice plays a sample");
        Assert(!rig.Spoken[0].Contains("["), "plain models get no bracket directions: " + rig.Spoken[0]);
        sites.Handle("voice/pNInz6obpgDQGcFmaJgB", new Dictionary<string, string>());
        sites.Handle("voice/onwK4e9ZLuTAKqWW03F9", new Dictionary<string, string>());
        Assert(rig.Played.Count == 3 && rig.Spoken.Count == 2, "hearing a voice again is free");
        sites.Handle("voice-model/eleven_v3", new Dictionary<string, string>());
        Assert(rig.Spoken.Last().StartsWith("[") && rig.Spoken.Last().Contains("Liftoff"), "v3 gets a direction: " + rig.Spoken.Last());
        sites.Handle("announcer-style/excited", new Dictionary<string, string>());
        Assert(rig.Spoken.Last().Contains("shouting"), "the sportscaster shouts liftoff: " + rig.Spoken.Last());
        sites.Handle("load-voices", new Dictionary<string, string>());
        string h = sites.Render("settings.kb").Html;
        Assert(h.Contains("Big Tony") && h.Contains("voice/ann1") && h.Contains("sports commentary"), "account voices are listed");
        // On a stream, the caption stays clean while the voice gets the direction.
        var st = eng.StartStream("v1", "Kerbal X", 0, 0);
        Run(eng, 0, 4);
        eng.StreamMoment("liftoff", 5);
        Run(eng, 5, 6);
        Assert(!st.Caption.Contains("[") && rig.Spoken.Last().Contains("[excited"), "caption: " + st.Caption + " / sent: " + rig.Spoken.Last());
    }

    public static void TestPrerecordedLinesAreUsedFirst()
    {
        var g = VoiceSettings();
        g.VoiceModel = Voice.DefaultModel;
        Assert(new GlobalSettings().VoiceId == "iP95p4xoKVk53GoZ742B" && Voice.DefaultModel == "eleven_v3", "Chris on v3 by default");
        var old = new DataNode("S"); old.Add("voiceId", "pNInz6obpgDQGcFmaJgB"); old.Add("voiceModel", "eleven_flash_v2_5");
        var moved = GlobalSettings.Load(old, null);
        Assert(moved.VoiceId == "iP95p4xoKVk53GoZ742B" && moved.VoiceModel == "eleven_v3", "the old defaults move to Chris once");
        var sn = new DataNode("S"); var kn = new DataNode("K"); var mine = new GlobalSettings { VoiceId = "pNInz6obpgDQGcFmaJgB", VoiceModel = "eleven_flash_v2_5" }; mine.Save(sn, kn);
        Assert(GlobalSettings.Load(sn, kn).VoiceId == "pNInz6obpgDQGcFmaJgB", "but a later choice of Adam sticks");

        var rig = Make(g);
        var eng = rig.Eng;
        var common = eng.CommonLines();
        Assert(common.Count > 80 && common.All(x => !x.Key.Contains("{")), "plenty of common lines: " + common.Count);
        Assert(common.Any(x => x.Key == "Five." && x.Value == "count") && common.Any(x => x.Key.Contains("Minmus")), "numbers and places");
        var dir = Path.Combine(Directory.GetCurrentDirectory(), "GameData/KerbalNews/Sites");
        var sites = new Sites(eng, rel => { var p = Path.Combine(dir, rel); return File.Exists(p) ? File.ReadAllText(p) : null; });
        sites.Now = () => 0;
        Assert(sites.Render("settings.kb").Html.Contains("action:prerecord"), "the button shows");
        sites.Handle("prerecord", new Dictionary<string, string>());
        Assert(rig.Spoken.Count == common.Count && eng.LinesToRecord().Count == 0 && !eng.Prerecording, "all recorded: " + rig.Spoken.Count + " / " + common.Count);
        Assert(rig.Spoken.Where(x => !common.Any(c => c.Speaker == "sfx" && c.Value == x)).All(x => x.StartsWith("[")), "with v3 directions");
        Assert(common.Count(c => c.Speaker == "sfx") == NewsEngine.Sounds.Length && common.Any(c => c.Speaker == "ctl" && c.Key == "Vehicle is supersonic."), "controller lines and sound effects are recorded too");
        Assert(!sites.Render("settings.kb").Html.Contains("action:prerecord"), "nothing left to record");

        // A whole countdown and launch now needs no new calls, apart from lines that name the craft.
        int before = rig.Spoken.Count;
        var st = eng.StartStream("v1", "Kerbal X", 0, 0);
        eng.CountdownStart(60, 1);
        Run(eng, 1, 62);
        eng.StreamSample(new TelemetrySample { UT = 1, Altitude = 80, Situation = "FLYING", Body = "Kerbin" }, 62);
        eng.StreamMoment("liftoff", 62);
        Run(eng, 62, 66);
        eng.StreamMoment("the vessel reached orbit around Kerbin", 200);
        Run(eng, 200, 205);
        var fresh = rig.Spoken.Skip(before).ToList();
        Assert(fresh.All(x => x.Contains("Kerbal X")), "only craft-name lines are new: " + string.Join(" | ", fresh.ToArray()));
        Assert(st.Said.Count >= 14, "and the announcer still said everything: " + string.Join(" | ", st.Said.Select(l => l.Text).ToArray()));
    }

    public static void TestShippedLinesPlayWithoutAKey()
    {
        // Record everything with a key, ship it, then play as a player with no key.
        var g = VoiceSettings();
        g.VoiceModel = Voice.DefaultModel;
        var rig = Make(g);
        rig.Eng.Prerecord(() => { });
        var shipped = new List<KeyValuePair<string, string>>();
        var banks = new List<string>();
        rig.Eng.ExportVoiceBank = (name, lines) => { banks.Add(name); shipped.AddRange(lines); };
        int n = rig.Eng.ExportBank();
        Assert(banks.Contains("chris-eleven_v3-calm") && banks.Contains("bill-eleven_v3-radio") && banks.Contains("sfx"), "three banks: " + string.Join(", ", banks.ToArray()));
        Assert(n > 60 && shipped.Count == n && shipped.All(x => !x.Value.Contains(rig.Eng.AgencyName)) && rig.Eng.BankName == "chris-eleven_v3-calm", "export: " + n + " " + rig.Eng.BankName);

        var player = new GlobalSettings { OfflineMode = true };
        var rig2 = Make(player);
        foreach (var kv in shipped) rig2.Cache[kv.Key] = rig.Cache[kv.Key];
        var st = rig2.Eng.StartStream("v1", "Kerbal X", 0, 0);
        rig2.Eng.CountdownStart(30, 1);
        Run(rig2.Eng, 1, 32);
        rig2.Eng.StreamMoment("liftoff", 32);
        Run(rig2.Eng, 32, 36);
        Assert(rig2.Spoken.Count == 0, "no key: no calls");
        Assert(rig2.Played.Count >= 12, "but the shipped lines are heard: " + rig2.Played.Count + " of " + string.Join(" | ", st.Said.Select(l => l.Text).ToArray()));
        Assert(st.Said.Any(l => l.Text.StartsWith("And liftoff! We have") || l.Text.StartsWith("Liftoff! The vehicle")), "a stock liftoff line is picked");
    }

    public static void TestVoiceSettingsSaveAndShow()
    {
        var g = new GlobalSettings { OfflineMode = true };
        var eng = new NewsEngine(new NewsState(), g, null, new SimpleClock(Day));
        var dir = Path.Combine(Directory.GetCurrentDirectory(), "GameData/KerbalNews/Sites");
        var sites = new Sites(eng, rel => { var p = Path.Combine(dir, rel); return File.Exists(p) ? File.ReadAllText(p) : null; });
        sites.Now = () => 0;
        string h = sites.Render("settings.kb").Html;
        Assert(!h.Contains("{{") && h.Contains("Livestream announcer and replays") && h.Contains("ElevenLabs API key") && h.Contains("Smooth (3 frames a second)"), "section renders");
        sites.Handle("save-voice", new Dictionary<string, string> { { "voiceKey", " el-123 " }, { "voiceCustom", "myvoice42" } });
        Assert(g.VoiceKey == "el-123" && g.VoiceId == "myvoice42" && g.VoiceOn, "key and custom voice saved");
        g.Keys.Remove(GlobalSettings.VoiceKeyId);
        sites.Handle("save-settings", new Dictionary<string, string> { { "voiceKey", "el-123" } });
        Assert(g.VoiceKey == "el-123", "any Save button keeps the voice key");
        sites.Handle("announcer/2", new Dictionary<string, string>());
        sites.Handle("voice-model/eleven_multilingual_v2", new Dictionary<string, string>());
        sites.Handle("replays/2", new Dictionary<string, string>());
        sites.Handle("replay-space/10", new Dictionary<string, string>());
        var sn = new DataNode("S"); var kn = new DataNode("K");
        g.Save(sn, kn);
        Assert(!sn.ToString().Contains("el-123"), "the key never goes in settings.cfg");
        var back = GlobalSettings.Load(sn, kn);
        Assert(back.Announcer == 2 && back.VoiceKey == "el-123" && back.VoiceId == "myvoice42" && back.VoiceModel == "eleven_multilingual_v2" && back.ReplayQuality == 2 && back.ReplayMaxGb == 10, "settings round trip");
        h = sites.Render("settings.kb").Html;
        Assert(h.Contains("forget-voice-key"), "forget the key");
        sites.Handle("forget-voice-key", new Dictionary<string, string>());
        Assert(g.VoiceKey == "" && !g.VoiceOn, "forgotten");
    }
}
