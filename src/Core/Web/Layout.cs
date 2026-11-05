using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KerbalNews.Web
{
    // ------------------------------------------------------------------ output

    /// <summary>A styled piece of inline text. Runs are joined into one rich-text string per paragraph.</summary>
    public class Run
    {
        public string Text = "";
        public bool Bold, Italic, Underline, Strike, Mono;
        public string Color, HoverColor;
        public float Size = 16, LetterSpacing;
        /// <summary>Index into PageLayout.Links, or -1.</summary>
        public int Link = -1;
    }

    /// <summary>A paragraph of text, positioned in CSS pixels from the page's top-left.</summary>
    public class TextItem
    {
        public float X, Y, W, H;
        public float FontSize = 16, LineHeight = 1.3f;
        public string Family = "sans", Align = "left", Color = "#000000FF";
        public bool Bold, Italic;
        public List<Run> Runs = new List<Run>();
        /// <summary>Links that appear in this paragraph, for hover redraws.</summary>
        public IEnumerable<int> LinkIds { get { return Runs.Where(r => r.Link >= 0).Select(r => r.Link).Distinct(); } }
        public string Plain { get { return string.Concat(Runs.Select(r => r.Text).ToArray()); } }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>The paragraph as TextMeshPro rich text. Runs in the hovered link use their hover color.</summary>
        public string Build(int hoverLink = -1)
        {
            var sb = new StringBuilder();
            foreach (var r in Runs)
            {
                var close = new List<string>();
                if (r.Link >= 0) { sb.Append("<link=\"" + r.Link.ToString(Inv) + "\">"); close.Add("</link>"); }
                string color = r.Link >= 0 && r.Link == hoverLink && r.HoverColor != null ? r.HoverColor : r.Color;
                if (color != null && color != Color) { sb.Append("<color=" + color + ">"); close.Add("</color>"); }
                if (Math.Abs(r.Size - FontSize) > 0.01f) { sb.Append("<size=" + (r.Size / FontSize * 100f).ToString("0.#", Inv) + "%>"); close.Add("</size>"); }
                if (r.Bold && !Bold) { sb.Append("<b>"); close.Add("</b>"); }
                if (r.Italic && !Italic) { sb.Append("<i>"); close.Add("</i>"); }
                if (r.Underline) { sb.Append("<u>"); close.Add("</u>"); }
                if (r.Strike) { sb.Append("<s>"); close.Add("</s>"); }
                if (r.Mono) { sb.Append("<mspace=0.6em>"); close.Add("</mspace>"); }
                if (Math.Abs(r.LetterSpacing) > 0.01f) { sb.Append("<cspace=" + (r.LetterSpacing / r.Size).ToString("0.###", Inv) + "em>"); close.Add("</cspace>"); }
                sb.Append(Escape(r.Text));
                for (int i = close.Count - 1; i >= 0; i--) sb.Append(close[i]);
            }
            return sb.ToString();
        }

        /// <summary>Keeps text from the AI or the player from being read as rich-text tags.</summary>
        public static string Escape(string s) { return (s ?? "").Replace("<", "<noparse><</noparse>"); }
    }

    public class RectItem
    {
        public float X, Y, W, H;
        public string Color, HoverColor;
        public int Link = -1;
    }

    public class ImageItem { public float X, Y, W, H; public string Src = "", Alt = ""; }

    public class InputItem
    {
        public float X, Y, W, H;
        public string Name = "", Value = "", Placeholder = "";
        public bool Multiline, Password;
        public float FontSize = 16;
        public string Family = "sans", Color = "#111111FF";
        public float PadX = 6, PadY = 6;
    }

    /// <summary>Where a click goes: an href (a URL or "action:..."), or a button's data-action.</summary>
    public class LinkTarget { public string Href = "", Action = "", Title = ""; }

    /// <summary>A clickable block area (buttons, block links). Inline links are found through the text instead.</summary>
    public class Region { public float X, Y, W, H; public int Link; }

    public class PageLayout
    {
        public float Width, Height;
        public string Background = "#FFFFFFFF";
        public List<RectItem> Rects = new List<RectItem>();
        public List<TextItem> Texts = new List<TextItem>();
        public List<ImageItem> Images = new List<ImageItem>();
        public List<InputItem> Inputs = new List<InputItem>();
        public List<LinkTarget> Links = new List<LinkTarget>();
        public List<Region> Regions = new List<Region>();

        /// <summary>The innermost clickable block at a point, or -1.</summary>
        public int RegionAt(float x, float y)
        {
            for (int i = Regions.Count - 1; i >= 0; i--)
            {
                var r = Regions[i];
                if (x >= r.X && x < r.X + r.W && y >= r.Y && y < r.Y + r.H) return r.Link;
            }
            return -1;
        }
    }

    /// <summary>Text measurement is the only thing the layout needs from the game: TextMeshPro there, an estimate in tests.</summary>
    public delegate void MeasureText(TextItem text, float maxWidth, out float width, out float height);

    // ------------------------------------------------------------------ engine

    /// <summary>
    /// Lays a styled page out into positioned boxes and paragraphs: block flow with margins, padding and borders,
    /// one-row and wrapping flex rows (gap, flex-grow, widths, align-items), inline text runs and form fields.
    /// The game layer only draws what this produces, so layout bugs show up in unit tests.
    /// </summary>
    public class Layout
    {
        readonly MeasureText measure;
        readonly PageLayout page = new PageLayout();
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const float Unbounded = 100000;

        static readonly HashSet<string> Skip = new HashSet<string> { "head", "style", "script", "title", "meta", "link" };
        static readonly HashSet<string> Shrink = new HashSet<string> { "button", "img" };

        Layout(MeasureText m) { measure = m; }

        public static PageLayout Run(Node root, float width, MeasureText measure)
        {
            var l = new Layout(measure);
            var body = root.Descendants().FirstOrDefault(n => n.Tag == "body") ?? root;
            var html = root.Descendants().FirstOrDefault(n => n.Tag == "html");
            l.page.Width = width;
            l.page.Background = body.Style.Color("background-color") ?? (html != null ? html.Style.Color("background-color") : null) ?? "#FFFFFFFF";
            float h;
            // The body's own background is the page background, not a rect.
            l.Box(body, 0, 0, width, -1, out h, true);
            l.page.Height = h;
            return l.page;
        }

        // ------------------------------------------------------------ box model

        float Px(Node n, string p, float basis = 0) { return n.Style == null ? 0 : n.Style.Length(p, 0, basis); }
        float Pl(Node n) { return Px(n, "padding-left") + Px(n, "border-left-width"); }
        float Pr(Node n) { return Px(n, "padding-right") + Px(n, "border-right-width"); }
        float Pt(Node n) { return Px(n, "padding-top") + Px(n, "border-top-width"); }
        float Pb(Node n) { return Px(n, "padding-bottom") + Px(n, "border-bottom-width"); }
        float Hpad(Node n) { return Pl(n) + Pr(n); }

        static bool Auto(Node n, string p) { var v = n.Style.Get(p); return v != null && v.Trim() == "auto"; }

        bool IsFlex(Node n) { return n.Style.Display == "flex" || n.Style.Display == "inline-flex"; }

        /// <summary>Border-box width a block takes in a container of width cw, or -1 to fill.</summary>
        float SpecifiedWidth(Node n, float cw)
        {
            if (n.Style.Has("width") && !Auto(n, "width")) return n.Style.Length("width", 0, cw) + Hpad(n);
            if (n.Tag == "img" && n.Attr("width") != "") return Style.ParseLength(n.Attr("width"), 64, 16, cw) + Hpad(n);
            return -1;
        }

        float ClampWidth(Node n, float w, float cw)
        {
            if (n.Style.Has("max-width")) w = Math.Min(w, n.Style.Length("max-width", Unbounded, cw) + Hpad(n));
            if (n.Style.Has("min-width")) w = Math.Max(w, n.Style.Length("min-width", 0, cw) + Hpad(n));
            return Math.Max(0, w);
        }

        /// <summary>A box's pieces that a flex row may stretch to the row's height.</summary>
        class BoxRec { public RectItem Bg, BorderBottom, BorderLeft, BorderRight; public Region Region; public float Y, H; }

        int NewLink(Node n)
        {
            string href = n.Tag == "a" ? n.Attr("href") : "";
            string action = n.Attr("data-action");
            if (href == "" && action == "") return -1;
            page.Links.Add(new LinkTarget { Href = href, Action = action, Title = n.Attr("title") });
            return page.Links.Count - 1;
        }

        /// <summary>Lays out one element at (x, y) with border-box width w. Returns its border-box height.</summary>
        BoxRec Box(Node n, float x, float y, float w, int link, out float h, bool isBody = false)
        {
            var rec = new BoxRec { Y = y };
            var st = n.Style;
            int own = NewLink(n);
            if (own >= 0) link = own;

            string bg = isBody ? null : st.Color("background-color");
            if (bg != null || st.Has("hover-background-color"))
            {
                rec.Bg = new RectItem { X = x, Y = y, W = w, Color = bg, HoverColor = st.Color("hover-background-color"), Link = link };
                page.Rects.Add(rec.Bg);
            }

            float cx = x + Pl(n), cy = y + Pt(n), cw = Math.Max(0, w - Hpad(n));
            float ch;
            if (n.Tag == "input" || n.Tag == "textarea") ch = Input(n, cx, cy, cw);
            else if (n.Tag == "img") ch = Image(n, cx, cy, cw);
            else if (IsFlex(n)) ch = Flex(n, cx, cy, cw, link);
            else ch = Flow(n, n.Children, cx, cy, cw, link);

            if (st.Has("height") && !Auto(n, "height") && !st.IsPercent("height")) ch = st.Length("height", ch);
            if (st.Has("min-height")) ch = Math.Max(ch, st.Length("min-height", 0));
            h = Pt(n) + ch + Pb(n);
            rec.H = h;
            if (rec.Bg != null) rec.Bg.H = h;

            Border(n, "top", x, y, w, Px(n, "border-top-width"));
            rec.BorderBottom = Border(n, "bottom", x, y + h - Px(n, "border-bottom-width"), w, Px(n, "border-bottom-width"));
            rec.BorderLeft = Border(n, "left", x, y, Px(n, "border-left-width"), h);
            rec.BorderRight = Border(n, "right", x + w - Px(n, "border-right-width"), y, Px(n, "border-right-width"), h);

            if (own >= 0)
            {
                rec.Region = new Region { X = x, Y = y, W = w, H = h, Link = own };
                page.Regions.Add(rec.Region);
            }
            return rec;
        }

        RectItem Border(Node n, string side, float x, float y, float w, float h)
        {
            float bw = Px(n, "border-" + side + "-width");
            string c = n.Style.Color("border-" + side + "-color");
            if (bw <= 0 || c == null) return null;
            var r = new RectItem { X = x, Y = y, W = side == "top" || side == "bottom" ? w : bw, H = side == "top" || side == "bottom" ? bw : h, Color = c };
            page.Rects.Add(r);
            return r;
        }

        float LineHeight(Style st)
        {
            var v = st.Get("line-height");
            if (v == null || v == "normal") return 1.3f;
            float f;
            if (float.TryParse(v.Trim(), NumberStyles.Float, Inv, out f)) return f;
            return Math.Max(0.8f, Style.ParseLength(v, st.FontSize * 1.3f, st.FontSize, st.FontSize) / st.FontSize);
        }

        float Input(Node n, float x, float y, float w)
        {
            var st = n.Style;
            bool multi = n.Tag == "textarea";
            int rows = 1;
            if (multi && !int.TryParse(n.Attr("rows", "4"), out rows)) rows = 4;
            float lh = st.FontSize * LineHeight(st);
            float h = lh * Math.Max(1, rows);
            // The field's own padding and border are part of the box; the game draws the text inside the content area.
            page.Inputs.Add(new InputItem
            {
                X = x, Y = y, W = w, H = h,
                Name = n.Attr("name"), Value = multi ? n.InnerText() : n.Attr("value"), Placeholder = n.Attr("placeholder"),
                Multiline = multi, Password = n.Attr("type") == "password",
                FontSize = st.FontSize, Family = st.Get("font-family", "sans"), Color = st.Color("color") ?? "#111111FF",
            });
            return h;
        }

        float Image(Node n, float x, float y, float w)
        {
            float iw = Math.Min(w, n.Attr("width") != "" ? Style.ParseLength(n.Attr("width"), w, 16, w) : w);
            float ih = n.Style.Has("height") ? n.Style.Length("height", iw) : n.Attr("height") != "" ? Style.ParseLength(n.Attr("height"), iw, 16, w) : iw;
            var ar = n.Style.Get("aspect-ratio");
            if (ar != null && !n.Style.Has("height"))
            {
                var parts = ar.Split('/');
                float a, b = 1;
                if (float.TryParse(parts[0].Trim(), NumberStyles.Float, Inv, out a) && (parts.Length < 2 || float.TryParse(parts[1].Trim(), NumberStyles.Float, Inv, out b)) && a > 0 && b > 0)
                    ih = iw * b / a;
            }
            page.Images.Add(new ImageItem { X = x, Y = y, W = iw, H = ih, Src = n.Attr("src"), Alt = n.Attr("alt") });
            return ih;
        }

        // ------------------------------------------------------------ block flow

        bool Hidden(Node n) { return !n.IsText && (Skip.Contains(n.Tag) || (n.Style != null && n.Style.Hidden)); }

        /// <summary>True for text and inline elements whose whole subtree is inline.</summary>
        bool IsInline(Node n)
        {
            if (n.IsText) return true;
            if (n.Style.Display != "inline") return false;
            return n.Children.All(c => Hidden(c) || IsInline(c));
        }

        static bool Blank(Node n) { return n.IsText && n.Text.Trim() == ""; }

        float Flow(Node parent, List<Node> children, float x, float y, float w, int link)
        {
            float cur = y, prevMargin = 0;
            bool first = true;
            var group = new List<Node>();
            string prefix = parent.Tag == "li" ? (parent.Parent != null && parent.Parent.Tag == "ol" ? (parent.Parent.Children.Where(c => c.Tag == "li").ToList().IndexOf(parent) + 1) + ". " : "• ") : null;

            Action flush = () =>
            {
                if (group.Count == 0) return;
                var t = Paragraph(parent, group, x, cur, w, link, prefix);
                group.Clear();
                if (t == null) return;
                prefix = null;
                cur += t.H;
                prevMargin = 0;
                first = false;
            };

            foreach (var c in children)
            {
                if (Hidden(c)) continue;
                if (IsInline(c)) { group.Add(c); continue; }
                if (group.Any(g => !Blank(g))) flush(); else group.Clear();

                float mt = Px(c, "margin-top"), mb = Px(c, "margin-bottom");
                // Adjacent vertical margins collapse to the larger one; the first child's top margin stays inside.
                cur += first ? mt : Math.Max(0, mt - prevMargin);
                float bw = BlockWidth(c, w);
                float bx = x + Px(c, "margin-left");
                if (Auto(c, "margin-left") && Auto(c, "margin-right")) bx = x + (w - bw) / 2;
                else if (Auto(c, "margin-left")) bx = x + w - bw - Px(c, "margin-right");
                float bh;
                Box(c, bx, cur, bw, link, out bh);
                cur += bh + mb;
                prevMargin = mb;
                first = false;
            }
            flush();
            // The last child's bottom margin stays inside the parent, like a padded box.
            return cur - y;
        }

        float BlockWidth(Node c, float w)
        {
            float avail = Math.Max(0, w - Px(c, "margin-left") - Px(c, "margin-right"));
            float sw = SpecifiedWidth(c, w);
            float bw = sw >= 0 ? sw : Shrink.Contains(c.Tag) ? Math.Min(avail, MaxContent(c, avail)) : avail;
            return Math.Min(ClampWidth(c, bw, w), Math.Max(avail, sw >= 0 ? sw : 0));
        }

        TextItem Paragraph(Node parent, List<Node> nodes, float x, float y, float w, int link, string prefix)
        {
            var st = parent.Style;
            var t = NewText(st);
            if (prefix != null) t.Runs.Add(MakeRun(st, prefix, link));
            bool space = true;
            foreach (var n in nodes) Collect(n, t.Runs, link, ref space);
            // Trim the trailing space.
            for (int i = t.Runs.Count - 1; i >= 0; i--)
            {
                var r = t.Runs[i];
                r.Text = r.Text.TrimEnd(' ');
                if (r.Text != "") break;
            }
            t.Runs.RemoveAll(r => r.Text == "");
            if (t.Runs.Count == 0 || t.Plain.Trim() == "" && prefix == null) return null;
            float mw, mh;
            measure(t, w, out mw, out mh);
            t.X = x; t.Y = y; t.W = w; t.H = mh;
            page.Texts.Add(t);
            return t;
        }

        static TextItem NewText(Style st)
        {
            return new TextItem
            {
                FontSize = st.FontSize, Family = st.Get("font-family", "sans"), Align = st.Get("text-align", "left"),
                Color = st.Color("color") ?? "#000000FF", Bold = st.Bold, Italic = st.Italic,
            };
        }

        TextItem NewTextWithLh(Style st) { var t = NewText(st); t.LineHeight = LineHeight(st); return t; }

        Run MakeRun(Style st, string text, int link)
        {
            var deco = st.Get("text-decoration", "");
            var r = new Run
            {
                Text = text, Bold = st.Bold, Italic = st.Italic, Size = st.FontSize, Link = link,
                Color = st.Color("color"), HoverColor = st.Color("hover-color"),
                Underline = deco.Contains("underline"), Strike = deco.Contains("line-through"),
                Mono = st.Get("font-family", "") == "mono",
                LetterSpacing = st.Length("letter-spacing", 0),
            };
            var tt = st.Get("text-transform", "");
            if (tt == "uppercase") r.Text = r.Text.ToUpperInvariant();
            else if (tt == "lowercase") r.Text = r.Text.ToLowerInvariant();
            return r;
        }

        void Collect(Node n, List<Run> runs, int link, ref bool space)
        {
            if (Hidden(n)) return;
            if (n.IsText)
            {
                var st = n.Parent != null ? n.Parent.Style : n.Style;
                bool pre = st != null && (st.Get("white-space", "") == "pre" || st.Get("white-space", "") == "pre-wrap");
                string s = pre ? n.Text : Collapse(n.Text, ref space);
                if (s != "") runs.Add(MakeRun(st ?? new Style(), s, link));
                return;
            }
            if (n.Tag == "br") { runs.Add(MakeRun(n.Style, "\n", link)); space = true; return; }
            int own = NewLink(n);
            foreach (var c in n.Children) Collect(c, runs, own >= 0 ? own : link, ref space);
        }

        static string Collapse(string s, ref bool space)
        {
            var sb = new StringBuilder();
            foreach (char ch in s)
            {
                if (ch == ' ' || ch == '\n' || ch == '\r' || ch == '\t')
                {
                    if (!space) sb.Append(' ');
                    space = true;
                }
                else { sb.Append(ch); space = false; }
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------ flex

        class Item { public Node N; public float Basis, Grow, Ml, Mr, Mt, Mb, W, H, Max = Unbounded, Min; public bool Fixed; public BoxRec Rec; public int Start, End; }

        float Gap(Node n, string which, float basis)
        {
            if (n.Style.Has(which)) return n.Style.Length(which, 0, basis);
            var g = n.Style.Get("gap");
            if (g == null) return 0;
            var parts = g.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string v = which == "row-gap" ? parts[0] : parts[parts.Length - 1];
            return Style.ParseLength(v, 0, n.Style.FontSize, basis);
        }

        static float Grow(Node n)
        {
            float g;
            var v = n.Style.Get("flex-grow") ?? n.Style.Get("flex");
            if (v == null) return 0;
            var first = v.Trim().Split(' ')[0];
            if (first == "auto") return 1;
            if (first == "none") return 0;
            return float.TryParse(first, NumberStyles.Float, Inv, out g) ? g : 0;
        }

        /// <summary>Flex items: element children, plus loose text wrapped in an anonymous box.</summary>
        List<Node> FlexChildren(Node n)
        {
            var list = new List<Node>();
            foreach (var c in n.Children)
            {
                if (Hidden(c) || Blank(c)) continue;
                if (c.IsText)
                {
                    var anon = new Node { Tag = "span", Parent = n, Style = InheritOnly(n.Style) };
                    anon.Children.Add(new Node { Tag = "#text", Text = c.Text, Parent = anon, Style = anon.Style });
                    list.Add(anon);
                }
                else list.Add(c);
            }
            return list;
        }

        static readonly string[] TextProps = { "color", "font-size", "font-weight", "font-style", "font-family", "text-align", "line-height", "letter-spacing", "text-transform", "white-space", "hover-color" };

        static Style InheritOnly(Style parent)
        {
            var st = new Style();
            foreach (var k in TextProps) if (parent.Has(k)) st.Props[k] = parent.Get(k);
            st.Props["display"] = "block";
            return st;
        }

        float Flex(Node n, float x, float y, float w, int link)
        {
            bool column = n.Style.Get("flex-direction", "row").StartsWith("column");
            if (column) return Flow(n, n.Children, x, y, w, link);   // a column is close enough to block flow here
            bool wrap = n.Style.Get("flex-wrap", "nowrap") == "wrap";
            float gapX = Gap(n, "column-gap", w), gapY = Gap(n, "row-gap", w);
            string align = n.Style.Get("align-items", "stretch");
            string justify = n.Style.Get("justify-content", "flex-start");

            var items = new List<Item>();
            foreach (var c in FlexChildren(n))
            {
                var it = new Item { N = c, Grow = Grow(c), Ml = Px(c, "margin-left"), Mr = Px(c, "margin-right"), Mt = Px(c, "margin-top"), Mb = Px(c, "margin-bottom") };
                float sw = SpecifiedWidth(c, w);
                if (sw >= 0) { it.Basis = sw; it.Fixed = true; }
                else it.Basis = it.Grow > 0 ? Hpad(c) : MaxContent(c, w);
                if (c.Style.Has("max-width")) it.Max = c.Style.Length("max-width", Unbounded, w) + Hpad(c);
                it.Basis = Math.Min(it.Basis, it.Max);
                it.Min = it.Fixed ? it.Basis : Math.Min(it.Basis, MinContent(c));
                items.Add(it);
            }
            if (items.Count == 0) return 0;

            // Break into lines.
            var lines = new List<List<Item>>();
            var line = new List<Item>();
            float used = 0;
            foreach (var it in items)
            {
                float outer = it.Basis + it.Ml + it.Mr;
                if (wrap && line.Count > 0 && used + gapX + outer > w + 0.5f) { lines.Add(line); line = new List<Item>(); used = 0; }
                used += (line.Count > 0 ? gapX : 0) + outer;
                line.Add(it);
            }
            lines.Add(line);

            float cur = y;
            for (int li = 0; li < lines.Count; li++)
            {
                var ln = lines[li];
                float gaps = gapX * (ln.Count - 1);
                float sum = ln.Sum(i => i.Basis + i.Ml + i.Mr);
                float free = w - gaps - sum;
                foreach (var it in ln) it.W = it.Basis;
                if (free > 0)
                {
                    // Grow, re-sharing what an item can't take because of max-width.
                    for (int pass = 0; pass < 3 && free > 0.5f; pass++)
                    {
                        var growers = ln.Where(i => i.Grow > 0 && i.W < i.Max - 0.01f).ToList();
                        float total = growers.Sum(i => i.Grow);
                        if (total <= 0) break;
                        float left = free;
                        foreach (var it in growers)
                        {
                            float add = Math.Min(free * it.Grow / total, it.Max - it.W);
                            it.W += add;
                            left -= add;
                        }
                        free = left;
                    }
                }
                else if (free < 0)
                {
                    var shrinkers = ln.Where(i => !i.Fixed).ToList();
                    float total = shrinkers.Sum(i => i.W);
                    if (total > 0)
                    {
                        // Shrink in proportion to size, but not below the longest word; then share what's left.
                        float need = -free;
                        for (int pass = 0; pass < 3 && need > 0.5f; pass++)
                        {
                            var able = shrinkers.Where(i => i.W > i.Min + 0.5f).ToList();
                            float tot = able.Sum(i => i.W);
                            if (tot <= 0) break;
                            float took = 0;
                            foreach (var it in able)
                            {
                                float cut = Math.Min(it.W - it.Min, need * it.W / tot);
                                it.W -= cut;
                                took += cut;
                            }
                            need -= took;
                        }
                    }
                    free = w - gaps - ln.Sum(i => i.W + i.Ml + i.Mr);
                }

                float startX = x, between = gapX;
                if (free > 0.5f)
                {
                    if (justify == "center") startX += free / 2;
                    else if (justify == "flex-end" || justify == "end") startX += free;
                    else if (justify == "space-between" && ln.Count > 1) between += free / (ln.Count - 1);
                }

                // Lay out each item at the top of the line, then align.
                float px = startX, lineH = 0;
                foreach (var it in ln)
                {
                    it.Start = Mark();
                    float h;
                    it.Rec = Box(it.N, px + it.Ml, cur + it.Mt, it.W, link, out h);
                    it.End = Mark();
                    it.H = h;
                    lineH = Math.Max(lineH, h + it.Mt + it.Mb);
                    px += it.Ml + it.W + it.Mr + between;
                }
                for (int k = 0; k < ln.Count; k++)
                {
                    var it = ln[k];
                    var self = it.N.Style.Get("align-self", align);
                    float room = lineH - it.Mt - it.Mb - it.H;
                    if (room <= 0.01f) continue;
                    if (self == "center") Shift(it.Start, it.End, room / 2);
                    else if (self == "flex-end" || self == "end") Shift(it.Start, it.End, room);
                    else if (self == "stretch" && !it.N.Style.Has("height")) Stretch(it.Rec, room);
                }
                cur += lineH + (li < lines.Count - 1 ? gapY : 0);
            }
            return cur - y;
        }

        // Marks and shifts let a flex row move an item's whole subtree after laying it out.
        struct MarkSet { public int R, T, I, N, G; }
        readonly List<MarkSet> marks = new List<MarkSet>();

        int Mark()
        {
            marks.Add(new MarkSet { R = page.Rects.Count, T = page.Texts.Count, I = page.Images.Count, N = page.Inputs.Count, G = page.Regions.Count });
            return marks.Count - 1;
        }

        /// <summary>Moves everything one item drew (between two marks) down by dy.</summary>
        void Shift(int from, int to, float dy)
        {
            var a = marks[from];
            var b = marks[to];
            for (int i = a.R; i < b.R; i++) page.Rects[i].Y += dy;
            for (int i = a.T; i < b.T; i++) page.Texts[i].Y += dy;
            for (int i = a.I; i < b.I; i++) page.Images[i].Y += dy;
            for (int i = a.N; i < b.N; i++) page.Inputs[i].Y += dy;
            for (int i = a.G; i < b.G; i++) page.Regions[i].Y += dy;
        }

        static void Stretch(BoxRec r, float dy)
        {
            if (r.Bg != null) r.Bg.H += dy;
            if (r.Region != null) r.Region.H += dy;
            if (r.BorderBottom != null) r.BorderBottom.Y += dy;
            if (r.BorderLeft != null) r.BorderLeft.H += dy;
            if (r.BorderRight != null) r.BorderRight.H += dy;
        }

        // ------------------------------------------------------------ intrinsic sizes

        /// <summary>The narrowest an element can get: its longest word, plus padding.</summary>
        float MinContent(Node n)
        {
            if (Hidden(n)) return 0;
            if (n.Tag == "input" || n.Tag == "textarea" || n.Tag == "img") return Math.Min(MaxContent(n, Unbounded), 60);
            string longest = n.InnerText().Split(new[] { ' ', '\n', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries).OrderByDescending(x => x.Length).FirstOrDefault() ?? "";
            if (longest == "") return Hpad(n);
            var t = NewTextWithLh(n.Style);
            var d = n.Descendants().FirstOrDefault(x => x.IsText && x.Text.Contains(longest));
            var st = d != null && d.Parent != null ? d.Parent.Style : n.Style;
            t.Runs.Add(MakeRun(st, longest, -1));
            float mw, mh;
            measure(t, Unbounded, out mw, out mh);
            return Math.Min(mw + 1, Unbounded) + Hpad(n);
        }

        /// <summary>The border-box width an element wants with no wrapping, capped at the available width.</summary>
        float MaxContent(Node n, float avail)
        {
            if (Hidden(n)) return 0;
            float sw = SpecifiedWidth(n, avail);
            if (sw >= 0) return sw;
            float inner;
            if (n.Tag == "input" || n.Tag == "textarea") inner = Math.Min(avail, 240);
            else if (n.Tag == "img") inner = 64;
            else if (IsFlex(n))
            {
                var kids = FlexChildren(n);
                inner = kids.Sum(c => MaxContent(c, avail) + Px(c, "margin-left") + Px(c, "margin-right")) + Gap(n, "column-gap", avail) * Math.Max(0, kids.Count - 1);
            }
            else
            {
                inner = 0;
                var group = new List<Node>();
                foreach (var c in n.Children.Concat(new Node[] { null }))
                {
                    if (c != null && Hidden(c)) continue;
                    if (c != null && IsInline(c)) { group.Add(c); continue; }
                    if (group.Count > 0)
                    {
                        var t = NewTextWithLh(n.Style);
                        bool space = true;
                        foreach (var g in group) Collect(g, t.Runs, -1, ref space);
                        if (t.Plain.Trim() != "")
                        {
                            float mw, mh;
                            measure(t, Unbounded, out mw, out mh);
                            inner = Math.Max(inner, mw + 1);
                        }
                        group.Clear();
                    }
                    if (c != null) inner = Math.Max(inner, MaxContent(c, avail) + Px(c, "margin-left") + Px(c, "margin-right"));
                }
            }
            return Math.Min(ClampWidth(n, inner + Hpad(n), avail), Math.Max(avail, Hpad(n)));
        }
    }
}
