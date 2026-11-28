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

        /// <summary>Every part on the craft with its modules and resources, read live. Null when there is no craft.</summary>
        public CraftSheet Sheet;
        /// <summary>The bodies in the save, so budgets hold for rescaled systems and planet packs.</summary>
        public List<EngBody> Bodies = new List<EngBody>();
        public EngBody Home { get { return Bodies.FirstOrDefault(b => b.IsHome); } }
        /// <summary>Game time of the reading, and how far ahead of the home planet each other planet is right now (degrees, 0-360).</summary>
        public double UT;
        public Dictionary<string, double> Phases = new Dictionary<string, double>();
        /// <summary>Height above the ground in flight, metres (-1 when not known).</summary>
        public double RadarAlt = -1;
        public List<EngContract> Contracts = new List<EngContract>();
        public List<EngExperiment> Experiments = new List<EngExperiment>();
        /// <summary>Science already gathered, by subject id ("temperatureScan@DunaInSpaceLow"), as a share of what the subject can give.</summary>
        public Dictionary<string, double> ScienceDone = new Dictionary<string, double>();
        /// <summary>Titles of every part the game has loaded, to spot a part named in a contract.</summary>
        public List<string> PartTitles = new List<string>();
        public EngBody BodyNamed(string name) { return DvPlanner.Find(Bodies, name); }
        /// <summary>True when the stage figures are the sheet's own estimate, not the game's readout.</summary>
        public bool DvEstimated;
        public EngSnapshot WithSheet(CraftSheet sheet) { var c = (EngSnapshot)MemberwiseClone(); c.Sheet = sheet; c.Stages = new List<EngStage>(Stages); return c; }
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
        public const int EngChatKeep = 2000;
        /// <summary>The KSP layer fills this with what the team can see right now.</summary>
        public Func<EngSnapshot> EngineeringData = () => null;
        /// <summary>Every unlocked part that could stand in for one on a craft, from the KSP layer. Slow the first time, so only asked for when needed.</summary>
        public Func<List<CraftPart>> EngineeringCatalog = () => null;
        public bool EngBusy;
        int engRun;

        public static readonly string[] EngTeam = { "linus", "wernher", "gene", "maxwell", "irma", "mortimer" };
        public static string EngName(string key)
        {
            switch (key) { case "linus": return "Linus, flight dynamics"; case "wernher": return "Wernher, propulsion and structures"; case "gene": return "Gene, mission planning"; case "maxwell": return "Maxwell, power and thermal"; case "irma": return "Irma, systems and crew"; case "mortimer": return "Mortimer, cost"; default: return "Engineering"; }
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
            sb.AppendLine("- maxwell: Maxwell, power and thermal. Generation, batteries, what draws power where, reactors, radiators, heat loops.");
            sb.AppendLine("- irma: Irma, systems and crew. Comms and relays, life support, habitat, science gear, action groups, docking hardware.");
            sb.AppendLine("- mortimer: Mortimer, cost. Price of the craft and its stages, what is oversized or could be cheaper, cost per tonne to orbit.");
            sb.AppendLine("Answer like real, friendly flight engineers: practical, concrete, step by step when asked how to do something, with numbers. Use the live data and the computed numbers you are given; do not invent craft data you were not given. If a number is missing, say what you would need (for example a target set, or staging set up). Explain orbital mechanics simply when the director seems new to it.");
            sb.AppendLine("You can see the whole craft. The CRAFT SHEET is the complete, live parts list read from the game: every part, which mod it is from, its stage, what it holds and what its modules do, including modded parts. Never say you cannot see the parts list. Modules you do not recognise are listed by their class name with their config values and resource rates: reason from those and from the mod's name. The POWER BUDGET, STAGING CHECK, COMPUTED CHECKS (heat, comms, landing, balance, launch, crew, cost) and DESIGN REVIEW are computed from that sheet: quote their numbers rather than redoing the sums, and point the director to the matching tab on the left when it helps. The MOD HANDBOOKS say how the mods on this craft behave and which of their features are switched on in this save: follow them, never advise about a mod feature that is off, and say so plainly when a modded part does something neither the handbooks nor its listed values explain. Judge in both directions: say when something is short, and also when the craft carries far more than the mission needs, with what trimming it would save.");
            sb.AppendLine("ASK BEFORE YOU GUESS. If the answer depends on something you were not told (where it is landing, where it is going, whether crew are aboard, whether it comes home, what will be running), do not assume: ask the director, at most two short questions, and offer likely answers as options. Only ask when the answer would change your advice. If the gap is minor, state the assumption in one clause and answer.");
            sb.AppendLine("Usually one specialist answers; add a second only when they add something real (at most three). Keep each reply short: two to six sentences, or a short numbered list for procedures. Name KSP parts and features by their stock names (Terrier, Poodle, maneuver node, SAS prograde) unless the mods list says otherwise.");
            if (snap != null && snap.Bodies.Count > 0 && snap.Home != null) sb.AppendLine(DvPlanner.Table(snap.Bodies, snap.Home) + " These are worked out from the real bodies in this save, so they hold even if the system is rescaled or has extra planets. Add 10-20% margin.");
            else if (snap == null || snap.StockSystem) sb.AppendLine(StockDvMap);
            else sb.AppendLine("A planet pack or rescale is installed, so stock delta-v maps do not apply: work from the bodies' real numbers given in the data.");
            sb.AppendLine("Reply with one JSON object only: {\"replies\": [{\"who\": \"linus|wernher|gene|maxwell|irma|mortimer\", \"text\": \"...\"}], \"ask\": {\"question\": \"...\", \"options\": [\"...\", \"...\"]}}. Leave \"ask\" out unless you need an answer from the director before you can advise; when you use it, the reply text should say what you need and why, and options are two to five short likely answers.");
            sb.AppendLine("UNLOCKED PARTS is read from the game: it is exactly what the director can build with right now, modded parts included, with what each part does. NEVER ask the director what they have unlocked or which mods they have; you already know. When asked what to fit or which part is best, recommend specific parts by their exact name from that list, with the number that matters (EC/s, kN, Isp, kW, mass, cost), and say how many to fit. Only recommend a part that is on the list. If the list for the kind you need is not shown, say which kind you would look at and give the answer from what is shown.");
            sb.AppendLine("TEAM MEMORY, when given, is what the team has on file from earlier: answers, decisions, meeting minutes, flight results and the director's stated preferences. Use it: do not ask again for something on file, keep to the director's preferences, and say \"as we found last time\" when you lean on it. If the director asks what was said or decided before, answer from it and say plainly when nothing is on file.");
            string level = EngLevelText();
            if (level != "") sb.AppendLine(level);
            var hints = EngModHints(Settings.Mods);
            if (hints.Count > 0) sb.AppendLine("MODS THAT CHANGE THE ADVICE (use them: suggest their tools and respect their rules):\n- " + string.Join("\n- ", hints.ToArray()));
            string mods = Settings.ModsInContext ? Settings.ModsText : "";
            if (!string.IsNullOrEmpty(mods)) sb.AppendLine("Installed mods (parts and features from these may be on the craft): " + mods);
            sb.AppendLine("Lines marked \"!\" in the design review or node check are real problems found in the craft data: bring them up when they matter to the question. When the director asks to review a design, go through the review and the mission plan; when they ask about a node, go through the node check and suggest exact fixes (which way to drag, roughly how many m/s).");
            return sb.ToString();
        }

        /// <summary>The unlocked parts as prompt text, with the kinds <paramref name="about"/> names listed in full.</summary>
        public string EngPartsText(string about, int maxLines)
        {
            List<CraftPart> parts = null;
            try { parts = EngineeringCatalog(); } catch (Exception e) { LastError = "Parts catalogue: " + e.Message; }
            return EngCatalog.Text(parts, about, maxLines);
        }

        string EngUser(EngSnapshot snap, double ut)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Today: " + Clock.FormatDate(ut) + ".");
            sb.AppendLine("LIVE DATA");
            sb.AppendLine(EngDataText(snap));
            var plan = EngCurrentPlan(snap);
            if (plan != null) sb.AppendLine(EngMissions.PlanText(plan));
            string question = State.EngChat.Where(m => m.Role == "player").Select(m => m.Text).LastOrDefault() ?? "";
            // The parts the question is about: the one before it counts too, so a short follow-up keeps its list.
            var lastTwo = State.EngChat.Where(m => m.Role == "player").Select(m => m.Text).ToList();
            string partsText = EngPartsText(string.Join(" ", lastTwo.Skip(Math.Max(0, lastTwo.Count - 2)).ToArray()), 120);
            if (partsText != "") sb.AppendLine(partsText);
            if (snap != null && snap.Sheet != null && snap.Sheet.Parts.Count > 0)
            {
                sb.AppendLine(snap.Sheet.Text(snap.Stages, question));
                var power = EngPower(snap, plan);
                if (power != null) sb.AppendLine(CraftSheet.PowerText(power));
                var staging = snap.Sheet.StagingIssues(snap.Stages);
                sb.AppendLine("STAGING CHECK (computed):\n- " + (staging.Count > 0 ? string.Join("\n- ", staging.ToArray()) : "No staging problems found."));
                var checkList = EngChecks(snap, plan);
                string ql = question.ToLowerInvariant();
                bool wantSwaps = CheckWords["swaps"].Concat(CheckWords["efficiency"]).Any(w => ql.Contains(w)) || EverythingWords.Any(w => ql.Contains(w));
                var tools = EngToolReports(snap, plan, checkList, wantSwaps).Concat(EngMissionReports(snap, plan, checkList, CheckWords["tech"].Any(w => ql.Contains(w)))).ToList();
                // Tools the question is not about stay off the prompt, apart from a no-go, a live warning in flight, and lessons.
                var send = tools.Where(t =>
                {
                    string[] words;
                    bool asked = CheckWords.TryGetValue(t.Key, out words) && words.Any(w => ql.Contains(w)) || EverythingWords.Any(w => ql.Contains(w)) && t.Key != "compare";
                    return asked || t.Key == "lessons" || t.Key == "flight" && t.Rating != "nominal" || t.Key == "gonogo" && t.Bad;
                }).ToList();
                string checks = EngChecksText(checkList.Concat(send).ToList(), question);
                if (checks != "") sb.AppendLine(checks);
                string books = ModKnowledge.BooksText(snap.Sheet);
                if (books != "") sb.AppendLine(books);
            }
            var review = EngReview(snap, plan);
            if (review.Count > 0) sb.AppendLine("DESIGN REVIEW (computed from the craft sheet):\n- " + string.Join("\n- ", review.ToArray()));
            if (snap != null && snap.Vessel != "" && State.EngMission == "")
                sb.AppendLine("NO MISSION IS SET: the director has not said where this craft is going or what it will do there. If the question depends on that (landing, power far from the sun, delta-v, comms range, heat shield, supplies), ask first.");
            string inc = IncidentText();
            if (inc != "") sb.AppendLine("PAST INVESTIGATIONS:\n" + inc);
            string memory = EngMemoryText(snap, question);
            if (memory != "") sb.AppendLine(memory);
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
            EngSnapshot snap = null;
            snap = EngLook();
            EngLearnMission(text, snap);
            EngLearnPreference(text, snap, ut);
            EngLine("player", ResponseParser.Clip(text, 1500), ut);
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

        /// <summary>Picks the mission up from the chat: a destination named in a question, or the answer to "where is it going?".
        /// The director then only has to say it once.</summary>
        void EngLearnMission(string text, EngSnapshot snap)
        {
            if (text.StartsWith("Plan this mission:")) return;
            bool named = EngMissions.FindBody(text, snap) != null || (snap == null || snap.Bodies.Count == 0) && EngMissions.Find(text) != null;
            if (!named) return;
            var last = State.EngChat.LastOrDefault();
            bool answering = last != null && last.Role != "player" && last.Actions.StartsWith("opt:");
            if (answering)
            {
                // "Will this land safely?" ... "Where?" ... "Duna": the question and the answer together are the mission.
                string asked = State.EngChat.Where(m => m.Role == "player").Select(m => m.Text).LastOrDefault() ?? "";
                State.EngMission = ResponseParser.Clip((asked + " " + text).Trim(), 200);
            }
            else if (State.EngMission == "") State.EngMission = ResponseParser.Clip(text, 200);
        }

        /// <summary>The power budget where the craft is going: the planned destination, else where it is now, else home.</summary>
        public PowerReport EngPower(EngSnapshot snap, EngPlan plan)
        {
            if (snap == null || snap.Sheet == null || snap.Sheet.Parts.Count == 0) return null;
            var home = snap.Home;
            EngBody there = plan != null && plan.Known ? snap.BodyNamed(plan.Body) : null;
            if (there == null && (snap.Scene == "flight" || snap.Scene == "map")) there = snap.BodyNamed(snap.Body);
            bool flying = snap.Scene == "flight" || snap.Scene == "map";
            return snap.Sheet.Power(home, there ?? home, snap.Bodies, flying ? snap.Crew : -1);
        }

        /// <summary>Every computed check on the craft as it stands, for where it is going. No AI call.</summary>
        public List<CheckReport> EngChecks(EngSnapshot snap, EngPlan plan)
        {
            if (snap == null || snap.Sheet == null || snap.Sheet.Parts.Count == 0) return new List<CheckReport>();
            bool flying = snap.Scene == "flight" || snap.Scene == "map";
            var c = new CheckContext
            {
                Sheet = snap.Sheet, Home = snap.Home, Bodies = snap.Bodies, Plan = plan, Stages = snap.Stages,
                Editor = snap.Scene == "vab" || snap.Scene == "sph", Sph = snap.Scene == "sph",
                Crew = flying ? snap.Crew : snap.Sheet.Parts.Sum(p => p.Crew),
            };
            if (plan != null && plan.Known) { c.There = snap.BodyNamed(plan.Body); c.Dest = EngMissions.Find(plan.Body, snap); }
            else if (flying) c.There = snap.BodyNamed(snap.Body);
            return CraftChecks.All(c);
        }

        /// <summary>The tools on top of the checks: go/no-go, windows, contracts, science, the in-flight watch, efficiency, swaps.
        /// <paramref name="catalog"/> true reads the unlocked parts list (slow the first time) for swaps.</summary>
        public List<CheckReport> EngToolReports(EngSnapshot snap, EngPlan plan, List<CheckReport> checks, bool catalog, string compareId = "")
        {
            var list = new List<CheckReport>();
            if (snap == null || snap.Sheet == null || snap.Sheet.Parts.Count == 0) return list;
            Func<string, string, Func<CheckReport>, CheckReport> run = (key, title, f) =>
            {
                string was = Crumb.Enter("tool " + key);
                try { var r = f(); r.Key = key; r.Title = title; return r; }
                catch (Exception e) { var r = new CheckReport { Key = key, Title = title, Rating = "not computed" }; r.Add("This could not be worked out (" + e.GetType().Name + ")."); return r; }
                finally { Crumb.Exit(was); }
            };
            bool flying = snap.Scene == "flight" || snap.Scene == "map";
            var power = EngPower(snap, plan);
            var staging = snap.Sheet.StagingIssues(snap.Stages);
            var lessons = EngTools.Lessons(State.Incidents, snap);
            list.Add(run("gonogo", "Go / no-go", () => EngTools.GoNoGo(snap, plan, power, staging, checks, lessons)));
            if (flying) list.Add(run("flight", "In flight", () => EngTools.Flight(snap, power)));
            list.Add(run("windows", "Windows", () => EngTools.Windows(snap, plan != null && plan.Known ? plan.Body : "", ut => Clock.FormatDate(ut))));
            list.Add(run("contracts", "Contracts", () => EngTools.Contracts(snap)));
            list.Add(run("science", "Science", () => EngTools.Science(snap, plan)));
            CheckReport swaps = null;
            if (catalog)
            {
                List<CraftPart> parts = null;
                try { parts = EngineeringCatalog(); } catch (Exception e) { LastError = "Parts catalogue: " + e.Message; }
                bool air = snap.Home != null && snap.Home.AtmoDepth > 0 && !flying;
                swaps = run("swaps", "Part swaps", () => EngTools.Swaps(snap.Sheet, parts, snap.Stages, air));
            }
            list.Add(run("efficiency", "Efficiency", () => EngTools.Efficiency(power, checks, swaps)));
            if (swaps != null) list.Add(swaps);
            list.Add(run("compare", "Compare", () =>
            {
                var then = State.EngSnaps.FirstOrDefault(x => x.Id == compareId) ?? State.EngSnaps.LastOrDefault(x => x.Craft == snap.Vessel) ?? State.EngSnaps.LastOrDefault();
                return EngTools.Compare(then, EngTools.Snap("", "now", snap, power, staging, checks));
            }));
            if (lessons.Count > 0)
            {
                var l = new CheckReport { Key = "lessons", Title = "Lessons", Rating = lessons.Count + " from earlier flights" };
                foreach (var x in lessons) l.Add(x);
                list.Add(l);
            }
            return list;
        }

        /// <summary>Saves the craft's numbers as they stand, to compare a later version against.</summary>
        public CraftSnap EngTakeSnapshot(string name)
        {
            EngSnapshot snap = null;
            snap = EngLook();
            if (snap == null || snap.Sheet == null || snap.Sheet.Parts.Count == 0) return null;
            var plan = EngCurrentPlan(snap);
            int n = State.EngSnaps.Count(x => x.Craft == snap.Vessel) + 1;
            var s = EngTools.Snap(State.NewId("v"), string.IsNullOrEmpty(name) ? snap.Vessel + " version " + n : ResponseParser.Clip(name, 60), snap, EngPower(snap, plan), snap.Sheet.StagingIssues(snap.Stages), EngChecks(snap, plan));
            State.EngSnaps.Add(s);
            EngRemember("snapshot", snap.Vessel, "Snapshot \"" + s.Name + "\": " + EngMath.N(s.Mass, "0.0") + " t, " + EngMath.N(s.Dv) + " m/s, " + s.Parts + " parts, " + EngMath.N(s.Cost) + " funds.", State.EngChat.Count > 0 ? State.EngChat.Last().UT : 0);
            if (State.EngSnaps.Count > 40) State.EngSnaps.RemoveRange(0, State.EngSnaps.Count - 40);
            return s;
        }

        static readonly Dictionary<string, string[]> CheckWords = new Dictionary<string, string[]>
        {
            { "gonogo", new[] { "ready", "go/no", "go no", "good to launch", "can i launch", "safe to launch" } },
            { "flight", new[] { "now", "right now", "burn", "descent", "landing" } },
            { "windows", new[] { "window", "when should", "when do", "when can", "phase angle", "depart", "leave for" } },
            { "contracts", new[] { "contract" } },
            { "science", new[] { "science", "experiment" } },
            { "efficiency", new[] { "efficien", "oversized", "trim", "lighter", "too much", "too many", "overbuilt", "save weight", "save mass" } },
            { "swaps", new[] { "swap", "better engine", "better motor", "cheaper", "alternative", "upgrade", "replace", "better part", "which engine" } },
            { "compare", new[] { "compare", "snapshot", "version", "changed", "last time" } },
            { "lessons", new[] { "lesson", "last time", "crash", "failed", "before" } },
            { "heat", new[] { "heat", "radiator", "cool", "reactor", "loop", "thermal", "overheat", "temperature" } },
            { "comms", new[] { "antenna", "signal", "comm", "relay", "range", "link", "transmit", "dish" } },
            { "landing", new[] { "land", "chute", "parachute", "reentry", "re-entry", "shield", "touchdown", "touch down", "splash", "legs" } },
            { "balance", new[] { "balance", "wobble", "flip", "torque", "stable", "stability", "tumble", "of mass", "spin", "steer", "control", "reaction wheel", "fins" } },
            { "launch", new[] { "launch", "twr", "pad", "lift", "structure", "strut", "limit", "too big", "too heavy", "thrust", "engine", "motor" } },
            { "crew", new[] { "crew", "kerbal", "food", "oxygen", "water", "supplies", "life support", "seat", "sas", "pilot", "radiation", "habitat" } },
            { "cost", new[] { "cost", "price", "funds", "cheap", "expensive", "money", "budget" } },
            { "needs", new[] { "what do i need", "what does it need", "requirement", "need for", "needs for", "what would it take" } },
            { "timeline", new[] { "timeline", "how long", "schedule", "duration", "trip take", "days" } },
            { "reliability", new[] { "reliab", "fail", "ignition", "mtbf", "wear", "malfunction", "break" } },
            { "setup", new[] { "action group", "setup", "set up", "abort", "hotkey", "forgot", "forget" } },
            { "mining", new[] { "mining", "mine ", "drill", "ore", "isru", "refuel", "converter", "harvest" } },
            { "ascent", new[] { "ascent", "gravity turn", "climb", "to orbit", "lift-off", "liftoff", "launch profile" } },
            { "aerobrake", new[] { "aerobrak", "aerocapture", "periapsis", "atmosphere pass", "reentry", "re-entry" } },
            { "plane", new[] { "plane", "wing", "lift", "jet", "runway", "take off", "takeoff", "spaceplane", "fly" } },
            { "rover", new[] { "rover", "wheel", "drive", "driving" } },
            { "docking", new[] { "dock", "rendezvous", "rcs", "station", "assembl", "port" } },
            { "launcher", new[] { "launcher", "payload", "lifter", "proven", "to orbit", "how much can" } },
            { "tech", new[] { "tech", "research", "unlock", "science points", "tree" } },
            { "relays", new[] { "relay", "network", "coverage" } },
            { "predicted", new[] { "predicted", "prediction", "actual", "how did it", "expected" } },
        };
        /// <summary>The tab that covers what a sentence is about ("" when none stands out): for a meeting's "Show me" links.</summary>
        public static string TabFor(string lower)
        {
            string best = ""; int bestLen = 0;
            foreach (var kv in CheckWords)
                foreach (var w in kv.Value)
                    if (w.Length > bestLen && w.Length >= 4 && lower.Contains(w)) { best = kv.Key; bestLen = w.Length; }
            if (best == "lessons") best = "overview";
            if (best == "") best = lower.Contains("power") || lower.Contains("solar") || lower.Contains("batter") ? "power" : lower.Contains("stag") ? "stages" : "";
            return best;
        }

        static readonly string[] ToolKeys = { "gonogo", "flight", "windows", "contracts", "science", "efficiency", "swaps", "compare", "lessons", "needs", "timeline", "reliability", "setup", "mining", "ascent", "aerobrake", "plane", "rover", "docking", "launcher", "tech", "relays", "predicted" };
        static readonly string[] EverythingWords = { "review", "everything", "oversized", "efficien", "overbuilt", "too much", "missing", "all checks", "ready", "go/no" };

        /// <summary>The checks as prompt text: in full where there is a problem or the question is about them, one line otherwise.</summary>
        public static string EngChecksText(List<CheckReport> checks, string question)
        {
            if (checks == null || checks.Count == 0) return "";
            string q = (question ?? "").ToLowerInvariant();
            bool all = EverythingWords.Any(w => q.Contains(w));
            var sb = new StringBuilder("COMPUTED CHECKS (worked out by the team's tools from the craft sheet; quote these numbers, do not redo them):\n");
            var brief = new List<string>();
            foreach (var r in checks)
            {
                string[] words;
                bool asked = all || CheckWords.TryGetValue(r.Key, out words) && words.Any(w => q.Contains(w)) || ToolKeys.Contains(r.Key);
                if (asked || r.Bad) sb.AppendLine(r.Text());
                else brief.Add(r.Title + ": " + r.Rating + (r.Fixes.Count > 0 ? " (" + r.Fixes.Count + " suggestion" + (r.Fixes.Count == 1 ? "" : "s") + ")" : ""));
            }
            if (brief.Count > 0) sb.AppendLine("Other checks, in brief: " + string.Join("; ", brief.ToArray()) + ".");
            return sb.ToString().TrimEnd();
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
                int chatBefore = State.EngChat.Count;
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
                if (State.EngChat.Count > chatBefore) EngFileAnswer(snap, ut);
                // A clarifying question: its likely answers become one-click buttons under the last reply.
                var ask = Json.Obj(root) != null && Json.Obj(root).ContainsKey("ask") ? Json.Obj(root)["ask"] : null;
                if (ask != null && Json.Obj(ask) != null)
                {
                    var opts = Json.Arr(Json.Obj(ask).ContainsKey("options") ? Json.Obj(ask)["options"] : null);
                    var clean = opts == null ? new List<string>() : opts.Select(o => (o == null ? "" : o.ToString()).Replace("|", "/").Trim()).Where(o => o != "" && o.Length <= 40).Take(5).ToList();
                    string q = Json.Str(ask, "question", "").Trim();
                    var lastMsg = State.EngChat.LastOrDefault();
                    if (lastMsg != null && lastMsg.Role != "player" && (clean.Count > 0 || q != ""))
                    {
                        if (q != "" && !lastMsg.Text.Contains(q)) lastMsg.Text = (lastMsg.Text + "\n" + ResponseParser.Clip(q, 300)).Trim();
                        lastMsg.Actions = "opt:" + string.Join("|", clean.ToArray());
                        RaiseCommunity();
                    }
                }
            });
        }

        public void EngClear() { State.EngChat.Clear(); State.EngMission = ""; engRun++; EngBusy = false; }
    }
}
