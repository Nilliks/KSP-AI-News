using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KerbalNews;
using KerbalNews.Web;

/// <summary>Tests for the in-game browser's HTML, CSS and template engines and the site pages.</summary>
public static class WebTests
{
    const double Day = 21600;
    static void Assert(bool c, string m) { if (!c) throw new Exception("Assertion failed: " + m); }
    static void Eq<T>(T e, T a, string m) { if (!Equals(e, a)) throw new Exception(m + ": expected <" + e + "> got <" + a + ">"); }

    public static void TestHtmlParsing()
    {
        var root = Html.Parse("<!doctype html><html><body><div class=\"a b\" id=x><p>One<p>Two &amp; <b>three</b><br>four</p><img src=y.png><!-- c --><ul><li>a<li>b</ul></div><style>p { color: red; }</style></body></html>");
        var div = root.Descendants().First(n => n.Tag == "div");
        Eq("x", div.Id, "unquoted attribute");
        Assert(div.Classes.Contains("b"), "classes");
        var ps = div.Children.Where(n => n.Tag == "p").ToList();
        Eq(2, ps.Count, "unclosed <p> auto-closes");
        Eq("Two & threefour", ps[1].InnerText(), "entities and inline children");
        Eq(2, root.Descendants().Count(n => n.Tag == "li"), "unclosed <li>");
        Eq("p { color: red; }", root.Find("style").InnerText(), "raw style text");
        Assert(root.Find("img") != null && root.Find("img").Children.Count == 0, "void element");
        Eq("—é", Html.Decode("&mdash;&#233;"), "named and numeric entities");
    }

    public static void TestCssCascade()
    {
        var root = Html.Parse("<body><div class=\"card big\" id=\"main\"><p>Hi <a href=\"x\">link</a></p></div><p class=\"note\" style=\"color: #00ff00\">n</p></body>");
        var sheet = Css.Parse(":root { --accent: #a3201c; } body { color: #111; font-size: 20px; } .card { padding: 4px 8px; background: var(--accent); } #main p { font-size: 1.5em; } .card > p a { color: blue; } p.note { color: red; } a:hover { color: orange; } h1, .big { border-bottom: 2px solid #ccc; }");
        Css.Apply(root, sheet);
        var div = root.Find("div");
        Eq("8px", div.Style.Get("padding-right"), "padding shorthand");
        Eq("#A3201CFF", div.Style.Color("background-color"), "css variable + background shorthand");
        Eq("2px", div.Style.Get("border-bottom-width"), "border shorthand via selector list");
        var p = div.Find("p");
        Eq(30f, p.Style.FontSize, "em resolved against parent");
        Eq("#111111FF", p.Style.Color("color"), "color inherited");
        var a = p.Find("a");
        Eq("#0000FFFF", a.Style.Color("color"), "child combinator + descendant");
        Eq("#FFA500FF", a.Style.Color("hover-color"), "hover stored separately");
        Eq("inline", a.Style.Display, "a is inline");
        var note = root.Descendants().First(n => n.Tag == "p" && n.Attr("class") == "note");
        Eq("#00FF00FF", note.Style.Color("color"), "inline style beats rules");
        Eq(null, Css.NormalizeColor("transparent"), "transparent");
        Eq("#FF000080", Css.NormalizeColor("rgba(255, 0, 0, 0.5)"), "rgba");
    }

    public static void TestTemplates()
    {
        var model = new Dictionary<string, object>
        {
            { "title", "A <b> & c" }, { "show", true }, { "none", new List<object>() },
            { "items", new List<object> { new Dictionary<string, object> { { "n", "x" } }, new Dictionary<string, object> { { "n", "y" } } } },
            { "nested", new Dictionary<string, object> { { "deep", "z" } } },
        };
        Eq("A &lt;b&gt; &amp; c|A <b> & c", Template.Render("{{title}}|{{{title}}}", model), "escaping");
        Eq("[x:1:A <b> & c][y:2:A <b> & c]", Template.Render("{{#items}}[{{n}}:{{@number}}:{{{title}}}]{{/items}}", model), "loops see outer scope");
        Eq("yes-empty-z", Template.Render("{{#show}}yes{{/show}}-{{^none}}empty{{/none}}-{{nested.deep}}", model), "sections");
        Eq("[x]", Template.Render("{{#items.0}}[{{n}}]{{/items.0}}", model), "index path");
        Eq("<i>z</i>", Template.Render("{{> p}}", model, n => n == "p" ? "<i>{{nested.deep}}</i>" : ""), "partials");
    }

