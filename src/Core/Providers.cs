using System;
using System.Collections.Generic;
using System.Linq;

namespace KerbalNews
{
    public enum ApiKind { Anthropic, OpenAICompatible, Gemini }

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
    }

    public class HttpRequestSpec
    {
        public string Url;
        public string Method = "POST";
        public Dictionary<string, string> Headers = new Dictionary<string, string>();
        public string Body;
        public int TimeoutSeconds = 120;
    }

    public class HttpResult
    {
        public int Status;
        public string Body = "";
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
            new ProviderPreset { Id = "ollama", Name = "Ollama (local)", Kind = ApiKind.OpenAICompatible, BaseUrl = "http://localhost:11434/v1",
                WriterModel = "llama3.1", CheapModel = "llama3.1", NeedsKey = false, JsonMode = true },
            new ProviderPreset { Id = "lmstudio", Name = "LM Studio (local)", Kind = ApiKind.OpenAICompatible, BaseUrl = "http://localhost:1234/v1",
                WriterModel = "local-model", CheapModel = "local-model", NeedsKey = false, JsonMode = false },
            new ProviderPreset { Id = "custom", Name = "Custom (OpenAI-compatible)", Kind = ApiKind.OpenAICompatible, BaseUrl = "http://localhost:8000/v1",
                WriterModel = "model", CheapModel = "model", NeedsKey = false, JsonMode = false },
        };

        public static ProviderPreset Get(string id)
        {
            return All.FirstOrDefault(p => p.Id == id) ?? All[0];
        }

        public static HttpRequestSpec Build(ProviderPreset p, string baseUrl, string apiKey, string model,
            string system, string user, int maxTokens, bool jsonMode)
        {
            var req = new HttpRequestSpec();
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
                        { "messages", new List<object> { new Dictionary<string, object> { { "role", "user" }, { "content", user } } } },
                    };
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
                            { "parts", new List<object> { new Dictionary<string, object> { { "text", user } } } } } } },
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
                            new Dictionary<string, object> { { "role", "user" }, { "content", user } } } },
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
                string msg = Json.Path(root, "error.message") as string ?? Json.Path(root, "0.error.message") as string ?? Truncate(res.Body, 200);
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
                default:
                    text = Json.Path(root, "choices.0.message.content") as string;
                    break;
            }
            if (string.IsNullOrEmpty(text)) { error = "Provider response had no text"; return false; }
            return true;
        }

        static string Truncate(string s, int n) { return s == null ? "" : (s.Length <= n ? s : s.Substring(0, n) + "..."); }
    }
}
