using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>How well known one kerbal is, built from what they have flown and what the press has said.</summary>
    public class CrewFame
    {
        public string Name = "";
        /// <summary>0-100.</summary>
        public double Fame;
        public double PeakFame;
        public int Flights;
        public double FirstFlightUT = -1;
        public double LastFlightUT = -1;
        /// <summary>Notable moments, newest last: "first kerbal in orbit (Year 1, Day 12)".</summary>
        public List<string> Highlights = new List<string>();
        public List<string> Scandals = new List<string>();
        public bool Dead;
        public double DiedUT = -1;
        public string DiedOn = "";
        /// <summary>Vessels already counted as a flight.</summary>
        public HashSet<string> Vessels = new HashSet<string>();
        // From the game roster (refreshed by the KSP layer).
        public string Trait = "";
        public int Level;
        public double Courage = 0.5, Stupidity = 0.5;
        public bool Badass, Veteran, OnRoster;
        public string Gender = "", Where = "", VesselName = "", RosterStatus = "available";
        /// <summary>What they have been through, in plain words with dates, oldest first.</summary>
        public List<string> Log = new List<string>();
        /// <summary>Crew chat with the director, recent notes from it, and a summary of older talks.</summary>
        public List<AssistantMessage> Chat = new List<AssistantMessage>();
        public List<string> Notes = new List<string>();
        public string MemorySummary = "";

        public string Tier
        {
            get
            {
                if (Fame >= 80) return "a living legend";
                if (Fame >= 55) return "a household name";
                if (Fame >= 30) return "a rising star";
                if (Fame >= 10) return "known to space fans";
                return "largely unknown to the public";
            }
        }

        /// <summary>Fans following their unofficial fan page on Kerbnet.</summary>
        public int Followers { get { return (int)Math.Round(Math.Pow(Math.Max(0, PeakFame * 0.6 + Fame * 0.4), 2.1) * 3.1 / 10) * 10; } }

        public void Save(DataNode n)
        {
            n.AddText("name", Name);
            n.Add("fame", Fame);
            n.Add("peak", PeakFame);
            n.Add("flights", Flights);
            n.Add("first", FirstFlightUT);
            n.Add("last", LastFlightUT);
            n.Add("dead", Dead);
            n.Add("died", DiedUT);
            n.AddText("diedOn", DiedOn);
            foreach (var h in Highlights) n.AddText("hl", h);
            foreach (var s in Scandals) n.AddText("scandal", s);
            foreach (var v in Vessels) n.Add("vessel", v);
            n.Add("trait", Trait); n.Add("level", Level); n.Add("courage", Courage); n.Add("stupidity", Stupidity);
            n.Add("badass", Badass); n.Add("veteran", Veteran); n.Add("onRoster", OnRoster); n.Add("gender", Gender);
            n.AddText("where", Where); n.AddText("vesselName", VesselName); n.Add("roster", RosterStatus);
            foreach (var l in Log) n.AddText("log", l);
            foreach (var x in Notes) n.AddText("note", x);
            n.AddText("memory", MemorySummary);
            var chat = n.AddNode("CHAT");
            foreach (var m in Chat) m.Save(chat.AddNode("MSG"));
        }

        public static CrewFame Load(DataNode n)
        {
            var c = new CrewFame
            {
                Name = n.GetText("name"), Fame = n.GetDouble("fame"), PeakFame = n.GetDouble("peak"), Flights = n.GetInt("flights"),
                FirstFlightUT = n.GetDouble("first", -1), LastFlightUT = n.GetDouble("last", -1), Dead = n.GetBool("dead"),
                DiedUT = n.GetDouble("died", -1), DiedOn = n.GetText("diedOn"),
            };
            foreach (var h in n.GetAll("hl")) c.Highlights.Add(TextCodec.Decode(h));
            foreach (var s in n.GetAll("scandal")) c.Scandals.Add(TextCodec.Decode(s));
            foreach (var v in n.GetAll("vessel")) c.Vessels.Add(v);
            c.Trait = n.Get("trait", ""); c.Level = n.GetInt("level"); c.Courage = n.GetDouble("courage", 0.5); c.Stupidity = n.GetDouble("stupidity", 0.5);
            c.Badass = n.GetBool("badass"); c.Veteran = n.GetBool("veteran"); c.OnRoster = n.GetBool("onRoster"); c.Gender = n.Get("gender", "");
            c.Where = n.GetText("where"); c.VesselName = n.GetText("vesselName"); c.RosterStatus = n.Get("roster", "available");
            foreach (var l in n.GetAll("log")) c.Log.Add(TextCodec.Decode(l));
            foreach (var x in n.GetAll("note")) c.Notes.Add(TextCodec.Decode(x));
            c.MemorySummary = n.GetText("memory");
            var chat = n.GetNode("CHAT");
            if (chat != null) foreach (var mn in chat.GetNodes("MSG")) c.Chat.Add(AssistantMessage.Load(mn));
            return c;
        }
    }

    public partial class NewsEngine
    {
        public CrewFame Fame(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            CrewFame c;
            if (!State.Crew.TryGetValue(name, out c)) { c = new CrewFame { Name = name }; State.Crew[name] = c; }
            return c;
        }

        /// <summary>Extra significance for a famous crew: a legend's flight or loss is bigger news than a rookie's.</summary>
        public double FameBoost(NewsEvent e)
        {
            if (!e.Crewed) return 0;
            double top = 0;
            CrewFame c;
            foreach (var n in e.Crew) if (State.Crew.TryGetValue(n, out c)) top = Math.Max(top, c.Fame);
            bool danger = e.Type == EventTypes.Failure || e.Type == EventTypes.CrewKilled;
            return top * (danger ? 0.12 : 0.15);
        }

        /// <summary>Crew earn fame from what they fly. Called for every ingested event, after its significance is known.</summary>
        void RecordCrew(NewsEvent e)
        {
            if (!e.Crewed) return;
            foreach (var name in e.Crew)
            {
                var c = Fame(name);
                if (c.Dead) continue;
                double gain = 0;
                switch (e.Type)
                {
                    case EventTypes.Launch:
                        if (e.VesselId != "" && c.Vessels.Add(e.VesselId))
                        {
                            c.Flights++;
                            if (c.FirstFlightUT < 0) { c.FirstFlightUT = e.UT; gain += 4; }
                            c.LastFlightUT = e.UT;
                            gain += 2;
                            if (c.Flights == 5 || c.Flights == 10 || c.Flights == 25) Highlight(c, Ordinal(c.Flights) + " flight", e.UT);
                        }
                        break;
                    case EventTypes.Orbit: gain += e.Body == "Kerbin" ? 2 : 5; break;
                    case EventTypes.Soi: gain += 3; break;
                    case EventTypes.Landing: gain += e.Body == "Kerbin" ? 1 : 9; break;
                    case EventTypes.Docking: gain += 2; break;
                    case EventTypes.Splashdown:
                    case EventTypes.Recovery: gain += 1.5; break;
                    case EventTypes.Failure: gain += 3; break; // surviving trouble makes a name too
                    case EventTypes.Milestone:
                        gain += 4 + e.Significance * 0.08;
                        if (e.Detail != "") Highlight(c, e.Detail, e.UT);
                        break;
                    case EventTypes.CrewKilled:
                        break;
                }
                string when = Clock.FormatDate(e.UT) + ": ";
                string on = e.VesselName != "" ? " aboard " + e.VesselName : "";
                switch (e.Type)
                {
                    case EventTypes.Launch: if (gain > 0) LifeLog(c, when + (c.Flights == 1 ? "first flight, launched" : "launched (flight " + c.Flights + ")") + on); break;
                    case EventTypes.Orbit: if (e.Body != "Kerbin") LifeLog(c, when + "reached orbit of " + e.Body + on); else if (c.Flights <= 1) LifeLog(c, when + "first time in orbit" + on); break;
                    case EventTypes.Soi: LifeLog(c, when + "flew to " + e.Body + on); break;
                    case EventTypes.Landing: if (e.Body != "Kerbin") LifeLog(c, when + "landed on " + e.Body + on); break;
                    case EventTypes.Failure: LifeLog(c, when + "survived a failure" + on + (e.Detail != "" ? " (" + ResponseParser.Clip(e.Detail, 80) + ")" : "")); break;
                    case EventTypes.Splashdown:
                    case EventTypes.Recovery: LifeLog(c, when + "came home" + on); break;
                    case EventTypes.Milestone: if (e.Detail != "") LifeLog(c, when + e.Detail); break;
                }
                // Bigger moments make bigger names, and each point is harder to earn near the top.
                gain *= 0.6 + e.Significance / 100.0;
                gain *= 1 - c.Fame / 130.0;
                c.Fame = Math.Min(100, c.Fame + gain);
                c.PeakFame = Math.Max(c.PeakFame, c.Fame);
            }
            if (e.Type == EventTypes.CrewKilled)
            {
                foreach (var name in e.Crew)
                {
                    var c = Fame(name);
                    if (c.Dead) continue;
                    c.Dead = true;
                    c.DiedUT = e.UT;
                    c.DiedOn = e.VesselName;
                    LifeLog(c, Clock.FormatDate(e.UT) + ": died" + (e.VesselName != "" ? " aboard " + e.VesselName : ""));
                    // Everyone who ever flew with them carries it.
                    foreach (var o in State.Crew.Values.Where(o => o != c && !o.Dead && !e.Crew.Contains(o.Name) && o.Vessels.Overlaps(c.Vessels)))
                        LifeLog(o, Clock.FormatDate(e.UT) + ": lost former crewmate " + name + (e.VesselName != "" ? " on " + e.VesselName : ""));
                    // Martyrdom: the lost are remembered.
                    c.Fame = Math.Min(100, Math.Max(c.Fame, 15) + 10);
                    c.PeakFame = Math.Max(c.PeakFame, c.Fame);
                }
            }
        }

        static void Highlight(CrewFame c, string what, double ut)
        {
            if (c.Highlights.Any(h => h.StartsWith(what + " ("))) return;
            c.Highlights.Add(what + " (" + Math.Floor(ut) + ")");
            if (c.Highlights.Count > 12) c.Highlights.RemoveAt(0);
        }

        static string Ordinal(int n)
        {
            string suf = n % 100 >= 11 && n % 100 <= 13 ? "th" : n % 10 == 1 ? "st" : n % 10 == 2 ? "nd" : n % 10 == 3 ? "rd" : "th";
            return n + suf;
        }

        /// <summary>Highlights store the UT in brackets; this shows them with game dates.</summary>
        public static string HighlightText(string h, IGameClock clock)
        {
            int i = h.LastIndexOf(" (");
            double ut;
            if (i > 0 && h.EndsWith(")") && double.TryParse(h.Substring(i + 2, h.Length - i - 3), NumberStyles.Float, CultureInfo.InvariantCulture, out ut))
                return h.Substring(0, i) + " (" + clock.FormatDate(ut) + ")";
            return h;
        }

        /// <summary>Fame fades slowly between flights, but never below a share of the peak.</summary>
        void DecayFame(double days)
        {
            foreach (var c in State.Crew.Values)
            {
                if (c.Dead) continue;
                double floor = c.PeakFame * 0.45;
                if (c.Fame > floor) c.Fame = Math.Max(floor, c.Fame - days * 0.04);
            }
        }

        /// <summary>The fact-sheet line about who is on board and how well the public knows them.</summary>
        public static string CrewFameText(Story story, NewsState state, IGameClock clock)
        {
            var names = story.Events.SelectMany(e => e.Crew).Distinct().ToList();
            if (names.Count == 0) return "";
            var parts = new List<string>();
            foreach (var n in names)
            {
                CrewFame c;
                if (!state.Crew.TryGetValue(n, out c)) continue;
                var bits = new List<string> { c.Tier };
                if (c.Flights > 1) bits.Add(Ordinal(c.Flights) + " flight");
                else if (c.Flights == 1) bits.Add("first flight");
                if (c.Highlights.Count > 0) bits.Add("known for: " + string.Join("; ", c.Highlights.Skip(Math.Max(0, c.Highlights.Count - 2)).Select(h => HighlightText(h, clock)).ToArray()));
                if (c.Scandals.Count > 0) bits.Add("past gossip: " + c.Scandals.Last());
                if (c.Followers >= 100) bits.Add("fan page with about " + c.Followers.ToString("N0", CultureInfo.InvariantCulture) + " followers");
                parts.Add(n + " (" + string.Join(", ", bits.ToArray()) + ")");
            }
            if (parts.Count == 0) return "";
            return "CREW IN THE PUBLIC EYE: " + string.Join("; ", parts.ToArray()) + ". Famous crew draw bigger, more personal coverage (fans, family, career retrospectives); unknowns get introduced to the public.";
        }

        /// <summary>The most famous living kerbal, if anyone is famous enough for gossip.</summary>
        public CrewFame Celebrity(int n)
        {
            var famous = State.Crew.Values.Where(c => !c.Dead && c.Fame >= 30).OrderByDescending(c => c.Fame).ToList();
            return famous.Count == 0 ? null : famous[n % Math.Min(3, famous.Count)];
        }

        // ------------------------------------------------------------------ anniversaries

        double Year { get { return Clock.YearSeconds > 0 ? Clock.YearSeconds : Day * 426; } }

        static readonly int[] AnniversaryYears = { 1, 2, 5, 10, 15, 20, 25, 50 };

        /// <summary>
        /// Once a day: queues a retrospective when a big first turns 1, 2, 5, 10... years old, and a year-in-review
        /// when a new year starts. Each is one feature story through the normal pipeline (one AI call).
        /// </summary>
        void TickAnniversaries(double ut)
        {
            if (State.LastAnniversaryCheckUT >= 0 && ut - State.LastAnniversaryCheckUT < Day * 0.5) return;
            if (!Settings.Retrospectives) { State.LastAnniversaryCheckUT = ut; return; }
            double last = State.LastAnniversaryCheckUT < 0 ? ut : State.LastAnniversaryCheckUT;
            State.LastAnniversaryCheckUT = ut;
            if (ut < last) return; // reverted

            int year = (int)(ut / Year) + 1;
            if (State.LastReviewYear < 0) State.LastReviewYear = year - 1; // a save from before this feature: no backlog
            if (year - 1 > State.LastReviewYear)
            {
                int reviewed = year - 1;
                State.LastReviewYear = reviewed;
                QueueYearInReview(reviewed, ut);
                return;
            }

            foreach (var a in Firsts())
            {
                foreach (int k in AnniversaryYears)
                {
                    double at = a.UT + k * Year;
                    if (at > ut || at < last - Day) continue;
                    string key = a.Key + ":" + k;
                    if (!State.Anniversaries.Add(key)) continue;
                    QueueAnniversary(a, k, ut);
                    return; // one a day at most
                }
            }
        }

        class First { public string Key; public double UT; public string What; public string Headline; public string Vessel; public List<string> Crew; }

        /// <summary>The agency's big firsts: milestone events and the biggest stories, oldest first.</summary>
        List<First> Firsts()
        {
            var list = new List<First>();
            foreach (var s in State.Stories.Where(s => s.Status == StoryStatus.Published && s.Kind != "filler" && s.Kind != "tip" && s.Kind != "release"))
            {
                var m = s.Events.FirstOrDefault(e => e.Type == EventTypes.Milestone && e.Significance >= 45);
                bool huge = s.Significance >= 70 && !s.HasEvent(EventTypes.CrewKilled);
                bool loss = s.HasEvent(EventTypes.CrewKilled);
                if (m == null && !huge && !loss) continue;
                var lead = m ?? s.Events.OrderByDescending(e => e.Significance).FirstOrDefault();
                if (lead == null) continue;
                var art = State.Articles.Where(a => a.StoryId == s.Id && !a.Removed).OrderByDescending(a => a.Outlet == "herald").ThenBy(a => a.PublishedUT).FirstOrDefault();
                list.Add(new First
                {
                    Key = s.Id, UT = lead.UT, Vessel = lead.VesselName, Crew = s.Events.SelectMany(e => e.Crew).Distinct().ToList(),
                    What = loss ? "the loss of " + string.Join(", ", s.Events.Where(e => e.Type == EventTypes.CrewKilled).SelectMany(e => e.Crew).Distinct().ToArray()) + (lead.VesselName != "" ? " aboard " + lead.VesselName : "")
                        : m != null && m.Detail != "" ? m.Detail : PromptBuilder.Describe(lead),
                    Headline = art != null ? art.Headline : "",
                });
            }
            return list.OrderBy(f => f.UT).ToList();
        }

        void QueueAnniversary(First a, int years, double ut)
        {
            string when = years == 1 ? "one year" : years + " years";
            bool loss = a.What.StartsWith("the loss of");
            string topic = (loss ? "Remembrance: " : "Anniversary: ") + when + " since " + a.What + " (" + Clock.FormatDate(a.UT) + ")."
                + (a.Headline != "" ? " The press covered it at the time as \"" + a.Headline + "\"." : "")
                + (a.Crew.Count > 0 ? " Crew then: " + string.Join(", ", a.Crew.ToArray()) + "." : "")
                + (loss ? " A respectful look back at those lost and what changed since." : " A retrospective: how it happened, how it was received, what the agency has done since, and where the crew are now.");
            QueueFeature(years >= 5 ? "herald" : (State.Anniversaries.Count % 2 == 0 ? "munmonthly" : "herald"), topic, ut, 30 + Math.Min(25, years * 2), "retrospective");
        }

        void QueueYearInReview(int year, double ut)
        {
            double from = (year - 1) * Year, to = year * Year;
            var top = State.Articles.Where(a => !a.Removed && a.PublishedUT >= from && a.PublishedUT < to && a.Kind == "news")
                .GroupBy(a => a.StoryId).Select(g => g.OrderByDescending(a => a.Significance).First())
                .OrderByDescending(a => a.Significance).Take(8).ToList();
            if (top.Count == 0) return;
            var stars = State.Crew.Values.Where(c => c.FirstFlightUT >= 0 && c.FirstFlightUT < to).OrderByDescending(c => c.Fame).Take(3).Select(c => c.Name).ToList();
            var lost = State.Crew.Values.Where(c => c.Dead && c.DiedUT >= from && c.DiedUT < to).Select(c => c.Name).ToList();
            string topic = "Year in review: Year " + year + ". The biggest stories of the year, in order of importance: "
                + string.Join("; ", top.Select(a => "\"" + a.Headline + "\" (" + Clock.FormatDate(a.PublishedUT) + ")").ToArray()) + "."
                + (stars.Count > 0 ? " Best-known crew: " + string.Join(", ", stars.ToArray()) + "." : "")
                + (lost.Count > 0 ? " Lost this year: " + string.Join(", ", lost.ToArray()) + "." : "")
                + " A special end-of-year edition: the year's arc, the highs and lows, and what to watch next year.";
            QueueFeature("herald", topic, ut, 55, "review");
        }

        void QueueFeature(string outlet, string topic, double ut, double significance, string special)
        {
            State.Stories.Add(new Story
            {
                Id = State.NewId("s"), Kind = "filler", Topic = topic, FillerOutlet = outlet, CreatedUT = ut, PublishUT = ut,
                Significance = significance, Special = special,
            });
        }
    }
}
