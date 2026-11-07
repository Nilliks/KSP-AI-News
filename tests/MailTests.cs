using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KerbalNews;
using KerbalNews.Web;

/// <summary>Kmail: mail after big moments, replies that really set things up, ignored mail, money that waits for the director.</summary>
public static class MailTests
{
    const double Day = 21600;
    static void Assert(bool c, string m) { if (!c) throw new Exception("Assertion failed: " + m); }

    static HttpResult Reply(string text)
    {
        var resp = new Dictionary<string, object> { { "content", new List<object> { new Dictionary<string, object> { { "type", "text" }, { "text", text } } } } };
        return new HttpResult { Status = 200, Body = Json.Write(resp) };
    }

    static DataNode Snap(NewsState s) { var n = new DataNode("K"); s.Save(n); return n; }

    static Sites MakeSites(NewsEngine eng, Func<double> now)
    {
        var dir = Path.Combine(Directory.GetCurrentDirectory(), "GameData/KerbalNews/Sites");
        var sites = new Sites(eng, rel => { var p = Path.Combine(dir, rel); return File.Exists(p) ? File.ReadAllText(p) : null; });
        sites.Now = now;
        return sites;
    }

    /// <summary>An offline engine after a crewed Mun landing has been covered, with Kmail at "More".</summary>
    static NewsEngine Landed(GlobalSettings g, IHttpTransport t)
    {
        var s = new NewsState();
        s.Profile.Name = "Kerbin Space Agency";
        g.MailLevel = 3;
        var eng = new NewsEngine(s, g, t, new SimpleClock(Day));
        eng.SyncCrew(new CrewProfile { Name = "Jebediah Kerman", Trait = "Pilot", Level = 2 });
        eng.SyncCrew(new CrewProfile { Name = "Bill Kerman", Trait = "Engineer", Level = 1 });
        eng.Tick(0, 0, true);
        eng.TrackLaunch("v1", CoreTests.Craft(), 10);
        eng.Ingest(CoreTests.Ev(EventTypes.Landing, 10, "v1", "Mun", "Jebediah Kerman"));
        eng.Tick(Day * 3, 1, true);
        return eng;
    }

    public static void TestKmailAfterABigLanding()
    {
        var g = new GlobalSettings { OfflineMode = true };
        var eng = Landed(g, null);
        var s = eng.State;
        Assert(s.Articles.Count > 0, "the landing was covered");
        Assert(s.Mail.Count == 0, "mail waits until the coverage has been out a while");
        eng.Tick(Day * 3.4, 2, true);
        Assert(s.Mail.Count >= 2, "a big landing brings several emails, got " + s.Mail.Count);
        var press = s.Mail.FirstOrDefault(m => m.Kind == "press" && m.Ask == "interview");
        Assert(press != null && press.Kerbal == "Jebediah Kerman" && press.Open && press.DueUT > Day * 3.4, "an outlet asks for Jeb");
        Assert(press.Subject != "" && press.Body.Contains("Jebediah"), "offline text: " + press.Subject);
        Assert(press.ToAddr == "director@kerbinspaceagency.kb", "addressed to the director: " + press.ToAddr);
        int popups = 0;
        eng.MailArrived += m => popups++;
        double now = Day * 3.8;
        eng.Tick(now, 3, true);
        Assert(popups == 0 || popups <= s.Mail.Count, "pop-ups only for new mail");
        Assert(eng.UnreadMail(now) == s.Mail.Count(m => m.UT <= now), "all unread");

        // The site.
        var sites = MakeSites(eng, () => now);
        var inbox = sites.Render("kmail.kb");
        Assert(inbox.Html.Contains("Kmail") && inbox.Html.Contains(press.Subject) && !inbox.Html.Contains("{{") && !inbox.Html.Contains("Missing template"), "inbox lists the mail");
        Assert(inbox.Html.Contains("Reply by"), "deadline badge");
        Assert(inbox.Html.Contains("Kmail (" + eng.UnreadMail(now) + ")"), "unread count in the KNN bar");
        foreach (var u in new[] { "kmail.kb/needs", "kmail.kb/sent", "kmail.kb/assistant", "kmail.kb/all", "kmail.kb/label/press", "kmail.kb/compose", "kmail.kb/search?q=jeb", Sites.MailUrl(press) })
        {
            var r = sites.Render(u);
            Assert(!r.Html.Contains("{{") && !r.Html.Contains("Missing template") && !r.Title.StartsWith("Error"), "renders " + u);
        }
        var open = sites.Render(Sites.MailUrl(press)).Html;
        Assert(open.Contains("What replying yes does") && open.Contains("Accept: book Jebediah") && open.Contains("did not respond"), "the open email explains the request");
        Assert(press.Read, "opening it marks it read");

        // Saying yes really books the interview.
        sites.Handle("mail-quick/" + press.Id + "/accept", new Dictionary<string, string>());
        Assert(press.Status == "accepted", "accepted: " + press.Status);
        var booked = s.Appearances.LastOrDefault(a => a.Kerbal == "Jebediah Kerman");
        Assert(booked != null && booked.Outlets.Contains(press.Org) && booked.Kind == "interview", "interview booked with that outlet");
        Assert(s.Mail.Any(m => m.Thread == press.Id && m.Outgoing && m.Body.Contains("Jebediah")), "the reply is in the conversation");
        var after = sites.Render(Sites.MailUrl(press)).Html;
        Assert(after.Contains("class=\"status\""), "status shown: " + press.Outcome + " / " + after.Substring(Math.Max(0, after.IndexOf("Back to inbox")), 1500));

        // Ignoring a request has an effect.
        var ignored = new MailMsg { Id = "e900", Thread = "e900", Kind = "press", Org = "observer", From = "Lisa Kerman", FromAddr = "l.kerman@observer.kb", Ask = "statement", Topic = "the Mun landing", Subject = "Request for comment", Body = "Comment?", UT = now, DueUT = now + 0.2 * Day, Status = "open" };
        s.Mail.Add(ignored);
        int tips = s.Stories.Count;
        eng.Tick(now + 0.5 * Day, 4, true);
        Assert(ignored.Status == "expired" && ignored.Outcome.Contains("did not respond"), "the story ran without us: " + ignored.Status);
        Assert(s.Stories.Count > tips, "a tip for the 'did not respond' story");

        // Writing to anyone gets an answer (offline: a short acknowledgement).
        var res = eng.MailSend("Rockomax", "Partnership", "Would you sponsor our Duna flight?", now + 0.6 * Day);
        Assert(res.StartsWith("Sent to Rockomax"), res);
        var sent = s.Mail.Last(m => m.IsRoot && m.Outgoing);
        Assert(sent.Kind == "sponsor" && s.Mail.Any(m => m.Thread == sent.Id && !m.Outgoing), "the company answers");

        // Saved and loaded.
        var back = NewsState.Load(Snap(s));
        Assert(back.Mail.Count == s.Mail.Count && back.Mail.Any(m => m.Id == press.Id && m.Status == "accepted" && m.Kerbal == "Jebediah Kerman"), "mail round-trips");
    }

