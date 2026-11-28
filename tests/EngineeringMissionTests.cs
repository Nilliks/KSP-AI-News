using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KerbalNews;
using KerbalNews.Web;

/// <summary>Part 4 of the engineering advisors: mission tools, the team meeting and team memory.</summary>
public static class EngineeringMissionTests
{
    static void Assert(bool c, string m) { if (!c) throw new Exception("Assertion failed: " + m); }
    static CraftPart Part(string name, string title, double mass, double cost, string mod = "stock") { return EngineeringToolsTests.Part(name, title, mass, cost, mod); }
    static CraftModule Mod(string name, string kind) { return new CraftModule { Name = name, Kind = kind }; }
    static HttpResult Reply(string text)
    {
        var resp = new Dictionary<string, object> { { "content", new List<object> { new Dictionary<string, object> { { "type", "text" }, { "text", text } } } } };
        return new HttpResult { Status = 200, Body = Json.Write(resp) };
    }

    /// <summary>A two-stage rocket: a booster stage dropped at stage 1, and an upper stage that stays.</summary>
    static EngSnapshot Rocket()
    {
        var s = new CraftSheet();
        var pod = Part("pod", "Mk1 Command Pod", 0.8, 600); pod.Parent = -1; pod.Crew = 1; pod.Modules.Add(Mod("ModuleCommand", "command")); pod.Y = 6;
        pod.Res.Add(new CraftRes { Name = CraftSheet.EC, Amount = 50, Max = 50 });
        var tank2 = Part("t2", "FL-T400 Fuel Tank", 0.25, 500); tank2.ResMass = 2; tank2.Res.Add(new CraftRes { Name = "LiquidFuel", Amount = 180, Max = 180 }); tank2.Res.Add(new CraftRes { Name = "Oxidizer", Amount = 220, Max = 220 }); tank2.Y = 4;
        var eng2 = Part("terrier", "LV-909 Terrier", 0.5, 390); eng2.Stage = 1; eng2.Modules.Add(EngineeringToolsTests.Engine(60, 345, 85)); eng2.Y = 3;
        var dec = Part("dec", "TD-12 Decoupler", 0.04, 200); dec.Stage = 2; dec.Modules.Add(new CraftModule { Name = "ModuleDecouple", Kind = "decoupler", Staged = true }); dec.Y = 2.5;
        var tank1 = Part("t1", "FL-T800 Fuel Tank", 0.5, 800); tank1.ResMass = 4; tank1.Res.Add(new CraftRes { Name = "LiquidFuel", Amount = 360, Max = 360 }); tank1.Res.Add(new CraftRes { Name = "Oxidizer", Amount = 440, Max = 440 }); tank1.Y = 1;
        var tank1b = Part("t1", "FL-T800 Fuel Tank", 0.5, 800); tank1b.ResMass = 4; tank1b.Res.Add(new CraftRes { Name = "LiquidFuel", Amount = 360, Max = 360 }); tank1b.Res.Add(new CraftRes { Name = "Oxidizer", Amount = 440, Max = 440 }); tank1b.Y = 0;
        var eng1 = Part("swivel", "LV-T45 Swivel", 1.5, 1200); eng1.Stage = 3; eng1.Modules.Add(EngineeringToolsTests.Engine(215, 320, 250)); eng1.Y = -1;
        foreach (var p in new[] { pod, tank2, eng2, dec, tank1, tank1b, eng1 }) s.Parts.Add(p);
        for (int i = 1; i < s.Parts.Count; i++) s.Parts[i].Parent = i - 1;
        foreach (var m in s.Parts.SelectMany(p => p.Modules).Where(m => m.Kind == "engine")) m.Staged = true;
        s.Prepare();
        var snap = new EngSnapshot { Scene = "vab", Vessel = "Pathfinder", Situation = "being built in the VAB", Sheet = s, Bodies = EngineeringToolsTests.Bodies(), Parts = s.Parts.Count, Mass = s.Parts.Sum(p => p.Mass), UT = 1000, CrewCapacity = 1 };
        snap.Stages = SheetDv.Solve(s);
        snap.TotalDvVac = snap.Stages.Sum(x => x.DvVac); snap.DeltaVReady = true;
        return snap;
    }

