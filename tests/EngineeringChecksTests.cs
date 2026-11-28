using System;
using System.Collections.Generic;
using System.Linq;
using KerbalNews;

/// <summary>The engineering team's computed checks and mod knowledge: heat loops, mod power, comms, landing, balance, launch, crew, cost.</summary>
public static class EngineeringChecksTests
{
    static void Assert(bool c, string m) { if (!c) throw new Exception("Assertion failed: " + m); }

    static List<EngBody> Bodies()
    {
        Func<string, string, double, double, double, double, double, double, bool, EngBody> b = (name, parent, mu, radius, g, atmo, pressure, sma, surface) =>
            new EngBody { Name = name, Parent = parent, Mu = mu, Radius = radius, Gravity = g, AtmoDepth = atmo, AtmoPressure = pressure, AtmoDensity = pressure * 1.225, Sma = sma, RotPeriod = 21549, HasSurface = surface };
        var list = new List<EngBody>
        {
            new EngBody { Name = "Sun", Mu = 1.1723328e18, Radius = 261600000, IsStar = true, HasSurface = false },
            b("Kerbin", "Sun", 3.5316e12, 600000, 9.81, 70000, 1, 13599840256, true),
            b("Mun", "Kerbin", 6.5138398e10, 200000, 1.63, 0, 0, 12000000, true),
            b("Duna", "Sun", 3.0136321e11, 320000, 2.94, 50000, 0.0667, 20726155264, true),
            b("Jool", "Sun", 2.82528e14, 6000000, 7.85, 200000, 15, 68773560320, false),
        };
        list[1].IsHome = true;
        foreach (var e in list.Where(x => !x.IsStar)) { var p = list.First(x => x.Name == e.Parent); e.SunDistance = p.IsStar ? e.Sma : p.Sma; }
        return list;
    }

    static CraftPart Part(string name, string title, double mass = 0.1, string mod = "stock", int stage = -1, int parent = 0)
    {
        return new CraftPart { Name = name, Title = title, Stage = stage, Parent = parent, Mod = mod, DryMass = mass, Cost = 100, HasPos = true, CrashTol = 12 };
    }
    static CraftModule Mod(string kind, string name = null, bool staged = false) { return new CraftModule { Name = name ?? "Module" + kind, Kind = kind, Staged = staged }; }
    static CraftPart With(this CraftPart p, CraftModule m) { p.Modules.Add(m); return p; }
    static CraftModule Set(this CraftModule m, string key, double v) { m.Num[key] = v; return m; }
    static CraftModule Text(this CraftModule m, string key, string v) { m.Txt[key] = v; return m; }
    static CraftPart Holds(this CraftPart p, string res, double amount) { p.Res.Add(new CraftRes { Name = res, Amount = amount, Max = amount }); return p; }

    static CraftPart Core() { var p = Part("probeCoreOcto", "Probodobodyne OKTO"); p.Parent = -1; return p.With(Mod("command").Set("crewMin", 0)).With(Mod("sas").Set("level", 1)).Holds(CraftSheet.EC, 100); }

    static CraftPart HeatMaker(string title, double kw, double temp, int loop, string module = "ModuleSystemHeatConverter", string kind = "converter")
    {
        return Part(title, title, 2, "NearFutureElectrical")
            .With(new CraftModule { Name = "ModuleSystemHeat", Kind = "other" }.Text("moduleID", "default").Set("currentLoopID", loop))
            .With(Mod(kind, module).Set("systemPower", kw).Set("systemOutletTemperature", temp).Text("systemHeatModuleID", "default"));
    }

    static CraftPart Radiator(int loop, string curve = "0:0;400:20;1000:200;2000:300")
    {
        return Part("radiator-universal-1", "XR-1500 Radiator", 0.5, "HeatControl")
            .With(new CraftModule { Name = "ModuleSystemHeat", Kind = "other" }.Text("moduleID", "default").Set("currentLoopID", loop))
            .With(Mod("radiator", "ModuleSystemHeatRadiator").Text("temperatureCurve", curve).Text("systemHeatModuleID", "default"));
    }

