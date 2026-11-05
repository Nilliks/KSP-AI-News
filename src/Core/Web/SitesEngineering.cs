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
                return new[] { "Review my design", "Is my TWR good for launch?", "Can this reach the Mun and come back?", "How should I stage this?" };
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
                { "lines", x.Text.Split('\n').Where(l => l.Trim() != "").Select(l => (object)l).ToList() },
            }).ToList();
            m["empty"] = State.EngChat.Count == 0;
            m["quick"] = QuickAsks(s).Select(q => (object)new Dictionary<string, object> { { "text", q }, { "url", "action:eng-ask/" + Uri.EscapeDataString(q) } }).ToList();
            return m;
        }
    }
}
