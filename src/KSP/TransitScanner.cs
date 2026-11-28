using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace KerbalNews.KSP
{
    /// <summary>
    /// Mission watch: finds the agency's craft that are coasting toward another world and when they get there.
    /// The game only works out encounters for the craft being flown, so this follows each craft's present orbit forward
    /// against the worlds that share its parent (the planets for a craft circling the Sun, the moons for one circling a planet)
    /// until it crosses into a sphere of influence. An orbit that hasn't changed is not worked out twice.
    /// </summary>
    public static class TransitScanner
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static string LastLog = "";

        class Found { public string Orbit; public CelestialBody Dest; public double Arrival = double.NaN, PeR = double.NaN; }
        static readonly Dictionary<string, Found> cache = new Dictionary<string, Found>();
        static List<Transit> last;
        static float lastReal = -100;
        static Game lastGame;

        /// <summary>Craft in transit right now, or null when the game can't be read at the moment.</summary>
        public static List<Transit> Scan(double ut)
        {
            if (HighLogic.CurrentGame == null) return null;
            if (HighLogic.LoadedScene != GameScenes.FLIGHT && HighLogic.LoadedScene != GameScenes.SPACECENTER && HighLogic.LoadedScene != GameScenes.TRACKSTATION) return null;
            if (FlightGlobals.Vessels == null || FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0) return null;
            if (lastGame != HighLogic.CurrentGame) { lastGame = HighLogic.CurrentGame; cache.Clear(); last = null; }
            // At high time warp the news engine asks many times a second; the answer barely moves in a few real seconds.
            if (last != null && Time.realtimeSinceStartup - lastReal < 5 && Time.realtimeSinceStartup >= lastReal)
                return last.Where(t => t.ArrivalUT > ut).ToList();
            var list = new List<Transit>();
            var home = WorldScanner.Home();
            int worked = 0, failed = 0;
            var alive = new HashSet<string>();
            foreach (var v in FlightGlobals.Vessels)
            {
                try
                {
                    if (v == null || !WorldScanner.Agency(v) || v.orbit == null || v.mainBody == null) continue;
                    if (v.situation != Vessel.Situations.ORBITING && v.situation != Vessel.Situations.ESCAPING) continue;
                    string id = WorldScanner.Id(v);
                    alive.Add(id);
                    var f = Find(v, id, ut, ref worked);
                    if (f == null || f.Dest == null || double.IsNaN(f.Arrival) || f.Arrival <= ut) continue;
                    var d = f.Dest;
                    var t = new Transit
                    {
                        Id = id, Name = WorldScanner.Name(v), Kind = v.vesselType.ToString(), From = v.mainBody.bodyName, Dest = d.bodyName,
                        Crew = WorldScanner.CrewOf(v), ArrivalUT = f.Arrival, LaunchUT = v.launchTime, Homecoming = d == home,
                        DistKm = (v.GetWorldPos3D() - d.position).magnitude / 1000,
                        HomeKm = home != null ? (v.GetWorldPos3D() - home.position).magnitude / 1000 : 0,
                    };
                    if (!double.IsNaN(f.PeR))
                    {
                        t.PeKm = (f.PeR - d.Radius) / 1000;
                        t.Impact = f.PeR <= d.Radius;
                        t.Atmosphere = !t.Impact && d.atmosphere && f.PeR < d.Radius + d.atmosphereDepth;
                    }
                    list.Add(t);
                }
                catch (Exception e) { failed++; if (failed == 1) Debug.LogWarning("[KerbalNews] Mission watch: " + e.Message); }
            }
            foreach (var gone in cache.Keys.Where(k => !alive.Contains(k)).ToList()) cache.Remove(gone);
            LastLog = list.Count + " craft in transit" + (list.Count > 0 ? " (" + string.Join("; ", list.Take(6).Select(t => t.Name + " to " + t.Dest + " in " + ((t.ArrivalUT - ut) / 21600).ToString("0.#", Inv) + " days").ToArray()) + ")" : "")
                + ", " + worked + " orbits worked out" + (failed > 0 ? ", " + failed + " failed" : "");
            last = list; lastReal = Time.realtimeSinceStartup;
            return list;
        }

        /// <summary>Where this craft's present orbit takes it. Worked out once per orbit; a few craft per scan at most, the rest next time.</summary>
        static Found Find(Vessel v, string id, double ut, ref int worked)
        {
            var o = v.orbit;
            string sig = v.mainBody.bodyName + "|" + o.semiMajorAxis.ToString("G9", Inv) + "|" + o.eccentricity.ToString("G9", Inv) + "|" + o.inclination.ToString("G7", Inv) + "|" + o.LAN.ToString("G7", Inv)
                + "|" + o.argumentOfPeriapsis.ToString("G7", Inv);
            Found f;
            if (cache.TryGetValue(id, out f) && f.Orbit == sig && (f.Dest == null || f.Arrival > ut)) return f;
            if (worked >= 8) return f;   // over budget this scan: the last answer stands until next time
            worked++;
            f = new Found { Orbit = sig };
            cache[id] = f;
            var parent = v.mainBody;
            double mu = parent.gravParameter;
            if (mu <= 0 || o.PeR <= 0 || double.IsNaN(o.semiMajorAxis) || o.semiMajorAxis == 0) return f;
            bool closed = o.eccentricity < 1 && o.semiMajorAxis > 0;
            bool star = parent.orbit == null || parent.referenceBody == null || parent.referenceBody == parent;
            double leave = star ? double.MaxValue : parent.sphereOfInfluence;
            double span = star ? 9.2e7 : 60 * 21600.0;   // ten Kerbin years around the Sun, sixty days around a planet
            if (closed && o.period > 0 && !double.IsInfinity(o.period)) span = Math.Min(span, o.period);
            double vCraft = Math.Sqrt(Math.Max(0, mu * (2 / o.PeR - 1 / o.semiMajorAxis)));
            Func<double, double[]> craft = t => WorldScanner.A(o.getRelativePositionAtUT(t));
            foreach (var b in FlightGlobals.Bodies)
            {
                if (b == null || b == parent || b.orbit == null || b.orbit.referenceBody != parent || b.sphereOfInfluence <= 0) continue;
                var bo = b.orbit;
                // Never at the same distance from the parent: no meeting.
                if (o.PeR > bo.ApR + b.sphereOfInfluence) continue;
                if (closed && o.ApR < bo.PeR - b.sphereOfInfluence) continue;
                double vBody = Math.Sqrt(Math.Max(0, mu * (2 / bo.PeR - 1 / bo.semiMajorAxis)));
                Func<double, double[]> body = t => WorldScanner.A(bo.getRelativePositionAtUT(t));
                double until = double.IsNaN(f.Arrival) ? ut + span : f.Arrival;
                double at = SkyMath.FirstWithin(craft, body, b.sphereOfInfluence, vCraft + vBody, ut, until, leave);
                if (double.IsNaN(at)) continue;
                f.Arrival = at; f.Dest = b;
                // The path past the destination, from how the craft comes in.
                var r = SkyMath.Sub(craft(at), body(at));
                var r2 = SkyMath.Sub(craft(at + 1), body(at + 1));
                f.PeR = SkyMath.PeriapsisRadius(r, SkyMath.Sub(r2, r), b.gravParameter);
            }
            return f;
        }
    }
}
