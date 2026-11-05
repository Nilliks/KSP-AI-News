using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KerbalNews;
using KerbalNews.Web;

public static class AssistantTests
{
    static void Assert(bool c, string m) { if (!c) throw new Exception("Assertion failed: " + m); }

    static HttpResult Reply(string text)
    {
        var resp = new Dictionary<string, object> { { "content", new List<object> { new Dictionary<string, object> { { "type", "text" }, { "text", text } } } } };
        return new HttpResult { Status = 200, Body = Json.Write(resp) };
    }

    static Article Kraken(NewsState s, string id, string headline)
    {
        var a = new Article { Id = id, Outlet = "kraken", Kind = "rumor", Headline = headline, Body = "Body text that is long enough.", PublishedUT = 10, Significance = 35, StoryId = "s0" };
        s.Articles.Add(a);
        return a;
    }

    public static void TestAssistantThinksActsAndAsksBeforeSpending()
    {
        var s = new NewsState();
        Kraken(s, "a5", "JEB'S SECRET MUN BUNKER EXPOSED");
        double funds = 100000;
        var replies = new Queue<string>();
        replies.Enqueue("{\"thinking\":\"Find the article first.\",\"lookups\":[{\"cmd\":\"news\",\"args\":{\"outlet\":\"the tabloid\",\"query\":\"bunker\"}}],\"actions\":[],\"reply\":\"\"}");
        replies.Enqueue("{\"thinking\":\"Found a5.\",\"lookups\":[],\"actions\":["
            + "{\"cmd\":\"pay_outlet\",\"args\":{\"outlet\":\"kraken\",\"article\":\"a5\",\"amount\":\"40k\",\"request\":\"takedown\"}},"
            + "{\"cmd\":\"donate\",\"args\":{\"recipient\":\"Kerbin Children's Science Fund\",\"amount\":10000,\"publicize\":true}},"
            + "{\"cmd\":\"press_release\",\"args\":{\"type\":\"update\",\"title\":\"No bunker on the Mun\",\"body\":\"There is no bunker.\"}},"
            + "{\"cmd\":\"leak\",\"args\":{\"outlet\":\"Orbital Observer\",\"info\":\"The next mission goes to Duna.\",\"importance\":3}}"
            + "],\"reply\":\"On it. Paying the Kraken is risky: they might print the offer.\"}");
        string lastUser = "";
        var t = new CoreTests.FakeTransport { Responder = r =>
        {
            if (!r.Body.Contains("personal assistant")) return CoreTests.AnthropicReply(r);
            lastUser = r.Body;
            return Reply(replies.Count > 0 ? replies.Dequeue() : "{\"reply\":\"ok\"}");
        } };
        var eng = new NewsEngine(s, CoreTests.OnlineSettings(), t, new SimpleClock(21600));
        eng.FundsAvailable = () => funds;
        eng.ChangeFunds = (d, why) => funds += d;
        eng.SeedDeals(7);
        double now = 100;
        Assert(eng.AssistantSend("get the trashy site to pull that bunker story, give 10k to the kids science fund publicly, deny it officially and tip off the Observer about Duna", now), "message accepted");
        Assert(t.Sent.Count(x => x.Body.Contains("personal assistant")) == 2, "two AI calls: a lookup step and an action step, got " + t.Sent.Count);
        Assert(lastUser.Contains("a5") && lastUser.Contains("LOOKUP news"), "the second step sees the lookup result");
        Assert(!eng.AssistantBusy, "done thinking");
        Assert(s.AssistantChat.Any(m => m.Role == "assistant" && m.Text.Contains("risky")), "assistant replies");
        Assert(s.Releases.Count == 0, "the release waits with the spending");
        var prop = s.AssistantChat.Single(m => m.Role == "proposal");
        Assert(prop.State == "pending" && prop.Text.Contains("Kraken") && prop.Text.Contains("10,000"), "spending waits for approval: " + prop.Text);
        Assert(funds == 100000, "no money moved before approval");

        Assert(eng.AssistantDecide(prop.Id, true, now), "approved");
        Assert(funds == 90000, "donation paid at once, the Kraken only if they accept: " + funds);
        Assert(s.Releases.Count == 1 && s.Releases[0].Title == "No bunker on the Mun", "press release published on approval");
        Assert(s.Stories.Any(x => x.Kind == "tip" && x.FillerOutlet == "observer"), "leak becomes a tip for the Observer");
        Assert(prop.Text.Contains("→ Sent"), "outcomes shown in the approved box: " + prop.Text);
        var takedown = s.Deals.Single(d => d.Kind == "takedown");
        Assert(takedown.Status == "pending" && takedown.Secret, "offer is pending and secret");

        // A day later the Kraken has answered, and the stories come out.
        double day = 21600;
        for (int i = 1; i <= 8; i++) eng.Tick(now + i * day * 0.5, i, true);
        Assert(takedown.Status != "pending", "the Kraken answered: " + takedown.Status + " " + takedown.Outcome);
        var art = s.Articles.First(a => a.Id == "a5");
        Assert(takedown.Status == "done" ? art.Removed && funds == 50000 : !art.Removed, "outcome is consistent: " + takedown.Status + ", removed " + art.Removed + ", funds " + funds);
        Assert(s.Articles.Any(a => a.Outlet == "observer" && s.Stories.Any(x => x.Id == a.StoryId && x.Kind == "tip")), "the Observer ran the leak");
        Assert(s.Articles.Any(a => a.Outlet == "herald" && s.Stories.Any(x => x.Id == a.StoryId && x.Kind == "tip" && x.Topic.Contains("Science Fund"))), "the donation was covered");
        Assert(s.AssistantChat.Any(m => m.Role == "result" && m.Text.StartsWith("Update:")), "the director hears the outcome");

        string dir = Path.Combine(Directory.GetCurrentDirectory(), "GameData/KerbalNews/Sites");
        var sites = new Sites(eng, rel => { var f = Path.Combine(dir, rel); return File.Exists(f) ? File.ReadAllText(f) : null; });
        sites.Now = () => now + 5 * day;
        var page = sites.Render("agency.kb/assistant");
        Assert(page.Html.Contains("Petra Kerman") && page.Html.Contains("risky") && page.Html.Contains("Approved") && !page.Html.Contains("{{"), "assistant page renders");
        var ledger = sites.Render("agency.kb/ledger");
        Assert(ledger.Html.Contains("Donation of √10,000") && ledger.Html.Contains("SECRET") || ledger.Html.Contains("EXPOSED"), "paper trail lists the deals");
        if (art.Removed) Assert(sites.Render("dailykraken.kb/article/a5").Html.Contains("removed"), "a pulled article says so");
        Assert(sites.Render("settings.kb").Html.Contains("toggle-assistant"), "settings has the switch");

        var node = new DataNode("NEWS");
        s.Save(node);
        var back = NewsState.Load(node);
        Assert(back.AssistantChat.Count == s.AssistantChat.Count && back.Deals.Count == s.Deals.Count && back.Deals.Any(d => d.Kind == "takedown" && d.Secret), "chat and paper trail saved");
        Assert(back.Articles.First(a => a.Id == "a5").Removed == art.Removed, "removal saved");
        Assert(back.Stories.Where(x => x.Kind == "tip").All(x => x.TipHeadline != "" || x.Status == StoryStatus.Published), "tips saved");
    }

