using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace KerbalNews.KSP
{
    /// <summary>
    /// Reads the world for background news: asteroids and comets, eclipses and transfer windows, debris coming down and close
    /// passes, crew milestones, contract companies and part makers, launch counts, tech unlocks, stations and bases, far-off
    /// probes, and Kerbalism space weather. Stock data only (plus Kerbalism through reflection). Every reader is fenced off,
    /// so one failing never stops the others. Notes it needs between scans live in the save (State.WorldMemo).
    /// </summary>
    public static class WorldScanner
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static readonly List<string> LastLog = new List<string>();

        public static List<WorldFact> Scan(NewsState state, double ut, double day)
        {
            var facts = new List<WorldFact>();
            LastLog.Clear();
            if (state == null || HighLogic.CurrentGame == null) return facts;
            if (HighLogic.LoadedScene != GameScenes.FLIGHT && HighLogic.LoadedScene != GameScenes.SPACECENTER && HighLogic.LoadedScene != GameScenes.TRACKSTATION) return facts;
            if (FlightGlobals.Vessels == null || FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0) return facts;
            var memo = state.WorldMemo;
            double last = MemoNum(memo, "lastScan", ut);
            double dt = Math.Max(0, Math.Min(ut - last, 30 * day));
            memo["lastScan"] = ut.ToString("R", Inv);
            Run("sky objects", () => SkyObjects(facts, state, ut));
            Run("windows", () => Windows(facts, ut, day));
            Run("eclipses", () => Eclipses(facts, memo, ut, day));
            Run("safety", () => Safety(facts, state, ut));
            Run("crew", () => Crew(facts, memo, ut, dt, day));
            Run("companies", () => Companies(facts, state));
            Run("records", () => Records(facts, memo, ut));
            Run("tech", () => Tech(facts, state));
            Run("stations", () => Stations(facts, memo, ut, dt, day));
            Run("probes", () => Probes(facts, ut, day));
            Run("kerbalism", () => Kerbalism(facts, memo, ut, day));
            return facts;
        }

        static void Run(string what, Action a)
        {
            try { a(); }
            catch (Exception e) { LastLog.Add(what + " failed: " + e.Message); Debug.LogWarning("[KerbalNews] World scan (" + what + "): " + e.Message); }
        }

        // ------------------------------------------------------------------ helpers

        static double MemoNum(Dictionary<string, string> memo, string k, double def)
        {
            string v; double d;
            return memo.TryGetValue(k, out v) && double.TryParse(v, NumberStyles.Float, Inv, out d) ? d : def;
        }

        internal static string Id(Vessel v) { return v.persistentId.ToString(Inv); }
        internal static string Name(Vessel v) { var n = Names.Of(v); return n != "" ? n : "an unnamed craft"; }
        internal static CelestialBody Home() { var h = FlightGlobals.GetHomeBody(); return h ?? FlightGlobals.Bodies.FirstOrDefault(b => b.isHomeWorld); }
        internal static CelestialBody Sun() { return Planetarium.fetch != null ? Planetarium.fetch.Sun : FlightGlobals.Bodies[0]; }
        static Vector3d Pos(CelestialBody b, double ut) { return b.orbit != null ? b.orbit.getPositionAtUT(ut) : b.position; }
        internal static double[] A(Vector3d v) { return new[] { v.x, v.y, v.z }; }
        static string Km(double m) { return m >= 1e9 ? (m / 1e9).ToString("0.0", Inv) + " million km" : m >= 1e6 ? (m / 1e3).ToString("N0", Inv) + " km" : (m / 1e3).ToString("0", Inv) + " km"; }
        static string Days(double s, double day) { return NewsEngine.FmtDays(s / day); }

        internal static List<string> CrewOf(Vessel v)
        {
            try
            {
                var c = v.loaded ? v.GetVesselCrew() : (v.protoVessel != null ? v.protoVessel.GetVesselCrew() : null);
                return c == null ? new List<string>() : c.Where(k => k != null).Select(k => k.name).ToList();
            }
            catch (Exception) { return new List<string>(); }
        }

        internal static bool Agency(Vessel v)
        {
            return v != null && v.vesselType != VesselType.Debris && v.vesselType != VesselType.SpaceObject && v.vesselType != VesselType.Flag
                && v.vesselType != VesselType.Unknown && v.vesselType != VesselType.EVA && v.vesselType != VesselType.DeployedScienceController
                && v.vesselType != VesselType.DeployedSciencePart && v.vesselType != VesselType.DroppedPart && v.vesselType != VesselType.DeployedGroundPart;
        }

        static bool InSpace(Vessel v, CelestialBody home)
        {
            if (v.mainBody != home) return v.situation != Vessel.Situations.PRELAUNCH;
            return v.situation == Vessel.Situations.ORBITING || v.situation == Vessel.Situations.SUB_ORBITAL || v.situation == Vessel.Situations.ESCAPING;
        }

        static IEnumerable<string> PartNames(Vessel v)
        {
            if (v.protoVessel == null || v.protoVessel.protoPartSnapshots == null) yield break;
            foreach (var p in v.protoVessel.protoPartSnapshots) if (p != null) yield return p.partName ?? "";
        }

        static void Add(List<WorldFact> list, string key, string kind, int sig, string headline, string text, string body = "Kerbin")
        {
            list.Add(new WorldFact { Key = key, Kind = kind, Sig = Math.Max(1, Math.Min(5, sig)), Headline = headline, Text = text, Body = body });
        }

        // ------------------------------------------------------------------ sky

        /// <summary>Asteroids and comets the tracking station knows about that come near home, or that are newly found.</summary>
        static void SkyObjects(List<WorldFact> facts, NewsState state, double ut)
        {
            var home = Home(); var sun = Sun();
            foreach (var v in FlightGlobals.Vessels)
            {
                if (v == null || v.vesselType != VesselType.SpaceObject || v.orbit == null) continue;
                bool comet = PartNames(v).Any(n => n.IndexOf("Comet", StringComparison.OrdinalIgnoreCase) >= 0);
                string size = "";
                try { if (v.DiscoveryInfo != null) size = v.DiscoveryInfo.objectSize.ToString(); } catch (Exception) { }
                bool known = true;
                try { known = v.DiscoveryInfo == null || v.DiscoveryInfo.HaveKnowledgeAbout(DiscoveryLevels.StateVectors); } catch (Exception) { }
                string what = comet ? "comet" : "asteroid";
                string sizeText = size == "" ? "" : " (size class " + size + (size == "E" ? ", the largest kind" : size == "A" ? ", the smallest kind" : "") + ")";
                string nm = Name(v);
                bool nearHome = v.orbit.referenceBody == home;
                bool comingHome = false;
                try { comingHome = v.orbit.patchEndTransition == Orbit.PatchTransitionType.ENCOUNTER && v.orbit.nextPatch != null && v.orbit.nextPatch.referenceBody == home; } catch (Exception) { }
                if (comet)
                {
                    Add(facts, "comet:" + Id(v), "sky", 3, "New comet " + nm + " spotted", "Astronomers have spotted a new comet, " + nm + sizeText + ", in orbit around " + v.orbit.referenceBody.bodyName + "."
                        + (known ? " Its path is now known well enough to track it." : " Its path is still uncertain."), v.orbit.referenceBody.bodyName);
                    if (v.orbit.referenceBody == sun && home.orbit != null)
                    {
                        double r = v.orbit.getRelativePositionAtUT(ut).magnitude, rSoon = v.orbit.getRelativePositionAtUT(ut + 5 * 21600).magnitude;
                        if (r < home.orbit.semiMajorAxis * 1.5 && rSoon < r)
                            Add(facts, "comet:inner:" + Id(v), "sky", 3, nm + " heads for the inner system", "Comet " + nm + " is falling toward the Sun and is now " + Km(r) + " from it, closer than 1.5 times " + home.bodyName + "'s distance. Comets often brighten and grow tails as they warm up.", "Sun");
                    }
                }
                if (nearHome || comingHome)
                {
                    var o = nearHome ? v.orbit : v.orbit.nextPatch;
                    double pe = o.PeA;
                    bool impact = pe < (home.atmosphere ? home.atmosphereDepth : 0);
                    bool insideMun = pe < 12000000;
                    int sig = impact ? (size == "E" || size == "D" ? 5 : 4) : insideMun ? 3 : 2;
                    if (size == "E" && sig < 4) sig++;
                    Add(facts, "roid:" + Id(v) + (impact ? ":impact" : ""), "sky", sig,
                        impact ? "Space rock on course to hit " + home.bodyName : "An " + what + " passes close to " + home.bodyName,
                        "Tracking stations report " + (comet ? "comet " : "asteroid ") + nm + sizeText + (nearHome ? " is inside " + home.bodyName + "'s sphere of influence" : " will enter " + home.bodyName + "'s sphere of influence")
                        + (impact ? ". Its path dips into the atmosphere: it will hit the air at high speed. Small rocks burn up; bigger ones can reach the ground." : ", with a closest approach of " + Km(pe) + " above the surface" + (insideMun ? ", closer than the Mun." : ".")),
                        home.bodyName);
                }
            }
        }

        /// <summary>Hohmann windows from home to the other planets, a few days ahead.</summary>
        static void Windows(List<WorldFact> facts, double ut, double day)
        {
            var home = Home(); var sun = Sun();
            if (home == null || home.orbit == null || home.orbit.referenceBody != sun) return;
            var rh = A(Pos(home, ut) - sun.position);
            var vh = A(Pos(home, ut + 60) - Pos(home, ut));
            var normal = SkyMath.Cross(rh, vh);
            foreach (var b in FlightGlobals.Bodies)
            {
                if (b == null || b == home || b.orbit == null || b.orbit.referenceBody != sun) continue;
                if (b.orbit.eccentricity > 0.3) continue;   // Moho-like oddballs: the simple window math lies
                var rt = A(Pos(b, ut) - sun.position);
                double t = SkyMath.SecondsToWindow(rh, rt, normal, home.orbit.semiMajorAxis, b.orbit.semiMajorAxis, sun.gravParameter);
                if (double.IsNaN(t) || t > 12 * day) continue;
                double syn = SkyMath.Synodic(home.orbit.semiMajorAxis, b.orbit.semiMajorAxis, sun.gravParameter);
                long cycle = (long)Math.Floor((ut + t) / Math.Max(day, syn / 4));
                bool big = b.bodyName == "Duna" || b.bodyName == "Eve" || b.bodyName == "Jool";
                string when = t < day ? "now open" : "opens in about " + Days(t, day) + " days";
                Add(facts, "window:" + b.bodyName + ":" + cycle, "sky", big ? 3 : 2, "Launch window to " + b.bodyName + " " + (t < day ? "opens" : "approaches"),
                    "The launch window from " + home.bodyName + " to " + b.bodyName + " " + when + ". The planets line up like this only about every " + Days(syn, day) + " days, which is when a trip costs the least fuel. Mission planners everywhere watch these dates.", b.bodyName);
            }
        }

        /// <summary>Eclipses seen from home in the next two days. The Mun eclipses the Sun often, so each kind is told at most once a season.</summary>
        static void Eclipses(List<WorldFact> facts, Dictionary<string, string> memo, double ut, double day)
        {
            var home = Home(); var sun = Sun();
            if (home == null) return;
            foreach (var moon in FlightGlobals.Bodies.Where(b => b != null && b.orbit != null && b.orbit.referenceBody == home))
            {
                string found = ""; double at = 0;
                for (double t = ut; t < ut + 2 * day && found == ""; t += 300)
                {
                    var hp = Pos(home, t);
                    found = SkyMath.Eclipse(A(sun.position - hp), sun.Radius, A(Pos(moon, t) - hp), moon.Radius, home.Radius);
                    at = t;
                }
                if (found == "") continue;
                string mk = "eclipseLast:" + moon.bodyName + ":" + found;
                if (ut - MemoNum(memo, mk, -1e12) < 40 * day) continue;
                memo[mk] = ut.ToString("R", Inv);
                string inHours = ((at - ut) / 3600).ToString("0", Inv);
                Add(facts, "eclipse:" + moon.bodyName + ":" + found + ":" + Math.Floor(at / day), "sky", moon.bodyName == "Mun" ? 2 : 3,
                    found == "solar" ? moon.bodyName + " to cross the Sun" : "Eclipse of the " + moon.bodyName,
                    found == "solar" ? "In about " + inHours + " hours the " + moon.bodyName + " will pass in front of the Sun as seen from parts of " + home.bodyName + ", a solar eclipse. Observers are reminded never to look at the Sun directly."
                                     : "In about " + inHours + " hours the " + moon.bodyName + " will slip into " + home.bodyName + "'s shadow, a lunar eclipse visible from the night side.", moon.bodyName);
            }
        }

        // ------------------------------------------------------------------ orbital safety

        static void Safety(List<WorldFact> facts, NewsState state, double ut)
        {
            var home = Home();
            double atmo = home.atmosphere ? home.atmosphereDepth : 0;
            var falling = new List<Vessel>();
            var orbiting = new List<Vessel>();
            foreach (var v in FlightGlobals.Vessels)
            {
                if (v == null || v.orbit == null || v.mainBody != home) continue;
                if (v.situation != Vessel.Situations.ORBITING && v.situation != Vessel.Situations.SUB_ORBITAL) continue;
                if (v.vesselType == VesselType.SpaceObject) continue;
                orbiting.Add(v);
                if (atmo > 0 && v.orbit.PeA < atmo && v.orbit.ApA > atmo && v != FlightGlobals.ActiveVessel && !state.WorldSeen.ContainsKey("reentry:" + Id(v)))
                    falling.Add(v);
            }
            // Debris and dead satellites dipping into the air, grouped into one story.
            if (falling.Count > 0)
            {
                foreach (var v in falling) state.WorldSeen["reentry:" + Id(v)] = ut;
                var debris = falling.Where(v => v.vesselType == VesselType.Debris).ToList();
                var sats = falling.Where(v => v.vesselType != VesselType.Debris && CrewOf(v).Count == 0).ToList();
                if (debris.Count > 0)
                    Add(facts, "reentry:debris:" + Math.Floor(ut / 3600), "safety", debris.Count >= 5 ? 2 : 1, debris.Count == 1 ? "Space junk coming down" : debris.Count + " pieces of space junk coming down",
                        (debris.Count == 1 ? "A piece of debris (" + Name(debris[0]) + ") is" : debris.Count + " pieces of debris, including " + string.Join(", ", debris.Take(3).Select(Name).ToArray()) + ", are")
                        + " on paths that dip into " + home.bodyName + "'s atmosphere and will burn up on re-entry. Most of it will not reach the ground.");
                foreach (var v in sats.Take(2))
                    Add(facts, "reentry:sat:" + Id(v), "safety", 2, Name(v) + " is on its way down",
                        "The uncrewed craft " + Name(v) + " is on a path that dips to " + Km(Math.Max(0, v.orbit.PeA)) + " above " + home.bodyName + ", inside the atmosphere. It will re-enter unless it changes course.");
            }
            // Close passes: crewed craft and stations against everything else, over the next two hours.
            foreach (var a in orbiting.Where(x => x.vesselType == VesselType.Station || (Agency(x) && CrewOf(x).Count > 0)).Take(6))
            {
                if (a.situation != Vessel.Situations.ORBITING) continue;
                foreach (var b in orbiting)
                {
                    if (b == a || b.situation != Vessel.Situations.ORBITING) continue;
                    if (b.orbit.PeA > a.orbit.ApA + 5000 || b.orbit.ApA < a.orbit.PeA - 5000) continue;   // never at the same height
                    double min = double.MaxValue, at = ut;
                    for (double t = ut; t < ut + 7200; t += 20)
                    {
                        double d = (a.orbit.getRelativePositionAtUT(t) - b.orbit.getRelativePositionAtUT(t)).magnitude;
                        if (d < min) { min = d; at = t; }
                    }
                    if (min > 10000 || min < 200) continue;   // under 200 m it's a rendezvous, not a scare
                    string pair = string.CompareOrdinal(Id(a), Id(b)) < 0 ? Id(a) + "-" + Id(b) : Id(b) + "-" + Id(a);
                    bool crewed = CrewOf(a).Count > 0;
                    Add(facts, "pass:" + pair + ":" + Math.Floor(at / 21600), "safety", crewed ? 3 : 2, "Close call in orbit for " + Name(a),
                        "Trackers predict " + (b.vesselType == VesselType.Debris ? "a piece of debris (" + Name(b) + ")" : Name(b)) + " will pass within " + (min / 1000).ToString("0.0", Inv) + " km of "
                        + Name(a) + (crewed ? ", which has crew aboard," : "") + " in about " + ((at - ut) / 60).ToString("0", Inv) + " minutes. A miss, but a reminder of how crowded " + home.bodyName + " orbit is getting.");
                }
            }
        }

        // ------------------------------------------------------------------ crew

        static readonly int[] CrewDayMarks = { 10, 30, 100, 200, 365, 500, 1000 };

        static void Crew(List<WorldFact> facts, Dictionary<string, string> memo, double ut, double dt, double day)
        {
            var home = Home();
            var inSpace = new Dictionary<string, Vessel>();
            foreach (var v in FlightGlobals.Vessels)
                if (v != null && Agency(v) && InSpace(v, home))
                    foreach (var c in CrewOf(v)) inSpace[c] = v;
            foreach (var kv in inSpace)
            {
                string k = "space:" + kv.Key;
                double before = MemoNum(memo, k, 0), now = before + dt / day;
                memo[k] = now.ToString("0.###", Inv);
                foreach (int m in CrewDayMarks)
                    if (before < m && now >= m)
                        Add(facts, "crewdays:" + kv.Key + ":" + m, "crew", m >= 365 ? 3 : 2, kv.Key + " passes " + m + " days in space",
                            kv.Key + " has now spent " + m + " days in space in total, and is currently aboard " + Name(kv.Value) + (kv.Value.mainBody != home ? " at " + kv.Value.mainBody.bodyName : "") + ".", kv.Value.mainBody.bodyName);
                if (!memo.ContainsKey("flown:" + kv.Key))
                {
                    memo["flown:" + kv.Key] = "1";
                    Add(facts, "rookie:" + kv.Key, "crew", 2, kv.Key + " earns their wings", kv.Key + " has made a first trip to space, aboard " + Name(kv.Value) + ". The rookie joins the short list of kerbals who have flown.");
                }
            }
            // Kerbonauts who left the corps (fired or retired by a mod), but not those lost.
            var game = HighLogic.CurrentGame;
            if (game == null || game.CrewRoster == null) return;
            var roster = game.CrewRoster.Crew.Where(c => c != null && c.type == ProtoCrewMember.KerbalType.Crew).ToList();
            var lost = roster.Where(c => c.rosterStatus == ProtoCrewMember.RosterStatus.Dead || c.rosterStatus == ProtoCrewMember.RosterStatus.Missing).Select(c => c.name);
            var names = roster.Select(c => c.name).ToList();
            string prev;
            if (memo.TryGetValue("roster", out prev))
                foreach (var gone in prev.Split('|').Where(x => x != "" && !names.Contains(x)))
                {
                    double days = MemoNum(memo, "space:" + gone, 0);
                    Add(facts, "left:" + gone, "crew", days > 30 ? 3 : 2, gone + " leaves the astronaut corps",
                        gone + " is no longer on the agency's astronaut roster." + (days > 0 ? " Over a career they spent " + NewsEngine.FmtDays(days) + " days in space." : " They never flew."));
                }
            memo["roster"] = string.Join("|", names.Except(lost).ToArray());
        }

        // ------------------------------------------------------------------ companies

        static void Companies(List<WorldFact> facts, NewsState state)
        {
            var home = Home();
            // Part makers whose hardware reaches space, or another world, for the first time.
            foreach (var v in FlightGlobals.Vessels)
            {
                if (v == null || !Agency(v) || !InSpace(v, home) || v.protoVessel == null || v.protoVessel.protoPartSnapshots == null) continue;
                foreach (var g in v.protoVessel.protoPartSnapshots.Where(p => p != null && p.partInfo != null && !string.IsNullOrEmpty(p.partInfo.manufacturer)).GroupBy(p => p.partInfo.manufacturer))
                {
                    string maker = g.Key, where = v.mainBody == home ? "space" : v.mainBody.bodyName;
                    string part = g.First().partInfo.title;
                    Add(facts, "maker:" + maker + ":" + where, "company", where == "space" ? 1 : 2, maker + " celebrates hardware " + (where == "space" ? "in space" : "at " + where),
                        maker + ", maker of the " + part + ", says its hardware is now " + (where == "space" ? "flying in space" : "at " + where) + " aboard " + Name(v) + ". The company is using it in its advertising.", v.mainBody.bodyName);
                }
            }
            // Big contracts on offer, with the company behind them.
            var cs = Contracts.ContractSystem.Instance;
            if (cs == null || cs.Contracts == null) return;
            int n = 0;
            foreach (var c in cs.Contracts)
            {
                if (c == null || c.ContractState != Contracts.Contract.State.Offered || n >= 2) continue;
                if (c.Prestige != Contracts.Contract.ContractPrestige.Exceptional && c.FundsCompletion < 150000) continue;
                string agent = c.Agent != null ? c.Agent.Name : "";
                if (agent == "") continue;
                n++;
                Add(facts, "contract:" + c.ContractGuid, "company", 2, agent + " wants the agency's help",
                    agent + " has put out a contract the agency can take: \"" + Names.Of(c.Title) + "\", worth " + c.FundsCompletion.ToString("N0", Inv) + " funds on completion. The agency has not said whether it will accept.");
            }
        }

        // ------------------------------------------------------------------ records

        static readonly int[] LaunchMarks = { 10, 25, 50, 100, 150, 200, 300, 500, 1000 };

        static void Records(List<WorldFact> facts, Dictionary<string, string> memo, double ut)
        {
            var seen = new HashSet<string>((memo.ContainsKey("launched") ? memo["launched"] : "").Split('|').Where(x => x != ""));
            int before = (int)MemoNum(memo, "launchCount", seen.Count);
            int count = before;
            foreach (var v in FlightGlobals.Vessels)
            {
                if (v == null || !Agency(v) || v.situation == Vessel.Situations.PRELAUNCH) continue;
                if (v.launchTime <= 0 || seen.Contains(Id(v))) continue;
                seen.Add(Id(v)); count++;
            }
            // Keep the id list short; the count carries on.
            memo["launched"] = string.Join("|", seen.Skip(Math.Max(0, seen.Count - 400)).ToArray());
            memo["launchCount"] = count.ToString(Inv);
            foreach (int m in LaunchMarks)
                if (before < m && count >= m)
                    Add(facts, "launches:" + m, "record", m >= 100 ? 3 : 2, "The agency's " + Ordinal(m) + " flight",
                        "By the tracking station's count, the agency has now flown " + m + " craft. Record keepers say few would have predicted that pace.");
        }

        static string Ordinal(int n) { int t = n % 100; return n + (t >= 11 && t <= 13 ? "th" : n % 10 == 1 ? "st" : n % 10 == 2 ? "nd" : n % 10 == 3 ? "rd" : "th"); }

        // ------------------------------------------------------------------ tech

        static void Tech(List<WorldFact> facts, NewsState state)
        {
            if (ResearchAndDevelopment.Instance == null || HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX || PartLoader.LoadedPartsList == null) return;
            foreach (var g in PartLoader.LoadedPartsList.Where(p => p != null && !string.IsNullOrEmpty(p.TechRequired)).GroupBy(p => p.TechRequired))
            {
                if (state.WorldSeen.ContainsKey("tech:" + g.Key)) continue;
                if (ResearchAndDevelopment.GetTechnologyState(g.Key) != RDTech.State.Available) continue;
                var parts = g.Select(p => p.title).Where(t => !string.IsNullOrEmpty(t)).Take(4).ToList();
                if (parts.Count == 0) continue;
                string title = TechTitle(g.Key);
                Add(facts, "tech:" + g.Key, "tech", 2, "Agency unlocks " + title,
                    "The agency's research has unlocked " + title + ", giving its engineers new hardware: " + string.Join(", ", parts.ToArray()) + (g.Count() > parts.Count ? " and more" : "") + ". The agency has not said what it plans to build with it.");
            }
        }

        static string TechTitle(string id)
        {
            try
            {
                var m = typeof(ResearchAndDevelopment).GetMethod("GetTechnologyTitle", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                var t = m != null ? m.Invoke(null, new object[] { id }) as string : null;
                if (!string.IsNullOrEmpty(t)) return Names.Of(t);
            }
            catch (Exception) { }
            var sb = new System.Text.StringBuilder();
            foreach (char ch in id) { if (char.IsUpper(ch) && sb.Length > 0) sb.Append(' '); sb.Append(sb.Length == 0 ? char.ToUpperInvariant(ch) : char.ToLowerInvariant(ch)); }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ stations and bases

        static readonly int[] AgeMarks = { 30, 100, 365, 730, 1000, 1500 };
        static readonly int[] StayMarks = { 5, 10, 30, 100, 365 };

        static void Stations(List<WorldFact> facts, Dictionary<string, string> memo, double ut, double dt, double day)
        {
            var home = Home();
            foreach (var v in FlightGlobals.Vessels)
            {
                if (v == null || !Agency(v)) continue;
                bool station = v.vesselType == VesselType.Station && v.situation == Vessel.Situations.ORBITING;
                bool baseThere = (v.vesselType == VesselType.Base || v.vesselType == VesselType.Lander) && v.mainBody != home && (v.situation == Vessel.Situations.LANDED || v.situation == Vessel.Situations.SPLASHED);
                var crew = CrewOf(v);
                if (station || v.vesselType == VesselType.Base)
                {
                    double age = (ut - v.launchTime) / day, ageBefore = (ut - dt - v.launchTime) / day;
                    if (v.launchTime > 0)
                        foreach (int m in AgeMarks)
                            if (ageBefore < m && age >= m)
                                Add(facts, "age:" + Id(v) + ":" + m, "base", m >= 365 ? 3 : 2, Name(v) + " marks " + m + " days",
                                    Name(v) + (station ? ", the agency's station in orbit around " : ", the agency's base on ") + v.mainBody.bodyName + ", has now been up for " + m + " days." + (crew.Count > 0 ? " Aboard now: " + string.Join(", ", crew.ToArray()) + "." : " Nobody is aboard right now."), v.mainBody.bodyName);
                    // Crew rotations.
                    string k = "crewOn:" + Id(v), was;
                    string nowCrew = string.Join("|", crew.OrderBy(x => x).ToArray());
                    if (memo.TryGetValue(k, out was) && was != nowCrew && crew.Count > 0)
                    {
                        var arrived = crew.Where(c => !was.Split('|').Contains(c)).ToList();
                        var left = was.Split('|').Where(c => c != "" && !crew.Contains(c)).ToList();
                        if (arrived.Count > 0)
                            Add(facts, "rotation:" + Id(v) + ":" + Math.Floor(ut / day), "base", 2, "New crew at " + Name(v),
                                string.Join(", ", arrived.ToArray()) + (arrived.Count == 1 ? " has" : " have") + " arrived at " + Name(v) + " (" + v.mainBody.bodyName + ")" + (left.Count > 0 ? ", taking over from " + string.Join(", ", left.ToArray()) : "") + ". Crew aboard now: " + crew.Count + ".", v.mainBody.bodyName);
                    }
                    memo[k] = nowCrew;
                }
                // Long stays on other worlds.
                if (baseThere && crew.Count > 0)
                {
                    string k = "stay:" + Id(v);
                    double before = MemoNum(memo, k, 0), now = before + dt / day;
                    memo[k] = now.ToString("0.###", Inv);
                    foreach (int m in StayMarks)
                        if (before < m && now >= m)
                        {
                            double best = MemoNum(memo, "bestStay:" + v.mainBody.bodyName, 0);
                            bool record = now > best;
                            if (record) memo["bestStay:" + v.mainBody.bodyName] = now.ToString("0.###", Inv);
                            Add(facts, k + ":" + m, record ? "record" : "base", m >= 30 ? 3 : 2, (record ? "Record stay on " : m + " days on ") + v.mainBody.bodyName,
                                string.Join(", ", crew.ToArray()) + " have now lived on " + v.mainBody.bodyName + " for " + m + " days at " + Name(v) + "." + (record ? " That is the longest anyone has stayed there." : ""), v.mainBody.bodyName);
                        }
                }
                else if (memo.ContainsKey("stay:" + Id(v))) memo.Remove("stay:" + Id(v));
            }
        }

        // ------------------------------------------------------------------ far probes

        static void Probes(List<WorldFact> facts, double ut, double day)
        {
            var home = Home(); var sun = Sun();
            if (home == null || home.orbit == null) return;
            var planets = FlightGlobals.Bodies.Where(b => b != null && b.orbit != null && b.orbit.referenceBody == sun && b.orbit.semiMajorAxis > home.orbit.semiMajorAxis).OrderBy(b => b.orbit.semiMajorAxis).ToList();
            foreach (var v in FlightGlobals.Vessels)
            {
                if (v == null || !Agency(v) || v.orbit == null || CrewOf(v).Count > 0) continue;
                if (v.mainBody != sun) continue;
                double r = (v.GetWorldPos3D() - sun.position).magnitude;
                if (r < home.orbit.semiMajorAxis * 2) continue;
                foreach (var p in planets.Where(p => r > p.orbit.semiMajorAxis))
                    Add(facts, "probe:" + Id(v) + ":past:" + p.bodyName, "probe", p == planets.LastOrDefault() ? 4 : 2, Name(v) + " passes the orbit of " + p.bodyName,
                        "The agency's probe " + Name(v) + " is now " + Km(r) + " from the Sun, farther out than " + p.bodyName + "'s orbit." + (p == planets.LastOrDefault() ? " Nothing the agency has built has ever been this far." : ""), "Sun");
                if (v.launchTime > 0)
                {
                    int age = (int)((ut - v.launchTime) / day);
                    int bucket = age / 200;
                    if (bucket >= 1)
                        Add(facts, "probe:" + Id(v) + ":age:" + bucket, "probe", 1, Name(v) + ", " + (bucket * 200) + " days out",
                            Name(v) + " has been flying for " + (bucket * 200) + " days and is now " + Km(r) + " from the Sun" + (v.situation == Vessel.Situations.ESCAPING ? ", on a path that leaves the solar system for good." : ".") + " It is a long way from home.", "Sun");
                }
                if (v.situation == Vessel.Situations.ESCAPING)
                    Add(facts, "probe:" + Id(v) + ":escape", "probe", 4, Name(v) + " is leaving the solar system", "The agency's probe " + Name(v) + " is moving fast enough to escape the Sun's pull. It will never come back.", "Sun");
            }
        }

        // ------------------------------------------------------------------ Kerbalism

        static Type kerbalismApi; static bool kerbalismLooked;

        static Type KerbalismApi()
        {
            if (kerbalismLooked) return kerbalismApi;
            kerbalismLooked = true;
            try
            {
                foreach (var la in AssemblyLoader.loadedAssemblies)
                {
                    var t = la.assembly.GetType("KERBALISM.API");
                    if (t != null) { kerbalismApi = t; break; }
                }
            }
            catch (Exception) { }
            return kerbalismApi;
        }

        static object Call(Type t, string name, Vessel v)
        {
            var m = t.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Vessel) }, null);
            return m == null ? null : m.Invoke(null, new object[] { v });
        }

        /// <summary>Solar storms and crew radiation, when Kerbalism is installed.</summary>
        static void Kerbalism(List<WorldFact> facts, Dictionary<string, string> memo, double ut, double day)
        {
            var api = KerbalismApi();
            if (api == null) return;
            LastLog.Add("Kerbalism API found");
            var home = Home();
            var crewed = FlightGlobals.Vessels.Where(v => v != null && Agency(v) && InSpace(v, home) && CrewOf(v).Count > 0).ToList();
            var probe = crewed.FirstOrDefault() ?? FlightGlobals.Vessels.FirstOrDefault(v => v != null && Agency(v) && InSpace(v, home));
            if (probe != null)
            {
                bool now = Call(api, "StormInProgress", probe) as bool? ?? false;
                bool soon = Call(api, "StormIncoming", probe) as bool? ?? false;
                long slot = (long)Math.Floor(ut / (2 * day));
                if (now) Add(facts, "storm:" + slot, "mod", crewed.Count > 0 ? 4 : 3, "Solar storm hits",
                    "A solar storm is under way: a burst of charged particles from the Sun is washing over " + probe.mainBody.bodyName + "'s neighborhood. Radiation outside the magnetic field is far above normal."
                    + (crewed.Count > 0 ? " Crews in space (" + string.Join(", ", crewed.Take(3).Select(Name).ToArray()) + ") are expected to shelter." : ""), probe.mainBody.bodyName);
                else if (soon) Add(facts, "stormwarn:" + slot, "mod", 2, "Solar storm warning",
                    "Space weather forecasters warn that a solar storm is on its way and should arrive within days. Radio blackouts and high radiation are possible for craft outside the magnetic field.", probe.mainBody.bodyName);
            }
            foreach (var v in crewed.Take(4))
            {
                object r = Call(api, "Radiation", v);
                double rad = r is double ? (double)r : double.NaN;
                if (double.IsNaN(rad)) continue;
                double perHour = rad * 3600;   // Kerbalism reports rad/s
                if (perHour < 0.01) continue;
                Add(facts, "rad:" + Id(v) + ":" + Math.Floor(ut / (10 * day)), "mod", 2, "Radiation worries for " + Name(v) + "'s crew",
                    "Instruments aboard " + Name(v) + " show radiation of about " + (perHour * 1000).ToString("0", Inv) + " millirad per hour, high enough that doctors are tracking the crew's total dose: " + string.Join(", ", CrewOf(v).ToArray()) + ".", v.mainBody.bodyName);
            }
        }
    }
}