    static CheckContext Ctx(CraftSheet s, string mission = "", List<EngStage> stages = null)
    {
        s.Prepare();
        var bodies = Bodies();
        var snap = new EngSnapshot { Scene = "vab", Vessel = "Test", Sheet = s, Bodies = bodies };
        if (stages != null) snap.Stages = stages;
        var plan = mission == "" ? null : EngMissions.Plan(mission, snap);
        var c = new CheckContext { Sheet = s, Home = bodies[1], Bodies = bodies, Plan = plan, Stages = snap.Stages, Editor = true, Crew = s.Parts.Sum(p => p.Crew) };
        if (plan != null && plan.Known) { c.There = snap.BodyNamed(plan.Body); c.Dest = EngMissions.Find(plan.Body, snap); }
        return c;
    }

    public static void TestHeatLoopsAreJudgedBothWays()
    {
        // A reactor at 800 K making 600 kW with two radiators that shed about 147 kW each at 800 K: short.
        var s = new CraftSheet();
        s.Parts.Add(Core());
        var reactor = HeatMaker("MX-1 Garnet Fission Reactor", 600, 800, 1, "ModuleSystemHeatFissionReactor", "other");
        reactor.Modules.Last().Text("ElectricalGeneration", "0:0;100:60").Set("ElectricalGeneration.max", 60).Set("CriticalTemperature", 1300);
        s.Parts.Add(reactor);
        s.Parts.Add(Radiator(1)); s.Parts.Add(Radiator(1));
        s.Prepare();
        var heat = ModKnowledge.Heat(s);
        Assert(heat.Rating == "short" && heat.Bad && heat.Lines.Any(l => l.Text.Contains("Loop 1") && l.Text.Contains("600 kW")), "600 kW into 293 kW of radiators is short: " + heat.Text());
        Assert(heat.Fixes.Any(f => f.Text.Contains("more XR-1500 Radiator") && f.Text.Contains("Loop ID 1")), "the fix names the radiator and the loop: " + heat.Text());

        // Radiators left on another loop: the classic mistake.
        var wrong = new CraftSheet();
        wrong.Parts.Add(Core()); wrong.Parts.Add(HeatMaker("Reactor", 100, 800, 0, "ModuleSystemHeatFissionReactor", "other")); wrong.Parts.Add(Radiator(3));
        wrong.Prepare();
        var w = ModKnowledge.Heat(wrong);
        Assert(w.Bad && w.Lines.Any(l => l.Text.Contains("no radiator on it")) && w.Lines.Any(l => l.Text.Contains("Loop 3") && l.Text.Contains("nothing making heat")), "a radiator on the wrong loop is called out: " + w.Text());

        // Ten radiators for a small converter: oversized, with the saving.
        var big = new CraftSheet();
        big.Parts.Add(Core()); big.Parts.Add(HeatMaker("ISRU", 100, 1000, 0));
        for (int i = 0; i < 10; i++) big.Parts.Add(Radiator(0));
        big.Prepare();
        var o = ModKnowledge.Heat(big);
        Assert(o.Rating == "oversized" && o.Fixes.Any(f => f.Text.Contains("could go") && f.Value.Contains("saves")), "ten radiators for 100 kW is oversized: " + o.Text());

        // Stock core heat without System Heat.
        var stock = new CraftSheet();
        stock.Parts.Add(Core());
        stock.Parts.Add(Part("ISRU", "Convert-O-Tron 250", 4).With(Mod("converter")).With(Mod("coreheat").Set("maxCoolant", 500)));
        stock.Parts.Add(Part("radPanelSm", "Thermal Control System (small)", 0.05).With(Mod("radiator").Set("kw", 50)));
        stock.Prepare();
        var st = ModKnowledge.Heat(stock);
        Assert(st.Rating == "short" && st.Fixes.Any(f => f.Text.Contains("Thermal Control System")), "stock converter with one small radiator: " + st.Text());
        var none = new CraftSheet(); none.Parts.Add(Core()); none.Prepare();
        Assert(ModKnowledge.Heat(none).Rating == "nothing makes heat", "a plain probe makes no heat");
    }

