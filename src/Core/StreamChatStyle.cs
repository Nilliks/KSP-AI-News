using System;
using System.Collections.Generic;
using System.Linq;

namespace KerbalNews
{
    /// <summary>
    /// What a real launch-stream chat sounds like, in the Kerbin universe: the usual crowd (people saying where they
    /// watch from, kids, engineers, doubters, people at work, emote walls), running jokes, off-topic questions,
    /// typos and caps. The lines are original, written in the style of public launch streams, not copied from them.
    /// Used for the offline chat and to give the AI a fresh mix of voices for every batch.
    /// </summary>
    public static class StreamChatStyle
    {
        // ------------------------------------------------------------------ handles

        static readonly string[] HandleHeads = { "jeb", "mun", "minmus", "duna", "kerb", "snack", "strut", "booster", "apoapsis", "delta_v", "val", "bill", "bob", "boom", "orbit", "eve", "jool", "launchpad", "nosecone", "fairing", "gimbal", "throttle", "kraken", "mystery_goo", "parachute", "ladder", "solar" };
        static readonly string[] HandleTails = { "fan", "lover", "watcher", "guy", "kid", "enjoyer", "_official", "_irl", "head", "nerd", "4life", "dad", "mom", "_fr", "lord", "main", "_tv", "addict" };
        static readonly string[] Regulars = { "taxpayer_ted", "dr_linmy", "first_timer", "truth_orbiter", "rocket_bob", "kiddo_kerman", "snackhatch", "debriscounter", "gimbal_lock", "grandma_kerman", "KSC_janitor_maybe", "moar_boosters", "astro_nerd_77" };

        /// <summary>A believable chat name: mostly generated, sometimes one of the regulars who show up to every stream.</summary>
        public static string Handle(Random rng)
        {
            if (rng.Next(5) == 0) return Regulars[rng.Next(Regulars.Length)];
            string h = HandleHeads[rng.Next(HandleHeads.Length)];
            switch (rng.Next(6))
            {
                case 0: return h + HandleTails[rng.Next(HandleTails.Length)];
                case 1: return h + "_" + HandleTails[rng.Next(HandleTails.Length)] + rng.Next(1, 100);
                case 2: return h + rng.Next(1, 10000);
                case 3: return "xX_" + h + "_Xx";
                case 4: return char.ToUpperInvariant(h[0]) + h.Substring(1) + HandleTails[rng.Next(HandleTails.Length)] + rng.Next(80, 100);
                default: return "its_" + h;
            }
        }

        // ------------------------------------------------------------------ lines

