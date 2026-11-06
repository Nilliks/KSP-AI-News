using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KerbalNews.Web;
using UnityEngine;

namespace KerbalNews.KSP
{
    /// <summary>
    /// Unattended in-game check. Create GameData/KerbalNews/PluginData/selftest.txt and start KSP: at the main menu
    /// the mod loads demo news (never a real save), opens the browser on every site, tries the page actions,
    /// takes screenshots into PluginData/SelfTest, writes results.txt and quits. The flag file is deleted at once,
    /// so it runs only once. Put "ai" in the file to also make one real AI story and one community reply.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public partial class SelfTest : MonoBehaviour
    {
        readonly List<string> log = new List<string>();
        string outDir;
        int shot;
        int failures;

        static string Root { get { return Path.Combine(KSPUtil.ApplicationRootPath, "GameData/KerbalNews/PluginData"); } }

        void Start()
        {
            string flag = Path.Combine(Root, "selftest.txt");
            if (!File.Exists(flag)) { Destroy(this); return; }
            string mode = "";
            try { mode = File.ReadAllText(flag).ToLowerInvariant(); File.Delete(flag); }
            catch (Exception e) { Debug.LogError("[KerbalNews] Self-test flag: " + e.Message); }
            DontDestroyOnLoad(this);
            outDir = Path.Combine(Root, "SelfTest");
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir)) File.Delete(f);
            modeEng = mode.Contains("eng");
            modeCareer = mode.Contains("career");
            modeImg = mode.Contains("img");
            StartCoroutine(Run(mode.Contains("ai"), mode.Contains("flight") || modeCareer || modeImg, !mode.Contains("stay")));
        }

        void Log(string s) { log.Add(s); Debug.Log("[KerbalNews] SELFTEST " + s); }
        void Check(bool ok, string what) { if (!ok) failures++; Log((ok ? "PASS " : "FAIL ") + what); }

        IEnumerator Run(bool withAi, bool withFlight, bool quit)
        {
            yield return new WaitForSeconds(4);
            var sys = NewsSystem.Instance;
            if (sys == null) { Log("FAIL NewsSystem missing"); Finish(quit); yield break; }
            var g = NewsSystem.Settings;
            bool wasOffline = g.OfflineMode;
            var oldDigest = sys.Engine.Digest;
            var oldSitesDigest = sys.Sites.Digest;
            var oldNow = sys.Sites.Now;
            var clock = new KspClock();
            double now = 0;
            NewsState state = null;
            try
            {
                state = DemoNews.Build(clock, out now);
                Check(state.Articles.Count > 5, "demo news built: " + state.Articles.Count + " articles, " + state.Forum.Count + " threads");
            }
            catch (Exception e) { Check(false, "demo news: " + e); Finish(quit); yield break; }

            // Demo news only: the real save is never loaded or written.
            sys.Engine.Digest = () => null;
            sys.Sites.Digest = () => new WorldDigest { UT = now, Satellites = 3, Stations = 1, Debris = 14, CrewInSpace = 1, CrewTotal = 7, DebrisByBody = { { "Kerbin", 12 }, { "Mun", 2 } }, CrewInSpaceNames = { "Jebediah Kerman" } };
            sys.Sites.Now = () => now;
            sys.Attach(state);

            var br = Browser.Instance ?? gameObject.AddComponent<Browser>();
            yield return null;
            yield return null;

            var top = state.Articles.OrderByDescending(a => a.Significance).First();
            var ama = state.Forum.First(t => t.Kind == "ama");
            var urls = new List<string> { Sites.Home, Sites.ArticleUrl(top), "kerbnet.kb", Sites.ForumHost, Sites.ForumHost + "/t/" + ama.Id, Sites.ForumHost + "/submit",
                "tracker.kb", "agency.kb", "agency.kb/assistant", "agency.kb/ledger", "agency.kb/mods", Sites.EngineeringHost, "settings.kb", "telemetry.kb", Sites.Home + "/search?q=mun", Sites.Home + "/polls", Sites.PressHost, Sites.GalleryHost, Sites.LiveHost };
            var rumor = state.Articles.FirstOrDefault(a => a.Outlet == "kraken");
            if (rumor != null) urls.Add(Sites.ArticleUrl(rumor));
            urls.AddRange(Outlets.All.Select(o => o.Domain));
            foreach (var u in urls)
            {
                yield return Visit(br, u);
            }

            // Scroll the article to the comments.
            br.Open(Sites.ArticleUrl(top));
            yield return new WaitForSeconds(0.5f);
            br.ScrollTo(1);
            yield return Shot("article-bottom");
            yield return UiCheck(br, Sites.ArticleUrl(top));

            // Actions: reply anonymously, vote, see the community answer (offline templates, no AI).
            g.OfflineMode = true;
            int before = top.Comments.Count;
            br.Act("action:comment/a:" + top.Id + "/anon", new Dictionary<string, string> { { "comment", "Self-test: does the anonymous reply box work?" }, { "handle", "selftest_kerbal" } });
            var mine = top.Comments.LastOrDefault();
            Check(top.Comments.Count == before + 1 && mine != null && mine.Author == "selftest_kerbal" && !mine.Agency, "anonymous comment posted");
            sys.Engine.Tick(now + 1, Time.realtimeSinceStartup, true);
            Check(top.Comments.Count(c => mine != null && c.ReplyTo == mine.Id) > 0, "community replied to it (templates)");
            br.Act("action:vote/a:" + top.Id + "/" + top.Comments[0].Id + "/up", null);
            br.Open(Sites.ArticleUrl(top));
            yield return new WaitForSeconds(0.5f);
            br.ScrollTo(1);
            yield return Shot("article-after-reply");

            br.Open(Sites.ForumHost + "/submit");
            yield return new WaitForSeconds(0.5f);
            br.Act("action:post-thread/anon", new Dictionary<string, string> { { "title", "Self-test thread: who else saw the Big Bertha explosion?" }, { "body", "It rattled my windows." }, { "handle", "selftest_kerbal" } });
            yield return new WaitForSeconds(0.5f);
            Check(br.CurrentPage != null && br.CurrentPage.Url.Contains("/t/"), "new Snackboard thread opened: " + (br.CurrentPage != null ? br.CurrentPage.Url : "none"));
            sys.Engine.Tick(now + 2, Time.realtimeSinceStartup, true);
            br.Open(br.CurrentPage.Url);
            yield return Shot("snackboard-new-thread");

            br.Open("settings.kb");
            yield return new WaitForSeconds(0.3f);
            float scaleBefore = Browser.Scale, zoomBefore = g.PageZoom;
            var winBefore = br.WindowSize;
            br.Act("action:zoom/+", null);
            yield return new WaitForSeconds(0.6f);
            Check(g.PageZoom > zoomBefore && Math.Abs(Browser.Scale - scaleBefore) < 0.001f && (br.WindowSize - winBefore).sqrMagnitude < 0.01f, "A+ makes the page bigger, not the window: zoom " + zoomBefore.ToString("0.0") + " -> " + g.PageZoom.ToString("0.0"));
            yield return Shot("settings-bigger-text");
            br.Act("action:zoom/reset", null);
            g.OfflineMode = wasOffline;
            sys.SaveSettings(); // the scale buttons saved settings while offline mode was forced on

            if (withAi) yield return AiCheck(sys, br, state, now);

            sys.Engine.Digest = oldDigest;
            sys.Sites.Digest = oldSitesDigest;
            sys.Sites.Now = oldNow;
            sys.Attach(new NewsState());
            if (withFlight) yield return FlightCheck(sys, br);
            Finish(quit);
        }

        public const string TestSave = "KerbalNewsSelfTest";

        /// <summary>
        /// In-flight check in a throwaway sandbox save (never the player's): launch the stock Kerbal X, go live,
        /// stage, and check the press photos, the stream chat and its video. Writes into saves/KerbalNewsSelfTest only.
        /// </summary>
        IEnumerator FlightCheck(NewsSystem sys, Browser br)
        {
            // The browser added at the main menu lives on this persistent object; drop it so flight has only one.
            var own = GetComponent<Browser>();
            if (own != null) { Destroy(own); yield return null; }
            string craft = Path.Combine(KSPUtil.ApplicationRootPath, "Ships/VAB/Kerbal X.craft");
            if (!File.Exists(craft)) { Check(false, "flight: stock craft missing at " + craft); yield break; }
            bool started = false;
            try
            {
                // Prefer the game's own new-game path, which adds the scenario modules other mods expect
                // (Kerbalism breaks engines and decouplers on a vessel whose save has no Kerbalism scenario).
                var game = NewGameTheStockWay() ?? new Game();
                game.Title = TestSave;
                game.Mode = modeCareer ? Game.Modes.CAREER : Game.Modes.SANDBOX;
                if (game.Parameters == null) game.Parameters = new GameParameters();
                if (game.flightState == null) game.flightState = new FlightState();
                if (game.CrewRoster == null) game.CrewRoster = KerbalRoster.GenerateInitialCrewRoster(Game.Modes.SANDBOX);
                game.startScene = GameScenes.SPACECENTER;
                game.flagURL = "Squad/Flags/default";
                HighLogic.SaveFolder = TestSave;
                HighLogic.CurrentGame = game;
                GamePersistence.SaveGame(game, "persistent", TestSave, SaveMode.OVERWRITE);
                game.Start();
                started = true;
            }
            catch (Exception e) { Check(false, "flight: new test game: " + e); }
            if (!started) yield break;
            float t0 = Time.realtimeSinceStartup;
            while (HighLogic.LoadedScene != GameScenes.SPACECENTER && Time.realtimeSinceStartup - t0 < 120) yield return new WaitForSeconds(1);
            Check(HighLogic.LoadedScene == GameScenes.SPACECENTER, "flight: test save " + TestSave + " reached the space center");
            yield return new WaitForSeconds(5);
            if (modeImg) { yield return ImageCheck(sys); yield break; }
            if (modeCareer) { yield return ContractCheck(sys); yield break; }
            if (modeEng) { yield return WarpCheck(sys); yield return EditorCheck(sys, craft); }
            // Launch clamps depend on other mods (in a heavily modded install they can fail to release), so the test
            // flies a copy of the craft without them, kept inside the throwaway save.
            try
            {
                string copy = Path.Combine(KSPUtil.ApplicationRootPath, "saves/" + TestSave + "/Ships/VAB/KerbalNewsSelfTest.craft");
                Directory.CreateDirectory(Path.GetDirectoryName(copy));
                int removed;
                File.WriteAllText(copy, CraftText.WithoutParts(File.ReadAllText(craft), "launchClamp", out removed));
                Log("  test craft without " + removed + " launch clamps: " + copy);
                craft = copy;
            }
            catch (Exception e) { Log("  could not strip clamps, flying the stock craft: " + e.Message); }
            try { FlightDriver.StartWithNewLaunch(craft, "Squad/Flags/default", "LaunchPad", VesselCrewManifest.FromConfigNode(ConfigNode.Load(craft))); }
            catch (Exception e) { Check(false, "flight: launch: " + e); yield break; }
            t0 = Time.realtimeSinceStartup;
            while (!(HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && FlightGlobals.ActiveVessel != null) && Time.realtimeSinceStartup - t0 < 180) yield return new WaitForSeconds(1);
            var v = FlightGlobals.ActiveVessel;
            try
            {
                var sc = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.scenarios : null;
                if (sc != null)
                    Log("  scenarios in the test save: " + string.Join(", ", sc.Cast<object>().Select(x => { var f = x.GetType().GetField("moduleName"); return f != null ? (string)f.GetValue(x) : x.ToString(); }).ToArray()));
            }
            catch (Exception e) { Log("  could not list scenarios: " + e.Message); }
            Check(v != null, "flight: Kerbal X on the pad");
            if (v == null) yield break;
            yield return new WaitForSeconds(3);
            br = Browser.Instance ?? br;
            var eng = sys.Engine;
            string id = v.persistentId.ToString();
            PressCamera.IgnoreDaylight = true;
            yield return Go("live.kb");
            br = Browser.Instance ?? br;
            br.Act("action:stream-start", null);
            Check(eng.Stream != null && eng.Stream.VesselId == id, "flight: live stream started for " + Names.Of(v));
            float pad = Time.realtimeSinceStartup;
            while (!eng.HasPressShot(id, "pad") && Time.realtimeSinceStartup - pad < 20) yield return new WaitForSeconds(0.5f);
            Check(eng.HasPressShot(id, "pad"), "flight: press photo of the craft on the pad");
            yield return Shot("flight-live-pad");

            // Full throttle, then stage until the craft actually moves (the first stage may be only clamps or
            // decouplers, and another mod can eat a single activation). Throttle is held every frame in case
            // something else resets it.
            // Some mods write the throttle after us each frame; the fly-by-wire hook runs last and wins.
            FlightInputCallback full = st0 => st0.mainThrottle = 1f;
            v.OnFlyByWire += full;
            if (FlightInputHandler.state != null) FlightInputHandler.state.mainThrottle = 1f;
            yield return new WaitForSeconds(0.5f);
            // Engines only run while physics does: wait for the craft to unpack and make sure the game is not paused.
            float unpack = Time.realtimeSinceStartup;
            while (v.packed && Time.realtimeSinceStartup - unpack < 20) yield return new WaitForSeconds(0.5f);
            if (FlightDriver.Pause) { Log("  game was paused; unpausing"); FlightDriver.SetPause(false); yield return new WaitForSeconds(0.5f); }
            Log("  before staging: packed " + v.packed + ", paused " + FlightDriver.Pause + ", timeScale " + Time.timeScale.ToString("0.00") + ", warp " + TimeWarp.CurrentRate.ToString("0.0") + ", ctrlState throttle " + (v.ctrlState != null ? v.ctrlState.mainThrottle.ToString("0.00") : "?"));
            StreamDriver.CutLog.Clear();
            float lift = Time.realtimeSinceStartup, nextStage = 0, nextFrameShot = 0;
            int frames = 0;
            int staged = 0;
            double startAlt = v.altitude;
            bool moving = false;
            while (Time.realtimeSinceStartup - lift < 50)
            {
                if (FlightInputHandler.state != null) FlightInputHandler.state.mainThrottle = 1f;
                if (v.ctrlState != null) v.ctrlState.mainThrottle = 1f;
                // Something in a heavily modded install zeroes the throttle that reaches the engines; KSP 1.12's
                // per-engine independent throttle bypasses the vessel throttle entirely.
                if (staged > 0 && !moving) IndependentThrottle(v);
                if (!moving && (v.srfSpeed > 2 || v.altitude > startAlt + 5))
                {
                    moving = true;
                    Log("  craft moving after " + staged + " staging(s), " + (Time.realtimeSinceStartup - lift).ToString("0.0") + " s, situation " + v.situation);
                }
                if (Time.realtimeSinceStartup > nextFrameShot) { nextFrameShot = Time.realtimeSinceStartup + 5; SaveFrame("stream-" + (++frames).ToString("00")); }
                if (!moving && staged < 4 && Time.realtimeSinceStartup > nextStage)
                {
                    global::KSP.UI.Screens.StageManager.ActivateNextStage();
                    staged++;
                    nextStage = Time.realtimeSinceStartup + 2.5f;
                    Log("  staged (" + staged + "), now stage " + v.currentStage + ", situation " + v.situation + ", speed " + v.srfSpeed.ToString("0.0") + ", throttle " + (FlightInputHandler.state != null ? FlightInputHandler.state.mainThrottle.ToString("0.00") : "?") + "/" + (v.ctrlState != null ? v.ctrlState.mainThrottle.ToString("0.00") : "?") + ", packed " + v.packed + ", timeScale " + Time.timeScale.ToString("0.00"));
                    StartCoroutine(LogEngines(v));
                }
                yield return null;
            }
            try { v.OnFlyByWire -= full; } catch (Exception) { }
            Check(moving, "flight: Kerbal X left the pad (" + staged + " stagings, situation " + v.situation + ", altitude " + v.altitude.ToString("0") + " m)");
            Check(eng.HasPressShot(id, "liftoff"), "flight: liftoff photo");
            Check(eng.HasPressShot(id, "ascent"), "flight: ascent photo");
            var st = eng.Stream;
            Check(st != null && st.Chat.Count(c => !c.System) >= 5, "flight: stream chat has " + (st != null ? st.Chat.Count(c => !c.System) : 0) + " viewer messages, " + (st != null ? st.Viewers : 0) + " viewers");
            Check(st != null && st.Moments.Contains("liftoff"), "flight: liftoff shown on stream: " + (st != null ? string.Join("; ", st.Moments.ToArray()) : ""));
            Check(StreamDriver.Frame != null, "flight: stream video rendered");
            foreach (var c in StreamDriver.CutLog) Log("  camera cut " + c);
            var groundUsed = StreamDriver.CutLog.Where(c => !c.Contains("Hull camera") && !c.Contains(" skipped ")).Select(c => c.Substring(c.IndexOf(' ') + 1, c.IndexOf(" (") - c.IndexOf(' ') - 1)).Distinct().ToList();
            Check(StreamDriver.CutLog.Count(c => !c.Contains(" skipped ")) >= 4, "flight: the stream cut between cameras " + StreamDriver.CutLog.Count(c => !c.Contains(" skipped ")) + " times");
            Check(groundUsed.Count >= 2, "flight: at least two ground cameras used: " + string.Join(", ", groundUsed.ToArray()));
            Check(st != null && st.Camera != "", "flight: camera name on the stream page: " + (st != null ? st.Camera : ""));
            // Hull cameras: forced on for a few seconds, whatever the distance, so they are seen even on a short flight.
            StreamDriver.ForceHull = true;
            int before = StreamDriver.CutLog.Count;
            for (int i = 0; i < 4; i++) { yield return new WaitForSeconds(4f); SaveFrame("stream-hull-" + i); }
            StreamDriver.ForceHull = false;
            var hullUsed = StreamDriver.CutLog.Skip(before).Where(c => c.Contains("Hull camera") && !c.Contains(" skipped ")).ToList();
            foreach (var c in StreamDriver.CutLog.Skip(before)) Log("  camera cut " + c);
            Check(hullUsed.Count > 0, "flight: hull cameras used: " + hullUsed.Count);
            Check(st != null && st.Moments.Any(m => m.Contains("onboard")), "flight: the chat heard about the onboard cameras");
            if (st != null) foreach (var c in st.Chat.Where(c => !c.System).Take(8)) Log("  chat " + c.Author + ": " + c.Text);
            if (modeEng) yield return EngFlightCheck(sys, v);
            yield return Go("live.kb");
            yield return Shot("flight-live-ascent");
            var spot = eng.State.Forum.FirstOrDefault(t => t.Image != "");
            Check(spot != null, "flight: a spotter posted a photo on Snackboard" + (spot != null ? ": " + spot.Title : ""));
            if (spot != null) { yield return Go(Sites.ForumHost + "/t/" + spot.Id); yield return Shot("flight-spotter-thread"); }
            yield return Go(Sites.GalleryHost);
            yield return Shot("flight-gallery");
            foreach (var g2 in eng.State.Gallery.Where(x => x.IsPress))
            {
                Log("  press photo " + g2.Kind + ": " + g2.Credit);
                try { File.Copy(Path.Combine(GalleryStore.Dir, g2.File), Path.Combine(outDir, "press-" + g2.Kind + "-" + g2.Id + ".jpg"), true); }
                catch (Exception e) { Log("  could not copy " + g2.File + ": " + e.Message); }
            }
            br.Act("action:stream-end", null);
            Check(eng.Stream == null && eng.State.Streams.Count > 0, "flight: stream ended and archived");
            PressCamera.IgnoreDaylight = false;
            if (modeEng) { yield return CrashCheck(sys, v); yield return SaveCheck(sys); }
        }

        Game NewGameTheStockWay()
        {
            try
            {
                var m = typeof(GamePersistence).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    .FirstOrDefault(x => x.Name == "CreateNewGame");
                if (m == null) { Log("  GamePersistence.CreateNewGame not found; building the game by hand"); return null; }
                var ps = m.GetParameters();
                var args = new object[ps.Length];
                bool firstString = true;
                for (int i = 0; i < ps.Length; i++)
                {
                    var t = ps[i].ParameterType;
                    if (t == typeof(string)) { args[i] = firstString ? TestSave : "Squad/Flags/default"; firstString = false; }
                    else if (t == typeof(Game.Modes)) args[i] = modeCareer ? Game.Modes.CAREER : Game.Modes.SANDBOX;
                    else if (t == typeof(GameParameters)) args[i] = new GameParameters();
                    else if (t == typeof(GameScenes)) args[i] = GameScenes.SPACECENTER;
                    else if (ps[i].HasDefaultValue) args[i] = ps[i].DefaultValue;
                    else args[i] = t.IsValueType ? Activator.CreateInstance(t) : null;
                }
                var g = m.Invoke(null, args) as Game;
                Log("  new game via " + m + (g != null && g.scenarios != null ? ", " + g.scenarios.Count + " scenarios" : ""));
                return g;
            }
            catch (Exception e) { Log("  CreateNewGame failed, building the game by hand: " + (e.InnerException ?? e).Message); return null; }
        }

        static string Field(object o, string name)
        {
            try { var f = o.GetType().GetField(name); return f != null ? Convert.ToString(f.GetValue(o), System.Globalization.CultureInfo.InvariantCulture) : "?"; }
            catch (Exception) { return "?"; }
        }

        void IndependentThrottle(Vessel v)
        {
            try
            {
                foreach (var e in v.FindPartModulesImplementing<ModuleEngines>())
                {
                    var t = e.GetType();
                    var on = t.GetField("independentThrottle");
                    var pct = t.GetField("independentThrottlePercentage");
                    if (on == null || pct == null) return;
                    if (!(bool)on.GetValue(e)) { on.SetValue(e, true); pct.SetValue(e, 100f); }
                }
            }
            catch (Exception) { }
        }

        IEnumerator LogEngines(Vessel v)
        {
            yield return new WaitForSeconds(2f);
            try
            {
                foreach (var e in v.FindPartModulesImplementing<ModuleEngines>())
                    Log("    engine " + (e.part != null && e.part.partInfo != null ? e.part.partInfo.name : "?") + ": ignited " + e.EngineIgnited + ", flameout " + e.flameout + ", throttle " + e.currentThrottle.ToString("0.00") + " (requested " + Field(e, "requestedThrottle") + ", independent " + Field(e, "independentThrottle") + "), thrust " + e.finalThrust.ToString("0.0") + " kN" + (e.status != null ? ", " + e.status : ""));
            }
            catch (Exception ex) { Log("    engine log failed: " + ex.Message); }
        }

        /// <summary>Opens a page in whichever browser this scene has, and waits until it is really showing.</summary>
        IEnumerator Go(string url)
        {
            for (int i = 0; i < 4; i++)
            {
                var b = Browser.Instance;
                if (b == null) { Log("  no browser in this scene yet"); yield return new WaitForSeconds(1); continue; }
                b.Open(url);
                yield return null;
                yield return new WaitForSeconds(0.8f);
                if (b.Showing && b.Url == url && b.CurrentPage != null && b.CurrentPage.Url == url) yield break;
                Log("  browser did not show " + url + " (showing " + b.Showing + ", url " + b.Url + ", page " + (b.CurrentPage != null ? b.CurrentPage.Url : "none") + "), retrying");
            }
            Check(false, "browser opened " + url);
        }

        IEnumerator Visit(Browser br, string url)
        {
            br.Open(url);
            yield return null;
            yield return new WaitForSeconds(0.6f);
            var l = br.CurrentLayout;
            var p = br.CurrentPage;
            bool ok = p != null && l != null && p.Title != "Not found" && p.Title != "Error" && l.Texts.Count > 3;
            Check(ok, url + " -> \"" + (p != null ? p.Title : "?") + "\" height " + (l != null ? l.Height.ToString("0") : "?") + ", " + (l != null ? l.Texts.Count + " paragraphs, " + l.Links.Count + " links, " + l.Inputs.Count + " fields" : ""));
            var ex = new List<string>();
            int over = br.CountOverflows(ex);
            Check(over == 0, url + " text fits its boxes" + (over > 0 ? ": " + over + " overflow, e.g. " + string.Join(" | ", ex.ToArray()) : ""));
            yield return Shot(url.Replace("/", "_").Replace("?", "_").Replace("=", "_"));
        }

        /// <summary>Saves what the livestream video shows right now, as a JPG next to the screenshots.</summary>
        void SaveFrame(string name)
        {
            var rt = StreamDriver.Frame;
            if (rt == null) return;
            var prev = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                var tex = new Texture2D(640, 360, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 640, 360), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, (++shot).ToString("00") + "-" + name + ".jpg"), ImageConversion.EncodeToJPG(tex, 85));
                Destroy(tex);
            }
            catch (Exception e) { Log("  could not save stream frame: " + e.Message); }
            finally { RenderTexture.active = prev; }
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            string file = Path.Combine(outDir, (++shot).ToString("00") + "-" + name + ".png");
            ScreenCapture.CaptureScreenshot(file);
            yield return new WaitForSeconds(0.8f);
        }

        IEnumerator AiCheck(NewsSystem sys, Browser br, NewsState state, double now)
        {
            var g = NewsSystem.Settings;
            if (!g.CanCallAi) { Log("SKIP AI check: AI is off or no key"); yield break; }
            Log("AI check with " + g.Preset.Name + " / " + g.WriterModel);
            var eng = sys.Engine;
            eng.TrackLaunch("demo3", new CraftSnapshot { VesselName = "Duna Dawn", Parts = 88, MassTons = 140, HeightM = 38, Stages = 4, DeltaV = 7800, CrewCapacity = 3, Crew = { "Valentina Kerman", "Bob Kerman" } }, now + 100);
            var e = new NewsEvent { Type = EventTypes.Soi, UT = now + 200, VesselId = "demo3", VesselName = "Duna Dawn", Body = "Duna" };
            e.Crew.Add("Valentina Kerman");
            e.Crew.Add("Bob Kerman");
            eng.Ingest(e);
            double day = new KspClock().DaySeconds;
            double t = now + 4 * day;
            sys.Sites.Now = () => t;
            int count = state.Articles.Count;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 180 && state.Articles.Count == count)
            {
                eng.Tick(t, Time.realtimeSinceStartup, true);
                yield return new WaitForSeconds(1);
            }
            var fresh = state.Articles.Skip(count).ToList();
            Check(fresh.Count > 0 && fresh.All(a => !a.Offline), "AI wrote " + fresh.Count + " articles in " + (Time.realtimeSinceStartup - start).ToString("0") + " s" + (eng.LastError != "" ? " (last error: " + eng.LastError + ")" : ""));
            if (fresh.Count == 0) yield break;
            var lead = fresh.OrderByDescending(a => a.Body.Length).First();
            Log("AI headline: " + lead.Headline);
            yield return Visit(br, Sites.ArticleUrl(lead));

            br.Act("action:comment/a:" + lead.Id + "/agency", new Dictionary<string, string> { { "comment", "The crew is healthy and the capture burn went exactly to plan. More soon." } });
            var mine = lead.Comments.Last();
            start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 120 && state.CommunityJobs.Count > 0)
            {
                eng.Tick(t + 1, Time.realtimeSinceStartup, true);
                yield return new WaitForSeconds(1);
            }
            var replies = lead.Comments.Where(c => c.ReplyTo == mine.Id || lead.Comments.Any(p => p.Id == c.ReplyTo && p.ReplyTo == mine.Id)).ToList();
            Check(replies.Count > 0, "AI community replied to the official comment: " + replies.Count + " replies");
            foreach (var r in replies.Take(4)) Log("  " + r.Author + ": " + Sites.Unlinked(state, r.Text));
            br.Open(Sites.ArticleUrl(lead));
            yield return new WaitForSeconds(0.5f);
            br.ScrollTo(1);
            yield return Shot("ai-article-comments");

            // The personal assistant: a lookup step, then an action that waits for approval.
            yield return Go("agency.kb/assistant");
            br = Browser.Instance ?? br;
            int before = state.AssistantChat.Count;
            br.Act("action:assistant-send", new Dictionary<string, string> { { "ask", "Give me a two-line briefing on the latest coverage, then donate 5k to the Kerbin Museum of Flight and announce it." } });
            start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 150 && eng.AssistantBusy) yield return new WaitForSeconds(1);
            var said = state.AssistantChat.Skip(before).ToList();
            foreach (var m in said) Log("  assistant " + m.Role + ": " + m.Text.Replace("\n", " / "));
            Check(said.Any(m => m.Role == "assistant"), "assistant answered in " + (Time.realtimeSinceStartup - start).ToString("0") + " s" + (eng.LastError != "" ? " (last error: " + eng.LastError + ")" : ""));
            var ask = said.FirstOrDefault(m => m.Role == "proposal" && m.State == "pending");
            Check(ask != null, "assistant asked before spending");
            if (ask != null) br.Act("action:assistant-yes/" + ask.Id, null);
            Check(state.Deals.Any(d => d.Kind == "donation"), "donation on the paper trail");
            yield return Go("agency.kb/assistant");
            yield return Shot("ai-assistant");
            yield return Go("agency.kb/ledger");
            yield return Shot("ai-ledger");

            // Mods: the scan found something, and the AI hears about it.
            Check(g.Mods.Count > 0, "mod scan found " + g.Mods.Count + " mods, " + g.Mods.Count(m => m.Include && m.Installed) + " shared with the AI");
            Log("  AI sees mods: " + g.ModsText);

            // Crew chat: a kerbal with stats answers in character.
            eng.SyncCrew(new CrewProfile { Name = "Valentina Kerman", Trait = "Pilot", Level = 3, Courage = 0.55, Stupidity = 0.4, Badass = true, Veteran = true });
            var val = state.Crew["Valentina Kerman"];
            int lines = val.Chat.Count;
            eng.CrewSend("Valentina Kerman", "How did the Duna capture feel from the cockpit?", t + 2);
            start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 90 && eng.CrewChatBusy != "") yield return new WaitForSeconds(1);
            var answer = val.Chat.Skip(lines).LastOrDefault(m => m.Role == "crew");
            Check(answer != null && !answer.Text.StartsWith("("), "crew chat: Valentina answered in " + (Time.realtimeSinceStartup - start).ToString("0") + " s" + (answer != null ? ": " + answer.Text : ""));

            // Booking: the assistant schedules her for an interview.
            yield return Go("agency.kb/assistant");   // the ask box only exists on the assistant page
            br = Browser.Instance ?? br;
            before = state.AssistantChat.Count;
            br.Act("action:assistant-send", new Dictionary<string, string> { { "ask", "Book Valentina for an interview with the Orbital Observer tomorrow about the Duna capture." } });
            start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 150 && eng.AssistantBusy) yield return new WaitForSeconds(1);
            var booked = state.Appearances.LastOrDefault(x => x.Kerbal == "Valentina Kerman");
            Check(booked != null && booked.Outlets.Contains("observer"), "assistant booked an interview: " + (booked != null ? booked.Kind + " with " + booked.Outlets + " on " + new KspClock().FormatDate(booked.AtUT) : "none"));
            if (booked == null) foreach (var m in state.AssistantChat.Skip(before)) Log("  assistant " + m.Role + ": " + m.Text.Replace("\n", " / "));
        }

        void Finish(bool quit)
        {
            Log(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            try { File.WriteAllLines(Path.Combine(outDir, "results.txt"), log.ToArray()); }
            catch (Exception e) { Debug.LogError("[KerbalNews] Self-test results: " + e.Message); }
            if (quit) Application.Quit();
        }
    }
}
