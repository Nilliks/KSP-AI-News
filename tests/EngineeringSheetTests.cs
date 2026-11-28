using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KerbalNews;
using KerbalNews.Web;

/// <summary>The engineering team's craft sheet: live parts, power, staging, and delta-v from the save's real bodies.</summary>
public static class EngineeringSheetTests
{
    static void Assert(bool c, string m) { if (!c) throw new Exception("Assertion failed: " + m); }

    static HttpResult Reply(string text)
    {
        var resp = new Dictionary<string, object> { { "content", new List<object> { new Dictionary<string, object> { { "type", "text" }, { "text", text } } } } };
        return new HttpResult { Status = 200, Body = Json.Write(resp) };
    }

    /// <summary>The stock system, optionally with every body's size scaled (surface gravity kept), as a rescale mod does.</summary>
    static List<EngBody> Bodies(double scale = 1)
    {
        Func<string, string, double, double, double, double, double, double, double, bool, EngBody> b = (name, parent, mu, radius, g, atmo, pressure, sma, rot, surface) =>
            new EngBody { Name = name, Parent = parent, Mu = mu * scale * scale, Radius = radius * scale, Gravity = g, AtmoDepth = atmo * (scale > 1 ? 1.25 : 1), AtmoPressure = pressure, Sma = sma * scale, RotPeriod = rot, HasSurface = surface };
        var list = new List<EngBody>
        {
            new EngBody { Name = "Sun", Mu = 1.1723328e18 * scale * scale, Radius = 261600000 * scale, IsStar = true, HasSurface = false },
            b("Kerbin", "Sun", 3.5316e12, 600000, 9.81, 70000, 1, 13599840256, 21549.4, true),
            b("Mun", "Kerbin", 6.5138398e10, 200000, 1.63, 0, 0, 12000000, 138984, true),
            b("Minmus", "Kerbin", 1.7658e9, 60000, 0.491, 0, 0, 47000000, 40400, true),
            b("Duna", "Sun", 3.0136321e11, 320000, 2.94, 50000, 0.0667, 20726155264, 65517.9, true),
            b("Ike", "Duna", 1.8568369e10, 130000, 1.10, 0, 0, 3200000, 65517.9, true),
            b("Eve", "Sun", 8.1717302e12, 700000, 16.7, 90000, 5, 9832684544, 80500, true),
            b("Jool", "Sun", 2.82528e14, 6000000, 7.85, 200000, 15, 68773560320, 36000, false),
        };
        list[1].IsHome = true;
        foreach (var e in list.Where(x => !x.IsStar)) { var p = list.First(x => x.Name == e.Parent); e.SunDistance = p.IsStar ? e.Sma : p.Sma; }
        return list;
    }

    static CraftPart Part(string name, string title, int stage = -1, int parent = -1, string mod = "stock", double mass = 0.1)
    {
        return new CraftPart { Name = name, Title = title, Stage = stage, Parent = parent, Mod = mod, DryMass = mass, Cost = 100 };
    }
    static CraftModule Mod(string kind, bool staged = false) { return new CraftModule { Name = "Module" + kind, Kind = kind, Staged = staged }; }
    static CraftModule Uses(CraftModule m, string res, double rate) { m.Rates.Add(new CraftRate { Res = res, PerSec = rate }); return m; }