        static readonly string[] Hello =
        {
            "hi from Kerbin City", "watching from the beach by the causeway!!", "hello from the hills inland", "first", "first time catching one live",
            "anyone else here from the island airfield?", "3am here, worth it", "hi chat", "hi mom", "skipping class for this", "watching at work, dont tell my boss",
            "ok who else has this on the big screen", "back again for another one", "just woke up what did i miss", "my whole family is watching this",
            "watching from the hospital waiting room, good distraction", "made popcorn for this", "here before it gets crowded", "greetings from the night side",
        };
        static readonly string[] OffTopic =
        {
            "what music is this", "does anyone know if the snack bar at KSC is open to visitors", "is this live or a replay?", "how do i get a job at the space center",
            "anyone know a good telescope for a beginner", "my cat is watching too", "my dog is scared of the TV now", "can you see this from Kerbin City?",
            "is the gift shop still selling the plush jeb", "what time is it there", "can someone explain what apoapsis means", "does the crew get snacks up there",
            "why is the chat so fast", "the sound is out of sync for me", "stream lagging for anyone else?", "720p gang", "is there a replay after", "who's paying for this",
            "how much does one of these cost", "will this be on the news tomorrow", "can they see us from up there", "what's the yellow stuff on the tanks",
        };
        static readonly string[] Jokes =
        {
            "MOAR BOOSTERS", "needs more struts", "did they remember the parachutes this time", "jeb is definitely holding the throttle with his foot",
            "somewhere an intern just pressed space by accident", "rapid unplanned disassembly speedrun any%", "the kraken is watching",
            "i too launch things into space every morning (my coffee mug)", "that rocket has more parts than my car", "explosions are just very fast learning",
            "plot twist: it's going to the Mun", "if this works i'm buying a snack", "i have 3 exams tomorrow and i'm here", "engineers with 4 cups of coffee rn",
            "ksc parking lot is going to be wild after this", "that's a lot of fuel for one kerbal", "they put a whole snack bar on that thing",
            "imagine being the guy who tightens the bolts", "every launch my heart rate does a gravity turn",
        };
        static readonly string[] Doubters =
        {
            "this is CGI", "taxes at work...", "could have fixed the roads with this money", "why do we need rockets when we have planes",
            "kerbin is flat and this proves nothing", "i'll believe it when they bring back a rock", "remember the last one lol",
            "funding this while my bus is always late", "just watching for the explosion tbh", "how many of these blew up this year?",
        };
        static readonly string[] Nerds =
        {
            "nice gravity turn", "looking very nominal", "that's a clean ascent profile", "throttle looks steady", "watching the g-load, looking good",
            "max q soon", "the shock diamonds in that plume", "TWR looks healthy", "pitching over right on schedule", "that's textbook",
            "they're going to burn a bit long to raise apoapsis i think", "telemetry looks good so far", "fuel use looks right for this stage",
            "not bad for a first stage", "ok that plume expansion as the air thins is beautiful",
        };
        static readonly string[] Kids =
        {
            "im 9 and this is the coolest thing ever", "my son is jumping on the couch", "my daughter wants to be an astronaut now", "WHEN IS IT GOING TO THE MUN",
            "my teacher put this on in class!!", "is there a kerbal inside???", "can i go next", "kiddo says hi to the crew",
        };
        static readonly string[] Memes =
        {
            "this is fine 🔥", "stonks", "rocket go up", "press F to pay respects", "big if true", "skill issue", "we're so back", "it's so over",
            "average tuesday at KSC", "no thoughts just rocket", "sheeeesh", "I am once again asking for more boosters", "main character energy",
            "certified kerbin moment", "they did the thing", "NPC behavior", "to the mun 🚀", "built different", "that's crazy bro", "real", "based",
            "jeb's mom watching rn", "not the kraken again 💀", "💀💀💀", "this rocket pays rent in my head", "average struts enjoyer", "ok boomer (booster)",
            "chat is this real", "W rocket", "L camera angle", "rocket said 👋", "bro is going to space", "lowkey peak", "it's giving orbit",
        };
        static readonly string[] Rude =
        {
            "this stream is boring", "who cares", "boring until it explodes", "mods are asleep post rockets", "shut up chat", "nobody asked",
            "you're all nerds", "cringe", "touch grass", "L stream", "cope", "imagine paying taxes for this lol", "worst camera work ever",
            "can the chat stop spamming", "this guy again 🙄", "stop asking about the music", "go back to school kid", "nerd", "ratio",
            "ok and?", "yawn", "my grandma builds better rockets", "skip to the explosion",
        };
        static readonly string[] RandomWords = { "pickles", "banana", "soup", "hello?", "potato", "toast", "yes", "no", "bruh", "wait", "huh", "ok", "lmao", "cheese", "goose", "aaaaa", "spoon", "?", "hm", "lol", "first", "moon", "snacks", "help", "wow" };
        static readonly string[] Yelling =
        {
            "WHY IS NOBODY TALKING ABOUT THE FUEL", "I LOVE THIS AGENCY", "SAY HI TO MY MOM", "GO FASTER", "THIS IS THE BEST DAY OF MY LIFE",
            "MODS BAN THIS GUY", "CAMERA MAN DO YOUR JOB", "I'M NOT CRYING YOU'RE CRYING", "EVERYBODY SHUT UP IT'S HAPPENING", "WHERE IS JEB",
            "HELLO FROM THE NIGHT SIDE", "MORE BOOSTERS", "WHO ELSE IS HERE AT 4AM", "STOP SPAMMING", "I CAN'T SEE ANYTHING", "TURN THE VOLUME UP",
        };
        /// <summary>A few viewers from abroad, as on any big public stream. Only used when the game's fonts can draw them.</summary>
        static readonly string[] Chinese = { "加油！", "牛逼", "太酷了", "哇哇哇", "冲冲冲", "火箭好帅", "大家好", "666", "哈哈哈哈", "厉害了", "起飞！", "好美", "第一次看直播", "成功了！", "太强了吧" };
        /// <summary>Set by the game when its fonts can draw emoji. KSP's cannot, so emoji become chat emote codes as on many stream sites.</summary>
        public static bool EmojiOk;

