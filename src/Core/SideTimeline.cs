using System;
using System.Collections.Generic;
using System.Linq;

namespace KerbalNews
{
    /// <summary>
    /// FMRS (Flight Manager for Reusable Stages) flies dropped stages in a side timeline: it loads an earlier save, the player
    /// lands the booster, then FMRS loads the main save again and merges the booster into it. News written in the side
    /// timeline would be thrown away with it, so while there KNN makes no news calls and writes no stories. What the booster
    /// itself did (landed, splashed down, was recovered, was destroyed, lost its crew) is held in memory and reported once
    /// the main timeline is back, together with the booster's entry in the list of tracked craft. A livestream is not part
    /// of the save and simply carries on (see Stream.cs).
    /// </summary>
    public partial class NewsEngine
    {
        /// <summary>Set by the KSP layer: true while FMRS has switched to a dropped stage.</summary>
        public Func<bool> SideTimeline = () => false;
        /// <summary>Set by the KSP layer: the id of the dropped stage being flown in the side timeline, or null.</summary>
        public Func<string> SideVesselId = () => null;
        /// <summary>Set by the KSP layer: the name of the main vessel the dropped stage came from, or null.</summary>
        public Func<string> SideMainName = () => null;
        /// <summary>Set by the KSP layer: a few words on where the dropped stage is, such as "2.1 km from the launch site", or null.</summary>
        public Func<string> SideWhere = () => null;
        /// <summary>Set by the KSP layer: events to drop entirely (a vessel being moved by VesselMover).</summary>
        public Func<NewsEvent, bool> SuppressEvent = e => false;
        readonly List<NewsEvent> sideCarry = new List<NewsEvent>();
        readonly Dictionary<string, TrackedVessel> sideTracked = new Dictionary<string, TrackedVessel>();
        readonly List<StreamArchive> sideArchives = new List<StreamArchive>();
        public int SideCarryCount { get { return sideCarry.Count; } }

        /// <summary>Reported for any vessel that comes down in a side flight.</summary>
        static readonly HashSet<string> Carryable = new HashSet<string> { EventTypes.Landing, EventTypes.Splashdown, EventTypes.Recovery };
        /// <summary>Reported only for the dropped stage itself: the main vessel is a ghost in the side timeline, and its fate there is not real.</summary>
        static readonly HashSet<string> CarryableForBooster = new HashSet<string> { EventTypes.Failure, EventTypes.CrewKilled, EventTypes.Livestream };

        bool InSide()
        {
            try { return SideTimeline(); } catch { return false; }
        }

        static string Try(Func<string> f) { try { return f() ?? ""; } catch { return ""; } }

        /// <summary>True when the event was held back (side timeline).</summary>
        bool HoldForSide(NewsEvent e)
        {
            if (!InSide()) return false;
            if (e == null || sideCarry.Count >= 20 || sideCarry.Any(x => x.Type == e.Type && x.VesselId == e.VesselId)) return true;
            string booster = Try(SideVesselId);
            bool isBooster = booster != "" && e.VesselId == booster;
            // The stream belongs to the main vessel, so its "people watched" event is real whichever vessel is on screen.
            bool carry = Carryable.Contains(e.Type) || e.Type == EventTypes.Livestream || isBooster && CarryableForBooster.Contains(e.Type);
            // The main vessel flies on by itself in the side timeline. Whatever happens to that copy did not happen.
            if (Stream != null && e.VesselId == Stream.VesselId && e.Type != EventTypes.Livestream) carry = false;
            if (!carry) return true;
            if (isBooster && e.Type != EventTypes.Livestream) e.Detail = BoosterNote(e) + (string.IsNullOrEmpty(e.Detail) ? "" : " " + e.Detail);
            sideCarry.Add(e);
            var t = Tracked(e.VesselId);
            if (t != null) sideTracked[e.VesselId] = t;
            return true;
        }

        /// <summary>One sentence for the press on what this vessel is: a stage dropped by the main vessel and flown back.</summary>
        string BoosterNote(NewsEvent e)
        {
            string main = Try(SideMainName);
            if (main == "" && Stream != null) main = Stream.VesselName;
            string where = e.Type == EventTypes.Landing || e.Type == EventTypes.Splashdown ? Try(SideWhere) : "";
            string what = "This is a booster stage" + (main != "" ? " dropped by " + main : " dropped during the launch");
            switch (e.Type)
            {
                case EventTypes.Landing: return what + ", flown back under control and landed" + (where != "" ? " " + where : "") + " so it can be used again.";
                case EventTypes.Splashdown: return what + ", flown back and brought down in the water" + (where != "" ? " " + where : "") + ".";
                case EventTypes.Recovery: return what + ", recovered after flying back.";
                case EventTypes.Failure: return what + ". The attempt to fly it back and land it failed; the main vessel was not affected.";
                case EventTypes.CrewKilled: return what + ". The crew were aboard the stage as it flew back.";
                default: return what + ".";
            }
        }

        /// <summary>A stream that ended in a side flight: its entry would be lost with the side save, so it is kept for the main one.</summary>
        void HoldArchiveForSide(StreamArchive a)
        {
            if (a != null && InSide() && !sideArchives.Contains(a)) sideArchives.Add(a);
        }

        /// <summary>Back on the main timeline: report what the boosters did.</summary>
        void FlushSide(double ut = double.NaN)
        {
            if (sideCarry.Count == 0 && sideArchives.Count == 0 && sideTracked.Count == 0 || InSide()) return;
            foreach (var kv in sideTracked)
                if (!State.Tracked.ContainsKey(kv.Key)) State.Tracked[kv.Key] = kv.Value;
            sideTracked.Clear();
            foreach (var a in sideArchives)
                if (!State.Streams.Any(x => x.Id == a.Id)) State.Streams.Add(a);
            sideArchives.Clear();
            var list = sideCarry.ToList();
            sideCarry.Clear();
            foreach (var e in list)
            {
                // The side flight ran past the moment the main save was made; nothing is reported from the future.
                if (!double.IsNaN(ut) && e.UT > ut) e.UT = ut;
                Ingest(e);
            }
        }
    }
}
