using System;
using System.Collections.Generic;
using System.Linq;

namespace KerbalNews
{
    public class GeneratedCoverage
    {
        public List<Article> Articles = new List<Article>();
        public List<SocialPost> Social = new List<SocialPost>();
    }

    /// <summary>Turns the model's JSON into articles, and rejects output we should not save.</summary>
    public static class ResponseParser
    {
        static readonly string[] ForbiddenPhrases =
        {
            "quicksave", "quick save", "save file", "savefile", "reverted the flight", "revert to launch",
            "video game", "kerbal space program game", "as an ai", "language model",
        };

        /// <summary>Models sometimes wrap JSON in fences or prose; take the outermost object.</summary>
        public static string ExtractJson(string text)
        {
            if (text == null) return null;
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start < 0 || end <= start) return null;
            return text.Substring(start, end - start + 1);
        }

        public static bool TryParse(string text, CoveragePlan plan, out GeneratedCoverage result, out string error)
        {
            result = null;
            error = null;
            string json = ExtractJson(text);
            object root;
            if (json == null || !Json.TryParse(json, out root) || Json.Obj(root) == null)
            {
                error = "Response was not a JSON object";
                return false;
            }
            var cov = new GeneratedCoverage();
            var wanted = new HashSet<string>(plan.Outlets.Select(o => o.OutletId));
            var seen = new HashSet<string>();
            var arts = Json.Arr(Json.Get(root, "articles")) ?? new List<object>();
            foreach (var a in arts)
            {
                string outlet = (Json.Str(a, "outlet", "") ?? "").Trim().ToLowerInvariant();
                if (!wanted.Contains(outlet) || seen.Contains(outlet)) continue;
                string headline = Clean(Json.Str(a, "headline", ""));
                string body = (Json.Str(a, "body", "") ?? "").Replace("\r\n", "\n").Trim();
                if (headline.Length < 4 || body.Length < 40) continue;
                if (ContainsForbidden(headline) || ContainsForbidden(body)) continue;
                var assign = plan.Outlets.First(o => o.OutletId == outlet);
                var art = new Article
                {
                    Outlet = outlet,
                    Kind = Clean(Json.Str(a, "kind", assign.Kind)),
                    Headline = Clip(headline, 160),
                    Subhead = Clip(Clean(Json.Str(a, "subhead", "")), 300),
                    Reporter = Clip(Clean(Json.Str(a, "reporter", "")), 60),
                    Body = Clip(body, 8000),
                };
                if (art.Kind == "") art.Kind = assign.Kind;
                var tags = Json.Arr(Json.Get(a, "tags"));
                if (tags != null) foreach (var t in tags.OfType<string>().Take(8)) art.Tags.Add(Clean(t).ToLowerInvariant().Replace(",", ""));
                var comments = Json.Arr(Json.Get(a, "comments"));
                if (comments != null)
                    foreach (var c in comments.Take(NewsState.MaxCommentsPerArticle))
                    {
                        string ct = Clean(Json.Str(c, "text", ""));
                        if (ct == "" || ContainsForbidden(ct)) continue;
                        art.Comments.Add(new Comment
                        {
                            Author = Clip(Clean(Json.Str(c, "author", "Anonymous")), 40),
                            Text = Clip(ct, 1000),
                            Likes = (int)Math.Max(0, Json.Num(c, "likes", 0)),
                        });
                    }
                seen.Add(outlet);
                cov.Articles.Add(art);
            }
            if (cov.Articles.Count == 0)
            {
                error = "No usable articles in response";
                return false;
            }
            var social = Json.Arr(Json.Get(root, "social"));
            if (social != null)
                foreach (var p in social.Take(30))
                {
                    string t = Clean(Json.Str(p, "text", ""));
                    if (t == "" || ContainsForbidden(t)) continue;
                    string handle = Clip(Clean(Json.Str(p, "handle", "@kerbal")), 40);
                    if (!handle.StartsWith("@")) handle = "@" + handle;
                    cov.Social.Add(new SocialPost
                    {
                        Handle = handle,
                        Persona = Clip(Clean(Json.Str(p, "persona", "")), 20),
                        Text = Clip(t, 400),
                        Likes = (int)Math.Max(0, Json.Num(p, "likes", 0)),
                        Reshares = (int)Math.Max(0, Json.Num(p, "reshares", 0)),
                    });
                }
            result = cov;
            return true;
        }

        public static bool ContainsForbidden(string s)
        {
            var l = s.ToLowerInvariant();
            return ForbiddenPhrases.Any(f => l.Contains(f));
        }

        static string Clean(string s) { return (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); }
        static string Clip(string s, int n) { return s.Length <= n ? s : s.Substring(0, n); }
    }
}
