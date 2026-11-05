using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>Game facts the engine needs from the host, so the core never touches KSP directly.</summary>
    public interface IGameClock
    {
        double DaySeconds { get; }
        string FormatDate(double ut);
    }

    public class SimpleClock : IGameClock
    {
        public double DaySeconds { get; set; }
        public SimpleClock(double daySeconds = 21600) { DaySeconds = daySeconds; }
        public string FormatDate(double ut)
        {
            double year = DaySeconds * 426;
            int y = (int)(ut / year) + 1;
            int d = (int)((ut % year) / DaySeconds) + 1;
            return "Year " + y + ", Day " + d;
        }
    }

    public static class PromptBuilder
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>
        /// The system prompt holds everything that rarely changes within a save (rules, outlets,
        /// commenters, agency canon), so providers with prompt caching can reuse it.
        /// </summary>
        public static string System(NewsState state)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are the newsroom of the Kerbin universe: you write in-universe news coverage, comment sections and social media posts about a kerbal space agency.");
            sb.AppendLine();
            sb.AppendLine("HARD RULES");
            sb.AppendLine("- Write entirely in-universe. Never mention video games, players, save files, quicksaves, reverts, mods, or Earth, humans, NASA or real countries.");
            sb.AppendLine("- Report only what is in the FACT SHEET. Use only numbers from the fact sheet, rounded naturally. Never invent missions, launches, deaths, rescues, or results that are not listed.");
            sb.AppendLine("- You may invent small color: reporter names, quotes from members of the public, a bakery near KSC, a professor's reaction. Do not invent new facts about the agency's history.");
            sb.AppendLine("- Vessel and kerbal names must be spelled exactly as given.");
            sb.AppendLine("- Match the size of the reaction to the significance score in the assignment. A routine event gets a short, calm brief and a handful of mild comments. A historic first gets awe, crowded comments, and heated debate. A loss of crew is somber.");
            sb.AppendLine("- Loss of crew is always handled with respect: no jokes from mainstream outlets, focus on the individuals, calls for accountability. Tabloids may question safety but not mock the dead.");
            sb.AppendLine("- Public reaction should be realistic and mixed: enthusiasts and skeptics, people excited and upset at once, people who forget past failures and people who never do.");
            sb.AppendLine("- Follow real press patterns: repeats of a first get less attention; crews in danger beat any success for attention; a failure is reported through the memory of the agency's last similar failure; tabloids and commenters mock euphemisms such as 'rapid unplanned disassembly'; trade press judges engineering.");
            sb.AppendLine("- Kerbin calendar: dates are given as 'Year Y, Day D'. Refer to dates that way, never in Earth months.");
            sb.AppendLine();
            sb.AppendLine("TONE");
            sb.AppendLine(SillinessText(state == null ? 0 : state.Silliness));
            sb.AppendLine();
            sb.AppendLine("OUTLETS (id: name. style. bias.)");
            foreach (var o in Outlets.All)
                sb.AppendLine("- " + o.Id + ": " + o.Name + ". " + o.Style + " Bias: " + o.Bias);
            sb.AppendLine();
            sb.AppendLine("RECURRING COMMENTERS (use these handles often, alongside one-off handles you invent, so the community feels familiar):");
            foreach (var c in Outlets.Commenters) sb.AppendLine("- " + c.Handle + ": " + c.Persona);
            sb.AppendLine();
            sb.AppendLine("KERBNET (the social network): short posts from a diverse public: enthusiasts, skeptics, scientists, worried parents, budget hawks, meme accounts, local residents near KSC, and occasionally verified accounts of officials. Posts react to each other like a real discussion.");
            if (state != null)
            {
                sb.AppendLine();
                sb.AppendLine("AGENCY CANON (fixed facts written by the agency; they override your imagination):");
                sb.Append(ProfileText(state));
            }
            sb.AppendLine();
            sb.AppendLine("OUTPUT: reply with one JSON object only, no markdown fences, matching the schema in the request.");
            return sb.ToString();
        }

        public static string SillinessText(int silliness)
        {
            if (silliness <= 0) return "Silliness 0 of 10: grounded and realistic. Write like real aerospace journalism and a real comment section. Kerbal names and setting are played straight.";
            if (silliness <= 3) return "Silliness " + silliness + " of 10: mostly realistic, with a light touch of Kerbal charm in comments and the tabloid.";
            if (silliness <= 6) return "Silliness " + silliness + " of 10: playful. Puns in tabloid headlines, more colorful commenters, the occasional snack shortage at KSC. Mainstream reporting stays factual.";
            return "Silliness " + silliness + " of 10: full Kerbal. Puns everywhere, Jeb worship, absurd commenters, dramatic tabloids. Facts and crew-death respect still apply.";
        }

        public static string ProfileText(NewsState state)
        {
            var p = state.Profile;
            var sb = new StringBuilder();
            sb.AppendLine("- Agency: " + p.Name + " (" + p.ShortName + "). Use this name instead of 'KSC' when referring to the agency; KSC is the launch site.");
            if (Compressor.ProfileSummaryValid(state))
            {
                Line(sb, "Background (summarized)", state.ProfileSummary);
                Line(sb, "House rules for the press (must follow)", p.HouseRules);
                return sb.ToString();
            }
            Line(sb, "Founding story", p.Founding);
            Line(sb, "Mission and goals", p.Mission);
            Line(sb, "Funding and politics", p.Politics);
            Line(sb, "Public reputation so far", p.Reputation);
            Line(sb, "Key people", p.KeyPeople);
            Line(sb, "World lore", p.WorldLore);
            Line(sb, "House rules for the press (must follow)", p.HouseRules);
            return sb.ToString();
        }

        static void Line(StringBuilder sb, string label, string value)
        {
            if (!string.IsNullOrEmpty(value) && value.Trim() != "") sb.AppendLine("- " + label + ": " + Clip(value.Trim(), 2500));
        }

        static string Clip(string s, int n) { return s.Length <= n ? s : s.Substring(0, n) + "..."; }

        /// <summary>The per-request fact sheet and assignment.</summary>
        public static string User(Story story, CoveragePlan plan, NewsState state, WorldDigest digest, IGameClock clock, double now, bool wantImage = false)
        {
            var sb = new StringBuilder();
            sb.AppendLine("FACT SHEET");
            sb.AppendLine("Today: " + clock.FormatDate(now) + ".");
            if (story.Kind == "filler")
            {
                sb.AppendLine("Nothing major has happened for a while. Write a feature or opinion piece on this topic: " + story.Topic);
            }
            else if (story.Kind == "release")
            {
                var rel = state.Releases.FirstOrDefault(r => r.Id == story.ReleaseId);
                string type = rel != null ? rel.Type : "announcement";
                sb.AppendLine("The agency issued an official press release (" + ReleaseTypeText(type) + ") on " + clock.FormatDate(story.CreatedUT) + ".");
                sb.AppendLine("Title: " + (rel != null ? rel.Title : story.Topic));
                if (rel != null && rel.Body != "") sb.AppendLine("Full text: " + rel.Body);
                if (rel != null && rel.Shots.Count > 0) sb.AppendLine("It came with " + rel.Shots.Count + " official photo(s)" + Captions(state, rel) + ".");
                if (rel != null && rel.VesselName != "") sb.AppendLine("It concerns the vessel \"" + rel.VesselName + "\".");
                var known = state.Articles.Where(a => a.PublishedUT <= now && state.Stories.Any(st => st.Id == a.StoryId && st.VesselId != "" && st.VesselId == story.VesselId)).OrderByDescending(a => a.PublishedUT).Take(4).ToList();
                if (known.Count > 0) sb.AppendLine("What the press already reported about this vessel: " + string.Join("; ", known.Select(a => "\"" + a.Headline + "\" (" + Outlets.NameOf(a.Outlet) + ")").ToArray()) + ".");
                sb.AppendLine("Cover the release itself: what the agency said, what it left out, and how it fits the public record. Quote it accurately. If it contradicts facts the public already knows (for example it claims success for a vessel that was seen to explode), outlets call that out sharply. If it withholds details or calls them classified, outlets speculate and the watchdog complains. A clear, honest incident statement softens criticism. Teasers get speculation about what is coming.");
            }
            else
            {
                sb.AppendLine("Events in this story (in order):");
                foreach (var e in story.Events.OrderBy(x => x.UT))
                    sb.AppendLine("- " + clock.FormatDate(e.UT) + ": " + (story.Silent ? DescribeSilent(e) : Describe(e)));
            }
            if (story.Silent)
            {
                if (story.Craft != null) sb.AppendLine(VisibleCraftText(story.Craft));
                sb.AppendLine("TELEMETRY SILENT: the agency has released no telemetry, specifications or crew details for this vessel. The press knows only what was visible from the ground or reported above. Do not state its mass, part count, delta-v, crew, destination or orbital details. Outlets may note the secrecy: the tabloid and the watchdog speculate, the trade press grumbles about missing data, commenters guess.");
            }
            else if (story.Craft != null) sb.AppendLine(CraftText(story.Craft));

            TrackedVessel tv;
            if (story.VesselId != "" && state.Tracked.TryGetValue(story.VesselId, out tv) && tv.History.Count > 0)
                sb.AppendLine("Earlier, the public already heard about this vessel: " + string.Join("; ", tv.History.ToArray()) + ".");

            sb.AppendLine();
            sb.AppendLine("PUBLIC MOOD: " + state.Mood.Describe());

            var past = PastFailure(story, state);
            if (past != null) sb.AppendLine("Most similar past setback the press will remember: \"" + past.Headline + "\" (" + clock.FormatDate(past.PublishedUT) + ").");

            var recent = state.Articles.Where(a => a.PublishedUT <= now).OrderByDescending(a => a.PublishedUT).Take(6).ToList();
            if (recent.Count > 0)
            {
                sb.AppendLine("Recent headlines (do not repeat them):");
                foreach (var a in recent) sb.AppendLine("- " + Outlets.NameOf(a.Outlet) + ": " + a.Headline);
            }

            if (state.MemorySummary != "")
                sb.AppendLine("ARCHIVE SUMMARY (what the public remembers from earlier, canon):\n" + state.MemorySummary);
            var lore = Compressor.FreshLore(state, now);
            if (lore.Count > 0)
            {
                sb.AppendLine("Agency lore journal (canon from its date):");
                foreach (var l in lore.Skip(Math.Max(0, lore.Count - 12))) sb.AppendLine("- " + clock.FormatDate(l.UT) + ": " + Clip(l.Text, 800));
            }

            if (digest != null) sb.AppendLine(DigestText(digest));

            sb.AppendLine();
            sb.AppendLine("ASSIGNMENT");
            sb.AppendFormat(Inv, "Significance: {0:0}/100 (tier: {1}). {2}\n", story.Significance, plan.Tier, TierText(plan.Tier));
            sb.AppendLine("Write one piece for each outlet below, each in its own voice and angle (they should not read like copies):");
            foreach (var o in plan.Outlets)
            {
                List<string> reps;
                string known = state.Reporters.TryGetValue(o.OutletId, out reps) && reps.Count > 0 ? " Known reporters (reuse one, or add a new name): " + string.Join(", ", reps.ToArray()) + "." : "";
                sb.AppendFormat(Inv, "- outlet \"{0}\" ({1}): kind \"{2}\", about {3} words, {4} comments.{5}\n",
                    o.OutletId, Outlets.NameOf(o.OutletId), o.Kind, o.Words, o.Comments, known);
            }
            sb.AppendFormat(Inv, "Also write {0} Kerbnet social posts reacting to this news, from a diverse mix of people, some replying to each other.\n", plan.SocialPosts);
            sb.AppendLine();
            sb.AppendLine("JSON SCHEMA");
            sb.AppendLine("{\"articles\":[{\"outlet\":\"<outlet id>\",\"kind\":\"<kind>\",\"headline\":\"...\",\"subhead\":\"one sentence\",\"reporter\":\"<kerbal name> Kerman\",\"body\":\"paragraphs separated by \\n\\n\",\"tags\":[\"short\",\"tags\"],\"comments\":[{\"author\":\"handle\",\"text\":\"...\",\"likes\":12}]}],\"social\":[{\"handle\":\"@name\",\"persona\":\"enthusiast|skeptic|scientist|official|local|meme|worried|budget\",\"text\":\"under 240 characters\",\"likes\":40,\"reshares\":5}]}");
            if (wantImage)
                sb.AppendLine("Also add a top-level field \"image_prompt\": two or three sentences describing one realistic press photograph for this story, showing only what a photographer could actually see (the rocket on the pad, the launch plume, crowds, the crew waving, the recovered capsule, a press conference). No text in the picture.");
            if (plan.Tier == "story" || plan.Tier == "major" || plan.Tier == "historic")
                sb.AppendLine("Also add a top-level field \"forum\": one thread a member of the public starts on Snackboard (the community forum) about this news: {\"board\":\"general|launches|missions|science|policy|memes\",\"title\":\"...\",\"body\":\"short post\",\"author\":\"handle\",\"score\":120,\"replies\":[{\"author\":\"handle\",\"text\":\"...\",\"likes\":30,\"reply_to\":\"\"}]} with " + (plan.Tier == "story" ? "3 to 5" : "6 to 10") + " replies; reply_to is \"\" for a top-level reply or \"r1\", \"r2\"... to answer an earlier reply in the list.");
            sb.AppendLine("Likes and reshares should scale with significance (a brief gets single or double digits; a historic first gets thousands).");
            return sb.ToString();
        }

        /// <summary>Asks for the community's replies to something the player posted, or for AMA questions.</summary>
        public static string Community(NewsEngine eng, CommunityJob job, IGameClock clock, double now)
        {
            var state = eng.State;
            var sb = new StringBuilder();
            sb.AppendLine("COMMUNITY DISCUSSION");
            sb.AppendLine("Today: " + clock.FormatDate(now) + ".");
            var a = eng.ArticleOf(job.Target);
            var t = eng.ThreadOf(job.Target);
            List<Comment> list;
            var shot = eng.ShotOf(job.Target);
            if (shot != null)
            {
                sb.AppendLine("Where: the agency's public photo gallery. The agency just released a photo" + (shot.Caption != "" ? " captioned \"" + shot.Caption + "\"" : "") + ".");
                sb.AppendLine("It shows " + (shot.VesselName != "" ? "the vessel \"" + shot.VesselName + "\"" : "an agency scene") + (shot.Body != "" ? " at " + shot.Body : "") + (shot.Situation != "" ? " (" + shot.Situation.ToLowerInvariant().Replace("_", " ") + ")" : "") + ", taken " + clock.FormatDate(shot.UT) + ".");
                list = shot.Comments;
            }
            else if (a != null)
            {
                sb.AppendLine("Where: the comment section under the " + Outlets.NameOf(a.Outlet) + " article \"" + a.Headline + "\".");
                sb.AppendLine("Article (start): " + Excerpt(a.Body, 900));
                list = a.Comments;
            }
            else
            {
                sb.AppendLine("Where: Snackboard, the community forum, board b/" + t.Board + ". Thread \"" + t.Title + "\" by " + Who(t.Author, t.Player, t.Agency, eng) + ":");
                if (t.Body != "") sb.AppendLine(Excerpt(t.Body, 1500));
                list = t.Comments;
            }
            sb.AppendLine();
            sb.AppendLine("PUBLIC MOOD: " + state.Mood.Describe());
            sb.AppendLine();
            var shown = Comment.Threaded(list).Select(x => x.Key).ToList();
            if (shown.Count > 30) shown = shown.Skip(shown.Count - 30).ToList();
            if (shown.Count > 0)
            {
                sb.AppendLine("Discussion so far (oldest first; [id] author: text):");
                foreach (var c in shown)
                    sb.AppendLine("[" + c.Id + "]" + (c.ReplyTo != "" ? " (reply to " + c.ReplyTo + ")" : "") + " " + Who(c.Author, c.Player, c.Agency, eng) + ": " + Excerpt(c.Text, 500) + " (" + c.Likes + " likes)");
                sb.AppendLine();
            }
            var mine = list.FirstOrDefault(c => c.Id == job.CommentId);
            bool agency = mine != null ? mine.Agency : t != null && t.Agency;
            sb.AppendLine("ASSIGNMENT");
            if (job.Mode == "ama")
            {
                sb.AppendLine("The agency's official account opened this Ask Me Anything thread. Write " + job.Count + " questions from different members of the public, as top-level replies (reply_to \"\"). Mix them: technical, budget, crew safety, future plans, fun or silly, one pointed or skeptical. Each question is short and specific to this agency's history. Do not answer them; the agency will.");
            }
            else
            {
                string what = mine != null ? "comment [" + mine.Id + "]" : shot != null ? "this photo" : "the opening post of this thread";
                if (shot != null && mine == null)
                    sb.AppendLine("The agency's official account released this photo. People react to the picture itself: awe, jokes, technical questions about what they can see, a skeptic or two, someone asking for a wallpaper version.");
                else if (agency)
                    sb.AppendLine("The player just posted " + what + " from the verified official account of " + eng.AgencyName + ". Everyone can see it is the agency. React the way people react when an organization's official account joins a discussion: some thank them or are excited, some ask pointed follow-up questions, some mock corporate or PR language, skeptics push back. Do not invent facts the agency did not state.");
                else
                    sb.AppendLine("The player just posted " + what + " anonymously as \"" + (mine != null ? mine.Author : t.Author) + "\". Nobody knows this is the agency: treat it as an ordinary member of the public. If it sounds like insider knowledge or PR spin, people may get suspicious and say so.");
                sb.AppendLine("Write " + job.Count + " replies from different people. Most answer the player's post (reply_to \"" + (mine != null ? mine.Id : "") + "\"), some answer each other. Keep each reply under 80 words, in the voice of the recurring commenters or new ones.");
            }
            sb.AppendLine();
            sb.AppendLine("JSON SCHEMA");
            sb.AppendLine("{\"replies\":[{\"author\":\"handle\",\"text\":\"...\",\"likes\":12,\"reply_to\":\"<an [id] above, or r1, r2... for an earlier reply in this list, or empty>\"}],\"player_likes\":25}");
            sb.AppendLine("player_likes is how many likes the player's post gets. Scale likes with how many people follow this news" + (agency ? "; official posts draw more attention." : "."));
            return sb.ToString();
        }

        static string ReleaseTypeText(string t)
        {
            switch (t)
            {
                case "incident": return "incident statement";
                case "update": return "mission update";
                case "teaser": return "teaser";
                default: return "announcement";
            }
        }

        static string Captions(NewsState state, PressRelease r)
        {
            var caps = r.Shots.Select(id => state.Gallery.FirstOrDefault(g => g.Id == id)).Where(g => g != null && g.Caption != "").Select(g => "\"" + g.Caption + "\"").ToArray();
            return caps.Length > 0 ? ", captioned " + string.Join(", ", caps) : "";
        }

        static string Who(string author, bool player, bool agency, NewsEngine eng)
        {
            if (player && agency) return author + " (OFFICIAL " + eng.AgencyName + " account)";
            return author;
        }

        static string Excerpt(string s, int n) { s = (s ?? "").Replace("\n", " "); return s.Length <= n ? s : s.Substring(0, n) + "..."; }

        /// <summary>A photo description when the writer didn't give one (offline mode or an older model).</summary>
        public static string FallbackImagePrompt(Story story, NewsState state)
        {
            var lead = OfflineWriter.LeadEvent(story);
            string v = story.Craft != null && story.Craft.VesselName != "" ? story.Craft.VesselName : lead != null ? lead.VesselName : "the rocket";
            string size = story.Craft != null ? SizeClass(story.Craft) : "a rocket";
            if (lead == null) return "Kerbals gather outside the space center for a press briefing.";
            switch (lead.Type)
            {
                case EventTypes.Launch: return size + " named " + v + " lifts off from the launch pad on a column of flame and smoke, watched by kerbals behind a fence.";
                case EventTypes.Failure: return "A plume of smoke and debris in the sky above the space center where " + v + " broke apart, kerbal onlookers staring upward.";
                case EventTypes.CrewKilled: return "Flags at half staff at the space center at dusk, flowers left by kerbals at the main gate.";
                case EventTypes.Splashdown: return "A scorched capsule floating in the ocean under parachutes, recovery boat approaching.";
                case EventTypes.Recovery: return "Recovery crews around a scorched capsule, kerbonauts climbing out and waving.";
                case EventTypes.Landing: return lead.Body == "Kerbin" ? "A capsule resting on grassland after landing, kerbal recovery crews approaching." : "A grainy broadcast image of a lander on the grey surface of " + lead.Body + ", shown on a big screen in mission control as kerbals cheer.";
                default: return "Mission control at the space center, kerbal engineers cheering in front of large screens showing " + v + ".";
            }
        }

        static string TierText(string tier)
        {
            switch (tier)
            {
                case "brief": return "Minor news: a short, calm brief. Few readers care; comments are sparse and mild.";
                case "story": return "Solid news story. Some public interest and a real comment discussion.";
                case "major": return "Major news, front-page. Strong public reaction, debate in comments, social media buzzing.";
                case "historic": return "Historic, everyone is talking about it. Big emotion (joy or grief), officials weigh in, heated debates.";
                case "feature": return "Quiet news day feature. Thoughtful, not breaking news.";
                default: return "";
            }
        }

        public static string Describe(NewsEvent e)
        {
            string crew = e.Crewed ? " with crew " + string.Join(", ", e.Crew.ToArray()) : " (uncrewed)";
            string v = "\"" + e.VesselName + "\"";
            string at = e.Body != "" ? " " + e.Body : "";
            switch (e.Type)
            {
                case EventTypes.Launch: return v + " launched" + crew + (e.Detail != "" ? " from " + e.Detail : "") + ".";
                case EventTypes.Orbit: return v + " reached a stable orbit around" + at + crew + "." + DetailText(e);
                case EventTypes.Soi: return v + " entered the sphere of influence of" + at + crew + "." + DetailText(e);
                case EventTypes.Landing: return v + " landed on" + at + crew + "." + DetailText(e);
                case EventTypes.Splashdown: return v + " splashed down on" + at + crew + "." + DetailText(e);
                case EventTypes.Docking: return v + " docked" + (e.Detail != "" ? " with \"" + e.Detail + "\"" : "") + (at != "" ? " in orbit of" + at : "") + ".";
                case EventTypes.Failure: return v + " suffered a failure" + (at != "" ? " near" + at : "") + crew + "." + DetailText(e);
                case EventTypes.CrewKilled: return "Kerbonaut " + string.Join(", ", e.Crew.ToArray()) + " was killed aboard " + v + (at != "" ? " near" + at : "") + "." + DetailText(e);
                case EventTypes.Recovery: return v + " was recovered" + crew + "." + DetailText(e);
                case EventTypes.Milestone: return "Agency milestone: " + e.Detail + (at != "" ? " (" + e.Body + ")" : "") + (e.VesselName != "" ? " by " + v : "") + ".";
                case EventTypes.ContractDone: return "The agency completed a contract: " + e.Detail + ".";
                case EventTypes.ContractFailed: return "The agency failed a contract: " + e.Detail + ".";
                case EventTypes.ContractAccepted: return "The agency accepted a contract: " + e.Detail + ".";
                default: return e.Type + ": " + v + " " + e.Detail;
            }
        }

        /// <summary>What a ground observer could say about an event on a silent vessel.</summary>
        public static string DescribeSilent(NewsEvent e)
        {
            string v = "\"" + e.VesselName + "\"";
            switch (e.Type)
            {
                case EventTypes.Launch: return "A rocket the agency calls " + v + " launched" + (e.Detail != "" ? " from " + e.Detail : "") + "; the agency did not say whether crew were aboard.";
                case EventTypes.Failure: return v + " was seen to be destroyed" + (e.Body == "Kerbin" || e.Body == "" ? " in the skies over Kerbin; the explosion was visible for kilometres" : "") + ".";
                case EventTypes.Orbit: return "Amateur satellite trackers report a new object in orbit around Kerbin, presumably " + v + ". Its exact orbit and purpose are not public.";
                case EventTypes.CrewKilled: return "The agency confirmed that kerbonaut " + string.Join(", ", e.Crew.ToArray()) + " was killed aboard " + v + ".";
                case EventTypes.Recovery: return v + " was recovered by agency teams.";
                case EventTypes.Splashdown: return "A capsule believed to be from " + v + " was seen splashing down.";
                case EventTypes.Landing: return "A craft believed to be " + v + " was seen landing.";
                default: return Describe(e);
            }
        }

        /// <summary>What photographers at the fence could see of a silent craft on the pad: size and shape, no specs.</summary>
        public static string VisibleCraftText(CraftSnapshot c)
        {
            var sb = new StringBuilder();
            sb.Append("Seen on the launch pad");
            if (c.LaunchSite != "") sb.Append(" at " + c.LaunchSite);
            sb.Append(": \"" + c.VesselName + "\", ");
            if (c.HeightM > 0) sb.AppendFormat(Inv, "roughly {0:0} m tall, ", Math.Round(c.HeightM / 5) * 5);
            sb.Append(SizeClass(c));
            if (c.Stages > 1) sb.AppendFormat(Inv, ", with about {0} visible stages", c.Stages);
            if (c.CrewCapacity > 0) sb.Append(", with what looks like a crew capsule on top");
            sb.Append(". Photographers at the fence got pictures; nothing else is known.");
            return sb.ToString();
        }

        public static string SizeClass(CraftSnapshot c)
        {
            if (c.MassTons > 150 || c.HeightM > 50) return "a large rocket";
            if (c.MassTons > 40 || c.HeightM > 25) return "a mid-size rocket";
            return "a small rocket";
        }

        static string DetailText(NewsEvent e) { return e.Detail != "" ? " " + e.Detail : ""; }

        public static string CraftText(CraftSnapshot c)
        {
            var sb = new StringBuilder();
            sb.Append("Craft \"" + c.VesselName + "\": ");
            if (c.Description != "") sb.Append(c.Description + " ");
            sb.AppendFormat(Inv, "Type {0}, {1} parts, {2:0.#} t at launch, about {3:0} m tall, {4} stages",
                c.VesselType == "" ? "rocket" : c.VesselType, c.Parts, c.MassTons, c.HeightM, c.Stages);
            if (c.DeltaV > 0) sb.AppendFormat(Inv, ", about {0:0} m/s of delta-v", c.DeltaV);
            if (c.Engines != "") sb.Append(", engines: " + c.Engines);
            if (c.CrewCapacity > 0) sb.Append(", seats for " + c.CrewCapacity);
            if (c.Crew.Count > 0) sb.Append(", crew at launch: " + string.Join(", ", c.Crew.ToArray()));
            if (c.LaunchSite != "") sb.Append(", launched from " + c.LaunchSite);
            sb.Append(".");
            return sb.ToString();
        }

        public static string DigestText(WorldDigest d)
        {
            var sb = new StringBuilder();
            sb.AppendFormat(Inv, "STATE OF THE UNIVERSE (publicly visible): {0} satellites, {1} stations, {2} surface bases, {3} pieces of tracked debris, {4} kerbonauts in space",
                d.Satellites, d.Stations, d.Bases, d.Debris, d.CrewInSpace);
            if (d.CrewInSpaceNames.Count > 0) sb.Append(" (" + string.Join(", ", d.CrewInSpaceNames.Take(12).ToArray()) + ")");
            sb.AppendFormat(Inv, ", {0} kerbonauts on the roster, {1} lost in the line of duty.", d.CrewTotal, d.CrewLost);
            if (d.DebrisByBody.Count > 0)
                sb.Append(" Debris by body: " + string.Join(", ", d.DebrisByBody.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " " + kv.Value).ToArray()) + ".");
            if (d.Records.Count > 0) sb.Append(" Agency records and firsts: " + string.Join("; ", d.Records.Take(15).ToArray()) + ".");
            if (d.Career != "") sb.Append(" " + d.Career);
            return sb.ToString();
        }

        static Article PastFailure(Story story, NewsState state)
        {
            if (!(story.HasEvent(EventTypes.Failure) || story.HasEvent(EventTypes.CrewKilled))) return null;
            return state.Articles
                .Where(a => a.StoryId != story.Id && a.Tags.Any(t => t == "failure" || t == "crew loss"))
                .OrderByDescending(a => (story.HasEvent(EventTypes.CrewKilled) && a.Tags.Contains("crew loss") ? 1000 : 0) + a.Significance)
                .ThenByDescending(a => a.PublishedUT)
                .FirstOrDefault();
        }
    }
}
