using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace KerbalNews
{
    /// <summary>
    /// The assistant with the AI off: it understands a handful of short typed commands (look things up, publish a
    /// release, post, donate, leak, open a town hall) and runs them through the same actions, approvals and paper trail.
    /// </summary>
    public partial class NewsEngine
    {
        public const string OfflineAssistantHelp =
            "The AI is off, so I only understand short commands for now:\n" +
            "- news, finances, ledger, mood, crew, mail, vessels, outlets, reporters, orders: look something up\n" +
            "- read <headline words>: read an article\n" +
            "- release: <title> | <text>: publish a press release\n" +
            "- post: <text>: post on the agency's Kerbnet account\n" +
            "- donate <amount> to <who>: give money (add \"publicly\" to announce it)\n" +
            "- leak to <outlet>: <information>: tip off one outlet anonymously\n" +
            "- town hall: <title>: open a town hall Q&A on Snackboard\n" +
            "Turn the AI on in Settings and I can do anything you ask in plain words.";

        static readonly string[] OfflineLookups = { "news", "finances", "ledger", "mood", "crew", "mail", "vessels", "outlets", "reporters", "orders", "lore", "gallery", "forum", "wiki" };

        /// <summary>Answers one typed command without the AI.</summary>
        void AssistantOffline(string text, double ut)
        {
            string t = text.Trim();
            string low = t.ToLowerInvariant().TrimEnd('.', '!', '?');
            Match m;

            if (low == "" || low == "help" || low == "?" || low == "commands") { AssistantSay("assistant", OfflineAssistantHelp, ut); return; }
            string first = low.Split(' ')[0];
            if (OfflineLookups.Contains(first) && low.Split(' ').Length <= 3)
            {
                var args = new Dictionary<string, object>();
                string rest = t.Length > first.Length ? t.Substring(first.Length).Trim() : "";
                if (rest != "") args[first == "wiki" ? "page" : "query"] = rest;
                AssistantSay("assistant", Lookup(first, args, ut), ut);
                return;
            }
            if ((m = Regex.Match(t, @"^read\s+(.+)$", RegexOptions.IgnoreCase)).Success)
            {
                AssistantSay("assistant", Lookup("read", new Dictionary<string, object> { { "article", m.Groups[1].Value.Trim() } }, ut), ut);
                return;
            }
            var actions = new List<object>();
            if ((m = Regex.Match(t, @"^(?:press\s+)?release\s*:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)).Success)
            {
                var parts = m.Groups[1].Value.Split(new[] { '|' }, 2);
                string title = parts[0].Trim(), body = parts.Length > 1 ? parts[1].Trim() : parts[0].Trim();
                actions.Add(Act("press_release", new Dictionary<string, object> { { "type", "announcement" }, { "title", title }, { "body", body } }));
            }
            else if ((m = Regex.Match(t, @"^(?:post|kerbnet)\s*:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)).Success)
                actions.Add(Act("kerbnet_post", new Dictionary<string, object> { { "text", m.Groups[1].Value.Trim() } }));
            else if ((m = Regex.Match(t, @"^donate\s+√?\s*([\d,\.]+)\s*(k)?\s+(?:funds\s+)?to\s+(.+?)(\s+publicly)?$", RegexOptions.IgnoreCase)).Success)
            {
                double amount;
                if (!double.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out amount) || amount <= 0)
                { AssistantSay("assistant", "How much? For example: donate 5000 to the Mun Valley school.", ut); return; }
                if (m.Groups[2].Success) amount *= 1000;
                actions.Add(Act("donate", new Dictionary<string, object> { { "recipient", m.Groups[3].Value.Trim() }, { "amount", amount }, { "publicize", m.Groups[4].Success } }));
            }
            else if ((m = Regex.Match(t, @"^leak\s+to\s+(.+?)\s*:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)).Success)
                actions.Add(Act("leak", new Dictionary<string, object> { { "outlet", m.Groups[1].Value.Trim() }, { "info", m.Groups[2].Value.Trim() } }));
            else if ((m = Regex.Match(t, @"^town\s*hall\s*:?\s*(.*)$", RegexOptions.IgnoreCase)).Success)
                actions.Add(Act("town_hall", new Dictionary<string, object> { { "title", m.Groups[1].Value.Trim() != "" ? m.Groups[1].Value.Trim() : "Town hall with the director" }, { "body", "Ask the director anything." } }));

            if (actions.Count == 0) { AssistantSay("assistant", "I didn't catch that. " + OfflineAssistantHelp, ut); return; }
            AssistantAct(actions, ut);
        }

        static object Act(string cmd, Dictionary<string, object> args) { return new Dictionary<string, object> { { "cmd", cmd }, { "args", args } }; }
    }
}
