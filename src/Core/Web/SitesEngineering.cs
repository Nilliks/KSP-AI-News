using System;
using System.Collections.Generic;
using System.Linq;

namespace KerbalNews.Web
{
    /// <summary>engineering.kb: the engineering team's chat beside the live craft data.</summary>
    public partial class Sites
    {
        public const string EngineeringHost = "engineering.kb";

        static string SceneLabel(string s)
        {
            switch (s) { case "flight": return "Flight"; case "map": return "Flight · Map view"; case "vab": return "Vehicle Assembly Building"; case "sph": return "Space Plane Hangar"; case "tracking": return "Tracking Station"; default: return "Space Center"; }
        }

        static string[] QuickAsks(EngSnapshot s)
        {
            string sc = s == null ? "spacecenter" : s.Scene;
            if (sc == "vab" || sc == "sph")
                return new[] { "Full design review", "Do I have enough power?", "What am I missing?", "Check my staging", "Is my TWR good for launch?", "Is anything oversized?" };
            if (sc == "flight" || sc == "map")
            {
                var l = new List<string> { "Check my delta-v budget" };
                l.Add(s.Target != "" ? "When is the transfer window to " + s.Target + "?" : "How do I get to the Mun from here?");
                l.Add(s.HasOrbit && s.PeA < Math.Max(s.BodyAtmosphere, 1) ? "How do I circularize?" : "How do I land safely?");
                if (s.Node != null) l.Insert(0, "Check my maneuver node");
                l.Add("Review my design");
                return l.ToArray();
            }
            return new[] { "What should we fly next?", "Explain a gravity turn", "How much delta-v for a Mun landing?", "How do transfer windows work?" };
        }

        /// <summary>Which report the left side shows. Not saved: it is where the director is looking, not game state.</summary>
        public string EngTab = "overview";
        static readonly string[][] EngTabs = { new[] { "overview", "Overview" }, new[] { "power", "Power" }, new[] { "stages", "Stages" }, new[] { "parts", "Parts" }, new[] { "problems", "All problems" } };

        static object Line(string text, string value = "", bool bad = false, string tag = "", bool fix = false)
        {
            return new Dictionary<string, object> { { "text", text }, { "value", value }, { "bad", bad }, { "mod", tag == "stock" ? "" : tag }, { "fix", fix } };
        }
        static object Line(SheetLine l) { return Line(l.Text, l.Value, l.Bad, l.Tag, l.Fix); }

        /// <summary>The mission picker: the mission as three choices, read back from the plain-English mission text.</summary>
        void MissionPicker(Dictionary<string, object> m, EngSnapshot s)
        {
            string t = " " + (State.EngMission ?? "").ToLowerInvariant() + " ";
            var body = EngMissions.FindBody(t, s);
            bool flyby = t.Contains("flyby") || t.Contains("fly by"), land = !flyby && (t.Contains("land") || t.Contains("surface") || t.Contains("base") || t.Contains("rover"));
            bool ret = t.Contains("return") || t.Contains("back") || t.Contains("home") || t.Contains("round trip");
            var dests = new List<object>();
            if (s != null && s.Home != null)
            {
                var home = s.Home;
                var order = s.Bodies.Where(b => !b.IsStar && b.Name != home.Name)
                    .OrderBy(b => b.Parent == home.Name ? 0 : 1)
                    .ThenBy(b => { var p = b.Parent == home.Parent ? b : s.BodyNamed(b.Parent); return p != null ? p.Sma : double.MaxValue; })
                    .ThenBy(b => b.Parent == home.Parent ? 0 : b.Sma).Take(30);
                foreach (var b in order)
                    dests.Add(new Dictionary<string, object> { { "name", b.Name }, { "on", body != null && body.Name == b.Name }, { "url", "action:eng-pick/dest/" + Uri.EscapeDataString(b.Name) } });
            }
            m["hasPicker"] = dests.Count > 0;
            m["dests"] = dests;
            string goal = flyby ? "flyby" : land ? "land" : "orbit";
            m["goals"] = new[] { new[] { "flyby", "Flyby" }, new[] { "orbit", "Orbit" }, new[] { "land", "Land" } }
                .Select(g => (object)new Dictionary<string, object> { { "name", g[1] }, { "on", body != null && goal == g[0] }, { "url", "action:eng-pick/goal/" + g[0] } }).ToList();
            m["trips"] = new[] { new[] { "oneway", "One-way" }, new[] { "return", "Return" } }
                .Select(g => (object)new Dictionary<string, object> { { "name", g[1] }, { "on", body != null && ret == (g[0] == "return") }, { "url", "action:eng-pick/trip/" + g[0] } }).ToList();
        }