    public static void TestModPowerReactorsElectricEnginesAndCryoTanks()
    {
        var bodies = Bodies();
        var s = new CraftSheet();
        s.Parts.Add(Core().With(Mod("wheel").Set("torque", 0.5)));
        var reactor = Part("reactor-125", "MX-0 Fission Reactor", 1, "NearFutureElectrical")
            .With(new CraftModule { Name = "ModuleSystemHeatFissionReactor", Kind = "other" }.Text("ElectricalGeneration", "0:0;100:60").Set("ElectricalGeneration.max", 60));
        reactor.Modules[0].Rates.Add(new CraftRate { Res = "EnrichedUranium", PerSec = -0.00001 });
        reactor.Holds("EnrichedUranium", 60);
        s.Parts.Add(reactor);
        s.Parts.Add(Part("ionArgon-0625", "KP-01 Pulsed Inductive Thruster", 0.3, "NearFuturePropulsion", 0).With(Mod("engine", "ModuleEnginesFX", true).Set("thrust", 5).Set("ispVac", 4000).Set("ec", 150).Text("props", "ElectricCharge, ArgonGas")));
        s.Parts.Add(Part("cryotank", "H250-64 Cryogenic Tank", 1, "CryoTanks").With(new CraftModule { Name = "ModuleCryoTank", Kind = "other" }.Set("CoolingCost", 0.09)).Holds("LqdHydrogen", 20000));
        s.Parts.Add(Part("amtank", "A-834M Antimatter Tank", 1, "FarFutureTechnologies").With(new CraftModule { Name = "ModuleAntimatterTank", Kind = "other" }.Set("ContainmentCost", 2)).Holds("Antimatter", 100));
        s.Prepare();
        var p = s.Power(bodies[1], bodies[1], bodies);
        Assert(Math.Abs(p.Steady - 60) < 0.01 && p.Sources.Any(l => l.Text.Contains("reactor at full power") && l.Value.Contains("fuel for")), "the reactor counts as a constant 60 EC/s with a fuel life: " + CraftSheet.PowerText(p));
        Assert(Math.Abs(p.Cooling - 1.8) < 0.01 && Math.Abs(p.Critical - 2) < 0.01 && p.Always > 3.79, "tank cooling and antimatter containment are always-on loads: " + p.Always);
        Assert(Math.Abs(p.Engines - 150) < 0.01 && p.There.Any(l => l.Text.Contains("Electric engines at full thrust need 150") && l.Value.Contains("generation covers")), "the electric engine's draw is set against generation: " + CraftSheet.PowerText(p));
        Assert(p.Fixes.Any(f => f.Text.Contains("flat out")), "a fix says what generation the engines need");

        var books = ModKnowledge.BooksFor(s).Select(b => b.Title).ToList();
        Assert(books.Contains("Near Future Electrical") && books.Contains("Near Future Propulsion") && books.Contains("CryoTanks") && books.Contains("Far Future Technologies") && books.Contains("System Heat") && !books.Contains("Kerbalism") && !books.Contains("Kerbal Atomics"),
            "handbooks are picked by the parts on the craft: " + string.Join(", ", books.ToArray()));
        string text = ModKnowledge.BooksText(s);
        Assert(text.Contains("antimatter") && text.Contains("EnrichedUranium") && text.Length < 3700, "the handbook text is bounded and on topic: " + text.Length);

        // Kerbalism's features and background processes, as the probe reports them.
        var k = new CraftSheet();
        k.Parts.Add(Core()); k.Parts[0].Crew = 2;
        k.ModFacts["Kerbalism"] = "installed"; k.ModFacts["Kerbalism.Reliability"] = "off"; k.ModFacts["Kerbalism.Radiation"] = "on";
        k.Processes.Add(new CraftProcess { Name = "scrubber", Title = "Scrubber", Mod = "Kerbalism", Capacity = 2, Rates = { new CraftRate { Res = CraftSheet.EC, PerSec = -0.1 }, new CraftRate { Res = "CarbonDioxide", PerSec = -0.01 } } });
        k.CrewRules.Add(new CrewRule { Name = "climatization", Input = CraftSheet.EC, PerSec = 0.05, Mod = "Kerbalism", Varies = true });
        k.Prepare();
        var kp = k.Power(bodies[1], bodies[1], bodies);
        Assert(kp.Demand.Any(l => l.Text.StartsWith("Scrubber")) && kp.Demand.Any(l => l.Text.StartsWith("Climatization for 2 crew")) && Math.Abs(kp.Always - 0.2) < 0.001 && !kp.Unknown, "Kerbalism processes and per-kerbal power are in the budget: " + CraftSheet.PowerText(kp));
        string kb = ModKnowledge.BooksText(k);
        Assert(kb.Contains("on: Radiation") && kb.Contains("off: Reliability") && !ModKnowledge.FeatureOn(k, "Kerbalism.Reliability") && ModKnowledge.FeatureOn(k, "Kerbalism.Radiation"), "the handbook lists which features are on: " + kb);
    }