    /// <summary>A Duna probe: core, wheel, two fixed panels, two batteries, a dish and a modded scanner.</summary>
    static CraftSheet Probe(int panels = 2)
    {
        var s = new CraftSheet();
        var core = Part("probeCoreHex", "Probodobodyne HECS");
        core.Modules.Add(Uses(Mod("command"), CraftSheet.EC, -0.025));
        var w = Uses(Mod("wheel"), CraftSheet.EC, -0.25); w.Num["torque"] = 0.5; core.Modules.Add(w);
        core.Res.Add(new CraftRes { Name = CraftSheet.EC, Amount = 10, Max = 10 });
        s.Parts.Add(core);
        for (int i = 0; i < panels; i++) { var p = Part("solarPanels5", "OX-STAT Photovoltaic Panels", -1, 0, "stock", 0.005); var m = Mod("solar"); m.Num["rate"] = 0.35; p.Modules.Add(m); p.Surface = true; s.Parts.Add(p); }
        for (int i = 0; i < 2; i++) { var p = Part("batteryBankMini", "Z-200 Rechargeable Battery Bank", -1, 0, "stock", 0.01); p.Res.Add(new CraftRes { Name = CraftSheet.EC, Amount = 200, Max = 200 }); s.Parts.Add(p); }
        var dish = Part("HighGainAntenna5", "HG-5 High Gain Antenna", -1, 0);
        var a = Mod("antenna"); a.Num["power"] = 5e6; a.Num["ec"] = 51.4; a.Num["mits"] = 5.71; a.Txt["type"] = "relay"; dish.Modules.Add(a); s.Parts.Add(dish);
        var scan = Part("SCANsat_Scanner", "SCAN RADAR Altimetry Sensor", -1, 0, "SCANsat");
        var sm = new CraftModule { Name = "SCANsat", Kind = "other" }; Uses(sm, CraftSheet.EC, -0.4); sm.Num["sensorType"] = 1; sm.Num["max_height"] = 500000; scan.Modules.Add(sm);
        scan.Info.Add("SCANsat: Altimetry sensor, best altitude 5 km to 500 km."); s.Parts.Add(scan);
        s.Prepare();
        return s;
    }

    public static void TestDeltaVComesFromTheRealBodies()
    {
        var bodies = Bodies();
        var kerbin = bodies[1];
        double up = DvPlanner.Ascent(kerbin);
        Assert(Math.Abs(up - 3400) < 150, "Kerbin to low orbit about 3,400: " + up);
        var mun = DvPlanner.Route(bodies, kerbin, bodies[2]);
        Assert(Math.Abs(mun.Transfer - 860) < 25 && Math.Abs(mun.Capture - 310) < 30 && Math.Abs(mun.Land - 580) < 40 && Math.Abs(mun.Ascent - 580) < 40, "Mun legs match the community map: " + mun.Transfer + " " + mun.Capture + " " + mun.Land + " " + mun.Ascent);
        var duna = DvPlanner.Route(bodies, kerbin, bodies[4]);
        Assert(Math.Abs(duna.Transfer - 1070) < 40 && Math.Abs(duna.Capture - 610) < 40 && duna.Ascent > 1100 && duna.Ascent < 1550 && duna.Aero, "Duna legs: " + duna.Transfer + " " + duna.Capture + " " + duna.Ascent);
        var ike = DvPlanner.Route(bodies, kerbin, bodies[5]);
        Assert(ike != null && ike.Capture > 100 && ike.Capture < 900 && ike.Land > 300 && ike.Land < 600, "a moon of another planet is routed through its planet: " + (ike == null ? "none" : ike.Capture + " " + ike.Land));
        double eve = DvPlanner.Ascent(bodies[6]);
        Assert(eve > 6500 && eve < 8600, "Eve ascent is the hard one: " + eve);
        Assert(DvPlanner.Route(bodies, kerbin, bodies[7]).NoSurface, "a gas giant has no surface");

        // A 2.5x rescale: the stock map is useless, the computed one follows the bodies.
        var big = Bodies(2.5);
        double bigUp = DvPlanner.Ascent(big[1]);
        Assert(bigUp > up * 1.4 && bigUp < up * 1.8, "a 2.5x Kerbin costs much more to leave: " + bigUp);
        var snap = new EngSnapshot { Scene = "vab", Vessel = "Test", StockSystem = false, Bodies = big, DeltaVReady = true };
        snap.Stages.Add(new EngStage { Stage = 1, DvVac = 6000, DvAsl = 5200, TwrAsl = 1.4, TwrVac = 1.6 });
        var plan = EngMissions.Plan("land on Duna and come back", snap);
        Assert(plan.Known && plan.Computed && plan.Body == "Duna" && plan.Land && plan.Return && plan.Legs.Count == 6, "the planner works on a rescaled system: " + plan.Note + " legs " + plan.Legs.Count);
        Assert(Math.Abs(plan.Legs[0].Dv - bigUp) < 1 && plan.Legs[0].Name.Contains("Kerbin") && plan.Margin < 0, "launch leg uses the rescaled number and the craft is short");
        Assert(EngMissions.PlanText(plan).Contains("real bodies"), "the plan says where its numbers come from");
        Assert(DvPlanner.Table(big, big[1]).Contains("Duna: transfer") && DvPlanner.Table(big, big[1]).Contains("Mun: transfer"), "reference table for the prompt");
        // Without body data the stock table still answers, as before.
        Assert(EngMissions.Plan("land on the Mun", new EngSnapshot { Scene = "vab" }).Total > 5000, "stock fallback");
        Assert(EngMissions.FindBody("fly to the sun and back", snap) == null && EngMissions.FindBody("orbit Ike", snap).Name == "Ike", "body names are matched as words");
    }

