using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace KerbalNews
{
    /// <summary>
    /// A game-independent mirror of KSP's ConfigNode (name, ordered key/values, child nodes).
    /// The core saves into this; the KSP layer copies it to and from a real ConfigNode.
    /// Keeping the core free of KSP types lets us unit-test persistence outside the game.
    /// </summary>
    public class DataNode
    {
        public string Name;
        public readonly List<KeyValuePair<string, string>> Values = new List<KeyValuePair<string, string>>();
        public readonly List<DataNode> Nodes = new List<DataNode>();

        public DataNode(string name = "") { Name = name; }

        public void Add(string key, string value) { Values.Add(new KeyValuePair<string, string>(key, value ?? "")); }
        public void Add(string key, double value) { Add(key, value.ToString("R", CultureInfo.InvariantCulture)); }
        public void Add(string key, int value) { Add(key, value.ToString(CultureInfo.InvariantCulture)); }
        public void Add(string key, bool value) { Add(key, value ? "True" : "False"); }
        /// <summary>Free text: encoded so newlines, braces and comment markers survive a ConfigNode.</summary>
        public void AddText(string key, string text) { Add(key, TextCodec.Encode(text)); }

        public DataNode AddNode(string name)
        {
            var n = new DataNode(name);
            Nodes.Add(n);
            return n;
        }

        public string Get(string key, string def = null)
        {
            foreach (var kv in Values) if (kv.Key == key) return kv.Value;
            return def;
        }

        public List<string> GetAll(string key)
        {
            var l = new List<string>();
            foreach (var kv in Values) if (kv.Key == key) l.Add(kv.Value);
            return l;
        }

        public string GetText(string key, string def = "")
        {
            var v = Get(key);
            return v == null ? def : TextCodec.Decode(v);
        }

        public double GetDouble(string key, double def = 0)
        {
            double d;
            var v = Get(key);
            return v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : def;
        }

        public int GetInt(string key, int def = 0)
        {
            int d;
            var v = Get(key);
            return v != null && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out d) ? d : def;
        }

        public bool GetBool(string key, bool def = false)
        {
            var v = Get(key);
            if (v == null) return def;
            return v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public DataNode GetNode(string name)
        {
            foreach (var n in Nodes) if (n.Name == name) return n;
            return null;
        }

        public List<DataNode> GetNodes(string name)
        {
            var l = new List<DataNode>();
            foreach (var n in Nodes) if (n.Name == name) l.Add(n);
            return l;
        }
    }

    /// <summary>
    /// Encodes free text for ConfigNode storage. ConfigNode values cannot hold newlines,
    /// braces or "//" safely, so text is stored as base64 ("b64:") and long text is
    /// gzipped first ("gz:") to keep persistent.sfs small.
    /// </summary>
    public static class TextCodec
    {
        const int GzipThreshold = 400;

        public static string Encode(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            byte[] raw = Encoding.UTF8.GetBytes(text);
            if (raw.Length >= GzipThreshold)
            {
                using (var ms = new MemoryStream())
                {
                    using (var gz = new GZipStream(ms, CompressionMode.Compress, true))
                        gz.Write(raw, 0, raw.Length);
                    return "gz:" + Convert.ToBase64String(ms.ToArray());
                }
            }
            return "b64:" + Convert.ToBase64String(raw);
        }

        public static string Decode(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            try
            {
                if (stored.StartsWith("b64:", StringComparison.Ordinal))
                    return Encoding.UTF8.GetString(Convert.FromBase64String(stored.Substring(4)));
                if (stored.StartsWith("gz:", StringComparison.Ordinal))
                {
                    byte[] data = Convert.FromBase64String(stored.Substring(3));
                    using (var ms = new MemoryStream(data))
                    using (var gz = new GZipStream(ms, CompressionMode.Decompress))
                    using (var outMs = new MemoryStream())
                    {
                        var buf = new byte[4096];
                        int n;
                        while ((n = gz.Read(buf, 0, buf.Length)) > 0) outMs.Write(buf, 0, n);
                        return Encoding.UTF8.GetString(outMs.ToArray());
                    }
                }
            }
            catch (Exception)
            {
                // Corrupt or hand-edited value: fall through and show it raw rather than lose it.
            }
            return stored;
        }
    }
}