        /// <summary>Changes one choice of the mission picker and rewrites the mission text. No AI call.</summary>
        public void EngPick(string what, string value)
        {
            EngSnapshot s = null;
            try { s = Engine.EngineeringData(); } catch { }
            string t = " " + (State.EngMission ?? "").ToLowerInvariant() + " ";
            var body = EngMissions.FindBody(t, s);
            string dest = body != null ? body.Name : "";
            bool flyby = t.Contains("flyby") || t.Contains("fly by"), land = !flyby && (t.Contains("land") || t.Contains("surface") || t.Contains("base") || t.Contains("rover"));
            bool ret = t.Contains("return") || t.Contains("back") || t.Contains("home") || t.Contains("round trip");
            string goal = flyby ? "flyby" : land ? "land" : "orbit";
            if (what == "dest") dest = value; else if (what == "goal") goal = value; else if (what == "trip") ret = value == "return";
            if (dest == "") { if (s != null && s.Home != null) { var first = s.Bodies.Where(b => b.Parent == s.Home.Name).OrderBy(b => b.Sma).FirstOrDefault(); if (first != null) dest = first.Name; } }
            if (dest == "") return;
            State.EngMission = (goal == "flyby" ? "Flyby of " : goal == "land" ? "Land on " : "Orbit ") + dest + (ret ? " and return" : "");
        }

