using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KerbalNews
{
    /// <summary>
    /// Minimal JSON reader/writer. KSP ships no JSON library we can rely on, and
    /// other mods may bundle conflicting versions of Newtonsoft, so we carry our own.
    /// Objects parse to Dictionary&lt;string, object&gt;, arrays to List&lt;object&gt;,
    /// numbers to double, plus string, bool and null.
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("null JSON");
            var p = new Parser(text);
            p.SkipWs();
            object v = p.ReadValue();
            p.SkipWs();
            if (!p.End) throw new FormatException("Trailing characters at " + p.Pos);
            return v;
        }

        public static bool TryParse(string text, out object value)
        {
            try { value = Parse(text); return true; }
            catch (Exception) { value = null; return false; }
        }

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object v)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string) { WriteString(sb, (string)v); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is double || v is float || v is int || v is long || v is decimal)
            {
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d)) sb.Append("null");
                else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            var dict = v as IDictionary<string, object>;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, kv.Key);
                    sb.Append(':');
                    WriteValue(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }
            var list = v as System.Collections.IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteValue(sb, item);
                }
                sb.Append(']');
                return;
            }
            WriteString(sb, v.ToString());
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---- helpers for reading parsed trees ----

        public static Dictionary<string, object> Obj(object o) { return o as Dictionary<string, object>; }
        public static List<object> Arr(object o) { return o as List<object>; }

        public static object Get(object o, string key)
        {
            var d = Obj(o);
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        public static string Str(object o, string key, string def = null)
        {
            var v = Get(o, key);
            if (v == null) return def;
            if (v is string) return (string)v;
            if (v is double) return ((double)v).ToString(CultureInfo.InvariantCulture);
            if (v is bool) return (bool)v ? "true" : "false";
            return def;
        }

        public static double Num(object o, string key, double def = 0)
        {
            var v = Get(o, key);
            if (v is double) return (double)v;
            double d;
            if (v is string && double.TryParse((string)v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return def;
        }

        /// <summary>Follows a path like "choices.0.message.content".</summary>
        public static object Path(object root, string path)
        {
            object cur = root;
            foreach (var part in path.Split('.'))
            {
                if (cur == null) return null;
                int idx;
                var arr = Arr(cur);
                if (arr != null && int.TryParse(part, out idx))
                    cur = idx >= 0 && idx < arr.Count ? arr[idx] : null;
                else
                    cur = Get(cur, part);
            }
            return cur;
        }

        class Parser
        {
            readonly string s;
            int i;
            public Parser(string s) { this.s = s; }
            public bool End { get { return i >= s.Length; } }
            public int Pos { get { return i; } }

            public void SkipWs()
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            }

            char Peek() { if (i >= s.Length) throw new FormatException("Unexpected end"); return s[i]; }

            public object ReadValue()
            {
                SkipWs();
                char c = Peek();
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return ReadString();
                if (c == 't') { Expect("true"); return true; }
                if (c == 'f') { Expect("false"); return false; }
                if (c == 'n') { Expect("null"); return null; }
                if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                throw new FormatException("Unexpected '" + c + "' at " + i);
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw new FormatException("Expected " + word + " at " + i);
                i += word.Length;
            }

            Dictionary<string, object> ReadObject()
            {
                var d = new Dictionary<string, object>();
                i++; // {
                SkipWs();
                if (Peek() == '}') { i++; return d; }
                while (true)
                {
                    SkipWs();
                    if (Peek() != '"') throw new FormatException("Expected key at " + i);
                    string key = ReadString();
                    SkipWs();
                    if (Peek() != ':') throw new FormatException("Expected ':' at " + i);
                    i++;
                    d[key] = ReadValue();
                    SkipWs();
                    char c = Peek();
                    i++;
                    if (c == ',') continue;
                    if (c == '}') return d;
                    throw new FormatException("Expected ',' or '}' at " + (i - 1));
                }
            }

            List<object> ReadArray()
            {
                var l = new List<object>();
                i++; // [
                SkipWs();
                if (Peek() == ']') { i++; return l; }
                while (true)
                {
                    l.Add(ReadValue());
                    SkipWs();
                    char c = Peek();
                    i++;
                    if (c == ',') continue;
                    if (c == ']') return l;
                    throw new FormatException("Expected ',' or ']' at " + (i - 1));
                }
            }

            string ReadString()
            {
                var sb = new StringBuilder();
                i++; // opening quote
                while (true)
                {
                    if (i >= s.Length) throw new FormatException("Unterminated string");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (i >= s.Length) throw new FormatException("Bad escape");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw new FormatException("Bad unicode escape");
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default: throw new FormatException("Bad escape \\" + e);
                    }
                }
            }

            double ReadNumber()
            {
                int start = i;
                if (s[i] == '-') i++;
                while (i < s.Length && "0123456789+-.eE".IndexOf(s[i]) >= 0) i++;
                return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }
}