    // ------------------------------------------------------------- sites

    static string SitesDir()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 5 && dir != null; i++)
        {
            var cand = Path.Combine(dir, "GameData/KerbalNews/Sites");
            if (Directory.Exists(cand)) return cand;
            dir = Path.GetDirectoryName(dir.TrimEnd('/'));
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "GameData/KerbalNews/Sites");
    }

    static Sites MakeSites(out NewsEngine eng, out double now)
    {
        var s = new NewsState();
        eng = new NewsEngine(s, new GlobalSettings { OfflineMode = true }, null, new SimpleClock(Day));
        eng.Tick(0, 0, true);
        eng.TrackLaunch("v1", new CraftSnapshot { VesselName = "Mun Arrow", Parts = 40, MassTons = 55, HeightM = 22, Stages = 3, DeltaV = 3900, Crew = { "Jebediah Kerman" } }, 10);
        var e = new NewsEvent { Type = EventTypes.Landing, UT = 10, VesselId = "v1", VesselName = "Mun Arrow", Body = "Mun" };
        e.Crew.Add("Jebediah Kerman");
        eng.Ingest(e);
        eng.Tick(Day * 3, 1, true);
        s.Lore.Add(new LoreEntry { UT = 5, Text = "The Duna Union rivalry begins." });
        string dir = SitesDir();
        var sites = new Sites(eng, rel => { var p = Path.Combine(dir, rel); return File.Exists(p) ? File.ReadAllText(p) : null; });
        double t = Day * 3;
        sites.Now = () => t;
        sites.Digest = () => new WorldDigest { Satellites = 2, Debris = 7, DebrisByBody = { { "Kerbin", 7 } } };
        sites.ActiveVesselId = () => "v1";
        sites.ActiveVesselName = () => "Mun Arrow";
        now = t;
        return sites;
    }

    public static void TestEveryPageRenders()
    {
        NewsEngine eng; double now;
        var sites = MakeSites(out eng, out now);
        var art = eng.State.Articles.First();
        var urls = new List<string> { "kerbalnews.kb", "kerbalnews.kb/search?q=mun", "kerbnet.kb", "kerbnet.kb/story/" + art.StoryId, "tracker.kb", "agency.kb", "settings.kb", "telemetry.kb" };
        foreach (var o in Outlets.All) urls.Add(o.Domain);
        foreach (var a in eng.State.Articles) urls.Add(Sites.ArticleUrl(a));
        foreach (var u in urls)
        {
            var page = sites.Render(u);
            Assert(page.Title != "Error" && page.Title != "Not found" && !page.Html.Contains("Missing template"), "page renders: " + u + " -> " + page.Title + " " + page.Html.Substring(0, Math.Min(200, page.Html.Length)));
            Assert(!page.Html.Contains("{{"), "no unrendered tags: " + u);
            var root = Html.Parse(page.Html);
            Css.Apply(root, Css.Parse(page.Css));
            Assert(root.Find("body") != null, "has a body: " + u);
        }
        var portal = sites.Render("kerbalnews.kb").Html;
        Assert(portal.Contains("Mun Arrow") || portal.Contains("Mun"), "portal shows the story");
        Assert(portal.Contains("AI is off"), "AI-off banner");
        var article = sites.Render(Sites.ArticleUrl(art)).Html;
        Assert(article.Contains("40") && article.Contains("parts"), "spec card");
        Assert(article.Contains("action:rewrite/" + art.StoryId), "rewrite link");
        Assert(art.Read, "opening marks read");
        var settings = sites.Render("settings.kb").Html;
        Assert(settings.Contains("AI services") && settings.Contains("What uses what") && !settings.Contains("action:job-model/"), "jobs are closed until Change is clicked");
        sites.Handle("job-open/writer", null);
        settings = sites.Render("settings.kb").Html;
        Assert(settings.Contains("action:job-model/writer/deepseek-chat") && settings.Contains("action:job-model/writer/deepseek-reasoner"), "model picker lists models");
        sites.Handle("job-open/writer", null);
        Eq("Not found", sites.Render("nowhere.kb").Title, "404");
    }

    public static void TestSilentTelemetry()
    {
        var s = new NewsState();
        var eng = new NewsEngine(s, new GlobalSettings { OfflineMode = true }, null, new SimpleClock(Day));
        eng.Tick(0, 0, true);
        s.SetSilent("spy", true);
        eng.TrackLaunch("spy", new CraftSnapshot { VesselName = "Project X", Parts = 99, MassTons = 300 }, 10);
        eng.Ingest(new NewsEvent { Type = EventTypes.Launch, UT = 10, VesselId = "spy", VesselName = "Project X" });
        Assert(eng.Ingest(new NewsEvent { Type = EventTypes.Orbit, UT = 300, VesselId = "spy", VesselName = "Project X", Body = "Kerbin" }) != null, "amateur trackers spot a new object in Kerbin orbit");
        Assert(eng.Ingest(new NewsEvent { Type = EventTypes.Soi, UT = 900, VesselId = "spy", VesselName = "Project X", Body = "Duna" }) == null, "nor is arriving at Duna");
        var story = s.Stories.Single();
        var prompt = PromptBuilder.User(story, CoveragePlan.For(story), s, null, new SimpleClock(), 20);
        eng.Tick(Day * 3, 1, true);
        Assert(story.Silent, "story marked silent");
        var prompt2 = PromptBuilder.User(story, CoveragePlan.For(story), s, null, new SimpleClock(), Day * 3);
        Assert(prompt2.Contains("TELEMETRY SILENT") && !prompt2.Contains("99 parts") && !prompt2.Contains("300"), "prompt hides specs");
        Assert(prompt2.Contains("Seen on the launch pad") && prompt2.Contains("a large rocket"), "the public still sees the rocket on the pad");
        Assert(prompt2.Contains("Amateur satellite trackers"), "orbit described as an amateur sighting");
        Assert(PromptBuilder.DescribeSilent(new NewsEvent { Type = EventTypes.Failure, VesselName = "X", Body = "Kerbin" }).Contains("visible for kilometres"), "explosions over Kerbin are seen");
        Assert(s.Articles.Count > 0 && s.Articles.All(a => !a.Body.Contains("99 parts")), "articles hide specs");

        // Switching to share later lets the next events through; the setting survives save/load.
        s.SetSilent("spy", false);
        Assert(eng.Ingest(new NewsEvent { Type = EventTypes.Landing, UT = Day * 4, VesselId = "spy", VesselName = "Project X", Body = "Duna" }) != null, "shared again");
        s.SilentByDefault = true;
        var n = new DataNode("K"); s.Save(n);
        var back = NewsState.Load(n);
        Assert(!back.IsSilent("spy") && back.IsSilent("new-vessel") && back.SilentByDefault, "privacy saved per vessel and default");
    }

    public static void TestModelLists()
    {
        var oa = Providers.ParseModelList(ApiKind.OpenAICompatible, "{\"data\":[{\"id\":\"deepseek-chat\"},{\"id\":\"text-embedding-3\"},{\"id\":\"deepseek-reasoner\"}]}");
        Eq("deepseek-chat,deepseek-reasoner", string.Join(",", oa.ToArray()), "openai-style list, embeddings filtered");
        var ge = Providers.ParseModelList(ApiKind.Gemini, "{\"models\":[{\"name\":\"models/gemini-3.7-flash\",\"supportedGenerationMethods\":[\"generateContent\"]},{\"name\":\"models/embedding-001\",\"supportedGenerationMethods\":[\"embedContent\"]}]}");
        Eq("gemini-3.7-flash", string.Join(",", ge.ToArray()), "gemini list");
        var req = Providers.BuildListModels(Providers.Get("anthropic"), null, "k");
        Assert(req.Method == "GET" && req.Url.EndsWith("/models?limit=100") && req.Headers["x-api-key"] == "k", "anthropic list request");
        var g = new GlobalSettings();
        g.FetchedModels["deepseek"] = "deepseek-v4-flash deepseek-chat";
        Assert(g.ModelChoices.Contains("deepseek-v4-flash") && g.ModelChoices.Count(x => x == "deepseek-chat") == 1, "fetched models merged without duplicates");
        var sn = new DataNode("S"); var kn = new DataNode("K"); g.Save(sn, kn);
        Assert(GlobalSettings.Load(sn, kn).ModelChoices.Contains("deepseek-v4-flash"), "fetched list saved");
    }
}
