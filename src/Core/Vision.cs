using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>
    /// Letting the AI see. Each photo is looked at once and the description (GalleryShot.Look) is reused as text by
    /// every later article, comment section and Kerbopedia page, so a picture costs at most one look. The livestream
    /// sends a small camera frame with the chat batch at big moments. Only models that can see get pictures; a model
    /// that can't is skipped (or another provider looks, if the director set one); offline nothing is sent.
    /// </summary>
    public partial class NewsEngine
    {
        /// <summary>Set by the KSP layer: a gallery file as a small JPEG (about 512 px wide), or null.</summary>
        public Func<string, byte[]> PhotoJpeg;
        /// <summary>Set by the KSP layer: the livestream camera's current frame as a small JPEG, or null.</summary>
        public Func<byte[]> StreamFrameJpeg;

        bool lookBusy;
        double nextLookUT = -1;
        public double StreamFrameGapSeconds = 150;

        bool VisionAllowed { get { return Settings.PhotoLook && Settings.CanCallAi && Settings.AiWrites(GlobalSettings.AiMost); } }

        /// <summary>Whether this main-provider model can be sent the picture directly.</summary>
        bool MainSees(string model) { return VisionAllowed && Providers.CanSee(Settings.Preset, model); }

        /// <summary>The provider that looks at a picture on its own (main cheap model if it can see, else the helper), or null.</summary>
        ProviderPreset Looker(out string model, out string key, out string baseUrl)
        {
            model = key = baseUrl = "";
            if (!VisionAllowed) return null;
            if (Providers.CanSee(Settings.Preset, Settings.CheapModel)) { model = Settings.CheapModel; key = Settings.Key; baseUrl = Settings.BaseUrl; return Settings.Preset; }
            var h = Providers.Get(Settings.VisionProviderId);
            if (h == null || h.Id == Settings.Preset.Id) return null;
            string k;
            key = Settings.Keys.TryGetValue(h.Id, out k) ? k : "";
            if (h.NeedsKey && key == "") return null;
            model = h.CheapModel;
            if (!Providers.CanSee(h, model)) return null;
            return h;
        }

        public bool CanLook { get { string m, k, b; return Looker(out m, out k, out b) != null; } }

        GalleryShot ShotByRef(string shotRef)
        {
            if (string.IsNullOrEmpty(shotRef) || !shotRef.StartsWith("gallery:")) return null;
            string file = shotRef.Substring(8);
            return State.Gallery.FirstOrDefault(g => g.File == file);
        }

        byte[] JpegOf(GalleryShot g)
        {
            if (g == null || g.File == "" || PhotoJpeg == null) return null;
            try { return PhotoJpeg(g.File); } catch (Exception) { return null; }
        }

        /// <summary>Looks at one photo nobody has described yet: public ones and ones a pending story will run, newest first.</summary>
        void TickLooks(double ut, bool allowCalls)
        {
            if (lookBusy || !allowCalls || InSide() || ut < nextLookUT || PhotoJpeg == null) return;
            nextLookUT = ut + 0.05 * Day;
            string model, key, baseUrl;
            var p = Looker(out model, out key, out baseUrl);
            if (p == null) return;
            var wanted = new HashSet<string>(State.Stories.Where(s => s.Status == StoryStatus.Pending).Select(s => ShotFor(s)).Where(x => x != null));
            // A photo a pending story will run is left for that story's own call when the writer can see: no extra call.
            bool writerSees = MainSees(Settings.CheapModel) && MainSees(Settings.WriterModel);
            var g = State.Gallery.Where(x => x.Look == "" && x.LookTries < 2 && x.File != "" && !(writerSees && wanted.Contains("gallery:" + x.File)) && (x.Public || x.IsPress || wanted.Contains("gallery:" + x.File)))
                .OrderByDescending(x => wanted.Contains("gallery:" + x.File)).ThenByDescending(x => x.UT).FirstOrDefault();
            if (g == null) return;
            var jpeg = JpegOf(g);
            g.LookTries++;
            if (jpeg == null) return;
            var u = new StringBuilder();
            u.AppendLine("A photo from " + AgencyName + "'s space program in the world of Kerbal Space Program (KSP1)" + (g.IsPress ? ", taken by a press photographer" : "") + ".");
            if (g.Caption != "") u.AppendLine("Caption: " + g.Caption);
            if (g.VesselName != "") u.AppendLine("Craft: " + g.VesselName + (g.Situation != "" ? ", " + g.Situation.ToLowerInvariant() : "") + (g.Body != "" ? " at " + g.Body : "") + ".");
            u.AppendLine("Describe in two or three plain sentences what can actually be seen: the craft's shape, size and colours, the setting, the light, and anything striking or odd. In-universe: kerbals, never humans or a video game. Reply with JSON only: {\"look\":\"...\"}");
            lookBusy = true;
            AskWith(p, baseUrl, key, model, "You are the photo desk of a Kerbin newspaper. You describe pictures accurately and briefly.", u.ToString(), 300, jpeg, (root, err, text, res) =>
            {
                lookBusy = false;
                string look = root != null ? (Json.Str(root, "look", "") ?? "").Trim() : "";
                if (root == null && text != null && text.Trim() != "" && !text.Contains("{")) look = text.Trim();
                if (look != "" && !ResponseParser.ContainsForbidden(look)) g.Look = ResponseParser.Clip(look, 600);
                else if (err != null) LastError = "Photo desk: " + err;
            });
        }

        /// <summary>One JSON request to a given provider, with an optional picture. A provider that refuses the picture is marked blind.</summary>
        void AskWith(ProviderPreset p, string baseUrl, string key, string model, string system, string user, int maxTokens, byte[] jpeg, Action<object, string, string, HttpResult> done)
        {
            var req = Providers.Build(p, baseUrl, key, model, system, user, Math.Min(Settings.MaxTokens, maxTokens), p.JsonMode, jpeg);
            req.TimeoutSeconds = Settings.TimeoutSeconds;
            Transport.Send(req, res =>
            {
                if (jpeg != null && Providers.RefusedImage(res)) Providers.MarkBlind(p, model);
                string text, err;
                object root = null;
                if (Providers.TryExtractText(p.Kind, res, out text, out err))
                {
                    var json = ResponseParser.ExtractJson(text) ?? "";
                    if (!Json.TryParse(json, out root) || Json.Obj(root) == null) { var f = ResponseParser.Repair(text); if (f == null || !Json.TryParse(f, out root) || Json.Obj(root) == null) { root = null; err = "Reply was not JSON"; } }
                }
                done(root, root == null ? (err ?? "No reply") : null, text ?? "", res);
            });
        }

        /// <summary>What the photos of this story show, for the fact sheet. "" when nothing has been looked at.</summary>
        public string PhotoLooks(Story story)
        {
            var shots = new List<GalleryShot>();
            var main = ShotByRef(ShotFor(story));
            if (main != null) shots.Add(main);
            if (story.VesselId != "") shots.AddRange(State.Gallery.Where(g => g.VesselId == story.VesselId && g.Look != "" && (g.Public || g.IsPress) && !shots.Contains(g)).OrderByDescending(g => g.UT).Take(2));
            var seen = shots.Where(g => g.Look != "").Take(3).ToList();
            if (seen.Count == 0) return "";
            return "WHAT THE PHOTOS SHOW (the photo desk looked at them; writers and commenters may describe and react to how things look): "
                + string.Join(" | ", seen.Select(g => (g == main ? "Main photo" : g.IsPress ? "Press photo" : "Agency photo") + (g.Caption != "" ? " (\"" + ResponseParser.Clip(g.Caption, 80) + "\")" : "") + ": " + g.Look).ToArray());
        }

        // ------------------------------------------------------------------ livestream camera

        /// <summary>A frame for this chat batch: at a big moment, or every couple of minutes, when the chat model can see.</summary>
        byte[] StreamFrameFor(StreamSession st, double hype, double realTime, string model)
        {
            if (StreamFrameJpeg == null || !MainSees(model)) return null;
            bool big = hype >= 3;
            if (!big && realTime - st.LastFrameReal < StreamFrameGapSeconds) return null;
            if (big && realTime - st.LastFrameReal < 45) return null;
            byte[] f = null;
            try { f = StreamFrameJpeg(); } catch (Exception) { f = null; }
            if (f != null) st.LastFrameReal = realTime;
            return f;
        }

        /// <summary>When the chat model is blind but a helper can see: the helper looks at the camera at big moments, and the chat reads its words.</summary>
        void StreamHelperLook(StreamSession st, double hype, double realTime)
        {
            if (StreamFrameJpeg == null || st.LookBusy || hype < 3 || realTime - st.LastFrameReal < 45) return;
            if (Providers.CanSee(Settings.Preset, Settings.CheapModel)) return;
            string model, key, baseUrl;
            var p = Looker(out model, out key, out baseUrl);
            if (p == null) return;
            byte[] f = null;
            try { f = StreamFrameJpeg(); } catch (Exception) { f = null; }
            if (f == null) return;
            st.LastFrameReal = realTime;
            st.LookBusy = true;
            AskWith(p, baseUrl, key, model, "You describe live video frames from a kerbal rocket livestream in one or two short sentences, in-universe (kerbals, never a video game).",
                "This is the livestream camera right now" + (st.VesselName != "" ? " (" + st.VesselName + ")" : "") + ". What do viewers see? Reply with JSON only: {\"look\":\"...\"}", 200, f, (root, err, text, res) =>
                {
                    st.LookBusy = false;
                    string look = root != null ? (Json.Str(root, "look", "") ?? "").Trim() : "";
                    if (look != "" && !ResponseParser.ContainsForbidden(look)) st.CameraLook = ResponseParser.Clip(look, 300);
                });
        }
    }
}
