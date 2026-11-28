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
            if (story.Kind == "tip") return WriteTip(story, plan, state);
            var rng = new Random(StableHash(story.Id));
            var cov = new GeneratedCoverage();
            var lead = LeadEvent(story);
            string agency = state.Profile.ShortName != "" ? state.Profile.ShortName : "KSC";
            string vessel = story.Craft != null && story.Craft.VesselName != "" ? story.Craft.VesselName : (lead != null ? lead.VesselName : "");
            bool killed = story.HasEvent(EventTypes.CrewKilled);
            bool failed = story.HasEvent(EventTypes.Failure);

            var v = Vars(story, lead, agency, vessel, state, clock);
            foreach (var o in plan.Outlets)
            {
                var art = new Article { Outlet = o.OutletId, Kind = o.Kind, Offline = true };
                art.Headline = Headline(story, lead, o.OutletId, v, rng);
                art.Subhead = Subhead(story, lead, v, rng);
                art.Reporter = Reporter(o.OutletId, rng);
                art.Body = Body(story, lead, o.OutletId, agency, state, clock, v, rng);
                art.Tags.AddRange(Tags(story));
                int n = o.Comments;
                var seen = new HashSet<string>();
                for (int i = 0; i < n; i++)
                {
                    var c = Outlets.Commenters[rng.Next(Outlets.Commenters.Count)];
                    string text = CannedComment(c.Handle, killed, failed, rng);
                    for (int k = 0; k < 12 && seen.Contains(CommentKey(text)); k++) text = CannedComment(c.Handle, killed, failed, rng);
                    if (seen.Contains(CommentKey(text))) continue;
                    seen.Add(CommentKey(text));
                    art.Comments.Add(new Comment { Author = c.Handle, Text = text, Likes = rng.Next(0, 1 + (int)story.Significance * 3) });
                }
                cov.Articles.Add(art);
            }
            // A few distinct posts rather than many copies.
            var socialLines = CannedSocial(v, killed, failed, rng, Math.Min(plan.SocialPosts, 8));
            var socialHandles = Outlets.Commenters.Select(c => c.Handle).OrderBy(x => rng.Next()).ToList();
            for (int i = 0; i < socialLines.Count; i++)
            {
                cov.Social.Add(new SocialPost
                {
                    Handle = "@" + socialHandles[i % socialHandles.Count],
                    Persona = "public",
                    Text = socialLines[i],
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
                    Title = Phrases.Fresh(killed ? "f_title_loss" : failed ? "f_title_fail" : "f_title_good", v, rng),
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
                    art.Comments.Add(new Comment { Author = c.Handle, Text = Phrases.Rule("c_release", null, rng), Likes = rng.Next(1, 30) });
                }
                cov.Articles.Add(art);
            }
            for (int i = 0; i < plan.SocialPosts; i++)
                cov.Social.Add(new SocialPost { Handle = "@" + Outlets.Commenters[rng.Next(Outlets.Commenters.Count)].Handle, Persona = "public", Text = Pick(rng, agency + " just put out a statement: " + rel.Title, "Read the " + agency + " release. Thoughts?", "Another " + agency + " press release, another round of questions."), Likes = rng.Next(1, 60), Reshares = rng.Next(0, 10) });
            return cov;
        }

        /// <summary>Template replies to the player's post when no AI is available.</summary>

        /// <summary>Livestream chat without AI: reactions to the telemetry, moments and anything the player wrote.</summary>
        public static List<ChatMessage> StreamChat(NewsEngine eng, StreamSession st, List<string> moments, List<ChatMessage> said)
        {
            var rng = new Random(StableHash(st.VesselId + st.Batches + "|" + st.Chat.Count));
            var list = new List<ChatMessage>();
            foreach (var m in said)
                list.Add(new ChatMessage { Author = StreamChatStyle.Handle(rng), Text = m.Agency
                    ? Pick(rng, "the official account is here!", "hi " + eng.AgencyName + "! how's the crew?", "can you share the delta-v?", "thanks for streaming this", "OFFICIAL ACCOUNT SPOTTED", "admin pls say hi to jeb", "hire me")
                    : "@" + m.Author + " " + Pick(rng, "true", "lol", "source?", "agreed", "you sound like you work there", "this guy gets it", "no", "facts", "ratio") });
            var t = st.Last;
            int n = 10 + rng.Next(5);
            list.AddRange(StreamChatStyle.Lines(st, moments, n, rng, st.VesselName ?? ""));
            if (t != null && rng.Next(3) == 0)
                list.Insert(rng.Next(list.Count + 1), new ChatMessage { Author = StreamChatStyle.Handle(rng), Text = Pick(rng, TelemetrySample.Km(t.Altitude) + " already", t.Speed.ToString("N0") + " m/s, wow", t.FuelPct >= 0 && t.FuelPct < 15 ? "fuel is getting low..." : "how much fuel is left?") });
            return list.Take(24).ToList();
        }

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
                    text = Phrases.Rule("r_photo", null, rng);
                else text = Phrases.Rule(job.Mode == "townhall" ? "r_townhall" : job.Mode == "ama" ? "r_ama" : agency ? "r_agency" : "r_public", null, rng);
                for (int k = 0; k < 4 && outList.Any(x => x.Text == text); k++) text = Phrases.Rule(job.Mode == "townhall" ? "r_townhall" : job.Mode == "ama" ? "r_ama" : agency ? "r_agency" : "r_public", null, rng);
                outList.Add(new Comment { Author = c.Handle, Text = text, Likes = rng.Next(1, agency ? 40 : 12), ReplyTo = job.Mode == "ama" || job.Mode == "townhall" ? "" : job.CommentId });
            }
            return outList;
        }

        public static NewsEvent LeadEvent(Story s)
        {
            return s.Events.OrderByDescending(e => e.Type == EventTypes.CrewKilled ? 1000 : e.Type == EventTypes.Failure ? 500
                : e.Type == EventTypes.Milestone ? e.Significance - 10 : e.Significance).FirstOrDefault();
        }

        static GeneratedCoverage WriteTip(Story story, CoveragePlan plan, NewsState state)
        {
            var rng = new Random(StableHash(story.Id));
            var cov = new GeneratedCoverage();
            foreach (var o in plan.Outlets)
            {
                string head = story.TipHeadline != "" ? story.TipHeadline : "A development at " + (state.Profile.ShortName != "" ? state.Profile.ShortName : "KSC");
                if (o.OutletId == "kraken") head = head.ToUpperInvariant() + "!";
                var art = new Article { Outlet = o.OutletId, Kind = o.Kind, Offline = true, Headline = head, Reporter = Reporter(o.OutletId, rng) };
                art.Subhead = o.Kind == "sponsored" ? "Sponsored by " + state.Profile.Name : "";
                art.Body = story.TipBody != "" ? story.TipBody : story.Topic;
                for (int i = 0; i < o.Comments; i++)
                {
                    var c = Outlets.Commenters[rng.Next(Outlets.Commenters.Count)];
                    art.Comments.Add(new Comment { Author = c.Handle, Text = Phrases.Rule("c_tip", null, rng), Likes = rng.Next(0, 30) });
                }
                cov.Articles.Add(art);
            }
            return cov;
        }

        static string Pick(Random r, params string[] options) { return options[r.Next(options.Length)]; }

        /// <summary>The facts a template may use, by name. Empty ones make the options that need them drop out.</summary>
        static Dictionary<string, string> Vars(Story s, NewsEvent e, string agency, string vessel, NewsState state, IGameClock clock)
        {
            var v = new Dictionary<string, string>();
            v["agency"] = agency;
            v["vessel"] = vessel;
            string what = e != null ? WhatHappened(e, agency, vessel != "" ? vessel : "the craft") : agency + " news update";
            v["what"] = what;
            v["what_l"] = what.Length > 1 ? char.ToLowerInvariant(what[0]) + what.Substring(1) : what;
            v["what_up"] = what.ToUpperInvariant();
            string body = e != null && e.Body != "" && e.Body != "Kerbin" ? e.Body : "";
            v["body"] = body;
            var crew = e != null ? e.Crew : new List<string>();
            v["crew"] = string.Join(", ", crew.ToArray());
            v["crew_first"] = crew.Count > 0 ? crew[0].Split(' ')[0] : "";
            v["date"] = e != null ? clock.FormatDate(e.UT) : "";
            v["mood"] = state.Mood.Describe();
            if (!s.Silent && s.Craft != null && s.Craft.Parts > 0)
            {
                v["mass"] = Math.Round(s.Craft.MassTons).ToString(Inv);
                v["parts"] = s.Craft.Parts.ToString(Inv);
                v["stages"] = s.Craft.Stages.ToString(Inv);
                if (s.Craft.DeltaV > 0) v["dv"] = Math.Round(s.Craft.DeltaV).ToString("#,0", Inv);
                v["size"] = s.Craft.MassTons > 150 ? "a heavy lifter" : s.Craft.MassTons > 40 ? "a mid-size launcher" : "a light vehicle";
            }
            return v;
        }

        static string Headline(Story s, NewsEvent e, string outlet, Dictionary<string, string> v, Random r)
        {
            if (s.Kind == "filler") return Phrases.Fresh(outlet == "kraken" ? "hl_filler_kraken" : "hl_filler", v, r);
            if (e == null) return v["agency"] + " news update";
            // Mining news brings its own headline, built from the exchange's numbers.
            if (e.Type == EventTypes.Mining && e.Title != "") return outlet == "kraken" ? e.Title.ToUpperInvariant() + "!" : e.Title;
            bool loss = e.Type == EventTypes.CrewKilled, fail = loss || e.Type == EventTypes.Failure;
            string rule;
            switch (outlet)
            {
                case "kraken": rule = s.Silent ? "hl_kraken_secret" : fail ? "hl_kraken_fail" : "hl_kraken"; break;
                case "inquirer": rule = loss ? "hl_loss" : fail ? "hl_inquirer_fail" : "hl_inquirer"; break;
                case "observer": rule = loss ? "hl_loss" : fail ? "hl_fail" : "hl_observer"; break;
                case "kscwatch": rule = fail ? "hl_kscwatch_fail" : "hl_kscwatch"; break;
                case "munmonthly": rule = loss ? "hl_loss" : "hl_munmonthly"; break;
                case "science": rule = loss ? "hl_loss" : "hl_science"; break;
                case "council": rule = loss ? "hl_council_loss" : fail ? "hl_fail" : "hl_council"; break;
                default: rule = loss ? "hl_loss" : fail ? "hl_fail" : "hl_good"; break;
            }
            string h = Phrases.Fresh(rule, v, r);
            if (h == "") h = Phrases.Fresh(loss ? "hl_loss" : fail ? "hl_fail" : "hl_good", v, r);
            return Phrases.Cap(h != "" ? h : v["what"]);
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
                case EventTypes.Mining: return e.Title != "" ? e.Title : agency + " mining update";
                default: return agency + " update: " + vessel;
            }
        }

        static string Subhead(Story s, NewsEvent e, Dictionary<string, string> v, Random r)
        {
            if (s.Kind == "filler") return Phrases.Rule("filler_mid", v, r);
            if (e == null) return "";
            if (e.Type == EventTypes.Mining) { int dot = e.Detail.IndexOf(". ", StringComparison.Ordinal); return dot > 0 ? e.Detail.Substring(0, dot + 1) : e.Detail; }
            if (e.Type == EventTypes.CrewKilled) return Phrases.Rule("sub_loss", v, r);
            if (e.Type == EventTypes.Failure) return e.Crewed ? "The crew's status is the first question on everyone's mind." : Phrases.Rule("sub_fail", v, r).Replace("The crew's status is the first question on everyone's mind.", "No crew were aboard.");
            var sv = new Dictionary<string, string>(v);
            if (!e.Crewed) sv["crew"] = "";
            string sub = Phrases.Rule("sub_good", sv, r);
            if (sub.StartsWith("A crewed") && !e.Crewed) sub = "An uncrewed flight for " + v["agency"] + ".";
            if (sub.StartsWith("An uncrewed") && e.Crewed) sub = "A crewed flight for " + v["agency"] + ".";
            return sub;
        }

        static readonly string[] FirstNames = { "Bobak", "Lisdun", "Maxzor", "Gregrim", "Tedrin", "Meloise", "Haldin", "Corbur", "Sidmund", "Valdred", "Jeneny", "Aldbert" };

        static string Reporter(string outlet, Random r) { return FirstNames[r.Next(FirstNames.Length)] + " Kerman"; }

        static string Body(Story s, NewsEvent lead, string outlet, string agency, NewsState state, IGameClock clock, Dictionary<string, string> v, Random r)
        {
            var sb = new StringBuilder();
            if (s.Kind == "filler")
            {
                sb.Append(Phrases.Rule("filler_open", v, r) + " " + s.Topic + "\n\n");
                sb.Append(Phrases.Rule("filler_mid", v, r) + "\n\n");
                var d = state.DigestHistory.LastOrDefault();
                if (d != null)
                    sb.AppendFormat(Inv, "By the latest public count the agency has {0} satellites and {1} stations in operation, with {2} tracked pieces of debris and {3} kerbonauts currently in space.", d.Satellites, d.Stations, d.Debris, d.CrewInSpace);
                return sb.ToString().Trim();
            }
            if (lead != null && lead.Type == EventTypes.Mining)
            {
                // A business story, not a flight story: the facts, then what the market makes of it.
                foreach (var e in s.Events.OrderBy(x => x.UT))
                    sb.Append(e.Type == EventTypes.Mining ? e.Detail + "\n\n" : clock.FormatDate(e.UT) + ": " + (s.Silent ? PromptBuilder.DescribeSilent(e) : PromptBuilder.Describe(e)) + "\n\n");
                sb.Append(Phrases.Rule("w_market", v, r));
                return sb.ToString().Trim();
            }
            bool loss = lead != null && lead.Type == EventTypes.CrewKilled, fail = loss || lead != null && lead.Type == EventTypes.Failure;
            string open = Phrases.Rule(loss ? "open_loss" : fail ? "open_fail" : "open_good", v, r);
            if (open != "") sb.Append(Phrases.Cap(open) + (loss ? "" : " " + Phrases.Cap(Phrases.Rule(fail ? "quote_fail" : "quote_good", v, r))) + "\n\n");
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
            string angle = outlet == "observer" || outlet == "kscwatch" || outlet == "inquirer" || outlet == "kraken" || outlet == "munmonthly" || outlet == "science" ? Phrases.Rule("angle_" + outlet, v, r) : "";
            if (angle != "" && !(fail && (outlet == "inquirer" || outlet == "kraken"))) sb.Append(angle + "\n\n");
            string close = Phrases.Rule(loss ? "close_loss" : fail ? "close_fail" : "close_good", v, r);
            sb.Append(close);
            if (!close.Contains(v["mood"]) && !loss) sb.Append(" " + v["mood"]);
            return sb.ToString().Trim();
        }

        static IEnumerable<string> Tags(Story s)
        {
            if (s.Kind == "filler") { yield return "feature"; yield break; }
            if (s.HasEvent(EventTypes.CrewKilled)) yield return "crew loss";
            if (s.HasEvent(EventTypes.Failure)) yield return "failure";
            foreach (var t in s.Events.Select(e => e.Type).Distinct()) if (t != EventTypes.Failure && t != EventTypes.CrewKilled) yield return t;
            foreach (var b in s.Events.Select(e => e.Body).Where(b => b != "").Distinct()) yield return b.ToLowerInvariant();
        }

        /// <summary>Two comments count as the same when they end the same way, whatever opener they start with.</summary>
        static string CommentKey(string t)
        {
            t = t ?? "";
            foreach (var rule in new[] { "c_good_core", "c_fail_core", "c_loss" })
                foreach (var core in Phrases.Options(rule)) if (t.Contains(core)) return core;
            return t;
        }

        static string CannedComment(string handle, bool killed, bool failed, Random r)
        {
            return Phrases.Rule(killed ? "c_loss" : failed ? "c_fail" : "c_good", null, r);
        }

        static List<string> CannedSocial(Dictionary<string, string> v, bool killed, bool failed, Random r, int n)
        {
            var list = new List<string>();
            for (int k = 0; k < n * 4 && list.Count < n; k++)
            {
                string line = Phrases.Rule(killed ? "s_loss" : failed ? "s_fail" : "s_good", v, r);
                if (line != "" && !list.Contains(line)) list.Add(line);
            }
            return list;
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

        /// <summary>A plain KTV segment from an article, when the writer gave none (offline, or an older reply).</summary>
        public static TvSegment TvFrom(Article a)
        {
            string lead = a.Body.Split(new[] { "\n\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).FirstOrDefault(x => x != "") ?? a.Subhead;
            string head = a.Headline.Length > 70 ? a.Headline.Substring(0, 67) + "..." : a.Headline;
            return new TvSegment { Headline = head, Script = "Good evening. " + (lead.Length > 400 ? lead.Substring(0, 397) + "..." : lead), Ticker = (Outlets.NameOf(a.Outlet) + ": " + head) };
        }
    }
}