    public static void TestDeltaVIsWorkedOutFromTheSheetAlone()
    {
        var snap = Rocket();
        var st = snap.Stages;
        Assert(snap.Sheet.Parts.First(p => p.Name == "swivel").DropStage == 2 && snap.Sheet.Parts.First(p => p.Name == "pod").DropStage < 0, "the booster leaves with the decoupler: " + string.Join(",", snap.Sheet.Parts.Select(p => p.Name + ":" + p.DropStage).ToArray()));
        Assert(st.Count == 2 && st[0].Stage == 3 && st[1].Stage == 1, "two burning stages: " + string.Join(",", st.Select(x => x.Stage + "=" + x.DvVac.ToString("0")).ToArray()));
        // Booster: 14.09 t down to 6.09 t at 320 s. Upper: 3.55 t down to 1.55 t at 345 s.
        double m0 = 0.8 + 0.25 + 2 + 0.5 + 0.04 + 1 + 8 + 1.5;
        Assert(Math.Abs(st[0].StartMass - m0) < 0.01 && Math.Abs(st[0].DvVac - 320 * 9.80665 * Math.Log(m0 / (m0 - 8))) < 1, "booster stage from the rocket equation: " + st[0].DvVac);
        Assert(Math.Abs(st[1].DvVac - 345 * 9.80665 * Math.Log(3.55 / 1.55)) < 1 && Math.Abs(st[1].TwrVac - 60 / (3.55 * 9.80665)) < 0.01, "upper stage: " + st[1].DvVac);
        Assert(Math.Abs(st[0].TwrAsl - 215 * 250.0 / 320 / (m0 * 9.80665)) < 0.01, "sea-level thrust to weight");
    }

    public static void TestTimelineNeedsAndAerobraking()
    {
        var snap = Rocket();
        var plan = EngMissions.Plan("Land on Duna and return", snap);
        var dest = EngMissions.Find("Duna", snap);
        var duna = snap.BodyNamed("Duna");
        double wait = EngMissionTools.WaitForReturn(snap.Bodies, snap.Home, duna);
        Assert(wait > 100 * 21600 && wait < 600 * 21600 && EngMissionTools.WaitForReturn(snap.Bodies, snap.Home, snap.BodyNamed("Mun")) == 0, "the wait at Duna for the way home is months; a moon has none: " + wait / 21600);
        var t = EngMissionTools.Timeline(snap, plan, dest, null, new List<CheckReport>());
        Assert(t.Lines.Any(l => l.Text.Contains("wait at Duna")) && t.Lines.Any(l => l.Text.StartsWith("Day 1: Launch")) && t.Lines.Any(l => l.Text == "Whole trip"), "the timeline lays out the legs and the wait: " + t.Text());
        Assert(t.Rating == "runs out of delta-v" && t.Bad, "a small rocket does not make the round trip: " + t.Rating);
        var need = EngMissionTools.Requirements(snap, plan, dest, null, new List<CheckReport>(), 1);
        Assert(need.Lines.Any(l => l.Text.StartsWith("Delta-v:") && l.Bad) && need.Lines.Any(l => l.Text.StartsWith("Power at Duna: sunlight is 43%")) && need.Lines.Any(l => l.Text.StartsWith("Coming home: entry at Kerbin")), "what the mission needs: " + need.Text());
        Assert(need.Rating.EndsWith("not met"), need.Rating);
        var bare = new EngSnapshot { Scene = "spacecenter", Bodies = snap.Bodies };
        var open = EngMissionTools.Requirements(bare, EngMissions.Plan("Orbit Mun", bare), EngMissions.Find("Mun", bare), null, null, 0);
        Assert(open.Rating == "for any craft" && open.Lines.Any(l => l.Text.StartsWith("Delta-v:")), "the needs sheet works with no craft open: " + open.Text());
        var aero = EngMissionTools.Aerobrake(snap, plan, dest);
        Assert(aero.Lines.Count(l => l.Text.StartsWith("At ")) == 2 && aero.Lines.Any(l => l.Text.Contains("periapsis of about")) && aero.Rating == "needs a shield", "aerobraking at Duna and at home, and no shield: " + aero.Text());
    }

    public static void TestReliabilityOnlyWhenKerbalismHasItOn()
    {
        var snap = Rocket();
        var sheet = snap.Sheet;
        Assert(EngMissionTools.Reliability(sheet, snap.Stages, null, 0).Rating == "parts do not fail", "no failure mod");
        sheet.ModFacts["Kerbalism"] = "installed"; sheet.ModFacts["Kerbalism.Reliability"] = "off";
        var off = EngMissionTools.Reliability(sheet, snap.Stages, null, 0);
        Assert(off.Rating == "off in this save" && off.Lines.Count == 1 && !off.Bad, "installed but switched off: no advice");
        sheet.ModFacts["Kerbalism.Reliability"] = "on";
        var rel = new CraftModule { Name = "Reliability", Kind = "other" }; rel.Num["rated_operation_duration"] = 60; rel.Num["rated_ignitions"] = 1; rel.Num["mtbf"] = 1e9;
        sheet.Parts.First(p => p.Name == "terrier").Modules.Add(rel);
        var plan = EngMissions.Plan("Orbit Mun and return", snap);
        var on = EngMissionTools.Reliability(sheet, snap.Stages, plan, 0);
        Assert(on.Lines.Any(l => l.Bad && l.Value == "past its rating") && on.Fixes.Count > 0 && on.Rating == "problem", "the upper engine burns longer than it is rated for: " + on.Text());
        var gyro = Part("gyro", "Small Reaction Wheel", 0.05, 600); var r2 = new CraftModule { Name = "Reliability", Kind = "other" }; r2.Num["mtbf"] = 100 * 21600; gyro.Modules.Add(r2); sheet.Parts.Add(gyro);
        var trip = EngMissionTools.Reliability(sheet, snap.Stages, plan, 300 * 21600);
        Assert(trip.Lines.Any(l => l.Text.StartsWith("Small Reaction Wheel: about 95% chance") && l.Text.EndsWith("there is only one")), "a part likely to fail on a long trip with no spare: " + trip.Text());
    }

