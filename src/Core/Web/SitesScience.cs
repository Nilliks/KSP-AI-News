using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KerbalNews.Web
{
    /// <summary>agency.kb/science: the Science Office, where returned results are interpreted and findings published.</summary>
    public partial class Sites
    {
        /// <summary>Results ticked for the next review (kept while the game runs, not saved).</summary>
        readonly HashSet<string> sciPicked = new HashSet<string>();
        string sciBody = "";
        public string ScienceResultText = "";

        Dictionary<string, object> ScienceModel(string filter)
        {
            var m = M();
            if (filter != "interpreted" && filter != "published" && filter != "all") filter = "waiting";
            var all = State.Science.AsEnumerable().Reverse().ToList();
            Func<ScienceResult, string> status = r =>
            {
                var f = Engine.FindingFor(r);
                return f == null ? "waiting" : f.Status == "journal" || f.Status == "press" ? "published" : f.Status == "internal" ? "internal" : "interpreted";
            };
            m["countWaiting"] = all.Count(r => status(r) == "waiting");
            m["countInterpreted"] = all.Count(r => status(r) == "interpreted" || status(r) == "internal");
            m["countPublished"] = all.Count(r => status(r) == "published");
            foreach (var f in new[] { "waiting", "interpreted", "published", "all" }) m["is_" + f] = filter == f;
            var bodies = all.Select(r => r.Body).Where(b => b != "").Distinct().OrderBy(b => b == "Kerbin" ? "" : b).ToList();
            if (sciBody != "" && !bodies.Contains(sciBody)) sciBody = "";
            m["bodies"] = bodies.Select(b => (object)new Dictionary<string, object> { { "name", b }, { "on", b == sciBody }, { "url", "action:sci-body/" + Uri.EscapeDataString(b) } }).ToList();
            m["allBodies"] = sciBody == "";
            var shown = all.Where(r => (sciBody == "" || r.Body == sciBody) && (filter == "all" || status(r) == filter || (filter == "interpreted" && status(r) == "internal"))).Take(60).ToList();
            m["results"] = shown.Select(r => (object)ResultModel(r, status(r))).ToList();
            m["empty"] = shown.Count == 0;
            m["noScience"] = all.Count == 0;
            var picked = sciPicked.Select(k => Engine.ResultByKey(k)).Where(r => r != null && Engine.FindingFor(r) == null).ToList();
            m["picked"] = picked.Count;
            m["hasPicked"] = picked.Count > 0;
            m["busy"] = Engine.SciBusy;
            m["offline"] = !Engine.Settings.CanCallAi;
            m["team"] = NewsEngine.ScienceTeam.Select(s => (object)new Dictionary<string, object> { { "name", s.Name }, { "field", s.Field } }).ToList();
            m["kerbalism"] = Engine.KerbalismScience;
            m["result"] = ScienceResultText;
            return m;
        }

        Dictionary<string, object> ResultModel(ScienceResult r, string status)
        {
            string key = NewsEngine.ResultKey(r);
            var f = Engine.FindingFor(r);
            var d = new Dictionary<string, object>
            {
                { "title", r.Title != "" ? r.Title : r.SubjectId },
                { "line", (r.Transmitted ? "Transmitted from " : "Recovered with ") + (r.VesselName != "" ? r.VesselName : "an agency craft") + (r.Crew.Count > 0 ? " (" + string.Join(", ", r.Crew.ToArray()) + ")" : "")
                    + " · " + Date(r.UT) + " · " + r.Amount.ToString("0.0", CultureInfo.InvariantCulture) + " science · " + Math.Round(r.Completion * 100) + "% of this subject now known" },
                { "resultText", r.ResultText },
                { "first", Engine.FirstFromBody(r) },
                { "firstLabel", "FIRST FROM " + r.Body.ToUpperInvariant() },
                { "partial", Engine.KerbalismScience && r.Completion < 0.99 && status == "waiting" },
                { "partialLabel", "KERBALISM · " + Math.Round(r.Completion * 100) + "% COLLECTED" },
                { "waiting", status == "waiting" }, { "interpreted", status == "interpreted" }, { "internal", status == "internal" }, { "published", status == "published" },
                { "picked", sciPicked.Contains(key) },
                { "pickUrl", "action:sci-pick/" + Uri.EscapeDataString(key) },
                { "askUrl", "action:sci-ask-one/" + Uri.EscapeDataString(key) },
            };
            if (f != null)
            {
                d["finding"] = new Dictionary<string, object>
                {
                    { "scientist", f.Scientist }, { "field", f.Field }, { "confidence", f.Confidence }, { "importance", f.Importance }, { "title", f.Title },
                    { "text", f.Finding }, { "unknowns", f.Unknowns }, { "next", f.NextStep }, { "question", f.Question }, { "offline", f.Offline },
                    { "journalUrl", "action:sci-publish/" + f.Id + "|journal" }, { "pressUrl", "action:sci-publish/" + f.Id + "|press" }, { "internalUrl", "action:sci-publish/" + f.Id + "|internal" },
                    { "canPublish", f.Status == "new" },
                };
                if (f.Status == "journal")
                {
                    var a = State.Articles.FirstOrDefault(x => x.Id == f.ArticleId);
                    d["pubLine"] = a != null && a.PublishedUT <= Now() ? "Published in the Kerbin Science Review on " + Date(a.PublishedUT) : "In peer review at the Kerbin Science Review, out " + Date(f.PublishUT);
                    d["pubUrl"] = a != null && a.PublishedUT <= Now() ? ArticleUrl(a) : "";
                }
                else if (f.Status == "press")
                {
                    d["pubLine"] = "Given to the press on " + Date(f.PublishUT);
                    d["pubUrl"] = PressHost;
                }
                else if (f.Status == "internal") d["pubLine"] = "Kept internal";
            }
            return d;
        }

        ActionResult HandleScience(string verb, string arg, IDictionary<string, string> form)
        {
            var r = new ActionResult();
            switch (verb)
            {
                case "sci-pick":
                    if (!sciPicked.Remove(arg)) sciPicked.Add(arg);
                    return r;
                case "sci-body":
                    sciBody = sciBody == arg ? "" : arg;
                    return r;
                case "sci-ask-one":
                    ScienceResultText = Engine.SciAnalyse(new[] { arg }, Get(form, "sciq"), Now()) ? "" : "The science team is busy or that result was already interpreted.";
                    sciPicked.Remove(arg);
                    return r;
                case "sci-ask":
                {
                    var keys = sciPicked.ToList();
                    ScienceResultText = keys.Count == 0 ? "Tick at least one result first." : Engine.SciAnalyse(keys, Get(form, "sciq"), Now()) ? "" : "The science team is busy or those results were already interpreted.";
                    if (ScienceResultText == "") sciPicked.Clear();
                    return r;
                }
                case "sci-publish":
                {
                    int bar = arg.LastIndexOf('|');
                    if (bar < 0) return r;
                    string id = arg.Substring(0, bar), how = arg.Substring(bar + 1);
                    ScienceResultText = Engine.SciPublish(id, how, Now())
                        ? how == "journal" ? "Sent to the Kerbin Science Review. Peer review takes a few days." : how == "press" ? "Given to the press. Expect coverage tomorrow." : "Kept internal."
                        : "That finding was already handled.";
                    return r;
                }
            }
            return null;
        }
    }
}
