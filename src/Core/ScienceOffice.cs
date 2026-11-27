using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>The science team's reading of one returned result, and what the director chose to do with it.</summary>
    public class ScienceFinding
    {
        public string Id = "";
        public double UT;
        public string ResultKey = "";
        public string Subject = "";        // "Surface Sample from Mun Midlands"
        public string Body = "";
        public string VesselId = "";
        public string Scientist = "";
        public string Field = "";
        public string Title = "";
        public string Finding = "";
        public string Unknowns = "";
        public string NextStep = "";
        public string Confidence = "";
        public int Importance = 2;
        public string Question = "";
        /// <summary>"new" (only the director has seen it), "journal", "press" or "internal".</summary>
        public string Status = "new";
        public string ArticleId = "";
        public string ReleaseId = "";
        public double PublishUT;
        public bool Offline;

        public void Save(DataNode n)
        {
            n.Add("id", Id); n.Add("ut", UT); n.AddText("result", ResultKey); n.AddText("subject", Subject); n.Add("body", Body); n.Add("vessel", VesselId);
            n.AddText("scientist", Scientist); n.AddText("field", Field); n.AddText("title", Title); n.AddText("finding", Finding); n.AddText("unknowns", Unknowns);
            n.AddText("next", NextStep); n.Add("confidence", Confidence); n.Add("importance", Importance); n.AddText("question", Question); n.Add("status", Status);
            n.Add("article", ArticleId); n.Add("release", ReleaseId); n.Add("publish", PublishUT); n.Add("offline", Offline);
        }

        public static ScienceFinding Load(DataNode n)
        {
            return new ScienceFinding
            {
                Id = n.Get("id", ""), UT = n.GetDouble("ut"), ResultKey = n.GetText("result"), Subject = n.GetText("subject"), Body = n.Get("body", ""), VesselId = n.Get("vessel", ""),
                Scientist = n.GetText("scientist"), Field = n.GetText("field"), Title = n.GetText("title"), Finding = n.GetText("finding"), Unknowns = n.GetText("unknowns"),
                NextStep = n.GetText("next"), Confidence = n.Get("confidence", ""), Importance = n.GetInt("importance", 2), Question = n.GetText("question"), Status = n.Get("status", "new"),
                ArticleId = n.Get("article", ""), ReleaseId = n.Get("release", ""), PublishUT = n.GetDouble("publish"), Offline = n.GetBool("offline"),
            };
        }
    }

    public class Scientist { public string Name, Field; }

    public partial class NewsEngine
    {
        public static readonly List<Scientist> ScienceTeam = new List<Scientist>
        {
            new Scientist { Name = "Dr. Linmy Kerman", Field = "planetary geology" },
            new Scientist { Name = "Dr. Bobak Kerman", Field = "atmospheres and climate" },
            new Scientist { Name = "Dr. Gene Kerman", Field = "biology and crew health" },
            new Scientist { Name = "Dr. Wernher Kerman", Field = "space physics" },
        };

        /// <summary>Who on the team reads a given experiment.</summary>
        public static Scientist ScientistFor(string experiment, string situation)
        {
            string e = (experiment ?? "").ToLowerInvariant();
            if (e.Contains("sample") || e.Contains("seismic") || e.Contains("surface") || e.Contains("drill") || e.Contains("rock") || e.Contains("geo")) return ScienceTeam[0];
            if (e.Contains("temperature") || e.Contains("barometer") || e.Contains("pressure") || e.Contains("atmosphere") || e.Contains("weather") || e.Contains("climate")) return ScienceTeam[1];
            if (e.Contains("goo") || e.Contains("bio") || e.Contains("crew") || e.Contains("eva") || e.Contains("habitat") || e.Contains("health") || e.Contains("materials"))
                return (e.Contains("crew") || e.Contains("eva")) && (situation ?? "").StartsWith("Srf") ? ScienceTeam[0] : ScienceTeam[2];
            return ScienceTeam[3];   // gravity, magnetometer, radiation, telescopes and anything unknown
        }

        public static string ResultKey(ScienceResult r) { return r.SubjectId + "|" + r.VesselId + "|" + Math.Round(r.UT).ToString(CultureInfo.InvariantCulture); }

        public ScienceResult ResultByKey(string key) { return State.Science.FirstOrDefault(r => ResultKey(r) == key); }

        public ScienceFinding FindingFor(ScienceResult r) { string k = ResultKey(r); return State.Findings.LastOrDefault(f => f.ResultKey == k); }

        /// <summary>True when this is the first result from that body the agency has ever brought home.</summary>
        public bool FirstFromBody(ScienceResult r)
        {
            if (r.Body == "" || r.Body == "Kerbin") return false;
            return !State.Science.Any(x => x != r && x.Body == r.Body && x.UT < r.UT);
        }

        public bool KerbalismScience { get { return Settings.Mods != null && Settings.Mods.Any(m => m.Installed && (m.Folder + " " + m.Name).ToLowerInvariant().Contains("kerbalism")); } }

        public bool SciBusy;

        /// <summary>The team reads the chosen results: one AI call for all of them (templates offline). Each result gets its own finding.</summary>
        public bool SciAnalyse(IEnumerable<string> keys, string question, double ut)
        {
            var results = (keys ?? new string[0]).Select(ResultByKey).Where(r => r != null && FindingFor(r) == null).Distinct().Take(8).ToList();
            if (results.Count == 0 || SciBusy) return false;
            question = ResponseParser.Clip((question ?? "").Trim(), 300);
            if (!Settings.CanCallAi)
            {
                foreach (var r in results) State.Findings.Add(OfflineFinding(r, question, ut));
                return true;
            }
            SciBusy = true;
            var sb = new StringBuilder();
            sb.AppendLine("You are the science team of " + AgencyName + " in Kerbal Space Program (KSP1): " + string.Join("; ", ScienceTeam.Select(s => s.Name + " (" + s.Field + ")").ToArray()) + ".");
            sb.AppendLine("You interpret experiment results the agency has brought home, for the director, like real scientists writing an internal note.");
            sb.AppendLine("RULES:");
            sb.AppendLine("- Base each finding ONLY on the data given: the experiment, where it was taken, how much of the subject is now known, and the game's own result text. Read the result text as the observation. Do not invent measurements, numbers or discoveries it does not support.");
            sb.AppendLine("- Separate what the data shows from what it might mean, and say plainly what it cannot tell us yet.");
            sb.AppendLine("- Importance 1-5: 1 routine repeat, 2 useful, 3 notable, 4 a first or a real surprise, 5 historic (first data ever from a new world, or something that changes plans). Repeats of a subject that is already mostly known are 1-2.");
            sb.AppendLine("- Confidence: low (a hint, little data), moderate, or high (clear and repeated).");
            sb.AppendLine("- Plain, careful language, short sentences. Kerbal names end in Kerman. Earlier findings are the team's memory: build on them and do not contradict them without saying so.");
            if (KerbalismScience) sb.AppendLine("This save uses Kerbalism: experiments run over time and send data in parts, so a result may be partial; radiation belts, magnetospheres and storms are real phenomena here.");
            var lore = State.Profile;
            if (lore != null && lore.ShortName != "") sb.AppendLine("Agency: " + AgencyName + ".");
            sb.AppendLine("Reply with JSON only: {\"findings\":[{\"result\":1,\"scientist\":\"one of the team\",\"title\":\"a short paper-style title\",\"finding\":\"2-4 sentences\",\"unknowns\":\"1-2 sentences\",\"next_step\":\"one sentence\",\"confidence\":\"low|moderate|high\",\"importance\":3}]} with one entry per result, in order.");
            var u = new StringBuilder();
            u.AppendLine("Results to interpret:");
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                u.AppendLine((i + 1) + ". " + (r.Title != "" ? r.Title : r.SubjectId) + " | experiment " + r.Experiment + ", body " + (r.Body != "" ? r.Body : "?") + ", situation " + r.Situation + (r.Biome != "" ? ", biome " + r.Biome : "")
                    + " | " + (r.Transmitted ? "transmitted" : "recovered") + " from " + (r.VesselName != "" ? r.VesselName : "an agency craft") + (r.Crew.Count > 0 ? " with " + string.Join(", ", r.Crew.ToArray()) : "")
                    + " on " + Clock.FormatDate(r.UT) + " | " + r.Amount.ToString("0.0", CultureInfo.InvariantCulture) + " science, " + Math.Round(r.Completion * 100) + "% of this subject now known"
                    + (FirstFromBody(r) ? " | FIRST data ever from " + r.Body : "")
                    + (r.ResultText != "" ? " | result text: \"" + ResponseParser.Clip(r.ResultText, 400) + "\"" : " | no result text"));
            }
            var past = State.Findings.Where(f => f.Finding != "").Skip(Math.Max(0, State.Findings.Count - 6)).ToList();
            if (past.Count > 0)
            {
                u.AppendLine("Earlier findings (the team's memory):");
                foreach (var f in past) u.AppendLine("- " + f.Title + ": " + ResponseParser.Clip(f.Finding, 220));
            }
            if (question != "") u.AppendLine("The director asks: " + question);
            AskJson(Settings.Writer, sb.ToString(), u.ToString(), Math.Min(4000, 500 + results.Count * 350), 0, (root, err, text, res) =>
            {
                SciBusy = false;
                var arr = root != null ? Json.Arr(Json.Get(root, "findings")) : null;
                if (arr == null) { LastError = err ?? "The science team's reply could not be read"; RaiseCommunity(); return; }
                for (int i = 0; i < results.Count; i++)
                {
                    var o = arr.FirstOrDefault(x => (int)Json.Num(x, "result", -1) == i + 1) ?? (i < arr.Count ? arr[i] : null);
                    var f = o != null ? FindingFrom(results[i], o, question, ut) : OfflineFinding(results[i], question, ut);
                    State.Findings.Add(f);
                }
                RaiseCommunity();
            });
            return true;
        }

        ScienceFinding NewFinding(ScienceResult r, string question, double ut)
        {
            var who = ScientistFor(r.Experiment, r.Situation);
            return new ScienceFinding
            {
                Id = State.NewId("sf"), UT = ut, ResultKey = ResultKey(r), Subject = r.Title != "" ? r.Title : r.SubjectId, Body = r.Body, VesselId = r.VesselId,
                Scientist = who.Name, Field = who.Field, Question = question,
            };
        }

        ScienceFinding FindingFrom(ScienceResult r, object o, string question, double ut)
        {
            var f = NewFinding(r, question, ut);
            string name = Json.Str(o, "scientist", "").Trim();
            var member = ScienceTeam.FirstOrDefault(s => name != "" && (s.Name.Contains(name) || name.Contains(s.Name.Replace("Dr. ", ""))));
            if (member != null) { f.Scientist = member.Name; f.Field = member.Field; }
            f.Title = ResponseParser.Clip(Json.Str(o, "title", "").Trim(), 160);
            f.Finding = ResponseParser.Clip(Json.Str(o, "finding", "").Trim(), 1200);
            f.Unknowns = ResponseParser.Clip(Json.Str(o, "unknowns", "").Trim(), 500);
            f.NextStep = ResponseParser.Clip(Json.Str(o, "next_step", "").Trim(), 300);
            string c = Json.Str(o, "confidence", "moderate").Trim().ToLowerInvariant();
            f.Confidence = c == "low" || c == "high" ? c : "moderate";
            f.Importance = Math.Max(1, Math.Min(5, (int)Math.Round(Json.Num(o, "importance", 2))));
            if (f.Title == "") f.Title = f.Subject;
            if (f.Finding == "") return OfflineFinding(r, question, ut);
            return f;
        }

        /// <summary>A careful note from templates when there is no AI: it restates the observation and what it can't tell yet.</summary>
        ScienceFinding OfflineFinding(ScienceResult r, string question, double ut)
        {
            var f = NewFinding(r, question, ut);
            f.Offline = true;
            bool first = FirstFromBody(r);
            string where = (r.Body != "" ? r.Body : "the target") + (r.Biome != "" ? " (" + r.Biome + ")" : "");
            f.Title = (first ? "First data from " : "New data from ") + where + ": " + (r.Experiment != "" ? r.Experiment : "experiment");
            f.Finding = (r.ResultText != "" ? "The record reads: \"" + r.ResultText.Trim() + "\" " : "")
                + "This adds " + r.Amount.ToString("0.0", CultureInfo.InvariantCulture) + " units of data; " + Math.Round(r.Completion * 100) + "% of what this experiment can tell us here is now known.";
            f.Unknowns = r.Completion < 0.5 ? "This is a first look; more data from the same place is needed before drawing conclusions." : "One site and one instrument: we cannot yet say how typical this is.";
            f.NextStep = r.Completion < 0.9 ? "Repeat the measurement to complete the record." : "Take the same experiment somewhere new and compare.";
            f.Confidence = r.Completion >= 0.9 ? "moderate" : "low";
            f.Importance = first ? 4 : r.Completion > 0.95 && r.Amount < 2 ? 1 : 2;
            return f;
        }

        /// <summary>
        /// What the director does with a finding. "journal": a paper in the Kerbin Science Review after a few days of review,
        /// and the news picks up the notable ones. "press": an agency press release out the next day. "internal": nothing public.
        /// </summary>
        public bool SciPublish(string findingId, string how, double ut)
        {
            var f = State.Findings.FirstOrDefault(x => x.Id == findingId);
            if (f == null || f.Status != "new") return false;
            if (how == "internal") { f.Status = "internal"; return true; }
            if (how == "press")
            {
                string body = f.Finding + (f.NextStep != "" ? " Next, our scientists want to: " + LowerFirst(f.NextStep) : "");
                var rel = PublishRelease("announcement", "Science update: " + f.Title, body + " (" + f.Scientist + ", " + f.Field + ")", f.VesselId, null, ut);
                if (rel == null) return false;
                f.Status = "press"; f.ReleaseId = rel.Id; f.PublishUT = ut;
                return true;
            }
            if (how != "journal") return false;
            double when = ut + 2.5 * Day;   // peer review
            var a = new Article
            {
                Id = State.NewId("a"), Outlet = Outlets.Get("science") != null ? "science" : Outlets.All[0].Id, Kind = "paper", Headline = f.Title,
                Subhead = f.Finding.Length > 240 ? ResponseParser.Clip(f.Finding, 237) + "..." : f.Finding,
                Body = PaperBody(f), Reporter = f.Scientist.Replace("Dr. ", "") + ", " + AgencyName + " Science Office", PublishedUT = when,
                Significance = 15 + f.Importance * 10, Offline = f.Offline, Model = f.Offline ? "" : Settings.WriterModel,
            };
            a.Tags.Add("science"); a.Tags.Add("research paper");
            if (f.Body != "") a.Tags.Add(f.Body);
            State.Articles.Add(a);
            f.Status = "journal"; f.ArticleId = a.Id; f.PublishUT = when;
            // Notable papers make the news a little later; routine ones only interest specialists.
            if (f.Importance >= 3)
            {
                string outlets = "herald" + (f.Importance >= 4 ? ",observer,munmonthly" : "") + (f.Importance >= 5 ? ",kraken,inquirer" : "");
                outlets = string.Join(",", outlets.Split(',').Where(o => Settings.SiteOn(o)).ToArray());
                if (outlets != "")
                    Tip(outlets, "The agency's scientists published a peer-reviewed paper in the Kerbin Science Review: \"" + f.Title + "\". " + f.Finding + " Still unknown: " + f.Unknowns
                        + " Lead author " + f.Scientist + " (" + f.Field + "). Confidence: " + f.Confidence + ".",
                        "Agency scientists report: " + f.Title, f.Finding + "\n\n" + "What they can't say yet: " + f.Unknowns, 20 + f.Importance * 9, when + 0.5 * Day);
            }
            return true;
        }

        static string LowerFirst(string s) { return string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1); }

        string PaperBody(ScienceFinding f)
        {
            var r = ResultByKey(f.ResultKey);
            var sb = new StringBuilder();
            sb.Append("Abstract. ").Append(f.Finding).Append("\n\n");
            if (r != null)
                sb.Append("What was measured. ").Append(r.Title != "" ? r.Title : r.SubjectId).Append(", ").Append(r.Transmitted ? "transmitted from " : "recovered with ").Append(r.VesselName != "" ? r.VesselName : "an agency craft")
                  .Append(" on ").Append(Clock.FormatDate(r.UT)).Append(": ").Append(r.Amount.ToString("0.0", CultureInfo.InvariantCulture)).Append(" units of data, ")
                  .Append(Math.Round(r.Completion * 100)).Append("% of this subject.").Append(r.ResultText != "" ? " Field note: \"" + r.ResultText.Trim() + "\"" : "").Append("\n\n");
            if (f.Unknowns != "") sb.Append("Limits. ").Append(f.Unknowns).Append("\n\n");
            if (f.NextStep != "") sb.Append("Next steps. ").Append(f.NextStep).Append("\n\n");
            sb.Append("Confidence: ").Append(f.Confidence).Append(". Peer reviewed by the Kerbin Science Review.");
            return sb.ToString();
        }
    }
}
