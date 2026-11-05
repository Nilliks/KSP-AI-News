using System;
using System.Collections.Generic;
using System.Linq;

namespace KerbalNews.Web
{
    /// <summary>agency.kb/assistant (the chat) and agency.kb/ledger (the paper trail).</summary>
    public partial class Sites
    {
        static readonly string[] AssistantExamples =
        {
            "Give me a morning briefing",
            "What is the press saying about us this week?",
            "How much money do we have, and what have we spent?",
            "Donate 10,000 to the Kerbin Children's Science Fund and announce it",
            "Get the Daily Kraken to take down their latest story about us",
            "Leak a hint about our next mission to the Orbital Observer",
        };

        Dictionary<string, object> AssistantModel()
        {
            var m = M();
            var g = Engine.Settings;
            m["on"] = g.AssistantOn;
            m["off"] = !g.AssistantOn;
            m["offline"] = !g.CanCallAi;
            m["confirm"] = g.AssistantConfirm;
            m["name"] = NewsEngine.AssistantName;
            m["busy"] = Engine.AssistantBusy;
            m["status"] = Engine.AssistantStatus;
            double f = Engine.FundsAvailable();
            m["funds"] = double.IsNaN(f) ? "not tracked in this game mode" : "√" + NewsEngine.Money(f);
            m["examples"] = AssistantExamples.Select(e => (object)new Dictionary<string, object> { { "text", e }, { "url", "action:assistant-ask/" + Uri.EscapeDataString(e) } }).ToList();
            m["messages"] = State.AssistantChat.AsEnumerable().Reverse().Take(60).Select(x => (object)new Dictionary<string, object>
            {
                { "player", x.Role == "player" }, { "assistant", x.Role == "assistant" }, { "result", x.Role == "result" }, { "proposal", x.Role == "proposal" },
                { "pending", x.Role == "proposal" && x.State == "pending" }, { "stateText", x.State == "confirmed" ? "Approved" : x.State == "cancelled" ? "Cancelled" : "" },
                { "lines", x.Text.Split('\n').Where(l => l.Trim() != "").Select(l => (object)l).ToList() },
                { "date", Date(x.UT) }, { "yesUrl", "action:assistant-yes/" + x.Id }, { "noUrl", "action:assistant-no/" + x.Id },
            }).ToList();
            m["empty"] = State.AssistantChat.Count == 0;
            m["orders"] = State.Orders.Select((o, i) => (object)new Dictionary<string, object> { { "n", i + 1 }, { "text", o.Text }, { "removeUrl", "action:assistant-unorder/" + (i + 1) } }).ToList();
            m["hasOrders"] = State.Orders.Count > 0;
            return m;
        }

        /// <summary>Rescans GameData for mods (set by the KSP layer). Returns a line for the page.</summary>
        public Func<string> RescanMods = () => "Scanning only works in game.";
        string modScanNote = "";

        Dictionary<string, object> ModsModel()
        {
            var m = M();
            var g = Engine.Settings;
            var mods = g.Mods.OrderByDescending(x => x.IsNew).ThenByDescending(x => x.Installed).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            m["on"] = g.ModsInContext;
            m["count"] = g.Mods.Count(x => x.Installed);
            m["included"] = g.Mods.Count(x => x.Installed && x.Include);
            m["scanNote"] = modScanNote;
            m["mods"] = mods.Select(x => (object)new Dictionary<string, object>
            {
                { "name", x.Name }, { "folder", x.Folder }, { "version", x.Version == "" ? "version unknown" : "v" + x.Version }, { "note", x.Note },
                { "include", x.Include }, { "library", x.Library }, { "installed", x.Installed }, { "isNew", x.IsNew }, { "i", g.Mods.IndexOf(x) },
            }).ToList();
            string preview = g.ModsText;
            m["preview"] = preview == "" ? "Nothing: no mods are shared." : preview;
            // Seen now: the NEW tags show this once.
            if (g.Mods.Any(x => x.IsNew)) { foreach (var x in g.Mods) x.IsNew = false; SaveSettings(); }
            return m;
        }

        Dictionary<string, object> LedgerModel()
        {
            var m = M();
            double spent = State.Deals.Where(d => d.Amount < 0).Sum(d => -d.Amount), got = State.Deals.Where(d => d.Amount > 0).Sum(d => d.Amount);
            m["spent"] = "√" + NewsEngine.Money(spent);
            m["received"] = "√" + NewsEngine.Money(got);
            m["deals"] = State.Deals.OrderByDescending(d => d.UT).Take(100).Select(d => (object)new Dictionary<string, object>
            {
                { "date", Date(d.UT) }, { "kind", d.Kind }, { "summary", d.Summary }, { "outcome", d.Outcome }, { "status", d.Status },
                { "amount", d.Amount == 0 ? "" : (d.Amount > 0 ? "+√" : "-√") + NewsEngine.Money(Math.Abs(d.Amount)) }, { "income", d.Amount > 0 },
                { "secret", d.Secret && !d.Exposed }, { "exposed", d.Exposed }, { "backfired", d.Status == "backfired" },
            }).ToList();
            m["empty"] = State.Deals.Count == 0;
            m["ties"] = State.Ties.Where(t => t.Score != 0).OrderBy(t => t.Score).Select(t => (object)new Dictionary<string, object>
            {
                { "name", t.Name }, { "outlet", Outlets.NameOf(t.Outlet) }, { "feeling", t.Feeling }, { "score", (t.Score > 0 ? "+" : "") + t.Score }, { "good", t.Score > 0 },
                { "notes", string.Join(" · ", t.Notes.Skip(Math.Max(0, t.Notes.Count - 3)).ToArray()) },
            }).ToList();
            m["hasTies"] = State.Ties.Any(t => t.Score != 0);
            return m;
        }
    }
}
