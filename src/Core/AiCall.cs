using System;
using System.Collections.Generic;

namespace KerbalNews
{
    public partial class NewsEngine
    {
        /// <summary>
        /// One JSON request to the current provider: builds it, sends it, pulls out the text, and parses the JSON
        /// (repairing a truncated reply if it can). done gets the parsed object or null, an error, the raw text and the HTTP result.
        /// Callers keep their own retry rules.
        /// </summary>
        void AskJson(string model, string system, string user, int maxTokens, int timeoutSeconds, Action<object, string, string, HttpResult> done)
        {
            AskJson(model, system, user, maxTokens, timeoutSeconds, null, done);
        }

        /// <summary>As AskJson, with a picture for a model that can see. If the provider refuses it, the model is marked blind and the request goes again without it.</summary>
        void AskJson(string model, string system, string user, int maxTokens, int timeoutSeconds, byte[] jpeg, Action<object, string, string, HttpResult> done)
        {
            var preset = Settings.Preset;
            var req = Providers.Build(preset, Settings.BaseUrl, Settings.Key, model, system, user, Math.Min(Settings.MaxTokens, maxTokens), preset.JsonMode, jpeg);
            req.TimeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : Settings.TimeoutSeconds;
            Transport.Send(req, res =>
            {
                if (jpeg != null && Providers.RefusedImage(res))
                {
                    Providers.MarkBlind(preset, model);
                    AskJson(model, system, user, maxTokens, timeoutSeconds, null, done);
                    return;
                }
                string text, err;
                object root = null;
                if (Providers.TryExtractText(preset.Kind, res, out text, out err))
                {
                    var json = ResponseParser.ExtractJson(text) ?? "";
                    if (!Json.TryParse(json, out root) || Json.Obj(root) == null)
                    {
                        var fixedJson = ResponseParser.Repair(text);
                        if (fixedJson == null || !Json.TryParse(fixedJson, out root) || Json.Obj(root) == null) { root = null; err = "Reply was not JSON"; }
                    }
                }
                done(root, root == null ? (err ?? "No reply") : null, text ?? "", res);
            });
        }
    }
}