    public static void TestCommsRangeAndSignal()
    {
        var s = new CraftSheet();
        s.Parts.Add(Core());
        s.Parts.Add(Part("longAntenna", "Communotron 16", 0.005).With(Mod("antenna").Set("power", 5e5).Set("combinable", 1).Set("exponent", 1).Text("type", "direct")));
        s.Facts["dsnPower"] = 2e9; s.Facts["trackingLevel"] = 1; s.Facts["commnet"] = 1; s.Facts["signalForControl"] = 1;
        var mun = CraftChecks.Comms(Ctx(s, "orbit the Mun"));
        // Range = sqrt(5e5 * 2e9) = 31.6 Mm; the Mun is 12 Mm away.
        Assert(mun.Rating == "good" && mun.Lines.Any(l => l.Text.StartsWith("At Mun") && l.Value.StartsWith("signal")), "a Communotron 16 reaches the Mun: " + mun.Text());
        var duna = CraftChecks.Comms(Ctx(s, "orbit Duna"));
        Assert(duna.Rating == "no link" && duna.Bad && duna.Fixes.Any(f => f.Text.Contains("antenna power of about") && f.Text.Contains("tracking station upgrade")), "it does not reach Duna, and the fix says how much is needed: " + duna.Text());
        Assert(Math.Abs(CraftChecks.Signal(0, 100) - 1) < 1e-9 && CraftChecks.Signal(100, 100) == 0 && Math.Abs(CraftChecks.Signal(50, 100) - 0.5) < 1e-9, "the signal curve");
        var two = new[] { Mod("antenna").Set("power", 100).Set("combinable", 1).Set("exponent", 0.75), Mod("antenna").Set("power", 100).Set("combinable", 1).Set("exponent", 0.75) };
        Assert(Math.Abs(CraftChecks.Combine(two) - 100 * Math.Pow(2, 0.75)) < 1e-6, "two equal antennas combine with the exponent");
        var none = new CraftSheet(); none.Parts.Add(Core()); none.Facts["signalForControl"] = 1;
        var n = CraftChecks.Comms(Ctx(none));
        Assert(n.Rating == "none" && n.Bad, "a probe with no antenna is a problem: " + n.Text());
        s.ModFacts["RemoteTech"] = "installed";
        Assert(CraftChecks.Comms(Ctx(s)).Rating == "not computed", "RemoteTech turns the stock sums off");
    }

