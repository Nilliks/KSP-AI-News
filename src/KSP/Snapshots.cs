using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KerbalNews.KSP
{
    /// <summary>What the public can see of a craft and of the universe, read from stock game data only.</summary>
    public static class Snapshots
    {
        public static CraftSnapshot Craft(Vessel v)
        {
            var s = new CraftSnapshot();
            if (v == null) return s;
            try
            {
                s.VesselName = v.vesselName;
                s.VesselType = v.vesselType.ToString();
                s.Parts = v.parts.Count;
                s.MassTons = Math.Round(v.GetTotalMass(), 1);
                s.LaunchSite = SiteName(v.landedAt);

                int maxStage = 0, crewCap = 0, radialEngines = 0, wings = 0, pods = 0;
                float lo = float.MaxValue, hi = float.MinValue;
                Vector3 up = v.transform != null ? v.transform.up : Vector3.up;
                var engines = new Dictionary<string, int>();
                foreach (var p in v.parts)
                {
                    maxStage = Math.Max(maxStage, p.inverseStage);
                    crewCap += p.CrewCapacity;
                    if (p.FindModuleImplementing<ModuleCommand>() != null && p.CrewCapacity > 0) pods++;
                    if (p.FindModuleImplementing<ModuleLiftingSurface>() != null) wings++;
                    if (p.FindModuleImplementing<ModuleEngines>() != null)
                    {
                        string title = p.partInfo != null ? p.partInfo.title : p.name;
                        int n;
                        engines.TryGetValue(title, out n);
                        engines[title] = n + 1;
                        if (p.attachMode == AttachModes.SRF_ATTACH) radialEngines++;
                    }
                    float h = Vector3.Dot(p.transform.position, up);
                    lo = Math.Min(lo, h);
                    hi = Math.Max(hi, h);
                }
                s.Stages = maxStage + 1;
                s.CrewCapacity = crewCap;
                s.HeightM = hi > lo ? Math.Round(hi - lo + 2, 0) : 0;
                s.Engines = string.Join(", ", engines.OrderByDescending(kv => kv.Value).Take(6).Select(kv => kv.Key + " x" + kv.Value).ToArray());
                try
                {
                    if (v.VesselDeltaV != null) s.DeltaV = Math.Round(v.VesselDeltaV.TotalDeltaVVac, 0);
                }
                catch (Exception) { /* delta-v app not ready on this vessel */ }
                foreach (var c in v.GetVesselCrew()) s.Crew.Add(c.name);
                s.Description = Describe(s, radialEngines, wings, pods);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[KerbalNews] Craft snapshot incomplete: " + e.Message);
            }
            return s;
        }

        static readonly string[] Words = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten" };
        static string Num(int n) { return n >= 0 && n < Words.Length ? Words[n] : n.ToString(); }

        static string Describe(CraftSnapshot s, int radial, int wings, int pods)
        {
            bool plane = wings >= 3 && s.LaunchSite.Contains("runway");
            string kind = plane ? "spaceplane" : s.VesselType == "Rover" ? "rover" : "rocket";
            string d = "A " + (s.Stages > 1 && !plane ? Num(s.Stages) + "-stage " : "") + kind;
            var bits = new List<string>();
            if (radial > 0) bits.Add(Num(radial) + " radially mounted booster" + (radial == 1 ? "" : "s"));
            if (pods > 0 && s.CrewCapacity > 0) bits.Add("crew accommodation for " + Num(s.CrewCapacity));
            else bits.Add("no crew");
            return d + " with " + string.Join(" and ", bits.ToArray()) + ".";
        }

        public static string SiteName(string landedAt)
        {
            if (string.IsNullOrEmpty(landedAt)) return "";
            if (landedAt.IndexOf("LaunchPad", StringComparison.OrdinalIgnoreCase) >= 0) return "the KSC launch pad";
            if (landedAt.IndexOf("Runway", StringComparison.OrdinalIgnoreCase) >= 0) return "the KSC runway";
            return landedAt.Replace("_", " ");
        }

        // ------------------------------------------------------------ world digest

        struct VesselInfo
        {
            public VesselType Type;
            public Vessel.Situations Situation;
            public string Body;
            public List<string> Crew;
        }

        static IEnumerable<VesselInfo> AllVessels()
        {
            if (HighLogic.LoadedSceneIsFlight && FlightGlobals.Vessels != null && FlightGlobals.Vessels.Count > 0)
            {
                foreach (var v in FlightGlobals.Vessels)
                {
                    if (v == null) continue;
                    yield return new VesselInfo
                    {
                        Type = v.vesselType, Situation = v.situation,
                        Body = v.mainBody != null ? v.mainBody.bodyName : "",
                        Crew = v.GetVesselCrew().Select(c => c.name).ToList(),
                    };
                }
                yield break;
            }
            var game = HighLogic.CurrentGame;
            if (game == null || game.flightState == null) yield break;
            foreach (var pv in game.flightState.protoVessels)
            {
                if (pv == null) continue;
                string body = "";
                try
                {
                    int idx = pv.orbitSnapShot != null ? pv.orbitSnapShot.ReferenceBodyIndex : -1;
                    if (idx >= 0 && idx < FlightGlobals.Bodies.Count) body = FlightGlobals.Bodies[idx].bodyName;
                }
                catch (Exception) { }
                yield return new VesselInfo
                {
                    Type = pv.vesselType, Situation = pv.situation, Body = body,
                    Crew = pv.GetVesselCrew().Select(c => c.name).ToList(),
                };
            }
        }

        static bool InSpace(VesselInfo v)
        {
            if (v.Body != "Kerbin") return v.Situation != Vessel.Situations.PRELAUNCH;
            return v.Situation == Vessel.Situations.ORBITING || v.Situation == Vessel.Situations.SUB_ORBITAL || v.Situation == Vessel.Situations.ESCAPING;
        }

        public static WorldDigest Digest(NewsState state)
        {
            var d = new WorldDigest();
            try
            {
                foreach (var v in AllVessels())
                {
                    if (v.Type == VesselType.Flag || v.Type == VesselType.SpaceObject || v.Type == VesselType.Unknown) continue;
                    if (v.Type == VesselType.Debris)
                    {
                        if (v.Situation == Vessel.Situations.LANDED || v.Situation == Vessel.Situations.SPLASHED) continue;
                        d.Debris++;
                        Bump(d.DebrisByBody, v.Body);
                        continue;
                    }
                    Bump(d.VesselsByBody, v.Body);
                    bool orbiting = v.Situation == Vessel.Situations.ORBITING;
                    if ((v.Type == VesselType.Probe || v.Type == VesselType.Relay) && orbiting) d.Satellites++;
                    if (v.Type == VesselType.Station && orbiting) d.Stations++;
                    if (v.Type == VesselType.Base && (v.Situation == Vessel.Situations.LANDED || v.Situation == Vessel.Situations.SPLASHED)) d.Bases++;
                    if (v.Crew.Count > 0 && InSpace(v))
                    {
                        d.CrewedVessels++;
                        d.CrewInSpace += v.Crew.Count;
                        d.CrewInSpaceNames.AddRange(v.Crew);
                    }
                }
                var game = HighLogic.CurrentGame;
                if (game != null && game.CrewRoster != null)
                    foreach (var c in game.CrewRoster.Crew)
                    {
                        if (c.rosterStatus == ProtoCrewMember.RosterStatus.Dead || c.rosterStatus == ProtoCrewMember.RosterStatus.Missing) d.CrewLost++;
                        else d.CrewTotal++;
                    }
                if (game != null && game.Mode == Game.Modes.CAREER)
                {
                    var parts = new List<string>();
                    if (Funding.Instance != null) parts.Add("Agency budget: " + Funding.Instance.Funds.ToString("N0") + " funds");
                    if (Reputation.Instance != null) parts.Add("public standing score " + Reputation.Instance.reputation.ToString("0"));
                    if (ResearchAndDevelopment.Instance != null) parts.Add(ResearchAndDevelopment.Instance.Science.ToString("0") + " science banked");
                    if (parts.Count > 0) d.Career = string.Join(", ", parts.ToArray()) + ".";
                }
                if (state != null) d.Records.AddRange(state.Records);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[KerbalNews] World digest incomplete: " + e.Message);
            }
            return d;
        }

        static void Bump(Dictionary<string, int> d, string k)
        {
            if (string.IsNullOrEmpty(k)) k = "unknown";
            int n;
            d.TryGetValue(k, out n);
            d[k] = n + 1;
        }
    }
}
