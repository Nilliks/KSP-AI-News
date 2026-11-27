using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KerbalNews
{
    /// <summary>What one service has been asked to do: requests, the tokens its replies reported, ElevenLabs letters and pictures.</summary>
    public class UsageStats
    {
        public long Calls, Failed, InTokens, OutTokens, Letters, Pictures;

        public void AddFrom(UsageStats o)
        {
            Calls += o.Calls; Failed += o.Failed; InTokens += o.InTokens; OutTokens += o.OutTokens; Letters += o.Letters; Pictures += o.Pictures;
        }

        public bool Empty { get { return Calls == 0 && Failed == 0; } }

        public void Save(DataNode n)
        {
            n.Add("calls", Calls.ToString(CultureInfo.InvariantCulture));
            n.Add("failed", Failed.ToString(CultureInfo.InvariantCulture));
            n.Add("in", InTokens.ToString(CultureInfo.InvariantCulture));
            n.Add("out", OutTokens.ToString(CultureInfo.InvariantCulture));
            n.Add("letters", Letters.ToString(CultureInfo.InvariantCulture));
            n.Add("pictures", Pictures.ToString(CultureInfo.InvariantCulture));
        }

        public static UsageStats Load(DataNode n)
        {
            Func<string, long> get = k => { long v; return long.TryParse(n.Get(k, "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? Math.Max(0, v) : 0; };
            return new UsageStats { Calls = get("calls"), Failed = get("failed"), InTokens = get("in"), OutTokens = get("out"), Letters = get("letters"), Pictures = get("pictures") };
        }
    }

    /// <summary>
    /// Counts every paid request the mod sends, per service, so the Settings page can show what the AI has actually been used for.
    /// It sits between the engine and the real transport and never changes a request or a reply. Free requests (model lists,
    /// key checks, anything that is not a POST) are not counted. Totals are kept in settings.cfg, since they are real money
    /// whichever save they were spent in; the session count starts at zero each time the game starts.
    /// </summary>
    public class UsageMeter : IHttpTransport
    {
        readonly IHttpTransport inner;
        readonly Func<GlobalSettings> settings;

        public UsageMeter(IHttpTransport inner, Func<GlobalSettings> settings) { this.inner = inner; this.settings = settings; }

        public IHttpTransport Inner { get { return inner; } }

        public void Send(HttpRequestSpec request, Action<HttpResult> done)
        {
            if (request == null || request.Method != "POST") { inner.Send(request, done); return; }
            string service = ServiceFor(settings(), request.Url);
            long letters = service == Services.ElevenLabs ? LettersIn(request) : 0;
            bool picture = IsPicture(request.Url);
            inner.Send(request, res =>
            {
                try { Record(settings(), service, res, letters, picture); }
                catch (Exception) { } // counting must never break a request
                done(res);
            });
        }

        public static void Record(GlobalSettings g, string service, HttpResult res, long letters, bool picture)
        {
            if (g == null) return;
            var one = new UsageStats { Calls = 1 };
            if (res == null || !res.Ok) one.Failed = 1;
            else
            {
                long i, o;
                if (ReadTokens(res.Body, out i, out o)) { one.InTokens = i; one.OutTokens = o; }
                one.Letters = letters;
                if (picture) one.Pictures = 1;
            }
            g.CountUsage(service, one);
        }

        /// <summary>Which service a request goes to, matched on the address it is sent to. A local server is "local".</summary>
        public static string ServiceFor(GlobalSettings g, string url)
        {
            string host = Host(url);
            if (host == "") return "other";
            if (host == Host(Voice.BaseUrl)) return Services.ElevenLabs;
            if (g != null)
            {
                // The player's own addresses first, so a custom endpoint is credited to the service it was typed into.
                foreach (var p in Providers.All)
                    if (Host(g.BaseUrlOf(p.Id)) == host) return p.Id;
                foreach (var kv in g.ImageBaseUrls)
                    if (Host(kv.Value) == host) return Img(kv.Key);
            }
            foreach (var ip in ImageProviders.All)
                if (Host(ip.BaseUrl) == host) return Img(ip.Id);
            return Providers.IsLocal(url) ? "local" : "other";
        }

        static string Img(string imageId) { var s = Services.ForImage(imageId); return s != "" ? s : imageId; }

        static string Host(string url)
        {
            Uri u;
            return !string.IsNullOrEmpty(url) && Uri.TryCreate(url.Trim(), UriKind.Absolute, out u) ? u.Host.ToLowerInvariant() + ":" + u.Port : "";
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
            return Pair(root, "usage.prompt_tokens", "usage.completion_tokens", out input, out output)
                || Pair(root, "usage.input_tokens", "usage.output_tokens", out input, out output)
                || Pair(root, "usageMetadata.promptTokenCount", "usageMetadata.candidatesTokenCount", out input, out output)
                || Pair(root, "prompt_eval_count", "eval_count", out input, out output);
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

        /// <summary>"12,345" or "1.2 M": short enough for a table cell.</summary>
        public static string Short(long n)
        {
            if (n >= 10000000) return (n / 1e6).ToString("0", CultureInfo.InvariantCulture) + " M";
            if (n >= 1000000) return (n / 1e6).ToString("0.0", CultureInfo.InvariantCulture) + " M";
            return n.ToString("#,0", CultureInfo.InvariantCulture);
        }
    }
}
