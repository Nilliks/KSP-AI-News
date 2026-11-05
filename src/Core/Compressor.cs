using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>
    /// Keeps prompts short as a career grows. Older lore entries and past coverage are folded
    /// into a running "archive summary" (written by the cheap model, or trimmed offline), and a
    /// very long agency profile is summarized once and cached until the player edits it.
    /// </summary>
    public static class Compressor
    {
        public const int LoreCharBudget = 3000;
        public const int StoryBudget = 20;
        public const int KeepRecentStories = 5;
        public const int KeepRecentLore = 4;
        public const int ProfileCharBudget = 6000;
        public const int SummaryMaxChars = 4000;

        public static List<LoreEntry> FreshLore(NewsState s, double now)
        {
            return s.Lore.Where(l => l.UT > s.MemoryUpToUT && l.UT <= now).OrderBy(l => l.UT).ToList();
        }

        /// <summary>One headline per published story that is not yet in the archive summary.</summary>
        public static List<Article> FreshStories(NewsState s, double now)
        {
            return s.Articles.Where(a => a.PublishedUT > s.MemoryUpToUT && a.PublishedUT <= now)
                .GroupBy(a => a.StoryId)
                .Select(g => g.OrderByDescending(a => a.Outlet == "herald" ? 1 : 0).ThenBy(a => a.PublishedUT).First())
                .OrderBy(a => a.PublishedUT).ToList();
        }

        public static bool MemoryNeedsCompression(NewsState s, double now)
        {
            int loreChars = FreshLore(s, now).Sum(l => l.Text.Length);
            return loreChars > LoreCharBudget || FreshStories(s, now).Count > StoryBudget;
        }

        /// <summary>The point up to which we fold things in, leaving the most recent items verbatim.</summary>
        public static double CutPoint(NewsState s, double now)
        {
            var stories = FreshStories(s, now);
            var lore = FreshLore(s, now);
            double cut = s.MemoryUpToUT;
            if (stories.Count > KeepRecentStories) cut = Math.Max(cut, stories[stories.Count - KeepRecentStories - 1].PublishedUT);
            if (lore.Count > KeepRecentLore) cut = Math.Max(cut, lore[lore.Count - KeepRecentLore - 1].UT);
            return cut;
        }

        static string Material(NewsState s, double cut, IGameClock clock)
        {
            var sb = new StringBuilder();
            foreach (var l in s.Lore.Where(l => l.UT > s.MemoryUpToUT && l.UT <= cut).OrderBy(l => l.UT))
                sb.AppendLine("- LORE " + clock.FormatDate(l.UT) + ": " + l.Text);
            foreach (var a in s.Articles.Where(a => a.PublishedUT > s.MemoryUpToUT && a.PublishedUT <= cut)
                         .GroupBy(a => a.StoryId).Select(g => g.OrderByDescending(a => a.Significance).First()).OrderBy(a => a.PublishedUT))
                sb.AppendLine("- NEWS " + clock.FormatDate(a.PublishedUT) + " (significance " + a.Significance.ToString("0") + "): " + a.Headline + (a.Subhead != "" ? ". " + a.Subhead : ""));
            return sb.ToString();
        }

        public static string MemorySystemPrompt()
        {
            return "You are the archivist of the Kerbin press. You maintain a compact running summary of everything the public knows about a space agency. " +
                   "Merge the previous summary with the new items into one updated summary of at most 350 words. Keep every name, date, first, record, failure and loss of crew; " +
                   "drop routine repeats into counts (\"six more uncrewed launches\"). Lore items are canon and must be preserved in meaning. " +
                   "Reply with JSON only: {\"summary\":\"...\"}";
        }

        public static string MemoryUserPrompt(NewsState s, double cut, IGameClock clock)
        {
            return "PREVIOUS SUMMARY:\n" + (s.MemorySummary == "" ? "(none yet)" : s.MemorySummary) + "\n\nNEW ITEMS:\n" + Material(s, cut, clock);
        }

        public static bool TryReadSummary(string text, out string summary)
        {
            summary = null;
            object root;
            var json = ResponseParser.ExtractJson(text);
            if (json == null || !Json.TryParse(json, out root)) return false;
            summary = (Json.Str(root, "summary", "") ?? "").Trim();
            if (summary.Length < 20) return false;
            if (summary.Length > SummaryMaxChars) summary = summary.Substring(0, SummaryMaxChars);
            return true;
        }

        public static void ApplyMemory(NewsState s, double cut, string summary)
        {
            s.MemorySummary = summary;
            s.MemoryUpToUT = cut;
        }

        /// <summary>Offline fallback: keep a dated list of the most important headlines, trimmed to budget.</summary>
        public static void CompressOffline(NewsState s, double cut, IGameClock clock)
        {
            var lines = new List<string>();
            if (s.MemorySummary != "") lines.AddRange(s.MemorySummary.Split('\n'));
            lines.AddRange(Material(s, cut, clock).Split('\n').Where(l => l.Trim() != ""));
            // Drop the least important NEWS lines first until it fits; lore lines are canon and stay longest.
            while (string.Join("\n", lines.ToArray()).Length > SummaryMaxChars && lines.Count > 1)
            {
                int idx = lines.FindIndex(l => l.StartsWith("- NEWS"));
                lines.RemoveAt(idx >= 0 ? idx : 0);
            }
            ApplyMemory(s, cut, string.Join("\n", lines.ToArray()));
        }

        // ---------------------------------------------------------- profile

        public static string ProfileRaw(AgencyProfile p)
        {
            return string.Join("\n", new[] { p.Founding, p.Mission, p.Politics, p.Reputation, p.KeyPeople, p.WorldLore, p.HouseRules });
        }

        public static string ProfileHash(AgencyProfile p) { return OfflineWriter.StableHash(ProfileRaw(p)).ToString(); }

        public static bool ProfileNeedsSummary(NewsState s)
        {
            return ProfileRaw(s.Profile).Length > ProfileCharBudget && s.ProfileSummaryHash != ProfileHash(s.Profile);
        }

        public static bool ProfileSummaryValid(NewsState s)
        {
            return s.ProfileSummary != "" && s.ProfileSummaryHash == ProfileHash(s.Profile);
        }

        public static string ProfileSystemPrompt()
        {
            return "You compress a space agency's background notes for a newsroom. Keep every fact, name and rule; remove repetition and flourish. " +
                   "House rules must be kept word for word. At most 600 words. Reply with JSON only: {\"summary\":\"...\"}";
        }

        public static string ProfileUserPrompt(NewsState s)
        {
            var p = s.Profile;
            return "Founding story: " + p.Founding + "\nMission and goals: " + p.Mission + "\nFunding and politics: " + p.Politics +
                   "\nPublic reputation: " + p.Reputation + "\nKey people: " + p.KeyPeople + "\nWorld lore: " + p.WorldLore;
        }
    }
}
