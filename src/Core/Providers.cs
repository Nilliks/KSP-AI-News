using System;
using System.Collections.Generic;
using System.Linq;

namespace KerbalNews
{
    public enum ApiKind { Anthropic, OpenAICompatible, Gemini, Ollama }

    public class ProviderPreset
    {
        public string Id;
        public string Name;
        public ApiKind Kind;
        public string BaseUrl;
        public string WriterModel;
        public string CheapModel;
        public bool NeedsKey = true;
        public bool JsonMode = true;
        public string KeyHint = "";
        /// <summary>Suggested model ids shown in settings. The live list can be fetched from the provider.</summary>
        public List<string> Models = new List<string>();
    }

    public class HttpRequestSpec
    {
        public string Url;
        public string Method = "POST";
        public Dictionary<string, string> Headers = new Dictionary<string, string>();
        public string Body;
        public int TimeoutSeconds = 120;
        /// <summary>For the spending count: the feature that asked, and the text provider and model asked (see UsageMeter).</summary>
        public string Feature = "", ProviderId = "", Model = "";
        /// <summary>For a request no text provider built (a picture): the service it goes to.</summary>
        public string Service = "";
    }

    public class HttpResult
    {
        public int Status;
        public string Body = "";
        /// <summary>The raw reply, for binary answers such as speech audio.</summary>
        public byte[] Bytes;
        public string Error; // transport-level error (DNS, TLS, timeout)
        public bool Ok { get { return Error == null && Status >= 200 && Status < 300; } }
    }

    /// <summary>Sends one HTTP request and calls back exactly once. The KSP layer uses UnityWebRequest.</summary>
    public interface IHttpTransport
    {
        void Send(HttpRequestSpec request, Action<HttpResult> done);
    }

    /// <summary>
    /// Every supported provider maps onto one of three wire formats. Most providers speak the
    /// OpenAI chat-completions format, so adding one is a new preset, not new code.
    /// Model names are defaults the player can change in settings.
    /// </summary>
    public static class Providers
    {
        public static readonly List<ProviderPreset> All = new List<ProviderPreset>
        {
            new ProviderPreset { Id = "anthropic", Name = "Anthropic (Claude)", Kind = ApiKind.Anthropic, BaseUrl = "https://api.anthropic.com/v1",
                WriterModel = "claude-sonnet-5-5", CheapModel = "claude-haiku-5-5", KeyHint = "sk-ant-..." },
            new ProviderPreset { Id = "openai", Name = "OpenAI (GPT)", Kind = ApiKind.OpenAICompatible, BaseUrl = "https://api.openai.com/v1",
                WriterModel = "gpt-5", CheapModel = "gpt-5-mini", KeyHint = "sk-..." },
            new ProviderPreset { Id = "gemini", Name = "Google (Gemini)", Kind = ApiKind.Gemini, BaseUrl = "https://generativelanguage.googleapis.com/v1beta",
                WriterModel = "gemini-2.5-pro", CheapModel = "gemini-2.5-flash", KeyHint = "AIza..." },
            new ProviderPreset { Id = "xai", Name = "xAI (Grok)", Kind = ApiKind.OpenAICompatible, BaseUrl = "https://api.x.ai/v1",
                WriterModel = "grok-4", CheapModel = "grok-3-mini", KeyHint = "xai-..." },
            new ProviderPreset { Id = "mistral", Name = "Mistral", Kind = ApiKind.OpenAICompatible, BaseUrl = "https://api.mistral.ai/v1",
                WriterModel = "mistral-large-latest", CheapModel = "mistral-small-latest" },
            new ProviderPreset { Id = "deepseek", Name = "DeepSeek", Kind = ApiKind.OpenAICompatible, BaseUrl = "https://api.deepseek.com",
                WriterModel = "deepseek-chat", CheapModel = "deepseek-chat" },
            new ProviderPreset { Id = "openrouter", Name = "OpenRouter", Kind = ApiKind.OpenAICompatible, BaseUrl = "https://openrouter.ai/api/v1",
                WriterModel = "anthropic/claude-sonnet-5.5", CheapModel = "anthropic/claude-haiku-5.5", KeyHint = "sk-or-..." },
            // Ollama's own API, not its OpenAI-compatible one: only the native API lets us raise the context window, which
            // otherwise silently cuts off the start of long prompts (the instructions).
            new ProviderPreset { Id = "ollama", Name = "Ollama (local)", Kind = ApiKind.Ollama, BaseUrl = "http://localhost:11434",
                WriterModel = "qwen2.5:14b", CheapModel = "qwen2.5:7b", NeedsKey = false, JsonMode = true },
            new ProviderPreset { Id = "lmstudio", Name = "LM Studio (local)", Kind = ApiKind.OpenAICompatible, BaseUrl = "http://localhost:1234/v1",
                WriterModel = "local-model", CheapModel = "local-model", NeedsKey = false, JsonMode = false },
            new ProviderPreset { Id = "llamacpp", Name = "llama.cpp server (local)", Kind = ApiKind.OpenAICompatible, BaseUrl = "http://localhost:8080/v1",
                WriterModel = "local-model", CheapModel = "local-model", NeedsKey = false, JsonMode = false },
            new ProviderPreset { Id = "koboldcpp", Name = "KoboldCpp (local)", Kind = ApiKind.OpenAICompatible, BaseUrl = "http://localhost:5001/v1",
                WriterModel = "local-model", CheapModel = "local-model", NeedsKey = false, JsonMode = false },
            new ProviderPreset { Id = "jan", Name = "Jan (local)", Kind = ApiKind.OpenAICompatible, BaseUrl = "http://localhost:1337/v1",
                WriterModel = "local-model", CheapModel = "local-model", NeedsKey = false, JsonMode = false },
            new ProviderPreset { Id = "custom", Name = "Custom (OpenAI-compatible)", Kind = ApiKind.OpenAICompatible, BaseUrl = "http://localhost:8000/v1",
                WriterModel = "model", CheapModel = "model", NeedsKey = false, JsonMode = false },
        };

