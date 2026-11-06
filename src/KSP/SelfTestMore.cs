using System;
using System.Collections;
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
            yield return Go(Sites.EngineeringHost);
            yield return Shot("eng-vab");
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
