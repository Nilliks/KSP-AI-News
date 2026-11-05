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
            var eng = Eng;
            var v = FlightGlobals.ActiveVessel;
            if (eng == null || !On || v == null || v.mainBody == null || v.mainBody != FlightGlobals.GetHomeBody()) return;
            string id = v.persistentId.ToString();
            if (id != vesselId) { vesselId = id; lastSit = v.situation; padTime = 0; liftoffAt = -1; recoveryAt = -1; ascentShots = 0; }
            float now = Time.realtimeSinceStartup;
            var sit = v.situation;
            bool silent = eng.State.IsSilent(id);

            if (sit == Vessel.Situations.PRELAUNCH)
            {
                padTime += Time.deltaTime;
                if (padTime > 4 && !eng.HasPressShot(id, "pad")) Shoot(v, "pad", null);
            }
            if (lastSit == Vessel.Situations.PRELAUNCH && (sit == Vessel.Situations.FLYING || sit == Vessel.Situations.SUB_ORBITAL))
            {
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
            if (busy || MapView.MapIsEnabled || FlightDriver.Pause) return;
            busy = true;
            StartCoroutine(TakePhoto(v, kind, point));
        }

        IEnumerator TakePhoto(Vessel v, string kind, double[] point)
        {
            // Let the fireball grow before the shutter fires.
            if (kind == "explosion") yield return new WaitForSeconds(0.25f);
            yield return new WaitForEndOfFrame();
            try
            {
                var eng = Eng;
                if (eng != null && v != null && v.mainBody != null)
                {
                    string credit;
                    var tex = Compose(v, kind, point, out credit);
                    if (tex != null)
                    {
                        try
                        {
                            Directory.CreateDirectory(GalleryStore.Dir);
                            string file = "press-" + v.persistentId + "-" + kind + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".jpg";
                            File.WriteAllBytes(Path.Combine(GalleryStore.Dir, file), GalleryStore.Jpeg(tex));
                            eng.AddPressShot(file, Planetarium.GetUniversalTime(), v.persistentId.ToString(), Names.Of(v), v.mainBody.bodyName, v.situation.ToString(), kind, credit);
                            if (kind != "ascent")
                                ScreenMessages.PostScreenMessage("Kerbal News: a press photographer got a shot of " + Names.Of(v) + " (" + kind + ")", 3f, ScreenMessageStyle.UPPER_RIGHT);
                        }
                        finally { Destroy(tex); }
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[KerbalNews] Press photo failed: " + e); }
            finally { busy = false; }
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
                    : Math.Max(ground, 0) + (sea ? 3 : 2);
                cam = body.GetWorldSurfacePosition(lat, lon, camAlt);
                if (!ClearView(body, cam, target)) continue;
                // On the ground the whole stack must be in view, not just its middle: check the base too.
                if ((kind == "pad" || kind == "liftoff") && !ClearView(body, cam, target - up * (size * 0.42), 20)) continue;
                found = true;
                place = style == "helicopter" ? "from a news helicopter" : style == "chase" ? "from a chase plane"
                    : sea ? "from a press boat offshore" : PlaceName(body, lat, lon, ground);
            }
            if (!found) return null;

            bool day = Daylight(body, cam);
            if (!day && !IgnoreDaylight && kind != "liftoff" && kind != "explosion") return null;

            double d = (target - cam).magnitude;
            float vfov = Mathf.Clamp((float)(2 * Math.Atan(frame * 0.5 / d) * 180 / Math.PI), 0.25f, 35f);
            // A little unsteadiness: hand-held from a helicopter, tracking a fast rocket, or wind on a tripod.
            float wobble = style == "ground" && kind == "pad" ? 0.004f : 0.012f;
            Vector3d aim = (target - cam).normalized;
            aim = (aim + new Vector3d(Jitter(wobble), Jitter(wobble), Jitter(wobble)) * (vfov * Math.PI / 180)).normalized;
            Vector3 camUp = (Vector3)((cam - body.position).normalized);

            var tex = RenderView(cam, aim, camUp, vfov);
            if (tex == null) return null;
            Develop(tex, d, style, kind);

            int lens = (int)Math.Round(12.0 / Math.Tan(vfov * Math.PI / 360) / 50) * 50;
            string who = Photographers[rng.Next(Photographers.Length)];
            string outlet = Outlets.NameOf(PhotoOutlets[rng.Next(PhotoOutlets.Length)]);
            credit = who + " for " + outlet + ", " + Math.Max(50, lens) + " mm " + place + (style == "ground" ? string.Format(", about {0:0.#} km away", d / 1000) : "");
            return tex;
        }

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
                if (!RenderInto(rt, pos, Quaternion.LookRotation((Vector3)dir, up), vfov)) return null;
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

        /// <summary>Draws the scene from another spot into a render texture, then puts every game camera back. False if no camera was found.</summary>
        public static bool RenderInto(RenderTexture rt, Vector3d pos, Quaternion rot, float vfov)
        {
            var cams = Camera.allCameras.Where(c => c != null && CameraNames.Contains(c.name)).OrderBy(c => c.depth).ToList();
            if (cams.Count == 0) return false;
            var saved = cams.Select(c => new { c, p = c.transform.position, r = c.transform.rotation, f = c.fieldOfView, t = c.targetTexture }).ToList();
            try
            {
                foreach (var c in cams)
                {
                    if (c.name == "Camera ScaledSpace") c.transform.position = (Vector3)ScaledSpace.LocalToScaledSpace(pos);
                    else if (c.name != "GalaxyCamera") c.transform.position = (Vector3)pos;
                    c.transform.rotation = rot;
                    c.fieldOfView = vfov;
                    c.targetTexture = rt;
                    c.Render();
                }
            }
            finally
            {
                foreach (var s in saved) { s.c.transform.position = s.p; s.c.transform.rotation = s.r; s.c.fieldOfView = s.f; s.c.targetTexture = s.t; }
            }
            return true;
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
            if (style != "ground") { sx *= 2; sy *= 2; }
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
