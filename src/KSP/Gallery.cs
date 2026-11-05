using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace KerbalNews.KSP
{
    /// <summary>
    /// The gallery hotkey (F8) and stock screenshot import. Pictures are stored next to the save, in
    /// saves/&lt;save&gt;/KerbalNews/gallery, as 1600 px JPEGs; their records live in the save itself.
    /// </summary>
    public static class GalleryStore
    {
        public const int MaxWidth = 1600;

        public static string Dir
        {
            get { return Path.Combine(KSPUtil.ApplicationRootPath, "saves/" + (HighLogic.SaveFolder ?? "default") + "/KerbalNews/gallery"); }
        }

        /// <summary>Shrinks a screenshot to at most 1600 px wide and returns JPEG bytes.</summary>
        public static byte[] Jpeg(Texture2D src)
        {
            int w = Math.Min(MaxWidth, src.width);
            int h = Math.Max(1, (int)Math.Round(src.height * (w / (double)src.width)));
            var rt = RenderTexture.GetTemporary(w, h);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var small = new Texture2D(w, h, TextureFormat.RGB24, false);
            small.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            small.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var bytes = ImageConversion.EncodeToJPG(small, 88);
            UnityEngine.Object.Destroy(small);
            return bytes;
        }

        public static IEnumerator Capture()
        {
            var sys = NewsSystem.Instance;
            if (sys == null || !sys.HasGame) yield break;
            // Keep the browser out of the picture.
            bool hidden = Browser.Instance != null && Browser.Instance.HideForCapture(true);
            yield return new WaitForEndOfFrame();
            Texture2D tex = null;
            try
            {
                tex = ScreenCapture.CaptureScreenshotAsTexture();
                Directory.CreateDirectory(Dir);
                string file = "shot-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".jpg";
                File.WriteAllBytes(Path.Combine(Dir, file), Jpeg(tex));
                var v = HighLogic.LoadedSceneIsFlight ? FlightGlobals.ActiveVessel : null;
                sys.Engine.AddShot(file, Planetarium.GetUniversalTime(), v != null ? v.persistentId.ToString() : "", v != null ? v.vesselName : "",
                    v != null && v.mainBody != null ? v.mainBody.bodyName : "", v != null ? v.situation.ToString() : "");
                ScreenMessages.PostScreenMessage("Kerbal News: screenshot added to your gallery (private)", 3f, ScreenMessageStyle.UPPER_CENTER);
            }
            catch (Exception e) { Debug.LogError("[KerbalNews] Gallery capture failed: " + e); }
            finally
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
                if (hidden) Browser.Instance.HideForCapture(false);
            }
        }

        /// <summary>Copies the newest stock screenshots (F1) that aren't in the gallery yet.</summary>
        public static int ImportStock()
        {
            var sys = NewsSystem.Instance;
            if (sys == null || !sys.HasGame) return 0;
            string stock = Path.Combine(KSPUtil.ApplicationRootPath, "Screenshots");
            if (!Directory.Exists(stock)) return 0;
            Directory.CreateDirectory(Dir);
            int n = 0;
            foreach (var path in Directory.GetFiles(stock, "*.png").Concat(Directory.GetFiles(stock, "*.jpg")).OrderByDescending(File.GetLastWriteTime).Take(10))
            {
                string file = "f1-" + Path.GetFileNameWithoutExtension(path) + ".jpg";
                if (sys.Engine.State.Gallery.Any(g => g.File == file)) continue;
                var tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                try
                {
                    if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path))) continue;
                    File.WriteAllBytes(Path.Combine(Dir, file), Jpeg(tex));
                    sys.Engine.AddShot(file, Planetarium.GetUniversalTime(), "", "", "", "");
                    n++;
                }
                catch (Exception e) { Debug.LogWarning("[KerbalNews] Could not import " + path + ": " + e.Message); }
                finally { UnityEngine.Object.Destroy(tex); }
            }
            return n;
        }
    }
}