    public static void TestSetupMiningAndDocking()
    {
        var snap = Rocket();
        var sheet = snap.Sheet;
        Assert(EngMissionTools.Setup(sheet, 1).Rating == "not read", "no action data, no guessing");
        for (int i = 0; i < 2; i++)
        {
            var panel = Part("panel", "SP-L Solar Panel", 0.025, 440); panel.Deployed = 0; var sm = EngineeringToolsTests.Solar(1.6); sm.Num["actions"] = 3; panel.Modules.Add(sm); sheet.Parts.Add(panel);
        }
        var setup = EngMissionTools.Setup(sheet, 1);
        Assert(setup.Lines.Any(l => l.Text == "2 of 2 solar panels that fold on no action group") && setup.Lines.Any(l => l.Text.StartsWith("Crew aboard and nothing on the Abort key")) && setup.Rating == "2 suggestions", "panels and abort: " + setup.Text());
        foreach (var p in sheet.Parts.Where(p => p.Name == "panel")) p.Modules[0].Txt["groups"] = "Toggle Solar Panel>Custom01";
        sheet.Parts[0].Modules[0].Txt["groups"] = "Stage>Abort";
        Assert(EngMissionTools.Setup(sheet, 1).Rating == "ok", "set up: " + EngMissionTools.Setup(sheet, 1).Text());

        Assert(EngMissionTools.Mining(sheet, null, null).Rating == "no mining gear", "nothing to mine with");
        var drill = Part("drill", "Drill-O-Matic", 0.25, 1000); var dm = Mod("ModuleResourceHarvester", "drill"); dm.Txt["mines"] = "Ore"; dm.Rates.Add(new CraftRate { Res = CraftSheet.EC, PerSec = -15 }); dm.Rates.Add(new CraftRate { Res = "Ore", PerSec = 1.5 }); drill.Modules.Add(dm); sheet.Parts.Add(drill);
        var isru = Part("isru", "Convert-O-Tron 125", 1.25, 1000); var cm = Mod("ModuleResourceConverter", "converter"); cm.Label = "Lf+Ox"; cm.Rates.Add(new CraftRate { Res = CraftSheet.EC, PerSec = -30 }); cm.Rates.Add(new CraftRate { Res = "Ore", PerSec = -0.5 }); cm.Rates.Add(new CraftRate { Res = "LiquidFuel", PerSec = 0.225 }); cm.Rates.Add(new CraftRate { Res = "Oxidizer", PerSec = 0.275 }); isru.Modules.Add(cm); sheet.Parts.Add(isru);
        var power = sheet.Power(snap.Home, snap.Home, snap.Bodies);
        var mine = EngMissionTools.Mining(sheet, null, power);
        Assert(mine.Lines.Any(l => l.Bad && l.Text.StartsWith("Drills bring up Ore but no tank holds it")) && mine.Lines.Any(l => l.Bad && l.Value == "short of power") && mine.Lines.Any(l => l.Text.StartsWith("Filling the 900 LiquidFuel aboard")), "no ore tank, not enough power, and the fill-up time: " + mine.Text());

        Assert(EngMissionTools.Docking(sheet, snap.Mass).Rating == "no docking port", "no port");
        var port = Part("port", "Clamp-O-Tron Docking Port", 0.05, 280); var pm = Mod("ModuleDockingNode", "dock"); pm.Txt["size"] = "size1"; port.Modules.Add(pm); port.Y = 7; sheet.Parts.Add(port);
        var dock = EngMissionTools.Docking(sheet, snap.Mass);
        Assert(dock.Lines.Any(l => l.Text.Contains("fits standard (1.25 m) ports")) && dock.Lines.Any(l => l.Text.StartsWith("No RCS thrusters")) && dock.Fixes.Count > 0, "a port but no thrusters: " + dock.Text());
    }

