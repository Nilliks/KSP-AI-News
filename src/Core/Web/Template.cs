using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KerbalNews.Web
{
    /// <summary>
    /// Mustache-style templates so site designs stay plain HTML:
    /// {{name}} (HTML-escaped), {{{name}}} (raw), {{#list}}...{{/list}} (loop or if),
    /// {{^name}}...{{/name}} (if not), {{&gt; partial}} (include). Dotted names walk objects;
    /// names not found in the current item are looked up in enclosing scopes.
    /// </summary>
    public static class Template
    {
        public static string Render(string template, object model, Func<string, string> partials = null)
        {
            var sb = new StringBuilder();
            var stack = new List<object> { model };
            Render(template ?? "", 0, (template ?? "").Length, stack, sb, partials, 0);
            return sb.ToString();
        }

        static void Render(string t, int start, int end, List<object> stack, StringBuilder sb, Func<string, string> partials, int depth)
        {
            if (depth > 12) return;
            int i = start;
            while (i < end)
            {
                int open = t.IndexOf("{{", i, StringComparison.Ordinal);
                if (open < 0 || open >= end) { sb.Append(t, i, end - i); return; }
                sb.Append(t, i, open - i);
                bool triple = open + 2 < t.Length && t[open + 2] == '{';
                int close = t.IndexOf(triple ? "}}}" : "}}", open, StringComparison.Ordinal);
                if (close < 0) { sb.Append(t, open, end - open); return; }
                string tag = t.Substring(open + (triple ? 3 : 2), close - open - (triple ? 3 : 2)).Trim();
                i = close + (triple ? 3 : 2);
                if (tag.Length == 0) continue;
                char kind = tag[0];
                if (kind == '!') continue;
                if (kind == '>')
                {
                    if (partials != null) Render(partials(tag.Substring(1).Trim()) ?? "", stack[stack.Count - 1], partials, sb, depth + 1);
                    continue;
                }
                if (kind == '#' || kind == '^')
                {
                    string name = tag.Substring(1).Trim();
                    int bodyEnd, after;
                    FindClose(t, i, end, name, out bodyEnd, out after);
                    object v = Lookup(stack, name);
                    if (kind == '^')
                    {
                        if (!Truthy(v)) Render(t, i, bodyEnd, stack, sb, partials, depth + 1);
                    }
                    else if (v is IList)
                    {
                        int idx = 0;
                        var list = (IList)v;
                        foreach (var item in list)
                        {
                            stack.Add(new LoopItem(item, idx++, list.Count));
                            Render(t, i, bodyEnd, stack, sb, partials, depth + 1);
                            stack.RemoveAt(stack.Count - 1);
                        }
                    }
                    else if (Truthy(v))
                    {
                        bool scope = v is IDictionary;
                        if (scope) stack.Add(v);
                        Render(t, i, bodyEnd, stack, sb, partials, depth + 1);
                        if (scope) stack.RemoveAt(stack.Count - 1);
                    }
                    i = after;
                    continue;
                }
                if (kind == '&') { tag = tag.Substring(1).Trim(); triple = true; }
                string s = Str(Lookup(stack, tag));
                sb.Append(triple ? s : Html.Escape(s));
            }
        }

        static void Render(string t, object model, Func<string, string> partials, StringBuilder sb, int depth)
        {
            Render(t, 0, t.Length, new List<object> { model }, sb, partials, depth);
        }

        static void FindClose(string t, int from, int end, string name, out int bodyEnd, out int after)
        {
            int depth = 1, i = from;
            while (i < end)
            {
                int open = t.IndexOf("{{", i, StringComparison.Ordinal);
                if (open < 0 || open >= end) break;
                int close = t.IndexOf("}}", open, StringComparison.Ordinal);
                if (close < 0) break;
                string tag = t.Substring(open + 2, close - open - 2).Trim();
                if ((tag.StartsWith("#") || tag.StartsWith("^")) && tag.Substring(1).Trim() == name) depth++;
                else if (tag.StartsWith("/") && tag.Substring(1).Trim() == name)
                {
                    if (--depth == 0) { bodyEnd = open; after = close + 2; return; }
                }
                i = close + 2;
            }
            bodyEnd = end;
            after = end;
        }

        class LoopItem
        {
            public readonly object Item;
            public readonly int Index, Count;
            public LoopItem(object item, int index, int count) { Item = item; Index = index; Count = count; }
        }

        static object Lookup(List<object> stack, string name)
        {
            if (name == ".") return Unwrap(stack[stack.Count - 1]);
            var parts = name.Split('.');
            for (int s = stack.Count - 1; s >= 0; s--)
            {
                var scope = stack[s];
                var li = scope as LoopItem;
                if (li != null)
                {
                    if (parts[0] == "@index") return li.Index;
                    if (parts[0] == "@number") return li.Index + 1;
                    if (parts[0] == "@first") return li.Index == 0;
                    if (parts[0] == "@last") return li.Index == li.Count - 1;
                    scope = li.Item;
                }
                bool found;
                var v = Member(scope, parts[0], out found);
                if (!found) continue;
                for (int i = 1; i < parts.Length && v != null; i++) v = Member(v, parts[i], out found);
                return v;
            }
            return null;
        }

        static object Unwrap(object o) { var li = o as LoopItem; return li != null ? li.Item : o; }

        static object Member(object o, string name, out bool found)
        {
            found = false;
            var d = o as IDictionary;
            if (d != null)
            {
                if (d.Contains(name)) { found = true; return d[name]; }
                return null;
            }
            var l = o as IList;
            int idx;
            if (l != null && int.TryParse(name, out idx) && idx >= 0 && idx < l.Count) { found = true; return l[idx]; }
            return null;
        }

        static bool Truthy(object v)
        {
            if (v == null) return false;
            if (v is bool) return (bool)v;
            if (v is string) return ((string)v).Length > 0;
            if (v is IList) return ((IList)v).Count > 0;
            if (v is int) return (int)v != 0;
            if (v is double) return (double)v != 0;
            return true;
        }

        static string Str(object v)
        {
            if (v == null) return "";
            if (v is double) return ((double)v).ToString("0.##", CultureInfo.InvariantCulture);
            if (v is float) return ((float)v).ToString("0.##", CultureInfo.InvariantCulture);
            if (v is IFormattable) return ((IFormattable)v).ToString(null, CultureInfo.InvariantCulture);
            return v.ToString();
        }
    }
}
