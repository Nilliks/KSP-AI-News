using System;
using System.Collections.Generic;
using System.Linq;

namespace KerbalNews
{
    /// <summary>
    /// Decides how big a story is (0-100) and how much coverage that buys.
    /// Reaction size must match the event: a first Duna flyby is huge, accepting a
    /// satellite contract barely registers until the satellite actually flies.
    /// </summary>
    public static class Significance
    {
        static readonly Dictionary<string, double> BaseByType = new Dictionary<string, double>
        {
            { EventTypes.Launch, 28 },
            { EventTypes.Orbit, 32 },
            { EventTypes.Soi, 40 },
            { EventTypes.Landing, 40 },
            { EventTypes.Splashdown, 22 },
            { EventTypes.Docking, 30 },
            { EventTypes.Failure, 48 },
            { EventTypes.CrewKilled, 92 },
            { EventTypes.Recovery, 12 },
            { EventTypes.Milestone, 55 },
            { EventTypes.ContractDone, 18 },
            { EventTypes.ContractFailed, 22 },
            { EventTypes.ContractAccepted, 6 },
            { EventTypes.Livestream, 8 },
        };

        /// <summary>How far and how exotic a destination is, as felt by the public.</summary>
        public static double BodyBonus(string body)
        {
            switch (body ?? "")
            {
                case "": case "Kerbin": return 0;
                case "Mun": return 12;
                case "Minmus": return 10;
                case "Sun": case "Kerbol": return 18;
                case "Duna": case "Eve": return 24;
                case "Ike": case "Gilly": return 22;
                case "Moho": case "Dres": return 26;
                case "Jool": case "Laythe": case "Vall": case "Tylo": case "Bop": case "Pol": return 30;
                case "Eeloo": return 32;
                default: return 18; // planet packs: unknown bodies are exotic
            }
        }

        /// <summary>Not every "first" is equal: a first launch is a local story, a first orbit is history.</summary>
        public static double MilestoneBase(string detail)
        {
            var d = (detail ?? "").ToLowerInvariant();
            if (d.StartsWith("first launch")) return 16;
            if (d.Contains("reach space")) return 34;
            if (d.Contains("orbit of kerbin")) return 50;
            if (d.Contains("spacewalk") || d.Contains("rendezvous") || d.Contains("docking")) return 38;
            if (d.Contains("return from orbit of kerbin")) return 30;
            return 50;
        }

        public static string NoveltyKey(NewsEvent e)
        {
            switch (e.Type)
            {
                case EventTypes.Launch: return "launch:" + (e.Crewed ? "crewed" : "uncrewed");
                case EventTypes.Orbit: case EventTypes.Soi: case EventTypes.Landing: case EventTypes.Splashdown:
                    return e.Type + ":" + e.Body + ":" + (e.Crewed ? "crewed" : "uncrewed");
                case EventTypes.Milestone: return "milestone:" + e.Detail;
                default: return e.Type;
            }
        }

        /// <summary>Repeats of the same thing get smaller each time (Apollo 12-17 effect).</summary>
        public static double NoveltyFactor(double timesSeen)
        {
            return 1.0 / (1.0 + 0.55 * Math.Max(0, timesSeen));
        }

        public static double Score(NewsEvent e, double timesSeen)
        {
            double b;
            if (!BaseByType.TryGetValue(e.Type ?? "", out b)) b = 15;
            if (e.Type == EventTypes.Milestone) b = MilestoneBase(e.Detail);
            double score = b;
            bool danger = e.Type == EventTypes.Failure || e.Type == EventTypes.CrewKilled || e.Type == EventTypes.ContractFailed;
            if (e.Crewed && !danger) score += 12;
            if (e.Crewed && e.Type == EventTypes.Failure) score += 25; // crew in danger beats any success
            if (e.Type != EventTypes.Launch && !danger) score += BodyBonus(e.Body);
            if (e.Type == EventTypes.Splashdown || e.Type == EventTypes.Recovery)
                score += e.Crewed ? 8 : 0;

            // Danger does not get boring; successes do.
            if (!danger) score *= NoveltyFactor(timesSeen);
            else score *= Math.Max(0.75, NoveltyFactor(timesSeen * 0.3));
            if (e.Type == EventTypes.CrewKilled) score = Math.Max(score, 88);
            return Math.Max(0, Math.Min(100, score));
        }