    public static void TestKmailMoneyAlwaysWaitsForTheDirector()
    {
        var g = new GlobalSettings { OfflineMode = true };
        var eng = Landed(g, null);
        var s = eng.State;
        double funds = 50000;
        eng.FundsAvailable = () => funds;
        eng.ChangeFunds = (d, why) => funds += d;
        double now = Day * 3.5;
        var offer = new MailMsg { Id = "e800", Thread = "e800", Kind = "sponsor", From = "Kerbodyne", FromAddr = "partnerships@kerbodyne.kb", Ask = "sponsor", Amount = 30000, Topic = "the Mun landing", Subject = "Sponsorship proposal", Body = "We'd like to sponsor you.", UT = now - 1, DueUT = now + 4 * Day, Status = "open" };
        s.Mail.Add(offer);
        var sites = MakeSites(eng, () => now);
        sites.Handle("mail-quick/e800/accept", new Dictionary<string, string>());
        Assert(offer.Status == "awaiting" && funds == 50000, "yes alone moves no money: " + offer.Status);
        Assert(sites.Render(Sites.MailUrl(offer)).Html.Contains("Approve: take the"), "an approve button");
        sites.Handle("mail-quick/e800/approve", new Dictionary<string, string>());
        Assert(funds == 80000 && offer.Status == "accepted", "approved: funds " + funds);
        Assert(s.Deals.Any(d => d.Kind == "sponsorship"), "on the paper trail");
        Assert(eng.MailApprove(offer, now).Contains("Nothing") && funds == 80000, "only once");
    }

    public static void TestAssistantAsksBeforeTakingMailMoney()
    {
        var s = new NewsState();
        double now = 100;
        s.Mail.Add(new MailMsg { Id = "e5", Thread = "e5", Kind = "sponsor", From = "Kerbodyne", FromAddr = "partnerships@kerbodyne.kb", Ask = "sponsor", Amount = 20000, Subject = "Sponsorship", Body = "Offer.", UT = 50, DueUT = now + 4 * Day, Status = "awaiting" });
        s.Mail.Add(new MailMsg { Id = "e6", Thread = "e6", Kind = "fan", From = "Minmus Hill Primary", FromAddr = "office@minmushill.kb", Subject = "Hello", Body = "We love rockets!", UT = 60 });
        string lastUser = "";
        var t = new CoreTests.FakeTransport { Responder = r =>
        {
            if (!r.Body.Contains("personal assistant")) return CoreTests.AnthropicReply(r);
            lastUser = r.Body;
            return Reply("{\"thinking\":\"Take the money, thank the school.\",\"lookups\":[],\"actions\":[{\"cmd\":\"approve_mail\",\"args\":{\"email\":\"e5\"}},{\"cmd\":\"reply_mail\",\"args\":{\"email\":\"e6\",\"text\":\"Thank you, we love you too!\"}}],\"reply\":\"I've lined it up; approve below.\"}");
        } };
        var g = CoreTests.OnlineSettings();
        g.AssistantConfirm = false;   // even with approvals off, mail money waits
        var eng = new NewsEngine(s, g, t, new SimpleClock(Day));
        double funds = 1000;
        eng.FundsAvailable = () => funds;
        eng.ChangeFunds = (d, why) => funds += d;
        eng.AssistantSend("take Kerbodyne's money and answer the school", now);
        Assert(lastUser.Contains("KMAIL:") && lastUser.Contains("e5"), "the assistant sees the inbox");
        var prop = s.AssistantChat.SingleOrDefault(m => m.Role == "proposal");
        Assert(prop != null && prop.State == "pending" && prop.Text.Contains("20,000"), "money from mail waits for the director");
        Assert(funds == 1000 && !s.Mail.Any(m => m.Thread == "e6" && m.Outgoing), "nothing happened yet");
        eng.AssistantDecide(prop.Id, true, now);
        Assert(funds == 21000, "approved: " + funds);
        Assert(s.Mail.Any(m => m.Thread == "e6" && m.Outgoing && m.ByAssistant), "the school got its answer, signed by the assistant");
    }
}
