using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>A kerbal's stats from the game roster, refreshed by the KSP layer.</summary>
    public class CrewProfile
    {
        public string Name = "";
        public string Trait = "";        // Pilot, Engineer, Scientist, Tourist (or a modded trait)
        public int Level;
        public double Courage;          // 0-1
        public double Stupidity;        // 0-1
        public bool Badass;
        public bool Veteran;
        public string Gender = "";
        /// <summary>available, assigned (in flight), dead or missing.</summary>
        public string Status = "available";
        public string VesselName = "";
        /// <summary>Plain words: "in orbit of the Mun", "landed on Minmus".</summary>
        public string Where = "";
    }

    public partial class NewsEngine
    {
        public const int CrewChatKeep = 40;
        public const int CrewMemoryBudget = 1800;
        public string CrewChatBusy = "";
        int crewRun;

        /// <summary>The KSP layer reports the roster now and then. Unknown kerbals are added, so the whole crew can be talked to.</summary>
        public void SyncCrew(CrewProfile p)
        {
            if (p == null || string.IsNullOrEmpty(p.Name)) return;
            var c = Fame(p.Name);
            c.Trait = p.Trait; c.Level = p.Level; c.Courage = p.Courage; c.Stupidity = p.Stupidity; c.Badass = p.Badass; c.Veteran = p.Veteran;
            c.Gender = p.Gender; c.Where = p.Where; c.VesselName = p.VesselName; c.RosterStatus = p.Status;
            c.OnRoster = true;
            if ((p.Status == "dead" || p.Status == "missing") && !c.Dead && c.Flights > 0) { c.Dead = true; c.DiedUT = c.DiedUT < 0 ? c.LastFlightUT : c.DiedUT; }
        }

        /// <summary>Plain-English personality from the stats, for the prompt.</summary>
        public static string Personality(CrewFame c)
        {
            var bits = new List<string>();
            bits.Add(c.Courage >= 0.8 ? "fearless, almost reckless" : c.Courage >= 0.55 ? "brave and steady" : c.Courage >= 0.3 ? "cautious, gets nervous about risky flights" : "openly anxious about danger, needs reassurance");
            bits.Add(c.Stupidity >= 0.75 ? "very dim and easily confused, takes things literally, cheerful about it" : c.Stupidity >= 0.5 ? "not the sharpest, misreads technical things now and then" : c.Stupidity >= 0.25 ? "sensible and practical" : "very sharp, analytical, likes numbers and details");
            if (c.Badass) bits.Add("a famously unflappable badass who laughs at danger");
            if (c.Veteran) bits.Add("one of the agency's original veterans, proud of it");
            switch ((c.Trait ?? "").ToLowerInvariant())
            {
                case "pilot": bits.Add("thinks like a pilot: flying, handling, the feel of the craft"); break;
                case "engineer": bits.Add("thinks like an engineer: parts, fuel, what could break and how to fix it"); break;
                case "scientist": bits.Add("thinks like a scientist: experiments, data, curiosity about every rock"); break;
                case "tourist": bits.Add("a paying tourist, not crew: excited, out of their depth"); break;
            }
            return string.Join("; ", bits.ToArray());
        }

        string CrewSystem(CrewFame c)
        {
            var sb = new StringBuilder();
            string agency = AgencyName;
            sb.AppendLine("You are " + c.Name + ", a kerbal " + (c.Trait != "" ? c.Trait.ToLowerInvariant() : "astronaut") + " (level " + c.Level + ") at " + agency + ", a space agency in the Kerbin universe (kerbals, the Mun, Minmus, Duna; currency is funds, written √). You are chatting privately with the agency's director, your boss.");
            sb.AppendLine("Personality (stay in character, it comes from your stats): " + Personality(c) + ".");
            sb.AppendLine("Speak in first person, casually, like a real astronaut texting their boss: short replies (one to four sentences), your own opinions and feelings, kerbal humour where it fits. Refer to your real memories below when relevant, never invent flights, deaths or events that are not listed. If you do not remember something, say so. You cannot do things in the game; you can only talk.");
            sb.AppendLine("Tone: " + PromptBuilder.SillinessText(State.Silliness));
            sb.AppendLine("If you have an interview or press briefing coming up and the director tells you what to say, stress, avoid or keep secret, acknowledge it in character (a nervous kerbal worries, a dim one may garble it, a badass may grumble) and put the instruction in \"coaching\".");
            sb.AppendLine("Reply with one JSON object only: {\"reply\": \"what you say\", \"remember\": \"one short note worth remembering from this exchange (a promise, a feeling, a fact about the director), or empty\", \"coaching\": \"the director's instruction for your upcoming press appearance, in their words, or empty\"}.");
            return sb.ToString();
        }

        string CrewUser(CrewFame c, double ut)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Today: " + Clock.FormatDate(ut) + ".");
            if (c.Dead) sb.AppendLine("(You died" + (c.DiedOn != "" ? " aboard " + c.DiedOn : "") + ".)");
            else if (c.RosterStatus == "assigned" && c.VesselName != "") sb.AppendLine("Right now you are aboard " + c.VesselName + (c.Where != "" ? ", " + c.Where : "") + ", talking over the radio.");
            else sb.AppendLine("Right now you are at the space center.");
            sb.AppendLine("Your record: " + (c.Flights == 0 ? "no flights yet (a rookie)" : c.Flights + " flight" + (c.Flights == 1 ? "" : "s")) + "; the public sees you as " + c.Tier + ".");
            if (c.Highlights.Count > 0) sb.AppendLine("Career highlights: " + string.Join("; ", c.Highlights.Select(h => HighlightText(h, Clock)).ToArray()) + ".");
            if (c.Log.Count > 0) sb.AppendLine("What you have been through (oldest first):\n- " + string.Join("\n- ", c.Log.ToArray()));
            if (c.Scandals.Count > 0) sb.AppendLine("Tabloid gossip about you: " + string.Join("; ", c.Scandals.ToArray()));
            var press = State.PublishedArticles(ut).Where(a => a.Headline.Contains(c.Name) || a.Headline.Contains(FirstName(c.Name))).OrderByDescending(a => a.PublishedUT).Take(3).ToList();
            if (press.Count > 0) sb.AppendLine("Recent press about you: " + string.Join("; ", press.Select(a => "\"" + a.Headline + "\" (" + Outlets.NameOf(a.Outlet) + ")").ToArray()) + ".");
            var crewmates = State.Crew.Values.Where(o => o != c && o.Vessels.Overlaps(c.Vessels)).Select(o => o.Name + (o.Dead ? " (lost)" : "")).Take(6).ToList();
            if (crewmates.Count > 0) sb.AppendLine("People you have flown with: " + string.Join(", ", crewmates.ToArray()) + ".");
            foreach (var ap in UpcomingFor(c.Name))
                sb.AppendLine("COMING UP: " + (ap.Kind == "interview" ? "an interview with " + Outlets.NameOf(ap.Outlets.Split(',')[0]) : "a press briefing") + " on " + Clock.FormatDate(ap.AtUT) + " about " + ap.Topic + "." + (ap.Coaching.Count > 0 ? " The director's instructions so far: " + string.Join("; ", ap.Coaching.ToArray()) + "." : " The director has not told you what to say yet."));
            if (c.MemorySummary != "") sb.AppendLine("Your memory of earlier talks with the director: " + c.MemorySummary);
            if (c.Notes.Count > 0) sb.AppendLine("Things you noted from recent talks: " + string.Join("; ", c.Notes.ToArray()));
            sb.AppendLine();
            sb.AppendLine("The conversation so far (newest last):");
            foreach (var m in c.Chat.Skip(Math.Max(0, c.Chat.Count - 14)))
                sb.AppendLine((m.Role == "player" ? "Director" : m.Role == "note" ? "(Agency note)" : FirstName(c.Name)) + ": " + m.Text);
            sb.AppendLine();
            sb.AppendLine("Reply as " + FirstName(c.Name) + ".");
            return sb.ToString();
        }

        static string FirstName(string name) { int i = (name ?? "").IndexOf(' '); return i > 0 ? name.Substring(0, i) : name ?? ""; }

        public AssistantMessage CrewLine(CrewFame c, string role, string text, double ut)
        {
            var m = new AssistantMessage { Id = State.NewId("c"), Role = role, Text = text ?? "", UT = ut };
            c.Chat.Add(m);
            if (c.Chat.Count > CrewChatKeep) c.Chat.RemoveRange(0, c.Chat.Count - CrewChatKeep);
            RaiseCommunity();
            return m;
        }

        /// <summary>The director says something to a kerbal. One AI call for the answer; a second, rare one condenses old memories.</summary>
        public bool CrewSend(string name, string text, double ut)
        {
            text = (text ?? "").Trim();
            CrewFame c;
            if (text == "" || string.IsNullOrEmpty(name) || !State.Crew.TryGetValue(name, out c)) return false;
            if (c.Dead || !Settings.CrewChatOn) return false;
            CrewLine(c, "player", ResponseParser.Clip(text, 1200), ut);
            if (c.RosterStatus == "frozen")
            {
                CrewLine(c, "crew", "(" + FirstName(c.Name) + " is frozen in a DeepFreeze pod and can't talk until thawed.)", ut);
                return true;
            }
            if (InSide())
            {
                CrewLine(c, "crew", "(You're flying a dropped stage with FMRS, and this talk would be forgotten when you jump back. Talk again on the main flight.)", ut);
                return true;
            }
            if (!Settings.CanCallAi)
            {
                CrewLine(c, "crew", OfflineCrewReply(c), ut);
                return true;
            }
            int run = ++crewRun;
            CrewChatBusy = c.Name;
            CrewStep(run, c, ut, 0);
            return true;
        }

        string OfflineCrewReply(CrewFame c)
        {
            if (c.RosterStatus == "assigned") return "(Static on the radio. Turn the AI on in Settings to talk to your crew.)";
            return "(" + FirstName(c.Name) + " is out on the training grounds. Turn the AI on in Settings to talk to your crew.)";
        }

        void CrewStep(int run, CrewFame c, double ut, int failures)
        {
            AskJson(Settings.CheapModel, CrewSystem(c), CrewUser(c, ut), 1200, 0, (root, err, text, res) =>
            {
                if (run != crewRun) return;
                // A plain-text answer is still an answer.
                if (root == null && text.Trim() != "" && !text.Contains("{")) root = new Dictionary<string, object> { { "reply", text.Trim() } };
                if (root == null)
                {
                    LastError = err ?? "No reply";
                    if (failures < 1 && !IsPermanent(res)) { CrewStep(run, c, ut, failures + 1); return; }
                    CrewChatBusy = "";
                    CrewLine(c, "crew", "(The line crackles and drops. Try again in a moment.)", ut);
                    return;
                }
                CrewChatBusy = "";
                string reply = Json.Str(root, "reply", "").Trim();
                string remember = Json.Str(root, "remember", "").Trim();
                string coaching = Json.Str(root, "coaching", "").Trim();
                var next = UpcomingFor(c.Name).FirstOrDefault();
                if (coaching.Length > 3 && next != null && next.Coaching.Count < 12) next.Coaching.Add(ResponseParser.Clip(coaching, 300));
                CrewLine(c, "crew", reply == "" ? "..." : ResponseParser.Clip(reply, 1500), ut);
                if (remember != "" && remember.Length > 3)
                {
                    c.Notes.Add(Clock.FormatDate(ut) + ": " + ResponseParser.Clip(remember, 200));
                    if (c.Notes.Sum(n => n.Length) + c.MemorySummary.Length > CrewMemoryBudget) CompressCrewMemory(c);
                }
            });
        }

        /// <summary>Folds the oldest notes into one short summary (one cheap call; offline it keeps the newest notes).</summary>
        void CompressCrewMemory(CrewFame c)
        {
            int take = Math.Max(1, c.Notes.Count - 4);
            var old = c.Notes.Take(take).ToList();
            if (!Settings.CanCallAi)
            {
                c.MemorySummary = ResponseParser.Clip((c.MemorySummary + " " + string.Join(" ", old.ToArray())).Trim(), 900);
                c.Notes.RemoveRange(0, take);
                return;
            }
            string sys = "You condense a kerbal astronaut's memories of conversations with their boss. Keep promises, strong feelings, opinions and facts; drop small talk. Write in first person, at most 120 words. Reply with JSON: {\"summary\": \"...\"}.";
            string user = "Earlier summary: " + (c.MemorySummary == "" ? "(none)" : c.MemorySummary) + "\nNew notes:\n- " + string.Join("\n- ", old.ToArray());
            AskJson(Settings.CheapModel, sys, user, 600, 0, (root, err, text, res) =>
            {
                if (root == null) return;
                string sum = Json.Str(root, "summary", "").Trim();
                if (sum == "") return;
                c.MemorySummary = ResponseParser.Clip(sum, 900);
                foreach (var o in old) c.Notes.Remove(o);
            });
        }

        /// <summary>Lets the director strike a memory (by note text index, or "all").</summary>
        public void CrewForget(string name, string which)
        {
            CrewFame c;
            if (!State.Crew.TryGetValue(name ?? "", out c)) return;
            int i;
            if (which == "all") { c.Notes.Clear(); c.MemorySummary = ""; c.Chat.Clear(); }
            else if (which == "summary") c.MemorySummary = "";
            else if (int.TryParse(which, NumberStyles.Integer, CultureInfo.InvariantCulture, out i) && i >= 0 && i < c.Notes.Count) c.Notes.RemoveAt(i);
        }

        /// <summary>Adds a line to a kerbal's life log, newest last, capped.</summary>
        static void LifeLog(CrewFame c, string line)
        {
            if (c.Log.Count > 0 && c.Log[c.Log.Count - 1] == line) return;
            c.Log.Add(line);
            if (c.Log.Count > 30) c.Log.RemoveAt(0);
        }
    }
}
