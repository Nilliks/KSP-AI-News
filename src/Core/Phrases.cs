using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>
    /// A small phrase grammar for template text. A pattern mixes fixed words, [rule] (one random option of that rule,
    /// expanded in turn) and {var} (a fact about the story). Options that name a fact the story doesn't have are skipped,
    /// so "a photo of {vessel}" never reads "a photo of ". Rules are built from interchangeable openings, middles and
    /// endings, so the same story rarely comes out the same way twice.
    /// </summary>
    public static class Phrases
    {
        /// <summary>The last few hundred lines written, so a fresh draw can avoid them (per game session).</summary>
        static readonly Queue<string> recent = new Queue<string>();
        static readonly HashSet<string> recentSet = new HashSet<string>();

        /// <summary>Forgets the recent lines (a fresh game session). Tests use it so one test's draws don't depend on the ones before it.</summary>
        public static void ForgetRecent() { recent.Clear(); recentSet.Clear(); }

        public static string Expand(string pattern, Dictionary<string, string> v, Random rng, int depth = 0)
        {
            if (string.IsNullOrEmpty(pattern) || depth > 12) return pattern ?? "";
            var sb = new StringBuilder();
            int i = 0;
            while (i < pattern.Length)
            {
                char c = pattern[i];
                if (c == '[' || c == '{')
                {
                    char close = c == '[' ? ']' : '}';
                    int j = pattern.IndexOf(close, i + 1);
                    if (j < 0) { sb.Append(pattern.Substring(i)); break; }
                    string name = pattern.Substring(i + 1, j - i - 1);
                    if (c == '{') { string val; sb.Append(v != null && v.TryGetValue(name, out val) ? val : ""); }
                    else sb.Append(Rule(name, v, rng, depth + 1));
                    i = j + 1;
                    continue;
                }
                sb.Append(c);
                i++;
            }
            string outText = Tidy(sb.ToString());
            return depth == 0 ? Sentences(outText) : outText;
        }

        /// <summary>Capitalises the first letter after a full stop, ! or ? and after a line break, so pieces that can sit anywhere still read right.</summary>
        static string Sentences(string s)
        {
            var ch = s.ToCharArray();
            for (int i = 1; i < ch.Length; i++)
            {
                bool after = ch[i - 1] == '\n' || (i >= 2 && ch[i - 1] == ' ' && (ch[i - 2] == '.' || ch[i - 2] == '!' || ch[i - 2] == '?'));
                if (after && char.IsLower(ch[i])) ch[i] = char.ToUpperInvariant(ch[i]);
            }
            return new string(ch);
        }

        /// <summary>One option of a rule, expanded. Options needing a missing fact are left out.</summary>
        public static string Rule(string name, Dictionary<string, string> v, Random rng, int depth = 0)
        {
            string[] opts;
            if (!Bank.TryGetValue(name, out opts)) return "";
            var ok = opts.Where(o => Usable(o, v)).ToList();
            if (ok.Count == 0) return "";
            return Expand(ok[rng.Next(ok.Count)], v, rng, depth);
        }

        /// <summary>Like Rule, but tries a few times for a line not used recently.</summary>
        public static string Fresh(string name, Dictionary<string, string> v, Random rng)
        {
            string s = "";
            for (int k = 0; k < 8; k++)
            {
                s = Rule(name, v, rng);
                if (!recentSet.Contains(s)) break;
            }
            Remember(s);
            return s;
        }

        static void Remember(string s)
        {
            if (s == "" || recentSet.Contains(s)) return;
            recent.Enqueue(s); recentSet.Add(s);
            while (recent.Count > 400) recentSet.Remove(recent.Dequeue());
        }

        static bool Usable(string option, Dictionary<string, string> v)
        {
            int i = 0;
            while ((i = option.IndexOf('{', i)) >= 0)
            {
                int j = option.IndexOf('}', i + 1);
                if (j < 0) break;
                string val;
                if (v == null || !v.TryGetValue(option.Substring(i + 1, j - i - 1), out val) || string.IsNullOrEmpty(val)) return false;
                i = j + 1;
            }
            return true;
        }

        static string Tidy(string s)
        {
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\b([Aa]) ([aeiouAEIOU][a-z])", "$1n $2");
            return s.Replace(" .", ".").Replace(" ,", ",").Replace(" !", "!").Replace(" ?", "?").Replace("...", "\u2026").Replace("..", ".").Replace("\u2026", "...").Trim();
        }

        /// <summary>The raw options of a rule (empty when there is no such rule).</summary>
        public static string[] Options(string name) { string[] o; return Bank.TryGetValue(name, out o) ? o : new string[0]; }

        public static string Cap(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }

        /// <summary>How many different texts a rule can make (capped), for tests.</summary>
        public static double Variety(string name, int depth = 0)
        {
            string[] opts;
            if (depth > 8 || !Bank.TryGetValue(name, out opts)) return 1;
            double total = 0;
            foreach (var o in opts)
            {
                double prod = 1;
                int i = 0;
                while ((i = o.IndexOf('[', i)) >= 0) { int j = o.IndexOf(']', i); if (j < 0) break; prod *= Variety(o.Substring(i + 1, j - i - 1), depth + 1); i = j + 1; }
                total += prod;
                if (total > 1e9) return 1e9;
            }
            return total;
        }

        // ================================================================== the bank

        public static readonly Dictionary<string, string[]> Bank = new Dictionary<string, string[]>
        {
            // ---------------- headlines: one rule per outlet voice and kind of news ----------------
            { "hl_good", new[] {
                "{what}", "{what}", "{agency} [win_verb] as {what_l}", "{what}: [good_tag]", "[good_open] {what_l}", "{vessel} [good_verb_past]",
                "{what} in [smooth_adj] flight", "{agency}'s {vessel} [good_verb_past]", "A [smooth_adj] day for {agency}: {what_l}", "{what}, and the crowd [crowd_verb]",
            } },
            { "hl_fail", new[] {
                "{vessel} lost [fail_where]", "{agency} [loses_verb] {vessel}", "[fail_open] {vessel} [fail_verb]", "{vessel} [fail_verb] [fail_when]", "Setback for {agency} as {vessel} [fail_verb]",
                "{agency} investigating after {vessel} [fail_verb]", "Bad day at {agency}: {vessel} [fail_verb]",
            } },
            { "hl_loss", new[] {
                "{agency} mourns {crew} after {vessel} disaster", "{crew} lost aboard {vessel}", "Tragedy at {agency}: {crew} killed", "Kerbin grieves for {crew}", "{vessel} crew lost; {agency} in mourning",
                "\"They were the best of us\": {agency} remembers {crew}",
            } },
            { "hl_kraken", new[] {
                "You won't BELIEVE what really happened when {what_l}", "{what}... or did it? Our insider says NO", "[kraken_open] {vessel}: [kraken_claim]", "What {agency} WON'T tell you about {vessel}",
                "{vessel}: [kraken_claim]", "[kraken_number] things about {vessel} that don't add up", "WAKE UP: {what_l} is not the whole story", "Is {agency} covering up the truth about {vessel}?",
            } },
            { "hl_kraken_fail", new[] { "Was it the KRAKEN? The {vessel} theory {agency} won't talk about", "SABOTAGE? [kraken_number] clues in the {vessel} disaster", "{vessel} 'accident'? Our sources laugh", "WHO really blew up {vessel}?" } },
            { "hl_kraken_secret", new[] { "SECRET ROCKET: what {agency} is HIDING from you", "They launched WHAT? Insiders spill on {agency}'s mystery craft", "BLACKOUT at {agency}: what's on that rocket?", "No data, no answers: {agency}'s ghost launch" } },
            { "hl_inquirer", new[] { "[inq_open] {what_up}!", "{what_up}!", "[inq_open] {vessel} [inq_verb]!", "{crew_first}'s BIG DAY!", "SPACE HEROES: {crew}!", "[inq_open] {agency} DOES IT AGAIN!" } },
            { "hl_inquirer_fail", new[] { "KABOOM! {vessel} goes up in smoke", "Another {agency} rocket, another fireball", "FIREBALL! {vessel} [fail_verb]", "{vessel} DISASTER: [inq_fail_tail]", "UP IN FLAMES: {vessel}" } },
            { "hl_observer", new[] { "Analysis: {what_l}", "{what}: a {mass}-tonne, {stages}-stage design under the microscope", "By the numbers: {vessel}", "{what}: [obs_angle]", "Technical review: {vessel}", "{vessel}, {parts} parts and {dv} m/s: [obs_angle]" } },
            { "hl_kscwatch", new[] { "Our take: {what_l}. Was it worth it?", "{what}. [watch_question]", "Counting the cost of {vessel}", "{vessel}: [watch_question]", "The bill for {vessel}, and who pays it" } },
            { "hl_kscwatch_fail", new[] { "Questions {agency} must answer after the {vessel} loss", "{vessel}: how much did that cost?", "After {vessel}, [watch_demand]", "Another loss, another silence from {agency}" } },
            { "hl_munmonthly", new[] { "The kerbals behind the moment: {crew}", "Why {what_l} matters", "{crew_first}, in their own words", "A long road to {body}", "Portrait of a flight: {vessel}", "What it felt like: {what_l}" } },
            { "hl_science", new[] { "Researchers weigh in: what {vessel} means for science at {body}", "What {vessel} could teach us about {body}", "Science at {body}: [sci_angle]", "Researchers weigh in: what {vessel} means for science" } },
            { "hl_council", new[] { "Planetary Council congratulates {agency}", "Council statement on {what_l}", "The Council welcomes {what_l}" } },
            { "hl_council_loss", new[] { "Statement from the Planetary Council on the loss of {crew}", "Council orders flags lowered for {crew}" } },
            { "hl_filler", new[] { "A quiet week at {agency}: what comes next?", "Inside {agency} between missions", "Opinion: where should {agency} go from here?", "[quiet_open] at the space center", "The waiting game at {agency}", "What {agency} is (probably) building now", "Letters: readers on the space program" } },
            { "hl_filler_kraken", new[] { "Is {agency} hiding a BASE on the Mun? Our insider says YES", "The Kraken is REAL, and {agency} knows it", "Cafeteria insider: the kerbonaut FEUD nobody is talking about", "Why is {agency} SO QUIET? [kraken_claim]" } },

            { "win_verb", new[] { "celebrates", "cheers", "scores a win", "has a day to remember" } },
            { "good_tag", new[] { "a clean flight", "everything to plan", "nominal, and then some", "a textbook mission", "one for the scrapbook" } },
            { "good_open", new[] { "Watch: ", "Live from the coast: ", "Mission report: ", "Confirmed: ", "" } },
            { "good_verb_past", new[] { "delivers", "does the job", "makes it look easy", "comes through", "hits its marks" } },
            { "smooth_adj", new[] { "smooth", "flawless", "picture-perfect", "textbook", "tidy" } },
            { "crowd_verb", new[] { "goes wild", "roars", "can't get enough", "cheers from the causeway" } },
            { "loses_verb", new[] { "loses", "writes off", "says goodbye to" } },
            { "fail_open", new[] { "Disaster:", "Breaking:", "Trouble:", "Oh no:" } },
            { "fail_verb", new[] { "breaks apart", "fails", "is lost", "comes apart", "explodes", "goes down" } },
            { "fail_where", new[] { "near {body}", "on the way up", "in a fireball", "over the ocean", "minutes after launch" } },
            { "fail_when", new[] { "minutes into its flight", "on a bad day for the program", "in front of a crowd", "" } },
            { "kraken_open", new[] { "EXPOSED:", "LEAKED:", "THE TRUTH ABOUT", "SHOCKING:", "THEY LIED:" } },
            { "kraken_claim", new[] { "the photos don't match", "insiders say it never flew", "the Kraken was spotted nearby", "the numbers were FAKED", "a secret second crew?", "follow the snack money", "something is in the fairing" } },
            { "kraken_number", new[] { "7", "5", "9", "12", "3" } },
            { "inq_open", new[] { "WOW!", "THEY DID IT!", "INCREDIBLE!", "HISTORY!", "OMG!", "BOOM!" } },
            { "inq_verb", new[] { "STUNS KERBIN", "DOES THE IMPOSSIBLE", "MAKES HISTORY", "SMASHES IT" } },
            { "inq_fail_tail", new[] { "WHO IS TO BLAME?", "FAMILIES DEMAND ANSWERS", "SHOCK AT KSC", "WAS IT RUSHED?" } },
            { "obs_angle", new[] { "what the data shows", "what went right", "a solid piece of engineering", "where the margins were", "the design choices that mattered" } },
            { "watch_question", new[] { "Was it worth it?", "Who paid for it?", "What did it really cost?", "And what about the debris?", "Is anyone keeping count?" } },
            { "watch_demand", new[] { "the public deserves the full report", "it's time for an independent review", "the agency needs to slow down", "somebody should be answering questions" } },
            { "sci_angle", new[] { "a first look", "what the data might hold", "questions worth asking", "why researchers are excited" } },
            { "quiet_open", new[] { "Quiet days", "A slow week", "Between launches", "Calm before the next storm" } },

            // ---------------- subheads ----------------
            { "sub_good", new[] { "A crewed flight for {agency}.", "An uncrewed flight for {agency}.", "{crew}: [crew_state].", "The flight went [plan_word], officials said.", "[sub_good_any]" } },
            { "sub_good_any", new[] { "Another step for the program.", "It is the latest in a busy run of flights.", "Crowds lined the coast to watch.", "Flight controllers called it a good day.", "The agency says more is to come." } },
            { "crew_state", new[] { "in good spirits, officials say", "all systems normal, according to the agency", "safe and well", "a busy day aboard" } },
            { "plan_word", new[] { "to plan", "smoothly", "exactly as planned", "well" } },
            { "sub_fail", new[] { "The crew's status is the first question on everyone's mind.", "No crew were aboard.", "Engineers are already looking at the telemetry.", "The agency has not yet said what went wrong.", "It is the program's latest setback." } },
            { "sub_loss", new[] { "The agency has not yet said what went wrong.", "Flags flew at half staff across the space center.", "Messages of sympathy poured in from across Kerbin.", "An investigation is expected." } },

            // ---------------- article body paragraphs ----------------
            { "open_good", new[] {
                "[time_word], {what_l}.", "{what}, {agency} [confirmed_verb] [time_word_l].", "It was [day_word] at the space center as {what_l}.", "{agency} [had_word] [day_word]: {what_l}.",
                "Spectators [watch_verb] as {what_l}.", "[time_word], the news came through: {what_l}.",
            } },
            { "open_fail", new[] {
                "[time_word], {vessel} [fail_verb] [fail_when].", "{agency} [confirmed_verb] that {vessel} was lost.", "It was a hard day at the space center: {vessel} [fail_verb].",
                "Spectators watched in silence as {vessel} [fail_verb].",
            } },
            { "open_loss", new[] { "{agency} [confirmed_verb] the loss of {crew} aboard {vessel}.", "Kerbin is in mourning after {crew} died aboard {vessel}.", "The space center fell quiet [time_word_l] when word came that {crew} had been lost aboard {vessel}." } },
            { "time_word_l", new[] { "today", "earlier today", "this morning", "late today", "on {date}" } },
            { "time_word", new[] { "Today", "Early today", "This morning", "Late today", "Earlier today", "On {date}" } },
            { "confirmed_verb", new[] { "confirmed", "announced", "said", "reported" } },
            { "day_word", new[] { "a busy day", "a good day", "a big day", "a day to remember", "a long day" } },
            { "had_word", new[] { "had", "enjoyed", "marked" } },
            { "watch_verb", new[] { "cheered", "looked on", "held their breath", "lined the causeway" } },
            { "quote_good", new[] {
                "\"[quote_good_core],\" [speaker] said.", "\"[quote_good_core],\" said [speaker].", "[speaker] put it simply: \"[quote_good_core].\"",
            } },
            { "quote_good_core", new[] { "Everything went the way we planned it", "The team did exactly what they trained for", "We're proud of this one", "It's a good day to work here", "This is why we do it",
                "We'll go through the data, but it looks clean", "Honestly, it was a little boring, and boring is good", "The crew made it look easy" } },
            { "quote_fail", new[] { "\"[quote_fail_core],\" [speaker] said.", "[speaker] said: \"[quote_fail_core].\"" } },
            { "quote_fail_core", new[] { "We will find out what happened", "It's a setback, not the end", "Our first job is to understand the data", "Rockets are hard, and today reminded us", "We'll learn from this" } },
            { "speaker", new[] { "a flight director", "an agency spokesperson", "one ground controller", "a senior engineer", "a member of the launch team", "an official at the space center" } },
            { "close_good", new[] { "{agency} officials called the flight a success.", "The agency has not said what comes next.", "More flights are expected soon.", "[mood_line]", "Fans are already asking where the agency will go next." } },
            { "close_fail", new[] { "Engineers are expected to review the telemetry.", "The agency says it will share what it learns.", "No date has been set for the next attempt.", "[mood_line]" } },
            { "close_loss", new[] { "Flags at the space center flew at half staff. Members of the public left flowers at the main gate.", "A memorial is being planned.", "The agency has asked for privacy for the families.", "Launches are on hold while the investigation goes on." } },
            { "mood_line", new[] { "{mood}" } },
            { "angle_observer", new[] { "At {mass} tonnes and {parts} parts, the vehicle is {size} by the standards of the program so far.", "The design carried {dv} m/s of delta-v across {stages} stages.", "Engineers we spoke to were interested in the {stages}-stage layout." } },
            { "angle_kscwatch", new[] { "Nobody has said what this flight cost.", "Every flight leaves something behind in orbit. We are keeping count.", "Success or not, the public deserves the numbers.", "We will be asking for the budget figures." } },
            { "angle_inquirer", new[] { "Fans went WILD!", "Our photographer was there!", "Kids across Kerbin are already playing rocket in the garden!", "Even our editor shed a tear!" } },
            { "angle_kraken", new[] { "Our sources say there's more to this story.", "Ask yourself: why was the camera feed so short?", "We have questions. They have no answers.", "Remember who told you first." } },
            { "angle_munmonthly", new[] { "For the people who build and fly these machines, days like this are the reason for the long hours.", "Behind every launch are hundreds of kerbals whose names never make the news.", "It is the kind of day that ends up on a classroom wall." } },
            { "angle_science", new[] { "Researchers say any data from {body} will be studied for years.", "The science community is watching closely.", "Even a short visit can answer questions that telescopes can't." } },

            // ---------------- quiet-day features ----------------
            { "filler_open", new[] { "It has been a quiet stretch for {agency}.", "No launches this week, but the space center is anything but idle.", "The pad is empty and the coffee is strong: it's a waiting week at {agency}.", "Between missions, {agency} is quieter than usual." } },
            { "filler_mid", new[] { "Workers in the assembly building say something new is taking shape, but nobody will say what.", "Crews are training in the simulators and the engineers are going over the data from the last flight.", "Readers have been writing in with ideas for where the agency should go next.", "Local cafés near the space center say business is slower without the launch crowds." } },

            // ---------------- comments and posts ----------------
            { "c_good", new[] { "[c_good_core]", "[c_good_core] [c_tail]", "[c_open] [c_good_core]" } },
            { "c_good_core", new[] { "Great to see!", "What a time to be alive.", "Was that worth the money? Genuinely asking.", "Saw it from my back garden!", "Another one for the history books.", "Jeb would be proud.",
                "Goosebumps every time.", "My kids watched the whole thing.", "Proud of the team.", "Clean flight, nice work.", "Now do it again!", "This is what the money is for.", "That's how it's done.",
                "Who else was on the causeway?", "Best news this week.", "Still can't believe we can do this.", "Somebody give the engineers a raise." } },
            { "c_fail", new[] { "[c_fail_core]", "[c_open] [c_fail_core]" } },
            { "c_fail_core", new[] { "Back to the drawing board, I suppose.", "How much did that cost us?", "Test flights fail. That's what they're for.", "This is the part they never show on the posters.",
                "Glad nobody was hurt.", "They'll figure it out.", "That's a lot of money on fire.", "Somebody check the struts.", "Did anyone get video?", "Rockets are hard.", "Not again." } },
            { "c_loss", new[] { "Rest easy. Fly safe up there.", "Thoughts with the families tonight.", "There needs to be a full inquiry into this.", "Heartbreaking. They knew the risks and went anyway.",
                "I don't have words.", "We owe them answers.", "Lit a candle for the crew tonight.", "This one hurts." } },
            { "c_release", new[] { "[c_release_core]", "[c_open] [c_release_core]" } },
            { "c_release_core", new[] { "Nice to hear it from them directly.", "Sounds like PR to me.", "I'll believe it when I see it.", "Good. More of this, please.", "What aren't they saying?",
                "Short and sweet. I like it.", "Someone in the press office earned their lunch today.", "Read it twice and still don't know what it means.", "Good to see them being open about it.",
                "Fine words. Show us the numbers.", "This is the kind of update I want more often.", "Who writes these?", "That last paragraph is doing a lot of work.", "Finally, some straight answers.",
                "I'd like to hear from the engineers, not the spokespeople.", "Love the confidence.", "Bookmarking this for when it goes wrong.", "Polite, careful, and tells us nothing." } },
            { "c_tip", new[] { "[c_tip_core]", "[c_open] [c_tip_core]" } },
            { "c_tip_core", new[] { "Interesting.", "Well, well, well.", "Can anyone confirm this?", "Follow the money.", "Not surprised.", "This is going to be a long week for the press office.",
                "Source? Anyone?", "If true, that's a big deal.", "I'll wait for the official word.", "My cousin works at the space center and says it's nonsense.", "Every week there's a new rumour.",
                "Somebody's getting a stern talking-to.", "This smells like a leak.", "Not the first time I've heard this.", "Grain of salt, folks.", "Called it.", "The agency will deny it by lunch.",
                "And nobody at the agency wants to comment? Funny." } },
            { "c_open", new[] { "Honestly?", "Ok.", "Not gonna lie:", "Wow.", "Well.", "Hm.", "Right." } },
            { "c_tail", new[] { "", "", "Can't wait for the next one.", "More please.", "Bring on the next mission." } },
            { "s_good", new[] { "{vessel} looked incredible today!", "Proud of {agency} today.", "ok but who's paying for all these rockets", "Took the kids to watch {vessel}. They won't stop talking about it.",
                "Clear skies, loud engines, best morning in weeks.", "Every launch still gives me goosebumps.", "Anyone else get a good photo of {vessel}?", "Space is cool and I will not be taking questions.",
                "The traffic on the causeway was worse than the countdown.", "Can't wait to see where {vessel} goes next.", "{what}. Wow.", "{crew_first} is my hero now.", "[s_open] {vessel} [s_good_verb]",
                "Still thinking about that launch.", "Office stopped working to watch {vessel}. Worth it.", "{agency} is on a roll." } },
            { "s_open", new[] { "ngl", "honestly", "lowkey", "ok", "wow" } },
            { "s_good_verb", new[] { "was beautiful", "slapped", "made my day", "went perfectly", "was LOUD" } },
            { "s_fail", new[] { "Watched {vessel} come apart from the beach. Still processing.", "rapid unplanned disassembly is a funny way to say 'it blew up'", "Saw the fireball from the causeway. Wow.",
                "That's going to cost someone their weekend.", "So... was that supposed to happen?", "My dog is still hiding under the bed after that bang.", "Rockets are hard. They'll fix it.", "How much did that one cost us?",
                "Thoughts with the engineers tonight.", "{agency} having a rough one.", "Glad it was uncrewed." } },
            { "s_loss", new[] { "Can't stop thinking about the {vessel} crew. #FlySafe", "Somebody at {agency} needs to answer for this.", "Flags at half mast in my town tonight.", "They knew the risks. Doesn't make it easier.",
                "Hold the families close today.", "We owe them answers, not a press release.", "Turned the news off. Couldn't watch any more.", "Fly safe, {crew_first}." } },

            // ---------------- Snackboard ----------------
            { "f_title_good", new[] { "{vessel} discussion thread", "Today's launch discussion thread", "{what}! Discuss", "Did anyone else watch {vessel}?", "{vessel}: what's next?", "Rate the {vessel} flight out of 10" } },
            { "f_title_fail", new[] { "What went wrong with {vessel}?", "What went wrong today?", "{vessel} failure megathread", "Theories about {vessel}?" } },
            { "f_title_loss", new[] { "Thread for {vessel}: thinking of the crew", "Thinking of the crew today", "Remembering {crew}" } },

            // ---------------- replies to the player on the community sites ----------------
            { "r_agency", new[] { "Appreciate the official response. Any numbers you can share?", "That reads like it came straight from the press office.", "Thanks for engaging with us here, that's rare.", "So what are you NOT telling us?",
                "Good to hear it from you directly.", "Can you confirm the crew is safe?", "Nice of the agency to show up.", "Can we get a statement in plain words?", "Ok, but when's the next launch?", "Respect for answering in the comments." } },
            { "r_public", new[] { "Interesting take.", "Source?", "I was thinking the same thing.", "Hard disagree, but fair point.", "You sound like you work at KSC.", "This. Exactly this.", "Underrated comment.", "Not sure I agree.",
                "Big if true.", "Can you explain that a bit more?", "lol", "Came here to say this." } },
            { "r_photo", new[] { "Wallpaper material.", "Is that real or a render?", "Look at that view!", "Zoom and enhance, what's that on the left?", "Framing this one.", "How much did this photo cost us?", "Printing this for my wall.", "Unreal." } },
            { "r_townhall", new[] { "What exactly went wrong, and what have you changed since?", "Why should we trust the next launch?", "How much did the failure cost taxpayers?", "Were the crews told about the risks?", "Who signed off on that flight?",
                "When will the full investigation be published?", "Thank you for facing us. What can families expect from you now?", "Will you pause launches until it is fixed?", "What would you do differently?", "Who checks your safety work?" } },
            { "r_ama", new[] { "What is the next big mission?", "How do you decide who flies?", "What does a launch actually cost?", "Is it true the cafeteria runs out of snacks before every launch?", "How do you keep the crews safe after what happened last time?",
                "When are we going to Duna?", "What happens to all the debris?", "Do you ever get scared watching a launch?", "What's the hardest part of the job?", "Can I visit the space center?", "Favorite rocket so far?" } },

            // ---------------- world news endings ----------------
            { "w_sky", new[] { "Amateur astronomers are being urged to look up. Observatories will keep watching and share what they find.", "Clubs across Kerbin are planning viewing nights.", "Astronomers say there's no danger, just a good show." } },
            { "w_safety", new[] { "Tracking stations are watching closely. Experts say most small objects burn up harmlessly, but the growing crowd in orbit worries some.", "Debris experts say the numbers are worth watching.", "Nobody is in danger, officials say, but nobody wants the problem to grow." } },
            { "w_crew", new[] { "Colleagues say the milestone is a credit to years of training, and fans have flooded the agency with messages.", "Friends say the honor hasn't gone to their head.", "Their hometown is planning a small celebration." } },
            { "w_company", new[] { "Industry watchers say space work is becoming a real business on Kerbin, and the suppliers want everyone to know it.", "The company says more orders are coming.", "Analysts say parts makers are the quiet winners of the space age." } },
            { "w_record", new[] { "Statisticians are already updating the record books.", "Fans are already arguing about whether it will last.", "It will be a hard one to beat." } },
            { "w_tech", new[] { "Experts are guessing what the agency will build next with it. The agency has not said.", "Engineers say it opens up new kinds of missions.", "It may not make headlines, but it changes what is possible." } },
            { "w_base", new[] { "It is a quiet kind of success: no launch, no fireworks, just the work going on day after day.", "Nobody cheers for a day of routine, but it is what a working program looks like." } },
            { "w_probe", new[] { "Far from home, it is a reminder of how big the system is, and how long the agency's machines keep working.", "Engineers still check on it every day.", "Its signal takes a while to come home, but it still comes." } },
            { "w_market", new[] { "Traders say off-world ore is still a young market, and prices can swing on a single cargo.", "Analysts say the exchange is getting used to metal that comes from the sky.", "Buyers on Kerbin say they will take all the agency can bring home." } },
            { "w_watch", new[] { "Flight controllers say the long cruise is the quiet part of any mission. The busy days come at the end.", "Until then the flight team checks on it every day, and so will we.", "The countdown is on.", "Space fans have already marked the date." } },
            { "w_mod", new[] { "Mission planners say they take space weather seriously, and crews have shelter plans.", "Forecasters will keep watching the Sun." } },

            // ---------------- Kmail (no AI) ----------------
            { "m_open_press", new[] { "Dear Director,", "Hello Director,", "Director," } },
            { "m_open_warm", new[] { "Hello!", "Hi there!", "Dear Director," } },
            { "m_congrats", new[] { "Congratulations on {topic}.", "What a week for your agency, with {topic}.", "Our readers have been following {topic} closely.", "Following {topic}, we have a request." } },
            { "m_interview", new[] { "{outlet} would love an exclusive interview with {kerbal} about it.", "We would like to sit down with {kerbal} for an exclusive interview.", "Could {kerbal} spare thirty minutes for an exclusive interview with {outlet}?" } },
            { "m_reply_by", new[] { "Could you let us know by {due}?", "We'd need an answer by {due}.", "We go to print on {due}, so please let us know before then." } },
            { "m_statement", new[] { "We are writing about {topic} and would like the agency's statement.", "We're preparing a story on {topic} and want to give the agency a chance to comment.", "Our story on {topic} would be better with the agency's side." } },
            { "m_statement_due", new[] { "Our story runs on {due}; without a reply we'll note that the agency did not respond.", "We publish on {due}. If we don't hear back, the story will say the agency did not respond." } },
            { "m_sponsor", new[] { "{from} would like to sponsor your next flight for √{amount}, in return for our logo on the craft and a mention in your releases.", "Our board has approved √{amount} to sponsor an upcoming flight. We'd ask for our logo on the craft and a line in your releases.", "We'd like to put √{amount} behind your next mission, with our name on the side of the rocket." } },
            { "m_sponsor_due", new[] { "The offer stands until {due}.", "We need an answer by {due}.", "The offer is good until {due}." } },
            { "m_visit", new[] { "Our students have been following {kerbal} and they have SO many questions. Could {kerbal_first} visit us?", "The whole class drew pictures of {kerbal_first}'s rocket. Would {kerbal_first} come and see them?", "We would be thrilled if {kerbal} could visit and answer a few questions." } },
            { "m_visit_due", new[] { "Any day before {due} works.", "We're free any morning before {due}.", "Anytime before {due} would be wonderful." } },
            { "m_hearing", new[] { "The Council's science committee requests your testimony about {topic} on {due}. Please confirm your attendance.", "The committee will hear testimony on {topic} on {due}, and asks that you attend.", "You are invited to appear before the science committee on {due} to discuss {topic}." } },

            // ---------------- Kerbopedia (no AI) ----------------
            // ---------------- crew chat with the AI off ----------------
            { "crew_away_brave", new[] { "All good up here, boss. {vessel} is flying like a dream.", "Reading you loud and clear. Say the word and we go.", "Nothing to report but a great view of {where}.", "Systems green. Honestly, I could do this all week.", "Copy that. Ready for whatever's next." } },
            { "crew_away_calm", new[] { "Signal's a bit weak, but we're fine. Checklists done.", "Everything nominal aboard {vessel}.", "Doing the rounds now. I'll call if anything changes.", "Copy. Quiet shift so far.", "We're well. Tell the ground team thanks for the update." } },
            { "crew_away_goofy", new[] { "Is this thing on? Hi! Space is great!", "I pressed a button and nothing exploded. Good day!", "Can someone tell me which way is up? Asking for a friend.", "Snacks are holding out. Mostly.", "Ground, the view of {where} is SO good." } },
            { "crew_home_brave", new[] { "When's my next flight? I'm getting restless.", "Put me on the next one, boss. Any one.", "The simulators are too easy. Give me the real thing.", "Been running drills all morning. Ready when you are." } },
            { "crew_home_calm", new[] { "Morning, boss. Going through the manuals again.", "Training's going well. Let me know when you need me.", "I'm around the astronaut complex if you need anything.", "Just finished a sim session. Learned a thing or two." } },
            { "crew_home_goofy", new[] { "Hi boss! I found where they hide the good snacks.", "I named the training capsule. Don't ask what.", "Is it launch day? It feels like launch day.", "I only fell off the centrifuge once today!" } },

            { "wk_craft_lead", new[] { "{title} is a {kind} built by {agency}.", "{title} is a {kind} operated by {agency}.", "{title} is a {kind} designed and flown by {agency}.", "{title} is a {kind} in the {agency} fleet." } },
        };
    }
}
