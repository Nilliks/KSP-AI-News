using System;
using System.Linq;
using UnityEngine;

namespace KerbalNews.KSP
{
    /// <summary>
    /// Feeds the livestream in flight: telemetry every few seconds, staging and explosions as moments, and a small
    /// video feed cut like a real launch broadcast: cameras on the ground first, then cameras on the rocket's hull.
    /// Leaving flight ends the stream, except when FMRS (Flight Manager for Reusable Stages) is the one reloading the
    /// scene: its jump to a dropped booster and back is a save load, and the broadcast carries on through both. While
    /// FMRS flies the booster the broadcast follows it; back on the main flight it returns to the main vessel.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class StreamDriver : MonoBehaviour
    {
        const int FW = 640, FH = 360;
        public static RenderTexture Frame { get; private set; }
        /// <summary>Every cut the director made this flight, for the self-test: "12.3s Pad camera".</summary>
        public static readonly System.Collections.Generic.List<string> CutLog = new System.Collections.Generic.List<string>();
        /// <summary>Self-test only: use the hull cameras whatever the distance.</summary>
        public static bool ForceHull;

        // ---- FMRS hand-over. The stream lives in the engine, which outlives the scene; this driver is rebuilt by every load.
        /// <summary>True when the scene now loading was ordered by FMRS and the stream should survive it.</summary>
        static bool keep;
        static bool paused;
        static float pausedAt, clockOffset;
        /// <summary>The stream's clock: real seconds, minus the time spent on FMRS's loading screens, so the chat, the
        /// countdown, the announcer and the replay carry on from where they stopped instead of jumping ahead.</summary>
        public static float Now { get { return (paused ? pausedAt : Time.realtimeSinceStartup) - clockOffset; } }
        /// <summary>FMRS's "switched to a dropped stage" flag as it was when this flight scene started.</summary>
        public static bool SceneSide { get; private set; }
        /// <summary>The dropped stage FMRS put the player on in this scene (persistentId), or "".</summary>
        public static string SideBoosterId { get; private set; }
        /// <summary>For the self-test: how many scene loads a stream has been carried through.</summary>
        public static int Handovers { get; private set; }

        static StreamDriver() { SideBoosterId = ""; }

        /// <summary>Starts the stream clock again after a hand-over (or after one that never came back).</summary>
        public static void Resume()
        {
            if (paused) { clockOffset += Time.realtimeSinceStartup - pausedAt; paused = false; }
            keep = false;
        }

        bool latched;
        int frames;
        Vessel focus;
        float downSince = -1;
        bool oldLiftoff;

        float nextSample, nextFrame, lastBoom, nextRecTry, nextFlight;
        string liftedFor = "";
        readonly System.Random rng = new System.Random();

        static NewsEngine Eng { get { var s = NewsSystem.Instance; return s != null && s.HasGame ? s.Engine : null; } }

        void Start()
        {
            GameEvents.onStageActivate.Add(OnStage);
            GameEvents.onPartDie.Add(OnPartDie);
            GameEvents.onGameSceneLoadRequested.Add(OnSceneRequested);
        }

        /// <summary>
        /// A scene load is starting. If FMRS ordered it (a jump to a dropped stage, between stages, or back to the main
        /// vessel, which can go by way of the Space Center when the stage was recovered), the stream is kept and its clock
        /// stops until the next flight scene is up. Any other load ends the stream as before.
        /// </summary>
        void OnSceneRequested(GameScenes to)
        {
            keep = false;
            var eng = Eng;
            if (eng == null || eng.Stream == null || !ModCompat.FmrsInstalled) return;
            if (!SceneSide && !ModCompat.FmrsSwitchedRaw()) return;
            if (to != GameScenes.FLIGHT && to != GameScenes.SPACECENTER && to != GameScenes.TRACKSTATION) return;
            keep = true;
            Handovers++;
            if (!paused) { pausedAt = Time.realtimeSinceStartup; paused = true; }
            Debug.Log("[KerbalNews] FMRS is switching vessels: the livestream carries on through the load.");
        }

        void OnDestroy()
        {
            GameEvents.onStageActivate.Remove(OnStage);
            GameEvents.onPartDie.Remove(OnPartDie);
            GameEvents.onGameSceneLoadRequested.Remove(OnSceneRequested);
            SceneSide = false;
            SideBoosterId = "";
            var eng = Eng;
            if (eng != null && eng.Stream != null && !keep) eng.EndStream(Planetarium.GetUniversalTime());
            if (Frame != null) { Frame.Release(); Destroy(Frame); Frame = null; }
        }


        /// <summary>The stats overlay's numbers for this instant, or null when nothing is streaming.</summary>
        public static OverlayModel Overlay()
        {
            var eng = Eng;
            if (eng == null || eng.Stream == null) return null;
            var v = Find(eng.Stream);
            var n = v != null ? Numbers(v) : new LiveNumbers();
            if (v == null && eng.Stream.Last != null)
            {
                var t = eng.Stream.Last;
                n.UT = t.UT; n.Altitude = t.Altitude; n.Speed = t.Speed; n.Apoapsis = t.Apoapsis; n.Periapsis = t.Periapsis; n.Stage = t.Stage; n.FuelPct = t.FuelPct; n.Situation = t.Situation;
            }
            return StreamOverlay.Build(eng.Stream, n, eng.CountdownLeft(Now));
        }

        /// <summary>The craft's numbers this instant: for the overlay and the flight controller's callouts.</summary>
        static LiveNumbers Numbers(Vessel v)
        {
            var n = new LiveNumbers();
            {
                n.UT = Planetarium.GetUniversalTime();
                n.Altitude = v.altitude;
                n.Speed = v.situation == Vessel.Situations.ORBITING || v.situation == Vessel.Situations.ESCAPING ? v.obt_speed : v.srfSpeed;
                if (v.orbit != null) { n.Apoapsis = v.orbit.ApA; n.Periapsis = v.orbit.PeA; }
                n.Throttle = v.ctrlState != null ? v.ctrlState.mainThrottle : 0;
                n.Q = v.dynamicPressurekPa;
                n.Stage = v.currentStage;
                n.Situation = v.situation.ToString();
                n.AtmosphereTop = v.mainBody != null && v.mainBody.atmosphere ? v.mainBody.atmosphereDepth : 0;
                double amount = 0, max = 0;
                if (v.parts != null)
                    foreach (var p in v.parts)
                        foreach (PartResource r in p.Resources)
                            if (r.resourceName == "LiquidFuel" || r.resourceName == "Oxidizer" || r.resourceName == "SolidFuel") { amount += r.amount; max += r.maxAmount; }
                n.FuelPct = max > 0 ? amount / max * 100 : -1;
                n.Mach = v.mach;
                double thrust = 0;
                foreach (var e in v.FindPartModulesImplementing<ModuleEngines>())
                    if (e != null && e.EngineIgnited && !e.flameout) thrust += e.finalThrust;
                n.Thrust = thrust;
            }
            return n;
        }

        Vessel Streamed()
        {
            var eng = Eng;
            return eng == null || eng.Stream == null ? null : Find(eng.Stream);
        }

        /// <summary>The vessel on screen: the one the stream is about, or the dropped booster while FMRS is flying it.</summary>
        static Vessel Find(StreamSession st)
        {
            string id = st.OnBooster ? st.FocusId : st.VesselId;
            var v = FlightGlobals.ActiveVessel;
            if (v != null && v.persistentId.ToString() == id) return v;
            var found = FlightGlobals.Vessels == null ? null : FlightGlobals.Vessels.FirstOrDefault(x => x != null && x.loaded && x.persistentId.ToString() == id);
            // A booster that broke up: stay with whatever the player is left looking at.
            return found == null && st.OnBooster ? v : found;
        }

        void OnStage(int stage)
        {
            var eng = Eng;
            if (eng == null || eng.Stream == null || eng.Stream.OnBooster || Streamed() != FlightGlobals.ActiveVessel) return;
            eng.StreamMoment("stage " + stage + " activated", Now);
        }

        void OnPartDie(Part p)
        {
            var eng = Eng;
            if (eng == null || eng.Stream == null || p == null || p.vessel == null) return;
            if (eng.Stream.OnBooster || SceneSide)
            {
                // Only the booster is real in a side flight. Losing its root part is losing the booster.
                if (focus != null && p.vessel == focus && p == focus.rootPart) eng.StreamBoosterEnd("lost", "", Now);
                return;
            }
            if (p.vessel.persistentId.ToString() != eng.Stream.VesselId) return;
            if (Now - lastBoom < 6) return;
            lastBoom = Now;
            eng.StreamMoment("a part of the vessel exploded", Now);
        }

        void Update()
        {
            if (!latched)
            {
                // Give the scene (and FMRS, which settles its flag in its own Start) a few frames before reading anything.
                if (!FlightGlobals.ready || FlightGlobals.ActiveVessel == null || ++frames < 10) return;
                latched = true;
                SceneSide = ModCompat.FmrsInstalled && ModCompat.FmrsSwitchedRaw();
                SideBoosterId = SceneSide ? FlightGlobals.ActiveVessel.persistentId.ToString() : "";
                bool carried = paused;
                Resume();
                if (carried && Eng != null && Eng.Stream != null) Debug.Log("[KerbalNews] Livestream carried through the FMRS load; now on " + (SceneSide ? "the dropped stage " : "the main vessel ") + Names.Of(FlightGlobals.ActiveVessel) + ".");
            }
            var eng = Eng;
            if (eng == null || eng.Stream == null) return;
            float now = Now;
            eng.SetRealTime(now);
            FollowFmrs(eng, now);
            var v = Streamed();
            if (v != null && now > nextSample)
            {
                nextSample = now + 3f;
                // Liftoff is when the craft actually leaves the ground, not when the first stage fires.
                string vid = v.persistentId.ToString();
                // A stream carried through an FMRS jump has had its liftoff: the booster, or the main vessel met again, is not a launch.
                if (liftedFor != vid && (eng.Stream.Lifted || eng.Stream.OnBooster)) { liftedFor = vid; oldLiftoff = true; }
                else if (liftedFor != vid && v.situation == Vessel.Situations.FLYING && v.srfSpeed > 3)
                {
                    liftedFor = vid;
                    eng.StreamMoment("liftoff", now);
                    eng.Stream.Overlay.LiftoffUT = Planetarium.GetUniversalTime();
                    eng.Stream.Overlay.StagesAtLiftoff = v.currentStage;
                }
                try { eng.StreamSample(Sample(v), now); }
                catch (Exception e) { Debug.LogWarning("[KerbalNews] Stream telemetry: " + e.Message); }
            }
            // The flight controller's callouts (supersonic, max Q, cutoff) need the numbers more often than the chat does.
            if (v != null && now > nextFlight)
            {
                nextFlight = now + 0.2f;
                try { eng.StreamFlight(Numbers(v), now); }
                catch (Exception e) { Debug.LogWarning("[KerbalNews] Stream callouts: " + e.Message); nextFlight = now + 5; }
            }
            eng.TickStream(now, !eng.WarpHold && TimeWarp.CurrentRate <= 1.01f);   // the stream chat waits out a time warp too
        }

        /// <summary>
        /// FMRS: in a side flight the broadcast follows the dropped stage the player is flying and calls its landing or
        /// loss; back on the main flight it returns to the main vessel.
        /// </summary>
        void FollowFmrs(NewsEngine eng, float now)
        {
            var st = eng.Stream;
            if (!SceneSide)
            {
                if (st.OnBooster) { eng.StreamFocusMain(now); focus = null; nextSample = 0; }
                return;
            }
            var av = FlightGlobals.ActiveVessel;
            if (av != null && st.BoosterEnd != "lost" && av.persistentId.ToString() != st.VesselId && av.persistentId.ToString() != st.FocusId)
            {
                focus = av;
                downSince = -1;
                eng.StreamFocusBooster(av.persistentId.ToString(), Names.Of(av), now);
                nextSample = 0;
            }
            if (!st.OnBooster || st.BoosterEnd != "") return;
            if (focus == null && av != null && av.persistentId.ToString() == st.FocusId) focus = av;
            if (focus == null) { eng.StreamBoosterEnd("lost", "", now); return; }   // Unity null: the vessel is gone
            bool down = focus.situation == Vessel.Situations.LANDED || focus.situation == Vessel.Situations.SPLASHED;
            if (!down || focus.srfSpeed > 1.5) { downSince = -1; return; }
            if (downSince < 0) { downSince = now; return; }
            // Standing still for a couple of seconds: not a bounce, and not about to tip over the moment it touches.
            if (now - downSince > 2.5f)
                eng.StreamBoosterEnd(focus.situation == Vessel.Situations.SPLASHED ? "splashed" : "landed", focus.mainBody != null ? focus.mainBody.bodyName : "", now);
        }

        void LateUpdate()
        {
            var eng = Eng;
            var br = Browser.Instance;
            if (eng == null || eng.Stream == null || !latched || MapView.MapIsEnabled) return;
            bool watching = br != null && br.Showing && br.Url.StartsWith(KerbalNews.Web.Sites.LiveHost);
            string rec = eng.RecordingFolder;
            if (!watching && rec == "") return;
            float now = Now;
            bool drew = false;
            if (watching && now >= nextFrame)
            {
                nextFrame = now + 0.33f;
                try { DrawFrame(eng); drew = true; }
                catch (Exception e) { Debug.LogWarning("[KerbalNews] Stream video: " + e.Message); nextFrame = now + 5; }
            }
            // The replay records the broadcast whether or not anyone has the page open.
            if (rec != "" && now >= nextRecTry)
            {
                int n = eng.ReplayFrameDue(now);
                if (n <= 0) return;
                try
                {
                    if (!drew && !watching) DrawFrame(eng);
                    ReplayStore.SaveFrame(rec, n);
                }
                catch (Exception e) { Debug.LogWarning("[KerbalNews] Replay recording: " + e.Message); nextRecTry = now + 5; }
            }
        }

        // ------------------------------------------------------------------ the camera director
        // A real broadcast has no camera that flies along with the rocket. Before and just after liftoff it cuts between
        // fixed cameras on the ground; long-lens trackers follow the climb; as the rocket gets far away the broadcast
        // moves to small cameras bolted to the hull. The player's own view is never used while a camera can be made.

        class Shot
        {
            public string Name;
            public bool Ground, Fixed, Tracking;
            public float BadUntil;          // washed out or black: skipped for a while
            // Ground cameras: a spot on the surface (and, when fixed, an aim point that never moves).
            public double Lat, Lon, Alt;
            public Vector3d AimAt;          // body-relative, so it stays put while the floating origin moves
            public float Fov;
            // Hull cameras: a part, an offset and a look direction in that part's own axes.
            public Part Part;
            public Vector3d Offset, Look, Up;
        }

        readonly System.Collections.Generic.List<Shot> groundCams = new System.Collections.Generic.List<Shot>();
        readonly System.Collections.Generic.List<Shot> hullCams = new System.Collections.Generic.List<Shot>();
        string camsFor = "";
        Shot shot;
        float shotUntil, lostSince = -1, liftoffAt = -1;
        int hullParts;
        double trackStart;
        // Liftoff belongs to the wide pad camera: it is cut to in the last seconds of the count (or when the engines
        // light on the pad) and held until the rocket climbs out of its frame, so the takeoff itself is always seen.
        bool padHold, sawOnGround;
        const float PadHoldAfterLiftoff = 12f;

        void DrawFrame(NewsEngine eng)
        {
            if (Frame == null) { Frame = new RenderTexture(FW, FH, 24); Frame.Create(); }
            var v = Streamed();
            if (v == null) { PlayerView(); return; }
            float now = Now;
            string id = v.persistentId.ToString();
            if (camsFor != id) { camsFor = id; SetUpGround(v); hullCams.Clear(); shot = null; liftoffAt = -1; trackStart = 0; padHold = false; sawOnGround = false; }
            bool onGround = v.situation == Vessel.Situations.PRELAUNCH || v.situation == Vessel.Situations.LANDED || v.situation == Vessel.Situations.SPLASHED;
            if (onGround) sawOnGround = true;
            // Checked every frame here: the telemetry only looks for liftoff every 3 s, which is too late for the camera.
            if (liftoffAt < 0 && (liftedFor == id || (sawOnGround && !onGround)))
            {
                // After an FMRS jump the liftoff is old news: no hold, and the cameras behave as in mid-flight.
                bool old = liftedFor == id && oldLiftoff;
                liftoffAt = old ? now - 1000 : now;
                if (old) { if (padHold) { padHold = false; shotUntil = now; } }
                else if (sawOnGround && !HoldPad(eng, v, now, "liftoff") && padHold) { padHold = false; shotUntil = now; }
            }
            else if (liftoffAt < 0 && onGround)
            {
                double left = eng.CountdownLeft(now);
                bool soon = (!double.IsNaN(left) && left <= 10) || EnginesLit(v);
                if (soon && !padHold) HoldPad(eng, v, now, double.IsNaN(left) || left > 10 ? "engines lit" : "final count");
                else if (!soon && padHold) { padHold = false; shotUntil = now + 4f; }   // a hold, scrub or shutdown on the pad
            }
            if (v.parts != null && v.parts.Count != hullParts) { hullParts = v.parts.Count; hullCams.Clear(); }   // staged or broke up
            if (hullCams.Count == 0) SetUpHull(v);
            RetryTrackers(v, now);

            // Keep a shot the rocket has left for a moment, so it visibly flies out of frame before the cut.
            bool ok = shot != null && Usable(shot, v);
            if (!ok && shot != null && lostSince < 0) lostSince = now;
            if (ok) lostSince = -1;
            if (shot == null || now > shotUntil || (!ok && now - lostSince > 1.2f)) Cut(eng, v, now);
            if (shot == null || !Render(shot, v, now)) { PlayerView(); return; }
            // A long lens through thick, low air can render almost pure white, and haze grows as the rocket climbs away,
            // so the picture is checked at the start of each shot and every second after. A shot nobody could watch is dropped.
            if (shot != checkedShot || now > nextCheck)
            {
                checkedShot = shot;
                nextCheck = now + 1f;
                float mean = Brightness();
                if (mean > (shot.Ground ? 232 : 225) || mean < 10)
                {
                    CutLog.Add(now.ToString("0.0") + "s skipped " + shot.Name + " (brightness " + mean.ToString("0") + ")");
                    shot.BadUntil = now + (shot.Ground ? 60 : 15);
                    Cut(eng, v, now);
                    checkedShot = shot;
                    if (shot == null || !Render(shot, v, now)) PlayerView();
                }
            }
        }

        float nextCheck;
        Shot checkedShot;
        Texture2D probe;

        /// <summary>Average brightness (0-255) of the current video frame, from a coarse grid of pixels.</summary>
        float Brightness()
        {
            var prev = RenderTexture.active;
            try
            {
                if (probe == null) probe = new Texture2D(FW, FH, TextureFormat.RGB24, false);
                RenderTexture.active = Frame;
                probe.ReadPixels(new Rect(0, 0, FW, FH), 0, 0);
                var px = probe.GetPixels32();
                if (px.Length == 0) return 128;
                double sum = 0; int n = 0;
                for (int i = 0; i < px.Length; i += 97) { sum += (px[i].r + px[i].g + px[i].b) / 3.0; n++; }
                return (float)(sum / n);
            }
            catch (Exception) { return 128; }
            finally { RenderTexture.active = prev; }
        }

        static bool EnginesLit(Vessel v)
        {
            try { return v.FindPartModulesImplementing<ModuleEngines>().Any(e => e.EngineIgnited && !e.flameout && e.finalThrust > 0.1f); }
            catch (Exception) { return false; }
        }

        /// <summary>Cuts to the wide pad camera (or the closest thing to it that can see the rocket) and holds it for liftoff.</summary>
        bool HoldPad(NewsEngine eng, Vessel v, float now, string why)
        {
            var pick = groundCams.FirstOrDefault(c => c.Name == "Pad camera" && Usable(c, v))
                ?? groundCams.FirstOrDefault(c => c.Fixed && Usable(c, v))
                ?? groundCams.FirstOrDefault(c => !c.Fixed && Usable(c, v));
            if (pick == null) return false;
            padHold = true;
            lostSince = -1;
            shotUntil = liftoffAt < 0 ? float.MaxValue : liftoffAt + PadHoldAfterLiftoff;
            if (pick != shot)
            {
                shot = pick;
                eng.StreamCamera(shot.Name, false, now);
                CutLog.Add(now.ToString("0.0") + "s " + shot.Name + " (held for " + why + ", alt " + v.altitude.ToString("0") + " m)");
            }
            return true;
        }

        void PlayerView()
        {
            var main = Camera.allCameras.FirstOrDefault(c => c != null && c.name == "Camera 00");
            if (main != null) PressCamera.RenderInto(Frame, main.transform.position, main.transform.rotation, main.fieldOfView);
        }

        bool NearHome(Vessel v) { return v.mainBody != null && v.mainBody == FlightGlobals.GetHomeBody() && v.altitude < 70000; }

        /// <summary>Chooses the next camera: ground cameras first, hull cameras more and more as the rocket gets far away.</summary>
        void Cut(NewsEngine eng, Vessel v, float now)
        {
            padHold = false;
            var ground = NearHome(v) ? groundCams.Where(c => c != shot && Usable(c, v)).ToList() : new System.Collections.Generic.List<Shot>();
            var hull = hullCams.Where(c => c != shot && Usable(c, v)).ToList();
            // Distance to the nearest tracker: the pad cameras sit right under the climb, so they would say "close" forever.
            var trackers = groundCams.Where(c => !c.Fixed).ToList();
            double far = trackers.Count == 0 ? 1e9 : trackers.Min(c => (Center(v) - CamPos(c, v)).magnitude);
            bool justLifted = liftoffAt >= 0 && now - liftoffAt < 20;
            // Hull cameras take over as the rocket gets 1.5 to 6.5 km further from the trackers than it was on the pad;
            // past that the broadcast stays onboard.
            if (trackStart <= 0 && far < 1e8) trackStart = far;
            double away = far - trackStart;
            double hullOdds = v.situation == Vessel.Situations.PRELAUNCH || v.situation == Vessel.Situations.LANDED || justLifted ? 0.05
                : Math.Max(0.1, Math.Min(1, (away - 1500) / 5000));
            // For the first seconds of the climb the broadcast stays on the ground cameras whenever one can see the rocket.
            if (justLifted) hullOdds = 0;
            if (ground.Count == 0) hullOdds = 1;
            if (hull.Count == 0) hullOdds = 0;
            Shot next = null;
            if (ForceHull && hull.Count > 0) hullOdds = 1;
            if (rng.NextDouble() < hullOdds) next = hull[rng.Next(hull.Count)];
            else if (ground.Count > 0)
            {
                // Right at liftoff the close cameras on the pad get most of the airtime.
                var close = ground.Where(c => c.Fixed).ToList();
                next = justLifted && close.Count > 0 && rng.NextDouble() < 0.6 ? close[rng.Next(close.Count)] : ground[rng.Next(ground.Count)];
            }
            if (next == null && shot != null && Usable(shot, v)) next = shot;
            shot = next;
            lostSince = -1;
            shotUntil = now + (shot != null && shot.Ground ? 4f + (float)rng.NextDouble() * 6f : 6f + (float)rng.NextDouble() * 9f);
            if (shot != null)
            {
                eng.StreamCamera(shot.Name, !shot.Ground, now);
                CutLog.Add(now.ToString("0.0") + "s " + shot.Name + " (alt " + v.altitude.ToString("0") + " m, nearest ground camera " + (far > 1e8 ? "none" : (far / 1000).ToString("0.0") + " km") + ")");
            }
        }

        Vector3d Center(Vessel v) { Vector3d c; float size; PressCamera.Measure(v, out c, out size); return c; }

        Vector3d CamPos(Shot s, Vessel v) { return v.mainBody.GetWorldSurfacePosition(s.Lat, s.Lon, s.Alt); }

        /// <summary>True while this camera can actually see the rocket.</summary>
        bool Usable(Shot s, Vessel v)
        {
            if (s.BadUntil > Now) return false;
            if (!s.Ground) return s.Part != null && s.Part.vessel == v && v.parts != null && v.parts.Contains(s.Part);
            if (!NearHome(v)) return false;
            Vector3d center; float size;
            PressCamera.Measure(v, out center, out size);
            Vector3d cam = CamPos(s, v);
            Vector3d to = center - cam;
            double d = to.magnitude;
            if (s.Fixed)
            {
                // A fixed camera only sees what passes through its frame.
                Vector3d aim = v.mainBody.position + s.AimAt - cam;
                double off = Vector3d.Angle(aim, to);
                if (off > s.Fov * 0.5 * 0.9 || d > 4000) return false;
            }
            else if (d > 90000) return false;
            return PressCamera.ClearView(v.mainBody, cam, center, Math.Min(60, d * 0.3));
        }

        bool Render(Shot s, Vessel v, float now)
        {
            if (s.Ground)
            {
                var body = v.mainBody;
                Vector3d cam = CamPos(s, v);
                Vector3 up = (Vector3)(cam - body.position).normalized;
                Vector3d center; float size;
                PressCamera.Measure(v, out center, out size);
                if (s.Fixed)
                {
                    // Close cameras shake a little when the engines light.
                    float shake = liftoffAt >= 0 && now - liftoffAt < 8 ? 0.35f : 0f;
                    var rot = Quaternion.LookRotation((Vector3)(body.position + s.AimAt - cam), up);
                    if (shake > 0) rot = rot * Quaternion.Euler(Jit(shake), Jit(shake), 0);
                    return PressCamera.RenderInto(Frame, cam, rot, s.Fov);
                }
                // A tracking camera pans with the rocket and zooms as far as its lens goes.
                double d = (center - cam).magnitude;
                float frame = Math.Max(size * 3f, 70f) + (float)v.altitude * 0.03f;
                float fov = Mathf.Clamp((float)(2 * Math.Atan(frame * 0.5 / d) * 180 / Math.PI), s.Fov, 40f);
                return PressCamera.RenderInto(Frame, cam, Quaternion.LookRotation((Vector3)(center - cam), up), fov);
            }
            var t = s.Part.transform;
            Vector3d px = t.right, py = t.up, pz = t.forward;
            Vector3d pos = (Vector3d)t.position + px * s.Offset.x + py * s.Offset.y + pz * s.Offset.z;
            Vector3d look = px * s.Look.x + py * s.Look.y + pz * s.Look.z;
            Vector3d upv = px * s.Up.x + py * s.Up.y + pz * s.Up.z;
            var r = Quaternion.LookRotation((Vector3)look, (Vector3)upv);
            // Buffeting: the hull shakes with air pressure and thrust.
            float buffet = Mathf.Clamp((float)(v.dynamicPressurekPa / 40.0 + Math.Max(0, v.geeForce - 1) * 0.08), 0f, 1.2f);
            if (buffet > 0.02f) r = r * Quaternion.Euler(Jit(buffet), Jit(buffet), 0);
            return PressCamera.RenderInto(Frame, pos, r, s.Fov);
        }

        float Jit(float a) { return (float)(rng.NextDouble() * 2 - 1) * a; }

        /// <summary>Puts cameras around the launch site: two fixed ones at the pad and two long-lens trackers further out.</summary>
        void SetUpGround(Vessel v)
        {
            groundCams.Clear();
            if (v.mainBody == null || v.mainBody != FlightGlobals.GetHomeBody() || v.altitude > 30000) return;
            var body = v.mainBody;
            Vector3d center; float size;
            PressCamera.Measure(v, out center, out size);
            Vector3d up = (center - body.position).normalized;
            Vector3d east = Vector3d.Cross(up, new Vector3d(0, 1, 0));
            if (east.magnitude < 0.1) east = Vector3d.Cross(up, new Vector3d(1, 0, 0));
            east = east.normalized;
            Vector3d north = Vector3d.Cross(east, up).normalized;
            bool onGround = v.situation == Vessel.Situations.PRELAUNCH || v.situation == Vessel.Situations.LANDED || v.situation == Vessel.Situations.SPLASHED;
            Vector3d baseAt = center - up * (size * 0.5);
            if (onGround)
            {
                // A camera a couple of hundred metres out that frames the whole rocket on the pad.
                AddGround(v, "Pad camera", baseAt, east, north, 180 + size * 4, 300 + size * 6, 2, true, center, size, 0);
                // One close to the base, looking up the side of the rocket.
                AddGround(v, "Pad camera 2: base", baseAt, east, north, 35 + size, 60 + size * 2, 1, true, center + up * (size * 0.6), size, 0);
            }
            AddGround(v, "Tracking camera", baseAt, east, north, 3500, 7000, 3, false, center, size, 0.25f);
            AddGround(v, "Long range tracker", baseAt, east, north, 9000, 16000, 3, false, center, size, 0.15f);
        }

        bool AddGround(Vessel v, string name, Vector3d baseAt, Vector3d east, Vector3d north, double minD, double maxD, double height, bool fix, Vector3d aim, float size, float minFov)
        {
            var body = v.mainBody;
            // First look for a spot with nothing in the way at all; failing that, accept one where only buildings or the
            // tower may hide the pad itself (the rocket climbs out above them within seconds).
            for (int i = 0; i < 36; i++)
            {
                double az = rng.NextDouble() * Math.PI * 2, dist = minD + rng.NextDouble() * (maxD - minD);
                Vector3d spot = baseAt + (east * Math.Cos(az) + north * Math.Sin(az)) * dist;
                double lat = body.GetLatitude(spot), lon = body.GetLongitude(spot);
                double alt = Math.Max(0, body.TerrainAltitude(lat, lon, true)) + height;
                Vector3d camAt = body.GetWorldSurfacePosition(lat, lon, alt);
                bool strict = i < 24;
                if (!PressCamera.ClearView(body, camAt, strict ? aim : aim + (aim - body.position).normalized * (size * 2), strict ? Math.Min(60, dist * 0.3) : dist)) continue;
                var s = new Shot { Name = name, Ground = true, Fixed = fix, Tracking = !fix, Lat = lat, Lon = lon, Alt = alt, AimAt = aim - body.position, Fov = minFov };
                if (fix)
                {
                    // Frame the rocket with room above it, so it climbs through the shot before leaving.
                    double d = (aim - camAt).magnitude;
                    s.Fov = Mathf.Clamp((float)(2 * Math.Atan(size * 1.6 / d) * 180 / Math.PI), 20f, 75f);
                }
                groundCams.Add(s);
                return true;
            }
            Debug.Log("[KerbalNews] Stream: no clear spot for the " + name + " (" + minD.ToString("0") + "-" + maxD.ToString("0") + " m out)");
            return false;
        }

        float nextTrackerTry;

        /// <summary>Tracking cameras that could not be placed yet get another try every few seconds, while the craft is low.</summary>
        void RetryTrackers(Vessel v, float now)
        {
            if (now < nextTrackerTry || groundCams.Count(c => !c.Fixed) >= 2 || v.mainBody == null || v.mainBody != FlightGlobals.GetHomeBody() || v.altitude > 20000) return;
            nextTrackerTry = now + 5;
            Vector3d center; float size;
            PressCamera.Measure(v, out center, out size);
            Vector3d up = (center - v.mainBody.position).normalized;
            Vector3d east = Vector3d.Cross(up, new Vector3d(0, 1, 0));
            if (east.magnitude < 0.1) east = Vector3d.Cross(up, new Vector3d(1, 0, 0));
            east = east.normalized;
            Vector3d north = Vector3d.Cross(east, up).normalized;
            Vector3d baseAt = center - up * (size * 0.5);
            if (!groundCams.Any(c => c.Name == "Tracking camera")) AddGround(v, "Tracking camera", baseAt, east, north, 3500, 7000, 3, false, center, size, 0.25f);
            if (!groundCams.Any(c => c.Name == "Long range tracker")) AddGround(v, "Long range tracker", baseAt, east, north, 9000, 16000, 3, false, center, size, 0.15f);
        }

        /// <summary>
        /// Bolts small cameras to the outside of the craft: one high up looking back down the body, one low down looking
        /// up it, and one looking out at the horizon. They are fixed to their parts, so they move and shake with the rocket.
        /// </summary>
        void SetUpHull(Vessel v)
        {
            hullCams.Clear();
            if (v.parts == null || v.parts.Count == 0) return;
            Vector3d center; float size;
            PressCamera.Measure(v, out center, out size);
            Vector3d axis = v.ReferenceTransform != null ? ((Vector3d)v.ReferenceTransform.up).normalized : ((Vector3d)v.transform.up).normalized;
            Part top = null, bottom = null;
            double hi = double.MinValue, lo = double.MaxValue, spread = 0;
            foreach (var p in v.parts)
            {
                if (p == null) continue;
                Vector3d rel = (Vector3d)p.transform.position - center;
                double h = Vector3d.Dot(rel, axis);
                if (h > hi) { hi = h; top = p; }
                if (h < lo) { lo = h; bottom = p; }
                spread = Math.Max(spread, (rel - axis * h).magnitude);
            }
            if (top == null) return;
            // Any side of the rocket will do; the control point's forward axis is a stable choice.
            Vector3d side = v.ReferenceTransform != null ? ((Vector3d)v.ReferenceTransform.forward).normalized : Vector3d.Cross(axis, new Vector3d(1, 0, 0)).normalized;
            double radius = Math.Min(4, Math.Max(0.9, spread * 0.5 + 0.9));
            // Mount the aft-looking camera on the highest fuel tank, below the capsule, so it looks down the stages to the
            // engines and plume instead of at the capsule roof. Without tanks, the part just below the top.
            System.Func<Part, double> height = p => Vector3d.Dot((Vector3d)p.transform.position - center, axis);
            Part high = v.parts.Where(p => p != null && p != top && HoldsFuel(p)).OrderByDescending(height).FirstOrDefault();
            if (high == null) high = v.parts.Where(p => p != null && p != top).OrderByDescending(height).FirstOrDefault() ?? top;
            AddHull("Hull camera: looking aft", high, OnSkin(v, high, side, radius, 0.8), axis * -1 + side * 0.06, side, 75);
            if (bottom != high)
                AddHull("Hull camera: looking up", bottom, OnSkin(v, bottom, side, radius) + axis * 0.5, axis + side * 0.22, side, 62);
            AddHull("Hull camera: horizon", top, OnSkin(v, top, side, radius), side - axis * 0.45, axis, 70);
        }

        static bool HoldsFuel(Part p)
        {
            if (p.Resources == null) return false;
            foreach (PartResource r in p.Resources)
                if (r.maxAmount > 1 && (r.resourceName == "LiquidFuel" || r.resourceName == "Oxidizer" || r.resourceName == "SolidFuel")) return true;
            return false;
        }

        /// <summary>
        /// A spot just outside the part's skin on the given side: a ray from outside finds the hull, so the camera never
        /// ends up inside a wide tank. Without a hit it falls back to a guessed radius.
        /// </summary>
        static Vector3d OnSkin(Vessel v, Part p, Vector3d side, double guess, double standOff = 0.3)
        {
            Vector3d c = p.transform.position;
            try
            {
                const float reach = 25f;
                var hits = Physics.RaycastAll((Vector3)(c + side * reach), (Vector3)(side * -1), reach, 1);   // parts sit on layer 0
                double best = double.MaxValue;
                Vector3d at = c;
                foreach (var h in hits)
                {
                    if (h.collider == null) continue;
                    var hp = h.collider.GetComponentInParent<Part>();
                    if (hp == null || hp.vessel != v || h.distance >= best) continue;
                    best = h.distance; at = h.point;
                }
                if (best < reach) return at + side * standOff;
            }
            catch (Exception) { }
            return c + side * (guess + standOff - 0.3);
        }

        void AddHull(string name, Part p, Vector3d at, Vector3d look, Vector3d up, float fov)
        {
            if (p == null) return;
            var t = p.transform;
            Vector3d px = t.right, py = t.up, pz = t.forward;
            Vector3d rel = at - (Vector3d)t.position;
            look = look.normalized; up = up.normalized;
            hullCams.Add(new Shot
            {
                Name = name, Part = p, Fov = fov,
                Offset = new Vector3d(Vector3d.Dot(rel, px), Vector3d.Dot(rel, py), Vector3d.Dot(rel, pz)),
                Look = new Vector3d(Vector3d.Dot(look, px), Vector3d.Dot(look, py), Vector3d.Dot(look, pz)),
                Up = new Vector3d(Vector3d.Dot(up, px), Vector3d.Dot(up, py), Vector3d.Dot(up, pz)),
            });
        }

        static TelemetrySample Sample(Vessel v)
        {
            var s = new TelemetrySample
            {
                UT = Planetarium.GetUniversalTime(), Altitude = v.altitude, Situation = v.situation.ToString(), Body = v.mainBody != null ? v.mainBody.bodyName : "",
                Speed = v.situation == Vessel.Situations.ORBITING || v.situation == Vessel.Situations.ESCAPING ? v.obt_speed : v.srfSpeed,
                G = v.geeForce, Stage = v.currentStage,
            };
            if (v.orbit != null) { s.Apoapsis = v.orbit.ApA; s.Periapsis = v.orbit.PeA; }
            double amount = 0, max = 0;
            if (v.parts != null)
                foreach (var p in v.parts)
                    foreach (PartResource r in p.Resources)
                        if (r.resourceName == "LiquidFuel" || r.resourceName == "Oxidizer" || r.resourceName == "SolidFuel") { amount += r.amount; max += r.maxAmount; }
            s.FuelPct = max > 0 ? amount / max * 100 : -1;
            var home = FlightGlobals.GetHomeBody();
            if (home != null && v.mainBody != home) s.SignalDelay = (v.GetWorldPos3D() - home.position).magnitude / 299792458.0;
            var crew = v.GetVesselCrew();
            if (crew != null) foreach (var c in crew) s.Crew.Add(c.name);
            return s;
        }
    }
}