    public static void TestAscentLauncherAndProvenRockets()
    {
        var snap = Rocket();
        var asc = EngMissionTools.Ascent(snap, new List<CheckReport>());
        Assert(asc.Lines.Any(l => l.Text.StartsWith("Lift-off thrust to weight 1.22")) && asc.Lines.Any(l => l.Text.StartsWith("Gravity turn")) && asc.Lines.Any(l => l.Text.StartsWith("Stage 1 takes over")), "the climb, stage by stage: " + asc.Text());
        Assert(asc.Lines.Any(l => l.Text.StartsWith("Orbit at Kerbin takes about")), asc.Text());
        // Payload by bisection: a stage of 10 t to 4 t at 300 s gives 2696 m/s; carrying 2 t more leaves 300 g0 ln(12/6) = 2039.
        var one = new List<EngStage> { new EngStage { Stage = 0, StartMass = 10, EndMass = 4, DvVac = 300 * 9.80665 * Math.Log(2.5), IspVac = 300 } };
        Assert(Math.Abs(EngMissionTools.Payload(one, 300 * 9.80665 * Math.Log(2)) - 2) < 0.01 && EngMissionTools.Payload(one, 5000) == 0, "payload for a given delta-v");
        var proven = new List<ProvenRocket> { new ProvenRocket { Name = "Old Faithful", LaunchMass = 40, OrbitMass = 5, Cost = 20000, Flights = 3 } };
        var lr = EngMissionTools.Launcher(snap, proven);
        Assert(lr.Lines.Any(x => x.Text.StartsWith("Proven: Old Faithful, 40.0 t at lift-off, put 5.0 t in orbit") && x.Value == "3 flights"), "proven rockets are listed: " + lr.Text());
        Assert(lr.Rating.StartsWith("lifts ") || lr.Rating == "does not reach orbit", lr.Rating);
    }

    public static void TestPlaneAndRoverChecks()
    {
        var s = new CraftSheet();
        var cockpit = Part("cockpit", "Mk1 Cockpit", 1.25, 1200); cockpit.Parent = -1; cockpit.Crew = 1; cockpit.Modules.Add(Mod("ModuleCommand", "command")); cockpit.Y = 3;
        var body = Part("fuse", "Mk1 Liquid Fuel Fuselage", 0.25, 550); body.ResMass = 2; body.Res.Add(new CraftRes { Name = "LiquidFuel", Amount = 400, Max = 400 }); body.Y = 1;
        var jet = Part("jet", "J-33 Wheesley", 1.5, 1400); jet.Stage = 0; var jm = EngineeringToolsTests.Engine(120, 10500, 10500, "LiquidFuel, IntakeAir"); jm.Staged = true; jet.Modules.Add(jm); jet.Y = -1;
        s.Parts.Add(cockpit); s.Parts.Add(body); s.Parts.Add(jet);
        for (int i = 0; i < 2; i++) { var w = Part("wing", "Swept Wing", 0.1, 500); var wm = Mod("ModuleLiftingSurface", "wing"); wm.Num["lift"] = 1; w.Modules.Add(wm); w.Y = 2.5; w.X = i == 0 ? -2 : 2; s.Parts.Add(w); }
        s.Prepare();
        Assert(EngMissionTools.IsPlane(s, true) && !EngMissionTools.IsRover(s), "wings and a jet make it a plane");
        var p = EngMissionTools.Plane(s, EngineeringToolsTests.Bodies()[1]);
        Assert(p.Lines.Any(l => l.Value == "unstable" && l.Bad) && p.Lines.Any(l => l.Value == "no air" && l.Bad) && p.Lines.Any(l => l.Value == "cannot steer") && p.Lines.Any(l => l.Value == "needs three"), "lift ahead of the centre of mass, no intake, no control surfaces, no wheels: " + p.Text());
        foreach (var w in s.Parts.Where(x => x.Name == "wing")) w.Y = 0.5;
        var intake = Part("intake", "Circular Intake", 0.1, 680); intake.Modules.Add(Mod("ModuleResourceIntake", "intake")); intake.Y = 4; s.Parts.Add(intake);
        var p2 = EngMissionTools.Plane(s, EngineeringToolsTests.Bodies()[1]);
        Assert(p2.Lines.Any(l => l.Value == "stable") && !p2.Lines.Any(l => l.Value == "no air"), "moved back and given air: " + p2.Text());

        var r = new CraftSheet();
        var core = Part("core", "Probodobodyne RoveMate", 0.15, 800); core.Parent = -1; core.Modules.Add(Mod("ModuleCommand", "command")); core.Y = 3; r.Parts.Add(core);
        for (int i = 0; i < 4; i++) { var w = Part("wheel", "RoveMax Model M1", 0.075, 450); var wm = Mod("ModuleWheelBase", "leg"); wm.Txt["type"] = "powered wheel"; w.Modules.Add(wm); w.X = i < 2 ? -0.4 : 0.4; w.Z = i % 2 == 0 ? -0.6 : 0.6; w.Y = 0; r.Parts.Add(w); }
        r.Prepare();
        Assert(EngMissionTools.IsRover(r), "four powered wheels and no wings make it a rover");
        var rv = EngMissionTools.Rover(r, EngineeringToolsTests.Bodies()[2], null);
        Assert(rv.Lines.Any(l => l.Text.StartsWith("4 wheels, 4 powered")) && rv.Lines.Any(l => l.Text.Contains("rolls over past about") && l.Value == "tippy") && rv.Lines.Any(l => l.Text.StartsWith("Gravity on Mun")), "a tall narrow rover on the Mun: " + rv.Text());
    }

