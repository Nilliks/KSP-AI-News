using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KerbalNews
{
    /// <summary>An active contract and its conditions, as the game words them.</summary>
    public class EngContract { public string Title = ""; public List<EngContractParam> Params = new List<EngContractParam>(); }
    public class EngContractParam { public string Text = ""; public bool Done; }

    /// <summary>An experiment carried on the craft: what it is worth and where it can run (the game's situation and biome masks).</summary>
    public class EngExperiment { public string Id = "", Title = ""; public double Base, Cap; public int Situations, Biomes; }

    /// <summary>A saved picture of a craft's numbers, to compare a later version against.</summary>
    public class CraftSnap
    {
        public string Id = "", Name = "", Craft = "";
        public double UT, Mass, Cost, Dv;
        public int Parts;
        public Dictionary<string, string> Ratings = new Dictionary<string, string>();
        public Dictionary<string, int> Groups = new Dictionary<string, int>();

        public void Save(DataNode n)
        {
            n.Add("id", Id); n.AddText("name", Name); n.AddText("craft", Craft); n.Add("ut", UT); n.Add("mass", Mass); n.Add("cost", Cost); n.Add("dv", Dv); n.Add("parts", Parts);
            n.AddText("ratings", string.Join("|", Ratings.Select(k => k.Key + "=" + k.Value).ToArray()));
            n.AddText("groups", string.Join("|", Groups.Select(k => k.Key.Replace("|", "/").Replace("=", "-") + "=" + k.Value.ToString(CultureInfo.InvariantCulture)).ToArray()));
        }

        public static CraftSnap Load(DataNode n)
        {
            var s = new CraftSnap { Id = n.Get("id", ""), Name = n.GetText("name"), Craft = n.GetText("craft") };
            double d; int i;
            if (double.TryParse(n.Get("ut", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.UT = d;
            if (double.TryParse(n.Get("mass", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.Mass = d;
            if (double.TryParse(n.Get("cost", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.Cost = d;
            if (double.TryParse(n.Get("dv", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.Dv = d;
            if (int.TryParse(n.Get("parts", "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) s.Parts = i;
            foreach (var kv in (n.GetText("ratings") ?? "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)) { int cut = kv.IndexOf('='); if (cut > 0) s.Ratings[kv.Substring(0, cut)] = kv.Substring(cut + 1); }
            foreach (var kv in (n.GetText("groups") ?? "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)) { int cut = kv.LastIndexOf('='); if (cut > 0 && int.TryParse(kv.Substring(cut + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) s.Groups[kv.Substring(0, cut)] = i; }
            return s;
        }
    }

    /// <summary>
    /// The engineering tools that work on top of the checks: part swaps from the unlocked catalogue, what a fix would
    /// change, version compare, the go/no-go poll, transfer windows, contract and science fit, lessons from past
    /// failures and the in-flight watch list. All arithmetic, no AI call.
    /// </summary>
    public static class EngTools
    {
        static string N(double v, string f = "0") { return v.ToString(f, CultureInfo.InvariantCulture); }
        static string Signed(double v, string f = "0") { return (v >= 0 ? "+" : "−") + N(Math.Abs(v), f); }
        static string Count(int n, string title) { return (n > 1 ? n + " × " : "") + title; }
        const double G0 = 9.80665;

        // ---------------------------------------------------------------- what a fix would change

        /// <summary>The craft with some copies of a part added (or, with a negative count, taken away): for trying a fix.</summary>
        public static CraftSheet With(CraftSheet sheet, string partName, int delta)
        {
            var copy = new CraftSheet { Trial = true, Root = sheet.Root, Flying = sheet.Flying, BuiltAt = sheet.BuiltAt, Facts = sheet.Facts, ModFacts = sheet.ModFacts, Processes = sheet.Processes, CrewRules = sheet.CrewRules };
            copy.Parts.AddRange(sheet.Parts);
            var like = sheet.Parts.FirstOrDefault(p => p.Name == partName);
            if (like == null) return copy;
            delta = Math.Max(-500, Math.Min(500, delta));
            if (delta > 0) for (int i = 0; i < delta; i++) copy.Parts.Add(like);
            else for (int i = 0; i < -delta; i++) { int at = copy.Parts.FindLastIndex(p => p.Name == partName); if (at >= 0) copy.Parts.RemoveAt(at); }
            return copy;
        }

        // ---------------------------------------------------------------- part swaps

        static HashSet<string> Props(CraftModule m) { return new HashSet<string>(m.T("props").Split(',').Select(x => x.Trim()).Where(x => x != "")); }
        static bool Fits(CraftPart a, CraftPart b)
        {
            var x = a.Bulkhead.Split(',').Select(s => s.Trim()).Where(s => s != "" && s != "srf").ToList();
            var y = b.Bulkhead.Split(',').Select(s => s.Trim()).Where(s => s != "" && s != "srf").ToList();
            if (x.Count == 0 || y.Count == 0) return a.Surface || x.Count == y.Count;
            return x.Any(s => y.Contains(s));
        }
        static CraftModule MainEngine(CraftPart p) { return p.Of("engine").OrderByDescending(m => m.N("thrust")).FirstOrDefault(); }

        static double Metric(CraftPart p, string kind)
        {
            switch (kind)
            {
                case "solar": return p.Of("solar").Sum(m => m.N("rate"));
                case "battery": return p.Max(CraftSheet.EC);
                case "wheel": return p.Of("wheel").Sum(m => m.N("torque"));
                case "antenna": return p.Of("antenna").Select(m => m.N("power")).DefaultIfEmpty(0).Max();
                case "radiator": return p.Of("radiator").Sum(m => m.T("temperatureCurve") != "" ? m.CurveMax("temperatureCurve") : m.N("kw"));
                case "chute": return p.ChuteArea;
            }
            return 0;
        }
        static readonly string[][] MetricKinds =
        {
            new[] { "solar", "solar output", "EC/s" }, new[] { "battery", "battery capacity", "EC" }, new[] { "wheel", "reaction wheel torque", "kNm" },
            new[] { "antenna", "antenna power", "" }, new[] { "radiator", "radiator capacity", "kW" }, new[] { "chute", "parachute area", "m²" },
        };
        static string KindOf(CraftPart p)
        {
            if (p.Has("command") || p.Has("engine") || p.Crew > 0) return "";
            if (p.Has("solar")) return "solar";
            if (p.Has("radiator")) return "radiator";
            if (p.Has("chute")) return "chute";
            if (p.Has("antenna") && p.Of("antenna").Any(m => m.T("type") != "internal")) return "antenna";
            if (p.Has("wheel")) return "wheel";
            if (p.Max(CraftSheet.EC) >= 50 && p.Res.Count(r => !CraftSheet.Hidden(r.Name)) == 1 && !p.Has("generator")) return "battery";
            return "";
        }

        /// <summary>Better or cheaper parts for the same job, from everything unlocked (modded parts included).</summary>
        public static CheckReport Swaps(CraftSheet sheet, IList<CraftPart> catalog, IList<EngStage> stages, bool launchInAir)
        {
            var r = new CheckReport { Key = "swaps", Title = "Part swaps" };
            if (catalog == null || catalog.Count == 0) { r.Rating = "no catalogue"; r.Add("The list of unlocked parts could not be read, so no swaps are suggested."); return r; }
            int found = 0;
            int firstStage = stages.Count > 0 ? stages.Max(s => s.Stage) : -1;
            var engines = catalog.Where(p => MainEngine(p) != null && MainEngine(p).N("thrust") > 0).ToList();
            foreach (var st in stages.OrderByDescending(s => s.Stage))
            {
                if (st.DvVac < 50 || st.StartMass <= 0 || st.EndMass <= 0) continue;
                var active = sheet.Parts.Where(p => p.Has("engine") && p.Stage >= st.Stage && p.DropStage < st.Stage && MainEngine(p).N("thrust") > 0).ToList();
                if (active.Count == 0) continue;
                if (active.Select(p => p.Name).Distinct().Count() > 1) { r.Add("Stage " + st.Stage + " burns " + active.Select(p => p.Name).Distinct().Count() + " kinds of engine together, so a one-for-one swap is not worked out."); continue; }
                var old = active[0]; var om = MainEngine(old); int n = active.Count;
                bool pad = launchInAir && st.Stage == firstStage;
                double oldIsp = pad && om.N("ispAsl") > 0 ? (om.N("ispAsl") + om.N("ispVac")) / 2 : om.N("ispVac");
                double oldDv = oldIsp * G0 * Math.Log(st.StartMass / st.EndMass);
                double oldTwr = pad && st.TwrAsl > 0 ? st.TwrAsl : st.TwrVac;
                double oldThrust = pad && om.N("ispAsl") > 0 ? om.N("thrust") * om.N("ispAsl") / om.N("ispVac") : om.N("thrust");
                // In space a weaker engine is a fair trade, but not one that leaves the stage with less than half its push.
                double floor = pad ? 1.25 : Math.Min(oldTwr, Math.Max(0.35, oldTwr * 0.5));
                var props = Props(om);
                CraftPart best = null, cheap = null; double bestDv = oldDv, bestTwr = 0, cheapDv = 0, cheapSave = 0;
                foreach (var c in engines)
                {
                    if (c.Name == old.Name) continue;
                    var cm = MainEngine(c);
                    if (!props.SetEquals(Props(cm)) || (cm.N("locked") > 0) != (om.N("locked") > 0) || !Fits(old, c)) continue;
                    if (pad && cm.N("ispAsl") < cm.N("ispVac") * 0.6) continue;
                    double dm = n * (c.DryMass - old.DryMass);
                    if (st.EndMass + dm <= 0) continue;
                    double isp = pad && cm.N("ispAsl") > 0 ? (cm.N("ispAsl") + cm.N("ispVac")) / 2 : cm.N("ispVac");
                    double dv = isp * G0 * Math.Log((st.StartMass + dm) / (st.EndMass + dm));
                    double thrust = pad && cm.N("ispAsl") > 0 ? cm.N("thrust") * cm.N("ispAsl") / cm.N("ispVac") : cm.N("thrust");
                    double twr = oldThrust > 0 ? oldTwr * (thrust / oldThrust) * st.StartMass / (st.StartMass + dm) : 0;
                    if (twr < floor) continue;
                    if (dv > bestDv * 1.02) { best = c; bestDv = dv; bestTwr = twr; }
                    double save = n * (old.Cost - c.Cost);
                    if (save > old.Cost * n * 0.15 && dv > oldDv * 0.97 && save > cheapSave) { cheap = c; cheapSave = save; cheapDv = dv; }
                }
                if (best != null)
                {
                    found++;
                    r.Fix("Stage " + st.Stage + ": swap " + Count(n, old.Title) + " for " + Count(n, best.Title) + (best.Mod != "" && best.Mod != "stock" ? " [" + best.Mod + "]" : ""),
                        Signed(bestDv - oldDv) + " m/s, TWR " + N(oldTwr, "0.00") + " → " + N(bestTwr, "0.00") + ", " + Signed(n * (best.Cost - old.Cost)) + " funds");
                }
                if (cheap != null && cheap != best)
                {
                    found++;
                    r.Fix("Stage " + st.Stage + ": " + Count(n, cheap.Title) + (cheap.Mod != "" && cheap.Mod != "stock" ? " [" + cheap.Mod + "]" : "") + " does nearly the same job as " + Count(n, old.Title) + " for less",
                        "saves " + N(cheapSave) + " funds, " + Signed(cheapDv - oldDv) + " m/s");
                }
                if (best == null && cheap == null) r.Add("Stage " + st.Stage + ": " + Count(n, old.Title) + " is already the best unlocked engine for this fuel and size.", "keep");
            }

            foreach (var g in sheet.Parts.GroupBy(p => p.Name))
            {
                var old = g.First(); string kind = KindOf(old);
                if (kind == "") continue;
                var info = MetricKinds.First(k => k[0] == kind);
                double each = Metric(old, kind); int n = g.Count();
                if (each <= 0) continue;
                double total = each * n, mass = g.Sum(p => p.DryMass), cost = g.Sum(p => p.Cost);
                CraftPart light = null, cheap = null; double lightMass = mass * 0.9, cheapCost = cost * 0.85; int lightN = 0, cheapN = 0;
                foreach (var c in catalog)
                {
                    if (c.Name == old.Name || KindOf(c) != kind) continue;
                    double m = Metric(c, kind);
                    if (m <= 0) continue;
                    if (kind == "solar" && old.Of("solar").Any(x => x.N("tracking") > 0) != c.Of("solar").Any(x => x.N("tracking") > 0)) continue;
                    if (kind == "antenna" && old.Of("antenna").First().T("type") != c.Of("antenna").First().T("type")) continue;
                    if (kind == "radiator" && (old.Of("radiator").First().T("temperatureCurve") != "") != (c.Of("radiator").First().T("temperatureCurve") != "")) continue;
                    int k = kind == "antenna" ? (m >= each ? n : 0) : (int)Math.Ceiling(total / m - 1e-9);
                    if (k <= 0 || k > Math.Max(8, n * 4)) continue;
                    double cm = k * c.DryMass, cc = k * c.Cost;
                    if (cm < lightMass && cc <= cost * 1.5 + 200) { light = c; lightMass = cm; lightN = k; }
                    if (cc < cheapCost && cm <= mass * 1.5 + 0.02) { cheap = c; cheapCost = cc; cheapN = k; }
                }
                string have = Count(n, old.Title) + " (" + N(total, total < 10 ? "0.##" : "0") + (info[2] != "" ? " " + info[2] : "") + " of " + info[1] + ")";
                if (light != null && mass - lightMass > 0.005)
                {
                    found++;
                    r.Fix("Lighter: " + Count(lightN, light.Title) + (light.Mod != "" && light.Mod != "stock" ? " [" + light.Mod + "]" : "") + " gives the same as " + have,
                        "saves " + N(mass - lightMass, "0.###") + " t, " + Signed(lightN * light.Cost - cost) + " funds");
                }
                if (cheap != null && cheap != light && cost - cheapCost > 50)
                {
                    found++;
                    r.Fix("Cheaper: " + Count(cheapN, cheap.Title) + (cheap.Mod != "" && cheap.Mod != "stock" ? " [" + cheap.Mod + "]" : "") + " gives the same as " + have,
                        "saves " + N(cost - cheapCost) + " funds, " + Signed(cheapN * cheap.DryMass - mass, "0.###") + " t");
                }
            }
            r.Rating = found == 0 ? "nothing better unlocked" : found + " swap" + (found == 1 ? "" : "s") + " found";
            r.Note = "Swaps come from the " + catalog.Count + " unlocked parts that make power, thrust, torque, cooling, signal or drag, modded parts included. Engines are matched on fuel and stack size; check the fit in the builder.";
            return r;
        }

        // ---------------------------------------------------------------- efficiency

        static readonly Regex SaveMass = new Regex(@"saves (?:about |up to )?([0-9.]+) t", RegexOptions.Compiled);
        static readonly Regex SaveFunds = new Regex(@"(?:saves |, )([0-9]+) funds", RegexOptions.Compiled);

        /// <summary>Everything the checks found that the craft carries beyond what the job needs, in one list with the totals.</summary>
        public static CheckReport Efficiency(PowerReport power, IEnumerable<CheckReport> checks, CheckReport swaps)
        {
            var r = new CheckReport { Key = "efficiency", Title = "Efficiency" };
            double mass = 0, funds = 0;
            Action<string, SheetLine> take = (title, l) =>
            {
                string v = l.Value;
                bool saving = v.Contains("saves") || l.Text.Contains("could go") || l.Text.Contains("more than") || l.Text.Contains("stronger than") || l.Text.Contains("times the");
                if (!saving) return;
                r.Lines.Add(new SheetLine(title + ": " + l.Text, v) { Fix = true });
                var m = SaveMass.Match(v); double d;
                if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) mass += d;
                m = SaveFunds.Match(v);
                if (m.Success && v.Contains("saves") && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) funds += d;
            };
            if (power != null) foreach (var l in power.Fixes) take("Power", l);
            foreach (var c in checks) foreach (var l in c.Fixes) take(c.Title, l);
            if (swaps != null) foreach (var l in swaps.Fixes) take("Swap", l);
            if (r.Lines.Count == 0) { r.Rating = "nothing to trim"; r.Add("Nothing on the craft is clearly more than the mission needs."); return r; }
            r.Rating = (mass > 0 ? "up to " + N(mass, "0.##") + " t" : "") + (mass > 0 && funds > 0 ? " and " : "") + (funds > 0 ? N(funds) + " funds" : "") + (mass <= 0 && funds <= 0 ? r.Lines.Count + " ideas" : " to save");
            r.Note = "Spares kept on purpose are not waste: these are only the places where the numbers say the craft carries more than the job needs. Some savings overlap, so the total is an upper figure.";
            return r;
        }

        // ---------------------------------------------------------------- snapshots and compare

        public static CraftSnap Snap(string id, string name, EngSnapshot s, PowerReport power, IList<string> staging, IEnumerable<CheckReport> checks)
        {
            var snap = new CraftSnap { Id = id, Name = name, Craft = s.Vessel, UT = s.UT, Mass = s.Mass, Parts = s.Parts, Dv = s.TotalDvVac };
            if (s.Sheet != null)
            {
                snap.Cost = s.Sheet.Parts.Sum(p => p.Cost);
                foreach (var g in s.Sheet.Parts.GroupBy(p => p.Title)) snap.Groups[g.Key] = g.Count();
            }
            if (power != null) snap.Ratings["Power"] = power.Rating;
            if (staging != null) { int bad = staging.Count(x => x.StartsWith("!")); snap.Ratings["Staging"] = bad > 0 ? bad + " problem" + (bad == 1 ? "" : "s") : "ok"; }
            foreach (var c in checks) if (c.Key != "cost") snap.Ratings[c.Title] = c.Rating;
            return snap;
        }

        /// <summary>What changed between a saved version and the craft as it stands now.</summary>
        public static CheckReport Compare(CraftSnap then, CraftSnap now)
        {
            var r = new CheckReport { Key = "compare", Title = "Compare" };
            if (then == null || now == null) { r.Rating = "no snapshot"; r.Add("Take a snapshot first, change the craft, then compare."); return r; }
            r.Add("Against \"" + then.Name + "\"" + (then.Craft != "" && then.Craft != now.Craft ? " (a different craft: " + then.Craft + ")" : ""));
            r.Add("Mass", N(then.Mass, "0.0#") + " → " + N(now.Mass, "0.0#") + " t (" + Signed(now.Mass - then.Mass, "0.0#") + ")");
            r.Add("Delta-v (vacuum)", N(then.Dv) + " → " + N(now.Dv) + " m/s (" + Signed(now.Dv - then.Dv) + ")", now.Dv < then.Dv * 0.95);
            r.Add("Cost", N(then.Cost) + " → " + N(now.Cost) + " funds (" + Signed(now.Cost - then.Cost) + ")");
            r.Add("Parts", then.Parts + " → " + now.Parts + " (" + Signed(now.Parts - then.Parts) + ")");
            int changed = 0;
            foreach (var k in now.Ratings.Keys.Union(then.Ratings.Keys))
            {
                string a, b;
                then.Ratings.TryGetValue(k, out a); now.Ratings.TryGetValue(k, out b);
                if ((a ?? "") == (b ?? "")) continue;
                changed++;
                bool worse = Worse(b) && !Worse(a);
                r.Add(k, (a ?? "none") + " → " + (b ?? "none"), worse);
            }
            if (changed == 0) r.Add("No check changed its verdict.");
            var added = now.Groups.Where(g => !then.Groups.ContainsKey(g.Key) || then.Groups[g.Key] < g.Value).Select(g => "+" + (g.Value - (then.Groups.ContainsKey(g.Key) ? then.Groups[g.Key] : 0)) + " " + g.Key).ToList();
            var gone = then.Groups.Where(g => !now.Groups.ContainsKey(g.Key) || now.Groups[g.Key] < g.Value).Select(g => "−" + (g.Value - (now.Groups.ContainsKey(g.Key) ? now.Groups[g.Key] : 0)) + " " + g.Key).ToList();
            if (added.Count > 0) r.Add("Added: " + string.Join(", ", added.Take(10).ToArray()) + (added.Count > 10 ? " and " + (added.Count - 10) + " more" : ""));
            if (gone.Count > 0) r.Add("Removed: " + string.Join(", ", gone.Take(10).ToArray()) + (gone.Count > 10 ? " and " + (gone.Count - 10) + " more" : ""));
            r.Rating = Signed(now.Dv - then.Dv) + " m/s, " + Signed(now.Mass - then.Mass, "0.0#") + " t";
            return r;
        }

        static bool Worse(string rating)
        {
            if (rating == null) return false;
            string x = rating.ToLowerInvariant();
            return x.StartsWith("short") || x.Contains("problem") || x.Contains("no link") || x == "none" || x.Contains("too ") || x.Contains("over the");
        }

        // ---------------------------------------------------------------- go / no-go

        /// <summary>The launch poll: each discipline answers go or no-go from its own check.</summary>
        public static CheckReport GoNoGo(EngSnapshot s, EngPlan plan, PowerReport power, IList<string> staging, IList<CheckReport> checks, IList<string> lessons)
        {
            var r = new CheckReport { Key = "gonogo", Title = "Go / no-go" };
            int no = 0, caution = 0;
            Action<string, int, string> poll = (who, lv, why) =>
            {
                if (lv == 2) no++; else if (lv == 1) caution++;
                r.Lines.Add(new SheetLine(who + (why != "" ? ": " + why : ""), lv == 2 ? "NO-GO" : lv == 1 ? "GO, with a caution" : "GO", lv == 2));
            };
            Func<string, CheckReport> ck = key => checks.FirstOrDefault(c => c.Key == key);
            Func<CheckReport, string> first = c => c.Lines.Where(l => l.Bad).Select(l => l.Text).FirstOrDefault() ?? "";
            Func<CheckReport, int> level = c => c == null ? 0 : c.Bad ? 2 : (c.Rating == "check" || c.Rating == "marginal" || c.Rating == "tight" || c.Rating == "weak" || c.Rating == "patchy") ? 1 : 0;

            if (plan != null && plan.Known && plan.Have > 0)
                poll("Flight dynamics", plan.Margin < 0 ? 2 : plan.Margin < plan.Total * plan.MarginPct / 100 ? 1 : 0,
                    plan.Margin < 0 ? N(-plan.Margin) + " m/s short for \"" + plan.Mission + "\"" : N(plan.Margin) + " m/s spare for \"" + plan.Mission + "\"");
            else poll("Flight dynamics", 1, plan != null && plan.Known ? "no delta-v readout yet" : "no mission set, so delta-v is not judged");
            int stagingBad = staging == null ? 0 : staging.Count(x => x.StartsWith("!"));
            var launch = ck("launch");
            poll("Propulsion and staging", stagingBad > 0 || level(launch) == 2 ? 2 : Math.Max(level(launch), staging != null && staging.Count > 0 ? 1 : 0),
                stagingBad > 0 ? staging.First(x => x.StartsWith("!")).Substring(2) : launch != null && launch.Bad ? first(launch) : staging != null && staging.Count > 0 ? staging[0] : "");
            if (power != null) poll("Power", power.Problems > 0 || power.Rating.StartsWith("short") ? 2 : power.Rating == "tight" ? 1 : 0, power.Rating + (power.Where != "" ? " at " + power.Where : ""));
            var heat = ck("heat"); if (heat != null && heat.Rating != "nothing makes heat") poll("Thermal", level(heat), heat.Bad ? first(heat) : heat.Rating);
            var comms = ck("comms"); if (comms != null) poll("Comms", level(comms), comms.Bad ? first(comms) : comms.Rating);
            var bal = ck("balance"); if (bal != null) poll("Guidance and control", level(bal), bal.Bad ? first(bal) : bal.Rating);
            var land = ck("landing"); if (land != null) poll("Landing and recovery", level(land), land.Bad ? first(land) : land.Rating);
            var crew = ck("crew"); if (crew != null && crew.Rating != "uncrewed") poll("Crew systems", level(crew), crew.Bad ? first(crew) : crew.Rating);
            if (lessons != null && lessons.Count > 0) poll("Lessons from earlier flights", 1, lessons[0]);
            r.Rating = no > 0 ? "NO-GO (" + no + ")" : caution > 0 ? "GO with " + caution + " caution" + (caution == 1 ? "" : "s") : "GO";
            if (no > 0) r.Note = "Open the tab named in each no-go line for the numbers and a fix.";
            return r;
        }

        // ---------------------------------------------------------------- transfer windows

        static EngBody PlanetOf(EngBody b, IList<EngBody> bodies)
        {
            var at = b; int guard = 0;
            while (at != null && guard++ < 6) { var parent = DvPlanner.Find(bodies, at.Parent); if (parent == null || parent.IsStar) return at; at = parent; }
            return b;
        }

        /// <summary>When each planet can next be reached, how long the flight takes, and how long the wait for the way home is.</summary>
        public static CheckReport Windows(EngSnapshot s, string destination, Func<double, string> date = null)
        {
            var r = new CheckReport { Key = "windows", Title = "Transfer windows" };
            var home = s.Home;
            if (home == null || s.Bodies.Count == 0) { r.Rating = "no data"; r.Add("The bodies of this save could not be read."); return r; }
            var planet = PlanetOf(home, s.Bodies);
            var star = DvPlanner.Find(s.Bodies, planet.Parent);
            double day = s.Sheet != null ? s.Sheet.Fact("day", 21600) : 21600;
            string best = "";
            var rows = new List<KeyValuePair<double, SheetLine>>();
            if (star != null)
                foreach (var b in s.Bodies.Where(x => !x.IsStar && x.Parent == planet.Parent && x.Name != planet.Name))
                {
                    double phase;
                    if (!s.Phases.TryGetValue(b.Name, out phase) || b.Sma <= 0 || planet.Sma <= 0) continue;
                    double ideal = EngMath.TransferPhase(planet.Sma, b.Sma);
                    ideal = ((ideal % 360) + 360) % 360;
                    double wait = EngMath.TimeToWindow(star.Mu, planet.Sma, b.Sma, phase, ideal);
                    if (wait < 0) continue;
                    double flight = DvPlanner.HalfPeriod(star.Mu, planet.Sma, b.Sma);
                    // Another star, or a body so far out that the coast runs to thousands of years, is not a transfer to plan a window for.
                    if (double.IsNaN(flight) || double.IsInfinity(flight) || flight > 200 * 426 * day || b.Sma > planet.Sma * 500) continue;
                    double t1 = planet.Period > 0 ? planet.Period : 2 * Math.PI * Math.Sqrt(Math.Pow(planet.Sma, 3) / star.Mu), t2 = b.Period > 0 ? b.Period : 2 * Math.PI * Math.Sqrt(Math.Pow(b.Sma, 3) / star.Mu);
                    double synodic = Math.Abs(1 / (1 / t1 - 1 / t2));
                    // The way home: the window back opens when home sits the right angle ahead of the craft's planet.
                    double back = ((EngMath.TransferPhase(b.Sma, planet.Sma) % 360) + 360) % 360;
                    double phaseOnArrival = ((360 - phase) + (wait + flight) * (EngMath.MeanMotion(star.Mu, planet.Sma) - EngMath.MeanMotion(star.Mu, b.Sma))) % 360;
                    phaseOnArrival = ((phaseOnArrival % 360) + 360) % 360;
                    double stay = EngMath.TimeToWindow(star.Mu, b.Sma, planet.Sma, phaseOnArrival, back);
                    bool mine = string.Equals(b.Name, destination, StringComparison.OrdinalIgnoreCase);
                    var line = new SheetLine(b.Name + ": window opens " + (wait < day ? "today" : "in " + N(wait / day) + " days") + (s.UT > 0 && date != null ? " (" + date(s.UT + wait) + ")" : "") + ", flight " + N(flight / day) + " days"
                        + (stay >= 0 ? ", then " + N(stay / day) + " days there before the window home" : "") + "; windows repeat every " + N(synodic / day) + " days.",
                        wait < day ? "now" : N(wait / day) + " d") { Tag = mine ? "your mission" : "" };
                    rows.Add(new KeyValuePair<double, SheetLine>(mine ? -1 : wait, line));
                    if (mine) best = wait < day ? "open now" : "in " + N(wait / day) + " days";
                }
            foreach (var row in rows.OrderBy(x => x.Key)) r.Lines.Add(row.Value);
            foreach (var m in s.Bodies.Where(x => x.Parent == home.Name).OrderBy(x => x.Sma))
                r.Add(m.Name + ": a moon of " + home.Name + ", so there is no window to wait for. Flight about " + EngMath.Dur(DvPlanner.HalfPeriod(home.Mu, home.Radius + DvPlanner.LowOrbitAlt(home), m.Sma)) + ".", "any time");
            if (r.Lines.Count == 0) { r.Rating = "no data"; r.Add("No planet positions were read."); return r; }
            r.Rating = best != "" ? destination + " " + best : rows.Count > 0 ? "next: " + rows.OrderBy(x => x.Key).First().Value.Text.Split(':')[0] : "any time";
            r.Note = "Windows are for a Hohmann transfer between circular orbits: good to within a few days. Leaving up to a week either side costs little.";
            return r;
        }

        // ---------------------------------------------------------------- contracts

        static readonly Regex Number = new Regex(@"\b(\d[\d,]*)\b", RegexOptions.Compiled);

        /// <summary>Which active contracts this craft could fly, by reading each condition against the craft.</summary>
        public static CheckReport Contracts(EngSnapshot s)
        {
            var r = new CheckReport { Key = "contracts", Title = "Contracts" };
            var sheet = s.Sheet;
            if (s.Contracts.Count == 0) { r.Rating = "none active"; r.Add("No active contracts."); return r; }
            int fits = 0;
            foreach (var c in s.Contracts.Take(12))
            {
                int met = 0, missed = 0, unknown = 0; var notes = new List<string>();
                foreach (var p in c.Params.Where(x => !x.Done))
                {
                    string t = p.Text.ToLowerInvariant();
                    bool? ok = null;
                    var num = Number.Match(t); double n = 0;
                    if (num.Success) double.TryParse(num.Groups[1].Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out n);
                    if (t.Contains("antenna")) ok = sheet.Parts.Any(x => x.Of("antenna").Any(m => m.T("type") != "internal"));
                    else if (t.Contains("docking port")) ok = sheet.Count("dock") > 0;
                    else if (t.Contains("generate power") || t.Contains("can generate") || t.Contains("power generator")) ok = sheet.Count("solar") + sheet.Count("generator") > 0;
                    else if ((t.Contains("crew capacity") || t.Contains("room for") || t.Contains("seats")) && n > 0) ok = sheet.Parts.Sum(x => x.Crew) >= n;
                    else if (t.Contains("wheels")) ok = sheet.Parts.Any(x => x.Of("leg").Any(m => m.T("type").Contains("wheel")));
                    else if (t.Contains("units of") && n > 0)
                    {
                        var res = sheet.Parts.SelectMany(x => x.Res).Select(x => x.Name).Distinct().FirstOrDefault(x => t.Contains(Spaced(x)) || t.Contains(x.ToLowerInvariant()));
                        ok = res != null && sheet.Max(res) >= n;
                    }
                    else
                    {
                        // A part named in the condition: is it on the craft?
                        string named = s.PartTitles.Where(x => x.Length >= 6 && t.Contains(x.ToLowerInvariant())).OrderByDescending(x => x.Length).FirstOrDefault();
                        if (named != null) ok = sheet.Parts.Any(x => string.Equals(x.Title, named, StringComparison.OrdinalIgnoreCase));
                    }
                    if (ok == true) met++;
                    else if (ok == false) { missed++; notes.Add(p.Text); }
                    else unknown++;
                }
                if (met + missed == 0) continue;
                bool fit = missed == 0;
                if (fit) fits++;
                r.Lines.Add(new SheetLine(c.Title + (fit ? ": this craft has what it asks for" + (unknown > 0 ? " (" + unknown + " condition" + (unknown == 1 ? " is" : "s are") + " about the flight itself)" : "") : ": missing " + string.Join("; ", notes.Take(3).ToArray())), fit ? "fits" : met + " of " + (met + missed)));
            }
            if (r.Lines.Count == 0) r.Add(s.Contracts.Count + " active contract" + (s.Contracts.Count == 1 ? "" : "s") + ", none with conditions about how the craft is built.");
            r.Rating = fits > 0 ? fits + " fit" + (fits == 1 ? "s" : "") : "none fit yet";
            r.Note = "Only the conditions about the craft itself are read (parts, seats, antennas, power, resources). Where it has to go is up to the flight.";
            return r;
        }

        static string Spaced(string camel) { return Regex.Replace(camel, "([a-z])([A-Z])", "$1 $2").ToLowerInvariant(); }

        // ---------------------------------------------------------------- science

        static readonly string[][] Situations =
        {
            new[] { "1", "SrfLanded", "landed" }, new[] { "2", "SrfSplashed", "splashed down" }, new[] { "4", "FlyingLow", "flying low" },
            new[] { "8", "FlyingHigh", "flying high" }, new[] { "16", "InSpaceLow", "in low orbit" }, new[] { "32", "InSpaceHigh", "in high orbit" },
        };

        /// <summary>What the experiments aboard can still earn at the destination, from the save's science archive.</summary>
        public static CheckReport Science(EngSnapshot s, EngPlan plan)
        {
            var r = new CheckReport { Key = "science", Title = "Science" };
            if (s.Experiments.Count == 0)
            {
                bool any = s.Sheet != null && (s.Sheet.Count("science") > 0 || s.Sheet.HasModule("Experiment"));
                r.Rating = any ? "not read" : "none aboard";
                r.Add(any ? "Experiments are aboard, but the science archive is not available here (sandbox game, or a mod replaces it)." : "No science experiments aboard.");
                return r;
            }
            string body = plan != null && plan.Known ? plan.Body : s.Body;
            var target = s.BodyNamed(body);
            bool land = plan != null && plan.Known ? plan.Land : true;
            bool air = target != null && target.AtmoDepth > 0, surface = target == null || target.HasSurface;
            int fresh = 0, done = 0;
            foreach (var e in s.Experiments)
            {
                var open = new List<string>(); var spent = new List<string>();
                foreach (var sit in Situations)
                {
                    int bit = int.Parse(sit[0], CultureInfo.InvariantCulture);
                    if ((e.Situations & bit) == 0) continue;
                    if (bit <= 2 && (!land || !surface)) continue;
                    if ((bit == 4 || bit == 8) && !air) continue;
                    if (bit == 2 && !(target != null && target.IsHome)) continue;
                    string prefix = e.Id + "@" + body + sit[1];
                    bool biome = (e.Biomes & bit) != 0;
                    var hits = s.ScienceDone.Where(k => biome ? k.Key.StartsWith(prefix, StringComparison.Ordinal) : k.Key == prefix).ToList();
                    if (hits.Count == 0) open.Add(sit[2] + (biome ? " (every biome)" : ""));
                    else if (biome) open.Add(sit[2] + " (" + hits.Count + " biome" + (hits.Count == 1 ? "" : "s") + " done, more to find)");
                    else if (hits[0].Value < 0.9) open.Add(sit[2] + " (" + N(hits[0].Value * 100) + "% gathered)");
                    else spent.Add(sit[2]);
                }
                if (open.Count > 0) fresh++; else done++;
                r.Add((e.Title != "" ? e.Title : e.Id) + " at " + body + ": " + (open.Count > 0 ? "still to gather " + string.Join(", ", open.ToArray()) : "nothing new" + (spent.Count > 0 ? " (" + string.Join(", ", spent.ToArray()) + " already done)" : " for this mission")),
                    open.Count > 0 ? "worth taking" : "already done");
            }
            r.Rating = fresh + " of " + s.Experiments.Count + " still pay at " + body;
            if (done > 0 && fresh == 0) r.Fix("Every experiment aboard has already given its science at " + body + ". They are dead weight on this trip unless a contract wants them.");
            if (plan == null || !plan.Known) r.Note = "No mission is set, so this is judged for " + body + ". Pick a destination in the mission row.";
            return r;
        }

        // ---------------------------------------------------------------- lessons from history

        /// <summary>Findings from earlier failure investigations that fit this craft: same name, or a part the findings mention.</summary>
        public static List<string> Lessons(IEnumerable<Incident> incidents, EngSnapshot s)
        {
            var r = new List<string>();
            if (incidents == null || s == null) return r;
            var titles = s.Sheet != null ? s.Sheet.Parts.Select(p => p.Title.ToLowerInvariant()).Where(t => t.Length >= 6).Distinct().ToList() : new List<string>();
            string craft = (s.Vessel ?? "").ToLowerInvariant();
            foreach (var inc in incidents.Where(i => i.Status == "done" && (i.Fixes != "" || i.Cause != "")).Reverse())
            {
                string text = (inc.Cause + " " + inc.Findings + " " + inc.Fixes).ToLowerInvariant();
                string name = (inc.VesselName ?? "").ToLowerInvariant();
                bool sameCraft = craft != "" && name != "" && (craft == name || craft.Split(' ')[0].Length >= 4 && name.StartsWith(craft.Split(' ')[0]));
                string part = titles.FirstOrDefault(t => text.Contains(t));
                if (!sameCraft && part == null) continue;
                r.Add((inc.VesselName != "" ? inc.VesselName : "An earlier craft") + " was lost: " + (inc.Cause != "" ? inc.Cause.TrimEnd('.') + ". " : "") + (inc.Fixes != "" ? "Next time: " + inc.Fixes : "")
                    + (sameCraft ? "" : " (this craft also carries the " + s.Sheet.Parts.First(p => p.Title.ToLowerInvariant() == part).Title + ")"));
                if (r.Count >= 4) break;
            }
            return r;
        }

        // ---------------------------------------------------------------- in flight

        /// <summary>Things worth a glance right now, from the craft's state in flight.</summary>
        public static CheckReport Flight(EngSnapshot s, PowerReport power)
        {
            var r = new CheckReport { Key = "flight", Title = "In flight" };
            var sheet = s.Sheet;
            if (sheet == null || !(s.Scene == "flight" || s.Scene == "map")) { r.Rating = "not flying"; r.Add("These checks run while a craft is flying."); return r; }
            bool bad = false, warn = false;
            var body = s.BodyNamed(s.Body);
            bool inAir = s.BodyAtmosphere > 0 && s.Altitude < s.BodyAtmosphere;
            bool landed = s.Situation.StartsWith("landed") || s.Situation.StartsWith("splashed") || s.Situation.StartsWith("on the launch");
            double ec = sheet.Amount(CraftSheet.EC), ecMax = sheet.Max(CraftSheet.EC);

            if (ecMax > 0)
            {
                bool low = ec < ecMax * 0.15;
                r.Add("Electric charge " + N(ec) + " of " + N(ecMax) + (power != null && power.GenThere < power.Coast ? ", and the craft uses more than it makes" : ""), N(ec / ecMax * 100) + "%", low);
                bad |= low;
                if (low) r.Fix("Power is nearly gone: extend the panels, point them at the sun, or shut down what is not needed" + (sheet.Count("command") > 0 && sheet.Parts.All(p => p.Crew == 0) ? ". A probe with no charge cannot be controlled." : "."));
            }
            var folded = sheet.Parts.Where(p => p.Deployed == 0 && (p.Has("solar") || p.Has("antenna") || p.Has("radiator"))).ToList();
            var out_ = sheet.Parts.Where(p => p.Deployed == 1 && (p.Has("solar") || p.Has("antenna") || p.Has("radiator"))).ToList();
            if (!inAir && !landed && folded.Count > 0)
            {
                warn = true;
                r.Add("In space with " + string.Join(", ", folded.GroupBy(p => p.Title).Select(g => Count(g.Count(), g.Key)).Take(4).ToArray()) + " still folded.", "extend");
            }
            if (inAir && !landed && out_.Count > 0 && s.Speed > 150)
            {
                bad = true;
                r.Add("Flying at " + N(s.Speed) + " m/s in the air with " + string.Join(", ", out_.GroupBy(p => p.Title).Select(g => Count(g.Count(), g.Key)).Take(4).ToArray()) + " extended: the airflow can tear them off.", "retract", true);
            }
            if (s.HasOrbit && s.BodyAtmosphere > 0 && s.PeA < s.BodyAtmosphere && s.PeA > 0 && !inAir)
            {
                r.Add("Periapsis " + EngMath.Dist(s.PeA) + " is inside " + s.Body + "'s atmosphere (top at " + EngMath.Dist(s.BodyAtmosphere) + "): the orbit will decay" + (out_.Count > 0 ? ", and folding parts should be in before then" : "") + ".", "aerobraking");
                warn = true;
            }
            // Coming down on a body with no air: where the braking burn has to start.
            if (body != null && !landed && s.VerticalSpeed < -5 && s.BodyAtmosphere <= 0 && s.Stages.Count > 0)
            {
                var st = s.Stages.Where(x => x.DvActual > 1 || x.DvVac > 1).OrderByDescending(x => x.Stage).FirstOrDefault();
                double g = s.BodyGravity > 0 ? s.BodyGravity : 1.6;
                if (st != null && st.TwrVac > 0)
                {
                    double accel = st.TwrVac * G0 - g, speed = s.Speed;
                    double height = s.RadarAlt >= 0 ? s.RadarAlt : s.Altitude;
                    if (accel <= 0) { bad = true; r.Add("Descending at " + N(-s.VerticalSpeed) + " m/s and the engine cannot beat " + s.Body + "'s gravity: it cannot stop.", "", true); }
                    else
                    {
                        double need = speed * speed / (2 * accel);
                        bool late = need > height * 0.9;
                        r.Add("Stopping from " + N(speed) + " m/s takes about " + EngMath.Dist(need) + " at full thrust; the ground is " + EngMath.Dist(height) + " below.", late ? "burn now" : "start by " + EngMath.Dist(need * 1.2), late);
                        bad |= late;
                    }
                }
            }
            // Parachutes: armed, and is it safe to open them.
            var chutes = sheet.Parts.Where(p => p.Has("chute")).ToList();
            if (chutes.Count > 0 && inAir && s.VerticalSpeed < -10)
            {
                var states = chutes.SelectMany(p => p.Of("chute")).ToList();
                int stowed = states.Count(m => m.T("state") == "STOWED");
                bool unsafeNow = states.Any(m => m.T("safe") == "UNSAFE"), risky = states.Any(m => m.T("safe") == "RISKY");
                if (stowed == states.Count && s.RadarAlt >= 0 && s.RadarAlt < 3000 && !unsafeNow) { bad = true; r.Add("Falling at " + N(-s.VerticalSpeed) + " m/s, " + EngMath.Dist(s.RadarAlt) + " up, and no parachute is armed.", "deploy", true); }
                else if (unsafeNow) { warn = true; r.Add("Too fast and hot to open the parachutes yet: they would tear off.", "wait"); }
                else if (risky) r.Add("Parachutes can open now but it is still risky at this speed.", "soon");
            }
            var gear = sheet.Parts.Where(p => p.Modules.Any(m => m.Name == "ModuleWheelDeployment" && m.T("stateString") == "Retracted")).ToList();
            if (gear.Count > 0 && !landed && s.VerticalSpeed < -1 && s.RadarAlt >= 0 && s.RadarAlt < 1500) { warn = true; r.Add(gear.Count + " landing leg" + (gear.Count == 1 ? " is" : "s are") + " still retracted, " + EngMath.Dist(s.RadarAlt) + " above the ground.", "lower"); }
            if (s.GForce > 8) { warn = true; r.Add("Pulling " + N(s.GForce, "0.0") + " g.", "ease off"); }
            if (r.Lines.Count == 0) r.Add("Nothing needs attention right now.");
            r.Rating = bad ? "act now" : warn ? "check" : "nominal";
            return r;
        }
    }
}