        void SheetModel(Dictionary<string, object> m, EngSnapshot s, EngPlan plan, List<string> review)
        {
            var sheet = s != null ? s.Sheet : null;
            bool has = sheet != null && sheet.Parts.Count > 0;
            m["hasSheet"] = has;
            if (!has && EngTab != "overview") EngTab = "overview";
            var power = has ? Engine.EngPower(s, plan) : null;
            var staging = has ? sheet.StagingIssues(s.Stages) : new List<string>();
            int powerBad = power != null ? power.Problems : 0, stagingBad = staging.Count(x => x.StartsWith("!"));
            int reviewBad = review.Count(x => x.StartsWith("!") && !x.Contains("See the power budget") && !x.Contains("see the staging check"));
            m["tabs"] = EngTabs.Select(t => (object)new Dictionary<string, object>
            {
                { "label", t[1] }, { "on", EngTab == t[0] }, { "url", "action:eng-tab/" + t[0] },
                { "count", t[0] == "power" ? powerBad : t[0] == "stages" ? stagingBad : t[0] == "problems" ? powerBad + stagingBad + reviewBad : 0 },
            }).ToList();
            foreach (var t in EngTabs) m["tab_" + t[0]] = EngTab == t[0];
            if (!has) return;
            double age = Math.Max(0, EngClock() - sheet.BuiltAt);
            m["liveNote"] = "live from the " + (sheet.Flying ? "craft" : "builder") + (age < 1.5 ? ", just read" : ", read " + EngMath.N(age) + " s ago");

            if (power != null)
            {
                m["powerRating"] = power.Rating; m["powerWhere"] = power.Where;
                m["powerBad"] = power.Rating.StartsWith("short");
                m["pSources"] = power.Sources.Select(Line).ToList(); m["pDemand"] = power.Demand.Select(Line).ToList();
                m["pThere"] = power.There.Select(Line).ToList(); m["pFixes"] = power.Fixes.Select(Line).ToList();
                m["hasSources"] = power.Sources.Count > 0; m["hasDemand"] = power.Demand.Count > 0; m["hasThere"] = power.There.Count > 0; m["hasFixes"] = power.Fixes.Count > 0;
            }
            m["stageList"] = sheet.Stages(s.Stages).Select(ev =>
            {
                var dropped = sheet.Parts.Where(p => p.DropStage == ev.Stage).ToList();
                bool editor = s.Scene == "vab" || s.Scene == "sph";
                return (object)new Dictionary<string, object>
                {
                    { "n", ev.Stage }, { "fires", ev.Fires.Count > 0 ? string.Join(", ", ev.Fires.ToArray()) : "nothing staged" }, { "empty", ev.Fires.Count == 0 },
                    { "dv", ev.Dv != null && ev.Dv.DvVac > 0.5 ? EngMath.N(ev.Dv.DvVac) + " m/s, TWR " + EngMath.N(editor ? (ev.Dv.TwrAsl > 0 && ev.Stage == sheet.MaxStage ? ev.Dv.TwrAsl : ev.Dv.TwrVac) : ev.Dv.TwrVac, "0.00") : "" },
                    { "drops", dropped.Count > 0 ? "drops " + dropped.Count + " part" + (dropped.Count == 1 ? "" : "s") + ", " + EngMath.N(dropped.Sum(p => p.DryMass + p.ResMass), "0.0#") + " t" : "" },
                };
            }).ToList();
            m["stagingIssues"] = staging.Select(x => Line(x.StartsWith("! ") ? x.Substring(2) : x, "", x.StartsWith("!"))).ToList();
            m["stagingClean"] = staging.Count == 0;
            var everywhere = sheet.Everywhere();
            m["partList"] = sheet.Groups().Take(120).Select(g => (object)new Dictionary<string, object>
            {
                { "title", (g.Count > 1 ? g.Count + " × " : "") + g.First.Title }, { "mod", g.First.Mod == "stock" ? "" : g.First.Mod },
                { "mass", EngMath.N(g.Mass, "0.0##") + " t" },
                { "when", (g.First.Modules.Any(x => x.Staged) ? "stage " + g.First.Stage + " · " : "") + (g.First.DropStage >= 0 ? "dropped in stage " + g.First.DropStage : "stays") },
                { "holds", CraftSheet.ResText(g.First.Res, g.Count) }, { "does", CraftSheet.Does(g.First, everywhere, true) },
            }).ToList();
            m["partCount"] = sheet.Parts.Count;
            var mods = sheet.Parts.Where(p => p.Mod != "" && p.Mod != "stock").GroupBy(p => p.Mod).OrderByDescending(g => g.Count()).Select(g => g.Key + " (" + g.Count() + ")").ToArray();
            m["partMods"] = mods.Length > 0 ? "Modded parts from: " + string.Join(", ", mods) + "." : "";
            m["partEverywhere"] = everywhere.Count > 0 ? "On most parts, so not repeated below: " + string.Join(", ", everywhere.ToArray()) + "." : "";

            // Everything that is wrong, worst first, in one list.
            var all = new List<object>();
            if (power != null) foreach (var l in power.There.Where(x => x.Bad)) all.Add(Line("Power: " + l.Text + (l.Value != "" ? " " + l.Value : ""), "", true));
            foreach (var x in staging.Where(x => x.StartsWith("!"))) all.Add(Line("Staging: " + x.Substring(2), "", true));
            foreach (var x in review.Where(x => x.StartsWith("!") && !x.Contains("See the power budget") && !x.Contains("see the staging check"))) all.Add(Line(x.Substring(2), "", true));
            foreach (var x in staging.Where(x => !x.StartsWith("!"))) all.Add(Line("Staging: " + x));
            foreach (var x in review.Where(x => !x.StartsWith("!"))) all.Add(Line(x));
            if (power != null) foreach (var l in power.Fixes) all.Add(Line("Power: " + l.Text + (l.Value != "" ? " (" + l.Value + ")" : ""), "", false, "", true));
            m["allProblems"] = all;
        }

