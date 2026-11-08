using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>
    /// Text to speech through ElevenLabs, for the livestream announcer. The reply is raw 16-bit mono PCM at 22 050 Hz,
    /// so the game can play it without decoding anything. The key is the player's own, kept in secrets.cfg.
    /// </summary>
    public static class Voice
    {
        public const int Rate = 22050;
        public const string BaseUrl = "https://api.elevenlabs.io/v1";
        public const string DefaultVoice = "iP95p4xoKVk53GoZ742B";   // Chris
        public const string DefaultModel = "eleven_v3";
        public const string DefaultController = "pqHfZKP75CvOlQylNhV4";   // Bill

        /// <summary>A few of ElevenLabs' stock voices that suit a launch commentator: id, name, description.</summary>
        public static readonly string[][] Voices =
        {
            new[] { "iP95p4xoKVk53GoZ742B", "Chris", "casual, upbeat (default)" },
            new[] { "pNInz6obpgDQGcFmaJgB", "Adam", "deep, steady" },
            new[] { "nPczCjzI2devNBz1zQrb", "Brian", "warm narrator" },
            new[] { "onwK4e9ZLuTAKqWW03F9", "Daniel", "British news presenter" },
            new[] { "pqHfZKP75CvOlQylNhV4", "Bill", "older, trustworthy" },
            new[] { "JBFqnCBsd6RMkjVDRZzb", "George", "British, calm" },
            new[] { "cjVigY5qzO86Huf0OWal", "Eric", "smooth, confident" },
            new[] { "CwhRBWXzGAHq8TQ4Fs17", "Roger", "relaxed, assured" },
            new[] { "EXAVITQu4vr4xnSDxMaL", "Sarah", "clear, professional" },
            new[] { "XrExE9yKIg1WjnnlVkGX", "Matilda", "warm, friendly" },
            new[] { "Xb7hH8MSUJpSbSDYk0k2", "Alice", "British, crisp" },
            new[] { "21m00Tcm4TlvDq8ikWAM", "Rachel", "clear, bright" },
        };

        /// <summary>Models: id, name, what it is like.</summary>
        public static readonly string[][] Models =
        {
            new[] { "eleven_flash_v2_5", "Flash", "fastest and cheapest (half a credit per letter)" },
            new[] { "eleven_turbo_v2_5", "Turbo", "a little more natural, same price as Flash" },
            new[] { "eleven_multilingual_v2", "Multilingual v2", "best sounding of the plain models, slower, a full credit per letter" },
            new[] { "eleven_v3", "v3 (directed)", "the most expressive: it follows delivery directions, so liftoff is shouted, a loss is said quietly and holds sound tense. Slower to answer" },
        };

        /// <summary>Models that act on bracketed delivery directions such as [excited] instead of reading them out.</summary>
        public static bool TakesDirections(string model) { return (model ?? "").StartsWith("eleven_v3") || (model ?? "").StartsWith("eleven_v4"); }

        /// <summary>How the announcer delivers a line: routine, big (liftoff, orbit), tense (hold, scrub), loss, count (the final ten).</summary>
        public static string Direction(string mood, string style)
        {
            bool hype = style == "excited";
            if (style == "radio")
                switch (mood)
                {
                    case "tense": return "[urgent but controlled, clipped radio call]";
                    case "loss": return "[flat, quiet, controlled radio call]";
                    case "big": return "[steady, quietly pleased radio call]";
                    default: return "[calm, clipped mission control radio call]";
                }
            switch (mood)
            {
                case "big": return hype ? "[excited, shouting over a cheering crowd]" : "[proud, rising excitement]";
                case "tense": return hype ? "[tense, urgent]" : "[serious, steady voice]";
                case "loss": return hype ? "[shocked, quiet]" : "[quiet, somber voice]";
                case "count": return hype ? "[excited, building intensity]" : "[clear, firm]";
                default: return hype ? "[energetic broadcaster voice]" : "[calm, professional broadcast voice]";
            }
        }

        /// <summary>The words as sent: with a delivery direction in front for models that take one.</summary>
        public static string Spoken(string text, string mood, string style, string model)
        {
            return TakesDirections(model) ? Direction(mood, style) + " " + text : text;
        }

        /// <summary>Asks ElevenLabs for the voices on the player's account (stock voices plus any they added from the library).</summary>
        public static HttpRequestSpec ListVoices(string key)
        {
            var req = new HttpRequestSpec { Url = BaseUrl + "/voices", Method = "GET", Body = null, TimeoutSeconds = 30 };
            req.Headers["xi-api-key"] = key ?? "";
            return req;
        }

        /// <summary>"id|name|description" for each voice in a /voices reply, or null with a reason.</summary>
        public static List<string> ParseVoices(HttpResult res, out string error)
        {
            error = null;
            if (res == null || res.Error != null || !res.Ok) { error = res == null ? "No reply" : res.Error ?? "ElevenLabs answered " + res.Status + (ErrorText(res) != "" ? ": " + ErrorText(res) : ""); return null; }
            object root;
            if (!Json.TryParse(res.Body, out root)) { error = "The voice list wasn't readable."; return null; }
            var list = new List<string>();
            foreach (var v in Json.Arr(Json.Get(root, "voices")) ?? new List<object>())
            {
                string id = Json.Str(v, "voice_id", ""), name = Json.Str(v, "name", "");
                if (id == "" || name == "") continue;
                var labels = Json.Obj(Json.Get(v, "labels"));
                var bits = new List<string>();
                if (labels != null)
                    foreach (var k in new[] { "description", "descriptive", "accent", "age", "use_case", "use case" })
                    {
                        object o;
                        if (labels.TryGetValue(k, out o) && o is string && (string)o != "" && !bits.Contains((string)o)) bits.Add(((string)o).Replace("_", " "));
                    }
                string clean = ShortName(name);
                list.Add(id.Replace("|", "") + "|" + clean + "|" + string.Join(", ", bits.Take(3).ToArray()).Replace("|", " ").Replace(";", ","));
            }
            if (list.Count == 0) error = "The account has no voices.";
            return list.Count == 0 ? null : list;
        }

        static string ShortName(string name)
        {
            name = name.Replace("|", " ").Replace(";", ",").Trim();
            int dash = name.IndexOf(" - ", StringComparison.Ordinal);
            return ResponseParser.Clip(dash > 0 ? name.Substring(0, dash) : name, 40);
        }

        public static string VoiceName(string id)
        {
            var v = Voices.FirstOrDefault(x => x[0] == id);
            return v != null ? v[1] : id;
        }

        public static HttpRequestSpec Build(string key, string voiceId, string model, string text, string style = "calm")
        {
            model = string.IsNullOrEmpty(model) ? DefaultModel : model;
            bool hype = style == "excited";   // "radio" (the flight controller) reads like calm
            // v3 takes its delivery from the directions in the text, and only three stability settings; the older models
            // get a livelier, less steady read for the excited style.
            var vs = TakesDirections(model)
                ? new Dictionary<string, object> { { "stability", 0.5 }, { "similarity_boost", 0.75 } }
                : new Dictionary<string, object> { { "stability", hype ? 0.3 : 0.5 }, { "similarity_boost", 0.75 }, { "style", hype ? 0.5 : 0.15 }, { "use_speaker_boost", true } };
            var body = new Dictionary<string, object>
            {
                { "text", text ?? "" },
                { "model_id", model },
                { "voice_settings", vs },
            };
            var req = new HttpRequestSpec
            {
                Url = BaseUrl + "/text-to-speech/" + Uri.EscapeDataString(string.IsNullOrEmpty(voiceId) ? DefaultVoice : voiceId) + "?output_format=pcm_22050",
                Body = Json.Write(body),
                TimeoutSeconds = 30,
            };
            req.Headers["xi-api-key"] = key ?? "";
            req.Headers["Content-Type"] = "application/json";
            return req;
        }

        /// <summary>The PCM from a reply, or a short reason it failed.</summary>
        public static bool TryExtract(HttpResult res, out byte[] pcm, out string error)
        {
            pcm = null;
            error = null;
            if (res == null) { error = "No reply"; return false; }
            if (res.Error != null) { error = "Could not reach ElevenLabs: " + res.Error; return false; }
            if (!res.Ok)
            {
                string detail = ErrorText(res);
                error = res.Status == 401 ? "ElevenLabs refused the key" + (detail != "" ? ": " + detail : ".")
                    : IsQuota(res) ? "The ElevenLabs account is out of characters" + (detail != "" ? ": " + detail : ".")
                    : res.Status == 404 ? "ElevenLabs doesn't know that voice" + (detail != "" ? ": " + detail : ".")
                    : "ElevenLabs answered " + res.Status + (detail != "" ? ": " + detail : "");
                return false;
            }
            var bytes = res.Bytes;
            if (bytes == null || bytes.Length < 200) { error = "ElevenLabs sent no audio."; return false; }
            // A JSON body where audio should be means something went wrong upstream.
            if (bytes[0] == (byte)'I' && bytes[1] == (byte)'D' && bytes[2] == (byte)'3' || bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0) { error = "ElevenLabs sent MP3 instead of raw audio."; return false; }
            if (bytes[0] == (byte)'{') { error = "ElevenLabs sent text instead of audio: " + ResponseParser.Clip(Encoding.UTF8.GetString(bytes), 120); return false; }
            if (bytes.Length % 2 == 1) Array.Resize(ref bytes, bytes.Length - 1);
            pcm = bytes;
            return true;
        }

        /// <summary>Errors that won't fix themselves this session: a bad key or an empty account. The voice stays off until settings change.</summary>
        public static bool IsFatal(HttpResult res)
        {
            return res != null && res.Error == null && (res.Status == 401 || res.Status == 403 || IsQuota(res));
        }

        static bool IsQuota(HttpResult res)
        {
            if (res.Status == 402) return true;
            string b = (res.Body ?? "").ToLowerInvariant();
            return b.Contains("quota_exceeded") || b.Contains("insufficient") && b.Contains("credit");
        }

        static string ErrorText(HttpResult res)
        {
            object root;
            string body = res.Body;
            if (string.IsNullOrEmpty(body) && res.Bytes != null && res.Bytes.Length < 4000) body = Encoding.UTF8.GetString(res.Bytes);
            if (string.IsNullOrEmpty(body) || !Json.TryParse(body, out root)) return "";
            var d = Json.Get(root, "detail");
            string msg = d is string ? (string)d : Json.Str(d, "message", "");
            return ResponseParser.Clip(msg ?? "", 160);
        }

        /// <summary>
        /// Cuts the quiet off the start (and, for countdown numbers, the end) of a clip. v3 clips often open with a breath or
        /// a pause, which put each number late and made the gaps uneven. Keeps a few milliseconds so words aren't clipped.
        /// </summary>
        public static byte[] Trim(byte[] pcm, bool end)
        {
            if (pcm == null || pcm.Length < 400) return pcm;
            int n = pcm.Length / 2;
            const int threshold = 900;            // about -31 dB
            int window = Rate / 100;              // 10 ms
            Func<int, bool> loud = i => { int v = (short)(pcm[2 * i] | pcm[2 * i + 1] << 8); return v > threshold || v < -threshold; };
            int first = 0;
            while (first < n && !loud(first)) first++;
            if (first >= n) return pcm;           // all quiet: leave it
            int last = n - 1;
            if (end) while (last > first && !loud(last)) last--;
            first = Math.Max(0, first - window * 2);
            last = end ? Math.Min(n - 1, last + window * 6) : n - 1;
            if (first == 0 && last == n - 1) return pcm;
            var outBytes = new byte[(last - first + 1) * 2];
            Array.Copy(pcm, first * 2, outBytes, 0, outBytes.Length);
            return outBytes;
        }

        // ------------------------------------------------------------------ sound effects

        public const string SfxModel = "eleven_text_to_sound_v3";

        /// <summary>A sound effect from ElevenLabs (crowd, control room), as raw PCM like the voice.</summary>
        public static HttpRequestSpec BuildSfx(string key, string prompt, double seconds, bool loop)
        {
            var body = new Dictionary<string, object>
            {
                { "text", prompt ?? "" },
                { "duration_seconds", Math.Max(0.5, Math.Min(30, seconds)) },
                { "prompt_influence", 0.5 },
                { "model_id", SfxModel },
            };
            if (loop) body["loop"] = true;
            var req = new HttpRequestSpec { Url = BaseUrl + "/sound-generation?output_format=pcm_22050", Body = Json.Write(body), TimeoutSeconds = 60 };
            req.Headers["xi-api-key"] = key ?? "";
            req.Headers["Content-Type"] = "application/json";
            return req;
        }

        static short S(byte[] b, int i) { return (short)(b[2 * i] | b[2 * i + 1] << 8); }
        static void Put(byte[] b, int i, double v) { int x = (int)Math.Round(Math.Max(-32767, Math.Min(32767, v))); b[2 * i] = (byte)x; b[2 * i + 1] = (byte)(x >> 8); }

        /// <summary>Blends the end of a clip into its start, so it loops without a click even if it wasn't made seamless.</summary>
        public static byte[] LoopSmooth(byte[] pcm)
        {
            if (pcm == null) return null;
            int n = pcm.Length / 2, f = Math.Min(Rate / 2, n / 4);
            if (f < 32) return pcm;
            var o = new byte[(n - f) * 2];
            Array.Copy(pcm, 0, o, 0, o.Length);
            for (int i = 0; i < f; i++)
            {
                double a = (double)i / f;
                Put(o, i, S(pcm, i) * a + S(pcm, n - f + i) * (1 - a));
            }
            return o;
        }

        /// <summary>A pure tone with soft edges, for the Quindar beeps around a controller's radio call.</summary>
        public static double[] Tone(double hz, double seconds, double amp)
        {
            int n = (int)(Rate * seconds), edge = Rate / 200;
            var o = new double[n];
            for (int i = 0; i < n; i++)
            {
                double env = Math.Min(1, Math.Min(i, n - 1 - i) / (double)edge);
                o[i] = Math.Sin(2 * Math.PI * hz * i / Rate) * amp * env;
            }
            return o;
        }

        /// <summary>
        /// Makes a clean voice clip sound like a mission control radio loop: a narrow 300-3000 Hz band, a little drive and
        /// hiss, and (for the controller) the Quindar tones, 2525 Hz before the call and 2475 Hz after, as on Apollo.
        /// Stations answering the poll get a thinner, slightly lower channel so they don't sound like the same person.
        /// </summary>
        public static byte[] Radio(byte[] pcm, bool quindar, bool station)
        {
            if (pcm == null || pcm.Length < 4) return pcm;
            int n = pcm.Length / 2;
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = S(pcm, i) / 32768.0;
            if (station) x = Resample(x, 0.93);
            double lo = station ? 450 : 300, hi = station ? 2500 : 3000;
            double aH = Rate / (Rate + 2 * Math.PI * lo), aL = 2 * Math.PI * hi / (Rate + 2 * Math.PI * hi);
            double hpPrev = 0, hpIn = 0, l1 = 0, l2 = 0, peak = 1e-6;
            var rng = new Random(n);
            var y = new double[x.Length];
            for (int i = 0; i < x.Length; i++)
            {
                double hp = aH * (hpPrev + x[i] - hpIn); hpIn = x[i]; hpPrev = hp;
                l1 += aL * (hp - l1); l2 += aL * (l1 - l2);
                double v = Math.Tanh(l2 * (station ? 3.2 : 2.4));
                v += (rng.NextDouble() - 0.5) * 0.012;
                y[i] = v;
                peak = Math.Max(peak, Math.Abs(v));
            }
            double g = 0.85 / peak;
            var parts = new List<double[]>();
            if (quindar) { parts.Add(Tone(2525, 0.25, 0.22)); parts.Add(new double[Rate / 12]); }
            for (int i = 0; i < y.Length; i++) y[i] *= g;
            parts.Add(y);
            // Keying off: a short burst of static, then the closing tone.
            var tail = new double[Rate / 14];
            for (int i = 0; i < tail.Length; i++) tail[i] = (rng.NextDouble() - 0.5) * 0.18 * (1 - (double)i / tail.Length);
            parts.Add(tail);
            if (quindar) { parts.Add(new double[Rate / 20]); parts.Add(Tone(2475, 0.25, 0.22)); }
            int total = parts.Sum(p => p.Length);
            var o = new byte[total * 2];
            int k = 0;
            foreach (var p in parts) foreach (var v in p) Put(o, k++, v * 32767);
            return o;
        }

        static double[] Resample(double[] x, double speed)
        {
            int n = (int)(x.Length / speed);
            var o = new double[n];
            for (int i = 0; i < n; i++)
            {
                double p = i * speed;
                int a = (int)p;
                double f = p - a;
                o[i] = a + 1 < x.Length ? x[a] * (1 - f) + x[a + 1] * f : x[Math.Min(a, x.Length - 1)];
            }
            return o;
        }

        /// <summary>
        /// Shortens the long pauses inside a line. v3 in particular leaves half-second gaps between phrases, which makes a
        /// commentator sound slow; any quiet stretch over 0.2 s is cut to 0.12 s. Works on recorded clips as they play, so
        /// nothing needs recording again.
        /// </summary>
        public static byte[] Tighten(byte[] pcm)
        {
            if (pcm == null || pcm.Length < Rate) return pcm;
            int n = pcm.Length / 2, win = Rate / 100;
            int wins = n / win;
            var quiet = new bool[wins];
            for (int w = 0; w < wins; w++)
            {
                int peak = 0;
                for (int i = w * win; i < (w + 1) * win; i++) { int v = Math.Abs((int)S(pcm, i)); if (v > peak) peak = v; }
                quiet[w] = peak < 700;
            }
            const int longGap = 20, keep = 12;
            var o = new System.IO.MemoryStream(pcm.Length);
            int wi = 0;
            while (wi < wins)
            {
                int run = 0;
                while (wi + run < wins && quiet[wi + run]) run++;
                if (run >= longGap && wi > 0 && wi + run < wins)
                {
                    o.Write(pcm, wi * win * 2, keep / 2 * win * 2);
                    o.Write(pcm, (wi + run - keep / 2) * win * 2, keep / 2 * win * 2);
                    wi += run;
                }
                else if (run > 0) { o.Write(pcm, wi * win * 2, run * win * 2); wi += run; }
                else { o.Write(pcm, wi * win * 2, win * 2); wi++; }
            }
            if (wins * win < n) o.Write(pcm, wins * win * 2, (n - wins * win) * 2);
            return o.ToArray();
        }

        public static double Seconds(byte[] pcm) { return pcm == null ? 0 : pcm.Length / 2.0 / Rate; }

        /// <summary>Name for a cached clip: the same words in the same voice are only ever paid for once.</summary>
        public static string CacheKey(string voice, string model, string text)
        {
            unchecked
            {
                ulong h = 1469598103934665603UL;
                foreach (char c in (voice ?? "") + "|" + (model ?? "") + "|" + (text ?? "")) { h ^= c; h *= 1099511628211UL; }
                return h.ToString("x16");
            }
        }

        /// <summary>A WAV file around raw PCM, so a saved clip plays in any media player.</summary>
        public static byte[] Wav(byte[] pcm)
        {
            pcm = pcm ?? new byte[0];
            var w = new System.IO.MemoryStream(44 + pcm.Length);
            Action<string> s = x => { var b = Encoding.ASCII.GetBytes(x); w.Write(b, 0, b.Length); };
            Action<int> i32 = x => w.Write(BitConverter.GetBytes(x), 0, 4);
            Action<short> i16 = x => w.Write(BitConverter.GetBytes(x), 0, 2);
            s("RIFF"); i32(36 + pcm.Length); s("WAVE");
            s("fmt "); i32(16); i16(1); i16(1); i32(Rate); i32(Rate * 2); i16(2); i16(16);
            s("data"); i32(pcm.Length); w.Write(pcm, 0, pcm.Length);
            return w.ToArray();
        }

        /// <summary>The PCM inside a WAV written by Wav (or raw PCM as it is).</summary>
        public static byte[] FromWav(byte[] wav)
        {
            if (wav == null || wav.Length < 44 || wav[0] != 'R' || wav[1] != 'I') return wav;
            var pcm = new byte[wav.Length - 44];
            Array.Copy(wav, 44, pcm, 0, pcm.Length);
            return pcm;
        }
    }
}