    public static void TestPowerBudgetIsJudgedBothWays()
    {
        var bodies = Bodies();
        var probe = Probe();
        var home = probe.Power(bodies[1], bodies[1], bodies);
        Assert(Math.Abs(home.SolarHome - 0.70) < 1e-9 && Math.Abs(home.Battery - 410) < 1e-9, "generation and storage add up: " + home.SolarHome + " " + home.Battery);
        Assert(Math.Abs(home.Always - 0.025) < 1e-9 && Math.Abs(home.Wheels - 0.25) < 1e-9 && Math.Abs(home.Working - 0.4) < 1e-9 && Math.Abs(home.Transmit - 51.4) < 1e-9, "each consumer lands in its bucket, the modded one too");
        Assert(home.Rating != "short" && home.Demand.Any(l => l.Text.Contains("SCAN RADAR") && l.Tag == "SCANsat"), "fine at Kerbin, modded part named with its mod: " + home.Rating);

        var duna = probe.Power(bodies[1], bodies[4], bodies);
        Assert(Math.Abs(duna.SolarThere - 0.70 * Math.Pow(13599840256.0 / 20726155264.0, 2)) < 1e-6 && duna.SolarThere < 0.31, "panels make less at Duna: " + duna.SolarThere);
        Assert(duna.Rating.StartsWith("short") && duna.There.Any(l => l.Bad), "short at Duna with the scanner on: " + duna.Rating);
        Assert(duna.ShadowSeconds > 300 && duna.ShadowSeconds < 1500 && duna.ShadowNeed > 0, "time in Duna's shadow: " + duna.ShadowSeconds);
        Assert(duna.Fixes.Any(f => f.Text.StartsWith("Add ") && f.Text.Contains("OX-STAT")), "the fix names the panel already on the craft: " + string.Join(" / ", duna.Fixes.Select(f => f.Text).ToArray()));

        // A panel that makes almost nothing (folded on the pad) must not turn into "add millions of panels":
        // trying that fix copied the part that many times and froze the game.
        var dark = Probe();
        foreach (var m in dark.Parts.SelectMany(x => x.Of("solar"))) m.Num["rate"] = 1e-9;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var dim = dark.Power(bodies[1], bodies[4], bodies);
        Assert(sw.ElapsedMilliseconds < 500, "a near-dead panel is judged at once: " + sw.ElapsedMilliseconds + " ms");
        Assert(!dim.Fixes.Any(f => f.Text.StartsWith("Add ") && f.Text.Contains("OX-STAT")) && dim.Fixes.Any(f => f.Text.Contains("makes almost nothing")), "the advice changes instead: " + string.Join(" / ", dim.Fixes.Select(f => f.Text).ToArray()));
        Assert(EngTools.With(dark, dark.Parts.First(x => x.Has("solar")).Name, 2000000).Parts.Count < dark.Parts.Count + 501, "a trial never copies more than a few hundred parts");
        string text = CraftSheet.PowerText(duna);
        Assert(text.Contains("POWER BUDGET") && text.Contains("rating: short") && text.Contains("at Duna") && text.Contains("Fix:"), "power text for the AI");

        var heavy = Probe(24).Power(bodies[1], bodies[1], bodies);
        Assert(heavy.Rating == "oversized" && heavy.Fixes.Any(f => f.Text.Contains("could go") && f.Value.Contains("saves")), "too many panels is called out with the saving: " + heavy.Rating);

        // An RTG works in the dark and a fuel cell lasts as long as its fuel.
        var rtg = Probe(0);
        var g = Part("rtg", "PB-NUK Radioisotope Thermoelectric Generator", -1, 0); g.Modules.Add(Uses(Mod("generator"), CraftSheet.EC, 0.75)); rtg.Parts.Add(g);
        var cell = Part("FuelCell", "Fuel Cell", -1, 0); var cm = Mod("converter"); Uses(cm, "LiquidFuel", -0.001); Uses(cm, CraftSheet.EC, 1.5); cell.Modules.Add(cm); rtg.Parts.Add(cell);
        var tank = Part("tank", "Oscar-B", -1, 0); tank.Res.Add(new CraftRes { Name = "LiquidFuel", Amount = 18, Max = 18 }); rtg.Parts.Add(tank);
        var clamp = Part("launchClamp1", "TT18-A Launch Stability Enhancer", 0, 0); clamp.Modules.Add(Mod("clamp", true)); clamp.Modules.Add(Uses(Mod("generator"), CraftSheet.EC, 1)); rtg.Parts.Add(clamp);
        rtg.Prepare();
        var pr = rtg.Power(bodies[1], bodies[7], bodies);
        Assert(Math.Abs(pr.Steady - 0.75) < 1e-9 && Math.Abs(pr.FuelCell - 1.5) < 1e-9, "RTG is constant, fuel cell separate, the launch clamp's generator is not counted: " + pr.Steady);
        Assert(pr.Sources.Any(l => l.Text.Contains("Fuel Cell") && l.Value.Contains("for 5 h")), "fuel cell life from the fuel aboard: " + string.Join(" / ", pr.Sources.Select(l => l.Text + "=" + l.Value).ToArray()));
        Assert(pr.Rating == "right-sized" || pr.Rating == "tight", "an RTG probe is fine at Jool: " + pr.Rating);
    }

