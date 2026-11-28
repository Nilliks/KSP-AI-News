using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using KerbalNews.Web;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace KerbalNews.KSP
{
    /// <summary>Self-test extras ("eng" and "career" modes): engineering data in the editor and in flight, the time-warp hold,
    /// a crash investigation, saving the new data, and posting a news contract. All inside the throwaway save.</summary>
    public partial class SelfTest
    {
        bool modeEng, modeCareer, modeImg;

        static object CallStatic(string type, string method, params object[] args)
        {
            foreach (var a in AssemblyLoader.loadedAssemblies)
            {
                var t = a.assembly.GetType(type, false);
                if (t == null) continue;
                var m = t.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(x => x.Name == method && x.GetParameters().Length >= args.Length
                    && x.GetParameters().Skip(args.Length).All(p => p.IsOptional));
                if (m == null) continue;
                var full = args.Concat(m.GetParameters().Skip(args.Length).Select(p => p.DefaultValue)).ToArray();
                return m.Invoke(null, full);
            }
            throw new MissingMethodException(type + "." + method);
        }

        void LogSnap(string where, EngSnapshot s)
        {
            Log("  " + where + ": scene " + s.Scene + ", " + s.Vessel + " (" + s.Situation + "), " + s.Parts + " parts, " + s.Mass.ToString("0.0") + " t, crew " + s.Crew + "/" + s.CrewCapacity
                + ", dv ready " + s.DeltaVReady + ", total " + s.TotalDvVac.ToString("0") + " vac / " + s.TotalDvAsl.ToString("0") + " ASL / " + s.TotalDvActual.ToString("0") + " actual"
                + (s.HasOrbit ? ", Ap " + s.ApA.ToString("0") + " Pe " + s.PeA.ToString("0") + " inc " + s.Inclination.ToString("0.0") : "")
                + (s.Target != "" ? ", target " + s.Target + " phase " + s.PhaseAngle.ToString("0.0") + " relInc " + s.RelInclination.ToString("0.0") : "")
                + ", modules " + s.Modules.Count + ", resources " + string.Join("/", s.ResourceMax.Keys.Take(6).ToArray()));
            foreach (var st in s.Stages.OrderByDescending(x => x.Stage))
                Log("    stage " + st.Stage + ": " + st.DvVac.ToString("0") + " vac, " + st.DvAsl.ToString("0") + " ASL, " + st.DvActual.ToString("0") + " actual, TWR " + st.TwrVac.ToString("0.00") + "/" + st.TwrAsl.ToString("0.00") + ", mass " + st.StartMass.ToString("0.0") + "->" + st.EndMass.ToString("0.0") + " t, burn " + st.BurnTime.ToString("0") + " s, thrust " + st.ThrustVac.ToString("0") + " kN, Isp " + st.IspVac.ToString("0"));
        }

        IEnumerator EditorCheck(NewsSystem sys, string craft)
        {
            bool ok = true;
            try { CallStatic("EditorDriver", "StartEditor", EditorFacility.VAB); }
            catch (Exception e) { ok = false; Check(false, "eng: open the VAB: " + (e.InnerException ?? e).Message); }
            if (!ok) yield break;
            float t0 = Time.realtimeSinceStartup;
            while ((HighLogic.LoadedScene != GameScenes.EDITOR || EditorLogic.fetch == null) && Time.realtimeSinceStartup - t0 < 90) yield return new WaitForSeconds(1);
            yield return new WaitForSeconds(4);
            try { CallStatic("EditorLogic", "LoadShipFromFile", craft); }
            catch (Exception e) { Check(false, "eng: load Kerbal X in the VAB: " + (e.InnerException ?? e).Message); }
            t0 = Time.realtimeSinceStartup;
            EngSnapshot s = null;
            while (Time.realtimeSinceStartup - t0 < 30)
            {
                yield return new WaitForSeconds(2);
                try { s = EngineeringCapture.Snapshot(); } catch (Exception e) { Check(false, "eng: editor snapshot threw " + e); yield break; }
                if (s.DeltaVReady && s.Stages.Count > 0) break;
            }
            LogSnap("VAB", s);
            Check(s != null && s.Scene == "vab" && s.Parts > 10 && s.Mass > 10, "eng: VAB sees the craft being built (" + (s != null ? s.Parts + " parts, " + s.Mass.ToString("0") + " t" : "none") + ")");
            Check(s != null && s.DeltaVReady && s.Stages.Count >= 2 && s.TotalDvVac > 1000, "eng: VAB delta-v by stage (" + (s != null ? s.Stages.Count + " stages, " + s.TotalDvVac.ToString("0") + " m/s" : "none") + ")");
            var plan = sys.Engine.EngCurrentPlan(s);
            foreach (var r in sys.Engine.EngReview(s, plan)) Log("    review: " + r);
            // The live craft sheet: every part read from the builder, nothing saved.
            var sheet = s != null ? s.Sheet : null;
            Check(sheet != null && sheet.Parts.Count == s.Parts, "eng: craft sheet lists every part in the builder (" + (sheet != null ? sheet.Parts.Count.ToString() : "none") + " of " + (s != null ? s.Parts : 0) + ")");
            if (sheet != null)
            {
                int engines = sheet.Count("engine"), tanks = sheet.Parts.Count(p => p.Max("LiquidFuel") > 0), decouplers = sheet.Count("decoupler");
                Check(engines >= 3 && tanks >= 3 && decouplers >= 1 && sheet.Count("command") >= 1, "eng: sheet recognises engines, tanks, decouplers and the pod (" + engines + " engines, " + tanks + " tanks, " + decouplers + " decouplers)");
                Check(sheet.All("engine").All(m => m.N("thrust") > 0 && m.N("ispVac") > 100 && m.T("props") != ""), "eng: engines have thrust, Isp and propellants read");
                Check(sheet.Parts.All(p => p.Title != "" && p.Mod != "") && sheet.Parts.Any(p => p.DropStage >= 0) && sheet.Parts.Any(p => p.DropStage < 0), "eng: parts have names, a source mod and a drop stage");
                Check(s.Bodies.Count > 5 && s.Home != null && DvPlanner.Ascent(s.Home) > 1000, "eng: bodies read from the save (" + s.Bodies.Count + ", launch " + (s.Home != null ? DvPlanner.Ascent(s.Home).ToString("0") : "?") + " m/s)");
                var power = sys.Engine.EngPower(s, plan);
                Log("    power: " + (power != null ? power.Rating + ", battery " + power.Battery.ToString("0") + " EC, solar " + power.SolarHome.ToString("0.##") + ", always " + power.Always.ToString("0.###") + ", wheels " + power.Wheels.ToString("0.###") : "none"));
                Check(power != null && power.Battery > 0 && power.Always + power.Wheels > 0, "eng: power budget has batteries and consumers");
                foreach (var x in sheet.StagingIssues(s.Stages)) Log("    staging: " + x);
                foreach (var ev in sheet.Stages(s.Stages)) Log("    stage " + ev.Stage + ": " + string.Join(", ", ev.Fires.ToArray()));
                string text = sheet.Text(s.Stages, "");
                Log("    sheet text: " + text.Length + " characters for " + sheet.Parts.Count + " parts");
                foreach (var line in text.Split('\n').Take(70)) Log("      " + line);
                // Part 2: the computed checks, what the mods reported, and the parachute numbers behind the landing sums.
                var checks = sys.Engine.EngChecks(s, plan);
                Check(checks.Count == 7 && checks.All(c => c.Rating != "" && c.Rating != "not computed"), "eng: all seven checks ran (" + string.Join(", ", checks.Select(c => c.Key + "=" + c.Rating).ToArray()) + ")");
                foreach (var c in checks) foreach (var line in c.Text().Split('\n')) Log("    " + line);
                {
                    // The same craft judged for a far destination: power, comms, landing and supplies there.
                    string keep = sys.Engine.State.EngMission;
                    sys.Engine.State.EngMission = "Land on Duna and return";
                    var far = sys.Engine.EngCurrentPlan(s);
                    var farChecks = sys.Engine.EngChecks(s, far);
                    Log("    with the mission \"Land on Duna and return\" (plan known: " + (far != null && far.Known) + "): " + string.Join(", ", farChecks.Select(c => c.Key + "=" + c.Rating).ToArray()));
                    foreach (var c in farChecks.Where(c => c.Key == "comms" || c.Key == "landing" || c.Key == "crew")) foreach (var line in c.Text().Split('\n')) Log("      " + line);
                    sys.Engine.State.EngMission = keep;
                }
                // Part 3: the tools on top of the checks, and the catalogue of unlocked parts behind the swap finder.
                try
                {
                    float c0 = Time.realtimeSinceStartup;
                    var catalog = EngineeringExtras.Catalog();
                    Log("    parts catalogue: " + catalog.Count + " unlocked parts in " + ((Time.realtimeSinceStartup - c0) * 1000).ToString("0") + " ms (" + string.Join(", ", catalog.GroupBy(x => x.Mod).OrderByDescending(x => x.Count()).Take(8).Select(x => x.Key + " " + x.Count()).ToArray()) + ")");
                    Check(catalog.Count > 20, "eng: the swap finder sees the unlocked parts (" + catalog.Count + ")");
                    float t1 = Time.realtimeSinceStartup;
                    var tools = sys.Engine.EngToolReports(s, plan, checks, true);
                    Log("    tools took " + ((Time.realtimeSinceStartup - t1) * 1000).ToString("0") + " ms");
                    Check(tools.Count >= 6 && tools.All(c => c.Rating != "not computed"), "eng: the tools ran (" + string.Join(", ", tools.Select(c => c.Key + "=" + c.Rating).ToArray()) + ")");
                    foreach (var c in tools) foreach (var line in c.Text().Split('\n').Take(30)) Log("    " + line);
                    Log("    contracts read: " + s.Contracts.Count + ", experiments aboard: " + s.Experiments.Count + ", phases: " + s.Phases.Count);
                    var shot = sys.Engine.EngTakeSnapshot("self-test version");
                    Check(shot != null && sys.Engine.State.EngSnaps.Contains(shot), "eng: a snapshot of the craft is kept");
                    string keepM = sys.Engine.State.EngMission;
                    sys.Engine.State.EngMission = "Land on Duna and return";
                    var farPlan = sys.Engine.EngCurrentPlan(s);
                    foreach (var c in sys.Engine.EngToolReports(s, farPlan, sys.Engine.EngChecks(s, farPlan), false).Where(c => c.Key == "windows" || c.Key == "gonogo" || c.Key == "science")) foreach (var line in c.Text().Split('\n').Take(20)) Log("      (Duna) " + line);
                    sys.Engine.State.EngMission = keepM;
                }
                catch (Exception e) { Check(false, "eng: tools threw " + e); }
                // Part 4: the mission tools, the game-side reads behind them, the team's memory and a saved craft.
                try
                {
                    float m0 = Time.realtimeSinceStartup;
                    var mission = sys.Engine.EngMissionReports(s, plan, checks, true);
                    Log("    mission tools took " + ((Time.realtimeSinceStartup - m0) * 1000).ToString("0") + " ms");
                    Check(mission.Count >= 9 && mission.All(c => c.Rating != "not computed"), "eng: the mission tools ran (" + string.Join(", ", mission.Select(c => c.Key + "=" + c.Rating).ToArray()) + ")");
                    foreach (var c in mission) foreach (var line in c.Text().Split('\n').Take(24)) Log("    " + line);
                    int withActions = sheet.Parts.Count(p => p.Modules.Any(x => x.Num.ContainsKey("actions")));
                    Log("    action groups: " + withActions + " parts have actions; on a group: " + string.Join(" | ", sheet.Parts.SelectMany(p => p.Modules.Select(x => x.T("groups")).Where(g => g != "").Select(g => p.Title + ": " + g)).Take(12).ToArray()));
                    Check(withActions > 5, "eng: action groups read from the builder (" + withActions + " parts)");
                    var est = SheetDv.Solve(sheet);
                    Log("    delta-v from the sheet alone: " + string.Join(", ", est.Select(x => "stage " + x.Stage + " " + x.DvVac.ToString("0")).ToArray()) + " = " + est.Sum(x => x.DvVac).ToString("0") + " m/s; the game says " + s.TotalDvVac.ToString("0"));
                    Check(est.Count >= 2 && Math.Abs(est.Sum(x => x.DvVac) - s.TotalDvVac) < s.TotalDvVac * 0.15, "eng: the team's own delta-v estimate is within 15% of the game's (" + est.Sum(x => x.DvVac).ToString("0") + " against " + s.TotalDvVac.ToString("0") + ")");
                    var lockedParts = EngineeringExtras.Locked();
                    Log("    tech tree: " + lockedParts.Count + " useful parts still locked, science in hand " + EngineeringExtras.SciencePoints().ToString("0.#") + (lockedParts.Count > 0 ? "; e.g. " + lockedParts[0].Title + " in " + lockedParts[0].TechTitle + " (" + lockedParts[0].TechCost.ToString("0") + ")" : ""));
                    var relayList = EngineeringExtras.Relays();
                    Log("    relays flying: " + relayList.Count + (relayList.Count > 0 ? ": " + string.Join(", ", relayList.Take(6).Select(x => x.Name + " at " + x.Body + " " + x.Power.ToString("0.##e0")).ToArray()) : ""));
                    string keepM = sys.Engine.State.EngMission;
                    sys.Engine.State.EngMission = "Land on Duna and return";
                    var farPlan = sys.Engine.EngCurrentPlan(s);
                    foreach (var c in sys.Engine.EngMissionReports(s, farPlan, sys.Engine.EngChecks(s, farPlan), false).Where(c => c.Key == "timeline" || c.Key == "needs" || c.Key == "aerobrake" || c.Key == "relays" || c.Key == "reliability")) foreach (var line in c.Text().Split('\n').Take(24)) Log("      (Duna) " + line);
                    sys.Engine.State.EngMission = keepM;
                    // A saved craft read from its file. The test save may have none, so the stock craft folder stands in.
                    var saved = EngineeringExtras.Saved();
                    Log("    saved craft in this save: " + saved.Count);
                    string file = saved.Count > 0 ? saved[0][2] : System.IO.Path.Combine(KSPUtil.ApplicationRootPath, "Ships/VAB/Kerbal X.craft");
                    if (System.IO.File.Exists(file))
                    {
                        float f0 = Time.realtimeSinceStartup;
                        bool opened = sys.Engine.EngOpenSaved(file);
                        var look = sys.Engine.EngPinned;
                        Log("    saved craft " + file + ": " + (look != null ? look.Vessel + ", " + look.Parts + " parts, " + look.Mass.ToString("0.0") + " t, " + look.Stages.Count + " stages, " + look.TotalDvVac.ToString("0") + " m/s (estimate), " + look.Situation : "not read") + " in " + ((Time.realtimeSinceStartup - f0) * 1000).ToString("0") + " ms");
                        Check(opened && look != null && look.Parts > 5 && look.Stages.Count > 0 && look.Sheet.Parts.Any(p => p.DropStage >= 0), "eng: a saved craft file is read for review (" + (look != null ? look.Parts + " parts" : "none") + ")");
                        sys.Engine.EngPinned = null;
                    }
                    var note = sys.Engine.EngRemember("note", s.Vessel, "Self-test note: the team keeps this on file.", Planetarium.GetUniversalTime());
                    Check(note != null && sys.Engine.EngMemoryText(s, "what is on file about the self-test note?").Contains("Self-test note"), "eng: the team's memory files a note and finds it again");
                    sys.Engine.State.EngNotes.Remove(note);
                }
                catch (Exception e) { Check(false, "eng: mission tools threw " + e); }
                Check(sheet.Parts.All(p => p.HasPos) && sheet.All("engine").All(m => m.N("aimed") > 0), "eng: part positions and engine thrust directions read");
                Check(sheet.Parts.Sum(p => p.Cost) > 1000 && sheet.Parts.Any(p => p.CrashTol > 0), "eng: part costs and crash tolerances read (" + sheet.Parts.Sum(p => p.Cost).ToString("0") + " funds)");
                foreach (var p in sheet.Parts.Where(p => p.Has("chute"))) Log("    parachute " + p.Title + ": drag area " + p.ChuteArea.ToString("0.0") + " m2, min pressure " + p.Of("chute").First().N("minPressure").ToString("0.###"));
                Check(sheet.Parts.Where(p => p.Has("chute")).All(p => p.ChuteArea > 10 && p.ChuteArea < 20000), "eng: parachute drag areas read from the game");
                try
                {
                    foreach (Part part in EditorLogic.fetch.ship.parts)
                    {
                        bool isChute = false; if (part.Modules != null) foreach (PartModule pm0 in part.Modules) if (pm0 != null && pm0.GetType().Name.Contains("Parachute")) isChute = true;
                        if (!isChute) continue;
                        foreach (PartModule pm in part.Modules) if (pm != null && pm.GetType().Name == "ModuleDragModifier") Log("    chute raw " + part.partInfo.name + ": modifier " + CraftReader.Str(pm, "dragCubeName") + " = " + CraftReader.Num(pm, "dragModifier"));
                        var cubes = CraftReader.Get(CraftReader.Get(part, "DragCubes"), "Cubes") as System.Collections.IEnumerable;
                        if (cubes != null) foreach (var cube in cubes)
                        {
                            var ar = CraftReader.Get(cube, "Area") as float[]; var dr = CraftReader.Get(cube, "Drag") as float[]; var dm = CraftReader.Get(cube, "DragModifiers") as float[];
                            Log("    chute raw " + part.partInfo.name + ": cube " + CraftReader.Str(cube, "Name") + " area " + (ar != null ? string.Join(",", ar.Select(x => x.ToString("0.##")).ToArray()) : "?") + " drag " + (dr != null ? string.Join(",", dr.Select(x => x.ToString("0.###")).ToArray()) : "?") + " modifiers " + (dm != null ? string.Join(",", dm.Select(x => x.ToString("0.##")).ToArray()) : "?"));
                        }
                    }
                }
                catch (Exception e) { Log("    chute raw: " + e.Message); }
                Log("    facts: " + string.Join(", ", sheet.Facts.Select(f => f.Key + "=" + f.Value.ToString("0.###")).ToArray()));
                Check(sheet.HasFact("day") && sheet.HasFact("commnet"), "eng: save facts read (day length, CommNet)");
                Log("    mod facts: " + string.Join(", ", sheet.ModFacts.Select(f => f.Key + "=" + f.Value).ToArray()));
                foreach (var pr in sheet.Processes) Log("    process " + pr.Title + " [" + pr.Name + "] capacity " + pr.Capacity.ToString("0.###") + ": " + CraftSheet.RatesText(pr.Rates));
                foreach (var cr in sheet.CrewRules) Log("    crew rule " + cr.Name + ": " + cr.Input + " " + cr.PerSec.ToString("0.########") + "/s" + (cr.Varies ? " (varies)" : "") + (cr.Output != "" ? " -> " + cr.Output : ""));
                Log("    handbooks: " + string.Join(", ", ModKnowledge.BooksFor(sheet).Select(x => x.Title).ToArray()));
                try
                {
                    string census = CraftReader.Census();
                    string file = System.IO.Path.Combine(KSPUtil.ApplicationRootPath, "GameData/KerbalNews/PluginData/module-census.txt");
                    System.IO.File.WriteAllText(file, census);
                    Check(census.Length > 100, "eng: module census written (" + census.Split('\n').Length + " kinds of mod module)");
                }
                catch (Exception e) { Check(false, "eng: module census: " + e.Message); }
                float r0 = Time.realtimeSinceStartup;
                for (int i = 0; i < 5; i++) CraftReader.Read(EditorLogic.fetch.ship.parts, EditorLogic.fetch.ship.parts[0], false);
                Log("    reading the craft takes " + ((Time.realtimeSinceStartup - r0) / 5 * 1000).ToString("0.0") + " ms");
            }
            yield return Go(Sites.EngineeringHost);
            yield return Shot("eng-vab");
            foreach (var tab in new[] { "power", "stages", "parts", "problems", "heat", "comms", "landing", "balance", "launch", "crew", "cost", "gonogo", "windows", "contracts", "science", "efficiency", "swaps", "compare" })
            {
                sys.Sites.Handle("action:eng-tab/" + tab, null);
                yield return Go(Sites.EngineeringHost);
                yield return Shot("eng-vab-" + tab);
            }
            foreach (var tab in new[] { "needs", "timeline", "ascent", "aerobrake", "relays", "setup", "reliability", "launcher", "docking", "predicted", "meeting", "notes", "saved" })
            {
                sys.Sites.Handle("action:eng-tab/" + tab, null);
                yield return Go(Sites.EngineeringHost);
                yield return Shot("eng-vab-" + tab);
            }
            sys.Sites.Handle("action:eng-tab/overview", null);
            sys.Sites.Handle("action:eng-menu/tabs:Mission", null);
            yield return Go(Sites.EngineeringHost);
            yield return Shot("eng-vab-menu-mission");
            sys.Sites.Handle("action:eng-menu/dest", null);
            yield return Go(Sites.EngineeringHost);
            yield return Shot("eng-vab-menu-goto");
            sys.Sites.Handle("action:eng-menu/dest", null);
            sys.Sites.Handle("action:eng-tab/overview", null);
            HighLogic.LoadScene(GameScenes.SPACECENTER);
            t0 = Time.realtimeSinceStartup;
            while (HighLogic.LoadedScene != GameScenes.SPACECENTER && Time.realtimeSinceStartup - t0 < 90) yield return new WaitForSeconds(1);
            yield return new WaitForSeconds(4);
        }

        IEnumerator WarpCheck(NewsSystem sys)
        {
            var eng = sys.Engine;
            double ut0 = Planetarium.GetUniversalTime();
            var r = eng.PublishRelease("announcement", "Self-test: warp check", "A routine statement used to test that news waits for time warp to end.", "", null, ut0);
            string storyId = r != null ? r.StoryId : "";
            bool warped = true;
            try { TimeWarp.SetRate(6, true, false); } catch (Exception e) { warped = false; Check(false, "warp: could not warp: " + e.Message); }
            if (!warped) yield break;
            int during = 0; bool held = false;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 30)
            {
                yield return new WaitForSeconds(1);
                held |= eng.WarpHold;
                during = eng.State.Articles.Count(a => a.StoryId == storyId);
            }
            double days = (Planetarium.GetUniversalTime() - ut0) / 21600;
            try { TimeWarp.SetRate(0, true, false); } catch { }
            Check(held && during == 0, "warp: no AI news during " + days.ToString("0") + " days of warp (held " + held + ", articles " + during + ", status " + eng.Status + ")");
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 150 && eng.State.Articles.Count(a => a.StoryId == storyId) == 0) yield return new WaitForSeconds(1);
            int after = eng.State.Articles.Count(a => a.StoryId == storyId);
            Check(after > 0 && !eng.WarpHold, "warp: the waiting story was written " + (Time.realtimeSinceStartup - t0).ToString("0") + " s after the warp ended (" + after + " articles)");
        }

        IEnumerator EngFlightCheck(NewsSystem sys, Vessel v)
        {
            var eng = sys.Engine;
            if (v == null || FlightGlobals.ActiveVessel != v) { Check(false, "eng: no active vessel in flight"); yield break; }
            EngSnapshot s = null;
            try { s = EngineeringCapture.Snapshot(); } catch (Exception e) { Check(false, "eng: flight snapshot threw " + e); yield break; }
            LogSnap("flight", s);
            Check(s.Scene == "flight" && s.Body == "Kerbin" && s.Parts > 5 && s.Speed > 10, "eng: flight data (" + s.Situation + ", " + s.Altitude.ToString("0") + " m, " + s.Speed.ToString("0") + " m/s)");
            Check(s.DeltaVReady && s.Stages.Count > 0 && s.TotalDvActual > 100, "eng: flight delta-v by stage (" + s.Stages.Count + " stages, " + s.TotalDvActual.ToString("0") + " m/s actual)");

            // Target the Mun and check the phase angle against an independent reckoning (the Mun's own sense of rotation).
            var mun = FlightGlobals.Bodies.FirstOrDefault(b => b.bodyName == "Mun");
            if (mun != null)
            {
                // The open browser holds input locks, which block targeting otherwise.
                try { FlightGlobals.fetch.SetVesselTarget(mun, true); } catch (Exception e) { Log("  could not set target: " + e.Message); }
                yield return new WaitForSeconds(1);
                s = EngineeringCapture.Snapshot();
                double ut = Planetarium.GetUniversalTime();
                Vector3d rv = v.orbit.getRelativePositionAtUT(ut), rm = mun.orbit.getRelativePositionAtUT(ut), vm = mun.orbit.getOrbitalVelocityAtUT(ut);
                Vector3d n = Vector3d.Cross(rm, vm);
                double ang = Vector3d.Angle(rv, rm);
                double expect = Vector3d.Dot(Vector3d.Cross(rv, rm), n) > 0 ? ang : 360 - ang;
                double diff = Math.Abs(((s.PhaseAngle - expect) % 360 + 540) % 360 - 180);
                Log("  phase angle: engine " + s.PhaseAngle.ToString("0.0") + ", independent " + expect.ToString("0.0") + ", ideal " + EngMath.TransferPhase(s.TransferR1, s.TransferR2).ToString("0.0") + ", transfer r1 " + s.TransferR1.ToString("0") + " r2 " + s.TransferR2.ToString("0"));
                Check(s.Target == "Mun" && s.PhaseAngle >= 0 && diff < 3, "eng: target " + (s.Target != "" ? s.Target : "not set") + ", phase angle " + s.PhaseAngle.ToString("0") + "° (independent check " + expect.ToString("0") + "°)");
            }

            // A maneuver node: 150 m/s prograde, five minutes out.
            ManeuverNode node = null;
            try
            {
                node = v.patchedConicSolver.AddManeuverNode(Planetarium.GetUniversalTime() + 300);
                node.DeltaV = new Vector3d(0, 0, 150);
                v.patchedConicSolver.UpdateFlightPlan();
            }
            catch (Exception e) { Check(false, "eng: place a maneuver node: " + e.Message); }
            yield return new WaitForSeconds(1);
            if (node != null)
            {
                s = EngineeringCapture.Snapshot();
                Check(s.Node != null && Math.Abs(s.Node.Dv - 150) < 2 && Math.Abs(s.Node.Prograde - 150) < 2 && s.Node.TimeTo > 200, "eng: node read back (" + (s.Node != null ? s.Node.Dv.ToString("0") + " m/s, prograde " + s.Node.Prograde.ToString("0") + ", in " + s.Node.TimeTo.ToString("0") + " s, after: " + s.Node.AfterBody + " Ap " + s.Node.AfterApA.ToString("0") + " Pe " + s.Node.AfterPeA.ToString("0") : "none") + ")");
                foreach (var line in NewsEngine.EngNodeCheck(s)) Log("    node check: " + line);
                try { node.RemoveSelf(); } catch (Exception e) { Log("  could not remove the node: " + e.Message); }
            }

            // What the data costs: it is read every 4 s all flight long.
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 20; i++) EngineeringCapture.Snapshot();
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds / 20;
            Check(ms < 2, "eng: reading the craft data costs " + ms.ToString("0.00") + " ms");

            // One question to the team, with the real provider.
            int before = eng.State.EngChat.Count;
            eng.EngSend("Check my delta-v budget: can this craft reach a stable Kerbin orbit, and what then?", Planetarium.GetUniversalTime());
            float t0 = Time.realtimeSinceStartup;
            while (eng.EngBusy && Time.realtimeSinceStartup - t0 < 120) yield return new WaitForSeconds(1);
            var replies = eng.State.EngChat.Skip(before).Where(m => m.Role != "player").ToList();
            Check(replies.Count > 0 && !replies[0].Text.StartsWith("("), "eng: the team answered in " + (Time.realtimeSinceStartup - t0).ToString("0") + " s");
            foreach (var m in replies) Log("    " + NewsEngine.EngName(m.Role) + ": " + m.Text.Replace("\n", " / "));
            yield return Go(Sites.EngineeringHost);
            yield return Shot("eng-flight");
        }

        IEnumerator CrashCheck(NewsSystem sys, Vessel v)
        {
            var eng = sys.Engine;
            if (v == null) yield break;
            yield return new WaitForSeconds(5);   // a little more flight data
            int before = eng.State.Incidents.Count;
            try { v.Die(); } catch (Exception e) { Check(false, "crash: destroy the test craft: " + e.Message); yield break; }
            float t0 = Time.realtimeSinceStartup;
            while (eng.State.Incidents.Count == before && Time.realtimeSinceStartup - t0 < 15) yield return new WaitForSeconds(0.5f);
            var inc = eng.State.Incidents.LastOrDefault();
            Check(eng.State.Incidents.Count > before && inc.Telemetry.Count > 0, "crash: incident opened with " + (inc != null ? inc.Telemetry.Count : 0) + " lines of flight data");
            if (inc == null) yield break;
            foreach (var l in inc.Telemetry.Skip(Math.Max(0, inc.Telemetry.Count - 3))) Log("    data: " + l);
            eng.EngInvestigate(inc.Id, Planetarium.GetUniversalTime());
            t0 = Time.realtimeSinceStartup;
            while (inc.Status == "working" && Time.realtimeSinceStartup - t0 < 120) yield return new WaitForSeconds(1);
            Check(inc.Status == "done" && inc.Cause != "", "crash: investigation done in " + (Time.realtimeSinceStartup - t0).ToString("0") + " s: " + inc.Cause);
            Log("    findings: " + inc.Findings);
            var rel = eng.EngShareFindings(inc.Id, Planetarium.GetUniversalTime());
            Check(rel != null && rel.Type == "incident", "crash: findings shared as a press release" + (rel != null ? ": " + rel.Title : ""));
            yield return Go(Sites.EngineeringHost);
            yield return Shot("eng-incident");
        }

        IEnumerator SaveCheck(NewsSystem sys)
        {
            var eng = sys.Engine;
            eng.State.EngMission = "land on the Mun and come back";
            yield return null;
            string path = Path.Combine(KSPUtil.ApplicationRootPath, "saves/" + TestSave + "/selftest-check.sfs");
            try
            {
                var game = HighLogic.CurrentGame.Updated();
                GamePersistence.SaveGame(game, "selftest-check", TestSave, SaveMode.OVERWRITE);
            }
            catch (Exception e) { Check(false, "save: write: " + e.Message); yield break; }
            yield return new WaitForSeconds(1);
            try
            {
                var root = ConfigNode.Load(path);
                var gameNode = root.GetNode("GAME") ?? root;
                ConfigNode news = null;
                foreach (var sc in gameNode.GetNodes("SCENARIO")) if (sc.GetValue("name") == "NewsScenario") news = sc.GetNode("KERBALNEWS");
                var back = news != null ? NewsState.Load(ConfigBridge.ToData(news)) : null;
                Check(back != null && back.EngChat.Count == eng.State.EngChat.Count && back.Incidents.Count == eng.State.Incidents.Count
                    && back.Incidents.Last().Cause == eng.State.Incidents.Last().Cause && back.EngMission == eng.State.EngMission,
                    "save: engineering chat, incidents and mission plan survive a real save (" + (back != null ? back.EngChat.Count + " lines, " + back.Incidents.Count + " incidents" : "no news node") + ")");
            }
            catch (Exception e) { Check(false, "save: read back: " + e); }
        }

        /// <summary>"img" mode: a press photo from the image service. The key comes from PluginData/selftest-imgkey.txt (deleted at once),
        /// lives in memory only for the test and is never written to settings or secrets.</summary>
        IEnumerator ImageCheck(NewsSystem sys)
        {
            var eng = sys.Engine;
            var g = eng.Settings;
            string keyFile = Path.Combine(KSPUtil.ApplicationRootPath, "GameData/KerbalNews/PluginData/selftest-imgkey.txt");
            string key = "";
            try { if (File.Exists(keyFile)) { key = File.ReadAllText(keyFile).Trim(); File.Delete(keyFile); } } catch (Exception e) { Check(false, "img: read the test key: " + e.Message); }
            string oldProvider = g.ImageProviderId, oldTier = g.ImageMinTier, oldKey;
            g.Keys.TryGetValue("img:gemini", out oldKey);
            if (key == "") key = oldKey ?? "";   // or the key the player saved on the Settings page
            if (key == "") { Check(false, "img: no Gemini key in Settings or selftest-imgkey.txt"); yield break; }
            g.ImageProviderId = "gemini"; g.Keys["img:gemini"] = key; g.ImageMinTier = "brief";
            key = "";
            try
            {
                Check(g.ImagesOn, "img: images on with " + g.ImagePreset.Name + " (" + g.ImageModel + ")");
                bool? ok = null; string msg = "", file = "";
                eng.TestImage((a, b, c) => { ok = a; msg = b; file = c; });
                float t0 = Time.realtimeSinceStartup;
                while (ok == null && Time.realtimeSinceStartup - t0 < 180) yield return new WaitForSeconds(1);
                Check(ok == true, "img: sample photo in " + (Time.realtimeSinceStartup - t0).ToString("0") + " s: " + msg + (file != "" ? " (" + file + ")" : ""));

                // A real story: the photo lands on its articles.
                var r = eng.PublishRelease("announcement", "Agency unveils new launch pad floodlights", "The space center installed new floodlights at the launch pad so night launches can be photographed properly.", "", null, Planetarium.GetUniversalTime());
                string storyId = r != null ? r.StoryId : "";
                t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 300 && !eng.State.Articles.Any(a => a.StoryId == storyId && a.Image != "")) yield return new WaitForSeconds(1);
                var art = eng.State.Articles.FirstOrDefault(a => a.StoryId == storyId && a.Image != "");
                Check(art != null, "img: article photo in " + (Time.realtimeSinceStartup - t0).ToString("0") + " s (" + eng.ImageStatus + ", " + eng.State.Articles.Count(a => a.StoryId == storyId) + " articles)");
                var story = eng.State.Stories.FirstOrDefault(x => x.Id == storyId);
                if (story != null) Log("    prompt: " + story.ImagePrompt);
                if (art != null)
                {
                    string path = Path.Combine(KSPUtil.ApplicationRootPath, "GameData/KerbalNews/PluginData/Images/" + art.Image);
                    if (File.Exists(path)) { try { File.Copy(path, Path.Combine(outDir, "img-" + art.Image), true); } catch { } }
                    yield return Go(Sites.ArticleUrl(art));
                    yield return Shot("img-article");
                }
            }
            finally
            {
                g.ImageProviderId = oldProvider; g.ImageMinTier = oldTier;
                if (oldKey != null) g.Keys["img:gemini"] = oldKey; else g.Keys.Remove("img:gemini");
            }
        }

        /// <summary>Wheel scrolling, the scrollbar and resizing from the edges and corners.</summary>
        /// <summary>Science Office: a demo result from the Mun, the team's reading (AI when a key is set), then a journal paper.</summary>
        IEnumerator ScienceCheck(NewsSystem sys, Browser br, double now)
        {
            var eng = sys.Engine;
            var r = eng.RecordScience(new ScienceResult { UT = now - 600, SubjectId = "surfaceSample@MunSrfLandedMidlands", Title = "Surface Sample from Mun Midlands", Experiment = "surfaceSample", Body = "Mun", Situation = "SrfLanded", Biome = "Midlands",
                Amount = 61.2, Completion = 1, VesselId = "demo1", VesselName = "Mun Lander I", ResultText = "This sample is mostly regolith, a fine grey dust with a few glassy beads mixed in." });
            if (r != null && r.Crew.Count == 0) r.Crew.Add("Jebediah Kerman");
            Log("  science: Kerbalism detected = " + eng.KerbalismScience + ", AI = " + eng.Settings.CanCallAi);
            yield return Go("agency.kb/science");
            yield return Shot("science-waiting");
            string key = NewsEngine.ResultKey(r);
            br.Act("action:sci-pick/" + Uri.EscapeDataString(key), null);
            br.Act("action:sci-ask", new System.Collections.Generic.Dictionary<string, string> { { "sciq", "Could any of this be water ice?" } });
            float t0 = Time.realtimeSinceStartup;
            while (eng.FindingFor(r) == null && Time.realtimeSinceStartup - t0 < 90) yield return new WaitForSeconds(0.5f);
            var f = eng.FindingFor(r);
            Check(f != null, "science: the team interpreted the Mun sample" + (f != null ? " (" + f.Scientist + ", importance " + f.Importance + ", " + (f.Offline ? "template" : "AI") + "): " + f.Title : ", last error: " + eng.LastError));
            if (f == null) yield break;
            Log("  science finding: " + f.Finding);
            Log("  science unknowns: " + f.Unknowns + " | next: " + f.NextStep);
            yield return Go("agency.kb/science/interpreted");
            yield return Shot("science-interpreted");
            br.Act("action:sci-publish/" + f.Id + "|journal", null);
            Check(f.Status == "journal" && eng.State.Articles.Any(a => a.Id == f.ArticleId), "science: finding sent to the Kerbin Science Review");
            var paper = eng.State.Articles.FirstOrDefault(a => a.Id == f.ArticleId);
            if (paper != null)
            {
                var oldNow = sys.Sites.Now;
                double later = paper.PublishedUT + 60;
                sys.Sites.Now = () => later;
                yield return Go(Sites.ArticleUrl(paper));
                yield return Shot("science-paper");
                Check(br.CurrentPage != null && br.CurrentPage.Title.Contains(paper.Headline.Substring(0, Math.Min(12, paper.Headline.Length))), "science: the paper's page opens: " + (br.CurrentPage != null ? br.CurrentPage.Title : ""));
                sys.Sites.Now = oldNow;
            }
            yield return Go("agency.kb/science/published");
            yield return Shot("science-published");
        }

        /// <summary>Background world news: read the real game world, then write one roundup (one AI call) and open a story.</summary>
        IEnumerator WikiCheck(NewsSystem sys, Browser br, double now)
        {
            var eng = sys.Engine;
            var pages = eng.State.Wiki.Values.ToList();
            Log("  wiki: " + pages.Count + " pages: " + string.Join(", ", pages.Select(p => p.Kind + ":" + p.Title).ToArray()));
            var craft = pages.FirstOrDefault(p => p.Kind == "craft");
            Check(craft != null, "wiki: the covered mission made a craft page");
            yield return Visit(br, "kerbopedia.kb");
            if (craft == null) yield break;
            yield return Visit(br, Sites.WikiUrl(craft));
            if (eng.Settings.CanCallAi)
            {
                Check(eng.WikiRewrite(craft.Slug, "", now), "wiki: the AI rewrite started");
                float t0 = Time.realtimeSinceStartup;
                while (eng.WikiBusy && Time.realtimeSinceStartup - t0 < 120) yield return new WaitForSeconds(0.5f);
                Check(craft.WrittenUT >= 0 && craft.Sections.Count > 0, "wiki: the AI wrote the page (" + craft.Sections.Count + " sections): " + ResponseParser.Clip(craft.Lead, 200));
                foreach (var sct in craft.Sections) Log("  wiki section " + sct.Heading + ": " + ResponseParser.Clip(sct.Text.Replace("\n", " "), 300));
                yield return Visit(br, Sites.WikiUrl(craft));
            }
            string res = eng.Execute("edit_wiki", new Dictionary<string, object> { { "page", craft.Title }, { "sections", new List<object> { new Dictionary<string, object> { { "heading", "Self-test" }, { "text", "This section was added by the self-test." } } } }, { "note", "Self-test edit" } }, now);
            Check(craft.Sections.Any(x => x.Heading == "Self-test"), "wiki: the assistant's edit landed: " + res);
            yield return Go(Sites.WikiUrl(craft) + "/history");
            yield return Shot("wiki-history");
        }

        IEnumerator KmailCheck(NewsSystem sys, Browser br, double now)
        {
            var eng = sys.Engine;
            // Logos: every file the pages point at is installed.
            var missing = new[] { "herald", "observer", "inquirer", "kraken", "munmonthly", "kscwatch", "science", "council", "knn", "icon-kmail", "icon-wiki", "icon-agency", "icon-knn", "icon-herald" }
                .Where(id => !Browser.ImageExists("Sites/logos/" + id + ".png")).ToList();
            Check(missing.Count == 0, "logos installed" + (missing.Count > 0 ? ", missing: " + string.Join(", ", missing.ToArray()) : ""));
            yield return Visit(br, "herald.kb");
            yield return Shot("logo-herald");
            // Kmail: an interview request, accepted from the page, books the kerbal.
            var kerbal = eng.State.Crew.Values.Where(c => !c.Dead).OrderByDescending(c => c.OnRoster).FirstOrDefault();
            if (kerbal == null) { Log("SKIP Kmail booking: nobody on the roster"); yield break; }
            var m = new MailMsg { Id = "eselftest", Thread = "eselftest", Kind = "press", Org = "herald", From = "Selma Kerman", FromAddr = "s.kerman@herald.kb", Ask = "interview", Kerbal = kerbal.Name, Topic = "the self-test", Subject = "Interview request: " + kerbal.Name, Body = "Dear Director,\n\nCould we talk to " + kerbal.Name + "?\n\nSelma Kerman", UT = now - 1, DueUT = now + 2 * 21600, Status = "open" };
            eng.State.Mail.Add(m);
            yield return Visit(br, "kmail.kb");
            yield return Shot("kmail-inbox");
            yield return Visit(br, Sites.MailUrl(m));
            yield return Shot("kmail-open");
            br.Act("action:mail-quick/eselftest/accept", null);
            Check(m.Status == "accepted" && eng.State.Appearances.Any(a => a.Kerbal == kerbal.Name && a.Status == "scheduled"), "Kmail: accepting books " + kerbal.Name + ": " + m.Outcome);
            yield return Visit(br, Sites.MailUrl(m));
            yield return Shot("kmail-accepted");
        }

        IEnumerator WorldCheck(NewsSystem sys, Browser br, double now)
        {
            var eng = sys.Engine;
            var facts = WorldScanner.Scan(eng.State, now, 21600);
            Log("  world: scan found " + facts.Count + " facts" + (WorldScanner.LastLog.Count > 0 ? " (" + string.Join("; ", WorldScanner.LastLog.ToArray()) + ")" : ""));
            foreach (var f in facts.Take(25)) Log("  world fact [" + f.Kind + " " + f.Sig + "] " + f.Key + ": " + f.Headline + " | " + f.Text);
            Check(!WorldScanner.LastLog.Any(l => l.Contains("failed")), "world: every reader ran without errors");
            int before = eng.State.Articles.Count;
            foreach (var f in facts.Where(f => f.Sig < 4).Take(3)) eng.State.WorldPending.Add(f);
            if (eng.State.WorldPending.Count == 0)
                eng.State.WorldPending.Add(new WorldFact { Key = "selftest:window", Kind = "sky", Sig = 3, UT = now, Body = "Duna", Headline = "Launch window to Duna approaches", Text = "The launch window from Kerbin to Duna opens in about 4 days. The planets line up like this only about every 228 days." });
            eng.State.WorldPending.Add(new WorldFact { Key = "selftest:crew", Kind = "crew", Sig = 2, UT = now, Headline = "Valentina Kerman passes 30 days in space", Text = "Valentina Kerman has now spent 30 days in space in total." });
            Check(eng.WorldRoundupNow(now), "world: a roundup was started");
            float t0 = Time.realtimeSinceStartup;
            while (eng.WorldBusy && Time.realtimeSinceStartup - t0 < 90) yield return new WaitForSeconds(0.5f);
            var made = eng.State.Articles.Skip(before).Where(a => a.Tags.Contains("world")).ToList();
            Check(made.Count >= 2, "world: roundup wrote " + made.Count + " stories (" + (made.Count > 0 && made[0].Offline ? "templates" : "AI") + "): " + string.Join(" / ", made.Select(a => a.Outlet + ": " + a.Headline).ToArray()));
            foreach (var a in made) Log("  world story " + a.Outlet + ": " + a.Headline + " | " + ResponseParser.Clip(a.Body.Replace("\n", " "), 300));
            if (made.Count > 0)
            {
                var oldNow = sys.Sites.Now;
                double later = made.Max(a => a.PublishedUT) + 60;
                sys.Sites.Now = () => later;
                yield return Go(Sites.ArticleUrl(made[0]));
                yield return Shot("world-story");
                Check(br.CurrentPage != null && br.CurrentPage.Title.Contains(made[0].Headline.Substring(0, Math.Min(10, made[0].Headline.Length))), "world: the story's page opens");
                yield return Go("settings.kb");
                br.ScrollTo(0.45f);
                yield return new WaitForSeconds(0.3f);
                yield return Shot("world-settings");
                sys.Sites.Now = oldNow;
            }
        }

        IEnumerator UiCheck(Browser br, string url)
        {
            br.Open(url);
            yield return new WaitForSeconds(0.5f);
            br.ScrollTo(0);
            yield return null;
            float bar0 = br.ScrollbarValue;
            br.Wheel(-3);   // three notches down
            yield return new WaitForSeconds(0.5f);
            float y1 = br.ScrollY;
            Check(y1 > 250 && y1 < 400, "ui: three wheel notches scroll " + y1.ToString("0") + " units down");
            Check(bar0 > 0.95f && br.ScrollbarValue < bar0, "ui: scrollbar follows (" + bar0.ToString("0.00") + " -> " + br.ScrollbarValue.ToString("0.00") + ")");
            br.Wheel(-1); br.Wheel(-1);   // quick notches in a row keep adding up while the glide runs
            yield return new WaitForSeconds(0.5f);
            Check(br.ScrollY > y1 + 150, "ui: notches in a row add up (" + br.ScrollY.ToString("0") + ")");
            br.Wheel(500);
            yield return new WaitForSeconds(0.6f);
            Check(br.ScrollY < 1, "ui: scrolling up stops at the top (" + br.ScrollY.ToString("0") + ")");
            yield return Shot("ui-scrollbar");

            Vector2 pos0 = br.WindowPos, size0 = br.WindowSize;
            float w0 = br.PageWidth;
            br.ResizeForTest(new Vector2(-100, 60), -1, 1);   // drag the top-left corner up and left
            yield return new WaitForSeconds(0.6f);
            Check(br.WindowSize.x > size0.x + 80 && br.WindowPos.x < pos0.x, "ui: top-left corner resizes and moves the window (" + size0.x.ToString("0") + " -> " + br.WindowSize.x.ToString("0") + " wide)");
            Check(br.PageWidth > w0 + 80, "ui: the page re-flows to the new width (" + w0.ToString("0") + " -> " + br.PageWidth.ToString("0") + ")");
            yield return Shot("ui-resized-bigger");
            br.ResizeForTest(new Vector2(-5000, 5000), 1, -1);   // bottom-right corner far up and left
            yield return new WaitForSeconds(0.6f);
            Check(br.WindowSize.x >= 519 && br.WindowSize.y >= 359, "ui: the window stops at its smallest size (" + br.WindowSize.x.ToString("0") + " x " + br.WindowSize.y.ToString("0") + ")");
            yield return Shot("ui-resized-smallest");
            br.SetWindowForTest(pos0, size0);
            yield return new WaitForSeconds(0.5f);
            br.DragTitleForTest(new Vector2(120, -80));   // right and down
            yield return new WaitForSeconds(0.4f);
            Check(br.WindowPos.x > pos0.x + 50 && br.WindowPos.y > pos0.y + 30 && Math.Abs(br.WindowSize.x - size0.x) < 1, "ui: dragging the title bar moves the window without resizing it (" + pos0 + " -> " + br.WindowPos + ")");
            yield return Shot("ui-title-bar-moved");
            br.DragTitleForTest(new Vector2(0, 99999));   // far off the top of the screen
            yield return new WaitForSeconds(0.3f);
            Check(br.WindowPos.y >= 0, "ui: the title bar cannot leave the top of the screen (" + br.WindowPos.y.ToString("0") + ")");
            br.SetWindowForTest(pos0, size0);
            yield return new WaitForSeconds(0.5f);
        }

        IEnumerator ContractCheck(NewsSystem sys)
        {
            yield return new WaitForSeconds(5);
            Check(KnnContract.Available(), "career: the contract system is there");
            if (!KnnContract.Available()) yield break;
            var t = new TieIn
            {
                Id = "ti-selftest", Kind = "rescue", Kerbal = "Jebediah Kerman", Title = "Self-test: bring Jebediah Kerman home",
                Description = "A test contract posted by the Kerbal News self-test.", Synopsis = "Recover Jebediah Kerman on Kerbin.", Done = "Test done.",
                Advance = 1000, Reward = 5000, Failure = 0, DeadlineDays = 30,
            };
            bool posted = false;
            try { posted = KnnContract.Post(t); } catch (Exception e) { Check(false, "career: post a news contract threw " + (e.InnerException ?? e)); }
            KnnContract c = null;
            for (int i = 0; i < 6; i++)
            {
                c = Contracts.ContractSystem.Instance.Contracts.OfType<KnnContract>().FirstOrDefault(x => x.TieId == t.Id);
                Log("  after " + i + " s: " + (c != null ? c.ContractState + ", expires " + c.DateExpire.ToString("0") + " (now " + Planetarium.GetUniversalTime().ToString("0") + ")" : "not in the list") + ", " + Contracts.ContractSystem.Instance.Contracts.Count + " contracts");
                yield return new WaitForSeconds(1);
            }
            Check(posted && c != null && c.ContractState == Contracts.Contract.State.Offered, "career: news contract offered in Mission Control" + (c != null ? ": \"" + c.Title + "\", " + c.FundsAdvance + " / " + c.FundsCompletion + " funds, rep " + c.ReputationCompletion + ", " + c.ParameterCount + " parameter(s)" : ""));
            if (c != null)
            {
                try { c.Decline(); } catch (Exception e) { Log("  decline threw " + e.Message); }
                yield return new WaitForSeconds(1);
                Check(c.ContractState == Contracts.Contract.State.Declined, "career: the contract can be declined (" + c.ContractState + ")");
            }
        }
    }
}
