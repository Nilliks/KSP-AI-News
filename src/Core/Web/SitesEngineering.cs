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
        static readonly string[][] EngTabs =
        {
            new[] { "overview", "Overview" }, new[] { "power", "Power" }, new[] { "heat", "Heat" }, new[] { "stages", "Stages" }, new[] { "parts", "Parts" },
            new[] { "comms", "Comms" }, new[] { "landing", "Landing" }, new[] { "balance", "Balance" }, new[] { "launch", "Launch" }, new[] { "crew", "Crew" }, new[] { "cost", "Cost" },
            new[] { "problems", "All problems" },
            new[] { "gonogo", "Go / no-go" }, new[] { "flight", "In flight" }, new[] { "windows", "Windows" }, new[] { "contracts", "Contracts" }, new[] { "science", "Science" },
            new[] { "efficiency", "Efficiency" }, new[] { "swaps", "Part swaps" }, new[] { "compare", "Compare" },
            new[] { "needs", "What it needs" }, new[] { "timeline", "Timeline" }, new[] { "ascent", "Ascent" }, new[] { "aerobrake", "Aerobraking" }, new[] { "relays", "Relays" },
            new[] { "mining", "Mining" }, new[] { "docking", "Docking" }, new[] { "plane", "Plane" }, new[] { "rover", "Rover" }, new[] { "setup", "Setup" }, new[] { "reliability", "Reliability" },
            new[] { "tech", "Tech advice" }, new[] { "launcher", "Launcher" }, new[] { "predicted", "Predicted vs actual" },
            new[] { "meeting", "Meeting" }, new[] { "notes", "Team notes" }, new[] { "saved", "Saved craft" },
        };
        /// <summary>Tabs that work with no craft open.</summary>
        static readonly string[] NoCraftTabs = { "overview", "needs", "timeline", "relays", "predicted", "meeting", "notes", "saved" };
        /// <summary>Tabs with their own layout, so they are always offered when there is a craft.</summary>
        static readonly string[] OwnTabs = { "overview", "parts", "stages", "problems", "power", "compare", "swaps", "tech", "meeting", "notes", "saved" };
        /// <summary>Which drop-down is open ("" = none): a tab group ("tabs:Checks") or a mission choice ("dest", "goal", "trip", "margin", "level").</summary>
        public string EngMenu = "";
        public string EngMeetView = "", EngNotesFilter = "all", EngNoteEdit = "";
        public int EngMeetLen = 10;
        /// <summary>The tabs in rows by what they are for.</summary>
        static readonly string[][] EngTabGroups =
        {
            new[] { "Craft", "overview", "parts", "stages", "problems" },
            new[] { "Checks", "power", "heat", "comms", "landing", "balance", "launch", "crew", "cost", "setup", "reliability" },
            new[] { "Mission", "needs", "timeline", "gonogo", "flight", "windows", "ascent", "aerobrake", "relays", "contracts", "science", "mining", "docking", "plane", "rover" },
            new[] { "Improve", "efficiency", "swaps", "tech", "launcher", "compare", "predicted" },
            new[] { "Team", "meeting", "notes", "saved" },
        };
        /// <summary>The saved version the Compare tab is set against ("" = the latest of this craft).</summary>
        public string EngCompareId = "";
        /// <summary>Lines of the older design review that a dedicated check now covers in more detail.</summary>
        static readonly string[] Covered = { "See the power budget", "see the staging check", "The landing stage", "No landing legs", "No parachutes", "No heat shield", "Launch stage TWR", "No antenna" };
        static bool IsCovered(string line) { return Covered.Any(x => line.Contains(x)); }

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
            var goals = new[] { new[] { "flyby", "Flyby" }, new[] { "orbit", "Orbit" }, new[] { "land", "Land" } }
                .Select(g => (object)new Dictionary<string, object> { { "name", g[1] }, { "on", body != null && goal == g[0] }, { "url", "action:eng-pick/goal/" + g[0] } }).ToList();
            var trips = new[] { new[] { "oneway", "One-way" }, new[] { "return", "Return" } }
                .Select(g => (object)new Dictionary<string, object> { { "name", g[1] }, { "on", body != null && ret == (g[0] == "return") }, { "url", "action:eng-pick/trip/" + g[0] } }).ToList();
            var margins = new[] { 5, 10, 20, 30 }
                .Select(g => (object)new Dictionary<string, object> { { "name", g + "%" }, { "on", State.EngMargin == g }, { "url", "action:eng-margin/" + g } }).ToList();
            var levels = new[] { new[] { "short", "Short" }, new[] { "normal", "Normal" }, new[] { "teach", "Teach me" } }
                .Select(g => (object)new Dictionary<string, object> { { "name", g[1] }, { "on", State.EngLevel == g[0] }, { "url", "action:eng-level/" + g[0] } }).ToList();
            // One row of drop-downs; the open one lists its choices underneath.
            Func<string, string, string, object> menu = (key, label, value) => new Dictionary<string, object> { { "label", label + (value != "" ? ": " + value : "") }, { "open", EngMenu == key }, { "url", "action:eng-menu/" + key } };
            var menus = new List<object>();
            if (dests.Count > 0)
            {
                menus.Add(menu("dest", "Go to", body != null ? body.Name : "choose"));
                menus.Add(menu("goal", "Do", body != null ? (goal == "flyby" ? "Flyby" : goal == "land" ? "Land" : "Orbit") : ""));
                menus.Add(menu("trip", "Trip", body != null ? (ret ? "Return" : "One-way") : ""));
                menus.Add(menu("margin", "Margin", State.EngMargin + "%"));
            }
            menus.Add(menu("level", "Answers", State.EngLevel == "short" ? "Short" : State.EngLevel == "teach" ? "Teach me" : "Normal"));
            m["pickMenus"] = menus;
            // The destination list, a line for each planet with its moons after it.
            var destRows = new List<object>();
            if (EngMenu == "dest" && s != null && s.Home != null)
            {
                var home = s.Home;
                Func<EngBody, object> chip = b2 => new Dictionary<string, object> { { "name", b2.Name }, { "on", body != null && body.Name == b2.Name }, { "url", "action:eng-pick/dest/" + Uri.EscapeDataString(b2.Name) } };
                var moons = s.Bodies.Where(b2 => b2.Parent == home.Name).OrderBy(b2 => b2.Sma).ToList();
                if (moons.Count > 0) destRows.Add(new Dictionary<string, object> { { "head", "Near " + home.Name }, { "items", moons.Select(chip).ToList() } });
                foreach (var planet in s.Bodies.Where(b2 => !b2.IsStar && b2.Parent == home.Parent && b2.Name != home.Name).OrderBy(b2 => b2.Sma))
                    destRows.Add(new Dictionary<string, object> { { "head", "" }, { "items", new[] { planet }.Concat(s.Bodies.Where(b2 => b2.Parent == planet.Name).OrderBy(b2 => b2.Sma)).Select(chip).ToList() } });
            }
            m["destRows"] = destRows;
            m["pickOpen"] = EngMenu == "dest" || EngMenu == "goal" || EngMenu == "trip" || EngMenu == "margin" || EngMenu == "level";
            m["pickItems"] = EngMenu == "goal" ? goals : EngMenu == "trip" ? trips : EngMenu == "margin" ? margins : EngMenu == "level" ? levels : new List<object>();
        }

        /// <summary>Changes one choice of the mission picker and rewrites the mission text. No AI call.</summary>
        public void EngPick(string what, string value)
        {
            EngSnapshot s = null;
            try { s = Engine.EngLook(); } catch { }
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
            if (!has && !NoCraftTabs.Contains(EngTab)) EngTab = "overview";
            var power = has ? Engine.EngPower(s, plan) : null;
            var staging = has ? sheet.StagingIssues(s.Stages) : new List<string>();
            int powerBad = power != null ? power.Problems : 0, stagingBad = staging.Count(x => x.StartsWith("!"));
            int reviewBad = review.Count(x => x.StartsWith("!") && !IsCovered(x));
            var checks = has ? Engine.EngChecks(s, plan) : new List<CheckReport>();
            int checksBad = checks.Sum(x => x.Problems);
            // The parts catalogue is slow to read the first time, so only the tabs that need it ask for it.
            var tools = has ? Engine.EngToolReports(s, plan, checks, EngTab == "swaps" || EngTab == "efficiency", EngCompareId) : new List<CheckReport>();
            tools.AddRange(Engine.EngMissionReports(s, plan, checks, EngTab == "tech"));
            bool flyingNow = s != null && (s.Scene == "flight" || s.Scene == "map");
            if (EngTab == "flight" && !flyingNow) EngTab = "overview";
            Func<string[], object> tabOf = t =>
            {
                var ck = checks.FirstOrDefault(x => x.Key == t[0]) ?? tools.FirstOrDefault(x => x.Key == t[0]);
                return new Dictionary<string, object>
                {
                    { "label", t[1] }, { "on", EngTab == t[0] }, { "url", "action:eng-tab/" + t[0] },
                    { "count", t[0] == "power" ? powerBad : t[0] == "stages" ? stagingBad : t[0] == "problems" ? powerBad + stagingBad + reviewBad + checksBad : ck != null && t[0] != "compare" && t[0] != "contracts" ? ck.Problems : 0 },
                };
            };
            // Each group is one drop-down: the bar shows the groups, and the open one lists its tabs underneath.
            Func<string, bool> offered = k => (has || NoCraftTabs.Contains(k)) && (OwnTabs.Contains(k) && (has || NoCraftTabs.Contains(k)) || checks.Any(x => x.Key == k) || tools.Any(x => x.Key == k));
            if (!offered(EngTab)) EngTab = "overview";
            var groups = EngTabGroups.Select(g => new { name = g[0], keys = g.Skip(1).Where(offered).ToList() }).Where(g => g.keys.Count > 0).ToList();
            m["tabGroups"] = groups.Select(g =>
            {
                var tabs = g.keys.Select(k => (Dictionary<string, object>)tabOf(EngTabs.First(t => t[0] == k))).ToList();
                var cur = tabs.FirstOrDefault(t => (bool)t["on"]);
                return (object)new Dictionary<string, object>
                {
                    { "name", g.name }, { "label", cur != null ? g.name + ": " + cur["label"] : g.name }, { "on", cur != null }, { "open", EngMenu == "tabs:" + g.name },
                    { "count", tabs.Sum(t => (int)t["count"]) }, { "url", "action:eng-menu/tabs:" + g.name },
                };
            }).ToList();
            var openGroup = groups.FirstOrDefault(g => EngMenu == "tabs:" + g.name);
            m["tabMenu"] = openGroup == null ? new List<object>() : openGroup.keys.Select(k => tabOf(EngTabs.First(t => t[0] == k))).ToList();
            m["hasTabMenu"] = openGroup != null;
            foreach (var t in EngTabs) m["tab_" + t[0]] = EngTab == t[0];
            var shown = checks.FirstOrDefault(x => x.Key == EngTab) ?? tools.FirstOrDefault(x => x.Key == EngTab);
            if ((EngTab == "swaps" || EngTab == "tech") && shown == null) shown = new CheckReport { Title = EngTab == "tech" ? "Tech advice" : "Part swaps", Rating = "no craft" };
            m["snaps"] = State.EngSnaps.AsEnumerable().Reverse().Take(12).Select(x => (object)new Dictionary<string, object>
            {
                { "name", x.Name }, { "meta", EngMath.N(x.Mass, "0.0") + " t · " + EngMath.N(x.Dv) + " m/s · " + x.Parts + " parts · " + Date(x.UT) },
                { "on", x.Id == EngCompareId }, { "url", "action:eng-compare/" + x.Id }, { "del", "action:eng-snap-del/" + x.Id },
            }).ToList();
            m["hasSnaps"] = State.EngSnaps.Count > 0;
            m["tab_check"] = shown != null;
            if (shown != null)
            {
                m["ckTitle"] = shown.Title; m["ckRating"] = shown.Rating; m["ckBad"] = shown.Bad; m["ckNote"] = shown.Note;
                m["ckLines"] = shown.Lines.Select(Line).ToList(); m["ckFixes"] = shown.Fixes.Select(Line).ToList(); m["hasCkFixes"] = shown.Fixes.Count > 0;
            }
            TeamModel(m, s);
            if (!has) return;
            // The scorecard: every check with its verdict, one click from its tab.
            var score = new List<object>();
            Action<string, string, string, bool> row = (key, label, rating, bad) => score.Add(new Dictionary<string, object> { { "label", label }, { "rating", rating }, { "bad", bad }, { "url", "action:eng-tab/" + key } });
            if (power != null) row("power", "Power", power.Rating, power.Problems > 0 || power.Rating.StartsWith("short"));
            row("stages", "Staging", stagingBad > 0 ? stagingBad + " problem" + (stagingBad == 1 ? "" : "s") : staging.Count > 0 ? "check" : "ok", stagingBad > 0);
            foreach (var ck in checks) row(ck.Key, ck.Title, ck.Rating, ck.Bad);
            var poll = tools.FirstOrDefault(x => x.Key == "gonogo");
            if (poll != null) { m["goRating"] = poll.Rating; m["goBad"] = poll.Bad; }
            var live = tools.FirstOrDefault(x => x.Key == "flight");
            if (live != null && live.Rating != "nominal") row("flight", "In flight", live.Rating, live.Bad);
            var lessons = tools.FirstOrDefault(x => x.Key == "lessons");
            m["lessons"] = lessons == null ? new List<object>() : lessons.Lines.Select(l => (object)l.Text).ToList();
            m["hasLessons"] = lessons != null;
            m["score"] = score;
            m["hasScore"] = score.Count > 0;
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
            foreach (var ck in checks) foreach (var l in ck.Lines.Where(x => x.Bad)) all.Add(Line(ck.Title + ": " + l.Text + (l.Value != "" ? " (" + l.Value + ")" : ""), "", true));
            foreach (var x in review.Where(x => x.StartsWith("!") && !IsCovered(x))) all.Add(Line(x.Substring(2), "", true));
            foreach (var x in staging.Where(x => !x.StartsWith("!"))) all.Add(Line("Staging: " + x));
            foreach (var x in review.Where(x => !x.StartsWith("!") && !IsCovered(x))) all.Add(Line(x));
            if (power != null) foreach (var l in power.Fixes) all.Add(Line("Power: " + l.Text + (l.Value != "" ? " (" + l.Value + ")" : ""), "", false, "", true));
            foreach (var ck in checks) foreach (var l in ck.Fixes) all.Add(Line(ck.Title + ": " + l.Text + (l.Value != "" ? " (" + l.Value + ")" : ""), "", false, "", true));
            if (all.Count == 0) all.Add(Line("Nothing found: every check passes."));
            m["allProblems"] = all;
        }

        /// <summary>The meeting, team notes and saved craft pages.</summary>
        void TeamModel(Dictionary<string, object> m, EngSnapshot s)
        {
            m["reviewing"] = Engine.EngPinned != null;
            m["reviewName"] = Engine.EngPinned != null ? Engine.EngPinned.Vessel : "";
            m["dvEstimated"] = s != null && s.DvEstimated;
            if (EngTab == "saved")
            {
                List<string[]> saved = null;
                try { saved = Engine.EngineeringSaved(); } catch { }
                saved = saved ?? new List<string[]>();
                m["savedList"] = saved.Take(80).Select((x, i) => (object)new Dictionary<string, object> { { "name", x[0] }, { "where", x.Length > 1 ? x[1] : "" }, { "url", "action:eng-saved/" + i } }).ToList();
                m["hasSaved"] = saved.Count > 0;
            }
            if (EngTab == "notes")
            {
                string craft = s != null ? s.Vessel : "";
                IEnumerable<EngNote> list = State.EngNotes;
                switch (EngNotesFilter)
                {
                    case "pinned": list = list.Where(n => n.Pinned); break;
                    case "craft": list = list.Where(n => craft != "" && n.Craft == craft); break;
                    case "all": break;
                    default: list = list.Where(n => n.Kind == EngNotesFilter); break;
                }
                var filters = new List<string[]> { new[] { "all", "All" }, new[] { "pinned", "Pinned" }, new[] { "preference", "Your preferences" } };
                if (craft != "") filters.Add(new[] { "craft", craft });
                foreach (var k in new[] { "meeting", "qa", "review", "plan", "flight", "snapshot", "investigation", "note" }) if (State.EngNotes.Any(n => n.Kind == k)) filters.Add(new[] { k, char.ToUpperInvariant(NewsEngine.KindLabel(k)[0]) + NewsEngine.KindLabel(k).Substring(1) });
                m["noteFilters"] = filters.Select(f => (object)new Dictionary<string, object> { { "name", f[1] }, { "on", EngNotesFilter == f[0] }, { "url", "action:eng-notes/" + Uri.EscapeDataString(f[0]) } }).ToList();
                var shownNotes = list.Reverse().Take(80).ToList();
                m["notes"] = shownNotes.Select(n => (object)new Dictionary<string, object>
                {
                    { "id", n.Id }, { "meta", Date(n.UT) + " · " + NewsEngine.KindLabel(n.Kind) + (n.Craft != "" ? " · " + n.Craft : "") }, { "text", n.Text }, { "pinned", n.Pinned },
                    { "editing", EngNoteEdit == n.Id }, { "pin", "action:eng-note-pin/" + n.Id }, { "del", "action:eng-note-del/" + n.Id }, { "edit", "action:eng-note-edit/" + n.Id },
                }).ToList();
                m["noteCount"] = State.EngNotes.Count;
                m["noNotes"] = shownNotes.Count == 0;
            }
            if (EngTab == "meeting")
            {
                var now = Engine.EngMeetingNow;
                var view = EngMeetView == "new" && now == null ? null : now ?? State.EngMeetings.FirstOrDefault(x => x.Id == EngMeetView) ?? (EngMeetView == "new" ? null : State.EngMeetings.LastOrDefault());
                m["mtForm"] = now == null;
                m["mtOffline"] = !Engine.Settings.CanCallAi;
                m["mtLen"] = EngMeetLen;
                m["mtLens"] = new[] { 1, 2, 3, 5, 8, 10, 15, 20, 30, 40, 50, 75, 100 }.Select(n => (object)new Dictionary<string, object> { { "n", n }, { "on", n <= EngMeetLen }, { "at", n == EngMeetLen }, { "url", "action:eng-meet-len/" + n } }).ToList();
                int ag, pr, tk, vt; NewsEngine.MeetingPlan(EngMeetLen, out ag, out pr, out tk, out vt);
                m["mtShape"] = (ag > 0 ? "an agenda, " : "") + (pr == 5 ? "each specialist prepares a position, " : pr == 1 ? "opening positions, " : "") + (tk > 0 ? tk + " turn" + (tk == 1 ? "" : "s") + " of discussion, " : "") + (vt > 0 ? "a vote, " : "") + "then the minutes";
                m["mtFollow"] = view != null && now == null && view.Status != "running";
                m["mtPast"] = State.EngMeetings.AsEnumerable().Reverse().Take(12).Select(x => (object)new Dictionary<string, object> { { "title", x.Objective }, { "meta", Date(x.UT) + (x.Craft != "" ? " · " + x.Craft : "") + " · " + x.Used + " call" + (x.Used == 1 ? "" : "s") }, { "on", view == x }, { "url", "action:eng-meet-view/" + x.Id } }).ToList();
                m["hasMtPast"] = State.EngMeetings.Count > 0;
                m["mtHas"] = view != null;
                if (view != null)
                {
                    string who; string next = NewsEngine.MeetingNext(view, out who);
                    m["mtId"] = view.Id; m["mtObjective"] = view.Objective;
                    m["mtRunning"] = view.Status == "running"; m["mtWaiting"] = view.Status == "waiting"; m["mtDone"] = view.Status == "done"; m["mtStopped"] = view.Status == "stopped";
                    m["mtLive"] = view.Status == "running" || view.Status == "waiting";
                    m["mtStatus"] = view.Status == "running" ? "Call " + (view.Used + 1) + " of " + view.Budget + ": " + (next == "minutes" ? "Gene is writing the minutes" : next == "agenda" ? "Gene is setting the agenda" : next == "prep" || next == "prep-all" ? "the specialists are preparing" : next == "vote" ? NewsEngine.EngShort(who) + " is voting" : NewsEngine.EngShort(who) + " is speaking") + "..."
                        : view.Status == "waiting" ? "Waiting for your decision (" + view.Used + " of " + view.Budget + " calls used)" : view.Status == "done" ? "Finished, " + view.Used + " of " + view.Budget + " calls used" : "Stopped after " + view.Used + " calls";
                    m["mtAgenda"] = view.Agenda.Cast<object>().ToList(); m["hasMtAgenda"] = view.Agenda.Count > 0;
                    m["mtTurns"] = view.Turns.Select(t => (object)new Dictionary<string, object>
                    {
                        { "who", t.Kind == "test" ? "Tested live" : t.Who == "director" ? "You" : NewsEngine.EngName(t.Who) }, { "tag", t.Kind == "position" ? "opening position" : t.Kind == "vote" ? "vote" : "" },
                        { "text", t.Text }, { "test", t.Kind == "test" }, { "you", t.Who == "director" },
                    }).ToList();
                    m["mtQuestion"] = view.Question; m["mtA"] = view.OptionA; m["mtB"] = view.OptionB;
                    m["mtMinutes"] = view.Minutes.Split('\n').Where(l => l.Trim() != "").Cast<object>().ToList();
                    m["mtDecisions"] = view.Decisions.Cast<object>().ToList(); m["hasMtDecisions"] = view.Decisions.Count > 0;
                    m["mtOpen"] = view.Open.Cast<object>().ToList(); m["hasMtOpen"] = view.Open.Count > 0;
                    m["mtActions"] = view.Actions.Select((a, i) =>
                    {
                        string al = a.ToLowerInvariant();
                        string tab = NewsEngine.TabFor(al);
                        return (object)new Dictionary<string, object> { { "text", a }, { "done", i < view.Done.Count && view.Done[i] }, { "url", "action:eng-meet-tick/" + view.Id + "/" + i }, { "show", tab != "" ? "action:eng-tab/" + tab : "" } };
                    }).ToList();
                    m["hasMtActions"] = view.Actions.Count > 0;
                }
            }
        }

        /// <summary>Real time in seconds, for the "read N s ago" note. The KSP layer sets it; tests leave it at zero.</summary>
        public static Func<double> EngClock = () => 0;

        Dictionary<string, object> EngineeringModel()
        {
            var m = M();
            var g = Engine.Settings;
            EngSnapshot s = null;
            try { s = Engine.EngLook(); } catch { }
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
                m["marginPct"] = EngMath.N(plan.MarginPct);
                m["verdict"] = plan.Have <= 0 ? "" : plan.Margin >= plan.Total * plan.MarginPct / 100 ? EngMath.N(plan.Margin) + " m/s spare" : plan.Margin >= 0 ? "tight: " + EngMath.N(plan.Margin) + " m/s spare" : EngMath.N(-plan.Margin) + " m/s short";
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
            m["team"] = NewsEngine.EngSpecialists.Select(k => (object)new Dictionary<string, object> { { "name", NewsEngine.EngShort(k) }, { "spoke", lastSpoke.Contains(k) } }).ToList();
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