    /// <summary>Pod on top, upper stage, decoupler, booster. Stage numbers count down as they fire.</summary>
    static CraftSheet Rocket(int chuteStage = 0, int upperEngineStage = 1, int decouplerStage = 1, bool rootAtBottom = false)
    {
        var s = new CraftSheet();
        var pod = Part("mk1pod", "Mk1 Command Pod", -1, -1, "stock", 0.8); pod.Crew = 1; pod.Modules.Add(Mod("command"));
        var chute = Part("parachuteSingle", "Mk16 Parachute", chuteStage, 0); chute.Modules.Add(Mod("chute", true));
        var tank2 = Part("fuelTank", "FL-T400 Fuel Tank", -1, 0, "stock", 0.25); tank2.Res.Add(new CraftRes { Name = "LiquidFuel", Amount = 180, Max = 180 }); tank2.Res.Add(new CraftRes { Name = "Oxidizer", Amount = 220, Max = 220 });
        var terrier = Part("liquidEngine3", "LV-909 \"Terrier\" Liquid Fuel Engine", upperEngineStage, 2, "stock", 0.5);
        var e2 = Mod("engine", true); e2.Num["thrust"] = 60; e2.Num["ispVac"] = 345; e2.Num["ispAsl"] = 85; e2.Txt["props"] = "LiquidFuel, Oxidizer"; terrier.Modules.Add(e2);
        var dec = Part("Decoupler_1", "TD-12 Decoupler", decouplerStage, 3, "stock", 0.04); dec.Modules.Add(Mod("decoupler", true));
        var tank1 = Part("fuelTank_long", "FL-T800 Fuel Tank", -1, 4, "stock", 0.5); tank1.Res.Add(new CraftRes { Name = "LiquidFuel", Amount = 360, Max = 360 }); tank1.Res.Add(new CraftRes { Name = "Oxidizer", Amount = 440, Max = 440 });
        var swivel = Part("liquidEngine2", "LV-T45 \"Swivel\" Liquid Fuel Engine", 2, 5, "stock", 1.5);
        var e1 = Mod("engine", true); e1.Num["thrust"] = 215; e1.Num["ispVac"] = 320; e1.Num["ispAsl"] = 250; e1.Txt["props"] = "LiquidFuel, Oxidizer"; swivel.Modules.Add(e1);
        s.Parts.AddRange(new[] { pod, chute, tank2, terrier, dec, tank1, swivel });
        if (rootAtBottom)
        {
            // The same rocket built from the booster up: every parent link points the other way.
            swivel.Parent = -1; tank1.Parent = 6; dec.Parent = 5; terrier.Parent = 4; tank2.Parent = 3; pod.Parent = 2; chute.Parent = 0;
            s.Root = 6;
        }
        s.Prepare();
        return s;
    }

