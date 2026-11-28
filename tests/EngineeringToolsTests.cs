using System;
using System.Collections.Generic;
using System.Linq;
using KerbalNews;
using KerbalNews.Web;

/// <summary>The engineering tools on top of the checks: swaps, efficiency, compare, go/no-go, windows, contracts, science, lessons, in flight.</summary>
public static class EngineeringToolsTests
{
    static void Assert(bool c, string m) { if (!c) throw new Exception("Assertion failed: " + m); }

    public static CraftPart Part(string name, string title, double mass, double cost, string mod = "stock")
    {
        return new CraftPart { Name = name, Title = title, Mod = mod, DryMass = mass, Cost = cost, HasPos = true, CrashTol = 12, Parent = 0, Bulkhead = "size1, srf" };
    }
    public static CraftModule Engine(double thrust, double ispVac, double ispAsl, string props = "LiquidFuel, Oxidizer")
    {
        var m = new CraftModule { Name = "ModuleEngines", Kind = "engine", Staged = true };
        m.Num["thrust"] = thrust; m.Num["ispVac"] = ispVac; m.Num["ispAsl"] = ispAsl; m.Txt["props"] = props;
        return m;
    }
    public static CraftModule Solar(double rate) { var m = new CraftModule { Name = "ModuleDeployableSolarPanel", Kind = "solar" }; m.Num["rate"] = rate; return m; }

    public static List<EngBody> Bodies()
    {
        var sun = new EngBody { Name = "Sun", Mu = 1.1723328e18, Radius = 261600000, IsStar = true, HasSurface = false };
        var kerbin = new EngBody { Name = "Kerbin", Parent = "Sun", Mu = 3.5316e12, Radius = 600000, Gravity = 9.81, AtmoDepth = 70000, AtmoPressure = 1, AtmoDensity = 1.225, Sma = 13599840256, IsHome = true, SunDistance = 13599840256 };
        var mun = new EngBody { Name = "Mun", Parent = "Kerbin", Mu = 6.5138398e10, Radius = 200000, Gravity = 1.63, Sma = 12000000, SunDistance = 13599840256 };
        var duna = new EngBody { Name = "Duna", Parent = "Sun", Mu = 3.0136321e11, Radius = 320000, Gravity = 2.94, AtmoDepth = 50000, AtmoPressure = 0.0667, AtmoDensity = 0.08, Sma = 20726155264, SunDistance = 20726155264 };
        return new List<EngBody> { sun, kerbin, mun, duna };
    }

    public static EngSnapshot Craft()
    {
        var s = new CraftSheet();
        var core = Part("probeCoreOcto", "Probodobodyne OKTO", 0.1, 450); core.Parent = -1;
        core.Modules.Add(new CraftModule { Name = "ModuleCommand", Kind = "command" });
        core.Res.Add(new CraftRes { Name = CraftSheet.EC, Amount = 50, Max = 50 });
        s.Parts.Add(core);
        var tank = Part("fuelTank", "FL-T400 Fuel Tank", 0.25, 500); tank.ResMass = 2; s.Parts.Add(tank);
        var eng = Part("liquidEngine3", "LV-909 Terrier", 0.5, 390); eng.Stage = 0; eng.Modules.Add(Engine(60, 345, 85)); s.Parts.Add(eng);
        for (int i = 0; i < 6; i++) { var p = Part("solarPanels5", "OX-STAT Photovoltaic Panels", 0.005, 75); p.Modules.Add(Solar(0.35)); p.Surface = true; s.Parts.Add(p); }
        s.Prepare();
        var snap = new EngSnapshot { Scene = "vab", Vessel = "Hopper", Sheet = s, Bodies = Bodies(), Parts = s.Parts.Count, Mass = s.Parts.Sum(p => p.Mass), UT = 1000 };
        snap.Stages.Add(new EngStage { Stage = 0, DvVac = 345 * 9.80665 * Math.Log(2.88 / 0.88), TwrVac = 2.1, StartMass = 2.88, EndMass = 0.88, ThrustVac = 60, IspVac = 345 });
        snap.TotalDvVac = snap.Stages[0].DvVac;
        return snap;
    }

