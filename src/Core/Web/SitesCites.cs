using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace KerbalNews.Web
{
    /// <summary>Citations: writers mark another outlet's article as [[a12]] or a Snackboard thread as [[t7]]; pages show a link.</summary>
    public partial class Sites
    {
        static readonly Regex CiteRx = new Regex(@"\[\[\s*([at]\d+)\s*\]\]", RegexOptions.Compiled);

        /// <summary>HTML for player or AI text, with citations turned into links. Unknown or unpublished ids are dropped.</summary>
        public string Linked(string text)
        {
            text = text ?? "";
            var sb = new System.Text.StringBuilder();
            int last = 0;
            double now = Now();
            foreach (Match m in CiteRx.Matches(text))
            {
                sb.Append(Html.Escape(text.Substring(last, m.Index - last)));
                last = m.Index + m.Length;
                string id = m.Groups[1].Value;
                if (id[0] == 'a')
                {
                    var a = State.Articles.FirstOrDefault(x => x.Id == id && x.PublishedUT <= now);
                    if (a != null) sb.Append("<a class=\"cite\" href=\"" + Html.Escape(ArticleUrl(a)) + "\">" + Html.Escape(Outlets.NameOf(a.Outlet) + ": “" + a.Headline + "”") + "</a>");
                }
                else
                {
                    var t = State.Forum.FirstOrDefault(x => x.Id == id && x.UT <= now);
                    if (t != null) sb.Append("<a class=\"cite\" href=\"" + Html.Escape(ForumHost + "/t/" + t.Id) + "\">" + Html.Escape("Snackboard: “" + t.Title + "”") + "</a>");
                }
            }
            sb.Append(Html.Escape(text.Substring(last)));
            return sb.ToString();
        }

        /// <summary>Plain text with citations written out, for excerpts and prompts.</summary>
        public static string Unlinked(NewsState state, string text)
        {
            return CiteRx.Replace(text ?? "", m =>
            {
                string id = m.Groups[1].Value;
                var a = id[0] == 'a' ? state.Articles.FirstOrDefault(x => x.Id == id) : null;
                if (a != null) return Outlets.NameOf(a.Outlet) + "'s \"" + a.Headline + "\"";
                var t = id[0] == 't' ? state.Forum.FirstOrDefault(x => x.Id == id) : null;
                return t != null ? "the Snackboard thread \"" + t.Title + "\"" : "";
            });
        }
    }
}
