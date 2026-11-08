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
        public const string DefaultVoice = "pNInz6obpgDQGcFmaJgB";
        public const string DefaultModel = "eleven_flash_v2_5";

        /// <summary>A few of ElevenLabs' stock voices that suit a launch commentator: id, name, description.</summary>
        public static readonly string[][] Voices =
        {
            new[] { "pNInz6obpgDQGcFmaJgB", "Adam", "deep, steady" },
            new[] { "nPczCjzI2devNBz1zQrb", "Brian", "warm narrator" },
            new[] { "onwK4e9ZLuTAKqWW03F9", "Daniel", "British news presenter" },
            new[] { "pqHfZKP75CvOlQylNhV4", "Bill", "older, trustworthy" },
            new[] { "JBFqnCBsd6RMkjVDRZzb", "George", "British, calm" },
            new[] { "cjVigY5qzO86Huf0OWal", "Eric", "smooth, confident" },
            new[] { "iP95p4xoKVk53GoZ742B", "Chris", "casual, upbeat" },
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
            bool hype = style == "excited";
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