    public static void TestLandingParachutesBothWays()
    {
        // A one tonne pod under a canopy worth 660 m² of drag area lands at about 5.5 m/s on Kerbin.
        Func<int, double, CraftSheet> pod = (chutes, area) =>
        {
            var s = new CraftSheet();
            var p = Part("mk1pod", "Mk1 Command Pod", 0.8); p.Parent = -1; p.Crew = 1; p.CrashTol = 14;
            s.Parts.Add(p.With(Mod("command").Set("crewMin", 1)));
            for (int i = 0; i < chutes; i++) { var c = Part("parachuteSingle", "Mk16 Parachute", 0.1).With(Mod("chute", null, true)); c.ChuteArea = area; s.Parts.Add(c); }
            return s;
        };
        double v = CraftChecks.ChuteSpeed(1.0, 660, Bodies()[1]);
        Assert(v > 5 && v < 6.5, "one tonne under a Mk16-sized canopy lands at about 5.5 m/s: " + v);
        var ok = CraftChecks.Landing(Ctx(pod(1, 660)));
        Assert(ok.Rating == "safe" && ok.Lines.Any(l => l.Text.Contains("touchdown at about") && l.Value == "safe"), "one parachute is safe: " + ok.Text());
        var fast = CraftChecks.Landing(Ctx(pod(1, 60)));
        Assert(fast.Rating == "problem" && fast.Fixes.Any(f => f.Text.StartsWith("Add ") && f.Text.Contains("Mk16 Parachute")), "a canopy ten times too small is too fast, with a count to add: " + fast.Text());
        var many = CraftChecks.Landing(Ctx(pod(5, 660)));
        Assert(many.Rating == "oversized" && many.Fixes.Any(f => f.Text.Contains("could go") && f.Value.Contains("saves")), "five parachutes on a one tonne pod is oversized: " + many.Text());
        var bare = CraftChecks.Landing(Ctx(pod(0, 0)));
        Assert(bare.Rating == "problem" && bare.Lines.Any(l => l.Bad && l.Text.Contains("No parachutes")), "a crewed pod with no parachute: " + bare.Text());
        // Duna's thin air: the same canopy lands far faster there.
        var duna = CraftChecks.Landing(Ctx(pod(1, 660), "land on Duna"));
        Assert(duna.Rating == "marginal" && duna.Lines.Any(l => l.Text.Contains("at Duna") && l.Value != "safe"), "the same parachute lands twice as fast at Duna: " + duna.Text());
        // The Mun has no air: the engine's thrust to weight there decides it.
        var lander = pod(0, 0);
        var stages = new List<EngStage> { new EngStage { Stage = 0, DvVac = 3000, TwrVac = 0.15, StartMass = 5, EndMass = 2, ThrustVac = 7.4 } };
        var munBad = CraftChecks.Landing(Ctx(lander, "land on the Mun", stages));
        Assert(munBad.Lines.Any(l => l.Bad && l.Text.Contains("no air") && l.Value == "cannot stop"), "a TWR of 0.9 on the Mun cannot stop: " + munBad.Text());
        var orbit = CraftChecks.Landing(Ctx(pod(2, 660), "orbit the Mun"));
        Assert(orbit.Rating == "no landing planned" && orbit.Fixes.Count == 1, "an orbit-only mission with parachutes: " + orbit.Text());
    }

    public static void TestBalanceThrustAndTurning()
    {
        Func<double, CraftSheet> rocket = offset =>
        {
            var s = new CraftSheet();
            var core = Core().With(Mod("wheel").Set("torque", 0.5)); core.Y = 4; s.Parts.Add(core);
            var tank = Part("tank", "FL-T800 Fuel Tank", 0.5); tank.Y = 2; tank.ResMass = 4; s.Parts.Add(tank);
            var eng = Part("engine", "LV-T30 Reliant", 1.25, "stock", 0, 1); eng.Y = 0; eng.X = offset;
            eng.With(Mod("engine", "ModuleEngines", true).Set("thrust", 200).Set("ispVac", 300).Set("aimed", 1).Set("px", offset).Set("py", -0.5).Set("pz", 0).Set("dx", 0).Set("dy", -1).Set("dz", 0).Text("props", "LiquidFuel, Oxidizer"));
            s.Parts.Add(eng);
            return s;
        };
        var straight = CraftChecks.Balance(Ctx(rocket(0)));
        Assert(!straight.Bad && straight.Lines.Any(l => l.Value == "balanced"), "an engine on the centre line is balanced: " + straight.Text());
        var off = CraftChecks.Balance(Ctx(rocket(1.5)));
        Assert(off.Rating == "problem" && off.Lines.Any(l => l.Bad && l.Text.Contains("off the centre of mass") && l.Value == "it will tumble"), "an engine 1.5 m off centre with no gimbal tumbles: " + off.Text());
        // A heavy last stage on one small wheel is sluggish; a tiny one with many wheels is oversized.
        var heavy = new CraftSheet();
        var c0 = Core().With(Mod("wheel").Set("torque", 0.5)); c0.DryMass = 20; heavy.Parts.Add(c0);
        var far = Part("truss", "Long truss", 20); far.Y = 10; heavy.Parts.Add(far);
        var slow = CraftChecks.Balance(Ctx(heavy));
        Assert(slow.Lines.Any(l => l.Text.Contains("quarter circle") && l.Value == "sluggish") && slow.Fixes.Any(f => f.Text.Contains("reaction wheel torque")), "40 t on 0.5 kNm is sluggish: " + slow.Text());
        var light = new CraftSheet();
        light.Parts.Add(Core().With(Mod("wheel").Set("torque", 0.5)));
        for (int i = 0; i < 4; i++) light.Parts.Add(Part("wheelLarge", "Advanced Reaction Wheel Module, Large", 0.2).With(Mod("wheel").Set("torque", 30)));
        var quick = CraftChecks.Balance(Ctx(light));
        Assert(quick.Rating == "oversized" && quick.Fixes.Any(f => f.Text.Contains("More reaction wheels") && f.Value.Contains("saves")), "four big wheels on a 1 t probe is oversized: " + quick.Text());
    }

