using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>
    /// Template coverage built from the same fact sheet. Less colorful than AI output, but free,
    /// and it keeps the mod working with no key, no internet, or a provider outage.
    /// </summary>
    public static class OfflineWriter
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static GeneratedCoverage Write(Story story, CoveragePlan plan, NewsState state, IGameClock clock)
        {
            if (story.Kind == "release") return WriteRelease(story, plan, state, clock);
            var rng = new Random(StableHash(story.Id));
            var cov = new GeneratedCoverage();
            var lead = LeadEvent(story);
            string agency = state.Profile.ShortName != "" ? state.Profile.ShortName : "KSC";
            string vessel = story.Craft != null && story.Craft.VesselName != "" ? story.Craft.VesselName : (lead != null ? lead.VesselName : "");
            bool killed = story.HasEvent(EventTypes.CrewKilled);
            bool failed = story.HasEvent(EventTypes.Failure);

            foreach (var o in plan.Outlets)
            {
                var art = new Article { Outlet = o.OutletId, Kind = o.Kind, Offline = true };
                art.Headline = Headline(story, lead, o.OutletId, agency, vessel, rng);
                art.Subhead = Subhead(story, lead, agency);
                art.Reporter = Reporter(o.OutletId, rng);
                art.Body = Body(story, lead, o.OutletId, agency, state, clock);
                art.Tags.AddRange(Tags(story));
                int n = o.Comments;
                for (int i = 0; i < n; i++)
                {
                    var c = Outlets.Commenters[rng.Next(Outlets.Commenters.Count)];
                    art.Comments.Add(new Comment { Author = c.Handle, Text = CannedComment(c.Handle, killed, failed, rng), Likes = rng.Next(0, 1 + (int)story.Significance * 3) });
                }
                cov.Articles.Add(art);
            }
            for (int i = 0; i < plan.SocialPosts; i++)
            {
                cov.Social.Add(new SocialPost
                {
                    Handle = "@" + Outlets.Commenters[rng.Next(Outlets.Commenters.Count)].Handle,
                    Persona = "public",
                    Text = CannedSocial(vessel, agency, killed, failed, rng),
                    Likes = rng.Next(1, 10 + (int)(story.Significance * story.Significance / 4)),
                    Reshares = rng.Next(0, 3 + (int)story.Significance),
                });
            }
            if (plan.Tier == "story" || plan.Tier == "major" || plan.Tier == "historic")
            {
                var who = Outlets.Commenters[rng.Next(Outlets.Commenters.Count)];
                var t = new ForumThread
                {
                    Board = failed || killed ? "general" : "launches",
                    Title = (killed ? "Thread for " + vessel + ": thinking of the crew" : failed ? "What went wrong with " + vessel + "?" : vessel + " discussion thread"),
                    Body = cov.Articles.Count > 0 ? cov.Articles[0].Subhead : "",
                    Author = who.Handle, Score = rng.Next(5, 20 + (int)story.Significance * 4),
                };
                int replies = plan.Tier == "story" ? 3 : 6;
                for (int i = 0; i < replies; i++)
                {
                    var c = Outlets.Commenters[rng.Next(Outlets.Commenters.Count)];
                    t.Comments.Add(new Comment { Author = c.Handle, Text = CannedComment(c.Handle, killed, failed, rng), Likes = rng.Next(1, 10 + (int)story.Significance), ReplyTo = i > 0 && rng.Next(3) == 0 ? "#" + rng.Next(i) : "" });
                }
                cov.Forum = t;
            }
            return cov;
        }

        static GeneratedCoverage WriteRelease(Story story, CoveragePlan plan, NewsState state, IGameClock clock)
        {
            var rng = new Random(StableHash(story.Id));
            var cov = new GeneratedCoverage();
            var rel = state.Releases.FirstOrDefault(r => r.Id == story.ReleaseId) ?? new PressRelease { Title = story.Topic };
            string agency = state.Profile.ShortName != "" ? state.Profile.ShortName : "KSC";
            foreach (var o in plan.Outlets)
            {
                var art = new Article { Outlet = o.OutletId, Kind = o.Kind, Offline = true, Reporter = Reporter(o.OutletId, rng) };
                switch (o.OutletId)
                {
                    case "kraken": art.Headline = "LEAKED: what " + agency + "'s \"" + rel.Title + "\" REALLY means (it's worse than you think)"; break;
                    case "inquirer": art.Headline = rel.Type == "incident" ? agency + " SPEAKS OUT: \"" + rel.Title + "\"" : "What is " + agency + " up to now?"; break;
                    case "kscwatch": art.Headline = "Reading between the lines of " + agency + "'s latest statement"; break;
                    case "observer": art.Headline = "Analysis: what " + agency + "'s announcement means for the program"; break;
                    default: art.Headline = agency + ": " + rel.Title; break;
                }
                art.Subhead = rel.Type == "incident" ? "The agency issued a statement on the incident." : "The agency issued a " + (rel.Type == "update" ? "mission update" : rel.Type) + ".";
                art.Body = agency + " released a statement on " + clock.FormatDate(rel.UT) + " titled \"" + rel.Title + "\".\n\n"
                    + (rel.Body != "" ? "\"" + (rel.Body.Length > 600 ? rel.Body.Substring(0, 600) + "..." : rel.Body) + "\"\n\n" : "")
                    + (o.OutletId == "kscwatch" ? "As always, the statement leaves questions unanswered. We will keep asking them." : "Further details are expected in the coming days. " + state.Mood.Describe());
                art.Tags.Add("press release");
                for (int i = 0; i < o.Comments; i++)
                {
                    var c = Outlets.Commenters[rng.Next(Outlets.Commenters.Count)];
                    art.Comments.Add(new Comment { Author = c.Handle, Text = Pick(rng, "Nice to hear it from them directly.", "Sounds like PR to me.", "I'll believe it when I see it.", "Good. More of this, please.", "What aren't they saying?"), Likes = rng.Next(1, 30) });
                }
                cov.Articles.Add(art);
            }
            for (int i = 0; i < plan.SocialPosts; i++)
                cov.Social.Add(new SocialPost { Handle = "@" + Outlets.Commenters[rng.Next(Outlets.Commenters.Count)].Handle, Persona = "public", Text = Pick(rng, agency + " just put out a statement: " + rel.Title, "Read the " + agency + " release. Thoughts?", "Another " + agency + " press release, another round of questions."), Likes = rng.Next(1, 60), Reshares = rng.Next(0, 10) });
            return cov;
        }

        /// <summary>Template replies to the player's post when no AI is available.</summary>
        public static List<Comment> CommunityReplies(NewsEngine eng, CommunityJob job)
        {
            var rng = new Random(StableHash(job.Target + job.CommentId + job.UT.ToString("R", Inv)));
            var list = eng.CommentsOf(job.Target) ?? new List<Comment>();
            var mine = list.FirstOrDefault(c => c.Id == job.CommentId);
            var thread = eng.ThreadOf(job.Target);
            bool agency = mine != null ? mine.Agency : thread != null && thread.Agency;
            var outList = new List<Comment>();
            for (int i = 0; i < job.Count; i++)
            {
                var c = Outlets.Commenters[((StableHash(job.Target) & 0x7fffffff) + i * 3) % Outlets.Commenters.Count];
                string text;
                if (job.Target.StartsWith("g:") && job.CommentId == "")
                    text = Pick(rng, "Wallpaper material.", "Is that real or a render?", "Look at that view!", "Zoom and enhance, what's that on the left?", "Framing this one.", "How much did this photo cost us?");
                else if (job.Mode == "ama")
                    text = Pick(rng, "What is the next big mission?", "How do you decide who flies?", "What does a launch actually cost?", "Is it true the cafeteria runs out of snacks before every launch?",
                        "How do you keep the crews safe after what happened last time?", "When are we going to Duna?", "What happens to all the debris?", "Do you ever get scared watching a launch?");
                else if (agency)
                    text = Pick(rng, "Appreciate the official response. Any numbers you can share?", "That reads like it came straight from the press office.", "Thanks for engaging with us here, that's rare.",
                        "So what are you NOT telling us?", "Good to hear it from you directly.", "Can you confirm the crew is safe?");
                else
                    text = Pick(rng, "Interesting take.", "Source?", "I was thinking the same thing.", "Hard disagree, but fair point.", "You sound like you work at KSC.", "This. Exactly this.");
                outList.Add(new Comment { Author = c.Handle, Text = text, Likes = rng.Next(1, agency ? 40 : 12), ReplyTo = job.Mode == "ama" ? "" : job.CommentId });
            }
            return outList;
        }

        public static NewsEvent LeadEvent(Story s)
        {
            return s.Events.OrderByDescending(e => e.Type == EventTypes.CrewKilled ? 1000 : e.Type == EventTypes.Failure ? 500
                : e.Type == EventTypes.Milestone ? e.Significance - 10 : e.Significance).FirstOrDefault();
        }

        static string Pick(Random r, params string[] options) { return options[r.Next(options.Length)]; }

        static string Headline(Story s, NewsEvent e, string outlet, string agency, string vessel, Random r)
        {
            if (s.Kind == "filler" && outlet == "kraken") return Pick(r, "Is " + agency + " hiding a BASE on the Mun? Our insider says YES", "The Kraken is REAL, and " + agency + " knows it", "Cafeteria insider: the kerbonaut FEUD nobody is talking about");
            if (s.Kind == "filler") return Pick(r, "A quiet week at " + agency + ": what comes next?", "Inside " + agency + " between missions", "Opinion: where should " + agency + " go from here?");
            if (e == null) return agency + " news update";
            string body = e.Body;
            string crew = string.Join(", ", e.Crew.ToArray());
            string what = WhatHappened(e, agency, vessel);
            switch (outlet)
            {
                case "kraken":
                    if (s.Silent) return Pick(r, "SECRET ROCKET: what " + agency + " is HIDING from you", "They launched WHAT? Insiders spill on " + agency + "'s mystery craft");
                    if (e.Type == EventTypes.Failure) return Pick(r, "Was it the KRAKEN? The " + vessel + " theory " + agency + " won't talk about", "SABOTAGE? 7 clues in the " + vessel + " disaster");
                    return Pick(r, "You won't BELIEVE what really happened when " + what.Substring(0, 1).ToLowerInvariant() + what.Substring(1), what + "... or did it? Our insider says no");
                case "inquirer":
                    if (e.Type == EventTypes.CrewKilled) return "TRAGEDY: " + crew + " lost aboard " + vessel;
                    if (e.Type == EventTypes.Failure) return Pick(r, "KABOOM! " + vessel + " goes up in smoke", "Another " + agency + " rocket, another fireball");
                    return Pick(r, "WOW! ", "THEY DID IT! ", "") + what.ToUpperInvariant().Substring(0, 1) + what.Substring(1) + "!";
                case "observer":
                    if (!s.Silent && s.Craft != null && s.Craft.Parts > 0) return what + ": a " + Math.Round(s.Craft.MassTons) + "-tonne, " + s.Craft.Stages + "-stage design under the microscope";
                    return "Analysis: " + what;
                case "kscwatch":
                    if (e.Type == EventTypes.CrewKilled || e.Type == EventTypes.Failure) return "Questions " + agency + " must answer after the " + vessel + " loss";
                    return "Our take: " + what + ". Was it worth it?";
                case "munmonthly":
                    return e.Crewed && crew != "" ? "The kerbals behind the moment: " + crew : "Why " + what.ToLowerInvariant() + " matters";
                case "science":
                    return "Researchers weigh in: what " + vessel + " means for science" + (body != "" && body != "Kerbin" ? " at " + body : "");
                case "council":
                    if (e.Type == EventTypes.CrewKilled) return "Statement from the Planetary Council on the loss of " + crew;
                    return "Planetary Council congratulates " + agency;
                default:
                    if (e.Type == EventTypes.CrewKilled) return agency + " mourns " + crew + " after " + vessel + " disaster";
                    return what;
            }
        }

        static string WhatHappened(NewsEvent e, string agency, string vessel)
        {
            string body = e.Body;
            switch (e.Type)
            {
                case EventTypes.CrewKilled: return string.Join(", ", e.Crew.ToArray()) + " killed aboard " + vessel;
                case EventTypes.Failure: return vessel + " lost" + (body != "" && body != "Kerbin" ? " near " + body : "");
                case EventTypes.Launch: return agency + " launches " + vessel;
                case EventTypes.Orbit: return vessel + " reaches orbit around " + body;
                case EventTypes.Soi: return vessel + " arrives at " + body;
                case EventTypes.Landing: return (e.Crewed ? "Crew lands on " : vessel + " touches down on ") + body;
                case EventTypes.Splashdown: return vessel + " splashes down" + (e.Crewed ? ", crew safe" : "");
                case EventTypes.Docking: return vessel + " docks in orbit";
                case EventTypes.Milestone: return agency + " celebrates its " + e.Detail + (e.VesselName != "" ? " with " + e.VesselName : "");
                case EventTypes.ContractDone: return agency + " completes contract: " + e.Detail;
                case EventTypes.ContractFailed: return agency + " misses contract deadline: " + e.Detail;
                default: return agency + " update: " + vessel;
            }
        }

        static string Subhead(Story s, NewsEvent e, string agency)
        {
            if (s.Kind == "filler") return "With no launches in recent days, a look at the state of the program.";
            if (e == null) return "";
            if (e.Type == EventTypes.CrewKilled) return "The agency has not yet said what went wrong.";
            if (e.Type == EventTypes.Failure) return e.Crewed ? "The crew's status is the first question on everyone's mind." : "No crew were aboard.";
            return e.Crewed ? "A crewed flight for " + agency + "." : "An uncrewed flight for " + agency + ".";
        }

        static readonly string[] FirstNames = { "Bobak", "Lisdun", "Maxzor", "Gregrim", "Tedrin", "Meloise", "Haldin", "Corbur", "Sidmund", "Valdred", "Jeneny", "Aldbert" };

        static string Reporter(string outlet, Random r) { return FirstNames[r.Next(FirstNames.Length)] + " Kerman"; }

        static string Body(Story s, NewsEvent lead, string outlet, string agency, NewsState state, IGameClock clock)
        {
            var sb = new StringBuilder();
            if (s.Kind == "filler")
            {
                sb.Append("It has been a quiet stretch for " + agency + ". " + s.Topic + "\n\n");
                var d = state.DigestHistory.LastOrDefault();
                if (d != null)
                    sb.AppendFormat(Inv, "By the latest public count the agency has {0} satellites and {1} stations in operation, with {2} tracked pieces of debris and {3} kerbonauts currently in space.", d.Satellites, d.Stations, d.Debris, d.CrewInSpace);
                return sb.ToString();
            }
            foreach (var e in s.Events.OrderBy(x => x.UT))
                sb.Append(clock.FormatDate(e.UT) + ": " + (s.Silent ? PromptBuilder.DescribeSilent(e) : PromptBuilder.Describe(e)) + " ");
            sb.Append("\n\n");
            if (s.Silent)
            {
                if (s.Craft != null) sb.Append(PromptBuilder.VisibleCraftText(s.Craft) + " ");
                sb.Append(agency + " has released no telemetry or specifications for this flight.\n\n");
            }
            else if (s.Craft != null)
                sb.Append(PromptBuilder.CraftText(s.Craft) + "\n\n");
            if (outlet == "observer" && !s.Silent && s.Craft != null && s.Craft.Parts > 0)
                sb.AppendFormat(Inv, "At {0:0.#} tonnes and {1} parts, the vehicle is {2} by the standards of the program so far.\n\n",
                    s.Craft.MassTons, s.Craft.Parts, s.Craft.MassTons > 150 ? "a heavy lifter" : s.Craft.MassTons > 40 ? "a mid-size launcher" : "a light vehicle");
            if (lead != null && lead.Type == EventTypes.CrewKilled)
                sb.Append("Flags at the space center flew at half staff. Members of the public left flowers at the main gate.");
            else if (lead != null && lead.Type == EventTypes.Failure)
                sb.Append("Engineers are expected to review the telemetry. " + state.Mood.Describe());
            else
                sb.Append(agency + " officials called the flight a success. " + state.Mood.Describe());
            return sb.ToString();
        }

        static IEnumerable<string> Tags(Story s)
        {
            if (s.Kind == "filler") { yield return "feature"; yield break; }
            if (s.HasEvent(EventTypes.CrewKilled)) yield return "crew loss";
            if (s.HasEvent(EventTypes.Failure)) yield return "failure";
            foreach (var t in s.Events.Select(e => e.Type).Distinct()) if (t != EventTypes.Failure && t != EventTypes.CrewKilled) yield return t;
            foreach (var b in s.Events.Select(e => e.Body).Where(b => b != "").Distinct()) yield return b.ToLowerInvariant();
        }

        static string CannedComment(string handle, bool killed, bool failed, Random r)
        {
            if (killed) return Pick(r, "Rest easy. Fly safe up there.", "Thoughts with the families tonight.", "There needs to be a full inquiry into this.", "Heartbreaking. They knew the risks and went anyway.");
            if (failed) return Pick(r, "Back to the drawing board, I suppose.", "How much did that cost us?", "Test flights fail. That's what they're for.", "This is the part they never show on the posters.");
            return Pick(r, "Great to see!", "What a time to be alive.", "Was that worth the money? Genuinely asking.", "Saw it from my back garden!", "Another one for the history books.", "Jeb would be proud.");
        }

        static string CannedSocial(string vessel, string agency, bool killed, bool failed, Random r)
        {
            if (killed) return Pick(r, "Can't stop thinking about the " + vessel + " crew. #FlySafe", "Somebody at " + agency + " needs to answer for this.");
            if (failed) return Pick(r, "Watched " + vessel + " come apart from the beach. Still processing.", "rapid unplanned disassembly is a funny way to say 'it blew up'");
            return Pick(r, vessel + " looked incredible today!", "Proud of " + agency + " today.", "ok but who's paying for all these rockets", "Took the kids to watch " + vessel + ". They won't stop talking about it.");
        }

        public static int StableHash(string s)
        {
            unchecked
            {
                int h = 23;
                foreach (char c in s ?? "") h = h * 31 + c;
                return h;
            }
        }
    }
}
