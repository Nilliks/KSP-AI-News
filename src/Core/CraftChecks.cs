using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KerbalNews
{
    /// <summary>What a check needs to know besides the craft: where it is going, who is aboard, and the save's bodies.</summary>
    public class CheckContext
    {
        public CraftSheet Sheet;
        public EngBody Home, There;
        public IList<EngBody> Bodies = new List<EngBody>();
        public EngPlan Plan;
        public EngMissions.Dest Dest;
        public IList<EngStage> Stages = new List<EngStage>();
        /// <summary>Crew aboard in flight; in the builder every seat counts as filled.</summary>
        public int Crew;
        public bool Editor, Sph;
        public bool HasMission { get { return Plan != null && Plan.Known; } }
        public bool Away { get { return HasMission && There != null && Home != null && There.Name != Home.Name; } }
    }

    /// <summary>
    /// The computed checks beyond power, heat and staging: comms, landing, balance, launch and structure, crew, cost.
    /// Each is plain arithmetic on the craft sheet, so none of them costs an AI call, and each judges both ways:
    /// not enough, and more than the job needs.
    /// </summary>
    public static class CraftChecks
    {
        static string N(double v, string f = "0") { return v.ToString(f, CultureInfo.InvariantCulture); }
        static string Count(IEnumerable<CraftPart> g) { var l = g.ToList(); return (l.Count > 1 ? l.Count + " × " : "") + l[0].Title; }
        static string Big(double v) { return v >= 1e12 ? N(v / 1e12, "0.#") + "T" : v >= 1e9 ? N(v / 1e9, "0.#") + "G" : v >= 1e6 ? N(v / 1e6, "0.#") + "M" : v >= 1e3 ? N(v / 1e3, "0.#") + "k" : N(v); }
        const double G0 = 9.80665;

        public static List<CheckReport> All(CheckContext c)
        {
            var list = new List<CheckReport>();
            if (c == null || c.Sheet == null || c.Sheet.Parts.Count == 0) return list;
            Func<string, string, Func<CheckReport>, CheckReport> run = (key, title, f) =>
            {
                try { var r = f(); r.Key = key; r.Title = title; return r; }
                catch (Exception e) { var r = new CheckReport { Key = key, Title = title, Rating = "not computed" }; r.Add("This check could not be worked out (" + e.GetType().Name + ")."); return r; }
            };
            list.Add(run("heat", "Heat", () => ModKnowledge.Heat(c.Sheet)));
            list.Add(run("comms", "Comms", () => Comms(c)));
            list.Add(run("landing", "Landing", () => Landing(c)));
            list.Add(run("balance", "Balance", () => Balance(c)));
            list.Add(run("launch", "Launch", () => Launch(c)));
            list.Add(run("crew", "Crew", () => Crew(c)));
            list.Add(run("cost", "Cost", () => Cost(c)));
            return list;
        }

        // ---------------------------------------------------------------- comms

        /// <summary>Combined antenna power the way CommNet adds it: the strongest, raised by the combinable ones.</summary>
        public static double Combine(IEnumerable<CraftModule> antennas)
        {
            var list = antennas.Where(m => m.N("power") > 0).ToList();
            if (list.Count == 0) return 0;
            double best = list.Max(m => m.N("power"));
            var comb = list.Where(m => m.N("combinable") > 0).ToList();
            if (comb.Count < 2) return best;
            double top = comb.Max(m => m.N("power")), sum = comb.Sum(m => m.N("power"));
            double exp = comb.Sum(m => m.N("power") * m.N("exponent", 0.75)) / sum;
            return Math.Max(best, top * Math.Pow(sum / top, exp));
        }

        /// <summary>Signal strength (0 to 1) at a distance, for a link with the given range.</summary>
        public static double Signal(double distance, double range)
        {
            if (range <= 0 || distance >= range) return 0;
            double x = 1 - distance / range;
            return (3 - 2 * x) * x * x;
        }

        static EngBody PlanetOf(EngBody b, IList<EngBody> bodies)
        {
            var at = b; int guard = 0;
            while (at != null && guard++ < 6)
            {
                var parent = DvPlanner.Find(bodies, at.Parent);
                if (parent == null || parent.IsStar) return at;
                at = parent;
            }
            return b;
        }

        /// <summary>Closest and farthest a craft at <paramref name="there"/> gets from home.</summary>
        public static void Distances(EngBody home, EngBody there, IList<EngBody> bodies, out double min, out double max)
        {
            min = max = 0;
            if (home == null || there == null || there.Name == home.Name) return;
            var hp = PlanetOf(home, bodies); var tp = PlanetOf(there, bodies);
            if (hp.Name == tp.Name)
            {
                // Same planet system: a moon of home, or two moons of one planet.
                double a = home.Name == hp.Name ? 0 : home.Sma, b = there.Name == tp.Name ? 0 : there.Sma;
                min = Math.Abs(a - b); max = a + b;
                return;
            }
            double extra = (there.Name == tp.Name ? 0 : there.Sma) + (home.Name == hp.Name ? 0 : home.Sma);
            min = Math.Max(0, Math.Abs(hp.Sma - tp.Sma) - extra); max = hp.Sma + tp.Sma + extra;
        }

        public static CheckReport Comms(CheckContext c)
        {
            var r = new CheckReport();
            var sheet = c.Sheet;
            if (sheet.ModFacts.ContainsKey("RemoteTech")) { r.Rating = "not computed"; r.Add("RemoteTech replaces the stock network (pointed dishes, line of sight, signal delay), so these range sums do not apply. Check its own range readouts."); return r; }
            if (sheet.HasFact("commnet") && sheet.Fact("commnet") == 0) { r.Rating = "not needed"; r.Add("CommNet is switched off in this save: probes are always in contact and antennas only matter for sending science."); return r; }
            var parts = sheet.Parts.Where(p => p.Of("antenna").Any(m => m.N("power") > 0)).ToList();
            bool probe = sheet.Parts.Where(p => p.Has("command")).All(p => p.Of("command").All(m => m.N("crewMin") <= 0));
            bool needLink = sheet.Fact("signalForControl") > 0;
            if (parts.Count == 0)
            {
                r.Rating = "none";
                r.Add("No antenna at all: no link home and no way to send science." + (probe && needLink ? " This save needs a link to fly a probe, so it would be dead on the pad." : ""), "", probe);
                return r;
            }
            var all = parts.SelectMany(p => p.Of("antenna")).ToList();
            double power = Combine(all), relay = Combine(all.Where(m => m.T("type") == "relay"));
            bool assumed = sheet.Fact("dsnPower") <= 0;
            double dsn = (assumed ? 2.5e11 : sheet.Fact("dsnPower")) * sheet.Fact("dsnMod", 1);
            double mod = sheet.Fact("rangeMod", 1);
            double range = Math.Sqrt(power * dsn) * mod;
            foreach (var g in parts.GroupBy(p => p.Name).OrderByDescending(g => g.First().Of("antenna").Max(m => m.N("power"))).Take(4))
            {
                var m = g.First().Of("antenna").OrderByDescending(x => x.N("power")).First();
                r.Add(Count(g) + (m.T("type") != "" ? ", " + m.T("type") : "") + (m.N("combinable") > 0 ? "" : ", does not combine"), Big(m.N("power")), false, g.First().Mod);
            }
            if (all.Count > 1) r.Add("Combined antenna power", Big(power));
            r.Add("Reach of the link to the tracking station (" + (assumed ? "assuming it is fully upgraded" : "level " + N(sheet.Fact("trackingLevel"))) + ")", EngMath.Dist(range));

            string rating = "good";
            if (c.Away)
            {
                double min, max;
                Distances(c.Home, c.There, c.Bodies, out min, out max);
                double near = Signal(min, range), far = Signal(max, range);
                if (max - min < max * 0.05) r.Add("At " + c.There.Name + ", " + EngMath.Dist(max) + " from home", far > 0 ? "signal " + N(far * 100) + "%" : "no link", far <= 0);
                else
                {
                    r.Add("At " + c.There.Name + " when it is closest (" + EngMath.Dist(min) + ")", near > 0 ? "signal " + N(near * 100) + "%" : "no link", near <= 0);
                    r.Add("At " + c.There.Name + " when it is farthest (" + EngMath.Dist(max) + ")", far > 0 ? "signal " + N(far * 100) + "%" : "no link", far <= 0);
                }
                rating = near <= 0 ? "no link" : far <= 0 ? "patchy" : far < 0.15 ? "weak" : "good";
                if (far < 0.15)
                {
                    // Power for a 15% signal at the farthest point.
                    double needRange = max / 0.75, needPower = needRange * needRange / dsn / (mod * mod);
                    r.Fix("For a steady link at " + c.There.Name + " the craft needs antenna power of about " + Big(needPower) + " (it has " + Big(power) + ")" + (assumed ? "" : sheet.Fact("trackingLevel") < 3 ? ", or a tracking station upgrade" : "") + ", or a relay closer to " + c.There.Name + ".");
                }
                else
                {
                    var strongest = all.OrderByDescending(m => m.N("power")).First();
                    double alone = Signal(max, Math.Sqrt(strongest.N("power") * dsn) * mod);
                    int extra = parts.Count(p => p.Of("antenna").All(m => m != strongest) && !p.Has("command"));
                    if (alone >= 0.5 && extra > 0) r.Add("The strongest antenna alone holds " + N(alone * 100) + "% at " + c.There.Name + ". The other " + extra + " add little range; they are spares.");
                }
            }
            else if (c.Home != null)
            {
                // No destination: say how far out the link holds.
                string always = "", sometimes = "";
                foreach (var b in c.Bodies.Where(x => !x.IsStar && x.Name != c.Home.Name))
                {
                    double min, max;
                    Distances(c.Home, b, c.Bodies, out min, out max);
                    if (max <= 0) continue;
                    if (Signal(max, range) > 0.05) { if (always == "" || max > Far(c, always)) always = b.Name; }
                    else if (Signal(min, range) > 0.05) { if (sometimes == "" || max > Far(c, sometimes)) sometimes = b.Name; }
                }
                r.Add(always != "" ? "Holds a link all the way out to " + always + (sometimes != "" ? ", and reaches " + sometimes + " only when it is on the near side" : "") + "." : "The link does not hold as far as any other body.", "", always == "" && probe && needLink);
                r.Note = "Pick a destination in the mission row to see the signal there.";
                rating = always == "" ? "short range" : "good";
            }
            if (relay > 0) r.Add("As a relay for other craft, back to the tracking station", EngMath.Dist(Math.Sqrt(relay * dsn) * mod));
            if (probe && needLink) r.Add("This save needs a link to fly a probe: with no signal it cannot be steered or throttled.");
            if (ModKnowledge.FeatureOn(sheet, "Kerbalism.Science")) r.Add("Kerbalism: the data rate falls with distance, so far from home science trickles in even with a link.");
            r.Rating = rating;
            return r;
        }

        static double Far(CheckContext c, string name) { double min, max; Distances(c.Home, DvPlanner.Find(c.Bodies, name), c.Bodies, out min, out max); return max; }

        // ---------------------------------------------------------------- landing

        /// <summary>Share of a parachute's drag-cube area that turns into drag (the game's own drag multipliers).</summary>
        public const double ChuteK = 0.8;

        internal static double Density(EngBody b) { return b == null ? 0 : b.AtmoDensity > 0 ? b.AtmoDensity : b.AtmoPressure * 1.225; }
        internal static double Grav(EngBody b) { return b.Gravity > 0 ? b.Gravity : b.Mu / (b.Radius * b.Radius); }

        /// <summary>Speed the craft settles at under its parachutes at the surface, m/s.</summary>
        public static double ChuteSpeed(double tonnes, double area, EngBody b)
        {
            double rho = Density(b);
            if (rho <= 0 || area <= 0 || tonnes <= 0) return 0;
            return Math.Sqrt(2 * tonnes * 1000 * Grav(b) / (rho * area * ChuteK));
        }

        public static CheckReport Landing(CheckContext c)
        {
            var r = new CheckReport();
            var sheet = c.Sheet;
            var stay = sheet.Parts.Where(p => p.DropStage < 0).ToList();
            if (stay.Count == 0) stay = sheet.Parts;
            double dry = stay.Sum(p => p.DryMass), wet = stay.Sum(p => p.Mass);
            var chutes = stay.Where(p => p.Has("chute")).ToList();
            double area = chutes.Sum(p => p.ChuteArea);
            bool realChute = sheet.HasModule("RealChuteModule");
            var legs = stay.Where(p => p.Has("leg")).ToList();
            var shields = stay.Where(p => p.Has("ablator")).ToList();
            bool crewed = c.Crew > 0;

            var targets = new List<EngBody>();
            bool planned = c.HasMission;
            if (planned && c.Plan.Land && c.There != null) targets.Add(c.There);
            if (c.Home != null && (!planned || c.Plan.Return) && !targets.Contains(c.Home)) targets.Add(c.Home);
            if (planned && targets.Count == 0)
            {
                r.Rating = "no landing planned";
                r.Add("The mission as set does not land anywhere" + (chutes.Count + legs.Count > 0 ? ", yet the craft carries " + (chutes.Count > 0 ? chutes.Count + " parachute" + (chutes.Count == 1 ? "" : "s") : "") + (chutes.Count > 0 && legs.Count > 0 ? " and " : "") + (legs.Count > 0 ? legs.Count + " landing leg" + (legs.Count == 1 ? "" : "s") : "") + "." : "."));
                if (chutes.Count + legs.Count > 0) r.Fix("If it really never lands, the landing gear is dead weight.", "saves " + N(chutes.Concat(legs).Sum(p => p.Mass), "0.##") + " t");
                return r;
            }
            r.Add("What lands is the last stage: " + stay.Count + " parts, " + N(dry, "0.0#") + " t empty, " + N(wet, "0.0#") + " t with everything full.");
            var weak = stay.Where(p => p.CrashTol > 0 && p.Mass > 0.02 && !p.Has("chute")).OrderBy(p => p.CrashTol).FirstOrDefault();
            bool bad = false, marginal = false, over = false; int shielded = 0, warmEntries = 0; double spare = 0;
            foreach (var b in targets)
            {
                bool home = c.Home != null && b.Name == c.Home.Name;
                double rho = Density(b);
                if (!b.HasSurface) { r.Add(b.Name + " has no surface to land on.", "", true); bad = true; continue; }
                if (rho > 0)
                {
                    // Coming in from orbit or beyond: how fast, and is there a shield.
                    double vOrb = DvPlanner.OrbitSpeed(b);
                    double vEntry = home && planned && c.Dest != null ? vOrb + c.Dest.Transfer : vOrb;
                    if (!home && c.Dest != null) vEntry = vOrb + c.Dest.Capture;
                    double ablator = shields.Sum(p => p.Res.Where(x => x.Name == "Ablator" || x.Name == "AblativeShielding").Sum(x => x.Amount));
                    bool hot = vEntry > 3000, warm = vEntry > 2400;
                    bool noShield = shields.Count == 0;
                    r.Add("Entry at " + b.Name + " at about " + N(vEntry) + " m/s" + (noShield ? ", no heat shield" : ", heat shield" + (ablator > 0 ? " with " + N(ablator) + " ablator" : "")),
                        hot ? "hot" : warm ? "warm" : "gentle", noShield && hot && crewed);
                    if (noShield && hot) { bad |= crewed; r.Fix("Add a heat shield under the part that comes down at " + b.Name + ": " + N(vEntry) + " m/s is too fast to trust bare parts."); }
                    else if (noShield && warm) r.Add("No heat shield: at this speed a pod usually survives a shallow entry, but a shield is cheap insurance.");
                    if (!noShield) { shielded++; if (warm) warmEntries++; spare = ablator; }

                    if (realChute && area <= 0) { r.Add("Parachutes on this craft are RealChute: their landing speed is set in the part's own window and is not summed here."); continue; }
                    if (area <= 0)
                    {
                        bool powered = c.Stages.Any(s => s.DvVac > 50);
                        r.Add("No parachutes for " + b.Name + (powered ? ": landing would be on engine power alone" : "") + ".", "", crewed && home);
                        bad |= crewed && home;
                        continue;
                    }
                    double vDry = ChuteSpeed(dry, area, b), vWet = ChuteSpeed(wet, area, b);
                    double tol = weak != null ? weak.CrashTol : 8;
                    double minP = chutes.SelectMany(p => p.Of("chute")).Max(m => m.N("minPressure", 0.04));
                    bool slow = vWet <= Math.Min(tol * 0.85, 8), ok = vDry <= tol * 0.9;
                    r.Add(chutes.Count + " parachute" + (chutes.Count == 1 ? "" : "s") + " at " + b.Name + ": touchdown at about " + N(vDry, "0.0") + " m/s empty, " + N(vWet, "0.0") + " m/s full"
                        + (weak != null ? " (the " + weak.Title + " survives " + N(weak.CrashTol) + " m/s)" : ""), slow ? "safe" : ok ? "safe if tanks are empty" : "too fast", !ok);
                    if (b.AtmoPressure > 0 && b.AtmoPressure < minP * 1.5) r.Add("The air at " + b.Name + " is thin (" + N(b.AtmoPressure, "0.###") + " atm): parachutes open late or not at all. Plan a braking burn.", "", b.AtmoPressure < minP);
                    if (!ok)
                    {
                        bad = true;
                        var one = chutes.GroupBy(p => p.Name).OrderByDescending(g => g.Count()).First().First();
                        double target = Math.Min(6.5, tol * 0.8), needArea = 2 * dry * 1000 * Grav(b) / (Density(b) * ChuteK * target * target);
                        if (one.ChuteArea > 0) r.Fix("Add " + (int)Math.Ceiling((needArea - area) / one.ChuteArea) + " more " + one.Title + " to come down at about " + N(target, "0.0") + " m/s at " + b.Name + (!home && Density(b) < 0.3 ? ", or brake with the engine for the last " + N(vDry) + " m/s" : "") + ".");
                    }
                    else if (!slow) marginal = true;
                    else if (chutes.Count >= 2 && vWet < 4)
                    {
                        // More canopy than the job needs: how many could go and still land at 6 m/s full.
                        var sorted = chutes.OrderBy(p => p.ChuteArea).ToList();
                        double left = area, mass = 0, cost = 0; int k = 0;
                        foreach (var p in sorted)
                        {
                            if (k >= sorted.Count - 1 || ChuteSpeed(wet, left - p.ChuteArea, b) > Math.Min(6, tol * 0.7)) break;
                            left -= p.ChuteArea; mass += p.Mass; cost += p.Cost; k++;
                        }
                        if (k > 0) { over = true; r.Fix(k + " parachute" + (k == 1 ? "" : "s") + " could go and it would still touch down at " + N(ChuteSpeed(wet, left, b), "0.0") + " m/s full at " + b.Name + ".", "saves " + N(mass, "0.##") + " t" + (cost > 0 ? ", " + N(cost) + " funds" : "")); }
                    }
                }
                else
                {
                    // No air: engines do all of it.
                    double g = Grav(b);
                    var last = c.Stages.Where(s => s.DvVac > 30 && s.TwrVac > 0).OrderBy(s => s.Stage).FirstOrDefault();
                    EngStage ls = last;
                    if (planned && c.Plan != null)
                    {
                        var leg = c.Plan.Legs.FirstOrDefault(l => l.Name.StartsWith("Land"));
                        int stg;
                        if (leg != null && leg.Stages.StartsWith("stage ") && int.TryParse(leg.Stages.Substring(6).Split(',')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out stg))
                            ls = c.Stages.FirstOrDefault(s => s.Stage == stg) ?? last;
                    }
                    if (ls == null) { r.Add(b.Name + " has no air, so landing is on engines, and no stage has delta-v left for it.", "", true); bad = true; }
                    else
                    {
                        double local = ls.TwrVac * G0 / g;
                        bool low = local < 1.3, soft = local < 2;
                        r.Add("Landing on " + b.Name + " (no air) with stage " + ls.Stage + ": thrust to weight there is " + N(local, "0.0") + (c.Dest != null ? ", and it takes about " + N(c.Dest.Land) + " m/s" : ""), low ? "cannot stop" : soft ? "sluggish" : local > 8 ? "far more than needed" : "good", low);
                        bad |= low; marginal |= soft && !low;
                        if (low) r.Fix("The landing stage needs a thrust to weight of 2 or more on " + b.Name + ": a bigger engine, or less mass to land.");
                        if (local > 8 && ls.DvVac > 200) { over = true; r.Fix("The landing engine is much stronger than " + b.Name + " needs (" + N(local, "0.0") + " times the craft's weight there). A smaller, lighter engine would add delta-v."); }
                    }
                    if (legs.Count == 0) r.Add("No landing legs: it would land on its engine bell or tanks, which is risky on a slope.");
                    else Tipping(r, stay, legs, sheet);
                }
            }
            if (shielded > 0 && warmEntries == 0 && spare > 150) r.Fix("Every entry is gentle: most of the ablator will come home unused. Lowering it in the builder saves weight.", "up to " + N(spare * 0.001 * 0.7, "0.##") + " t");
            if (!planned) r.Note = "No mission is set, so this assumes a return to " + (c.Home != null ? c.Home.Name : "home") + ". Pick where it lands in the mission row.";
            r.Rating = bad ? "problem" : marginal ? "marginal" : over ? "oversized" : "safe";
            return r;
        }

        /// <summary>How tall the lander stands against how wide its legs are.</summary>
        static void Tipping(CheckReport r, List<CraftPart> stay, List<CraftPart> legs, CraftSheet sheet)
        {
            if (!stay.All(p => p.HasPos) || legs.Count < 3) { if (legs.Count < 3) r.Add("Only " + legs.Count + " landing leg" + (legs.Count == 1 ? "" : "s") + ": it takes three to stand."); return; }
            var up = Axis(sheet);
            var com = Centre(stay, false);
            double span = legs.Min(p => Perp(Sub(Pos(p), com), up));
            double low = legs.Min(p => Dot(Sub(Pos(p), com), up));
            double height = -low;
            if (span <= 0.05 || height <= 0) return;
            double ratio = height / span, slope = Math.Atan(span / height) * 180 / Math.PI;
            r.Add("Centre of mass sits " + N(height, "0.0") + " m above the feet, which stand " + N(span, "0.0") + " m out: it tips past about " + N(slope) + "° of slope.", ratio > 2.2 ? "tall and narrow" : ratio > 1.3 ? "fair" : "stable", ratio > 3.5);
            if (ratio > 2.2) r.Fix("Mount the legs further out or lower the heavy parts: a wider stance lets it stay up on slopes.");
        }

        // ---------------------------------------------------------------- balance

        internal static double[] Pos(CraftPart p) { return new[] { p.X, p.Y, p.Z }; }
        internal static double[] Sub(double[] a, double[] b) { return new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] }; }
        internal static double Dot(double[] a, double[] b) { return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]; }
        static double[] Cross(double[] a, double[] b) { return new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] }; }
        internal static double Len(double[] a) { return Math.Sqrt(Dot(a, a)); }
        internal static double Perp(double[] a, double[] axis) { double along = Dot(a, axis); return Math.Sqrt(Math.Max(0, Dot(a, a) - along * along)); }

        internal static double[] Centre(IEnumerable<CraftPart> parts, bool dryOnly)
        {
            double m = 0; var c = new double[3];
            foreach (var p in parts)
            {
                double pm = dryOnly ? p.DryMass : p.Mass;
                m += pm; c[0] += p.X * pm; c[1] += p.Y * pm; c[2] += p.Z * pm;
            }
            if (m > 0) { c[0] /= m; c[1] /= m; c[2] /= m; }
            return c;
        }

        /// <summary>The way the craft points when it flies: opposite to the exhaust of the first engines to fire.</summary>
        internal static double[] Axis(CraftSheet sheet)
        {
            var engines = sheet.Parts.Where(p => p.Of("engine").Any(m => m.N("aimed") > 0)).ToList();
            if (engines.Count > 0)
            {
                int first = engines.Max(p => p.Stage);
                var f = new double[3];
                foreach (var p in engines.Where(p => p.Stage == first))
                    foreach (var m in p.Of("engine").Where(m => m.N("aimed") > 0).Take(1))
                    { f[0] -= m.N("dx") * m.N("thrust"); f[1] -= m.N("dy") * m.N("thrust"); f[2] -= m.N("dz") * m.N("thrust"); }
                double len = Len(f);
                if (len > 1e-6) return new[] { f[0] / len, f[1] / len, f[2] / len };
            }
            return new[] { 0.0, 1.0, 0.0 };
        }

        public static CheckReport Balance(CheckContext c)
        {
            var r = new CheckReport();
            var sheet = c.Sheet;
            if (!sheet.Parts.Any(p => p.HasPos)) { r.Rating = "not computed"; r.Add("Part positions could not be read, so balance is not worked out."); return r; }
            var up = Axis(sheet);
            bool bad = false, warn = false, over = false;
            var engineParts = sheet.Parts.Where(p => p.Of("engine").Any(m => m.N("aimed") > 0 && m.N("thrust") > 0)).ToList();
            int lines = 0;
            string lastKey = "";
            for (int k = sheet.MaxStage; k >= 0; k--)
            {
                var active = engineParts.Where(p => p.Stage >= k && p.DropStage < k && p.Modules.Any(m => m.Kind == "engine" && (m.Staged || p.Stage == k))).ToList();
                if (active.Count == 0) continue;
                string key = string.Join(",", active.Select(p => p.Index.ToString()).ToArray());
                if (key == lastKey) continue;
                lastKey = key;
                var present = sheet.Parts.Where(p => p.DropStage < k).ToList();
                double worstTorque = 0, worstArm = 0, authority = 0, total = 0, straight = 0;
                foreach (bool dryRun in new[] { false, true })
                {
                    var com = Centre(present, dryRun);
                    var f = new double[3]; var tq = new double[3]; double sum = 0, gimbal = 0;
                    foreach (var p in active)
                    {
                        // A part with several engine modes fires one at a time: take the strongest.
                        var m = p.Of("engine").Where(x => x.N("aimed") > 0).OrderByDescending(x => x.N("thrust")).First();
                        double t = m.N("thrust") * m.N("limit", 100) / 100;
                        var fi = new[] { -m.N("dx") * t, -m.N("dy") * t, -m.N("dz") * t };
                        var arm = Sub(new[] { m.N("px"), m.N("py"), m.N("pz") }, com);
                        var ti = Cross(arm, fi);
                        for (int i = 0; i < 3; i++) { f[i] += fi[i]; tq[i] += ti[i]; }
                        sum += t;
                        double range = p.Of("gimbal").Select(g => g.N("range")).DefaultIfEmpty(0).Max();
                        if (range > 0) gimbal += t * Math.Sin(range * Math.PI / 180) * Math.Abs(Dot(arm, up));
                    }
                    double wheels = present.SelectMany(p => p.Of("wheel")).Sum(m => m.N("torque"));
                    double torque = Len(tq), force = Len(f);
                    if (torque > worstTorque) { worstTorque = torque; worstArm = force > 0 ? torque / force : 0; authority = gimbal + wheels; }
                    total = sum; straight = force;
                }
                if (total > 0 && straight < total * 0.97)
                {
                    r.Add("Stage " + k + ": the engines are angled, so " + N((1 - straight / total) * 100) + "% of their thrust cancels out.", "", straight < total * 0.85);
                    warn = true; lines++;
                }
                if (worstArm > 0.03 && worstTorque > 0.5)
                {
                    bool flips = worstTorque > authority * 0.9, hard = worstTorque > authority * 0.35;
                    if (flips || hard)
                    {
                        r.Add("Stage " + k + ": thrust passes " + N(worstArm, "0.00") + " m off the centre of mass and twists the craft with " + N(worstTorque) + " kNm; gimbals and reaction wheels can push back with " + N(authority) + " kNm.", flips ? "it will tumble" : "fights the controls", flips);
                        if (flips) { bad = true; r.Fix("Stage " + k + ": move the engines or the heavy parts so the thrust lines up with the centre of mass (turn on the centre of mass and centre of thrust markers in the builder), or add gimballed engines."); }
                        else warn = true;
                        lines++;
                    }
                }
            }
            if (engineParts.Count > 0 && lines == 0) r.Add("Thrust passes through the centre of mass in every stage, full or empty.", "balanced");

            // Fins: behind the centre of mass is stable, ahead of it flips the rocket.
            bool air = c.Home != null && c.Home.AtmoDepth > 0;
            var atLaunch = sheet.Parts;
            // Pods and heat shields have body lift too; only real fins and wings steer the rocket in the air.
            var fins = atLaunch.Where(p => p.HasPos && p.Of("wing").Any(m => m.N("lift") > 0) && !p.Has("command") && !p.Has("ablator") && !p.Has("chute") && p.Crew == 0).ToList();
            if (air && fins.Count > 0 && !c.Sph && !sheet.Flying)
            {
                var com = Centre(atLaunch, false);
                double lift = fins.Sum(p => p.Of("wing").Sum(m => m.N("lift")));
                double where = fins.Sum(p => Dot(Sub(Pos(p), com), up) * p.Of("wing").Sum(m => m.N("lift"))) / lift;
                bool ahead = where > 0.3;
                r.Add("Fins and wings sit on average " + N(Math.Abs(where), "0.0") + " m " + (where > 0 ? "above" : "below") + " the centre of mass at lift-off.", ahead ? "unstable" : "stable", ahead);
                if (ahead) { bad = true; r.Fix("Move the fins to the bottom of the first stage: lift ahead of the centre of mass turns the rocket around in the air."); }
            }
            else if (air && fins.Count == 0 && !c.Sph && !sheet.Flying && engineParts.Count > 0 && !sheet.Parts.Any(p => p.Has("gimbal")) && !sheet.Parts.Any(p => p.Has("wheel")))
            {
                r.Add("Nothing steers it through the air: no fins, no gimballed engine, no reaction wheels.", "", true);
                bad = true;
            }

            // How quickly the final stage turns on its reaction wheels.
            var stay = sheet.Parts.Where(p => p.DropStage < 0 && p.HasPos).ToList();
            if (stay.Count > 0)
            {
                var com = Centre(stay, false);
                double inertia = stay.Sum(p => p.Mass * Math.Max(0.25, Dot(Sub(Pos(p), com), Sub(Pos(p), com))));
                var wheelParts = stay.Where(p => p.Has("wheel")).ToList();
                double torque = wheelParts.SelectMany(p => p.Of("wheel")).Sum(m => m.N("torque"));
                bool rcs = stay.Any(p => p.Has("rcs"));
                if (torque > 0 && inertia > 0)
                {
                    Func<double, double> quarter = tq => 2 * Math.Sqrt(Math.PI / 2 / (tq / inertia));
                    double t90 = quarter(torque);
                    bool slow = t90 > 45;
                    r.Add("The last stage (" + N(stay.Sum(p => p.Mass), "0.0#") + " t) turns a quarter circle in about " + N(t90) + " s on its reaction wheels (" + N(torque, "0.#") + " kNm)" + (rcs ? ", plus RCS" : ""), slow ? "sluggish" : t90 < 3 ? "very quick" : "fine");
                    if (slow && !rcs) { warn = true; r.Fix("Add reaction wheel torque of about " + N(inertia * Math.PI / 2 / 100 - torque, "0.#") + " kNm more to turn a quarter circle in 20 s."); }
                    var extras = wheelParts.Where(p => !p.Has("command")).OrderBy(p => p.Of("wheel").Sum(m => m.N("torque"))).ToList();
                    if (t90 < 3 && extras.Count > 0)
                    {
                        double left = torque, mass = 0, ec = 0; int n = 0;
                        foreach (var p in extras)
                        {
                            double one = p.Of("wheel").Sum(m => m.N("torque"));
                            if (left - one <= 0 || quarter(left - one) > 10) break;
                            left -= one; mass += p.Mass; ec += p.Of("wheel").Sum(m => Math.Max(0, -m.Rate(CraftSheet.EC))); n++;
                        }
                        if (n > 0) { over = true; r.Fix("More reaction wheels than the last stage needs: " + n + " could go and it would still turn a quarter circle in " + N(quarter(left)) + " s.", "saves " + N(mass, "0.##") + " t" + (ec > 0 ? " and up to " + CraftSheet.Ec(ec) : "")); }
                    }
                }
            }
            r.Rating = bad ? "problem" : warn ? "check" : over ? "oversized" : "balanced";
            if (c.Sph) r.Note = "For planes the builder's own centre of lift marker is the better guide; the plane checks come in a later part.";
            return r;
        }

        // ---------------------------------------------------------------- launch and structure

        public static CheckReport Launch(CheckContext c)
        {
            var r = new CheckReport();
            var sheet = c.Sheet;
            bool bad = false, warn = false, over = false;
            double mass = sheet.Parts.Sum(p => p.Mass);
            string site = c.Sph ? "runway" : "launch pad", hall = c.Sph ? "hangar" : "assembly building";
            if (sheet.Fact("massLimit") > 0 && sheet.Fact("massLimit") < 1e9)
            {
                double lim = sheet.Fact("massLimit");
                r.Add("Mass " + N(mass, "0.0") + " t; the " + site + " (level " + N(sheet.Fact("siteLevel")) + ") takes " + N(lim, "0.#") + " t", mass > lim ? N(mass - lim, "0.0") + " t over" : N(lim - mass, "0.0") + " t spare", mass > lim);
                bad |= mass > lim;
            }
            else r.Add("Mass at launch", N(mass, "0.0") + " t");
            if (sheet.Fact("partLimit") > 0 && sheet.Fact("partLimit") < 1e6)
            {
                double lim = sheet.Fact("partLimit");
                r.Add(sheet.Parts.Count + " parts; the " + hall + " (level " + N(sheet.Fact("hallLevel")) + ") allows " + N(lim), sheet.Parts.Count > lim ? N(sheet.Parts.Count - lim) + " over" : "ok", sheet.Parts.Count > lim);
                bad |= sheet.Parts.Count > lim;
            }
            if (sheet.HasFact("sizeY"))
            {
                double x = sheet.Fact("sizeX"), y = sheet.Fact("sizeY"), z = sheet.Fact("sizeZ");
                bool lim = sheet.Fact("limitY") > 0 && sheet.Fact("limitY") < 1e6;
                bool tooBig = lim && (x > sheet.Fact("limitX") || y > sheet.Fact("limitY") || z > sheet.Fact("limitZ"));
                r.Add("Size " + N(x, "0.0") + " × " + N(y, "0.0") + " × " + N(z, "0.0") + " m" + (lim ? "; the " + site + " takes " + N(sheet.Fact("limitX"), "0.#") + " × " + N(sheet.Fact("limitY"), "0.#") + " × " + N(sheet.Fact("limitZ"), "0.#") + " m" : ""), tooBig ? "too big" : lim ? "fits" : "", tooBig);
                bad |= tooBig;
            }

            // Lift-off thrust, both ways.
            var order = c.Stages.OrderByDescending(s => s.Stage).ToList();
            bool air = c.Home != null && c.Home.AtmoDepth > 0;
            if (order.Count > 0 && !sheet.Flying && !c.Sph)
            {
                var first = order[0];
                double twr = air && first.TwrAsl > 0 ? first.TwrAsl : first.TwrVac;
                string verdict = twr < 1 ? "cannot lift off" : twr < 1.15 ? "barely lifts" : twr < 1.3 ? "slow" : twr <= 2.2 ? "good" : "more than needed";
                r.Add("Thrust to weight at lift-off", N(twr, "0.00") + ", " + verdict, twr < 1.15);
                bad |= twr < 1.15;
                if (twr < 1.3) r.Fix("Aim for 1.3 to 1.7 at lift-off: add thrust to the first stage, or take " + N(mass * (1 - twr / 1.3), "0.0") + " t off the rocket.");
                if (twr > 2.2)
                {
                    over = true;
                    r.Fix("Lift-off thrust is " + N(twr, "0.0") + " times the weight. Above about 2 the rocket fights thick air and burns hot: a smaller first-stage engine, fewer boosters, or more fuel on the same engines would do better. The thrust limiter in the builder is the quick fix.");
                }
                foreach (var st in order.Skip(1))
                {
                    if (st.DvVac < 400 || st.TwrVac < 3 || st.StartMass <= 0) continue;
                    var eng = sheet.Parts.Where(p => p.Stage == st.Stage && p.Has("engine")).ToList();
                    double engMass = eng.Sum(p => p.DryMass);
                    if (engMass < st.StartMass * 0.08) continue;
                    over = true;
                    r.Fix("Stage " + st.Stage + " has a thrust to weight of " + N(st.TwrVac, "0.0") + " in space, where 0.5 to 1.5 is plenty, and its engines are " + N(engMass / st.StartMass * 100) + "% of its mass. A smaller engine would add delta-v.");
                }
            }
            // Engines built for space, lit on the pad.
            if (air && !sheet.Flying)
            {
                int firstStage = sheet.Parts.Where(p => p.Modules.Any(m => m.Kind == "engine" && m.Staged)).Select(p => p.Stage).DefaultIfEmpty(-1).Max();
                foreach (var g in sheet.Parts.Where(p => p.Stage == firstStage && p.Of("engine").Any(m => m.Staged && m.N("ispVac") > 0 && m.N("ispAsl") > 0 && m.N("ispAsl") < m.N("ispVac") * 0.6 && m.N("ec") <= 0)).GroupBy(p => p.Name))
                {
                    var m = g.First().Of("engine").First();
                    r.Add(Count(g) + " fires on the pad but is built for space: at sea level it gives " + N(m.N("ispAsl") / m.N("ispVac") * 100) + "% of its thrust.", "", true);
                    bad = true;
                }
            }
            // Acceleration as tanks run dry.
            double limitG = c.Crew > 0 ? 6 : 12;
            foreach (var st in order)
            {
                if (st.EndMass <= 0 || st.ThrustVac <= 0) continue;
                double g = st.ThrustVac / st.EndMass / G0;
                if (g > limitG) { warn = true; r.Add("Stage " + st.Stage + " reaches " + N(g, "0.0") + " g as its tanks run dry" + (c.Crew > 0 ? ", hard on the crew" : "") + ". Throttle back late in the burn.", ""); }
            }
            // Wobble: a long thin stack, or heavy boosters hung by one joint.
            bool kjr = sheet.ModFacts.ContainsKey("KJR");
            bool struts = sheet.Parts.Any(p => p.Name.ToLowerInvariant().Contains("strut") && !p.Has("leg"));
            int column = 0;
            if (sheet.Root >= 0 && sheet.Root < sheet.Parts.Count)
            {
                var at = sheet.Parts[sheet.Root]; int guard = 0;
                while (at != null && guard++ < 500)
                {
                    column++;
                    var next = at.Children.Select(i => sheet.Parts[i]).Where(p => !p.Surface).OrderByDescending(p => p.Children.Count + p.Mass).FirstOrDefault();
                    at = next;
                }
            }
            if (!c.Sph && !sheet.Flying)
            {
                if (column >= 14 && !struts && !kjr) { warn = true; r.Add("The main stack is " + column + " parts long with no struts: tall thin stacks sway under thrust. Autostrut the upper parts to the heaviest part (advanced tweakables), or widen the stack."); }
                foreach (var g in sheet.Parts.Where(p => p.Surface && p.Has("decoupler")).GroupBy(p => p.Name))
                {
                    double hung = g.Max(p => Below(sheet, p));
                    if (hung > 15 && !struts && !kjr) { warn = true; r.Add(Count(g) + " each carry " + N(hung, "0.0") + " t on a single joint. Strut the top of each booster to the core, or it will sag and pull off under thrust."); }
                }
                if (kjr) r.Add("Kerbal Joint Reinforcement is installed, so joints are stiffer than stock.");
            }
            r.Rating = bad ? "problem" : warn ? "check" : over ? "oversized" : "ok";
            return r;
        }

        static double Below(CraftSheet sheet, CraftPart p)
        {
            double m = 0; var todo = new Stack<int>(p.Children); int guard = 0;
            while (todo.Count > 0 && guard++ < 5000) { var q = sheet.Parts[todo.Pop()]; m += q.Mass; foreach (int i in q.Children) todo.Push(i); }
            return m;
        }

        // ---------------------------------------------------------------- crew

        static readonly string[] SasLevels = { "holds its heading only", "holds prograde and retrograde", "also holds normal and radial", "also holds a target and maneuver nodes" };

        /// <summary>Per-kerbal needs by life support mod when the mod itself could not be asked. Units per second.</summary>
        static List<CrewRule> DefaultRules(CraftSheet sheet)
        {
            var l = new List<CrewRule>();
            if (sheet.ModFacts.ContainsKey("TACLifeSupport") || sheet.Max("Food") > 0 && sheet.Max("Water") > 0 && sheet.Max("Oxygen") > 0 && !sheet.ModFacts.ContainsKey("Kerbalism"))
            {
                l.Add(new CrewRule { Name = "food", Input = "Food", PerSec = 0.000016927083, Mod = "TAC Life Support" });
                l.Add(new CrewRule { Name = "water", Input = "Water", PerSec = 0.000011188078, Mod = "TAC Life Support" });
                l.Add(new CrewRule { Name = "oxygen", Input = "Oxygen", PerSec = 0.001713537562, Mod = "TAC Life Support" });
            }
            if (sheet.ModFacts.ContainsKey("USILifeSupport") || sheet.Max("Supplies") > 0) l.Add(new CrewRule { Name = "supplies", Input = "Supplies", PerSec = 0.00005, Mod = "USI Life Support" });
            if (sheet.ModFacts.ContainsKey("Snacks") || sheet.Max("Snacks") > 0) l.Add(new CrewRule { Name = "snacks", Input = "Snacks", PerSec = 3.0 / 21600, Mod = "Snacks" });
            return l;
        }

        public static CheckReport Crew(CheckContext c)
        {
            var r = new CheckReport();
            var sheet = c.Sheet;
            int seats = sheet.Parts.Sum(p => p.Crew), crew = c.Crew;
            var pods = sheet.Parts.Where(p => p.Of("command").Any(m => m.N("crewMin") > 0)).ToList();
            var probes = sheet.Parts.Where(p => p.Of("command").Any(m => m.N("crewMin") <= 0)).ToList();
            bool bad = false, warn = false, over = false;
            r.Add(seats == 0 ? "No seats: this craft flies without crew." : seats + " seat" + (seats == 1 ? "" : "s") + (sheet.Flying ? ", " + crew + " aboard" : ""));
            if (pods.Count > 0 && probes.Count == 0)
            {
                int need = pods.Min(p => (int)p.Of("command").Max(m => m.N("crewMin")));
                r.Add("Control comes only from crewed pods: it needs at least " + need + " aboard to fly, and a pilot for SAS to hold anything but the heading.");
            }
            if (probes.Count > 0)
            {
                int level = (int)probes.SelectMany(p => p.Of("sas")).Select(m => m.N("level")).DefaultIfEmpty(-1).Max();
                if (level >= 0) r.Add("Without a pilot, the best probe core (" + probes.OrderByDescending(p => p.Of("sas").Select(m => m.N("level")).DefaultIfEmpty(-1).Max()).First().Title + ") " + SasLevels[Math.Min(3, level)] + ".", "SAS level " + level);
                else r.Add("The probe core has no SAS of its own: without a pilot the craft will not hold a heading.", "", pods.Count == 0);
                bad |= level < 0 && pods.Count == 0;
            }
            if (crew <= 0) { r.Rating = bad ? "problem" : "uncrewed"; return r; }

            // Supplies against the length of the trip.
            var rules = sheet.CrewRules.Where(x => x.Input != CraftSheet.EC && x.PerSec > 0).ToList();
            if (rules.Count == 0) rules = DefaultRules(sheet);
            double day = sheet.Fact("day", 21600);
            double trip = c.HasMission && c.Dest != null ? c.Dest.Travel * (c.Plan.Return ? 2 : 1) : 0;
            double limit = double.MaxValue; string limiting = "";
            foreach (var rule in rules.GroupBy(x => x.Input).Select(g => g.First()))
            {
                double have = sheet.Amount(rule.Input), cap = sheet.Max(rule.Input);
                if (cap <= 0 && have <= 0)
                {
                    r.Add("No " + rule.Input + " aboard, and " + rule.Mod + " has each kerbal using it.", "", true);
                    bad = true; limit = 0; limiting = rule.Input;
                    continue;
                }
                double secs = have / (rule.PerSec * crew);
                if (secs < limit) { limit = secs; limiting = rule.Input; }
                r.Add(rule.Input + ": " + N(have, "0.#") + " aboard for " + crew + " crew" + (rule.Varies ? " (the rate varies)" : ""), N(secs / day, secs / day < 10 ? "0.#" : "0") + " days", false, rule.Mod);
            }
            if (rules.Count > 0 && trip > 0)
            {
                string tripText = N(trip / day) + " days (" + (c.Plan.Return ? "there and back" : "one way") + (c.Plan.Return && c.There != null && PlanetOf(c.There, c.Bodies).Name != PlanetOf(c.Home, c.Bodies).Name ? ", not counting the wait for the window home" : "") + ")";
                bool shortBy = limit < trip, tight = !shortBy && limit < trip * 1.25;
                r.Add("The trip to " + c.Plan.Body + " takes about " + tripText + "; " + limiting + " runs out first, after " + N(limit / day) + " days.", shortBy ? "runs out" : tight ? "tight" : "enough", shortBy);
                bad |= shortBy; warn |= tight;
                if (shortBy)
                    foreach (var rule in rules.GroupBy(x => x.Input).Select(g => g.First()))
                    {
                        double need = rule.PerSec * crew * trip * 1.25, have = sheet.Amount(rule.Input);
                        if (have < need) r.Fix("Carry " + N(need - have, "0.#") + " more " + rule.Input + " (25% margin included).");
                    }
                else if (limit > trip * 4 && limit < double.MaxValue)
                {
                    double extraMass = 0;
                    foreach (var rule in rules.GroupBy(x => x.Input).Select(g => g.First()))
                    {
                        double have = sheet.Amount(rule.Input), need = rule.PerSec * crew * trip * 1.5;
                        double per = sheet.Parts.Where(p => p.Max(rule.Input) > 0).Sum(p => p.ResMass) / Math.Max(1e-9, sheet.Parts.Where(p => p.Max(rule.Input) > 0).Sum(p => p.Res.Sum(x => x.Amount)));
                        if (have > need) extraMass += (have - need) * per;
                    }
                    if (extraMass > 0.05) { over = true; r.Fix("Supplies last " + N(limit / trip, "0.#") + " times the trip. Carrying one and a half times the trip instead would do.", "saves about " + N(extraMass, "0.##") + " t"); }
                }
            }
            else if (rules.Count > 0) r.Note = "Pick a destination in the mission row to compare supplies with the length of the trip.";
            else if (sheet.ModFacts.ContainsKey("Kerbalism")) r.Add("Kerbalism is installed but its supply rates could not be read, so supplies are not checked here.");

            if (ModKnowledge.FeatureOn(sheet, "Kerbalism.Habitat") || ModKnowledge.FeatureOn(sheet, "Kerbalism.LivingSpace"))
            {
                double vol = sheet.Named("Habitat").Sum(m => m.Any("volume", "Volume"));
                if (vol > 0) r.Add("Living space (Kerbalism): " + N(vol, "0.#") + " m³ for " + crew + " crew", N(vol / crew, "0.#") + " m³ each");
            }
            if (ModKnowledge.FeatureOn(sheet, "Kerbalism.Radiation") || ModKnowledge.FeatureOn(sheet, "Kerbalism.Shielding"))
            {
                double have = sheet.Amount("Shielding"), cap = sheet.Max("Shielding");
                if (cap > 0) r.Add("Radiation shielding (Kerbalism): " + N(have, "0.#") + " of " + N(cap, "0.#") + " fitted" + (c.Away && have < cap * 0.5 ? ". Beyond the home system's protection the dose adds up on a long trip." : "."), "", false);
            }
            r.Rating = bad ? "problem" : warn ? "tight" : over ? "oversized" : "ok";
            return r;
        }

        // ---------------------------------------------------------------- cost

        public static CheckReport Cost(CheckContext c)
        {
            var r = new CheckReport();
            var sheet = c.Sheet;
            double total = sheet.Parts.Sum(p => p.Cost);
            r.Add("Whole craft as it stands", N(total) + " funds");
            var stay = sheet.Parts.Where(p => p.DropStage < 0).ToList();
            double kept = stay.Sum(p => p.Cost);
            foreach (var g in sheet.Parts.Where(p => p.DropStage >= 0).GroupBy(p => p.DropStage).OrderByDescending(g => g.Key))
                r.Add("Thrown away in stage " + g.Key + " (" + g.Count() + " parts)", N(g.Sum(p => p.Cost)) + " funds");
            r.Add("Kept to the end (" + stay.Count + " parts): what recovery can pay back", N(kept) + " funds");
            foreach (var g in sheet.Parts.GroupBy(p => p.Name).OrderByDescending(g => g.Sum(p => p.Cost)).Take(5))
            {
                double cost = g.Sum(p => p.Cost);
                if (cost <= 0) continue;
                r.Add("Most expensive: " + Count(g), N(cost) + " funds, " + N(cost / Math.Max(1, total) * 100) + "%", false, g.First().Mod);
            }
            double payload = stay.Sum(p => p.Mass);
            if (payload > 0 && total > 0) r.Add("Cost for each tonne of the last stage", N(total / payload) + " funds");
            if (sheet.Fact("career", 1) == 0) r.Note = "This is not a career game, so funds do not limit anything.";
            r.Rating = N(total) + " funds";
            return r;
        }
    }
}
