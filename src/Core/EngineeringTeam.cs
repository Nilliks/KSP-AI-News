using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>Something the engineering team keeps on file: a question and its answer, a decision, a preference, a result.</summary>
    public class EngNote
    {
        public string Id = "", Kind = "", Craft = "", Text = "";
        public double UT;
        public bool Pinned;
        public void Save(DataNode n) { n.Add("id", Id); n.Add("kind", Kind); n.AddText("craft", Craft); n.AddText("text", Text); n.Add("ut", UT); n.Add("pinned", Pinned); }
        public static EngNote Load(DataNode n) { return new EngNote { Id = n.Get("id", ""), Kind = n.Get("kind", ""), Craft = n.GetText("craft"), Text = n.GetText("text"), UT = n.GetDouble("ut"), Pinned = n.GetBool("pinned") }; }
    }

    public class MeetingTurn
    {
        /// <summary>Who spoke (a team key, "director" or "tools"), what kind of turn (agenda, position, talk, test, vote, director), the words, and a one-line summary.</summary>
        public string Who = "", Kind = "talk", Text = "", Note = "";
    }

    /// <summary>A team meeting: specialists speak in turn, one AI call each, about an objective the director typed.</summary>
    public class EngMeeting
    {
        public string Id = "", Objective = "", Craft = "", FollowsId = "";
        /// <summary>running, waiting (the director has to break a tie), done or stopped.</summary>
        public string Status = "running";
        public double UT;
        public int Budget = 10, Used;
        public bool Closing;
        public string Next = "";
        public string Question = "", OptionA = "", OptionB = "";
        public string Minutes = "";
        public List<string> Agenda = new List<string>(), Decisions = new List<string>(), Open = new List<string>();
        public List<string> Actions = new List<string>();
        public List<bool> Done = new List<bool>();
        public List<MeetingTurn> Turns = new List<MeetingTurn>();

        public void Save(DataNode n)
        {
            n.Add("id", Id); n.AddText("objective", Objective); n.AddText("craft", Craft); n.Add("follows", FollowsId); n.Add("status", Status); n.Add("ut", UT);
            n.Add("budget", Budget); n.Add("used", Used); n.Add("closing", Closing); n.Add("next", Next);
            n.AddText("question", Question); n.AddText("a", OptionA); n.AddText("b", OptionB); n.AddText("minutes", Minutes);
            foreach (var x in Agenda) n.AddText("agenda", x);
            foreach (var x in Decisions) n.AddText("decision", x);
            foreach (var x in Open) n.AddText("open", x);
            for (int i = 0; i < Actions.Count; i++) n.AddText("action", (i < Done.Count && Done[i] ? "1" : "0") + Actions[i]);
            foreach (var t in Turns) { var tn = n.AddNode("TURN"); tn.Add("who", t.Who); tn.Add("kind", t.Kind); tn.AddText("text", t.Text); tn.AddText("note", t.Note); }
        }

        public static EngMeeting Load(DataNode n)
        {
            var m = new EngMeeting
            {
                Id = n.Get("id", ""), Objective = n.GetText("objective"), Craft = n.GetText("craft"), FollowsId = n.Get("follows", ""), Status = n.Get("status", "done"), UT = n.GetDouble("ut"),
                Budget = n.GetInt("budget", 10), Used = n.GetInt("used"), Closing = n.GetBool("closing"), Next = n.Get("next", ""),
                Question = n.GetText("question"), OptionA = n.GetText("a"), OptionB = n.GetText("b"), Minutes = n.GetText("minutes"),
            };
            foreach (var x in n.GetAll("agenda")) m.Agenda.Add(TextCodec.Decode(x));
            foreach (var x in n.GetAll("decision")) m.Decisions.Add(TextCodec.Decode(x));
            foreach (var x in n.GetAll("open")) m.Open.Add(TextCodec.Decode(x));
            foreach (var x in n.GetAll("action")) { string t = TextCodec.Decode(x); if (t.Length > 0) { m.Done.Add(t[0] == '1'); m.Actions.Add(t.Substring(1)); } }
            foreach (var tn in n.GetNodes("TURN")) m.Turns.Add(new MeetingTurn { Who = tn.Get("who", ""), Kind = tn.Get("kind", "talk"), Text = tn.GetText("text"), Note = tn.GetText("note") });
            // A meeting cut off by leaving the game cannot pick its call back up.
            if (m.Status == "running") m.Status = "stopped";
            return m;
        }
    }

    public partial class NewsEngine
    {
        // ------------------------------------------------------------------ what the KSP layer adds for part 4

        /// <summary>Parts still locked in the tech tree, with the node that unlocks each. Empty in sandbox.</summary>
        public Func<List<CraftPart>> EngineeringLocked = () => null;
        public Func<List<EngRelay>> EngineeringRelays = () => new List<EngRelay>();
        /// <summary>Science points in hand, or -1 when the game has none (sandbox).</summary>
        public Func<double> EngineeringScience = () => -1;
        /// <summary>Saved craft files: {name, where ("VAB" or "SPH"), path}.</summary>
        public Func<List<string[]>> EngineeringSaved = () => new List<string[]>();
        public Func<string, EngSnapshot> EngineeringLoadSaved = path => null;

        /// <summary>A saved craft the director opened for review: the team looks at it instead of the live craft until it is closed.</summary>
        public EngSnapshot EngPinned;

        /// <summary>What the team is looking at: the saved craft under review, else the live craft.</summary>
        public EngSnapshot EngLook()
        {
            if (EngPinned != null) return EngPinned;
            EngSnapshot s = null;
            try { s = EngineeringData(); } catch (Exception e) { LastError = "Engineering data: " + e.Message; }
            // With no readout from the game (some mod setups never give one), the sheet's own sums stand in.
            if (s != null && s.Sheet != null && s.Sheet.Parts.Count > 0 && s.Stages.Count == 0 && (s.Scene == "vab" || s.Scene == "sph"))
            {
                var est = SheetDv.Solve(s.Sheet);
                if (est.Count > 0) { s.Stages = est; s.TotalDvVac = est.Sum(x => x.DvVac); s.TotalDvAsl = est.Sum(x => x.DvAsl); s.TotalDvActual = s.TotalDvVac; s.DeltaVReady = true; s.DvEstimated = true; }
            }
            return s;
        }

        public bool EngOpenSaved(string path)
        {
            EngSnapshot s = null;
            try { s = EngineeringLoadSaved(path); } catch (Exception e) { LastError = "Saved craft: " + e.Message; }
            if (s == null || s.Sheet == null || s.Sheet.Parts.Count == 0) return false;
            if (s.Stages.Count == 0)
            {
                var est = SheetDv.Solve(s.Sheet);
                s.Stages = est; s.TotalDvVac = est.Sum(x => x.DvVac); s.TotalDvAsl = est.Sum(x => x.DvAsl); s.TotalDvActual = s.TotalDvVac; s.DeltaVReady = est.Count > 0; s.DvEstimated = true;
            }
            EngPinned = s;
            return true;
        }

        /// <summary>The mission-side reports. <paramref name="heavy"/> also reads the locked parts for tech advice.</summary>
        public List<CheckReport> EngMissionReports(EngSnapshot snap, EngPlan plan, List<CheckReport> checks, bool heavy)
        {
            var list = new List<CheckReport>();
            if (snap == null) return list;
            Func<string, string, Func<CheckReport>, CheckReport> run = (key, title, f) =>
            {
                string was = Crumb.Enter("mission tool " + key);
                try { var r = f(); r.Key = key; r.Title = title; return r; }
                catch (Exception e) { var r = new CheckReport { Key = key, Title = title, Rating = "not computed" }; r.Add("This could not be worked out (" + e.GetType().Name + ")."); return r; }
                finally { Crumb.Exit(was); }
            };
            bool has = snap.Sheet != null && snap.Sheet.Parts.Count > 0;
            bool flying = snap.Scene == "flight" || snap.Scene == "map";
            var power = has ? EngPower(snap, plan) : null;
            EngMissions.Dest dest = plan != null && plan.Known ? EngMissions.Find(plan.Body, snap) : null;
            EngBody there = plan != null && plan.Known ? snap.BodyNamed(plan.Body) : null;
            int crew = !has ? 0 : flying ? snap.Crew : snap.Sheet.Parts.Sum(p => p.Crew);
            list.Add(run("needs", "What it needs", () => EngMissionTools.Requirements(snap, plan, dest, power, checks, crew)));
            list.Add(run("timeline", "Timeline", () => EngMissionTools.Timeline(snap, plan, dest, power, checks)));
            List<EngRelay> relays = null;
            try { relays = EngineeringRelays(); } catch { }
            list.Add(run("relays", "Relays", () => EngMissionTools.Relays(snap, plan, relays)));
            list.Add(run("predicted", "Predicted vs actual", () => EngMissionTools.Predicted(State.EngPredictions, snap.Vessel)));
            if (!has) return list;
            double trip = dest != null ? dest.Travel * (plan.Return ? 2 : 1) + (plan.Return && !plan.Flyby ? EngMissionTools.WaitForReturn(snap.Bodies, snap.Home, there) : 0) : 0;
            list.Add(run("reliability", "Reliability", () => EngMissionTools.Reliability(snap.Sheet, snap.Stages, plan, trip)));
            list.Add(run("setup", "Setup", () => EngMissionTools.Setup(snap.Sheet, crew)));
            list.Add(run("mining", "Mining", () => EngMissionTools.Mining(snap.Sheet, there, power)));
            if (!flying) list.Add(run("ascent", "Ascent", () => EngMissionTools.Ascent(snap, checks)));
            list.Add(run("aerobrake", "Aerobraking", () => EngMissionTools.Aerobrake(snap, plan, dest)));
            if (EngMissionTools.IsPlane(snap.Sheet, snap.Scene == "sph")) list.Add(run("plane", "Plane", () => EngMissionTools.Plane(snap.Sheet, snap.Home)));
            if (EngMissionTools.IsRover(snap.Sheet)) list.Add(run("rover", "Rover", () => EngMissionTools.Rover(snap.Sheet, there, power)));
            list.Add(run("docking", "Docking", () => EngMissionTools.Docking(snap.Sheet, snap.Mass)));
            if (!flying) list.Add(run("launcher", "Launcher", () => EngMissionTools.Launcher(snap, State.EngProven)));
            if (heavy)
            {
                List<CraftPart> locked = null; double science = -1;
                try { locked = EngineeringLocked(); } catch (Exception e) { LastError = "Tech tree: " + e.Message; }
                try { science = EngineeringScience(); } catch { }
                bool air = snap.Home != null && snap.Home.AtmoDepth > 0 && !flying;
                list.Add(run("tech", "Tech advice", () => EngMissionTools.Tech(snap.Sheet, locked, snap.Stages, air, science)));
            }
            return list;
        }

        // ------------------------------------------------------------------ predicted against actual, proven rockets

        /// <summary>Called with each flight sample: notes what the team expected at launch, then fills in what happened.</summary>
        void EngTrackFlight(EngSnapshot s, double ut)
        {
            if (s == null || s.VesselId == "" || s.Home == null) return;
            var p = State.EngPredictions.LastOrDefault(x => x.VesselId == s.VesselId);
            bool pad = s.Situation == "on the launch pad" || s.Situation.StartsWith("landed on " + s.Home.Name) && s.Speed < 1 && s.Altitude < 2000;
            if (p == null)
            {
                if (!pad || s.Stages.Count == 0) return;
                p = new EngPrediction { Id = State.NewId("pr"), VesselId = s.VesselId, Craft = s.Vessel, UT = ut, LaunchDv = s.TotalDvVac };
                p.Set("Delta-v to orbit", "m/s", DvPlanner.Ascent(s.Home));
                var order = s.Stages.Where(x => x.DvVac > 1 && x.EndMass > 0).ToList();
                if (order.Count > 0) p.Set("Peak g on the way up", "g", order.Take(Math.Max(1, order.Count)).Max(x => x.ThrustVac / (x.EndMass * 9.80665)));
                // The full reading (with the parts list) is only taken once, here, for the landing figure and the cost.
                EngSnapshot full = null;
                try { full = EngineeringData(); } catch { }
                if (full != null && full.Sheet != null && full.Sheet.Parts.Count > 0)
                {
                    var stay = full.Sheet.Parts.Where(x => x.DropStage < 0).ToList();
                    double area = stay.Sum(x => x.ChuteArea);
                    if (area > 0) p.Set("Touchdown speed", "m/s", CraftChecks.ChuteSpeed(stay.Sum(x => x.DryMass), area, s.Home));
                    p.PeakG = 0;
                    engLaunch[s.VesselId] = new[] { s.Mass, full.Sheet.Parts.Sum(x => x.Cost) };
                }
                else engLaunch[s.VesselId] = new[] { s.Mass, 0.0 };
                State.EngPredictions.Add(p);
                if (State.EngPredictions.Count > 30) State.EngPredictions.RemoveAt(0);
                return;
            }
            if (p.Landed) return;
            if (!p.Orbited) p.PeakG = Math.Max(p.PeakG, s.GForce);
            bool orbit = s.HasOrbit && s.Body == s.Home.Name && s.PeA > Math.Max(s.Home.AtmoDepth, 1000) && s.Situation.StartsWith("orbiting");
            if (orbit && !p.Orbited)
            {
                p.Orbited = true;
                if (p.LaunchDv > 0 && s.TotalDvVac > 0) p.Actual("Delta-v to orbit", Math.Max(0, p.LaunchDv - s.TotalDvVac));
                if (p.PeakG > 0) p.Actual("Peak g on the way up", p.PeakG);
                double[] at;
                engLaunch.TryGetValue(s.VesselId, out at);
                var known = State.EngProven.FirstOrDefault(x => x.Name == s.Vessel);
                if (known != null) { known.Flights++; known.UT = ut; known.OrbitMass = s.Mass; }
                else State.EngProven.Add(new ProvenRocket { Name = s.Vessel, LaunchMass = at != null ? at[0] : 0, OrbitMass = s.Mass, Cost = at != null ? at[1] : 0, UT = ut });
                if (State.EngProven.Count > 40) State.EngProven.RemoveAt(0);
                var it = p.Items.FirstOrDefault(x => x[0] == "Delta-v to orbit");
                if (it != null && it[3] != "") EngRemember("flight", s.Vessel, s.Vessel + " reached orbit using " + EngMath.N(EngPrediction.Num(it[3])) + " m/s (planned " + EngMath.N(EngPrediction.Num(it[2])) + "), " + EngMath.N(s.Mass, "0.0") + " t in orbit.", ut);
            }
            bool down = (s.Situation.StartsWith("landed on") || s.Situation.StartsWith("splashed down")) && !pad;
            if (down && (p.Orbited || ut - p.UT > 120))
            {
                p.Landed = true;
                if (Math.Abs(p.LastVertical) > 0.2 && s.Body == s.Home.Name) p.Actual("Touchdown speed", Math.Abs(p.LastVertical));
                var it = p.Items.FirstOrDefault(x => x[0] == "Touchdown speed");
                if (it != null && it[3] != "") EngRemember("flight", s.Vessel, s.Vessel + " touched down at " + EngMath.N(EngPrediction.Num(it[3]), "0.0") + " m/s (predicted " + EngMath.N(EngPrediction.Num(it[2]), "0.0") + ").", ut);
            }
            else if (s.VerticalSpeed < -0.2) p.LastVertical = s.VerticalSpeed;
        }
        readonly Dictionary<string, double[]> engLaunch = new Dictionary<string, double[]>();

        // ------------------------------------------------------------------ team memory

        public const int EngNotesKeep = 3000;
        static readonly string[] PreferenceWords = { "i prefer", "i like ", "i always", "i never", "from now on", "always ", "never ", "i want you to", "remember that", "remember:", "keep in mind", "i hate", "i don't like", "i dont like", "my rule", "don't suggest", "dont suggest", "stop suggesting" };
        static readonly string[] RecallWords = { "remember", "last time", "did we", "have we", "what did", "earlier", "before", "previous", "on file", "history", "again" };

        /// <summary>Files something with the team. The same text is not kept twice.</summary>
        public EngNote EngRemember(string kind, string craft, string text, double ut, bool pinned = false)
        {
            text = ResponseParser.Clip((text ?? "").Trim(), 700);
            if (text == "") return null;
            var same = State.EngNotes.LastOrDefault(n => n.Text == text && n.Kind == kind);
            if (same != null) { same.UT = ut; return same; }
            var note = new EngNote { Id = State.NewId("n"), Kind = kind, Craft = craft ?? "", Text = text, UT = ut, Pinned = pinned };
            State.EngNotes.Add(note);
            if (State.EngNotes.Count > EngNotesKeep)
            {
                int drop = State.EngNotes.FindIndex(n => !n.Pinned && n.Kind != "preference");
                if (drop >= 0) State.EngNotes.RemoveAt(drop);
            }
            return note;
        }

        static IEnumerable<string> NoteWords(string text)
        {
            return (text ?? "").ToLowerInvariant().Split(new[] { ' ', ',', '.', '?', '!', ':', ';', '(', ')', '"', '\n', '/', '-' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 3 && !StopWords.Contains(w));
        }
        static readonly HashSet<string> StopWords = new HashSet<string> { "this", "that", "with", "have", "what", "will", "does", "from", "they", "them", "then", "than", "when", "where", "which", "about", "into", "your", "should", "would", "could", "there", "their", "much", "many", "need", "enough", "craft", "stage" };

        /// <summary>What the team has on file that bears on this question: pinned notes, the director's preferences, this craft's notebook, and matches by subject.</summary>
        public string EngMemoryText(EngSnapshot snap, string question, int maxChars = 2600)
        {
            if (State.EngNotes.Count == 0) return "";
            string craft = snap != null ? snap.Vessel : "";
            string q = (question ?? "").ToLowerInvariant();
            bool recall = RecallWords.Any(w => q.Contains(w));
            var picked = new List<EngNote>();
            Action<IEnumerable<EngNote>> take = list => { foreach (var n in list) if (!picked.Contains(n)) picked.Add(n); };
            take(State.EngNotes.Where(n => n.Pinned).Reverse().Take(10));
            take(State.EngNotes.Where(n => n.Kind == "preference").Reverse().Take(recall ? 20 : 10));
            if (craft != "") take(State.EngNotes.Where(n => n.Craft == craft && n.Kind != "preference").Reverse().Take(recall ? 14 : 6));
            var qw = new HashSet<string>(NoteWords(question));
            if (qw.Count > 0)
                take(State.EngNotes.Where(n => !picked.Contains(n)).Select(n => new { n, score = NoteWords(n.Text + " " + n.Craft).Distinct().Count(w => qw.Contains(w)) })
                    .Where(x => x.score > 0).OrderByDescending(x => x.score).ThenByDescending(x => x.n.UT).Take(recall ? 12 : 5).Select(x => x.n));
            if (recall) take(State.EngNotes.AsEnumerable().Reverse().Take(8));
            if (picked.Count == 0) return "";
            var sb = new StringBuilder("TEAM MEMORY (on file from earlier; " + State.EngNotes.Count + " notes kept in all, these are the ones that bear on this):\n");
            foreach (var n in picked.OrderBy(n => n.Kind == "preference" ? 0 : n.Pinned ? 1 : 2).ThenBy(n => n.UT))
            {
                string line = "- [" + Clock.FormatDate(n.UT) + ", " + KindLabel(n.Kind) + (n.Craft != "" ? ", " + n.Craft : "") + (n.Pinned ? ", pinned" : "") + "] " + n.Text;
                if (sb.Length + line.Length > maxChars) break;
                sb.AppendLine(line);
            }
            return sb.ToString().TrimEnd();
        }

        public static string KindLabel(string kind)
        {
            switch (kind)
            {
                case "preference": return "director's preference"; case "qa": return "asked and answered"; case "review": return "design review"; case "meeting": return "meeting";
                case "plan": return "mission plan"; case "snapshot": return "snapshot"; case "flight": return "flight result"; case "investigation": return "investigation"; case "note": return "note";
                default: return kind;
            }
        }

        /// <summary>Called for each thing the director types to the team: a stated preference is filed as one.</summary>
        void EngLearnPreference(string text, EngSnapshot snap, double ut)
        {
            string t = " " + (text ?? "").ToLowerInvariant() + " ";
            if (t.Length < 12 || t.TrimEnd().EndsWith("?")) return;
            if (PreferenceWords.Any(w => t.Contains(w))) EngRemember("preference", "", text, ut);
        }

        /// <summary>Files the answer the team just gave with the question that prompted it.</summary>
        void EngFileAnswer(EngSnapshot snap, double ut)
        {
            int at = State.EngChat.FindLastIndex(m => m.Role == "player");
            if (at < 0) return;
            string q = State.EngChat[at].Text;
            var answers = State.EngChat.Skip(at + 1).Where(m => m.Role != "player").ToList();
            if (answers.Count == 0) return;
            string a = string.Join(" ", answers.Select(m => EngShort(m.Role) + ": " + m.Text).ToArray());
            bool review = q.StartsWith("Full design review", StringComparison.OrdinalIgnoreCase) || q.ToLowerInvariant().Contains("review");
            EngRemember(review ? "review" : "qa", snap != null ? snap.Vessel : "", "Q: " + ResponseParser.Clip(q, 160) + " A: " + ResponseParser.Clip(a, 480), ut);
        }

        // ------------------------------------------------------------------ the team

        public static readonly string[] EngSpecialists = { "gene", "linus", "wernher", "maxwell", "irma", "mortimer" };
        public static string EngShort(string key)
        {
            switch (key) { case "linus": return "Linus"; case "wernher": return "Wernher"; case "gene": return "Gene"; case "maxwell": return "Maxwell"; case "irma": return "Irma"; case "mortimer": return "Mortimer"; case "director": return "Director"; case "tools": return "Tested live"; default: return "Engineering"; }
        }
        static string EngFocus(string key)
        {
            switch (key)
            {
                case "gene": return "mission planning: the whole-mission budget and margins, the order of events, go or no-go, contracts, what to do next. You chair the meeting.";
                case "linus": return "flight dynamics: orbits, transfers and their windows, ascent, landing, aerobraking, rendezvous and docking approach.";
                case "wernher": return "propulsion and structures: engines, staging, thrust to weight, Isp, fuel, mass, balance, launch limits, engine reliability, better engines.";
                case "maxwell": return "power and thermal: generation, batteries, what draws power where, reactors, radiators and heat loops, mining power.";
                case "irma": return "systems and crew: comms and relays, life support and supplies, habitat and radiation, science gear, action groups and setup, docking hardware.";
                case "mortimer": return "cost: the price of the craft and of each stage, what is oversized or could be cheaper, cost per tonne to orbit, reuse and recovery.";
                default: return "engineering.";
            }
        }
        static readonly Dictionary<string, string[]> EngDesk = new Dictionary<string, string[]>
        {
            { "gene", new[] { "gonogo", "timeline", "needs", "contracts", "windows", "predicted" } },
            { "linus", new[] { "windows", "ascent", "aerobrake", "landing", "docking", "flight", "plane", "rover" } },
            { "wernher", new[] { "launch", "balance", "reliability", "swaps", "launcher", "ascent", "tech" } },
            { "maxwell", new[] { "heat", "mining", "efficiency" } },
            { "irma", new[] { "comms", "relays", "crew", "science", "setup", "docking" } },
            { "mortimer", new[] { "cost", "efficiency", "swaps", "launcher", "compare" } },
        };

        /// <summary>How much explaining the director wants: "short", "normal" or "teach".</summary>
        string EngLevelText()
        {
            switch (State.EngLevel)
            {
                case "short": return "The director wants short answers: the verdict and the number, one or two sentences, no background unless asked.";
                case "teach": return "The director wants to learn: explain why, define terms the first time, and say how they could work it out themselves next time. A little longer is fine.";
                default: return "";
            }
        }

        // ------------------------------------------------------------------ team meeting

        public EngMeeting EngMeetingNow { get { return State.EngMeetings.LastOrDefault(m => m.Status == "running" || m.Status == "waiting"); } }
        int meetRun;

        public static void MeetingPlan(int budget, out int agenda, out int prep, out int talk, out int vote)
        {
            budget = Math.Max(1, Math.Min(100, budget));
            agenda = budget >= 4 ? 1 : 0;
            prep = budget >= 14 ? 5 : budget >= 6 ? 1 : 0;
            vote = budget >= 30 ? 6 : 0;
            talk = Math.Max(0, budget - 1 - agenda - prep - vote);
        }

        /// <summary>The director calls a meeting on an objective, for a set number of AI calls (1 to 100).</summary>
        public EngMeeting EngMeetingStart(string objective, int budget, double ut, string followsId = "")
        {
            objective = ResponseParser.Clip((objective ?? "").Trim(), 400);
            if (objective == "" || !Settings.EngineeringOn || !Settings.CanCallAi || EngMeetingNow != null) return null;
            var snap = EngLook();
            var m = new EngMeeting { Id = State.NewId("mt"), Objective = objective, Budget = Math.Max(1, Math.Min(100, budget)), UT = ut, Craft = snap != null ? snap.Vessel : "", FollowsId = followsId ?? "" };
            State.EngMeetings.Add(m);
            if (State.EngMeetings.Count > 60) State.EngMeetings.RemoveAt(0);
            EngLearnPreference(objective, snap, ut);
            EngBusy = true;
            MeetingStep(m, ++meetRun, ut, 0);
            return m;
        }

        /// <summary>The director says something in the meeting. It costs no AI call; the next speaker answers it.</summary>
        public void EngMeetingSay(string text, double ut)
        {
            var m = EngMeetingNow;
            text = ResponseParser.Clip((text ?? "").Trim(), 600);
            if (m == null || text == "") return;
            m.Turns.Add(new MeetingTurn { Who = "director", Kind = "director", Text = text, Note = "Director: " + ResponseParser.Clip(text, 160) });
            EngLearnPreference(text, null, ut);
            RaiseCommunity();
        }

        /// <summary>Wraps the meeting up now: the chair writes the minutes with the next call.</summary>
        public void EngMeetingStop(double ut)
        {
            var m = EngMeetingNow;
            if (m == null) return;
            m.Closing = true;
            if (m.Status == "waiting") { m.Status = "running"; EngBusy = true; MeetingStep(m, ++meetRun, ut, 0); }
            RaiseCommunity();
        }

        /// <summary>The director breaks a tie the team could not.</summary>
        public void EngMeetingDecide(string option, double ut)
        {
            var m = EngMeetingNow;
            if (m == null || m.Status != "waiting") return;
            string chosen = option == "b" ? m.OptionB : m.OptionA;
            m.Turns.Add(new MeetingTurn { Who = "director", Kind = "director", Text = "My decision: " + chosen, Note = "Director decided: " + ResponseParser.Clip(chosen, 160) });
            m.Question = ""; m.OptionA = ""; m.OptionB = "";
            m.Status = "running";
            EngBusy = true;
            MeetingStep(m, ++meetRun, ut, 0);
        }

        public void EngMeetingTick(string id, int index)
        {
            var m = State.EngMeetings.FirstOrDefault(x => x.Id == id);
            if (m == null || index < 0 || index >= m.Actions.Count) return;
            while (m.Done.Count < m.Actions.Count) m.Done.Add(false);
            m.Done[index] = !m.Done[index];
        }

        /// <summary>What the next call is: agenda, prep (one specialist or all at once), talk, vote or minutes, and who makes it.</summary>
        public static string MeetingNext(EngMeeting m, out string who)
        {
            int agenda, prep, talk, vote;
            MeetingPlan(m.Budget, out agenda, out prep, out talk, out vote);
            int i = m.Used;
            who = "gene";
            if (m.Closing || i >= m.Budget - 1) return "minutes";
            if (i < agenda) return "agenda";
            i -= agenda;
            if (i < prep) { if (prep == 1) { who = "team"; return "prep-all"; } who = EngSpecialists[1 + i]; return "prep"; }
            i -= prep;
            if (i < talk)
            {
                var spoken = m.Turns.Where(t => t.Kind == "talk").ToList();
                string last = spoken.Count > 0 ? spoken.Last().Who : "";
                if (m.Next != "" && m.Next != last && EngSpecialists.Contains(m.Next)) who = m.Next;
                else who = EngSpecialists.Where(k => k != last).OrderBy(k => spoken.Count(t => t.Who == k)).ThenBy(k => Array.IndexOf(EngSpecialists, k)).First();
                return "talk";
            }
            i -= talk;
            if (i < vote) { who = EngSpecialists[i]; return "vote"; }
            return "minutes";
        }

        string MeetingData(EngSnapshot snap, string who, List<CheckReport> all, EngPlan plan)
        {
            var sb = new StringBuilder();
            sb.AppendLine("LIVE DATA");
            sb.AppendLine(EngDataText(snap));
            if (plan != null) sb.AppendLine(EngMissions.PlanText(plan));
            if (snap != null && snap.Sheet != null && snap.Sheet.Parts.Count > 0)
            {
                sb.AppendLine(snap.Sheet.Text(snap.Stages, "", who == "wernher" || who == "team" ? 5000 : 2500));
                if (who == "maxwell" || who == "team") { var power = EngPower(snap, plan); if (power != null) sb.AppendLine(CraftSheet.PowerText(power)); }
                if (who == "wernher" || who == "gene" || who == "team") { var staging = snap.Sheet.StagingIssues(snap.Stages); if (staging.Count > 0) sb.AppendLine("STAGING CHECK (computed):\n- " + string.Join("\n- ", staging.ToArray())); }
            }
            // The unlocked parts for this desk and for what the meeting is about, so proposals name real parts.
            var now = EngMeetingNow;
            string deskParts = who == "maxwell" ? " power radiator batter" : who == "wernher" ? " engine tank" : who == "linus" ? " antenna parachute" : who == "irma" ? " crew probe science" : "";
            string partsText = EngPartsText((now != null ? now.Objective : "") + deskParts, 45);
            if (partsText != "") sb.AppendLine(partsText + "\nPropose only parts on this list, by their exact name.");
            string[] desk;
            if (!EngDesk.TryGetValue(who, out desk)) desk = new string[0];
            var mine = all.Where(r => who == "team" ? r.Bad : desk.Contains(r.Key)).ToList();
            if (mine.Count > 0) sb.AppendLine("YOUR DESK'S COMPUTED REPORTS (quote these numbers, do not redo them):\n" + string.Join("\n", mine.Select(r => r.Text()).ToArray()));
            var rest = all.Where(r => !mine.Contains(r) && r.Rating != "").Select(r => r.Title + ": " + r.Rating).ToArray();
            if (rest.Length > 0) sb.AppendLine("Other desks, in brief: " + string.Join("; ", rest) + ".");
            if (snap != null && snap.Sheet != null && (who == "maxwell" || who == "wernher" || who == "irma")) { string books = ModKnowledge.BooksText(snap.Sheet, 1800); if (books != "") sb.AppendLine(books); }
            return sb.ToString();
        }

        void MeetingStep(EngMeeting m, int run, double ut, int failures)
        {
            string who;
            string kind = MeetingNext(m, out who);
            var snap = EngLook();
            var plan = EngCurrentPlan(snap);
            var checks = EngChecks(snap, plan);
            var all = checks.Concat(EngToolReports(snap, plan, checks, who == "mortimer" || who == "wernher")).Concat(EngMissionReports(snap, plan, checks, who == "wernher")).ToList();

            var sys = new StringBuilder();
            sys.AppendLine((who == "team" ? "You are the engineering team" : "You are " + EngShort(who) + ", an engineer") + " at " + AgencyName + ", a space agency in Kerbal Space Program (KSP1)." + (who == "team" ? "" : " Your desk is " + EngFocus(who)) + " The director (the player) has called a team meeting. The team: " + string.Join("; ", EngSpecialists.Select(k => k + " = " + EngShort(k) + ", " + EngFocus(k).Split(':')[0]).ToArray()) + ".");
            sys.AppendLine("Speak only for your own specialty and leave the rest to the others. Be a working engineer: concrete, with the numbers from your data. Do not act out a personality. Do not repeat what is already in the points so far: add to it, or challenge it by name when your numbers disagree. Never invent craft data. Never advise about a mod feature the handbooks say is off.");
            string level = EngLevelText();
            if (level != "") sys.AppendLine(level);
            var u = new StringBuilder();
            u.AppendLine("Today: " + Clock.FormatDate(ut) + ".");
            u.AppendLine("MEETING OBJECTIVE (from the director): " + m.Objective);
            var prev = State.EngMeetings.FirstOrDefault(x => x.Id == m.FollowsId);
            if (prev != null) u.AppendLine("THIS FOLLOWS AN EARLIER MEETING on \"" + prev.Objective + "\". Its minutes: " + prev.Minutes + (prev.Actions.Count > 0 ? " Its action list: " + string.Join("; ", prev.Actions.Select((a, i) => a + (i < prev.Done.Count && prev.Done[i] ? " (done)" : " (not done)")).ToArray()) : ""));
            if (m.Agenda.Count > 0) u.AppendLine("AGENDA: " + string.Join("; ", m.Agenda.ToArray()));
            u.AppendLine(MeetingData(snap, who, all, plan));
            string mem = EngMemoryText(snap, m.Objective, 1500);
            if (mem != "") u.AppendLine(mem);
            // Older turns are carried as their one-line points; the last few are given word for word.
            var notes = m.Turns.Where(t => t.Note != "").Select(t => "- " + (t.Kind == "test" ? "" : EngShort(t.Who) + ": ") + t.Note).ToList();
            if (notes.Count > 0) u.AppendLine("POINTS SO FAR:\n" + string.Join("\n", notes.ToArray()));
            var recent = m.Turns.Skip(Math.Max(0, m.Turns.Count - 5)).ToList();
            if (recent.Count > 0) u.AppendLine("THE LAST TURNS, word for word:\n" + string.Join("\n", recent.Select(t => EngShort(t.Who) + ": " + t.Text).ToArray()));
            int tokens = 700;
            switch (kind)
            {
                case "agenda":
                    u.AppendLine("YOUR TURN: open the meeting. Set an agenda of two to five points that will settle the objective, in the order to take them.");
                    sys.AppendLine("Reply with one JSON object only: {\"text\": \"one or two sentences opening the meeting\", \"agenda\": [\"point\", \"point\"]}");
                    break;
                case "prep":
                    u.AppendLine("YOUR TURN: give your desk's opening position on the objective: where you stand, the one or two numbers that drive it, and what you would change.");
                    sys.AppendLine("Reply with one JSON object only: {\"text\": \"two to four sentences\", \"note\": \"your position in one line\"}");
                    break;
                case "prep-all":
                    u.AppendLine("YOUR TURN: write each specialist's opening position on the objective, each from their own desk only, each with the number that drives it.");
                    sys.AppendLine("Reply with one JSON object only: {\"positions\": [{\"who\": \"linus|wernher|maxwell|irma|mortimer\", \"text\": \"one to three sentences\", \"note\": \"the position in one line\"}]}");
                    tokens = 1400;
                    break;
                case "talk":
                    u.AppendLine("YOUR TURN to speak. Answer the last speaker or the director if they raised something for your desk; otherwise take the next open agenda point from your desk's side. Two to five sentences.");
                    u.AppendLine("If you want an idea checked, add \"test\": the mod will change the craft that way on paper and report the computed result to the meeting (no change is made to the real craft).");
                    sys.AppendLine("Reply with one JSON object only: {\"text\": \"what you say\", \"note\": \"your point in one line\", \"next\": \"key of who should answer, or empty\", \"test\": {\"part\": \"exact part name from the craft sheet or a part you know is unlocked\", \"count\": 2}" + (who == "gene" ? ", \"deadlock\": {\"question\": \"...\", \"a\": \"...\", \"b\": \"...\"}" : "") + "}. Leave \"test\" out unless a concrete add or remove (negative count) would settle something." + (who == "gene" ? " Use \"deadlock\" only when the team is split between two courses and more talk will not settle it: the director then chooses." : ""));
                    break;
                case "vote":
                    u.AppendLine("YOUR TURN: the vote. From your desk only, say whether the course the meeting has arrived at should go ahead: yes, no, or yes with a change, and the one reason.");
                    sys.AppendLine("Reply with one JSON object only: {\"vote\": \"yes|no|changes\", \"text\": \"one sentence\"}");
                    tokens = 300;
                    break;
                default:
                    u.AppendLine("YOUR TURN: close the meeting and write the minutes. Say what was decided and why, in plain words with the numbers that mattered; list the decisions; list what the director should now do, each as one concrete step in the builder or in flight; list anything left open." + (m.Turns.Count == 0 ? " No one has spoken yet (a very short meeting): weigh every desk's reports yourself and give the team's considered answer." : ""));
                    sys.AppendLine("Reply with one JSON object only: {\"minutes\": \"one or two short paragraphs\", \"decisions\": [\"...\"], \"actions\": [\"...\"], \"open\": [\"...\"]}");
                    tokens = 1500;
                    break;
            }
            AskJson("Engineering team", Settings.Writer, sys.ToString(), u.ToString(), tokens, 0, (root, err, text, res) =>
            {
                if (run != meetRun) return;
                if (root == null)
                {
                    LastError = err ?? "No reply";
                    if (failures < 1 && !IsPermanent(res)) { MeetingStep(m, run, ut, failures + 1); return; }
                    m.Status = "stopped"; EngBusy = false;
                    m.Turns.Add(new MeetingTurn { Who = "tools", Kind = "test", Text = "(The connection to the team was lost, so the meeting stopped here. Start a follow-up to carry on.)" });
                    RaiseCommunity();
                    return;
                }
                m.Used++;
                m.Next = "";
                Func<string, string> get = k => ResponseParser.Clip(Json.Str(root, k, "").Trim(), 1600);
                Func<string, List<string>> arr = k =>
                {
                    var o = Json.Obj(root); var a = o != null && o.ContainsKey(k) ? Json.Arr(o[k]) : null;
                    return a == null ? new List<string>() : a.Select(x => x == null ? "" : ResponseParser.Clip(x.ToString().Trim(), 300)).Where(x => x != "").Take(8).ToList();
                };
                switch (kind)
                {
                    case "agenda":
                        m.Agenda = arr("agenda").Take(5).ToList();
                        if (get("text") != "") m.Turns.Add(new MeetingTurn { Who = "gene", Kind = "agenda", Text = get("text") });
                        break;
                    case "prep":
                        m.Turns.Add(new MeetingTurn { Who = who, Kind = "position", Text = get("text"), Note = ResponseParser.Clip(get("note"), 200) });
                        break;
                    case "prep-all":
                    {
                        var o = Json.Obj(root); var list = o != null && o.ContainsKey("positions") ? Json.Arr(o["positions"]) : null;
                        if (list != null)
                            foreach (var p in list.Take(6))
                            {
                                string k = Json.Str(p, "who", "").Trim().ToLowerInvariant();
                                if (!EngSpecialists.Contains(k)) continue;
                                m.Turns.Add(new MeetingTurn { Who = k, Kind = "position", Text = ResponseParser.Clip(Json.Str(p, "text", "").Trim(), 800), Note = ResponseParser.Clip(Json.Str(p, "note", "").Trim(), 200) });
                            }
                        break;
                    }
                    case "talk":
                    {
                        string said = get("text");
                        m.Turns.Add(new MeetingTurn { Who = who, Kind = "talk", Text = said == "" ? "..." : said, Note = ResponseParser.Clip(get("note"), 200) });
                        string next = get("next").ToLowerInvariant();
                        if (EngSpecialists.Contains(next)) m.Next = next;
                        var ro = Json.Obj(root);
                        if (ro != null && ro.ContainsKey("test") && Json.Obj(ro["test"]) != null)
                        {
                            string part = Json.Str(ro["test"], "part", "").Trim();
                            int count; int.TryParse(Json.Str(ro["test"], "count", "1").Split('.')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out count);
                            string result = EngTryChange(snap, plan, part, count == 0 ? 1 : count);
                            if (result != "") m.Turns.Add(new MeetingTurn { Who = "tools", Kind = "test", Text = result, Note = result });
                        }
                        if (who == "gene" && ro != null && ro.ContainsKey("deadlock") && Json.Obj(ro["deadlock"]) != null && m.Used < m.Budget - 1)
                        {
                            string a = Json.Str(ro["deadlock"], "a", "").Trim(), b = Json.Str(ro["deadlock"], "b", "").Trim();
                            if (a != "" && b != "") { m.Question = ResponseParser.Clip(Json.Str(ro["deadlock"], "question", "The team is split.").Trim(), 300); m.OptionA = ResponseParser.Clip(a, 200); m.OptionB = ResponseParser.Clip(b, 200); m.Status = "waiting"; }
                        }
                        break;
                    }
                    case "vote":
                    {
                        string v = get("vote").ToLowerInvariant();
                        v = v.StartsWith("y") ? "yes" : v.StartsWith("n") ? "no" : "changes";
                        m.Turns.Add(new MeetingTurn { Who = who, Kind = "vote", Text = (v == "yes" ? "Yes. " : v == "no" ? "No. " : "Yes, with a change. ") + get("text"), Note = "votes " + v });
                        string w2;
                        if (MeetingNext(m, out w2) == "minutes")
                        {
                            int yes = m.Turns.Count(t => t.Kind == "vote" && t.Note == "votes yes"), no = m.Turns.Count(t => t.Kind == "vote" && t.Note == "votes no");
                            if (yes == no && yes > 0 && !m.Closing) { m.Question = "The vote is tied " + yes + " to " + no + "."; m.OptionA = "Go ahead as discussed"; m.OptionB = "Do not go ahead: rework it first"; m.Status = "waiting"; }
                        }
                        break;
                    }
                    default:
                        m.Minutes = get("minutes");
                        if (m.Minutes == "") m.Minutes = ResponseParser.Clip(text.Trim(), 1600);
                        m.Decisions = arr("decisions"); m.Actions = arr("actions"); m.Open = arr("open");
                        m.Done = m.Actions.Select(x => false).ToList();
                        m.Status = "done";
                        EngRemember("meeting", m.Craft, "Meeting on \"" + ResponseParser.Clip(m.Objective, 120) + "\": " + ResponseParser.Clip(m.Minutes, 380) + (m.Decisions.Count > 0 ? " Decided: " + ResponseParser.Clip(string.Join("; ", m.Decisions.ToArray()), 180) : ""), ut);
                        break;
                }
                RaiseCommunity();
                if (m.Status == "running") MeetingStep(m, run, ut, 0);
                else EngBusy = false;
            });
        }

        /// <summary>Tries a change on paper for the meeting: adds or removes copies of a part and reports what the sums say.</summary>
        public string EngTryChange(EngSnapshot snap, EngPlan plan, string partTitle, int count)
        {
            if (snap == null || snap.Sheet == null || snap.Sheet.Parts.Count == 0 || string.IsNullOrEmpty(partTitle)) return "";
            var sheet = snap.Sheet;
            Func<CraftPart, bool> match = p => string.Equals(p.Title, partTitle, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, partTitle, StringComparison.OrdinalIgnoreCase);
            var like = sheet.Parts.FirstOrDefault(match) ?? sheet.Parts.FirstOrDefault(p => p.Title.IndexOf(partTitle, StringComparison.OrdinalIgnoreCase) >= 0);
            bool fromCatalog = false;
            if (like == null && count > 0)
            {
                List<CraftPart> catalog = null;
                try { catalog = EngineeringCatalog(); } catch { }
                if (catalog != null) like = catalog.FirstOrDefault(match) ?? catalog.FirstOrDefault(p => p.Title.IndexOf(partTitle, StringComparison.OrdinalIgnoreCase) >= 0);
                fromCatalog = like != null;
            }
            if (like == null) return "Tested live: no part called \"" + ResponseParser.Clip(partTitle, 60) + "\" is on the craft or unlocked, so that could not be tried.";
            count = Math.Max(-50, Math.Min(50, count));
            CraftSheet trial;
            if (fromCatalog)
            {
                trial = EngTools.With(sheet, "", 0);
                for (int i = 0; i < count; i++) trial.Parts.Add(like);
            }
            else trial = EngTools.With(sheet, like.Name, count);
            int had = sheet.Parts.Count(p => p.Name == like.Name);
            if (count < 0 && had == 0) return "";
            int real = trial.Parts.Count - sheet.Parts.Count;
            if (real == 0) return "";
            var after = snap.WithSheet(trial);
            var dvBefore = SheetDv.Solve(sheet); var dvAfter = SheetDv.Solve(trial);
            after.Stages = dvAfter; after.TotalDvVac = dvAfter.Sum(x => x.DvVac);
            var before = snap.WithSheet(sheet); before.Stages = dvBefore; before.TotalDvVac = dvBefore.Sum(x => x.DvVac);
            var sb = new StringBuilder("Tested live: " + (real > 0 ? "adding " : "removing ") + (Math.Abs(real) > 1 ? Math.Abs(real) + " × " : "") + like.Title + ": mass " + (real > 0 ? "+" : "−") + EngMath.N(Math.Abs(real) * like.Mass, "0.0##") + " t, cost " + (real > 0 ? "+" : "−") + EngMath.N(Math.Abs(real) * like.Cost) + " funds");
            double d = dvAfter.Sum(x => x.DvVac) - dvBefore.Sum(x => x.DvVac);
            if (dvBefore.Count > 0) sb.Append(", delta-v " + (d >= 0 ? "+" : "−") + EngMath.N(Math.Abs(d)) + " m/s (estimate)");
            var p0 = EngPower(before, plan); var p1 = EngPower(after, plan);
            if (p0 != null && p1 != null)
            {
                double n0 = p0.GenThere - p0.Always, n1 = p1.GenThere - p1.Always;
                if (p0.Rating != p1.Rating || Math.Abs(n0 - n1) > 0.005) sb.Append(", power " + p0.Rating + " → " + p1.Rating + " (" + (n1 >= 0 ? "+" : "−") + CraftSheet.Ec(Math.Abs(n1)) + " spare while coasting)");
            }
            var c0 = EngChecks(before, plan); var c1 = EngChecks(after, plan);
            foreach (var x in c1)
            {
                var o = c0.FirstOrDefault(y => y.Key == x.Key);
                if (o != null && o.Rating != x.Rating && x.Key != "cost") sb.Append(", " + x.Title.ToLowerInvariant() + " " + o.Rating + " → " + x.Rating);
            }
            return sb.Append(".").ToString();
        }
    }
}
