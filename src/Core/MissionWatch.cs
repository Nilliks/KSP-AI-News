using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KerbalNews
{
    /// <summary>One of the agency's craft coasting toward another world, as the tracking station sees it right now.</summary>
    public class Transit
    {
        public string Id = "", Name = "";
        /// <summary>The game's vessel type (Probe, Ship, Relay, Lander...).</summary>
        public string Kind = "";
        /// <summary>The body it orbits now, and the one whose sphere of influence it will enter.</summary>
        public string From = "", Dest = "";
        public List<string> Crew = new List<string>();
        /// <summary>When it crosses into the destination's sphere of influence.</summary>
        public double ArrivalUT;
        public double LaunchUT;
        /// <summary>Distance to the destination and to the home world right now. 0: unknown.</summary>
        public double DistKm, HomeKm;
        /// <summary>Closest approach above the surface on the present path. NaN: unknown.</summary>
        public double PeKm = double.NaN;
        /// <summary>The present path meets the surface / dips into the air.</summary>
        public bool Impact, Atmosphere;
        /// <summary>The destination is the home world: a trip back.</summary>
        public bool Homecoming;
    }

    /// <summary>
    /// Mission watch: while a craft is on its way to another world the press counts down its arrival ("40 days out from Duna"),
    /// the way real coverage follows a probe's long cruise. The pieces ride in the background roundup, so they cost no extra call.
    /// One piece per destination at each mark; a fleet is told as one story, led by whichever craft gets there first.
    /// </summary>
    public partial class NewsEngine
    {
        /// <summary>Set by the KSP layer: craft that are coasting toward another body. Null (or a null result): can't tell right now.</summary>
        public Func<double, List<Transit>> Transits;

        /// <summary>Days-to-go marks. A countdown piece is written when the lead craft first drops to or under each one.</summary>
        public static readonly int[] WatchMarks = { 5, 10, 25, 50, 100, 200, 300, 500, 1000 };
        /// <summary>Closer than this the arrival story itself takes over.</summary>
        public const double WatchMinDays = 2;
        const string WatchNoteEnd = "Do not predict success or failure.";

        static string WatchPrefix(string dest, string id) { return "watch:" + dest + ":" + id + ":"; }

        /// <summary>Reads the craft in transit, queues a countdown piece at each new mark and keeps waiting pieces up to date.</summary>
        public void ScanTransits(double ut)
        {
            if (Transits == null) return;
            List<Transit> list;
            try { list = Transits(ut); }
            catch (Exception e) { LastError = "Mission watch: " + e.Message; return; }
            if (list == null) return;
            var live = new List<string>();
            Dictionary<string, string> reach = null;
            var groups = list.Where(t => t != null && t.Id != "" && t.Dest != "" && !State.IsSilent(t.Id) && t.ArrivalUT - ut >= WatchMinDays * Day).GroupBy(t => t.Dest);
            foreach (var g in groups)
            {
                var order = g.OrderBy(t => t.ArrivalUT).ToList();
                var lead = order[0];
                string prefix = WatchPrefix(lead.Dest, lead.Id);
                live.Add(prefix);
                double left = (lead.ArrivalUT - ut) / Day;
                int mark = WatchMarks.FirstOrDefault(m => left <= m);
                string key = prefix + (mark == 0 ? "far" : mark.ToString(CultureInfo.InvariantCulture));
                if (reach == null) reach = AgencyReach();
                var fact = WatchFact(lead, order.Skip(1).ToList(), key, ut, reach);
                var waiting = State.WorldPending.FirstOrDefault(f => f.Kind == "watch" && f.Key.StartsWith(prefix, StringComparison.Ordinal));
                if (State.WorldSeen.ContainsKey(key))
                {
                    // Same mark as before: keep the waiting piece's numbers fresh (course corrections move the date).
                    if (waiting != null) { waiting.Headline = fact.Headline; waiting.Text = fact.Text; waiting.Due = fact.Due; waiting.Sig = fact.Sig; }
                    continue;
                }
                State.WorldSeen[key] = ut;
                State.WorldPending.RemoveAll(f => f.Kind == "watch" && f.Key.StartsWith(prefix, StringComparison.Ordinal));
                State.WorldPending.Add(fact);
            }
            // The trip ended, or the path no longer leads there: nothing to count down to.
            State.WorldPending.RemoveAll(f => f.Kind == "watch" && !live.Any(p => f.Key.StartsWith(p, StringComparison.Ordinal)));
        }

        WorldFact WatchFact(Transit t, List<Transit> behind, string key, double ut, Dictionary<string, string> reach)
        {
            double left = (t.ArrivalUT - ut) / Day;
            bool crewed = t.Crew.Count > 0;
            string been;
            bool first = !t.Homecoming && !reach.TryGetValue(t.Dest, out been);
            reach.TryGetValue(t.Dest, out been);
            int sig = 2 + (crewed || first || left <= 10 ? 1 : 0);
            string name = "\"" + t.Name + "\"";
            string what = crewed
                ? "a crewed " + WatchKind(t.Kind) + " carrying " + string.Join(", ", t.Crew.ToArray())
                : "an uncrewed " + WatchKind(t.Kind);
            var p = new List<string>();
            p.Add("A MISSION WATCH piece: a countdown story about one of " + AgencyName + "'s own craft that is in flight right now. Follow it the way the press follows a real spacecraft on a long cruise, building anticipation for the arrival. Give the time left and the expected date as stated. "
                + AgencyName + " has not said what the craft will do when it gets there, so do not state plans. " + WatchNoteEnd);
            p.Add(t.Homecoming
                ? name + ", " + what + ", is on its way home: it is {left} from " + t.Dest + "."
                : name + ", " + what + ", is {left} from " + t.Dest + ".");
            p.Add("It is expected to cross into " + t.Dest + "'s sphere of influence around " + Clock.FormatDate(t.ArrivalUT) + ".");
            if (t.LaunchUT > 0 && ut > t.LaunchUT) p.Add("It launched on " + Clock.FormatDate(t.LaunchUT) + " and has been flying for " + FmtDays(Math.Floor((ut - t.LaunchUT) / Day)) + " days.");
            string where = "";
            if (t.DistKm > 0) where = "It is now " + WatchKm(t.DistKm) + " from " + t.Dest;
            if (t.HomeKm > 0 && !t.Homecoming) where += (where == "" ? "It is now " : " and ") + WatchKm(t.HomeKm) + " from home";
            if (where != "") p.Add(where + (t.From != "" ? ", coasting in orbit around " + (t.From == "Sun" ? "the Sun" : t.From) : "") + ".");
            if (t.Impact) p.Add("On its present path it would meet the surface of " + t.Dest + ". The path can still be changed with a course correction.");
            else if (t.Atmosphere) p.Add("On its present path it would dip into " + t.Dest + "'s atmosphere.");
            else if (!double.IsNaN(t.PeKm) && t.PeKm > 0) p.Add("On its present path its closest approach would be about " + WatchKm(t.PeKm) + " above " + t.Dest + "'s surface.");
            if (first) p.Add("Nothing built by " + AgencyName + " has reached " + t.Dest + " before.");
            else if (!t.Homecoming) p.Add(AgencyName + " has " + (been == "landed" ? "landed on " : "been to ") + t.Dest + " before.");
            if (behind.Count > 0)
                p.Add("Also on the way to " + t.Dest + ": " + string.Join(", ", behind.Take(4).Select(b => "\"" + b.Name + "\"" + (b.Crew.Count > 0 ? " (crewed)" : "") + ", about " + FmtDays(Math.Max(1, Math.Round((b.ArrivalUT - t.ArrivalUT) / Day))) + " days behind").ToArray())
                    + (behind.Count > 4 ? ", and " + (behind.Count - 4) + " more" : "") + ".");
            string head = t.Homecoming ? t.Name + " is {left} from home"
                : left <= 10 ? t.Name + " on final approach: {left} to " + t.Dest
                : t.Name + " is {left} from " + t.Dest;
            return new WorldFact { Key = key, Kind = "watch", Sig = sig, UT = ut, Body = t.Dest, Due = t.ArrivalUT, Headline = head, Text = string.Join(" ", p.ToArray()) };
        }

        static string WatchKind(string vesselType)
        {
            switch ((vesselType ?? "").ToLowerInvariant())
            {
                case "probe": return "probe";
                case "relay": return "relay satellite";
                case "lander": return "lander";
                case "rover": return "rover";
                case "station": return "station";
                case "base": return "base";
                case "plane": return "spaceplane";
                default: return "spacecraft";
            }
        }

        static string WatchKm(double km)
        {
            var inv = CultureInfo.InvariantCulture;
            if (km >= 1e6) return (km / 1e6).ToString(km >= 1e7 ? "0" : "0.0", inv) + " million km";
            if (km >= 1000) return (Math.Round(km / 100) * 100).ToString("#,0", inv) + " km";
            return Math.Round(km).ToString("0", inv) + " km";
        }

        /// <summary>"40 days", or "about 1,100 days (2.6 years)" on the long hauls.</summary>
        public string WatchLeft(double seconds)
        {
            double days = Math.Max(1, Math.Round(seconds / Day));
            string s = days.ToString("#,0", CultureInfo.InvariantCulture) + (days == 1 ? " day" : " days");
            double years = Clock.YearSeconds > 0 ? seconds / Clock.YearSeconds : 0;
            return years >= 1 ? "about " + s + " (" + years.ToString("0.#", CultureInfo.InvariantCulture) + " years)" : s;
        }

        /// <summary>Before a roundup: drop countdowns that ran out, and let only the most pressing one into this roundup.</summary>
        List<WorldFact> WatchHeldBack(double ut)
        {
            State.WorldPending.RemoveAll(f => f.Kind == "watch" && f.Due - ut < WatchMinDays * Day);
            var all = State.WorldPending.Where(f => f.Kind == "watch").OrderByDescending(f => f.Sig).ThenBy(f => f.Due).ToList();
            return all.Skip(1).ToList();
        }

        /// <summary>Fills in the time left as of the moment the piece is written, so a piece that waited is never out of date.</summary>
        void WatchResolve(WorldFact f, double ut)
        {
            if (f.Kind != "watch") return;
            string left = WatchLeft(f.Due - ut);
            f.Headline = f.Headline.Replace("{left}", left.StartsWith("about ", StringComparison.Ordinal) ? left.Substring(6) : left);
            f.Text = f.Text.Replace("{left}", left);
        }

        /// <summary>The facts of a countdown piece without the note to the writer.</summary>
        static string WatchOffline(string text)
        {
            int i = text.IndexOf(WatchNoteEnd, StringComparison.Ordinal);
            return i >= 0 ? text.Substring(i + WatchNoteEnd.Length).Trim() : text;
        }

        /// <summary>Remembers that the press counted this trip down, so the arrival story can say so.</summary>
        void WatchPublished(WorldFact f, double ut)
        {
            int cut = f.Key.LastIndexOf(':');
            if (f.Kind != "watch" || cut <= 0) return;
            string k = "watched:" + f.Key.Substring(0, cut + 1);
            if (!State.WorldMemo.ContainsKey(k)) State.WorldMemo[k] = ut.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>An arrival the press had been counting down to says so, which makes the moment land harder.</summary>
        void AnnotateArrival(NewsEvent e)
        {
            if (e == null || e.Type != EventTypes.Soi || string.IsNullOrEmpty(e.VesselId) || string.IsNullOrEmpty(e.Body)) return;
            string k = "watched:" + WatchPrefix(e.Body, e.VesselId), v;
            double since;
            if (!State.WorldMemo.TryGetValue(k, out v)) return;
            State.WorldMemo.Remove(k);
            if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out since) || e.UT <= since) return;
            string note = "The press had been counting down to this arrival since " + Clock.FormatDate(since) + ".";
            e.Detail = string.IsNullOrEmpty(e.Detail) ? note : e.Detail.TrimEnd() + " " + note;
        }
    }
}
