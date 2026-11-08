using System;
using System.Collections.Generic;
using System.Linq;

namespace KerbalNews
{
    public class GeneratedCoverage
    {
        public List<Article> Articles = new List<Article>();
        public List<SocialPost> Social = new List<SocialPost>();
        /// <summary>The writer's description of a press photo, when one was asked for.</summary>
        public string ImagePrompt = "";
        /// <summary>A Snackboard thread about the news, for bigger stories.</summary>
        public ForumThread Forum;
        /// <summary>A short KTV broadcast segment written in the same call.</summary>
        public TvSegment Tv;
        /// <summary>Emails to the director written in the same call (see NewsEngine.MailForCall), or null.</summary>
        public List<object> Emails;
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

        /// <summary>Best-effort fix for a truncated or slightly malformed JSON object: escapes raw control characters
        /// inside strings, cuts back to the last complete object or array, and closes whatever is still open.</summary>
        public static string Repair(string text)
        {
            if (text == null) return null;
            int start = text.IndexOf('{');
            if (start < 0) return null;
            var sb = new System.Text.StringBuilder();
            var stack = new List<char>();
            bool inStr = false, esc = false;
            int goodLen = -1;
            List<char> goodStack = null;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (inStr)
                {
                    if (esc) { esc = false; sb.Append(c); continue; }
                    if (c == '\\') { esc = true; sb.Append(c); continue; }
                    if (c == '"') { inStr = false; sb.Append(c); continue; }
                    if (c == '\n') { sb.Append("\\n"); continue; }
                    if (c == '\r') continue;
                    if (c == '\t') { sb.Append("\\t"); continue; }
                    sb.Append(c);
                    continue;
                }
                if (c == '"') { inStr = true; sb.Append(c); continue; }
                if (c == '{' || c == '[') { stack.Add(c); sb.Append(c); continue; }
                if (c == '}' || c == ']')
                {
                    if (stack.Count == 0) break;
                    stack.RemoveAt(stack.Count - 1);
                    // Drop a trailing comma before the closer.
                    int k = sb.Length - 1;
                    while (k >= 0 && char.IsWhiteSpace(sb[k])) k--;
                    if (k >= 0 && sb[k] == ',') sb.Remove(k, 1);
                    sb.Append(c);
                    if (stack.Count == 0) return sb.ToString();
                    goodLen = sb.Length;
                    goodStack = new List<char>(stack);
                    continue;
                }
                sb.Append(c);
            }
            if (goodStack == null) return null;
            var outSb = new System.Text.StringBuilder(sb.ToString(0, goodLen));
            for (int j = goodStack.Count - 1; j >= 0; j--) outSb.Append(goodStack[j] == '{' ? '}' : ']');
            return outSb.ToString();
        }

        public static bool TryParse(string text, CoveragePlan plan, out GeneratedCoverage result, out string error)
        {
            result = null;
            error = null;
            string json = ExtractJson(text);
            object root = null;
            bool ok = json != null && Json.TryParse(json, out root) && Json.Obj(root) != null;
            if (!ok)
            {
                // A reply cut off at the token limit, or with raw line breaks in strings: keep every complete item.
                string fixedJson = Repair(text);
                ok = fixedJson != null && Json.TryParse(fixedJson, out root) && Json.Obj(root) != null;
            }
            if (!ok || root == null)
            {
                error = "Response was not a JSON object";
                return false;
            }
            var cov = new GeneratedCoverage();
            cov.Emails = Json.Arr(Json.Get(root, "emails"));
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
            cov.ImagePrompt = Clip(Clean(Json.Str(root, "image_prompt", "")), 1200);
            cov.Forum = ParseThread(Json.Get(root, "forum"));
            var tv = Json.Get(root, "tv");
            if (Json.Obj(tv) != null)
            {
                var seg = new TvSegment { Headline = Clip(Clean(Json.Str(tv, "headline", "")), 90), Script = Clip(Clean(Json.Str(tv, "script", "")), 900), Ticker = Clip(Clean(Json.Str(tv, "ticker", "")), 120) };
                if (seg.Headline != "" && seg.Script != "" && !ContainsForbidden(seg.Script + " " + seg.Headline)) cov.Tv = seg;
            }
            result = cov;
            return true;
        }

        public static readonly string[] Boards = { "general", "launches", "missions", "science", "policy", "memes", "ama" };

        public static ForumThread ParseThread(object f)
        {
            if (Json.Obj(f) == null) return null;
            string title = Clean(Json.Str(f, "title", ""));
            if (title.Length < 4 || ContainsForbidden(title)) return null;
            string board = Clean(Json.Str(f, "board", "general")).ToLowerInvariant().TrimStart('b', '/');
            if (Array.IndexOf(Boards, board) < 0) board = "general";
            var t = new ForumThread
            {
                Board = board, Title = Clip(title, 200),
                Body = Clip((Json.Str(f, "body", "") ?? "").Replace("\r\n", "\n").Trim(), 3000),
                Author = Clip(Clean(Json.Str(f, "author", "kerbal_reader")), 40).TrimStart('@'),
                Score = (int)Math.Max(1, Json.Num(f, "score", 10)),
            };
            if (ContainsForbidden(t.Body)) t.Body = "";
            t.Comments = ParseReplies(Json.Arr(Json.Get(f, "replies")), null, 30);
            return t;
        }

        /// <summary>
        /// Community replies. reply_to is an existing comment id, "rN" for the N-th reply in this list, or "" for top level.
        /// knownIds are the comments already in the discussion. Ids are filled in by the engine.
        /// </summary>
        public static List<Comment> ParseReplies(List<object> arr, ICollection<string> knownIds, int max)
        {
            var list = new List<Comment>();
            if (arr == null) return list;
            var refs = new List<Comment>();
            foreach (var c in arr.Take(max))
            {
                string text = Clean(Json.Str(c, "text", ""));
                if (text == "" || ContainsForbidden(text)) { refs.Add(null); continue; }
                var cm = new Comment
                {
                    Author = Clip(Clean(Json.Str(c, "author", "kerbal_reader")), 40).TrimStart('@'),
                    Text = Clip(text, 1200),
                    Likes = (int)Math.Max(0, Json.Num(c, "likes", 1)),
                };
                string rt = Clean(Json.Str(c, "reply_to", "") ?? "");
                int n;
                if (rt.StartsWith("r") && int.TryParse(rt.Substring(1), out n) && n >= 1 && n <= refs.Count && refs[n - 1] != null)
                    cm.ReplyTo = "#" + list.IndexOf(refs[n - 1]);   // position among kept replies; the engine turns it into an id
                else if (knownIds != null && knownIds.Contains(rt)) cm.ReplyTo = rt;
                refs.Add(cm);
                list.Add(cm);
            }
            return list;
        }

        public static bool ContainsForbidden(string s)
        {
            var l = s.ToLowerInvariant();
            return ForbiddenPhrases.Any(f => l.Contains(f));
        }

        public static string Clean(string s) { return (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); }
        public static string Clip(string s, int n) { return s.Length <= n ? s : s.Substring(0, n); }
    }
}
