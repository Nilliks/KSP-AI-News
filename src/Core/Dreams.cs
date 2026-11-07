using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KerbalNews
{
    /// <summary>A world in the game's solar system, as the press would describe it.</summary>
    public class BodyInfo
    {
        public string Name = "", Parent = "";
        public double RadiusKm, Gravity, OrbitKm, PeriodDays;
        public bool Atmosphere, Ocean, Star;
    }

    /// <summary>
    /// Speculative pieces: every so often a writer dreams about somewhere the agency hasn't been yet
    /// (or hasn't landed crew on) and works out what it would take. They ride in the background roundup, so they cost no extra call.
    /// </summary>
    public partial class NewsEngine
    {
        /// <summary>Set by the KSP layer: the bodies in this game (Kopernicus planets included). Null uses the stock system.</summary>
        public Func<List<BodyInfo>> Bodies;

        /// <summary>Days between speculative pieces for the world news setting (1 a little, 2 normal, 3 lots).</summary>
        public double DreamIntervalDays { get { return Settings.WorldNews >= 3 ? 5 : Settings.WorldNews == 2 ? 8 : 14; } }

        static readonly CultureInfo DInv = CultureInfo.InvariantCulture;

        public static List<BodyInfo> StockBodies()
        {
            Func<string, string, double, double, double, double, bool, bool, BodyInfo> b = (n, p, r, g, o, per, atm, ocean) =>
                new BodyInfo { Name = n, Parent = p, RadiusKm = r, Gravity = g, OrbitKm = o, PeriodDays = per, Atmosphere = atm, Ocean = ocean };
            return new List<BodyInfo>
            {
                new BodyInfo { Name = "Sun", Star = true, RadiusKm = 261600 },
                b("Moho", "Sun", 250, 0.28, 5263138, 102, false, false),
                b("Eve", "Sun", 700, 1.7, 9832684, 261, true, true),
                b("Gilly", "Eve", 13, 0.005, 31500, 16.5, false, false),
                b("Kerbin", "Sun", 600, 1.0, 13599840, 426, true, true),
                b("Mun", "Kerbin", 200, 0.166, 12000, 6.4, false, false),
                b("Minmus", "Kerbin", 60, 0.05, 47000, 50, false, false),
                b("Duna", "Sun", 320, 0.3, 20726155, 802, true, false),
                b("Ike", "Duna", 130, 0.11, 3200, 3, false, false),
                b("Dres", "Sun", 138, 0.115, 40839348, 1595, false, false),
                b("Jool", "Sun", 6000, 0.8, 68773560, 4845, true, false),
                b("Laythe", "Jool", 500, 0.8, 27184, 2.4, true, true),
                b("Vall", "Jool", 300, 0.235, 43152, 4.9, false, false),
                b("Tylo", "Jool", 600, 0.8, 68500, 9.8, false, false),
                b("Bop", "Jool", 65, 0.06, 128500, 25, false, false),
                b("Pol", "Jool", 44, 0.038, 179890, 41, false, false),
                b("Eeloo", "Sun", 210, 0.172, 90118820, 7360, false, false),
            };
        }

        /// <summary>Where the agency has been, from its own stories and Kerbopedia: body name to "landed" or "visited".</summary>
        public Dictionary<string, string> AgencyReach()
        {
            var reach = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in State.Stories)
                foreach (var e in s.Events)
                {
                    if (string.IsNullOrEmpty(e.Body)) continue;
                    bool landed = e.Type == EventTypes.Landing || e.Type == EventTypes.Splashdown;
                    bool there = landed || e.Type == EventTypes.Orbit || e.Type == EventTypes.Soi;
                    if (!there) continue;
                    if (landed && !(e.Type == EventTypes.Splashdown && e.Body == "Kerbin")) reach[e.Body] = "landed";
                    else if (!reach.ContainsKey(e.Body)) reach[e.Body] = "visited";
                }
            foreach (var p in State.Wiki.Values.Where(p => p.Kind == "place"))
            {
                bool landed = p.Flights.Any(f => f.Outcome.IndexOf("Landed", StringComparison.OrdinalIgnoreCase) >= 0);
                if (landed) reach[p.Title] = "landed";
                else if (!reach.ContainsKey(p.Title)) reach[p.Title] = "visited";
            }
            return reach;
        }

        /// <summary>The next speculative piece, or null when it isn't due or there is nowhere left to dream about.</summary>
        public WorldFact Dream(double ut, Random rng)
        {
            double last;
            if (State.WorldMemo.ContainsKey("dream:last") && double.TryParse(State.WorldMemo["dream:last"], NumberStyles.Float, DInv, out last) && ut - last < DreamIntervalDays * Day) return null;
            List<BodyInfo> bodies = null;
            try { bodies = Bodies != null ? Bodies() : null; } catch (Exception) { bodies = null; }
            if (bodies == null || bodies.Count < 2) bodies = StockBodies();
            var home = bodies.FirstOrDefault(x => x.Name == "Kerbin") ?? bodies.FirstOrDefault(x => !x.Star && x.Parent != "" && bodies.Any(s => s.Star && s.Name == x.Parent));
            if (home == null) return null;
            var reach = AgencyReach();
            reach[home.Name] = "landed";

            // Candidates: places never reached, and places reached but never landed on (a crewed landing, a base).
            var options = new List<KeyValuePair<BodyInfo, string>>();
            foreach (var x in bodies.Where(x => !x.Star && x.Name != home.Name))
            {
                string r;
                if (!reach.TryGetValue(x.Name, out r)) options.Add(new KeyValuePair<BodyInfo, string>(x, "reach"));
                else if (r == "visited" && x.Name != "Jool") options.Add(new KeyValuePair<BodyInfo, string>(x, "land"));
                else if (r == "landed" && rng.NextDouble() < 0.3) options.Add(new KeyValuePair<BodyInfo, string>(x, "stay"));
            }
            options = options.Where(o => !RecentlyDreamt(o.Key.Name, ut)).ToList();
            if (options.Count == 0) return null;
            // Dreams grow with the program: the next step out is likelier than the far edge.
            Func<BodyInfo, double> weight = x =>
            {
                bool moonOfHome = x.Parent == home.Name;
                bool nearPlanet = x.Parent == home.Parent && Math.Abs(x.OrbitKm - home.OrbitKm) < home.OrbitKm * 0.6;
                bool moonOfReached = reach.ContainsKey(x.Parent);
                return moonOfHome ? 5 : nearPlanet ? 4 : moonOfReached ? 3 : 1.5;
            };
            double total = options.Sum(o => weight(o.Key) * (o.Value == "reach" ? 1.0 : 0.8));
            double pick = rng.NextDouble() * total;
            var choice = options[options.Count - 1];
            foreach (var o in options) { pick -= weight(o.Key) * (o.Value == "reach" ? 1.0 : 0.8); if (pick <= 0) { choice = o; break; } }

            var t = choice.Key;
            State.WorldMemo["dream:last"] = ut.ToString("R", DInv);
            State.WorldMemo["dream:" + t.Name] = ut.ToString("R", DInv);
            return new WorldFact
            {
                Key = "dream:" + t.Name + ":" + (int)Math.Floor(ut / Day), Kind = "dream", UT = ut, Sig = 2, Body = t.Name,
                Headline = choice.Value == "reach" ? "What would it take to reach " + t.Name + "?" : choice.Value == "land" ? "Could kerbals ever walk on " + t.Name + "?" : "Could kerbals live on " + t.Name + "?",
                Text = DreamText(t, choice.Value, home, bodies, reach),
            };
        }

        bool RecentlyDreamt(string body, double ut)
        {
            double last;
            return State.WorldMemo.ContainsKey("dream:" + body) && double.TryParse(State.WorldMemo["dream:" + body], NumberStyles.Float, DInv, out last) && ut - last < 40 * Day;
        }

        string DreamText(BodyInfo t, string goal, BodyInfo home, List<BodyInfo> bodies, Dictionary<string, string> reach)
        {
            var parts = new List<string>();
            string agency = AgencyName;
            parts.Add("A SPECULATIVE piece: the writer dreams about " + (goal == "reach" ? "the agency one day going to " + t.Name : goal == "land" ? "kerbals one day landing on " + t.Name : "kerbals one day staying on " + t.Name + " for good, with a base or outpost")
                + " and works out what it would take: the kind of rocket, the trip, the dangers, the crew or probe, the cost and the patience. It is daydreaming and reasoning, clearly labelled as such. "
                + agency + " has announced NO such plan; say so if it comes up. Do not claim anything has been decided, built or scheduled.");
            var parent = bodies.FirstOrDefault(x => x.Name == t.Parent);
            bool moonOfHome = t.Parent == home.Name;
            string where = parent == null ? "" : parent.Star ? t.Name + " is a planet circling the " + parent.Name : t.Name + " is a moon of " + parent.Name;
            if (where != "")
            {
                if (moonOfHome) where += ", about " + Km(t.OrbitKm) + " from " + home.Name;
                else if (parent != null && parent.Star) where += (t.OrbitKm < home.OrbitKm ? ", closer to the " + parent.Name + " than " + home.Name + " is" : ", farther from the " + parent.Name + " than " + home.Name + " is") + " (" + (t.OrbitKm / home.OrbitKm).ToString("0.0#", DInv) + " times " + home.Name + "'s distance)";
                parts.Add(where + ".");
            }
            if (t.PeriodDays > 0 && parent != null && parent.Star)
            {
                double syn = Math.Abs(1.0 / (1.0 / home.PeriodDays - 1.0 / t.PeriodDays));
                parts.Add("Its year lasts " + Math.Round(t.PeriodDays) + " Kerbin days; good launch windows from " + home.Name + " come roughly every " + Math.Round(syn) + " days, and a one-way trip would take months or longer.");
            }
            parts.Add("Size: " + Math.Round(t.RadiusKm) + " km radius. Surface gravity: " + t.Gravity.ToString("0.###", DInv) + " g (" + home.Name + " is 1 g)." + (t.Atmosphere ? " It has an atmosphere" + (t.Ocean ? " and oceans." : ".") : " No atmosphere, so no parachutes: landing is all engine work."));
            if (t.Gravity > 1.2 && t.Atmosphere) parts.Add("Getting back off the surface would be extremely hard.");
            var moons = bodies.Where(x => x.Parent == t.Name).Select(x => x.Name).ToList();
            if (moons.Count > 0) parts.Add("Its moons: " + string.Join(", ", moons.ToArray()) + ".");
            string here;
            if (reach.TryGetValue(t.Name, out here)) parts.Add(agency + " has " + (here == "landed" ? "already landed there" : "already been there, but never landed") + ".");
            var been = reach.Keys.Where(k => k != home.Name).ToList();
            parts.Add(been.Count > 0 ? "So far " + agency + " has reached: " + string.Join(", ", been.ToArray()) + "." : agency + " has not yet gone beyond " + home.Name + ".");
            if (parent != null && !parent.Star && !reach.ContainsKey(parent.Name) && parent.Name != home.Name) parts.Add("Getting there means first making the long trip to " + parent.Name + ".");
            return string.Join(" ", parts.ToArray());
        }

        static string Km(double km) { return km >= 1e6 ? (km / 1e6).ToString("0.#", DInv) + " million km" : Math.Round(km).ToString("#,0", DInv) + " km"; }
    }
}
