using System;
using System.Collections.Generic;
using System.Linq;

namespace KerbalNews
{
    /// <summary>One batch of science data that reached Kerbin (transmitted or recovered).</summary>
    public class ScienceResult
    {
        public double UT;
        public string SubjectId = "";
        /// <summary>The game's own title, e.g. "Surface Sample from Mun Midlands".</summary>
        public string Title = "";
        public string Experiment = "";
        public string Body = "";
        public string Situation = "";
        public string Biome = "";
        public double Amount;
        /// <summary>How much of this subject's value has now been earned, 0-1. Low on a first look, near 1 on repeats.</summary>
        public double Completion;
        public bool Transmitted;
        public string VesselId = "";
        public string VesselName = "";
        public List<string> Crew = new List<string>();
        /// <summary>The journal paper that wrote it up ("" while waiting).</summary>
        public string PaperStoryId = "";

        public void Save(DataNode n)
        {
            n.Add("ut", UT);
            n.AddText("subject", SubjectId);
            n.AddText("title", Title);
            n.Add("experiment", Experiment);
            n.Add("body", Body);
            n.Add("situation", Situation);
            n.AddText("biome", Biome);
            n.Add("amount", Amount);
            n.Add("completion", Completion);
            n.Add("transmitted", Transmitted);
            n.Add("vesselId", VesselId);
            n.AddText("vesselName", VesselName);
            foreach (var c in Crew) n.AddText("crew", c);
            if (PaperStoryId != "") n.Add("paper", PaperStoryId);
        }

        public static ScienceResult Load(DataNode n)
        {
            var r = new ScienceResult
            {
                UT = n.GetDouble("ut"), SubjectId = n.GetText("subject"), Title = n.GetText("title"), Experiment = n.Get("experiment", ""),
                Body = n.Get("body", ""), Situation = n.Get("situation", ""), Biome = n.GetText("biome"), Amount = n.GetDouble("amount"),
                Completion = n.GetDouble("completion"), Transmitted = n.GetBool("transmitted"), VesselId = n.Get("vesselId", ""),
                VesselName = n.GetText("vesselName"), PaperStoryId = n.Get("paper", ""),
            };
            foreach (var c in n.GetAll("crew")) r.Crew.Add(TextCodec.Decode(c));
            return r;
        }

        static readonly string[] Situations = { "SrfLanded", "SrfSplashed", "FlyingLow", "FlyingHigh", "InSpaceLow", "InSpaceHigh" };

        /// <summary>Splits a stock subject id ("surfaceSample@MunSrfLandedMidlands") into experiment, body, situation and biome.</summary>
        public static void ParseSubject(string id, IEnumerable<string> bodies, out string experiment, out string body, out string situation, out string biome)
        {
            experiment = body = situation = biome = "";
            if (string.IsNullOrEmpty(id)) return;
            int at = id.IndexOf('@');
            experiment = at < 0 ? id : id.Substring(0, at);
            if (at < 0) return;
            string rest = id.Substring(at + 1);
            body = (bodies ?? Enumerable.Empty<string>()).Where(b => !string.IsNullOrEmpty(b) && rest.StartsWith(b, StringComparison.Ordinal)).OrderByDescending(b => b.Length).FirstOrDefault() ?? "";
            rest = rest.Substring(body.Length);
            foreach (var s in Situations)
                if (rest.StartsWith(s, StringComparison.Ordinal)) { situation = s; biome = rest.Substring(s.Length); return; }
            biome = rest;
        }
    }

    public partial class NewsEngine
    {
        public const int MaxScienceResults = 400;

        /// <summary>Records science as it comes home. No AI call: results wait for the next journal issue.</summary>
        public ScienceResult RecordScience(ScienceResult r)
        {
            if (r == null || r.Amount <= 0) return null;
            // A recovery delivers many subjects at once; merge exact repeats from the same vessel on the same day.
            var same = State.Science.LastOrDefault(x => x.SubjectId == r.SubjectId && x.VesselId == r.VesselId && Math.Abs(x.UT - r.UT) < Day);
            if (same != null) { same.Amount += r.Amount; same.Completion = Math.Max(same.Completion, r.Completion); return same; }
            State.Science.Add(r);
            if (State.Science.Count > MaxScienceResults) State.Science.RemoveRange(0, State.Science.Count - MaxScienceResults);
            return r;
        }

        /// <summary>Science waiting to be written up, oldest first.</summary>
        public List<ScienceResult> UnpublishedScience() { return State.Science.Where(x => x.PaperStoryId == "").OrderBy(x => x.UT).ToList(); }
    }
}
