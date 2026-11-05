using System;
using System.Collections.Generic;
using System.Linq;
using Contracts;
using UnityEngine;

namespace KerbalNews.KSP
{
    /// <summary>
    /// A real contract posted because of a news story. The contract system never generates it at random: Generate only
    /// succeeds while <see cref="Next"/> holds a tie-in from the engine. Reputation is always zero (the mod has no reputation effects).
    /// </summary>
    public class KnnContract : Contract
    {
        internal static TieIn Next;
        string tieId = "", kind = "", kerbal = "", body = "", title = "", desc = "", synopsis = "", done = "";
        int debris;

        protected override bool Generate()
        {
            var t = Next;
            if (t == null) return false;
            Next = null;
            tieId = t.Id; kind = t.Kind; kerbal = t.Kerbal; body = t.Body; title = t.Title; desc = t.Description; synopsis = t.Synopsis; done = t.Done; debris = t.DebrisTarget;
            SetFunds((float)t.Advance, (float)t.Reward, (float)t.Failure, null);
            SetReputation(0f, 0f, null);
            SetScience(0f, null);
            SetExpiry(10, 30);
            SetDeadlineDays((float)t.DeadlineDays, null);
            AddParameters();
            return true;
        }

        void AddParameters()
        {
            switch (kind)
            {
                case "rescue": AddParameter(new KnnRescueParameter { Kerbal = kerbal }, "rescue"); break;
                case "land": AddParameter(new KnnLandParameter { Body = body }, "land"); break;
                case "science": AddParameter(new KnnScienceParameter { Body = body }, "science"); break;
                case "debris": AddParameter(new KnnDebrisParameter { Target = debris }, "debris"); break;
            }
        }

        public string TieId { get { return tieId; } }
        public override bool CanBeCancelled() { return true; }
        public override bool CanBeDeclined() { return true; }
        public override bool MeetRequirements() { return true; }
        protected override string GetHashString() { return "knn-" + tieId; }
        protected override string GetTitle() { return title; }
        protected override string GetDescription() { return desc; }
        protected override string GetSynopsys() { return synopsis; }
        protected override string MessageCompleted() { return done; }

        protected override void OnSave(ConfigNode n)
        {
            n.AddValue("tie", tieId); n.AddValue("kind", kind); n.AddValue("kerbal", kerbal); n.AddValue("body", body); n.AddValue("debris", debris);
            n.AddValue("title", TextCodec.Encode(title)); n.AddValue("desc", TextCodec.Encode(desc)); n.AddValue("synopsis", TextCodec.Encode(synopsis)); n.AddValue("done", TextCodec.Encode(done));
        }

        protected override void OnLoad(ConfigNode n)
        {
            tieId = n.GetValue("tie") ?? ""; kind = n.GetValue("kind") ?? ""; kerbal = n.GetValue("kerbal") ?? ""; body = n.GetValue("body") ?? "";
            int.TryParse(n.GetValue("debris") ?? "0", out debris);
            title = TextCodec.Decode(n.GetValue("title") ?? ""); desc = TextCodec.Decode(n.GetValue("desc") ?? "");
            synopsis = TextCodec.Decode(n.GetValue("synopsis") ?? ""); done = TextCodec.Decode(n.GetValue("done") ?? "");
        }

        /// <summary>Posts the tie-in in Mission Control. False when this game has no contract system right now.</summary>
        public static bool Post(TieIn t)
        {
            if (!Available()) return false;
            if (ContractSystem.Instance.Contracts.OfType<KnnContract>().Any(x => x.TieId == t.Id)) return true;
            Next = t;
            Contract c;
            try { c = Contract.Generate(typeof(KnnContract), ContractPrestige.Significant, t.Id.GetHashCode(), State.Offered); }
            finally { Next = null; }
            if (c == null) return false;
            ContractSystem.Instance.Contracts.Add(c);
            GameEvents.Contract.onOffered.Fire(c);
            t.ContractGuid = c.ContractGuid.ToString();
            Debug.Log("[KerbalNews] Posted contract \"" + t.Title + "\"");
            return true;
        }

        public static bool Available()
        {
            return HighLogic.CurrentGame != null && HighLogic.CurrentGame.Mode == Game.Modes.CAREER && ContractSystem.Instance != null && ContractSystem.Instance.Contracts != null;
        }
    }

