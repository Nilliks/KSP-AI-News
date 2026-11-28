using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>One resource on a part.</summary>
    public class CraftRes { public string Name = ""; public double Amount, Max; }

    /// <summary>A resource a module makes (+) or uses (-), per second, when it is running.</summary>
    public class CraftRate { public string Res = ""; public double PerSec; }

    /// <summary>
    /// One part module, read generically by the KSP layer. Kind is what the module does (engine, solar, generator, converter,
    /// drill, command, wheel, antenna, chute, decoupler, dock, rcs, light, leg, ablator, fairing, clamp, science, lab, radiator,
    /// alternator, gimbal, sas, intake, wing, cargo) or "other" for anything the reader does not recognise, which is how modded
    /// modules arrive: with their class name, config values and declared resource rates.
    /// </summary>
    public class CraftModule
    {
        public string Name = "";
        public string Kind = "other";
        public string Label = "";
        public bool Staged;
        public Dictionary<string, double> Num = new Dictionary<string, double>();
        public Dictionary<string, string> Txt = new Dictionary<string, string>();
        public List<CraftRate> Rates = new List<CraftRate>();
        /// <summary>Keys in Num and Txt that came straight from a mod's own config fields on a module of a known kind.</summary>
        public List<string> Cfg = new List<string>();

        /// <summary>The first of several possible field names that is present: mods rename things between versions.</summary>
        public double Any(params string[] keys) { foreach (var k in keys) { double v; if (Num.TryGetValue(k, out v)) return v; } return 0; }
        public bool Is(string prefix) { return Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase); }
        /// <summary>A curve the reader stored as "x:y;x:y", read at x by straight lines between the points.</summary>
        public double Curve(string key, double x)
        {
            string t = T(key);
            if (t == "") return 0;
            double px = 0, py = 0; bool first = true;
            foreach (var pair in t.Split(';'))
            {
                var xy = pair.Split(':');
                double cx, cy;
                if (xy.Length != 2 || !double.TryParse(xy[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cx) || !double.TryParse(xy[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cy)) continue;
                if (x <= cx) return first || cx == px ? cy : py + (cy - py) * (x - px) / (cx - px);
                px = cx; py = cy; first = false;
            }
            return py;
        }
        public double CurveMax(string key) { return N(key + ".max"); }

        public double N(string key, double def = 0) { double v; return Num.TryGetValue(key, out v) ? v : def; }
        public string T(string key) { string v; return Txt.TryGetValue(key, out v) ? v : ""; }
        public double Rate(string res) { return Rates.Where(r => r.Res == res).Sum(r => r.PerSec); }
    }

    /// <summary>A process a mod runs in the background, scaled to what this craft carries.</summary>
    public class CraftProcess { public string Name = "", Title = "", Mod = ""; public double Capacity; public List<CraftRate> Rates = new List<CraftRate>(); }

    /// <summary>A per-kerbal need from a life support mod: uses Input (and makes Output) at PerSec for each crew member.</summary>
    public class CrewRule { public string Name = "", Input = "", Output = "", Mod = ""; public double PerSec; public bool Varies; }

    /// <summary>One part on the craft.</summary>
    public class CraftPart
    {
        public int Index, Parent = -1;
        public string Name = "", Title = "", Mod = "";
        public double DryMass, ResMass, Cost;   // tonnes, tonnes, funds
        /// <summary>The stage this part is activated in (the game's inverse stage; the highest number fires first).</summary>
        public int Stage = -1;
        public bool Surface, CrossFeed = true;
        public int Crew;
        public List<CraftRes> Res = new List<CraftRes>();
        public List<CraftModule> Modules = new List<CraftModule>();
        /// <summary>The part's tooltip text per module, as the game shows it in the parts list.</summary>
        public List<string> Info = new List<string>();

        /// <summary>Worked out by <see cref="CraftSheet.Prepare"/>: the stage in which this part leaves the craft, or -1 if it never does.</summary>
        public int DropStage = -1;
        public List<int> Children = new List<int>();

        /// <summary>Centre of mass in the root part's frame (metres), when the reader could get it.</summary>
        public double X, Y, Z;
        public bool HasPos;
        /// <summary>Impact speed the part survives (m/s), hottest it can get (K), and the drag area of its opened parachute (m², raw from the game's drag cube).</summary>
        public double CrashTol, MaxTemp, ChuteArea;
        /// <summary>For parts that fold out (panels, antennas, radiators): 1 extended, 0 retracted, -1 does not fold.</summary>
        public int Deployed = -1;
        /// <summary>The stack sizes the part fits ("size1, srf"), as the part file declares them.</summary>
        public string Bulkhead = "";
        /// <summary>For a part still locked: the tech node that unlocks it, its title and its science cost.</summary>
        public string Tech = "", TechTitle = "";
        public double TechCost;
        public double Mass { get { return DryMass + ResMass; } }

        public bool Has(string kind) { return Modules.Any(m => m.Kind == kind); }
        public IEnumerable<CraftModule> Of(string kind) { return Modules.Where(m => m.Kind == kind); }
        public double Max(string res) { return Res.Where(r => r.Name == res).Sum(r => r.Max); }
    }

    /// <summary>A body in the save, with what the planners need. Distances in metres, mu in m^3/s^2, pressure in atmospheres.</summary>
    public class EngBody
    {
        public string Name = "", Parent = "";
        public double Mu, Radius, Gravity, AtmoDepth, AtmoPressure, Sma, Soi, RotPeriod, SunDistance;
        /// <summary>Air density at the surface, kg/m³ (Kerbin 1.225).</summary>
        public double AtmoDensity;
        /// <summary>Orbital period around the parent, seconds.</summary>
        public double Period;
        public bool HasSurface = true, IsHome, IsStar, Oxygen;
    }

    /// <summary>A line in a report. Bad lines are real problems; Fix lines are suggestions.</summary>
    public class SheetLine
    {
        public string Text = "", Value = "", Tag = "";
        public bool Bad, Fix;
        public SheetLine() { }
        public SheetLine(string text, string value = "", bool bad = false, string tag = "") { Text = text; Value = value; Bad = bad; Tag = tag; }
    }

    /// <summary>The power budget, worked out from the modules' declared rates.</summary>
    public class PowerReport
    {
        public List<SheetLine> Sources = new List<SheetLine>(), Demand = new List<SheetLine>(), There = new List<SheetLine>(), Fixes = new List<SheetLine>();
        public double Battery, SolarHome, SolarThere, Steady, FuelCell, Alternator, Always, Wheels, Working, Transmit;
        public double ShadowSeconds, ShadowNeed;
        /// <summary>From mods: electric engines at full thrust, capacitor burst rate and store, tank cooling, and loads that must never lose power.</summary>
        public double Engines, Burst, BurstStore, Cooling, Critical;
        public List<string> Notes = new List<string>();
        public string Where = "", Rating = "";
        public bool Unknown;
        public double GenThere { get { return SolarThere + Steady; } }
        /// <summary>Coasting: the things that are always on, plus the reaction wheels holding attitude.</summary>
        public double Coast { get { return Always + Wheels * CraftSheet.WheelHold; } }
        public int Problems { get { return There.Count(l => l.Bad); } }
    }

    /// <summary>What happens in one stage.</summary>
    public class StageEvents
    {
        public int Stage;
        public List<string> Fires = new List<string>();
        public EngStage Dv;
    }

    /// <summary>
    /// Everything about the craft as it is right now: every part with its modules and resources, read live by the KSP layer.
    /// The analysis here is plain arithmetic, so it costs no AI call and works offline.
    /// </summary>
    public class CraftSheet
    {
        public List<CraftPart> Parts = new List<CraftPart>();
        public int Root;
        /// <summary>Real time (seconds) the sheet was read, for the "updated N s ago" note.</summary>
        public double BuiltAt;
        public bool Flying;
        /// <summary>Numbers about the save the checks need: tracking station range, launch site limits, craft size, day length.</summary>
        public Dictionary<string, double> Facts = new Dictionary<string, double>();
        /// <summary>Which features of installed mods are switched on in this save ("Kerbalism.Reliability" = "on").</summary>
        public Dictionary<string, string> ModFacts = new Dictionary<string, string>();
        /// <summary>Background processes a mod runs on this craft (Kerbalism's scrubbers, fuel cells and the like), with their rates.</summary>
        public List<CraftProcess> Processes = new List<CraftProcess>();
        /// <summary>What each crew member uses, per second, as the life support mod defines it.</summary>
        public List<CrewRule> CrewRules = new List<CrewRule>();
        /// <summary>Parts lying loose in the builder, not attached to the craft.</summary>
        public int Loose;
        /// <summary>True for a what-if copy of the craft, so a trial does not start trials of its own.</summary>
        public bool Trial;

        public double Fact(string key, double def = 0) { double v; return Facts.TryGetValue(key, out v) ? v : def; }
        public bool HasFact(string key) { return Facts.ContainsKey(key); }
        public string ModFact(string key) { string v; return ModFacts.TryGetValue(key, out v) ? v : ""; }
        public bool HasModule(string prefix) { return Parts.Any(p => p.Modules.Any(m => m.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))); }
        public IEnumerable<CraftModule> Named(string prefix) { return Parts.SelectMany(p => p.Modules.Where(m => m.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))); }

        /// <summary>Share of full reaction wheel power used to hold attitude while coasting.</summary>
        public const double WheelHold = 0.15;
        public const string EC = "ElectricCharge";

        static string N(double v, string f = "0") { return v.ToString(f, CultureInfo.InvariantCulture); }
        /// <summary>A rate in EC/s with enough digits to mean something.</summary>
        public static string Ec(double v) { double a = Math.Abs(v); return N(v, a >= 100 ? "0" : a >= 10 ? "0.0" : a >= 0.1 ? "0.00" : "0.###") + " EC/s"; }

        public IEnumerable<CraftModule> All(string kind) { return Parts.SelectMany(p => p.Of(kind)); }
        public int Count(string kind) { return Parts.Count(p => p.Has(kind)); }
        public double Max(string res) { return Parts.Sum(p => p.Max(res)); }
        public double Amount(string res) { return Parts.SelectMany(p => p.Res).Where(r => r.Name == res).Sum(r => r.Amount); }
        public int MaxStage { get { return Parts.Count == 0 ? -1 : Parts.Max(p => p.Stage); } }

        /// <summary>
        /// Works out the tree and when each part leaves. The game's root part can be anywhere (often a tank), so the tree is
        /// re-rooted at the part that stays to the end: the root if it can control the craft, otherwise the command part
        /// behind the most decouplers.
        /// </summary>
        public void Prepare()
        {
            int n = Parts.Count;
            for (int i = 0; i < n; i++) { Parts[i].Index = i; Parts[i].Children.Clear(); Parts[i].DropStage = -1; }
            if (n == 0) return;
            var adj = new List<int>[n];
            for (int i = 0; i < n; i++) adj[i] = new List<int>();
            for (int i = 0; i < n; i++) { int p = Parts[i].Parent; if (p >= 0 && p < n && p != i) { adj[i].Add(p); adj[p].Add(i); } }
            if (Root < 0 || Root >= n) Root = 0;
            int anchor = Root;
            if (!Parts[Root].Has("command"))
            {
                int best = -1, bestDepth = -1;
                foreach (var c in Parts.Where(p => p.Has("command")))
                {
                    int depth = 0, at = c.Index, guard = 0;
                    while (at != Root && at >= 0 && guard++ < n) { if (IsDecoupler(Parts[at])) depth++; at = Parts[at].Parent; }
                    if (depth > bestDepth) { bestDepth = depth; best = c.Index; }
                }
                if (best >= 0) anchor = best;
            }
            // Breadth-first from the anchor: the anchor's side of every decoupler is the side that stays.
            var seen = new bool[n];
            var queue = new Queue<int>();
            var order = new List<int>();
            var from = new int[n];
            seen[anchor] = true; from[anchor] = -1; queue.Enqueue(anchor);
            while (queue.Count > 0)
            {
                int at = queue.Dequeue(); order.Add(at);
                foreach (int next in adj[at]) if (!seen[next]) { seen[next] = true; from[next] = at; Parts[at].Children.Add(next); queue.Enqueue(next); }
            }
            foreach (int i in order)
            {
                var p = Parts[i];
                int inherited = from[i] >= 0 ? Parts[from[i]].DropStage : -1;
                // A decoupler leaves with what it drops; it fires first when its stage number is the higher one.
                p.DropStage = IsDecoupler(p) ? Math.Max(inherited, p.Stage) : inherited;
            }
            Root = anchor;
        }

        static bool IsDecoupler(CraftPart p) { return p.Modules.Any(m => (m.Kind == "decoupler" || m.Kind == "clamp") && m.Staged); }

        // ---------------------------------------------------------------- parts list

        /// <summary>The parts grouped by type, stage and contents: "4 x FL-T400 Fuel Tank".</summary>
        public class Group
        {
            public CraftPart First; public int Count; public double Mass, Cost;
        }

        public List<Group> Groups()
        {
            var map = new Dictionary<string, Group>();
            var list = new List<Group>();
            foreach (var p in Parts)
            {
                string key = p.Name + "|" + p.Stage + "|" + p.DropStage + "|" + string.Join(",", p.Res.Select(r => r.Name + N(r.Max)).ToArray());
                Group g;
                if (!map.TryGetValue(key, out g)) { g = new Group { First = p }; map[key] = g; list.Add(g); }
                g.Count++; g.Mass += p.DryMass + p.ResMass; g.Cost += p.Cost;
            }
            return list.OrderByDescending(g => g.First.DropStage).ThenByDescending(g => g.Mass).ToList();
        }

        /// <summary>Modules a mod hangs on parts for its own records. Named in full only when the director asks about the part.</summary>
        static readonly string[] Bookkeeping = { "InterstellarFuelSwitch", "IFSCryostat", "PersistentEngine", "ProcessController", "PlannerController", "Reliability", "SolarPanelFixer", "ModuleG3", "ModuleWheel", "TweakScale", "ModuleB9PartSwitch", "ModuleFuelJettison", "GeometryPartModule", "FARPartModule", "ModuleSystemHeat" };

        static readonly string[] QuietKinds = { "gimbal", "sas", "cargo", "coreheat" };

        /// <summary>What a part does, in a few words, from its modules.</summary>
        public static string Does(CraftPart p) { return Does(p, null, false); }

        /// <summary><paramref name="skip"/> names modules left out (ones that sit on nearly every part); <paramref name="brief"/>
        /// keeps unrecognised modules to their name, rates and a few values.</summary>
        public static string Does(CraftPart p, ICollection<string> skip, bool brief)
        {
            var bits = new List<string>();
            var also = new List<string>();
            foreach (var m in p.Modules)
            {
                if (skip != null && m.Kind == "other" && skip.Contains(m.Name)) continue;
                switch (m.Kind)
                {
                    case "engine":
                        bits.Add("engine " + N(m.N("thrust")) + " kN, Isp " + N(m.N("ispVac")) + (m.N("ispAsl") > 0 ? "/" + N(m.N("ispAsl")) : "") + " s" + (m.T("props") != "" ? ", burns " + m.T("props") : "") + (m.N("locked") > 0 ? ", solid (cannot throttle)" : "") + (m.N("limit", 100) < 99.5 ? ", limited to " + N(m.N("limit")) + "%" : "") + (m.Label != "" ? " [" + m.Label + "]" : ""));
                        break;
                    case "rcs": bits.Add("RCS " + N(m.N("thrust"), "0.#") + " kN" + (m.T("props") != "" ? " (" + m.T("props") + ")" : "")); break;
                    case "solar": bits.Add("solar " + Ec(m.N("rate")) + (m.N("tracking") > 0 ? ", tracks the sun" : ", fixed") + (m.N("retracted") > 0 ? ", starts retracted" : "")); break;
                    case "generator": case "alternator": case "converter": case "drill":
                        if (m.Rates.Count > 0) bits.Add((m.Kind == "generator" ? "generator" : m.Kind == "alternator" ? "alternator" : m.Kind == "drill" ? "drill" : "converter") + (m.Label != "" ? " \"" + m.Label + "\"" : "") + ": " + RatesText(m.Rates));
                        else bits.Add(m.Kind);
                        break;
                    case "command": bits.Add("control" + (m.N("crewMin") > 0 ? " (needs " + N(m.N("crewMin")) + " crew)" : " (probe)") + (m.Rate(EC) < 0 ? ", uses " + Ec(-m.Rate(EC)) : "")); break;
                    case "wheel": bits.Add("reaction wheel " + N(m.N("torque"), "0.#") + " kNm" + (m.Rate(EC) < 0 ? ", up to " + Ec(-m.Rate(EC)) : "")); break;
                    case "antenna": bits.Add((m.T("type") == "" ? "" : m.T("type") + " ") + "antenna, power " + Big(m.N("power")) + (m.N("ec") > 0 ? ", " + Ec(m.N("ec")) + " while sending" : "")); break;
                    case "chute": bits.Add("parachute"); break;
                    case "decoupler": bits.Add(m.Staged ? "decoupler" : "decoupler (staging off)"); break;
                    case "clamp": bits.Add("launch clamp"); break;
                    case "dock": bits.Add("docking port" + (m.T("size") != "" ? " " + m.T("size") : "")); break;
                    case "light": bits.Add("light" + (m.Rate(EC) < 0 ? " " + Ec(-m.Rate(EC)) : "")); break;
                    case "leg": bits.Add(m.T("type") == "" ? "landing leg or wheel" : m.T("type")); break;
                    case "ablator": bits.Add("heat shield"); break;
                    case "fairing": bits.Add("fairing"); break;
                    case "science": bits.Add("experiment" + (m.Label != "" ? " " + m.Label : "")); break;
                    case "lab": bits.Add("science lab"); break;
                    case "radiator": bits.Add("radiator" + (m.N("kw") > 0 ? " " + N(m.N("kw")) + " kW" : "") + (m.Rate(EC) < 0 ? ", " + Ec(-m.Rate(EC)) : "")); break;
                    case "intake": bits.Add("air intake"); break;
                    case "wing": if (!bits.Contains("wing or control surface") && !p.Has("command") && !p.Has("ablator") && p.Crew == 0) bits.Add("wing or control surface"); break;
                    case "other":
                        if (brief && m.Rates.Count == 0) { if (!Bookkeeping.Any(b => m.Name.StartsWith(b, StringComparison.Ordinal)) && !also.Contains(m.Name)) also.Add(m.Name); break; }
                        var o = new StringBuilder(m.Name);
                        if (m.Label != "") o.Append(" \"" + m.Label + "\"");
                        var kv = new List<string>();
                        if (m.Rates.Count > 0) kv.Add(RatesText(m.Rates));
                        foreach (var t in m.Txt.Where(x => x.Value.IndexOf(';') < 0).Take(brief ? 1 : 4)) kv.Add(t.Key + "=" + t.Value);
                        foreach (var t in m.Num.Take(brief ? 3 : 10 - Math.Min(4, m.Txt.Count))) kv.Add(t.Key + "=" + N(t.Value, "0.###"));
                        if (kv.Count > 0) o.Append(" (" + string.Join(", ", kv.ToArray()) + ")");
                        bits.Add(o.ToString());
                        break;
                    default: if (!QuietKinds.Contains(m.Kind)) bits.Add(m.Kind); break;
                }
                // What a mod's own version of a stock module adds (a heat loop, a reactor curve): shown when the part is asked about.
                if (!brief && m.Kind != "other" && m.Cfg.Count > 0)
                    bits.Add(m.Name + " {" + string.Join(", ", m.Cfg.Take(12).Select(k => k + "=" + (m.Num.ContainsKey(k) ? N(m.Num[k], "0.###") : m.T(k))).ToArray()) + "}");
            }
            if (also.Count > 0) bits.Add("mod modules: " + string.Join(", ", also.Take(5).ToArray()) + (also.Count > 5 ? " and " + (also.Count - 5) + " more" : ""));
            if (p.Crew > 0) bits.Insert(0, p.Crew + " seat" + (p.Crew == 1 ? "" : "s"));
            return string.Join("; ", bits.ToArray());
        }

        static string Big(double v) { return v >= 1e9 ? N(v / 1e9, "0.#") + "G" : v >= 1e6 ? N(v / 1e6, "0.#") + "M" : v >= 1e3 ? N(v / 1e3, "0.#") + "k" : N(v); }

        public static string RatesText(IEnumerable<CraftRate> rates)
        {
            var use = rates.Where(r => r.PerSec < 0).Select(r => N(-r.PerSec, "0.####") + " " + Short(r.Res) + "/s").ToArray();
            var make = rates.Where(r => r.PerSec > 0).Select(r => N(r.PerSec, "0.####") + " " + Short(r.Res) + "/s").ToArray();
            return (use.Length > 0 ? "uses " + string.Join(" + ", use) : "") + (use.Length > 0 && make.Length > 0 ? ", " : "") + (make.Length > 0 ? "makes " + string.Join(" + ", make) : "");
        }

        /// <summary>Resources a mod uses for its own bookkeeping (Kerbalism's start with an underscore): not cargo.</summary>
        public static bool Hidden(string res) { return res.StartsWith("_"); }

        /// <summary>Unrecognised modules that sit on most parts (a mod's per-part bookkeeping): named once, not on every line.</summary>
        public List<string> Everywhere()
        {
            if (Parts.Count < 6) return new List<string>();
            return Parts.SelectMany(p => p.Modules.Where(m => m.Kind == "other").Select(m => m.Name).Distinct()).GroupBy(n => n)
                .Where(g => g.Count() > Parts.Count * 0.4).Select(g => g.Key).ToList();
        }

        public static string Short(string res) { return res == EC ? "EC" : res; }

        public static string ResText(IEnumerable<CraftRes> res, int count = 1)
        {
            return string.Join(", ", res.Where(r => r.Max > 0 && !Hidden(r.Name)).Select(r => Short(r.Name) + " " + (Math.Abs(r.Amount - r.Max) < 0.01 * Math.Max(1, r.Max) ? N(r.Max * count, "0.#") : N(r.Amount * count, "0.#") + "/" + N(r.Max * count, "0.#"))).ToArray());
        }

        // ---------------------------------------------------------------- power

        /// <summary>The power budget at <paramref name="there"/> (the destination), judged in both directions.</summary>
        public PowerReport Power(EngBody home, EngBody there, IList<EngBody> bodies) { return Power(home, there, bodies, -1); }

        /// <summary><paramref name="crew"/> is how many are aboard, or -1 to count every seat as filled.</summary>
        public PowerReport Power(EngBody home, EngBody there, IList<EngBody> bodies, int crew)
        {
            var r = new PowerReport();
            r.Battery = Max(EC);
            double sunScale = 1;
            if (home != null && there != null && home.SunDistance > 0 && there.SunDistance > 0) sunScale = (home.SunDistance / there.SunDistance) * (home.SunDistance / there.SunDistance);
            r.Where = there != null ? there.Name : home != null ? home.Name : "";
            bool away = there != null && home != null && there.Name != home.Name && Math.Abs(sunScale - 1) > 0.02;

            foreach (var g in PartGroups(p => p.Modules.Any(m => m.Kind == "solar" && m.N("rate") > 0)))
            {
                double rate = g.Sum(p => p.Of("solar").Sum(m => m.N("rate")));
                r.SolarHome += rate;
                r.Sources.Add(new SheetLine(Label(g) + (g[0].Of("solar").Any(m => m.N("tracking") <= 0) ? " (fixed, full output only when facing the sun)" : ""), Ec(rate) + (home != null ? " at " + home.Name : ""), false, g[0].Mod));
            }
            r.SolarThere = r.SolarHome * sunScale;
            if (away && r.SolarHome > 0) r.Sources.Add(new SheetLine("The same panels at " + there.Name, Ec(r.SolarThere)));

            foreach (var g in PartGroups(p => Makers(p).Any()))
            {
                var first = Makers(g[0]).ToList();
                foreach (var m in first)
                {
                    double rate = g.Sum(p => Makers(p).Where(x => x.Name == m.Name && x.Label == m.Label).Sum(x => x.Rate(EC)));
                    var inputs = m.Rates.Where(x => x.PerSec < 0 && x.Res != EC).ToList();
                    if (m.Kind == "alternator") { r.Alternator += rate; r.Sources.Add(new SheetLine(Label(g) + " alternator, only while the engine burns", Ec(rate), false, g[0].Mod)); }
                    else if (inputs.Count > 0)
                    {
                        r.FuelCell += rate;
                        // How long the fuel lasts at full output.
                        double life = double.MaxValue;
                        foreach (var i in inputs) { double have = Amount(i.Res); double use = -i.PerSec * g.Count; if (use > 0) life = Math.Min(life, have / use); }
                        r.Sources.Add(new SheetLine(Label(g) + (m.Label != "" ? " \"" + m.Label + "\"" : "") + ", burning " + string.Join(" and ", inputs.Select(i => Short(i.Res)).ToArray()), Ec(rate) + (life < double.MaxValue ? " for " + EngMath.Dur(life) : ""), life < 600, g[0].Mod));
                    }
                    else { r.Steady += rate; r.Sources.Add(new SheetLine(Label(g) + ", constant", Ec(rate), false, g[0].Mod)); }
                }
            }
            if (r.Battery > 0)
            {
                var bats = Parts.Where(p => p.Max(EC) > 0).ToList();
                r.Sources.Add(new SheetLine("Batteries (" + bats.Count + " part" + (bats.Count == 1 ? "" : "s") + " holding charge)", N(r.Battery) + " EC"));
            }

            // Demand. Each consumer goes in one bucket by what its module is.
            double biggestTx = 0; string txName = "";
            foreach (var g in PartGroups(p => p.Modules.Any(m => Draw(m) > 0)))
            {
                foreach (var kind in g[0].Modules.Where(m => Draw(m) > 0).Select(m => m.Kind + "|" + m.Name + "|" + m.Label).Distinct().ToList())
                {
                    var m0 = g[0].Modules.First(m => m.Kind + "|" + m.Name + "|" + m.Label == kind);
                    double rate = g.Sum(p => p.Modules.Where(m => m.Kind + "|" + m.Name + "|" + m.Label == kind).Sum(m => Draw(m)));
                    string what;
                    if (m0.Kind == "command") { r.Always += rate; what = Label(g) + ", always on"; }
                    else if (m0.Kind == "wheel") { r.Wheels += rate; what = Label(g) + ", at full torque"; }
                    else if (m0.Kind == "antenna")
                    {
                        double one = Draw(m0);
                        if (one > biggestTx) { biggestTx = one; txName = g[0].Title; }
                        what = Label(g) + ", only while sending";
                    }
                    else
                    {
                        r.Working += rate;
                        what = Label(g) + (m0.Kind == "other" ? " (" + (m0.Label != "" ? m0.Label : m0.Name) + ")" : m0.Label != "" ? " \"" + m0.Label + "\"" : "") + ", while running";
                    }
                    r.Demand.Add(new SheetLine(what, Ec(rate), false, g[0].Mod));
                }
            }
            r.Transmit = biggestTx;
            ModKnowledge.AddPower(this, r, crew >= 0 ? crew : Parts.Sum(p => p.Crew));
            // A mod that keeps its own hidden resources (Kerbalism does) runs processes whose draw is not declared to the game.
            r.Unknown = Processes.Count == 0 && Parts.Any(p => p.Res.Any(x => Hidden(x.Name)));

            // The verdict at the destination.
            var b = there ?? home;
            if (b != null && b.Radius > 0 && b.Mu > 0 && !b.IsStar)
            {
                double rr = b.Radius + DvPlanner.LowOrbitAlt(b);
                double period = 2 * Math.PI * Math.Sqrt(rr * rr * rr / b.Mu);
                r.ShadowSeconds = period * Math.Asin(Math.Min(1, b.Radius / rr)) / Math.PI;
            }
            string at = r.Where != "" ? " at " + r.Where : "";
            double gen = r.GenThere, coast = r.Coast, work = r.Coast + r.Working;
            bool anyControl = Count("command") > 0;
            if (!anyControl && r.Demand.Count == 0) { r.Rating = "nothing draws power"; return r; }
            foreach (var note in r.Notes) r.There.Add(new SheetLine(note));

            r.There.Add(new SheetLine("Coasting" + at + ": makes " + Ec(gen).Replace(" EC/s", "") + ", uses " + Ec(coast).Replace(" EC/s", ""), Signed(gen - coast), gen < coast));
            if (r.Working > 0) r.There.Add(new SheetLine("Everything running: makes " + Ec(gen).Replace(" EC/s", "") + ", uses " + Ec(work).Replace(" EC/s", ""), Signed(gen - work), gen < work));
            double worst = Math.Max(coast, work);
            if (gen < worst && r.Battery > 0 && worst - gen > 0) r.There.Add(new SheetLine("Batteries empty in sunlight after", EngMath.Dur(r.Battery / (worst - gen)), true));
            if (r.SolarThere > 0 && r.ShadowSeconds > 0)
            {
                // In shadow only the constant sources work.
                double dark = Math.Max(0, worst - r.Steady);
                r.ShadowNeed = dark * r.ShadowSeconds;
                bool bad = r.ShadowNeed > r.Battery, tight = !bad && r.ShadowNeed > r.Battery * 0.8;
                r.There.Add(new SheetLine(EngMath.Dur(r.ShadowSeconds) + " in " + r.Where + "'s shadow each low orbit needs" + (tight ? " (tight)" : ""), N(r.ShadowNeed) + " of " + N(r.Battery) + " EC", bad));
            }
            if (r.Transmit > 0)
            {
                double net = gen - coast - r.Transmit;
                var ant = All("antenna").OrderByDescending(m => m.N("ec")).First();
                string mits = ant.N("ec") > 0 && ant.N("mits") > 0 && net < 0 ? N(r.Battery / -net * ant.N("mits")) + " Mits per full charge" : "no limit";
                r.There.Add(new SheetLine("Sending science with the " + txName + " (" + Ec(r.Transmit) + ")", net < 0 ? mits : "covered", net < 0 && r.Battery < r.Transmit * 5));
            }
            if (r.FuelCell > 0) r.There.Add(new SheetLine("Fuel cells can add (while their fuel lasts)", Ec(r.FuelCell)));
            bool enginesShort = false;
            if (r.Engines > 0)
            {
                // Electric engines burn charge as propellant: what generation cannot cover comes out of thrust.
                double spare = Math.Max(0, gen - coast), share = Math.Min(1, spare / r.Engines);
                double store = r.Battery + r.BurstStore, gap = r.Engines - spare;
                string how = share >= 0.999 ? "covered at full thrust" : "generation covers " + N(share * 100) + "% of full thrust" + (store > 0 && gap > 0 ? "; stored charge carries a full-thrust burn for " + EngMath.Dur(store / gap) : "");
                enginesShort = share < 0.5 && (store <= 0 || store / Math.Max(gap, 1e-9) < 120);
                r.There.Add(new SheetLine("Electric engines at full thrust need " + Ec(r.Engines), how, enginesShort));
                if (share < 0.999) r.Fixes.Add(new SheetLine("To run the electric engines flat out, generation" + at + " has to reach", Ec(coast + r.Engines)) { Fix = true });
            }
            if (r.Critical > 0 && r.SolarThere > 0 && r.Steady < r.Critical)
                r.There.Add(new SheetLine("Antimatter containment (" + Ec(r.Critical) + ") depends on batteries in shadow: if they run flat the antimatter is lost. A constant source (RTG or reactor) covering it is far safer.", "", r.ShadowSeconds > 0 && r.Battery < r.Critical * r.ShadowSeconds * 2));
            if (r.Unknown) r.There.Add(new SheetLine("A mod on this craft runs its own processes (life support and the like) whose power draw is not read yet, so real demand is higher than shown.", "", false));

            // Rating, both ways.
            if (worst <= 0) r.Rating = "nothing draws power";
            else if (gen < coast) r.Rating = "short";
            else if (gen < worst) r.Rating = "short when everything runs";
            else if (r.ShadowNeed > r.Battery) r.Rating = "short in shadow";
            else if (enginesShort) r.Rating = "short for the electric engines";
            else if (gen < worst * 1.2) r.Rating = "tight";
            else if (r.Unknown) r.Rating = "enough for what could be read";
            else if (gen > worst * 3 && gen - worst > 0.5) r.Rating = "oversized";
            else r.Rating = "right-sized";

            Fixes(r, home, there, sunScale, worst, crew);
            return r;
        }

        /// <summary>What the power rating becomes with some copies of a part added or removed.</summary>
        string Then(string partName, int delta, EngBody home, EngBody there, int crew)
        {
            if (Trial) return "";
            try { return ", then: " + EngTools.With(this, partName, delta).Power(home, there, null, crew).Rating; } catch { return ""; }
        }

        void Fixes(PowerReport r, EngBody home, EngBody there, double sunScale, double worst, int crew)
        {
            var panel = Parts.Where(p => p.Has("solar")).GroupBy(p => p.Name).OrderByDescending(g => g.Count()).Select(g => g.First()).FirstOrDefault();
            double gen = r.GenThere;
            string at = r.Where != "" ? " at " + r.Where : "";
            if (gen < worst)
            {
                double need = worst * 1.2 - gen;
                if (panel != null)
                {
                    double one = panel.Of("solar").Sum(m => m.N("rate")) * sunScale;
                    if (one > 0) { int k = (int)Math.Ceiling(need / one); r.Fixes.Add(new SheetLine("Add " + k + " more " + panel.Title, "+" + Ec(k * one) + at + Then(panel.Name, k, home, there, crew)) { Fix = true }); }
                }
                else r.Fixes.Add(new SheetLine("Add a power source making at least", Ec(worst * 1.2) + at) { Fix = true });
                if (sunScale < 0.15 && r.SolarHome > 0) r.Fixes.Add(new SheetLine("Sunlight" + at + " is " + N(sunScale * 100, "0.#") + "% of home. An RTG or reactor is the better source this far out.", "") { Fix = true });
            }
            if (r.ShadowNeed > r.Battery * 0.8 && r.ShadowSeconds > 0)
            {
                double want = r.ShadowNeed * 1.3;
                var bat = Parts.Where(p => p.Max(EC) > 0 && !p.Has("command")).GroupBy(p => p.Name).OrderByDescending(g => g.Count()).Select(g => g.First()).FirstOrDefault();
                if (bat != null) { int k = (int)Math.Ceiling((want - r.Battery) / bat.Max(EC)); if (k > 0) r.Fixes.Add(new SheetLine("Add " + k + " more " + bat.Title, "shadow needs " + N(r.ShadowNeed) + " of " + N(r.Battery + k * bat.Max(EC)) + " EC" + Then(bat.Name, k, home, there, crew)) { Fix = true }); }
                else r.Fixes.Add(new SheetLine("Add batteries to reach", N(want) + " EC") { Fix = true });
            }
            if (r.Rating == "oversized" && panel != null)
            {
                // How many panels could go and still cover the worst case with 30% to spare.
                var panels = Parts.Where(p => p.Has("solar")).OrderBy(p => p.Of("solar").Sum(m => m.N("rate"))).ToList();
                double spare = gen - worst * 1.3, mass = 0, cost = 0; int k = 0;
                foreach (var p in panels)
                {
                    double one = p.Of("solar").Sum(m => m.N("rate")) * sunScale;
                    if (one <= 0 || one > spare) break;
                    spare -= one; mass += p.DryMass; cost += p.Cost; k++;
                }
                if (k > 0) r.Fixes.Add(new SheetLine("You make " + Ec(gen) + at + " and never use more than " + Ec(worst) + ". " + k + " panel" + (k == 1 ? "" : "s") + " could go.", "saves " + N(mass, "0.###") + " t" + (cost > 0 ? ", " + N(cost) + " funds" : "")) { Fix = true });
            }
            if (r.Battery > 0 && r.ShadowSeconds > 0 && r.ShadowNeed > 0 && r.Battery > r.ShadowNeed * 6 && r.Battery > 1000 && r.Transmit * 60 < r.Battery / 3)
                r.Fixes.Add(new SheetLine("Batteries hold " + N(r.Battery) + " EC but a pass through shadow needs " + N(r.ShadowNeed) + ". Some could go.", "") { Fix = true });
        }

        static string Signed(double v) { return (v >= 0 ? "+" : "−") + Ec(Math.Abs(v)); }
        /// <summary>Modules that make power other than solar panels. A launch clamp's generator stays on the pad, so it does not count.</summary>
        static IEnumerable<CraftModule> Makers(CraftPart p) { return p.Has("clamp") ? Enumerable.Empty<CraftModule>() : p.Modules.Where(m => m.Kind != "solar" && m.Rate(EC) > 0); }
        /// <summary>EC per second a module uses when it runs.</summary>
        static double Draw(CraftModule m) { return m.Kind == "antenna" ? m.N("ec") : Math.Max(0, -m.Rate(EC)); }

        List<List<CraftPart>> PartGroups(Func<CraftPart, bool> where)
        {
            return Parts.Where(where).GroupBy(p => p.Name).Select(g => g.ToList()).ToList();
        }
        static string Label(List<CraftPart> g) { return (g.Count > 1 ? g.Count + " × " : "") + g[0].Title; }

        // ---------------------------------------------------------------- staging

        /// <summary>What fires in each stage, highest (first) to lowest.</summary>
        public List<StageEvents> Stages(IList<EngStage> dv)
        {
            var list = new List<StageEvents>();
            for (int k = MaxStage; k >= 0; k--)
            {
                var ev = new StageEvents { Stage = k, Dv = dv == null ? null : dv.FirstOrDefault(x => x.Stage == k) };
                var here = Parts.Where(p => p.Stage == k).ToList();
                Add(ev, here.Where(p => p.Modules.Any(m => m.Kind == "engine" && m.Staged)), "ignite", "ignites");
                Add(ev, here.Where(p => p.Modules.Any(m => m.Kind == "clamp" && m.Staged)), "release", "releases");
                Add(ev, here.Where(p => p.Modules.Any(m => m.Kind == "decoupler" && m.Staged)), "fire", "fires");
                Add(ev, here.Where(p => p.Modules.Any(m => m.Kind == "fairing" && m.Staged)), "open", "opens");
                Add(ev, here.Where(p => p.Modules.Any(m => m.Kind == "chute" && m.Staged)), "deploy", "deploys");
                list.Add(ev);
            }
            return list;
        }

        static void Add(StageEvents ev, IEnumerable<CraftPart> parts, string many, string one)
        {
            foreach (var g in parts.GroupBy(p => p.Title))
                ev.Fires.Add((g.Count() > 1 ? g.Count() + " × " : "") + g.Key + " " + (g.Count() > 1 ? many : one));
        }

        /// <summary>Likely staging mistakes. Lines starting with "!" are real problems.</summary>
        public List<string> StagingIssues(IList<EngStage> dv)
        {
            var r = new List<string>();
            if (Parts.Count == 0) return r;
            Func<CraftPart, bool> eng = p => p.Modules.Any(m => m.Kind == "engine" && m.Staged);
            var engines = Parts.Where(eng).ToList();
            var chutes = Parts.Where(p => p.Modules.Any(m => m.Kind == "chute" && m.Staged)).ToList();
            var clamps = Parts.Where(p => p.Modules.Any(m => m.Kind == "clamp" && m.Staged)).ToList();
            int first = engines.Count > 0 ? engines.Max(p => p.Stage) : -1;

            foreach (var g in engines.Where(p => p.DropStage > p.Stage).GroupBy(p => p.Title + "|" + p.Stage + "|" + p.DropStage))
                r.Add("! " + Label(g.ToList()) + " ignite" + (g.Count() == 1 ? "s" : "") + " in stage " + g.First().Stage + " but " + (g.Count() == 1 ? "is" : "are") + " already thrown away in stage " + g.First().DropStage + ".");
            foreach (var g in engines.Where(p => p.DropStage == p.Stage && p.DropStage >= 0).GroupBy(p => p.Title + "|" + p.Stage))
                r.Add("! " + Label(g.ToList()) + " ignite" + (g.Count() == 1 ? "s" : "") + " in stage " + g.First().Stage + ", the same stage that drops " + (g.Count() == 1 ? "it" : "them") + ". Move the decoupler one stage later.");

            // An engine lit with the stage below still bolted on: its exhaust hits whatever is attached under it.
            foreach (var e in engines)
            {
                var under = e.Children.Select(i => Parts[i]).Where(c => !c.Surface && (c.DropStage < e.Stage) && (IsDecoupler(c) || c.Children.Count > 0 || c.DryMass + c.ResMass > 0.2)).ToList();
                if (under.Count == 0) continue;
                var c0 = under[0];
                r.Add("! " + e.Title + " ignites in stage " + e.Stage + " but the " + c0.Title + " under it " + (c0.DropStage >= 0 ? "is not dropped until stage " + c0.DropStage : "is never dropped") + ", so it fires into the stage below.");
            }

            foreach (var g in chutes.GroupBy(p => p.Stage))
            {
                var c = g.First();
                var later = engines.Where(e => e.Stage < c.Stage && e.DropStage < c.Stage && c.DropStage < e.Stage).ToList();
                var same = engines.Where(e => e.Stage == c.Stage && c.DropStage < c.Stage).ToList();
                if (same.Count > 0) r.Add("! Parachutes deploy in stage " + c.Stage + " together with the " + same[0].Title + ". They will tear off or burn.");
                else if (later.Count > 0) r.Add("! Parachutes deploy in stage " + c.Stage + ", before the " + later[0].Title + " ignites in stage " + later[0].Stage + ". Move them to the last stage.");
            }

            foreach (var g in clamps.GroupBy(p => p.Stage))
            {
                if (first < 0) break;
                if (g.Key > first) r.Add("! Launch clamps release in stage " + g.Key + ", before the engines ignite in stage " + first + ". The rocket drops onto the pad.");
                else if (g.Key < first - 1) r.Add("! Launch clamps do not release until stage " + g.Key + ", but the engines ignite in stage " + first + ".");
            }

            // Engines that need something the craft does not carry at all.
            foreach (var g in engines.GroupBy(p => p.Name))
            {
                foreach (var m in g.First().Of("engine"))
                {
                    var missing = m.T("props").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim())
                        .Where(x => x != "" && x != "IntakeAir" && x != "IntakeAtm" && x != EC && Max(x) <= 0).ToList();
                    if (missing.Count > 0) { r.Add("! " + g.First().Title + " burns " + string.Join(" and ", missing.ToArray()) + ", and there is none aboard."); break; }
                }
            }
            if (dv != null && dv.Count > 0)
                foreach (var k in engines.Select(p => p.Stage).Distinct().OrderByDescending(x => x))
                {
                    var st = dv.FirstOrDefault(x => x.Stage == k);
                    bool laterAlso = dv.Any(x => x.Stage < k && x.DvVac > 1);
                    if ((st == null || st.DvVac < 1) && !r.Any(x => x.Contains("none aboard")) && engines.Where(e => e.Stage == k).All(e => e.DropStage < k))
                        r.Add("! The engines in stage " + k + " show no delta-v: they have no fuel they can reach" + (laterAlso ? ", or a later stage is using the same tanks" : "") + ". Check fuel lines and crossfeed.");
                }

            // Dead weight: engines that burn out and are never dropped while later engines exist.
            foreach (var g in engines.Where(e => e.DropStage < 0 && e.Modules.Any(m => m.Kind == "engine" && m.N("locked") > 0) && engines.Any(o => o.Stage < e.Stage)).GroupBy(p => p.Title))
                r.Add(Label(g.ToList()) + (g.Count() == 1 ? " is a solid booster that is" : " are solid boosters that are") + " never dropped: dead weight for every later burn.");

            var staged = new HashSet<int>(Parts.Where(p => p.Modules.Any(m => m.Staged)).Select(p => p.Stage));
            for (int k = MaxStage; k >= 0; k--) if (!staged.Contains(k)) r.Add("Stage " + k + " is empty: pressing space there does nothing.");

            foreach (var g in Parts.Where(p => p.Modules.Any(m => m.Kind == "fairing" && !m.Staged)).GroupBy(p => p.Title))
                r.Add(Label(g.ToList()) + " has staging turned off, so it will not open with the stages.");
            foreach (var g in Parts.Where(p => p.Modules.Any(m => m.Kind == "decoupler" && !m.Staged)).GroupBy(p => p.Title))
                r.Add(Label(g.ToList()) + " has staging turned off: it only fires by hand.");
            return r;
        }

        // ---------------------------------------------------------------- missing parts

        /// <summary>Things the craft lacks, beyond the basic review. Lines starting with "!" are problems.</summary>
        public List<string> Missing(bool crewed, bool landing, bool docking)
        {
            var r = new List<string>();
            if (Parts.Count == 0) return r;
            bool torque = All("wheel").Any(m => m.N("torque") > 0) || Parts.Any(p => p.Has("command") && p.Has("wheel"));
            bool rcs = Count("rcs") > 0, gimbal = Parts.Any(p => p.Has("gimbal")), fins = Count("wing") > 0;
            if (Count("command") > 0 && !torque && !rcs)
                r.Add((gimbal || fins ? "" : "! ") + "No reaction wheels or RCS: the craft can only turn " + (gimbal ? "while an engine with gimbal is burning" : fins ? "with fins, in the air" : "not at all") + ".");
            if (rcs && Max("MonoPropellant") <= 0 && All("rcs").Any(m => m.T("props").Contains("MonoPropellant"))) r.Add("! RCS thrusters are fitted but there is no monopropellant aboard.");
            if (docking && Count("dock") == 0) r.Add("! No docking port, and the mission includes docking.");
            if (docking && Count("dock") > 0 && !rcs) r.Add("A docking port but no RCS: docking with the main engine alone is hard.");
            if (landing && crewed && !Parts.Any(p => p.Title.ToLowerInvariant().Contains("ladder") || p.Modules.Any(m => m.Name.Contains("Ladder")))) r.Add("No ladder: the crew may not be able to climb back in after a surface walk.");
            if (Count("science") == 0 && Count("lab") == 0) r.Add("No science experiments aboard.");
            foreach (var g in Parts.Where(p => p.Modules.Any(m => m.Kind == "solar" && m.N("retracted") > 0)).GroupBy(p => p.Title).Take(1))
                r.Add("Solar panels start retracted: remember to extend them (an action group helps).");
            return r;
        }

        // ---------------------------------------------------------------- text for the AI

        /// <summary>
        /// The craft as compact text: stage by stage, then the parts grouped by type. A 300-part craft is about the size of
        /// a 60-part one because identical parts fold into one line. <paramref name="question"/> picks the parts whose
        /// full tooltip is added, so detail is only spent where the director is asking.
        /// </summary>
        public string Text(IList<EngStage> dv, string question, int maxChars = 9000)
        {
            var sb = new StringBuilder();
            var groups = Groups();
            sb.AppendLine("CRAFT SHEET (read live from the " + (Flying ? "flying craft" : "vehicle builder") + "; this is the complete parts list): " + Parts.Count + " parts, " + groups.Count + " different lines.");
            sb.AppendLine("Stages (the highest number fires first):");
            foreach (var ev in Stages(dv))
            {
                var carried = Parts.Where(p => p.DropStage == ev.Stage).ToList();
                sb.Append("- Stage " + ev.Stage + ": " + (ev.Fires.Count > 0 ? string.Join(", ", ev.Fires.ToArray()) : "nothing staged"));
                if (carried.Count > 0) sb.Append(". Drops " + carried.Count + " part" + (carried.Count == 1 ? "" : "s") + ", " + N(carried.Sum(p => p.DryMass + p.ResMass), "0.0#") + " t");
                sb.AppendLine(".");
            }
            var everywhere = Everywhere();
            var asked = Mentioned(question).Take(4).ToList();
            if (everywhere.Count > 0) sb.AppendLine("Modules that sit on most parts and are left off the lines below: " + string.Join(", ", everywhere.ToArray()) + ".");
            sb.AppendLine("Parts (count × name [mod], stage it activates in if staged, when it is dropped; what it holds; what it does):");
            int shown = 0;
            foreach (var g in groups)
            {
                var p = g.First;
                var line = new StringBuilder("- " + (g.Count > 1 ? g.Count + " × " : "") + p.Title + (p.Mod != "" && p.Mod != "stock" ? " [" + p.Mod + "]" : ""));
                if (p.Modules.Any(m => m.Staged)) line.Append(", stage " + p.Stage);
                line.Append(p.DropStage >= 0 ? ", dropped in stage " + p.DropStage : ", stays to the end");
                line.Append(", " + N(g.Mass, "0.0##") + " t");
                string res = ResText(p.Res, g.Count);
                if (res != "") line.Append("; holds " + res);
                bool wanted = asked.Any(a => a.Name == p.Name);
                string does = Does(p, everywhere, !wanted);
                int cap = wanted ? 700 : 300;
                if (does != "") line.Append("; " + (does.Length > cap ? does.Substring(0, cap) + "..." : does));
                if (sb.Length + line.Length > maxChars - 600) { sb.AppendLine("- ...and " + (groups.Count - shown) + " more lines of smaller parts (" + N(groups.Skip(shown).Sum(x => x.Mass), "0.0#") + " t in total)."); break; }
                sb.AppendLine(line.ToString());
                shown++;
            }
            var totals = Parts.SelectMany(p => p.Res).Where(x => x.Max > 0 && !Hidden(x.Name)).GroupBy(x => x.Name).Select(x => Short(x.Key) + " " + N(x.Sum(y => y.Amount), "0.#") + "/" + N(x.Sum(y => y.Max), "0.#")).ToArray();
            if (totals.Length > 0) sb.AppendLine("Resources aboard (amount/capacity): " + string.Join(", ", totals) + ".");

            // Tooltips for the parts the question is about.
            foreach (var p in asked)
            {
                if (p.Info.Count == 0) continue;
                string info = string.Join(" | ", p.Info.ToArray());
                sb.AppendLine("Detail for " + p.Title + ": " + (info.Length > 700 ? info.Substring(0, 700) + "..." : info));
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>Part types the text names, by title words or by what they do.</summary>
        public IEnumerable<CraftPart> Mentioned(string question)
        {
            string q = " " + (question ?? "").ToLowerInvariant() + " ";
            if (q.Trim() == "") yield break;
            var kinds = new Dictionary<string, string[]>
            {
                { "engine", new[] { "engine", "motor", "thrust", "isp" } }, { "solar", new[] { "solar", "panel" } }, { "antenna", new[] { "antenna", "dish", "signal", "comm", "relay" } },
                { "generator", new[] { "rtg", "generator", "reactor" } }, { "converter", new[] { "fuel cell", "converter", "isru" } }, { "drill", new[] { "drill", "mining" } },
                { "chute", new[] { "parachute", "chute" } }, { "wheel", new[] { "reaction wheel", "torque" } }, { "radiator", new[] { "radiator", "heat", "cooling" } },
                { "dock", new[] { "dock" } }, { "science", new[] { "experiment", "science" } }, { "ablator", new[] { "heat shield", "heatshield", "reentry" } },
            };
            var done = new HashSet<string>();
            foreach (var p in Parts)
            {
                if (done.Contains(p.Name)) continue;
                string t = p.Title.ToLowerInvariant();
                bool hit = t.Length >= 3 && q.Contains(t);
                if (!hit) hit = t.Split(new[] { ' ', '-', '"', '(', ')' }, StringSplitOptions.RemoveEmptyEntries).Any(w => w.Length >= 5 && q.Contains(" " + w));
                if (!hit) hit = kinds.Any(k => p.Has(k.Key) && k.Value.Any(w => q.Contains(w)));
                if (!hit) hit = p.Modules.Any(m => m.Kind == "other" && m.Name.Length >= 6 && q.Contains(m.Name.ToLowerInvariant()));
                if (hit) { done.Add(p.Name); yield return p; }
            }
        }

        public static string PowerText(PowerReport r)
        {
            if (r == null) return "";
            var sb = new StringBuilder("POWER BUDGET (computed, trust these numbers; rating: " + r.Rating + "):\n");
            foreach (var l in r.Sources) sb.AppendLine("- Source: " + l.Text + ": " + l.Value);
            foreach (var l in r.Demand) sb.AppendLine("- Demand: " + l.Text + ": " + l.Value);
            sb.AppendLine("- Reaction wheels are counted at " + N(WheelHold * 100) + "% of full power while coasting.");
            foreach (var l in r.There) sb.AppendLine("- " + (l.Bad ? "! " : "") + l.Text + (l.Value != "" ? ": " + l.Value : ""));
            foreach (var l in r.Fixes) sb.AppendLine("- Fix: " + l.Text + (l.Value != "" ? " (" + l.Value + ")" : ""));
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// Delta-v budgets worked out from the bodies in the save (gravity, radius, atmosphere, orbits), so they hold for
    /// rescaled systems and planet packs. Patched conics and Hohmann transfers: first estimates, good to about 10%.
    /// </summary>
    public static class DvPlanner
    {
        public static double LowOrbitAlt(EngBody b)
        {
            if (b == null) return 0;
            return b.AtmoDepth > 0 ? b.AtmoDepth + Math.Max(10000, b.Radius * 0.0167) : Math.Max(10000, b.Radius * 0.05);
        }

        static double LowR(EngBody b) { return b.Radius + LowOrbitAlt(b); }
        public static double OrbitSpeed(EngBody b) { return Math.Sqrt(b.Mu / LowR(b)); }

        /// <summary>Surface to low orbit, with gravity and drag losses, launching with the body's spin.</summary>
        public static double Ascent(EngBody b)
        {
            if (b == null || b.Mu <= 0 || b.Radius <= 0 || !b.HasSurface) return 0;
            double h = LowOrbitAlt(b), r = b.Radius + h, v = Math.Sqrt(b.Mu / r);
            double g = b.Gravity > 0 ? b.Gravity : b.Mu / (b.Radius * b.Radius);
            double ideal = Math.Sqrt(v * v + 2 * g * h * b.Radius / r);
            double loss = 0;
            if (b.AtmoDepth > 0)
            {
                double p = Math.Max(0, b.AtmoPressure);
                loss = g * b.AtmoDepth / 680 * (p > 1 ? Math.Pow(p, 0.35) : 1);
            }
            double spin = b.RotPeriod > 0 ? 2 * Math.PI * b.Radius / b.RotPeriod : 0;
            if (spin > ideal * 0.3) spin = 0;
            return ideal + loss - spin;
        }

        /// <summary>Low orbit to the surface under power (no air), or just the deorbit burn where parachutes do the rest.</summary>
        public static double Landing(EngBody b)
        {
            if (b == null || b.Mu <= 0 || !b.HasSurface) return 0;
            double h = LowOrbitAlt(b), r = b.Radius + h, v = Math.Sqrt(b.Mu / r);
            double g = b.Gravity > 0 ? b.Gravity : b.Mu / (b.Radius * b.Radius);
            if (b.AtmoDepth > 0 && b.AtmoPressure >= 0.05) return Math.Max(30, v * 0.03);
            return Math.Sqrt(v * v + 2 * g * h * b.Radius / r);
        }

        /// <summary>Burn from low orbit to leave (or arrive) with the given speed at infinity.</summary>
        public static double Eject(EngBody b, double vInf)
        {
            double r = LowR(b);
            return Math.Sqrt(vInf * vInf + 2 * b.Mu / r) - Math.Sqrt(b.Mu / r);
        }

        /// <summary>Seconds a Hohmann transfer takes between two circular orbits.</summary>
        public static double HalfPeriod(double mu, double r1, double r2) { double a = (r1 + r2) / 2; return mu > 0 ? Math.PI * Math.Sqrt(a * a * a / mu) : 0; }

                public static EngBody Find(IList<EngBody> bodies, string name) { return bodies == null ? null : bodies.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)); }

        /// <summary>Legs from low orbit at home to low orbit at the target and back, or null when the route is not one this handles.</summary>
        public static EngMissions.Dest Route(IList<EngBody> bodies, EngBody home, EngBody target)
        {
            if (bodies == null || home == null || target == null || target.IsStar || target.Name == home.Name || home.Mu <= 0 || target.Mu <= 0) return null;
            var d = new EngMissions.Dest { Name = target.Name, Gravity = target.Gravity, Aero = target.AtmoDepth > 0, NoSurface = !target.HasSurface };
            bool homeAir = home.AtmoDepth > 0;
            if (string.Equals(target.Parent, home.Name, StringComparison.OrdinalIgnoreCase))
            {
                double dv1, dv2;
                EngMath.Hohmann(home.Mu, LowR(home), target.Sma, out dv1, out dv2);
                d.Transfer = dv1;
                d.Travel = HalfPeriod(home.Mu, LowR(home), target.Sma);
                d.Capture = Eject(target, dv2);
                d.Return = d.Capture + (homeAir ? 0 : dv1);
            }
            else
            {
                var parent = Find(bodies, home.Parent);
                var planet = string.Equals(target.Parent, home.Parent, StringComparison.OrdinalIgnoreCase) ? target : Find(bodies, target.Parent);
                if (parent == null || planet == null || planet.Name == home.Name || !string.Equals(planet.Parent, home.Parent, StringComparison.OrdinalIgnoreCase) || home.Sma <= 0 || planet.Sma <= 0) return null;
                double v1, v2;
                EngMath.Hohmann(parent.Mu, home.Sma, planet.Sma, out v1, out v2);
                d.Transfer = Eject(home, v1);
                d.Travel = HalfPeriod(parent.Mu, home.Sma, planet.Sma);
                double back;
                if (planet == target) { d.Capture = Eject(target, v2); back = d.Capture; }
                else
                {
                    // Brake at the planet into an ellipse reaching the moon, then capture at the moon.
                    double rp = LowR(planet), rm = target.Sma;
                    double hyp = Math.Sqrt(v2 * v2 + 2 * planet.Mu / rp);
                    double ell = Math.Sqrt(planet.Mu * (2 / rp - 2 / (rp + rm)));
                    double atMoon = Math.Abs(Math.Sqrt(planet.Mu / rm) - ell * rp / rm);
                    d.Capture = Math.Max(0, hyp - ell) + Eject(target, atMoon);
                    d.Aero = planet.AtmoDepth > 0 || target.AtmoDepth > 0;
                    back = d.Capture;
                }
                d.Return = back + (homeAir ? 0 : Eject(home, v1));
            }
            if (target.HasSurface) { d.Land = Landing(target); d.Ascent = Ascent(target); }
            d.Computed = true;
            return d;
        }

        /// <summary>A short reference table for the prompt: the worlds reachable from home with their main legs.</summary>
        public static string Table(IList<EngBody> bodies, EngBody home, int max = 14)
        {
            if (bodies == null || home == null) return "";
            var sb = new StringBuilder("Delta-v reference computed for this system (m/s, from a " + EngMath.Dist(LowOrbitAlt(home)) + " orbit of " + home.Name + ", approximate): launch from " + home.Name + " to low orbit " + EngMath.N(Ascent(home)) + ".");
            int n = 0;
            foreach (var b in bodies.Where(x => !x.IsStar && x.Name != home.Name).OrderBy(x => x.Parent == home.Name ? 0 : x.Parent == home.Parent ? 1 : 2).ThenBy(x => x.Sma))
            {
                var d = Route(bodies, home, b);
                if (d == null) continue;
                sb.Append(" " + b.Name + ": transfer " + EngMath.N(d.Transfer) + ", capture " + EngMath.N(d.Capture) + (d.Aero ? " (less with aerobraking)" : "") + (b.HasSurface ? ", landing " + EngMath.N(d.Land) + (b.AtmoDepth > 0 && b.AtmoPressure >= 0.05 ? " (parachutes)" : "") + ", ascent " + EngMath.N(d.Ascent) : ", no surface") + ", return " + EngMath.N(d.Return) + ".");
                if (++n >= max) break;
            }
            return sb.ToString();
        }
    }
}
