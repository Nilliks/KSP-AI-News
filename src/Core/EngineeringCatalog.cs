using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>
    /// The parts the director can build with right now, as the team reads them: every unlocked part, modded ones
    /// included, each with what it can do. The whole list is long, so a question gets the kinds it is about in full
    /// and a count of the rest.
    /// </summary>
    public static class EngCatalog
    {
        public class Topic
        {
            public string Key, Title;
            public string[] Words;
            public Func<CraftPart, bool> Is;
            public Func<CraftPart, double> Rank;
        }

        static bool IsRtg(CraftModule m) { return m.Is("ModuleRadioisotopeGenerator") || m.Is("ModuleDecayingRTG") || m.Name.IndexOf("RTG", StringComparison.Ordinal) >= 0; }
        static double RtgPower(CraftModule m) { return m.Any("BasePower", "basePower", "PowerOutput", "power"); }

        /// <summary>EC per second the part can make at its best: panels in full sun at home, generators, fuel cells and
        /// a life support mod's generator processes running, reactors at full power, RTGs when new.</summary>
        public static double EcMade(CraftPart p)
        {
            double v = p.Of("solar").Sum(m => m.N("rate"));
            foreach (var m in p.Modules)
            {
                if (m.Kind == "solar") continue;
                if (ModKnowledge.IsReactor(m)) v += ModKnowledge.ReactorPower(m);
                else if (IsRtg(m) && RtgPower(m) > 0) v += RtgPower(m);
                else if (m.Kind == "generator" || m.Kind == "converter" || m.Kind == "process") v += Math.Max(0, m.Rate(CraftSheet.EC));
            }
            return v;
        }
        static bool Battery(CraftPart p) { return p.Max(CraftSheet.EC) >= 50 && !p.Has("command") && !p.Has("engine") && p.Crew == 0 && EcMade(p) <= 0; }
        static bool Cargo(CraftPart p) { return p.Res.Any(r => r.Max > 0 && r.Name != CraftSheet.EC && !CraftSheet.Hidden(r.Name)); }

        public static readonly Topic[] Topics =
        {
            new Topic { Key = "power", Title = "POWER SOURCES (solar panels, generators, RTGs, reactors, fuel cells)", Words = new[] { "power", "electric", "solar", "panel", "rtg", "reactor", "generator", "fuel cell", "charge", " ec" },
                Is = p => EcMade(p) > 0 && !p.Has("engine") && !p.Has("command"), Rank = EcMade },
            new Topic { Key = "battery", Title = "BATTERIES", Words = new[] { "power", "electric", "batter", "charge", "storage", " ec" }, Is = Battery, Rank = p => p.Max(CraftSheet.EC) },
            new Topic { Key = "engine", Title = "ENGINES", Words = new[] { "engine", "motor", "thrust", "isp", "booster", "propulsion", "twr", "delta-v", "delta v", "nuclear", "ion " },
                Is = p => p.Has("engine"), Rank = p => p.Of("engine").Sum(m => m.N("thrust")) },
            new Topic { Key = "rcs", Title = "RCS THRUSTERS", Words = new[] { "rcs", "thruster", "docking" }, Is = p => p.Has("rcs") && !p.Has("command"), Rank = p => p.Of("rcs").Sum(m => m.N("thrust")) },
            new Topic { Key = "antenna", Title = "ANTENNAS", Words = new[] { "antenna", "comm", "relay", "signal", "dish", "transmit" },
                Is = p => p.Of("antenna").Any(m => m.T("type") != "internal") && !p.Has("command"), Rank = p => p.Of("antenna").Select(m => m.N("power")).DefaultIfEmpty(0).Max() },
            new Topic { Key = "radiator", Title = "RADIATORS AND HEAT SHIELDS", Words = new[] { "heat", "radiator", "cool", "thermal", "shield", "reentry", "re-entry", "ablat" },
                Is = p => p.Has("radiator") || p.Has("ablator"), Rank = p => p.Of("radiator").Sum(m => m.T("temperatureCurve") != "" ? m.CurveMax("temperatureCurve") : m.N("kw")) },
            new Topic { Key = "landing", Title = "PARACHUTES, LEGS AND WHEELS", Words = new[] { "parachute", "chute", "landing", "land on", "leg", "gear", "wheel", "rover", "touchdown" },
                Is = p => p.Has("chute") || p.Has("leg"), Rank = p => p.ChuteArea },
            new Topic { Key = "control", Title = "PODS, PROBE CORES AND REACTION WHEELS", Words = new[] { "reaction wheel", "torque", "sas", "probe", "control", "pod", "cockpit", "capsule", "command" },
                Is = p => p.Has("command") || p.Has("wheel"), Rank = p => p.Of("wheel").Sum(m => m.N("torque")) },
            new Topic { Key = "science", Title = "SCIENCE", Words = new[] { "science", "experiment", "lab", "sensor", "instrument" }, Is = p => p.Has("science") || p.Has("lab") || p.Modules.Any(m => m.Name == "Experiment"), Rank = p => -p.Cost },
            new Topic { Key = "mining", Title = "DRILLS AND CONVERTERS", Words = new[] { "drill", "mine", "mining", "isru", "convert", "ore", "refine", "harvest" }, Is = p => p.Has("drill") || p.Has("converter"), Rank = p => p.DryMass },
            new Topic { Key = "crew", Title = "CREW SPACE", Words = new[] { "crew", "seat", "hab", "cabin", "station", "living", "passenger" }, Is = p => p.Crew > 0, Rank = p => p.Crew },
            new Topic { Key = "tank", Title = "TANKS AND STORES", Words = new[] { "tank", "fuel", "propellant", "monoprop", "xenon", "hydrogen", "store", "storage", "life support", "food", "water", "oxygen", "supplies" },
                Is = p => Cargo(p) && !p.Has("engine") && !p.Has("command") && p.Crew == 0, Rank = p => p.ResMass > 0 ? p.ResMass : p.Res.Sum(r => r.Max) * 0.001 },
            new Topic { Key = "join", Title = "DOCKING PORTS, DECOUPLERS AND FAIRINGS", Words = new[] { "dock", "decoupl", "separator", "fairing", "stag" }, Is = p => p.Has("dock") || p.Has("decoupler") || p.Has("fairing"), Rank = p => -p.DryMass },
            new Topic { Key = "aero", Title = "WINGS AND INTAKES", Words = new[] { "wing", "plane", "intake", "aero", "fin", "control surface", "jet", "glide" }, Is = p => (p.Has("wing") || p.Has("intake")) && !p.Has("command") && !p.Has("engine"), Rank = p => -p.DryMass },
        };

        /// <summary>Words that mean "tell me what to fit" without naming a kind of part.</summary>
        static readonly string[] General = { "unlocked", "available", "recommend", "which part", "what part", "what should i add", "what do i have", "what can i", "parts do i", "suggest", "upgrade", "better part" };

        /// <summary>One part, one line: name, mod, mass, cost, size, and what it does.</summary>
        public static string Line(CraftPart p)
        {
            // Reactors, RTGs and a mod's processes are said in plain words here, so their raw module fields stay out.
            var plain = new CraftPart { Crew = p.Crew };
            plain.Modules.AddRange(p.Modules.Where(m => !ModKnowledge.IsReactor(m) && !(IsRtg(m) && RtgPower(m) > 0) && m.Kind != "process"));
            string does = CraftSheet.Does(plain, null, true);
            int cut = does.IndexOf("mod modules:", StringComparison.Ordinal);
            if (cut >= 0) does = does.Substring(0, cut).TrimEnd(' ', ';');
            var extra = new List<string>();
            foreach (var m in p.Modules.Where(ModKnowledge.IsReactor)) { double ec = ModKnowledge.ReactorPower(m); extra.Add(ec > 0 ? "reactor " + CraftSheet.Ec(ec) + " at full power (constant, needs radiators)" : "reactor"); }
            foreach (var m in p.Modules.Where(m => IsRtg(m) && RtgPower(m) > 0)) extra.Add("RTG " + CraftSheet.Ec(RtgPower(m)) + (m.Any("HalfLife", "halfLife") > 0 ? ", halves every " + EngMath.N(m.Any("HalfLife", "halfLife"), "0.#") + " years" : ", constant"));
            foreach (var m in p.Modules.Where(m => m.Kind == "process")) extra.Add((m.Label != "" ? m.Label : "process") + ": " + CraftSheet.RatesText(m.Rates));
            if (p.ChuteArea > 0) extra.Add(EngMath.N(p.ChuteArea) + " m² of canopy");
            var holds = p.Res.Where(r => r.Max > 0 && !CraftSheet.Hidden(r.Name)).Take(4).Select(r => r.Name + " " + EngMath.N(r.Max, "0.#")).ToArray();
            if (holds.Length > 0) extra.Add("holds " + string.Join(", ", holds));
            string all = string.Join("; ", extra.Concat(new[] { does }).Where(x => x != "").ToArray());
            return "- " + p.Title + (p.Mod != "" && p.Mod != "stock" ? " [" + p.Mod + "]" : "") + ": " + EngMath.N(p.DryMass, "0.###") + " t, " + EngMath.N(p.Cost) + " funds"
                + (p.Bulkhead != "" ? ", fits " + p.Bulkhead : "") + (all != "" ? "; " + ResponseParser.Clip(all, 280) : "");
        }

        /// <summary>The prompt section. <paramref name="about"/> is the question (and the one before it); the kinds of part it
        /// names are listed in full, strongest first, up to <paramref name="maxLines"/> lines in all.</summary>
        public static string Text(IList<CraftPart> catalog, string about, int maxLines = 120)
        {
            if (catalog == null || catalog.Count == 0) return "";
            string q = " " + (about ?? "").ToLowerInvariant() + " ";
            var asked = Topics.Where(t => t.Words.Any(w => q.Contains(w))).ToList();
            bool general = asked.Count == 0 && General.Any(w => q.Contains(w));
            var sb = new StringBuilder();
            var mods = catalog.GroupBy(p => p.Mod == "" ? "stock" : p.Mod).OrderByDescending(g => g.Count()).Select(g => g.Key + " " + g.Count()).ToArray();
            sb.AppendLine("UNLOCKED PARTS (read from the game: exactly what the director can build with right now, modded parts included): " + catalog.Count + " parts ("
                + string.Join(", ", mods.Take(12).ToArray()) + (mods.Length > 12 ? " and " + (mods.Length - 12) + " more mods" : "") + ").");
            sb.AppendLine("By kind: " + string.Join(", ", Topics.Select(t => new { t.Key, N = catalog.Count(t.Is) }).Where(x => x.N > 0).Select(x => x.Key + " " + x.N).ToArray()) + ".");
            if (asked.Count == 0 && !general)
            {
                sb.AppendLine("The parts of a kind are listed in full when the director's question is about that kind.");
                return sb.ToString().TrimEnd();
            }
            var show = general ? Topics.Where(t => t.Key == "power" || t.Key == "battery" || t.Key == "engine" || t.Key == "antenna" || t.Key == "radiator" || t.Key == "control").ToList() : asked;
            int per = Math.Max(6, maxLines / Math.Max(1, show.Count));
            if (general) per = Math.Min(per, 12);
            foreach (var t in show)
            {
                var parts = catalog.Where(t.Is).OrderByDescending(t.Rank).ThenBy(p => p.Cost).ToList();
                if (parts.Count == 0) { sb.AppendLine(t.Title + ": none unlocked."); continue; }
                sb.AppendLine(t.Title + ", " + parts.Count + " unlocked" + (parts.Count > per ? ", the " + per + " strongest shown" : "") + ":");
                foreach (var p in parts.Take(per)) sb.AppendLine(Line(p));
            }
            return sb.ToString().TrimEnd();
        }
    }
}
