using System;
using System.Linq;
using UnityEngine;

namespace KerbalNews.KSP
{
    /// <summary>
    /// Feeds the livestream in flight: telemetry every few seconds, staging and explosions as moments, and a small
    /// video feed. Near the home world the feed is a long-lens tracking camera on the ground, like a real launch
    /// broadcast; higher up it is the player's own view. Leaving flight ends the stream.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class StreamDriver : MonoBehaviour
    {
        const int FW = 640, FH = 360;
        public static RenderTexture Frame { get; private set; }

        float nextSample, nextFrame, lastBoom;
        string liftedFor = "";
        string trackerFor = "";
        double trackLat, trackLon, trackAlt;
        bool haveTracker;
        readonly System.Random rng = new System.Random();

        static NewsEngine Eng { get { var s = NewsSystem.Instance; return s != null && s.HasGame ? s.Engine : null; } }

        void Start()
        {
            GameEvents.onStageActivate.Add(OnStage);
            GameEvents.onPartDie.Add(OnPartDie);
        }

        void OnDestroy()
        {
            GameEvents.onStageActivate.Remove(OnStage);
            GameEvents.onPartDie.Remove(OnPartDie);
            var eng = Eng;
            if (eng != null && eng.Stream != null) eng.EndStream(Planetarium.GetUniversalTime());
            if (Frame != null) { Frame.Release(); Destroy(Frame); Frame = null; }
        }

        Vessel Streamed()
        {
            var eng = Eng;
            if (eng == null || eng.Stream == null) return null;
            string id = eng.Stream.VesselId;
            var v = FlightGlobals.ActiveVessel;
            if (v != null && v.persistentId.ToString() == id) return v;
            return FlightGlobals.Vessels == null ? null : FlightGlobals.Vessels.FirstOrDefault(x => x != null && x.loaded && x.persistentId.ToString() == id);
        }

        void OnStage(int stage)
        {
            var eng = Eng;
            if (eng == null || eng.Stream == null || Streamed() != FlightGlobals.ActiveVessel) return;
            eng.StreamMoment("stage " + stage + " activated", Time.realtimeSinceStartup);
        }

        void OnPartDie(Part p)
        {
            var eng = Eng;
            if (eng == null || eng.Stream == null || p == null || p.vessel == null) return;
            if (p.vessel.persistentId.ToString() != eng.Stream.VesselId) return;
            if (Time.realtimeSinceStartup - lastBoom < 6) return;
            lastBoom = Time.realtimeSinceStartup;
            eng.StreamMoment("a part of the vessel exploded", Time.realtimeSinceStartup);
        }

        void Update()
        {
            var eng = Eng;
            if (eng == null || eng.Stream == null) return;
            float now = Time.realtimeSinceStartup;
            eng.SetRealTime(now);
            var v = Streamed();
            if (v != null && now > nextSample)
            {
                nextSample = now + 3f;
                // Liftoff is when the craft actually leaves the ground, not when the first stage fires.
                string vid = v.persistentId.ToString();
                if (liftedFor != vid && v.situation == Vessel.Situations.FLYING && v.srfSpeed > 3)
                {
                    liftedFor = vid;
                    eng.StreamMoment("liftoff", now);
                }
                try { eng.StreamSample(Sample(v), now); }
                catch (Exception e) { Debug.LogWarning("[KerbalNews] Stream telemetry: " + e.Message); }
            }
            eng.TickStream(now, !eng.WarpHold && TimeWarp.CurrentRate <= 1.01f);   // the stream chat waits out a time warp too
        }

        void LateUpdate()
        {
            var eng = Eng;
            var br = Browser.Instance;
            if (eng == null || eng.Stream == null || br == null || !br.Showing || !br.Url.StartsWith(KerbalNews.Web.Sites.LiveHost)) return;
            if (MapView.MapIsEnabled || Time.realtimeSinceStartup < nextFrame) return;
            nextFrame = Time.realtimeSinceStartup + 0.33f;
            try { DrawFrame(eng); }
            catch (Exception e) { Debug.LogWarning("[KerbalNews] Stream video: " + e.Message); nextFrame = Time.realtimeSinceStartup + 5; }
        }

        void DrawFrame(NewsEngine eng)
        {
            if (Frame == null) { Frame = new RenderTexture(FW, FH, 24); Frame.Create(); }
            var v = Streamed();
            if (v != null && v.mainBody != null && v.mainBody == FlightGlobals.GetHomeBody() && v.altitude < 60000 && Tracker(v))
            {
                var body = v.mainBody;
                Vector3d cam = body.GetWorldSurfacePosition(trackLat, trackLon, trackAlt);
                Vector3d center; float size;
                PressCamera.Measure(v, out center, out size);
                double d = (center - cam).magnitude;
                float frame = Math.Max(size * 3f, 70f) + (float)v.altitude * 0.03f;
                float fov = Mathf.Clamp((float)(2 * Math.Atan(frame * 0.5 / d) * 180 / Math.PI), 0.2f, 40f);
                Vector3 up = (Vector3)(cam - body.position).normalized;
                PressCamera.RenderInto(Frame, cam, Quaternion.LookRotation((Vector3)(center - cam), up), fov);
                return;
            }
            // Onboard or chase view: the player's own camera.
            var main = Camera.allCameras.FirstOrDefault(c => c != null && c.name == "Camera 00");
            if (main != null) PressCamera.RenderInto(Frame, main.transform.position, main.transform.rotation, main.fieldOfView);
        }

        /// <summary>Places a tracking camera a few kilometres from the craft, once per streamed vessel, with a clear view.</summary>
        bool Tracker(Vessel v)
        {
            string id = v.persistentId.ToString();
            if (trackerFor == id) return haveTracker;
            trackerFor = id;
            haveTracker = false;
            var body = v.mainBody;
            Vector3d center; float size;
            PressCamera.Measure(v, out center, out size);
            Vector3d up = (center - body.position).normalized;
            Vector3d east = Vector3d.Cross(up, new Vector3d(0, 1, 0));
            if (east.magnitude < 0.1) east = Vector3d.Cross(up, new Vector3d(1, 0, 0));
            east = east.normalized;
            Vector3d north = Vector3d.Cross(east, up).normalized;
            for (int i = 0; i < 10 && !haveTracker; i++)
            {
                double az = rng.NextDouble() * Math.PI * 2, dist = 3500 + rng.Next(3500);
                Vector3d spot = center + (east * Math.Cos(az) + north * Math.Sin(az)) * dist;
                double lat = body.GetLatitude(spot), lon = body.GetLongitude(spot);
                double alt = Math.Max(0, body.TerrainAltitude(lat, lon, true)) + 3;
                Vector3d camAt = body.GetWorldSurfacePosition(lat, lon, alt);
                if (!PressCamera.ClearView(body, camAt, center)) continue;
                if (v.situation == Vessel.Situations.PRELAUNCH || v.situation == Vessel.Situations.LANDED)
                    if (!PressCamera.ClearView(body, camAt, center - up * 15, 20)) continue;
                trackLat = lat; trackLon = lon; trackAlt = alt;
                haveTracker = true;
            }
            return haveTracker;
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
            var crew = v.GetVesselCrew();
            if (crew != null) foreach (var c in crew) s.Crew.Add(c.name);
            return s;
        }
    }
}
