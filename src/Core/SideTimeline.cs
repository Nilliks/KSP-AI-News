using System;
using System.Collections.Generic;
using System.Linq;

namespace KerbalNews
{
    /// <summary>
    /// FMRS (Flight Manager for Reusable Stages) flies dropped stages in a side timeline: it loads an earlier save, the player
    /// lands the booster, then FMRS loads the main save again. Anything KNN did there would be thrown away, so while in a side
    /// timeline it makes no AI calls, writes no news, and only keeps the booster's own landing or recovery, which it reports
    /// once the main timeline is back.
    /// </summary>
    public partial class NewsEngine
    {
        /// <summary>Set by the KSP layer: true while FMRS has switched to a dropped stage.</summary>
        public Func<bool> SideTimeline = () => false;
        /// <summary>Set by the KSP layer: events to drop entirely (a vessel being moved by VesselMover).</summary>
        public Func<NewsEvent, bool> SuppressEvent = e => false;
        readonly List<NewsEvent> sideCarry = new List<NewsEvent>();
        public int SideCarryCount { get { return sideCarry.Count; } }

        static readonly HashSet<string> Carryable = new HashSet<string> { EventTypes.Landing, EventTypes.Splashdown, EventTypes.Recovery };

        bool InSide()
        {
            try { return SideTimeline(); } catch { return false; }
        }

        /// <summary>True when the event was held back (side timeline).</summary>
        bool HoldForSide(NewsEvent e)
        {
            if (!InSide()) return false;
            if (e != null && Carryable.Contains(e.Type) && sideCarry.Count < 20 && !sideCarry.Any(x => x.Type == e.Type && x.VesselId == e.VesselId))
                sideCarry.Add(e);
            return true;
        }

        /// <summary>Back on the main timeline: report what the boosters did.</summary>
        void FlushSide()
        {
            if (sideCarry.Count == 0 || InSide()) return;
            var list = sideCarry.ToList();
            sideCarry.Clear();
            foreach (var e in list) Ingest(e);
        }
    }
}
