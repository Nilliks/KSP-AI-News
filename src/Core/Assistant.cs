using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KerbalNews
{

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
            if (InSide())
            {
                AssistantSay("assistant", "You're flying a dropped stage with FMRS right now, and anything I did here would be undone when you jump back. Ask me again once you're back on the main flight.", ut);
                return true;
            }
            if (!Settings.CanCallAi)
            {
                AssistantOffline(text, ut);
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
            AskJson(Settings.Writer, AssistantSystem(), AssistantUser(step, notes, ut), 4000, 0, (root, err, text, res) =>
            {
                if (run != assistantRun) return; // a reload or a newer request replaced this one
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
            // Money from Kmail always waits for the director, whatever the setting says.
            if (!list.Any(a => Arg(a, "cmd") == "approve_mail") && (!Settings.AssistantConfirm || !list.Any(a => Spends(Arg(a, "cmd")))))
            {
                foreach (var a in list) AssistantSay("result", Execute(Arg(a, "cmd"), Json.Obj(Json.Get(a, "args")) ?? new Dictionary<string, object>(), ut), ut);
                return;
            }
            // Bookings cost nothing and announce nothing, so they never wait behind a deal.
            foreach (var a in list.Where(a => RunsNow(Arg(a, "cmd"))).ToList()) AssistantSay("result", Execute(Arg(a, "cmd"), Json.Obj(Json.Get(a, "args")) ?? new Dictionary<string, object>(), ut), ut);
            var waiting = list.Where(a => !RunsNow(Arg(a, "cmd"))).ToList();
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

        static bool RunsNow(string cmd) { return cmd == "schedule_appearance" || cmd == "cancel_appearance"; }

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
- wiki {page?}: Kerbopedia, the public encyclopedia of the agency's craft, crew and places: the page list, or one page's current text.
- mail {filter?: unread|open, query?, email?}: Kmail, the director's inbox: a list of conversations with ids (e12), or with email one conversation in full.
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
- edit_wiki {page, lead?, sections?: [{heading, text}], rewrite?, remember?, note?}: change a Kerbopedia page (title or slug; a new title makes a new page). lead replaces the opening; each section replaces the one with that heading, adds it if new, or removes it when text is """"; rewrite asks Kerbopedia's writers to redo the page following an instruction; remember is a lasting wish the page keeps in later updates; note is the edit summary. Look the page up first when changing existing text. Free, but the page history shows the edit came from the agency.
- reply_mail {email, text?, decision?: accept|decline|counter|none, kerbal?, when?, statement?}: answer an email in Kmail for the director (signed as you, for the Director). Write the text yourself unless given. What you agree to really happens: an accepted interview is booked, a statement is printed, a visit takes place. decision says what the reply does with the sender's request (counter = yes, but with another kerbal or date). Read the email first if you haven't.
- send_mail {to, subject, body}: write a new email to anyone: an outlet (id or name), a company, the Council, a kerbal's family, a school. They answer, and may make an offer or write a story.
- approve_mail {email}: take the money in an email offer the director has accepted (a sponsorship). Money moves only with this, and it always waits for the director's OK.
- mail_auto {on: true|false}: whether you answer routine mail (fan letters, citizens, simple press questions) on your own. Only when the director asks.
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
            string inc = IncidentText();
            if (inc != "") sb.AppendLine("ENGINEERING INVESTIGATIONS (internal findings are confidential unless the director decides otherwise; released ones are public):\n" + inc);
            string inbox = Settings.SiteOn("kmail") ? InboxText(ut) : "";
            if (inbox != "") sb.AppendLine(inbox);
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
    }
}
