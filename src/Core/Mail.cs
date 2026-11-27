using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>One email in Kmail. A conversation is a root mail plus the mails whose Thread is the root's id.</summary>
    public class MailMsg
    {
        public string Id = "", Thread = "";
        public string From = "", FromAddr = "", To = "", ToAddr = "";
        /// <summary>The outlet id for press mail, else "".</summary>
        public string Org = "";
        /// <summary>press, sponsor, council, fan, family, public; "me" for the director's own mail.</summary>
        public string Kind = "press";
        public string Subject = "", Body = "";
        public double UT;
        public bool Read, Outgoing, ByAssistant, Notified;
        // What the sender wants (on the root mail only).
        /// <summary>interview, statement, sponsor, hearing, visit, update, or "" for nothing.</summary>
        public string Ask = "";
        public string Kerbal = "", Topic = "", StoryId = "";
        public double Amount;
        public double DueUT = -1;
        /// <summary>"" (no ask), open, awaiting (money waits for the director's OK), accepted, declined, expired, done.</summary>
        public string Status = "";
        public string Outcome = "";
        /// <summary>A reply the director has sent and the sender still has to answer.</summary>
        public bool Waiting;

        public bool IsRoot { get { return Thread == "" || Thread == Id; } }
        public bool Open { get { return Status == "open" || Status == "awaiting"; } }
        public bool Important { get { return Ask != "" && Ask != "update" || Kind == "council"; } }

        public void Save(DataNode n)
        {
            n.Add("id", Id); n.Add("thread", Thread); n.AddText("from", From); n.AddText("fromAddr", FromAddr); n.AddText("to", To); n.AddText("toAddr", ToAddr);
            n.Add("org", Org); n.Add("kind", Kind); n.AddText("subject", Subject); n.AddText("body", Body); n.Add("ut", UT);
            n.Add("read", Read); n.Add("out", Outgoing); n.Add("assistant", ByAssistant); n.Add("notified", Notified);
            n.Add("ask", Ask); n.AddText("kerbal", Kerbal); n.AddText("topic", Topic); n.Add("story", StoryId); n.Add("amount", Amount); n.Add("due", DueUT);
            n.Add("status", Status); n.AddText("outcome", Outcome); n.Add("waiting", Waiting);
        }

        public static MailMsg Load(DataNode n)
        {
            return new MailMsg
            {
                Id = n.Get("id", ""), Thread = n.Get("thread", ""), From = n.GetText("from"), FromAddr = n.GetText("fromAddr"), To = n.GetText("to"), ToAddr = n.GetText("toAddr"),
                Org = n.Get("org", ""), Kind = n.Get("kind", "press"), Subject = n.GetText("subject"), Body = n.GetText("body"), UT = n.GetDouble("ut"),
                Read = n.GetBool("read"), Outgoing = n.GetBool("out"), ByAssistant = n.GetBool("assistant"), Notified = n.GetBool("notified"),
                Ask = n.Get("ask", ""), Kerbal = n.GetText("kerbal"), Topic = n.GetText("topic"), StoryId = n.Get("story", ""), Amount = n.GetDouble("amount"), DueUT = n.GetDouble("due", -1),
                Status = n.Get("status", ""), Outcome = n.GetText("outcome"), Waiting = n.GetBool("waiting"),
            };
        }
    }

    public partial class NewsEngine
    {
        public const int MaxMail = 400;
        public bool MailBusy;
        /// <summary>Raised when a mail becomes visible in the inbox (for the in-game pop-up).</summary>
        public event Action<MailMsg> MailArrived;
        double nextMailTickUT = -1;
        readonly Random mailRng = new Random();
        /// <summary>Stories waiting to bring mail, with the time it may arrive (a while after the coverage is out).</summary>
        readonly List<KeyValuePair<Story, double>> mailQueue = new List<KeyValuePair<Story, double>>();

        public static readonly string[] Companies =
        {
            "Rockomax Conglomerate", "Kerbodyne", "Probodobodyne Inc", "C7 Aerospace Division", "Jebediah Kerman's Junkyard and Spaceship Parts Co.", "Periapsis Rocket Supplies Co",
            "Reaction Systems Ltd", "STEADLER Engineering Corps", "Zaltonic Electronics", "Kerlington Model Rockets and Paper Products Inc", "FLOOYD Dynamics Research Labs",
            "Goliath National Products", "Integrated Integrals", "Moving Parts Experts Group", "Sean's Cannery", "WinterOwl Aircraft Emporium", "Vac-Co Advanced Suction Systems",
        };
        static readonly string[] Councillors = { "Dunlie", "Hadsted", "Merrin", "Tobald", "Wenfrey", "Gilly" };
        static readonly string[] Schools = { "Kerbal Elementary, Year 3", "KSC Shores High School Rocketry Club", "Minmus Hill Primary", "The Junior Astronomers of Kerbin", "Mun Valley Scouts, Troop 12" };
        static readonly string[] Citizens = { "a.very.angry.taxpayer", "worried_neighbour_ksc", "Ferny Kerman", "skeptic_sam", "Orbitfan1982", "Mabel Kerman" };
        static readonly string[] Relatives = { "Gwen", "Lodrin", "Hanbree", "Melzy", "Tedbert", "Agathy", "Dorric" };

        public string DirectorAddr { get { return "director@" + DomainWord(AgencyName) + ".kb"; } }

        static string DomainWord(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in (s ?? "").ToLowerInvariant()) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.Length > 0 ? sb.ToString() : "agency";
        }

        static string Addr(string name, string domain)
        {
            var parts = (name ?? "").ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string user = parts.Length >= 2 ? parts[0][0] + "." + DomainWord(parts[parts.Length - 1]) : DomainWord(name);
            return user + "@" + domain;
        }

        public IEnumerable<MailMsg> VisibleMail(double ut) { return State.Mail.Where(m => m.UT <= ut); }
        public int UnreadMail(double ut) { return State.Mail.Count(m => m.UT <= ut && !m.Read && !m.Outgoing); }
        public MailMsg MailRoot(MailMsg m) { return m == null || m.IsRoot ? m : State.Mail.FirstOrDefault(x => x.Id == m.Thread) ?? m; }
        public List<MailMsg> MailThread(MailMsg root) { return State.Mail.Where(x => x.Id == root.Id || x.Thread == root.Id).OrderBy(x => x.UT).ToList(); }

        public MailMsg FindMail(string key, double ut)
        {
            key = (key ?? "").Trim();
            if (key == "") return null;
            var m = State.Mail.FirstOrDefault(x => x.Id == key && x.UT <= ut);
            if (m != null) return MailRoot(m);
            return VisibleMail(ut).Where(x => x.IsRoot && (x.Subject.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0 || x.From.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderByDescending(x => x.Open).ThenByDescending(x => x.UT).FirstOrDefault();
        }

        MailMsg AddMail(MailMsg m)
        {
            if (m.Id == "") m.Id = State.NewId("e");
            if (m.Thread == "") m.Thread = m.Id;
            if (m.To == "" && !m.Outgoing) { m.To = "Director"; m.ToAddr = DirectorAddr; }
            State.Mail.Add(m);
            if (State.Mail.Count > MaxMail)
            {
                var drop = State.Mail.Where(x => !x.Open).OrderBy(x => x.UT).Take(State.Mail.Count - MaxMail).ToList();
                foreach (var d in drop) State.Mail.Remove(d);
            }
            return m;
        }

        // ------------------------------------------------------------------ who writes in

        /// <summary>Called for each published story: big ones bring 1-3 emails.</summary>
        bool MailFollows(Story story)
        {
            if (Settings.MailLevel <= 0 || !Settings.SiteOn("kmail")) return false;
            if (story.Kind != "mission" && story.Kind != "milestone") return false;
            double need = Settings.MailLevel >= 3 ? 25 : Settings.MailLevel == 2 ? 35 : 50;
            return story.Significance >= need;
        }

        void MailOnStory(Story story, double ut, GeneratedCoverage cov = null)
        {
            var planned = story.PlannedMail;
            story.PlannedMail = null;
            if (!MailFollows(story)) return;
            if (planned != null && planned.Count > 0 && cov != null && cov.Emails != null)
            {
                // Written in the news call: no mail call of its own. Anything the writer left out gets the template.
                for (int i = 0; i < planned.Count; i++)
                {
                    var m = planned[i];
                    var o = cov.Emails.FirstOrDefault(x => (int)Json.Num(x, "n", -1) == i + 1) ?? (i < cov.Emails.Count ? cov.Emails[i] : null);
                    string s = o != null ? (Json.Str(o, "subject", "") ?? "").Trim() : "", b = o != null ? (Json.Str(o, "body", "") ?? "").Trim() : "";
                    if (s == "" || b == "") OfflineMail(m);
                    else { m.Subject = ResponseParser.Clip(s, 140); m.Body = ResponseParser.Clip(b, 2500); }
                    Deliver(m, ut + 0.25 * Day);
                }
                return;
            }
            mailQueue.Add(new KeyValuePair<Story, double>(story, ut + 0.25 * Day));
        }

        /// <summary>
        /// The emails a story brings, as an extra part of its news request, so the mail costs no call of its own.
        /// Empty when the story brings none or the AI doesn't write mail at this setting.
        /// </summary>
        string MailForCall(Story story, double ut)
        {
            story.PlannedMail = null;
            if (story.MoodApplied || !MailFollows(story) || !Settings.AiWrites(GlobalSettings.AiMost)) return "";
            var batch = MailPlan(story, ut);
            if (batch.Count == 0) return "";
            story.PlannedMail = batch;
            var u = new StringBuilder();
            u.AppendLine();
            u.AppendLine("KMAIL");
            u.AppendLine("Also add a top-level field \"emails\": [{\"n\":1,\"subject\":\"...\",\"body\":\"...\"}], one per email below, in order. They are emails to the director of " + AgencyName + ", sent a few hours after this coverage. Write each the way that sender would: press brisk and professional, companies businesslike, Council offices formal, schools sweet and a bit chaotic, families personal and emotional, angry citizens rude but not hateful. 60-160 words each, signed with the sender's name. Keep every request, name, amount and date exactly as given.");
            for (int i = 0; i < batch.Count; i++) u.AppendLine((i + 1) + ". From " + SenderLine(batch[i]) + ": " + AskText(batch[i]) + ".");
            return u.ToString();
        }

        /// <summary>The requests a story brings, decided by the game (the AI only writes the words).</summary>
        List<MailMsg> MailPlan(Story story, double ut)
        {
            var list = new List<MailMsg>();
            double sig = story.Significance;
            int n = sig >= 70 ? 3 : sig >= 45 ? 2 : 1;
            if (Settings.MailLevel >= 3) n++;
            if (Settings.MailLevel == 1) n = Math.Max(1, n - 1);
            bool bad = story.HasEvent(EventTypes.Failure) || story.HasEvent(EventTypes.CrewKilled);
            var crew = story.Events.SelectMany(e => e.Crew).Distinct().Where(c => { CrewFame k; return !State.Crew.TryGetValue(c, out k) || !k.Dead; }).ToList();
            string title = story.Title;
            var outlets = Outlets.Enabled.Where(o => o.Id != "council").OrderBy(o => mailRng.Next()).ToList();
            var tried = new List<Func<MailMsg>>();
            // The press always comes first.
            if (outlets.Count > 0)
            {
                var o = outlets[0];
                if (!bad && crew.Count > 0) tried.Add(() => Press(o, "interview", crew[mailRng.Next(crew.Count)], title, story, ut, 1.5));
                else tried.Add(() => Press(o, "statement", "", title, story, ut, 1));
            }
            if (bad && story.HasEvent(EventTypes.CrewKilled))
            {
                var lost = story.Events.Where(e => e.Type == EventTypes.CrewKilled).SelectMany(e => e.Crew).FirstOrDefault();
                if (lost != null) tried.Add(() => Family(lost, "lost", story, ut));
            }
            if (!bad) tried.Add(() => Sponsor(story, ut));
            if (!bad && crew.Count > 0) tried.Add(() => Visit(crew[mailRng.Next(crew.Count)], story, ut));
            if (bad || sig >= 60) tried.Add(() => Council(bad ? "hearing" : "", story, ut));
            if (outlets.Count > 1) tried.Add(() => Press(outlets[1], bad ? "statement" : crew.Count > 0 ? "interview" : "statement", crew.Count > 0 ? crew[mailRng.Next(crew.Count)] : "", title, story, ut, 1));
            tried.Add(() => Public(story, ut));
            foreach (var f in tried.Take(n + (bad ? 1 : 0))) { var m = f(); if (m != null) list.Add(m); }
            return list;
        }

        string ReporterName(string outlet)
        {
            var known = State.Ties.Where(t => t.Outlet == outlet).OrderByDescending(t => t.Score).FirstOrDefault();
            List<string> reps;
            return known != null ? known.Name : State.Reporters.TryGetValue(outlet, out reps) && reps.Count > 0 ? reps[mailRng.Next(reps.Count)] : ReporterFirstNames[mailRng.Next(ReporterFirstNames.Length)] + " Kerman";
        }

        MailMsg Press(OutletDef o, string ask, string kerbal, string title, Story story, double ut, double dueDays)
        {
            string who = ReporterName(o.Id);
            return new MailMsg
            {
                Kind = "press", Org = o.Id, From = who, FromAddr = Addr(who, o.Domain), Ask = ask, Kerbal = kerbal, Topic = title, StoryId = story != null ? story.Id : "",
                DueUT = ut + dueDays * Day, Status = "open",
            };
        }

        MailMsg Sponsor(Story story, double ut)
        {
            string co = Companies[mailRng.Next(Companies.Length)];
            double amt = Math.Round((8000 + story.Significance * 600) * (0.7 + mailRng.NextDouble() * 0.6) / 1000) * 1000;
            return new MailMsg { Kind = "sponsor", From = co, FromAddr = "partnerships@" + DomainWord(co.Split(' ')[0]) + ".kb", Ask = "sponsor", Amount = amt, Topic = story.Title, StoryId = story.Id, DueUT = ut + 4 * Day, Status = "open" };
        }

        MailMsg Visit(string kerbal, Story story, double ut)
        {
            string school = Schools[mailRng.Next(Schools.Length)];
            return new MailMsg { Kind = "fan", From = school, FromAddr = "office@" + DomainWord(school.Split(',')[0]) + ".kb", Ask = "visit", Kerbal = kerbal, Topic = story != null ? story.Title : "", StoryId = story != null ? story.Id : "", DueUT = ut + 5 * Day, Status = "open" };
        }

        MailMsg Council(string ask, Story story, double ut)
        {
            string c = "Councillor " + Councillors[mailRng.Next(Councillors.Length)] + " Kerman";
            return new MailMsg { Kind = "council", From = "Office of " + c, FromAddr = Addr(c.Substring(11), "council.gov.kb"), Ask = ask, Topic = story != null ? story.Title : "the agency's budget", StoryId = story != null ? story.Id : "", DueUT = ask != "" ? ut + 3 * Day : -1, Status = ask != "" ? "open" : "" };
        }

        MailMsg Family(string kerbal, string why, Story story, double ut)
        {
            string rel = Relatives[mailRng.Next(Relatives.Length)] + " Kerman";
            return new MailMsg { Kind = "family", From = rel, FromAddr = Addr(rel, "kmail.kb"), Ask = "update", Kerbal = kerbal, Topic = why, StoryId = story != null ? story.Id : "", DueUT = ut + 2 * Day, Status = "open" };
        }

        MailMsg Public(Story story, double ut)
        {
            string who = Citizens[mailRng.Next(Citizens.Length)];
            return new MailMsg { Kind = "public", From = who, FromAddr = DomainWord(who) + "@kmail.kb", Topic = story != null ? story.Title : "the space program", StoryId = story != null ? story.Id : "" };
        }

        /// <summary>A quiet-day letter: fans, families of kerbals far away, citizens, or a company.</summary>
        MailMsg QuietMail(double ut)
        {
            var away = State.Crew.Values.Where(c => !c.Dead && c.Where != "" && c.Where.IndexOf("Kerbin", StringComparison.OrdinalIgnoreCase) < 0 && c.LastFlightUT >= 0 && ut - c.LastFlightUT > 10 * Day).ToList();
            double r = mailRng.NextDouble();
            if (away.Count > 0 && r < 0.25) return Family(away[mailRng.Next(away.Count)].Name, "away", null, ut);
            var flown = State.Crew.Values.Where(c => !c.Dead && c.Flights > 0).ToList();
            if (r < 0.5 && flown.Count > 0) return Visit(flown[mailRng.Next(flown.Count)].Name, null, ut);
            if (r < 0.65) { var m = Council("", null, ut); m.Topic = "the agency's budget"; return m; }
            if (r < 0.8 && State.Articles.Count > 0) { var s = new Story { Id = "", Significance = 30 }; var sp = Sponsor(s, ut); sp.Topic = "the space program"; sp.StoryId = ""; return sp; }
            return Public(null, ut);
        }

        // ------------------------------------------------------------------ writing the words

        string AskText(MailMsg m)
        {
            switch (m.Ask)
            {
                case "interview": return "asks for an exclusive interview with " + m.Kerbal + " about " + m.Topic + "; reply by " + Clock.FormatDate(m.DueUT);
                case "statement": return "asks for an official statement on " + m.Topic + "; their story runs on " + Clock.FormatDate(m.DueUT) + " either way";
                case "sponsor": return "offers a sponsorship of √" + Money(m.Amount) + " (their logo on the next flight and in releases); offer stands until " + Clock.FormatDate(m.DueUT);
                case "hearing": return "invites the director to testify at a Planetary Council hearing about " + m.Topic + " on " + Clock.FormatDate(m.DueUT);
                case "visit": return "asks if " + m.Kerbal + " could visit and talk to the children; reply by " + Clock.FormatDate(m.DueUT);
                case "update": return m.Topic == "lost" ? "is family of " + m.Kerbal + ", who died on the mission, and wants answers from the agency" : "is family of " + m.Kerbal + ", away on a long mission, and wants to know how they are and when they come home";
                default: return m.Kind == "council" ? "writes about " + m.Topic + " and the agency's budget, no reply needed but appreciated" : m.Kind == "public" ? "is a member of the public with an opinion (praise or complaint) about " + m.Topic : "writes about " + m.Topic;
            }
        }

        string SenderLine(MailMsg m)
        {
            string org = m.Kind == "press" ? " at " + Outlets.NameOf(m.Org) + " (" + Outlets.Get(m.Org).Style + ")" : m.Kind == "sponsor" ? " (a parts company)" : m.Kind == "council" ? " (Planetary Council)" : m.Kind == "fan" ? " (school or club)" : m.Kind == "family" ? " (a kerbonaut's family)" : " (a member of the public)";
            return m.From + org;
        }

        void WriteMail(List<MailMsg> batch, double ut)
        {
            if (batch.Count == 0) return;
            if (!Settings.AiWrites(GlobalSettings.AiMost)) { foreach (var m in batch) { OfflineMail(m); Deliver(m, ut); } return; }
            MailBusy = true;
            var sb = new StringBuilder();
            sb.AppendLine("You write emails sent to the director of " + AgencyName + ", a space agency in the world of Kerbal Space Program (KSP1), through its in-game email, Kmail. Each email is from a different sender; write it the way that real person would: press emails are brisk and professional, companies are businesslike, Council offices formal, schools sweet and a bit chaotic, families personal and emotional, angry citizens rude but not hateful.");
            sb.AppendLine(PromptBuilder.SillinessText(State.Silliness));
            sb.AppendLine("RULES: Use only the facts given; never invent missions or results. Keep every request, name, amount and date exactly as given. 60-160 words each. Sign off with the sender's name. Kerbal names end in Kerman. Everyone is a kerbal: never say human.");
            string canon = PromptBuilder.ProfileText(State);
            if (canon != "") sb.AppendLine(canon);
            sb.AppendLine("Reply with JSON only: {\"emails\":[{\"n\":1,\"subject\":\"...\",\"body\":\"...\"}]} one per email, in order.");
            var u = new StringBuilder();
            u.AppendLine("Date: " + Clock.FormatDate(ut));
            var recent = State.PublishedArticles(ut).Take(5).ToList();
            if (recent.Count > 0) u.AppendLine("Latest headlines: " + string.Join(" | ", recent.Select(a => a.Headline).ToArray()));
            for (int i = 0; i < batch.Count; i++) u.AppendLine((i + 1) + ". From " + SenderLine(batch[i]) + ": " + AskText(batch[i]) + ".");
            AskJson(Settings.Cheap, sb.ToString(), u.ToString(), Math.Min(3000, 300 + batch.Count * 350), 0, (root, err, text, res) =>
            {
                MailBusy = false;
                var arr = root != null ? Json.Arr(Json.Get(root, "emails")) : null;
                for (int i = 0; i < batch.Count; i++)
                {
                    var o = arr == null ? null : arr.FirstOrDefault(x => (int)Json.Num(x, "n", -1) == i + 1) ?? (i < arr.Count ? arr[i] : null);
                    string s = o != null ? Json.Str(o, "subject", "").Trim() : "", b = o != null ? Json.Str(o, "body", "").Trim() : "";
                    if (s == "" || b == "") OfflineMail(batch[i]);
                    else { batch[i].Subject = ResponseParser.Clip(s, 140); batch[i].Body = ResponseParser.Clip(b, 2500); }
                    Deliver(batch[i], ut);
                }
                if (arr == null && err != null) LastError = err;
            });
        }

        void Deliver(MailMsg m, double ut)
        {
            m.UT = Math.Max(m.UT, ut + (0.02 + mailRng.NextDouble() * 0.25) * Day);
            if (m.DueUT >= 0 && m.DueUT < m.UT + 0.5 * Day) m.DueUT = m.UT + 0.5 * Day;
            AddMail(m);
        }

        void OfflineMail(MailMsg m)
        {
            string sign = "\n\n" + m.From;
            var rng = new Random(OfflineWriter.StableHash(m.From + m.Subject + m.UT.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + m.Ask + m.Kerbal));
            var v = new Dictionary<string, string>
            {
                { "topic", m.Topic }, { "kerbal", m.Kerbal }, { "kerbal_first", FirstName(m.Kerbal) }, { "outlet", Outlets.NameOf(m.Org) },
                { "due", m.DueUT >= 0 ? Clock.FormatDate(m.DueUT) : "" }, { "from", m.From }, { "amount", Money(m.Amount) },
            };
            Func<string, string> R = rule => Phrases.Rule(rule, v, rng);
            switch (m.Ask)
            {
                case "interview": m.Subject = "Interview request: " + m.Kerbal; m.Body = R("m_open_press") + "\n\n" + R("m_congrats") + " " + R("m_interview") + " " + R("m_reply_by") + sign; break;
                case "statement": m.Subject = "Request for comment: " + m.Topic; m.Body = R("m_open_press") + "\n\n" + R("m_statement") + " " + R("m_statement_due") + sign; break;
                case "sponsor": m.Subject = "Sponsorship proposal"; m.Body = R("m_open_press") + "\n\n" + R("m_sponsor") + " " + R("m_sponsor_due") + sign; break;
                case "hearing": m.Subject = "Invitation to testify: " + m.Topic; m.Body = "Director,\n\n" + R("m_hearing") + sign; break;
                case "visit": m.Subject = "Can " + FirstName(m.Kerbal) + " visit our class?"; m.Body = R("m_open_warm") + "\n\n" + R("m_visit") + " " + R("m_visit_due") + sign; break;
                case "update": m.Subject = m.Topic == "lost" ? "About " + m.Kerbal : "When is " + FirstName(m.Kerbal) + " coming home?"; m.Body = m.Topic == "lost" ? "Director,\n\nNobody has told us anything about what happened to " + m.Kerbal + ". We deserve answers." + sign : "Hello,\n\n" + FirstName(m.Kerbal) + " has been away a long time now. Is everything all right? When will they be back?" + sign; break;
                default: m.Subject = m.Kind == "council" ? "Budget review" : "About " + m.Topic; m.Body = m.Kind == "council" ? "Director,\n\nThe Council will review the agency's budget this season. We would welcome any notes on the program's progress." + sign : "To whoever reads these,\n\nI've been following " + m.Topic + ". Some of us are watching what you do with our money." + sign; break;
            }
        }

        // ------------------------------------------------------------------ ticking

        void TickMail(double ut, bool allowCalls)
        {
            // Mail that arrived: pop-up for the important ones.
            foreach (var m in State.Mail.Where(x => !x.Notified && x.UT <= ut).ToList())
            {
                m.Notified = true;
                if (!m.Outgoing && MailArrived != null) MailArrived(m);
            }
            if (ut < nextMailTickUT) return;
            nextMailTickUT = ut + 0.1 * Day;
            foreach (var m in State.Mail.Where(x => x.IsRoot && x.Status == "open" && x.DueUT >= 0 && x.DueUT < ut && !x.Waiting).ToList()) MailExpire(m, ut);
            if (Settings.MailLevel <= 0 || !Settings.SiteOn("kmail") || InSide() || MailBusy) return;
            if (Settings.CanCallAi && !allowCalls) return;
            // Mail is never urgent: it waits behind the news and any other AI work.
            if (Busy || PendingCount > 0 || WikiBusy || AssistantBusy) return;
            if (mailQueue.Count > 0)
            {
                if (mailQueue[0].Value > ut) return;
                // Every story whose mail is due goes into one call.
                var batch = new List<MailMsg>();
                while (mailQueue.Count > 0 && mailQueue[0].Value <= ut && batch.Count < 6)
                {
                    batch.AddRange(MailPlan(mailQueue[0].Key, ut));
                    mailQueue.RemoveAt(0);
                }
                WriteMail(batch, ut);
                return;
            }
            if (State.MailAuto && TickAutoReply(ut)) return;
            // Quiet days: a letter every few days.
            double every = Settings.MailLevel >= 3 ? 1.5 : Settings.MailLevel == 2 ? 3 : 6;
            if (State.LastStoryUT < 0 || !State.Mail.Any()) return;   // nobody writes to an agency that hasn't done anything yet
            if (State.LastQuietMailUT < 0) { State.LastQuietMailUT = ut; return; }
            if (ut - State.LastQuietMailUT < every * Day || ut - State.LastStoryUT < Day) return;
            State.LastQuietMailUT = ut;
            var q = QuietMail(ut);
            if (q == null) return;
            var quiet = new List<MailMsg> { q };
            // With the AI writing, the next two quiet-day letters are written in the same call and arrive on their own days later.
            if (Settings.AiWrites(GlobalSettings.AiMost))
                for (int k = 1; k <= 2; k++)
                {
                    var later = QuietMail(ut);
                    if (later == null || quiet.Any(x => x.From == later.From)) continue;
                    double shift = k * every * Day;
                    later.UT = ut + shift;
                    if (later.DueUT >= 0) later.DueUT += shift;
                    quiet.Add(later);
                    State.LastQuietMailUT = later.UT;
                }
            WriteMail(quiet, ut);
        }

        void MailExpire(MailMsg m, double ut)
        {
            m.Status = "expired";
            string agency = AgencyName;
            switch (m.Ask)
            {
                case "interview":
                    m.Outcome = "No reply, so " + Outlets.NameOf(m.Org) + " wrote that the agency did not respond.";
                    Tie(m.Org, m.From, -2, "got no answer to an interview request", ut);
                    Tip(m.Org, Outlets.NameOf(m.Org) + " asked " + agency + " for an interview with " + m.Kerbal + " about " + m.Topic + " and got no reply. Mention it, briefly and a little pointedly.", agency + " silent on " + m.Kerbal + " interview request", "", 18, ut + 0.8 * Day, "news");
                    break;
                case "statement":
                    m.Outcome = "No reply, so the story ran with \"the agency did not respond\".";
                    Tie(m.Org, m.From, -1, "got no comment on " + ResponseParser.Clip(m.Topic, 40), ut);
                    Tip(m.Org, Outlets.NameOf(m.Org) + " asked " + agency + " for comment on " + m.Topic + ". The agency did not respond. Write the story with that line in it.", agency + " did not respond on " + ResponseParser.Clip(m.Topic, 60), "", 22, ut + 0.1 * Day, "news");
                    break;
                case "sponsor":
                    m.Outcome = "The offer lapsed.";
                    AddMail(new MailMsg { Thread = m.Id, Kind = m.Kind, From = m.From, FromAddr = m.FromAddr, Subject = "Re: " + m.Subject, Body = "Director,\n\nAs we haven't heard back, we'll take our sponsorship elsewhere this season. Best of luck.\n\n" + m.From, UT = ut });
                    break;
                case "hearing":
                    m.Outcome = "The director didn't answer; the Council noticed.";
                    State.Mood.Trust = Math.Max(0, State.Mood.Trust - 3);
                    if (Outlets.Get("council") != null && Settings.SiteOn("council")) Tip("council", "The director of " + agency + " did not answer the Council's invitation to testify about " + m.Topic + ". A short, cool statement noting the absence.", "Agency director absent from hearing", "", 25, ut + 0.2 * Day, "statement");
                    break;
                case "visit":
                    m.Outcome = "No reply; the class was disappointed.";
                    State.Mood.Excitement = Math.Max(0, State.Mood.Excitement - 1);
                    break;
                case "update":
                    m.Outcome = "No reply; the family went to the press.";
                    State.Mood.Trust = Math.Max(0, State.Mood.Trust - 3);
                    string paper = Settings.SiteOn("inquirer") ? "inquirer" : "herald";
                    Tip(paper, "The family of " + m.Kerbal + " says " + agency + " has ignored their questions" + (m.Topic == "lost" ? " about how " + m.Kerbal + " died." : " while " + m.Kerbal + " is away on a long mission."), "Family of " + m.Kerbal + " says agency ignores them", "", 35, ut + 0.3 * Day, "news");
                    break;
            }
            RaiseCommunity();
        }

        // ------------------------------------------------------------------ answering

        /// <summary>Quick answers from the email page: accept, decline, other (with a kerbal), approve (money).</summary>
        public string MailQuick(string id, string what, string kerbal, double ut, bool byAssistant = false)
        {
            var m = FindMail(id, ut);
            if (m == null) return "That email is gone.";
            if (what == "approve") return MailApprove(m, ut);
            if (!m.Open) return "That request is already settled.";
            string text;
            switch (what)
            {
                case "accept": text = AcceptText(m, ""); break;
                case "other":
                    if (FindKerbal(kerbal) == null) return "Type the name of a kerbal on the roster first.";
                    text = AcceptText(m, FindKerbal(kerbal).Name); break;
                default: text = DeclineText(m); what = "decline"; break;
            }
            return MailReply(m.Id, text, what, what == "other" ? kerbal : "", "", "", ut, byAssistant);
        }

        string AcceptText(MailMsg m, string other)
        {
            string k = other != "" ? other : m.Kerbal;
            switch (m.Ask)
            {
                case "interview": return other != "" ? "Thanks for asking. " + FirstName(m.Kerbal) + " isn't available, but " + other + " would be glad to talk to you. We'll be in touch with a time." : "Happy to. " + FirstName(k) + " will be glad to talk to you. We'll be in touch with a time.";
                case "statement": return "Here is our statement: the agency is proud of its team, is reviewing what happened, and will share what we learn.";
                case "sponsor": return "Thank you. We'd be glad to accept your sponsorship on those terms.";
                case "hearing": return "I will attend the hearing.";
                case "visit": return "What a lovely letter. " + k + " would love to visit your class.";
                case "update": return "Thank you for writing. I'm so sorry for the wait. " + (m.Topic == "lost" ? "We will share everything we learn about what happened to " + m.Kerbal + ", and someone from the agency will call you today." : FirstName(m.Kerbal) + " is well, and we'll keep you posted on the return date.");
                default: return "Thank you for writing to us.";
            }
        }

        string DeclineText(MailMsg m)
        {
            switch (m.Ask)
            {
                case "interview": return "Thank you for the request, but we can't make " + FirstName(m.Kerbal) + " available right now.";
                case "statement": return "The agency has no comment at this time.";
                case "sponsor": return "Thank you for the offer, but we'll have to decline for now.";
                case "hearing": return "I regret I'm unable to attend the hearing.";
                case "visit": return "Thank you so much for asking. Sadly " + FirstName(m.Kerbal) + "'s schedule is full right now.";
                default: return "Thank you for writing. We can't help with this right now.";
            }
        }

        /// <summary>
        /// The director (or the assistant) replies. decision: accept, decline, counter, or "" to let the AI read the reply.
        /// Whatever is agreed is set up; money waits for the director's OK.
        /// </summary>
        public string MailReply(string id, string text, string decision, string kerbal, string when, string statement, double ut, bool byAssistant = false)
        {
            var m = FindMail(id, ut);
            if (m == null) return "I couldn't find that email.";
            text = (text ?? "").Trim();
            if (text == "") return "The reply was empty.";
            var me = AddMail(new MailMsg { Thread = m.Id, Kind = "me", Outgoing = true, From = byAssistant ? AssistantName + " for the Director" : "Director", FromAddr = DirectorAddr, To = m.From, ToAddr = m.FromAddr, Subject = "Re: " + m.Subject, Body = ResponseParser.Clip(text, 3000), UT = ut, Read = true, ByAssistant = byAssistant, Notified = true });
            foreach (var x in MailThread(m)) x.Read = true;
            decision = (decision ?? "").ToLowerInvariant();
            if (decision == "") decision = m.Open ? GuessDecision(text) : "none";
            if (!Settings.CanCallAi || decision != "none" && decision != "")
            {
                string res = m.Open && (decision == "accept" || decision == "decline" || decision == "counter") ? MailDecide(m, decision, kerbal, when, statement != "" ? statement : text, ut) : "";
                SenderAnswer(m, decision, ut, null);
                return "Sent." + (res != "" ? " " + res : "");
            }
            // Let the AI read what the director said and write the sender's answer.
            m.Waiting = true;
            MailBusy = true;
            var sb = new StringBuilder();
            sb.AppendLine("The director of " + AgencyName + " (a space agency in Kerbal Space Program) has replied to an email. Decide what the director agreed to, and write the sender's short answer (40-120 words, in the sender's voice, signed with their name). Kerbal names end in Kerman. Everyone is a kerbal.");
            sb.AppendLine("Reply with JSON only: {\"decision\":\"accept|decline|counter|none\",\"kerbal\":\"who, if the director named someone else\",\"when\":\"when, if the director gave a time, e.g. in 2 days\",\"amount\":0,\"statement\":\"the quote to print, for a statement\",\"story\":\"if the sender will now write something public, one sentence on what; else empty\",\"answer\":\"the sender's reply email body\"}");
            sb.AppendLine("counter means the director asked for different terms (a different amount or date); put the amount they asked for in amount.");
            var u = new StringBuilder();
            u.AppendLine("Date: " + Clock.FormatDate(ut));
            u.AppendLine("The sender: " + SenderLine(m) + ". " + (m.Ask != "" && m.Open ? "Their request: " + AskText(m) + "." : "No open request."));
            foreach (var x in MailThread(m)) u.AppendLine((x.Outgoing ? "DIRECTOR: " : m.From.ToUpperInvariant() + ": ") + ResponseParser.Clip(x.Body, 900));
            AskJson(Settings.Cheap, sb.ToString(), u.ToString(), 900, 0, (root, err, raw, res) =>
            {
                MailBusy = false;
                m.Waiting = false;
                string d = root != null ? Arg(root, "decision").ToLowerInvariant() : GuessDecision(text);
                string res2 = "";
                if (m.Open && (d == "accept" || d == "decline" || d == "counter"))
                {
                    double amt = root != null ? Json.Num(root, "amount", 0) : 0;
                    if (d == "counter" && m.Ask == "sponsor" && amt > 0)
                    {
                        // A company meets a fair counter part way, and walks from a greedy one.
                        double ratio = amt / Math.Max(1, m.Amount);
                        if (ratio <= 1.25) { m.Amount = Math.Round(amt / 1000) * 1000; d = "accept"; }
                        else if (ratio <= 1.6) { m.Amount = Math.Round((m.Amount + amt) / 2000) * 1000; m.DueUT = ut + 2 * Day; }
                        else d = "decline";
                    }
                    if (d != "counter") res2 = MailDecide(m, d, root != null ? Arg(root, "kerbal") : kerbal, root != null ? Arg(root, "when") : when, root != null && Arg(root, "statement") != "" ? Arg(root, "statement") : text, ut);
                }
                string story = root != null ? Arg(root, "story") : "";
                if (story != "" && m.Kind == "press" && (m.Ask == "" || !m.Open) && d == "none") Tip(m.Org, ResponseParser.Clip(story, 600) + " (from an email exchange with the director of " + AgencyName + ")", "", "", 20, ut + 0.3 * Day, "news");
                SenderAnswer(m, d, ut, root != null ? Arg(root, "answer") : null);
                if (res2 != "" && byAssistant) AssistantSay("result", res2, ut);
                RaiseCommunity();
            });
            return "Sent. " + m.From + " will answer soon.";
        }

        static string GuessDecision(string text)
        {
            string t = " " + (text ?? "").ToLowerInvariant() + " ";
            foreach (var no in new[] { " no ", "decline", "can't", "cannot", "not able", "unable", "no comment", "sorry", "not at this time", "pass " }) if (t.Contains(no)) return "decline";
            foreach (var yes in new[] { "yes", "sure", "happy to", "glad to", "accept", "agree", "sounds good", "will attend", "of course", "deal", "we'd love", "would love" }) if (t.Contains(yes)) return "accept";
            return "none";
        }

        /// <summary>Carries out what the director agreed to.</summary>
        string MailDecide(MailMsg m, string decision, string kerbal, string when, string statement, double ut)
        {
            if (decision == "decline")
            {
                m.Status = "declined";
                switch (m.Ask)
                {
                    case "interview": m.Outcome = "Declined."; Tie(m.Org, m.From, -1, "was turned down for an interview", ut); break;
                    case "statement":
                        m.Outcome = "No comment.";
                        Tie(m.Org, m.From, -1, "got \"no comment\"", ut);
                        Tip(m.Org, AgencyName + " declined to comment to " + Outlets.NameOf(m.Org) + " on " + m.Topic + ". Write the story with that line.", AgencyName + " declines to comment on " + ResponseParser.Clip(m.Topic, 60), "", 22, ut + 0.2 * Day, "news");
                        break;
                    case "hearing": m.Outcome = "Declined; the Council is not pleased."; State.Mood.Trust = Math.Max(0, State.Mood.Trust - 2); break;
                    case "update": m.Outcome = "The family was turned away."; State.Mood.Trust = Math.Max(0, State.Mood.Trust - 2); break;
                    default: m.Outcome = "Declined."; break;
                }
                return m.Outcome;
            }
            string who = kerbal != "" && FindKerbal(kerbal) != null ? FindKerbal(kerbal).Name : m.Kerbal;
            switch (m.Ask)
            {
                case "interview":
                {
                    var c = FindKerbal(who);
                    if (c == null || c.Dead) { m.Status = "accepted"; m.Outcome = "Accepted, but " + who + " can't be booked."; return m.Outcome; }
                    string res = ScheduleAppearance(new Dictionary<string, object> { { "kerbal", c.Name }, { "kind", "interview" }, { "outlet", m.Org }, { "when", when != "" ? when : "tomorrow" }, { "topic", m.Topic } }, ut);
                    Tie(m.Org, m.From, 2, "got the interview with " + c.Name + " they asked for", ut);
                    m.Status = "accepted"; m.Kerbal = c.Name; m.Outcome = res;
                    return res;
                }
                case "statement":
                {
                    m.Status = "done";
                    string q = ResponseParser.Clip(statement, 800);
                    Tie(m.Org, m.From, 1, "got a statement on " + ResponseParser.Clip(m.Topic, 40), ut);
                    Tip(m.Org, Outlets.NameOf(m.Org) + " asked " + AgencyName + " for comment on " + m.Topic + ". The agency's statement, to quote: \"" + q + "\"", AgencyName + " responds on " + ResponseParser.Clip(m.Topic, 60), "", 25, ut + 0.2 * Day, "news");
                    m.Outcome = "Your statement will be in " + Outlets.NameOf(m.Org) + "'s story.";
                    Log("response", "Statement to " + Outlets.NameOf(m.Org) + " on " + ResponseParser.Clip(m.Topic, 60), 0, false, ut);
                    return m.Outcome;
                }
                case "sponsor":
                    m.Status = "awaiting";
                    m.Outcome = "Waiting for your OK before the √" + Money(m.Amount) + " deal is signed.";
                    return m.Outcome + " Approve it in Kmail.";
                case "hearing":
                    m.Status = "accepted";
                    State.Mood.Trust = Math.Min(100, State.Mood.Trust + 2);
                    if (Settings.SiteOn("council")) Tip("council", "The director of " + AgencyName + " testified before the Council's science committee about " + m.Topic + ". A measured statement on what the committee heard.", "Agency director testifies to Council", "", 28, Math.Max(ut, m.DueUT) + 0.1 * Day, "statement");
                    m.Outcome = "You'll testify on " + Clock.FormatDate(Math.Max(ut, m.DueUT)) + ".";
                    Log("hearing", "Agreed to testify before the Council about " + ResponseParser.Clip(m.Topic, 60), 0, false, ut);
                    return m.Outcome;
                case "visit":
                {
                    var c = FindKerbal(who);
                    m.Status = "accepted";
                    State.Mood.Excitement = Math.Min(100, State.Mood.Excitement + 2);
                    string name = c != null ? c.Name : who;
                    Tip("herald", name + " of " + AgencyName + " visited " + m.From + " and answered the students' questions. A warm local story.", name + " visits " + m.From, "", 16, ut + 1 * Day, "feature");
                    if (c != null) CrewLine(c, "note", "Visited " + m.From + " to talk to the students.", ut);
                    m.Outcome = name + " will visit " + m.From + "; the Herald may cover it.";
                    return m.Outcome;
                }
                case "update":
                    m.Status = "done";
                    State.Mood.Trust = Math.Min(100, State.Mood.Trust + 1);
                    m.Outcome = "You answered the family.";
                    return m.Outcome;
                default:
                    m.Status = m.Status == "" ? "" : "done";
                    return "";
            }
        }

        /// <summary>Money moves only here, after the director's OK.</summary>
        public string MailApprove(MailMsg m, double ut)
        {
            if (m == null || m.Status != "awaiting") return "Nothing is waiting for approval there.";
            m.Status = "accepted";
            if (m.Ask == "sponsor")
            {
                if (!double.IsNaN(FundsAvailable())) ChangeFunds(m.Amount, "Sponsorship from " + m.From);
                var d = Log("sponsorship", "Sponsorship from " + m.From + ": √" + Money(m.Amount), m.Amount, false, ut);
                d.Target = m.From;
                Tip("observer", m.From + " is sponsoring " + AgencyName + " for √" + Money(m.Amount) + "; its logo will fly on the next mission.", m.From + " signs on as agency sponsor", "", 20, ut + 0.5 * Day, "news");
                AddMail(new MailMsg { Thread = m.Id, Kind = m.Kind, From = m.From, FromAddr = m.FromAddr, Subject = "Re: " + m.Subject, Body = "Director,\n\nWonderful. The √" + Money(m.Amount) + " is on its way and our logo team will be in touch.\n\n" + m.From, UT = ut + 0.05 * Day });
                m.Outcome = "Signed: √" + Money(m.Amount) + " received from " + m.From + ".";
            }
            RaiseCommunity();
            return m.Outcome;
        }

        void SenderAnswer(MailMsg m, string decision, double ut, string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                if (m.Kind == "public" || decision == "none" && m.Kind != "press") return;   // not every letter gets a reply back
                body = decision == "decline" ? "Understood. Thanks for letting us know." : decision == "accept" || decision == "counter" ? "Thank you! We'll be in touch with the details." : "Thanks for getting back to us.";
                body = (m.Kind == "fan" ? "Hello again!\n\n" : "Director,\n\n") + body + "\n\n" + m.From;
            }
            AddMail(new MailMsg { Thread = m.Id, Kind = m.Kind, Org = m.Org, From = m.From, FromAddr = m.FromAddr, Subject = "Re: " + m.Subject, Body = ResponseParser.Clip(body, 2000), UT = ut + (0.03 + mailRng.NextDouble() * 0.15) * Day });
        }

        // ------------------------------------------------------------------ writing new mail

        /// <summary>The director (or the assistant) writes to anyone. The recipient answers, and may make an offer or write a story.</summary>
        public string MailSend(string to, string subject, string body, double ut, bool byAssistant = false)
        {
            to = (to ?? "").Trim(); body = (body ?? "").Trim();
            if (to == "" || body == "") return "An email needs someone to send it to and some text.";
            string kind = "public", org = "", name = to, addr = to.Contains("@") ? to : "";
            var o = FindOutlet(to.Contains("@") ? to.Substring(to.IndexOf('@') + 1).Replace(".kb", "") : to) ?? Outlets.All.FirstOrDefault(x => to.Contains("@") && to.EndsWith("@" + x.Domain));
            string co = Companies.FirstOrDefault(c => c.IndexOf(to, StringComparison.OrdinalIgnoreCase) >= 0 || to.IndexOf(c.Split(' ')[0], StringComparison.OrdinalIgnoreCase) >= 0);
            if (o != null) { kind = "press"; org = o.Id; name = to.Contains("@") ? ReporterName(o.Id) : o.Name + " newsroom"; if (addr == "") addr = "newsroom@" + o.Domain; }
            else if (co != null) { kind = "sponsor"; name = co; if (addr == "") addr = "partnerships@" + DomainWord(co.Split(' ')[0]) + ".kb"; }
            else if (to.IndexOf("council", StringComparison.OrdinalIgnoreCase) >= 0) { kind = "council"; name = "Planetary Council"; if (addr == "") addr = "office@council.gov.kb"; }
            else if (FindKerbal(to) != null) { kind = "family"; name = FindKerbal(to).Name; if (addr == "") addr = Addr(name, DomainWord(AgencyName) + ".kb"); }
            else if (addr == "") addr = DomainWord(to) + "@kmail.kb";
            var root = AddMail(new MailMsg { Kind = kind, Org = org, Outgoing = true, From = byAssistant ? AssistantName + " for the Director" : "Director", FromAddr = DirectorAddr, To = name, ToAddr = addr, Subject = ResponseParser.Clip(subject != "" ? subject : "(no subject)", 140), Body = ResponseParser.Clip(body, 3000), UT = ut, Read = true, Notified = true, ByAssistant = byAssistant });
            if (!Settings.CanCallAi)
            {
                AddMail(new MailMsg { Thread = root.Id, Kind = kind, Org = org, From = name, FromAddr = addr, Subject = "Re: " + root.Subject, Body = "Thank you for your email. We've received it and will be in touch.\n\n" + name, UT = ut + 0.2 * Day });
                return "Sent to " + name + ".";
            }
            MailBusy = true;
            var sb = new StringBuilder();
            sb.AppendLine("The director of " + AgencyName + " (a space agency in Kerbal Space Program) has emailed someone. Write the recipient's reply (40-150 words, in their voice, signed with their name), and say what, if anything, they will now do. Be realistic: newsrooms like tips and exclusives but don't print propaganda; companies want something for their money; the Council is formal and cautious. Kerbal names end in Kerman. Everyone is a kerbal.");
            sb.AppendLine("Reply with JSON only: {\"from\":\"the person who answers\",\"answer\":\"...\",\"offer\":{\"ask\":\"sponsor|interview|none\",\"amount\":0,\"kerbal\":\"\",\"days\":3},\"story\":\"if they will publish something because of this email, one sentence on what; else empty\"}");
            var u = new StringBuilder();
            u.AppendLine("Date: " + Clock.FormatDate(ut));
            u.AppendLine("To: " + name + (o != null ? " (" + o.Style + ")" : kind == "sponsor" ? " (a parts company)" : ""));
            u.AppendLine("Subject: " + root.Subject);
            u.AppendLine(root.Body);
            AskJson(Settings.Cheap, sb.ToString(), u.ToString(), 900, 0, (r, err, raw, res) =>
            {
                MailBusy = false;
                string who = r != null && Arg(r, "from") != "" ? ResponseParser.Clip(Arg(r, "from"), 80) : name;
                string ans = r != null ? Arg(r, "answer") : "";
                if (ans == "") ans = "Thank you for your email. We'll be in touch.\n\n" + who;
                var reply = AddMail(new MailMsg { Thread = root.Id, Kind = kind, Org = org, From = who, FromAddr = addr, Subject = "Re: " + root.Subject, Body = ResponseParser.Clip(ans, 2000), UT = ut + (0.05 + mailRng.NextDouble() * 0.3) * Day });
                var offer = r != null ? Json.Obj(Json.Get(r, "offer")) : null;
                string ask = offer != null ? Arg(offer, "ask") : "";
                if (ask == "sponsor" && Json.Num(offer, "amount", 0) > 0)
                {
                    // Their offer becomes the open request on the conversation.
                    root.Ask = "sponsor"; root.Amount = Math.Min(500000, Math.Round(Json.Num(offer, "amount", 0) / 1000) * 1000); root.Status = "open"; root.DueUT = reply.UT + Math.Max(1, Math.Min(10, Json.Num(offer, "days", 4))) * Day;
                    root.From = who; root.FromAddr = addr;
                }
                else if (ask == "interview" && kind == "press" && FindKerbal(Arg(offer, "kerbal")) != null)
                {
                    root.Ask = "interview"; root.Kerbal = FindKerbal(Arg(offer, "kerbal")).Name; root.Topic = root.Subject; root.Status = "open"; root.DueUT = reply.UT + 2 * Day;
                    root.From = who; root.FromAddr = addr;
                }
                string story = r != null ? Arg(r, "story") : "";
                if (story != "" && kind == "press") Tip(org, ResponseParser.Clip(story, 600) + " (prompted by an email from the director of " + AgencyName + ": " + ResponseParser.Clip(root.Body, 400) + ")", "", "", 22, reply.UT + 0.3 * Day, "news");
                RaiseCommunity();
            });
            return "Sent to " + name + ". Expect an answer soon.";
        }

        // ------------------------------------------------------------------ routine mail by the assistant

        static bool Routine(MailMsg m) { return m.IsRoot && !m.Outgoing && (m.Kind == "public" || m.Kind == "fan" && m.Ask == "" || m.Kind == "council" && m.Ask == "" || m.Kind == "press" && m.Ask == ""); }

        bool TickAutoReply(double ut)
        {
            var due = State.Mail.Where(m => m.UT <= ut - 0.1 * Day && Routine(m) && !State.Mail.Any(x => x.Thread == m.Id && x.Outgoing)).Take(4).ToList();
            if (due.Count == 0) return false;
            if (!Settings.AiWrites(GlobalSettings.AiMost))
            {
                foreach (var m in due) MailReply(m.Id, "Thank you for writing to " + AgencyName + ". We read every letter, and we appreciate you taking the time.", "none", "", "", "", ut, true);
                AssistantSay("result", "I answered " + due.Count + " routine email" + (due.Count == 1 ? "" : "s") + " for you: " + string.Join(", ", due.Select(m => m.From).ToArray()) + ".", ut);
                return true;
            }
            MailBusy = true;
            var sb = new StringBuilder();
            sb.AppendLine("You are " + AssistantName + ", assistant to the director of " + AgencyName + " (a space agency in Kerbal Space Program). The director asked you to answer routine email for them. Write a short, warm, professional reply to each (30-90 words), signed \"" + AssistantName + ", for the Director\". Promise nothing that costs money or commits the crew. Stay consistent with the agency profile.");
            string canon = PromptBuilder.ProfileText(State);
            if (canon != "") sb.AppendLine(canon);
            sb.AppendLine("Reply with JSON only: {\"replies\":[{\"n\":1,\"body\":\"...\"}]}");
            var u = new StringBuilder();
            for (int i = 0; i < due.Count; i++) u.AppendLine((i + 1) + ". From " + SenderLine(due[i]) + ", subject \"" + due[i].Subject + "\": " + ResponseParser.Clip(due[i].Body, 700));
            AskJson(Settings.Cheap, sb.ToString(), u.ToString(), 1200, 0, (root, err, raw, res) =>
            {
                MailBusy = false;
                var arr = root != null ? Json.Arr(Json.Get(root, "replies")) : null;
                for (int i = 0; i < due.Count; i++)
                {
                    var o = arr == null ? null : arr.FirstOrDefault(x => (int)Json.Num(x, "n", -1) == i + 1) ?? (i < arr.Count ? arr[i] : null);
                    string b = o != null ? Json.Str(o, "body", "").Trim() : "";
                    if (b == "") b = "Thank you for writing to " + AgencyName + ". We read every letter, and we appreciate you taking the time.\n\n" + AssistantName + ", for the Director";
                    var m = due[i];
                    AddMail(new MailMsg { Thread = m.Id, Kind = "me", Outgoing = true, From = AssistantName + " for the Director", FromAddr = DirectorAddr, To = m.From, ToAddr = m.FromAddr, Subject = "Re: " + m.Subject, Body = ResponseParser.Clip(b, 2000), UT = ut, Read = true, ByAssistant = true, Notified = true });
                    m.Read = true;
                }
                AssistantSay("result", "I answered " + due.Count + " routine email" + (due.Count == 1 ? "" : "s") + " for you: " + string.Join(", ", due.Select(m => m.From).ToArray()) + ". They're in Kmail under \"Handled by assistant\".", ut);
                RaiseCommunity();
            });
            return true;
        }

        // ------------------------------------------------------------------ for the assistant

        public string MailLookup(string filter, string query, double ut)
        {
            var roots = VisibleMail(ut).Where(m => m.IsRoot).OrderByDescending(m => MailThread(m).Max(x => x.UT)).ToList();
            filter = (filter ?? "").ToLowerInvariant();
            if (filter == "unread") roots = roots.Where(m => MailThread(m).Any(x => !x.Read && !x.Outgoing && x.UT <= ut)).ToList();
            else if (filter == "open") roots = roots.Where(m => m.Open).ToList();
            if (!string.IsNullOrEmpty(query)) roots = roots.Where(m => MailThread(m).Any(x => (x.Subject + " " + x.Body + " " + x.From).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            if (roots.Count == 0) return "No emails match.";
            var sb = new StringBuilder();
            sb.AppendLine("Kmail (id | from | subject | status | latest):");
            foreach (var m in roots.Take(25))
            {
                var last = MailThread(m).Where(x => x.UT <= ut).Last();
                sb.AppendLine(m.Id + " | " + (m.Outgoing ? "to " + m.To : m.From) + " | " + m.Subject + " | " + MailStatusText(m, ut) + " | " + (last.Outgoing ? "we wrote last" : "they wrote last") + ", " + Clock.FormatDate(last.UT) + (MailThread(m).Any(x => !x.Read && !x.Outgoing && x.UT <= ut) ? ", UNREAD" : ""));
            }
            return sb.ToString();
        }

        public string MailStatusText(MailMsg m, double ut)
        {
            switch (m.Status)
            {
                case "open": return m.Waiting ? "waiting for their answer" : (m.Ask == "sponsor" ? "offer √" + Money(m.Amount) + ", " : "") + "reply by " + Clock.FormatDate(m.DueUT) + " (" + m.Ask + ")";
                case "awaiting": return "the director must approve √" + Money(m.Amount);
                case "accepted": return "accepted" + (m.Outcome != "" ? ": " + m.Outcome : "");
                case "declined": return "declined";
                case "expired": return "ignored: " + m.Outcome;
                case "done": return "answered" + (m.Outcome != "" ? ": " + m.Outcome : "");
                default: return "no request";
            }
        }

        public string MailRead(string key, double ut)
        {
            var m = FindMail(key, ut);
            if (m == null) return "I couldn't find that email.";
            m.Read = true;
            var sb = new StringBuilder();
            sb.AppendLine("Email " + m.Id + ": " + m.Subject + " | " + MailStatusText(m, ut) + (m.Ask != "" ? " | request: " + m.Ask + (m.Kerbal != "" ? " with " + m.Kerbal : "") : ""));
            foreach (var x in MailThread(m).Where(x => x.UT <= ut)) { x.Read = true; sb.AppendLine((x.Outgoing ? "FROM US" : "FROM " + x.From + " <" + x.FromAddr + ">") + ", " + Clock.FormatDate(x.UT) + ":\n" + ResponseParser.Clip(x.Body, 1500)); }
            return sb.ToString();
        }

        /// <summary>A line for the assistant's context: what's waiting in the inbox.</summary>
        string InboxText(double ut)
        {
            int unread = UnreadMail(ut);
            var open = VisibleMail(ut).Where(m => m.IsRoot && m.Open).OrderBy(m => m.DueUT).Take(6).ToList();
            if (unread == 0 && open.Count == 0) return "";
            return "KMAIL: " + unread + " unread." + (open.Count > 0 ? " Open requests: " + string.Join("; ", open.Select(m => m.Id + " " + m.From + " \"" + m.Subject + "\" (" + MailStatusText(m, ut) + ")").ToArray()) : "") + (State.MailAuto ? " You answer routine mail on your own." : "");
        }
    }
}
