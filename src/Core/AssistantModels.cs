using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KerbalNews
{
    /// <summary>One line of the assistant chat.</summary>
    public class AssistantMessage
    {
        public string Id = "";
        /// <summary>player, assistant, result (what actually happened), or proposal (waiting for the director's OK).</summary>
        public string Role = "player";
        public string Text = "";
        public double UT;
        /// <summary>For a proposal: the actions as JSON, and pending / confirmed / cancelled.</summary>
        public string Actions = "";
        public string State = "";

        public void Save(DataNode n)
        {
            n.Add("id", Id); n.Add("role", Role); n.AddText("text", Text); n.Add("ut", UT);
            if (Actions != "") { n.AddText("actions", Actions); n.Add("state", State); }
        }

        public static AssistantMessage Load(DataNode n)
        {
            return new AssistantMessage { Id = n.Get("id", ""), Role = n.Get("role", "player"), Text = n.GetText("text"), UT = n.GetDouble("ut"), Actions = n.GetText("actions"), State = n.Get("state", "") };
        }
    }

    /// <summary>
    /// The paper trail: everything the assistant did for the agency, what it cost, and how it turned out.
    /// Secret deals can come out later, when a reporter finds the records.
    /// </summary>
    public class Deal
    {
        public string Id = "";
        public string Kind = "";        // donation, takedown, coverage, legal, leak, ad, sponsorship, briefing, release, post
        public double UT;
        public string Target = "";      // outlet id, charity or company name
        public string ArticleId = "";
        /// <summary>Money moved: negative is spent, positive is received. Zero until it actually changes hands.</summary>
        public double Amount;
        /// <summary>Money offered or asked for.</summary>
        public double Offered;
        public string Summary = "";
        public string Detail = "";
        public bool Secret;
        public string Status = "pending"; // pending, done, refused, backfired
        public double ResolveUT = -1;
        public string Outcome = "";
        /// <summary>Chance per day that a reporter finds the records, while the window is open.</summary>
        public double ExposeChance;
        public double ExposeUntilUT = -1;
        public double ExposedUT = -1;

        public bool Exposed { get { return ExposedUT >= 0; } }

        public void Save(DataNode n)
        {
            n.Add("id", Id); n.Add("kind", Kind); n.Add("ut", UT); n.AddText("target", Target); n.Add("article", ArticleId);
            n.Add("amount", Amount); n.Add("offered", Offered); n.AddText("summary", Summary); n.AddText("detail", Detail); n.Add("secret", Secret);
            n.Add("status", Status); n.Add("resolve", ResolveUT); n.AddText("outcome", Outcome); n.Add("expose", ExposeChance); n.Add("exposeUntil", ExposeUntilUT); n.Add("exposed", ExposedUT);
        }

        public static Deal Load(DataNode n)
        {
            return new Deal
            {
                Id = n.Get("id", ""), Kind = n.Get("kind", ""), UT = n.GetDouble("ut"), Target = n.GetText("target"), ArticleId = n.Get("article", ""),
                Amount = n.GetDouble("amount"), Offered = n.GetDouble("offered"), Summary = n.GetText("summary"), Detail = n.GetText("detail"), Secret = n.GetBool("secret"),
                Status = n.Get("status", "pending"), ResolveUT = n.GetDouble("resolve", -1), Outcome = n.GetText("outcome"), ExposeChance = n.GetDouble("expose"),
                ExposeUntilUT = n.GetDouble("exposeUntil", -1), ExposedUT = n.GetDouble("exposed", -1),
            };
        }
    }
}
