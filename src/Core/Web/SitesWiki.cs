using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KerbalNews.Web
{
    /// <summary>kerbopedia.kb: the encyclopedia of the agency's craft, crew, the places it reached, and the agency itself.</summary>
    public partial class Sites
    {
        public const string WikiHost = "kerbopedia.kb";
        static readonly CultureInfo WInv = CultureInfo.InvariantCulture;

        public static string WikiUrl(WikiPage p) { return WikiHost + "/wiki/" + p.Slug; }

        static readonly string[][] WikiKinds = { new[] { "craft", "Craft" }, new[] { "crew", "Crew" }, new[] { "place", "Places visited" }, new[] { "topic", "Other topics" } };

        PageResult WikiSite(string url, string path, Dictionary<string, string> query)
        {
            if (path.StartsWith("wiki/"))
            {
                string rest = path.Substring(5);
                bool history = rest.EndsWith("/history");
                if (history) rest = rest.Substring(0, rest.Length - 8);
                var p = Engine.WikiGet(Uri.UnescapeDataString(rest));
                if (p == null) return Page(url, "wiki", "missing.html", WikiFrame(new Dictionary<string, object> { { "missing", Uri.UnescapeDataString(rest).Replace('_', ' ') } }), "Not found - Kerbopedia", false);
                return history ? Page(url, "wiki", "history.html", WikiHistoryModel(p), "History of " + p.Title + " - Kerbopedia", false)
                    : Page(url, "wiki", "page.html", WikiPageModel(p), p.Title + " - Kerbopedia", true);
            }
            if (path == "random")
            {
                var all = State.Wiki.Values.ToList();
                if (all.Count > 0) return Page(url, "wiki", "page.html", WikiPageModel(all[new Random().Next(all.Count)]), "Kerbopedia", true);
            }
            if (path.StartsWith("list/")) return Page(url, "wiki", "list.html", WikiListModel(path.Substring(5)), "Kerbopedia", true);
            if (path == "timeline") return Page(url, "wiki", "timeline.html", WikiTimelineModel(), "Timeline - Kerbopedia", true);
            if (path == "search") return Page(url, "wiki", "list.html", WikiSearchModel(query.ContainsKey("q") ? query["q"] : ""), "Search - Kerbopedia", false);
            return Page(url, "wiki", "index.html", WikiHomeModel(), "Kerbopedia", true);
        }

        Dictionary<string, object> WikiFrame(Dictionary<string, object> m)
        {
            m["wikiCount"] = State.Wiki.Count;
            m["kindLinks"] = WikiKinds.Where(k => State.Wiki.Values.Any(p => p.Kind == k[0])).Select(k => (object)new Dictionary<string, object> { { "name", k[1] }, { "url", WikiHost + "/list/" + k[0] } }).ToList();
            var ag = State.Wiki.Values.FirstOrDefault(p => p.Kind == "agency");
            m["agencyUrl"] = ag != null ? WikiUrl(ag) : "";
            m["busy"] = Engine.WikiBusy;
            return m;
        }

        IEnumerable<WikiPage> WikiPages { get { return State.Wiki.Values; } }

        List<Article> WikiRefs(WikiPage p)
        {
            double now = Now();
            return p.Refs.Select(id => State.Articles.FirstOrDefault(a => a.Id == id) ?? State.Archive.FirstOrDefault(a => a.Id == id))
                .Where(a => a != null && !a.Removed && a.PublishedUT <= now).ToList();
        }

        /// <summary>Text to HTML: one paragraph per line, [n] cites linked to the article, other pages' titles linked once.</summary>
        string WikiHtml(string text, WikiPage self, List<Article> refs, HashSet<string> linked)
        {
            var sb = new StringBuilder();
            var titles = WikiPages.Where(p => p != self && p.Title.Length >= 3).OrderByDescending(p => p.Title.Length).ToList();
            foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line == "") continue;
                if (line.StartsWith("- ")) line = line.Substring(2);
                var marks = new List<string>();
                string esc = Html.Escape(line);
                foreach (var t in titles)
                {
                    if (linked.Contains(t.Slug)) continue;
                    string et = Html.Escape(t.Title);
                    int i = WholeWord(esc, et);
                    if (i < 0) continue;
                    marks.Add("<a href=\"" + WikiUrl(t) + "\">" + et + "</a>");
                    esc = esc.Substring(0, i) + "\u0001" + (marks.Count - 1) + "\u0002" + esc.Substring(i + et.Length);
                    linked.Add(t.Slug);
                }
                var o = new StringBuilder();
                for (int i = 0; i < esc.Length; i++)
                {
                    char ch = esc[i];
                    if (ch == '\u0001')
                    {
                        int end = esc.IndexOf('\u0002', i);
                        o.Append(marks[int.Parse(esc.Substring(i + 1, end - i - 1), WInv)]);
                        i = end;
                    }
                    else if (ch == '[')
                    {
                        int end = esc.IndexOf(']', i);
                        int n;
                        if (end > i && end - i <= 4 && int.TryParse(esc.Substring(i + 1, end - i - 1), NumberStyles.Integer, WInv, out n) && n >= 1 && n <= refs.Count)
                        {
                            o.Append("<a class=\"cite\" href=\"" + ArticleUrl(refs[n - 1]) + "\">[" + n + "]</a>");
                            i = end;
                        }
                        else if (end > i && end - i <= 4 && int.TryParse(esc.Substring(i + 1, end - i - 1), NumberStyles.Integer, WInv, out n)) i = end;   // a cite to an article that is gone
                        else o.Append(ch);
                    }
                    else o.Append(ch);
                }
                sb.Append("<p>").Append(o).Append("</p>");
            }
            return sb.ToString();
        }

        static int WholeWord(string s, string w)
        {
            int from = 0;
            while (true)
            {
                int i = s.IndexOf(w, from, StringComparison.Ordinal);
                if (i < 0) return -1;
                bool before = i == 0 || !char.IsLetterOrDigit(s[i - 1]);
                bool after = i + w.Length >= s.Length || !char.IsLetterOrDigit(s[i + w.Length]);
                if (before && after) return i;
                from = i + 1;
            }
        }

        string WikiImage(WikiPage p, List<Article> refs)
        {
            var ids = new HashSet<string>(p.Flights.Select(f => f.VesselId).Where(v => v != ""));
            var shot = State.Gallery.Where(g => (g.Public || g.IsPress) && (ids.Contains(g.VesselId) || p.Kind == "place" && g.Body == p.Title)).OrderByDescending(g => g.IsPress && g.Kind == "liftoff").ThenByDescending(g => g.UT)
                .Select(g => "kbshot:" + g.File).FirstOrDefault(src => ImageExists(src));
            if (shot != null) return shot;
            // No picture at all means no picture box, rather than an empty frame.
            return refs.Where(x => x.Image != "").Select(x => ImageUrl(x.Image)).LastOrDefault(src => src != "" && ImageExists(src)) ?? "";
        }

        static object Row(string label, string value, string url = "") { return new Dictionary<string, object> { { "label", label }, { "value", value }, { "url", url } }; }

        Dictionary<string, object> WikiPageModel(WikiPage p)
        {
            var m = WikiFrame(M());
            var refs = WikiRefs(p);
            var linked = new HashSet<string>();
            bool ai = p.WrittenUT >= 0;
            m["title"] = p.Title;
            m["slug"] = p.Slug;
            m["historyUrl"] = WikiUrl(p) + "/history";
            m["leadHtml"] = WikiHtml(p.Lead != "" ? p.Lead : Engine.WikiTemplateLead(p), p, refs, linked);
            var secs = ai && p.Sections.Count > 0 ? p.Sections : Engine.WikiTemplateSections(p);
            m["sections"] = secs.Select((s, i) => (object)new Dictionary<string, object> { { "n", i + 1 }, { "heading", s.Heading }, { "html", WikiHtml(s.Text, p, refs, linked) } }).ToList();
            m["toc"] = secs.Count >= 3;
            m["template"] = !ai;
            m["updated"] = Date(p.UpdatedUT);
            m["refs"] = refs.Select((a, i) => (object)new Dictionary<string, object> { { "n", i + 1 }, { "headline", a.Headline }, { "outlet", Outlets.NameOf(a.Outlet) }, { "date", Date(a.PublishedUT) }, { "url", ArticleUrl(a) } }).ToList();
            m["hasRefs"] = refs.Count > 0;
            m["flights"] = p.Flights.OrderBy(f => f.LaunchUT).Select((f, i) => (object)new Dictionary<string, object>
            {
                { "n", i + 1 }, { "date", Date(f.LaunchUT) }, { "crew", f.Crew.Count > 0 ? string.Join(", ", f.Crew.ToArray()) : "Uncrewed" },
                { "outcome", f.Outcome }, { "bad", f.Rank >= 5 }, { "good", f.Rank == 4 || f.Rank == 3 },
            }).ToList();
            m["hasFlights"] = p.Flights.Count > 0;
            string img = WikiImage(p, refs);
            m["image"] = img;
            m["caption"] = p.Kind == "craft" ? p.Title + " in flight" : p.Title;
            m["infoTitle"] = p.Kind == "craft" ? "Overview" : p.Kind == "crew" ? "Kerbal" : p.Kind == "place" ? "Exploration" : "Agency";
            m["info"] = WikiInfo(p);
            m["specs"] = WikiSpecs(p);
            m["hasSpecs"] = p.Kind == "craft" && p.Craft != null;
            m["assistantUrl"] = "agency.kb/assistant";
            // Pages that name this one.
            m["related"] = WikiPages.Where(x => x != p && (x.Facts.Any(f => f.Contains(p.Title)) || x.Flights.Any(f => f.Crew.Contains(p.Title)))).OrderBy(x => x.Title).Take(12)
                .Select(x => (object)new Dictionary<string, object> { { "title", x.Title }, { "url", WikiUrl(x) } }).ToList();
            return m;
        }

        List<object> WikiInfo(WikiPage p)
        {
            var rows = new List<object>();
            string ag = State.Profile.Name;
            var agPage = State.Wiki.Values.FirstOrDefault(x => x.Kind == "agency");
            switch (p.Kind)
            {
                case "craft":
                {
                    var c = p.Craft;
                    var first = p.Flights.OrderBy(f => f.LaunchUT).FirstOrDefault();
                    var last = p.Flights.OrderBy(f => f.LaunchUT).LastOrDefault();
                    if (c != null && c.VesselType != "") rows.Add(Row("Type", c.VesselType + (p.Flights.Any(f => f.Crew.Count > 0) ? ", crewed" : "")));
                    rows.Add(Row("Operator", ag, agPage != null ? WikiUrl(agPage) : ""));
                    if (last != null) rows.Add(Row("Status", last.Rank >= 5 ? "Lost" : last.Outcome == "In flight" || last.Outcome == "Reached orbit" || last.Outcome == "Docked" || last.Outcome.StartsWith("Orbited") || last.Outcome.StartsWith("Reached ") ? "Active" : "Retired between flights"));
                    if (first != null) rows.Add(Row("First flight", Date(first.LaunchUT)));
                    int bad = p.Flights.Count(f => f.Rank >= 5);
                    rows.Add(Row("Flights", p.Flights.Count + (bad > 0 ? " (" + bad + " failed)" : "")));
                    if (c != null && c.LaunchSite != "") rows.Add(Row("Launch site", c.LaunchSite));
                    break;
                }
                case "crew":
                {
                    CrewFame k;
                    State.Crew.TryGetValue(p.Title, out k);
                    if (k != null)
                    {
                        rows.Add(Row("Role", k.Trait != "" ? k.Trait : "Astronaut"));
                        rows.Add(Row("Agency", ag, agPage != null ? WikiUrl(agPage) : ""));
                        rows.Add(Row("Level", k.Level.ToString(WInv)));
                        rows.Add(Row("Flights", k.Flights.ToString(WInv)));
                        if (k.FirstFlightUT >= 0) rows.Add(Row("First flight", Date(k.FirstFlightUT)));
                        rows.Add(Row("Status", k.Dead ? "Died " + Date(k.DiedUT) : k.Where != "" ? k.Where : k.RosterStatus != "" ? Cap(k.RosterStatus) : "Active"));
                    }
                    var craft = WikiPages.Where(x => x.Kind == "craft" && x.Flights.Any(f => f.Crew.Contains(p.Title))).Take(4).ToList();
                    foreach (var x in craft) rows.Add(Row(craft.IndexOf(x) == 0 ? "Flew on" : "", x.Title, WikiUrl(x)));
                    break;
                }
                case "place":
                {
                    string first = p.Facts.FirstOrDefault() ?? "";
                    int ci = first.IndexOf(": ", StringComparison.Ordinal);
                    if (ci > 0) rows.Add(Row("First reached", first.Substring(0, ci)));
                    var craft = WikiPages.Where(x => x.Kind == "craft" && x.Facts.Any(f => f.Contains(" " + p.Title))).ToList();
                    rows.Add(Row("Missions", craft.Count.ToString(WInv)));
                    rows.Add(Row("Landed on", p.Facts.Any(f => f.Contains("landed on " + p.Title)) ? "Yes" : "Not yet"));
                    var crew = WikiPages.Where(x => x.Kind == "crew" && x.Facts.Any(f => f.Contains(p.Title))).Take(4).ToList();
                    foreach (var x in crew) rows.Add(Row(crew.IndexOf(x) == 0 ? "Crew who went" : "", x.Title, WikiUrl(x)));
                    break;
                }
                case "agency":
                {
                    var pr = State.Profile;
                    if (pr.ShortName != "") rows.Add(Row("Short name", pr.ShortName));
                    rows.Add(Row("Craft flown", WikiPages.Count(x => x.Kind == "craft").ToString(WInv)));
                    rows.Add(Row("Crew", WikiPages.Count(x => x.Kind == "crew").ToString(WInv)));
                    rows.Add(Row("Places reached", WikiPages.Count(x => x.Kind == "place").ToString(WInv)));
                    foreach (var x in WikiPages.Where(x => x.Kind == "place").OrderBy(x => x.CreatedUT).Take(6)) rows.Add(Row("", x.Title, WikiUrl(x)));
                    break;
                }
            }
            return rows;
        }

        static string Cap(string s) { return s == "" ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }

        List<object> WikiSpecs(WikiPage p)
        {
            var rows = new List<object>();
            var c = p.Craft;
            if (c == null) return rows;
            if (c.HeightM > 0) rows.Add(Row("Height", c.HeightM.ToString("0.#", WInv) + " m"));
            if (c.MassTons > 0) rows.Add(Row("Mass", c.MassTons.ToString("#,0.#", WInv) + " t"));
            if (c.Parts > 0) rows.Add(Row("Parts", c.Parts.ToString(WInv)));
            if (c.Stages > 0) rows.Add(Row("Stages", c.Stages.ToString(WInv)));
            if (c.Engines != "") rows.Add(Row("Engines", c.Engines));
            if (c.DeltaV > 0) rows.Add(Row("Delta-v", Math.Round(c.DeltaV).ToString("#,0", WInv) + " m/s"));
            if (c.CrewCapacity > 0) rows.Add(Row("Crew", c.CrewCapacity + (c.CrewCapacity == 1 ? " seat" : " seats")));
            return rows;
        }

        Dictionary<string, object> WikiHistoryModel(WikiPage p)
        {
            var m = WikiFrame(M());
            m["title"] = p.Title;
            m["pageUrl"] = WikiUrl(p);
            m["revs"] = p.History.AsEnumerable().Reverse().Select(h => (object)new Dictionary<string, object> { { "date", Date(h.UT) }, { "by", h.By }, { "note", h.Note }, { "agency", h.By.StartsWith("An account at") } }).ToList();
            return m;
        }

        object Entry(WikiPage p)
        {
            string lead = p.Lead != "" ? p.Lead : Engine.WikiTemplateLead(p);
            int dot = lead.IndexOf(". ", StringComparison.Ordinal);
            return new Dictionary<string, object> { { "title", p.Title }, { "url", WikiUrl(p) }, { "blurb", dot > 0 ? lead.Substring(0, dot + 1) : lead } };
        }

        Dictionary<string, object> WikiListModel(string kind)
        {
            var m = WikiFrame(M());
            var k = WikiKinds.FirstOrDefault(x => x[0] == kind);
            m["heading"] = k != null ? k[1] : "All pages";
            m["entries"] = WikiPages.Where(p => k == null || p.Kind == kind).OrderBy(p => p.Title).Select(Entry).ToList();
            m["none"] = !WikiPages.Any(p => k == null || p.Kind == kind);
            return m;
        }

        Dictionary<string, object> WikiSearchModel(string q)
        {
            var m = WikiFrame(M());
            q = (q ?? "").Trim();
            m["heading"] = "Search results for \"" + q + "\"";
            m["q"] = q;
            var hits = q == "" ? new List<WikiPage>() : WikiPages.Where(p => p.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || p.Lead.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                || p.Facts.Any(f => f.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) || p.Sections.Any(s => s.Text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderByDescending(p => p.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ThenBy(p => p.Title).ToList();
            m["entries"] = hits.Select(Entry).ToList();
            m["none"] = hits.Count == 0;
            return m;
        }

        Dictionary<string, object> WikiTimelineModel()
        {
            var m = WikiFrame(M());
            var items = WikiPages.Where(p => p.Kind == "craft").SelectMany(p => p.Flights.Select((f, i) => new { p, f, i }))
                .OrderBy(x => x.f.LaunchUT)
                .Select(x => (object)new Dictionary<string, object>
                {
                    { "date", Date(x.f.LaunchUT) }, { "title", x.p.Title }, { "url", WikiUrl(x.p) },
                    { "text", (x.i == 0 ? "first flight" : "flight " + (x.i + 1)) + ", " + (x.f.Crew.Count > 0 ? "crew " + string.Join(", ", x.f.Crew.ToArray()) : "uncrewed") + ": " + x.f.Outcome.ToLowerInvariant() },
                }).ToList();
            m["items"] = items;
            m["none"] = items.Count == 0;
            return m;
        }

        Dictionary<string, object> WikiHomeModel()
        {
            var m = WikiFrame(M());
            var craft = WikiPages.Where(p => p.Kind == "craft").ToList();
            var feat = WikiPages.Where(p => p.Kind != "agency" && p.WrittenUT >= 0).OrderByDescending(p => p.WrittenUT).FirstOrDefault()
                ?? craft.OrderByDescending(p => p.UpdatedUT).FirstOrDefault();
            if (feat != null)
            {
                var refs = WikiRefs(feat);
                string lead = feat.Lead != "" ? feat.Lead : Engine.WikiTemplateLead(feat);
                m["featured"] = new Dictionary<string, object> { { "title", feat.Title }, { "url", WikiUrl(feat) }, { "text", ResponseParser.Clip(lead, 420) + (lead.Length > 420 ? "..." : "") }, { "image", WikiImage(feat, refs) } };
            }
            m["recent"] = WikiPages.OrderByDescending(p => p.UpdatedUT).Take(6).Select(p =>
            {
                string f = p.Facts.LastOrDefault() ?? "";
                int c = f.IndexOf(": ", StringComparison.Ordinal);
                return (object)new Dictionary<string, object> { { "title", p.Title }, { "url", WikiUrl(p) }, { "text", c > 0 ? f.Substring(c + 2) : "Page updated" }, { "date", Date(p.UpdatedUT) } };
            }).ToList();
            m["firsts"] = State.Records.Skip(Math.Max(0, State.Records.Count - 6)).Select(r => (object)r).ToList();
            m["hasFirsts"] = State.Records.Count > 0;
            m["counts"] = WikiKinds.Select(k => (object)new Dictionary<string, object> { { "name", k[1] }, { "count", WikiPages.Count(p => p.Kind == k[0]) }, { "url", WikiHost + "/list/" + k[0] } }).Where(x => (int)((Dictionary<string, object>)x)["count"] > 0).ToList();
            m["empty"] = State.Wiki.Count == 0;
            return m;
        }
    }
}
