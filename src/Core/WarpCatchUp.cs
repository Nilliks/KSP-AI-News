using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>
    /// Time warp: every automatic AI call waits until the warp is over (and the player has stopped warping for a few seconds,
    /// so chained warps count as one). After a long warp the backlog is trimmed: a few top stories are written on their own,
    /// the rest go into one "while you were away" roundup instead of one call each.
    /// </summary>
    public partial class NewsEngine
    {
        public const double WarpSettleSeconds = 6;
        public const int CatchUpKeep = 3;
        double warpFromUT = -1, lastHoldReal = double.NegativeInfinity;
        public bool WarpHold { get; private set; }

        /// <summary>Turns the KSP layer's "calls allowed" into the real answer, and runs the catch-up when a warp ends.</summary>
        bool WarpGate(double ut, double realTime, bool allowCalls)
        {
            if (!allowCalls)
            {
                if (warpFromUT < 0) warpFromUT = ut;
                lastHoldReal = realTime;
                WarpHold = true;
                return false;
            }
            if (realTime - lastHoldReal < WarpSettleSeconds) { WarpHold = true; return false; }
            WarpHold = false;
            if (warpFromUT >= 0)
            {
                double from = warpFromUT;
                warpFromUT = -1;
                if (ut - from >= Day) CatchUp(from, ut);
            }
            return true;
        }

        /// <summary>Trims what piled up during a warp. Returns how many stories went into the roundup.</summary>
        public int CatchUp(double fromUT, double ut)
        {
            var due = State.Stories.Where(s => s.Status == StoryStatus.Pending && s.PublishUT <= ut && !IsInFlight(s)).ToList();
            if (due.Count <= 1) return 0;
            int merged = 0;
            // Quiet-day pieces written for days that are long gone: keep only the newest.
            var fillers = due.Where(s => s.Kind == "filler" && s.Special == "").OrderByDescending(s => s.CreatedUT).ToList();
            foreach (var f in fillers.Skip(1)) { State.Stories.Remove(f); due.Remove(f); }
            // Several anniversaries or reviews: keep the newest of each kind.
            foreach (var kind in new[] { "retrospective", "review" })
                foreach (var f in due.Where(s => s.Kind == "filler" && s.Special == kind).OrderByDescending(s => s.CreatedUT).Skip(1).ToList())
                { State.Stories.Remove(f); due.Remove(f); }

            var news = due.Where(s => s.Kind != "filler").OrderByDescending(s => s.Significance).ToList();
            if (news.Count <= CatchUpKeep) return 0;
            var rest = news.Skip(CatchUpKeep).OrderBy(s => s.CreatedUT).ToList();
            var lines = new List<string>();
            foreach (var s in rest)
            {
                string what = s.Events.Count > 0 ? string.Join(" ", s.Events.Take(3).Select(PromptBuilder.Describe).ToArray()) : s.Topic;
                if (what.Trim() == "") continue;
                lines.Add(Clock.FormatDate(s.CreatedUT) + ": " + ResponseParser.Clip(what, 300));
                // Still part of the public memory (archive, tracker), just not written up on its own.
                Finalize(s, new GeneratedCoverage(), new CoveragePlan { Tier = "none" }, ut);
                merged++;
            }
            if (lines.Count == 0) return 0;
            State.Stories.Add(new Story
            {
                Id = State.NewId("s"), Kind = "filler", Special = "catchup", FillerOutlet = FirstOutletOn("herald", "observer", "munmonthly") ?? "herald",
                Topic = "Between " + Clock.FormatDate(fromUT) + " and " + Clock.FormatDate(ut) + ":\n- " + string.Join("\n- ", lines.Take(25).ToArray()),
                CreatedUT = ut, PublishUT = ut, Significance = Math.Min(70, 30 + lines.Count * 3),
            });
            return merged;
        }
    }
}
