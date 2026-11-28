using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KerbalNews
{
    /// <summary>A relay craft already flying: where it is and how strong its relay antennas are together.</summary>
    public class EngRelay { public string Name = "", Body = ""; public double Power, Altitude; }

    /// <summary>A rocket that has reached orbit before, kept so the team can say what is proven.</summary>
    public class ProvenRocket
    {
        public string Name = "";
        public double LaunchMass, OrbitMass, Cost, UT;
        public int Flights = 1;
        public void Save(DataNode n) { n.AddText("name", Name); n.Add("launch", LaunchMass); n.Add("orbit", OrbitMass); n.Add("cost", Cost); n.Add("ut", UT); n.Add("flights", Flights); }
        public static ProvenRocket Load(DataNode n) { return new ProvenRocket { Name = n.GetText("name"), LaunchMass = n.GetDouble("launch"), OrbitMass = n.GetDouble("orbit"), Cost = n.GetDouble("cost"), UT = n.GetDouble("ut"), Flights = Math.Max(1, n.GetInt("flights", 1)) }; }
    }

    /// <summary>What the team said a flight would do, and what it then did.</summary>
    public class EngPrediction
    {
        public string Id = "", VesselId = "", Craft = "";
        public double UT;
        /// <summary>label|unit|predicted|actual (actual empty until it happens).</summary>
        public List<string[]> Items = new List<string[]>();
        // Working values while the flight is under way (not shown).
        public double LaunchDv, PeakG, LastVertical;
        public bool Orbited, Landed;

        public void Set(string label, string unit, double predicted) { Items.Add(new[] { label, unit, predicted.ToString("R", CultureInfo.InvariantCulture), "" }); }
        public void Actual(string label, double value)
        {
            var it = Items.FirstOrDefault(x => x[0] == label);
            if (it != null) it[3] = value.ToString("R", CultureInfo.InvariantCulture);
        }
        public static double Num(string s) { double d; return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0; }

        public void Save(DataNode n)
        {
            n.Add("id", Id); n.Add("vessel", VesselId); n.AddText("craft", Craft); n.Add("ut", UT); n.Add("launchDv", LaunchDv); n.Add("peakG", PeakG); n.Add("orbited", Orbited); n.Add("landed", Landed);
            foreach (var it in Items) n.AddText("item", string.Join("|", it));
        }
        public static EngPrediction Load(DataNode n)
        {
            var p = new EngPrediction { Id = n.Get("id", ""), VesselId = n.Get("vessel", ""), Craft = n.GetText("craft"), UT = n.GetDouble("ut"), LaunchDv = n.GetDouble("launchDv"), PeakG = n.GetDouble("peakG"), Orbited = n.GetBool("orbited"), Landed = n.GetBool("landed") };
            foreach (var raw in n.GetAll("item")) { var parts = TextCodec.Decode(raw).Split('|'); if (parts.Length == 4) p.Items.Add(parts); }
            return p;
        }
    }

    /// <summary>
    /// Delta-v by stage worked out from the craft sheet alone, for when the game's own readout is not there
    /// (a saved craft being reviewed, a design being tried in a meeting). An estimate: it burns each dropped group's
    /// fuel with every engine running at the time, which is right for simple stacks and asparagus and close for the rest.
    /// </summary>
    public static class SheetDv
    {
        const double G0 = 9.80665;

        public static List<EngStage> Solve(CraftSheet sheet)
        {
            var list = new List<EngStage>();
            if (sheet == null || sheet.Parts.Count == 0) return list;
            var parts = sheet.Parts.Where(p => !p.Has("clamp")).ToList();
            var left = parts.Select(p => p.ResMass).ToArray();
            int top = parts.Max(p => p.Stage);
            for (int k = top; k >= 0; k--)
            {
                var alive = Enumerable.Range(0, parts.Count).Where(i => parts[i].DropStage < k).ToList();
                var engines = alive.Where(i => parts[i].Stage >= k).SelectMany(i => parts[i].Of("engine").Where(m => m.Staged && m.N("thrust") > 0)).ToList();
                if (engines.Count == 0) continue;
                var props = new HashSet<string>(engines.SelectMany(m => m.T("props").Split(',')).Select(x => x.Trim()).Where(x => x != "" && x != CraftSheet.EC && x != "IntakeAir"));
                Func<int, bool> fuelled = i => left[i] > 1e-6 && parts[i].Res.Any(r => props.Contains(r.Name) && r.Amount > 0);
                var withFuel = alive.Where(fuelled).ToList();
                if (withFuel.Count == 0) continue;
                // The group that leaves next takes its fuel with it, so that is what this burn uses. With nothing left to drop, burn the rest.
                int next = withFuel.Max(i => parts[i].DropStage);
                var group = next >= 0 ? withFuel.Where(i => parts[i].DropStage == next).ToList() : withFuel;
                double fuel = group.Sum(i => left[i]);
                double m0 = alive.Sum(i => parts[i].DryMass + left[i]);
                double m1 = m0 - fuel;
                if (m1 <= 0 || fuel <= 0) continue;
                double thrust = engines.Sum(m => m.N("thrust") * m.N("limit", 100) / 100);
                double flow = engines.Sum(m => m.N("ispVac") > 0 ? m.N("thrust") * m.N("limit", 100) / 100 / m.N("ispVac") : 0);
                double flowAsl = engines.Sum(m => m.N("ispAsl") > 0 ? m.N("thrust") * m.N("limit", 100) / 100 / m.N("ispVac") : 0);
                if (thrust <= 0 || flow <= 0) continue;
                double isp = thrust / flow;
                double thrustAsl = engines.Sum(m => m.N("ispVac") > 0 ? m.N("thrust") * m.N("limit", 100) / 100 * m.N("ispAsl") / m.N("ispVac") : 0);
                double ispAsl = flowAsl > 0 ? thrustAsl / flowAsl : 0;
                var st = new EngStage
                {
                    Stage = k, StartMass = m0, EndMass = m1, ThrustVac = thrust, IspVac = isp,
                    DvVac = isp * G0 * Math.Log(m0 / m1), DvAsl = ispAsl > 0 ? ispAsl * G0 * Math.Log(m0 / m1) : 0,
                    TwrVac = thrust / (m0 * G0), TwrAsl = thrustAsl / (m0 * G0), BurnTime = fuel * 1000 * G0 / flow / 1000,
                };
                st.DvActual = st.DvVac; st.TwrActual = st.TwrVac;
                list.Add(st);
                foreach (int i in group) left[i] = 0;
            }
            return list;
        }
    }

    /// <summary>
    /// The mission-side tools: timeline, reliability, what the mission needs, setup and action groups, mining,
    /// ascent and aerobraking, planes and rovers, docking, launcher rating, tech advice, relays. All arithmetic, no AI call.
    /// </summary>
    public static class EngMissionTools
    {
        static string N(double v, string f = "0") { return v.ToString(f, CultureInfo.InvariantCulture); }
        const double G0 = 9.80665;
        static double Day(CraftSheet s) { double d = s != null ? s.Fact("day", 21600) : 21600; return d > 0 ? d : 21600; }
        static string Days(double seconds, CraftSheet s) { double d = seconds / Day(s); return d < 1 ? EngMath.Dur(seconds) : N(d, d < 10 ? "0.#" : "0") + " days"; }
        static string Count(int n, string title) { return (n > 1 ? n + " × " : "") + title; }

        // ---------------------------------------------------------------- timeline

        /// <summary>How long the craft waits at a planet before the way home opens (a Hohmann return), seconds. 0 for moons of home.</summary>
        public static double WaitForReturn(IList<EngBody> bodies, EngBody home, EngBody target)
        {
            if (bodies == null || home == null || target == null) return 0;
            if (string.Equals(target.Parent, home.Name, StringComparison.OrdinalIgnoreCase)) return 0;
            var parent = DvPlanner.Find(bodies, home.Parent);
            var planet = string.Equals(target.Parent, home.Parent, StringComparison.OrdinalIgnoreCase) ? target : DvPlanner.Find(bodies, target.Parent);
            if (parent == null || planet == null || home.Sma <= 0 || planet.Sma <= 0 || planet.Name == home.Name) return 0;
            double w1 = Math.Sqrt(parent.Mu / Math.Pow(home.Sma, 3)), w2 = Math.Sqrt(parent.Mu / Math.Pow(planet.Sma, 3));
            double t = DvPlanner.HalfPeriod(parent.Mu, home.Sma, planet.Sma);
            // Home is ahead of the planet by (w1 t - pi) on arrival, and has to be (pi - w1 t) ahead when leaving.
            double now = w1 * t - Math.PI, need = Math.PI - w1 * t;
            double two = 2 * Math.PI;
            double diff = w1 > w2 ? need - now : now - need;
            diff = ((diff % two) + two) % two;
            double rate = Math.Abs(w1 - w2);
            return rate > 0 ? diff / rate : 0;
        }

        /// <summary>The mission leg by leg: what each costs, which stage pays, how long it takes and what the craft lives on meanwhile.</summary>
        public static CheckReport Timeline(EngSnapshot s, EngPlan plan, EngMissions.Dest dest, PowerReport power, List<CheckReport> checks)
        {
            var r = new CheckReport { Key = "timeline", Title = "Timeline" };
            if (plan == null || !plan.Known) { r.Rating = "no mission"; r.Add("Pick a destination in the mission row and the trip is laid out here leg by leg."); return r; }
            var sheet = s.Sheet;
            var home = s.Home; var there = s.BodyNamed(plan.Body);
            double travel = dest != null ? dest.Travel : 0, wait = plan.Return && !plan.Flyby ? WaitForReturn(s.Bodies, home, there) : 0;
            double clock = 0; bool anyShort = false;
            foreach (var leg in plan.Legs)
            {
                double t = leg.Name.StartsWith("Launch") ? 360 : leg.Name.StartsWith("Transfer") || leg.Name.StartsWith("Flyby") ? travel
                    : leg.Name.StartsWith("Return") || leg.Name.StartsWith("Free-return") ? travel : leg.Name.StartsWith("Land") || leg.Name.StartsWith("Ascent") ? 600 : 0;
                if (leg.Name.StartsWith("Return") && wait > 0)
                {
                    r.Add("Day " + N(clock / Day(sheet) + 1) + ": wait at " + plan.Body + " for the way home to open", "about " + Days(wait, sheet));
                    clock += wait;
                }
                r.Add("Day " + N(clock / Day(sheet) + 1) + ": " + leg.Name, N(leg.Dv) + " m/s" + (leg.Stages != "" ? ", " + leg.Stages : "") + (t > 3600 ? ", " + Days(t, sheet) : ""), leg.Short);
                anyShort |= leg.Short;
                clock += t;
            }
            r.Add("Whole trip", "about " + Days(clock, sheet));
            if (power != null) r.Add("Power " + power.Where, power.Rating, power.Problems > 0);
            foreach (var key in new[] { "comms", "crew", "landing", "heat" })
            {
                var ck = checks != null ? checks.FirstOrDefault(x => x.Key == key) : null;
                if (ck != null && ck.Rating != "" && ck.Rating != "not needed" && ck.Rating != "nothing makes heat") r.Add(ck.Title + " for this trip", ck.Rating, ck.Bad);
            }
            if (wait > 0) r.Note = "The wait is for a minimum-energy return. Leaving earlier is possible but costs more delta-v than the budget shows.";
            r.Rating = anyShort ? "runs out of delta-v" : "about " + Days(clock, sheet);
            return r;
        }

        // ---------------------------------------------------------------- reliability (Kerbalism)

        /// <summary>Engine burn time and ignitions against what Kerbalism rates them for, and parts likely to fail on a long trip.
        /// Only when Kerbalism's reliability feature is switched on in this save.</summary>
        public static CheckReport Reliability(CraftSheet sheet, IList<EngStage> stages, EngPlan plan, double tripSeconds)
        {
            var r = new CheckReport { Key = "reliability", Title = "Reliability" };
            if (!sheet.ModFacts.ContainsKey("Kerbalism")) { r.Rating = "parts do not fail"; r.Add("No part failure mod was found, so parts do not wear out or fail on their own in this save."); return r; }
            if (!ModKnowledge.FeatureOn(sheet, "Kerbalism.Reliability")) { r.Rating = "off in this save"; r.Add("Kerbalism is installed but its Reliability feature is switched off in this save, so parts do not fail and engines have no ignition or burn limits."); return r; }
            bool bad = false, warn = false;
            foreach (var g in sheet.Parts.Where(p => p.Has("engine") && p.Modules.Any(m => m.Name == "Reliability")).GroupBy(p => p.Name + "/" + p.Stage))
            {
                var p0 = g.First(); int n = g.Count();
                var rel = p0.Modules.First(m => m.Name == "Reliability");
                double ratedBurn = rel.Any("rated_operation_duration"), ratedIgn = rel.Any("rated_ignitions");
                var st = stages != null ? stages.FirstOrDefault(x => x.Stage == p0.Stage) : null;
                double burn = st != null ? st.BurnTime : 0;
                int ignitions = 1;
                if (plan != null && plan.Known) ignitions = Math.Max(1, plan.Legs.Count(l => (" " + l.Stages.Replace(",", " ") + " ").Contains(" " + p0.Stage + " ")));
                string label = Count(n, p0.Title) + " (stage " + p0.Stage + ")";
                if (ratedBurn > 0 && burn > 0)
                {
                    bool over = burn > ratedBurn, close = burn > ratedBurn * 0.8;
                    r.Add(label + ": burns " + EngMath.Dur(burn) + " to empty its tanks, rated for " + EngMath.Dur(ratedBurn), over ? "past its rating" : close ? "close to its rating" : "within rating", over);
                    bad |= over; warn |= close && !over;
                    if (over) r.Fix("Run " + p0.Title + " for less than " + EngMath.Dur(ratedBurn) + ": more engines sharing the burn, less fuel in that stage, or an engine rated for longer.");
                }
                if (ratedIgn > 0)
                {
                    bool over = ignitions > ratedIgn;
                    r.Add(label + ": " + N(ratedIgn) + " ignition" + (ratedIgn == 1 ? "" : "s") + " rated, the plan lights it " + ignitions + " time" + (ignitions == 1 ? "" : "s"), over ? "too few ignitions" : ignitions == ratedIgn ? "none spare" : "ok", over);
                    bad |= over; warn |= !over && ignitions == ratedIgn && ratedIgn > 1;
                    if (over) r.Fix("Give stage " + p0.Stage + " an engine with more ignitions, or plan its burns so it is lit at most " + N(ratedIgn) + " time" + (ratedIgn == 1 ? "" : "s") + ".");
                }
                if (ratedBurn <= 0 && ratedIgn <= 0) r.Add(label + ": no burn or ignition limit on this engine.");
            }
            // Parts that are likely to break over the length of the trip, and have no twin to take over.
            if (tripSeconds > 0)
            {
                var risky = new List<KeyValuePair<double, string>>();
                foreach (var g in sheet.Parts.Where(p => !p.Has("engine") && p.Modules.Any(m => m.Name == "Reliability" && m.Any("mtbf") > 0)).GroupBy(p => p.Name))
                {
                    var p0 = g.First(); int n = g.Count();
                    double mtbf = p0.Modules.First(m => m.Name == "Reliability").Any("mtbf");
                    double chance = 1 - Math.Exp(-tripSeconds / mtbf);
                    if (chance < 0.15) continue;
                    double all = Math.Pow(chance, n);
                    risky.Add(new KeyValuePair<double, string>(all, Count(n, p0.Title) + ": about " + N(chance * 100) + "% chance each of failing during the trip" + (n > 1 ? ", " + N(all * 100) + "% that all " + n + " fail" : ", and there is only one")));
                }
                foreach (var x in risky.OrderByDescending(x => x.Key).Take(8)) { r.Add(x.Value, x.Key > 0.3 ? "carry a spare" : "watch", x.Key > 0.5); warn |= x.Key > 0.3; bad |= x.Key > 0.5; }
                if (risky.Any(x => x.Key > 0.3)) r.Fix("For parts likely to fail: fit a second one, pick High quality in the part's menu in the builder (heavier and dearer, lasts longer), and bring an engineer to repair them.");
            }
            if (r.Lines.Count == 0) r.Add("Nothing on this craft is close to a reliability limit" + (tripSeconds > 0 ? " for a trip of " + Days(tripSeconds, sheet) : "") + ".");
            r.Note = "From Kerbalism's Reliability settings in this save. Engines can also fail to light: a spare engine or a test firing on the pad lowers the risk.";
            r.Rating = bad ? "problem" : warn ? "tight" : "ok";
            return r;
        }

        // ---------------------------------------------------------------- what the mission needs

        /// <summary>What any craft needs for this mission, each line set against what this craft has when there is one.</summary>
        public static CheckReport Requirements(EngSnapshot s, EngPlan plan, EngMissions.Dest dest, PowerReport power, List<CheckReport> checks, int crew)
        {
            var r = new CheckReport { Key = "needs", Title = "What it needs" };
            if (plan == null || !plan.Known) { r.Rating = "no mission"; r.Add("Pick a destination in the mission row to see what the mission asks of a craft."); return r; }
            var sheet = s.Sheet; var home = s.Home; var there = s.BodyNamed(plan.Body);
            bool has = sheet != null && sheet.Parts.Count > 0;
            int unmet = 0;
            Action<string, string, bool> need = (text, value, bad) => { r.Add(text, value, bad); if (bad) unmet++; };

            double total = plan.Total, withMargin = plan.Need;
            need("Delta-v: " + N(total) + " m/s, " + N(withMargin) + " with the " + N(plan.MarginPct) + "% margin", has && plan.Have > 0 ? "has " + N(plan.Have) : "", has && plan.Have > 0 && plan.Have < total);
            foreach (var leg in plan.Legs) r.Add("   " + leg.Name, N(leg.Dv) + " m/s");
            if (home != null) need("Lift-off from " + home.Name + ": thrust to weight 1.3 to 1.7 at the surface", has && s.Stages.Count > 0 ? "has " + N(LaunchTwr(s), "0.00") : "", has && s.Stages.Count > 0 && LaunchTwr(s) > 0 && LaunchTwr(s) < 1.15);
            if (there != null)
            {
                if (plan.Land && there.HasSurface)
                {
                    double g = CraftChecks.Grav(there);
                    bool air = there.AtmoDepth > 0;
                    if (air) need("Landing on " + there.Name + ": air at " + N(there.AtmoPressure, "0.###") + " atm" + (there.AtmoPressure < 0.2 ? ", too thin for parachutes alone: parachutes plus a braking burn" : ", parachutes work") , Rating(checks, "landing"), IsBad(checks, "landing"));
                    else need("Landing on " + there.Name + " (no air): engines only, thrust of at least " + N(2 * g / G0, "0.00") + " times the lander's weight at home (2 times its weight there), legs, about " + N(dest != null ? dest.Land : DvPlanner.Landing(there)) + " m/s", Rating(checks, "landing"), IsBad(checks, "landing"));
                }
                if (home != null && there.SunDistance > 0 && home.SunDistance > 0)
                {
                    double share = Math.Pow(home.SunDistance / there.SunDistance, 2);
                    string advice = share < 0.1 ? ": too weak for panels, bring an RTG or a reactor" : share < 0.5 ? ": a panel makes " + N(share * 100) + "% of its rated output, so the same power takes about " + N(1 / share, "0.#") + " times the panel area" : share > 2 ? ": fewer panels do, and radiators matter more" : "";
                    need("Power at " + there.Name + ": sunlight is " + N(share * 100) + "% of what it is at " + home.Name + advice + (power != null && power.Problems == 0 && share < 0.5 && share >= 0.1 ? "; this craft's panels still cover it" : ""), power != null ? power.Rating : "", power != null && power.Problems > 0);
                    if (plan.Land && there.RotPeriod > 0 && there.HasSurface)
                    {
                        double night = there.RotPeriod / 2;
                        double draw = power != null ? power.Always : 0;
                        r.Add("Night on the surface of " + there.Name + " lasts about " + EngMath.Dur(night) + (draw > 0 ? ": batteries for " + N(draw * night) + " EC at this craft's steady draw, or a power source that does not need the sun" : ": size batteries for it, or bring a power source that does not need the sun"), has ? "has " + N(sheet.Max(CraftSheet.EC)) + " EC" : "", has && draw > 0 && sheet.Max(CraftSheet.EC) < draw * night && (power == null || power.Steady <= draw));
                    }
                }
                if (has && sheet.HasFact("commnet") && sheet.Fact("commnet") == 0) r.Add("Comms: CommNet is off in this save, so no antenna is needed for control.");
                else if (home != null)
                {
                    double min, max; CraftChecks.Distances(home, there, s.Bodies, out min, out max);
                    double dsn = has && sheet.Fact("dsnPower") > 0 ? sheet.Fact("dsnPower") * Math.Max(1, sheet.Fact("dsnMod", 1)) : 250e9;
                    double mult = has && sheet.Fact("rangeMod") > 0 ? sheet.Fact("rangeMod") : 1;
                    double needPower = max > 0 ? Math.Pow(max / mult, 2) / dsn : 0;
                    need("Comms: " + there.Name + " is up to " + EngMath.Dist(max) + " from " + home.Name + ": antenna power of at least " + Power(needPower) + " to reach the tracking station direct (more for a usable signal), or a relay on the way", Rating(checks, "comms"), IsBad(checks, "comms"));
                }
            }
            if (plan.Return && home != null && home.AtmoDepth > 0)
            {
                double vEntry = DvPlanner.OrbitSpeed(home) + (dest != null ? dest.Transfer : 0);
                need("Coming home: entry at " + home.Name + " at about " + N(vEntry) + " m/s" + (vEntry > 3000 ? ": a heat shield with ablator" : vEntry > 2400 ? ": a heat shield is wise" : ": a pod survives a shallow entry") + ", and parachutes for what comes down", "", false);
            }
            if (crew > 0)
            {
                double trip = (dest != null ? dest.Travel : 0) * (plan.Return ? 2 : 1) + (plan.Return && !plan.Flyby ? WaitForReturn(s.Bodies, home, there) : 0);
                var rules = has ? sheet.CrewRules : new List<CrewRule>();
                if (rules.Count > 0 && trip > 0)
                    foreach (var rule in rules.Where(x => x.PerSec > 0 && x.Input != CraftSheet.EC).Take(4))
                    {
                        double want = rule.PerSec * crew * trip, have = sheet.Amount(rule.Input);
                        need("Crew: " + N(want) + " " + rule.Input + " for " + crew + " over " + Days(trip, sheet) + (rule.Varies ? " (the rate varies)" : ""), "has " + N(have), have < want);
                    }
                else if (trip > 0) r.Add("Crew: " + crew + " aboard for about " + Days(trip, sheet) + ". No life support mod is installed, so they need only seats.");
            }
            r.Rating = !has ? "for any craft" : unmet == 0 ? "all met" : unmet + " not met";
            if (!has) r.Note = "No craft is open, so these are the bare needs. Open one in the builder to see each line checked.";
            return r;
        }

        static string Rating(List<CheckReport> checks, string key) { var c = checks != null ? checks.FirstOrDefault(x => x.Key == key) : null; return c != null ? c.Rating : ""; }
        static bool IsBad(List<CheckReport> checks, string key) { var c = checks != null ? checks.FirstOrDefault(x => x.Key == key) : null; return c != null && c.Bad; }
        static double LaunchTwr(EngSnapshot s) { var first = s.Stages.Where(x => x.DvVac > 1).OrderByDescending(x => x.Stage).FirstOrDefault(); return first == null ? 0 : first.TwrAsl > 0 ? first.TwrAsl : first.TwrVac; }
        static string Power(double p) { return p >= 1e9 ? N(p / 1e9, "0.#") + "G" : p >= 1e6 ? N(p / 1e6, "0.#") + "M" : p >= 1e3 ? N(p / 1e3, "0.#") + "k" : N(p); }

        // ---------------------------------------------------------------- setup and action groups

        static IEnumerable<string> GroupsOf(CraftPart p) { return p.Modules.Select(m => m.T("groups")).Where(g => g != ""); }
        static bool Grouped(CraftPart p) { return GroupsOf(p).Any(); }
        static bool InGroup(CraftSheet sheet, string group) { return sheet.Parts.Any(p => GroupsOf(p).Any(g => g.IndexOf(">" + group, StringComparison.OrdinalIgnoreCase) >= 0 || g.IndexOf("+" + group, StringComparison.OrdinalIgnoreCase) >= 0)); }

        /// <summary>Things set in the builder that are easy to forget: action groups, abort, parts that can only be worked by hand.</summary>
        public static CheckReport Setup(CraftSheet sheet, int crew)
        {
            var r = new CheckReport { Key = "setup", Title = "Setup" };
            bool read = sheet.Parts.Any(p => p.Modules.Any(m => m.Num.ContainsKey("actions")));
            if (!read) { r.Rating = "not read"; r.Add("Action groups could not be read from this craft."); return r; }
            int bad = 0, tips = 0;
            Action<string, string, Func<CraftPart, bool>, string> kind = (what, action, pick, why) =>
            {
                var parts = sheet.Parts.Where(pick).ToList();
                if (parts.Count < 2) return;
                int loose = parts.Count(p => !Grouped(p));
                if (loose == 0) { r.Add(parts.Count + " " + what + ": on an action group", "ok"); return; }
                r.Add(loose + " of " + parts.Count + " " + what + " on no action group", "by hand only");
                r.Fix("Put \"" + action + "\" for the " + what + " on a custom action group: " + why);
                tips++;
            };
            kind("solar panels that fold", "Toggle Solar Panel", p => p.Has("solar") && p.Deployed >= 0, "one key opens them all after the fairing goes, and closes them before aerobraking.");
            kind("antennas that fold", "Toggle Antenna", p => p.Has("antenna") && p.Deployed >= 0, "one key opens them once in space.");
            kind("radiators that fold", "Toggle Radiator", p => p.Has("radiator") && p.Deployed >= 0, "they must be open before a reactor or drill is switched on.");
            kind("drills", "Toggle Drill", p => p.Has("drill"), "all drills start and stop together.");
            kind("science experiments", "run the experiment", p => p.Has("science"), "one key takes every reading at once in each new place.");
            // Engines and parachutes that nothing will ever fire.
            foreach (var g in sheet.Parts.Where(p => (p.Has("engine") || p.Has("chute")) && !p.Modules.Any(m => (m.Kind == "engine" || m.Kind == "chute") && m.Staged) && !Grouped(p)).GroupBy(p => p.Name))
            {
                r.Add(Count(g.Count(), g.First().Title) + ": staging is off and no action group works it", "by hand only", true);
                bad++;
            }
            if (sheet.Parts.Any(p => p.Has("leg") && p.Of("leg").Any(m => m.T("type") == "landing leg")) && !InGroup(sheet, "Gear")) { r.Add("Landing legs are not on the Gear key.", "check"); tips++; }
            if (sheet.Parts.Any(p => p.Of("leg").Any(m => m.T("type").Contains("wheel"))) && !InGroup(sheet, "Brakes")) { r.Add("Wheels are not on the Brakes key.", "check"); tips++; }
            if (crew > 0 && !sheet.Flying)
            {
                bool abort = InGroup(sheet, "Abort");
                r.Add(abort ? "The Abort key is set up." : "Crew aboard and nothing on the Abort key.", abort ? "ok" : "not set");
                if (!abort) { r.Fix("Set Abort to shut the engines down and fire the decoupler under the pod (and an escape tower if there is one): one key gets the crew clear of a failing rocket."); tips++; }
            }
            if (sheet.Flying)
                foreach (var g in sheet.Parts.Where(p => p.Deployed == 0 && (p.Has("solar") || p.Has("antenna") || p.Has("radiator"))).GroupBy(p => p.Name))
                    r.Add(Count(g.Count(), g.First().Title) + ": still folded", "open it");
            if (r.Lines.Count == 0) r.Add("Nothing needs setting up: there is nothing here that folds, drills or runs by a key.");
            r.Rating = bad > 0 ? "problem" : tips > 0 ? tips + " suggestion" + (tips == 1 ? "" : "s") : "ok";
            return r;
        }

        // ---------------------------------------------------------------- mining and refuelling

        static readonly string[] Fuels = { "LiquidFuel", "Oxidizer", "MonoPropellant", "LqdHydrogen", "LqdMethane", "XenonGas", "ArgonGas", "Lithium" };

        /// <summary>Drills, converters and tanks: what it mines, what it makes, what that draws and how long a fill-up takes.</summary>
        public static CheckReport Mining(CraftSheet sheet, EngBody there, PowerReport power)
        {
            var r = new CheckReport { Key = "mining", Title = "Mining" };
            bool kerbalism = sheet.ModFacts.ContainsKey("Kerbalism");
            var drills = sheet.Parts.Where(p => p.Has("drill") || p.Modules.Any(m => m.Name == "Harvester" || m.Name == "HarvesterSystemHeat")).ToList();
            var plants = sheet.Parts.Where(p => p.Has("converter")).ToList();
            var custom = sheet.Parts.Where(p => p.Modules.Any(m => m.Name.IndexOf("Mining", StringComparison.OrdinalIgnoreCase) >= 0 && m.Kind == "other")).ToList();
            if (drills.Count == 0 && plants.Count == 0 && custom.Count == 0) { r.Rating = "no mining gear"; r.Add("No drills or converters on this craft."); return r; }
            bool bad = false;
            double ec = 0;
            var mined = new HashSet<string>();
            foreach (var g in drills.GroupBy(p => p.Name))
            {
                var p0 = g.First(); int n = g.Count();
                foreach (var m in p0.Modules.Where(m => m.Kind == "drill" || m.Name.StartsWith("Harvester", StringComparison.Ordinal)))
                {
                    string res = m.T("mines"); if (res == "") res = m.T("resource"); if (res == "") res = m.T("ResourceName");
                    double use = m.Kind == "drill" ? Math.Max(0, -m.Rate(CraftSheet.EC)) : m.Any("ec_rate");
                    double rate = m.Kind == "drill" ? m.Rates.Where(x => x.PerSec > 0).Sum(x => x.PerSec) : m.Any("rate");
                    if (res != "") mined.Add(res);
                    ec += use * n;
                    r.Add(Count(n, p0.Title) + ": mines " + (res != "" ? res : "a resource") + (rate > 0 ? ", up to " + N(rate * n * 3600, "0.#") + " an hour on rich ground" : ""), use > 0 ? CraftSheet.Ec(use * n) : "", false, p0.Mod);
                    break;
                }
            }
            var made = new Dictionary<string, double>();
            foreach (var g in plants.GroupBy(p => p.Name))
            {
                var p0 = g.First(); int n = g.Count();
                foreach (var m in p0.Of("converter"))
                {
                    double use = Math.Max(0, -m.Rate(CraftSheet.EC));
                    var outs = m.Rates.Where(x => x.PerSec > 0 && x.Res != CraftSheet.EC).ToList();
                    var ins = m.Rates.Where(x => x.PerSec < 0 && x.Res != CraftSheet.EC).ToList();
                    if (outs.Count == 0) continue;
                    r.Add(Count(n, p0.Title) + (m.Label != "" ? " (" + m.Label + ")" : "") + ": " + string.Join(" + ", ins.Select(x => x.Res).ToArray()) + " into " + string.Join(" + ", outs.Select(x => x.Res + " " + N(x.PerSec * n * 3600, "0.#") + "/h").ToArray()), use > 0 ? CraftSheet.Ec(use * n) : "", false, p0.Mod);
                    foreach (var o in outs) { double v; made.TryGetValue(o.Res, out v); made[o.Res] = Math.Max(v, o.PerSec * n); }
                    foreach (var i in ins) if (!mined.Contains(i.Res) && sheet.Max(i.Res) <= 0) { r.Add("Nothing on the craft mines or carries " + i.Res + " for " + p0.Title + ".", "", true); bad = true; }
                }
                ec = Math.Max(ec, ec);   // converters run one recipe at a time; the largest draw is added below
                ec += p0.Of("converter").Select(m => Math.Max(0, -m.Rate(CraftSheet.EC))).DefaultIfEmpty(0).Max() * n;
            }
            foreach (var g in custom.GroupBy(p => p.Name)) r.Add(Count(g.Count(), g.First().Title) + ": mining equipment from " + g.First().Mod + ", read by its listed values on the Parts tab.", "", false, g.First().Mod);
            foreach (string res in mined)
                if (sheet.Max(res) <= 0) { r.Add("Drills bring up " + res + " but no tank holds it.", "", true); bad = true; r.Fix("Add a tank for " + res + ": drills stop the moment there is nowhere to put it."); }
            // How long a fill-up takes for the tanks aboard.
            foreach (var kv in made.Where(k => Fuels.Contains(k.Key)))
            {
                double cap = sheet.Max(kv.Key);
                if (cap > 0 && kv.Value > 0) r.Add("Filling the " + N(cap) + " " + kv.Key + " aboard", "about " + Days(cap / kv.Value, sheet) + " at full rate");
            }
            if (ec > 0 && power != null)
            {
                double spare = power.Steady + (there != null ? power.SolarThere : power.SolarHome) - power.Always;
                bool shortEc = spare < ec;
                r.Add("Everything running draws " + CraftSheet.Ec(ec) + "; the craft has " + CraftSheet.Ec(Math.Max(0, spare)) + " to spare " + power.Where, shortEc ? "short of power" : "covered", shortEc);
                if (shortEc) { bad = true; r.Fix("Add " + CraftSheet.Ec(ec - Math.Max(0, spare)) + " of generation that works where it mines (fuel cells or a reactor work at night; panels do not), or run fewer drills at once."); }
            }
            bool hot = drills.Concat(plants).Any(p => p.Has("coreheat"));
            if (hot && sheet.Count("radiator") == 0) { r.Add("Drills and converters heat up and slow down without cooling, and there are no radiators.", "", true); bad = true; r.Fix("Add radiators: see the Heat tab for how much."); }
            r.Note = kerbalism
                ? "Kerbalism runs mining in the background at a rate set by how rich the ground is. Scan first: a poor site can take many times longer."
                : "Rates are for rich ground and a five-star engineer. Real ore concentration is usually 2 to 12%, and with no engineer aboard stock drills and converters run at a twentieth of this.";
            r.Rating = bad ? "problem" : "works";
            return r;
        }

        // ---------------------------------------------------------------- ascent and aerobraking

        /// <summary>The climb to orbit: thrust to weight stage by stage, when to turn, and which stage finishes the job.</summary>
        public static CheckReport Ascent(EngSnapshot s, List<CheckReport> checks)
        {
            var r = new CheckReport { Key = "ascent", Title = "Ascent" };
            var home = s.Home; var sheet = s.Sheet;
            if (home == null || s.Stages.Count == 0) { r.Rating = "no stages"; r.Add("No engine stages to climb with yet."); return r; }
            bool air = home.AtmoDepth > 0;
            double need = DvPlanner.Ascent(home), sum = 0;
            bool bad = false, warn = false;
            var order = s.Stages.Where(x => x.DvVac > 1).OrderByDescending(x => x.Stage).ToList();
            if (order.Count == 0) { r.Rating = "no stages"; r.Add("No stage has any delta-v."); return r; }
            var first = order[0];
            double twr = air && first.TwrAsl > 0 ? first.TwrAsl : first.TwrVac;
            string verdict = twr < 1 ? "will not lift off" : twr < 1.2 ? "sluggish: loses a lot to gravity" : twr <= 1.8 ? "good" : twr <= 2.5 ? "strong: throttle back low down" : "far too strong";
            r.Add("Lift-off thrust to weight " + N(twr, "0.00"), verdict, twr < 1);
            bad |= twr < 1; warn |= twr < 1.2 || twr > 2.5;
            if (twr > 2.5) r.Fix("With " + N(twr, "0.0") + " times its weight in thrust it fights the air and overheats low down. Lower the thrust limiter on the first stage, or fit fewer or smaller engines and save the mass.");
            if (twr >= 1 && twr < 1.2) r.Fix("Raise lift-off thrust to weight to about 1.3 to 1.5: more thrust, or less fuel in the first stage.");
            if (air)
            {
                double turnAt = twr >= 1.6 ? 50 : twr >= 1.3 ? 80 : 100;
                r.Add("Gravity turn: tip over 5 to 10° at about " + N(turnAt) + " m/s, then follow prograde; aim to be near 45° by " + EngMath.Dist(home.AtmoDepth * 0.15) + " and flat by " + EngMath.Dist(home.AtmoDepth * 0.6) + ".");
                if (sheet != null && sheet.Parts.Any(p => p.Of("engine").Any(m => m.N("locked") > 0 && p.Stage == first.Stage)) && twr > 2) r.Add("The first stage has solid boosters, which cannot be throttled: set their thrust limiter in the builder instead.");
            }
            foreach (var st in order)
            {
                if (sum >= need) break;
                double before = sum; sum += st.DvVac;
                if (st == first) continue;
                // A stage that lights before orbital speed has to hold the craft up while it works.
                bool early = before < need * 0.7;
                double t = st.TwrVac;
                bool weak = early ? t < 0.8 : t < 0.3;
                r.Add("Stage " + st.Stage + " takes over at about " + N(before) + " m/s of " + N(need) + " with thrust to weight " + N(t, "0.00"), weak ? (early ? "too weak this early" : "slow to circularise") : "ok", weak && early);
                if (weak) { warn = true; bad |= early && t < 0.5; r.Fix(early ? "Stage " + st.Stage + " lights well short of orbital speed and cannot hold the craft up at " + N(t, "0.00") + ". Give it more thrust, or give the stage below more fuel so it hands over later." : "Stage " + st.Stage + " is weak for circularising: start its burn early, before apoapsis."); }
            }
            bool reaches = sum >= need;
            r.Add("Orbit at " + home.Name + " takes about " + N(need) + " m/s; the craft has " + N(order.Sum(x => x.DvVac)) + " in all", reaches ? "reaches orbit" : "short", !reaches);
            bad |= !reaches;
            var bal = checks != null ? checks.FirstOrDefault(x => x.Key == "balance") : null;
            if (bal != null && bal.Bad && air) r.Add("The Balance check found a problem that matters on the way up.", bal.Rating, true);
            if (sheet != null && air && sheet.Count("fairing") == 0 && sheet.Parts.Any(p => p.DropStage < 0 && (p.Has("solar") && p.Deployed < 0 || p.Has("antenna") && p.Surface))) r.Add("No fairing: fixed panels and dishes on the payload take the full air flow. Fine for a sturdy craft, draggy for a wide one.");
            r.Rating = bad ? "problem" : warn ? "flyable, could be better" : "good";
            return r;
        }

        /// <summary>Using the air to slow down: what it saves, how hot it gets and where to aim.</summary>
        public static CheckReport Aerobrake(EngSnapshot s, EngPlan plan, EngMissions.Dest dest)
        {
            var r = new CheckReport { Key = "aerobrake", Title = "Aerobraking" };
            var home = s.Home; var sheet = s.Sheet;
            var places = new List<KeyValuePair<EngBody, double[]>>();   // body, {speed in, delta-v saved}
            if (plan != null && plan.Known)
            {
                var there = s.BodyNamed(plan.Body);
                if (there != null && there.AtmoDepth > 0 && dest != null && !plan.Flyby) places.Add(new KeyValuePair<EngBody, double[]>(there, new[] { DvPlanner.OrbitSpeed(there) + dest.Capture, dest.Capture }));
                if (plan.Return && home != null && home.AtmoDepth > 0 && dest != null) places.Add(new KeyValuePair<EngBody, double[]>(home, new[] { DvPlanner.OrbitSpeed(home) + dest.Transfer, dest.Transfer }));
            }
            else if (home != null && home.AtmoDepth > 0) places.Add(new KeyValuePair<EngBody, double[]>(home, new[] { DvPlanner.OrbitSpeed(home), 0 }));
            if (places.Count == 0) { r.Rating = "no air to use"; r.Add("Nowhere on this mission has an atmosphere to brake in."); return r; }
            var shields = sheet != null ? sheet.Parts.Where(p => p.Has("ablator")).ToList() : new List<CraftPart>();
            bool bad = false;
            foreach (var kv in places)
            {
                var b = kv.Key; double vIn = kv.Value[0], saved = kv.Value[1];
                bool thick = b.AtmoPressure >= 0.5;
                double lo = b.AtmoDepth * (thick ? 0.42 : 0.22), hi = b.AtmoDepth * (thick ? 0.52 : 0.32);
                bool hot = vIn > 3000, warm = vIn > 2400;
                r.Add("At " + b.Name + ": arrives at about " + N(vIn) + " m/s" + (saved > 0 ? "; braking in the air saves up to " + N(saved) + " m/s of engine burn" : ""), hot ? "hot" : warm ? "warm" : "gentle", hot && shields.Count == 0 && sheet != null);
                r.Add("   Aim the first pass at a periapsis of about " + EngMath.Dist(lo) + " to " + EngMath.Dist(hi) + " (air ends at " + EngMath.Dist(b.AtmoDepth) + "): start high, and lower it on later passes if it does not slow enough.");
                if (hot && shields.Count == 0 && sheet != null) { bad = true; r.Fix("At " + N(vIn) + " m/s into " + b.Name + " the craft needs a heat shield facing forward, or several high, gentle passes instead of one deep one."); }
            }
            if (sheet != null)
            {
                var sticking = sheet.Parts.Where(p => p.Deployed >= 0 && (p.Has("solar") || p.Has("antenna") || p.Has("radiator"))).GroupBy(p => p.Title).Select(g => Count(g.Count(), g.Key)).ToList();
                if (sticking.Count > 0) r.Add("Fold before each pass or the air tears them off: " + string.Join(", ", sticking.Take(6).ToArray()) + ".");
                var fragile = sheet.Parts.Where(p => p.MaxTemp > 0 && p.MaxTemp < 1300 && p.DropStage < 0 && p.Mass > 0.01).OrderBy(p => p.MaxTemp).FirstOrDefault();
                if (fragile != null) r.Add("The part that overheats first is " + fragile.Title + " (" + N(fragile.MaxTemp) + " K): keep it behind the shield or out of the airflow.");
            }
            r.Note = "Periapsis figures are starting points worked from the depth and thickness of the air, not a simulation: quicksave before the first pass.";
            r.Rating = bad ? "needs a shield" : "usable";
            return r;
        }

        // ---------------------------------------------------------------- planes and rovers

        static bool Jet(CraftModule m) { return m.T("props").IndexOf("IntakeAir", StringComparison.OrdinalIgnoreCase) >= 0 || m.T("props").IndexOf("IntakeAtm", StringComparison.OrdinalIgnoreCase) >= 0; }
        public static bool IsPlane(CraftSheet sheet, bool sph)
        {
            double lift = sheet.Parts.Where(p => !p.Has("command") && !p.Has("ablator")).Sum(p => p.Of("wing").Sum(m => m.N("lift")));
            return lift > 0.5 && (sph || sheet.All("engine").Any(Jet) || sheet.Count("intake") > 0);
        }
        public static bool IsRover(CraftSheet sheet) { return sheet.Parts.Count(p => p.Of("leg").Any(m => m.T("type").Contains("wheel"))) >= 3 && !sheet.All("engine").Any(Jet) && sheet.Parts.Sum(p => p.Of("wing").Sum(m => m.N("lift"))) < 0.5; }

        /// <summary>A craft that flies on wings: lift for its weight, where the lift sits, air for the engines, wheels.</summary>
        public static CheckReport Plane(CraftSheet sheet, EngBody home)
        {
            var r = new CheckReport { Key = "plane", Title = "Plane" };
            var wings = sheet.Parts.Where(p => p.HasPos && !p.Has("command") && !p.Has("ablator") && p.Of("wing").Any(m => m.N("lift") > 0)).ToList();
            double lift = wings.Sum(p => p.Of("wing").Sum(m => m.N("lift")));
            double mass = sheet.Parts.Sum(p => p.Mass), dry = sheet.Parts.Sum(p => p.DryMass);
            if (lift <= 0 || mass <= 0) { r.Rating = "no wings"; r.Add("No lifting surfaces on this craft."); return r; }
            bool bad = false, warn = false;
            double per = lift / mass;
            string v = per < 0.15 ? "very heavy for its wings" : per < 0.3 ? "fast take-off and landing" : per <= 0.8 ? "good" : "more wing than it needs";
            r.Add("Wing area " + N(lift, "0.0") + " for " + N(mass, "0.0") + " t: " + N(per, "0.00") + " of lift for each tonne (" + N(lift / dry, "0.00") + " with empty tanks)", v, per < 0.15);
            bad |= per < 0.15; warn |= per < 0.3;
            if (per < 0.3) r.Fix("Add wing: about " + N(mass * 0.4 - lift, "0.0") + " more lift rating brings it to 0.4 per tonne, which takes off and lands at a sensible speed.");
            if (per > 0.8) r.Fix("It carries more wing than it needs: about " + N(lift - mass * 0.6, "0.0") + " of lift rating could go, saving weight and drag.");
            // Where the lift sits along the fuselage against the centre of mass, full and empty.
            var fwd = CraftChecks.Axis(sheet);
            Func<bool, double> offset = empty =>
            {
                var com = CraftChecks.Centre(sheet.Parts.Where(p => p.HasPos), empty);
                return wings.Sum(p => CraftChecks.Dot(CraftChecks.Sub(CraftChecks.Pos(p), com), fwd) * p.Of("wing").Sum(m => m.N("lift"))) / lift;
            };
            if (sheet.Parts.All(p => p.HasPos))
            {
                double full = offset(false), empty = offset(true);
                Func<double, string> where = o => N(Math.Abs(o), "0.0#") + " m " + (o > 0 ? "ahead of" : "behind");
                bool flips = full > 0.05 || empty > 0.05, nose = full < -2.5;
                r.Add("Centre of lift is " + where(full) + " the centre of mass with full tanks, " + where(empty) + " it when empty", flips ? "unstable" : nose ? "nose heavy" : "stable", flips);
                if (flips) { bad = true; r.Fix("Move the main wing back or the heavy parts forward until the lift sits just behind the centre of mass, full and empty. Ahead of it, the nose pitches up and it flips."); }
                else if (nose) { warn = true; r.Fix("The lift is a long way behind the centre of mass: it will be hard to raise the nose. Move the wing forward or add control surfaces at the front."); }
                else if (Math.Abs(full - empty) > 1) r.Add("The balance shifts " + N(Math.Abs(full - empty), "0.0") + " m as fuel burns: put the tanks nearer the centre so it handles the same all flight.");
            }
            var control = sheet.Parts.Where(p => p.Of("wing").Any(m => m.N("control") > 0)).ToList();
            r.Add(control.Count + " control surface" + (control.Count == 1 ? "" : "s"), control.Count == 0 ? "cannot steer" : control.Count < 3 ? "few" : "ok", control.Count == 0);
            bad |= control.Count == 0;
            var jets = sheet.Parts.Where(p => p.Of("engine").Any(Jet)).ToList();
            int intakes = sheet.Count("intake");
            if (jets.Count > 0)
            {
                r.Add(jets.Count + " air-breathing engine" + (jets.Count == 1 ? "" : "s") + ", " + intakes + " intake" + (intakes == 1 ? "" : "s"), intakes == 0 ? "no air" : "ok", intakes == 0);
                if (intakes == 0) { bad = true; r.Fix("Add an air intake: jet engines flame out without one."); }
                double thrust = jets.Sum(p => p.Of("engine").Where(Jet).Sum(m => m.N("thrust")));
                double twr = thrust / (mass * G0);
                r.Add("Jet thrust to weight " + N(twr, "0.00"), twr < 0.25 ? "weak" : twr > 1.2 ? "far more than it needs" : "ok", twr < 0.2);
                bool rockets = sheet.All("engine").Any(m => !Jet(m) && m.T("props").Contains("Oxidizer"));
                double ox = sheet.Amount("Oxidizer");
                if (!rockets && ox > 0) { r.Fix("Jets burn liquid fuel only and nothing here burns oxidizer: empty the oxidizer in the builder.", "saves " + N(ox * 0.005, "0.0#") + " t"); warn = true; }
            }
            var gear = sheet.Parts.Where(p => p.Has("leg")).ToList();
            r.Add(gear.Count + " landing gear", gear.Count < 3 ? "needs three" : "ok", gear.Count < 3);
            bad |= gear.Count < 3;
            if (gear.Count >= 3 && gear.All(p => p.HasPos))
            {
                var com = CraftChecks.Centre(sheet.Parts.Where(p => p.HasPos), false);
                var behind = gear.Where(p => CraftChecks.Dot(CraftChecks.Sub(CraftChecks.Pos(p), com), fwd) < 0).ToList();
                if (behind.Count > 0)
                {
                    double back = behind.Average(p => -CraftChecks.Dot(CraftChecks.Sub(CraftChecks.Pos(p), com), fwd));
                    if (back > 2.5) { r.Add("The main wheels sit " + N(back, "0.0") + " m behind the centre of mass", "hard to lift the nose"); r.Fix("Move the rear wheels to just behind the centre of mass so it can rotate for take-off."); warn = true; }
                }
                else { r.Add("No wheels behind the centre of mass", "tail strike", true); bad = true; }
            }
            r.Note = "Lift figures are the game's wing ratings, so the bands are rules of thumb: 0.3 to 0.8 per tonne flies comfortably at " + (home != null ? home.Name : "home") + ".";
            r.Rating = bad ? "problem" : warn ? "flies, could be better" : "good";
            return r;
        }

        /// <summary>A craft that drives: wheels, power for them, and how easily it rolls over where it is going.</summary>
        public static CheckReport Rover(CraftSheet sheet, EngBody there, PowerReport power)
        {
            var r = new CheckReport { Key = "rover", Title = "Rover" };
            var wheels = sheet.Parts.Where(p => p.Of("leg").Any(m => m.T("type").Contains("wheel"))).ToList();
            if (wheels.Count == 0) { r.Rating = "no wheels"; r.Add("No wheels on this craft."); return r; }
            bool bad = false, warn = false;
            int powered = wheels.Count(p => p.Of("leg").Any(m => m.T("type") == "powered wheel"));
            r.Add(wheels.Count + " wheels, " + powered + " powered", wheels.Count < 3 ? "needs three" : powered == 0 ? "nothing drives it" : "ok", wheels.Count < 3 || powered == 0);
            bad |= wheels.Count < 3 || powered == 0;
            double motor = wheels.Sum(p => p.Modules.Where(m => m.Name.IndexOf("Motor", StringComparison.OrdinalIgnoreCase) >= 0).Sum(m => Math.Max(0, -m.Rate(CraftSheet.EC))));
            if (motor > 0 && power != null)
            {
                double make = power.Steady + (there != null ? power.SolarThere : power.SolarHome) - power.Always;
                bool shortEc = make < motor;
                double battery = sheet.Max(CraftSheet.EC);
                r.Add("Driving flat out draws " + CraftSheet.Ec(motor) + "; the craft has " + CraftSheet.Ec(Math.Max(0, make)) + " spare " + power.Where, shortEc ? "runs on battery: about " + EngMath.Dur(battery / Math.Max(1e-6, motor - Math.Max(0, make))) + " of driving" : "drives all day", false);
                warn |= shortEc;
                if (shortEc) r.Fix("Add " + CraftSheet.Ec(motor - Math.Max(0, make)) + " of generation to drive without stopping to recharge, or accept " + EngMath.Dur(battery / Math.Max(1e-6, motor - Math.Max(0, make))) + " of driving on a full battery.");
            }
            if (wheels.All(p => p.HasPos) && sheet.Parts.All(p => p.HasPos) && wheels.Count >= 3)
            {
                // Up is away from the wheels: the line from the middle of the wheels to the centre of mass.
                var com = CraftChecks.Centre(sheet.Parts, false);
                var mid = new[] { wheels.Average(p => p.X), wheels.Average(p => p.Y), wheels.Average(p => p.Z) };
                var up = CraftChecks.Sub(com, mid);
                double height = CraftChecks.Len(up);
                if (height > 0.01)
                {
                    up = new[] { up[0] / height, up[1] / height, up[2] / height };
                    // The narrow way across the wheels: the direction on the ground in which the outermost wheel is nearest.
                    var e1 = Math.Abs(up[0]) < 0.9 ? new[] { 1.0, 0, 0 } : new[] { 0, 1.0, 0 };
                    double d1 = CraftChecks.Dot(e1, up);
                    e1 = new[] { e1[0] - d1 * up[0], e1[1] - d1 * up[1], e1[2] - d1 * up[2] };
                    double l1 = CraftChecks.Len(e1); e1 = new[] { e1[0] / l1, e1[1] / l1, e1[2] / l1 };
                    var e2 = new[] { up[1] * e1[2] - up[2] * e1[1], up[2] * e1[0] - up[0] * e1[2], up[0] * e1[1] - up[1] * e1[0] };
                    double track = double.MaxValue;
                    for (int deg = 0; deg < 180; deg += 5)
                    {
                        double c = Math.Cos(deg * Math.PI / 180), sn = Math.Sin(deg * Math.PI / 180);
                        double reach = wheels.Max(p => { var o = CraftChecks.Sub(CraftChecks.Pos(p), mid); return Math.Abs(CraftChecks.Dot(o, e1) * c + CraftChecks.Dot(o, e2) * sn); });
                        track = Math.Min(track, reach);
                    }
                    if (track > 0.05)
                    {
                        double slope = Math.Atan(track / height) * 180 / Math.PI;
                        r.Add("Centre of mass " + N(height, "0.0#") + " m above the wheels, which stand " + N(track, "0.0#") + " m out: it rolls over past about " + N(slope) + "° of slope or a hard turn", slope < 30 ? "tippy" : slope < 45 ? "fair" : "stable", slope < 20);
                        if (slope < 30) { warn = true; bad |= slope < 20; r.Fix("Widen the wheel base or lower the heavy parts: under about 30° it rolls in turns."); }
                    }
                }
            }
            if (there != null && there.HasSurface)
            {
                double g = CraftChecks.Grav(there);
                if (g < 2) r.Add("Gravity on " + there.Name + " is " + N(g, "0.00") + " m/s²: wheels have little grip and it bounces and flips easily. Drive slowly, and a reaction wheel helps to right it.", sheet.Count("wheel") > 0 ? "has a reaction wheel" : "no reaction wheel");
            }
            if (!InGroup(sheet, "Brakes") && sheet.Parts.Any(p => p.Modules.Any(m => m.Num.ContainsKey("actions")))) r.Add("Wheels are not on the Brakes key.", "check");
            r.Rating = bad ? "problem" : warn ? "drives, could be better" : "good";
            return r;
        }

        // ---------------------------------------------------------------- docking and assembly

        /// <summary>Ports, thrusters and control for meeting another craft.</summary>
        public static CheckReport Docking(CraftSheet sheet, double massT)
        {
            var r = new CheckReport { Key = "docking", Title = "Docking" };
            var ports = sheet.Parts.Where(p => p.Has("dock")).ToList();
            if (ports.Count == 0) { r.Rating = "no docking port"; r.Add("No docking port on this craft."); return r; }
            bool bad = false, warn = false;
            foreach (var g in ports.GroupBy(p => p.Of("dock").First().T("size")))
                r.Add(Count(g.Count(), g.First().Title) + (g.Key != "" ? ", fits " + SizeName(g.Key) + " ports" : ""), "", false, g.First().Mod);
            r.Add("Ports only join ports of the same size: check what the craft it meets carries.");
            var rcs = sheet.Parts.Where(p => p.Has("rcs")).ToList();
            double mono = rcs.SelectMany(p => p.Of("rcs")).Select(m => m.T("props")).Where(x => x != "").SelectMany(x => x.Split(',')).Select(x => x.Trim()).Distinct().Sum(res => sheet.Amount(res) * (res == "MonoPropellant" ? 0.004 : 0.005));
            if (rcs.Count == 0)
            {
                r.Add("No RCS thrusters: it can turn but cannot slide sideways to line up.", "hard to dock", false);
                warn = true;
                r.Fix("Add four RCS thruster blocks around the centre of mass and some monopropellant: sideways nudges are what docking is made of.");
            }
            else
            {
                double m = massT > 0 ? massT : sheet.Parts.Sum(p => p.Mass);
                double dv = mono > 0 && m > mono ? 240 * G0 * Math.Log(m / (m - mono)) : 0;
                r.Add(rcs.Count + " RCS part" + (rcs.Count == 1 ? "" : "s") + ", thruster fuel for about " + N(dv) + " m/s", dv < 10 ? "too little" : dv > 150 ? "far more than docking needs" : "ok", dv < 5);
                if (dv < 10) { warn = true; bad |= dv < 5; r.Fix("Carry thruster fuel for 20 to 40 m/s: a docking usually takes 10 to 20."); }
                if (dv > 150) r.Fix("Docking takes 10 to 20 m/s of thruster fuel. Draining some in the builder saves weight.", "up to " + N(mono * (1 - 40 / dv), "0.0#") + " t");
                if (rcs.All(p => p.HasPos) && sheet.Parts.All(p => p.HasPos))
                {
                    var com = CraftChecks.Centre(sheet.Parts, false);
                    var axis = CraftChecks.Axis(sheet);
                    double mean = rcs.Average(p => CraftChecks.Dot(CraftChecks.Sub(CraftChecks.Pos(p), com), axis));
                    double spread = rcs.Max(p => CraftChecks.Dot(CraftChecks.Sub(CraftChecks.Pos(p), com), axis)) - rcs.Min(p => CraftChecks.Dot(CraftChecks.Sub(CraftChecks.Pos(p), com), axis));
                    bool off = Math.Abs(mean) > 0.4 && spread < Math.Abs(mean) * 2 + 0.2;
                    r.Add("Thrusters sit on average " + N(Math.Abs(mean), "0.0#") + " m " + (mean > 0 ? "ahead of" : "behind") + " the centre of mass", off ? "it will turn when it slides" : "balanced", false);
                    if (off) { warn = true; r.Fix("Move the thrusters level with the centre of mass, or add a second ring the same distance the other side, so a sideways push does not also spin it."); }
                }
            }
            bool control = sheet.Count("command") > 0;
            if (!control) { r.Add("No pod or probe core: nothing to fly it with.", "", true); bad = true; }
            if (sheet.Count("wheel") == 0 && rcs.Count == 0) { r.Add("No reaction wheel and no RCS: it cannot hold still to dock.", "", true); bad = true; }
            if (sheet.Count("light") == 0) r.Add("No lights: docking on the night side is done blind.", "optional");
            double limit = sheet.Fact("partLimit");
            if (limit > 0) r.Add("This is " + sheet.Parts.Count + " parts; the building allows " + N(limit) + " per launch, so a larger station goes up in pieces like this one.");
            r.Rating = bad ? "problem" : warn ? "can dock, could be easier" : "ready to dock";
            return r;
        }

        static string SizeName(string size)
        {
            switch (size) { case "size0": return "Jr. (0.625 m)"; case "size1": return "standard (1.25 m)"; case "size2": return "Sr. (2.5 m)"; default: return size; }
        }

        // ---------------------------------------------------------------- launcher rating

        /// <summary>Extra mass the craft could carry and still make the given delta-v, by bisection on the rocket equation.</summary>
        public static double Payload(IList<EngStage> stages, double needDv)
        {
            var list = stages.Where(x => x.DvVac > 1 && x.StartMass > x.EndMass && x.EndMass > 0).ToList();
            if (list.Count == 0) return 0;
            Func<double, double> dv = extra => list.Sum(x => x.DvVac / Math.Log(x.StartMass / x.EndMass) * Math.Log((x.StartMass + extra) / (x.EndMass + extra)));
            if (dv(0) < needDv) return 0;
            double lo = 0, hi = list.Max(x => x.StartMass) * 4;
            for (int i = 0; i < 50; i++) { double mid = (lo + hi) / 2; if (dv(mid) >= needDv) lo = mid; else hi = mid; }
            return lo;
        }

        /// <summary>What the rocket can put in low orbit, what that costs a tonne, and which rockets have done it before.</summary>
        public static CheckReport Launcher(EngSnapshot s, List<ProvenRocket> proven)
        {
            var r = new CheckReport { Key = "launcher", Title = "Launcher" };
            var home = s.Home; var sheet = s.Sheet;
            if (home == null || sheet == null || s.Stages.Count == 0) { r.Rating = "no stages"; r.Add("No engine stages to rate yet."); AddProven(r, proven, sheet); return r; }
            double need = DvPlanner.Ascent(home);
            var order = s.Stages.Where(x => x.DvVac > 1).OrderByDescending(x => x.Stage).ToList();
            // Launch legs lose some thrust low down, so the stages are counted the way the planner counts them.
            double have = 0;
            foreach (var st in order) have += st == order.First() && st.DvAsl > 0 ? (st.DvVac + st.DvAsl) / 2 : st.DvVac;
            double cost = sheet.Parts.Sum(p => p.Cost);
            if (have < need) { r.Rating = "does not reach orbit"; r.Add("Low orbit at " + home.Name + " takes about " + N(need) + " m/s and the craft has " + N(have) + " for the climb.", "short by " + N(need - have), true); AddProven(r, proven, sheet); return r; }
            // The stages that are spent getting to orbit are the launcher; whatever is left above them is what it carries.
            double sum = 0; EngStage last = null;
            foreach (var st in order) { sum += st == order.First() && st.DvAsl > 0 ? (st.DvVac + st.DvAsl) / 2 : st.DvVac; last = st; if (sum >= need) break; }
            double spare = sum - need;
            double inOrbit = last.EndMass + (last.StartMass - last.EndMass) * (last.DvVac > 0 ? Math.Min(1, spare / last.DvVac) : 0);
            double extra = Payload(order.TakeWhile(x => x != last).Concat(new[] { last }).ToList(), need * (order.First().DvAsl > 0 ? 1.03 : 1));
            r.Add("Reaches low " + home.Name + " orbit in stage " + last.Stage + " with about " + N(inOrbit, "0.0#") + " t arriving", N(spare) + " m/s of that stage left");
            r.Add("It could carry about " + N(extra, "0.0#") + " t more to low orbit as it stands", extra < 0.05 ? "no spare lift" : "");
            if (cost > 0 && inOrbit > 0) r.Add("Cost for each tonne in orbit", N(cost / inOrbit) + " funds");
            double launchMass = order.First().StartMass;
            if (launchMass > 0) r.Add("Share of lift-off mass that reaches orbit", N(inOrbit / launchMass * 100, "0.#") + "%");
            double left = order.Sum(x => x.DvVac) - (sum - spare) ;
            r.Add("Delta-v left once in orbit", N(Math.Max(0, order.SkipWhile(x => x != last).Skip(1).Sum(x => x.DvVac) + spare)) + " m/s");
            if (inOrbit / Math.Max(1e-6, launchMass) > 0.2 && extra > inOrbit) r.Fix("This launcher is far bigger than its load: it could lift about " + N(extra, "0.0") + " t more. A smaller first stage would do the same job for less.");
            AddProven(r, proven, sheet);
            r.Rating = "lifts " + N(inOrbit, "0.0") + " t to low orbit";
            return r;
        }

        static void AddProven(CheckReport r, List<ProvenRocket> proven, CraftSheet sheet)
        {
            if (proven == null || proven.Count == 0) { r.Note = "No rocket of yours has reached orbit yet in this save. Once one does it is listed here as proven."; return; }
            foreach (var p in proven.OrderByDescending(x => x.UT).Take(8))
                r.Add("Proven: " + p.Name + ", " + N(p.LaunchMass, "0.0") + " t at lift-off, put " + N(p.OrbitMass, "0.0") + " t in orbit" + (p.Cost > 0 ? ", " + N(p.Cost) + " funds (" + N(p.Cost / Math.Max(0.01, p.OrbitMass)) + " a tonne)" : ""), p.Flights + " flight" + (p.Flights == 1 ? "" : "s"));
        }

        // ---------------------------------------------------------------- tech tree advice

        /// <summary>Which research would help this craft most: locked parts tried in place of the ones fitted, node by node.</summary>
        public static CheckReport Tech(CraftSheet sheet, List<CraftPart> locked, IList<EngStage> stages, bool pad, double science)
        {
            var r = new CheckReport { Key = "tech", Title = "Tech advice" };
            if (locked == null) { r.Rating = "not read"; r.Add("The tech tree could not be read."); return r; }
            if (locked.Count == 0) { r.Rating = "everything unlocked"; r.Add("Every part is already unlocked (a sandbox game, or the tree is complete), so there is nothing to research. Part swaps covers what is available."); return r; }
            var found = new List<KeyValuePair<double, string[]>>();
            foreach (var node in locked.Where(p => p.Tech != "").GroupBy(p => p.Tech))
            {
                var swaps = EngTools.Swaps(sheet, node.ToList(), stages, pad);
                foreach (var f in swaps.Fixes.Take(2))
                {
                    double gain = 0;
                    var m = System.Text.RegularExpressions.Regex.Match(f.Value, @"\+(\d+) m/s");
                    if (m.Success) gain = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    double costSci = node.First().TechCost;
                    string title = node.First().TechTitle != "" ? node.First().TechTitle : node.Key;
                    found.Add(new KeyValuePair<double, string[]>(gain > 0 ? gain / Math.Max(1, costSci) : 0.0001 / Math.Max(1, costSci), new[] { title + (costSci > 0 ? " (" + N(costSci) + " science" + (science >= 0 ? science >= costSci ? ", affordable now" : ", " + N(costSci - science) + " short" : "") + ")" : "") + ": " + f.Text, f.Value }));
                }
            }
            if (found.Count == 0) { r.Rating = "nothing locked would help"; r.Add("None of the " + locked.Count + " parts still locked would do a better job than what is fitted."); return r; }
            foreach (var x in found.OrderByDescending(x => x.Key).Take(8)) r.Add(x.Value[0], x.Value[1]);
            r.Note = "Ranked by gain for each point of science. " + locked.Count + " parts are still locked" + (science >= 0 ? "; you have " + N(science) + " science." : ".");
            r.Rating = found.Count + " upgrade" + (found.Count == 1 ? "" : "s") + " to research";
            return r;
        }

        // ---------------------------------------------------------------- relay network

        /// <summary>Whether the relays already flying carry this craft's signal home from where it is going.</summary>
        public static CheckReport Relays(EngSnapshot s, EngPlan plan, List<EngRelay> relays)
        {
            var r = new CheckReport { Key = "relays", Title = "Relays" };
            var sheet = s.Sheet; var home = s.Home;
            if (sheet != null && sheet.ModFacts.ContainsKey("RemoteTech")) { r.Rating = "not computed"; r.Add("RemoteTech replaces the stock network, so relay ranges are not summed here."); return r; }
            if (sheet != null && sheet.HasFact("commnet") && sheet.Fact("commnet") == 0) { r.Rating = "not needed"; r.Add("CommNet is switched off in this save, so no relays are needed."); return r; }
            relays = relays ?? new List<EngRelay>();
            double dsn = sheet != null && sheet.Fact("dsnPower") > 0 ? sheet.Fact("dsnPower") * Math.Max(1, sheet.Fact("dsnMod", 1)) : 250e9;
            double mult = sheet != null && sheet.Fact("rangeMod") > 0 ? sheet.Fact("rangeMod") : 1;
            double craft = sheet != null ? CraftChecks.Combine(sheet.All("antenna")) : 0;
            if (relays.Count == 0) r.Add("No relay craft are flying yet.");
            EngBody there = plan != null && plan.Known ? s.BodyNamed(plan.Body) : null;
            if (there == null || home == null)
            {
                foreach (var x in relays.Take(10)) r.Add(x.Name + " at " + x.Body, "relay power " + Power(x.Power));
                r.Rating = relays.Count + " relay" + (relays.Count == 1 ? "" : "s") + " flying";
                r.Note = "Pick a destination in the mission row to see whether they cover it.";
                return r;
            }
            double min, max; CraftChecks.Distances(home, there, s.Bodies, out min, out max);
            double direct = Math.Sqrt(craft * dsn) * mult;
            bool directOk = craft > 0 && direct >= max;
            r.Add("Direct to the tracking station from " + there.Name + " (up to " + EngMath.Dist(max) + ")", craft <= 0 ? "no antenna" : directOk ? "reaches, signal " + N(CraftChecks.Signal(max, direct) * 100) + "% at the farthest" : "out of range");
            // Relays in the destination's system: the craft only has to reach them, and they have to reach home.
            var system = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { there.Name };
            foreach (var b in s.Bodies) if (string.Equals(b.Parent, there.Name, StringComparison.OrdinalIgnoreCase)) system.Add(b.Name);
            var parentBody = DvPlanner.Find(s.Bodies, there.Parent);
            if (parentBody != null && !parentBody.IsStar) { system.Add(parentBody.Name); foreach (var b in s.Bodies) if (string.Equals(b.Parent, parentBody.Name, StringComparison.OrdinalIgnoreCase)) system.Add(b.Name); }
            bool covered = false;
            double local = there.Soi > 0 ? there.Soi : there.Radius * 20;
            if (parentBody != null && !parentBody.IsStar && parentBody.Soi > 0) local = Math.Max(local, parentBody.Soi);
            foreach (var x in relays.Where(x => system.Contains(x.Body)).OrderByDescending(x => x.Power).Take(6))
            {
                double up = Math.Sqrt(x.Power * dsn) * mult, down = Math.Sqrt(craft * x.Power) * mult;
                bool home1 = up >= max, craft1 = craft > 0 && down >= Math.Min(local, Math.Max(x.Altitude * 2 + there.Radius * 2, there.Radius * 4));
                r.Add(x.Name + " at " + x.Body + " (relay power " + Power(x.Power) + ")", !home1 ? "cannot reach home from there" : !craft1 ? "this craft cannot reach it" : "carries the signal home", false);
                covered |= home1 && craft1;
            }
            int elsewhere = relays.Count(x => !system.Contains(x.Body));
            if (elsewhere > 0) r.Add(elsewhere + " other relay" + (elsewhere == 1 ? "" : "s") + " elsewhere: they help only while they happen to sit between the craft and home.");
            if (!directOk && !covered)
            {
                double needRelay = Math.Pow(max / mult, 2) / dsn;
                r.Fix("Put a relay in orbit of " + there.Name + " with relay antenna power of at least " + Power(needRelay) + " (so it reaches home at the farthest), or fit this craft with at least " + Power(needRelay) + " of its own.");
            }
            if (covered || directOk) r.Add("A relay in a high orbit also covers the far side of " + there.Name + ", where a direct link is blocked by the planet itself.");
            r.Rating = covered ? "covered by relay" : directOk ? "direct link is enough" : "no link from " + there.Name;
            if (!covered && !directOk) r.Lines.Last().Bad = craft > 0 || relays.Count > 0;
            return r;
        }

        // ---------------------------------------------------------------- predicted against actual

        public static CheckReport Predicted(List<EngPrediction> list, string craft)
        {
            var r = new CheckReport { Key = "predicted", Title = "Predicted vs actual" };
            if (list == null || list.Count == 0) { r.Rating = "no flights yet"; r.Add("Each launch, the team notes what it expects (delta-v to orbit, peak g, touchdown speed) and fills in what happened. Nothing has flown since this was added."); return r; }
            var errs = new List<double>();
            foreach (var p in list.AsEnumerable().Reverse().Take(6))
            {
                r.Add(p.Craft + (p.Craft == craft ? " (this craft)" : ""), "flight");
                foreach (var it in p.Items)
                {
                    double pred = EngPrediction.Num(it[2]);
                    if (it[3] == "") { r.Add("   " + it[0] + ": expected " + N(pred, pred < 20 ? "0.0" : "0") + " " + it[1], "not reached yet"); continue; }
                    double act = EngPrediction.Num(it[3]);
                    double off = pred != 0 ? (act - pred) / Math.Abs(pred) * 100 : 0;
                    r.Add("   " + it[0] + ": expected " + N(pred, pred < 20 ? "0.0" : "0") + ", was " + N(act, act < 20 ? "0.0" : "0") + " " + it[1], (off >= 0 ? "+" : "−") + N(Math.Abs(off)) + "%", Math.Abs(off) > 35);
                    if (it[0].StartsWith("Delta-v to orbit")) errs.Add(off);
                }
            }
            if (errs.Count > 0)
            {
                double avg = errs.Average();
                if (Math.Abs(avg) > 7) r.Fix("Your climbs to orbit have cost about " + N(Math.Abs(avg)) + "% " + (avg > 0 ? "more" : "less") + " than the planner's figure. " + (avg > 0 ? "Carry that much extra in the launch stages, or see the Ascent tab for a cleaner climb." : "You can trim the launch margin by that much."));
            }
            r.Rating = list.Count + " flight" + (list.Count == 1 ? "" : "s") + " on record";
            return r;
        }
    }
}
