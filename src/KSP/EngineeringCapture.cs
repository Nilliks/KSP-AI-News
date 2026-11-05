using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KerbalNews.KSP
{
    /// <summary>
    /// Reads what the engineering team can see: the craft in flight or in the editor, its orbit, target and the game's own
    /// delta-v readout. Called when the director sends a message or opens the page, never every frame.
    /// </summary>
    static class EngineeringCapture
    {
        public static EngSnapshot Snapshot()
        {
            var s = new EngSnapshot();
            s.StockSystem = IsStock();
            var scene = HighLogic.LoadedScene;
            if (scene == GameScenes.EDITOR) { Editor(s); return s; }
            if (scene == GameScenes.FLIGHT && FlightGlobals.ActiveVessel != null) { Flight(s, FlightGlobals.ActiveVessel); return s; }
            s.Scene = scene == GameScenes.TRACKSTATION ? "tracking" : "spacecenter";
            Fleet(s);
            return s;
        }

        static bool IsStock()
        {
            var bodies = FlightGlobals.Bodies;
            if (bodies == null) return true;
            var kerbin = bodies.FirstOrDefault(b => b != null && b.bodyName == "Kerbin");
            return kerbin != null && Math.Abs(kerbin.Radius - 600000) < 1 && bodies.Any(b => b != null && b.bodyName == "Duna");
        }

        static void Editor(EngSnapshot s)
        {
            s.Scene = EditorDriver.editorFacility == EditorFacility.SPH ? "sph" : "vab";
            var ship = EditorLogic.fetch != null ? EditorLogic.fetch.ship : null;
            if (ship == null || ship.parts == null || ship.parts.Count == 0) { s.Situation = "nothing on the floor yet"; return; }
            s.Vessel = string.IsNullOrEmpty(ship.shipName) ? "Untitled craft" : ship.shipName;
            s.Situation = "being built in the " + (s.Scene == "sph" ? "SPH" : "VAB");
            s.Parts = ship.parts.Count;
            double mass = 0; int cap = 0;
            foreach (var p in ship.parts) { if (p == null) continue; mass += p.mass + p.GetResourceMass(); cap += p.CrewCapacity; }
            s.Mass = mass; s.CrewCapacity = cap;
            Home(s);
            Stages(s, ship.vesselDeltaV, false);
        }

        static void Home(EngSnapshot s)
        {
            var home = FlightGlobals.GetHomeBody();
            if (home != null) Body(s, home);
        }

        static void Body(EngSnapshot s, CelestialBody b)
        {
            s.Body = b.bodyName;
            s.BodyMu = b.gravParameter;
            s.BodyRadius = b.Radius;
            s.BodyGravity = b.GeeASL * 9.80665;
            s.BodyAtmosphere = b.atmosphere ? b.atmosphereDepth : 0;
            s.BodySoi = double.IsInfinity(b.sphereOfInfluence) ? 0 : b.sphereOfInfluence;
        }

        static void Flight(EngSnapshot s, Vessel v)
        {
            s.Scene = MapView.MapIsEnabled ? "map" : "flight";
            s.Vessel = Names.Of(v);
            s.Parts = v.parts != null ? v.parts.Count : 0;
            s.Mass = v.GetTotalMass();
            s.Crew = v.GetCrewCount(); s.CrewCapacity = v.GetCrewCapacity();
            s.CurrentStage = v.currentStage;
            s.Altitude = v.altitude; s.Speed = v.obt_speed;
            if (v.mainBody != null) { Body(s, v.mainBody); s.Situation = Situation(v); }
            var o = v.orbit;
            if (o != null && v.situation != Vessel.Situations.LANDED && v.situation != Vessel.Situations.SPLASHED && v.situation != Vessel.Situations.PRELAUNCH)
            {
                s.HasOrbit = true;
                s.ApA = o.ApA; s.PeA = o.PeA; s.Inclination = o.inclination; s.Eccentricity = o.eccentricity;
                s.Period = o.eccentricity < 1 ? o.period : 0; s.Sma = o.semiMajorAxis;
            }
            Target(s, v);
            Stages(s, v.VesselDeltaV, true);
        }

        static string Situation(Vessel v)
        {
            string b = v.mainBody.bodyName;
            switch (v.situation)
            {
                case Vessel.Situations.PRELAUNCH: return "on the launch pad";
                case Vessel.Situations.LANDED: return "landed on " + b;
                case Vessel.Situations.SPLASHED: return "splashed down on " + b;
                case Vessel.Situations.FLYING: return "flying in " + b + "'s atmosphere";
                case Vessel.Situations.SUB_ORBITAL: return "suborbital over " + b;
                case Vessel.Situations.ORBITING: return "orbiting " + b;
                case Vessel.Situations.ESCAPING: return "escaping " + b;
                case Vessel.Situations.DOCKED: return "docked, orbiting " + b;
                default: return "near " + b;
            }
        }

        static void Target(EngSnapshot s, Vessel v)
        {
            var t = FlightGlobals.fetch != null ? FlightGlobals.fetch.VesselTarget : null;
            if (t == null || v.orbit == null) return;
            var to = t.GetOrbit();
            s.Target = t.GetName() ?? "";
            double ut = Planetarium.GetUniversalTime();
            var tb = t as CelestialBody;
            if (tb != null) s.Target = tb.bodyName;
            if (to == null) return;
            try { s.TargetDistance = (v.GetWorldPos3D() - to.getPositionAtUT(ut)).magnitude; } catch { }

            // Walk the craft's orbit up until it shares a parent with the target (Kerbin orbit to Mun, or Kerbin's orbit to Duna).
            var mine = v.orbit;
            int hops = 0;
            while (mine != null && to.referenceBody != mine.referenceBody && mine.referenceBody != null && mine.referenceBody.orbit != null && hops < 4)
            { mine = mine.referenceBody.orbit; hops++; }
            if (mine == null || mine.referenceBody != to.referenceBody) return;
            s.Interplanetary = hops > 0;
            s.TransferParent = to.referenceBody.bodyName;
            s.TransferMu = to.referenceBody.gravParameter;
            s.TransferR1 = mine.semiMajorAxis;
            s.TransferR2 = to.semiMajorAxis;
            s.RelInclination = Vector3d.Angle(mine.GetOrbitNormal(), to.GetOrbitNormal());
            s.PhaseAngle = Phase(mine, to, ut);
        }

        /// <summary>Phase angle with the target ahead, 0-360. The angle alone has no sign, so it checks a minute later whether
        /// the gap is closing (target ahead of a faster inner craft) or opening; that avoids any handedness confusion.</summary>
        static double Phase(Orbit mine, Orbit target, double ut)
        {
            double a0 = Vector3d.Angle(mine.getRelativePositionAtUT(ut), target.getRelativePositionAtUT(ut));
            double a1 = Vector3d.Angle(mine.getRelativePositionAtUT(ut + 60), target.getRelativePositionAtUT(ut + 60));
            bool inner = mine.semiMajorAxis < target.semiMajorAxis;
            bool closing = a1 < a0;
            // Inner craft: a closing gap means the target is ahead. Outer craft: a closing gap means it is behind.
            bool ahead = inner ? closing : !closing;
            return ahead ? a0 : 360 - a0;
        }

        static void Stages(EngSnapshot s, VesselDeltaV dv, bool flight)
        {
            if (dv == null || !dv.IsReady || dv.OperatingStageInfo == null) return;
            s.DeltaVReady = true;
            foreach (var st in dv.OperatingStageInfo)
            {
                if (st == null || (st.deltaVinVac < 0.5 && st.thrustVac <= 0)) continue;
                s.Stages.Add(new EngStage
                {
                    Stage = st.stage, DvVac = st.deltaVinVac, DvAsl = st.deltaVatASL, DvActual = st.deltaVActual,
                    TwrVac = st.TWRVac, TwrAsl = st.TWRASL, TwrActual = st.TWRActual,
                    StartMass = st.startMass, EndMass = st.endMass, BurnTime = st.stageBurnTime, ThrustVac = st.thrustVac, IspVac = st.ispVac,
                });
            }
            s.TotalDvVac = dv.TotalDeltaVVac; s.TotalDvAsl = dv.TotalDeltaVASL; s.TotalDvActual = flight ? dv.TotalDeltaVActual : dv.TotalDeltaVVac;
        }

        static void Fleet(EngSnapshot s)
        {
            Home(s);
            var fs = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.flightState : null;
            if (fs == null || fs.protoVessels == null) return;
            foreach (var pv in fs.protoVessels)
            {
                if (pv == null) continue;
                var t = pv.vesselType;
                if (t == VesselType.Debris || t == VesselType.SpaceObject || t == VesselType.Flag || t == VesselType.EVA || t == VesselType.Unknown
                    || t == VesselType.DeployedScienceController || t == VesselType.DeployedSciencePart || t == VesselType.DroppedPart || t == VesselType.DeployedGroundPart) continue;
                string body = "";
                if (pv.orbitSnapShot != null && FlightGlobals.Bodies != null && pv.orbitSnapShot.ReferenceBodyIndex >= 0 && pv.orbitSnapShot.ReferenceBodyIndex < FlightGlobals.Bodies.Count)
                    body = FlightGlobals.Bodies[pv.orbitSnapShot.ReferenceBodyIndex].bodyName;
                var crew = pv.GetVesselCrew();
                s.Vessels.Add(Names.Of(pv.vesselName) + ": " + pv.situation.ToString().ToLowerInvariant().Replace('_', ' ') + (body != "" ? " at " + body : "") + (crew != null && crew.Count > 0 ? ", " + crew.Count + " crew" : ""));
                if (s.Vessels.Count >= 25) break;
            }
        }
    }
}