        /// <summary>Real time in seconds, for the "read N s ago" note. The KSP layer sets it; tests leave it at zero.</summary>
        public static Func<double> EngClock = () => 0;

        Dictionary<string, object> EngineeringModel()
        {
            var m = M();
            var g = Engine.Settings;
            EngSnapshot s = null;
            try { s = Engine.EngineeringData(); } catch { }
            m["on"] = g.EngineeringOn;
            m["off"] = !g.EngineeringOn;
            m["offline"] = !g.CanCallAi;
            m["busy"] = Engine.EngBusy;
            m["scene"] = SceneLabel(s == null ? "" : s.Scene);
            bool editor = s != null && (s.Scene == "vab" || s.Scene == "sph");
            bool flying = s != null && (s.Scene == "flight" || s.Scene == "map");
            m["hasVessel"] = s != null && s.Vessel != "";
            if (s != null && s.Vessel != "")
            {
                m["vessel"] = s.Vessel;
                m["situation"] = s.Situation;
                m["crewText"] = editor ? (s.CrewCapacity > 0 ? s.CrewCapacity + " seats" : "uncrewed") : (s.Crew > 0 ? s.Crew + " crew" : "uncrewed");
                m["mass"] = EngMath.N(s.Mass, "0.0") + " t";
                var cells = new List<object>();
                Action<string, string> cell = (k, v) => cells.Add(new Dictionary<string, object> { { "label", k }, { "value", v } });
                if (s.HasOrbit)
                {
                    cell("Apoapsis", EngMath.Dist(s.ApA)); cell("Periapsis", EngMath.Dist(s.PeA));
                    cell("Inclination", EngMath.N(s.Inclination, "0.0") + "°"); cell("Period", s.Period > 0 ? EngMath.Dur(s.Period) : "escape");
                }
                else if (flying) { cell("Altitude", EngMath.Dist(s.Altitude)); cell("Speed", EngMath.N(s.Speed) + " m/s"); }
                else if (editor) cell("Parts", s.Parts.ToString());
                if (s.Target != "")
                {
                    cell("Target", s.Target);
                    if (s.PhaseAngle >= 0) cell("Phase angle", EngMath.N(s.PhaseAngle) + "°");
                    else if (s.TargetDistance >= 0) cell("Distance", EngMath.Dist(s.TargetDistance));
                }
                m["cells"] = cells;
            }
            var stages = s == null ? new List<EngStage>() : s.Stages.OrderByDescending(x => x.Stage).ToList();
            m["hasStages"] = stages.Count > 0;
            m["noDv"] = s != null && s.Vessel != "" && stages.Count == 0;
            if (stages.Count > 0)
            {
                bool actual = flying;
                double max = Math.Max(1, stages.Max(x => actual ? x.DvActual : x.DvVac));
                m["dvKind"] = actual ? "current conditions" : "vacuum";
                m["stages"] = stages.Select(x =>
                {
                    double dv = actual ? x.DvActual : x.DvVac;
                    double twr = actual ? x.TwrActual : (editor ? x.TwrAsl : x.TwrVac);
                    return (object)new Dictionary<string, object>
                    {
                        { "n", x.Stage }, { "dv", EngMath.N(dv) + " m/s" }, { "pct", EngMath.N(Math.Max(2, dv / max * 100)) + "%" },
                        { "twr", EngMath.N(twr, "0.00") + (editor ? " at launch" : "") }, { "burn", EngMath.Dur(x.BurnTime) },
                    };
                }).ToList();
                m["total"] = EngMath.N(actual ? s.TotalDvActual : s.TotalDvVac) + " m/s";
            }
            m["facts"] = NewsEngine.EngFacts(s).Cast<object>().ToList();
            Func<string, object> chk = t => new Dictionary<string, object> { { "text", t.StartsWith("! ") ? t.Substring(2) : t }, { "bad", t.StartsWith("!") } };
            var node = NewsEngine.EngNodeCheck(s);
            m["hasNode"] = node.Count > 0;
            m["node"] = node.Select(chk).ToList();
            var plan = Engine.EngCurrentPlan(s);
            var review = s != null && (editor || flying) ? Engine.EngReview(s, plan) : new List<string>();
            SheetModel(m, s, plan, review);
            MissionPicker(m, s);
            m["hasReview"] = review.Count > 0;
            m["review"] = review.Select(chk).ToList();
            m["mission"] = State.EngMission;
            m["hasPlan"] = plan != null && plan.Known;
            m["planNote"] = plan == null ? "" : plan.Note;
            if (plan != null && plan.Known)
            {
                m["legs"] = plan.Legs.Select(l => (object)new Dictionary<string, object> { { "name", l.Name }, { "dv", EngMath.N(l.Dv) + " m/s" }, { "stages", l.Stages }, { "short", l.Short } }).ToList();
                m["total"] = EngMath.N(plan.Total) + " m/s";
                m["need"] = EngMath.N(plan.Need) + " m/s";
                m["verdict"] = plan.Have <= 0 ? "" : plan.Margin >= plan.Total * 0.1 ? EngMath.N(plan.Margin) + " m/s spare" : plan.Margin >= 0 ? "tight: " + EngMath.N(plan.Margin) + " m/s spare" : EngMath.N(-plan.Margin) + " m/s short";
            }
            var incs = State.Incidents.AsEnumerable().Reverse().Take(5).ToList();
            m["hasIncidents"] = incs.Count > 0;
            m["incidents"] = incs.Select(i => (object)new Dictionary<string, object>
            {
                { "id", i.Id }, { "title", i.VesselName != "" ? i.VesselName : "Unknown craft" }, { "date", Date(i.UT) },
                { "kind", i.Kind == EventTypes.CrewKilled ? "loss of crew" : "vehicle lost" }, { "crew", string.Join(", ", i.Crew.ToArray()) },
                { "open", i.Status == "open" }, { "working", i.Status == "working" }, { "done", i.Status == "done" },
                { "cause", i.Cause }, { "findings", i.Findings }, { "fixes", i.Fixes }, { "released", i.ReleaseId != "" },
            }).ToList();
            m["hasFleet"] = s != null && s.Vessels.Count > 0;
            m["fleet"] = s == null ? new List<object>() : s.Vessels.Take(12).Cast<object>().ToList();
            m["sceneNote"] = editor ? "The team sees the craft on the floor: stages, TWR, mass and delta-v. Set the launch body in the game's delta-v panel for other worlds."
                : flying ? "Set a target (a moon, planet or vessel) to get phase angles and transfer windows."
                : "In the VAB and SPH the team sees the craft being built. In flight they see your orbit, target and stages.";
            var recent = State.EngChat.Skip(Math.Max(0, State.EngChat.Count - 14)).ToList();
            var lastSpoke = new HashSet<string>(State.EngChat.AsEnumerable().Reverse().TakeWhile(x => x.Role != "player").Select(x => x.Role));
            m["team"] = NewsEngine.EngTeam.Select(k => (object)new Dictionary<string, object> { { "name", NewsEngine.EngName(k) }, { "spoke", lastSpoke.Contains(k) } }).ToList();
            m["messages"] = recent.Select(x => (object)new Dictionary<string, object>
            {
                { "player", x.Role == "player" }, { "who", NewsEngine.EngName(x.Role) },
                { "options", x == State.EngChat.LastOrDefault() && x.Actions.StartsWith("opt:") && !Engine.EngBusy
                    ? x.Actions.Substring(4).Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Select(o => (object)new Dictionary<string, object> { { "text", o }, { "url", "action:eng-ask/" + Uri.EscapeDataString(o) } }).ToList()
                    : new List<object>() },
                { "lines", x.Text.Split('\n').Where(l => l.Trim() != "").Select(l => (object)l).ToList() },
            }).ToList();
            m["empty"] = State.EngChat.Count == 0;
            m["quick"] = QuickAsks(s).Select(q => (object)new Dictionary<string, object> { { "text", q }, { "url", "action:eng-ask/" + Uri.EscapeDataString(q) } }).ToList();
            return m;
        }
    }
}