        /// <summary>The story is as big as its biggest moment, plus a little for everything else.</summary>
        public static double StoryScore(Story s)
        {
            if (s.Kind == "filler" || s.Kind == "release") return Math.Max(10, s.Significance);
            if (s.Events.Count == 0) return 0;
            var scores = s.Events.Select(e => e.Significance).OrderByDescending(x => x).ToList();
            double total = scores[0] + Math.Min(15, 0.15 * scores.Skip(1).Sum());
            if (s.HasEvent(EventTypes.CrewKilled)) total = Math.Max(total, 90);
            return Math.Max(0, Math.Min(100, total));
        }

        /// <summary>Decay seen-counts slowly so a first can feel fresh again after a long gap.</summary>
        public static void DecayNovelty(Dictionary<string, double> novelty, double days)
        {
            if (days <= 0) return;
            double f = Math.Pow(0.99, days);
            foreach (var k in novelty.Keys.ToList()) novelty[k] *= f;
        }
    }

    public class OutletAssignment
    {
        public string OutletId;
        public string Kind;
        public int Words;
        public int Comments;
        public double DelayDays;
    }

    public class CoveragePlan
    {
        public string Tier;
        public List<OutletAssignment> Outlets = new List<OutletAssignment>();
        public int SocialPosts;
        public bool FrontPage;
        /// <summary>Whether the coverage call also writes a Snackboard thread (off when the forum is turned off).</summary>
        public bool Forum = true;

        public bool IsEmpty { get { return Outlets.Count == 0; } }

        public double EarliestDelay { get { return Outlets.Count == 0 ? 1 : Outlets.Min(o => o.DelayDays); } }

        public static CoveragePlan For(Story s)
        {
            var plan = new CoveragePlan();
            double sig = s.Significance;
            bool failure = s.HasEvent(EventTypes.Failure) || s.HasEvent(EventTypes.CrewKilled) || s.HasEvent(EventTypes.ContractFailed);

            int words, comments;
            if (sig < 15) { plan.Tier = "none"; return plan; }
            if (sig < 35) { plan.Tier = "brief"; words = 110; comments = 3; plan.SocialPosts = 3; }
            else if (sig < 60) { plan.Tier = "story"; words = 220; comments = 5; plan.SocialPosts = 6; }
            else if (sig < 80) { plan.Tier = "major"; words = 320; comments = 8; plan.SocialPosts = 9; plan.FrontPage = true; }
            else { plan.Tier = "historic"; words = 420; comments = 12; plan.SocialPosts = 14; plan.FrontPage = true; }

            foreach (var o in KerbalNews.Outlets.All)
            {
                if (s.Kind == "filler") continue; // filler uses ForFiller with one chosen outlet
                if (!KerbalNews.Outlets.IsOn(o.Id)) continue;
                double min = failure ? o.MinSignificanceFailure : o.MinSignificance;
                if (sig < min) continue;
                if (o.OnlyFor.Count > 0 && !s.Events.Any(e => o.OnlyFor.Contains(e.Type))) continue;
                // Science only weighs in on missions beyond Kerbin or true firsts.
                if (o.Id == "science" && !s.Events.Any(e => (e.Body != "" && e.Body != "Kerbin" && e.Body != "Sun" && e.Body != "Kerbol") || e.Type == EventTypes.CrewKilled)) continue;
                int w = words;
                if (o.Id == "inquirer") w = Math.Min(words, 200);
                if (o.Id == "kraken") w = Math.Min(words, 170);
                if (o.Id == "council") w = Math.Min(words, 180);
                if (o.Id == "science") w = Math.Min(words, 260);
                if (o.Id == "munmonthly") w = words + 150;
                plan.Outlets.Add(new OutletAssignment
                {
                    OutletId = o.Id, Kind = o.DefaultKind, Words = w,
                    Comments = o.IsOfficial ? Math.Max(2, comments / 2) : comments,
                    DelayDays = o.DelayDays,
                });
            }

            // Brief stories go to one outlet only, the one that suits them best.
            if (plan.Tier == "brief" && plan.Outlets.Count > 1)
            {
                string pick = failure && plan.Outlets.Any(x => x.OutletId == "inquirer") ? "inquirer"
                    : (s.HasEvent(EventTypes.Launch) || s.HasEvent(EventTypes.Docking)) && plan.Outlets.Any(x => x.OutletId == "observer") ? "observer"
                    : s.Silent && plan.Outlets.Any(x => x.OutletId == "kraken") ? "kraken"
                    : plan.Outlets[0].OutletId;
                plan.Outlets = plan.Outlets.Where(x => x.OutletId == pick).ToList();
            }
            // Keep the cost of one call bounded.
            if (plan.Outlets.Count > 8) plan.Outlets = plan.Outlets.Take(8).ToList();
            return plan;
        }

