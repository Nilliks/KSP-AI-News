using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace KerbalNews
{
    /// <summary>One installed mod the AI may know about: a top-level GameData folder.</summary>
    public class ModInfo
    {
        public string Folder = "";
        public string Name = "";
        public string Version = "";
        /// <summary>The player's own description ("adds life support; kerbals need food"). Sent to the AI.</summary>
        public string Note = "";
        /// <summary>Whether the AI is told about this mod.</summary>
        public bool Include = true;
        /// <summary>Plumbing (a library or fix) rather than something the press could notice. Hidden from the AI by default.</summary>
        public bool Library;
        /// <summary>Found by the latest scan. A mod that is gone stays listed (with its note) but is not sent.</summary>
        public bool Installed = true;
        /// <summary>Added since the player last opened the list.</summary>
        public bool IsNew;
        public string FirstSeen = "";

        public void Save(DataNode n)
        {
            n.AddText("folder", Folder);
            n.AddText("name", Name);
            n.AddText("version", Version);
            n.AddText("note", Note);
            n.Add("include", Include);
            n.Add("library", Library);
            n.Add("installed", Installed);
            n.Add("new", IsNew);
            n.AddText("firstSeen", FirstSeen);
        }

        public static ModInfo Load(DataNode n)
        {
            return new ModInfo
            {
                Folder = n.GetText("folder"), Name = n.GetText("name"), Version = n.GetText("version"), Note = n.GetText("note"),
                Include = n.GetBool("include", true), Library = n.GetBool("library"), Installed = n.GetBool("installed", true),
                IsNew = n.GetBool("new"), FirstSeen = n.GetText("firstSeen"),
            };
        }
    }

    /// <summary>
    /// Finds installed mods by their GameData folders, keeps the list in the global settings, and turns it into a
    /// short line for the AI so articles can mention modded parts and features without guessing.
    /// </summary>
    public static class ModCatalog
    {
        /// <summary>Folders that are the game itself or this mod.</summary>
        static readonly HashSet<string> Skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Squad", "SquadExpansion", "KerbalNews" };

        /// <summary>Common libraries and fixes nobody in-universe would notice. Matched on the folder name, ignoring case and punctuation.</summary>
        static readonly string[] Libraries =
        {
            "Toolbar", "ToolbarControl", "ClickThroughBlocker", "ModuleManager", "Harmony", "KSPCommunityFixes", "KSPBugFixes", "B9PartSwitch",
            "B9AnimationModules", "CommunityResourcePack", "CommunityCategoryKit", "CommunityTechTree", "CommunityPartsTitles", "FirespitterCore",
            "KSPAPIExtensions", "MiniAVC", "ZeroMiniAVC", "KSPAVC", "SpaceTuxLibrary", "KSPWheel", "ModularFlightIntegrator", "PhysicsHoldFix",
            "KerbalChangelog", "Shabby", "UnitySafeShaders", "TextureUnlimited", "KSPModLib", "SolverEngines", "Scale", "Scale_Redist", "PQSModLoader",
            "CameraFocusChanger", "SmokeScreen", "RecoveryLib", "CustomPreLaunchChecks", "KSPBuildTools", "Filters", "ContractConfigurator",
            "DMagicScienceAnimate", "Kopernicus", "KSPe", "RasterPropMonitorCore", "JSI", "ASET", "PersistentRotationUpgraded", "Kerbalism-Config",
        };

        static string Key(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "") if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        public static bool IsLibrary(string folder)
        {
            string k = Key(folder).TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            if (k == "") return true;
            foreach (var l in Libraries) if (l != "" && k == Key(l)) return true;
            return k.EndsWith("lib") || k.EndsWith("library") || k.EndsWith("redist");
        }

        /// <summary>"000_Toolbar" → "Toolbar", "SpaceY-Lifters" → "SpaceY Lifters", "KerbalEngineer" → "Kerbal Engineer".</summary>
        static readonly string[] KeepWhole = { "MechJeb", "SCANsat", "ReStock", "TweakScale", "kOS", "KSPCF", "TUFX", "CryoTanks", "KerBalloons" };

        /// <summary>"RTGs": a lowercase s that ends the word makes the acronym plural, not a new word.</summary>
        static bool PluralAcronym(string s, int i) { return s[i + 1] == 's' && (i + 2 >= s.Length || !char.IsLower(s[i + 2])); }

        public static string PrettyName(string folder)
        {
            string s = (folder ?? "").Trim();
            // "000_Harmony", "001_ToolbarControl", "zzz_Deferred": load-order prefixes are not part of the name.
            s = System.Text.RegularExpressions.Regex.Replace(s, "^([0-9]+|z+)_+", "");
            if (s == "") s = folder ?? "";
            var keep = new List<KeyValuePair<int, int>>();
            foreach (var w in KeepWhole)
            {
                int at = s.IndexOf(w, StringComparison.Ordinal);
                if (at >= 0) keep.Add(new KeyValuePair<int, int>(at, at + w.Length));
            }
            Func<int, bool> inside = i => keep.Any(k => i > k.Key && i < k.Value);
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '_' || c == '-' || (c == '.' && i + 1 < s.Length && char.IsLetter(s[i + 1]))) { sb.Append(' '); continue; }
                if (i > 0 && !inside(i) && sb.Length > 0 && sb[sb.Length - 1] != ' ')
                {
                    char p = s[i - 1];
                    bool next = i + 1 < s.Length && char.IsLower(s[i + 1]);
                    // camelCase, the end of an acronym ("NTRMod"), a word after a digit ("G3Magnet"), a number after a word ("MechJeb2").
                    bool acronym = i + 1 < s.Length && (char.IsUpper(s[i + 1]) || char.IsDigit(s[i + 1]));
                    if ((char.IsUpper(c) && char.IsLower(p) && (next || acronym)) || (char.IsUpper(c) && char.IsUpper(p) && next && i >= 2 && !PluralAcronym(s, i)) || (char.IsUpper(c) && char.IsDigit(p) && next)
                        || (char.IsDigit(c) && char.IsLower(p) && !(i + 1 < s.Length && char.IsLetter(s[i + 1]))))
                        sb.Append(' ');
                }
                sb.Append(c);
            }
            return string.Join(" ", sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>Reads the mods in a GameData folder: one per top-level folder, with a name and version from a KSP-AVC .version file when there is one.</summary>
        public static List<ModInfo> Scan(string gameData)
        {
            var list = new List<ModInfo>();
            if (string.IsNullOrEmpty(gameData) || !Directory.Exists(gameData)) return list;
            foreach (var dir in Directory.GetDirectories(gameData).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string folder = Path.GetFileName(dir);
                if (Skip.Contains(folder) || folder.StartsWith(".")) continue;
                var m = new ModInfo { Folder = folder, Name = PrettyName(folder), Library = IsLibrary(folder) };
                try { ReadVersionFile(dir, m); }
                catch (Exception) { }
                list.Add(m);
            }
            return list;
        }

        static void ReadVersionFile(string dir, ModInfo m)
        {
            var files = Directory.GetFiles(dir, "*.version", SearchOption.TopDirectoryOnly).ToList();
            if (files.Count == 0)
                foreach (var sub in Directory.GetDirectories(dir).Take(12))
                    files.AddRange(Directory.GetFiles(sub, "*.version", SearchOption.TopDirectoryOnly));
            foreach (var f in files.Take(3))
            {
                object root;
                if (!Json.TryParse(File.ReadAllText(f), out root) || Json.Obj(root) == null) continue;
                string name = Json.Str(root, "NAME", "");
                name = System.Text.RegularExpressions.Regex.Replace(name, "<[^>]*>", "").Trim();
                if (name != "" && name.Length < 60) m.Name = name;
                var v = Json.Get(root, "VERSION");
                if (Json.Obj(v) != null)
                {
                    var parts = new List<string> { ((int)Json.Num(v, "MAJOR")).ToString(), ((int)Json.Num(v, "MINOR")).ToString() };
                    if (Json.Get(v, "PATCH") != null) parts.Add(((int)Json.Num(v, "PATCH")).ToString());
                    m.Version = string.Join(".", parts.ToArray());
                }
                else if (v is string) m.Version = (string)v;
                return;
            }
        }

        /// <summary>
        /// Merges a fresh scan into the known list: new folders are added (marked new), versions refreshed,
        /// vanished ones kept but marked not installed. The player's notes and choices are never lost. Returns the new mods.
        /// </summary>
        public static List<ModInfo> Merge(List<ModInfo> known, List<ModInfo> found, string today, bool firstScan)
        {
            var added = new List<ModInfo>();
            var byFolder = known.ToDictionary(k => k.Folder, StringComparer.OrdinalIgnoreCase);
            var present = new HashSet<string>(found.Select(f => f.Folder), StringComparer.OrdinalIgnoreCase);
            foreach (var f in found)
            {
                ModInfo k;
                if (byFolder.TryGetValue(f.Folder, out k))
                {
                    k.Installed = true;
                    if (f.Version != "") k.Version = f.Version;
                    k.Name = f.Name;   // names come from the folder or .version file, so a better reading replaces the old one
                    continue;
                }
                f.Include = !f.Library;
                f.IsNew = !firstScan;
                f.FirstSeen = today;
                known.Add(f);
                added.Add(f);
            }
            foreach (var k in known) if (!present.Contains(k.Folder)) k.Installed = false;
            known.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return added;
        }

        /// <summary>Scans GameData into the settings' list. The very first scan marks nothing as new. Returns the mods added.</summary>
        public static List<ModInfo> Refresh(GlobalSettings g, string gameData, string today)
        {
            var found = Scan(gameData);
            if (found.Count == 0 && !Directory.Exists(gameData ?? "")) return new List<ModInfo>();
            var added = Merge(g.Mods, found, today, !g.ModsScanned);
            g.ModsScanned = true;
            return added;
        }

        public static string Describe(List<ModInfo> added)
        {
            if (added.Count == 0) return "No new mods.";
            var names = added.Take(4).Select(a => a.Name).ToList();
            return "Added " + added.Count + " mod" + (added.Count == 1 ? "" : "s") + " to the list: " + string.Join(", ", names.ToArray()) + (added.Count > 4 ? " and " + (added.Count - 4) + " more" : "") + ".";
        }

        /// <summary>The compact line for prompts: included, installed mods by name, notes first, within a character budget.</summary>
        public static string Summary(List<ModInfo> mods, int budget = 1400)
        {
            var use = mods.Where(m => m.Include && m.Installed).ToList();
            if (use.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var m in use.Where(m => m.Note.Trim() != ""))
            {
                string line = m.Name + " (" + ResponseParser.Clip(m.Note.Trim(), 160) + "); ";
                if (sb.Length + line.Length > budget) break;
                sb.Append(line);
            }
            int shown = use.Count(m => m.Note.Trim() != "" && sb.ToString().Contains(m.Name + " ("));
            var rest = use.Where(m => m.Note.Trim() == "").ToList();
            int i = 0;
            for (; i < rest.Count; i++)
            {
                string piece = rest[i].Name + ", ";
                if (sb.Length + piece.Length > budget) break;
                sb.Append(piece);
            }
            string s = sb.ToString().TrimEnd(' ', ',', ';');
            int left = use.Count - shown - i;
            if (left > 0) s += " and " + left + " more";
            return s;
        }
    }
}
