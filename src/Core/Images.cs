using System;
using System.Collections.Generic;
using System.Linq;

namespace KerbalNews
{
    public enum ImageApiKind { OpenAIImages, Gemini, Fal }

    /// <summary>An image-generation service. Separate from the text provider, with its own key and model.</summary>
    public class ImagePreset
    {
        public string Id, Name, BaseUrl, Model, KeyHint = "";
        public ImageApiKind Kind;
        public List<string> Models = new List<string>();
    }

    /// <summary>
    /// Builds image requests and pulls the picture out of the reply. Every format returns the image inline
    /// (base64 or a data URI), so the plain-text transport is enough.
    /// </summary>
    public static class ImageProviders
    {
        public static readonly List<ImagePreset> All = new List<ImagePreset>
        {
            new ImagePreset { Id = "openai", Name = "OpenAI", Kind = ImageApiKind.OpenAIImages, BaseUrl = "https://api.openai.com/v1", Model = "gpt-image-1-mini", KeyHint = "sk-...",
                Models = { "gpt-image-1-mini", "gpt-image-1", "dall-e-3" } },
            new ImagePreset { Id = "gemini", Name = "Google Gemini", Kind = ImageApiKind.Gemini, BaseUrl = "https://generativelanguage.googleapis.com/v1beta", Model = "gemini-2.5-flash-image", KeyHint = "AIza...",
                Models = { "gemini-2.5-flash-image" } },
            new ImagePreset { Id = "xai", Name = "xAI Grok", Kind = ImageApiKind.OpenAIImages, BaseUrl = "https://api.x.ai/v1", Model = "grok-2-image", KeyHint = "xai-...",
                Models = { "grok-2-image" } },
            new ImagePreset { Id = "fal", Name = "fal", Kind = ImageApiKind.Fal, BaseUrl = "https://fal.run", Model = "fal-ai/flux/schnell",
                Models = { "fal-ai/flux/schnell", "fal-ai/flux/dev", "fal-ai/flux-pro/v1.1" } },
            new ImagePreset { Id = "custom", Name = "Custom (OpenAI-compatible)", Kind = ImageApiKind.OpenAIImages, BaseUrl = "http://localhost:8080/v1", Model = "" },
        };

        public static ImagePreset Get(string id) { return All.FirstOrDefault(p => p.Id == id); }

        /// <summary>House style sent with every picture, so the press photos look like one world.</summary>
        public const string Style = "Realistic press photograph for a newspaper, natural light, photojournalism. "
            + "The astronauts and people are kerbals: short, cheerful green humanoids with large round heads, wearing practical spacesuits or work clothes. "
            + "Rockets are boxy, practical and built from cylindrical stages. Do not put any text, captions, logos or watermarks in the image.";

        public static HttpRequestSpec Build(ImagePreset p, string baseUrl, string model, string key, string prompt)
        {
            string url = (string.IsNullOrEmpty(baseUrl) ? p.BaseUrl : baseUrl).TrimEnd('/');
            if (string.IsNullOrEmpty(model)) model = p.Model;
            string full = Style + "\n\nScene: " + prompt;
            var req = new HttpRequestSpec { TimeoutSeconds = 180 };
            req.Headers["Content-Type"] = "application/json";
            switch (p.Kind)
            {
                case ImageApiKind.Gemini:
                    req.Url = url + "/models/" + model + ":generateContent";
                    req.Headers["x-goog-api-key"] = key;
                    req.Body = Json.Write(new Dictionary<string, object>
                    {
                        { "contents", new List<object> { new Dictionary<string, object> { { "parts", new List<object> { new Dictionary<string, object> { { "text", full } } } } } } },
                        { "generationConfig", new Dictionary<string, object>
                            {
                                { "responseModalities", new List<object> { "IMAGE" } },
                                { "imageConfig", new Dictionary<string, object> { { "aspectRatio", "16:9" } } },
                            } },
                    });
                    break;
                case ImageApiKind.Fal:
                    req.Url = url + "/" + model;
                    req.Headers["Authorization"] = "Key " + key;
                    req.Body = Json.Write(new Dictionary<string, object>
                    {
                        { "prompt", full }, { "image_size", "landscape_16_9" }, { "num_images", 1 }, { "sync_mode", true }, { "output_format", "jpeg" },
                    });
                    break;
                default:
                    req.Url = url + "/images/generations";
                    if (!string.IsNullOrEmpty(key)) req.Headers["Authorization"] = "Bearer " + key;
                    var body = new Dictionary<string, object> { { "model", model }, { "prompt", full }, { "n", 1 } };
                    if (model.StartsWith("gpt-image")) { body["size"] = "1536x1024"; body["quality"] = "medium"; }
                    else if (model.StartsWith("dall-e")) { body["size"] = "1792x1024"; body["response_format"] = "b64_json"; }
                    else body["response_format"] = "b64_json";
                    req.Body = Json.Write(body);
                    break;
            }
            return req;
        }

