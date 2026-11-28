using System;
using System.Collections.Generic;
using System.Linq;

namespace KerbalNews.Web
{
    /// <summary>kmail.kb: the director's email. Inbox, folders, one conversation with its request, and compose.</summary>
    public partial class Sites
    {
        public const string KmailHost = "kmail.kb";
        /// <summary>A line shown once at the top of the next Kmail page (what a quick reply did).</summary>
        string mailNotice = "";

        public static string MailUrl(MailMsg m) { return KmailHost + "/mail/" + m.Id; }

        static readonly string[][] MailLabels =
        {
            new[] { "press", "Press" }, new[] { "sponsor", "Sponsors" }, new[] { "council", "Council" },
            new[] { "fan", "Fans and schools" }, new[] { "family", "Crew families" }, new[] { "public", "Public" },
        };

        static string MailKindLabel(string kind)
        {
            switch (kind)
            {
                case "press": return "Press";
                case "sponsor": return "Sponsor";
                case "council": return "Council";
                case "fan": return "Fans and schools";
                case "family": return "Crew family";
                case "public": return "Public";
                default: return "";
            }
        }

        PageResult Kmail(string url, string path, Dictionary<string, string> query)
        {
            double now = Now();
            if (path.StartsWith("mail/"))
            {
                var m = Engine.FindMail(path.Substring(5), now);
                if (m == null) return Page(url, "kmail", "index.html", KmailList("inbox", "", now), "Kmail", true);
                return Page(url, "kmail", "mail.html", KmailOpen(m, now), m.Subject + " - Kmail", true);
            }
            if (path == "compose")
            {
                var cm = KmailFrame(M(), "compose", now);
                cm["to"] = query.ContainsKey("to") ? query["to"] : "";
                cm["subject"] = query.ContainsKey("subject") ? query["subject"] : "";
                cm["outletList"] = string.Join(", ", Outlets.Enabled.Select(o => o.Name).ToArray());
                return Page(url, "kmail", "compose.html", cm, "Compose - Kmail", false);
            }
            if (path == "search") return Page(url, "kmail", "index.html", KmailList("search", query.ContainsKey("q") ? query["q"] : "", now), "Search - Kmail", true);
            string folder = path == "" ? "inbox" : path;
            return Page(url, "kmail", "index.html", KmailList(folder, "", now), "Kmail", true);
        }

        Dictionary<string, object> KmailFrame(Dictionary<string, object> m, string folder, double now)
        {
            var roots = Engine.VisibleMail(now).Where(x => x.IsRoot && !x.Deleted).ToList();
            Func<MailMsg, bool> unread = r => Engine.MailThread(r).Any(x => !x.Read && !x.Outgoing && x.UT <= now);
            Func<string, string, string, int, object> f = (id, name, link, count) => new Dictionary<string, object>
            {
                { "name", name }, { "url", KmailHost + (link != "" ? "/" + link : "") }, { "count", count > 0 ? count.ToString() : "" }, { "on", folder == id },
            };
            m["folders"] = new List<object>
            {
                f("inbox", "Inbox", "", roots.Count(r => !r.Outgoing && unread(r))),
                f("needs", "Needs a reply", "needs", roots.Count(r => r.Open)),
                f("sent", "Sent", "sent", 0),
                f("assistant", "Handled by assistant", "assistant", 0),
                f("all", "All mail", "all", 0),
                f("trash", "Trash", "trash", 0),
            };
            m["labels"] = MailLabels.Select(l => (object)new Dictionary<string, object> { { "name", l[1] }, { "url", KmailHost + "/label/" + l[0] }, { "on", folder == "label/" + l[0] } }).ToList();
            m["me"] = Engine.DirectorAddr;
            m["notice"] = mailNotice;
            mailNotice = "";
            m["busy"] = Engine.MailBusy;
            m["auto"] = State.MailAuto;
            m["assistantName"] = NewsEngine.AssistantName;
            m["assistantFirst"] = NewsEngine.AssistantName.Split(' ')[0];
            m["mailOff"] = !Engine.Settings.SiteOn("kmail") || Engine.Settings.MailLevel <= 0;
            return m;
        }

        string MailTime(double ut, double now)
        {
            double day = Clock.DaySeconds;
            if (Math.Floor(ut / day) == Math.Floor(now / day))
            {
                double s = ut - Math.Floor(ut / day) * day;
                int h = (int)(s / 3600), min = (int)(s % 3600 / 60);
                return h + ":" + min.ToString("00");
            }
            return ShortDate(ut, now);
        }

