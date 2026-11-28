using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>One stage from the game's delta-v readout.</summary>
    public class EngStage
    {
        public int Stage;
        public double DvVac, DvAsl, DvActual;
        public double TwrVac, TwrAsl, TwrActual;
        public double StartMass, EndMass;   // tonnes
        public double BurnTime;             // seconds
        public double ThrustVac;            // kN
        public double IspVac;
    }

    /// <summary>
    /// What the engineering team can see right now, filled in by the KSP layer: the scene, the active craft
    /// (in flight or being built), its orbit and target, and its stages. Everything the AI says about numbers comes from here.
    /// </summary>
    public class EngSnapshot
    {
        /// <summary>spacecenter, tracking, flight, map, vab or sph.</summary>
        public string Scene = "spacecenter";
        public string Vessel = "", VesselId = "";
        public string Situation = "";       // "orbiting Kerbin", "landed on the Mun", "being built in the VAB"
        public int Crew, CrewCapacity, Parts;
        public double Mass;                 // tonnes

        public string Body = "";
        public double BodyMu, BodyRadius, BodyGravity, BodyAtmosphere, BodySoi;   // m^3/s^2, m, m/s^2, m (top of atmosphere, 0 = none), m

        public bool HasOrbit;
        public double ApA, PeA, Inclination, Eccentricity, Period, Sma, Altitude, Speed;   // m, m, deg, -, s, m, m, m/s
        public double VerticalSpeed, GForce, Throttle = -1;
        /// <summary>The first maneuver node, if the director placed one.</summary>
        public EngNode Node;
        /// <summary>Part modules on the craft by name (count), and resource capacity by name: the design review reads these.</summary>
        public Dictionary<string, int> Modules = new Dictionary<string, int>();
        public Dictionary<string, double> ResourceMax = new Dictionary<string, double>();

        public string Target = "";
        /// <summary>Current phase angle to the target around their common parent (degrees, target ahead = 0-360), or -1.</summary>
        public double PhaseAngle = -1;
        public double RelInclination = -1;
        /// <summary>For a transfer around the common parent: its mu, the radius of the inner and outer orbit, and whether the
        /// craft is going to another planet (then r1 is its home planet's orbit).</summary>
        public double TransferMu, TransferR1, TransferR2;
        public bool Interplanetary;
        public string TransferParent = "";
        public double TargetDistance = -1;  // m

        public bool DeltaVReady;
        public List<EngStage> Stages = new List<EngStage>();
        public double TotalDvVac, TotalDvAsl, TotalDvActual;
        public int CurrentStage = -1;

        /// <summary>At the space center or tracking station: one line per active vessel.</summary>
        public List<string> Vessels = new List<string>();
        /// <summary>True when the bodies are the stock Kerbol system (the reference delta-v map only applies then).</summary>
        public bool StockSystem = true;
    }

    /// <summary>Plain orbital mechanics, so the team quotes computed numbers instead of guessing.</summary>
    public static class EngMath
    {
        /// <summary>Burns for a Hohmann transfer between circular orbits r1 and r2 (m/s).</summary>
        public static void Hohmann(double mu, double r1, double r2, out double dv1, out double dv2)
        {
            dv1 = dv2 = 0;
            if (mu <= 0 || r1 <= 0 || r2 <= 0) return;
            double a = (r1 + r2) / 2;
            dv1 = Math.Abs(Math.Sqrt(mu / r1) * (Math.Sqrt(r2 / a) - 1));
            dv2 = Math.Abs(Math.Sqrt(mu / r2) * (1 - Math.Sqrt(r1 / a)));
        }

        /// <summary>Phase angle (target ahead, degrees) to start a Hohmann transfer from r1 to r2. Negative when r2 is the inner orbit.</summary>
        public static double TransferPhase(double r1, double r2)
        {
            if (r1 <= 0 || r2 <= 0) return 0;
            return 180 * (1 - Math.Pow((r1 + r2) / (2 * r2), 1.5));
        }

        public static double MeanMotion(double mu, double r) { return mu > 0 && r > 0 ? Math.Sqrt(mu / (r * r * r)) * 180 / Math.PI : 0; }  // deg/s

        /// <summary>Seconds until the phase angle reaches the ideal one, or -1 if the orbits do not drift.</summary>
        public static double TimeToWindow(double mu, double r1, double r2, double phaseNow, double phaseIdeal)
        {
            double rel = MeanMotion(mu, r1) - MeanMotion(mu, r2);  // the inner craft gains on the outer one
            if (Math.Abs(rel) < 1e-12 || phaseNow < 0) return -1;
            double diff = rel > 0 ? phaseNow - phaseIdeal : phaseIdeal - phaseNow;
            diff = ((diff % 360) + 360) % 360;
            return diff / Math.Abs(rel);
        }

        /// <summary>Burn to circularize at apoapsis from the current orbit (m/s).</summary>
        public static double CircularizeAtAp(double mu, double rAp, double rPe)
        {
            if (mu <= 0 || rAp <= 0 || rPe <= 0) return 0;
            double a = (rAp + rPe) / 2;
            double vAp = Math.Sqrt(mu * (2 / rAp - 1 / a));
            return Math.Max(0, Math.Sqrt(mu / rAp) - vAp);
        }

        /// <summary>Burn time (s) for dv with thrust (kN) and starting mass (t) at the given Isp.</summary>
        public static double BurnTime(double dv, double thrustKn, double massT, double isp)
        {
            if (thrustKn <= 0 || massT <= 0 || isp <= 0) return -1;
            double ve = isp * 9.80665;
            double mEnd = massT * Math.Exp(-dv / ve);
            return (massT - mEnd) * 1000 * ve / (thrustKn * 1000);
        }

        public static string Dist(double m)
        {
            double a = Math.Abs(m);
            if (a >= 1e9) return (m / 1e9).ToString("0.##", CultureInfo.InvariantCulture) + " Gm";
            if (a >= 1e6) return (m / 1e6).ToString("0.##", CultureInfo.InvariantCulture) + " Mm";
            if (a >= 1e3) return (m / 1e3).ToString("0.#", CultureInfo.InvariantCulture) + " km";
            return m.ToString("0", CultureInfo.InvariantCulture) + " m";
        }

        public static string Dur(double s)
        {
            if (s < 0) return "unknown";
            if (s < 90) return s.ToString("0", CultureInfo.InvariantCulture) + " s";
            if (s < 5400) return (s / 60).ToString("0", CultureInfo.InvariantCulture) + " min";
            if (s < 6 * 3600 * 3) return (s / 3600).ToString("0.#", CultureInfo.InvariantCulture) + " h";
            return (s / 21600).ToString("0.#", CultureInfo.InvariantCulture) + " days";   // Kerbin days
        }

        public static string N(double v, string fmt = "0") { return v.ToString(fmt, CultureInfo.InvariantCulture); }
    }

    public partial class NewsEngine
    {
        public const int EngChatKeep = 60;
        /// <summary>The KSP layer fills this with what the team can see right now.</summary>
        public Func<EngSnapshot> EngineeringData = () => null;
        public bool EngBusy;
        int engRun;

        public static readonly string[] EngTeam = { "linus", "wernher", "gene" };
        public static string EngName(string key)
        {
            switch (key) { case "linus": return "Linus, flight dynamics"; case "wernher": return "Wernher, propulsion and structures"; case "gene": return "Gene, mission planning"; default: return "Engineering"; }
        }

        /// <summary>Numbers the team works from, computed here rather than by the model. Also shown on the page.</summary>
        public static List<string> EngFacts(EngSnapshot s)
        {
            var f = new List<string>();
            if (s == null) return f;
            if (s.HasOrbit && s.BodyMu > 0 && s.ApA > 0 && s.PeA < s.ApA)
            {
                double rAp = s.ApA + s.BodyRadius, rPe = s.PeA + s.BodyRadius;
                if (s.PeA < Math.Max(s.BodyAtmosphere, 0) || s.PeA < 0)
                    f.Add("Circularizing at apoapsis takes about " + EngMath.N(EngMath.CircularizeAtAp(s.BodyMu, rAp, Math.Max(rPe, 1))) + " m/s (periapsis is " + (s.PeA < 0 ? "below the surface" : "inside the atmosphere") + ").");
            }
            if (s.Target != "" && s.TransferMu > 0 && s.TransferR1 > 0 && s.TransferR2 > 0)
            {
                double dv1, dv2;
                EngMath.Hohmann(s.TransferMu, s.TransferR1, s.TransferR2, out dv1, out dv2);
                double ideal = EngMath.TransferPhase(s.TransferR1, s.TransferR2);
                string idealTxt = EngMath.N(((ideal % 360) + 360) % 360) + "°";
                if (!s.Interplanetary)
                    f.Add("Hohmann transfer to " + s.Target + " around " + s.TransferParent + ": departure burn about " + EngMath.N(dv1) + " m/s, arrival burn to match its orbit about " + EngMath.N(dv2) + " m/s (a capture into a low orbit around a moon or planet is usually less).");
                else
                    f.Add("Planet-to-planet Hohmann around " + s.TransferParent + ": " + s.Target + " needs a heliocentric change of about " + EngMath.N(dv1) + " m/s at departure. From a low parking orbit the ejection burn is larger than that, because it is done deep in the home planet's gravity well.");
                f.Add("Ideal phase angle for the transfer is about " + idealTxt + " (target ahead).");
                if (s.PhaseAngle >= 0)
                {
                    double t = EngMath.TimeToWindow(s.TransferMu, s.TransferR1, s.TransferR2, s.PhaseAngle, ideal);
                    f.Add("Current phase angle is " + EngMath.N(s.PhaseAngle) + "°; the next window opens in about " + EngMath.Dur(t) + ".");
                }
                if (s.RelInclination >= 0) f.Add("Relative inclination to " + s.Target + " is " + EngMath.N(s.RelInclination, "0.0") + "°" + (s.RelInclination > 3 ? ", worth fixing with a plane change at the ascending or descending node." : "."));
            }
            if (s.DeltaVReady && s.Stages.Count > 0)
            {
                var weak = s.Stages.Where(st => st.DvVac > 1 && st.TwrAsl > 0 && st.TwrAsl < 1.15 && st.Stage == s.Stages.Max(x => x.Stage)).ToList();
                foreach (var st in weak) f.Add("Stage " + st.Stage + " has a sea-level TWR of " + EngMath.N(st.TwrAsl, "0.00") + ", too low to lift off cleanly from Kerbin (aim for 1.2 to 1.6).");
            }
            return f;
        }

        /// <summary>The craft data as text for the prompt.</summary>
        public static string EngDataText(EngSnapshot s)
        {
            if (s == null) return "No live data (the game did not report any).";
            var sb = new StringBuilder();
            string where = s.Scene == "vab" ? "in the Vehicle Assembly Building" : s.Scene == "sph" ? "in the Space Plane Hangar" : s.Scene == "map" ? "in flight, map view" : s.Scene == "flight" ? "in flight" : s.Scene == "tracking" ? "at the tracking station" : "at the space center";
            sb.AppendLine("The director is " + where + ".");
            if (s.Vessel != "")
            {
                sb.AppendLine((s.Scene == "vab" || s.Scene == "sph" ? "Craft being built: " : "Active vessel: ") + s.Vessel + (s.Situation != "" ? " (" + s.Situation + ")" : "") + ". Mass " + EngMath.N(s.Mass, "0.0#") + " t, " + s.Parts + " parts, crew " + s.Crew + "/" + s.CrewCapacity + ".");
            }
            if (s.Body != "" && s.BodyMu > 0)
                sb.AppendLine(s.Body + ": radius " + EngMath.Dist(s.BodyRadius) + ", surface gravity " + EngMath.N(s.BodyGravity, "0.00") + " m/s², " + (s.BodyAtmosphere > 0 ? "atmosphere up to " + EngMath.Dist(s.BodyAtmosphere) : "no atmosphere") + (s.BodySoi > 0 ? ", sphere of influence " + EngMath.Dist(s.BodySoi) : "") + ", mu " + s.BodyMu.ToString("0.###e0", CultureInfo.InvariantCulture) + ".");
            if (s.HasOrbit)
                sb.AppendLine("Orbit: apoapsis " + EngMath.Dist(s.ApA) + ", periapsis " + EngMath.Dist(s.PeA) + ", inclination " + EngMath.N(s.Inclination, "0.0") + "°, eccentricity " + EngMath.N(s.Eccentricity, "0.000") + ", period " + (s.Period > 0 ? EngMath.Dur(s.Period) : "none (escape)") + ". Altitude " + EngMath.Dist(s.Altitude) + ", orbital speed " + EngMath.N(s.Speed) + " m/s.");
            if (s.Target != "") sb.AppendLine("Target: " + s.Target + (s.TargetDistance >= 0 ? ", " + EngMath.Dist(s.TargetDistance) + " away" : "") + ".");
            if (s.Stages.Count > 0)
            {
                sb.AppendLine("Delta-v by stage (stage numbers count down as they fire; highest fires first" + (s.CurrentStage >= 0 ? "; current stage " + s.CurrentStage : "") + "):");
                foreach (var st in s.Stages.OrderByDescending(x => x.Stage))
                    sb.AppendLine("- Stage " + st.Stage + ": " + EngMath.N(st.DvVac) + " m/s vacuum, " + EngMath.N(st.DvAsl) + " at sea level" + (s.Scene == "flight" || s.Scene == "map" ? ", " + EngMath.N(st.DvActual) + " actual" : "") + "; TWR " + EngMath.N(st.TwrVac, "0.00") + " vac / " + EngMath.N(st.TwrAsl, "0.00") + " ASL; mass " + EngMath.N(st.StartMass, "0.0#") + " → " + EngMath.N(st.EndMass, "0.0#") + " t; burn " + EngMath.Dur(st.BurnTime) + (st.IspVac > 0 ? "; Isp " + EngMath.N(st.IspVac) + " s" : "") + ".");
                sb.AppendLine("Total: " + EngMath.N(s.TotalDvVac) + " m/s vacuum, " + EngMath.N(s.TotalDvAsl) + " at sea level" + (s.Scene == "flight" || s.Scene == "map" ? ", " + EngMath.N(s.TotalDvActual) + " actual" : "") + ".");
            }
            else if (s.Vessel != "" && !s.DeltaVReady) sb.AppendLine("The delta-v readout is not available right now.");
            if (s.Vessels.Count > 0) sb.AppendLine("Active vessels:\n- " + string.Join("\n- ", s.Vessels.Take(20).ToArray()));
            if (s.Node != null) sb.AppendLine("MANEUVER NODE CHECK:\n- " + string.Join("\n- ", EngNodeCheck(s).ToArray()));
            var facts = EngFacts(s);
            if (facts.Count > 0) sb.AppendLine("Computed by the team's tools (trust these numbers):\n- " + string.Join("\n- ", facts.ToArray()));
            return sb.ToString().TrimEnd();
        }

        const string StockDvMap =
            "Reference delta-v (stock system, approximate, from a low orbit unless noted): Kerbin surface to low orbit (80 km) 3,400. " +
            "Low Kerbin orbit to: Mun transfer 860, Mun capture 310, Mun landing 580 (and the same back up); Minmus transfer 930, capture 160, landing 180; " +
            "Kerbin escape 950; Duna transfer 1,060 (with escape), Duna capture 250 (less with aerobraking), Duna landing about 1,450 without aerobraking (mostly free with chutes), Duna ascent 1,450; " +
            "Eve transfer 1,040, capture 80 with aerobraking, Eve ascent 8,000 (very hard); Moho transfer 1,710, capture 2,410; Jool transfer 1,920, capture 160 with aerobraking or gravity assists; " +
            "Ike from Duna orbit 300 plus landing 390; Gilly landing 30. Return from the Mun surface to Kerbin about 900, from Minmus about 350, relying on aerobraking at Kerbin. " +
            "Add 10-20% margin.";

        string EngSystem(EngSnapshot snap)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are the engineering team of " + AgencyName + ", a space agency in Kerbal Space Program (KSP1). The director (the player) talks to you in a group chat. The team:");
            sb.AppendLine("- linus: Linus, flight dynamics. Orbits, transfers, phase angles, rendezvous, landings, maneuver nodes, aerobraking.");
            sb.AppendLine("- wernher: Wernher, propulsion and structures. Engines, staging, TWR, Isp, fuel, mass, aerodynamics, wobble, part choice and craft design.");
            sb.AppendLine("- gene: Gene, mission planning. Whole-mission delta-v budgets, margins, contracts, crew safety, what to do next.");
            sb.AppendLine("Answer like real, friendly flight engineers: practical, concrete, step by step when asked how to do something, with numbers. Use the live data and the computed numbers you are given; do not invent craft data you were not given. If a number is missing, say what you would need (for example a target set, or staging set up). Explain orbital mechanics simply when the director seems new to it.");
            sb.AppendLine("Usually one specialist answers; add a second only when they add something real (at most three). Keep each reply short: two to six sentences, or a short numbered list for procedures. Name KSP parts and features by their stock names (Terrier, Poodle, maneuver node, SAS prograde) unless the mods list says otherwise.");
            if (snap == null || snap.StockSystem) sb.AppendLine(StockDvMap);
            else sb.AppendLine("A planet pack or rescale is installed, so stock delta-v maps do not apply: work from the bodies' real numbers given in the data.");
            sb.AppendLine("Reply with one JSON object only: {\"replies\": [{\"who\": \"linus|wernher|gene\", \"text\": \"...\"}]}.");
            var hints = EngModHints(Settings.Mods);
            if (hints.Count > 0) sb.AppendLine("MODS THAT CHANGE THE ADVICE (use them: suggest their tools and respect their rules):\n- " + string.Join("\n- ", hints.ToArray()));
            string mods = Settings.ModsInContext ? Settings.ModsText : "";
            if (!string.IsNullOrEmpty(mods)) sb.AppendLine("Installed mods (parts and features from these may be on the craft): " + mods);
            sb.AppendLine("Lines marked \"!\" in the design review or node check are real problems found in the craft data: bring them up when they matter to the question. When the director asks to review a design, go through the review and the mission plan; when they ask about a node, go through the node check and suggest exact fixes (which way to drag, roughly how many m/s).");
            return sb.ToString();
        }

        string EngUser(EngSnapshot snap, double ut)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Today: " + Clock.FormatDate(ut) + ".");
            sb.AppendLine("LIVE DATA");
            sb.AppendLine(EngDataText(snap));
            var plan = EngCurrentPlan(snap);
            if (plan != null) sb.AppendLine(EngMissions.PlanText(plan));
            var review = EngReview(snap, plan);
            if (review.Count > 0) sb.AppendLine("DESIGN REVIEW (from the parts list):\n- " + string.Join("\n- ", review.ToArray()));
            string inc = IncidentText();
            if (inc != "") sb.AppendLine("PAST INVESTIGATIONS:\n" + inc);
            sb.AppendLine();
            sb.AppendLine("The conversation so far (newest last):");
            foreach (var m in State.EngChat.Skip(Math.Max(0, State.EngChat.Count - 12)))
                sb.AppendLine((m.Role == "player" ? "Director" : EngName(m.Role)) + ": " + m.Text);
            return sb.ToString();
        }

        public AssistantMessage EngLine(string role, string text, double ut)
        {
            var m = new AssistantMessage { Id = State.NewId("e"), Role = role, Text = text ?? "", UT = ut };
            State.EngChat.Add(m);
            if (State.EngChat.Count > EngChatKeep) State.EngChat.RemoveRange(0, State.EngChat.Count - EngChatKeep);
            RaiseCommunity();
            return m;
        }

        /// <summary>The director asks the engineering team something. One writer-model call; the usage presets do not limit it,
        /// because the director asked.</summary>
        public bool EngSend(string text, double ut)
        {
            text = (text ?? "").Trim();
            if (text == "" || !Settings.EngineeringOn) return false;
            EngLine("player", ResponseParser.Clip(text, 1500), ut);
            EngSnapshot snap = null;
            try { snap = EngineeringData(); } catch (Exception e) { LastError = "Engineering data: " + e.Message; }
            if (!Settings.CanCallAi)
            {
                var facts = EngFacts(snap);
                EngLine("linus", facts.Count > 0 ? "(Offline, so just the numbers:) " + string.Join(" ", facts.ToArray()) : "(The team is offline. Turn the AI on in Settings to talk to them.)", ut);
                return true;
            }
            int run = ++engRun;
            EngBusy = true;
            EngStep(run, snap, ut, 0);
            return true;
        }

        void EngStep(int run, EngSnapshot snap, double ut, int failures)
        {
            AskJson("Engineering team", Settings.Writer, EngSystem(snap), EngUser(snap, ut), 1600, 0, (root, err, text, res) =>
            {
                if (run != engRun) return;
                if (root == null && text.Trim() != "" && !text.Contains("{")) root = new Dictionary<string, object> { { "replies", new List<object> { new Dictionary<string, object> { { "who", "linus" }, { "text", text.Trim() } } } } };
                if (root == null)
                {
                    LastError = err ?? "No reply";
                    if (failures < 1 && !IsPermanent(res)) { EngStep(run, snap, ut, failures + 1); return; }
                    EngBusy = false;
                    EngLine("gene", "(We lost the connection to the team. Try again in a moment.)", ut);
                    return;
                }
                EngBusy = false;
                int n = 0;
                var list = Json.Arr(Json.Obj(root) != null && Json.Obj(root).ContainsKey("replies") ? Json.Obj(root)["replies"] : null);
                if (list != null)
                    foreach (var r in list.Take(3))
                    {
                        string who = Json.Str(r, "who", "linus").Trim().ToLowerInvariant();
                        int sp = who.IndexOfAny(new[] { ' ', ',' });
                        if (sp > 0) who = who.Substring(0, sp);
                        if (!EngTeam.Contains(who)) who = "linus";
                        string t = Json.Str(r, "text", "").Trim();
                        if (t == "") continue;
                        EngLine(who, ResponseParser.Clip(t, 2000), ut);
                        n++;
                    }
                if (n == 0)
                {
                    string single = Json.Str(root, "reply", "").Trim();
                    EngLine("linus", single == "" ? "..." : ResponseParser.Clip(single, 2000), ut);
                }
            });
        }

        public void EngClear() { State.EngChat.Clear(); State.EngMission = ""; engRun++; EngBusy = false; }
    }
}
