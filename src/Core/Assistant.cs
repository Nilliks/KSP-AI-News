using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KerbalNews
{
    /// <summary>One line of the assistant chat.</summary>
    public class AssistantMessage
    {
        public string Id = "";
        /// <summary>player, assistant, result (what actually happened), or proposal (waiting for the director's OK).</summary>
        public string Role = "player";
        public string Text = "";
        public double UT;
        /// <summary>For a proposal: the actions as JSON, and pending / confirmed / cancelled.</summary>
        public string Actions = "";
        public string State = "";

        public void Save(DataNode n)
        {
            n.Add("id", Id); n.Add("role", Role); n.AddText("text", Text); n.Add("ut", UT);
            if (Actions != "") { n.AddText("actions", Actions); n.Add("state", State); }
        }

        public static AssistantMessage Load(DataNode n)
        {
            return new AssistantMessage { Id = n.Get("id", ""), Role = n.Get("role", "player"), Text = n.GetText("text"), UT = n.GetDouble("ut"), Actions = n.GetText("actions"), State = n.Get("state", "") };
        }
    }

    /// <summary>
    /// The paper trail: everything the assistant did for the agency, what it cost, and how it turned out.
    /// Secret deals can come out later, when a reporter finds the records.
    /// </summary>
    public class Deal
    {
        public string Id = "";
        public string Kind = "";        // donation, takedown, coverage, legal, leak, ad, sponsorship, briefing, release, post
        public double UT;
        public string Target = "";      // outlet id, charity or company name
        public string ArticleId = "";
        /// <summary>Money moved: negative is spent, positive is received. Zero until it actually changes hands.</summary>
        public double Amount;
        /// <summary>Money offered or asked for.</summary>
        public double Offered;
        public string Summary = "";
        public string Detail = "";
        public bool Secret;
        public string Status = "pending"; // pending, done, refused, backfired
        public double ResolveUT = -1;
        public string Outcome = "";
        /// <summary>Chance per day that a reporter finds the records, while the window is open.</summary>
        public double ExposeChance;
        public double ExposeUntilUT = -1;
        public double ExposedUT = -1;

        public bool Exposed { get { return ExposedUT >= 0; } }

        public void Save(DataNode n)
        {
            n.Add("id", Id); n.Add("kind", Kind); n.Add("ut", UT); n.AddText("target", Target); n.Add("article", ArticleId);
            n.Add("amount", Amount); n.Add("offered", Offered); n.AddText("summary", Summary); n.AddText("detail", Detail); n.Add("secret", Secret);
            n.Add("status", Status); n.Add("resolve", ResolveUT); n.AddText("outcome", Outcome); n.Add("expose", ExposeChance); n.Add("exposeUntil", ExposeUntilUT); n.Add("exposed", ExposedUT);
        }

        public static Deal Load(DataNode n)
        {
            return new Deal
            {
                Id = n.Get("id", ""), Kind = n.Get("kind", ""), UT = n.GetDouble("ut"), Target = n.GetText("target"), ArticleId = n.Get("article", ""),
                Amount = n.GetDouble("amount"), Offered = n.GetDouble("offered"), Summary = n.GetText("summary"), Detail = n.GetText("detail"), Secret = n.GetBool("secret"),
                Status = n.Get("status", "pending"), ResolveUT = n.GetDouble("resolve", -1), Outcome = n.GetText("outcome"), ExposeChance = n.GetDouble("expose"),
                ExposeUntilUT = n.GetDouble("exposeUntil", -1), ExposedUT = n.GetDouble("exposed", -1),
            };
        }
    }

    /// <summary>
    /// The director's personal assistant: a plain-English chat that looks things up and acts through a fixed command list.
    /// The model plans and writes; the engine decides what really happens, so bribes, leaks and threats can backfire.
    /// </summary>
    public partial class NewsEngine
    {
        public const int AssistantMaxSteps = 4;
        public const string AssistantName = "Petra Kerman";

        /// <summary>The game's money. NaN when the game has none (sandbox, science).</summary>
        public Func<double> FundsAvailable = () => double.NaN;
        /// <summary>Adds (positive) or spends (negative) money, with a reason for the game's ledger.</summary>
        public Action<double, string> ChangeFunds = (d, why) => { };
        public string AssistantStatus = "";
        public bool AssistantBusy { get; private set; }
        int assistantRun;
        Random dealRng = new Random();
        /// <summary>Tests fix the dice.</summary>
        public void SeedDeals(int seed) { dealRng = new Random(seed); }

        static readonly CultureInfo AInv = CultureInfo.InvariantCulture;

        // ------------------------------------------------------------------ chat

        public AssistantMessage AssistantSay(string role, string text, double ut)
        {
            var m = new AssistantMessage { Id = State.NewId("m"), Role = role, Text = text ?? "", UT = ut };
            State.AssistantChat.Add(m);
            if (State.AssistantChat.Count > NewsState.MaxAssistantMessages * 2) State.AssistantChat.RemoveRange(0, State.AssistantChat.Count - NewsState.MaxAssistantMessages);
            RaiseCommunity();
            return m;
        }

        /// <summary>The director writes to the assistant. The answer arrives later (one to four AI calls).</summary>
        public bool AssistantSend(string text, double ut)
        {
            text = (text ?? "").Trim();
            if (text == "" || !Settings.AssistantOn) return false;
            AssistantSay("player", ResponseParser.Clip(text, 2000), ut);
            if (!Settings.CanCallAi)
            {
                AssistantSay("assistant", "I can only help with that when the AI connection is on. Turn off offline mode in Settings and pick a provider.", ut);
                return true;
            }
            int run = ++assistantRun;
            AssistantBusy = true;
            AssistantStatus = AssistantName + " is thinking...";
            AssistantStep(run, 1, new List<string>(), ut, 0);
            return true;
        }

        void AssistantStep(int run, int step, List<string> notes, double ut, int failures)
        {
            var preset = Settings.Preset;
            var req = Providers.Build(preset, Settings.BaseUrl, Settings.Key, Settings.WriterModel, AssistantSystem(), AssistantUser(step, notes, ut), Math.Min(Settings.MaxTokens, 4000), preset.JsonMode);
            req.TimeoutSeconds = Settings.TimeoutSeconds;
            Transport.Send(req, res =>
            {
                if (run != assistantRun) return; // a reload or a newer request replaced this one
                string text, err;
                object root = null;
                if (Providers.TryExtractText(preset.Kind, res, out text, out err))
                {
                    var json = ResponseParser.ExtractJson(text) ?? "";
                    if (!Json.TryParse(json, out root) || Json.Obj(root) == null)
                    {
                        var fixedJson = ResponseParser.Repair(text);
                        if (fixedJson == null || !Json.TryParse(fixedJson, out root) || Json.Obj(root) == null) { root = null; err = "Reply was not JSON"; }
                    }
                }
                if (root == null)
                {
                    LastError = err ?? "No reply";
                    if (failures < 1 && !IsPermanent(res)) { AssistantStep(run, step, notes, ut, failures + 1); return; }
                    AssistantBusy = false;
                    AssistantStatus = "";
                    AssistantSay("assistant", "Sorry, I couldn't get through just now (" + LastError + "). Try again in a moment.", ut);
                    return;
                }
                var lookups = Json.Arr(Json.Get(root, "lookups")) ?? new List<object>();
                if (lookups.Count > 0 && step < AssistantMaxSteps)
                {
                    foreach (var l in lookups.Take(5))
                    {
                        string cmd = Arg(l, "cmd");
                        var args = Json.Obj(Json.Get(l, "args")) ?? new Dictionary<string, object>();
                        notes.Add("LOOKUP " + cmd + " " + Json.Write(args) + "\nRESULT:\n" + Lookup(cmd, args, ut));
                    }
                    AssistantStatus = AssistantName + " is looking things up (" + string.Join(", ", lookups.Take(5).Select(l => Arg(l, "cmd")).ToArray()) + ")...";
                    RaiseCommunity();
                    AssistantStep(run, step + 1, notes, ut, 0);
                    return;
                }
                AssistantBusy = false;
                AssistantStatus = "";
                string reply = Arg(root, "reply");
                var actions = Json.Arr(Json.Get(root, "actions")) ?? new List<object>();
                if (reply != "") AssistantSay("assistant", reply, ut);
                AssistantAct(actions, ut);
                if (reply == "" && actions.Count == 0) AssistantSay("assistant", "Done.", ut);
            });
        }

        /// <summary>Runs what can run now; spending waits for the director's OK when that setting is on.</summary>
        void AssistantAct(List<object> actions, double ut)
        {
            var list = actions.Take(8).ToList();
            // If anything costs money, the whole plan waits, so an announcement never goes out before the deal is approved.
            if (!Settings.AssistantConfirm || !list.Any(a => Spends(Arg(a, "cmd"))))
            {
                foreach (var a in list) AssistantSay("result", Execute(Arg(a, "cmd"), Json.Obj(Json.Get(a, "args")) ?? new Dictionary<string, object>(), ut), ut);
                return;
            }
            var waiting = list;
            var p = AssistantSay("proposal", "Needs your OK:\n" + string.Join("\n", waiting.Select(a => "- " + Describe(Arg(a, "cmd"), Json.Obj(Json.Get(a, "args")) ?? new Dictionary<string, object>())).ToArray()), ut);
            RaiseCommunity();
            p.Actions = Json.Write(waiting);
            p.State = "pending";
        }

        /// <summary>The director answers a proposal.</summary>
        public bool AssistantDecide(string messageId, bool approve, double ut)
        {
            var p = State.AssistantChat.FirstOrDefault(m => m.Id == messageId && m.Role == "proposal" && m.State == "pending");
            if (p == null) return false;
            p.State = approve ? "confirmed" : "cancelled";
            if (!approve) { p.Text += "\nNothing was sent."; RaiseCommunity(); return true; }
            object root;
            var list = Json.TryParse(p.Actions, out root) ? Json.Arr(root) : null;
            // Outcomes go into the approved box itself, so the chat reads in order.
            foreach (var a in list ?? new List<object>())
                p.Text += "\n→ " + Execute(Arg(a, "cmd"), Json.Obj(Json.Get(a, "args")) ?? new Dictionary<string, object>(), ut);
            RaiseCommunity();
            return true;
        }

        public void AssistantClear() { State.AssistantChat.Clear(); assistantRun++; AssistantBusy = false; AssistantStatus = ""; }

        static bool Spends(string cmd)
        {
            return cmd == "donate" || cmd == "pay_outlet" || cmd == "legal_threat" || cmd == "ad_campaign" || cmd == "hire_pr_firm" || cmd == "bribe_official"
                || cmd == "sock_puppets" || cmd == "open_house" || cmd == "award" || cmd == "fire_spokesperson";
        }

        void RaiseCommunity() { if (CommunityChanged != null) CommunityChanged(); }

        // ------------------------------------------------------------------ prompts

        public const string AssistantCommands = @"LOOKUPS (read-only; results come back to you):
- news {query?, outlet?, limit?}: recent published articles with ids (a12), outlet, date, kind, headline.
- read {article}: one article (id or words from its headline): body and top comments.
- forum {query?}: Snackboard threads with ids (t5).
- finances {}: current funds and recent spending and income.
- ledger {}: the full paper trail of your past deals, including secret ones and whether they leaked.
- vessels {}: the agency's tracked craft and whether each is silent.
- gallery {}: photos with ids (g3), captions, public or private.
- mood {}: public mood and the latest poll.
- outlets {}: every outlet's id, name and character.
- lore {}: the agency profile and lore journal.
- reporters {}: named reporters at each outlet and how they feel about the agency (goodwill or grudges, and why).
- orders {}: your standing orders.
- crew {}: the roster: each kerbal's trait, level, where they are, fame, and any scheduled interviews or briefings.
ACTIONS:
- press_release {type: announcement|update|incident|teaser, title, body, vessel?, photos?: [gallery ids]}: official release on the press room. Write the text yourself if not given.
- leak {outlet, info, importance?: 1-5}: anonymously give information to one outlet. It runs a story within a day. Leaks can be traced back to the agency.
- donate {recipient, amount, publicize?: true|false}: give agency money to a charity, school, museum, relief fund, etc.
- pay_outlet {outlet, article?, amount, request: takedown|favorable}: secretly pay an outlet to pull an article or to run a favourable story. Only some outlets take money; others refuse and may report it. Money only changes hands if accepted.
- legal_threat {article, demand: retraction|correction}: lawyers send a letter (costs legal fees, about 12000). Can make a story bigger.
- ad_campaign {outlets: [ids], message, budget}: buy labelled sponsored content. Budget is split across outlets; some outlets refuse ads.
- sponsorship {sponsor, amount, terms?}: pitch a company for sponsorship money (income). The answer comes in a day or two.
- briefing {outlets: [ids], topic, embargo_days?}: embargoed press briefing; stories run when the embargo lifts (some outlets break embargoes).
- comment {article or thread, text, as: agency|anon, handle?}: post a comment.
- forum_post {board, title, body, as: agency|anon, handle?}: new Snackboard thread. Boards: general, launches, missions, science, policy, memes, ama.
- kerbnet_post {text}: post on the agency's official Kerbnet account.
- town_hall {title, body, questions?: 3-10}: open a town hall Q&A on Snackboard.
- release_photo {photo, caption?}: make a private gallery photo public.
- add_lore {text}: add an entry to the lore journal (becomes canon for the press).
- interview {outlet, kerbal, topic}: give one outlet an exclusive interview with a named kerbal. Rival outlets may grumble.
- schedule_appearance {kerbal, kind: interview|briefing, outlet? (interview), outlets?: [ids] (briefing), when, topic}: book one of the crew (a real kerbal on the roster) for a future interview with one outlet or a press briefing. when: ""tomorrow"", ""in 3 days"", ""day 120"", ""in 6 hours"". The director can then coach the kerbal in crew chat; how well they stick to it depends on their personality.
- cancel_appearance {kerbal or appearance id}: call off a scheduled interview or briefing. The outlet will be annoyed.
- respond {article, stance: deny|no_comment|confirm, statement?}: the agency's official response to a specific story.
- correction_request {article, details}: politely ask an outlet to correct facts (free, no lawyers).
- hire_pr_firm {firm?, fee?}: hire a PR firm for 30 days (default fee 25000); releases come out more polished. Spin doctors can become a story.
- plant_rumor {where: snackboard|kraken, rumor}: secretly spread a fake or distracting rumor. Can be traced back.
- fire_spokesperson {name, reason?}: publicly dismiss a spokesperson (a scapegoat after a disaster); severance about 5000.
- bribe_official {official?, amount, goal}: secretly pay a Planetary Council official for funding or favours. High risk; may pay back a grant.
- sock_puppets {article or thread, amount, comments?: [texts]}: pay for fake accounts to post supportive comments. Readers may spot them.
- public_apology {about, text?}: an official apology (for a crew loss, failure, scandal).
- open_house {title?, cost?}: a public open day or fan event at KSC (default cost 20000).
- award {name, honoree?, amount}: fund an award or scholarship, e.g. in a kerbal's name.
- set_silent {vessel, silent: true|false}: keep a craft's telemetry from the press, or share it again.
- delete_post {target}: delete one of the agency's own comments or threads (id or words from it). Someone may have screenshotted it.
- edit_post {target, text}: change the text of one of the agency's own comments or threads.
- add_order {text}: remember a standing order from the director (""from now on..."", ""always..."", ""never...""). Follow it in every later request.
- remove_order {order}: drop a standing order (its number or words from it).";

        string AssistantSystem()
        {
            var sb = new StringBuilder();
            string agency = AgencyName;
            sb.AppendLine("You are " + AssistantName + ", the personal assistant and chief of staff to the director of " + agency + ", a space agency in the Kerbin universe (kerbals, the Mun, Minmus, Duna; currency is funds, written √). The person writing to you is the director.");
            sb.AppendLine("You act only through the commands below. The director writes in plain English and may phrase things any way; work out which commands they mean, and chain several if needed. If an essential detail is missing (an amount, which article), look it up or pick a sensible default and say which. If the request is truly unclear, ask one short question and take no action.");
            sb.AppendLine("Be a capable, loyal, realistic aide: carry out what the director asks, but warn briefly when something is risky (bribes can backfire, leaks can be traced, legal threats can draw attention). Never say an action succeeded: you do not know yet. Results are reported to the director separately after you act.");
            sb.AppendLine("When asked for a briefing or summary, use lookups (news, mood, finances, forum) and answer in a few short paragraphs. Keep replies short and conversational otherwise.");
            sb.AppendLine("Write any press release, comment or post text yourself, in good in-universe style, unless the director gives exact wording. Stay consistent with the agency profile and lore.");
            sb.AppendLine("When the director gives a lasting instruction (\"from now on\", \"always\", \"never\", a spending limit), store it with add_order.");
            sb.AppendLine("Reporters remember how the agency treats them: exclusives and tips build goodwill, threats, bribe attempts, denials and snubs build grudges. Use the reporters lookup when choosing who to approach.");
            sb.AppendLine();
            sb.AppendLine(AssistantCommands);
            sb.AppendLine();
            sb.AppendLine("Reply with JSON only, in this shape:");
            sb.AppendLine("{\"thinking\": \"one or two sentences\", \"lookups\": [{\"cmd\": \"news\", \"args\": {\"query\": \"...\"}}], \"actions\": [{\"cmd\": \"donate\", \"args\": {\"recipient\": \"...\", \"amount\": 25000}}], \"reply\": \"what you tell the director\"}");
            sb.AppendLine("Use lookups when you need ids or facts; you will get the results and continue. When lookups is empty you are done: actions run and your reply is shown. Do not put lookups and actions in the same reply.");
            // Everything above is fixed, so providers can cache it. Things the director changes go last.
            sb.AppendLine();
            if (Settings.AssistantConfirm) sb.AppendLine("The director has asked to approve anything that costs money: when your plan includes spending, the whole plan waits for an Approve button. So say what you have prepared (\"I've lined up...\", \"approve below and I'll send it\"), never that it is done.");
            if (Settings.ModsText != "") sb.AppendLine("Installed mods in this world: " + Settings.ModsText + ".");
            if (State.Orders.Count > 0)
            {
                sb.AppendLine("STANDING ORDERS FROM THE DIRECTOR (always follow these unless the director's new message clearly overrides one; if a request conflicts, say so):");
                for (int i = 0; i < State.Orders.Count; i++) sb.AppendLine((i + 1) + ". " + State.Orders[i].Text);
            }
            return sb.ToString();
        }

        string AssistantUser(int step, List<string> notes, double ut)
        {
            var sb = new StringBuilder();
            sb.AppendLine("TODAY: " + Clock.FormatDate(ut) + ".");
            sb.AppendLine("AGENCY: " + AgencyName + (State.Profile.KeyPeople != "" ? ". Key people: " + State.Profile.KeyPeople : "") + ".");
            sb.AppendLine("FUNDS: " + FundsText() + ".");
            sb.AppendLine("PUBLIC MOOD: " + State.Mood.Describe());
            var recent = State.PublishedArticles(ut).Take(10).ToList();
            if (recent.Count > 0)
            {
                sb.AppendLine("LATEST HEADLINES:");
                foreach (var a in recent) sb.AppendLine("- " + a.Id + " | " + Outlets.NameOf(a.Outlet) + " | " + Clock.FormatDate(a.PublishedUT) + " | " + a.Headline);
            }
            var open = State.Deals.Where(d => d.Status == "pending").ToList();
            if (open.Count > 0) sb.AppendLine("DEALS STILL OPEN: " + string.Join("; ", open.Select(d => d.Summary).ToArray()));
            sb.AppendLine("CONVERSATION (oldest first; the last line is the request to handle now):");
            foreach (var m in State.AssistantChat.Skip(Math.Max(0, State.AssistantChat.Count - 14)))
                sb.AppendLine((m.Role == "player" ? "DIRECTOR: " : m.Role == "assistant" ? "YOU: " : m.Role == "proposal" ? "(waiting for the director's OK, " + m.State + ") " : "(result) ") + m.Text);
            if (notes.Count > 0)
            {
                sb.AppendLine("YOUR LOOKUPS SO FAR:");
                foreach (var n in notes) sb.AppendLine(n);
            }
            sb.AppendLine("STEP " + step + " of " + AssistantMaxSteps + (step >= AssistantMaxSteps ? ". This is the last step: no more lookups, act and reply now." : "."));
            return sb.ToString();
        }

        string FundsText()
        {
            double f = FundsAvailable();
            return double.IsNaN(f) ? "this game does not track money (sandbox), so spending is unlimited and not deducted" : "√" + Money(f);
        }

        public static string Money(double v) { return Math.Round(v).ToString("#,0", AInv); }

        // ------------------------------------------------------------------ lookups

        string Lookup(string cmd, Dictionary<string, object> args, double ut)
        {
            try
            {
                var sb = new StringBuilder();
                switch (cmd)
                {
                    case "news":
                    {
                        string q = Arg(args, "query").ToLowerInvariant();
                        var o = FindOutlet(Arg(args, "outlet"));
                        int limit = Math.Max(1, Math.Min(25, (int)ArgNum(args, "limit", 15)));
                        var list = State.PublishedArticles(ut).Where(a => o == null || a.Outlet == o.Id);
                        if (q != "") list = list.Where(a => Words(q).Any(w => (a.Headline + " " + a.Subhead + " " + a.Body).ToLowerInvariant().Contains(w)));
                        foreach (var a in list.Take(limit))
                            sb.AppendLine(a.Id + " | " + Outlets.NameOf(a.Outlet) + " | " + Clock.FormatDate(a.PublishedUT) + " | " + a.Kind + " | " + a.Headline + " | " + a.Comments.Count + " comments");
                        return sb.Length == 0 ? "No matching articles." : sb.ToString();
                    }
                    case "read":
                    {
                        var a = FindArticle(Arg(args, "article"), ut);
                        if (a == null) return "No such article.";
                        sb.AppendLine(a.Id + " | " + Outlets.NameOf(a.Outlet) + " | " + Clock.FormatDate(a.PublishedUT) + " | " + a.Kind);
                        sb.AppendLine("Headline: " + a.Headline);
                        if (a.Subhead != "") sb.AppendLine("Subhead: " + a.Subhead);
                        sb.AppendLine(ResponseParser.Clip(Web.Sites.Unlinked(State, a.Body), 1500));
                        foreach (var c in a.Comments.OrderByDescending(c => c.Likes).Take(5)) sb.AppendLine("Comment by " + c.Author + ": " + ResponseParser.Clip(Web.Sites.Unlinked(State, c.Text), 200));
                        return sb.ToString();
                    }
                    case "forum":
                    {
                        string q = Arg(args, "query").ToLowerInvariant();
                        var list = State.Forum.Where(t => t.UT <= ut).OrderByDescending(t => t.UT).AsEnumerable();
                        if (q != "") list = list.Where(t => Words(q).Any(w => (t.Title + " " + t.Body).ToLowerInvariant().Contains(w)));
                        foreach (var t in list.Take(12)) sb.AppendLine(t.Id + " | b/" + t.Board + " | " + t.Author + " | " + t.Title + " | " + t.Comments.Count + " replies");
                        return sb.Length == 0 ? "No matching threads." : sb.ToString();
                    }
                    case "finances":
                    {
                        sb.AppendLine("Funds: " + FundsText());
                        foreach (var d in State.Deals.Where(d => d.Amount != 0).OrderByDescending(d => d.UT).Take(12))
                            sb.AppendLine(Clock.FormatDate(d.UT) + ": " + (d.Amount > 0 ? "+" : "-") + "√" + Money(Math.Abs(d.Amount)) + " " + d.Summary);
                        return sb.ToString();
                    }
                    case "ledger":
                    {
                        foreach (var d in State.Deals.OrderByDescending(d => d.UT).Take(25))
                            sb.AppendLine(d.Id + " | " + Clock.FormatDate(d.UT) + " | " + d.Kind + " | " + d.Summary + " | " + d.Status + (d.Outcome != "" ? ": " + d.Outcome : "") + (d.Secret ? (d.Exposed ? " | EXPOSED" : " | secret") : ""));
                        return sb.Length == 0 ? "The ledger is empty." : sb.ToString();
                    }
                    case "vessels":
                    {
                        foreach (var t in State.Tracked.Values.OrderByDescending(t => t.LaunchUT).Take(15))
                            sb.AppendLine(t.VesselId + " | " + (t.Craft != null ? t.Craft.VesselName : "?") + " | launched " + Clock.FormatDate(t.LaunchUT) + (State.IsSilent(t.VesselId) ? " | silent" : "") + (t.History.Count > 0 ? " | " + t.History.Last() : ""));
                        return sb.Length == 0 ? "No tracked vessels yet." : sb.ToString();
                    }
                    case "gallery":
                    {
                        foreach (var g in State.Gallery.OrderByDescending(g => g.UT).Take(15))
                            sb.AppendLine(g.Id + " | " + (g.Public ? "public" : "private") + " | " + (g.Caption != "" ? g.Caption : g.VesselName));
                        return sb.Length == 0 ? "No photos." : sb.ToString();
                    }
                    case "mood":
                    {
                        sb.AppendLine(State.Mood.Describe());
                        var p = State.Polls.LastOrDefault();
                        if (p != null) sb.AppendLine("Latest poll: " + p.Summary(AgencyName));
                        return sb.ToString();
                    }
                    case "outlets":
                        foreach (var o in Outlets.All) sb.AppendLine(o.Id + " | " + o.Name + " | " + o.Style + " " + (o.Bias ?? ""));
                        return sb.ToString();
                    case "reporters":
                    {
                        foreach (var o in Outlets.All)
                        {
                            List<string> reps;
                            var names = State.Reporters.TryGetValue(o.Id, out reps) ? reps : new List<string>();
                            var ties = State.Ties.Where(t => t.Outlet == o.Id).ToList();
                            var all = names.Concat(ties.Select(t => t.Name)).Distinct().ToList();
                            if (all.Count == 0) continue;
                            sb.AppendLine(o.Name + ": " + string.Join("; ", all.Select(n => { var t = ties.FirstOrDefault(x => x.Name == n); return n + (t == null ? " (no dealings yet)" : " (" + t.Feeling + ", " + t.Score + (t.Notes.Count > 0 ? ": " + string.Join(", ", t.Notes.ToArray()) : "") + ")"); }).ToArray()));
                        }
                        return sb.Length == 0 ? "No named reporters yet." : sb.ToString();
                    }
                    case "crew":
                    {
                        foreach (var c in State.Crew.Values.Where(x => x.OnRoster || x.Flights > 0).OrderByDescending(x => x.Fame).Take(30))
                        {
                            sb.Append(c.Name + ": " + (c.Trait != "" ? c.Trait + " L" + c.Level : "crew") + ", " + (c.Dead ? "deceased" : c.RosterStatus == "assigned" ? "aboard " + c.VesselName + (c.Where != "" ? " (" + c.Where + ")" : "") : "at KSC") + ", " + c.Flights + " flights, " + c.Tier);
                            var up = UpcomingFor(c.Name);
                            if (up.Count > 0) sb.Append("; booked: " + string.Join(", ", up.Select(x => x.Kind + " " + x.Id + " on " + Clock.FormatDate(x.AtUT) + (x.Coaching.Count > 0 ? " (coached)" : "")).ToArray()));
                            sb.AppendLine();
                        }
                        return sb.Length == 0 ? "No crew on record yet." : sb.ToString();
                    }
                    case "orders":
                        for (int i = 0; i < State.Orders.Count; i++) sb.AppendLine((i + 1) + ". " + State.Orders[i].Text + " (given " + Clock.FormatDate(State.Orders[i].UT) + ")");
                        return sb.Length == 0 ? "No standing orders." : sb.ToString();
                    case "lore":
                    {
                        var pr = State.Profile;
                        sb.AppendLine("Founding: " + pr.Founding + "\nMission: " + pr.Mission + "\nPolitics: " + pr.Politics + "\nPeople: " + pr.KeyPeople + "\nWorld: " + pr.WorldLore);
                        foreach (var l in State.Lore.Skip(Math.Max(0, State.Lore.Count - 10))) sb.AppendLine(Clock.FormatDate(l.UT) + ": " + l.Text);
                        return sb.ToString();
                    }
                }
                return "Unknown lookup \"" + cmd + "\".";
            }
            catch (Exception e) { return "Lookup failed: " + e.Message; }
        }

        static IEnumerable<string> Words(string q) { return q.Split(new[] { ' ', ',', '.', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 2); }

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
