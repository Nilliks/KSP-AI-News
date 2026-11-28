using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace KerbalNews.KSP
{
    /// <summary>
    /// Asks installed mods how they are set up in this save: which of their features are switched on, and what their
    /// background processes use. Everything is looked up by name at run time, so a mod that is missing, or a newer
    /// version that renamed something, simply leaves its facts out; nothing here can stop the mod loading.
    /// </summary>
    static class ModProbe
    {
        public static void Fill(CraftSheet sheet)
        {
            try { Kerbalism(sheet); } catch (Exception e) { Debug.LogWarning("[KerbalNews] Kerbalism probe: " + e.Message); }
            try { Installed(sheet); } catch { }
        }

        /// <summary>Mods the checks treat differently, found by one of their types being loaded.</summary>
        static void Installed(CraftSheet sheet)
        {
            var known = new[]
            {
                new[] { "RemoteTech", "RemoteTech.RTCore" }, new[] { "RealChute", "RealChute.RealChuteModule" }, new[] { "FAR", "FerramAerospaceResearch.FARAPI" },
                new[] { "KJR", "KerbalJointReinforcement.KJRManager" }, new[] { "TACLifeSupport", "Tac.LifeSupportModule" }, new[] { "USILifeSupport", "LifeSupport.ModuleLifeSupport" },
                new[] { "Snacks", "Snacks.SnacksScenario" }, new[] { "SystemHeat", "SystemHeat.ModuleSystemHeat" }, new[] { "DeadlyReentry", "DeadlyReentry.ModuleAeroReentry" },
            };
            foreach (var k in known) if (CraftReader.FindType(k[1]) != null) sheet.ModFacts[k[0]] = "installed";
        }

        static void Kerbalism(CraftSheet sheet)
        {
            var features = CraftReader.FindType("KERBALISM.Features");
            if (features == null) return;
            sheet.ModFacts["Kerbalism"] = "installed";
            foreach (var f in features.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (f.FieldType != typeof(bool)) continue;
                try { sheet.ModFacts["Kerbalism." + f.Name] = (bool)f.GetValue(null) ? "on" : "off"; } catch { }
            }
            var profile = CraftReader.FindType("KERBALISM.Profile");
            var processes = CraftReader.Static(profile, "processes") as IEnumerable;
            if (processes != null)
                foreach (var p in processes)
                {
                    if (p == null) continue;
                    var mods = CraftReader.Get(p, "modifiers") as IEnumerable;
                    if (mods == null) continue;
                    // The process runs in proportion to a hidden "capacity" resource the parts carry.
                    string cap = null;
                    foreach (var m in mods) { string name = m as string; if (name != null && sheet.Max(name) > 0) { cap = name; break; } }
                    if (cap == null) continue;
                    // Launch clamps carry a generator process too, and they stay on the pad.
                    double amount = sheet.Parts.Where(x => !x.Has("clamp")).SelectMany(x => x.Res).Where(x => x.Name == cap).Sum(x => x.Amount);
                    if (amount <= 0 && !sheet.Parts.Any(x => !x.Has("clamp") && x.Max(cap) > 0)) continue;
                    var cp = new CraftProcess { Name = CraftReader.Str(p, "name"), Title = CraftReader.Clean(CraftReader.Str(p, "title")), Mod = "Kerbalism", Capacity = amount };
                    if (cp.Title == "") cp.Title = cp.Name;
                    Rates(CraftReader.Get(p, "inputs"), cp, -1);
                    Rates(CraftReader.Get(p, "outputs"), cp, 1);
                    if (cp.Rates.Count > 0) sheet.Processes.Add(cp);
                }
            var rules = CraftReader.Static(profile, "rules") as IEnumerable;
            if (rules != null)
                foreach (var r in rules)
                {
                    if (r == null) continue;
                    double rate = CraftReader.Num(r, "rate"), interval = CraftReader.Num(r, "interval");
                    string input = CraftReader.Str(r, "input");
                    if (input == "" || rate <= 0) continue;
                    var mods = CraftReader.Get(r, "modifiers") as IEnumerable;
                    bool varies = false;
                    if (mods != null) foreach (var m in mods) { varies = true; break; }
                    sheet.CrewRules.Add(new CrewRule { Name = CraftReader.Str(r, "name"), Input = input, Output = CraftReader.Str(r, "output"), Mod = "Kerbalism", PerSec = interval > 0 ? rate / interval : rate, Varies = varies });
                }
        }

        class ProcessDef { public string Title = ""; public List<string> Caps = new List<string>(); public List<CraftRate> PerUnit = new List<CraftRate>(); }
        static List<ProcessDef> defs;

        /// <summary>For a part read on its own (the catalogue of unlocked parts): what a life support mod's background
        /// processes on that part use and make when running flat out, as a module of kind "process". On a craft the
        /// same thing is worked out for the whole vessel in <see cref="Kerbalism"/>.</summary>
        public static void AddProcesses(CraftPart c)
        {
            if (c == null) return;
            if (defs == null)
            {
                defs = new List<ProcessDef>();
                try
                {
                    var processes = CraftReader.Static(CraftReader.FindType("KERBALISM.Profile"), "processes") as IEnumerable;
                    if (processes != null)
                        foreach (var p in processes)
                        {
                            if (p == null) continue;
                            var d = new ProcessDef { Title = CraftReader.Clean(CraftReader.Str(p, "title")) };
                            if (d.Title == "") d.Title = CraftReader.Str(p, "name");
                            var mods = CraftReader.Get(p, "modifiers") as IEnumerable;
                            if (mods != null) foreach (var m in mods) { string name = m as string; if (!string.IsNullOrEmpty(name)) d.Caps.Add(name); }
                            var one = new CraftProcess { Capacity = 1 };
                            Rates(CraftReader.Get(p, "inputs"), one, -1);
                            Rates(CraftReader.Get(p, "outputs"), one, 1);
                            d.PerUnit = one.Rates;
                            if (d.Caps.Count > 0 && d.PerUnit.Count > 0) defs.Add(d);
                        }
                }
                catch (Exception e) { Debug.LogWarning("[KerbalNews] Process definitions: " + e.Message); }
            }
            foreach (var d in defs)
            {
                double cap = 0;
                foreach (string name in d.Caps) { cap = c.Max(name); if (cap > 0) break; }
                if (cap <= 0) continue;
                var m = new CraftModule { Name = "Process", Kind = "process", Label = d.Title };
                foreach (var r in d.PerUnit) m.Rates.Add(new CraftRate { Res = r.Res, PerSec = r.PerSec * cap });
                c.Modules.Add(m);
            }
        }

        static void Rates(object dict, CraftProcess cp, int sign)
        {
            var d = dict as IDictionary;
            if (d == null) return;
            foreach (DictionaryEntry e in d)
            {
                string name = e.Key as string;
                double rate;
                try { rate = Convert.ToDouble(e.Value); } catch { continue; }
                if (!string.IsNullOrEmpty(name) && rate > 0 && !double.IsNaN(rate)) cp.Rates.Add(new CraftRate { Res = name, PerSec = sign * rate * cp.Capacity });
            }
        }
    }
}
