using System;
using KerbalNews;
using UnityEngine;

namespace KerbalNews.KSP
{
    /// <summary>
    /// The engineering team's flight recorder: twice a second it notes the active craft's attitude, speed and control inputs
    /// (with SAS state), and it logs what the game reports happening to parts (hitting the ground, overheating, joints breaking,
    /// staging). A crash investigation reads this record, so the team can say "full right yaw for six seconds" instead of guessing.
    /// </summary>
    public class BlackBox
    {
        readonly Func<NewsEngine> engine;
        float nextSample;

        public BlackBox(Func<NewsEngine> engine) { this.engine = engine; }

        static double UT { get { return Planetarium.GetUniversalTime(); } }
        static string Id(Vessel v) { return v.persistentId.ToString(); }
        static string F(double d, string fmt = "0") { return d.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture); }
        static string Signed(float f) { return (f >= 0 ? "+" : "") + f.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture); }

        public void Register()
        {
            GameEvents.onCrash.Add(OnCrash);
            GameEvents.onCrashSplashdown.Add(OnSplashCrash);
            GameEvents.onOverheat.Add(OnOverheat);
            GameEvents.onPartJointBreak.Add(OnJointBreak);
            GameEvents.onStageActivate.Add(OnStage);
            GameEvents.onPartDie.Add(OnPartDie);
        }

        public void Unregister()
        {
            GameEvents.onCrash.Remove(OnCrash);
            GameEvents.onCrashSplashdown.Remove(OnSplashCrash);
            GameEvents.onOverheat.Remove(OnOverheat);
            GameEvents.onPartJointBreak.Remove(OnJointBreak);
            GameEvents.onStageActivate.Remove(OnStage);
            GameEvents.onPartDie.Remove(OnPartDie);
        }

        /// <summary>Called every frame from NewsSystem in flight; samples twice a second.</summary>
        public void Update()
        {
            if (Time.realtimeSinceStartup < nextSample) return;
            nextSample = Time.realtimeSinceStartup + 0.5f;
            var eng = engine();
            var v = FlightGlobals.ActiveVessel;
            if (eng == null || v == null || v.packed || !eng.Settings.EngineeringOn) return;
            if (v.situation == Vessel.Situations.PRELAUNCH || v.situation == Vessel.Situations.LANDED && v.srfSpeed < 1) return;
            try { eng.BlackBoxSample(Id(v), Sample(v)); }
            catch (Exception e) { Debug.LogWarning("[KerbalNews] Flight recorder: " + e.Message); nextSample = Time.realtimeSinceStartup + 10; }
        }

        static string Sample(Vessel v)
        {
            Vector3d up = v.upAxis;
            Vector3d fwd = v.ReferenceTransform != null ? (Vector3d)v.ReferenceTransform.up : up;
            double pitch = 90 - Vector3d.Angle(fwd, up);
            Vector3d axis = v.mainBody != null ? (Vector3d)v.mainBody.transform.up : new Vector3d(0, 1, 0);
            Vector3d north = Vector3d.Exclude(up, axis).normalized;
            Vector3d east = Vector3d.Cross(up, north);
            Vector3d flat = Vector3d.Exclude(up, fwd);
            double heading = flat.magnitude > 1e-3 ? (Math.Atan2(Vector3d.Dot(flat, east), Vector3d.Dot(flat, north)) * 180 / Math.PI + 360) % 360 : -1;
            double aoa = v.srfSpeed > 5 ? Vector3d.Angle(fwd, v.srf_velocity) : 0;
            var c = v.ctrlState;
            var input = FlightInputHandler.state;
            bool sas = v.ActionGroups != null && v.ActionGroups[KSPActionGroup.SAS];
            string sasMode = sas && v.Autopilot != null ? " (" + v.Autopilot.Mode + ")" : "";
            double ut = UT;
            return "T" + F(ut, "0.0") + ": " + v.situation.ToString().ToLowerInvariant().Replace('_', ' ')
                + ", alt " + F(v.altitude) + " m" + (v.radarAltitude < 5000 ? " (above ground " + F(v.radarAltitude) + " m)" : "")
                + ", speed " + F(v.srfSpeed) + " m/s, vertical " + F(v.verticalSpeed) + " m/s"
                + ", pitch " + F(pitch) + "°" + (heading >= 0 ? ", heading " + F(heading) + "°" : "") + (aoa > 0 ? ", angle of attack " + F(aoa) + "°" : "")
                + ", spin " + F(v.angularVelocity.magnitude * 180 / Math.PI) + "°/s"
                + (c != null ? ", ctrl pitch " + Signed(c.pitch) + " yaw " + Signed(c.yaw) + " roll " + Signed(c.roll) + ", throttle " + F(c.mainThrottle * 100) + "%" : "")
                + (input != null && (Math.Abs(input.pitch) > 0.05f || Math.Abs(input.yaw) > 0.05f || Math.Abs(input.roll) > 0.05f) ? ", input pitch " + Signed(input.pitch) + " yaw " + Signed(input.yaw) + " roll " + Signed(input.roll) : ", no pilot input")
                + ", SAS " + (sas ? "on" + sasMode : "off")
                + ", " + F(v.geeForce, "0.0") + " g, q " + F(v.dynamicPressurekPa, "0.0") + " kPa, " + (v.parts != null ? v.parts.Count : 0) + " parts";
        }

        static string PartName(Part p) { return p != null && p.partInfo != null ? p.partInfo.title : "a part"; }

        void Event(Part p, string what)
        {
            var eng = engine();
            if (eng == null || p == null || p.vessel == null || !eng.Settings.EngineeringOn) return;
            var v = p.vessel;
            eng.BlackBoxEvent(Id(v), "T" + F(UT, "0.0") + ": " + what + " at " + F(v.srfSpeed) + " m/s", UT);
        }

        void OnCrash(EventReport r) { if (r != null) Event(r.origin, PartName(r.origin) + " hit " + (string.IsNullOrEmpty(r.other) ? "the ground" : r.other)); }
        void OnSplashCrash(EventReport r) { if (r != null) Event(r.origin, PartName(r.origin) + " was destroyed hitting the water"); }
        void OnOverheat(EventReport r) { if (r != null) Event(r.origin, PartName(r.origin) + " overheated and exploded"); }
        void OnJointBreak(PartJoint j, float force)
        {
            if (j == null || j.Child == null) return;
            Event(j.Child, "the joint between " + PartName(j.Child) + " and " + PartName(j.Parent) + " broke (" + F(force) + " kN)");
        }
        void OnPartDie(Part p) { Event(p, PartName(p) + " was destroyed"); }
        void OnStage(int stage)
        {
            var v = FlightGlobals.ActiveVessel;
            if (v != null && v.rootPart != null) Event(v.rootPart, "stage " + stage + " activated");
        }
    }
}