        /// <summary>"Day 44" within the current year, else the full date.</summary>
        string ShortDate(double ut, double now)
        {
            string d = Date(ut);
            int comma = d.IndexOf(", ");
            return comma > 0 && Math.Floor(ut / Clock.YearSeconds) == Math.Floor(now / Clock.YearSeconds) ? d.Substring(comma + 2) : d;
        }

        /// <summary>The small tag on an inbox row: a deadline, an offer, who handled it, or the kind of sender.</summary>
        void MailBadge(MailMsg r, Dictionary<string, object> row, double now)
        {
            string text = "", cls = "";
            if (r.Status == "awaiting") { text = "Approve √" + NewsEngine.Money(r.Amount); cls = "offer"; }
            else if (r.Open && r.Waiting) { text = "Waiting for them"; cls = "plain"; }
            else if (r.Open && r.Ask == "sponsor") { text = "Offer: √" + NewsEngine.Money(r.Amount); cls = "offer"; }
            else if (r.Open && r.DueUT >= 0) { text = "Reply by " + ShortDate(r.DueUT, now); cls = "due"; }
            else if (Engine.MailThread(r).Any(x => x.ByAssistant)) { text = "Handled"; cls = "plain"; }
            else if (r.Status == "expired") { text = "Ignored"; cls = "bad"; }
            else if (r.Status == "accepted" || r.Status == "done") { text = r.Status == "accepted" ? "Accepted" : "Answered"; cls = "good"; }
            else if (r.Status == "declined") { text = "Declined"; cls = "plain"; }
            else if (r.Kind != "press" && r.Kind != "me") { text = MailKindLabel(r.Kind); cls = "plain"; }
            row["badge"] = text;
            row["badgeClass"] = cls;
        }

        Dictionary<string, object> KmailList(string folder, string q, double now)
        {
            var m = KmailFrame(M(), folder, now);
            bool trash = folder == "trash";
            var roots = Engine.VisibleMail(now).Where(x => x.IsRoot && x.Deleted == trash).ToList();
            IEnumerable<MailMsg> pick;
            string title;
            switch (folder)
            {
                case "needs": pick = roots.Where(r => r.Open); title = "Needs a reply"; break;
                case "sent": pick = roots.Where(r => Engine.MailThread(r).Any(x => x.Outgoing && !x.ByAssistant)); title = "Sent"; break;
                case "assistant": pick = roots.Where(r => Engine.MailThread(r).Any(x => x.ByAssistant)); title = "Handled by assistant"; break;
                case "all": pick = roots; title = "All mail"; break;
                case "trash": pick = roots; title = "Trash"; break;
                case "search":
                    pick = roots.Where(r => q != "" && Engine.MailThread(r).Any(x => x.UT <= now && (x.Subject + " " + x.Body + " " + x.From + " " + x.To).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));
                    title = "Search results for \"" + q + "\""; break;
                default:
                    if (folder.StartsWith("label/"))
                    {
                        string k = folder.Substring(6);
                        pick = roots.Where(r => r.Kind == k || r.Kind == "fan" && k == "fan");
                        var l = MailLabels.FirstOrDefault(x => x[0] == k);
                        title = l != null ? l[1] : "Label";
                    }
                    else { pick = roots.Where(r => !r.Outgoing || Engine.MailThread(r).Any(x => !x.Outgoing && x.UT <= now)); title = "Inbox"; }
                    break;
            }
            var rows = new List<object>();
            foreach (var r in pick.OrderByDescending(r => Engine.MailThread(r).Where(x => x.UT <= now).Max(x => x.UT)).Take(80))
            {
                var thread = Engine.MailThread(r).Where(x => x.UT <= now).ToList();
                var last = thread.Last();
                bool unread = thread.Any(x => !x.Read && !x.Outgoing);
                var lastIn = thread.LastOrDefault(x => !x.Outgoing);
                string who = lastIn != null ? lastIn.From : "To: " + r.To;
                if (thread.Count > 1) who += " (" + thread.Count + ")";
                var row = new Dictionary<string, object>
                {
                    { "url", MailUrl(r) }, { "from", who }, { "subject", r.Subject }, { "unread", unread },
                    { "snippet", ResponseParser.Clip(last.Body.Replace("\n", " ").Replace("  ", " "), 90) + (last.Body.Length > 90 ? "..." : "") },
                    { "time", MailTime(last.UT, now) },
                    { "delUrl", "action:mail-" + (trash ? "restore/" : "delete/") + r.Id }, { "delLabel", trash ? "Restore" : "Delete" },
                };
                MailBadge(r, row, now);
                rows.Add(row);
            }
            m["rows"] = rows;
            m["empty"] = rows.Count == 0;
            m["title"] = title;
            m["today"] = Date(now);
            m["q"] = q;
            m["unreadTotal"] = trash ? 0 : Engine.UnreadMail(now);
            m["trash"] = trash;
            m["trashFull"] = trash && rows.Count > 0;
            m["emptyText"] = trash ? "Trash is empty. Deleted mail waits here until you empty it, so you can still restore it." : "No mail here yet. Outlets, sponsors, the Council and the public write in after your big moments.";
            return m;
        }