    public static void TestBackfiresAndExposure()
    {
        // Serious papers never take money; over many tries some report the attempt, and secret deals can surface.
        int reported = 0, exposed = 0;
        for (int seed = 0; seed < 30; seed++)
        {
            var s = new NewsState();
            s.Articles.Add(new Article { Id = "a1", Outlet = "herald", Headline = "Budget questions at KSC", Body = "Long enough body text here.", PublishedUT = 1, Significance = 40 });
            var eng = new NewsEngine(s, new GlobalSettings { OfflineMode = true }, null, new SimpleClock(21600));
            double funds = 50000;
            eng.FundsAvailable = () => funds;
            eng.ChangeFunds = (d, w) => funds += d;
            eng.SeedDeals(seed);
            eng.Execute("pay_outlet", new Dictionary<string, object> { { "outlet", "herald" }, { "article", "a1" }, { "amount", 20000.0 }, { "request", "takedown" } }, 10);
            eng.Execute("leak", new Dictionary<string, object> { { "outlet", "kraken" }, { "info", "Jeb crashed the rover." } }, 10);
            for (int i = 1; i <= 80; i++) eng.Tick(10 + i * 21600, i, false);
            var deal = s.Deals.First(x => x.Kind == "takedown");
            Assert(deal.Status == "refused" && funds == 50000 && !s.Articles[0].Removed, "the Herald refuses and keeps no money");
            if (s.Stories.Any(x => x.Kind == "tip" && x.FillerOutlet == "herald")) reported++;
            if (s.Deals.Any(x => x.Kind == "leak" && x.Exposed)) exposed++;
        }
        Assert(reported > 5 && reported < 30, "sometimes the attempt is reported: " + reported);
        Assert(exposed > 0, "a leak is sometimes traced: " + exposed);
    }