    public static void TestPartSwapsFindBetterAndCheaperParts()
    {
        var snap = Craft();
        var catalog = new List<CraftPart>();
        var better = Part("ionX", "LV-N2 Efficient", 0.4, 900, "NearFuturePropulsion"); better.Modules.Add(Engine(50, 400, 100)); catalog.Add(better);
        var wrongFuel = Part("nerv", "LV-N Nerv", 3, 10000); wrongFuel.Modules.Add(Engine(60, 800, 185, "LiquidFuel")); catalog.Add(wrongFuel);
        var wrongSize = Part("poodle", "RE-L10 Poodle", 1.75, 1300); wrongSize.Bulkhead = "size2"; wrongSize.Modules.Add(Engine(250, 350, 90)); catalog.Add(wrongSize);
        var cheapEng = Part("spark", "48-7S Spark", 0.5, 240); cheapEng.Modules.Add(Engine(55, 340, 265)); catalog.Add(cheapEng);
        var bigPanel = Part("largeSolar", "Gigantor XL", 0.3, 3000); bigPanel.Modules.Add(Solar(24.4)); catalog.Add(bigPanel);
        var cheapPanel = Part("flatPanel", "SP-L Blanket", 0.004, 30, "NearFutureSolar"); cheapPanel.Modules.Add(Solar(0.7)); catalog.Add(cheapPanel);
        var r = EngTools.Swaps(snap.Sheet, catalog, snap.Stages, false);
        Assert(r.Fixes.Any(f => f.Text.Contains("swap LV-909 Terrier for LV-N2 Efficient [NearFuturePropulsion]") && f.Value.Contains("m/s") && f.Value.StartsWith("+")), "a higher Isp engine on the same fuel and size is offered with its gain: " + r.Text());
        Assert(!r.Text().Contains("Nerv") && !r.Text().Contains("Poodle"), "engines on another fuel or stack size are not offered: " + r.Text());
        Assert(r.Fixes.Any(f => f.Text.Contains("Stage 0: 48-7S Spark") && f.Value.StartsWith("saves 150 funds")), "a cheaper engine that does nearly the same is offered: " + r.Text());
        Assert(r.Fixes.Any(f => f.Text.StartsWith("Lighter: 3 × SP-L Blanket") && f.Value.Contains("saves")), "three better panels replace six: " + r.Text());
        Assert(!r.Text().Contains("Gigantor"), "a panel far bigger than needed is not a swap");
        var eff = EngTools.Efficiency(null, new List<CheckReport>(), r);
        Assert(eff.Lines.Count >= 2 && eff.Rating.Contains("to save"), "the efficiency report gathers the savings: " + eff.Text());
        Assert(EngTools.Swaps(snap.Sheet, new List<CraftPart>(), snap.Stages, false).Rating == "no catalogue", "no catalogue, no swaps");
    }

    public static void TestSnapshotsCompareAndSurviveASave()
    {
        var snap = Craft();
        var before = EngTools.Snap("v1", "First", snap, null, new List<string>(), new List<CheckReport> { new CheckReport { Key = "comms", Title = "Comms", Rating = "good" } });
        var n = new DataNode("SNAP"); before.Save(n);
        var back = CraftSnap.Load(n);
        Assert(back.Name == "First" && back.Craft == "Hopper" && Math.Abs(back.Mass - before.Mass) < 1e-9 && back.Groups["OX-STAT Photovoltaic Panels"] == 6 && back.Ratings["Comms"] == "good", "a snapshot survives saving");
        snap.Sheet.Parts.RemoveAll(p => p.Name == "solarPanels5");
        snap.Parts = snap.Sheet.Parts.Count; snap.Mass -= 0.03; snap.TotalDvVac += 40;
        var now = EngTools.Snap("", "now", snap, null, new List<string>(), new List<CheckReport> { new CheckReport { Key = "comms", Title = "Comms", Rating = "no link" } });
        var c = EngTools.Compare(back, now);
        Assert(c.Lines.Any(l => l.Text == "Comms" && l.Value == "good → no link" && l.Bad) && c.Lines.Any(l => l.Text.StartsWith("Removed: −6 OX-STAT")) && c.Rating.StartsWith("+40 m/s"), "compare shows what changed: " + c.Text());
        Assert(EngTools.Compare(null, now).Rating == "no snapshot", "nothing to compare against");
    }