        /// <summary>Gets the picture's bytes (PNG or JPEG) from a reply, or a readable error.</summary>
        public static bool TryExtract(ImageApiKind kind, HttpResult res, out byte[] bytes, out string error)
        {
            bytes = null;
            error = null;
            if (res.Error != null) { error = res.Error; return false; }
            object root;
            if (!Json.TryParse(res.Body ?? "", out root)) { error = "HTTP " + res.Status + ": reply was not JSON"; return false; }
            if (!res.Ok)
            {
                string msg = Json.Str(Json.Get(root, "error"), "message") ?? Json.Str(root, "detail") ?? Json.Str(root, "error");
                error = "HTTP " + res.Status + (msg != null ? ": " + msg : "");
                return false;
            }
            string b64 = null;
            switch (kind)
            {
                case ImageApiKind.Gemini:
                    var cand = Json.Arr(Json.Get(root, "candidates"));
                    var parts = cand != null && cand.Count > 0 ? Json.Arr(Json.Path(cand[0], "content.parts")) : null;
                    if (parts != null)
                        foreach (var part in parts)
                        {
                            var inline = Json.Get(part, "inlineData") ?? Json.Get(part, "inline_data");
                            if (inline != null) { b64 = Json.Str(inline, "data"); break; }
                        }
                    break;
                case ImageApiKind.Fal:
                    var imgs = Json.Arr(Json.Get(root, "images"));
                    if (imgs != null && imgs.Count > 0) b64 = Json.Str(imgs[0], "url");
                    break;
                default:
                    var data = Json.Arr(Json.Get(root, "data"));
                    if (data != null && data.Count > 0) b64 = Json.Str(data[0], "b64_json") ?? Json.Str(data[0], "url");
                    break;
            }
            if (string.IsNullOrEmpty(b64)) { error = "The reply had no image (the request may have been filtered)."; return false; }
            if (b64.StartsWith("data:"))
            {
                int comma = b64.IndexOf(',');
                b64 = comma > 0 ? b64.Substring(comma + 1) : "";
            }
            else if (b64.StartsWith("http")) { error = "The service returned a link instead of the image; this endpoint is not supported."; return false; }
            try { bytes = Convert.FromBase64String(b64); }
            catch (FormatException) { error = "The image data was not valid base64."; return false; }
            if (bytes.Length < 100) { error = "The image was empty."; return false; }
            return true;
        }

        public static string Extension(byte[] b)
        {
            return b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 ? ".jpg" : b.Length > 3 && b[0] == 'R' && b[1] == 'I' ? ".webp" : ".png";
        }

        static readonly string[] TierOrder = { "brief", "feature", "story", "major", "historic" };

        /// <summary>True when a story of this tier is big enough for a picture under the player's setting.</summary>
        public static bool TierQualifies(string tier, string minTier)
        {
            int t = Array.IndexOf(TierOrder, tier), m = Array.IndexOf(TierOrder, minTier);
            return t >= 0 && m >= 0 && t >= m;
        }
    }
}
