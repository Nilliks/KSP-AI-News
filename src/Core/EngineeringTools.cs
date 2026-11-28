using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>A maneuver node the director placed, and where it leads.</summary>
    public class EngNode
    {
        public double Dv, Prograde, Normal, Radial;   // m/s
        public double TimeTo;                       // s until the node
        public string AfterBody = "";
        public double AfterApA, AfterPeA, AfterInc;
        public bool AfterEscape;
        /// <summary>An encounter after the burn: the body and the periapsis there (m, negative = impact).</summary>
        public string EncounterBody = "";
        public double EncounterPeA;
    }

    /// <summary>One leg of a mission budget, and which stages pay for it.</summary>
    public class EngLeg
    {
        public string Name = "";
        public double Dv;
        public string Stages = "";
        public bool Short;
    }

    public class EngPlan
    {
        public string Mission = "";
        public string Body = "";
        public bool Land, Return, Flyby;
        public List<EngLeg> Legs = new List<EngLeg>();
        public double Need, Have, Margin;
        public bool Known, Computed;
        /// <summary>The margin the director wants on top of the budget, percent.</summary>
        public double MarginPct = 10;
        public string Note = "";
        public double Total { get { return Legs.Sum(l => l.Dv); } }
    }

    /// <summary>A crash or failure, and the engineering team's findings once the director asks for them.</summary>
    public class Incident
    {
        public string Id = "", VesselId = "", VesselName = "", Kind = "", Detail = "";
        public double UT;
        public List<string> Crew = new List<string>();
        /// <summary>The last flight data before it happened, oldest first.</summary>
        public List<string> Telemetry = new List<string>();
        /// <summary>The flight recorder: twice-a-second samples of attitude, control inputs and SAS for the last seconds, oldest first.</summary>
        public List<string> Samples = new List<string>();
        /// <summary>What the game reported happening to the craft: parts hitting the ground, overheating, joints breaking, staging.</summary>
        public List<string> Events = new List<string>();
        public string Craft = "";
        /// <summary>open, working, done.</summary>
        public string Status = "open";
        public string Cause = "", Findings = "", Fixes = "", PublicSummary = "";
        public string ReleaseId = "";

        public void Save(DataNode n)
        {
            n.Add("id", Id); n.Add("vessel", VesselId); n.AddText("vesselName", VesselName); n.Add("kind", Kind); n.AddText("detail", Detail); n.Add("ut", UT);
            foreach (var c in Crew) n.AddText("crew", c);
            foreach (var t in Telemetry) n.AddText("tel", t);
            foreach (var t in Samples) n.AddText("box", t);
            foreach (var t in Events) n.AddText("evt", t);
            n.AddText("craft", Craft); n.Add("status", Status == "working" ? "open" : Status);
            n.AddText("cause", Cause); n.AddText("findings", Findings); n.AddText("fixes", Fixes); n.AddText("public", PublicSummary); n.Add("release", ReleaseId);
        }

        public static Incident Load(DataNode n)
        {
            var i = new Incident
            {
                Id = n.Get("id", ""), VesselId = n.Get("vessel", ""), VesselName = n.GetText("vesselName"), Kind = n.Get("kind", ""), Detail = n.GetText("detail"), UT = n.GetDouble("ut"),
                Craft = n.GetText("craft"), Status = n.Get("status", "open"), Cause = n.GetText("cause"), Findings = n.GetText("findings"), Fixes = n.GetText("fixes"),
                PublicSummary = n.GetText("public"), ReleaseId = n.Get("release", ""),
            };
            foreach (var c in n.GetAll("crew")) i.Crew.Add(TextCodec.Decode(c));
            foreach (var t in n.GetAll("tel")) i.Telemetry.Add(TextCodec.Decode(t));
            foreach (var t in n.GetAll("box")) i.Samples.Add(TextCodec.Decode(t));
            foreach (var t in n.GetAll("evt")) i.Events.Add(TextCodec.Decode(t));
            return i;
        }
    }

    public static class EngMissions
    {
        /// <summary>Stock delta-v legs from low Kerbin orbit (approximate, community map): transfer, capture to low orbit,
        /// landing, ascent, return to Kerbin (aerobraking at home), and surface gravity in m/s².</summary>
        public class Dest { public string Name; public double Transfer, Capture, Land, Ascent, Return, Gravity, Travel; public bool Aero, NoSurface, Computed; }

        public static readonly Dest[] Stock =
        {
            new Dest { Name = "Mun", Transfer = 860, Capture = 310, Land = 580, Ascent = 580, Return = 310, Gravity = 1.63 },
            new Dest { Name = "Minmus", Transfer = 930, Capture = 160, Land = 180, Ascent = 180, Return = 160, Gravity = 0.49 },
            new Dest { Name = "Duna", Transfer = 1080, Capture = 610, Land = 1450, Ascent = 1450, Return = 610, Gravity = 2.94, Aero = true },
            new Dest { Name = "Ike", Transfer = 1360, Capture = 180, Land = 390, Ascent = 390, Return = 460, Gravity = 1.10 },
            new Dest { Name = "Eve", Transfer = 1040, Capture = 1410, Land = 80, Ascent = 8000, Return = 1500, Gravity = 16.7, Aero = true },
            new Dest { Name = "Gilly", Transfer = 1180, Capture = 410, Land = 30, Ascent = 30, Return = 640, Gravity = 0.05 },
            new Dest { Name = "Moho", Transfer = 1710, Capture = 2410, Land = 870, Ascent = 870, Return = 3170, Gravity = 2.70 },
            new Dest { Name = "Dres", Transfer = 1560, Capture = 1720, Land = 430, Ascent = 430, Return = 2140, Gravity = 1.13 },
            new Dest { Name = "Jool", Transfer = 1930, Capture = 2810, Return = 2900, Aero = true, NoSurface = true },
            new Dest { Name = "Laythe", Transfer = 3020, Capture = 1070, Land = 0, Ascent = 2900, Return = 3000, Gravity = 7.85, Aero = true },
        };

        public const double LaunchDv = 3400;

        public static Dest Find(string text)
        {
            string t = " " + (text ?? "").ToLowerInvariant() + " ";
            foreach (var d in Stock.OrderByDescending(x => x.Name.Length))
                if (t.Contains(d.Name.ToLowerInvariant())) return d;
            if (t.Contains(" moon")) return Stock[0];
            return null;
        }

        /// <summary>The body a plain-English mission names, from the bodies in the save.</summary>
        public static EngBody FindBody(string text, EngSnapshot s)
        {
            if (s == null || s.Bodies.Count == 0) return null;
            string t = " " + (text ?? "").ToLowerInvariant() + " ";
            var home = s.Home;
            foreach (var b in s.Bodies.Where(x => !x.IsStar && (home == null || x.Name != home.Name)).OrderByDescending(x => x.Name.Length))
                if (System.Text.RegularExpressions.Regex.IsMatch(t, @"(^|[^a-z0-9])(the )?" + System.Text.RegularExpressions.Regex.Escape(b.Name.ToLowerInvariant()) + @"($|[^a-z0-9])")) return b;
            if (t.Contains(" moon") && home != null) return s.Bodies.Where(x => x.Parent == home.Name).OrderBy(x => x.Sma).FirstOrDefault();
            return null;
        }

        /// <summary>The destination's legs: computed from the save's real bodies when the game reported them, else the stock table.</summary>
        public static Dest Find(string text, EngSnapshot s)
        {
            var body = FindBody(text, s);
            if (body != null)
            {
                var d = DvPlanner.Route(s.Bodies, s.Home, body);
                if (d != null) return d;
            }
            if (s != null && s.Bodies.Count > 0 && !s.StockSystem) return null;
            return Find(text);
        }

        static bool Has(string t, params string[] words) { return words.Any(w => t.Contains(w)); }

        /// <summary>Budget for a plain-English mission against the craft's stages. Works only for the stock system.</summary>
        public static EngPlan Plan(string mission, EngSnapshot s, double marginPct = 10)
        {
            var p = new EngPlan { Mission = (mission ?? "").Trim(), MarginPct = marginPct };
            string t = " " + p.Mission.ToLowerInvariant() + " ";
            var d = Find(t, s);
            bool bodies = s != null && s.Bodies.Count > 0 && s.Home != null;
            if (d == null)
            {
                p.Note = bodies && FindBody(t, s) != null ? "The team has no simple route worked out for that body yet, so they will reason from the data."
                    : bodies || s == null || s.StockSystem ? "No destination named, so the team will work it out from the data."
                    : "A planet pack is installed, so the stock budget does not apply.";
                return p;
            }
            p.Known = true;
            p.Computed = d.Computed;
            p.Body = d.Name;
            string home = bodies ? s.Home.Name : "Kerbin";
            p.Flyby = Has(t, "flyby", "fly by", "fly-by", "slingshot");
            p.Land = !p.Flyby && !d.NoSurface && Has(t, "land", "surface", "touch down", "plant a flag", "walk on", "base", "rover");
            p.Return = Has(t, "back", "return", "home", "and come", "round trip", "round-trip", "rescue");
            bool onGround = s == null || s.Scene == "vab" || s.Scene == "sph" || s.Scene == "spacecenter" || s.Scene == "tracking"
                || (s.Body == home && !s.HasOrbit);
            bool inLko = s != null && s.Body == home && s.HasOrbit && s.PeA > 0;
            double launch = bodies ? DvPlanner.Ascent(s.Home) : LaunchDv;
            bool chutes = d.Aero && (!d.Computed || d.Land < d.Ascent * 0.5);
            if (onGround && !inLko) p.Legs.Add(new EngLeg { Name = "Launch to low " + home + " orbit", Dv = launch });
            p.Legs.Add(new EngLeg { Name = (p.Flyby ? "Flyby of " : "Transfer to ") + d.Name, Dv = d.Transfer });
            if (!p.Flyby) p.Legs.Add(new EngLeg { Name = "Capture into low " + d.Name + " orbit" + (d.Aero ? " (aerobraking can save most of this)" : ""), Dv = d.Capture });
            if (p.Land)
            {
                p.Legs.Add(new EngLeg { Name = "Land on " + d.Name + (chutes ? " (parachutes do most of the work)" : ""), Dv = d.Land });
                if (p.Return) p.Legs.Add(new EngLeg { Name = "Ascent back to " + d.Name + " orbit", Dv = d.Ascent });
            }
            if (p.Return) p.Legs.Add(new EngLeg { Name = (p.Flyby ? "Free-return correction" : "Return to " + home + (bodies && s.Home.AtmoDepth <= 0 ? "" : " (aerobrake at home)")), Dv = p.Flyby ? 100 : d.Return });
            p.Need = p.Total * (1 + p.MarginPct / 100);
            Allocate(p, s, d);
            return p;
        }

        /// <summary>Pays each leg from the stages in firing order, so the plan says which stage does what and where it runs out.</summary>
        static void Allocate(EngPlan p, EngSnapshot s, Dest d)
        {
            if (s == null || s.Stages.Count == 0) { p.Note = "No staging data yet, so this is the budget only."; return; }
            bool flying = s.Scene == "flight" || s.Scene == "map";
            var pool = s.Stages.OrderByDescending(x => x.Stage).Select(x => new double[] { x.Stage, flying ? x.DvActual : x.DvVac, x.DvAsl > 0 ? x.DvAsl : x.DvVac }).ToList();
            int i = 0;
            double have = 0;
            foreach (var x in pool) have += x[1];
            p.Have = have;
            foreach (var leg in p.Legs)
            {
                double need = leg.Dv;
                var used = new List<string>();
                bool launch = leg.Name.StartsWith("Launch");
                while (need > 0.5 && i < pool.Count)
                {
                    // Low in the atmosphere a stage gives less than its vacuum figure; use the sea-level one for the launch leg.
                    double avail = launch && pool[i][1] == pool[i][2] ? pool[i][1] : (launch ? Math.Min(pool[i][1], (pool[i][1] + pool[i][2]) / 2) : pool[i][1]);
                    if (avail <= 0.5) { i++; continue; }
                    double take = Math.Min(avail, need);
                    need -= take;
                    double scale = avail > 0 ? take / avail : 1;
                    pool[i][1] -= pool[i][1] * scale; pool[i][2] -= pool[i][2] * scale;
                    if (!used.Contains(((int)pool[i][0]).ToString(CultureInfo.InvariantCulture))) used.Add(((int)pool[i][0]).ToString(CultureInfo.InvariantCulture));
                    if (pool[i][1] <= 0.5) i++;
                }
                leg.Short = need > 0.5;
                leg.Stages = used.Count == 0 ? "nothing left" : "stage " + string.Join(", ", used.ToArray()) + (leg.Short ? ", then " + EngMath.N(need) + " m/s short" : "");
            }
            p.Margin = have - p.Total;
        }

        public static string PlanText(EngPlan p)
        {
            if (p == null || !p.Known) return p == null ? "" : "Mission: " + p.Mission + ". " + p.Note;
            var sb = new StringBuilder();
            sb.AppendLine("Mission plan for \"" + p.Mission + "\" (" + (p.Computed ? "computed from this system's real bodies" : "stock delta-v map") + ", approximate):");
            foreach (var l in p.Legs) sb.AppendLine("- " + l.Name + ": " + EngMath.N(l.Dv) + " m/s" + (l.Stages != "" ? " (" + l.Stages + ")" : ""));
            sb.Append("Total " + EngMath.N(p.Total) + " m/s, about " + EngMath.N(p.Need) + " with the " + EngMath.N(p.MarginPct) + "% margin the director asked for.");
            if (p.Have > 0) sb.Append(" The craft has " + EngMath.N(p.Have) + " m/s, so it is " + (p.Margin >= p.Total * p.MarginPct / 100 ? EngMath.N(p.Margin) + " m/s over the budget." : p.Margin >= 0 ? "just enough with almost no margin (" + EngMath.N(p.Margin) + " m/s spare)." : EngMath.N(-p.Margin) + " m/s short."));
            if (p.Note != "") sb.Append(" " + p.Note);
            return sb.ToString();
        }
    }

    public partial class NewsEngine
    {
        static int Count(EngSnapshot s, params string[] modules) { int n = 0; foreach (var m in modules) { int c; if (s.Modules.TryGetValue(m, out c)) n += c; } return n; }
        static double Res(EngSnapshot s, params string[] names) { double n = 0; foreach (var r in names) { double c; if (s.ResourceMax.TryGetValue(r, out c)) n += c; } return n; }

        /// <summary>Known mods that change engineering advice, from the Mods tab list.</summary>
        public static List<string> EngModHints(IEnumerable<ModInfo> mods)
        {
            var hints = new List<string>();
            if (mods == null) return hints;
            var on = mods.Where(m => m.Installed).Select(m => (m.Folder + " " + m.Name).ToLowerInvariant()).ToList();
            Func<string[], bool> has = keys => on.Any(m => keys.Any(k => m.Contains(k)));
            if (has(new[] { "mechjeb" })) hints.Add("MechJeb is installed: point the director to its Maneuver Planner, Ascent Guidance, Rendezvous and Landing Guidance windows when they save effort, but still explain the numbers.");
            if (has(new[] { "kerbalengineer", "kerbal engineer" })) hints.Add("Kerbal Engineer Redux is installed: its flight and build readouts can be referenced.");
            if (has(new[] { "realchute" })) hints.Add("RealChute is installed: parachutes are RealChute parts that are set up in the editor (deployment altitude, canopy size); check the predicted landing speed.");
            if (has(new[] { "taclifesupport", "tac life support", "tac-ls" })) hints.Add("TAC Life Support is installed: crewed craft need Food, Water and Oxygen (and power for scrubbers) for the whole mission.");
            if (has(new[] { "usilifesupport", "usi-ls", "usi life support", "umbraspaceindustries" })) hints.Add("USI Life Support is installed: crew need Supplies (and give off Mulch); kerbals without supplies stop working.");
            if (has(new[] { "snacks" })) hints.Add("Snacks is installed: crew eat Snacks every day; pack enough for the trip.");
            if (has(new[] { "kerbalism" })) hints.Add("Kerbalism is installed: food, water, oxygen, radiation, stress and part failures matter; plan supplies, shielding and redundancy.");
            if (has(new[] { "remotetech" })) hints.Add("RemoteTech is installed: probes need a connection path with line of sight and pointed dishes, and there is signal delay; plan relays.");
            if (has(new[] { "ferramaerospace", "ferram", " far " })) hints.Add("FAR is installed: aerodynamics are realistic; check stability, keep fins at the back, and expect different gravity turns and reentry behaviour.");
            if (has(new[] { "deadlyreentry", "deadly reentry" })) hints.Add("Deadly Reentry is installed: anything coming back from orbit needs a heat shield and a shallow entry.");
            if (has(new[] { "principia" })) hints.Add("Principia is installed: n-body gravity, so patched-conic numbers and Hohmann windows are only first guesses.");
            if (has(new[] { "realsolarsystem", "realism overhaul", "realismoverhaul", "rss" })) hints.Add("Real Solar System / Realism Overhaul: real-scale planets and engines with limited ignitions and ullage; stock numbers are far too low.");
            if (has(new[] { "trajectories" })) hints.Add("Trajectories is installed: it shows the predicted impact point through the atmosphere.");
            if (has(new[] { "kerbalalarmclock", "alarm clock" })) hints.Add("Kerbal Alarm Clock is installed: suggest alarms for nodes and transfer windows.");
            if (has(new[] { "scansat" })) hints.Add("SCANsat is installed: scanning orbits (inclination, altitude) matter for mapping missions.");
            if (has(new[] { "kerbaljointreinforcement", "kjr" })) hints.Add("Kerbal Joint Reinforcement is installed, so wobbly stacks are less of a problem.");
            if (has(new[] { "transferwindowplanner", "transfer window planner", "triggertech" })) hints.Add("Transfer Window Planner is installed: point the director to it for interplanetary departure dates and ejection burns.");
            if (has(new[] { "kos" })) hints.Add("kOS is installed: scripts can automate launches and burns if the director asks.");
            if (has(new[] { "persistentthrust" })) hints.Add("Persistent Thrust is installed: low-thrust engines keep burning during time warp.");
            if (has(new[] { "tweakscale" })) hints.Add("TweakScale is installed: parts can be resized, which changes their mass and thrust.");
            return hints;
        }

        /// <summary>Design review from the parts list: things that end missions before launch. Each line starts with "!" when it is a problem.</summary>
        public List<string> EngReview(EngSnapshot s, EngPlan plan)
        {
            var r = new List<string>();
            if (s == null || s.Vessel == "" || s.Modules.Count == 0 && s.Parts == 0) return r;
            var mods = EngModHints(Settings.Mods);
            bool crewed = s.CrewCapacity > 0 || s.Crew > 0;
            bool home = plan == null || !plan.Known || plan.Return;
            int command = Count(s, "ModuleCommand");
            int chutes = Count(s, "ModuleParachute", "RealChuteModule", "ModuleKrParachute");
            int antennas = Count(s, "ModuleDataTransmitter", "ModuleRTAntenna", "ModuleRTAntennaPassive");
            int power = Count(s, "ModuleDeployableSolarPanel", "ModuleGenerator", "ModuleCurvedSolarPanel", "FNGenerator", "ModuleResourceConverter", "ModuleROTGenerator");
            double ec = Res(s, "ElectricCharge");
            if (command == 0) r.Add("! No probe core or command pod: nothing can control the craft.");
            if (crewed && chutes == 0 && home) r.Add("! No parachutes: the crew has no way to land safely on Kerbin.");
            else if (!crewed && chutes == 0 && home && plan != null && plan.Known) r.Add("No parachutes: fine if the probe is not coming home.");
            if (antennas == 0) r.Add(crewed ? "No antenna: the crew can fly it, but science cannot be transmitted and there is no link for probes." : "! No antenna: a probe loses its CommNet link (and control, with signal required) once it is out of range, and cannot transmit science.");
            if (command > 0 && power == 0) r.Add((ec < 600 ? "! " : "") + "No solar panels, RTG or fuel cell: the batteries (" + EngMath.N(ec) + " EC) are all the power there is.");
            if (s.Stages.Count > 0)
            {
                var order = s.Stages.OrderByDescending(x => x.Stage).ToList();
                var first = order[0];
                bool editor = s.Scene == "vab" || s.Scene == "sph";
                if (editor && s.Scene == "vab" && first.TwrAsl > 0 && first.TwrAsl < 1.15) r.Add("! Launch stage TWR is " + EngMath.N(first.TwrAsl, "0.00") + " at sea level: it will barely lift off (aim for 1.2 to 1.6).");
                foreach (var st in order.Skip(1))
                {
                    if (st.DvVac < 150 || st.TwrVac <= 0) continue;
                    if (st.TwrVac < 0.35) r.Add("! Stage " + st.Stage + " upper-stage TWR is only " + EngMath.N(st.TwrVac, "0.00") + " (burn " + EngMath.Dur(st.BurnTime) + "): circularizing and capture burns will be long and wasteful. A bigger engine or less mass would help.");
                    else if (st.TwrVac < 0.6) r.Add("Stage " + st.Stage + " has a low TWR of " + EngMath.N(st.TwrVac, "0.00") + " in vacuum: fine for transfers, slow for circularizing.");
                }
                if (plan != null && plan.Known && plan.Land)
                {
                    var d = EngMissions.Find(plan.Body, s);
                    var landLeg = plan.Legs.FirstOrDefault(l => l.Name.StartsWith("Land"));
                    int stg;
                    if (d != null && d.Gravity > 0 && landLeg != null && landLeg.Stages.StartsWith("stage ") && int.TryParse(landLeg.Stages.Substring(6).Split(',')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out stg))
                    {
                        var ls = s.Stages.FirstOrDefault(x => x.Stage == stg);
                        if (ls != null && ls.TwrVac > 0)
                        {
                            double local = ls.TwrVac * 9.80665 / d.Gravity;
                            if (local < 1.5 && !d.Aero) r.Add("! The landing stage (stage " + stg + ") would have a TWR of " + EngMath.N(local, "0.0") + " on " + d.Name + ": too low to stop safely (aim for 2 or more).");
                        }
                    }
                    if (Count(s, "ModuleWheelBase", "ModuleLandingLeg", "ModuleWheelDeployment") == 0) r.Add("No landing legs: landing on the engine bell or tanks is risky on uneven ground.");
                }
            }
            if (plan != null && plan.Known && plan.Have > 0)
            {
                if (plan.Margin < 0) r.Add("! Not enough delta-v for \"" + plan.Mission + "\": " + EngMath.N(-plan.Margin) + " m/s short of the " + EngMath.N(plan.Total) + " m/s budget.");
                else if (plan.Margin < plan.Total * plan.MarginPct / 100) r.Add("Delta-v for \"" + plan.Mission + "\" is just enough, with " + EngMath.N(plan.Margin) + " m/s spare; the margin asked for is " + EngMath.N(plan.MarginPct) + "%.");
            }
            bool far = plan != null && plan.Known && plan.Body != "Mun" && plan.Body != "Minmus";
            bool dre = mods.Any(h => h.StartsWith("Deadly Reentry"));
            if (crewed && home && Count(s, "ModuleAblator") == 0 && (dre || far)) r.Add((dre ? "! " : "") + "No heat shield: " + (dre ? "with Deadly Reentry, coming home from orbit needs one." : "coming home from " + plan.Body + " is a fast, hot entry; a heat shield is cheap insurance."));
            if (crewed)
            {
                if (mods.Any(h => h.StartsWith("TAC Life")) && Res(s, "Food", "Water", "Oxygen") <= 0) r.Add("! TAC Life Support is installed but the craft carries no Food, Water or Oxygen.");
                if (mods.Any(h => h.StartsWith("USI Life")) && Res(s, "Supplies") <= 0) r.Add("! USI Life Support is installed but the craft carries no Supplies.");
                if (mods.Any(h => h.StartsWith("Snacks")) && Res(s, "Snacks") <= 0) r.Add("! Snacks is installed but there are no Snacks aboard.");
                if (mods.Any(h => h.StartsWith("Kerbalism")) && Res(s, "Food", "Oxygen") <= 0) r.Add("! Kerbalism is installed but the craft carries no Food or Oxygen.");
            }
            if (s.Sheet != null && s.Sheet.Parts.Count > 0)
            {
                string mt = (State.EngMission ?? "").ToLowerInvariant();
                bool docking = mt.Contains("dock") || mt.Contains("station") || mt.Contains("rendezvous");
                r.AddRange(s.Sheet.Missing(crewed, plan != null && plan.Known && plan.Land, docking));
                var pw = EngPower(s, plan);
                if (pw != null && pw.Rating.StartsWith("short")) r.Add("! Power is " + pw.Rating + (pw.Where != "" ? " at " + pw.Where : "") + ": makes " + CraftSheet.Ec(pw.GenThere) + ", needs up to " + CraftSheet.Ec(pw.Coast + pw.Working) + ". See the power budget.");
                else if (pw != null && pw.Rating == "oversized") r.Add("Power is oversized" + (pw.Where != "" ? " for " + pw.Where : "") + ": makes " + CraftSheet.Ec(pw.GenThere) + " against a peak of " + CraftSheet.Ec(pw.Coast + pw.Working) + ". Some panels could go.");
                int stagingBad = s.Sheet.StagingIssues(s.Stages).Count(x => x.StartsWith("!"));
                if (stagingBad > 0) r.Add("! " + stagingBad + " staging problem" + (stagingBad == 1 ? "" : "s") + ": see the staging check.");
            }
            if (r.Count == 0) r.Add("No obvious problems: control, power, comms" + (crewed ? ", parachutes" : "") + " and staging look fine.");
            return r;
        }

        /// <summary>Checks a placed maneuver node against the craft.</summary>
        public static List<string> EngNodeCheck(EngSnapshot s)
        {
            var r = new List<string>();
            var n = s == null ? null : s.Node;
            if (n == null) return r;
            var cur = s.Stages.Where(x => x.DvActual > 0.5 || x.DvVac > 0.5).OrderByDescending(x => x.Stage).FirstOrDefault();
            double total = s.TotalDvActual > 0 ? s.TotalDvActual : s.TotalDvVac;
            r.Add("Node: " + EngMath.N(n.Dv) + " m/s (prograde " + EngMath.N(n.Prograde) + ", normal " + EngMath.N(n.Normal) + ", radial " + EngMath.N(n.Radial) + "), in " + EngMath.Dur(n.TimeTo) + ".");
            if (total > 0 && n.Dv > total) r.Add("! The burn needs " + EngMath.N(n.Dv) + " m/s but the craft only has " + EngMath.N(total) + " m/s.");
            else if (cur != null && n.Dv > (cur.DvActual > 0 ? cur.DvActual : cur.DvVac) + 1) r.Add("The current stage runs dry partway through; stage during the burn.");
            if (cur != null)
            {
                double burn = EngMath.BurnTime(n.Dv, cur.ThrustVac, cur.StartMass, cur.IspVac);
                if (burn > 0)
                {
                    r.Add("Estimated burn time " + EngMath.Dur(burn) + "; start about " + EngMath.Dur(burn / 2) + " before the node.");
                    if (burn / 2 > n.TimeTo) r.Add("! The node is too close: half the burn (" + EngMath.Dur(burn / 2) + ") is longer than the time left. Move the node one orbit later.");
                }
            }
            if (n.EncounterBody != "")
            {
                if (n.EncounterPeA < 0) r.Add("! The node puts you on an impact course with " + n.EncounterBody + ". Add a little radial or normal to raise the periapsis there, unless that is the plan.");
                else r.Add("Encounter with " + n.EncounterBody + ", periapsis " + EngMath.Dist(n.EncounterPeA) + ".");
            }
            else if (s.Target != "" && s.TransferR2 > 0 && !n.AfterEscape && Math.Abs(n.AfterApA + s.BodyRadius - s.TransferR2) / s.TransferR2 < 0.25)
                r.Add("The orbit reaches " + s.Target + "'s altitude but there is no encounter yet: slide the node along the orbit or add a few m/s prograde until one appears.");
            if (n.AfterBody != "" && !n.AfterEscape && n.EncounterBody == "")
            {
                if (n.AfterPeA < 0) r.Add("! After the burn the periapsis is below " + n.AfterBody + "'s surface.");
                else if (n.AfterBody == s.Body && s.BodyAtmosphere > 0 && n.AfterPeA < s.BodyAtmosphere) r.Add("After the burn the periapsis (" + EngMath.Dist(n.AfterPeA) + ") is inside the atmosphere: good for reentry or aerobraking, bad if you meant to stay in orbit.");
                else r.Add("After the burn: apoapsis " + EngMath.Dist(n.AfterApA) + ", periapsis " + EngMath.Dist(n.AfterPeA) + ".");
            }
            if (n.AfterEscape && n.EncounterBody == "") r.Add("After the burn the craft escapes " + n.AfterBody + ".");
            if (Math.Abs(n.Radial) > Math.Max(50, Math.Abs(n.Prograde) * 0.5)) r.Add("A lot of the burn is radial, which is inefficient; prograde or retrograde usually does the job for less.");
            return r;
        }

        // ------------------------------------------------------------------ mission plans

        /// <summary>Files the mission as it now stands in the craft's notebook.</summary>
        public void EngNoteMission(double ut)
        {
            if (State.EngMission == "") return;
            var s = EngLook();
            EngRemember("plan", s != null ? s.Vessel : "", "Mission set" + (s != null && s.Vessel != "" ? " for " + s.Vessel : "") + ": " + State.EngMission + ".", ut);
        }

        public EngPlan EngCurrentPlan(EngSnapshot s) { return State.EngMission == "" ? null : EngMissions.Plan(State.EngMission, s, State.EngMargin); }

        /// <summary>The director names a mission; the plan is computed here and the team is asked to go through it.</summary>
        public bool EngPlanMission(string mission, double ut)
        {
            mission = (mission ?? "").Trim();
            if (mission == "" || !Settings.EngineeringOn) return false;
            State.EngMission = ResponseParser.Clip(mission, 200);
            EngNoteMission(ut);
            return EngSend("Plan this mission: " + State.EngMission + ". Go through the delta-v budget leg by leg and say which stage does what.", ut);
        }

        // ------------------------------------------------------------------ incidents

        readonly Dictionary<string, List<string>> engTrack = new Dictionary<string, List<string>>();
        readonly Dictionary<string, string> engCraft = new Dictionary<string, string>();
        readonly Dictionary<string, List<string>> boxSamples = new Dictionary<string, List<string>>();
        readonly Dictionary<string, List<KeyValuePair<double, string>>> boxEvents = new Dictionary<string, List<KeyValuePair<double, string>>>();
        public const int BoxSamples = 24, BoxEvents = 30;

        static void Ring<T>(Dictionary<string, List<T>> d, string id, T item, int max)
        {
            List<T> l;
            if (!d.TryGetValue(id, out l)) { l = new List<T>(); d[id] = l; if (d.Count > 20) d.Remove(d.Keys.First(k => k != id)); }
            l.Add(item);
            while (l.Count > max) l.RemoveAt(0);
        }

        /// <summary>One flight-recorder sample (attitude, control inputs, SAS), recorded twice a second in flight.</summary>
        public void BlackBoxSample(string vesselId, string line)
        {
            if (string.IsNullOrEmpty(vesselId) || string.IsNullOrEmpty(line)) return;
            Ring(boxSamples, vesselId, line, BoxSamples);
        }

        /// <summary>Something the game reported happening to the craft. If an incident is already open for it, the event joins that incident.</summary>
        public void BlackBoxEvent(string vesselId, string line, double ut)
        {
            if (string.IsNullOrEmpty(vesselId) || string.IsNullOrEmpty(line)) return;
            Ring(boxEvents, vesselId, new KeyValuePair<double, string>(ut, line), BoxEvents);
            var inc = State.Incidents.LastOrDefault(i => i.VesselId == vesselId && Math.Abs(i.UT - ut) < 30);
            if (inc != null && !inc.Events.Contains(line) && inc.Events.Count < 40) inc.Events.Add(line);
        }

        /// <summary>The KSP layer reports the active vessel every few seconds in flight, so an investigation has the last data before a crash.</summary>
        public void EngRecord(EngSnapshot s, double ut)
        {
            if (s == null || s.VesselId == "") return;
            try { EngTrackFlight(s, ut); } catch (Exception e) { LastError = "Flight tracking: " + e.Message; }
            List<string> l;
            if (!engTrack.TryGetValue(s.VesselId, out l)) { l = new List<string>(); engTrack[s.VesselId] = l; if (engTrack.Count > 20) engTrack.Remove(engTrack.Keys.First(k => k != s.VesselId)); }
            var cur = s.Stages.OrderByDescending(x => x.Stage).FirstOrDefault();
            l.Add(Clock.FormatDate(ut) + " T" + EngMath.N(ut, "0") + ": " + s.Situation + ", altitude " + EngMath.Dist(s.Altitude) + ", speed " + EngMath.N(s.Speed) + " m/s"
                + (s.VerticalSpeed != 0 ? ", vertical " + EngMath.N(s.VerticalSpeed) + " m/s" : "") + (s.GForce > 0 ? ", " + EngMath.N(s.GForce, "0.0") + " g" : "")
                + (s.Throttle >= 0 ? ", throttle " + EngMath.N(s.Throttle * 100) + "%" : "") + ", stage " + s.CurrentStage + ", " + s.Parts + " parts, " + EngMath.N(s.TotalDvActual) + " m/s left"
                + (cur != null && cur.TwrActual > 0 ? ", TWR " + EngMath.N(cur.TwrActual, "0.00") : "") + (s.HasOrbit ? ", Ap " + EngMath.Dist(s.ApA) + " Pe " + EngMath.Dist(s.PeA) : ""));
            if (l.Count > 10) l.RemoveAt(0);
            if (s.Parts > 0) engCraft[s.VesselId] = s.Vessel + ": " + s.Parts + " parts, " + EngMath.N(s.Mass, "0.0") + " t, " + EngMath.N(s.TotalDvVac) + " m/s total (vacuum) at last check.";
        }

        /// <summary>A failure or a death opens an incident (merged with one for the same vessel in the last minute). No AI call until the director asks.</summary>
        void RecordIncident(NewsEvent e)
        {
            if (e == null || (e.Type != EventTypes.Failure && e.Type != EventTypes.CrewKilled)) return;
            var inc = State.Incidents.LastOrDefault(i => i.VesselId == e.VesselId && e.VesselId != "" && Math.Abs(i.UT - e.UT) < 60);
            if (inc == null)
            {
                inc = new Incident { Id = State.NewId("inc"), VesselId = e.VesselId ?? "", VesselName = e.VesselName ?? "", UT = e.UT, Kind = e.Type };
                List<string> tel;
                if (e.VesselId != null && engTrack.TryGetValue(e.VesselId, out tel)) inc.Telemetry.AddRange(tel);
                List<string> box;
                if (e.VesselId != null && boxSamples.TryGetValue(e.VesselId, out box)) inc.Samples.AddRange(box);
                List<KeyValuePair<double, string>> evs;
                if (e.VesselId != null && boxEvents.TryGetValue(e.VesselId, out evs)) inc.Events.AddRange(evs.Where(x => e.UT - x.Key < 120).Select(x => x.Value));
                string craft;
                if (e.VesselId != null && engCraft.TryGetValue(e.VesselId, out craft)) inc.Craft = craft;
                TrackedVessel tv;
                if (inc.Craft == "" && e.VesselId != null && State.Tracked.TryGetValue(e.VesselId, out tv) && tv.Craft != null) inc.Craft = PromptBuilder.CraftText(tv.Craft);
                State.Incidents.Add(inc);
                if (State.Incidents.Count > 40) State.Incidents.RemoveAt(0);
            }
            if (e.Type == EventTypes.CrewKilled) inc.Kind = EventTypes.CrewKilled;
            inc.Detail = (inc.Detail + " " + (e.Detail ?? "")).Trim();
            foreach (var c in e.Crew) if (!inc.Crew.Contains(c)) inc.Crew.Add(c);
        }

        public string EngInvestigating = "";

        /// <summary>The team investigates an incident: one writer-model call. Findings stay internal until the director shares them.</summary>
        public bool EngInvestigate(string id, double ut)
        {
            var inc = State.Incidents.FirstOrDefault(i => i.Id == id);
            if (inc == null || !Settings.EngineeringOn || inc.Status == "working") return false;
            if (!Settings.CanCallAi)
            {
                inc.Status = "done";
                inc.Cause = "Unknown (offline)";
                inc.Findings = (inc.Samples.Count > 0 ? "Last recorder sample: " + inc.Samples.Last() + ". " : inc.Telemetry.Count > 0 ? "Last data before the incident: " + inc.Telemetry.Last() + ". " : "No flight data was recorded before the incident. ")
                    + (inc.Events.Count > 0 ? "Reported: " + string.Join("; ", inc.Events.Skip(Math.Max(0, inc.Events.Count - 4)).ToArray()) + "." : "");
                return true;
            }
            inc.Status = "working";
            EngInvestigating = inc.Id;
            var sb = new StringBuilder();
            sb.AppendLine("You are the engineering team of " + AgencyName + " in Kerbal Space Program (KSP1), writing a failure investigation for the director. Work like a real accident board reading a flight recorder.");
            sb.AppendLine("RULES:");
            sb.AppendLine("- Use ONLY the recorded data below. Never invent a mechanical failure, a part, a reading or a cause the data does not show. If the data cannot explain something, say it is unknown.");
            sb.AppendLine("- Control inputs are recorded. If the controls were deflected hard (pitch, yaw or roll near +1 or -1) shortly before the crash, report that plainly as the first finding, in the data's own terms: for example \"we recorded a full right yaw input for the last six seconds, with SAS off, and the craft turned into the ground; we can't tell from the data why the input was made\". The pilot or autopilot may be the cause; say so without blaming named crew.");
            sb.AppendLine("- Separate what the data shows (certain) from what probably happened (likely). Mention a structural, aerodynamic, fuel or heat cause only if the samples or events show it (joints breaking, overheating, thrust gone, flipping with the controls neutral, and so on).");
            sb.AppendLine("- Speak like engineers in a briefing: short, plain sentences with the actual numbers.");
            var mods = EngModHints(Settings.Mods);
            if (mods.Count > 0) sb.AppendLine("Mods that matter: " + string.Join(" ", mods.ToArray()));
            sb.AppendLine("Reply with one JSON object only: {\"cause\": \"most probable cause, one short sentence, or 'Unknown' with what is missing\", \"certainty\": \"confirmed|likely|unknown\", \"findings\": \"what the recorder shows, 3-6 sentences, citing the numbers\", \"fixes\": \"what to change next time, 2-4 short points in one string\", \"public_summary\": \"a 2-3 sentence statement the agency could give the press, factual and calm, no blame on named crew\"}.");
            var u = new StringBuilder();
            u.AppendLine("Incident: " + (inc.Kind == EventTypes.CrewKilled ? "loss of crew" : "vehicle failure") + " of " + (inc.VesselName != "" ? inc.VesselName : "an agency craft") + " on " + Clock.FormatDate(inc.UT) + ".");
            if (inc.Detail != "") u.AppendLine("What was seen: " + inc.Detail);
            if (inc.Crew.Count > 0) u.AppendLine("Crew aboard: " + string.Join(", ", inc.Crew.ToArray()) + ".");
            if (inc.Craft != "") u.AppendLine("Craft: " + inc.Craft);
            if (inc.Samples.Count > 0)
                u.AppendLine("Flight recorder, twice a second, oldest first (pitch is the nose angle above the horizon; ctrl is the control output from -1 to +1, from the pilot or autopilot; input is what the pilot pressed):\n- " + string.Join("\n- ", inc.Samples.ToArray()));
            u.AppendLine(inc.Telemetry.Count > 0 ? "Slower flight data (every few seconds, oldest first):\n- " + string.Join("\n- ", inc.Telemetry.ToArray()) : "No slower flight data was recorded.");
            u.AppendLine(inc.Events.Count > 0 ? "What the game reported happening to the craft, in order:\n- " + string.Join("\n- ", inc.Events.ToArray()) : "No part events were reported.");
            if (inc.Samples.Count == 0 && inc.Telemetry.Count == 0) u.AppendLine("The recorder was not running for this vessel, so say the cause cannot be determined from data.");
            AskJson("Engineering team", Settings.Writer, sb.ToString(), u.ToString(), 1600, 0, (root, err, text, res) =>
            {
                EngInvestigating = "";
                if (root == null) { inc.Status = "open"; LastError = err ?? "No reply"; RaiseCommunity(); return; }
                inc.Cause = ResponseParser.Clip(Json.Str(root, "cause", "").Trim(), 300);
                string certainty = Json.Str(root, "certainty", "").Trim().ToLowerInvariant();
                if (certainty == "unknown" || certainty == "likely") inc.Cause = "(" + certainty + ") " + inc.Cause;
                inc.Findings = ResponseParser.Clip(Json.Str(root, "findings", "").Trim(), 2000);
                inc.Fixes = ResponseParser.Clip(Json.Str(root, "fixes", "").Trim(), 1200);
                inc.PublicSummary = ResponseParser.Clip(Json.Str(root, "public_summary", "").Trim(), 800);
                inc.Status = "done";
                EngRemember("investigation", inc.VesselName, "Investigation of " + (inc.VesselName != "" ? inc.VesselName : "a craft") + ": " + inc.Cause + (inc.Fixes != "" ? " Next time: " + ResponseParser.Clip(inc.Fixes, 300) : ""), ut);
                RaiseCommunity();
            });
            return true;
        }

        /// <summary>Shares an investigation's public summary as an incident press release, which the news then covers.</summary>
        public PressRelease EngShareFindings(string id, double ut)
        {
            var inc = State.Incidents.FirstOrDefault(i => i.Id == id);
            if (inc == null || inc.Status != "done" || inc.ReleaseId != "") return null;
            string body = (inc.PublicSummary != "" ? inc.PublicSummary : inc.Cause) + (inc.Cause != "" ? "\n\nProbable cause: " + inc.Cause : "");
            var r = PublishRelease("incident", "Findings of the " + (inc.VesselName != "" ? inc.VesselName : "incident") + " investigation", body, inc.VesselId, null, ut);
            if (r != null) { inc.ReleaseId = r.Id; Log("release", "Shared the engineering findings on " + (inc.VesselName != "" ? inc.VesselName : "the incident"), 0, false, ut); }
            return r;
        }

        /// <summary>For the assistant and the news: investigations, marked internal or released.</summary>
        public string IncidentText(string vesselId = null, bool publicOnly = false)
        {
            var list = State.Incidents.Where(i => i.Status == "done" && (vesselId == null || i.VesselId == vesselId) && (!publicOnly || i.ReleaseId != "")).Reverse().Take(4).ToList();
            if (list.Count == 0) return "";
            return string.Join("\n", list.Select(i => "- " + (i.VesselName != "" ? i.VesselName : "Incident") + " (" + Clock.FormatDate(i.UT) + ", " + (i.ReleaseId != "" ? "findings released to the press" : "internal, not released") + "): cause " + i.Cause + ". " + (publicOnly || i.ReleaseId != "" ? i.PublicSummary : i.Findings)).ToArray());
        }
    }
}