    public static void TestStagingMistakesAreCaught()
    {
        var dv = new List<EngStage> { new EngStage { Stage = 2, DvVac = 1900, TwrAsl = 1.5 }, new EngStage { Stage = 1, DvVac = 2100, TwrVac = 1.1 } };
        var good = Rocket();
        Assert(good.Parts[6].DropStage == 1 && good.Parts[5].DropStage == 1 && good.Parts[3].DropStage == -1 && good.Parts[0].DropStage == -1, "booster drops in stage 1, the upper stage stays");
        var none = good.StagingIssues(dv);
        Assert(none.Count == 0, "a correct rocket has no staging problems: " + string.Join(" / ", none.ToArray()));
        var ev = good.Stages(dv);
        Assert(ev.Count == 3 && ev[0].Stage == 2 && ev[0].Fires[0].Contains("Swivel") && ev[1].Fires.Any(f => f.Contains("Terrier")) && ev[1].Fires.Any(f => f.Contains("Decoupler")) && ev[2].Fires[0].Contains("Parachute"), "what fires, in order");

        // Built from the bottom: the tree is re-rooted at the pod, so the answer is the same.
        var flipped = Rocket(0, 1, 1, true);
        Assert(flipped.Root == 0 && flipped.Parts[6].DropStage == 1 && flipped.Parts[3].DropStage == -1 && flipped.StagingIssues(dv).Count == 0, "root part at the bottom gives the same staging: " + string.Join(" / ", flipped.StagingIssues(dv).ToArray()));

        var chutes = Rocket(2).StagingIssues(dv);
        Assert(chutes.Any(x => x.StartsWith("!") && x.Contains("Parachutes deploy in stage 2")), "parachutes staged with the first engines: " + string.Join(" / ", chutes.ToArray()));
        var late = Rocket(0, 1, 0).StagingIssues(dv);
        Assert(late.Any(x => x.StartsWith("!") && x.Contains("Terrier") && x.Contains("fires into the stage below")), "upper engine lit before the booster is dropped: " + string.Join(" / ", late.ToArray()));

        var dry = Rocket();
        foreach (var p in dry.Parts) p.Res.RemoveAll(r => r.Name == "Oxidizer");
        Assert(dry.StagingIssues(dv).Any(x => x.StartsWith("!") && x.Contains("Oxidizer") && x.Contains("none aboard")), "an engine with nothing to burn");

        var nofuel = Rocket().StagingIssues(new List<EngStage> { new EngStage { Stage = 2, DvVac = 1900 } });
        Assert(nofuel.Any(x => x.Contains("stage 1 show no delta-v")), "a stage the game gives no delta-v: " + string.Join(" / ", nofuel.ToArray()));

        var burningNow = Rocket(); burningNow.Flying = true;
        var inFlight = burningNow.StagingIssues(new List<EngStage> { new EngStage { Stage = 2, DvVac = 0 }, new EngStage { Stage = 1, DvVac = 900 } });
        Assert(!inFlight.Any(x => x.Contains("show no delta-v")), "in flight the stage that is burning is not called out for reading 0: " + string.Join(" / ", inFlight.ToArray()));

        var clamped = Rocket();
        var c = Part("launchClamp1", "TT18-A Launch Stability Enhancer", 3, 5); c.Modules.Add(Mod("clamp", true)); c.Surface = true; clamped.Parts.Add(c); clamped.Prepare();
        Assert(clamped.StagingIssues(dv).Any(x => x.Contains("Launch clamps release in stage 3, before the engines ignite")), "clamps released before ignition");

        var missing = Rocket().Missing(true, true, true);
        Assert(missing.Any(x => x.Contains("No reaction wheels or RCS")) && missing.Any(x => x.StartsWith("!") && x.Contains("No docking port")) && missing.Any(x => x.Contains("ladder")), "missing parts for a crewed landing with docking: " + string.Join(" / ", missing.ToArray()));
    }

