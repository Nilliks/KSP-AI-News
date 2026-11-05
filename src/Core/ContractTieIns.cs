using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KerbalNews
{
    /// <summary>A real KSP contract that a news story sparked: a rescue after a public outcry, a return to a world the
    /// public fell for, samples the science press wants, or a debris cleanup.</summary>
    public class TieIn
    {
        public string Id = "", Kind = "", Kerbal = "", Body = "", VesselName = "";
        public string Title = "", Description = "", Synopsis = "", Done = "";
        public string StoryId = "", Headline = "", Outlet = "";
        public double OfferedUT;
        public double Advance, Reward, Failure, DeadlineDays;
        public int DebrisTarget;
        /// <summary>new (waiting for the game to post it), offered, accepted, completed, failed, declined, cancelled, expired.</summary>
        public string Status = "new";
        public string ContractGuid = "";
        public bool Open { get { return Status == "new" || Status == "offered" || Status == "accepted"; } }

        public void Save(DataNode n)
        {
            n.Add("id", Id); n.Add("kind", Kind); n.AddText("kerbal", Kerbal); n.Add("body", Body); n.AddText("vessel", VesselName);
            n.AddText("title", Title); n.AddText("desc", Description); n.AddText("synopsis", Synopsis); n.AddText("done", Done);
            n.Add("story", StoryId); n.AddText("headline", Headline); n.Add("outlet", Outlet); n.Add("ut", OfferedUT);
            n.Add("advance", Advance); n.Add("reward", Reward); n.Add("failure", Failure); n.Add("deadline", DeadlineDays); n.Add("debris", DebrisTarget);
            n.Add("status", Status); n.Add("guid", ContractGuid);
        }

        public static TieIn Load(DataNode n)
        {
            return new TieIn
            {
                Id = n.Get("id", ""), Kind = n.Get("kind", ""), Kerbal = n.GetText("kerbal"), Body = n.Get("body", ""), VesselName = n.GetText("vessel"),
                Title = n.GetText("title"), Description = n.GetText("desc"), Synopsis = n.GetText("synopsis"), Done = n.GetText("done"),
                StoryId = n.Get("story", ""), Headline = n.GetText("headline"), Outlet = n.Get("outlet", ""), OfferedUT = n.GetDouble("ut"),
                Advance = n.GetDouble("advance"), Reward = n.GetDouble("reward"), Failure = n.GetDouble("failure"), DeadlineDays = n.GetDouble("deadline"),
                DebrisTarget = n.GetInt("debris"), Status = n.Get("status", "new"), ContractGuid = n.Get("guid", ""),
            };
        }
    }

    public partial class NewsEngine
    {
        public const double TieInCooldownDays = 15;
        public const int TieInMaxOpen = 3;
        /// <summary>Set by the KSP layer: posts the contract in Mission Control. False when it cannot right now (not career, wrong scene).</summary>
        public Func<TieIn, bool> PostContract = t => false;
        /// <summary>Set by the KSP layer: whether this game has contracts at all (career).</summary>
        public Func<bool> ContractsAvailable = () => false;

        static readonly Dictionary<string, double> BodyReward = new Dictionary<string, double>
        {
            { "Mun", 90000 }, { "Minmus", 80000 }, { "Duna", 320000 }, { "Ike", 300000 }, { "Eve", 420000 }, { "Gilly", 300000 },
            { "Moho", 450000 }, { "Dres", 380000 }, { "Laythe", 600000 }, { "Vall", 550000 }, { "Tylo", 650000 }, { "Bop", 500000 }, { "Pol", 500000 }, { "Eeloo", 650000 },
        };

        static double RewardFor(string body, double baseline) { double r; return BodyReward.TryGetValue(body ?? "", out r) ? r : baseline; }

        /// <summary>After a story is published: sometimes the coverage turns into a contract. No AI call; the text comes from the coverage.</summary>
        void ConsiderTieIn(Story story, List<Article> articles, double ut)
        {
            if (!Settings.ContractTieIns || articles == null || articles.Count == 0 || story.Kind == "release") return;
            bool career;
            try { career = ContractsAvailable(); } catch { career = false; }
            if (!career) return;
            if (State.TieIns.Count(t => t.Open) >= TieInMaxOpen) return;
            var lastT = State.TieIns.LastOrDefault();
            if (lastT != null && (ut - lastT.OfferedUT) / Day < TieInCooldownDays) return;
            var lead = articles.OrderByDescending(a => a.Significance).First();
            var rng = new Random(OfflineWriter.StableHash(story.Id + "tiein"));
            string text = (lead.Headline + " " + lead.Body).ToLowerInvariant();
            Func<string, bool> open = k => State.TieIns.Any(t => t.Open && t.Kind == k);
            string outletName = Outlets.NameOf(lead.Outlet);
            TieIn t2 = null;

            // 1. A rescue after a public outcry: a failure with crew still alive out there, or the press calling them stranded.
            var aliveAway = State.Crew.Values.Where(c => !c.Dead && c.RosterStatus == "assigned" && !(c.Where ?? "").Contains("Kerbin") && c.Where != "on the launch pad").ToList();
            var named = aliveAway.Where(c => story.Events.Any(e => e.Crew.Contains(c.Name)) || lead.Headline.Contains(c.Name) || lead.Body.Contains(c.Name)).ToList();
            bool outcry = story.HasEvent(EventTypes.Failure) || text.Contains("stranded") || text.Contains("trapped") || text.Contains("rescue") || text.Contains("stuck");
            if (t2 == null && !open("rescue") && named.Count > 0 && outcry && rng.NextDouble() < 0.6)
            {
                var k = named.OrderByDescending(c => c.Fame).First();
                string first = k.Name.Split(' ')[0];
                t2 = new TieIn
                {
                    Kind = "rescue", Kerbal = k.Name, VesselName = k.VesselName,
                    Title = "Bring " + k.Name + " home",
                    Description = "After " + outletName + " ran \"" + lead.Headline + "\", the public has been demanding that " + AgencyName + " bring " + k.Name + " home. Petitions, vigils outside the space center and a flood of letters later, the Planetary Council has put up emergency funds for the rescue.",
                    Synopsis = "Recover " + k.Name + " safely on Kerbin" + (k.Where != "" ? " (last reported " + k.Where + ")" : "") + ".",
                    Done = first + " is home. The crowds outside the gates are cheering.",
                    Advance = 20000, Reward = 70000 + Math.Min(60000, k.Fame * 600), Failure = 30000, DeadlineDays = 90,
                };
            }

            // 2. The public fell for a world after a big crewed landing and wants the agency back there.
            var land = story.Events.FirstOrDefault(e => e.Type == EventTypes.Landing && e.Crewed && e.Body != "" && e.Body != "Kerbin");
            if (t2 == null && !open("land") && land != null && story.Significance >= 60 && rng.NextDouble() < 0.3)
                t2 = new TieIn
                {
                    Kind = "land", Body = land.Body,
                    Title = "Back to " + land.Body + ", by popular demand",
                    Description = "\"" + lead.Headline + "\" (" + outletName + ") captured the public's imagination. Polls show kerbals want more: a sponsor consortium will pay for another crewed landing on " + land.Body + ".",
                    Synopsis = "Land a crewed vessel on " + land.Body + ".",
                    Done = "Kerbals are back on " + land.Body + ", and the sponsors are delighted.",
                    Advance = RewardFor(land.Body, 120000) * 0.2, Reward = RewardFor(land.Body, 120000), Failure = 0, DeadlineDays = 426,
                };

            // 3. The science press wants data from a world in the news.
            string body = story.Events.Select(e => e.Body).FirstOrDefault(b => b != "" && b != "Kerbin" && b != "Sun");
            bool sciencePress = articles.Any(a => a.Outlet == "science" || a.Outlet == "munmonthly");
            if (t2 == null && !open("science") && body != null && sciencePress && rng.NextDouble() < 0.2)
                t2 = new TieIn
                {
                    Kind = "science", Body = body,
                    Title = "Science from " + body + " for the journals",
                    Description = "Researchers quoted in " + outletName + " say the agency's flights to " + body + " raise more questions than they answer. A university consortium will fund any new experiment results from " + body + ".",
                    Synopsis = "Return or transmit any science from " + body + ".",
                    Done = "New data from " + body + " is on its way to the journals.",
                    Advance = 8000, Reward = RewardFor(body, 60000) * 0.35, Failure = 0, DeadlineDays = 320,
                };

            // 4. A debris scare: clean up low Kerbin orbit.
            var d = Digest();
            bool debrisTalk = text.Contains("debris") || text.Contains("junk");
            if (t2 == null && !open("debris") && d != null && d.Debris >= 25 && debrisTalk && rng.NextDouble() < 0.4)
                t2 = new TieIn
                {
                    Kind = "debris", DebrisTarget = Math.Max(3, Math.Min(10, d.Debris / 8)),
                    Title = "Clean up Kerbin orbit",
                    Description = "After \"" + lead.Headline + "\" (" + outletName + "), worried kerbals have flooded the Planetary Council with complaints about space junk. The Council will pay the agency to bring some of it down.",
                    Synopsis = "Remove " + Math.Max(3, Math.Min(10, d.Debris / 8)) + " pieces of debris from orbit around Kerbin.",
                    Done = "There is a little less junk over Kerbin, and the Council has noticed.",
                    Advance = 10000, Reward = 25000 + Math.Max(3, Math.Min(10, d.Debris / 8)) * 6000, Failure = 0, DeadlineDays = 213,
                };

            if (t2 == null) return;
            t2.Id = State.NewId("ti");
            t2.StoryId = story.Id; t2.Headline = lead.Headline; t2.Outlet = lead.Outlet; t2.OfferedUT = ut;
            State.TieIns.Add(t2);
            if (State.TieIns.Count > 40) State.TieIns.RemoveAt(0);
            TickTieIns(ut);
        }

        /// <summary>Posts new tie-ins once the game can (contracts exist only in career, and the system must be loaded).</summary>
        public void TickTieIns(double ut)
        {
            // Offers expire in Mission Control after a while; a lapsed one must not block new tie-ins forever.
            foreach (var t in State.TieIns)
            {
                double age = (ut - t.OfferedUT) / Day;
                if (t.Status == "offered" && age > 31) t.Status = "expired";
                else if (t.Status == "accepted" && age > t.DeadlineDays + 31) t.Status = "expired";
                else if (t.Status == "new" && age > 60) t.Status = "expired";
            }
            foreach (var t in State.TieIns.Where(x => x.Status == "new").ToList())
            {
                bool ok;
                try { ok = PostContract(t); } catch (Exception e) { LastError = "Contract: " + e.Message; ok = false; }
                if (ok) t.Status = "offered";
            }
        }

        /// <summary>The KSP layer reports what happened to a tie-in contract.</summary>
        public void TieInChanged(string id, string status)
        {
            var t = State.TieIns.FirstOrDefault(x => x.Id == id);
            if (t != null && !string.IsNullOrEmpty(status)) t.Status = status;
        }

        /// <summary>Contract news names the outcry that started it.</summary>
        void AnnotateContractEvent(NewsEvent e)
        {
            if (e == null || (e.Type != EventTypes.ContractDone && e.Type != EventTypes.ContractFailed && e.Type != EventTypes.ContractAccepted)) return;
            var t = State.TieIns.FirstOrDefault(x => x.Title == e.Detail);
            if (t == null) return;
            e.Detail += ". The contract was posted after public pressure that followed " + Outlets.NameOf(t.Outlet) + "'s story \"" + t.Headline + "\"";
            if (e.Type == EventTypes.ContractDone) t.Status = "completed";
            else if (e.Type == EventTypes.ContractFailed) t.Status = "failed";
            else t.Status = "accepted";
        }
    }
}
