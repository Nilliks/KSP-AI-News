using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KerbalNews.KSP
{
    /// <summary>
    /// The slower-changing things the engineering team reads beside the craft: where the planets are (for transfer
    /// windows), active contracts, science already gathered, and the catalogue of parts the player can build with.
    /// Read by name at run time so a game or mod difference costs one fact, not the page.
    /// </summary>
    static class EngineeringExtras
    {
        public static void Fill(EngSnapshot s)
        {
            Try("phases", () => Phases(s));
            Try("titles", () => Titles(s));
            // Contracts and the science archive change rarely and are long lists: read them every ten seconds at most.
            string key = HighLogic.LoadedScene + "|" + (s.Sheet != null ? s.Sheet.Parts.Count : 0);
            if (kept != null && keptKey == key && Time.realtimeSinceStartup - keptAt < 10f)
            {
                s.Contracts = kept.Contracts; s.Experiments = kept.Experiments; s.ScienceDone = kept.ScienceDone;
                return;
            }
            Try("contracts", () => Contracts(s));
            Try("science", () => Science(s));
            kept = s; keptKey = key; keptAt = Time.realtimeSinceStartup;
        }

        static EngSnapshot kept;
        static string keptKey = "";
        static float keptAt;

        static void Try(string what, Action a) { try { a(); } catch (Exception e) { Debug.LogWarning("[KerbalNews] Engineering " + what + ": " + e.Message); } }

        static void Phases(EngSnapshot s)
        {
            s.UT = Planetarium.GetUniversalTime();
            var home = FlightGlobals.GetHomeBody();
            if (home == null || FlightGlobals.Bodies == null) return;
            // The home planet: home itself, or the planet home orbits when home is a moon.
            var planet = home; int guard = 0;
            while (planet != null && guard++ < 5)
            {
                var parent = CraftReader.Get(planet, "referenceBody") as CelestialBody;
                if (parent == null || parent == planet) return;
                var grand = CraftReader.Get(parent, "referenceBody") as CelestialBody;
                if (grand == null || grand == parent || CraftReader.Get(parent, "orbit") == null) break;
                planet = parent;
            }
            var mine = CraftReader.Get(planet, "orbit") as Orbit;
            var star = CraftReader.Get(planet, "referenceBody") as CelestialBody;
            if (mine == null || star == null) return;
            foreach (var b in FlightGlobals.Bodies)
            {
                if (b == null || b == planet) continue;
                if (CraftReader.Get(b, "referenceBody") as CelestialBody != star) continue;
                var to = CraftReader.Get(b, "orbit") as Orbit;
                if (to == null) continue;
                s.Phases[b.bodyName] = EngineeringCapture.Phase(mine, to, s.UT);
            }
        }

        static void Contracts(EngSnapshot s)
        {
            var system = CraftReader.Static(CraftReader.FindType("Contracts.ContractSystem"), "Instance");
            var list = CraftReader.Get(system, "Contracts") as IEnumerable;
            if (list == null) return;
            foreach (var c in list)
            {
                if (c == null || CraftReader.Str(c, "ContractState") != "Active") continue;
                var ec = new EngContract { Title = CraftReader.Clean(CraftReader.Str(c, "Title")) };
                var pars = CraftReader.Get(c, "AllParameters") as IEnumerable;
                if (pars != null)
                    foreach (var p in pars)
                    {
                        if (p == null) continue;
                        string title = CraftReader.Clean(CraftReader.Str(p, "Title"));
                        if (title == "") continue;
                        ec.Params.Add(new EngContractParam { Text = title, Done = CraftReader.Str(p, "State") == "Complete" });
                        if (ec.Params.Count >= 12) break;
                    }
                if (ec.Title != "") s.Contracts.Add(ec);
                if (s.Contracts.Count >= 30) break;
            }
        }

        static void Science(EngSnapshot s)
        {
            if (s.Sheet == null) return;
            var rnd = CraftReader.FindType("ResearchAndDevelopment");
            if (rnd == null || CraftReader.Static(rnd, "Instance") == null) return;
            var ids = new HashSet<string>();
            foreach (var m in s.Sheet.Parts.SelectMany(p => p.Modules))
            {
                string id = m.Kind == "science" ? m.Label : m.Name == "Experiment" ? (m.T("experiment_id") != "" ? m.T("experiment_id") : m.T("experimentID")) : "";
                if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }
            foreach (string id in ids)
            {
                object exp = CraftReader.Call(rnd, "GetExperiment", id);
                if (exp == null) continue;
                s.Experiments.Add(new EngExperiment
                {
                    Id = id, Title = CraftReader.Clean(CraftReader.Str(exp, "experimentTitle")), Base = CraftReader.Num(exp, "baseValue"), Cap = CraftReader.Num(exp, "scienceCap"),
                    Situations = (int)CraftReader.Num(exp, "situationMask"), Biomes = (int)CraftReader.Num(exp, "biomeMask"),
                });
            }
            if (ids.Count == 0) return;
            var subjects = CraftReader.Call(rnd, "GetSubjects") as IEnumerable;
            if (subjects == null) return;
            foreach (var sub in subjects)
            {
                string id = CraftReader.Str(sub, "id");
                int at = id.IndexOf('@');
                if (at <= 0 || !ids.Contains(id.Substring(0, at))) continue;
                double cap = CraftReader.Num(sub, "scienceCap"), got = CraftReader.Num(sub, "science");
                s.ScienceDone[id] = cap > 0 ? Math.Min(1, got / cap) : 0;
            }
        }

        static List<string> titles;
        static void Titles(EngSnapshot s)
        {
            if (titles == null)
            {
                titles = new List<string>();
                var loaded = PartLoader.LoadedPartsList;
                if (loaded != null) foreach (var ap in loaded) { string t = CraftReader.Clean(ap != null ? ap.title : null); if (t.Length >= 4) titles.Add(t); }
            }
            s.PartTitles = titles;
        }

        // ---- the catalogue of parts the player can build with -----------------------------------------

        static List<CraftPart> catalog;
        static string catalogKey = "";
        static readonly string[] SwapKinds = { "engine", "solar", "wheel", "antenna", "radiator", "chute", "generator" };

        /// <summary>Every unlocked part that could stand in for something on a craft: engines, panels, batteries, reaction
        /// wheels, antennas, radiators, parachutes. Read once and kept until the tech tree changes.</summary>
        public static List<CraftPart> Catalog()
        {
            var loaded = PartLoader.LoadedPartsList;
            if (loaded == null) return new List<CraftPart>();
            var rnd = CraftReader.FindType("ResearchAndDevelopment");
            bool sandbox = HighLogic.CurrentGame == null || HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX || CraftReader.Static(rnd, "Instance") == null;
            Func<AvailablePart, bool> unlocked = ap =>
            {
                if (sandbox) return true;
                object ok = CraftReader.Call(rnd, "PartModelPurchased", ap);
                return !(ok is bool) || (bool)ok;
            };
            int count = 0;
            foreach (var ap in loaded) if (ap != null && unlocked(ap)) count++;
            string key = loaded.Count + "|" + count + "|" + sandbox;
            if (catalog != null && catalogKey == key) return catalog;
            var list = new List<CraftPart>();
            var none = new Dictionary<Part, int>();
            foreach (var ap in loaded)
            {
                if (ap == null || !unlocked(ap)) continue;
                string cat = CraftReader.Str(ap, "category");
                if (cat == "none" || CraftReader.Str(ap, "TechHidden") == "True") continue;
                var prefab = CraftReader.Get(ap, "partPrefab") as Part;
                if (prefab == null || prefab.Modules == null) continue;
                bool useful = false;
                foreach (PartModule m in prefab.Modules) if (m != null && SwapKinds.Contains(CraftReader.KindOfType(m.GetType()))) { useful = true; break; }
                if (!useful && prefab.Resources != null)
                    foreach (PartResource r in prefab.Resources) if (r != null && r.resourceName == CraftSheet.EC && r.maxAmount >= 50) { useful = true; break; }
                if (!useful) continue;
                try
                {
                    var c = CraftReader.ReadLoose(prefab);
                    if (c != null) list.Add(c);
                }
                catch { }
            }
            catalog = list; catalogKey = key;
            return list;
        }
    }
}