    public static void TestTheTeamSeesEveryPartAndAsksWhenUnsure()
    {
        var s = new NewsState();
        string body = "", system = "";
        string reply = "{\"replies\":[{\"who\":\"gene\",\"text\":\"That depends on where it sets down.\"}],\"ask\":{\"question\":\"Where is it landing?\",\"options\":[\"Kerbin\",\"Mun\",\"Duna\"]}}";
        var t = new CoreTests.FakeTransport { Responder = r =>
        {
            if (!r.Body.Contains("engineering team of")) return CoreTests.AnthropicReply(r);
            body = r.Body; system = r.Body;
            return Reply(reply);
        } };
        var eng = new NewsEngine(s, CoreTests.OnlineSettings(), t, new SimpleClock(100000));
        var snap = new EngSnapshot { Scene = "vab", Vessel = "Duna Surveyor 2", Situation = "being built in the VAB", Parts = 8, Mass = 1.2, DeltaVReady = true, StockSystem = false, Bodies = Bodies(), Sheet = Probe() };
        snap.Modules["ModuleCommand"] = 1; snap.Modules["ModuleDeployableSolarPanel"] = 2; snap.Modules["ModuleDataTransmitter"] = 1; snap.ResourceMax["ElectricCharge"] = 410;
        eng.EngineeringData = () => snap;

        Assert(eng.EngSend("will this land safely?", 100000), "sent");
        Assert(body.Contains("CRAFT SHEET") && body.Contains("2 × OX-STAT Photovoltaic Panels") && body.Contains("[SCANsat]") && body.Contains("SCANsat (uses 0.4 EC/s"), "the prompt carries the whole parts list, modded parts included");
        Assert(body.Contains("POWER BUDGET") && body.Contains("STAGING CHECK") && body.Contains("NO MISSION IS SET"), "the prompt carries the computed budgets and says no mission is set");
        Assert(system.Contains("Never say you cannot see the parts list") && system.Contains("ASK BEFORE YOU GUESS") && system.Contains("Delta-v reference computed for this system"), "the team is told it sees the craft, to ask first, and gets computed delta-v");
        Assert(!body.Contains("Detail for SCAN RADAR"), "tooltips are only sent for parts the question is about");
        var last = s.EngChat.Last();
        Assert(last.Role == "gene" && last.Text.Contains("Where is it landing?") && last.Actions == "opt:Kerbin|Mun|Duna", "the question and its quick answers are kept: " + last.Text + " / " + last.Actions);

        string dir = Path.Combine(Directory.GetCurrentDirectory(), "GameData/KerbalNews/Sites");
        var sites = new Sites(eng, rel => { var f = Path.Combine(dir, rel); return File.Exists(f) ? File.ReadAllText(f) : null; });
        sites.Now = () => 100000;
        string page = sites.Render("engineering.kb").Html;
        Assert(page.Contains("action:eng-ask/Duna") && page.Contains("Where is it landing?"), "quick-answer buttons show under the question");
        Assert(page.Contains("live from the builder") && page.Contains("action:eng-menu/tabs:Checks") && page.Contains("action:eng-menu/dest") && !page.Contains("action:eng-pick/dest/Duna"), "live note, and the tabs and mission picker as closed drop-downs");
        sites.Handle("action:eng-menu/dest", null);
        Assert(sites.Render("engineering.kb").Html.Contains("action:eng-pick/dest/Duna"), "the Go to drop-down opens to the list of worlds");
        sites.Handle("action:eng-menu/tabs:Checks", null);
        page = sites.Render("engineering.kb").Html;
        Assert(page.Contains("action:eng-tab/power") && page.Contains("action:eng-tab/crew") && !page.Contains("action:eng-pick/dest/Duna"), "one drop-down is open at a time, and Checks lists its tabs");
        sites.Handle("action:eng-menu/tabs:Checks", null);
        Assert(sites.EngMenu == "", "clicking an open drop-down closes it");

        reply = "{\"replies\":[{\"who\":\"linus\",\"text\":\"On Duna the chutes alone will not stop it.\"}]}";
        sites.Handle("action:eng-ask/Duna", null);
        Assert(s.EngMission.Contains("land") && s.EngMission.Contains("Duna"), "the answer sets the mission, so they only ask once: " + s.EngMission);
        Assert(body.Contains("Mission plan for") && body.Contains("at Duna") && !body.Contains("NO MISSION IS SET"), "the next call knows the mission and judges power at Duna");
        Assert(s.EngChat.Last().Actions == "" && !sites.Render("engineering.kb").Html.Contains("action:eng-ask/Mun"), "old quick answers go away");

        eng.EngSend("what does the SCAN RADAR sensor need?", 100000);
        Assert(body.Contains("Detail for SCAN RADAR Altimetry Sensor: SCANsat: Altimetry sensor"), "the tooltip is added for the part being asked about");

        sites.Handle("action:eng-tab/power", null);
        page = sites.Render("engineering.kb").Html;
        Assert(page.Contains("Generation and storage") && page.Contains("The same panels at Duna") && page.Contains("Try a fix") && page.Contains("SCANsat"), "power tab");
        sites.Handle("action:eng-tab/parts", null);
        page = sites.Render("engineering.kb").Html;
        Assert(page.Contains("7 parts, grouped") && page.Contains("2 × Z-200 Rechargeable Battery Bank") && page.Contains("Modded parts from: SCANsat (1)"), "parts tab");
        sites.Handle("action:eng-tab/stages", null);
        Assert(sites.Render("engineering.kb").Html.Contains("Staging check"), "stages tab");
        sites.Handle("action:eng-tab/problems", null);
        Assert(sites.Render("engineering.kb").Html.Contains("Everything the checks found"), "problems tab");
        foreach (var tab in new[] { "heat", "comms", "landing", "balance", "launch", "crew", "cost" })
        {
            sites.Handle("action:eng-tab/" + tab, null);
            page = sites.Render("engineering.kb").Html;
            Assert(page.Contains("class=\"btitle\">" + char.ToUpperInvariant(tab[0]) + tab.Substring(1) + ":") && page.Contains("class=\"row"), "the " + tab + " tab shows its report: " + (page.Length > 300 ? "" : page));
        }
        sites.Handle("action:eng-tab/comms", null);
        Assert(sites.Render("engineering.kb").Html.Contains("At Duna when it is farthest"), "comms are judged at the mission's destination");
        sites.Handle("action:eng-tab/overview", null);
        page = sites.Render("engineering.kb").Html;
        Assert(page.Contains("All checks") && page.Contains("action:eng-tab/landing\">Landing</a>"), "the overview has the scorecard with a link to each check");
        Assert(body.Contains("COMPUTED CHECKS") && body.Contains("Other checks, in brief"), "the prompt carries the computed checks");

        int sent = t.Sent.Count;
        sites.Handle("action:eng-pick/dest/Mun", null);
        sites.Handle("action:eng-pick/trip/return", null);
        Assert(s.EngMission == "Land on Mun and return" && t.Sent.Count == sent, "the mission picker rewrites the mission with no AI call: " + s.EngMission);
        sites.Handle("action:eng-pick/goal/orbit", null);
        Assert(s.EngMission == "Orbit Mun and return", "goal choice: " + s.EngMission);

        // A big craft stays a small prompt: identical parts fold into one line.
        var big = Probe(300);
        string sheetText = big.Text(null, "");
        Assert(sheetText.Contains("300 × OX-STAT") && sheetText.Length < 3000, "300 identical parts are one line: " + sheetText.Length);
    }
}