        Dictionary<string, object> KmailOpen(MailMsg r, double now)
        {
            var m = KmailFrame(M(), "", now);
            var thread = Engine.MailThread(r).Where(x => x.UT <= now).ToList();
            foreach (var x in thread) x.Read = true;
            m["id"] = r.Id;
            m["deleted"] = r.Deleted;
            m["backUrl"] = KmailHost + (r.Deleted ? "/trash" : "");
            m["backText"] = r.Deleted ? "Back to Trash" : "Back to inbox";
            m["subject"] = r.Subject;
            m["label"] = MailKindLabel(r.Kind);
            m["msgs"] = thread.Select(x => (object)new Dictionary<string, object>
            {
                { "from", x.Outgoing ? (x.ByAssistant ? NewsEngine.AssistantName + ", for the Director" : "Director") : x.From },
                { "addr", x.Outgoing ? x.FromAddr : x.FromAddr },
                { "to", x.Outgoing ? x.To : "me" },
                { "initials", Initials(x.Outgoing ? (x.ByAssistant ? NewsEngine.AssistantName : "Director") : x.From) },
                { "out", x.Outgoing },
                { "when", Date(x.UT) + ", " + MailTime(x.UT, x.UT) },
                { "paras", x.Body.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries).Select(p => (object)new Dictionary<string, object> { { "text", p.Trim() } }).ToList() },
            }).ToList();
            bool open = r.Open && r.Status == "open" && !r.Waiting;
            m["open"] = open;
            m["awaiting"] = r.Status == "awaiting";
            m["amount"] = NewsEngine.Money(r.Amount);
            m["waiting"] = r.Open && r.Waiting;
            m["acceptLabel"] = AcceptLabel(r, now);
            m["canOther"] = open && (r.Ask == "interview" || r.Ask == "visit");
            string st = r.Status != "" && r.Status != "open" ? Engine.MailStatusText(r, now) : "";
            m["mailStatus"] = st.Length > 0 ? char.ToUpperInvariant(st[0]) + st.Substring(1) : "";
            m["due"] = r.Open && r.DueUT >= 0 ? "Reply by " + Date(r.DueUT) : "";
            m["ifIgnored"] = r.Open && r.Status == "open" ? IfIgnored(r) + (r.Deleted ? " Deleting it counts the same as not answering." : "") : "";
            m["yesDoes"] = r.Open ? YesDoes(r) : "";
            var lastIn = thread.LastOrDefault(x => !x.Outgoing);
            m["replyTo"] = lastIn != null ? lastIn.From : r.To;
            m["assistantTip"] = AssistantTip(r);
            return m;
        }

        static string Initials(string name)
        {
            var parts = (name ?? "").Split(new[] { ' ', '.', '_' }, StringSplitOptions.RemoveEmptyEntries).Where(p => char.IsLetter(p[0])).ToList();
            if (parts.Count == 0) return "?";
            return (parts.Count == 1 ? parts[0].Substring(0, Math.Min(2, parts[0].Length)) : "" + parts[0][0] + parts[parts.Count - 1][0]).ToUpperInvariant();
        }

        string AcceptLabel(MailMsg r, double now)
        {
            switch (r.Ask)
            {
                case "interview": return "Accept: book " + FirstOf(r.Kerbal) + " for tomorrow";
                case "statement": return "Send a statement";
                case "sponsor": return "Accept the √" + NewsEngine.Money(r.Amount) + " offer";
                case "hearing": return "Accept: I'll testify";
                case "visit": return "Accept: " + FirstOf(r.Kerbal) + " will visit";
                case "update": return "Send a kind update";
                default: return "Accept";
            }
        }

        static string FirstOf(string name) { int i = (name ?? "").IndexOf(' '); return i > 0 ? name.Substring(0, i) : (name != "" ? name : "them"); }