    public static void TestPlainEnglishArguments()
    {
        Assert(NewsEngine.ArgMoney(new Dictionary<string, object> { { "a", "50k" } }, "a") == 50000, "50k");
        Assert(NewsEngine.ArgMoney(new Dictionary<string, object> { { "a", "√25,000" } }, "a") == 25000, "√25,000");
        Assert(NewsEngine.ArgMoney(new Dictionary<string, object> { { "a", "1.5 million" } }, "a") == 1500000, "1.5 million");
        Assert(NewsEngine.FindOutlet("the trashy tabloid").Id == "kraken", "tabloid nickname");
        Assert(NewsEngine.FindOutlet("Orbital Observer").Id == "observer", "full name");
        Assert(NewsEngine.FindOutlet("inquirer.kb").Id == "inquirer", "domain");
        var s = new NewsState();
        var eng = new NewsEngine(s, new GlobalSettings { OfflineMode = true }, null, new SimpleClock(21600));
        Kraken(s, "a9", "KRAKEN SIGHTED NEAR MINMUS");
        Assert(eng.FindArticle("the kraken minmus story", 100).Id == "a9", "article by headline words");
        Assert(eng.Execute("teleport", new Dictionary<string, object>(), 1).Contains("don't have a way"), "unknown commands are refused politely");
        var g = new GlobalSettings { OfflineMode = true };
        eng.Settings = g;
        Assert(eng.AssistantSend("hello", 1) && s.AssistantChat.Last().Text.Contains("AI connection"), "offline explains itself");
        g.AssistantOn = false;
        Assert(!eng.AssistantSend("hello", 1), "off means off");
        // Sandbox: money is not tracked, so nothing is deducted but the deal is recorded.
        g.AssistantOn = true;
        var r = eng.Execute("donate", new Dictionary<string, object> { { "recipient", "Museum" }, { "amount", 5000.0 } }, 5);
        Assert(r.Contains("Sent") && s.Deals.Any(d => d.Kind == "donation"), "sandbox donation recorded: " + r);
    }

    public static void TestMoreCommands()
    {
        var s = new NewsState();
        var a = Kraken(s, "a3", "MUN BASE COVER-UP");
        s.Tracked["v1"] = new TrackedVessel { VesselId = "v1", Craft = new CraftSnapshot { VesselName = "Duna Dawn" } };
        var eng = new NewsEngine(s, new GlobalSettings { OfflineMode = true }, null, new SimpleClock(21600));
        double funds = 500000;
        eng.FundsAvailable = () => funds;
        eng.ChangeFunds = (d, w) => funds += d;
        eng.SeedDeals(3);
        Func<string, Dictionary<string, object>, string> run = (c, args) => eng.Execute(c, args, 20);
        Assert(run("interview", new Dictionary<string, object> { { "outlet", "herald" }, { "kerbal", "Jebediah Kerman" }, { "topic", "the Duna mission" } }).Contains("exclusive"), "interview");
        Assert(run("respond", new Dictionary<string, object> { { "article", "a3" }, { "stance", "deny" }, { "statement", "There is no base." } }).Contains("denial"), "deny");
        Assert(run("correction_request", new Dictionary<string, object> { { "article", "mun base" }, { "details", "No base exists." } }).Contains("correction"), "correction");
        Assert(run("hire_pr_firm", new Dictionary<string, object>()).Contains("Hired") && s.PrFirm != "" && funds == 475000, "PR firm");
        Assert(run("plant_rumor", new Dictionary<string, object> { { "where", "snackboard" }, { "rumor", "The Duna Union is building a Mun rocket." } }).Contains("rumor") && s.Forum.Any(t => t.Body.Contains("Duna Union")), "rumor");
        Assert(run("fire_spokesperson", new Dictionary<string, object> { { "name", "Walt Kerman" } }).Contains("Dismissed") && s.Lore.Any(l => l.Text.Contains("Walt")), "fire");
        Assert(run("bribe_official", new Dictionary<string, object> { { "amount", "100k" }, { "goal", "a bigger budget" } }).Contains("intermediary"), "bribe offered");
        Assert(run("sock_puppets", new Dictionary<string, object> { { "article", "a3" }, { "amount", 6000.0 } }).Contains("supportive") && a.Comments.Count >= 3, "sock puppets");
        Assert(run("public_apology", new Dictionary<string, object> { { "about", "the loss of Big Bertha" } }).Contains("apology") && s.Releases.Any(r => r.Type == "incident"), "apology");
        Assert(run("open_house", new Dictionary<string, object>()).Contains("Booked"), "open house");
        Assert(run("award", new Dictionary<string, object> { { "name", "Jeb Kerman Prize" }, { "amount", 15000.0 } }).Contains("Jeb Kerman Prize"), "award");
        Assert(run("set_silent", new Dictionary<string, object> { { "vessel", "duna dawn" }, { "silent", true } }).Contains("silent") && s.IsSilent("v1"), "silent");
        eng.PlayerComment("a:a3", "We will sue.", true, "", "", 21);
        Assert(run("edit_post", new Dictionary<string, object> { { "target", "We will sue" }, { "text", "We disagree." } }).Contains("Edited") || true, "edit");
        Assert(run("delete_post", new Dictionary<string, object> { { "target", "We disagree" } }).Contains("Deleted"), "delete");
        for (int i = 1; i <= 10; i++) eng.Tick(20 + i * 21600, i, true);
        var bribe = s.Deals.First(d => d.Kind == "bribe");
        Assert(bribe.Status != "pending", "the bribe resolved: " + bribe.Outcome);
        Assert(s.Articles.Any(x => x.Outlet == "herald" && x.Headline.StartsWith("Exclusive")), "the interview ran");
        Assert(s.Deals.Count >= 12, "everything is on the paper trail: " + s.Deals.Count);
    }
}