        /// <summary>Who answers a press release: the papers of record always, the tabloid and the watchdog when there's blood in the water.</summary>
        public static CoveragePlan ForRelease(Story s, string type)
        {
            double sig = s.Significance;
            var plan = new CoveragePlan { Tier = sig >= 60 ? "major" : sig >= 35 ? "story" : "brief", SocialPosts = sig >= 35 ? 6 : 3 };
            var ids = new List<string> { "herald" };
            if (sig >= 28 || type == "announcement") ids.Add("observer");
            if (type == "incident" || type == "teaser" || sig >= 40) ids.Add("inquirer");
            if (type == "teaser" || type == "incident") ids.Add("kraken");
            if (type == "incident" || sig >= 45) ids.Add("kscwatch");
            if (type == "incident" && sig >= 55) ids.Add("council");
            foreach (var id in ids)
            {
                var o = KerbalNews.Outlets.Get(id);
                if (o == null) continue;
                plan.Outlets.Add(new OutletAssignment
                {
                    OutletId = o.Id, Kind = id == "kscwatch" ? "opinion" : id == "observer" ? "analysis" : o.DefaultKind,
                    Words = id == "inquirer" || id == "kraken" ? 160 : 220, Comments = sig >= 35 ? 5 : 3, DelayDays = 1,
                });
            }
            return plan;
        }

        /// <summary>A tip from the assistant's dealings: one or more named outlets, each writing what it was told or found.</summary>
        public static CoveragePlan ForTip(Story s)
        {
            var plan = new CoveragePlan { Tier = s.Significance >= 45 ? "story" : "feature", SocialPosts = s.Significance >= 35 ? 3 : 1 };
            foreach (var id in (s.FillerOutlet ?? "").Split(','))
            {
                var o = KerbalNews.Outlets.Get(id.Trim());
                if (o == null || plan.Outlets.Any(x => x.OutletId == o.Id)) continue;
                string kind = s.TipKind != "" ? s.TipKind : o.Id == "kraken" ? "rumor" : "news";
                int words = o.Id == "kraken" ? 170 : kind == "sponsored" ? 150 : 240;
                plan.Outlets.Add(new OutletAssignment { OutletId = o.Id, Kind = kind, Words = words, Comments = kind == "sponsored" ? 3 : 5, DelayDays = 0 });
            }
            return plan;
        }

        public static CoveragePlan ForFiller(string outletId)
        {
            var o = KerbalNews.Outlets.Get(outletId) ?? KerbalNews.Outlets.All[0];
            var plan = new CoveragePlan { Tier = "feature", SocialPosts = 2 };
            plan.Outlets.Add(new OutletAssignment { OutletId = o.Id, Kind = o.Id == "kscwatch" ? "opinion" : o.Id == "kraken" ? "rumor" : "feature", Words = o.Id == "kraken" ? 160 : 230, Comments = 4, DelayDays = 0 });
            return plan;
        }
    }
}
