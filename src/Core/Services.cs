using System;
using System.Collections.Generic;
using System.Linq;

namespace KerbalNews
{
    /// <summary>
    /// One company or server the mod can call. Each has one key (or, for a local server, one address), entered once on
    /// the Settings page, however many jobs it does: a Google key writes news, looks at photos and draws article photos.
    /// </summary>
    public class AiService
    {
        public string Id, Name;
        /// <summary>What the key is used for, and where to get one. Shown on the service's card.</summary>
        public string Does = "", Where = "", KeyHint = "";
        public bool NeedsKey = true;
        /// <summary>The text provider preset (Providers) and image preset (ImageProviders) this service's key unlocks. "" = none.</summary>
        public string TextId = "", ImageId = "";
        public bool Voice;
        /// <summary>The key can be checked with a request that costs nothing.</summary>
        public bool FreeCheck = true;
        /// <summary>A server on the player's own computer or network: it has an address instead of a key.</summary>
        public bool Local;
        /// <summary>Shown under "More services" rather than up front.</summary>
        public bool Minor;
    }

    /// <summary>What the last check or call said about a service's key. Kept for the session, never saved.</summary>
    public class ServiceStatus
    {
        public bool Ok;
        public string Message = "";
    }

    /// <summary>A job's pick: which provider, address, key and model to call.</summary>
    public class AiRoute
    {
        public ProviderPreset Preset;
        public string BaseUrl = "", Key = "", Model = "";
        public bool Ready { get { return Preset != null && (!Preset.NeedsKey || Key != ""); } }
        public bool Sees { get { return Providers.CanSee(Preset, Model); } }
        /// <summary>"deepseek / deepseek-chat": how a story records what wrote it.</summary>
        public string Label { get { return Preset.Id + " / " + Model; } }
    }

    public static class Services
    {
        public const string ElevenLabs = "elevenlabs";
        public const string Fal = "fal";
        /// <summary>The player's own image server (the "custom" image preset), kept apart from their own text server.</summary>
        public const string ImageServer = "custom-images";
        public const int MaxKeyLength = 300;

        public static readonly List<AiService> All = new List<AiService>();

        static Services()
        {
            // Every text provider is a service under the same id, so a new provider preset needs no second list.
            foreach (var p in Providers.All)
                All.Add(new AiService { Id = p.Id, Name = p.Name, KeyHint = p.KeyHint, NeedsKey = p.NeedsKey, TextId = p.Id, Local = !p.NeedsKey });
            Describe("anthropic", "Writes news and looks at photos. The best writing; costs more than the others.", "platform.claude.com, under \"API keys\"");
            Describe("openai", "Writes news, looks at photos, draws article photos.", "platform.openai.com, under \"API keys\"", "openai");
            Describe("gemini", "Writes news, looks at photos and the stream, draws article photos.", "aistudio.google.com, under \"Get API key\"", "gemini");
            Describe("xai", "Writes news, looks at photos, draws article photos.", "console.x.ai, under \"API keys\"", "xai");
            Describe("mistral", "Writes news. Some of its models can look at photos.", "console.mistral.ai, under \"API keys\"");
            Describe("deepseek", "Writes news, comments, chat and mail. Cheap. Cannot look at pictures.", "platform.deepseek.com, under \"API keys\"");
            Describe("openrouter", "One key for many companies' models, text only here.", "openrouter.ai, under \"Keys\"");
            Describe("ollama", "Free text models on your own computer.", "Start Ollama before the game");
            Describe("lmstudio", "Free text models on your own computer.", "Start LM Studio's local server before the game");
            Describe("llamacpp", "Free text models on your own computer.", "Start llama-server before the game");
            Describe("koboldcpp", "Free text models on your own computer.", "Start KoboldCpp before the game");
            Describe("jan", "Free text models on your own computer.", "Start Jan's local API server before the game");
            Describe("custom", "Any text server that speaks the OpenAI chat format.", "your server");
            Get("mistral").Minor = Get("openrouter").Minor = true;
            All.Add(new AiService { Id = Fal, Name = "fal", Does = "Draws article photos (FLUX). Pictures only.", Where = "fal.ai, under Dashboard > API keys", ImageId = "fal", FreeCheck = false, Minor = true });
            All.Add(new AiService { Id = ElevenLabs, Name = "ElevenLabs", Does = "The livestream announcer and flight controller speak out loud.", Where = "elevenlabs.io, under your profile > API keys", KeyHint = "sk_...", Voice = true });
            All.Add(new AiService { Id = ImageServer, Name = "Your own image server", Does = "Draws article photos through any server that speaks the OpenAI images format.", Where = "your server", ImageId = "custom", NeedsKey = false, Local = true });
        }

