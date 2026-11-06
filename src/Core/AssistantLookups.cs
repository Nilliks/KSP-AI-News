using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KerbalNews
{
    public partial class NewsEngine
    {

        // ------------------------------------------------------------------ lookups

        string Lookup(string cmd, Dictionary<string, object> args, double ut)
        {
            try
            {
                var sb = new StringBuilder();
                switch (cmd)
                {
                    case "news":
                    {
                        string q = Arg(args, "query").ToLowerInvariant();
                        var o = FindOutlet(Arg(args, "outlet"));
                        int limit = Math.Max(1, Math.Min(25, (int)ArgNum(args, "limit", 15)));
                        var list = State.PublishedArticles(ut).Where(a => o == null || a.Outlet == o.Id);
                        if (q != "") list = list.Where(a => Words(q).Any(w => (a.Headline + " " + a.Subhead + " " + a.Body).ToLowerInvariant().Contains(w)));
                        foreach (var a in list.Take(limit))
                            sb.AppendLine(a.Id + " | " + Outlets.NameOf(a.Outlet) + " | " + Clock.FormatDate(a.PublishedUT) + " | " + a.Kind + " | " + a.Headline + " | " + a.Comments.Count + " comments");
                        return sb.Length == 0 ? "No matching articles." : sb.ToString();
                    }
                    case "read":
                    {
                        var a = FindArticle(Arg(args, "article"), ut);
                        if (a == null) return "No such article.";
                        sb.AppendLine(a.Id + " | " + Outlets.NameOf(a.Outlet) + " | " + Clock.FormatDate(a.PublishedUT) + " | " + a.Kind);
                        sb.AppendLine("Headline: " + a.Headline);
                        if (a.Subhead != "") sb.AppendLine("Subhead: " + a.Subhead);
                        sb.AppendLine(ResponseParser.Clip(Web.Sites.Unlinked(State, a.Body), 1500));
                        foreach (var c in a.Comments.OrderByDescending(c => c.Likes).Take(5)) sb.AppendLine("Comment by " + c.Author + ": " + ResponseParser.Clip(Web.Sites.Unlinked(State, c.Text), 200));
                        return sb.ToString();
                    }
                    case "forum":
                    {
                        string q = Arg(args, "query").ToLowerInvariant();
                        var list = State.Forum.Where(t => t.UT <= ut).OrderByDescending(t => t.UT).AsEnumerable();
                        if (q != "") list = list.Where(t => Words(q).Any(w => (t.Title + " " + t.Body).ToLowerInvariant().Contains(w)));
                        foreach (var t in list.Take(12)) sb.AppendLine(t.Id + " | b/" + t.Board + " | " + t.Author + " | " + t.Title + " | " + t.Comments.Count + " replies");
                        return sb.Length == 0 ? "No matching threads." : sb.ToString();
                    }
                    case "finances":
                    {
                        sb.AppendLine("Funds: " + FundsText());
                        foreach (var d in State.Deals.Where(d => d.Amount != 0).OrderByDescending(d => d.UT).Take(12))
                            sb.AppendLine(Clock.FormatDate(d.UT) + ": " + (d.Amount > 0 ? "+" : "-") + "√" + Money(Math.Abs(d.Amount)) + " " + d.Summary);
                        return sb.ToString();
                    }
                    case "ledger":
                    {
                        foreach (var d in State.Deals.OrderByDescending(d => d.UT).Take(25))
                            sb.AppendLine(d.Id + " | " + Clock.FormatDate(d.UT) + " | " + d.Kind + " | " + d.Summary + " | " + d.Status + (d.Outcome != "" ? ": " + d.Outcome : "") + (d.Secret ? (d.Exposed ? " | EXPOSED" : " | secret") : ""));
                        return sb.Length == 0 ? "The ledger is empty." : sb.ToString();
                    }
                    case "vessels":
                    {
                        foreach (var t in State.Tracked.Values.OrderByDescending(t => t.LaunchUT).Take(15))
                            sb.AppendLine(t.VesselId + " | " + (t.Craft != null ? t.Craft.VesselName : "?") + " | launched " + Clock.FormatDate(t.LaunchUT) + (State.IsSilent(t.VesselId) ? " | silent" : "") + (t.History.Count > 0 ? " | " + t.History.Last() : ""));
                        return sb.Length == 0 ? "No tracked vessels yet." : sb.ToString();
                    }
                    case "gallery":
                    {
                        foreach (var g in State.Gallery.OrderByDescending(g => g.UT).Take(15))
                            sb.AppendLine(g.Id + " | " + (g.Public ? "public" : "private") + " | " + (g.Caption != "" ? g.Caption : g.VesselName));
                        return sb.Length == 0 ? "No photos." : sb.ToString();
                    }
                    case "mood":
                    {
                        sb.AppendLine(State.Mood.Describe());
                        var p = State.Polls.LastOrDefault();
                        if (p != null) sb.AppendLine("Latest poll: " + p.Summary(AgencyName));
                        return sb.ToString();
                    }
                    case "outlets":
                        foreach (var o in Outlets.Enabled) sb.AppendLine(o.Id + " | " + o.Name + " | " + o.Style + " " + (o.Bias ?? ""));
                        return sb.ToString();
                    case "reporters":
                    {
                        foreach (var o in Outlets.Enabled)
                        {
                            List<string> reps;
                            var names = State.Reporters.TryGetValue(o.Id, out reps) ? reps : new List<string>();
                            var ties = State.Ties.Where(t => t.Outlet == o.Id).ToList();
                            var all = names.Concat(ties.Select(t => t.Name)).Distinct().ToList();
                            if (all.Count == 0) continue;
                            sb.AppendLine(o.Name + ": " + string.Join("; ", all.Select(n => { var t = ties.FirstOrDefault(x => x.Name == n); return n + (t == null ? " (no dealings yet)" : " (" + t.Feeling + ", " + t.Score + (t.Notes.Count > 0 ? ": " + string.Join(", ", t.Notes.ToArray()) : "") + ")"); }).ToArray()));
                        }
                        return sb.Length == 0 ? "No named reporters yet." : sb.ToString();
                    }
                    case "crew":
                    {
                        foreach (var c in State.Crew.Values.Where(x => x.OnRoster || x.Flights > 0).OrderByDescending(x => x.Fame).Take(30))
                        {
                            sb.Append(c.Name + ": " + (c.Trait != "" ? c.Trait + " L" + c.Level : "crew") + ", " + (c.Dead ? "deceased" : c.RosterStatus == "assigned" ? "aboard " + c.VesselName + (c.Where != "" ? " (" + c.Where + ")" : "") : "at KSC") + ", " + c.Flights + " flights, " + c.Tier);
                            var up = UpcomingFor(c.Name);
                            if (up.Count > 0) sb.Append("; booked: " + string.Join(", ", up.Select(x => x.Kind + " " + x.Id + " on " + Clock.FormatDate(x.AtUT) + (x.Coaching.Count > 0 ? " (coached)" : "")).ToArray()));
                            sb.AppendLine();
                        }
                        return sb.Length == 0 ? "No crew on record yet." : sb.ToString();
                    }
                    case "orders":
                        for (int i = 0; i < State.Orders.Count; i++) sb.AppendLine((i + 1) + ". " + State.Orders[i].Text + " (given " + Clock.FormatDate(State.Orders[i].UT) + ")");
                        return sb.Length == 0 ? "No standing orders." : sb.ToString();
                    case "lore":
                    {
                        var pr = State.Profile;
                        sb.AppendLine("Founding: " + pr.Founding + "\nMission: " + pr.Mission + "\nPolitics: " + pr.Politics + "\nPeople: " + pr.KeyPeople + "\nWorld: " + pr.WorldLore);
                        foreach (var l in State.Lore.Skip(Math.Max(0, State.Lore.Count - 10))) sb.AppendLine(Clock.FormatDate(l.UT) + ": " + l.Text);
                        return sb.ToString();
                    }
                }
                return "Unknown lookup \"" + cmd + "\".";
            }
            catch (Exception e) { return "Lookup failed: " + e.Message; }
        }

        static IEnumerable<string> Words(string q) { return q.Split(new[] { ' ', ',', '.', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 2); }
    }
}