        static Providers()
        {
            Suggest("anthropic", "claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5", "claude-fable-5-1");
            Suggest("openai", "gpt-5", "gpt-5-mini", "gpt-5-nano", "gpt-4.1", "gpt-4.1-mini", "gpt-4o-mini");
            Suggest("gemini", "gemini-3.7-flash", "gemini-2.5-pro", "gemini-2.5-flash", "gemini-2.5-flash-lite");
            Suggest("xai", "grok-4", "grok-3", "grok-3-mini");
            Suggest("mistral", "mistral-large-latest", "mistral-medium-latest", "mistral-small-latest");
            Suggest("deepseek", "deepseek-chat", "deepseek-reasoner");
            Suggest("openrouter", "anthropic/claude-sonnet-5.5", "openai/gpt-5-mini", "google/gemini-2.5-flash", "deepseek/deepseek-chat");
            Suggest("ollama", "qwen2.5:14b", "qwen2.5:7b", "llama3.1:8b", "mistral-nemo", "gemma3:12b");
        }

        /// <summary>Context window (tokens) asked of a local Ollama model. Ollama's default is small enough to cut off our prompts.</summary>
        public static int LocalContext = 16384;

        /// <summary>True when the URL points at this computer or the home network: a local model server.</summary>
        public static bool IsLocal(string url)
        {
            Uri u;
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            string h = u.Host.ToLowerInvariant();
            if (h == "localhost" || h == "::1" || h == "[::1]" || h.EndsWith(".local") || h.StartsWith("127.") || h.StartsWith("10.") || h.StartsWith("192.168.")) return true;
            if (h.StartsWith("172."))
            {
                var parts = h.Split('.'); int b;
                if (parts.Length == 4 && int.TryParse(parts[1], out b) && b >= 16 && b <= 31) return true;
            }
            return false;
        }

