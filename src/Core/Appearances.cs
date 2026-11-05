using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>A kerbal booked to talk to the press: an interview with one outlet, or a briefing for several.</summary>
    public class CrewAppearance
    {
        public string Id = "";
        public string Kerbal = "";
        /// <summary>interview (one outlet) or briefing (a press conference for several).</summary>
        public string Kind = "interview";
        public string Outlets = "";
        public string Topic = "";
        public double BookedUT;
        public double AtUT;
        /// <summary>scheduled, done or cancelled.</summary>
        public string Status = "scheduled";
        /// <summary>What the director told the kerbal to say or not say, gathered from crew chat.</summary>
        public List<string> Coaching = new List<string>();
        public string DealId = "";
        public string Outcome = "";
        public bool Slipped;

        public void Save(DataNode n)
        {
            n.Add("id", Id); n.AddText("kerbal", Kerbal); n.Add("kind", Kind); n.Add("outlets", Outlets); n.AddText("topic", Topic);
            n.Add("booked", BookedUT); n.Add("at", AtUT); n.Add("status", Status); n.Add("deal", DealId); n.AddText("outcome", Outcome); n.Add("slipped", Slipped);
            foreach (var c in Coaching) n.AddText("coach", c);
        }

        public static CrewAppearance Load(DataNode n)
        {
            var a = new CrewAppearance
            {
                Id = n.Get("id", ""), Kerbal = n.GetText("kerbal"), Kind = n.Get("kind", "interview"), Outlets = n.Get("outlets", ""), Topic = n.GetText("topic"),
                BookedUT = n.GetDouble("booked"), AtUT = n.GetDouble("at"), Status = n.Get("status", "scheduled"), DealId = n.Get("deal", ""),
                Outcome = n.GetText("outcome"), Slipped = n.GetBool("slipped"),
            };
            foreach (var c in n.GetAll("coach")) a.Coaching.Add(TextCodec.Decode(c));
            return a;
        }
    }

    public partial class NewsEngine
    {
        static readonly string[] BriefingOutlets = { "herald", "observer", "inquirer", "munmonthly" };

        /// <summary>
        /// When: "now", "tomorrow", "in 3 days", "in 6 hours", "day 120", "next week", or a plain number of days. Returns a UT; never earlier than a few hours from now.
        /// </summary>
        public double ParseWhen(string when, double ut)
        {
            string w = (when ?? "").Trim().ToLowerInvariant();
            double minUT = ut + Day * 0.1;
            if (w == "" || w == "soon" || w == "asap") return ut + Day * 0.5;
            if (w == "now" || w == "today" || w == "right away") return minUT;
            if (w == "tomorrow") return ut + Day;
            if (w.Contains("next week")) return ut + Day * 7;
            var m = System.Text.RegularExpressions.Regex.Match(w, @"day\s+(\d+)");
            if (m.Success)
            {
                double year = Clock.YearSeconds > 0 ? Clock.YearSeconds : Day * 426;
                int d = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                double yearStart = Math.Floor(ut / year) * year;
                double at = yearStart + (d - 1) * Day + Day * 0.4;
                if (at <= ut) at += year;
                return Math.Max(minUT, at);
            }
            m = System.Text.RegularExpressions.Regex.Match(w, @"(\d+(?:\.\d+)?)\s*(hour|hr|h\b|day|d\b|week)?");
            if (m.Success)
            {
                double n = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                string unit = m.Groups[2].Value;
                double sec = unit.StartsWith("h") ? n * 3600 : unit.StartsWith("w") ? n * 7 * Day : n * Day;
                return Math.Max(minUT, ut + sec);
            }
            return ut + Day;
        }

        /// <summary>Upcoming appearances for one kerbal, soonest first.</summary>
        public List<CrewAppearance> UpcomingFor(string kerbal)
        {
            return State.Appearances.Where(a => a.Status == "scheduled" && a.Kerbal == kerbal).OrderBy(a => a.AtUT).ToList();
        }

        string ScheduleAppearance(Dictionary<string, object> a, double ut)
        {
            string who = Arg(a, "kerbal");
            var c = FindKerbal(who);
            if (c == null) return "I couldn't find a kerbal called \"" + who + "\" on the roster.";
            if (c.Dead) return c.Name + " is no longer with us.";
            string kind = Arg(a, "kind", "interview").ToLowerInvariant();
            kind = kind.Contains("brief") || kind.Contains("conference") ? "briefing" : "interview";
            string outlets;
            if (kind == "interview")
            {
                var o = FindOutlet(Arg(a, "outlet")) ?? Outlets.Get("herald");
                outlets = o.Id;
            }
            else
            {
                var list = ArgList(a, "outlets").Select(x => FindOutlet(x)).Where(x => x != null).Select(x => x.Id).Distinct().ToList();
                outlets = string.Join(",", (list.Count > 0 ? list : BriefingOutlets.ToList()).ToArray());
            }
            double at = ParseWhen(Arg(a, "when"), ut);
            string topic = ResponseParser.Clip(Arg(a, "topic", "their work at the agency"), 400);
            var ap = new CrewAppearance { Id = State.NewId("ap"), Kerbal = c.Name, Kind = kind, Outlets = outlets, Topic = topic, BookedUT = ut, AtUT = at };
            string names = string.Join(", ", outlets.Split(',').Select(Outlets.NameOf).ToArray());
            var d = Log("appearance", (kind == "interview" ? "Booked " + c.Name + " for an interview with " + names : "Booked " + c.Name + " to brief " + names) + " on " + Clock.FormatDate(at) + ": " + ResponseParser.Clip(topic, 60), 0, false, ut);
            d.Status = "pending";
            d.ResolveUT = -1; // settled by TickAppearances, not the deal resolver
            d.Target = outlets;
            ap.DealId = d.Id;
            State.Appearances.Add(ap);
            CrewLine(c, "note", "Scheduled: " + (kind == "interview" ? "interview with " + names : "press briefing for " + names) + " on " + Clock.FormatDate(at) + ", about " + topic + ". Talk to " + FirstName(c.Name) + " before then to say what they can and can't mention.", ut);
            return "Booked " + c.Name + (kind == "interview" ? " for an interview with " + names : " to give a press briefing to " + names) + " on " + Clock.FormatDate(at) + ". You can brief " + FirstName(c.Name) + " in crew chat before then.";
        }

        string CancelAppearance(Dictionary<string, object> a, double ut)
        {
            string key = Arg(a, "appearance", Arg(a, "kerbal"));
            var ap = State.Appearances.Where(x => x.Status == "scheduled").FirstOrDefault(x => x.Id == key || x.Kerbal.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0);
            if (ap == null) return "There's no scheduled appearance like that.";
            ap.Status = "cancelled";
            var d = State.Deals.FirstOrDefault(x => x.Id == ap.DealId);
            if (d != null) { d.Status = "refused"; d.Outcome = "Cancelled by the agency."; }
            foreach (var o in ap.Outlets.Split(',')) if (o != "") Tie(o, null, -1, "had " + ap.Kerbal + "'s " + ap.Kind + " cancelled", ut);
            CrewFame c;
            if (State.Crew.TryGetValue(ap.Kerbal, out c)) CrewLine(c, "note", "Cancelled: the " + ap.Kind + " on " + Clock.FormatDate(ap.AtUT) + ".", ut);
            return "Cancelled " + ap.Kerbal + "'s " + ap.Kind + ". The outlet" + (ap.Outlets.Contains(",") ? "s are" : " is") + " not pleased.";
        }

        CrewFame FindKerbal(string who)
        {
            who = (who ?? "").Trim();
            if (who == "") return null;
            CrewFame c;
            if (State.Crew.TryGetValue(who, out c)) return c;
            var all = State.Crew.Values.ToList();
            return all.FirstOrDefault(x => x.Name.Equals(who, StringComparison.OrdinalIgnoreCase))
                ?? all.FirstOrDefault(x => FirstName(x.Name).Equals(who, StringComparison.OrdinalIgnoreCase) || FirstName(x.Name).Equals(FirstName(who), StringComparison.OrdinalIgnoreCase))
                ?? all.FirstOrDefault(x => x.Name.IndexOf(who, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>The chance a kerbal blurts out something they were told not to, from their stats.</summary>
        public static double SlipChance(CrewFame c)
        {
            double p = 0.06 + c.Stupidity * 0.4 + (1 - c.Courage) * 0.12;
            if (c.Badass) p += 0.08;   // says what they think
            if (c.Veteran) p -= 0.05;  // media-trained
            p -= Math.Min(0.1, c.Level * 0.02);
            return Math.Max(0.02, Math.Min(0.7, p));
        }

        /// <summary>When a booked time arrives, the appearance becomes a story for the outlet(s), written with the kerbal's personality and coaching.</summary>
        void TickAppearances(double ut)
        {
            foreach (var ap in State.Appearances.Where(x => x.Status == "scheduled" && x.AtUT <= ut).ToList())
            {
                CrewFame c;
                State.Crew.TryGetValue(ap.Kerbal, out c);
                var d = State.Deals.FirstOrDefault(x => x.Id == ap.DealId);
                if (c == null || c.Dead)
                {
                    ap.Status = "cancelled";
                    if (d != null) { d.Status = "refused"; d.Outcome = "Called off: " + ap.Kerbal + " could not attend."; }
                    continue;
                }
                ap.Status = "done";
                bool coached = ap.Coaching.Count > 0;
                ap.Slipped = coached && dealRng.NextDouble() < SlipChance(c);
                var first = ap.Outlets.Split(',')[0];
                var reporter = Tie(first, null, ap.Kind == "interview" ? 2 : 1, "got " + (ap.Kind == "interview" ? "an interview" : "a briefing") + " with " + c.Name, ut);
                var sb = new StringBuilder();
                string place = c.RosterStatus == "assigned" && c.VesselName != "" ? " over a radio link from " + c.VesselName + (c.Where != "" ? ", " + c.Where : "") : " at the space center";
                if (ap.Kind == "interview")
                    sb.Append(Outlets.NameOf(first) + " interviewed " + c.Name + " of " + AgencyName + place + " about " + ap.Topic + ". The interviewer and byline is " + reporter.Name + ". Write it as an interview feature with direct quotes. ");
                else
                    sb.Append(c.Name + " of " + AgencyName + " gave a press briefing" + place + " about " + ap.Topic + ", taking questions from reporters. ");
                sb.Append(c.Name + "'s manner (from their record): " + Personality(c) + "; the public sees them as " + c.Tier + ". ");
                if (c.Log.Count > 0) sb.Append("What they have been through: " + string.Join("; ", c.Log.Skip(Math.Max(0, c.Log.Count - 6)).ToArray()) + ". ");
                if (coached)
                {
                    sb.Append("The agency briefed them beforehand on what to say and avoid: " + string.Join("; ", ap.Coaching.ToArray()) + ". ");
                    sb.Append(ap.Slipped
                        ? "But during the conversation " + FirstName(c.Name) + " slips up: they let ONE thing they were told to avoid come out (pick the juiciest), and the reporter pounces on it. That slip is the news angle. "
                        : FirstName(c.Name) + " sticks to the agency's line. Reporters may notice the careful, rehearsed answers. ");
                }
                else sb.Append("They were not coached, so they speak freely and honestly in their own voice. ");
                double sig = 26 + c.Fame * 0.25 + (ap.Slipped ? 12 : 0) + (ap.Kind == "briefing" ? 6 : 0);
                string headline = ap.Kind == "interview" ? c.Name + " on " + ResponseParser.Clip(ap.Topic, 70) : c.Name + " briefs the press on " + ResponseParser.Clip(ap.Topic, 60);
                Tip(ap.Outlets, sb.ToString(), headline, c.Name + " spoke to the press about " + ap.Topic + ".", sig, ut, ap.Kind == "interview" ? "feature" : "news", ap.DealId);
                ap.Outcome = ap.Slipped ? FirstName(c.Name) + " let something slip." : coached ? FirstName(c.Name) + " stayed on message." : FirstName(c.Name) + " spoke freely.";
                if (d != null) { d.Status = ap.Slipped ? "backfired" : "done"; d.Outcome = ap.Outcome; }
                LifeLog(c, Clock.FormatDate(ut) + ": " + (ap.Kind == "interview" ? "interview with " + Outlets.NameOf(first) : "gave a press briefing") + " about " + ResponseParser.Clip(ap.Topic, 60) + (ap.Slipped ? " (let something slip)" : ""));
                c.Fame = Math.Min(100, c.Fame + (ap.Kind == "briefing" ? 3 : 2));
                c.PeakFame = Math.Max(c.PeakFame, c.Fame);
                CrewLine(c, "note", (ap.Kind == "interview" ? "The interview" : "The briefing") + " happened. " + ap.Outcome, ut);
                AssistantSay("result", "Update: " + c.Name + "'s " + ap.Kind + " took place. " + ap.Outcome, ut);
            }
        }
    }
}