    public static void TestGoNoGoPollsEveryDiscipline()
    {
        var snap = Craft();
        var plan = new EngPlan { Mission = "orbit the Mun", Known = true, Have = 4000, Margin = -300, MarginPct = 10 };
        plan.Legs.Add(new EngLeg { Name = "x", Dv = 4300 });
        var checks = new List<CheckReport> { new CheckReport { Key = "comms", Title = "Comms", Rating = "good" }, new CheckReport { Key = "landing", Title = "Landing", Rating = "marginal" } };
        var bad = new CheckReport { Key = "balance", Title = "Balance", Rating = "problem" }; bad.Add("Stage 2 tumbles", "", true); checks.Add(bad);
        var r = EngTools.GoNoGo(snap, plan, null, new List<string>(), checks, new List<string> { "Hopper 1 was lost: chutes too early." });
        Assert(r.Rating == "NO-GO (2)" && r.Lines.Any(l => l.Text.StartsWith("Flight dynamics: 300 m/s short") && l.Value == "NO-GO") && r.Lines.Any(l => l.Text == "Guidance and control: Stage 2 tumbles" && l.Bad), "delta-v and balance are no-go: " + r.Text());
        Assert(r.Lines.Any(l => l.Text.StartsWith("Comms") && l.Value == "GO") && r.Lines.Any(l => l.Text.StartsWith("Landing") && l.Value == "GO, with a caution") && r.Lines.Any(l => l.Text.StartsWith("Lessons")), "the rest are go or cautions: " + r.Text());
        plan.Margin = 900;
        checks.Remove(bad);
        Assert(EngTools.GoNoGo(snap, plan, null, new List<string>(), checks, new List<string>()).Rating == "GO with 1 caution", "with the problems gone it is go");
    }

    public static void TestTransferWindowsFromThePlanetsPositions()
    {
        var snap = Craft();
        // Duna needs to be about 44 degrees ahead; put it at 60, so the window is a few weeks away.
        snap.Phases["Duna"] = 60;
        var r = EngTools.Windows(snap, "Duna", ut => "D" + (int)(ut / 21600));
        var line = r.Lines.First(l => l.Text.StartsWith("Duna"));
        Assert(line.Text.Contains("window opens in") && line.Text.Contains("flight") && line.Text.Contains("before the window home") && line.Tag == "your mission", "Duna gets a window, a flight time and the wait for the way home: " + r.Text());
        double days = double.Parse(line.Value.Replace(" d", ""), System.Globalization.CultureInfo.InvariantCulture);
        Assert(days > 20 && days < 80, "16 degrees to close at about 0.4 degrees a day: " + days);
        Assert(line.Text.Contains("repeat every") && r.Rating.StartsWith("Duna in"), "windows repeat on the synodic period: " + r.Text());
        Assert(r.Lines.Any(l => l.Text.StartsWith("Mun: a moon of Kerbin") && l.Value == "any time"), "a moon needs no window");
        snap.Phases["Duna"] = 44.4;
        Assert(EngTools.Windows(snap, "Duna").Lines.First().Value == "now", "at the ideal angle the window is open");
    }

    public static void TestContractsAndScienceAreReadAgainstTheCraft()
    {
        var snap = Craft();
        snap.PartTitles = new List<string> { "LV-909 Terrier", "Mk16 Parachute", "FL-T400 Fuel Tank" };
        var fits = new EngContract { Title = "Test LV-909 Terrier in orbit of Kerbin" };
        fits.Params.Add(new EngContractParam { Text = "Test LV-909 Terrier" }); fits.Params.Add(new EngContractParam { Text = "Orbit Kerbin" });
        var misses = new EngContract { Title = "Build a new station around Kerbin" };
        misses.Params.Add(new EngContractParam { Text = "Have an antenna" }); misses.Params.Add(new EngContractParam { Text = "Have a docking port" }); misses.Params.Add(new EngContractParam { Text = "Can generate power" }); misses.Params.Add(new EngContractParam { Text = "Have crew capacity for 5 kerbals" });
        snap.Contracts.Add(fits); snap.Contracts.Add(misses);
        var r = EngTools.Contracts(snap);
        Assert(r.Rating == "1 fits" && r.Lines.Any(l => l.Text.StartsWith("Test LV-909 Terrier") && l.Value == "fits"), "the part test fits: " + r.Text());
        Assert(r.Lines.Any(l => l.Text.StartsWith("Build a new station") && l.Text.Contains("missing Have an antenna; Have a docking port; Have crew capacity") && l.Value == "1 of 4"), "the station contract lists what is missing: " + r.Text());

        snap.Experiments.Add(new EngExperiment { Id = "temperatureScan", Title = "Temperature Scan", Situations = 63, Biomes = 7 });
        snap.Experiments.Add(new EngExperiment { Id = "mysteryGoo", Title = "Mystery Goo Observation", Situations = 48, Biomes = 0 });
        snap.ScienceDone["mysteryGoo@MunInSpaceLow"] = 1; snap.ScienceDone["mysteryGoo@MunInSpaceHigh"] = 1;
        snap.ScienceDone["temperatureScan@MunSrfLandedMidlands"] = 1;
        var plan = new EngPlan { Known = true, Body = "Mun", Land = true };
        var sc = EngTools.Science(snap, plan);
        Assert(sc.Rating == "1 of 2 still pay at Mun" && sc.Lines.Any(l => l.Text.StartsWith("Mystery Goo") && l.Value == "already done") && sc.Lines.Any(l => l.Text.StartsWith("Temperature Scan") && l.Text.Contains("1 biome done") && l.Text.Contains("in low orbit")), "science is read from the archive: " + sc.Text());
    }

