using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace KerbalNews.KSP
{
    /// <summary>
    /// Reads every part of a craft for the engineering team: the craft in the editor exactly as it stands (nothing has
    /// to be saved) or the craft in flight. Modules are read by reflection on their own fields, so a modded module is
    /// read the same way as a stock one: by what it is derived from, the resources it declares, and its config values.
    /// Nothing here names a mod, and nothing here can fail to load because a mod is missing.
    /// </summary>
    static class CraftReader
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        static readonly Dictionary<string, MemberInfo> members = new Dictionary<string, MemberInfo>();
        static readonly Dictionary<Type, string> kinds = new Dictionary<Type, string>();
        static readonly Dictionary<Type, FieldInfo[]> configFields = new Dictionary<Type, FieldInfo[]>();

        /// <summary>Base classes that say what a module does. A modded module derived from one of these is read as that kind.</summary>
        static readonly KeyValuePair<string, string>[] KindOf =
        {
            P("ModuleEngines", "engine"), P("ModuleRCS", "rcs"), P("ModuleDeployableSolarPanel", "solar"), P("ModuleCurvedSolarPanel", "solar"),
            P("ModuleAlternator", "alternator"), P("ModuleGenerator", "generator"), P("ModuleResourceHarvester", "drill"), P("BaseDrill", "drill"),
            P("BaseConverter", "converter"), P("ModuleCommand", "command"), P("ModuleReactionWheel", "wheel"), P("ModuleDataTransmitter", "antenna"),
            P("ModuleParachute", "chute"), P("RealChuteModule", "chute"), P("ModuleDecouplerBase", "decoupler"), P("ModuleDecouple", "decoupler"),
            P("ModuleAnchoredDecoupler", "decoupler"), P("LaunchClamp", "clamp"), P("ModuleDockingNode", "dock"), P("ModuleLight", "light"),
            P("ModuleWheelBase", "leg"), P("ModuleAblator", "ablator"), P("ModuleProceduralFairing", "fairing"), P("ModuleScienceExperiment", "science"),
            P("ModuleScienceLab", "lab"), P("ModuleActiveRadiator", "radiator"), P("ModuleGimbal", "gimbal"), P("ModuleSAS", "sas"),
            P("ModuleResourceIntake", "intake"), P("ModuleControlSurface", "wing"), P("ModuleLiftingSurface", "wing"), P("ModuleCargoBay", "cargo"),
        };
        static KeyValuePair<string, string> P(string a, string b) { return new KeyValuePair<string, string>(a, b); }

        /// <summary>Modules that only draw, animate or make noise: not worth a word to an engineer.</summary>
        static readonly string[] Cosmetic =
        {
            "FX", "Waterfall", "ModulePartVariants", "ModuleColorChanger", "FlagDecal", "ModuleAnimat", "ModuleJettison", "ModuleTestSubject",
            "ModuleCargoPart", "ModuleTripLogger", "RSE", "ModuleB9PartInfo", "ModuleB9DisableTransform", "TextureSwitch", "ModuleConductionMultiplier",
            "ModuleRestock", "ModuleStatusLight", "CModule", "ModuleDragModifier", "ModuleDeployableAntenna", "ModuleDeployableRadiator", "ModuleDeployablePart",
            "ModuleInventoryPart", "ModuleKISItem", "TweakScaleDisable", "ModuleSeeThroughObject", "InternalModule", "ModuleStockShabby", "Decal", "ModuleCoreHeat",
            "ModuleOverheatDisplay", "ModuleGroundPart", "ModuleCenterFollowTransform", "ModuleDockingNodeNamed", "ModuleLadder", "RetractableLadder", "ModuleTechColor",
            "FMRS_PM", "KOSNameTag", "ShipEffects", "ModuleFreeIva", "ModuleToggleCrossfeed",
        };
        static readonly string[] NoisyField = { "transform", "anim", "sound", "fx", "texture", "color", "gui", "debug", "url", "node", "layer", "persist", "localiz", "display", "menu", "icon", "mesh", "prefab", "shader" };
        static readonly string[] StagedKinds = { "engine", "decoupler", "clamp", "chute", "fairing" };

        public static CraftSheet Read(IList<Part> parts, Part root, bool flying)
        {
            var sheet = new CraftSheet { Flying = flying, BuiltAt = Time.realtimeSinceStartup };
            if (parts == null) return sheet;
            var index = new Dictionary<Part, int>();
            var live = parts.Where(p => p != null).ToList();
            for (int i = 0; i < live.Count; i++) index[live[i]] = i;
            foreach (var p in live)
            {
                var c = new CraftPart();
                try { ReadPart(p, c, index); }
                catch (Exception e) { Debug.LogWarning("[KerbalNews] Could not read part fully: " + e.Message); }
                if (c.Title == "") c.Title = c.Name != "" ? c.Name : "Unknown part";
                sheet.Parts.Add(c);
            }
            int r;
            sheet.Root = root != null && index.TryGetValue(root, out r) ? r : 0;
            try { sheet.Prepare(); } catch (Exception e) { Debug.LogWarning("[KerbalNews] Craft tree: " + e.Message); }
            return sheet;
        }

        static void ReadPart(Part p, CraftPart c, Dictionary<Part, int> index)
        {
            var info = p.partInfo;
            c.Name = info != null && info.name != null ? info.name : p.name ?? "";
            c.Title = Clean(info != null ? info.title : null);
            c.Mod = ModOf(Str(info, "partUrl"));
            c.DryMass = p.mass;
            c.ResMass = p.GetResourceMass();
            c.Cost = Num(info, "cost");
            c.Stage = p.inverseStage;
            c.Crew = p.CrewCapacity;
            c.Surface = p.attachMode == AttachModes.SRF_ATTACH;
            object cross = Get(p, "fuelCrossFeed");
            if (cross is bool) c.CrossFeed = (bool)cross;
            var parent = Get(p, "parent") as Part;
            int pi;
            if (parent != null && index.TryGetValue(parent, out pi)) c.Parent = pi;

            if (p.Resources != null)
                foreach (PartResource res in p.Resources)
                    if (res != null && !string.IsNullOrEmpty(res.resourceName)) c.Res.Add(new CraftRes { Name = res.resourceName, Amount = res.amount, Max = res.maxAmount });

            if (p.Modules != null)
            {
                int engines = 0;
                foreach (PartModule m in p.Modules) if (m != null && Kind(m.GetType()) == "engine") engines++;
                foreach (PartModule m in p.Modules)
                {
                    if (m == null) continue;
                    try
                    {
                        var cm = ReadModule(m, engines > 1);
                        if (cm != null) c.Modules.Add(cm);
                    }
                    catch (Exception e) { c.Modules.Add(new CraftModule { Name = m.GetType().Name, Label = "unreadable: " + e.GetType().Name }); }
                }
            }

            // The tooltip the game shows for this part, one entry per module.
            var infos = Get(info, "moduleInfos") as IEnumerable;
            if (infos != null)
                foreach (var mi in infos)
                {
                    string title = Clean(Str(mi, "moduleName")), text = Clean(Str(mi, "info"));
                    if (text == "") continue;
                    string line = (title != "" ? title + ": " : "") + text;
                    c.Info.Add(line.Length > 260 ? line.Substring(0, 260) + "..." : line);
                    if (c.Info.Count >= 8) break;
                }
        }

        static CraftModule ReadModule(PartModule m, bool manyEngines)
        {
            var type = m.GetType();
            string kind = Kind(type), name = type.Name;
            if (kind == "other" && Cosmetic.Any(x => name.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0)) return null;
            var c = new CraftModule { Name = name, Kind = kind };
            object staging = Get(m, "stagingEnabled");
            c.Staged = StagedKinds.Contains(kind) && (!(staging is bool) || (bool)staging);
            Handler(m, c);

            switch (kind)
            {
                case "engine":
                    c.Num["thrust"] = Num(m, "maxThrust");
                    object curve = Get(m, "atmosphereCurve");
                    c.Num["ispVac"] = Curve(curve, 0f); c.Num["ispAsl"] = Curve(curve, 1f);
                    c.Txt["props"] = Props(m);
                    if (Bool(m, "throttleLocked")) c.Num["locked"] = 1;
                    c.Num["limit"] = Num(m, "thrustPercentage", 100);
                    if (manyEngines) c.Label = Str(m, "engineID");
                    c.Rates.Clear();   // an engine's propellants are not a steady draw
                    break;
                case "rcs":
                    c.Num["thrust"] = Num(m, "thrusterPower");
                    string rp = Props(m);
                    c.Txt["props"] = rp != "" ? rp : Str(m, "resourceName");
                    c.Rates.Clear();
                    break;
                case "solar":
                    double rate = Num(m, "chargeRate");
                    if (rate <= 0) rate = Num(m, "TotalEnergyRate");
                    if (rate <= 0) rate = c.Rate(CraftSheet.EC);
                    c.Num["rate"] = rate;
                    if (Bool(m, "isTracking")) c.Num["tracking"] = 1;
                    object state = Get(m, "deployState");
                    if (state != null && state.ToString() == "RETRACTED") c.Num["retracted"] = 1;
                    c.Rates.Clear();
                    break;
                case "converter": case "drill":
                    c.Rates.Clear();
                    Ratios(Get(m, "inputList"), c, -1);
                    Ratios(Get(m, "outputList"), c, 1);
                    c.Label = Clean(Str(m, "ConverterName"));
                    if (kind == "drill" && Str(m, "ResourceName") != "") c.Txt["mines"] = Str(m, "ResourceName");
                    break;
                case "command":
                    c.Num["crewMin"] = Num(m, "minimumCrew");
                    break;
                case "wheel":
                    c.Num["torque"] = Math.Max(Num(m, "PitchTorque"), Math.Max(Num(m, "YawTorque"), Num(m, "RollTorque")));
                    break;
                case "antenna":
                    c.Num["power"] = Num(m, "antennaPower");
                    object at = Get(m, "antennaType");
                    if (at != null) c.Txt["type"] = at.ToString().ToLowerInvariant();
                    double interval = Num(m, "packetInterval");
                    if (interval > 0) { c.Num["ec"] = Num(m, "packetResourceCost") / interval; c.Num["mits"] = Num(m, "packetSize") / interval; }
                    c.Rates.Clear();
                    break;
                case "dock":
                    c.Txt["size"] = Str(m, "nodeType");
                    break;
                case "leg":
                    object wt = Get(m, "wheelType");
                    string w = wt == null ? "" : wt.ToString();
                    c.Txt["type"] = w == "LEG" ? "landing leg" : w == "MOTORIZED" ? "powered wheel" : w == "FREE" ? "wheel" : "";
                    break;
                case "science":
                    c.Label = Str(m, "experimentID");
                    break;
                case "light":
                    if (c.Rates.Count == 0) { double ec = Num(m, "resourceAmount"); if (ec > 0) c.Rates.Add(new CraftRate { Res = CraftSheet.EC, PerSec = -ec }); }
                    break;
                case "other":
                    if (name == "TweakScale")
                    {
                        // Only worth a word when the part has actually been resized.
                        double now = Num(m, "currentScale"), def = Num(m, "defaultScale");
                        if (now <= 0 || def <= 0 || Math.Abs(now - def) < 1e-4) return null;
                        c.Num["scale"] = now; c.Num["normalScale"] = def;
                        break;
                    }
                    Config(m, c);
                    break;
            }
            return c;
        }

        // ------------------------------------------------------------ pieces

        static string Kind(Type t)
        {
            string k;
            if (kinds.TryGetValue(t, out k)) return k;
            k = "other";
            for (var at = t; at != null && at != typeof(object); at = at.BaseType)
            {
                var hit = KindOf.FirstOrDefault(x => x.Key == at.Name);
                if (hit.Key != null) { k = hit.Value; break; }
            }
            // Solar panels from mods that do not derive from the stock one.
            if (k == "other" && t.Name.IndexOf("SolarPanel", StringComparison.OrdinalIgnoreCase) >= 0 && t.Name.IndexOf("Fixer", StringComparison.OrdinalIgnoreCase) < 0) k = "solar";
            kinds[t] = k;
            return k;
        }

        /// <summary>The resources a module declares it uses and makes (the game's own resource handler), per second.</summary>
        static void Handler(PartModule m, CraftModule c)
        {
            object h = Get(m, "resHandler");
            if (h == null) return;
            Rates(Get(h, "inputResources"), c, -1);
            Rates(Get(h, "outputResources"), c, 1);
        }

        static void Rates(object list, CraftModule c, int sign)
        {
            var e = list as IEnumerable;
            if (e == null) return;
            foreach (var r in e)
            {
                string name = Str(r, "name"); double rate = Num(r, "rate");
                if (name != "" && rate > 0 && !double.IsNaN(rate) && !double.IsInfinity(rate)) c.Rates.Add(new CraftRate { Res = name, PerSec = sign * rate });
            }
        }

        static void Ratios(object list, CraftModule c, int sign)
        {
            var e = list as IEnumerable;
            if (e == null) return;
            foreach (var r in e)
            {
                string name = Str(r, "ResourceName"); double rate = Num(r, "Ratio");
                if (name != "" && rate > 0 && !double.IsNaN(rate) && !double.IsInfinity(rate)) c.Rates.Add(new CraftRate { Res = name, PerSec = sign * rate });
            }
        }

        static string Props(PartModule m)
        {
            var e = Get(m, "propellants") as IEnumerable;
            if (e == null) return "";
            var names = new List<string>();
            foreach (var p in e) { string n = Str(p, "name"); if (n != "" && !names.Contains(n)) names.Add(n); }
            return string.Join(", ", names.ToArray());
        }

        static double Curve(object curve, float at)
        {
            if (curve == null) return 0;
            try
            {
                var eval = curve.GetType().GetMethod("Evaluate", new[] { typeof(float) });
                if (eval == null) return 0;
                return Convert.ToDouble(eval.Invoke(curve, new object[] { at }));
            }
            catch { return 0; }
        }

        /// <summary>A module nobody taught the reader about: its config values (the fields the game loads from the part file).</summary>
        static void Config(PartModule m, CraftModule c)
        {
            var type = m.GetType();
            FieldInfo[] fields;
            if (!configFields.TryGetValue(type, out fields))
            {
                var list = new List<FieldInfo>();
                for (var at = type; at != null && at.Name != "PartModule" && at != typeof(object); at = at.BaseType)
                    foreach (var f in at.GetFields(Any))
                    {
                        var ft = f.FieldType;
                        if (ft != typeof(float) && ft != typeof(double) && ft != typeof(int) && ft != typeof(bool) && ft != typeof(string)) continue;
                        string lower = f.Name.ToLowerInvariant();
                        if (NoisyField.Any(x => lower.Contains(x))) continue;
                        if (!f.GetCustomAttributes(true).Any(a => a.GetType().Name == "KSPField")) continue;
                        list.Add(f);
                    }
                fields = list.ToArray();
                configFields[type] = fields;
            }
            foreach (var f in fields)
            {
                object v;
                try { v = f.GetValue(m); } catch { continue; }
                if (v == null) continue;
                if (v is string)
                {
                    string s = Clean((string)v);
                    if (s != "" && s.Length <= 40 && c.Txt.Count < 4 && !s.StartsWith("#")) c.Txt[f.Name] = s;
                }
                else if (v is bool) { if ((bool)v && c.Num.Count < 10) c.Num[f.Name] = 1; }
                else
                {
                    double d = Convert.ToDouble(v);
                    if (d != 0 && !double.IsNaN(d) && !double.IsInfinity(d) && c.Num.Count < 10) c.Num[f.Name] = d;
                }
            }
        }

        static string ModOf(string partUrl)
        {
            if (string.IsNullOrEmpty(partUrl)) return "";
            string first = partUrl.Split('/')[0];
            return first == "Squad" || first == "SquadExpansion" ? "stock" : first;
        }

        static readonly Regex Tags = new Regex("<[^>]{1,40}>", RegexOptions.Compiled);
        static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.Compiled);
        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return Spaces.Replace(Tags.Replace(s, " "), " ").Trim();
        }

        // ------------------------------------------------------------ reflection

        public static object Get(object o, string name)
        {
            if (o == null) return null;
            var type = o.GetType();
            string key = type.FullName + "." + name;
            MemberInfo mi;
            if (!members.TryGetValue(key, out mi))
            {
                for (var at = type; at != null && mi == null; at = at.BaseType)
                {
                    mi = at.GetField(name, Any);
                    if (mi == null) { var p = at.GetProperty(name, Any); if (p != null && p.GetIndexParameters().Length == 0 && p.CanRead) mi = p; }
                }
                members[key] = mi;
            }
            if (mi == null) return null;
            try
            {
                var f = mi as FieldInfo;
                return f != null ? f.GetValue(o) : ((PropertyInfo)mi).GetValue(o, null);
            }
            catch { return null; }
        }

        public static double Num(object o, string name, double def = 0)
        {
            object v = Get(o, name);
            if (v == null || v is string || v is bool) return def;
            try { double d = Convert.ToDouble(v); return double.IsNaN(d) || double.IsInfinity(d) ? def : d; } catch { return def; }
        }

        public static string Str(object o, string name) { object v = Get(o, name); return v == null ? "" : v.ToString(); }
        public static bool Bool(object o, string name) { object v = Get(o, name); return v is bool && (bool)v; }

        // ------------------------------------------------------------ bodies

        public static List<EngBody> Bodies()
        {
            var list = new List<EngBody>();
            var bodies = FlightGlobals.Bodies;
            if (bodies == null) return list;
            var home = FlightGlobals.GetHomeBody();
            foreach (var b in bodies)
            {
                if (b == null) continue;
                try
                {
                    var e = new EngBody { Name = b.bodyName, Mu = b.gravParameter, Radius = b.Radius, Gravity = b.GeeASL * 9.80665, IsHome = b == home };
                    e.AtmoDepth = b.atmosphere ? b.atmosphereDepth : 0;
                    e.AtmoPressure = b.atmosphere ? Num(b, "atmospherePressureSeaLevel") / 101.325 : 0;
                    e.Oxygen = Bool(b, "atmosphereContainsOxygen");
                    e.Soi = double.IsInfinity(b.sphereOfInfluence) ? 0 : b.sphereOfInfluence;
                    e.RotPeriod = Num(b, "rotationPeriod");
                    object solid = Get(b, "hasSolidSurface");
                    e.HasSurface = !(solid is bool) || (bool)solid;
                    var parent = Get(b, "referenceBody") as CelestialBody;
                    object orbit = Get(b, "orbit");
                    e.IsStar = orbit == null || parent == null || parent == b;
                    if (!e.IsStar) { e.Parent = parent.bodyName; e.Sma = Num(orbit, "semiMajorAxis"); }
                    list.Add(e);
                }
                catch (Exception ex) { Debug.LogWarning("[KerbalNews] Body read: " + ex.Message); }
            }
            // Distance from the star: the orbit of whichever ancestor circles it.
            foreach (var e in list)
            {
                var at = e; int guard = 0;
                while (at != null && !at.IsStar && guard++ < 6)
                {
                    var parent = list.FirstOrDefault(x => x.Name == at.Parent);
                    if (parent == null || parent.IsStar) { e.SunDistance = at.Sma; break; }
                    at = parent;
                }
            }
            return list;
        }
    }
}
