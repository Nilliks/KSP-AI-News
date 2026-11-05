using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KerbalNews.Web
{
    /// <summary>Computed style of one element: plain CSS property names to values.</summary>
    public class Style
    {
        public readonly Dictionary<string, string> Props = new Dictionary<string, string>();
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public string Get(string p, string def = null) { string v; return Props.TryGetValue(p, out v) ? v : def; }
        public bool Has(string p) { return Props.ContainsKey(p); }

        public float FontSize { get { return Length("font-size", 16, 16); } }

        /// <summary>Length in px. em is relative to this element's font size, % to the given basis.</summary>
        public float Length(string p, float def, float percentBasis = 0)
        {
            var v = Get(p);
            if (v == null) return def;
            return ParseLength(v, def, p == "font-size" ? def : FontSizeRaw(), percentBasis);
        }

        float FontSizeRaw()
        {
            var v = Get("font-size");
            return v == null ? 16 : ParseLength(v, 16, 16, 16);
        }

        public static float ParseLength(string v, float def, float em, float percentBasis)
        {
            v = (v ?? "").Trim().ToLowerInvariant();
            if (v == "" || v == "auto" || v == "none") return def;
            float f;
            if (v.EndsWith("px") && float.TryParse(v.Substring(0, v.Length - 2), NumberStyles.Float, Inv, out f)) return f;
            if (v.EndsWith("rem") && float.TryParse(v.Substring(0, v.Length - 3), NumberStyles.Float, Inv, out f)) return f * 16;
            if (v.EndsWith("em") && float.TryParse(v.Substring(0, v.Length - 2), NumberStyles.Float, Inv, out f)) return f * em;
            if (v.EndsWith("%") && float.TryParse(v.Substring(0, v.Length - 1), NumberStyles.Float, Inv, out f)) return f / 100f * percentBasis;
            if (v.EndsWith("pt") && float.TryParse(v.Substring(0, v.Length - 2), NumberStyles.Float, Inv, out f)) return f * 4f / 3f;
            if (float.TryParse(v, NumberStyles.Float, Inv, out f)) return f;
            return def;
        }

        public bool IsPercent(string p) { var v = Get(p); return v != null && v.Trim().EndsWith("%"); }

        public bool Bold
        {
            get
            {
                var w = Get("font-weight", "normal");
                int n;
                return w == "bold" || w == "bolder" || (int.TryParse(w, out n) && n >= 600);
            }
        }

        public bool Italic { get { return Get("font-style", "normal") == "italic"; } }
        public string Display { get { return Get("display", "block"); } }
        public bool Hidden { get { return Display == "none"; } }

        /// <summary>Colors as #RRGGBBAA, or null when unset/transparent.</summary>
        public string Color(string p)
        {
            var v = Get(p);
            return v == null ? null : Css.NormalizeColor(v);
        }
    }

    public class CssRule
    {
        public Selector Selector;
        public List<KeyValuePair<string, string>> Decls = new List<KeyValuePair<string, string>>();
        public int Order;
    }

    /// <summary>One compound selector part: tag, #id, .classes. Parts are joined by descendant (' ') or child ('>').</summary>
    public class SelectorPart
    {
        public string Tag;
        public string Id;
        public List<string> Classes = new List<string>();
        public bool ChildOfPrevious;
    }

    public class Selector
    {
        public List<SelectorPart> Parts = new List<SelectorPart>();
        public bool Hover;
        public int Specificity;

        public static Selector Parse(string text)
        {
            var sel = new Selector();
            text = text.Trim();
            if (text.EndsWith(":hover")) { sel.Hover = true; text = text.Substring(0, text.Length - 6); }
            text = text.Replace(">", " > ");
            bool child = false;
            foreach (var tok in text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (tok == ">") { child = true; continue; }
                var part = new SelectorPart { ChildOfPrevious = child };
                child = false;
                string t = tok;
                int colon = t.IndexOf(':');
                if (colon >= 0) t = t.Substring(0, colon); // other pseudo-classes are ignored
                int i = 0;
                while (i < t.Length)
                {
                    char c = t[i];
                    int j = i + 1;
                    while (j < t.Length && t[j] != '.' && t[j] != '#') j++;
                    string name = t.Substring(c == '.' || c == '#' ? i + 1 : i, j - (c == '.' || c == '#' ? i + 1 : i));
                    if (c == '.') { part.Classes.Add(name); sel.Specificity += 10; }
                    else if (c == '#') { part.Id = name; sel.Specificity += 100; }
                    else if (name != "*") { part.Tag = name.ToLowerInvariant(); sel.Specificity += 1; }
                    i = j;
                }
                sel.Parts.Add(part);
            }
            return sel;
        }

        static bool MatchPart(SelectorPart p, Node n)
        {
            if (n == null || n.IsText) return false;
            if (p.Tag != null && p.Tag != n.Tag) return false;
            if (p.Id != null && p.Id != n.Id) return false;
            if (p.Classes.Count > 0)
            {
                var cls = n.Classes.ToList();
                if (p.Classes.Any(c => !cls.Contains(c))) return false;
            }
            return true;
        }

        public bool Matches(Node n)
        {
            if (Parts.Count == 0 || !MatchPart(Parts[Parts.Count - 1], n)) return false;
            var cur = n;
            for (int i = Parts.Count - 2; i >= 0; i--)
            {
                bool child = Parts[i + 1].ChildOfPrevious;
                cur = cur.Parent;
                if (child)
                {
                    if (!MatchPart(Parts[i], cur)) return false;
                }
                else
                {
                    while (cur != null && !MatchPart(Parts[i], cur)) cur = cur.Parent;
                    if (cur == null) return false;
                }
            }
            return true;
        }
    }

    public class Stylesheet
    {
        public List<CssRule> Rules = new List<CssRule>();
        public Dictionary<string, string> Variables = new Dictionary<string, string>();
    }

    /// <summary>A small CSS engine: parse, match, cascade, inherit. Enough to theme a news site.</summary>
    public static class Css
    {
        static readonly HashSet<string> Inherited = new HashSet<string>
        {
            "color", "font-size", "font-weight", "font-style", "font-family", "text-align", "line-height",
            "letter-spacing", "text-transform", "white-space", "hover-color",
        };

        public const string UserAgent = @"
            body { font-size: 16px; color: #222222; line-height: 1.35; }
            h1 { font-size: 2em; font-weight: bold; margin-bottom: 10px; }
            h2 { font-size: 1.5em; font-weight: bold; margin-bottom: 8px; }
            h3 { font-size: 1.2em; font-weight: bold; margin-bottom: 6px; }
            h4 { font-size: 1.05em; font-weight: bold; margin-bottom: 4px; }
            p { margin-bottom: 10px; }
            a { color: #1a4f8b; }
            b, strong { font-weight: bold; }
            i, em { font-style: italic; }
            small { font-size: 0.85em; }
            ul, ol { padding-left: 18px; margin-bottom: 8px; }
            li { margin-bottom: 4px; }
            blockquote { padding-left: 14px; border-left: 3px solid #999999; margin-bottom: 10px; font-style: italic; }
            hr { border-top: 1px solid #bbbbbb; margin-top: 8px; margin-bottom: 8px; }
            button { padding: 6px 12px; background: #dddddd; color: #111111; }
            input, textarea { padding: 6px; background: #ffffff; color: #111111; border: 1px solid #999999; }
            span, a, b, strong, i, em, small, code, br, label { display: inline; }
        ";

        public static Stylesheet Parse(string css, Stylesheet into = null, int orderBase = 0)
        {
            var sheet = into ?? new Stylesheet();
            css = StripComments(css ?? "");
            int order = orderBase + sheet.Rules.Count;
            int i = 0;
            while (i < css.Length)
            {
                int open = css.IndexOf('{', i);
                if (open < 0) break;
                int close = css.IndexOf('}', open);
                if (close < 0) break;
                string selText = css.Substring(i, open - i).Trim();
                string body = css.Substring(open + 1, close - open - 1);
                i = close + 1;
                if (selText.StartsWith("@")) continue; // @media etc. are not supported
                var decls = ParseDecls(body);
                if (selText == ":root")
                {
                    foreach (var d in decls) if (d.Key.StartsWith("--")) sheet.Variables[d.Key] = d.Value;
                    continue;
                }
                foreach (var one in selText.Split(','))
                {
                    if (one.Trim() == "") continue;
                    sheet.Rules.Add(new CssRule { Selector = Selector.Parse(one), Decls = decls, Order = order++ });
                }
            }
            return sheet;
        }

        static string StripComments(string s)
        {
            while (true)
            {
                int a = s.IndexOf("/*", StringComparison.Ordinal);
                if (a < 0) return s;
                int b = s.IndexOf("*/", a + 2, StringComparison.Ordinal);
                s = s.Substring(0, a) + (b < 0 ? "" : s.Substring(b + 2));
            }
        }

        public static List<KeyValuePair<string, string>> ParseDecls(string body)
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (var raw in (body ?? "").Split(';'))
            {
                int colon = raw.IndexOf(':');
                if (colon <= 0) continue;
                string k = raw.Substring(0, colon).Trim().ToLowerInvariant();
                string v = raw.Substring(colon + 1).Trim();
                if (v.EndsWith("!important")) v = v.Substring(0, v.Length - 10).Trim();
                if (k == "" || v == "") continue;
                foreach (var e in Expand(k, v)) list.Add(e);
            }
            return list;
        }

        /// <summary>Expands shorthands (padding, margin, border, background) into longhands.</summary>
        static IEnumerable<KeyValuePair<string, string>> Expand(string k, string v)
        {
            if (k == "padding" || k == "margin")
            {
                var p = v.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string t = p[0], r = p.Length > 1 ? p[1] : t, b = p.Length > 2 ? p[2] : t, l = p.Length > 3 ? p[3] : r;
                yield return new KeyValuePair<string, string>(k + "-top", t);
                yield return new KeyValuePair<string, string>(k + "-right", r);
                yield return new KeyValuePair<string, string>(k + "-bottom", b);
                yield return new KeyValuePair<string, string>(k + "-left", l);
                yield break;
            }
            if (k == "border" || k == "border-top" || k == "border-bottom" || k == "border-left" || k == "border-right")
            {
                string width = "1px", color = "#000000";
                bool none = false;
                foreach (var tok in v.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (tok == "none" || tok == "0") none = true;
                    else if (char.IsDigit(tok[0])) width = tok;
                    else if (tok != "solid" && tok != "dashed" && tok != "dotted" && tok != "double") color = tok;
                }
                var sides = k == "border" ? new[] { "top", "right", "bottom", "left" } : new[] { k.Substring(7) };
                foreach (var side in sides)
                {
                    yield return new KeyValuePair<string, string>("border-" + side + "-width", none ? "0" : width);
                    yield return new KeyValuePair<string, string>("border-" + side + "-color", color);
                }
                yield break;
            }
            if (k == "background")
            {
                yield return new KeyValuePair<string, string>("background-color", v.Split(' ')[0]);
                yield break;
            }
            yield return new KeyValuePair<string, string>(k, v);
        }

        /// <summary>Applies the cascade to the whole tree: user-agent rules, then the site's sheet, then style="" attributes.</summary>
        public static void Apply(Node root, Stylesheet sheet)
        {
            var ua = Parse(UserAgent);
            Walk(root, null, ua, sheet);
        }

        static void Walk(Node n, Style parent, Stylesheet ua, Stylesheet sheet)
        {
            var st = new Style();
            if (parent != null)
                foreach (var kv in parent.Props)
                    if (Inherited.Contains(kv.Key)) st.Props[kv.Key] = kv.Value;
            if (!n.IsText)
            {
                if (!Html.IsBlock(n.Tag) && n.Tag != "root") st.Props["display"] = "inline";
                var matched = new List<KeyValuePair<CssRule, int>>();
                foreach (var r in ua.Rules) if (r.Selector.Matches(n)) matched.Add(new KeyValuePair<CssRule, int>(r, 0));
                if (sheet != null) foreach (var r in sheet.Rules) if (r.Selector.Matches(n)) matched.Add(new KeyValuePair<CssRule, int>(r, 1));
                foreach (var m in matched.OrderBy(x => x.Value).ThenBy(x => x.Key.Selector.Specificity).ThenBy(x => x.Key.Order))
                    foreach (var d in m.Key.Decls)
                        Set(st, m.Key.Selector.Hover ? "hover-" + d.Key : d.Key, d.Value, parent, sheet);
                var inline = n.Attr("style");
                if (inline != "") foreach (var d in ParseDecls(inline)) Set(st, d.Key, d.Value, parent, sheet);
            }
            n.Style = st;
            foreach (var c in n.Children) Walk(c, st, ua, sheet);
        }

        static void Set(Style st, string k, string v, Style parent, Stylesheet sheet)
        {
            v = Resolve(v, sheet);
            if (v == "inherit") { if (parent != null && parent.Has(k)) st.Props[k] = parent.Get(k); return; }
            if (k == "font-size")
            {
                // Resolve relative font sizes against the parent now, so children inherit px.
                float parentSize = parent != null ? parent.FontSize : 16;
                float px = Style.ParseLength(v, parentSize, parentSize, parentSize);
                if (v == "smaller") px = parentSize * 0.85f;
                if (v == "larger") px = parentSize * 1.2f;
                v = px.ToString("0.##", CultureInfo.InvariantCulture) + "px";
            }
            st.Props[k] = v;
        }

        static string Resolve(string v, Stylesheet sheet)
        {
            int guard = 0;
            while (sheet != null && v.Contains("var(") && guard++ < 8)
            {
                int a = v.IndexOf("var(", StringComparison.Ordinal);
                int b = v.IndexOf(')', a);
                if (b < 0) break;
                var inner = v.Substring(a + 4, b - a - 4).Split(',');
                string name = inner[0].Trim();
                string val;
                if (!sheet.Variables.TryGetValue(name, out val)) val = inner.Length > 1 ? inner[1].Trim() : "";
                v = v.Substring(0, a) + val + v.Substring(b + 1);
            }
            return v;
        }

        static readonly Dictionary<string, string> Named = new Dictionary<string, string>
        {
            { "black", "#000000" }, { "white", "#ffffff" }, { "red", "#ff0000" }, { "green", "#008000" }, { "blue", "#0000ff" },
            { "gray", "#808080" }, { "grey", "#808080" }, { "silver", "#c0c0c0" }, { "navy", "#000080" }, { "maroon", "#800000" },
            { "orange", "#ffa500" }, { "yellow", "#ffff00" }, { "gold", "#ffd700" }, { "teal", "#008080" }, { "purple", "#800080" },
            { "darkred", "#8b0000" }, { "crimson", "#dc143c" }, { "beige", "#f5f5dc" }, { "ivory", "#fffff0" }, { "whitesmoke", "#f5f5f5" },
            { "lightgray", "#d3d3d3" }, { "darkgray", "#a9a9a9" }, { "dimgray", "#696969" }, { "skyblue", "#87ceeb" },
        };

        /// <summary>Normalizes #rgb, #rrggbb, #rrggbbaa, rgb(), rgba() and names to #RRGGBBAA. Null for transparent or invalid.</summary>
        public static string NormalizeColor(string v)
        {
            v = (v ?? "").Trim().ToLowerInvariant();
            if (v == "" || v == "transparent" || v == "none") return null;
            string named;
            if (Named.TryGetValue(v, out named)) v = named;
            if (v.StartsWith("#"))
            {
                string h = v.Substring(1);
                if (h.Length == 3 || h.Length == 4) h = string.Concat(h.Select(c => new string(c, 2)).ToArray());
                if (h.Length == 6) h += "ff";
                if (h.Length != 8 || !h.All(Uri.IsHexDigit)) return null;
                return "#" + h.ToUpperInvariant();
            }
            if (v.StartsWith("rgb"))
            {
                int a = v.IndexOf('('), b = v.IndexOf(')');
                if (a < 0 || b < a) return null;
                var parts = v.Substring(a + 1, b - a - 1).Split(',', ' ', '/').Where(x => x.Trim() != "").ToArray();
                if (parts.Length < 3) return null;
                Func<string, int> ch = x => { float f; return float.TryParse(x.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? (int)Math.Max(0, Math.Min(255, x.Contains("%") ? f * 2.55f : f)) : 0; };
                int alpha = 255;
                if (parts.Length > 3) { float f; if (float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f)) alpha = (int)Math.Round(Math.Max(0, Math.Min(255, f <= 1 ? f * 255 : f))); }
                if (alpha == 0) return null;
                return "#" + ch(parts[0]).ToString("X2") + ch(parts[1]).ToString("X2") + ch(parts[2]).ToString("X2") + alpha.ToString("X2");
            }
            return null;
        }
    }
}