        string YesDoes(MailMsg r)
        {
            string outlet = Outlets.NameOf(r.Org);
            switch (r.Ask)
            {
                case "interview": return "Books " + r.Kerbal + " for an interview with " + outlet + ", the same way the assistant schedules one. You can coach " + FirstOf(r.Kerbal) + " in crew chat before it happens, and other outlets may grumble about the exclusive.";
                case "statement": return "Your reply is quoted as the agency's statement in " + outlet + "'s story. Saying no means the story says the agency declined to comment.";
                case "sponsor": return "Nothing moves yet. Accepting asks you to approve the √" + NewsEngine.Money(r.Amount) + " here; only then is the money paid into your funds and the deal announced.";
                case "hearing": return "You testify before the Council's science committee, and the Council issues a statement about it. Trust in the agency goes up a little.";
                case "visit": return FirstOf(r.Kerbal) + " visits the class. The public warms to the program, and the Kerbin Herald may run a story.";
                case "update": return "A kind, honest answer reassures the family and nudges public trust up.";
                default: return "";
            }
        }

        string IfIgnored(MailMsg r)
        {
            string outlet = Outlets.NameOf(r.Org);
            switch (r.Ask)
            {
                case "interview": return "If you don't answer, " + outlet + " writes that the agency did not respond, and the reporter remembers it.";
                case "statement": return "If you don't answer, " + outlet + " runs its story saying the agency did not respond.";
                case "sponsor": return "If you don't answer, the offer lapses and they take their money elsewhere.";
                case "hearing": return "If you don't answer, the Council notes your absence in public and trust drops.";
                case "visit": return "If you don't answer, the class is disappointed.";
                case "update": return "If you don't answer, the family may go to the press.";
                default: return "";
            }
        }

        string AssistantTip(MailMsg r)
        {
            if (!r.Open) return "";
            switch (r.Ask)
            {
                case "interview": return "Want me to accept and give " + FirstOf(r.Kerbal) + " the questions ahead of time?";
                case "statement": return "I can draft a careful statement for you to approve.";
                case "sponsor": return "I can check whether the offer is fair before you sign.";
                default: return "I can answer this for you.";
            }
        }

        void KmailAction(string verb, string arg, IDictionary<string, string> form, ActionResult r)
        {
            double now = Now();
            string id = arg, what = "";
            int slash = arg.IndexOf('/');
            if (slash >= 0) { id = arg.Substring(0, slash); what = arg.Substring(slash + 1); }
            switch (verb)
            {
                case "mail-search":
                    r.Navigate = KmailHost + "/search?q=" + Uri.EscapeDataString(Get(form, "mq").Trim());
                    break;
                case "mail-quick":
                    mailNotice = Engine.MailQuick(id, what, Get(form, "kerbal"), now);
                    break;
                case "mail-reply":
                {
                    string body = Get(form, "body").Trim();
                    mailNotice = body == "" ? "Write something first." : Engine.MailReply(id, body, "", "", "", "", now);
                    break;
                }
                case "mail-send":
                {
                    string res = Engine.MailSend(Get(form, "to"), Get(form, "subject"), Get(form, "body"), now);
                    mailNotice = res;
                    if (res.StartsWith("Sent")) r.Navigate = KmailHost + "/sent";
                    break;
                }
                case "mail-assist":
                {
                    if (id == "inbox")
                    {
                        Engine.AssistantSend("Sum up my Kmail inbox: what needs a reply and by when, and what you'd suggest for each.", now);
                        r.Navigate = "agency.kb/assistant";
                        break;
                    }
                    var m = Engine.FindMail(id, now);
                    if (m == null) break;
                    Engine.AssistantSend("Please read email " + m.Id + " (\"" + m.Subject + "\" from " + m.From + ") and reply to it for me with reply_mail. Use your judgment and tell me what you said.", now);
                    r.Navigate = "agency.kb/assistant";
                    break;
                }
                case "mail-auto":
                    State.MailAuto = arg == "on";
                    mailNotice = State.MailAuto ? NewsEngine.AssistantName + " will answer routine mail (fan letters, citizens, simple press questions). Anything with a request still comes to you." : "You'll answer all mail yourself.";
                    break;
                case "mail-read":
                    foreach (var m in Engine.VisibleMail(now)) if (!Engine.InTrash(m)) m.Read = true;
                    break;
                case "mail-delete":
                    mailNotice = Engine.MailDelete(id, now);
                    // From the open email ("mail-delete/<id>/open"), go back to the inbox; from a list, stay on it.
                    if (what == "open") r.Navigate = KmailHost;
                    break;
                case "mail-restore":
                    mailNotice = Engine.MailRestore(id, now);
                    if (what == "open") r.Navigate = MailUrl(Engine.FindMail(id, now) ?? new MailMsg { Id = id });
                    break;
                case "mail-empty-trash":
                    mailNotice = Engine.MailEmptyTrash(now);
                    r.Navigate = KmailHost + "/trash";
                    break;
            }
        }
    }
}
