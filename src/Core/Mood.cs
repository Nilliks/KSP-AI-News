using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>
    /// Public mood as numbers, so a third failure in a row reads differently from a first.
    /// Built on real coverage patterns: novelty fades, danger spikes attention, crew deaths
    /// hit trust hard and recover slowly, rescues reframe a crisis as a triumph.
    /// </summary>
    public class Mood
    {
        public double Excitement = 40;
        public double Trust = 60;
        public int FailureStreak;
        public int CleanStreak;
        public double LastCrewLossUT = -1;
        public string LastFailureSummary = "";
        public Dictionary<string, double> Concerns = new Dictionary<string, double>();

        static double Clamp(double v) { return Math.Max(0, Math.Min(100, v)); }

        public void AddConcern(string tag, double amount)
        {
            double cur;
            Concerns.TryGetValue(tag, out cur);
            Concerns[tag] = Clamp(cur + amount);
        }

        /// <summary>Update mood once a story is published.</summary>
        public void ApplyStory(Story story, double ut)
        {
            double sig = story.Significance;
            bool killed = story.HasEvent(EventTypes.CrewKilled);
            bool failed = story.HasEvent(EventTypes.Failure) || story.HasEvent(EventTypes.ContractFailed);
            bool crewed = story.Events.Any(e => e.Crewed) || (story.Craft != null && story.Craft.Crew.Count > 0);
            bool rescued = failed && !killed && crewed && (story.HasEvent(EventTypes.Recovery) || story.HasEvent(EventTypes.Landing) || story.HasEvent(EventTypes.Splashdown));

            if (story.Kind == "filler")
            {
                Excitement = Clamp(Excitement + 1);
                return;
            }

            if (killed)
            {
                Trust = Clamp(Trust - 15 - 5 * FailureStreak);
                Excitement = Clamp(Excitement - 10);
                AddConcern("crew safety", 35);
                LastCrewLossUT = ut;
                FailureStreak++;
                CleanStreak = 0;
                LastFailureSummary = story.Title;
            }
            else if (rescued)
            {
                // "Successful failure": the crisis drew everyone in and the crew came home.
                Excitement = Clamp(Excitement + 10 + sig * 0.1);
                Trust = Clamp(Trust - 1);
                AddConcern("crew safety", 10);
                LastFailureSummary = story.Title;
            }
            else if (failed)
            {
                Trust = Clamp(Trust - (crewed ? 8 : 4) - 3 * FailureStreak);
                Excitement = Clamp(Excitement - 3);
                AddConcern(crewed ? "crew safety" : "reliability", crewed ? 20 : 10);
                AddConcern("budget", 5);
                FailureStreak++;
                CleanStreak = 0;
                LastFailureSummary = story.Title;
            }
            else
            {
                Excitement = Clamp(Excitement + sig * 0.2);
                CleanStreak++;
                FailureStreak = 0;
                // Trust recovers in small steps and more slowly after a crew loss.
                bool recentLoss = LastCrewLossUT >= 0;
                Trust = Clamp(Trust + (recentLoss ? 1 : 2) + Math.Min(3, CleanStreak * 0.5));
            }
        }

        /// <summary>Called with the number of Kerbin days since the last decay.</summary>
        public void Decay(double days)
        {
            if (days <= 0) return;
            // Excitement drifts toward a resting 35; attention fades when nothing happens.
            Excitement = 35 + (Excitement - 35) * Math.Pow(0.97, days);
            // Trust slowly drifts back toward a neutral 55.
            if (Trust < 55) Trust = Math.Min(55, Trust + 0.4 * days);
            foreach (var k in Concerns.Keys.ToList())
            {
                Concerns[k] = Math.Max(0, Concerns[k] - 1.5 * days);
                if (Concerns[k] <= 0.5) Concerns.Remove(k);
            }
        }

        public static string Word(double v)
        {
            if (v >= 85) return "very high";
            if (v >= 65) return "high";
            if (v >= 40) return "moderate";
            if (v >= 20) return "low";
            return "very low";
        }

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.AppendFormat("Public excitement: {0:0}/100 ({1}). Public trust in the agency: {2:0}/100 ({3}).",
                Excitement, Word(Excitement), Trust, Word(Trust));
            if (FailureStreak > 1) sb.AppendFormat(" The agency has had {0} failures in a row.", FailureStreak);
            if (CleanStreak > 2) sb.AppendFormat(" The agency is on a run of {0} clean missions.", CleanStreak);
            var c = Concerns.Where(kv => kv.Value >= 10).OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " (" + Word(kv.Value) + ")").ToArray();
            if (c.Length > 0) sb.Append(" Public concerns: " + string.Join(", ", c) + ".");
            if (LastFailureSummary != "") sb.Append(" Most recent setback the public remembers: " + LastFailureSummary + ".");
            return sb.ToString();
        }

        public void Save(DataNode n)
        {
            n.Add("excitement", Excitement);
            n.Add("trust", Trust);
            n.Add("failStreak", FailureStreak);
            n.Add("cleanStreak", CleanStreak);
            n.Add("lastCrewLoss", LastCrewLossUT);
            n.AddText("lastFailure", LastFailureSummary);
            var c = n.AddNode("CONCERNS");
            foreach (var kv in Concerns) c.AddText("c", kv.Key + "|" + kv.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }

        public static Mood Load(DataNode n)
        {
            var m = new Mood();
            if (n == null) return m;
            m.Excitement = n.GetDouble("excitement", 40);
            m.Trust = n.GetDouble("trust", 60);
            m.FailureStreak = n.GetInt("failStreak");
            m.CleanStreak = n.GetInt("cleanStreak");
            m.LastCrewLossUT = n.GetDouble("lastCrewLoss", -1);
            m.LastFailureSummary = n.GetText("lastFailure");
            var c = n.GetNode("CONCERNS");
            if (c != null)
                foreach (var raw in c.GetAll("c"))
                {
                    var s = TextCodec.Decode(raw);
                    int bar = s.LastIndexOf('|');
                    double v;
                    if (bar > 0 && double.TryParse(s.Substring(bar + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
                        m.Concerns[s.Substring(0, bar)] = v;
                }
            return m;
        }
    }
}
