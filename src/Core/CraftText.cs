using System;
using System.Collections.Generic;
using System.Text;

namespace KerbalNews
{
    /// <summary>Small edits to a .craft file's text, for the in-game self-test.</summary>
    public static class CraftText
    {
        /// <summary>Removes every top-level PART whose part name starts with the prefix, and every line in other parts
        /// that refers to a removed part (link, attN, srfN, sym).</summary>
        public static string WithoutParts(string craft, string prefix, out int removed)
        {
            removed = 0;
            var lines = craft.Replace("\r\n", "\n").Split('\n');
            int i = 0;
            var gone = new HashSet<string>();
            var kept = new List<object>(); // string lines or part blocks
            while (i < lines.Length)
            {
                string t = lines[i].Trim();
                if (t == "PART" && i + 1 < lines.Length && lines[i + 1].Trim() == "{")
                {
                    var block = new List<string> { lines[i] };
                    int depth = 0, j = i + 1;
                    for (; j < lines.Length; j++)
                    {
                        string u = lines[j].Trim();
                        block.Add(lines[j]);
                        if (u == "{") depth++;
                        else if (u == "}") { depth--; if (depth == 0) break; }
                    }
                    string name = "";
                    foreach (var l in block)
                    {
                        string u = l.Trim();
                        if (u.StartsWith("part ") || u.StartsWith("part=")) { name = u.Substring(u.IndexOf('=') + 1).Trim(); break; }
                    }
                    if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { gone.Add(name); removed++; }
                    else kept.Add(block);
                    i = j + 1;
                    continue;
                }
                kept.Add(lines[i]);
                i++;
            }
            var sb = new StringBuilder();
            foreach (var k in kept)
            {
                var block = k as List<string>;
                if (block == null) { sb.Append((string)k).Append('\n'); continue; }
                foreach (var l in block)
                {
                    string u = l.Trim();
                    bool refers = false;
                    if (u.StartsWith("link") || u.StartsWith("attN") || u.StartsWith("srfN") || u.StartsWith("sym"))
                        foreach (var g in gone) if (u.Contains(g)) { refers = true; break; }
                    if (!refers) sb.Append(l).Append('\n');
                }
            }
            return sb.ToString().TrimEnd('\n') + "\n";
        }
    }
}
