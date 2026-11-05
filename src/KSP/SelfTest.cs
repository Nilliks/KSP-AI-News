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
    public class SelfTest : MonoBehaviour
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
            StartCoroutine(Run(mode.Contains("ai"), !mode.Contains("stay")));
        }

        void Log(string s) { log.Add(s); Debug.Log("[KerbalNews] SELFTEST " + s); }
        void Check(bool ok, string what) { if (!ok) failures++; Log((ok ? "PASS " : "FAIL ") + what); }

        IEnumerator Run(bool withAi, bool quit)
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
                "tracker.kb", "agency.kb", "settings.kb", "telemetry.kb", Sites.Home + "/search?q=mun" };
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
            float scaleBefore = Browser.Scale;
            br.Act("action:scale/+", null);
            yield return new WaitForSeconds(0.6f);
            Check(Browser.Scale > scaleBefore, "A+ makes text bigger: " + scaleBefore.ToString("0.00") + " -> " + Browser.Scale.ToString("0.00"));
            yield return Shot("settings-bigger-text");
            br.Act("action:scale/auto", null);
            g.OfflineMode = wasOffline;

            if (withAi) yield return AiCheck(sys, br, state, now);

            sys.Engine.Digest = oldDigest;
            sys.Sites.Digest = oldSitesDigest;
            sys.Sites.Now = oldNow;
            sys.Attach(new NewsState());
            Finish(quit);
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
            foreach (var r in replies.Take(4)) Log("  " + r.Author + ": " + r.Text);
            br.Open(Sites.ArticleUrl(lead));
            yield return new WaitForSeconds(0.5f);
            br.ScrollTo(1);
            yield return Shot("ai-article-comments");
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
