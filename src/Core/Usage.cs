using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KerbalNews
{
    /// <summary>What one feature has cost: requests, the tokens the replies reported, and those tokens at the list price of the day.</summary>
    public class UsageStats
    {
        public long Calls, Failed, InTokens, OutTokens, Letters, Pictures;
        /// <summary>Requests that answered but could not be priced: no token count in the reply, or a model with no listed price.</summary>
        public long Unpriced;
        public double Cost;

        public void AddFrom(UsageStats o)
        {
            Calls += o.Calls; Failed += o.Failed; InTokens += o.InTokens; OutTokens += o.OutTokens; Letters += o.Letters; Pictures += o.Pictures;
            Unpriced += o.Unpriced; Cost += o.Cost;
        }

        public void Save(DataNode n)
        {
            var inv = CultureInfo.InvariantCulture;
            n.Add("calls", Calls.ToString(inv)); n.Add("failed", Failed.ToString(inv)); n.Add("in", InTokens.ToString(inv)); n.Add("out", OutTokens.ToString(inv));
            n.Add("letters", Letters.ToString(inv)); n.Add("pictures", Pictures.ToString(inv)); n.Add("unpriced", Unpriced.ToString(inv)); n.Add("cost", Cost);
        }

        public static UsageStats Load(DataNode n)
        {
            Func<string, long> get = k => { long v; return long.TryParse(n.Get(k, "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? Math.Max(0, v) : 0; };
            return new UsageStats { Calls = get("calls"), Failed = get("failed"), InTokens = get("in"), OutTokens = get("out"), Letters = get("letters"), Pictures = get("pictures"),
                Unpriced = get("unpriced"), Cost = Math.Max(0, n.GetDouble("cost")) };
        }
    }

    /// <summary>
    /// A running count for one span of time: this game session, this save, or everything on this computer. Kept per
    /// feature, with the hours played and launches flown over the same span, so spending can be read per hour and per launch.
    /// </summary>
    public class UsageBook
    {
        public Dictionary<string, UsageStats> By = new Dictionary<string, UsageStats>();
        public double PlaySeconds;
        public long Launches;
        /// <summary>When the count was last started from zero, as shown on the page ("" = since counting began).</summary>
        public string Since = "";

        public void Count(string feature, UsageStats one)
        {
            UsageStats s;
            if (!By.TryGetValue(feature, out s)) By[feature] = s = new UsageStats();
            s.AddFrom(one);
        }

        public UsageStats Of(string feature) { UsageStats s; return By.TryGetValue(feature, out s) ? s : new UsageStats(); }

        public UsageStats Total { get { var t = new UsageStats(); foreach (var s in By.Values) t.AddFrom(s); return t; } }

        public void Clear(string since) { By.Clear(); PlaySeconds = 0; Launches = 0; Since = since ?? ""; }

        public void Save(DataNode n)
        {
            n.Add("play", PlaySeconds);
            n.Add("launches", Launches.ToString(CultureInfo.InvariantCulture));
            n.AddText("since", Since);
            foreach (var kv in By)
            {
                if (kv.Value.Calls == 0) continue;
                var f = n.AddNode("FEATURE");
                f.AddText("name", kv.Key);
                kv.Value.Save(f);
            }
        }

        public static UsageBook Load(DataNode n)
        {
            var b = new UsageBook();
            if (n == null) return b;
            b.PlaySeconds = Math.Max(0, n.GetDouble("play"));
            long l; b.Launches = long.TryParse(n.Get("launches", "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out l) ? Math.Max(0, l) : 0;
            b.Since = n.GetText("since");
            foreach (var f in n.GetNodes("FEATURE"))
            {
                string name = f.GetText("name");
                if (name != "") b.By[name] = UsageStats.Load(f);
            }
            return b;
        }
    }

    /// <summary>
    /// Counts every paid request the mod sends. It sits between the engine and the real transport and never changes a
    /// request or a reply. Free requests (model lists, key checks, the price list: anything that is not a POST) are
    /// not counted. The dollars are the tokens each reply says it used, times the model's list price at that moment.
    /// </summary>
    public class UsageMeter : IHttpTransport
    {
        readonly IHttpTransport inner;
        readonly Action<string, UsageStats> count;

        public const string VoiceFeature = "Announcer voice", PictureFeature = "Article photos", Other = "Other";

        public UsageMeter(IHttpTransport inner, Action<string, UsageStats> count) { this.inner = inner; this.count = count; }

        public IHttpTransport Inner { get { return inner; } }

        public void Send(HttpRequestSpec request, Action<HttpResult> done)
        {
            if (request == null || request.Method != "POST") { inner.Send(request, done); return; }
            inner.Send(request, res =>
            {
                try { count(FeatureFor(request), Measure(request, res)); }
                catch (Exception) { }   // counting must never break a request
                done(res);
            });
        }

        static string FeatureFor(HttpRequestSpec r)
        {
            if (!string.IsNullOrEmpty(r.Feature)) return r.Feature;
            if (IsVoice(r.Url)) return VoiceFeature;
            if (IsPicture(r.Url)) return PictureFeature;
            return Other;
        }

        public static UsageStats Measure(HttpRequestSpec r, HttpResult res)
        {
            var one = new UsageStats { Calls = 1 };
            if (res == null || !res.Ok) { one.Failed = 1; return one; }
            if (IsVoice(r.Url)) { one.Letters = LettersIn(r); return one; }
            if (string.IsNullOrEmpty(r.ProviderId)) { if (IsPicture(r.Url)) one.Pictures = 1; return one; }
            var p = Providers.All.FirstOrDefault(x => x.Id == r.ProviderId);
            long i, o;
            bool counted = ReadTokens(res.Body, out i, out o);
            one.InTokens = i; one.OutTokens = o;
            if (p != null && !p.NeedsKey) return one;   // a model on the player's own computer costs nothing
            double a, b;
            if (counted && Pricing.TryGet(p, r.Model, out a, out b)) one.Cost = (a * i + b * o) / 1e6;
            else one.Unpriced = 1;
            return one;
        }

        static bool IsVoice(string url)
        {
            Uri a, b;
            return Uri.TryCreate(url ?? "", UriKind.Absolute, out a) && Uri.TryCreate(Voice.BaseUrl, UriKind.Absolute, out b) && a.Host == b.Host;
        }

        static bool IsPicture(string url)
        {
            string u = (url ?? "").ToLowerInvariant();
            return u.Contains("/images/") || u.Contains("://fal.run") || u.Contains(":generatecontent") && u.Contains("-image");
        }

        static long LettersIn(HttpRequestSpec r)
        {
            if (string.IsNullOrEmpty(r.Body) || (r.Url ?? "").IndexOf("/text-to-speech/", StringComparison.OrdinalIgnoreCase) < 0) return 0;
            object root;
            return Json.TryParse(r.Body, out root) ? (Json.Get(root, "text") as string ?? "").Length : 0;
        }

        /// <summary>The token counts a reply reports, in any of the formats the mod speaks (OpenAI, Anthropic, Gemini, Ollama).</summary>
        public static bool ReadTokens(string body, out long input, out long output)
        {
            input = output = 0;
            if (string.IsNullOrEmpty(body) || !body.TrimStart().StartsWith("{")) return false;
            object root;
            if (!Json.TryParse(body, out root)) return false;
            if (Pair(root, "usage.prompt_tokens", "usage.completion_tokens", out input, out output)) return true;
            if (Pair(root, "usage.input_tokens", "usage.output_tokens", out input, out output))
            {
                // Anthropic reports what it read from and wrote to its prompt cache apart from the rest of the input.
                input += Num(Json.Path(root, "usage.cache_creation_input_tokens")) + Num(Json.Path(root, "usage.cache_read_input_tokens"));
                return true;
            }
            if (Pair(root, "usageMetadata.promptTokenCount", "usageMetadata.candidatesTokenCount", out input, out output))
            {
                output += Num(Json.Path(root, "usageMetadata.thoughtsTokenCount"));   // Gemini bills its thinking as output
                return true;
            }
            return Pair(root, "prompt_eval_count", "eval_count", out input, out output);
        }

        static bool Pair(object root, string a, string b, out long x, out long y)
        {
            x = Num(Json.Path(root, a));
            y = Num(Json.Path(root, b));
            return x > 0 || y > 0;
        }

        static long Num(object v)
        {
            if (v is double) return (long)(double)v;
            if (v is long) return (long)v;
            if (v is int) return (int)v;
            double d;
            return v is string && double.TryParse((string)v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? (long)d : 0;
        }

        /// <summary>"12,345" or "1.2 M".</summary>
        public static string Short(long n)
        {
            if (n >= 10000000) return (n / 1e6).ToString("0", CultureInfo.InvariantCulture) + " M";
            if (n >= 1000000) return (n / 1e6).ToString("0.0", CultureInfo.InvariantCulture) + " M";
            return n.ToString("#,0", CultureInfo.InvariantCulture);
        }
    }
}
