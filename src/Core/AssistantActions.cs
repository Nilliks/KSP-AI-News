using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KerbalNews
{
    public partial class NewsEngine
    {

        // ------------------------------------------------------------------ actions

        string Describe(string cmd, Dictionary<string, object> a)
        {
            var o = FindOutlet(Arg(a, "outlet"));
            switch (cmd)
            {
                case "donate": return "Donate √" + Money(ArgMoney(a, "amount")) + " to " + Arg(a, "recipient") + (ArgBool(a, "publicize") ? " (announced)" : " (quietly)");
                case "pay_outlet":
                    var art = FindArticle(Arg(a, "article"), double.MaxValue);
                    return "Offer " + (o != null ? o.Name : Arg(a, "outlet")) + " √" + Money(ArgMoney(a, "amount")) + (Arg(a, "request") == "favorable" ? " for a favourable story" : " to take down " + (art != null ? "\"" + art.Headline + "\"" : "the article"));
                case "legal_threat":
                    var la = FindArticle(Arg(a, "article"), double.MaxValue);
                    return "Send a legal letter demanding a " + (Arg(a, "demand") == "correction" ? "correction" : "retraction") + " of " + (la != null ? "\"" + la.Headline + "\" (" + Outlets.NameOf(la.Outlet) + ")" : "the article") + ", legal fees about √12,000";
                case "ad_campaign": return "Ad campaign \"" + ResponseParser.Clip(Arg(a, "message"), 80) + "\" for √" + Money(ArgMoney(a, "budget")) + " in " + string.Join(", ", ArgList(a, "outlets").Select(x => { var oo = FindOutlet(x); return oo != null ? oo.Name : x; }).ToArray());
                case "edit_wiki": return "Edit the Kerbopedia page \"" + Arg(a, "page") + "\"";
                case "press_release": return "Publish the press release \"" + ResponseParser.Clip(Arg(a, "title"), 80) + "\"";
                case "leak": return "Leak to " + (o != null ? o.Name : Arg(a, "outlet")) + ": " + ResponseParser.Clip(Arg(a, "info"), 80);
                case "public_apology": return "Publish a public apology about " + Arg(a, "about", "recent events");
                case "kerbnet_post": return "Post on Kerbnet: " + ResponseParser.Clip(Arg(a, "text"), 80);
                case "comment": return "Comment " + (Arg(a, "as", "agency") == "anon" ? "anonymously" : "officially") + ": " + ResponseParser.Clip(Arg(a, "text"), 80);
                case "hire_pr_firm": return "Hire " + Arg(a, "firm", "a PR firm") + " for 30 days, √" + Money(Fee(a, "fee", 25000));
                case "bribe_official": return "Secretly pay " + Arg(a, "official", "a Planetary Council official") + " √" + Money(ArgMoney(a, "amount")) + " for " + Arg(a, "goal", "funding");
                case "sock_puppets": return "Pay √" + Money(Fee(a, "amount", 5000)) + " for fake accounts to back us on " + Arg(a, "article", Arg(a, "thread", "the story"));
                case "open_house": return "Hold \"" + Arg(a, "title", "KSC open house") + "\" for √" + Money(Fee(a, "cost", 20000));
                case "award": return "Fund \"" + Arg(a, "name", "an award") + "\" with √" + Money(ArgMoney(a, "amount"));
                case "fire_spokesperson": return "Dismiss " + Arg(a, "name", "the spokesperson") + " publicly (severance √5,000)";
                default: return cmd;
            }
        }

        static double Fee(Dictionary<string, object> a, string key, double def) { double v = ArgMoney(a, key); return v > 0 ? v : def; }

        /// <summary>Runs one command and reports what happened, in a line for the director.</summary>
        public string Execute(string cmd, Dictionary<string, object> a, double ut)
        {
            try
            {
                switch (cmd)
                {
                    case "press_release":
                    {
                        string vessel = Arg(a, "vessel");
                        var tv = State.Tracked.Values.FirstOrDefault(t => t.VesselId == vessel || (t.Craft != null && vessel != "" && t.Craft.VesselName.Equals(vessel, StringComparison.OrdinalIgnoreCase)));
                        var r = PublishRelease(Arg(a, "type", "announcement"), Arg(a, "title"), Arg(a, "body"), tv != null ? tv.VesselId : "", ArgList(a, "photos"), ut);
                        if (r == null) return "The press release was empty, so nothing was published.";
                        Log("release", "Press release: " + r.Title, 0, false, ut);
                        return "Published the press release \"" + r.Title + "\" in the press room. The press will react within a day.";
                    }
                    case "leak": return Leak(a, ut);
                    case "donate": return Donate(a, ut);
                    case "pay_outlet": return PayOutlet(a, ut);
                    case "legal_threat": return LegalThreat(a, ut);
                    case "ad_campaign": return AdCampaign(a, ut);
                    case "sponsorship": return Sponsorship(a, ut);
                    case "briefing": return Briefing(a, ut);
                    case "comment":
                    {
                        string target = Arg(a, "article");
                        if (target == "") target = Arg(a, "thread");
                        var art = FindArticle(target, ut);
                        var th = art == null ? State.Forum.FirstOrDefault(t => t.Id == target || (target.Length > 4 && t.Title.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0)) : null;
                        if (art == null && th == null) return "I couldn't find where to post that comment.";
                        bool official = Arg(a, "as", "agency") != "anon";
                        var c = PlayerComment(art != null ? "a:" + art.Id : "t:" + th.Id, Arg(a, "text"), official, Arg(a, "handle"), "", ut);
                        if (c == null) return "The comment was empty, so nothing was posted.";
                        if (!official) Log("post", "Anonymous comment on " + (art != null ? "\"" + art.Headline + "\"" : th.Title), 0, true, ut, 0.01);
                        return "Posted " + (official ? "an official" : "an anonymous") + " comment on " + (art != null ? "\"" + art.Headline + "\"" : "\"" + th.Title + "\"") + ".";
                    }
                    case "forum_post":
                    {
                        bool official = Arg(a, "as", "agency") != "anon";
                        var t = PlayerThread(Arg(a, "board", "general"), Arg(a, "title"), Arg(a, "body"), official, Arg(a, "handle"), false, 0, ut);
                        if (t == null) return "The thread needed a title, so nothing was posted.";
                        if (!official) Log("post", "Anonymous Snackboard thread: " + t.Title, 0, true, ut, 0.01);
                        return "Started the Snackboard thread \"" + t.Title + "\" in b/" + t.Board + ".";
                    }
                    case "kerbnet_post":
                    {
                        string text = ResponseParser.Clip(Arg(a, "text"), 280);
                        if (text == "") return "The post was empty.";
                        State.Social.Add(new SocialPost { Handle = "@" + (State.Profile.ShortName != "" ? State.Profile.ShortName : "Agency"), Persona = "official", Text = text, UT = ut, Likes = 20 + dealRng.Next(200), Reshares = 5 + dealRng.Next(60) });
                        return "Posted on the official Kerbnet account.";
                    }
                    case "town_hall":
                    {
                        var t = TownHall(Arg(a, "title", "Town hall with the director"), Arg(a, "body"), Math.Max(3, Math.Min(10, (int)ArgNum(a, "questions", 5))), ut);
                        return t == null ? "Couldn't open the town hall." : "Opened the town hall \"" + t.Title + "\" on Snackboard.";
                    }
                    case "release_photo":
                    {
                        string id = Arg(a, "photo");
                        var g = State.Gallery.FirstOrDefault(x => x.Id == id);
                        if (g == null) return "I couldn't find that photo.";
                        return ReleaseShot(g.Id, Arg(a, "caption", g.Caption), ut) ? "Released the photo to the public gallery." : "That photo was already public.";
                    }
                    case "interview": return Interview(a, ut);
                    case "schedule_appearance": return ScheduleAppearance(a, ut);
                    case "cancel_appearance": return CancelAppearance(a, ut);
                    case "respond": return Respond(a, ut);
                    case "correction_request": return CorrectionRequest(a, ut);
                    case "hire_pr_firm": return HirePr(a, ut);
                    case "plant_rumor": return PlantRumor(a, ut);
                    case "fire_spokesperson": return FireSpokesperson(a, ut);
                    case "bribe_official": return BribeOfficial(a, ut);
                    case "sock_puppets": return SockPuppets(a, ut);
                    case "public_apology":
                    {
                        string about = Arg(a, "about", "recent events");
                        var r = PublishRelease("incident", "An apology from " + AgencyName, Arg(a, "text", AgencyName + " apologises for " + about + ". We take full responsibility and will share what we learn."), "", null, ut);
                        Log("release", "Public apology: " + about, 0, false, ut);
                        return r == null ? "The apology was empty." : "Published the apology in the press room.";
                    }
                    case "open_house": return OpenHouse(a, ut);
                    case "award": return Award(a, ut);
                    case "set_silent":
                    {
                        string vessel = Arg(a, "vessel");
                        var tv = State.Tracked.Values.FirstOrDefault(x => x.VesselId == vessel || (x.Craft != null && vessel != "" && x.Craft.VesselName.IndexOf(vessel, StringComparison.OrdinalIgnoreCase) >= 0));
                        if (tv == null) return "I couldn't find a craft called \"" + vessel + "\".";
                        bool silent = Arg(a, "silent", "true") != "false";
                        State.SetSilent(tv.VesselId, silent);
                        return (tv.Craft != null ? tv.Craft.VesselName : "The craft") + (silent ? " is now silent: the press only sees what anyone can see from the ground." : " now shares its telemetry with the press again.");
                    }
                    case "add_order":
                    {
                        string text = ResponseParser.Clip(Arg(a, "text"), 300);
                        if (text == "") return "There was no order to remember.";
                        if (State.Orders.Count >= 20) return "I already have 20 standing orders; drop one first.";
                        State.Orders.Add(new LoreEntry { UT = ut, Text = text });
                        return "Standing order noted: " + text;
                    }
                    case "remove_order":
                    {
                        int i = RemoveOrder(Arg(a, "order"));
                        return i < 0 ? "I couldn't find that standing order." : "Dropped the standing order.";
                    }
                    case "edit_wiki": return WikiEdit(a, ut);
                    case "delete_post": return EditPost(a, ut, true);
                    case "edit_post": return EditPost(a, ut, false);
                    case "add_lore":
                    {
                        string text = Arg(a, "text");
                        if (text == "") return "Nothing to add to the lore journal.";
                        State.Lore.Add(new LoreEntry { UT = ut, Text = ResponseParser.Clip(text, 600) });
                        return "Added to the lore journal: " + ResponseParser.Clip(text, 120);
                    }
                }
                return "I don't have a way to do \"" + cmd + "\" yet.";
            }
            catch (Exception e) { return "Something went wrong with " + cmd + ": " + e.Message; }
        }

        /// <summary>Removes a standing order by its number (from 1) or by words from it. Returns its index, or -1.</summary>
        public int RemoveOrder(string which)
        {
            which = (which ?? "").Trim();
            int n;
            int i = int.TryParse(which, NumberStyles.Integer, AInv, out n) ? n - 1 : State.Orders.FindIndex(o => which.Length > 2 && o.Text.IndexOf(which, StringComparison.OrdinalIgnoreCase) >= 0);
            if (i < 0 || i >= State.Orders.Count) return -1;
            State.Orders.RemoveAt(i);
            return i;
        }

        static readonly string[] ReporterFirstNames = { "Bill", "Linfrey", "Sonbro", "Maddy", "Gusby", "Haldred", "Neldo", "Tessa", "Corvin", "Dilsy", "Ambrey", "Joleen" };

        /// <summary>Changes how a reporter feels about the agency. With no name, the outlet's best-known reporter (or a new one).</summary>
        public ReporterTie Tie(string outlet, string reporter, int delta, string note, double ut)
        {
            if (string.IsNullOrEmpty(outlet) || Outlets.Get(outlet) == null) return null;
            if (string.IsNullOrEmpty(reporter))
            {
                var known = State.Ties.Where(t => t.Outlet == outlet).OrderByDescending(t => Math.Abs(t.Score)).FirstOrDefault();
                List<string> reps;
                reporter = known != null ? known.Name : State.Reporters.TryGetValue(outlet, out reps) && reps.Count > 0 ? reps[0] : ReporterFirstNames[dealRng.Next(ReporterFirstNames.Length)] + " Kerman";
            }
            List<string> list;
            if (!State.Reporters.TryGetValue(outlet, out list)) State.Reporters[outlet] = list = new List<string>();
            if (!list.Contains(reporter) && list.Count < 6) list.Add(reporter);
            var tie = State.Ties.FirstOrDefault(t => t.Outlet == outlet && t.Name == reporter);
            if (tie == null) { tie = new ReporterTie { Outlet = outlet, Name = reporter }; State.Ties.Add(tie); }
            tie.Score = Math.Max(-10, Math.Min(10, tie.Score + delta));
            if (!string.IsNullOrEmpty(note)) { tie.Notes.Add(Clock.FormatDate(ut) + ": " + note); if (tie.Notes.Count > 6) tie.Notes.RemoveAt(0); }
            return tie;
        }

        string ReporterOf(Article a) { return a != null && !string.IsNullOrEmpty(a.Reporter) ? a.Reporter : null; }

        Deal Log(string kind, string summary, double amount, bool secret, double ut, double exposePerDay = 0, double exposeDays = 40)
        {
            var d = new Deal { Id = State.NewId("d"), Kind = kind, UT = ut, Summary = summary, Amount = amount, Secret = secret, Status = "done", ExposeChance = exposePerDay, ExposeUntilUT = exposePerDay > 0 ? ut + exposeDays * Day : -1 };
            State.Deals.Add(d);
            return d;
        }

        /// <summary>Takes money if there is enough. Sandbox games have no money, so nothing is taken.</summary>
        bool Pay(double amount, string why, out string problem)
        {
            problem = null;
            if (amount <= 0) return true;
            double f = FundsAvailable();
            if (!double.IsNaN(f) && f < amount) { problem = "There isn't enough money: that needs √" + Money(amount) + " and the agency has √" + Money(f) + "."; return false; }
            if (!double.IsNaN(f)) ChangeFunds(-amount, why);
            return true;
        }

        /// <summary>A story only the named outlets write, from what they were told or found.</summary>
        Story Tip(string outlets, string brief, string headline, string body, double significance, double publishUT, string kind = "", string dealId = "")
        {
            var s = new Story
            {
                Id = State.NewId("s"), Kind = "tip", FillerOutlet = outlets, Topic = brief, TipHeadline = headline, TipBody = body, TipKind = kind, DealId = dealId,
                CreatedUT = publishUT, PublishUT = publishUT, Significance = Math.Max(15, Math.Min(70, significance)),
            };
            State.Stories.Add(s);
            return s;
        }

        static readonly Dictionary<string, double> Ethics = new Dictionary<string, double>
        {
            { "herald", 0.5 }, { "observer", 0.55 }, { "inquirer", 0.9 }, { "kraken", 0.1 }, { "munmonthly", 0.35 }, { "kscwatch", 0.9 }, { "science", 0.6 }, { "council", 0.7 },
        };

        string Leak(Dictionary<string, object> a, double ut)
        {
            var o = FindOutlet(Arg(a, "outlet")) ?? Outlets.Get("kraken");
            string info = ResponseParser.Clip(Arg(a, "info"), 1500);
            if (info == "") return "There was nothing to leak.";
            int imp = Math.Max(1, Math.Min(5, (int)ArgNum(a, "importance", 2)));
            Tie(o.Id, null, 2, "got a leak from inside the agency", ut);
            var d = Log("leak", "Leak to " + o.Name + ": " + ResponseParser.Clip(info, 100), 0, true, ut, o.Id == "kraken" ? 0.03 : 0.015);
            d.Target = o.Id; d.Detail = info;
            double when = ut + (0.25 + dealRng.NextDouble() * 0.5) * Day;
            Tip(o.Id, o.Name + " received an anonymous tip from someone apparently inside " + AgencyName + ". The tip says: " + info + "\nThe outlet cannot fully verify it and attributes it to \"a source familiar with the matter\".",
                (o.Id == "kraken" ? "LEAKED: " : "Source: ") + ResponseParser.Clip(info.Split('.')[0], 90), "A source inside the agency told " + o.Name + ": " + info, 15 + imp * 9, when, "", d.Id);
            d.Outcome = o.Name + " has the tip.";
            return "Passed the information to " + o.Name + " through an anonymous channel. Expect a story within a day. There's a chance someone traces it back to us.";
        }

        string Donate(Dictionary<string, object> a, double ut)
        {
            string who = ResponseParser.Clip(Arg(a, "recipient", "a local charity"), 80);
            double amount = ArgMoney(a, "amount");
            if (amount <= 0) return "I need an amount for the donation.";
            string problem;
            if (!Pay(amount, "Donation to " + who, out problem)) return problem;
            bool pub = ArgBool(a, "publicize");
            var d = Log("donation", "Donation of √" + Money(amount) + " to " + who, -amount, false, ut);
            d.Target = who;
            double sig = 14 + Math.Min(30, amount / 5000);
            if (pub)
            {
                bool skeptic = dealRng.NextDouble() < 0.35;
                Tip(skeptic ? "herald,kscwatch" : "herald", AgencyName + " announced a donation of √" + Money(amount) + " to " + who + "." + (skeptic ? " KSC Watch asks whether it is a PR move timed to distract from recent coverage." : ""),
                    AgencyName + " gives √" + Money(amount) + " to " + who, AgencyName + " has donated √" + Money(amount) + " to " + who + ".", sig, ut + 0.3 * Day, "", d.Id);
                d.Outcome = "Announced; the press will pick it up.";
                return "Sent √" + Money(amount) + " to " + who + " and announced it. The Herald will likely cover it" + (skeptic ? ", and the watchdogs may call it a PR move." : ".");
            }
            if (dealRng.NextDouble() < 0.3)
                Tip("herald", who + " publicly thanked " + AgencyName + " for a quiet donation of √" + Money(amount) + " that the agency never announced.", who + " thanks " + AgencyName + " for quiet gift", who + " has thanked " + AgencyName + " for an unannounced donation of √" + Money(amount) + ".", sig - 4, ut + (1 + dealRng.NextDouble() * 4) * Day, "", d.Id);
            d.Outcome = "Sent quietly.";
            return "Sent √" + Money(amount) + " to " + who + " quietly. No announcement.";
        }

        string PayOutlet(Dictionary<string, object> a, double ut)
        {
            var o = FindOutlet(Arg(a, "outlet"));
            var art = FindArticle(Arg(a, "article"), ut);
            if (o == null && art != null) o = Outlets.Get(art.Outlet);
            if (o == null) return "I couldn't tell which outlet to approach.";
            bool favorable = Arg(a, "request") == "favorable" || (art == null && Arg(a, "request") != "takedown");
            if (!favorable && (art == null || art.Outlet != o.Id)) return "I couldn't find that article on " + o.Name + ".";
            double amount = ArgMoney(a, "amount");
            if (amount <= 0) return "I need an amount to offer.";
            double f = FundsAvailable();
            if (!double.IsNaN(f) && f < amount) return "We can't offer √" + Money(amount) + ": the agency has √" + Money(f) + ".";
            var d = Log(favorable ? "coverage" : "takedown", (favorable ? "Offered " + o.Name + " √" + Money(amount) + " for favourable coverage" : "Offered " + o.Name + " √" + Money(amount) + " to pull \"" + art.Headline + "\""), 0, true, ut);
            d.Target = o.Id; d.ArticleId = art != null ? art.Id : ""; d.Offered = amount; d.Status = "pending"; d.Detail = Arg(a, "story", Arg(a, "info"));
            d.ResolveUT = ut + (0.2 + dealRng.NextDouble() * 0.6) * Day;
            return "Made the offer to " + o.Name + " through a go-between. They'll answer within a day; no money moves unless they accept.";
        }

        string LegalThreat(Dictionary<string, object> a, double ut)
        {
            var art = FindArticle(Arg(a, "article"), ut);
            if (art == null) return "I couldn't find that article.";
            const double fee = 12000;
            string problem;
            if (!Pay(fee, "Legal fees", out problem)) return problem;
            Tie(art.Outlet, ReporterOf(art), -4, "threatened with lawyers over \"" + ResponseParser.Clip(art.Headline, 50) + "\"", ut);
            var d = Log("legal", "Legal letter to " + Outlets.NameOf(art.Outlet) + " over \"" + art.Headline + "\"", -fee, false, ut);
            d.Target = art.Outlet; d.ArticleId = art.Id; d.Status = "pending"; d.Detail = Arg(a, "demand", "retraction");
            d.ResolveUT = ut + (0.5 + dealRng.NextDouble()) * Day;
            return "Our lawyers sent the letter to " + Outlets.NameOf(art.Outlet) + " (√" + Money(fee) + " in fees). They usually answer in a day or two.";
        }

        static readonly Dictionary<string, double> AdRates = new Dictionary<string, double>
        {
            { "herald", 8000 }, { "observer", 7000 }, { "inquirer", 6000 }, { "kraken", 2000 }, { "munmonthly", 3000 },
        };

        string AdCampaign(Dictionary<string, object> a, double ut)
        {
            var outlets = ArgList(a, "outlets").Select(FindOutlet).Where(x => x != null).Distinct().ToList();
            if (outlets.Count == 0) outlets = new[] { "herald", "kraken" }.Select(Outlets.Get).ToList();
            double budget = ArgMoney(a, "budget");
            if (budget <= 0) return "I need a budget for the campaign.";
            string msg = ResponseParser.Clip(Arg(a, "message", "the agency's work"), 400);
            double share = budget / outlets.Count;
            var bought = new List<OutletDef>();
            var refused = new List<string>();
            foreach (var o in outlets)
            {
                double rate;
                if (!AdRates.TryGetValue(o.Id, out rate)) { refused.Add(o.Name + " doesn't sell ads"); continue; }
                if (share < rate) { refused.Add(o.Name + " wants at least √" + Money(rate)); continue; }
                bought.Add(o);
            }
            if (bought.Count == 0) return "No outlet took the campaign: " + string.Join("; ", refused.ToArray()) + ".";
            double cost = share * bought.Count;
            string problem;
            if (!Pay(cost, "Ad campaign", out problem)) return problem;
            var d = Log("ad", "Ad campaign \"" + ResponseParser.Clip(msg, 60) + "\" in " + string.Join(", ", bought.Select(o => o.Name).ToArray()), -cost, false, ut);
            Tip(string.Join(",", bought.Select(o => o.Id).ToArray()), AgencyName + " bought sponsored content. Its message: " + msg, msg, "Sponsored content from " + AgencyName + ". " + msg, 18, ut + 0.2 * Day, "sponsored", d.Id);
            d.Outcome = "Running in " + string.Join(", ", bought.Select(o => o.Name).ToArray());
            return "Bought sponsored content in " + string.Join(", ", bought.Select(o => o.Name).ToArray()) + " for √" + Money(cost) + "." + (refused.Count > 0 ? " Not placed: " + string.Join("; ", refused.ToArray()) + "." : "");
        }

        string Sponsorship(Dictionary<string, object> a, double ut)
        {
            string sponsor = ResponseParser.Clip(Arg(a, "sponsor", "a Kerbin company"), 80);
            double amount = ArgMoney(a, "amount");
            if (amount <= 0) amount = 50000;
            var d = Log("sponsorship", "Sponsorship pitch to " + sponsor + " for √" + Money(amount), 0, false, ut);
            d.Target = sponsor; d.Offered = amount; d.Status = "pending"; d.Detail = Arg(a, "terms");
            d.ResolveUT = ut + (1 + dealRng.NextDouble()) * Day;
            return "Pitched " + sponsor + " for √" + Money(amount) + ". They'll come back in a day or two.";
        }

        string Briefing(Dictionary<string, object> a, double ut)
        {
            var outlets = ArgList(a, "outlets").Select(FindOutlet).Where(x => x != null && x.Id != "council").Distinct().ToList();
            if (outlets.Count == 0) outlets = new[] { "herald", "observer", "inquirer" }.Select(Outlets.Get).ToList();
            string topic = ResponseParser.Clip(Arg(a, "topic"), 1500);
            if (topic == "") return "I need a topic for the briefing.";
            double embargo = Math.Max(0.2, Math.Min(10, ArgNum(a, "embargo_days", 1)));
            var d = Log("briefing", "Embargoed briefing for " + string.Join(", ", outlets.Select(o => o.Name).ToArray()) + ": " + ResponseParser.Clip(topic, 80), 0, false, ut);
            var broke = new List<string>();
            foreach (var o in outlets)
            {
                Tie(o.Id, null, 1, "invited to an embargoed briefing", ut);
                double chance = o.Id == "kraken" ? 0.5 : 0.05;
                bool breaks = dealRng.NextDouble() < chance;
                if (breaks) broke.Add(o.Name);
                double when = breaks ? ut + 0.15 * Day : ut + embargo * Day;
                Tip(o.Id, AgencyName + " held a briefing for selected press" + (breaks ? "; this outlet broke the embargo and is publishing early" : ", under an embargo that has now lifted") + ". What the agency told them: " + topic,
                    (breaks ? "Embargo broken: " : "") + ResponseParser.Clip(topic.Split('.')[0], 90), AgencyName + " briefed the press: " + topic, 24, when, "", d.Id);
            }
            d.Outcome = broke.Count > 0 ? "Embargo broken by " + string.Join(", ", broke.ToArray()) : "Embargo held";
            return "Held the briefing for " + string.Join(", ", outlets.Select(o => o.Name).ToArray()) + ", embargoed for " + embargo.ToString("0.#", AInv) + " day(s). Stories run when it lifts.";
        }

        string Interview(Dictionary<string, object> a, double ut)
        {
            var o = FindOutlet(Arg(a, "outlet")) ?? Outlets.Get("herald");
            string who = Arg(a, "kerbal", "the director");
            string topic = ResponseParser.Clip(Arg(a, "topic", "the program"), 600);
            var interviewer = Tie(o.Id, Arg(a, "reporter", null), 3, "got an exclusive interview with " + who, ut);
            var d = Log("interview", "Exclusive interview with " + who + " for " + o.Name + ": " + ResponseParser.Clip(topic, 60), 0, false, ut);
            d.Target = o.Id;
            Tip(o.Id, o.Name + " landed an exclusive sit-down interview with " + who + " of " + AgencyName + " about " + topic + ". Write it as an interview feature with quotes from " + who + " that fit the agency profile and lore. The interviewer and byline is " + interviewer.Name + ".",
                "Exclusive: " + who + " on " + ResponseParser.Clip(topic, 70), o.Name + " spoke with " + who + " about " + topic + ".", 32, ut + 0.4 * Day, o.Id == "kraken" ? "rumor" : "feature", d.Id);
            if (dealRng.NextDouble() < 0.4)
            {
                var rival = new[] { "herald", "observer", "inquirer" }.Where(x => x != o.Id).OrderBy(x => dealRng.Next()).First();
                Tie(rival, null, -1, "snubbed when " + o.Name + " got an exclusive", ut);
                Tip(rival, AgencyName + " gave " + o.Name + " an exclusive interview with " + who + ". " + Outlets.NameOf(rival) + " notes, a little sourly, that the agency picks favourites, and asks what questions " + who + " was not asked.", AgencyName + " picks favourites with " + o.Name + " exclusive", "", 20, ut + 1 * Day, "opinion", d.Id);
            }
            return "Set up an exclusive interview with " + who + " for " + o.Name + ". It runs within a day.";
        }

        string Respond(Dictionary<string, object> a, double ut)
        {
            var art = FindArticle(Arg(a, "article"), ut);
            if (art == null) return "I couldn't find that story.";
            string stance = Arg(a, "stance", "deny");
            string statement = ResponseParser.Clip(Arg(a, "statement"), 800);
            var o = Outlets.Get(art.Outlet);
            if (stance != "confirm") Tie(art.Outlet, ReporterOf(art), stance == "no_comment" ? -1 : -2, (stance == "no_comment" ? "stonewalled on \"" : "had the story \"") + ResponseParser.Clip(art.Headline, 50) + (stance == "no_comment" ? "\"" : "\" publicly denied"), ut);
            else Tie(art.Outlet, ReporterOf(art), 1, "had \"" + ResponseParser.Clip(art.Headline, 50) + "\" confirmed", ut);
            Log("response", (stance == "no_comment" ? "No comment on" : stance == "confirm" ? "Confirmed" : "Denied") + " \"" + art.Headline + "\"", 0, false, ut);
            string brief = stance == "no_comment"
                ? AgencyName + " declined to comment on " + o.Name + "'s story \"" + art.Headline + "\". The silence fuels speculation."
                : stance == "confirm" ? AgencyName + " confirmed " + o.Name + "'s story \"" + art.Headline + "\"." + (statement != "" ? " Statement: " + statement : "")
                : AgencyName + " officially denied " + o.Name + "'s story \"" + art.Headline + "\"." + (statement != "" ? " Statement: " + statement : "") + " The outlet responds; whether it stands by its story depends on how solid its reporting was.";
            Tip(art.Outlet, brief, (stance == "no_comment" ? AgencyName + " silent on " : stance == "confirm" ? AgencyName + " confirms " : AgencyName + " denies ") + ResponseParser.Clip(art.Headline, 80), brief, art.Significance * 0.7 + 8, ut + 0.3 * Day, art.Outlet == "kraken" ? "rumor" : "news");
            if (stance != "no_comment" && statement != "")
                State.Social.Add(new SocialPost { Handle = "@" + (State.Profile.ShortName != "" ? State.Profile.ShortName : "Agency"), Persona = "official", Text = ResponseParser.Clip(statement, 280), UT = ut, Likes = 30 + dealRng.Next(300), Reshares = 10 + dealRng.Next(80) });
            return (stance == "no_comment" ? "Told " + o.Name + " we have no comment." : stance == "confirm" ? "Confirmed the story to " + o.Name + "." : "Issued a denial of the story to " + o.Name + ".") + " Expect a follow-up.";
        }

        string CorrectionRequest(Dictionary<string, object> a, double ut)
        {
            var art = FindArticle(Arg(a, "article"), ut);
            if (art == null) return "I couldn't find that article.";
            Tie(art.Outlet, ReporterOf(art), -1, "asked to correct \"" + ResponseParser.Clip(art.Headline, 50) + "\"", ut);
            var d = Log("correction", "Asked " + Outlets.NameOf(art.Outlet) + " to correct \"" + art.Headline + "\"", 0, false, ut);
            d.Target = art.Outlet; d.ArticleId = art.Id; d.Detail = ResponseParser.Clip(Arg(a, "details"), 600); d.Status = "pending";
            d.ResolveUT = ut + (0.3 + dealRng.NextDouble() * 0.7) * Day;
            return "Sent a correction request to " + Outlets.NameOf(art.Outlet) + ". Editors usually answer within a day.";
        }

        string HirePr(Dictionary<string, object> a, double ut)
        {
            double fee = Fee(a, "fee", 25000);
            string firm = ResponseParser.Clip(Arg(a, "firm", "Bright Orbit Communications"), 60);
            string problem;
            if (!Pay(fee, "PR firm " + firm, out problem)) return problem;
            State.PrFirm = firm;
            State.PrFirmUntilUT = ut + 30 * Day;
            Log("pr", "Hired " + firm + " for 30 days", -fee, true, ut, 0.012, 30);
            return "Hired " + firm + " for 30 days (√" + Money(fee) + "). Our releases will read a lot smoother. The press may eventually notice the spin doctors.";
        }

        string PlantRumor(Dictionary<string, object> a, double ut)
        {
            string rumor = ResponseParser.Clip(Arg(a, "rumor"), 800);
            if (rumor == "") return "I need the rumor to spread.";
            string where = Arg(a, "where", "snackboard").ToLowerInvariant();
            var d = Log("rumor", "Planted a rumor" + (where.Contains("kraken") ? " with the Kraken" : " on Snackboard") + ": " + ResponseParser.Clip(rumor, 80), 0, true, ut, 0.025, 30);
            d.Detail = rumor;
            if (where.Contains("kraken") || where.Contains("tabloid"))
            {
                d.Target = "kraken";
                Tip("kraken", "An \"insider\" fed The Daily Kraken this rumor: " + rumor + ". The Kraken runs it breathlessly with no verification.", "INSIDER: " + ResponseParser.Clip(rumor.Split('.')[0], 90).ToUpperInvariant(), rumor, 30, ut + 0.3 * Day, "rumor", d.Id);
            }
            else
            {
                d.Target = "snackboard";
                var handles = new[] { "totally_not_ksc", "orbit_watcher_99", "pad_rat_insider", "quiet_engineer", "munshot_mike" };
                var t = PlayerThread("general", ResponseParser.Clip(rumor.Split('.')[0], 100), rumor, false, handles[dealRng.Next(handles.Length)], false, 0, ut);
                if (t != null) d.ArticleId = t.Id;
            }
            return "The rumor is out" + (d.Target == "kraken" ? " via the Kraken." : " on Snackboard under a throwaway account.") + " If anyone traces it, it'll look bad.";
        }

        string FireSpokesperson(Dictionary<string, object> a, double ut)
        {
            string name = ResponseParser.Clip(Arg(a, "name", "the agency spokesperson"), 60);
            string reason = ResponseParser.Clip(Arg(a, "reason", "recent communication failures"), 300);
            string problem;
            if (!Pay(5000, "Severance for " + name, out problem)) return problem;
            var d = Log("staff", "Dismissed " + name + " (" + reason + ")", -5000, false, ut);
            State.Lore.Add(new LoreEntry { UT = ut, Text = name + " was dismissed as spokesperson of " + AgencyName + " (" + reason + ")." });
            Tip("herald,kscwatch", AgencyName + " has dismissed its spokesperson " + name + ", citing " + reason + ". KSC Watch asks whether " + name + " is being made a scapegoat for decisions made higher up.", AgencyName + " dismisses spokesperson " + name, AgencyName + " has dismissed " + name + ".", 34, ut + 0.3 * Day, "", d.Id);
            return "Dismissed " + name + " and announced it. The watchdogs will ask if it's a scapegoat move.";
        }

        string BribeOfficial(Dictionary<string, object> a, double ut)
        {
            double amount = ArgMoney(a, "amount");
            if (amount <= 0) return "I need an amount.";
            double f = FundsAvailable();
            if (!double.IsNaN(f) && f < amount) return "We don't have √" + Money(amount) + " to offer.";
            var d = Log("bribe", "Secret payment offer of √" + Money(amount) + " to " + Arg(a, "official", "a Planetary Council official") + " for " + Arg(a, "goal", "funding"), 0, true, ut);
            d.Target = "council"; d.Offered = amount; d.Detail = Arg(a, "goal", "funding"); d.Status = "pending";
            d.ResolveUT = ut + (1 + dealRng.NextDouble() * 2) * Day;
            return "The offer went out through an intermediary. This one is dangerous if it comes out. We'll know in a few days.";
        }

        string SockPuppets(Dictionary<string, object> a, double ut)
        {
            string target = Arg(a, "article");
            if (target == "") target = Arg(a, "thread");
            var art = FindArticle(target, ut);
            var th = art == null ? State.Forum.FirstOrDefault(t => t.Id == target || (target.Length > 4 && t.Title.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0)) : null;
            if (art == null && th == null) return "I couldn't find where to send the accounts.";
            double cost = Fee(a, "amount", 5000);
            string problem;
            if (!Pay(cost, "Online reputation services", out problem)) return problem;
            var texts = ArgList(a, "comments");
            if (texts.Count == 0) texts = new List<string> { "Honestly the agency is doing great work, people forget how hard this is.", "I trust the engineers. Give them a break.", "Best space program in the system, no question.", "Can we talk about how impressive the last launch was?" };
            int n = Math.Max(3, Math.Min(8, (int)(cost / 1500)));
            string tgt = art != null ? "a:" + art.Id : "t:" + th.Id;
            var names = new[] { "happy_kerbal_22", "rocket_fan_live", "truth_in_space", "proud_kerbin_citizen", "orbit_lover_01", "jool_dreamer", "kerbin_first", "space_mom_88" };
            for (int i = 0; i < n; i++) PlayerComment(tgt, texts[i % texts.Count], false, names[i % names.Length], "", ut);
            bool spotted = dealRng.NextDouble() < 0.35;
            var d = Log("sockpuppets", "Paid for " + n + " fake supportive comments on " + (art != null ? "\"" + art.Headline + "\"" : "\"" + th.Title + "\""), -cost, true, ut, spotted ? 0.06 : 0.015, 30);
            if (spotted)
            {
                PlayerComment(tgt, "Anyone else notice " + n + " brand new accounts all praising the agency in the same hour? Bots or paid accounts, for sure.", false, "skeptical_observer", "", ut + 0.05 * Day);
                d.Outcome = "Readers are calling the accounts out.";
            }
            return "Posted " + n + " supportive comments from fresh accounts (√" + Money(cost) + ")." + (spotted ? " Someone's already pointing out they look fake." : "");
        }

        string OpenHouse(Dictionary<string, object> a, double ut)
        {
            double cost = Fee(a, "cost", 20000);
            string title = ResponseParser.Clip(Arg(a, "title", "KSC open house"), 100);
            string problem;
            if (!Pay(cost, title, out problem)) return problem;
            var d = Log("event", title + " (public event)", -cost, false, ut);
            string twist = dealRng.NextDouble() < 0.15 ? " A minor mishap (a long queue, a lost child found safe, a rained-out tour) also made the news." : "";
            Tip("herald,munmonthly", AgencyName + " opened its doors for \"" + title + "\". Thousands of kerbals toured the facilities, met kerbonauts and watched demonstrations." + twist, "Crowds flock to " + title, "Thousands visited " + AgencyName + ".", 28, ut + 1 * Day, "feature", d.Id);
            return "Booked \"" + title + "\" (√" + Money(cost) + "). Coverage comes the day after.";
        }

        string Award(Dictionary<string, object> a, double ut)
        {
            double amount = ArgMoney(a, "amount");
            if (amount <= 0) return "I need an amount for the award.";
            string name = ResponseParser.Clip(Arg(a, "name", "the " + AgencyName + " Scholarship"), 100);
            string who = ResponseParser.Clip(Arg(a, "honoree"), 80);
            string problem;
            if (!Pay(amount, name, out problem)) return problem;
            var d = Log("award", "Funded " + name + (who != "" ? " honouring " + who : "") + ", √" + Money(amount), -amount, false, ut);
            State.Lore.Add(new LoreEntry { UT = ut, Text = AgencyName + " established " + name + (who != "" ? " in honour of " + who : "") + " (√" + Money(amount) + ")." });
            Tip("herald", AgencyName + " established " + name + (who != "" ? " in honour of " + who : "") + ", worth √" + Money(amount) + ".", AgencyName + " launches " + name, "", 20 + Math.Min(15, amount / 10000), ut + 0.4 * Day, "", d.Id);
            return "Set up " + name + " with √" + Money(amount) + " and added it to the lore journal.";
        }

        string EditPost(Dictionary<string, object> a, double ut, bool delete)
        {
            string target = Arg(a, "target");
            if (target == "") return "Which post?";
            Comment found = null; ForumThread foundThread = null; string where = "";
            foreach (var art in State.Articles)
                foreach (var c in art.Comments)
                    if (c.Player && (c.Id == target || c.Text.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0)) { found = c; where = Outlets.NameOf(art.Outlet); }
            foreach (var t in State.Forum)
            {
                if (t.Player && (t.Id == target || t.Title.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0)) { foundThread = t; where = "Snackboard"; }
                foreach (var c in t.Comments)
                    if (c.Player && (c.Id == target || c.Text.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0)) { found = c; where = "Snackboard"; }
            }
            if (found == null && foundThread == null) return "I couldn't find one of our posts matching that.";
            string old = found != null ? found.Text : foundThread.Title + ": " + foundThread.Body;
            if (delete)
            {
                if (found != null) found.Text = "[deleted]";
                else { foundThread.Body = "[deleted]"; foundThread.Closed = true; }
            }
            else
            {
                string text = ResponseParser.Clip(Arg(a, "text"), 2000);
                if (text == "") return "What should it say now?";
                if (found != null) found.Text = text + " (edited)"; else foundThread.Body = text + "\n\n(edited)";
            }
            var d = Log(delete ? "delete" : "edit", (delete ? "Deleted" : "Edited") + " our post on " + where + ": \"" + ResponseParser.Clip(old, 60) + "\"", 0, false, ut);
            if (dealRng.NextDouble() < 0.3)
            {
                Tip("kraken", "Someone screenshotted a post by " + AgencyName + " before it was " + (delete ? "deleted" : "quietly edited") + ". The original said: \"" + ResponseParser.Clip(old, 400) + "\". The Kraken asks what the agency is hiding.", "DELETED! WHAT " + AgencyName.ToUpperInvariant() + " DOESN'T WANT YOU TO SEE", "The original post said: " + ResponseParser.Clip(old, 300), 28, ut + 0.5 * Day, "rumor", d.Id);
                d.Outcome = "Someone had a screenshot.";
                return (delete ? "Deleted it" : "Edited it") + ", but someone had already taken a screenshot.";
            }
            return delete ? "Deleted the post." : "Edited the post.";
        }
    }
}
