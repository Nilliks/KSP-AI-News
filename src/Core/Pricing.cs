using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>
    /// What each model costs, for the estimates beside the model drop-downs in Settings. Nothing here counts what was
    /// spent: it is list prices times a typical request. Prices come from LiteLLM's public price list (kept current
    /// by its maintainers, no key needed) and, for OpenRouter, from OpenRouter's own model list. A copy that ships with
    /// the mod fills in until the first refresh.
    /// </summary>
    public static class Pricing
    {
        public const string Url = "https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        /// <summary>"provider|model" in lower case to dollars per million tokens: in, out.</summary>
        static readonly Dictionary<string, double[]> table = new Dictionary<string, double[]>();
        /// <summary>The day the prices were last read from their source ("2026-11-28").</summary>
        public static string AsOf = "";
        /// <summary>Our service id for each provider name the price list uses.</summary>
        static readonly string[] Known = { "openai", "anthropic", "gemini", "xai", "mistral", "deepseek" };

        public static int Count { get { return table.Count; } }
        public static void Clear() { table.Clear(); AsOf = ""; }

        /// <summary>Typical request sizes (tokens in, tokens out) behind the estimates. Rough on purpose.</summary>
        public static readonly int[] BigStory = { 6000, 3500 }, Routine = { 4000, 1200 }, PhotoLook = { 1500, 200 };

        /// <summary>Reads lines of "provider|model in out" (and "# day"). Newer lines replace what is there unless onlyMissing.</summary>
        public static void Load(string text, bool onlyMissing = false)
        {
            foreach (var raw in (text ?? "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("#")) { string day = line.Substring(1).Trim(); if (!onlyMissing || AsOf == "") { if (string.CompareOrdinal(day, AsOf) > 0) AsOf = day; } continue; }
                var parts = line.Split(' ');
                double a, b;
                if (parts.Length != 3 || !double.TryParse(parts[1], NumberStyles.Float, Inv, out a) || !double.TryParse(parts[2], NumberStyles.Float, Inv, out b)) continue;
                if (onlyMissing && table.ContainsKey(parts[0])) continue;
                table[parts[0]] = new[] { a, b };
            }
        }

        public static string Serialize()
        {
            var sb = new StringBuilder("# " + AsOf + "\n");
            foreach (var kv in table.OrderBy(k => k.Key, StringComparer.Ordinal))
                sb.Append(kv.Key).Append(' ').Append(kv.Value[0].ToString("0.####", Inv)).Append(' ').Append(kv.Value[1].ToString("0.####", Inv)).Append('\n');
            return sb.ToString();
        }

        /// <summary>Reads LiteLLM's price list. Returns how many prices it gave; nothing changes when it gave none.</summary>
        public static int ReadLiteLlm(string body, string today)
        {
            object root;
            if (!Json.TryParse(body, out root)) return 0;
            var all = Json.Obj(root);
            if (all == null) return 0;
            var found = new Dictionary<string, double[]>();
            foreach (var kv in all)
            {
                var m = Json.Obj(kv.Value);
                if (m == null) continue;
                string provider = Json.Str(m, "litellm_provider", ""), mode = Json.Str(m, "mode", "");
                if (!Known.Contains(provider) || mode != "chat" && mode != "responses") continue;
                if (!m.ContainsKey("input_cost_per_token") || !m.ContainsKey("output_cost_per_token")) continue;
                string name = kv.Key.StartsWith(provider + "/") ? kv.Key.Substring(provider.Length + 1) : kv.Key;
                if (name == "" || name.Any(char.IsWhiteSpace)) continue;
                found[provider + "|" + name.ToLowerInvariant()] = new[] { Json.Num(m, "input_cost_per_token") * 1e6, Json.Num(m, "output_cost_per_token") * 1e6 };
            }
            if (found.Count == 0) return 0;
            foreach (var kv in found) table[kv.Key] = kv.Value;
            AsOf = today;
            return found.Count;
        }

        /// <summary>OpenRouter's model list carries each model's price per token.</summary>
        public static int ReadOpenRouter(string body)
        {
            object root;
            if (!Json.TryParse(body, out root)) return 0;
            int n = 0;
            foreach (var m in Json.Arr(Json.Get(root, "data")) ?? new List<object>())
            {
                string id = Json.Str(m, "id", "");
                var pr = Json.Get(m, "pricing");
                double a, b;
                if (id == "" || pr == null || id.Any(char.IsWhiteSpace) || !PerToken(Json.Get(pr, "prompt"), out a) || !PerToken(Json.Get(pr, "completion"), out b)) continue;
                table["openrouter|" + id.ToLowerInvariant()] = new[] { a * 1e6, b * 1e6 };
                n++;
            }
            return n;
        }

        static bool PerToken(object v, out double d)
        {
            d = 0;
            if (v is double) { d = (double)v; return d >= 0; }
            if (v is long) { d = (long)v; return d >= 0; }
            if (v is int) { d = (int)v; return d >= 0; }
            var s = v as string;
            return s != null && double.TryParse(s, NumberStyles.Float, Inv, out d) && d >= 0;
        }

        /// <summary>
        /// A model's price in dollars per million tokens. A dated or tagged name ("gpt-5-2025-08-07") takes the price of
        /// the longest listed name it starts with.
        /// </summary>
        public static bool TryGet(ProviderPreset p, string model, out double inPerM, out double outPerM)
        {
            inPerM = outPerM = 0;
            if (p == null || string.IsNullOrEmpty(model)) return false;
            string m = model.ToLowerInvariant(), pre = p.Id + "|";
            double[] v;
            if (!table.TryGetValue(pre + m, out v))
            {
                string best = null;
                foreach (var k in table.Keys)
                {
                    if (!k.StartsWith(pre)) continue;
                    string name = k.Substring(pre.Length);
                    if (name.Length < 5 || m.Length <= name.Length || !m.StartsWith(name) || "-:@.".IndexOf(m[name.Length]) < 0) continue;
                    if (best == null || name.Length > best.Length) best = name;
                }
                if (best == null) return false;
                v = table[pre + best];
            }
            inPerM = v[0]; outPerM = v[1];
            return true;
        }

        public static string Money(double dollars)
        {
            if (dollars <= 0) return "$0";
            if (dollars >= 0.995) return "$" + dollars.ToString("0.00", Inv);
            if (dollars >= 0.01) return "$" + dollars.ToString("0.00", Inv);
            if (dollars >= 0.001) return "$" + dollars.ToString("0.000", Inv);
            return "under $0.001";
        }

        /// <summary>What one request of a typical size costs, or -1 when the model's price is not known.</summary>
        public static double Estimate(ProviderPreset p, string model, int[] size)
        {
            double a, b;
            return TryGet(p, model, out a, out b) ? (a * size[0] + b * size[1]) / 1e6 : -1;
        }

        /// <summary>"$0.30 in, $1.20 out per million tokens · about $0.006 a big story". "" when the price is not known.</summary>
        public static string Line(ProviderPreset p, string model, int[] size, string what)
        {
            if (p != null && !p.NeedsKey) return "free, runs on your own computer";
            double a, b;
            if (!TryGet(p, model, out a, out b)) return "";
            if (a == 0 && b == 0) return "free";
            return "about " + Money((a * size[0] + b * size[1]) / 1e6) + " " + what + " ($" + a.ToString("0.00", Inv) + " in, $" + b.ToString("0.00", Inv) + " out per million tokens)";
        }
    }
}