    public static void TestTechAdviceAndRelays()
    {
        var snap = EngineeringToolsTests.Craft();
        Assert(EngMissionTools.Tech(snap.Sheet, new List<CraftPart>(), snap.Stages, false, -1).Rating == "everything unlocked", "sandbox");
        var better = Part("nerv", "LV-N Nerv", 0.4, 900); better.Modules.Add(EngineeringToolsTests.Engine(50, 800, 185)); better.Tech = "nuclearPropulsion"; better.TechTitle = "Nuclear Propulsion"; better.TechCost = 300;
        var tech = EngMissionTools.Tech(snap.Sheet, new List<CraftPart> { better }, snap.Stages, false, 120);
        Assert(tech.Lines.Any(l => l.Text.StartsWith("Nuclear Propulsion (300 science, 180 short): Stage 0: swap LV-909 Terrier for LV-N Nerv") && l.Value.StartsWith("+")), "a locked engine that would help, with what it costs: " + tech.Text());

        var sheet = snap.Sheet;
        sheet.Facts["commnet"] = 1; sheet.Facts["dsnPower"] = 250e9; sheet.Facts["rangeMod"] = 1;
        var ant = Part("ant", "Communotron 16", 0.005, 300); var am = Mod("ModuleDataTransmitter", "antenna"); am.Num["power"] = 500e3; ant.Modules.Add(am); sheet.Parts.Add(ant);
        var plan = EngMissions.Plan("Orbit Duna", snap);
        var none = EngMissionTools.Relays(snap, plan, new List<EngRelay>());
        Assert(none.Rating == "no link from Duna" && none.Fixes.Count == 1 && none.Bad, "a small antenna cannot reach home from Duna: " + none.Text());
        var relays = new List<EngRelay> { new EngRelay { Name = "Duna Relay 1", Body = "Duna", Power = 100e9, Altitude = 2e6 }, new EngRelay { Name = "Mun Relay", Body = "Mun", Power = 2e9, Altitude = 1e6 } };
        var with = EngMissionTools.Relays(snap, plan, relays);
        Assert(with.Rating == "covered by relay" && with.Lines.Any(l => l.Text.StartsWith("Duna Relay 1 at Duna") && l.Value == "carries the signal home") && with.Lines.Any(l => l.Text.StartsWith("1 other relay elsewhere")), "a strong relay at Duna carries it: " + with.Text());
        sheet.Facts["commnet"] = 0;
        Assert(EngMissionTools.Relays(snap, plan, relays).Rating == "not needed", "CommNet off");
    }

    static NewsEngine Engine(NewsState st, CoreTests.FakeTransport t, EngSnapshot snap)
    {
        var eng = new NewsEngine(st, CoreTests.OnlineSettings(), t, new SimpleClock(21600));
        eng.EngineeringData = () => snap;
        return eng;
    }

    public static void TestFlightsAreSetAgainstWhatWasPredicted()
    {
        var st = new NewsState();
        var full = Rocket();
        var chute = Part("chute", "Mk16 Parachute", 0.1, 422); chute.Modules.Add(Mod("ModuleParachute", "chute")); chute.ChuteArea = 600; full.Sheet.Parts.Add(chute); full.Sheet.Prepare();
        var eng = Engine(st, new CoreTests.FakeTransport(), full);
        Func<string, EngSnapshot> at = sit => new EngSnapshot { Scene = "flight", Vessel = "Pathfinder", VesselId = "v1", Situation = sit, Body = "Kerbin", Bodies = full.Bodies, Stages = full.Stages, TotalDvVac = full.TotalDvVac, Mass = full.Mass };
        eng.EngRecord(at("on the launch pad"), 100);
        Assert(st.EngPredictions.Count == 1 && st.EngPredictions[0].Items.Any(i => i[0] == "Delta-v to orbit") && st.EngPredictions[0].Items.Any(i => i[0] == "Touchdown speed"), "the launch is noted with what the team expects");
        var up = at("flying in Kerbin's atmosphere"); up.GForce = 3.2; eng.EngRecord(up, 160);
        var orbit = at("orbiting Kerbin"); orbit.HasOrbit = true; orbit.PeA = 80000; orbit.ApA = 85000; orbit.TotalDvVac = full.TotalDvVac - 3650; orbit.Mass = 3.5;
        eng.EngRecord(orbit, 500);
        var p = st.EngPredictions[0];
        Assert(p.Orbited && Math.Abs(EngPrediction.Num(p.Items.First(i => i[0] == "Delta-v to orbit")[3]) - 3650) < 1 && Math.Abs(EngPrediction.Num(p.Items.First(i => i[0].StartsWith("Peak g"))[3]) - 3.2) < 0.01, "orbit fills in the delta-v used and the peak g");
        Assert(st.EngProven.Count == 1 && st.EngProven[0].Name == "Pathfinder" && Math.Abs(st.EngProven[0].OrbitMass - 3.5) < 0.01 && st.EngProven[0].Cost > 1000, "the rocket is now proven");
        var fall = at("flying in Kerbin's atmosphere"); fall.VerticalSpeed = -6.4; eng.EngRecord(fall, 900);
        eng.EngRecord(at("splashed down on Kerbin"), 910);
        Assert(p.Landed && Math.Abs(EngPrediction.Num(p.Items.First(i => i[0] == "Touchdown speed")[3]) - 6.4) < 0.01, "touchdown fills in the landing speed");
        var rep = EngMissionTools.Predicted(st.EngPredictions, "Pathfinder");
        Assert(rep.Rating == "1 flight on record" && rep.Lines.Any(l => l.Text.Contains("Delta-v to orbit: expected") && l.Text.Contains("was 3650")), rep.Text());
        Assert(st.EngNotes.Any(n => n.Kind == "flight" && n.Text.Contains("reached orbit using 3650 m/s")) && st.EngNotes.Any(n => n.Text.Contains("touched down at 6.4 m/s")), "flight results go in the team's notes");

        // Everything survives a save.
        var node = new DataNode("ROOT"); st.Save(node);
        var back = NewsState.Load(node);
        Assert(back.EngPredictions.Count == 1 && back.EngPredictions[0].Items.Count == p.Items.Count && back.EngProven.Count == 1 && back.EngNotes.Count == st.EngNotes.Count, "predictions, proven rockets and notes are kept in the save");
    }