        static void Describe(string id, string does, string where, string imageId = "")
        {
            var s = Get(id);
            if (s == null) return;
            s.Does = does; s.Where = where; s.ImageId = imageId;
        }

        public static AiService Get(string id) { return All.FirstOrDefault(s => s.Id == id); }

        /// <summary>The service whose key a text provider uses (the same id).</summary>
        public static string ForText(string providerId) { return providerId ?? ""; }

        /// <summary>The service whose key an image preset uses.</summary>
        public static string ForImage(string imageId)
        {
            var s = All.FirstOrDefault(x => x.ImageId == imageId && imageId != "");
            return s != null ? s.Id : "";
        }

        /// <summary>
        /// Tidies a pasted key. False with a reason when it cannot be a key: pasted with spaces or a line break in the
        /// middle (usually a second thing copied along with it), or far too long.
        /// </summary>
        public static bool CleanKey(string raw, out string key, out string error)
        {
            key = (raw ?? "").Trim().Trim('"', '\'').Trim();
            error = null;
            if (key.Length > MaxKeyLength) { error = "That key is too long. Copy the key again, on its own."; return false; }
            if (key.Any(char.IsWhiteSpace)) { error = "An API key has no spaces in it. Copy the key again, on its own."; return false; }
            return true;
        }

        /// <summary>The last four characters, for "Key ends in ...". Never more, and nothing for a very short key.</summary>
        public static string Tail(string key)
        {
            return string.IsNullOrEmpty(key) || key.Length < 12 ? "" : key.Substring(key.Length - 4);
        }

        /// <summary>A request that checks a service's key (or address) and costs nothing, or null when the service has none.</summary>
        public static HttpRequestSpec BuildCheck(AiService s, GlobalSettings g)
        {
            if (s == null || !s.FreeCheck) return null;
            string key = g.KeyOf(s.Id);
            HttpRequestSpec req;
            if (s.Voice)
            {
                req = new HttpRequestSpec { Url = Voice.BaseUrl + "/user", Method = "GET", Body = null };
                req.Headers["xi-api-key"] = key;
            }
            else if (s.TextId != "")
            {
                var p = Providers.Get(s.TextId);
                req = Providers.BuildListModels(p, g.BaseUrlOf(p.Id), key);
            }
            else if (s.Id == ImageServer)
            {
                var p = ImageProviders.Get("custom");
                string v;
                string root = g.ImageBaseUrls.TryGetValue(p.Id, out v) && !string.IsNullOrEmpty(v) ? v : p.BaseUrl;
                req = new HttpRequestSpec { Url = root.TrimEnd('/') + "/models", Method = "GET", Body = null };
                if (key != "") req.Headers["Authorization"] = "Bearer " + key;
            }
            else return null;
            req.TimeoutSeconds = 15;
            return req;
        }

        /// <summary>
        /// Reads the answer to BuildCheck. Returns false when the answer says nothing about the key (the network was
        /// down, the service had a bad moment), so an earlier Working mark is not thrown away over it.
        /// </summary>
        public static bool ReadCheck(AiService s, HttpResult r, out bool ok, out string message)
        {
            ok = false;
            string what = s.Local ? "address" : "key";
            if (r.Error != null) { message = s.Local ? "Nothing answered at that address. " + s.Where + "." : "Could not connect: " + r.Error; return s.Local; }
            if (r.Ok)
            {
                ok = true;
                message = s.Local ? "The server answered." : "The key works.";
                if (s.TextId != "")
                {
                    int n = Providers.ParseModelList(Providers.Get(s.TextId).Kind, r.Body).Count;
                    if (n > 0) message += " " + n + " models available.";
                    else if (s.Local) { ok = false; message = "The server answered, but it has no models loaded."; }
                }
                return true;
            }
            // ElevenLabs keys can be limited to parts of its API. A real key that may not read the account page is
            // answered "missing_permissions"; a wrong key is answered "invalid_api_key".
            if (s.Voice && (r.Status == 401 || r.Status == 403) && (r.Body ?? "").IndexOf("missing_permission", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                ok = true;
                message = "The key works. It is a limited key, which is fine as long as it allows Text to Speech, Sound Effects and reading Voices.";
                return true;
            }
            if (r.Status == 400 || r.Status == 401 || r.Status == 403) { message = "The " + what + " was refused (" + r.Status + "). Check that it was pasted correctly."; return true; }
            if (r.Status == 404) { message = "That address did not answer as expected (404). Check the address under More options."; return true; }
            message = "The service answered with an error (" + r.Status + "). Try again later.";
            return false;
        }
    }
}
