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
            new[] { "JBFqnCBsd6RMkjVDRZzb", "George", "British, calm" },
            new[] { "21m00Tcm4TlvDq8ikWAM", "Rachel", "clear, bright" },
        };

        /// <summary>Models: id, name, what it is like.</summary>
        public static readonly string[][] Models =
        {
            new[] { "eleven_flash_v2_5", "Flash", "fastest and cheapest (half a credit per letter)" },
            new[] { "eleven_turbo_v2_5", "Turbo", "a little more natural, same price as Flash" },
            new[] { "eleven_multilingual_v2", "Multilingual v2", "best sounding, slower, a full credit per letter" },
        };

        public static string VoiceName(string id)
        {
            var v = Voices.FirstOrDefault(x => x[0] == id);
            return v != null ? v[1] : id;
        }

        public static HttpRequestSpec Build(string key, string voiceId, string model, string text)
        {
            var body = new Dictionary<string, object>
            {
                { "text", text ?? "" },
                { "model_id", string.IsNullOrEmpty(model) ? DefaultModel : model },
                { "voice_settings", new Dictionary<string, object> { { "stability", 0.45 }, { "similarity_boost", 0.75 }, { "style", 0.2 } } },
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