    public static void TestTheTeamRemembers()
    {
        var st = new NewsState();
        string user = "";
        var t = new CoreTests.FakeTransport { Responder = r => { if (!r.Body.Contains("engineering team of")) return CoreTests.AnthropicReply(r); user = r.Body; return Reply("{\"replies\":[{\"who\":\"maxwell\",\"text\":\"Two more panels cover it.\"}]}"); } };
        var snap = Rocket();
        var eng = Engine(st, t, snap);
        eng.EngSend("From now on I never want RTGs on crewed craft.", 100);
        Assert(st.EngNotes.Any(n => n.Kind == "preference" && n.Text.Contains("never want RTGs")), "a stated preference is filed");
        Assert(st.EngChat.Last().Role == "maxwell", "the new specialists can answer: " + st.EngChat.Last().Role);
        Assert(st.EngNotes.Any(n => n.Kind == "qa" && n.Craft == "Pathfinder" && n.Text.Contains("Two more panels")), "the answer is filed in the craft's notebook");
        eng.EngRemember("meeting", "Other Craft", "Meeting on \"Eve lander\": use four Mk16-XL parachutes.", 200);
        eng.EngRemember("note", "", "The Duna base uses the old docking port size.", 250, true);
        for (int i = 0; i < 40; i++) eng.EngRemember("qa", "Filler " + i, "Q: filler question " + i + " A: filler answer about nothing in particular " + i, 300 + i);
        eng.EngClear();
        eng.EngSend("How many parachutes for the Eve lander?", 400);
        Assert(user.Contains("TEAM MEMORY") && user.Contains("never want RTGs") && user.Contains("old docking port size") && user.Contains("four Mk16-XL parachutes") && !user.Contains("filler answer about nothing in particular 3 "), "the next call carries preferences, pinned notes and what matches the question, not everything");
        Assert(st.EngNotes.Count > 40, "nothing is thrown away when the chat is cleared");
        eng.EngSend("What did we decide last time?", 500);
        Assert(user.Contains("filler answer about nothing in particular 39"), "asking what was said before brings the recent record along");
        var node = new DataNode("ROOT"); st.Save(node);
        var back = NewsState.Load(node);
        Assert(back.EngNotes.Count == st.EngNotes.Count && back.EngNotes.Any(n => n.Pinned && n.Text.Contains("old docking port")), "notes are kept in the save");
        st.EngLevel = "teach";
        eng.EngSend("Why does that matter?", 600);
        Assert(user.Contains("wants to learn"), "the explanation level reaches the team");
    }

