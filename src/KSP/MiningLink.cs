using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace KerbalNews.KSP
{
    /// <summary>
    /// Soft link to the KSP Mining mod (GameData/KSPMining), found by reflection like the other mods in ModCompat, so
    /// KNN works the same with or without it. It reads that mod's ore catalog, warehouse, sales log and prices, and
    /// watches the active vessel's ore tanks, then hands plain numbers to MiningNews. It never changes anything in the
    /// other mod and never touches funds.
    ///   KSPMining.MiningScenario.Instance: Revision, HeldGoods(), Held(res), Sales, TotalEarned,
    ///     CurrentPricePerTonne(ore, res), BasePricePerTonne(ore, res), DriftFactor(ore)
    ///   KSPMining.OreCatalog: Ores, Density(res);  KSPMining.OreInfo: fields and DisplayName(res)
    /// If a later version renames any of these the link logs one warning and switches itself off.
    /// </summary>
    static class MiningLink
    {
        const BindingFlags Inst = BindingFlags.Public | BindingFlags.Instance, Stat = BindingFlags.Public | BindingFlags.Static;
        static bool looked, broken;
        static Type scenarioType, catalogType, oreType;
        static MiningCatalog catalog;
        static readonly Dictionary<string, object> oreObjects = new Dictionary<string, object>();

        static float nextPoll, nextWatch;
        static object lastScenario;
        static int lastRevision = int.MinValue;
        static NewsState lastState;
        static readonly Dictionary<string, double> lastPrice = new Dictionary<string, double>();

        // The vessel most recently recovered, so ore that turns up in the warehouse can be credited to it.
        static string recId = "", recName = "";
        static List<string> recCrew = new List<string>(), recVisited = new List<string>();
        static float recReal = -1000;

        // The active vessel's ore tanks at the last look.
        static string watchId = "";
        static int watchParts;
        static Dictionary<string, double> watchAmounts;

        static void Look()
        {
            if (looked) return;
            looked = true;
            try
            {
                scenarioType = ModCompat.FindType("KSPMining.MiningScenario");
                catalogType = ModCompat.FindType("KSPMining.OreCatalog");
                oreType = ModCompat.FindType("KSPMining.OreInfo");
                if (scenarioType != null && (catalogType == null || oreType == null)) Fail("its ore catalog was not found");
                else if (scenarioType != null) Debug.Log("[KerbalNews] KSP Mining found: ore hauls, sales and exchange prices can make the news.");
            }
            catch (Exception e) { Fail(e.Message); }
        }

        static void Fail(string why)
        {
            if (!broken) Debug.LogWarning("[KerbalNews] KSP Mining link switched off: " + why);
            broken = true;
        }

        public static bool Installed { get { Look(); return scenarioType != null && !broken; } }

        static object Scenario
        {
            get
            {
                var p = scenarioType.GetProperty("Instance", Stat);
                return p != null ? p.GetValue(null, null) : null;
            }
        }

        static object F(object o, string name)
        {
            var f = o.GetType().GetField(name, Inst);
            if (f != null) return f.GetValue(o);
            var p = o.GetType().GetProperty(name, Inst);
            if (p != null) return p.GetValue(o, null);
            throw new MissingMemberException(o.GetType().Name + "." + name);
        }

        static string S(object o, string name) { return F(o, name) as string ?? ""; }
        static double D(object o, string name) { return Convert.ToDouble(F(o, name), System.Globalization.CultureInfo.InvariantCulture); }

        static object Call(Type t, object target, string name, params object[] args)
        {
            var m = t.GetMethods(target == null ? Stat : Inst).FirstOrDefault(x => x.Name == name && x.GetParameters().Length == args.Length);
            if (m == null) throw new MissingMethodException(t.Name + "." + name);
            return m.Invoke(target, args);
        }

        /// <summary>The other mod's ore list as plain data, read once per session.</summary>
        static MiningCatalog Catalog()
        {
            if (catalog != null) return catalog;
            var cat = new MiningCatalog();
            var ores = catalogType.GetProperty("Ores", Stat).GetValue(null, null) as IEnumerable;
            var display = oreType.GetMethod("DisplayName", Stat);
            var lore = new Dictionary<string, string>();
            try
            {
                foreach (var n in GameDatabase.Instance.GetConfigNodes("KSPMINING_LORE"))
                {
                    string name = n.GetValue("name"), about = n.GetValue("about");
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(about)) lore[name] = about;
                }
            }
            catch (Exception) { }   // flavour text only
            foreach (var o in ores)
            {
                var ore = new MiningOre
                {
                    Id = S(o, "Id"), Title = S(o, "Title"), Tier = Convert.ToInt32(F(o, "Tier")), Raw = S(o, "RawResource"), Refined = S(o, "RefinedResource"),
                    Ingot = S(o, "IngotResource"), IngotUnit = S(o, "IngotUnit"), RawValue = D(o, "RawValuePerTonne"), RefinedValue = D(o, "RefinedValuePerTonne"),
                };
                if (ore.Ingot != "") ore.IngotValue = D(o, "IngotValuePerTonne");
                if (ore.IngotUnit == "") ore.IngotUnit = "bar";
                foreach (var b in S(o, "Bodies").Split(',')) if (b.Trim() != "") ore.Bodies.Add(b.Trim());
                foreach (var r in new[] { ore.Raw, ore.Refined, ore.Ingot })
                {
                    if (r == "") continue;
                    string shown = display != null ? display.Invoke(null, new object[] { r }) as string : null;
                    if (!string.IsNullOrEmpty(shown)) ore.Names[r] = Names.Of(shown);
                }
                string about;
                if (lore.TryGetValue(ore.Id, out about)) ore.About = about;
                if (ore.Id != "" && ore.Raw != "") { cat.Ores.Add(ore); oreObjects[ore.Id] = o; }
            }
            if (cat.Ores.Count > 0) catalog = cat;   // an empty list means the other mod has not loaded its configs yet: look again later
            return cat;
        }

        static double Price(object scenario, MiningCatalog cat, string resource)
        {
            var o = cat.OreOf(resource);
            if (o == null) return 0;
            try { if (scenario != null) return Convert.ToDouble(Call(scenarioType, scenario, "CurrentPricePerTonne", oreObjects[o.Id], resource)); }
            catch (Exception) { }
            return cat.ListPrice(resource);
        }

        /// <summary>Called when any vessel is recovered: remembers who it was, for the ore that is about to reach the warehouse.</summary>
        public static void NoteRecovery(ProtoVessel pv, TrackedVessel tracked)
        {
            if (pv == null || !Installed) return;
            recId = tracked != null ? pv.persistentId.ToString() : "";
            recName = Names.Of(pv.vesselName);
            recCrew = new List<string>();
            var crew = pv.GetVesselCrew();
            if (crew != null) foreach (var c in crew) if (c != null && c.name != null) recCrew.Add(c.name);
            recVisited = tracked != null ? tracked.Flags.Where(f => f.StartsWith("landed:") || f.StartsWith("soi:")).Select(f => f.Substring(f.IndexOf(':') + 1)).ToList() : new List<string>();
            recReal = Time.realtimeSinceStartup;
        }

        /// <summary>A few times a minute: the warehouse, the sales log, the exchange, and the active vessel's tanks.</summary>
        public static void Update(NewsEngine eng, double ut, double daySeconds)
        {
            if (!Installed || eng == null) return;
            float now = Time.realtimeSinceStartup;
            if (now < nextPoll) return;
            nextPoll = now + 3f;
            try
            {
                if (ModCompat.InFmrsSideFlight()) return;   // a dropped-stage flight that FMRS will undo
                var scenario = Scenario;
                var cat = Catalog();
                if (cat.Ores.Count == 0) return;
                if (scenario != null) Market(eng, scenario, cat, ut, daySeconds);
                if (now >= nextWatch) { nextWatch = now + 10f; Watch(eng, scenario, cat, ut); }
            }
            catch (Exception e) { Fail((e.InnerException ?? e).Message); }
        }

        static void Market(NewsEngine eng, object scenario, MiningCatalog cat, double ut, double daySeconds)
        {
            var memo = eng.State.WorldMemo;
            int revision = Convert.ToInt32(F(scenario, "Revision"));
            bool fresh = !ReferenceEquals(scenario, lastScenario) || !ReferenceEquals(eng.State, lastState);
            if (!fresh && revision == lastRevision && MiningNews.Baselined(memo)) { Prices(eng, scenario, cat, ut, daySeconds); return; }
            lastScenario = scenario; lastState = eng.State; lastRevision = revision;

            var held = new Dictionary<string, double>();
            foreach (var r in (IEnumerable)Call(scenarioType, scenario, "HeldGoods"))
            {
                string res = r as string;
                if (string.IsNullOrEmpty(res) || cat.OreOf(res) == null) continue;
                double t = Convert.ToDouble(Call(scenarioType, scenario, "Held", res));
                if (t > 0) held[res] = t;
            }
            var log = new List<MiningSale>();
            foreach (var s in (IEnumerable)F(scenario, "Sales"))
                if (s != null) log.Add(new MiningSale { UT = D(s, "UT"), Resource = S(s, "Resource"), Tonnes = D(s, "Tonnes"), Funds = D(s, "Funds") });
            double earned = D(scenario, "TotalEarned");

            if (!MiningNews.Baselined(memo))
            {
                // First look at this save: what is already there is old news.
                MiningNews.Baseline(memo, cat, held.Keys.Concat(log.Select(s => s.Resource)), earned);
                MiningNews.BaselineSales(memo, log);
                MiningNews.Arrivals(memo, held, null);
                RememberPrices(scenario, cat);
                return;
            }

            var sales = MiningNews.NewSales(memo, log);
            foreach (var s in sales)
            {
                double before, after = Price(scenario, cat, s.Resource);
                if (lastPrice.TryGetValue(s.Resource, out before) && before > 0 && after > 0) s.PriceDrop = Math.Max(0, 1 - after / before);
            }
            var sold = sales.GroupBy(s => s.Resource).ToDictionary(g => g.Key, g => g.Sum(s => s.Tonnes));
            var arrived = MiningNews.Arrivals(memo, held, sold);
            if (arrived.Count > 0)
            {
                var cargo = arrived.Select(kv => new MiningCargo { Resource = kv.Key, Tonnes = kv.Value, Value = kv.Value * Price(scenario, cat, kv.Key) }).ToList();
                bool mine = Time.realtimeSinceStartup - recReal < 180;
                var item = MiningNews.Recovery(memo, cat, mine ? recName : "", cargo, mine ? recVisited : null, ut);
                eng.ReportMining(item, mine ? recId : "", mine ? recName : "", mine ? recCrew : null, ut);
                recReal = -1000;
            }
            foreach (var item in MiningNews.Sales(memo, cat, sales, ut, daySeconds)) eng.ReportMining(item, "", "", null, ut);
            eng.ReportMining(MiningNews.Earnings(memo, earned), "", "", null, ut);
            RememberPrices(scenario, cat);
            Prices(eng, scenario, cat, ut, daySeconds);
        }

        static void RememberPrices(object scenario, MiningCatalog cat)
        {
            foreach (var o in cat.Ores)
                foreach (var r in new[] { o.Raw, o.Refined, o.Ingot })
                    if (r != "") lastPrice[r] = Price(scenario, cat, r);
        }

        /// <summary>The weekly market look. Uses the exchange's own drift, without the dip the agency's sales cause.</summary>
        static void Prices(NewsEngine eng, object scenario, MiningCatalog cat, double ut, double daySeconds)
        {
            var memo = eng.State.WorldMemo;
            double day = ut / Math.Max(1, daySeconds);
            if (MiningNews.Get(memo, "pxDay") != "" && day - MiningNews.Num(memo, "pxDay") < 7) return;
            var px = new Dictionary<string, double>();
            foreach (var o in cat.Ores)
            {
                var obj = oreObjects[o.Id];
                double p = Convert.ToDouble(Call(scenarioType, scenario, "BasePricePerTonne", obj, o.Raw)) * Convert.ToDouble(Call(scenarioType, scenario, "DriftFactor", obj));
                if (p > 0) px[o.Id] = p;
            }
            eng.ReportMining(MiningNews.Prices(memo, cat, px, ut, daySeconds), "", "", null, ut);
        }

        static Dictionary<string, double> Tanks(Vessel v, MiningCatalog cat)
        {
            var sum = new Dictionary<string, double>();
            if (v == null || v.parts == null) return sum;
            foreach (var p in v.parts)
            {
                if (p == null || p.Resources == null) continue;
                foreach (var r in p.Resources)
                {
                    if (r == null || r.amount <= 0 || cat.OreOf(r.resourceName) == null) continue;
                    double had;
                    sum.TryGetValue(r.resourceName, out had);
                    sum[r.resourceName] = had + r.amount;
                }
            }
            return sum;
        }

        static double Density(string resource)
        {
            try { return Convert.ToDouble(Call(catalogType, null, "Density", resource)); } catch (Exception) { return 0; }
        }

        /// <summary>
        /// The active vessel's ore tanks: raw ore going up while it sits on another world means a drill is working,
        /// and cast metal going up means a smelter is. Reading the tanks keeps this free of any drill or life-support mod.
        /// </summary>
        static void Watch(NewsEngine eng, object scenario, MiningCatalog cat, double ut)
        {
            var v = HighLogic.LoadedSceneIsFlight ? FlightGlobals.ActiveVessel : null;
            if (v == null || v.parts == null || ModCompat.VesselMoverBusy(null)) { watchId = ""; return; }
            string id = v.persistentId.ToString();
            var amounts = Tanks(v, cat);
            bool same = id == watchId && v.parts.Count == watchParts && watchAmounts != null;   // docking and undocking move ore without mining it
            var before = watchAmounts;
            watchId = id; watchParts = v.parts.Count; watchAmounts = amounts;
            if (!same || eng.State.IsSilent(id)) return;
            string body = v.mainBody != null ? v.mainBody.bodyName : "";
            var home = FlightGlobals.GetHomeBody();
            bool onGround = v.situation == Vessel.Situations.LANDED || v.situation == Vessel.Situations.SPLASHED;
            var memo = eng.State.WorldMemo;
            if (!MiningNews.Baselined(memo)) return;
            foreach (var kv in amounts)
            {
                double had;
                before.TryGetValue(kv.Key, out had);
                if (kv.Value - had <= 1e-6) continue;
                string kind = cat.Kind(kv.Key);
                MiningItem item = null;
                if (kind == "raw" && onGround && body != "" && (home == null || home.bodyName != body)) item = MiningNews.Drill(memo, cat, kv.Key, body, Names.Of(v));
                else if (kind == "ingot") item = MiningNews.Bars(memo, cat, kv.Key, onGround ? body : "", Names.Of(v));
                if (item == null) continue;
                var crew = v.GetVesselCrew();
                eng.ReportMining(item, eng.IsTracked(id) ? id : "", Names.Of(v), crew != null ? crew.Where(c => c != null && c.name != null).Select(c => c.name) : null, ut);
            }
        }

        /// <summary>
        /// For the in-game self-test: checks that everything this link reads from KSP Mining is still there, and returns
        /// the catalog so the test can run a sample cargo through the news. Null catalog: the mod is not installed (fine).
        /// </summary>
        public static MiningCatalog SelfCheck(out string report)
        {
            Look();
            if (scenarioType == null) { report = "KSP Mining is not installed: no mining news, nothing else changes"; return null; }
            if (broken) { report = "FAILED: the link switched itself off earlier (see the log)"; return new MiningCatalog(); }
            try
            {
                var cat = Catalog();
                var missing = new List<string>();
                foreach (var m in new[] { "HeldGoods:0", "Held:1", "CurrentPricePerTonne:2", "BasePricePerTonne:2", "DriftFactor:1" })
                {
                    string name = m.Split(':')[0]; int args = int.Parse(m.Split(':')[1]);
                    if (!scenarioType.GetMethods(Inst).Any(x => x.Name == name && x.GetParameters().Length == args)) missing.Add(name);
                }
                foreach (var p in new[] { "Revision", "Sales", "TotalEarned" }) if (scenarioType.GetProperty(p, Inst) == null) missing.Add(p);
                if (!catalogType.GetMethods(Stat).Any(x => x.Name == "Density")) missing.Add("Density");
                if (missing.Count > 0) { report = "FAILED: KSP Mining no longer has " + string.Join(", ", missing.ToArray()); return new MiningCatalog(); }
                report = cat.Ores.Count + " ores read (" + string.Join(", ", cat.Ores.Select(o => o.Title + " tier " + o.Tier + " @ " + o.RawValue.ToString("0")).ToArray()) + "), lore for "
                    + cat.Ores.Count(o => o.About != "") + ", scenario " + (Scenario != null ? "running" : "not loaded in this scene");
                return cat;
            }
            catch (Exception e) { report = "FAILED: " + (e.InnerException ?? e).Message; return new MiningCatalog(); }
        }

        /// <summary>One sentence for the story of a lost vessel that was carrying ore ("" otherwise, or without KSP Mining).</summary>
        public static string CargoLossText(Vessel v)
        {
            if (!Installed || v == null) return "";
            try
            {
                var cat = Catalog();
                var scenario = Scenario;
                var cargo = Tanks(v, cat).Select(kv => { double t = kv.Value * Density(kv.Key); return new MiningCargo { Resource = kv.Key, Tonnes = t, Value = t * Price(scenario, cat, kv.Key) }; }).ToList();
                return MiningNews.CargoLoss(cat, cargo);
            }
            catch (Exception) { return ""; }
        }
    }
}
