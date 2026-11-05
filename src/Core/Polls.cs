using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KerbalNews
{
    /// <summary>A public opinion poll. Numbers come from the mood model with sampling noise; no AI call is needed.</summary>
    public class Poll
    {
        public string Id = "", Pollster = "";
        public double UT;
        public int Approve, Trust, Funding, Interest, Margin = 3;
        public string TopConcern = "";

        public void Save(DataNode n)
        {
            n.Add("id", Id); n.AddText("pollster", Pollster); n.Add("ut", UT); n.Add("approve", Approve); n.Add("trust", Trust);
            n.Add("funding", Funding); n.Add("interest", Interest); n.Add("margin", Margin); n.AddText("concern", TopConcern);
        }

        public static Poll Load(DataNode n)
        {
            return new Poll
            {
                Id = n.Get("id", ""), Pollster = n.GetText("pollster"), UT = n.GetDouble("ut"), Approve = n.GetInt("approve"), Trust = n.GetInt("trust"),
                Funding = n.GetInt("funding"), Interest = n.GetInt("interest"), Margin = n.GetInt("margin", 3), TopConcern = n.GetText("concern"),
            };
        }

        public string Summary(string agency)
        {
            return Approve + "% approve of " + agency + ", " + Funding + "% back its current funding, " + Trust + "% trust what it says"
                + (TopConcern != "" ? "; top concern: " + TopConcern : "") + " (" + Pollster + ", margin of error " + Margin + " points)";
        }
    }

    public partial class NewsEngine
    {
        public const double PollEveryDays = 6;
        public const int MaxTownHallTrust = 8;
        static readonly string[] Pollsters = { "Kerbin Opinion Research", "Planetary Council Polling Unit", "Herald/KOR survey" };

        /// <summary>A poll every few days while there is news, and the day after a big story.</summary>
        void MaybePoll(double ut)
        {
            var last = State.Polls.LastOrDefault();
            if (last != null && ut - last.UT < Day) return;
            bool due = last == null ? State.Articles.Count >= 3 : ut - last.UT >= PollEveryDays * Day && State.Articles.Any(a => a.PublishedUT > last.UT);
            bool big = State.Articles.Any(a => a.PublishedUT > (last != null ? last.UT : 0) && a.PublishedUT <= ut && a.PublishedUT > ut - Day && a.Significance >= 60);
            if (due || big) TakePoll(ut);
        }

        public Poll TakePoll(double ut)
        {
            var m = State.Mood;
            var rng = new Random(OfflineWriter.StableHash("poll" + State.Polls.Count + (int)(ut / Day)));
            Func<double, int> noisy = v => (int)Math.Round(Math.Max(3, Math.Min(97, v + (rng.NextDouble() * 2 - 1) * 2.5)));
            double budget;
            m.Concerns.TryGetValue("budget", out budget);
            var top = m.Concerns.Where(kv => kv.Value >= 10).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault() ?? "";
            var p = new Poll
            {
                Id = State.NewId("p"), UT = ut, Pollster = Pollsters[State.Polls.Count % Pollsters.Length],
                Approve = noisy(m.Trust * 0.65 + m.Excitement * 0.35),
                Trust = noisy(m.Trust),
                Funding = noisy(25 + m.Trust * 0.35 + m.Excitement * 0.25 - budget * 0.3),
                Interest = noisy(m.Excitement),
                Margin = 2 + rng.Next(3),
                TopConcern = top,
            };
            var prev = State.Polls.LastOrDefault();
            State.Polls.Add(p);
            if (State.Polls.Count > 40) State.Polls.RemoveAt(0);
            string change = prev != null ? " (" + (p.Approve - prev.Approve >= 0 ? "+" : "") + (p.Approve - prev.Approve) + ")" : "";
            State.Social.Add(new SocialPost
            {
                Handle = "@KerbinPolls", Persona = "official", UT = ut, Likes = 20 + p.Interest * 3, Reshares = 5 + p.Interest,
                Text = "NEW POLL: " + p.Approve + "% approve of " + AgencyName + change + ". Funding support " + p.Funding + "%." + (top != "" ? " Top worry: " + top + "." : "") + " #" + p.Pollster.Split(' ')[0],
            });
            return p;
        }

        // ------------------------------------------------------------ town halls

        /// <summary>
        /// A town hall: an AMA with harder questions, for after a setback. Each question the agency answers wins back a
        /// little trust and eases the top concern; leaving most of them unanswered for two days costs trust.
        /// </summary>
        public ForumThread TownHall(string title, string body, int questions, double ut)
        {
            title = (title ?? "").Trim();
            if (title == "") title = "Town hall: your questions about " + (State.Mood.LastFailureSummary != "" ? State.Mood.LastFailureSummary : "our program");
            var t = new ForumThread
            {
                Id = State.NewId("t"), Board = "ama", Title = ResponseParser.Clip(title, 200), Body = ResponseParser.Clip((body ?? "").Trim(), 4000),
                Author = AgencyName, UT = ut, Player = true, Agency = true, Score = 1, Vote = 1, Kind = "townhall",
            };
            State.Forum.Add(t);
            Queue(new CommunityJob { Target = "t:" + t.Id, Mode = "townhall", Count = Math.Max(1, Math.Min(20, questions)), UT = ut });
            return t;
        }

        /// <summary>The agency answered a town hall question: trust back, the worry eased.</summary>
        void TownHallAnswered(ForumThread t, Comment answer)
        {
            if (t == null || t.Kind != "townhall" || !answer.Agency || answer.ReplyTo == "") return;
            var q = t.Comments.FirstOrDefault(c => c.Id == answer.ReplyTo);
            if (q == null || q.Player || q.ReplyTo != "") return;
            if (t.Comments.Count(c => c.Player && c.Agency && c.ReplyTo == q.Id) > 1) return; // already answered
            int answered = t.Comments.Count(c => !c.Player && c.ReplyTo == "" && t.Comments.Any(r => r.Player && r.Agency && r.ReplyTo == c.Id));
            if (answered > MaxTownHallTrust) return;
            var m = State.Mood;
            m.Trust = Math.Min(100, m.Trust + 1);
            var top = m.Concerns.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();
            if (top != null) m.Concerns[top] = Math.Max(0, m.Concerns[top] - 3);
        }

        /// <summary>Two days after a town hall, an agency that ignored most questions loses face.</summary>
        void TickTownHalls(double ut)
        {
            foreach (var t in State.Forum.Where(x => x.Kind == "townhall" && !x.Closed && ut - x.UT > 2 * Day))
            {
                t.Closed = true;
                var qs = t.Comments.Where(c => !c.Player && c.ReplyTo == "").ToList();
                int answered = qs.Count(c => t.Comments.Any(r => r.Player && r.Agency && r.ReplyTo == c.Id));
                if (qs.Count > 0 && answered * 2 < qs.Count) State.Mood.Trust = Math.Max(0, State.Mood.Trust - 3);
            }
        }
    }
}
