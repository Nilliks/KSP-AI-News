using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>The result of one computed check: a rating, the lines behind it, and suggested fixes.</summary>
    public class CheckReport
    {
        public string Key = "", Title = "", Rating = "", Note = "";
        public List<SheetLine> Lines = new List<SheetLine>(), Fixes = new List<SheetLine>();
        public int Problems { get { return Lines.Count(l => l.Bad); } }
        public bool Bad { get { return Problems > 0; } }

        public CheckReport Add(string text, string value = "", bool bad = false, string tag = "") { Lines.Add(new SheetLine(text, value, bad, tag)); return this; }
        public CheckReport Fix(string text, string value = "") { Fixes.Add(new SheetLine(text, value) { Fix = true }); return this; }

        public string Text()
        {
            var sb = new StringBuilder(Title.ToUpperInvariant() + " (computed; rating: " + Rating + "):\n");
            foreach (var l in Lines) sb.AppendLine("- " + (l.Bad ? "! " : "") + l.Text + (l.Value != "" ? ": " + l.Value : ""));
            foreach (var l in Fixes) sb.AppendLine("- Fix: " + l.Text + (l.Value != "" ? " (" + l.Value + ")" : ""));
            if (Note != "") sb.AppendLine("- Note: " + Note);
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>What the team is told about a mod when its parts are on the craft: how it works and what to check.</summary>
    public class ModBook
    {
        public string Title = "", Text = "";
        /// <summary>GameData folder names the mod's parts come from.</summary>
        public string[] Folders = new string[0];
        /// <summary>Module class name prefixes that mean the mod is at work on a part (it may patch stock parts).</summary>
        public string[] Modules = new string[0];
        /// <summary>A mod fact that must be present (for mods that work without parts of their own).</summary>
        public string Fact = "";
    }

    /// <summary>
    /// Mod knowledge: written handbooks, heat loop sums, and the power that mods add or use in ways the game's own
    /// resource declarations do not show. Everything is keyed on names read from the craft, so a mod that is not
    /// installed costs nothing, and a mod nobody wrote a handbook for is still read generically by the craft sheet.
    /// </summary>
    public static class ModKnowledge
    {
        static string N(double v, string f = "0") { return v.ToString(f, CultureInfo.InvariantCulture); }

        public static readonly ModBook[] Books =
        {
            new ModBook { Title = "System Heat", Folders = new[] { "SystemHeat", "HeatControl" }, Modules = new[] { "ModuleSystemHeat" },
                Text = "Heat is handled in loops. Every heat-making part (reactor, nuclear or fusion engine, converter, drill) and every radiator has a Loop ID set in the editor; only parts on the same loop exchange heat. Each producer adds a power in kW at an outlet temperature; the loop settles at the power-weighted temperature. Radiators shed more the hotter the loop is, so a radiator rated 100 kW at 1000 K sheds far less on a 400 K loop. If radiators shed less than is produced the loop temperature climbs until parts shut down or are damaged. Common mistakes: radiators left on a different Loop ID from the reactor; cold loops (converters, 300 to 500 K) sharing with hot loops (reactors, 800 K and up), which drags the hot loop down and overheats the cold parts; sizing radiators for engines that only burn briefly. Engines only make heat while burning." },
            new ModBook { Title = "Near Future Electrical", Folders = new[] { "NearFutureElectrical" }, Modules = new[] { "FissionReactor", "ModuleSystemHeatFissionReactor", "DischargeCapacitor", "FissionGenerator" },
                Text = "Fission reactors turn EnrichedUranium into a large, constant ElectricCharge supply, day or night and at any distance from the sun. They make far more heat than power and must have radiators on their heat loop sized for full power; a reactor without enough cooling scrams or damages its core. Output scales with the power setting, so a reactor can be run low to stretch fuel and cut heat. Fuel lasts years at full power; spent fuel becomes DepletedFuel. Reactors are heavy: for small loads near the sun, solar panels are lighter. Capacitors store StoredCharge and dump it as a burst of ElectricCharge at a set discharge rate: good for short electric engine burns, useless as a steady source. They recharge slowly from the main bus." },
            new ModBook { Title = "Near Future Propulsion", Folders = new[] { "NearFuturePropulsion" },
                Text = "Electric engines (ion, Hall, MPD, VASIMR, pulsed inductive) with very high Isp and very low thrust. They burn ElectricCharge as a propellant alongside Xenon, Argon or Lithium, at tens to thousands of EC per second at full thrust. If generation cannot cover that, thrust is limited in proportion, so the power source (large solar arrays near the sun, a reactor, or capacitors for short burns) must be sized for the engines, not for the bus. Burns take minutes to hours; TWR far below 1 is normal and they cannot lift off or land. Some engines have a variable Isp setting that trades thrust for efficiency." },
            new ModBook { Title = "Far Future Technologies", Folders = new[] { "FarFutureTechnologies" }, Modules = new[] { "ModuleFusionEngine", "ModuleAntimatterTank", "ModuleChargeableEngine", "FusionReactor", "ModuleFusionReactor" },
                Text = "End-of-tech-tree engines: fusion (Deuterium, Helium-3), fission fragment, nuclear salt water, antimatter and pulse engines, with Isp from thousands to hundreds of thousands of seconds. Most need a large burst of ElectricCharge to start (they charge up before ignition), so the craft needs capacitors or a strong power source even though the engine may power itself once running. They make megawatts of waste heat while burning and need System Heat radiators on their loop sized for that. Antimatter tanks need constant ElectricCharge for containment: if power fails the antimatter is lost and the tank explodes, so containment power must be covered through every shadow and with margin. Fuels such as Helium-3 and antimatter are scarce and usually harvested or made, not bought in bulk. Fusion reactors also serve as power plants." },
            new ModBook { Title = "Kerbal Atomics", Folders = new[] { "KerbalAtomics" }, Modules = new[] { "ModuleSystemHeatFissionEngine" },
                Text = "Nuclear thermal rockets burning liquid hydrogen (LqdHydrogen): Isp of 800 to 1000 s or more, modest thrust, heavy engines. Hydrogen is very light per litre, so tanks are large and the mass ratio is worse than the Isp suggests. With System Heat the engines have reactor cores that make heat while running and need radiators on their loop. Some can also generate power or run in a higher-thrust mode with oxidizer. Hydrogen boils off unless the tank is cooled (see CryoTanks)." },
            new ModBook { Title = "CryoTanks", Folders = new[] { "CryoTanks" }, Modules = new[] { "ModuleCryoTank" },
                Text = "Cryogenic fuels (liquid hydrogen, methane) boil away over time unless the tank is cooled. Cooling costs ElectricCharge all the time, in proportion to how much fuel the tank holds; tanks marked zero-boil-off cool themselves if powered, plain tanks lose a set percentage per hour. For a launch stage that burns out in minutes boil-off does not matter. For a transfer stage that waits days or months it does: either power the cooling (count it in the power budget, including in shadow) or carry extra fuel." },
            new ModBook { Title = "Cryogenic Engines", Folders = new[] { "CryoEngines" },
                Text = "Hydrogen and oxygen (hydrolox) and methane and oxygen (methalox) engines. Hydrolox gives high Isp (up to about 450 s) but needs bulky hydrogen tanks; methalox is denser with Isp between kerosene-type and hydrolox. Hydrogen boils off (see CryoTanks)." },
            new ModBook { Title = "Near Future Solar", Folders = new[] { "NearFutureSolar" },
                Text = "Larger and more efficient solar panels, including blanket arrays and concentrators. Output follows the inverse square of distance from the sun like stock panels: about 44 percent at Duna, 4 percent at Jool. Concentrator panels do well close to the sun. Big arrays are the lightest way to feed electric engines in the inner system." },
            new ModBook { Title = "Decaying RTGs", Folders = new[] { "DecayingRTGs" }, Modules = new[] { "ModuleRadioisotopeGenerator", "ModuleDecayingRTG" },
                Text = "RTG output falls over time with a half-life. Size RTG power for the end of the mission, not the start: after one half-life the output is half." },
            new ModBook { Title = "Kerbalism", Fact = "Kerbalism", Folders = new[] { "Kerbalism", "KerbalismConfig" },
                Text = "Kerbalism runs life support, radiation, comfort, science over time and part failures in the background, also for unloaded craft. Crew use food, water and oxygen and make waste; scrubbers, water recyclers and greenhouses are processes that use power. Habitats need pressure and living space; radiation dose depends on belts, solar storms and shielding. Data is transmitted at a rate that falls with distance. Only features marked on in the list below are active in this save: do not give advice about a feature that is off." },
            new ModBook { Title = "TAC Life Support", Fact = "TACLifeSupport", Text = "Each kerbal uses Food, Water and Oxygen every day and makes Waste, WasteWater and CarbonDioxide. Recyclers reduce what must be carried and use power. Running out kills the crew." },
            new ModBook { Title = "USI Life Support", Fact = "USILifeSupport", Text = "Each kerbal uses Supplies and makes Mulch; without supplies, or after too long in a small habitat, kerbals stop working. Recyclers and greenhouses stretch supplies. Habitation time scales with living space." },
            new ModBook { Title = "Snacks", Fact = "Snacks", Text = "Kerbals eat Snacks at meals each day. Running out has penalties set in the difficulty options." },
            new ModBook { Title = "RemoteTech", Fact = "RemoteTech", Text = "RemoteTech replaces CommNet: omni antennas work in all directions over short range, dishes must be pointed at a target, links need line of sight, and there is signal delay. The stock range sums in the comms check do not apply." },
            new ModBook { Title = "RealChute", Fact = "RealChute", Modules = new[] { "RealChuteModule" }, Text = "RealChute parachutes are sized in the part's own editor window for a target landing speed and mass. Check that window's numbers; the stock parachute sum here does not cover them." },
            new ModBook { Title = "Ferram Aerospace Research", Fact = "FAR", Text = "FAR replaces stock aerodynamics with a realistic model: stability needs the centre of lift behind the centre of mass through the whole speed range, fairings matter, and steep turns at speed break the craft apart." },
            new ModBook { Title = "TweakScale", Modules = new[] { "TweakScale" }, Text = "Resized parts: mass, thrust, capacity and cost change with scale, roughly with the cube of size for tanks and the square for engines. The sheet shows the scaled values." },
            new ModBook { Title = "Fuel switching (B9 Part Switch, Interstellar Fuel Switch)", Modules = new[] { "ModuleB9PartSwitch", "InterstellarFuelSwitch" }, Text = "Tanks can be switched between fuel types in the editor. The sheet shows what each tank holds now; an engine that finds none of its propellant aboard usually means a tank was left on the wrong type." },
            new ModBook { Title = "Persistent Thrust", Modules = new[] { "PersistentEngine" }, Text = "Engines keep thrusting during time warp, so long low-thrust burns are practical." },
            new ModBook { Title = "Space Dust", Folders = new[] { "SpaceDust" }, Modules = new[] { "ModuleSpaceDust" }, Text = "Resources such as Helium-3, Deuterium, Xenon and antimatter can be scanned for and harvested from atmospheres, exospheres and radiation belts with scoops and collectors. Harvest rate depends on the local concentration and on speed through it." },
            new ModBook { Title = "KIS and KAS", Folders = new[] { "KIS", "KAS" }, Text = "Parts can be carried in inventories and attached by kerbals on EVA; winches, pipes and struts connect craft on the ground for fuel transfer." },
            new ModBook { Title = "Stockalike Station Parts Expansion", Folders = new[] { "StationPartsExpansionRedux" }, Text = "Station modules: habitats, centrifuges (which need power to spin), cargo containers and large docking parts." },
            new ModBook { Title = "Kerbal Planetary Base Systems", Folders = new[] { "PlanetaryBaseInc" }, Text = "Base modules meant to sit on the surface on their own legs and link by ports at a fixed height." },
            new ModBook { Title = "OPT Spaceplane parts", Folders = new[] { "OPT", "OPT_Legacy", "OPT_Reconfig" }, Text = "Large spaceplane fuselages and high-performance air-breathing and hybrid engines; some engines change mode with altitude and speed." },
            new ModBook { Title = "Kerbal Foundries", Folders = new[] { "KerbalFoundries" }, Text = "Wheels, tracks and repulsors for rovers. They draw power while driving." },
            new ModBook { Title = "KerBalloons", Folders = new[] { "KerBalloon" }, Text = "Balloons lift a small payload in an atmosphere; lift depends on the body's air density." },
            new ModBook { Title = "Kerbal Reusability Expansion", Folders = new[] { "KerbalReusabilityExpansion" }, Text = "Grid fins and landing legs for boosters that fly back and land." },
        };

        /// <summary>The handbooks for mods that are actually at work on this craft.</summary>
        public static List<ModBook> BooksFor(CraftSheet sheet)
        {
            var r = new List<ModBook>();
            if (sheet == null) return r;
            var folders = new HashSet<string>(sheet.Parts.Select(p => p.Mod), StringComparer.OrdinalIgnoreCase);
            var modules = new HashSet<string>(sheet.Parts.SelectMany(p => p.Modules.Select(m => m.Name)));
            foreach (var b in Books)
            {
                bool hit = b.Folders.Any(f => folders.Contains(f)) || b.Modules.Any(m => modules.Any(x => x.StartsWith(m, StringComparison.OrdinalIgnoreCase)));
                // A mod that changes the rules without parts of its own counts when it is installed.
                if (!hit && b.Fact != "" && sheet.ModFacts.ContainsKey(b.Fact)) hit = true;
                if (hit) r.Add(b);
            }
            return r;
        }

        /// <summary>The handbooks as prompt text, with the on/off list of each mod's features where it was read.</summary>
        public static string BooksText(CraftSheet sheet, int maxChars = 3600)
        {
            var books = BooksFor(sheet);
            if (books.Count == 0) return "";
            var sb = new StringBuilder("MOD HANDBOOKS (how the mods at work on this craft behave; follow them):\n");
            foreach (var b in books)
            {
                string line = "- " + b.Title + ": " + b.Text;
                if (b.Fact != "")
                {
                    var feats = sheet.ModFacts.Where(f => f.Key.StartsWith(b.Fact + ".")).OrderBy(f => f.Key).ToList();
                    if (feats.Count > 0) line += " Features in this save: on: " + Join(feats.Where(f => f.Value == "on").Select(f => f.Key.Substring(b.Fact.Length + 1))) + "; off: " + Join(feats.Where(f => f.Value == "off").Select(f => f.Key.Substring(b.Fact.Length + 1))) + ".";
                }
                if (sb.Length + line.Length > maxChars) { sb.AppendLine("- " + b.Title + ": on this craft (handbook left out for length)."); continue; }
                sb.AppendLine(line);
            }
            return sb.ToString().TrimEnd();
        }

        static string Join(IEnumerable<string> items) { var a = items.ToArray(); return a.Length == 0 ? "none" : string.Join(", ", a); }

        /// <summary>True when a mod feature is known to be on. Unknown (the mod is absent, or could not be asked) is false.</summary>
        public static bool FeatureOn(CraftSheet sheet, string key) { return sheet != null && sheet.ModFact(key) == "on"; }

        // ---------------------------------------------------------------- power that mods add

        static readonly string[] ReactorModules = { "ModuleSystemHeatFissionReactor", "FissionReactor", "ModuleFusionReactor", "FusionReactor", "ModuleSystemHeatFissionEngine" };

        /// <summary>EC per second a reactor-like module makes at full power, from whichever field its version uses.</summary>
        public static double ReactorPower(CraftModule m)
        {
            double v = m.CurveMax("ElectricalGeneration");
            if (v <= 0) v = m.Any("ElectricalGeneration", "PowerGeneration", "powerGeneration", "MaxPowerGeneration", "CurrentPowerProduced");
            if (v <= 0) v = Math.Max(0, m.Rate(CraftSheet.EC));
            return v;
        }

        public static bool IsReactor(CraftModule m) { return ReactorModules.Any(n => m.Is(n)) || m.Name.IndexOf("Reactor", StringComparison.OrdinalIgnoreCase) >= 0 && ReactorPower(m) > 0; }

        /// <summary>
        /// Adds to the power budget what mods make and use outside the game's own declarations: reactors, decaying RTGs,
        /// tank cooling, antimatter containment, electric engines, and a life support mod's background processes.
        /// </summary>
        public static void AddPower(CraftSheet sheet, PowerReport r, int crew)
        {
            foreach (var g in sheet.Parts.GroupBy(p => p.Name))
            {
                var p0 = g.First(); int n = g.Count();
                string label = (n > 1 ? n + " × " : "") + p0.Title;
                bool declares = p0.Modules.Any(m => m.Kind != "solar" && m.Rate(CraftSheet.EC) > 0);
                foreach (var m in p0.Modules)
                {
                    if (m.Is("ModuleChargeableEngine") || m.Is("ModuleFusionEngine") || m.Is("FusionReactor"))
                    {
                        double rate = m.Any("ChargeRate", "chargeRate") * n, goal = m.Any("ChargeGoal", "chargeGoal");
                        if (rate > 0) { r.Working += rate; r.Demand.Add(new SheetLine(label + ", charging up before it starts" + (goal > 0 ? " (" + N(goal) + " EC to start)" : ""), CraftSheet.Ec(rate), false, p0.Mod)); }
                    }
                    if (m.Name == "IFSCryostat" || m.Name == "Greenhouse" || m.Name == "Harvester" || m.Name == "HarvesterSystemHeat" || m.Is("ModuleSpaceDust"))
                    {
                        bool cryo = m.Name == "IFSCryostat", grow = m.Name == "Greenhouse";
                        double ec = (cryo ? m.Any("powerReqKW") : grow || m.Name.StartsWith("Harvester", StringComparison.Ordinal) ? m.Any("ec_rate") : m.Any("PowerCost")) * n;
                        // A fuel-switch tank carries a cryostat for every fuel it could hold; only the one whose fuel is
                        // actually in the tank draws power, and only on the tanks that hold some.
                        if (cryo)
                        {
                            string fuel = m.T("resourceName");
                            int holding = fuel == "" ? 0 : g.Count(x => x.Res.Any(q => q.Name == fuel && q.Max > 0));
                            ec = m.Any("powerReqKW") * holding;
                        }
                        if (ec > 0)
                        {
                            if (cryo || grow) { r.Always += ec; if (cryo) r.Cooling += ec; } else r.Working += ec;
                            r.Demand.Add(new SheetLine(label + (cryo ? ", keeping its tank cold" : grow ? ", greenhouse lamps" : ", while it is working"), CraftSheet.Ec(ec), false, p0.Mod));
                        }
                        continue;
                    }
                    if (IsReactor(m) && !(declares && m.Rate(CraftSheet.EC) > 0))
                    {
                        double ec = ReactorPower(m) * n;
                        if (ec <= 0) continue;
                        r.Steady += ec;
                        var fuel = m.Rates.Where(x => x.PerSec < 0 && x.Res != CraftSheet.EC).ToList();
                        double life = double.MaxValue;
                        foreach (var f in fuel) { double have = sheet.Amount(f.Res), use = -f.PerSec * n; if (use > 0) life = Math.Min(life, have / use); }
                        r.Sources.Add(new SheetLine(label + ", reactor at full power (constant, needs its heat loop cooled)", CraftSheet.Ec(ec) + (life < double.MaxValue && life > 0 ? ", fuel for " + EngMath.Dur(life) : ""), false, p0.Mod));
                    }
                    else if ((m.Is("ModuleRadioisotopeGenerator") || m.Is("ModuleDecayingRTG") || m.Name.IndexOf("RTG", StringComparison.Ordinal) >= 0) && !declares)
                    {
                        double ec = m.Any("BasePower", "basePower", "PowerOutput", "power") * n;
                        if (ec <= 0) continue;
                        double half = m.Any("HalfLife", "halfLife");
                        r.Steady += ec;
                        r.Sources.Add(new SheetLine(label + ", RTG" + (half > 0 ? " (output halves every " + N(half, "0.#") + " years)" : ", constant"), CraftSheet.Ec(ec), false, p0.Mod));
                    }
                    else if (m.Is("DischargeCapacitor"))
                    {
                        double rate = m.Any("DischargeRate", "dischargeRate") * n, stored = sheet.Parts.Where(x => x.Name == p0.Name).Sum(x => x.Max("StoredCharge"));
                        if (rate > 0) r.Sources.Add(new SheetLine(label + ", capacitor: a burst only" + (stored > 0 ? ", " + N(stored) + " stored" : ""), "up to " + CraftSheet.Ec(rate) + (stored > 0 ? " for " + EngMath.Dur(stored / rate) : ""), false, p0.Mod));
                        r.Burst += rate; r.BurstStore += stored;
                    }
                    else if (m.Is("ModuleCryoTank"))
                    {
                        double per = m.Any("CoolingCost", "coolingCost");
                        double fuel = g.Sum(x => x.Res.Where(q => q.Name == "LqdHydrogen" || q.Name == "LqdMethane" || q.Name == "LH2" || q.Name == "LqdHe3" || q.Name == "LqdDeuterium").Sum(q => q.Max));
                        double boil = m.Any("BoiloffRate", "boiloffRate");
                        if (fuel <= 0) continue;
                        if (per > 0)
                        {
                            double ec = per * fuel / 1000;
                            r.Always += ec; r.Cooling += ec;
                            r.Demand.Add(new SheetLine(label + ", keeping " + N(fuel) + " units of cryogenic fuel cold", CraftSheet.Ec(ec), false, p0.Mod));
                        }
                        else if (boil > 0) r.Notes.Add(label + " cannot be cooled: the fuel boils off at " + N(boil, "0.###") + "% per hour (" + N(Math.Min(100, boil * 6), "0.#") + "% in a 6 hour day).");
                    }
                    else if (m.Is("ModuleAntimatterTank"))
                    {
                        double ec = m.Any("ContainmentCost", "containmentCost") * n;
                        if (ec <= 0) continue;
                        r.Always += ec; r.Critical += ec;
                        r.Demand.Add(new SheetLine(label + ", antimatter containment (must never lose power)", CraftSheet.Ec(ec), false, p0.Mod));
                    }
                    else if (m.Kind == "engine" && m.N("ec") > 0)
                    {
                        double ec = m.N("ec") * n;
                        r.Engines += ec;
                        r.Demand.Add(new SheetLine(label + (m.Label != "" ? " [" + m.Label + "]" : "") + ", burning at full thrust", CraftSheet.Ec(ec), false, p0.Mod));
                    }
                }
            }

            // A life support mod's background processes and per-kerbal needs.
            foreach (var pr in sheet.Processes)
            {
                double ec = pr.Rates.Where(x => x.Res == CraftSheet.EC).Sum(x => x.PerSec);
                if (Math.Abs(ec) < 1e-7) continue;
                string others = string.Join(", ", pr.Rates.Where(x => x.Res != CraftSheet.EC && x.PerSec < 0).Select(x => CraftSheet.Short(x.Res)).ToArray());
                if (ec > 0)
                {
                    if (others != "") { r.FuelCell += ec; r.Sources.Add(new SheetLine(pr.Title + " (" + pr.Mod + " process, burning " + others + ")", CraftSheet.Ec(ec), false, pr.Mod)); }
                    else { r.Steady += ec; r.Sources.Add(new SheetLine(pr.Title + " (" + pr.Mod + " process)", CraftSheet.Ec(ec), false, pr.Mod)); }
                }
                else { r.Always += -ec; r.Demand.Add(new SheetLine(pr.Title + " (" + pr.Mod + " process, runs all the time)", CraftSheet.Ec(-ec), false, pr.Mod)); }
            }
            if (crew > 0)
                foreach (var rule in sheet.CrewRules.Where(x => x.Input == CraftSheet.EC && x.PerSec > 0))
                {
                    double ec = rule.PerSec * crew;
                    r.Always += ec;
                    r.Demand.Add(new SheetLine(Cap(rule.Name) + " for " + crew + " crew (" + rule.Mod + (rule.Varies ? ", up to this much" : "") + ")", CraftSheet.Ec(ec), false, rule.Mod));
                }
        }

        static string Cap(string s) { return string.IsNullOrEmpty(s) ? "" : char.ToUpperInvariant(s[0]) + s.Substring(1); }

        // ---------------------------------------------------------------- heat

        class Loop
        {
            public int Id;
            public double Steady, SteadyTemp, Burst, BurstTemp, Limit = double.MaxValue;
            public List<string> Makers = new List<string>();
            public List<KeyValuePair<CraftPart, CraftModule>> Radiators = new List<KeyValuePair<CraftPart, CraftModule>>();
            public string LimitPart = "";
            public double Shed(double temp) { return Radiators.Sum(r => RadiatorAt(r.Value, temp)); }
        }

        /// <summary>kW a radiator sheds with its loop at this temperature.</summary>
        public static double RadiatorAt(CraftModule m, double temp)
        {
            if (m.T("temperatureCurve") != "") return Math.Max(0, m.Curve("temperatureCurve", temp));
            return m.N("kw");
        }

        static int LoopOf(CraftPart p, CraftModule m)
        {
            string id = m.T("systemHeatModuleID"); if (id == "") id = m.T("HeatModuleID");
            var heat = p.Modules.Where(x => x.Name == "ModuleSystemHeat").ToList();
            var mine = heat.FirstOrDefault(x => id != "" && x.T("moduleID") == id) ?? heat.FirstOrDefault();
            return mine == null ? 0 : (int)Math.Round(mine.Any("currentLoopID", "loopID", "LoopID"));
        }

        /// <summary>Heat made against heat shed, loop by loop for System Heat, or as one pool for stock core heat.</summary>
        public static CheckReport Heat(CraftSheet sheet)
        {
            var r = new CheckReport { Key = "heat", Title = "Heat" };
            if (sheet == null) return r;
            bool systemHeat = sheet.HasModule("ModuleSystemHeat");
            if (!systemHeat) return StockHeat(sheet, r);

            var loops = new Dictionary<int, Loop>();
            Func<int, Loop> loop = id => { Loop l; if (!loops.TryGetValue(id, out l)) { l = new Loop { Id = id }; loops[id] = l; } return l; };
            foreach (var p in sheet.Parts)
                foreach (var m in p.Modules)
                {
                    if (m.Name == "ModuleSystemHeat") continue;
                    if (m.Is("ModuleSystemHeatRadiator") || m.Kind == "radiator" && m.T("temperatureCurve") != "") { loop(LoopOf(p, m)).Radiators.Add(new KeyValuePair<CraftPart, CraftModule>(p, m)); continue; }
                    double kw = m.CurveMax("HeatGeneration");
                    if (kw <= 0) kw = m.Any("systemPower", "SystemPower", "HeatGeneration", "heatGeneration");
                    if (kw <= 0 || !m.Name.StartsWith("Module", StringComparison.Ordinal) && !m.Num.ContainsKey("systemPower") && !m.Num.ContainsKey("SystemPower")) continue;
                    if (!p.Modules.Any(x => x.Name == "ModuleSystemHeat")) continue;
                    double temp = m.Any("NominalTemperature", "systemOutletTemperature", "nominalTemperature");
                    if (temp <= 0) temp = 500;
                    var l = loop(LoopOf(p, m));
                    bool burst = m.Kind == "engine" || m.Is("ModuleSystemHeatEngine") || m.Is("ModuleSystemHeatFissionEngine") || m.Is("ModuleFusionEngine") || m.Is("ModuleSpaceDust") || m.Name.StartsWith("Harvester", StringComparison.Ordinal);
                    if (burst) { l.BurstTemp = (l.BurstTemp * l.Burst + temp * kw) / (l.Burst + kw); l.Burst += kw; }
                    else { l.SteadyTemp = (l.SteadyTemp * l.Steady + temp * kw) / (l.Steady + kw); l.Steady += kw; }
                    l.Makers.Add(p.Title + (burst ? " (while burning)" : "") + " " + N(kw) + " kW at " + N(temp) + " K");
                    double limit = m.Any("CriticalTemperature", "shutdownTemperature", "MaximumTemperature", "maximumTemperature");
                    if (limit > 0 && limit < l.Limit) { l.Limit = limit; l.LimitPart = p.Title; }
                }
            if (loops.Count == 0) { r.Rating = "nothing makes heat"; r.Add("No part on this craft makes loop heat."); return r; }

            bool anyShort = false, anyTight = false, anyOver = false;
            foreach (var l in loops.Values.OrderBy(x => x.Id))
            {
                string name = "Loop " + l.Id;
                double made = l.Steady, temp = l.Steady > 0 ? l.SteadyTemp : l.BurstTemp;
                if (made <= 0 && l.Burst <= 0)
                {
                    if (l.Radiators.Count > 0) { r.Add(name + " has " + l.Radiators.Count + " radiator" + (l.Radiators.Count == 1 ? "" : "s") + " but nothing making heat. If they are meant for another loop, set their Loop ID to match.", "", loops.Values.Any(o => o != l && o.Steady + o.Burst > 0 && o.Radiators.Count == 0)); }
                    continue;
                }
                foreach (var g in l.Makers.GroupBy(x => x)) r.Add(name + ": " + (g.Count() > 1 ? g.Count() + " × " : "") + g.Key);
                if (l.Radiators.Count == 0)
                {
                    r.Add(name + " makes " + N(made + l.Burst) + " kW and has no radiator on it. Add radiators and set them to Loop ID " + l.Id + ".", "", true);
                    anyShort = true;
                    continue;
                }
                var cases = new List<double[]>();
                if (l.Steady > 0) cases.Add(new[] { l.Steady, l.SteadyTemp, 0 });
                if (l.Burst > 0) cases.Add(new[] { l.Steady + l.Burst, (l.SteadyTemp * l.Steady + l.BurstTemp * l.Burst) / (l.Steady + l.Burst), 1 });
                foreach (var c in cases)
                {
                    double need = c[0], at = c[1], shed = l.Shed(at);
                    string when = c[2] > 0 ? " with the engines burning" : "";
                    if (shed >= need)
                    {
                        bool tight = shed < need * 1.15;
                        anyTight |= tight;
                        r.Add(name + when + ": makes " + N(need) + " kW at about " + N(at) + " K; " + l.Radiators.Count + " radiator" + (l.Radiators.Count == 1 ? "" : "s") + " shed " + N(shed) + " kW at that temperature" + (tight ? " (tight)" : ""), "+" + N(shed - need) + " kW");
                        if (c[2] == 0 && l.Burst <= 0 || c[2] > 0)
                        {
                            // Oversized: how many radiators could go and still shed 30% more than is made.
                            var rads = l.Radiators.OrderBy(x => RadiatorAt(x.Value, at)).ToList();
                            double spare = shed - need * 1.3, mass = 0, cost = 0; int k = 0;
                            foreach (var rad in rads) { double one = RadiatorAt(rad.Value, at); if (one <= 0 || one > spare || k >= rads.Count - 1) break; spare -= one; mass += rad.Key.Mass; cost += rad.Key.Cost; k++; }
                            if (k > 0 && shed > need * 2) { anyOver = true; r.Fix(name + ": radiators shed " + N(shed / need, "0.#") + " times what is made. " + k + " could go and still leave 30% spare.", "saves " + N(mass, "0.##") + " t" + (cost > 0 ? ", " + N(cost) + " funds" : "")); }
                        }
                    }
                    else
                    {
                        // Where the loop settles: radiators shed more as it heats up.
                        double settle = 0;
                        for (double t = at; t <= 4000; t += 25) if (l.Shed(t) >= need) { settle = t; break; }
                        bool fatal = settle <= 0 || settle > l.Limit;
                        anyShort |= fatal || c[2] == 0;
                        anyTight |= !fatal;
                        r.Add(name + when + ": makes " + N(need) + " kW at about " + N(at) + " K but the radiators shed only " + N(shed) + " kW there. "
                            + (settle <= 0 ? "They never catch up: the loop overheats." : "The loop climbs to about " + N(settle) + " K" + (l.Limit < double.MaxValue ? (settle > l.Limit ? ", past the " + N(l.Limit) + " K limit of the " + l.LimitPart + "." : ", under the " + N(l.Limit) + " K limit of the " + l.LimitPart + ", so it holds but runs hot.") : ".")),
                            "−" + N(need - shed) + " kW", fatal || c[2] == 0);
                        var best = l.Radiators.GroupBy(x => x.Key.Name).OrderByDescending(x => x.Count()).First().First();
                        double each = RadiatorAt(best.Value, at);
                        if (each > 0) r.Fix(name + ": add " + (int)Math.Ceiling((need * 1.15 - shed) / each) + " more " + best.Key.Title + " on Loop ID " + l.Id, "+" + N(each) + " kW each at " + N(at) + " K");
                        else r.Fix(name + ": add radiators that shed " + N(need * 1.15 - shed) + " kW at " + N(at) + " K");
                    }
                }
                if (l.Steady > 0 && l.Burst > 0 && Math.Abs(l.SteadyTemp - l.BurstTemp) > 300)
                    r.Add(name + " mixes parts at about " + N(Math.Min(l.SteadyTemp, l.BurstTemp)) + " K with parts at " + N(Math.Max(l.SteadyTemp, l.BurstTemp)) + " K. Hot and cold parts work better on separate loops.");
            }
            r.Rating = anyShort ? "short" : anyTight ? "tight" : anyOver ? "oversized" : "right-sized";
            r.Note = "Radiators shed more heat the hotter their loop runs, so their rating only applies at their rated temperature.";
            return r;
        }

        static CheckReport StockHeat(CraftSheet sheet, CheckReport r)
        {
            double need = 0; var makers = new List<string>();
            foreach (var g in sheet.Parts.Where(p => p.Has("coreheat") && (p.Has("converter") || p.Has("drill"))).GroupBy(p => p.Name))
            {
                double kw = g.Sum(p => p.Of("coreheat").Sum(m => m.N("maxCoolant")));
                if (kw <= 0) continue;
                need += kw;
                makers.Add((g.Count() > 1 ? g.Count() + " × " : "") + g.First().Title + ": up to " + N(kw) + " kW of cooling when running flat out");
            }
            double shed = sheet.All("radiator").Sum(m => m.N("kw"));
            int rads = sheet.Count("radiator");
            if (need <= 0)
            {
                r.Rating = rads > 0 ? "oversized" : "nothing makes heat";
                r.Add(rads > 0 ? "The craft carries " + rads + " radiator" + (rads == 1 ? "" : "s") + " (" + N(shed) + " kW) but nothing on it needs active cooling." : "Nothing on this craft needs active cooling.");
                if (rads > 0)
                {
                    var list = sheet.Parts.Where(p => p.Has("radiator")).ToList();
                    r.Fix("The radiators could go unless they are there for a nuclear engine's stray heat.", "saves " + N(list.Sum(p => p.Mass), "0.##") + " t");
                }
                return r;
            }
            foreach (var m in makers) r.Add(m);
            r.Add("Radiators: " + rads + " shedding " + N(shed) + " kW in total", (shed >= need ? "+" : "−") + N(Math.Abs(shed - need)) + " kW", shed < need * 0.5);
            if (shed < need) r.Add("Converters and drills throttle themselves when they run hot rather than break, so being short here costs output, not the part.");
            r.Rating = shed < need * 0.5 ? "short" : shed < need ? "tight" : shed > need * 2.5 ? "oversized" : "right-sized";
            if (shed < need)
            {
                var best = sheet.Parts.Where(p => p.Has("radiator")).GroupBy(p => p.Name).OrderByDescending(g => g.Count()).Select(g => g.First()).FirstOrDefault();
                double each = best == null ? 0 : best.Of("radiator").Sum(m => m.N("kw"));
                if (each > 0) r.Fix("Add " + (int)Math.Ceiling((need - shed) / each) + " more " + best.Title, "+" + N(each) + " kW each");
                else r.Fix("Add radiators worth " + N(need - shed) + " kW");
            }
            return r;
        }
    }
}