    public static void TestTeamMeetingRunsTurnByTurn()
    {
        int ag, pr, tk, vt;
        NewsEngine.MeetingPlan(1, out ag, out pr, out tk, out vt); Assert(ag + pr + tk + vt == 0, "one call is the minutes alone");
        NewsEngine.MeetingPlan(10, out ag, out pr, out tk, out vt); Assert(ag == 1 && pr == 1 && tk == 7 && vt == 0, "ten calls: " + ag + pr + tk + vt);
        NewsEngine.MeetingPlan(100, out ag, out pr, out tk, out vt); Assert(ag == 1 && pr == 5 && vt == 6 && tk == 87, "a hundred calls");

        var st = new NewsState();
        var snap = Rocket();
        var held = new List<Action<HttpResult>>();
        var bodies = new List<string>();
        var t = new CoreTests.FakeTransport { Responder = null };
        var eng = Engine(st, t, snap);
        Func<string> lastBody = () => t.Sent.Last().Body;
        Action<string> answer = json => { var cb = t.Pending[0]; t.Pending.RemoveAt(0); cb(Reply(json)); };

        var m = eng.EngMeetingStart("Can Pathfinder reach the Mun and come back?", 8, 1000);
        Assert(m != null && m.Status == "running" && t.Sent.Count == 1 && lastBody().Contains("Set an agenda") && lastBody().Contains("Can Pathfinder reach the Mun"), "the chair opens with the agenda");
        Assert(eng.EngMeetingStart("another", 5, 1000) == null, "one meeting at a time");
        answer("{\"text\":\"Let us settle the budget first.\",\"agenda\":[\"Delta-v budget\",\"Power\"]}");
        Assert(m.Agenda.Count == 2 && m.Used == 1 && lastBody().Contains("write each specialist's opening position"), "then opening positions in one call at this length");
        answer("{\"positions\":[{\"who\":\"linus\",\"text\":\"The transfer needs 860.\",\"note\":\"Transfer is 860 m/s\"},{\"who\":\"maxwell\",\"text\":\"50 EC is thin.\",\"note\":\"Battery is small\"}]}");
        Assert(m.Turns.Count(x => x.Kind == "position") == 2 && lastBody().Contains("YOUR TURN to speak") && lastBody().Contains("POINTS SO FAR") && lastBody().Contains("Battery is small"), "discussion starts with the points carried forward");
        Assert(lastBody().Contains("You are Gene"), "the first to speak is the one who has said least: " + lastBody().Substring(0, 200));
        eng.EngMeetingSay("Keep it under 20,000 funds.", 1001);
        answer("{\"text\":\"Wernher, can the upper stage do it?\",\"note\":\"Asks about the upper stage\",\"next\":\"wernher\"}");
        Assert(lastBody().Contains("You are Wernher") && lastBody().Contains("Director: Keep it under 20,000 funds."), "the named specialist answers and hears what the director said");
        answer("{\"text\":\"A second tank would help.\",\"note\":\"Wants a second upper tank\",\"test\":{\"part\":\"FL-T400 Fuel Tank\",\"count\":1}}");
        var test = m.Turns.LastOrDefault(x => x.Kind == "test");
        Assert(test != null && test.Text.StartsWith("Tested live: adding FL-T400 Fuel Tank: mass +2.25 t, cost +500 funds, delta-v +"), "an idea is tried on paper and the result joins the meeting: " + (test != null ? test.Text : "none"));
        Assert(snap.Sheet.Parts.Count(p => p.Name == "t2") == 1, "the real craft is not touched");
        Assert(lastBody().Contains("Tested live: adding FL-T400"), "the next speaker sees the result");
        eng.EngMeetingStop(1002);
        answer("{\"text\":\"Costs stay inside the limit.\",\"note\":\"Under budget\"}");
        Assert(lastBody().Contains("write the minutes"), "wrapping up goes straight to the minutes");
        answer("{\"minutes\":\"The craft can make the Mun and back with a second upper tank.\",\"decisions\":[\"Add one FL-T400 to the upper stage\"],\"actions\":[\"Add an FL-T400 under the pod\",\"Add a parachute\"],\"open\":[\"Battery size\"]}");
        Assert(m.Status == "done" && m.Minutes.Contains("second upper tank") && m.Actions.Count == 2 && m.Used == 6 && !eng.EngBusy, "minutes, decisions and a to-do list: " + m.Status + " " + m.Used);
        Assert(st.EngNotes.Any(n => n.Kind == "meeting" && n.Text.Contains("second upper tank")) && st.EngNotes.Any(n => n.Kind == "preference") == false, "the minutes go on file");
        eng.EngMeetingTick(m.Id, 0);
        Assert(m.Done[0] && !m.Done[1], "to-do items can be ticked");

        // A follow-up starts from the earlier minutes; a split goes to the director.
        var f = eng.EngMeetingStart("Settle the battery size", 3, 2000, m.Id);
        Assert(lastBody().Contains("THIS FOLLOWS AN EARLIER MEETING") && lastBody().Contains("Add an FL-T400 under the pod (done)") && lastBody().Contains("TEAM MEMORY"), "a follow-up remembers the last minutes and what was done");
        answer("{\"text\":\"We are split.\",\"note\":\"Split on batteries\",\"deadlock\":{\"question\":\"More battery or more panels?\",\"a\":\"Add a Z-400 battery\",\"b\":\"Add two panels\"}}");
        Assert(f.Status == "waiting" && f.OptionA == "Add a Z-400 battery" && t.Pending.Count == 0, "a deadlock waits for the director without spending a call");
        eng.EngMeetingDecide("b", 2001);
        Assert(f.Status == "running" && lastBody().Contains("Director: My decision: Add two panels"), "the director's choice goes back to the team");
        answer("{\"text\":\"Then two panels it is.\",\"note\":\"Two panels\"}");
        answer("{\"minutes\":\"Two panels.\",\"decisions\":[\"Add two panels\"],\"actions\":[],\"open\":[]}");
        Assert(f.Status == "done" && f.Used == 3, "the follow-up ends inside its three calls: " + f.Used);

        var node = new DataNode("ROOT"); st.Save(node);
        var back = NewsState.Load(node);
        Assert(back.EngMeetings.Count == 2 && back.EngMeetings[0].Turns.Count == m.Turns.Count && back.EngMeetings[0].Actions.Count == 2 && back.EngMeetings[0].Done[0] && back.EngMeetings[0].Agenda.Count == 2, "meetings are kept in the save");

        // The page.
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "GameData/KerbalNews/Sites");
        var sites = new Sites(eng, rel => { var file = Path.Combine(dir, rel); return File.Exists(file) ? File.ReadAllText(file) : null; });
        sites.Now = () => 3000;
        sites.Handle("action:eng-tab/meeting", null);
        sites.Handle("action:eng-meet-view/" + m.Id, null);
        string page = sites.Render("engineering.kb").Html;
        Assert(page.Contains("Call a meeting") && page.Contains("action:eng-meet-len/100") && page.Contains("Tested live") && page.Contains("action:eng-meet-tick/" + m.Id + "/1") && page.Contains("Show me") && page.Contains("Start as follow-up"), "the meeting page shows the form, the turns, the minutes and the to-do list");
        sites.Handle("action:eng-meet-len/20", null);
        Assert(sites.Render("engineering.kb").Html.Contains("Length: 20 AI calls"), "the length is set by the slider");
        int sent = t.Sent.Count;
        sites.Handle("action:eng-meet-start", new Dictionary<string, string> { { "objective", "Typed length" }, { "calls", "2" } });
        Assert(t.Sent.Count == sent + 1 && st.EngMeetings.Last().Budget == 2, "a typed length wins over the slider");
        answer("{\"text\":\"Short one.\",\"note\":\"Short\"}");
        answer("{\"minutes\":\"Done.\"}");
        sites.Handle("action:eng-tab/notes", null);
        page = sites.Render("engineering.kb").Html;
        var note = st.EngNotes.First(n => n.Kind == "meeting");
        Assert(page.Contains("Team notes: " + st.EngNotes.Count + " on file") && page.Contains("action:eng-note-pin/" + note.Id) && page.Contains("action:eng-note-del/" + note.Id), "the notes page lists what is on file");
        sites.Handle("action:eng-note-pin/" + note.Id, null);
        sites.Handle("action:eng-note-edit/" + note.Id, null);
        sites.Handle("action:eng-note-save/" + note.Id, new Dictionary<string, string> { { "noteedit", "Corrected minutes." } });
        Assert(note.Pinned && note.Text == "Corrected minutes.", "notes can be pinned and edited");
        sites.Handle("action:eng-note-add", new Dictionary<string, string> { { "notetext", "Always leave 200 m/s for docking." } });
        Assert(st.EngNotes.Last().Text == "Always leave 200 m/s for docking." && st.EngNotes.Last().Pinned, "a note can be added by hand");
        sites.Handle("action:eng-note-del/" + note.Id, null);
        Assert(!st.EngNotes.Contains(note), "and deleted");