    public static void TestLaunchLimitsAndThrustBothWays()
    {
        var s = new CraftSheet();
        s.Parts.Add(Core());
        var tank = Part("tank", "Tank", 20); s.Parts.Add(tank);
        s.Parts.Add(Part("poodle", "RE-L10 Poodle", 1.75, "stock", 0, 1).With(Mod("engine", "ModuleEngines", true).Set("thrust", 250).Set("ispVac", 350).Set("ispAsl", 90).Text("props", "LiquidFuel, Oxidizer")));
        s.Facts["massLimit"] = 18; s.Facts["siteLevel"] = 1; s.Facts["partLimit"] = 30; s.Facts["hallLevel"] = 1;
        var stages = new List<EngStage> { new EngStage { Stage = 0, DvVac = 2000, TwrVac = 1.2, TwrAsl = 0.3, StartMass = 21.9, EndMass = 10, ThrustVac = 250 } };
        var r = CraftChecks.Launch(Ctx(s, "", stages));
        Assert(r.Rating == "problem" && r.Lines.Any(l => l.Bad && l.Text.Contains("launch pad") && l.Value.Contains("over")), "over the pad's mass limit: " + r.Text());
        Assert(r.Lines.Any(l => l.Bad && l.Text.Contains("built for space")) && r.Lines.Any(l => l.Bad && l.Value.Contains("cannot lift off")), "a vacuum engine on the pad and a TWR of 0.3: " + r.Text());
        var strong = new List<EngStage> { new EngStage { Stage = 0, DvVac = 2000, TwrVac = 3.4, TwrAsl = 3.1, StartMass = 21.9, EndMass = 10, ThrustVac = 700 } };
        s.Facts.Clear();
        s.Parts[2].Modules[0].Num["ispAsl"] = 300;
        var o = CraftChecks.Launch(Ctx(s, "", strong));
        Assert(o.Rating == "oversized" && o.Fixes.Any(f => f.Text.Contains("times the weight")), "a lift-off TWR of 3.1 is more than needed: " + o.Text());
    }

    public static void TestCrewSuppliesAgainstTheTrip()
    {
        Func<double, CraftSheet> ship = food =>
        {
            var s = new CraftSheet();
            var pod = Part("mk1-3pod", "Mk1-3 Command Pod", 2.6); pod.Parent = -1; pod.Crew = 3;
            s.Parts.Add(pod.With(Mod("command").Set("crewMin", 1)));
            s.Parts.Add(Part("supplies", "Supply Container", 0.1).Holds("Supplies", food));
            s.Parts[1].ResMass = food * 0.001;
            s.ModFacts["USILifeSupport"] = "installed";
            s.Facts["day"] = 21600;
            return s;
        };
        // Three kerbals use 0.00015 supplies a second: Duna and back is roughly 600 days, about 1,900 units.
        var shortOf = CraftChecks.Crew(Ctx(ship(500), "land on Duna and return"));
        Assert(shortOf.Rating == "problem" && shortOf.Lines.Any(l => l.Bad && l.Text.Contains("Supplies runs out first")) && shortOf.Fixes.Any(f => f.Text.StartsWith("Carry ") && f.Text.Contains("more Supplies")), "500 supplies do not last to Duna and back: " + shortOf.Text());
        var plenty = CraftChecks.Crew(Ctx(ship(20000), "land on Duna and return"));
        Assert(plenty.Rating == "oversized" && plenty.Fixes.Any(f => f.Text.Contains("times the trip")), "20,000 supplies is far more than the trip needs: " + plenty.Text());
        var noPlan = CraftChecks.Crew(Ctx(ship(500)));
        Assert(noPlan.Note.Contains("Pick a destination") && noPlan.Lines.Any(l => l.Text.StartsWith("Supplies:") && l.Value.EndsWith("days")), "without a mission it still says how long supplies last: " + noPlan.Text());
        var probe = new CraftSheet(); probe.Parts.Add(Core());
        var pr = CraftChecks.Crew(Ctx(probe));
        Assert(pr.Rating == "uncrewed" && pr.Lines.Any(l => l.Value == "SAS level 1" && l.Text.Contains("prograde")), "a probe reports its SAS level: " + pr.Text());
    }

