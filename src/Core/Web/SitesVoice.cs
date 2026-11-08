using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KerbalNews.Web
{
    /// <summary>Settings for the livestream announcer (ElevenLabs voice) and stream replays.</summary>
    public partial class Sites
    {
        public string VoiceTestResult = "";
        public bool VoiceTestOk;

        void AddVoiceSettings(Dictionary<string, object> m, GlobalSettings g)
        {
            Func<string, string, bool, object> opt = (label, action, on) => new Dictionary<string, object> { { "label", label }, { "action", action }, { "on", on } };
            m["announcerOpts"] = new List<object> { opt("Off", "announcer/0", g.Announcer == 0), opt("Big moments", "announcer/1", g.Announcer == 1), opt("Full commentary", "announcer/2", g.Announcer == 2) };
            m["announcerHint"] = g.Announcer == 0 ? "No announcer on your streams."
                : g.Announcer == 1 ? "The announcer calls the countdown (the last ten seconds one by one), liftoff, orbit, landings, holds, scrubs and anything going wrong. No AI calls: the lines come from templates."
                : "Everything in Big moments, plus staging, camera cuts, the odd altitude and speed call on the climb, and one line of AI commentary with each chat update (no extra AI calls; it rides along).";
            m["announcerOn"] = g.Announcer > 0;
            bool hasKey = g.VoiceKey != "";
            m["voiceHasKey"] = hasKey;
            m["voiceSpoken"] = g.VoiceEnabled;
            var voices = AllVoices(g);
            m["voiceOpts"] = voices.Select(v => opt(v[1] + (v[2] != "" ? " (" + v[2] + ")" : ""), "voice/" + v[0], g.VoiceId == v[0])).ToList();
            m["voiceCustom"] = voices.Any(v => v[0] == g.VoiceId) ? "" : g.VoiceId;
            m["voicesLoaded"] = g.FetchedVoiceList.Count > 0;
            m["styleOpts"] = new List<object> { opt("Mission control (calm)", "announcer-style/calm", g.AnnouncerStyle != "excited"), opt("Sportscaster (excited)", "announcer-style/excited", g.AnnouncerStyle == "excited") };
            m["directed"] = Voice.TakesDirections(g.VoiceModel);
            if (hasKey)
            {
                var all = Engine.CommonLines();
                var todo = Engine.LinesToRecord();
                m["pre"] = new Dictionary<string, object>
                {
                    { "voice", Engine.VoiceLabel(g.VoiceId) }, { "total", all.Count }, { "todo", todo.Count }, { "done", all.Count - todo.Count },
                    { "letters", todo.Sum(x => Voice.Spoken(x.Key, x.Value, g.AnnouncerStyle, g.VoiceModel).Length).ToString("N0", Inv) },
                    { "busy", Engine.Prerecording }, { "status", Engine.PrerecordStatus }, { "allDone", todo.Count == 0 }, { "anyDone", all.Count > todo.Count }, { "bank", Engine.BankName },
                };
            }
            m["voiceModelOpts"] = Voice.Models.Select(v => opt(v[1], "voice-model/" + v[0], g.VoiceModel == v[0])).ToList();
            var cur = Voice.Models.FirstOrDefault(v => v[0] == g.VoiceModel);
            m["voiceModelHint"] = cur != null ? cur[1] + ": " + cur[2] + "." : "Model " + g.VoiceModel + ".";
            m["voiceVolumeOpts"] = new List<object> { opt("Quiet", "voice-volume/0.4", Math.Abs(g.VoiceVolume - 0.4) < 0.01), opt("Normal", "voice-volume/0.8", Math.Abs(g.VoiceVolume - 0.8) < 0.01), opt("Loud", "voice-volume/1", Math.Abs(g.VoiceVolume - 1) < 0.01) };
            m["voiceStatus"] = Engine.VoiceStatus;
            m["voiceTestResult"] = VoiceTestResult;
            m["voiceTestOk"] = VoiceTestOk;
            m["replayOpts"] = new List<object> { opt("Off", "replays/0", g.ReplayQuality == 0), opt("Normal (1 frame a second)", "replays/1", g.ReplayQuality == 1), opt("Smooth (3 frames a second)", "replays/2", g.ReplayQuality == 2) };
            m["replaySpaceOpts"] = new[] { 1, 3, 10 }.Select(n => opt(n + " GB", "replay-space/" + n, g.ReplayMaxGb == n)).ToList();
            long used = 0;
            if (Engine.ReplayDiskUse != null) { try { used = Engine.ReplayDiskUse(); } catch (Exception) { used = 0; } }
            m["replayUsed"] = used >= 1L << 30 ? (used / (double)(1L << 30)).ToString("0.0", Inv) + " GB" : (used / (double)(1L << 20)).ToString("0", Inv) + " MB";
            m["replaysOn"] = g.ReplayQuality > 0;
        }

        /// <summary>The stock voices, then any others loaded from the player's account.</summary>
        static List<string[]> AllVoices(GlobalSettings g)
        {
            var l = Voice.Voices.Select(v => new[] { v[0], v[1], v[2] }).ToList();
            foreach (var v in g.FetchedVoiceList) if (!l.Any(x => x[0] == v[0])) l.Add(v);
            return l;
        }

        void PlaySample()
        {
            VoiceTestResult = "Getting a sample...";
            VoiceTestOk = true;
            Engine.VoiceTest(msg => { VoiceTestOk = msg.StartsWith("Playing"); VoiceTestResult = msg; Changed(); });
        }

        ActionResult HandleVoice(string verb, string arg, IDictionary<string, string> form)
        {
            var g = Engine.Settings;
            var r = new ActionResult();
            int n;
            double d;
            switch (verb)
            {
                case "announcer": if (int.TryParse(arg, out n)) g.Announcer = Math.Max(0, Math.Min(2, n)); SaveSettings(); return r;
                case "toggle-voice": g.VoiceEnabled = !g.VoiceEnabled; Engine.ResetVoice(); SaveSettings(); return r;
                case "voice":
                    if (AllVoices(g).Any(v => v[0] == arg)) { g.VoiceId = arg; Engine.ResetVoice(); SaveSettings(); PlaySample(); }
                    return r;
                case "voice-model": if (Voice.Models.Any(v => v[0] == arg)) { g.VoiceModel = arg; Engine.ResetVoice(); SaveSettings(); PlaySample(); } return r;
                case "announcer-style": g.AnnouncerStyle = arg == "excited" ? "excited" : "calm"; SaveSettings(); PlaySample(); return r;
                case "prerecord":
                    if (!Engine.Prerecord(Changed)) { VoiceTestResult = Engine.PrerecordStatus != "" ? Engine.PrerecordStatus : "Save an ElevenLabs key first."; VoiceTestOk = true; }
                    return r;
                case "export-voice":
                {
                    int copied = Engine.ExportBank();
                    VoiceTestResult = copied > 0 ? "Copied " + copied + " recorded lines into GameData/KerbalNews/Voice/" + Engine.BankName + ". Everyone who gets this folder with the mod hears them, with or without a key." : "Nothing recorded to copy yet.";
                    VoiceTestOk = copied > 0;
                    return r;
                }
                case "load-voices":
                    VoiceTestResult = "Asking ElevenLabs for your voices...";
                    VoiceTestOk = true;
                    Engine.LoadVoices(msg => { VoiceTestOk = msg.StartsWith("Loaded"); VoiceTestResult = msg; if (VoiceTestOk) SaveSettings(); Changed(); });
                    return r;
                case "voice-volume": if (double.TryParse(arg, NumberStyles.Float, Inv, out d)) g.VoiceVolume = Math.Max(0, Math.Min(1, d)); SaveSettings(); return r;
                case "replays": if (int.TryParse(arg, out n)) g.ReplayQuality = Math.Max(0, Math.Min(2, n)); SaveSettings(); return r;
                case "replay-space": if (int.TryParse(arg, out n)) g.ReplayMaxGb = Math.Max(1, Math.Min(50, n)); SaveSettings(); return r;
                case "forget-voice-key": g.Keys.Remove(GlobalSettings.VoiceKeyId); VoiceTestResult = "ElevenLabs key forgotten."; VoiceTestOk = true; SaveSettings(); return r;
                case "save-voice":
                case "test-voice":
                {
                    string key = Get(form, "voiceKey").Trim();
                    if (key != "") { g.Keys[GlobalSettings.VoiceKeyId] = key; g.VoiceEnabled = true; }
                    string custom = Get(form, "voiceCustom").Trim();
                    if (custom != "") g.VoiceId = custom;
                    Engine.ResetVoice();
                    SaveSettings();
                    if (verb == "save-voice") { VoiceTestResult = "Saved."; VoiceTestOk = true; return r; }
                    PlaySample();
                    return r;
                }
            }
            return null;
        }
    }
}