        // A saved craft is read in place of the live one until it is closed.
        var savedCraft = EngineeringToolsTests.Craft(); savedCraft.Vessel = "Old Hopper"; savedCraft.Stages.Clear();
        savedCraft.Sheet.Parts[1].Res.Add(new CraftRes { Name = "LiquidFuel", Amount = 180, Max = 180 }); savedCraft.Sheet.Parts[1].Res.Add(new CraftRes { Name = "Oxidizer", Amount = 220, Max = 220 });
        eng.EngineeringSaved = () => new List<string[]> { new[] { "Old Hopper", "VAB", "path/Old Hopper.craft" } };
        eng.EngineeringLoadSaved = path => path == "path/Old Hopper.craft" ? savedCraft : null;
        sites.Handle("action:eng-tab/saved", null);
        Assert(sites.Render("engineering.kb").Html.Contains("action:eng-saved/0"), "saved craft are listed");
        sites.Handle("action:eng-saved/0", null);
        page = sites.Render("engineering.kb").Html;
        Assert(eng.EngPinned == savedCraft && page.Contains("Reviewing the saved craft") && page.Contains("Old Hopper") && page.Contains("the team's estimate") && savedCraft.Stages.Count > 0, "the saved craft is under review with estimated delta-v");
        sites.Handle("action:eng-live", null);
        Assert(eng.EngPinned == null && sites.Render("engineering.kb").Html.Contains("Pathfinder"), "back to the live craft");
        foreach (var tab in new[] { "needs", "timeline", "ascent", "aerobrake", "relays", "mining", "docking", "setup", "reliability", "launcher", "tech", "predicted" })
        {
            sites.Handle("action:eng-tab/" + tab, null);
            page = sites.Render("engineering.kb").Html;
            Assert(sites.EngTab == tab && page.Contains("class=\"btitle\">") && !page.Contains("not computed"), "the " + tab + " tab shows a computed report");
        }
    }
}
