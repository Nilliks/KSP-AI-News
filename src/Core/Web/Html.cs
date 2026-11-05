using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KerbalNews.Web
{
    /// <summary>A node in the page tree: an element, or a text run (Tag == "#text").</summary>
    public class Node
    {
        public string Tag;
        public string Text = "";
        public Dictionary<string, string> Attrs = new Dictionary<string, string>();
        public List<Node> Children = new List<Node>();
        public Node Parent;
        /// <summary>Filled by the cascade.</summary>
        public Style Style;

        public bool IsText { get { return Tag == "#text"; } }
        public string Attr(string name, string def = "") { string v; return Attrs.TryGetValue(name, out v) ? v : def; }
        public string Id { get { return Attr("id"); } }

        public IEnumerable<string> Classes
        {
            get { return Attr("class").Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries); }
        }

        public IEnumerable<Node> Descendants()
        {
            foreach (var c in Children)
            {
                yield return c;
                foreach (var d in c.Descendants()) yield return d;
            }
        }

        public Node Find(string tag) { return Descendants().FirstOrDefault(n => n.Tag == tag); }

        public string InnerText()
        {
            if (IsText) return Text;
            var sb = new StringBuilder();
            foreach (var c in Children) sb.Append(c.InnerText());
            return sb.ToString();
        }
    }

    /// <summary>
    /// A forgiving HTML parser for the tag set the news sites use. It handles void elements,
    /// unclosed paragraphs and list items, comments, entities, and raw &lt;style&gt; blocks.
    /// </summary>
    public static class Html
    {
        static readonly HashSet<string> Void = new HashSet<string> { "br", "hr", "img", "input", "meta", "link" };
        static readonly HashSet<string> AutoClose = new HashSet<string> { "p", "li" };
        static readonly HashSet<string> Block = new HashSet<string>
        {
            "html", "body", "div", "section", "header", "footer", "nav", "article", "aside", "main", "h1", "h2", "h3", "h4", "h5", "h6",
            "p", "ul", "ol", "li", "hr", "blockquote", "figure", "figcaption", "form", "table", "tr", "td", "th", "button", "textarea", "input", "img", "select", "label",
        };

        public static bool IsBlock(string tag) { return Block.Contains(tag); }

        public static Node Parse(string html)
        {
            var root = new Node { Tag = "root" };
            var cur = root;
            int i = 0;
            html = html ?? "";
            while (i < html.Length)
            {
                if (html[i] == '<')
                {
                    if (string.CompareOrdinal(html, i, "<!--", 0, 4) == 0)
                    {
                        int end = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                        i = end < 0 ? html.Length : end + 3;
                        continue;
                    }
                    if (i + 1 < html.Length && html[i + 1] == '!')
                    {
                        int end = html.IndexOf('>', i);
                        i = end < 0 ? html.Length : end + 1;
                        continue;
                    }
                    if (i + 1 < html.Length && html[i + 1] == '/')
                    {
                        int end = html.IndexOf('>', i);
                        if (end < 0) break;
                        string name = html.Substring(i + 2, end - i - 2).Trim().ToLowerInvariant();
                        // Close up to the matching open tag; ignore stray closers.
                        for (var n = cur; n != null && n != root; n = n.Parent)
                            if (n.Tag == name) { cur = n.Parent; break; }
                        i = end + 1;
                        continue;
                    }
                    int close;
                    var el = ReadTag(html, i, out close);
                    if (el == null) { AddText(cur, "<"); i++; continue; }
                    i = close;
                    if (AutoClose.Contains(el.Tag))
                    {
                        // <p> inside <p>, or <li> after <li>: close the previous one first.
                        for (var n = cur; n != null && n != root; n = n.Parent)
                        {
                            if (n.Tag == el.Tag) { cur = n.Parent; break; }
                            if (IsBlock(n.Tag) && n.Tag != "span") break;
                        }
                    }
                    el.Parent = cur;
                    cur.Children.Add(el);
                    bool selfClosed = el.Attrs.ContainsKey("/");
                    el.Attrs.Remove("/");
                    if (el.Tag == "style" || el.Tag == "script" || el.Tag == "textarea")
                    {
                        string endTag = "</" + el.Tag;
                        int end = html.IndexOf(endTag, i, StringComparison.OrdinalIgnoreCase);
                        if (end < 0) end = html.Length;
                        string raw = html.Substring(i, end - i);
                        if (el.Tag != "script") el.Children.Add(new Node { Tag = "#text", Text = el.Tag == "textarea" ? Decode(raw) : raw, Parent = el });
                        int gt = html.IndexOf('>', end);
                        i = gt < 0 ? html.Length : gt + 1;
                        continue;
                    }
                    if (!Void.Contains(el.Tag) && !selfClosed) cur = el;
                    continue;
                }
                int next = html.IndexOf('<', i);
                if (next < 0) next = html.Length;
                AddText(cur, Decode(html.Substring(i, next - i)));
                i = next;
            }
            return root;
        }

        static void AddText(Node parent, string text)
        {
            if (text.Length == 0) return;
            var last = parent.Children.LastOrDefault();
            if (last != null && last.IsText) { last.Text += text; return; }
            parent.Children.Add(new Node { Tag = "#text", Text = text, Parent = parent });
        }

        static Node ReadTag(string s, int start, out int end)
        {
            end = start;
            int i = start + 1;
            int nameStart = i;
            while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '-')) i++;
            if (i == nameStart) return null;
            var el = new Node { Tag = s.Substring(nameStart, i - nameStart).ToLowerInvariant() };
            while (i < s.Length)
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                if (i >= s.Length) return null;
                if (s[i] == '>') { end = i + 1; return el; }
                if (s[i] == '/') { el.Attrs["/"] = ""; i++; continue; }
                int ns = i;
                while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '=' && s[i] != '>' && s[i] != '/') i++;
                string name = s.Substring(ns, i - ns).ToLowerInvariant();
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                string val = "";
                if (i < s.Length && s[i] == '=')
                {
                    i++;
                    while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                    if (i < s.Length && (s[i] == '"' || s[i] == '\''))
                    {
                        char q = s[i++];
                        int vs = i;
                        while (i < s.Length && s[i] != q) i++;
                        val = s.Substring(vs, Math.Min(i, s.Length) - vs);
                        i++;
                    }
                    else
                    {
                        int vs = i;
                        while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '>') i++;
                        val = s.Substring(vs, i - vs);
                    }
                }
                if (name != "") el.Attrs[name] = Decode(val);
            }
            return null;
        }

        static readonly Dictionary<string, string> Entities = new Dictionary<string, string>
        {
            { "amp", "&" }, { "lt", "<" }, { "gt", ">" }, { "quot", "\"" }, { "apos", "'" }, { "nbsp", " " },
            { "mdash", "—" }, { "ndash", "–" }, { "hellip", "…" }, { "middot", "·" }, { "bull", "•" },
            { "copy", "©" }, { "rsquo", "’" }, { "lsquo", "‘" }, { "rdquo", "”" }, { "ldquo", "“" },
            { "raquo", "»" }, { "laquo", "«" }, { "times", "×" }, { "deg", "°" },
            { "hearts", "♥" }, { "rarr", "→" }, { "larr", "←" }, { "uarr", "↑" }, { "darr", "↓" }, { "trade", "™" }, { "reg", "®" },
            { "check", "✓" }, { "star", "☆" }, { "starf", "★" }, { "para", "¶" }, { "sect", "§" }, { "plusmn", "±" }, { "minus", "−" },
        };

        public static string Decode(string s)
        {
            if (s.IndexOf('&') < 0) return s;
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '&')
                {
                    int semi = s.IndexOf(';', i);
                    if (semi > i && semi - i <= 10)
                    {
                        string name = s.Substring(i + 1, semi - i - 1);
                        string rep;
                        if (Entities.TryGetValue(name, out rep)) { sb.Append(rep); i = semi; continue; }
                        if (name.StartsWith("#"))
                        {
                            int code;
                            bool ok = name.StartsWith("#x") || name.StartsWith("#X")
                                ? int.TryParse(name.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)
                                : int.TryParse(name.Substring(1), out code);
                            if (ok && code > 0 && code < 0x110000) { sb.Append(char.ConvertFromUtf32(code)); i = semi; continue; }
                        }
                    }
                }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        public static string Escape(string s)
        {
            return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