        static readonly Dictionary<string, string> EmojiCodes = new Dictionary<string, string>
        {
            { "\U0001F680", ":rocket:" }, { "\U0001F480", ":skull:" }, { "\U0001F525", ":fire:" }, { "\U0001F62D", ":sob:" }, { "\U0001F644", ":eyeroll:" },
            { "\U0001F44B", ":wave:" }, { "\U0001F602", ":joy:" }, { "\U0001F64F", ":pray:" }, { "\U0001F44F", ":clap:" }, { "\U0001F31D", ":moon:" },
            { "\U0001F315", ":moon:" }, { "\U0001F30D", ":planet:" }, { "\U0001F92F", ":mindblown:" }, { "\U0001F633", ":flushed:" }, { "\U0001F622", ":cry:" },
            { "\U0001F60D", ":heart_eyes:" }, { "\u2764\uFE0F", "<3" }, { "\u2764", "<3" }, { "\U0001F44D", ":thumbsup:" }, { "\U0001F4AF", ":100:" }, { "\U0001F389", ":tada:" },
        };

        /// <summary>Turns emoji into emote codes (:rocket:) when the game cannot draw them, and drops any other emoji.</summary>
        public static string Emotes(string text)
        {
            if (EmojiOk || string.IsNullOrEmpty(text) || !text.Any(c => char.IsSurrogate(c) || c == '\u2764')) return text;
            foreach (var kv in EmojiCodes) text = text.Replace(kv.Key, kv.Value);
            var sb = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsHighSurrogate(c)) { i++; continue; }
                if (char.IsSurrogate(c) || c == '\uFE0F' || c == '\u200D') continue;
                sb.Append(c);
            }
            string outText = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), " {2,}", " ").Trim();
            return outText == "" ? "o7" : outText;
        }

        /// <summary>Set by the game once it knows whether its fonts can show Chinese characters.</summary>
        public static bool CjkOk;
        /// <summary>Every Chinese character the offline lines use, so the game can load them into its font up front.</summary>
        public static string ChineseChars { get { return new string(string.Concat(Chinese).Where(c => c > 0x2E80).Distinct().ToArray()); } }

        static readonly string[] Spam = { "o7", "o7 o7 o7", "LETS GOOOO", "GO GO GO", "🚀🚀🚀", "W", "WWWWW", "HYPE", "!!!!!!", "gg", "POG", "Kerbin #1", "GODSPEED", "send it" };
        static readonly string[] Sad = { "F", "F in the chat", "o7", "no no no no", "oh no", "please be ok", "this is awful", "i can't watch", "is everyone ok??", "that's so sad", "pray for the crew", "my heart just dropped" };

        static readonly string[] PadLines =
        {
            "how long until launch", "it looks so small from this camera", "the steam coming off the tanks is so cool", "come onnn launch already",
            "the waiting is the worst part", "it looks so calm right now", "anyone have the countdown?", "first time seeing it on the pad live", "nervous for them",
        };
        static readonly string[] LiftoffLines =
        {
            "LIFTOFF", "LIFTOFF!!!!", "WE HAVE LIFTOFF", "GO GO GO", "LETS GOOOOOO", "LOOK AT IT GO", "goosebumps", "IM SHAKING", "that plume!!!",
            "the sound must be insane there", "i'm crying lol", "clear the tower!!", "THERES THE FIRE", "it's actually going",
            "I CANT BELIEVE THIS", "my whole house is cheering", "GODSPEED", "send it", "wow wow wow",
        };
        static readonly string[] AscentLines =
        {
            "look how fast it's going", "it's so small already", "where is it going", "is it supposed to tilt like that", "why is it going sideways? (serious question)",
            "@everyone it tilts on purpose it's a gravity turn", "the sky is getting dark", "speed going brrr", "that altitude number is going up so fast",
            "staging soon?", "smooth so far", "it looks like a star now", "camera person is doing their best", "is that a contrail?",
        };
        static readonly string[] StageLines =
        {
            "STAGING", "clean separation!", "bye bye booster", "there goes the first stage", "look at it tumble away", "nominal!!",
            "where does the booster land?", "is the booster coming back?", "see ya booster o7", "staging looked perfect",
        };
        static readonly string[] OnboardLines =
        {
            "ONBOARD CAMERA", "omg the view", "the hull cam is incredible", "look at the curve of kerbin", "you can see the coast!",
            "wallpaper material right there", "this view never gets old", "can they make the camera bigger", "the shaking on that camera lol",
        };
        static readonly string[] OrbitLines =
        {
            "WE'RE IN ORBIT", "orbit confirmed!!", "gg", "beautiful", "so peaceful up there", "imagine that view", "they did it",
            "historic", "chills", "o7 to everyone at mission control", "now do the Mun",
        };
        static readonly string[] QuietLines =
        {
            "so peaceful", "the coast phase is my favorite", "anyone still here?", "chat got quiet", "this is very relaxing",
            "lofi rocket to study to", "i could watch this for hours", "it's just floating lol", "waiting for the next burn",
        };

        static readonly string[] CountStartLines = { "CLOCK IS RUNNING", "here we gooo", "countdown started!!", "ok it's real now", "setting a timer", "anyone else's heart racing already", "grabbing snacks before it goes", "T-minus vibes", "they're actually doing it today" };
        static readonly string[] HoldLines = { "HOLD HOLD HOLD", "why are they holding", "nooooo a hold", "probably a sensor", "classic", "of course", "what happened??", "hold?? why", "it's always something", "they're being careful, that's good", "my snack is getting cold", "can we get an update on the hold?", "😭😭", "not again", "it's fine, holds are normal", "bruh" };
        static readonly string[] ResumeLines = { "WE'RE BACK", "count resumed!!", "LETS GOOO", "clock is moving again", "phew", "here we go again", "back on baby", "W" };
        static readonly string[] RecycleLines = { "recycling?? noooo", "back to square one", "here we go again lol", "groundhog day", "at this rate we launch next year", "they turned the clock BACK?", "my eyes hurt from waiting", "ok gonna make coffee", "patience chat", "every time" };
        static readonly string[] ScrubLines = { "SCRUBBED", "noooooo", "see you tomorrow", "boooo", "I skipped school for this", "better safe than sorry", "L", "F", "unbelievable", "well that's my evening ruined", "good call honestly", "is it rescheduled?", "classic KSC", "I'll be back tomorrow", "SCRUB SCRUB SCRUB", "😭", "at least it didn't blow up", "go home everyone" };
        static readonly string[] FinalTenLines = { "10", "9", "8", "7", "6", "5", "4", "3", "2", "1", "TEN", "NINE", "here it comes", "IT'S HAPPENING", "omg omg omg", "GO GO GO", "🚀🚀🚀", "hands shaking", "3", "2", "1" };
        static readonly string[] ZeroLines = { "NOW!!", "IGNITION?", "0!!", "GO", "is it going??", "come onnnn", "what's happening", "LIFTOFF??", "why isn't it moving" };
        static readonly string[] LateLines = { "uhh it's still there", "did they forget to press the button", "T-0 was ages ago lol", "is something wrong?", "anyone home?", "jeb fell asleep", "it's not moving", "awkward", "someone call mission control", "space bar broken?" };
        static readonly string[] SurpriseLines = { "WAIT IT'S GOING", "I THOUGHT THEY WERE HOLDING", "the countdown said otherwise lol", "um was that planned", "surprise launch!!", "the clock was wrong??", "WHAT", "ok then!!" };

        /// <summary>The part of the flight the stream is in, from the latest telemetry.</summary>
        public static string Phase(TelemetrySample t)
        {
            if (t == null) return "pad";
            if (t.Situation == "PRELAUNCH" || t.Situation == "LANDED" || t.Situation == "SPLASHED") return t.Altitude < 300 && t.Speed < 3 ? "pad" : "landed";
            if (t.Situation == "ORBITING" || t.Situation == "ESCAPING") return "orbit";
            return "ascent";
        }

        /// <summary>
        /// Offline chat: a few lines that answer what just happened, mixed with the usual drifting crowd. Each line may be
        /// roughed up the way people actually type.
        /// </summary>
        public static List<ChatMessage> Lines(StreamSession st, List<string> moments, int n, Random rng, string vesselName)
        {
            var outList = new List<ChatMessage>();
            string phase = Phase(st.Last);
            double hype = moments.Sum(m => NewsEngine.MomentWeight(m));
            foreach (var m in moments)
            {
                string ml = (m ?? "").ToLowerInvariant();
                string[] pool = ml.StartsWith("scrub") ? ScrubLines
                    : ml.StartsWith("hold") ? HoldLines
                    : ml.Contains("countdown resumed") ? ResumeLines
                    : ml.Contains("recycled") ? RecycleLines
                    : ml.Contains("countdown started") ? CountStartLines
                    : ml.Contains("final countdown") ? null
                    : ml.StartsWith("t-0 was") ? LateLines
                    : ml.StartsWith("t-0") ? ZeroLines
                    : ml.StartsWith("liftoff") && (ml.Contains("early") || ml.Contains("hold") || ml.Contains("scrubbed")) ? SurpriseLines
                    : ml.StartsWith("t-") ? new[] { "getting close", "almost!!", "ok ok ok", "nervous", "here we go", "chat is going to explode soon", "hype" }
                    : ml.Contains("crew lost") || ml.Contains("destroyed") || ml.Contains("explo") ? Sad
                    : ml.Contains("liftoff") ? LiftoffLines
                    : ml.Contains("stage") || ml.Contains("separat") ? StageLines
                    : ml.Contains("onboard") ? OnboardLines
                    : ml.Contains("orbit") ? OrbitLines : null;
                if (ml.Contains("final countdown"))
                {
                    // The chat counts down with the clock, out of step, as people type at different speeds.
                    for (int i = 10; i >= 1; i--) { outList.Add(Msg(rng, rng.Next(4) == 0 ? i + "!!" : i.ToString())); if (rng.Next(3) == 0) outList.Add(Msg(rng, i.ToString())); }
                    foreach (var line in FinalTenLines.Where(x => !char.IsDigit(x[0])).OrderBy(x => rng.Next()).Take(4)) outList.Insert(rng.Next(outList.Count + 1), Msg(rng, line));
                    continue;
                }
                if (pool == null) { outList.Add(Msg(rng, Pick(rng, "did everyone see that??", "wait what just happened", "what was that", "uhh"))); continue; }
                int k = Math.Min(pool.Length, 3 + (int)NewsEngine.MomentWeight(m));
                foreach (var line in pool.OrderBy(x => rng.Next()).Take(k)) outList.Add(Msg(rng, Rough(line, rng, true)));
                if (pool != Sad) for (int i = 0; i < 2; i++) outList.Add(Msg(rng, Spam[rng.Next(Spam.Length)]));
            }
            // The crowd: what people talk about depends on the phase, with a long random tail of everything else.
            var phaseLines = phase == "pad" ? PadLines : phase == "orbit" ? OrbitLines.Concat(QuietLines).ToArray() : phase == "ascent" ? AscentLines.Concat(Nerds).ToArray() : QuietLines;
            var mix = new List<string[]> { phaseLines, phaseLines, Hello, OffTopic, Jokes, Doubters, Nerds, Kids, phaseLines, Memes, Memes, Rude, RandomWords, Yelling, phaseLines, Hello };
            if (CjkOk) mix.Add(Chinese);
            if (st.WentOnboard && phase != "pad") mix.Add(OnboardLines);
            var used = new HashSet<string>(st.Chat.Skip(Math.Max(0, st.Chat.Count - 60)).Select(c => c.Text.ToLowerInvariant()));
            int guard = 0;
            while (outList.Count < n && guard++ < n * 6)
            {
                var pool = mix[rng.Next(mix.Count)];
                string line = pool[rng.Next(pool.Length)];
                if (line.Contains("{vessel}")) line = line.Replace("{vessel}", vesselName);
                if (used.Contains(line.ToLowerInvariant())) continue;
                used.Add(line.ToLowerInvariant());
                outList.Add(Msg(rng, Rough(line, rng, hype >= 2)));
            }
            if (rng.Next(4) == 0 && vesselName != "") outList.Insert(rng.Next(outList.Count + 1), Msg(rng, Rough(Pick(rng, "go " + vesselName + "!", vesselName + " looking good", "GO " + vesselName.ToUpperInvariant(), "is " + vesselName + " crewed?"), rng, hype >= 2)));
            return outList;
        }

        static ChatMessage Msg(Random rng, string text) { return new ChatMessage { Author = Handle(rng), Text = text }; }

        static string Pick(Random rng, params string[] xs) { return xs[rng.Next(xs.Length)]; }

        /// <summary>Types it like a person in a fast chat: lowercase, stretched letters when excited, the odd typo or extra "lol".</summary>
        public static string Rough(string line, Random rng, bool excited)
        {
            if (string.IsNullOrEmpty(line) || line.Any(c => c > 0x2000)) return line;   // leave emoji and Chinese lines alone
            if (line == line.ToUpperInvariant() && line.Any(char.IsLetter) && line.Length > 12) return line;   // yelling stays yelling
            int roll = rng.Next(100);
            if (excited && roll < 25) line = line.ToUpperInvariant();
            else if (roll < 45) line = line.ToLowerInvariant();
            if (excited && rng.Next(3) == 0) line = Stretch(line, rng);
            if (rng.Next(12) == 0 && line.Length > 6) line = Typo(line, rng);
            if (rng.Next(9) == 0) line += Pick(rng, " lol", " lmao", " haha", "!!", "!!!", " fr", " ngl");
            return line;
        }

        static string Stretch(string s, Random rng)
        {
            for (int i = s.Length - 1; i >= 0; i--)
                if ("aeiouAEIOU".IndexOf(s[i]) >= 0) return s.Substring(0, i) + new string(s[i], 2 + rng.Next(5)) + s.Substring(i + 1);
            return s;
        }

        static string Typo(string s, Random rng)
        {
            int i = 1 + rng.Next(s.Length - 3);
            if (!char.IsLetter(s[i]) || !char.IsLetter(s[i + 1])) return s;
            return s.Substring(0, i) + s[i + 1] + s[i] + s.Substring(i + 2);
        }

        // ------------------------------------------------------------------ guidance for the AI

        static readonly string[] Voices =
        {
            "someone saying where they are watching from (a Kerbin town, the beach, the night side, work, school)",
            "someone up very late or watching at work or in class",
            "a kid, or a parent talking about their kid",
            "an off-topic question that has nothing to do with the flight (the gift shop, music, a telescope, a pet)",
            "a stream-quality complaint (lag, audio delay, low resolution)",
            "a doubter or troll (CGI, taxes, roads, 'kerbin is flat') and someone answering them",
            "an engineer-type reading the telemetry precisely",
            "someone who doesn't understand what they're seeing and asks a basic question, and someone explaining it badly",
            "a running joke from the community (MOAR BOOSTERS, struts, the kraken, snacks, jeb's throttle foot)",
            "a short emote or one-word reaction (o7, W, gg, 🚀)",
            "someone repeating, or slightly changing, a message from a few lines above",
            "a long, heartfelt message among the short ones",
            "a regular who comes to every stream and greets others",
            "someone tagging a friend or replying to another viewer by @name",
            "someone being wrong with total confidence",
            "a message with a typo or all in lowercase with no punctuation",
            "someone asking where the crew is going or if anyone is inside",
            "a sarcastic or dry joke",
            "an internet meme phrase (stonks, skill issue, we're so back, 💀, this is fine, chat is this real)",
            "someone mildly rude or dismissive (who cares, boring, touch grass, nerd, ratio), no slurs and nothing hateful",
            "a single random word that means nothing (soup, pickles, bruh, ?)",
            "someone YELLING IN ALL CAPS",
        };

        /// <summary>A fresh, random set of voices and a few sample lines for this batch, so the AI does not settle into one tone.</summary>
        public static string Hints(Random rng, StreamSession st)
        {
            var voices = Voices.OrderBy(x => rng.Next()).Take(6).ToList();
            if (CjkOk && rng.Next(3) == 0) voices.Add("a viewer or two writing in Chinese (short, like 加油 or 太酷了)");
            string phase = Phase(st.Last);
            var pools = new List<string[]> { Hello, OffTopic, Jokes, Doubters, Nerds, Kids, Memes, Rude, Yelling, phase == "pad" ? PadLines : phase == "orbit" ? OrbitLines : AscentLines };
            var samples = pools.OrderBy(x => rng.Next()).Take(4).Select(p => p[rng.Next(p.Length)]).ToList();
            return "Mix in, this batch: " + string.Join("; ", voices.ToArray()) + ".\nThe tone of real chat, for style only (do not reuse these lines): \"" + string.Join("\", \"", samples.ToArray()) + "\".";
        }
    }
}
