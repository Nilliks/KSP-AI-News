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

        // ------------------------------------------------------------------ what happens later

        /// <summary>Deals that take time resolve, and secret ones may come out.</summary>
        void TickAssistant(double ut)
        {
            foreach (var d in State.Deals.Where(x => x.Status == "pending" && x.ResolveUT >= 0 && ut >= x.ResolveUT).ToList()) Resolve(d, ut);
            foreach (var d in State.Deals.Where(x => x.Secret && !x.Exposed && x.ExposeChance > 0 && x.ExposeUntilUT > ut && (x.Status == "done" || x.Status == "refused")).ToList())
            {
                double last = d.ResolveUT > d.UT ? d.ResolveUT : d.UT;
                double checkedTo;
                if (!exposeChecked.TryGetValue(d.Id, out checkedTo)) checkedTo = last;
                while (checkedTo + Day <= ut && !d.Exposed)
                {
                    checkedTo += Day;
                    if (dealRng.NextDouble() < d.ExposeChance) Expose(d, checkedTo);
                }
                exposeChecked[d.Id] = checkedTo;
            }
        }
        readonly Dictionary<string, double> exposeChecked = new Dictionary<string, double>();

        void Resolve(Deal d, double ut)
        {
            var o = Outlets.Get(d.Target);
            var art = State.Articles.FirstOrDefault(x => x.Id == d.ArticleId);
            double r = dealRng.NextDouble();
            switch (d.Kind)
            {
                case "takedown":
                case "coverage":
                {
                    double ethic = o != null && Ethics.ContainsKey(o.Id) ? Ethics[o.Id] : 0.5;
                    if (o != null && o.Id == "kraken")
                    {
                        double price = d.Kind == "takedown" ? 8000 + (art != null ? art.Significance : 30) * 600 : 6000;
                        double accept = Math.Min(0.92, 0.15 + 0.75 * d.Offered / price);
                        if (r < accept)
                        {
                            d.Amount = -d.Offered;
                            if (!double.IsNaN(FundsAvailable())) ChangeFunds(-d.Offered, "Payment to " + o.Name);
                            bool doubleCross = dealRng.NextDouble() < 0.12;
                            if (d.Kind == "takedown" && art != null) { art.Removed = true; art.RemovedNote = "This article has been removed."; }
                            else if (d.Kind == "coverage")
                                Tip("kraken", "The agency paid The Daily Kraken (secretly) to run a glowing story. Write a gushing, over-the-top positive piece about " + AgencyName + (d.Detail != "" ? " on this angle: " + d.Detail : "") + ". Do not mention the payment.", AgencyName.ToUpperInvariant() + ": THE HEROES KERBIN DOESN'T DESERVE", "Our reporters have nothing but praise for " + AgencyName + ".", 22, ut + 0.2 * Day, "rumor", d.Id);
                            if (doubleCross)
                            {
                                d.Status = "backfired";
                                d.Outcome = "They took the money, then published a story about the payment.";
                                Tip("kraken", "The Daily Kraken took √" + Money(d.Offered) + " from " + AgencyName + " to " + (d.Kind == "takedown" ? "pull its story" + (art != null ? " \"" + art.Headline + "\"" : "") : "run a puff piece") + ", and now gleefully tells its readers about the bribe.",
                                    "BUSTED: " + AgencyName.ToUpperInvariant() + " TRIED TO BUY US", AgencyName + " paid us √" + Money(d.Offered) + ". We kept it and are telling you anyway.", 45, ut + (1 + dealRng.NextDouble() * 2) * Day, "rumor", d.Id);
                                d.ExposedUT = ut;
                            }
                            else
                            {
                                d.Status = "done";
                                d.Outcome = d.Kind == "takedown" ? "Accepted. The article is gone." : "Accepted. A friendly story is coming.";
                                Tie("kraken", ReporterOf(art), 2, "was paid by the agency", ut);
                                d.ExposeChance = 0.02; d.ExposeUntilUT = ut + 60 * Day;
                            }
                        }
                        else
                        {
                            d.Status = "refused";
                            if (dealRng.NextDouble() < 0.5)
                            {
                                d.Outcome = "They said no, and they're writing about the offer.";
                                Tip("kraken", AgencyName + " tried to pay The Daily Kraken √" + Money(d.Offered) + " to " + (d.Kind == "takedown" ? "kill a story" + (art != null ? " (\"" + art.Headline + "\")" : "") : "write a puff piece") + ". The Kraken refused the \"insultingly low\" offer and now brags about it.",
                                    AgencyName.ToUpperInvariant() + " TRIED TO BRIBE THE KRAKEN", "They offered us √" + Money(d.Offered) + ". We said no.", 40, ut + 0.5 * Day, "rumor", d.Id);
                                d.ExposedUT = ut;
                            }
                            else { d.Outcome = "They turned the offer down."; d.ExposeChance = 0.01; d.ExposeUntilUT = ut + 30 * Day; }
                        }
                    }
                    else
                    {
                        d.Status = "refused";
                        string name = o != null ? o.Name : d.Target;
                        if (o != null && dealRng.NextDouble() < ethic)
                        {
                            d.Outcome = name + " refused and is reporting the attempt.";
                            Tie(o.Id, ReporterOf(art), -4, "was offered a bribe by the agency", ut);
                            Tip(o.Id, AgencyName + " offered " + name + " √" + Money(d.Offered) + " to " + (d.Kind == "takedown" ? "remove its article" + (art != null ? " \"" + art.Headline + "\"" : "") : "publish favourable coverage") + ". The outlet refused and is reporting the attempt as a serious breach of press ethics.",
                                AgencyName + " offered " + name + " money to " + (d.Kind == "takedown" ? "pull a story" : "cover it favourably"), AgencyName + " offered this newspaper √" + Money(d.Offered) + ". We declined.", 50, ut + 0.5 * Day, "news", d.Id);
                            d.ExposedUT = ut;
                        }
                        else { d.Outcome = name + " refused politely and kept it quiet, for now."; d.ExposeChance = 0.02; d.ExposeUntilUT = ut + 40 * Day; if (o != null) Tie(o.Id, ReporterOf(art), -2, "was quietly offered money by the agency", ut); }
                    }
                    break;
                }
                case "legal":
                {
                    string name = o != null ? o.Name : d.Target;
                    bool kraken = d.Target == "kraken";
                    bool soft = art != null && (art.Kind == "rumor" || art.Kind == "opinion");
                    double give = kraken ? 0.35 : soft ? 0.3 : 0.15;
                    double fight = kraken ? 0.4 : 0.45;
                    if (r < give && art != null)
                    {
                        d.Status = "done";
                        if (d.Detail == "correction") { art.Body = art.Body.TrimEnd() + "\n\nCorrection: an earlier version of this article contained claims " + AgencyName + " disputes. We have amended it."; d.Outcome = name + " added a correction."; }
                        else { art.Removed = true; art.RemovedNote = "This article was retracted following a legal complaint."; d.Outcome = name + " retracted the article."; }
                    }
                    else if (r < give + fight)
                    {
                        d.Status = "backfired";
                        d.Outcome = name + " refused and published a story about the legal threat.";
                        Tie(d.Target, ReporterOf(art), -1, "fought off the agency's lawyers", ut);
                        Tip(d.Target, AgencyName + "'s lawyers sent " + name + " a letter demanding a " + d.Detail + (art != null ? " of \"" + art.Headline + "\"" : "") + ". " + name + " stands by its reporting and accuses the agency of trying to intimidate the press. Other readers now pay more attention to the original story.",
                            kraken ? AgencyName.ToUpperInvariant() + " LAWYERS TRY TO SILENCE THE KRAKEN" : AgencyName + " threatens " + name + " with legal action", name + " has received a legal letter from " + AgencyName + " and stands by its story.", 42, ut + 0.4 * Day, kraken ? "rumor" : "news", d.Id);
                    }
                    else { d.Status = "refused"; d.Outcome = name + " ignored the letter."; }
                    break;
                }
                case "sponsorship":
                {
                    double approve = 50 + (State.Mood.Trust - 50) * 0.4 + (State.Mood.Excitement - 40) * 0.3;
                    double chance = Math.Max(0.05, Math.Min(0.85, 0.7 - d.Offered / 500000.0 + (approve - 50) / 150));
                    if (r < chance)
                    {
                        d.Status = "done";
                        d.Amount = d.Offered;
                        if (!double.IsNaN(FundsAvailable())) ChangeFunds(d.Offered, "Sponsorship from " + d.Target);
                        d.Outcome = d.Target + " signed for √" + Money(d.Offered) + ".";
                        Tip("herald", d.Target + " signed a √" + Money(d.Offered) + " sponsorship deal with " + AgencyName + (d.Detail != "" ? ". Terms: " + d.Detail : "") + ".", d.Target + " signs √" + Money(d.Offered) + " deal with " + AgencyName, d.Target + " will sponsor " + AgencyName + ".", 22 + Math.Min(20, d.Offered / 20000), ut + 0.3 * Day, "", d.Id);
                        if (dealRng.NextDouble() < 0.15)
                            Tip("kscwatch", "Questions are being raised about " + AgencyName + "'s sponsorship deal with " + d.Target + ": whether the sponsor gets influence over the program, and what the public was told about the terms.", "Who really pays for " + AgencyName + "? Questions over " + d.Target + " deal", "KSC Watch looks at the strings attached to the " + d.Target + " deal.", 34, ut + (4 + dealRng.NextDouble() * 12) * Day, "", d.Id);
                    }
                    else { d.Status = "refused"; d.Outcome = d.Target + " passed on the offer."; }
                    break;
                }
                case "correction":
                {
                    string name = o != null ? o.Name : d.Target;
                    double ethic = o != null && Ethics.ContainsKey(o.Id) ? Ethics[o.Id] : 0.5;
                    if (art != null && r < (d.Target == "kraken" ? 0.1 : 0.25 + ethic * 0.35))
                    {
                        art.Body = art.Body.TrimEnd() + "\n\nCorrection: " + (d.Detail != "" ? d.Detail : AgencyName + " pointed out errors in this article, which we have corrected.");
                        d.Status = "done"; d.Outcome = name + " added a correction.";
                    }
                    else if (d.Target == "kraken" && r < 0.45)
                    {
                        d.Status = "backfired"; d.Outcome = "The Kraken mocked the request in print.";
                        Tip("kraken", AgencyName + " politely asked The Daily Kraken to correct its story" + (art != null ? " \"" + art.Headline + "\"" : "") + ". The Kraken prints the request and mocks it.", AgencyName.ToUpperInvariant() + " WANTS A CORRECTION. HERE'S OUR ANSWER", "", 26, ut + 0.3 * Day, "rumor", d.Id);
                    }
                    else { d.Status = "refused"; d.Outcome = name + " stands by its story."; }
                    break;
                }
                case "bribe":
                {
                    if (r < 0.4)
                    {
                        d.Status = "done";
                        d.Amount = -d.Offered;
                        double grant = Math.Round(d.Offered * (1.5 + dealRng.NextDouble() * 2) / 1000) * 1000;
                        if (!double.IsNaN(FundsAvailable())) { ChangeFunds(-d.Offered, "Consulting fee"); ChangeFunds(grant, "Planetary Council grant"); }
                        d.Amount = grant - d.Offered;
                        d.Outcome = "The official took it. A √" + Money(grant) + " Council grant appeared soon after.";
                        d.ExposeChance = 0.03; d.ExposeUntilUT = ut + 90 * Day;
                    }
                    else if (r < 0.7) { d.Status = "refused"; d.Outcome = "The official refused and, so far, said nothing."; d.ExposeChance = 0.02; d.ExposeUntilUT = ut + 30 * Day; }
                    else
                    {
                        d.Status = "backfired"; d.ExposedUT = ut;
                        d.Outcome = "It was reported. The Council and the Inquirer are on it.";
                        Tip("council,inquirer", "A Planetary Council official reported that " + AgencyName + " offered them √" + Money(d.Offered) + " in exchange for " + d.Detail + ". The Council press office confirms an inquiry; the Inquirer investigates who authorised it.", "Council opens inquiry into " + AgencyName + " bribery offer", "", 62, ut + 0.5 * Day, "news", d.Id);
                    }
                    break;
                }
                default: d.Status = "done"; break;
            }
            AssistantSay("result", "Update: " + d.Summary + ". " + d.Outcome, ut);
        }

        /// <summary>A reporter finds the records of a secret deal.</summary>
        void Expose(Deal d, double ut)
        {
            d.ExposedUT = ut;
            string by = d.Target == "inquirer" ? "kscwatch" : "inquirer";
            string what = d.Kind == "bribe" ? AgencyName + " secretly paid a Planetary Council official √" + Money(d.Offered) + " shortly before receiving a Council grant"
                : d.Kind == "rumor" ? "a rumor that spread recently was planted by " + AgencyName + " itself"
                : d.Kind == "pr" ? AgencyName + " has quietly hired the spin doctors at " + State.PrFirm
                : d.Kind == "sockpuppets" ? "a wave of supportive online comments was bought and paid for by " + AgencyName
                : d.Kind == "leak" ? "the recent leak to " + Outlets.NameOf(d.Target) + " came from inside " + AgencyName + " itself" : d.Kind == "post" ? "an \"anonymous\" post defending the agency was written from inside " + AgencyName
                : "records show " + AgencyName + " " + (d.Amount < 0 ? "paid" : "offered") + " " + Outlets.NameOf(d.Target) + " √" + Money(d.Offered) + (d.Kind == "takedown" ? " to pull a story" : " for favourable coverage");
            Tip(by, "Documents obtained by " + Outlets.NameOf(by) + " suggest that " + what + ". Details: " + d.Summary + ". " + d.Detail, ResponseParser.Clip(Outlets.NameOf(by) + " investigation: " + what, 140), "Documents suggest " + what + ".", 48, ut, "news", d.Id);
            AssistantSay("result", "Bad news: " + Outlets.NameOf(by) + " found the paper trail for \"" + d.Summary + "\". Expect a story.", ut);
        }

        // ------------------------------------------------------------------ argument helpers

        static string Arg(object o, string key, string def = "")
        {
            var v = Json.Get(o, key);
            if (v == null) return def;
            if (v is string) { var s = ((string)v).Trim(); return s == "" ? def : s; }
            if (v is double) return ((double)v).ToString(AInv);
            if (v is bool) return (bool)v ? "true" : "false";
            return def;
        }

        static double ArgNum(object o, string key, double def)
        {
            var v = Json.Get(o, key);
            if (v is double) return (double)v;
            double d;
            return v is string && double.TryParse(((string)v).Trim(), NumberStyles.Float, AInv, out d) ? d : def;
        }

        static bool ArgBool(object o, string key)
        {
            var v = Json.Get(o, key);
            return v is bool ? (bool)v : v is string && ((string)v).Trim().ToLowerInvariant() == "true";
        }

        static List<string> ArgList(object o, string key)
        {
            var v = Json.Get(o, key);
            var l = Json.Arr(v);
            if (l != null) return l.Select(x => x is string ? (string)x : Convert.ToString(x, AInv)).Where(x => x != "").ToList();
            var s = v as string;
            return s == null ? new List<string>() : s.Split(',').Select(x => x.Trim()).Where(x => x != "").ToList();
        }

        static readonly Regex MoneyRx = new Regex(@"(-?\d[\d,]*(?:\.\d+)?)\s*(k|m|thousand|million)?", RegexOptions.IgnoreCase);

        /// <summary>Amounts may come as numbers or as text like "50k", "√25,000" or "1.5 million".</summary>
        public static double ArgMoney(object o, string key)
        {
            var v = Json.Get(o, key);
            if (v is double) return Math.Max(0, (double)v);
            var s = v as string;
            if (s == null) return 0;
            var m = MoneyRx.Match(s);
            if (!m.Success) return 0;
            double d;
            if (!double.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Float, AInv, out d)) return 0;
            string u = m.Groups[2].Value.ToLowerInvariant();
            if (u == "k" || u == "thousand") d *= 1000;
            if (u == "m" || u == "million") d *= 1000000;
            return Math.Max(0, d);
        }

        /// <summary>An outlet by id, name, domain or nickname ("the tabloid", "the watchdog").</summary>
        public static OutletDef FindOutlet(string s)
        {
            s = (s ?? "").Trim().ToLowerInvariant();
            if (s == "") return null;
            var o = Outlets.All.FirstOrDefault(x => x.Id == s || x.Name.ToLowerInvariant() == s || x.Domain == s);
            if (o != null) return o;
            if (s.Contains("kraken") || s.Contains("tabloid") || s.Contains("trash")) return Outlets.Get("kraken");
            if (s.Contains("watch") || s.Contains("watchdog")) return Outlets.Get("kscwatch");
            if (s.Contains("science") || s.Contains("review")) return Outlets.Get("science");
            if (s.Contains("council") || s.Contains("government")) return Outlets.Get("council");
            if (s.Contains("mun monthly") || s.Contains("monthly") || s.Contains("enthusiast")) return Outlets.Get("munmonthly");
            return Outlets.All.FirstOrDefault(x => x.Name.ToLowerInvariant().Contains(s) || s.Contains(x.Id));
        }

        /// <summary>An article by id or by words from its headline, newest first.</summary>
        public Article FindArticle(string s, double ut)
        {
            s = (s ?? "").Trim();
            if (s == "") return null;
            var byId = State.Articles.FirstOrDefault(a => a.Id == s && a.PublishedUT <= ut);
            if (byId != null) return byId;
            var words = Words(s.ToLowerInvariant()).ToList();
            if (words.Count == 0) return null;
            return State.PublishedArticles(ut)
                .Select(a => new { a, score = words.Count(w => a.Headline.ToLowerInvariant().Contains(w)) })
                .Where(x => x.score * 2 >= words.Count).OrderByDescending(x => x.score).Select(x => x.a).FirstOrDefault();
        }
    }
}