    public static void TestCostByStageAndAllChecksRun()
    {
        var s = new CraftSheet();
        s.Parts.Add(Core()); s.Parts[0].Cost = 450;
        var dec = Part("decoupler", "TD-12 Decoupler", 0.04, "stock", 1).With(Mod("decoupler", null, true)); dec.Cost = 200; s.Parts.Add(dec);
        var tank = Part("tank", "Tank", 5, "stock", -1, 1); tank.Cost = 3000; s.Parts.Add(tank);
        var c = Ctx(s);
        var cost = CraftChecks.Cost(c);
        Assert(cost.Rating == "3650 funds" && cost.Lines.Any(l => l.Text.StartsWith("Thrown away in stage 1 (2 parts)") && l.Value == "3200 funds") && cost.Lines.Any(l => l.Text.StartsWith("Stays to the end") && l.Value == "450 funds"), "cost splits by what is dropped: " + cost.Text());
        var all = CraftChecks.All(c);
        Assert(all.Count == 7 && all.All(x => x.Rating != "" && x.Title != "" && x.Rating != "not computed"), "every check runs and gives a rating: " + string.Join(", ", all.Select(x => x.Key + "=" + x.Rating).ToArray()));
        string text = NewsEngine.EngChecksText(all, "is my antenna strong enough?");
        Assert(text.Contains("COMMS (computed") && text.Contains("Other checks, in brief") && !text.Contains("COST (computed"), "the prompt gets the check the question is about in full and the rest in brief: " + text);
        Assert(NewsEngine.EngChecksText(all, "give me a full design review").Contains("COST (computed"), "a review gets every check in full");
    }

    public static void TestQuietPartLinesHideModBookkeeping()
    {
        var p = Part("fuelTank", "FL-T400 Fuel Tank", 0.25);
        p.With(new CraftModule { Name = "InterstellarFuelSwitch", Kind = "other" }.Set("selectedTankSetup", 6))
         .With(new CraftModule { Name = "IFSCryostat", Kind = "other" }.Set("powerReqKW", 2.5))
         .With(new CraftModule { Name = "Reliability", Kind = "other" }.Set("mtbf", 72576000))
         .With(new CraftModule { Name = "ModuleKethaneScanner", Kind = "other" }.Set("range", 5));
        string brief = CraftSheet.Does(p, null, true), full = CraftSheet.Does(p, null, false);
        Assert(!brief.Contains("IFSCryostat") && !brief.Contains("Reliability") && !brief.Contains("powerReqKW") && brief.Contains("mod modules: ModuleKethaneScanner"), "the short line names unknown modules and hides bookkeeping: " + brief);
        Assert(full.Contains("Reliability (mtbf=72576000)") && full.Contains("IFSCryostat (powerReqKW=2.5)"), "asking about the part brings the detail back: " + full);
        var curve = new CraftModule { Name = "X", Kind = "other" }.Text("c", "0:0;400:20;1000:200");
        Assert(Math.Abs(curve.Curve("c", 700) - 110) < 1e-6 && curve.Curve("c", 5000) == 200 && curve.Curve("c", -5) == 0, "curves read between their points");
    }
}