    public static void TestLessonsAndInFlightWatch()
    {
        var snap = Craft();
        var incs = new List<Incident>
        {
            new Incident { Id = "i1", VesselName = "Hopper", Status = "done", Cause = "Parachutes opened too fast", Fixes = "Arm the chutes below 250 m/s." },
            new Incident { Id = "i2", VesselName = "Other", Status = "done", Cause = "The LV-909 Terrier was lit inside the fairing", Fixes = "Stage the fairing first." },
            new Incident { Id = "i3", VesselName = "Unrelated", Status = "done", Cause = "Ran out of snacks", Fixes = "Pack more." },
        };
        var l = EngTools.Lessons(incs, snap);
        Assert(l.Count == 2 && l.Any(x => x.StartsWith("Hopper was lost")) && l.Any(x => x.Contains("this craft also carries the LV-909 Terrier")), "lessons fit by craft name or by a part named in the findings: " + string.Join(" | ", l.ToArray()));

        snap.Scene = "flight"; snap.Body = "Mun"; snap.BodyGravity = 1.63; snap.BodyAtmosphere = 0; snap.Situation = "suborbital over Mun";
        snap.VerticalSpeed = -80; snap.Speed = 300; snap.Altitude = 9000; snap.RadarAlt = 6000;
        snap.Sheet.Parts[0].Res[0].Amount = 3;
        foreach (var p in snap.Sheet.Parts.Where(p => p.Has("solar"))) p.Deployed = 0;
        var f = EngTools.Flight(snap, null);
        // Thrust to weight 2.1 gives 20.6 - 1.63 = 19 m/s² of braking: 300 m/s takes about 2.4 km.
        Assert(f.Lines.Any(x => x.Text.StartsWith("Stopping from 300 m/s takes about 2.4 km") && x.Value.StartsWith("start by")), "the braking distance on an airless body: " + f.Text());
        Assert(f.Rating == "act now" && f.Lines.Any(x => x.Bad && x.Text.StartsWith("Electric charge 3 of 50")) && f.Lines.Any(x => x.Text.Contains("still folded")), "low charge and folded panels are flagged: " + f.Text());
        snap.RadarAlt = 2000;
        Assert(EngTools.Flight(snap, null).Lines.Any(x => x.Bad && x.Value == "burn now"), "too low to wait");
    }

    public static void TestTheToolTabsShowOnThePage()
    {
        var st = new NewsState();
        var t = new CoreTests.FakeTransport { Responder = r => CoreTests.AnthropicReply(r) };
        var eng = new NewsEngine(st, CoreTests.OnlineSettings(), t, new SimpleClock(100000));
        var snap = Craft(); snap.DeltaVReady = true; snap.Situation = "being built in the VAB";
        eng.EngineeringData = () => snap;
        var cheap = Part("spark", "48-7S Spark", 0.5, 240); cheap.Modules.Add(Engine(55, 340, 265));
        eng.EngineeringCatalog = () => new List<CraftPart> { cheap };
        string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "GameData/KerbalNews/Sites");
        var sites = new Sites(eng, rel => { var f = System.IO.Path.Combine(dir, rel); return System.IO.File.Exists(f) ? System.IO.File.ReadAllText(f) : null; });
        sites.Now = () => 100000;
        foreach (var tab in new[] { "gonogo", "windows", "contracts", "science", "efficiency", "swaps", "compare" })
        {
            sites.Handle("action:eng-tab/" + tab, null);
            string page = sites.Render("engineering.kb").Html;
            Assert(page.Contains("class=\"btitle\">") && page.Contains("class=\"row") && !page.Contains("not computed"), "the " + tab + " tab shows a computed report");
        }
        sites.Handle("action:eng-tab/swaps", null);
        Assert(sites.Render("engineering.kb").Html.Contains("48-7S Spark"), "the swaps tab reads the unlocked parts");
        sites.Handle("action:eng-snap", new Dictionary<string, string> { { "snapname", "first try" } });
        Assert(st.EngSnaps.Count == 1 && st.EngSnaps[0].Name == "first try", "a snapshot is kept under its name");
        string cmp = sites.Render("engineering.kb").Html;
        Assert(cmp.Contains("first try") && cmp.Contains("action:eng-snap-del/" + st.EngSnaps[0].Id), "the compare tab opens on the new snapshot");
        sites.Handle("action:eng-margin/25", null);
        Assert(st.EngMargin == 25, "the margin setting is kept");
        Assert(t.Sent.Count == 0, "none of this makes an AI call");
    }
}
