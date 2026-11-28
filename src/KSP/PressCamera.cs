using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace KerbalNews.KSP
{
    /// <summary>
    /// Press photographers. During launches from the home world, a second viewpoint renders the scene the way someone
    /// on the ground (or in a helicopter or chase plane) would see it: a long lens from kilometres away, a little haze,
    /// a little shake. The player's own view is never moved; the game cameras are borrowed for one frame and put back.
    /// Pictures go to the save's gallery as public press photos, and the best moments get a Snackboard post.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class PressCamera : MonoBehaviour
    {
        const int W = 1600, H = 900;
        static readonly string[] CameraNames = { "GalaxyCamera", "Camera ScaledSpace", "Camera 01", "Camera 00" };
        static readonly string[] Photographers = { "Gus Kerman", "Mirela Kerman", "Tobald Kerman", "Hanbree Kerman", "Lodrey Kerman", "Sigrid Kerman" };
        static readonly string[] PhotoOutlets = { "herald", "observer", "inquirer", "kraken", "herald", "kscwatch" };

        readonly System.Random rng = new System.Random();
        string vesselId = "";
        Vessel.Situations lastSit;
        float padTime, liftoffAt = -1, nextAscent, recoveryAt = -1;
        bool wasOnPad;
        int ascentShots;
        bool busy;
        readonly Dictionary<string, float> lastExplosion = new Dictionary<string, float>();

        /// <summary>The self-test shoots whatever the time of day.</summary>
        public static bool IgnoreDaylight;

        static NewsEngine Eng { get { var s = NewsSystem.Instance; return s != null && s.HasGame ? s.Engine : null; } }
        static bool On { get { return NewsSystem.Settings != null && NewsSystem.Settings.PressPhotos; } }

        void Start() { GameEvents.onPartDie.Add(OnPartDie); }
        void OnDestroy() { GameEvents.onPartDie.Remove(OnPartDie); }

        void Update()
        {
            if (ModCompat.InFmrsSideFlight()) return;   // a dropped-stage flight that FMRS will undo
            var eng = Eng;
            var v = FlightGlobals.ActiveVessel;
            if (eng == null || !On || v == null || v.mainBody == null || v.mainBody != FlightGlobals.GetHomeBody()) return;
            string id = v.persistentId.ToString();
            if (id != vesselId) { vesselId = id; lastSit = v.situation; padTime = 0; skyRetries = 0; liftoffAt = -1; recoveryAt = -1; ascentShots = 0; wasOnPad = false; }
            float now = Time.realtimeSinceStartup;
            var sit = v.situation;
            bool silent = eng.State.IsSilent(id);

            if (sit == Vessel.Situations.PRELAUNCH && v.srfSpeed < 1)
            {
                wasOnPad = true;
                padTime += Time.deltaTime;
                // Give the scene time to settle, and retry every few seconds if the sky was missing.
                if (padTime > 8 && now >= nextPadTry && skyRetries < 6 && !busy && !eng.HasPressShot(id, "pad")) { nextPadTry = now + 4f; Shoot(v, "pad", null); }
            }
            // Liftoff: the craft was on the pad and is now flying, however the situation got there.
            if (wasOnPad && (sit == Vessel.Situations.FLYING || sit == Vessel.Situations.SUB_ORBITAL || v.srfSpeed > 12))
            {
                wasOnPad = false;
                liftoffAt = now + 2.5f;
                nextAscent = now + 14f;
                ascentShots = 0;
            }
            if (liftoffAt > 0 && now > liftoffAt) { liftoffAt = -1; if (!eng.HasPressShot(id, "liftoff")) Shoot(v, "liftoff", null); }
            if (ascentShots < 3 && nextAscent > 0 && now > nextAscent && (sit == Vessel.Situations.FLYING || sit == Vessel.Situations.SUB_ORBITAL) && v.altitude < 35000)
            {
                nextAscent = now + 22f;
                ascentShots++;
                Shoot(v, "ascent", null);
            }
            if (v.altitude >= 35000) nextAscent = -1;
            if (!silent && (sit == Vessel.Situations.LANDED || sit == Vessel.Situations.SPLASHED) && (lastSit == Vessel.Situations.FLYING || lastSit == Vessel.Situations.SUB_ORBITAL))
                recoveryAt = now + 4f;
            if (recoveryAt > 0 && now > recoveryAt) { recoveryAt = -1; if (!eng.HasPressShot(id, "recovery")) Shoot(v, "recovery", null); }
            lastSit = sit;
        }

        /// <summary>An exploding part of a launch over the home world, below the upper atmosphere, is seen for kilometres.</summary>
        void OnPartDie(Part p)
        {
            try
            {
                var eng = Eng;
                if (eng == null || !On || p == null || p.vessel == null) return;
                var v = p.vessel;
                if (v.mainBody == null || v.mainBody != FlightGlobals.GetHomeBody() || v.altitude > 40000) return;
                if (v.situation != Vessel.Situations.FLYING && v.situation != Vessel.Situations.SUB_ORBITAL) return;
                string id = v.persistentId.ToString();
                if (!eng.State.Tracked.ContainsKey(id) || eng.HasPressShot(id, "explosion")) return;
                float last;
                if (lastExplosion.TryGetValue(id, out last) && Time.realtimeSinceStartup - last < 20) return;
                lastExplosion[id] = Time.realtimeSinceStartup;
                var body = v.mainBody;
                Vector3d pos = p.transform.position;
                Shoot(v, "explosion", new[] { body.GetLatitude(pos), body.GetLongitude(pos), body.GetAltitude(pos) });
            }
            catch (Exception e) { Debug.LogWarning("[KerbalNews] Press photo (explosion): " + e.Message); }
        }

        void Shoot(Vessel v, string kind, double[] point)
        {
            if (busy || MapView.MapIsEnabled || FlightDriver.Pause) { Debug.Log("[KerbalNews] Press photo (" + kind + ") skipped: " + (busy ? "busy" : MapView.MapIsEnabled ? "map view" : "paused")); return; }
            busy = true;
            StartCoroutine(TakePhoto(v, kind, point));
        }

        IEnumerator TakePhoto(Vessel v, string kind, double[] point)
        {
            // Let the fireball grow before the shutter fires.
            if (kind == "explosion") yield return new WaitForSeconds(0.25f);
            yield return new WaitForEndOfFrame();
            // The picture is rendered and copied here; turning it into a JPEG and writing the file happen on a worker
            // thread, and the shot is filed once the file exists. Everything about the craft is noted now: after an
            // explosion it may be gone a moment later.
            Texture2D tex = null;
            FrameGrab.JpgJob job = null;
            string file = null, credit = null, id = null, name = null, body = null, situation = null;
            double ut = 0;
            NewsEngine eng = null;
            try
            {
                eng = Eng;
                if (eng != null && v != null && v.mainBody != null)
                {
                    tex = Compose(v, kind, point, out credit);
                    if (tex != null)
                    {
                        Directory.CreateDirectory(GalleryStore.Dir);
                        file = "press-" + v.persistentId + "-" + kind + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".jpg";
                        id = v.persistentId.ToString(); name = Names.Of(v); body = v.mainBody.bodyName; situation = v.situation.ToString();
                        ut = Planetarium.GetUniversalTime();
                        if (tex.width <= GalleryStore.MaxWidth) job = FrameGrab.SaveRgbJpg(tex, 88, Path.Combine(GalleryStore.Dir, file));
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[KerbalNews] Press photo failed: " + e); if (tex != null) Destroy(tex); tex = null; }
            float waitUntil = Time.realtimeSinceStartup + 10;
            while (tex != null && job != null && !job.Done && Time.realtimeSinceStartup < waitUntil) yield return null;
            try
            {
                if (tex != null)
                {
                    // No worker-thread encoder here (or it failed): encode and write the old way.
                    if (job == null || !job.Done || !job.Ok) File.WriteAllBytes(Path.Combine(GalleryStore.Dir, file), GalleryStore.Jpeg(tex));
                    eng.AddPressShot(file, ut, id, name, body, situation, kind, credit);
                    if (kind != "ascent")
                        ScreenMessages.PostScreenMessage("Kerbal News: a press photographer got a shot of " + name + " (" + kind + ")", 3f, ScreenMessageStyle.UPPER_RIGHT);
                }
            }
            catch (Exception e) { Debug.LogWarning("[KerbalNews] Press photo failed: " + e); }
            finally { if (tex != null) Destroy(tex); busy = false; }
        }

        // ------------------------------------------------------------------ framing

        /// <summary>Picks where the photographer stands and renders the shot. Null when no spot has a clear view or it's too dark.</summary>
        Texture2D Compose(Vessel v, string kind, double[] point, out string credit)
        {
            credit = "";
            var body = v.mainBody;
            Vector3d center; float size;
            Measure(v, out center, out size);
            Vector3d target = point != null ? body.GetWorldSurfacePosition(point[0], point[1], point[2]) : center;
            Vector3d up = (target - body.position).normalized;
            float frame;
            switch (kind)
            {
                case "pad": frame = size * 1.7f; break;
                case "liftoff": frame = Math.Max(size * 2.6f, 60f); target -= up * (size * 0.45); break;
                case "explosion": frame = Math.Max(size * 4f, 220f); break;
                case "recovery": frame = Math.Max(size * 3f, 30f); break;
                default: frame = Math.Max(size * 3.2f, 90f) + (float)v.altitude * 0.012f; break;
            }

            bool silent = Eng != null && Eng.State.IsSilent(v.persistentId.ToString());
            string style = kind == "recovery" ? "helicopter" : kind == "ascent" && !silent && v.altitude > 1500 && rng.Next(3) == 0 ? "chase" : "ground";
            if (style != "ground" && silent) style = "ground";
            // At the space center itself, accredited photographers work from the press site and remote cameras by the pad.
            bool atKsc = body.isHomeWorld && Math.Abs(v.latitude - KscLat) < 0.08 && Math.Abs(v.longitude - KscLon) < 0.25;
            if (atKsc && (kind == "pad" || kind == "liftoff") && rng.Next(100) < 55) style = kind == "liftoff" && rng.Next(3) == 0 ? "remote" : "onsite";

            Vector3d cam = Vector3d.zero;
            bool found = false;
            double dist = 0;
            string place = "";
            for (int attempt = 0; attempt < 14 && !found; attempt++)
            {
                double az = rng.NextDouble() * Math.PI * 2;
                Vector3d east = Vector3d.Cross(up, new Vector3d(0, 1, 0));
                if (east.magnitude < 0.1) east = Vector3d.Cross(up, new Vector3d(1, 0, 0));
                east = east.normalized;
                Vector3d north = Vector3d.Cross(east, up).normalized;
                Vector3d dir = east * Math.Cos(az) + north * Math.Sin(az);
                double alt = body.GetAltitude(target);
                switch (style)
                {
                    case "helicopter": dist = 500 + rng.Next(400); break;
                    case "chase": dist = 900 + rng.Next(900); break;
                    case "onsite": dist = kind == "pad" ? 260 + rng.Next(450) : 450 + rng.Next(700); break;
                    case "remote": dist = 110 + rng.Next(90); break;
                    default:
                        dist = kind == "pad" ? 1800 + rng.Next(2500) : kind == "liftoff" ? 3000 + rng.Next(3500) : kind == "explosion" ? 5000 + rng.Next(6000)
                            : Math.Min(30000, 5000 + alt * 0.7 + rng.Next(4000));
                        break;
                }
                Vector3d spot = target + dir * dist;
                double lat = body.GetLatitude(spot), lon = body.GetLongitude(spot);
                double ground = body.TerrainAltitude(lat, lon, true);
                bool sea = body.ocean && ground < 0;
                double camAlt = style == "helicopter" ? Math.Max(ground, 0) + 120 + rng.Next(120)
                    : style == "chase" ? Math.Max(alt - 200 + rng.Next(400), Math.Max(ground, 0) + 300)
                    : style == "onsite" ? Math.Max(ground, 0) + (rng.Next(3) == 0 ? 18 : 2)
                    : style == "remote" ? Math.Max(ground, 0) + 0.6
                    : Math.Max(ground, 0) + (sea ? 3 : 2);
                cam = body.GetWorldSurfacePosition(lat, lon, camAlt);
                if (!ClearView(body, cam, target)) continue;
                // On the ground the whole stack must be in view, not just its middle: check the base too.
                if (kind == "pad" && !ClearView(body, cam, target - up * (size * 0.42), 20)) continue;
                found = true;
                place = style == "helicopter" ? "from a news helicopter" : style == "chase" ? "from a chase plane"
                    : style == "remote" ? "with a sound-triggered remote camera beside the pad"
                    : style == "onsite" ? (camAlt - Math.Max(ground, 0) > 10 ? "from the roof of the KSC press site" : "from the KSC press site")
                    : sea ? "from a press boat offshore" : PlaceName(body, lat, lon, ground);
            }
            if (!found) { Debug.Log("[KerbalNews] Press photo (" + kind + "): no clear view from any spot"); return null; }

            bool day = Daylight(body, cam);
            if (!day && !IgnoreDaylight && kind != "liftoff" && kind != "explosion") { Debug.Log("[KerbalNews] Press photo (" + kind + "): too dark"); return null; }

            double d = (target - cam).magnitude;
            float vfov = Mathf.Clamp((float)(2 * Math.Atan(frame * 0.5 / d) * 180 / Math.PI), 0.25f, 35f);
            // A little unsteadiness: hand-held from a helicopter, tracking a fast rocket, or wind on a tripod.
            float wobble = style == "remote" ? 0.002f : (style == "ground" || style == "onsite") && kind == "pad" ? 0.004f : 0.012f;
            Vector3d aim = (target - cam).normalized;
            aim = (aim + new Vector3d(Jitter(wobble), Jitter(wobble), Jitter(wobble)) * (vfov * Math.PI / 180)).normalized;
            Vector3 camUp = (Vector3)((cam - body.position).normalized);

            var tex = RenderView(cam, aim, camUp, vfov);
            if (tex == null) { Debug.Log("[KerbalNews] Press photo (" + kind + "): render failed"); return null; }
            // Right after a scene loads the sky (drawn in scaled space) can still be missing, leaving the craft against black.
            if (day && (kind == "pad" || kind == "liftoff") && style != "helicopter" && skyRetries < 6 && SkyMissing(tex))
            {
                Destroy(tex);
                skyRetries++;
                Debug.Log("[KerbalNews] Press photo (" + kind + "): the sky came out black, trying again shortly");
                return null;
            }
            Develop(tex, d, style, kind);

            double mm = 12.0 / Math.Tan(vfov * Math.PI / 360);
            int lens = mm < 100 ? (int)Math.Round(mm / 5) * 5 : (int)Math.Round(mm / 50) * 50;
            string who = Photographers[rng.Next(Photographers.Length)];
            string outlet = Outlets.NameOf(PhotoOutlets[rng.Next(PhotoOutlets.Length)]);
            if (style == "onsite" || style == "remote") outlet = "the KSC press pool";
            credit = who + " for " + outlet + ", " + Math.Max(style == "remote" ? 14 : 24, lens) + " mm " + place
                + (style == "ground" ? string.Format(", about {0:0.#} km away", d / 1000) : style == "onsite" ? string.Format(", {0:0} m from the pad", d) : "");
            return tex;
        }

        const double KscLat = -0.0972, KscLon = -74.5577;

        double Jitter(float amount) { return (rng.NextDouble() * 2 - 1) * amount; }

        /// <summary>The middle of the craft and its size, from its parts.</summary>
        internal static void Measure(Vessel v, out Vector3d center, out float size)
        {
            center = v.GetWorldPos3D();
            size = 10;
            if (v.parts == null || v.parts.Count == 0) return;
            Vector3d sum = Vector3d.zero;
            foreach (var p in v.parts) sum += (Vector3d)p.transform.position;
            center = sum / v.parts.Count;
            double r = 0;
            foreach (var p in v.parts) r = Math.Max(r, ((Vector3d)p.transform.position - center).magnitude);
            size = Math.Max(4f, (float)(2 * r + 3));
        }

        /// <summary>True when no hill stands between the photographer and the subject.</summary>
        const int SceneryMask = 1 << 15;

        internal static bool ClearView(CelestialBody body, Vector3d from, Vector3d to, double stopShort = 60)
        {
            for (int i = 1; i < 12; i++)
            {
                Vector3d p = from + (to - from) * (i / 12.0);
                double lat = body.GetLatitude(p), lon = body.GetLongitude(p);
                if (body.TerrainAltitude(lat, lon, false) > body.GetAltitude(p) - 1) return false;
            }
            // Buildings, the tower and other scenery are colliders on the "Local Scenery" layer, not terrain.
            // Stop short of the craft so the pad under it does not count.
            try
            {
                Vector3d dir = to - from;
                double len = dir.magnitude;
                if (len > stopShort + 20)
                {
                    Vector3d end = from + dir * ((len - stopShort) / len);
                    if (Physics.Linecast((Vector3)from, (Vector3)end, SceneryMask)) return false;
                }
            }
            catch (Exception) { }
            return true;
        }

        int skyRetries;
        float nextPadTry;

        /// <summary>True when the top of a daytime frame is near-black: the sky wasn't drawn.</summary>
        static bool SkyMissing(Texture2D tex)
        {
            var px = tex.GetPixels32();
            int w = tex.width, h = tex.height;
            if (px.Length != w * h || h < 10) return false;
            long sum = 0; int n = 0;
            for (int y = h - h / 8; y < h; y++)   // Texture2D rows run bottom to top
                for (int x = 0; x < w; x += 4) { var c = px[y * w + x]; sum += c.r + c.g + c.b; n++; }
            return n > 0 && sum / (3.0 * n) < 28;
        }

        static bool Daylight(CelestialBody body, Vector3d at)
        {
            var sun = Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
            if (sun == null) return true;
            return Vector3d.Dot((at - body.position).normalized, (sun.position - at).normalized) > -0.03;
        }

        static string PlaceName(CelestialBody body, double lat, double lon, double ground)
        {
            if (ground > 400) return "from the hills inland";
            if (ground < 20) return lon > -74.6 ? "from the beach east of the space center" : "from the bay causeway";
            return "from a field near the space center";
        }

        // ------------------------------------------------------------------ rendering

        /// <summary>Renders the game's cameras from another spot into a texture, then puts every camera back.</summary>
        static Texture2D RenderView(Vector3d pos, Vector3d dir, Vector3 up, float vfov)
        {
            var rt = new RenderTexture(W, H, 24);
            Texture2D tex = null;
            var prev = RenderTexture.active;
            try
            {
                if (!RenderInto(rt, pos, Quaternion.LookRotation((Vector3)dir, up), vfov, true)) return null;
                RenderTexture.active = rt;
                tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
            }
            finally
            {
                RenderTexture.active = prev;
                rt.Release();
                Destroy(rt);
            }
            return tex;
        }

        /// <summary>
        /// Draws the scene from another spot into a render texture. It uses private copies of the game's cameras, never the
        /// game's own: moving those, even for one render, upsets effects that remember the last frame (anti-aliasing,
        /// motion blur, sky mods) and makes the player's view flicker between the two shots. False if no camera was found.
        /// </summary>
        public static bool RenderInto(RenderTexture rt, Vector3d pos, Quaternion rot, float vfov, bool warmUp = false)
        {
            var cams = Camera.allCameras.Where(c => c != null && CameraNames.Contains(c.name)).OrderBy(c => c.depth).ToList();
            if (cams.Count == 0) return false;
            // Scatterer's ocean starts from the camera's depth texture (_CameraDepthTexture) to know where solid things
            // stand in front of the water. A camera only makes that texture if asked to; ours weren't, so the ocean read
            // the one the player's camera had just made and left a hole in the shape of the craft as the player sees it:
            // a flat, sky-coloured ghost, bigger than the craft in the photo and centred in the frame. See RenderTwin.
            if (warmUp)
            {
                // Press photos also draw the flight cameras once first, so anything an effect keeps from a camera's
                // previous render (a frame-late depth copy, a history buffer) comes from this view and not the stream's.
                foreach (var src in cams)
                    if (src.name == "Camera 01" || src.name == "Camera 00") RenderTwin(src, rt, pos, rot, vfov);
            }
            foreach (var src in cams) RenderTwin(src, rt, pos, rot, vfov);
            return true;
        }

        static void RenderTwin(Camera src, RenderTexture rt, Vector3d pos, Quaternion rot, float vfov)
        {
            var c = Twin(src);
            if (c == null) return;
            c.CopyFrom(src);   // culling layers, clear flags, clip planes; this also copies the transform, so set ours after
            // Our own depth texture, so depth-reading effects (Scatterer's ocean, screen-space scattering) see this view.
            if (src.name == "Camera 01" || src.name == "Camera 00") c.depthTextureMode = src.depthTextureMode | DepthTextureMode.Depth;
            if (src.name == "Camera ScaledSpace") c.transform.position = (Vector3)ScaledSpace.LocalToScaledSpace(pos);
            else if (src.name != "GalaxyCamera") c.transform.position = (Vector3)pos;
            c.transform.rotation = rot;
            c.fieldOfView = vfov;
            c.targetTexture = rt;
            c.Render();
            c.targetTexture = null;
        }

        static readonly Dictionary<string, Camera> twins = new Dictionary<string, Camera>();

        /// <summary>A disabled camera of our own for one of the game's cameras. Disabled cameras never draw to the screen.</summary>
        static Camera Twin(Camera src)
        {
            Camera c;
            if (twins.TryGetValue(src.name, out c) && c != null) return c;
            var go = new GameObject("KerbalNews twin of " + src.name);
            c = go.AddComponent<Camera>();
            c.enabled = false;
            twins[src.name] = c;
            return c;
        }

        /// <summary>
        /// Makes a clean render look like a real long-lens photo: air haze that grows with distance, slight shake blur,
        /// softer contrast, a vignette and film grain.
        /// </summary>
        void Develop(Texture2D tex, double dist, string style, string kind)
        {
            var px = tex.GetPixels32();
            int n = px.Length;
            if (n != W * H) return;
            var src = (Color32[])px.Clone();
            float haze = Mathf.Clamp((float)(dist / 40000.0), 0.04f, 0.3f);
            int sx = rng.Next(-2, 3), sy = rng.Next(-1, 2);
            if (style == "helicopter" || style == "chase") { sx *= 2; sy *= 2; }
            else if (style == "onsite" || style == "remote") { sx /= 2; sy = 0; }
            const float hr = 196, hg = 206, hb = 216;
            int seed = rng.Next();
            for (int y = 0; y < H; y++)
            {
                float vy = (y - H * 0.5f) / (H * 0.5f);
                int y2 = Math.Min(H - 1, Math.Max(0, y + sy));
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    int x2 = Math.Min(W - 1, Math.Max(0, x + sx));
                    var a = src[i];
                    var b = src[y2 * W + x2];
                    float r = (a.r + b.r) * 0.5f, g = (a.g + b.g) * 0.5f, bl = (a.b + b.b) * 0.5f;
                    r += (hr - r) * haze; g += (hg - g) * haze; bl += (hb - bl) * haze;
                    // Softer contrast around mid-grey.
                    r = 128 + (r - 128) * 0.93f; g = 128 + (g - 128) * 0.93f; bl = 128 + (bl - 128) * 0.93f;
                    float vx = (x - W * 0.5f) / (W * 0.5f);
                    float vig = 1f - 0.22f * (vx * vx + vy * vy) * 0.5f;
                    seed = seed * 1103515245 + 12345;
                    float grain = ((seed >> 16) & 0xff) / 255f * 10f - 5f;
                    px[i] = new Color32(B(r * vig + grain), B(g * vig + grain), B(bl * vig + grain), 255);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
        }

        static byte B(float v) { return (byte)(v < 0 ? 0 : v > 255 ? 255 : v); }
    }
}
