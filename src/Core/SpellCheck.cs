using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>A misspelled word in a piece of text: where it starts and how long it is.</summary>
    public struct Misspelling
    {
        public int Start, Length;
        public Misspelling(int s, int l) { Start = s; Length = l; }
        public override string ToString() { return Start + "+" + Length; }
    }

    /// <summary>
    /// Spell checking for the in-game text boxes, entirely offline: a bundled English word list (SCOWL, in commonness
    /// levels), a list of KSP words, words the game supplies at run time (planets, parts, kerbals, craft, outlets) and
    /// the player's own "Add to dictionary" words. Checking a box is a hash lookup per word, so it runs on every key.
    /// Suggestions are worked out only when the player asks for them (a right-click on a flagged word).
    /// </summary>
    public class SpellCheck
    {
        // word (lower case) -> commonness rank; lower is more common. Game and player words share one low rank.
        readonly Dictionary<string, int> words = new Dictionary<string, int>(StringComparer.Ordinal);
        // How the game's own words are spelled (lower -> "Kerbin"), so suggestions keep their capitals.
        readonly Dictionary<string, string> casing = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly HashSet<string> ignored = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> userWords = new HashSet<string>(StringComparer.Ordinal);
        const int GameRank = 15;
        string userFile;

        public int Count { get { return words.Count; } }
        public bool Loaded { get { return words.Count > 0; } }

        /// <summary>Reads a word list: one word per line; "@N" lines set the commonness level of the words after them.</summary>
        public void LoadList(IEnumerable<string> lines, bool game)
        {
            int level = game ? GameRank : 60;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line == "" || line.StartsWith("#")) continue;
                if (line[0] == '@') { int l; if (int.TryParse(line.Substring(1), out l)) level = l; continue; }
                if (game) AddGameWord(line);
                else
                {
                    var k = Key(line);
                    if (!words.ContainsKey(k)) words[k] = level;
                }
            }
        }

        public void LoadFile(string path, bool game)
        {
            try { if (File.Exists(path)) LoadList(File.ReadAllLines(path, Encoding.UTF8), game); }
            catch (Exception) { }
        }

        /// <summary>The player's own words: read now, and "Add to dictionary" appends to the same file.</summary>
        public void LoadUserFile(string path)
        {
            userFile = path;
            try
            {
                if (!File.Exists(path)) return;
                foreach (var l in File.ReadAllLines(path, Encoding.UTF8)) { var w = l.Trim(); if (w != "") { AddGameWord(w); userWords.Add(Key(w)); } }
            }
            catch (Exception) { }
        }

        /// <summary>
        /// A word or name the game knows ("Kerbin", "Jebediah Kerman", "Mk1 Command Pod"): each word in it stops
        /// being flagged, and it can be suggested with its own capitals.
        /// </summary>
        public void AddGameWord(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (var m in Words(text))
            {
                var w = text.Substring(m.Start, m.Length);
                var k = Key(w);
                if (k.Length < 2) continue;
                int r;
                if (!words.TryGetValue(k, out r) || r > GameRank) words[k] = GameRank;
                if (w != k && !casing.ContainsKey(k)) casing[k] = w;
            }
        }

        public void AddGameWords(IEnumerable<string> names) { if (names != null) foreach (var n in names) AddGameWord(n); }

        /// <summary>"Add to dictionary": remembered for good, in the player's word file.</summary>
        public void AddUserWord(string word)
        {
            var k = Key(word);
            if (k == "" || userWords.Contains(k)) return;
            userWords.Add(k);
            AddGameWord(word);
            if (userFile == null) return;
            try
            {
                var dir = Path.GetDirectoryName(userFile);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(userFile, word.Trim() + "\n", Encoding.UTF8);
            }
            catch (Exception) { }
        }

        /// <summary>"Ignore": not flagged again until the game restarts.</summary>
        public void Ignore(string word) { ignored.Add(Key(word)); }

        static string Key(string w) { return w.Replace('’', '\'').Replace('‘', '\'').ToLowerInvariant(); }

        static bool IsLetter(char c) { return char.IsLetter(c); }
        static bool IsApos(char c) { return c == '\'' || c == '’'; }

        /// <summary>The words in a text: runs of letters with apostrophes inside ("don't", "Jeb's").</summary>
        public static List<Misspelling> Words(string text)
        {
            var list = new List<Misspelling>();
            if (string.IsNullOrEmpty(text)) return list;
            int i = 0, n = text.Length;
            while (i < n)
            {
                if (!IsLetter(text[i])) { i++; continue; }
                int s = i;
                while (i < n && (IsLetter(text[i]) || (IsApos(text[i]) && i + 1 < n && IsLetter(text[i + 1]) && i > s))) i++;
                list.Add(new Misspelling(s, i - s));
            }
            return list;
        }

        /// <summary>
        /// The misspelled words in a text. Words that are not really words are left alone: anything touching a digit
        /// or underscore (Mk1, LV-T45), web and mail addresses, paths, ALL-CAPS acronyms, camelCase and single letters.
        /// skipAt: a caret position; the word it touches is not flagged yet (it is still being typed).
        /// </summary>
        public List<Misspelling> Check(string text, int skipAt = -1)
        {
            var bad = new List<Misspelling>();
            if (!Loaded || string.IsNullOrEmpty(text)) return bad;
            foreach (var m in Words(text))
            {
                if (skipAt >= m.Start && skipAt <= m.Start + m.Length) continue;
                if (!Plain(text, m)) continue;
                if (!IsKnown(text.Substring(m.Start, m.Length))) bad.Add(m);
            }
            return bad;
        }

        /// <summary>Is this a run of letters worth checking, or a code, address, acronym or name-like token?</summary>
        static bool Plain(string text, Misspelling m)
        {
            int s = m.Start, e = m.Start + m.Length;   // e is one past the end
            if (m.Length < 2) return false;
            char before = s > 0 ? text[s - 1] : ' ', after = e < text.Length ? text[e] : ' ';
            char after2 = e + 1 < text.Length ? text[e + 1] : ' ';
            if (char.IsDigit(before) || char.IsDigit(after) || before == '_' || after == '_') return false;
            if (before == '@' || after == '@' || before == '/' || after == '/' || before == '\\' || after == '\\' || before == '#' || before == '<') return false;
            if (before == '.' && s > 1 && (IsLetter(text[s - 2]) || char.IsDigit(text[s - 2]))) return false;   // kerbalnews.kb, file.txt
            if ((after == '.' || after == ':') && (IsLetter(after2) || char.IsDigit(after2) || after2 == '/')) return false;   // www.x, https://
            if (before == '-' && s > 1 && char.IsDigit(text[s - 2])) return false;
            if (after == '-' && char.IsDigit(after2)) return false;   // LV-T45 style codes
            int upper = 0;
            for (int i = s; i < e; i++) if (char.IsUpper(text[i])) upper++;
            if (upper == m.Length || (upper == m.Length - 1 && text[e - 1] == 's' && m.Length > 2)) return false;   // NASA, KSC, RTGs
            for (int i = s + 1; i < e; i++) if (char.IsUpper(text[i]) && char.IsLower(text[i - 1])) return false;   // camelCase, McLeod-ish names
            return true;
        }

        /// <summary>Is a single word spelled right (in the lists, or a plural/possessive of one)?</summary>
        public bool IsKnown(string word)
        {
            var k = Key(word);
            if (k.Length < 2 || words.ContainsKey(k) || ignored.Contains(k)) return true;
            if (k.EndsWith("'s") && Has(k.Substring(0, k.Length - 2))) return true;
            if (k.EndsWith("s'") && Has(k.Substring(0, k.Length - 1))) return true;
            if (k.EndsWith("'")) return Has(k.TrimEnd('\''));
            // Plurals and simple endings of the game's own words (Kerbals, Mun's, Duna-bound is split by the hyphen).
            if (k.EndsWith("s") && Game(k.Substring(0, k.Length - 1))) return true;
            if (k.EndsWith("es") && Game(k.Substring(0, k.Length - 2))) return true;
            if (k.EndsWith("ian") && Game(k.Substring(0, k.Length - 3))) return true;   // Kerbinian, Dunian
            if (k.EndsWith("ers") && Game(k.Substring(0, k.Length - 3))) return true;
            return false;
        }

        bool Has(string k) { return k.Length > 0 && (words.ContainsKey(k) || ignored.Contains(k)); }
        bool Game(string k) { int r; return k.Length > 1 && words.TryGetValue(k, out r) && r == GameRank; }

        const string Alphabet = "abcdefghijklmnopqrstuvwxyz'";

        /// <summary>
        /// Up to max spellings the player might have meant, best first: one slip away before two, and common words
        /// before rare ones. Keeps the capitals of what was typed (or of the game word).
        /// </summary>
        public List<string> Suggest(string word, int max = 5)
        {
            var result = new List<string>();
            var k = Key(word);
            if (k.Length < 1 || k.Length > 30 || !Loaded) return result;
            var scored = new Dictionary<string, double>(StringComparer.Ordinal);
            var one = Edits(k);
            foreach (var e in one) Score(scored, k, e, 1);
            if (scored.Count < Math.Min(3, max) && k.Length <= 14)   // two slips away only when one slip finds little: it costs far more
                foreach (var e in one) foreach (var e2 in Edits(e)) Score(scored, k, e2, 2);
            foreach (var kv in scored.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(max))
                result.Add(Shape(word, kv.Key));
            return result;
        }

        void Score(Dictionary<string, double> scored, string typed, string cand, int dist)
        {
            if (cand == typed || cand.Length == 0) return;
            int rank;
            if (!words.TryGetValue(cand, out rank)) return;
            // Distance counts most; then how common the word is; a slip that keeps the first letter is likelier.
            double s = dist * 100 + rank;
            if (cand[0] != typed[0]) s += 40;
            if (cand.Length == typed.Length) s -= 5;
            s -= Slip(typed, cand);
            double old;
            if (!scored.TryGetValue(cand, out old) || s < old) scored[cand] = s;
        }

        static readonly string[] Rows = { "qwertyuiop", "asdfghjkl", "zxcvbnm" };

        static bool Near(char a, char b)
        {
            for (int r = 0; r < 3; r++)
            {
                int i = Rows[r].IndexOf(a);
                if (i < 0) continue;
                for (int r2 = Math.Max(0, r - 1); r2 <= Math.Min(2, r + 1); r2++)
                {
                    int j = Rows[r2].IndexOf(b);
                    if (j >= 0 && Math.Abs(j - i) <= 1) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// How natural a typing slip turns cand into typed: two letters swapped ("teh") or a neighbouring key hit
        /// ("rpcket") are the commonest mistakes, so those spellings rank first.
        /// </summary>
        static double Slip(string typed, string cand)
        {
            if (typed.Length != cand.Length) return 0;
            int first = -1, diffs = 0;
            for (int i = 0; i < typed.Length; i++) if (typed[i] != cand[i]) { if (first < 0) first = i; diffs++; }
            if (diffs == 2 && first + 1 < typed.Length && typed[first] == cand[first + 1] && typed[first + 1] == cand[first]) return 30;
            if (diffs == 1 && Near(typed[first], cand[first])) return 15;
            return 0;
        }

        /// <summary>Every string one edit away: a letter dropped, swapped with the next, changed or added.</summary>
        static HashSet<string> Edits(string w)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i <= w.Length; i++)
            {
                string a = w.Substring(0, i), b = w.Substring(i);
                if (b.Length > 0) set.Add(a + b.Substring(1));
                if (b.Length > 1) set.Add(a + b[1] + b[0] + b.Substring(2));
                foreach (char c in Alphabet)
                {
                    if (b.Length > 0 && c != b[0]) set.Add(a + c + b.Substring(1));
                    set.Add(a + c + b);
                }
            }
            return set;
        }

        /// <summary>Gives a suggestion the capitals of what was typed: "Teh" -> "The", "TEH" -> "THE".</summary>
        string Shape(string typed, string cand)
        {
            string own;
            if (casing.TryGetValue(cand, out own)) return own;
            if (typed.Length > 1 && typed.All(c => !char.IsLetter(c) || char.IsUpper(c))) return cand.ToUpperInvariant();
            if (typed.Length > 0 && char.IsUpper(typed[0])) return char.ToUpperInvariant(cand[0]) + cand.Substring(1);
            if (cand == "i") return "I";
            return cand;
        }

        /// <summary>The flagged word at a character position, if any.</summary>
        public static bool At(List<Misspelling> bad, int pos, out Misspelling hit)
        {
            foreach (var m in bad) if (pos >= m.Start && pos < m.Start + m.Length) { hit = m; return true; }
            hit = new Misspelling(-1, 0);
            return false;
        }
    }
}