        /// <summary>Ollama's address without a trailing /v1 (older settings saved the OpenAI-compatible one).</summary>
        static string OllamaRoot(string root)
        {
            root = root.TrimEnd('/');
            return root.EndsWith("/v1") ? root.Substring(0, root.Length - 3) : root;
        }

        /// <summary>Reasoning models (Qwen3, DeepSeek-R1...) think out loud in think tags before answering; keep only the answer.</summary>
        public static string StripThinking(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            foreach (var tag in new[] { "think", "thinking", "reasoning" })
            {
                string open = "<" + tag + ">", close = "</" + tag + ">";
                int a;
                while ((a = text.IndexOf(open, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    int b = text.IndexOf(close, a, StringComparison.OrdinalIgnoreCase);
                    if (b < 0) { text = text.Substring(0, a); break; }   // cut off mid-thought: nothing usable after it
                    text = text.Substring(0, a) + text.Substring(b + close.Length);
                }
                // Some servers drop the opening tag and send only the close.
                int c = text.IndexOf(close, StringComparison.OrdinalIgnoreCase);
                if (c >= 0) text = text.Substring(c + close.Length);
            }
            return text.Trim();
        }

        static void Suggest(string id, params string[] models)
        {
            var p = All.FirstOrDefault(x => x.Id == id);
            if (p != null) p.Models.AddRange(models);
        }

        /// <summary>A request that lists the provider's available models.</summary>
        public static HttpRequestSpec BuildListModels(ProviderPreset p, string baseUrl, string apiKey)
        {
            var req = new HttpRequestSpec { Method = "GET", TimeoutSeconds = 30 };
            string root = (string.IsNullOrEmpty(baseUrl) ? p.BaseUrl : baseUrl).TrimEnd('/');
            req.Url = root + "/models" + (p.Kind == ApiKind.Gemini ? "?pageSize=200" : "");
            switch (p.Kind)
            {
                case ApiKind.Ollama:
                    req.Url = OllamaRoot(root) + "/api/tags";
                    break;
                case ApiKind.Anthropic:
                    req.Url = root + "/models?limit=100";
                    req.Headers["x-api-key"] = apiKey ?? "";
                    req.Headers["anthropic-version"] = "2023-06-01";
                    break;
                case ApiKind.Gemini:
                    req.Headers["x-goog-api-key"] = apiKey ?? "";
                    break;
                default:
                    if (!string.IsNullOrEmpty(apiKey)) req.Headers["Authorization"] = "Bearer " + apiKey;
                    break;
            }
            return req;
        }

        public static List<string> ParseModelList(ApiKind kind, string body)
        {
            var list = new List<string>();
            object root;
            if (!Json.TryParse(body, out root)) return list;
            if (kind == ApiKind.Ollama)
            {
                foreach (var m in Json.Arr(Json.Get(root, "models")) ?? new List<object>())
                {
                    var name = Json.Str(m, "name", Json.Str(m, "model", ""));
                    if (name != "" && name.IndexOf("embed", StringComparison.OrdinalIgnoreCase) < 0) list.Add(name);
                }
            }
            else if (kind == ApiKind.Gemini)
            {
                foreach (var m in Json.Arr(Json.Get(root, "models")) ?? new List<object>())
                {
                    var methods = Json.Arr(Json.Get(m, "supportedGenerationMethods"));
                    if (methods != null && !methods.OfType<string>().Contains("generateContent")) continue;
                    var name = Json.Str(m, "name", "");
                    if (name.StartsWith("models/")) name = name.Substring(7);
                    if (name != "") list.Add(name);
                }
            }
            else
            {
                foreach (var m in Json.Arr(Json.Get(root, "data")) ?? new List<object>())
                {
                    var id = Json.Str(m, "id", "");
                    if (id == "") continue;
                    // Skip models that cannot write text (embeddings, images, audio, moderation).
                    var l = id.ToLowerInvariant();
                    if (l.Contains("embed") || l.Contains("whisper") || l.Contains("tts") || l.Contains("dall-e") || l.Contains("image") || l.Contains("moderation") || l.Contains("audio") || l.Contains("realtime") || l.Contains("transcribe")) continue;
                    list.Add(id);
                }
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list.Distinct().ToList();
        }

        public static ProviderPreset Get(string id)
        {
            return All.FirstOrDefault(p => p.Id == id) ?? All[0];
        }

        /// <summary>
        /// Whether a model can look at pictures, from its name (vision isn't something the APIs report).
        /// A model that turns out blind is remembered for the session (see MarkBlind).
        /// </summary>
        public static bool CanSee(ProviderPreset p, string model)
        {
            if (p == null || string.IsNullOrEmpty(model)) return false;
            string m = model.ToLowerInvariant();
            if (Blind.Contains(p.Id + "|" + m)) return false;
            switch (p.Id)
            {
                case "anthropic": return !m.Contains("claude-2") && !m.Contains("instant");
                case "gemini": return !m.Contains("embedding") && !m.Contains("aqa");
                case "openai": return !m.Contains("gpt-3.5") && !m.StartsWith("o1-mini") && !m.Contains("o3-mini") && !m.Contains("instruct");
                case "deepseek": return false;
                case "xai": return m.Contains("vision") || m.Contains("grok-4") || m.Contains("grok-5");
                case "mistral": return m.Contains("pixtral") || m.Contains("medium") || m.Contains("small-3") || m.Contains("small-latest");
            }
            // OpenRouter, local servers and custom endpoints: go by the model's name.
            foreach (var k in new[] { "claude", "gpt-4o", "gpt-4.1", "gpt-5", "gemini", "gemma3", "gemma-3", "llava", "vision", "-vl", "vl-", "pixtral", "llama-4", "llama4", "minicpm-v", "moondream", "qwen2.5vl", "qwen3-vl", "grok-4" })
                if (m.Contains(k)) return true;
            return false;
        }

        static readonly HashSet<string> Blind = new HashSet<string>();
        /// <summary>Forget which models refused pictures (a new game session, or a test).</summary>
        public static void ClearBlind() { Blind.Clear(); }

        /// <summary>The provider refused a picture: stop sending this model any, for this session.</summary>
        public static void MarkBlind(ProviderPreset p, string model) { if (p != null && model != null) Blind.Add(p.Id + "|" + model.ToLowerInvariant()); }

        /// <summary>Whether a failed reply looks like the model refusing an image.</summary>
        public static bool RefusedImage(HttpResult r)
        {
            if (r == null || r.Error != null || r.Status < 400 || r.Status >= 500) return false;
            string b = (r.Body ?? "").ToLowerInvariant();
            return b.Contains("image") || b.Contains("vision") || b.Contains("multimodal") || b.Contains("content type") || b.Contains("image_url");
        }

        public static HttpRequestSpec Build(ProviderPreset p, string baseUrl, string apiKey, string model,
            string system, string user, int maxTokens, bool jsonMode)
        {
            return Build(p, baseUrl, apiKey, model, system, user, maxTokens, jsonMode, null);
        }

        /// <summary>As Build, with an optional JPEG the model looks at alongside the text.</summary>
        public static HttpRequestSpec Build(ProviderPreset p, string baseUrl, string apiKey, string model,
            string system, string user, int maxTokens, bool jsonMode, byte[] jpeg)
        {
            string b64 = jpeg != null && jpeg.Length > 0 ? Convert.ToBase64String(jpeg) : null;
            var req = new HttpRequestSpec { ProviderId = p.Id, Model = model ?? "" };
            string root = (string.IsNullOrEmpty(baseUrl) ? p.BaseUrl : baseUrl).TrimEnd('/');
            req.Headers["Content-Type"] = "application/json";
            switch (p.Kind)
            {
                case ApiKind.Anthropic:
                {
                    req.Url = root + "/messages";
                    req.Headers["x-api-key"] = apiKey ?? "";
                    req.Headers["anthropic-version"] = "2023-06-01";
                    var body = new Dictionary<string, object>
                    {
                        { "model", model },
                        { "max_tokens", maxTokens },
                        // The system prompt is the same for every call in a save, so cache it.
                        { "system", new List<object> { new Dictionary<string, object> {
                            { "type", "text" }, { "text", system },
                            { "cache_control", new Dictionary<string, object> { { "type", "ephemeral" } } } } } },
                        { "messages", new List<object> { new Dictionary<string, object> { { "role", "user" }, { "content", b64 == null ? (object)user : new List<object> {
                            new Dictionary<string, object> { { "type", "image" }, { "source", new Dictionary<string, object> { { "type", "base64" }, { "media_type", "image/jpeg" }, { "data", b64 } } } },
                            new Dictionary<string, object> { { "type", "text" }, { "text", user } } } } } } },
                    };
                    req.Body = Json.Write(body);
                    break;
                }
                case ApiKind.Ollama:
                {
                    req.Url = OllamaRoot(root) + "/api/chat";
                    var body = new Dictionary<string, object>
                    {
                        { "model", model },
                        { "stream", false },
                        { "messages", new List<object> {
                            new Dictionary<string, object> { { "role", "system" }, { "content", system } },
                            b64 == null ? new Dictionary<string, object> { { "role", "user" }, { "content", user } }
                                : new Dictionary<string, object> { { "role", "user" }, { "content", user }, { "images", new List<object> { b64 } } } } },
                        { "options", new Dictionary<string, object> { { "num_ctx", LocalContext }, { "num_predict", maxTokens } } },
                    };
                    if (jsonMode) body["format"] = "json";
                    req.Body = Json.Write(body);
                    break;
                }
                case ApiKind.Gemini:
                {
                    req.Url = root + "/models/" + Uri.EscapeDataString(model) + ":generateContent";
                    req.Headers["x-goog-api-key"] = apiKey ?? "";
                    var gen = new Dictionary<string, object> { { "maxOutputTokens", Math.Max(maxTokens, 16000) } };
                    if (jsonMode) gen["responseMimeType"] = "application/json";
                    var body = new Dictionary<string, object>
                    {
                        { "systemInstruction", new Dictionary<string, object> { { "parts", new List<object> { new Dictionary<string, object> { { "text", system } } } } } },
                        { "contents", new List<object> { new Dictionary<string, object> {
                            { "role", "user" },
                            { "parts", b64 == null ? new List<object> { new Dictionary<string, object> { { "text", user } } }
                                : new List<object> { new Dictionary<string, object> { { "inline_data", new Dictionary<string, object> { { "mime_type", "image/jpeg" }, { "data", b64 } } } }, new Dictionary<string, object> { { "text", user } } } } } } },
                        { "generationConfig", gen },
                    };
                    req.Body = Json.Write(body);
                    break;
                }
                default:
                {
                    req.Url = root + "/chat/completions";
                    if (!string.IsNullOrEmpty(apiKey)) req.Headers["Authorization"] = "Bearer " + apiKey;
                    if (p.Id == "openrouter")
                    {
                        req.Headers["X-Title"] = "Kerbal News Network";
                    }
                    var body = new Dictionary<string, object>
                    {
                        { "model", model },
                        { "messages", new List<object> {
                            new Dictionary<string, object> { { "role", "system" }, { "content", system } },
                            new Dictionary<string, object> { { "role", "user" }, { "content", b64 == null ? (object)user : new List<object> {
                                new Dictionary<string, object> { { "type", "text" }, { "text", user } },
                                new Dictionary<string, object> { { "type", "image_url" }, { "image_url", new Dictionary<string, object> { { "url", "data:image/jpeg;base64," + b64 }, { "detail", "low" } } } } } } } } },
                    };
                    // OpenAI's newer models reject max_tokens; everyone else still expects it.
                    if (p.Id == "openai") body["max_completion_tokens"] = maxTokens;
                    else body["max_tokens"] = maxTokens;
                    if (jsonMode) body["response_format"] = new Dictionary<string, object> { { "type", "json_object" } };
                    req.Body = Json.Write(body);
                    break;
                }
            }
            return req;
        }

        /// <summary>Pulls the model's text out of a provider response, or returns an error message.</summary>
        public static bool TryExtractText(ApiKind kind, HttpResult res, out string text, out string error)
        {
            text = null;
            error = null;
            if (res.Error != null) { error = "Network error: " + res.Error; return false; }
            object root;
            Json.TryParse(res.Body, out root);
            if (!res.Ok)
            {
                string msg = Json.Path(root, "error.message") as string ?? Json.Path(root, "0.error.message") as string ?? Json.Get(root, "error") as string ?? Truncate(res.Body, 200);
                error = "HTTP " + res.Status + ": " + msg;
                return false;
            }
            if (root == null) { error = "Provider returned non-JSON: " + Truncate(res.Body, 200); return false; }
            switch (kind)
            {
                case ApiKind.Anthropic:
                {
                    var content = Json.Arr(Json.Get(root, "content"));
                    if (content != null)
                        text = string.Concat(content.Where(c => Json.Str(c, "type") == "text").Select(c => Json.Str(c, "text", "")).ToArray());
                    break;
                }
                case ApiKind.Gemini:
                {
                    var parts = Json.Arr(Json.Path(root, "candidates.0.content.parts"));
                    if (parts != null) text = string.Concat(parts.Select(pt => Json.Str(pt, "text", "")).ToArray());
                    if (string.IsNullOrEmpty(text))
                    {
                        string reason = Json.Path(root, "candidates.0.finishReason") as string ?? Json.Path(root, "promptFeedback.blockReason") as string;
                        if (reason != null) { error = "Gemini returned no text (" + reason + ")"; return false; }
                    }
                    break;
                }
                case ApiKind.Ollama:
                    text = Json.Path(root, "message.content") as string;
                    break;
                default:
                    text = Json.Path(root, "choices.0.message.content") as string;
                    break;
            }
            text = StrayScript(StripThinking(text));
            if (string.IsNullOrEmpty(text)) { error = "Provider response had no text"; return false; }
            return true;
        }

        /// <summary>
        /// Some models slip a word of Chinese, Japanese or Korean into English text ("The累计 time"), which the game's
        /// font can't show. When such characters are only a stray few in a Latin-script reply, they are taken out.
        /// A reply written mostly in one of those scripts is left alone.
        /// </summary>
        public static string StrayScript(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            int cjk = 0, latin = 0;
            foreach (char c in text)
            {
                if (IsCjk(c)) cjk++;
                else if (c < 0x250 && char.IsLetter(c)) latin++;
            }
            if (cjk == 0 || cjk * 3 > latin) return text;
            var sb = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (!IsCjk(c)) { sb.Append(c); continue; }
                // Keep words apart where a run sat between two letters ("The累计time" -> "The time").
                int j = i; while (j + 1 < text.Length && IsCjk(text[j + 1])) j++;
                bool before = sb.Length > 0 && char.IsLetterOrDigit(sb[sb.Length - 1]);
                bool after = j + 1 < text.Length && char.IsLetterOrDigit(text[j + 1]);
                if (before && after) sb.Append(' ');
                i = j;
            }
            return sb.ToString().Replace("  ", " ").Replace(" ,", ",").Replace(" .", ".");
        }

        static bool IsCjk(char c)
        {
            return (c >= 0x3040 && c <= 0x30FF) || (c >= 0x3400 && c <= 0x4DBF) || (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0xAC00 && c <= 0xD7AF)
                || (c >= 0xF900 && c <= 0xFAFF) || (c >= 0xFF00 && c <= 0xFFEF && !(c >= 0xFF01 && c <= 0xFF5E)) || (c >= 0x3000 && c <= 0x303F);
        }

        static string Truncate(string s, int n) { return s == null ? "" : (s.Length <= n ? s : s.Substring(0, n) + "..."); }
    }
}
