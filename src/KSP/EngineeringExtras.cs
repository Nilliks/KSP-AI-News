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

        static void Try(string what, Action a) { string was = Crumb.Enter(what); try { a(); } catch (Exception e) { Debug.LogWarning("[KerbalNews] Engineering " + what + ": " + e.Message); } finally { Crumb.Exit(was); } }

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
            // Checking what is unlocked walks every loaded part, so an open page does that twice a minute at most.
            if (catalog != null && Time.realtimeSinceStartup - catalogAt < 30f && catalogScene == HighLogic.LoadedScene) return catalog;
            string was = Crumb.Enter("parts catalogue");
            float t0 = Time.realtimeSinceStartup;
            try { return CatalogInner(); }
            finally
            {
                Crumb.Exit(was);
                catalogAt = Time.realtimeSinceStartup; catalogScene = HighLogic.LoadedScene;
                if (catalogAt - t0 > 0.5f) Debug.LogWarning("[KerbalNews] Slow parts catalogue: " + (catalogAt - t0).ToString("0.0") + " s");
            }
        }

        static float catalogAt;
        static GameScenes catalogScene;

        static List<CraftPart> CatalogInner()
        {
            string key; var open = Unlocked(out key);
            if (catalog != null && catalogKey == key) return catalog;
            if (pending == null || pendingKey != key) Begin(open, key);
            Step(int.MaxValue);
            return catalog ?? new List<CraftPart>();
        }

        // Reading a part from its prefab does not depend on what is unlocked, so each is read once per game session.
        static readonly Dictionary<string, CraftPart> readParts = new Dictionary<string, CraftPart>();
        static List<AvailablePart> pending;
        static List<CraftPart> building;
        static string pendingKey = "";
        static int pendingAt;
        /// <summary>Parts the catalogue could not read (no prefab, or the read threw).</summary>
        public static int unread;

        /// <summary>Every part the player can pick in the builder right now: unlocked (and bought, where the save asks for that),
        /// not hidden. The key changes when that set does.</summary>
        static List<AvailablePart> Unlocked(out string key)
        {
            var open = new List<AvailablePart>();
            var loaded = PartLoader.LoadedPartsList;
            key = "";
            if (loaded == null) return open;
            var rnd = CraftReader.FindType("ResearchAndDevelopment");
            bool sandbox = HighLogic.CurrentGame == null || HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX;
            // A career whose tech tree is not loaded in this scene: keep the last reading rather than call everything unlocked.
            if (!sandbox && CraftReader.Static(rnd, "Instance") == null) { key = catalog != null ? catalogKey : "tech tree not loaded"; return open; }
            foreach (var ap in loaded)
            {
                if (ap == null) continue;
                if (!sandbox) { object ok = CraftReader.Call(rnd, "PartModelPurchased", ap); if (ok is bool && !(bool)ok) continue; }
                string cat = CraftReader.Str(ap, "category");
                if (cat == "none" || CraftReader.Str(ap, "TechHidden") == "True") continue;
                open.Add(ap);
            }
            key = loaded.Count + "|" + open.Count + "|" + sandbox;
            return open;
        }

        static void Begin(List<AvailablePart> open, string key) { pending = open; pendingKey = key; pendingAt = 0; building = new List<CraftPart>(); }

        /// <summary>Reads up to <paramref name="fresh"/> parts not read before; true when the catalogue is complete.</summary>
        static bool Step(int fresh)
        {
            if (pending == null) return true;
            while (pendingAt < pending.Count)
            {
                var ap = pending[pendingAt];
                CraftPart c;
                if (!readParts.TryGetValue(ap.name, out c))
                {
                    if (fresh-- <= 0) return false;
                    c = null;
                    try
                    {
                        var prefab = CraftReader.Get(ap, "partPrefab") as Part;
                        if (prefab != null && prefab.Modules != null) { c = CraftReader.ReadLoose(prefab); ModProbe.AddProcesses(c); }
                        else unread++;
                    }
                    catch (Exception e) { unread++; if (unread <= 3) Debug.LogWarning("[KerbalNews] Catalogue could not read " + ap.name + ": " + e.Message); }
                    readParts[ap.name] = c;
                }
                if (c != null) building.Add(c);
                pendingAt++;
            }
            catalog = building; catalogKey = pendingKey; pending = null; building = null;
            catalogAt = Time.realtimeSinceStartup; catalogScene = HighLogic.LoadedScene;
            return true;
        }

        /// <summary>Builds the catalogue a few parts a frame after a scene loads, so the first question to the team does not
        /// stop the game while thousands of modded parts are read.</summary>
        public static IEnumerator Warm()
        {
            yield return new WaitForSeconds(4f);
            string key; List<AvailablePart> open;
            try { open = Unlocked(out key); } catch { yield break; }
            if (catalog != null && catalogKey == key) yield break;
            if (pending == null || pendingKey != key) Begin(open, key);
            float t0 = Time.realtimeSinceStartup; int n = open.Count;
            while (pending != null && pendingKey == key)
            {
                string was = Crumb.Enter("parts catalogue (background)");
                try { if (Step(15)) break; } catch { break; } finally { Crumb.Exit(was); }
                yield return null;
            }
            if (catalog != null && catalogKey == key) Debug.Log("[KerbalNews] Parts catalogue ready: " + catalog.Count + " of " + n + " unlocked parts read in the background over " + (Time.realtimeSinceStartup - t0).ToString("0.0") + " s");
        }
    
        // ---- part 4: locked parts, science in hand, relays, saved craft ---------------------------------

        static List<CraftPart> locked;
        static string lockedKey = "";

        /// <summary>Parts still locked in the tech tree, each with the node that unlocks it. Empty in sandbox.</summary>
        public static List<CraftPart> Locked()
        {
            var loaded = PartLoader.LoadedPartsList;
            var rnd = CraftReader.FindType("ResearchAndDevelopment");
            if (loaded == null || HighLogic.CurrentGame == null || HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX || CraftReader.Static(rnd, "Instance") == null) return new List<CraftPart>();
            var open = new List<AvailablePart>();
            foreach (var ap in loaded)
            {
                if (ap == null) continue;
                object ok = CraftReader.Call(rnd, "PartModelPurchased", ap);
                if (ok is bool && !(bool)ok) open.Add(ap);
            }
            string key = loaded.Count + "|" + open.Count;
            if (locked != null && lockedKey == key) return locked;
            // Node titles and costs come from the tech tree file the game loaded.
            var nodes = new Dictionary<string, KeyValuePair<string, double>>();
            try
            {
                var assets = CraftReader.Static(CraftReader.FindType("AssetBase"), "RnDTechTree");
                var techs = CraftReader.Call(assets, "GetTreeTechs") as IEnumerable;
                if (techs != null)
                    foreach (var t in techs)
                        nodes[CraftReader.Str(t, "techID")] = new KeyValuePair<string, double>(CraftReader.Clean(CraftReader.Str(t, "title")), CraftReader.Num(t, "scienceCost"));
            }
            catch { }
            var list = new List<CraftPart>();
            foreach (var ap in open)
            {
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
                    if (c == null) continue;
                    c.Tech = CraftReader.Str(ap, "TechRequired");
                    KeyValuePair<string, double> node;
                    if (nodes.TryGetValue(c.Tech, out node)) { c.TechTitle = node.Key; c.TechCost = node.Value; }
                    if (c.Tech != "") list.Add(c);
                }
                catch { }
            }
            locked = list; lockedKey = key;
            return list;
        }

        public static double SciencePoints()
        {
            var rnd = CraftReader.Static(CraftReader.FindType("ResearchAndDevelopment"), "Instance");
            return rnd == null ? -1 : CraftReader.Num(rnd, "Science", -1);
        }

        static List<EngRelay> relays;
        static float relaysAt;

        /// <summary>Craft already flying that carry relay antennas, with their combined relay power.</summary>
        public static List<EngRelay> Relays()
        {
            // Going through every craft's parts is slow with a big fleet, and relays do not change by the second.
            if (relays != null && Time.realtimeSinceStartup - relaysAt < 15f) return relays;
            var list = new List<EngRelay>();
            relays = list; relaysAt = Time.realtimeSinceStartup;
            var vessels = FlightGlobals.Vessels;
            if (vessels == null) return list;
            foreach (var v in vessels)
            {
                if (v == null || v == FlightGlobals.ActiveVessel) continue;
                string type = v.vesselType.ToString();
                if (type == "Debris" || type == "SpaceObject" || type == "EVA" || type == "Flag" || type == "Unknown") continue;
                var pv = CraftReader.Get(v, "protoVessel");
                var parts = CraftReader.Get(pv, "protoPartSnapshots") as IEnumerable;
                if (parts == null) continue;
                var mods = new List<CraftModule>();
                foreach (var pp in parts)
                {
                    var prefab = CraftReader.Get(CraftReader.Get(pp, "partInfo"), "partPrefab") as Part;
                    if (prefab == null || prefab.Modules == null) continue;
                    foreach (PartModule m in prefab.Modules)
                    {
                        if (m == null || CraftReader.KindOfType(m.GetType()) != "antenna") continue;
                        object at = CraftReader.Get(m, "antennaType");
                        if (at == null || at.ToString() != "RELAY") continue;
                        var cm = new CraftModule { Name = m.GetType().Name, Kind = "antenna" };
                        cm.Num["power"] = CraftReader.Num(m, "antennaPower");
                        if (CraftReader.Bool(m, "antennaCombinable")) { cm.Num["combinable"] = 1; cm.Num["exponent"] = CraftReader.Num(m, "antennaCombinableExponent", 0.75); }
                        mods.Add(cm);
                    }
                }
                if (mods.Count == 0) continue;
                var body = CraftReader.Get(v, "mainBody") as CelestialBody;
                list.Add(new EngRelay { Name = Names.Of(v.vesselName), Body = body != null ? body.bodyName : "", Power = CraftChecks.Combine(mods), Altitude = CraftReader.Num(v, "altitude") });
                if (list.Count >= 40) break;
            }
            return list;
        }

        /// <summary>Saved craft files of this save: {name, building, path}, newest first.</summary>
        public static List<string[]> Saved()
        {
            var list = new List<KeyValuePair<DateTime, string[]>>();
            string root = System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(KSPUtil.ApplicationRootPath, "saves"), HighLogic.SaveFolder ?? ""), "Ships");
            foreach (string where in new[] { "VAB", "SPH" })
            {
                string dir = System.IO.Path.Combine(root, where);
                if (!System.IO.Directory.Exists(dir)) continue;
                foreach (string file in System.IO.Directory.GetFiles(dir, "*.craft"))
                    list.Add(new KeyValuePair<DateTime, string[]>(System.IO.File.GetLastWriteTimeUtc(file), new[] { System.IO.Path.GetFileNameWithoutExtension(file), where, file }));
            }
            return list.OrderByDescending(x => x.Key).Select(x => x.Value).ToList();
        }

        static double V(string[] a, int i) { double d; return a != null && i < a.Length && double.TryParse(a[i].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d) ? d : 0; }

        /// <summary>Reads a saved craft file into a craft sheet: each part from its stock definition, with the file's staging,
        /// resources, positions and joins. Changes a mod makes to a part after it is placed (resizing, fuel switching) are not in it.</summary>
        public static EngSnapshot LoadSaved(string path)
        {
            var node = ConfigNode.Load(path);
            if (node == null) return null;
            var sheet = new CraftSheet { BuiltAt = Time.realtimeSinceStartup };
            var ids = new Dictionary<string, int>();
            var links = new List<KeyValuePair<int, string>>();
            var loader = CraftReader.FindType("PartLoader");
            var lib = CraftReader.Static(CraftReader.FindType("PartResourceLibrary"), "Instance");
            int unknown = 0;
            foreach (var pn in node.GetNodes("PART"))
            {
                string id = pn.GetValue("part") ?? "";
                int cut = id.LastIndexOf('_');
                string name = cut > 0 ? id.Substring(0, cut) : id;
                var ap = CraftReader.Call(loader, "getPartInfoByName", name) as AvailablePart;
                var prefab = ap != null ? CraftReader.Get(ap, "partPrefab") as Part : null;
                CraftPart c = null;
                if (prefab != null) { try { c = CraftReader.ReadLoose(prefab); } catch { } }
                if (c == null) { c = new CraftPart { Name = name, Title = name + " (part not installed)", Mod = "unknown" }; unknown++; }
                int stage; if (int.TryParse(pn.GetValue("istg") ?? "-1", out stage)) c.Stage = stage;
                var pos = (pn.GetValue("pos") ?? "").Split(',');
                if (pos.Length == 3) { c.X = V(pos, 0); c.Y = V(pos, 1); c.Z = V(pos, 2); c.HasPos = true; }
                c.Surface = !string.IsNullOrEmpty(pn.GetValue("srfN")) && !(pn.GetValue("srfN") ?? "").Contains("Null");
                var res = pn.GetNodes("RESOURCE");
                if (res.Length > 0)
                {
                    c.Res.Clear(); double mass = 0;
                    foreach (var rn in res)
                    {
                        string rname = rn.GetValue("name") ?? "";
                        double amount = V(new[] { rn.GetValue("amount") ?? "0" }, 0), max = V(new[] { rn.GetValue("maxAmount") ?? "0" }, 0);
                        c.Res.Add(new CraftRes { Name = rname, Amount = amount, Max = max });
                        mass += amount * CraftReader.Num(CraftReader.Call(lib, "GetDefinition", rname), "density");
                    }
                    c.ResMass = mass;
                }
                c.Parent = -1;
                ids[id] = sheet.Parts.Count;
                if (pn.values != null) foreach (ConfigNode.Value v in pn.values) if (v != null && v.name == "link") links.Add(new KeyValuePair<int, string>(sheet.Parts.Count, v.value));
                sheet.Parts.Add(c);
            }
            if (sheet.Parts.Count == 0) return null;
            foreach (var l in links) { int child; if (ids.TryGetValue(l.Value, out child) && child != l.Key) sheet.Parts[child].Parent = l.Key; }
            sheet.Root = 0;
            try { sheet.Prepare(); } catch (Exception e) { Debug.LogWarning("[KerbalNews] Saved craft tree: " + e.Message); }
            bool sph = (node.GetValue("type") ?? "") == "SPH" || path.Replace('\\', '/').Contains("/SPH/");
            try { CraftReader.Facts(sheet, null, sph); } catch { }
            try { ModProbe.Fill(sheet); } catch { }
            var s = new EngSnapshot { Scene = sph ? "sph" : "vab", Vessel = Names.Of(node.GetValue("ship") ?? System.IO.Path.GetFileNameWithoutExtension(path)), Sheet = sheet };
            s.Situation = "a saved craft file" + (unknown > 0 ? ", " + unknown + " part" + (unknown == 1 ? "" : "s") + " not installed" : "");
            s.Parts = sheet.Parts.Count; s.Mass = sheet.Parts.Where(p => !p.Has("clamp")).Sum(p => p.Mass); s.CrewCapacity = sheet.Parts.Sum(p => p.Crew);
            try { var live = EngineeringCapture.Snapshot(); s.Bodies = live.Bodies; s.StockSystem = live.StockSystem; s.UT = live.UT; s.Phases = live.Phases; s.Contracts = live.Contracts; s.PartTitles = live.PartTitles; } catch { }
            return s;
        }
    }
}