    /// <summary>Done when the kerbal is safely back on the roster; failed if they die.</summary>
    public class KnnRescueParameter : ContractParameter
    {
        public string Kerbal = "";
        float next;
        protected override string GetTitle() { return "Recover " + Kerbal + " on Kerbin"; }
        protected override string GetHashString() { return "knn-rescue-" + Kerbal; }
        protected override void OnUpdate()
        {
            if (Time.realtimeSinceStartup < next || HighLogic.CurrentGame == null || HighLogic.CurrentGame.CrewRoster == null) return;
            next = Time.realtimeSinceStartup + 2;
            var k = HighLogic.CurrentGame.CrewRoster[Kerbal];
            if (k == null) return;
            if (k.rosterStatus == ProtoCrewMember.RosterStatus.Available) SetComplete();
            else if (k.rosterStatus == ProtoCrewMember.RosterStatus.Dead || k.rosterStatus == ProtoCrewMember.RosterStatus.Missing)
            {
                // DeepFreeze marks frozen kerbals dead but keeps them as Unowned: that is not a death.
                if (k.type != ProtoCrewMember.KerbalType.Unowned) SetFailed();
            }
        }
        protected override void OnSave(ConfigNode n) { n.AddValue("kerbal", Kerbal); }
        protected override void OnLoad(ConfigNode n) { Kerbal = n.GetValue("kerbal") ?? ""; }
    }

    /// <summary>Done when a crewed vessel is landed or splashed on the body.</summary>
    public class KnnLandParameter : ContractParameter
    {
        public string Body = "";
        float next;
        protected override string GetTitle() { return "Land a crewed vessel on " + Body; }
        protected override string GetHashString() { return "knn-land-" + Body; }
        protected override void OnUpdate()
        {
            if (Time.realtimeSinceStartup < next || !HighLogic.LoadedSceneIsFlight) return;
            next = Time.realtimeSinceStartup + 1;
            var v = FlightGlobals.ActiveVessel;
            if (v != null && v.mainBody != null && v.mainBody.bodyName == Body && (v.situation == Vessel.Situations.LANDED || v.situation == Vessel.Situations.SPLASHED) && v.GetCrewCount() > 0)
                SetComplete();
        }
        protected override void OnSave(ConfigNode n) { n.AddValue("body", Body); }
        protected override void OnLoad(ConfigNode n) { Body = n.GetValue("body") ?? ""; }
    }

    /// <summary>Done when any science from the body reaches Kerbin.</summary>
    public class KnnScienceParameter : ContractParameter
    {
        public string Body = "";
        protected override string GetTitle() { return "Return or transmit science from " + Body; }
        protected override string GetHashString() { return "knn-science-" + Body; }
        protected override void OnRegister() { GameEvents.OnScienceRecieved.Add(OnScience); }
        protected override void OnUnregister() { GameEvents.OnScienceRecieved.Remove(OnScience); }
        void OnScience(float amount, ScienceSubject s, ProtoVessel pv, bool reverse)
        {
            if (s == null || s.id == null) return;
            int at = s.id.IndexOf('@');
            if (at >= 0 && s.id.Substring(at + 1).StartsWith(Body, StringComparison.Ordinal)) SetComplete();
        }
        protected override void OnSave(ConfigNode n) { n.AddValue("body", Body); }
        protected override void OnLoad(ConfigNode n) { Body = n.GetValue("body") ?? ""; }
    }

    /// <summary>Done when the debris around the home world has dropped by the target count since the contract was accepted.</summary>
    public class KnnDebrisParameter : ContractParameter
    {
        public int Target = 5;
        int baseline = -1;
        float next;
        protected override string GetTitle() { return "Remove " + Target + " pieces of debris from Kerbin orbit" + (baseline >= 0 ? " (" + Math.Max(0, baseline - Count()) + " so far)" : ""); }
        protected override string GetHashString() { return "knn-debris"; }

        static int Count()
        {
            var home = FlightGlobals.GetHomeBody();
            if (FlightGlobals.Vessels == null || home == null) return -1;
            return FlightGlobals.Vessels.Count(v => v != null && v.vesselType == VesselType.Debris && v.mainBody == home && v.situation == Vessel.Situations.ORBITING);
        }

        protected override void OnUpdate()
        {
            if (Time.realtimeSinceStartup < next || !HighLogic.LoadedSceneIsFlight) return;
            next = Time.realtimeSinceStartup + 3;
            int n = Count();
            if (n < 0) return;
            if (baseline < 0) { baseline = n; return; }
            if (baseline - n >= Target) SetComplete();
        }
        protected override void OnSave(ConfigNode n) { n.AddValue("target", Target); n.AddValue("baseline", baseline); }
        protected override void OnLoad(ConfigNode n) { int.TryParse(n.GetValue("target") ?? "5", out Target); if (!int.TryParse(n.GetValue("baseline") ?? "-1", out baseline)) baseline = -1; }
    }
}
