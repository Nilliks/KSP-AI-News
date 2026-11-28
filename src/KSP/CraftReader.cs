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
            P("ModuleResourceIntake", "intake"), P("ModuleCoreHeat", "coreheat"), P("ModuleControlSurface", "wing"), P("ModuleLiftingSurface", "wing"), P("ModuleCargoBay", "cargo"),
        };
        static KeyValuePair<string, string> P(string a, string b) { return new KeyValuePair<string, string>(a, b); }

        /// <summary>Modules that only draw, animate or make noise: not worth a word to an engineer.</summary>
        static readonly string[] Cosmetic =
        {
            "FX", "Waterfall", "ModulePartVariants", "ModuleColorChanger", "FlagDecal", "ModuleAnimat", "ModuleJettison", "ModuleTestSubject",
            "ModuleCargoPart", "ModuleTripLogger", "RSE", "ModuleB9PartInfo", "ModuleB9DisableTransform", "TextureSwitch", "ModuleConductionMultiplier",
            "ModuleRestock", "ModuleStatusLight", "CModule", "ModuleDragModifier", "ModuleDeployableAntenna", "ModuleDeployableRadiator", "ModuleDeployablePart",
            "ModuleInventoryPart", "ModuleKISItem", "TweakScaleDisable", "ModuleSeeThroughObject", "InternalModule", "ModuleStockShabby", "Decal",
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
            rootT = null;
            try { rootT = Get(root ?? (live.Count > 0 ? live[0] : null), "transform"); } catch { }
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

        public static string KindOfType(Type t) { return Kind(t); }

        /// <summary>One part on its own (a part from the parts list, not on a craft).</summary>
        public static CraftPart ReadLoose(Part p)
        {
            var c = new CraftPart();
            var keep = rootT; rootT = null;
            try { ReadPart(p, c, new Dictionary<Part, int>()); } finally { rootT = keep; }
            if (c.Title == "") c.Title = c.Name;
            c.Stage = -1;
            return c;
        }

        static void ReadPart(Part p, CraftPart c, Dictionary<Part, int> index)
        {
            var info = p.partInfo;
            c.Name = info != null && info.name != null ? info.name : p.name ?? "";
            c.Title = Clean(info != null ? info.title : null);
            c.Mod = ModOf(Str(info, "partUrl"));
            c.DryMass = p.mass;
            c.ResMass = p.GetResourceMass();
            c.Cost = Cost(p, info);
            c.CrashTol = Num(p, "crashTolerance");
            c.Bulkhead = Str(info, "bulkheadProfiles");
            c.MaxTemp = Num(p, "maxTemp");
            try { Place(p, c); } catch { }
            try { c.ChuteArea = ChuteArea(p); } catch { }
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
                        // Anything that folds out says whether it is out.
                        object ds = Get(m, "deployState");
                        // A fixed panel reports itself as extended for ever; with no fold animation it does not fold.
                        object anim = ds != null ? Get(m, "animationName") : null;
                        if (ds != null && !(anim is string && (string)anim == "")) { string st = ds.ToString(); c.Deployed = st == "EXTENDED" || st == "EXTENDING" ? 1 : st == "RETRACTED" || st == "RETRACTING" ? 0 : c.Deployed; }
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
            try { Groups(m, c); } catch { }

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
                    try { EngineExtras(m, c); } catch { }
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
                case "sas":
                    c.Num["level"] = Num(m, "SASServiceLevel");
                    break;
                case "gimbal":
                    c.Num["range"] = Num(m, "gimbalRange");
                    break;
                case "radiator":
                    c.Num["kw"] = Num(m, "maxEnergyTransfer") / 50;
                    break;
                case "coreheat":
                    c.Num["maxCoolant"] = Num(m, "MaxCoolant");
                    c.Num["goal"] = Num(m, "CoreTempGoal");
                    c.Num["shutdown"] = Num(m, "CoreShutdownTemp");
                    break;
                case "wing":
                    c.Num["lift"] = Num(m, "deflectionLiftCoeff");
                    if (Num(m, "ctrlSurfaceArea") > 0) c.Num["control"] = Num(m, "ctrlSurfaceArea");
                    break;
                case "chute":
                    c.Num["minPressure"] = Num(m, "minAirPressureToOpen");
                    c.Num["deployAlt"] = Num(m, "deployAltitude");
                    c.Txt["state"] = Str(m, "deploymentState");
                    c.Txt["safe"] = Str(m, "deploymentSafeState");
                    break;
                case "ablator":
                    c.Txt["res"] = Str(m, "ablativeResource");
                    break;
                case "decoupler":
                    c.Num["force"] = Num(m, "ejectionForce");
                    break;
                case "wheel":
                    c.Num["torque"] = Math.Max(Num(m, "PitchTorque"), Math.Max(Num(m, "YawTorque"), Num(m, "RollTorque")));
                    break;
                case "antenna":
                    c.Num["power"] = Num(m, "antennaPower");
                    if (Bool(m, "antennaCombinable")) { c.Num["combinable"] = 1; c.Num["exponent"] = Num(m, "antennaCombinableExponent", 0.75); }
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
                    Config(m, c, true);
                    // Which resource the module is about (a cryostat's fuel, a harvester's ore) decides whether it runs at all.
                    if (!c.Txt.ContainsKey("resourceName")) { string rn = Str(m, "resourceName"); if (rn != "" && rn.Length <= 40) c.Txt["resourceName"] = rn; }
                    if (c.Rates.Count == 0)
                        foreach (var pair in new[] { "inputs|outputs", "Inputs|Outputs", "inputList|outputList", "inputResources|outputResources" })
                        {
                            var io = pair.Split('|');
                            Ratios(Get(m, io[0]), c, -1); Ratios(Get(m, io[1]), c, 1);
                            if (c.Rates.Count > 0) break;
                        }
                    break;
            }
            // A mod's own subclass of a stock module (a heat-loop radiator, a fusion engine): its extra config fields.
            if (kind != "other" && type.Assembly != typeof(PartModule).Assembly) Config(m, c, false);
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
        /// <summary>Which of the module's actions sit on an action group ("Toggle Panels>Custom01"), and how many actions it has.</summary>
        static void Groups(PartModule m, CraftModule c)
        {
            var acts = Get(m, "Actions") as IEnumerable;
            if (acts == null) return;
            int n = 0; var on = new List<string>();
            foreach (var a in acts)
            {
                if (a == null) continue;
                n++;
                object g = Get(a, "actionGroup");
                string name = g == null ? "" : g.ToString();
                if (name != "" && name != "None" && name != "0") on.Add(Clean(Str(a, "guiName")) + ">" + name.Replace(", ", "+"));
            }
            c.Num["actions"] = n;
            if (on.Count > 0) c.Txt["groups"] = string.Join("; ", on.ToArray());
        }

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

        /// <summary>A module's config values (the fields the game loads from the part file). With <paramref name="all"/> every
        /// class level is read; without it only the levels a mod added on top of a stock module.</summary>
        static void Config(PartModule m, CraftModule c, bool all)
        {
            var type = m.GetType();
            var stock = typeof(PartModule).Assembly;
            FieldInfo[] fields;
            if (!configFields.TryGetValue(type, out fields))
            {
                var list = new List<FieldInfo>();
                for (var at = type; at != null && at.Name != "PartModule" && at != typeof(object); at = at.BaseType)
                {
                    if (!all && at.Assembly == stock) break;
                    foreach (var f in at.GetFields(Any))
                    {
                        var ft = f.FieldType;
                        bool curve = ft.Name == "FloatCurve";
                        if (!curve && ft != typeof(float) && ft != typeof(double) && ft != typeof(int) && ft != typeof(bool) && ft != typeof(string)) continue;
                        string lower = f.Name.ToLowerInvariant();
                        if (NoisyField.Any(x => lower.Contains(x))) continue;
                        if (!curve && !f.GetCustomAttributes(true).Any(a => a.GetType().Name == "KSPField")) continue;
                        list.Add(f);
                    }
                }
                fields = list.ToArray();
                configFields[type] = fields;
            }
            bool mod = type.Assembly != stock;
            int maxNum = mod ? 28 : 10, maxTxt = mod ? 6 : 4, nums = 0, txts = 0, curves = 0;
            foreach (var f in fields)
            {
                object v;
                try { v = f.GetValue(m); } catch { continue; }
                if (v == null || c.Num.ContainsKey(f.Name) || c.Txt.ContainsKey(f.Name)) continue;
                if (f.FieldType.Name == "FloatCurve")
                {
                    if (curves >= 4) continue;
                    double max; string pts = CurvePoints(v, out max);
                    if (pts == "") continue;
                    c.Txt[f.Name] = pts; c.Num[f.Name + ".max"] = max; curves++;
                    if (!all) c.Cfg.Add(f.Name + ".max");
                }
                else if (v is string)
                {
                    string s = Clean((string)v);
                    if (s != "" && s.Length <= 40 && txts < maxTxt && !s.StartsWith("#") && s.IndexOf(';') < 0) { c.Txt[f.Name] = s; txts++; if (!all) c.Cfg.Add(f.Name); }
                }
                else if (v is bool) { if ((bool)v && nums < maxNum) { c.Num[f.Name] = 1; nums++; if (!all) c.Cfg.Add(f.Name); } }
                else
                {
                    double d = Convert.ToDouble(v);
                    if (d != 0 && !double.IsNaN(d) && !double.IsInfinity(d) && nums < maxNum) { c.Num[f.Name] = d; nums++; if (!all) c.Cfg.Add(f.Name); }
                }
            }
        }

        /// <summary>A curve from a part file as "x:y;x:y" (at most ten points) and its highest value.</summary>
        static string CurvePoints(object floatCurve, out double max)
        {
            max = 0;
            var keys = Get(Get(floatCurve, "Curve"), "keys") as IEnumerable;
            if (keys == null) return "";
            var pts = new List<string>();
            foreach (var k in keys)
            {
                double x = Num(k, "time"), y = Num(k, "value");
                if (pts.Count == 0 || y > max) max = y;
                if (pts.Count < 10) pts.Add(x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ":" + y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }
            return string.Join(";", pts.ToArray());
        }

        // ------------------------------------------------------------ geometry, cost and the rest

        static object rootT;

        static double[] Vec(object v) { return v == null ? null : new[] { Num(v, "x"), Num(v, "y"), Num(v, "z") }; }

        /// <summary>The part's centre of mass in the root part's frame.</summary>
        static void Place(Part p, CraftPart c)
        {
            if (rootT == null) return;
            object t = Get(p, "transform"), off = Get(p, "CoMOffset");
            if (t == null) return;
            object world = off != null ? Call(t, "TransformPoint", off) : Get(t, "position");
            var v = Vec(Call(rootT, "InverseTransformPoint", world));
            if (v == null) return;
            c.X = v[0]; c.Y = v[1]; c.Z = v[2]; c.HasPos = true;
        }

        /// <summary>Where the engine pushes from and which way its exhaust points (root frame), and the electric charge it
        /// burns per second at full thrust when charge is one of its propellants.</summary>
        static void EngineExtras(PartModule m, CraftModule c)
        {
            var tts = Get(m, "thrustTransforms") as IEnumerable;
            if (tts != null && rootT != null)
            {
                double[] pos = new double[3], dir = new double[3]; int n = 0;
                foreach (var tt in tts)
                {
                    if (tt == null) continue;
                    var a = Vec(Call(rootT, "InverseTransformPoint", Get(tt, "position")));
                    var d = Vec(Call(rootT, "InverseTransformDirection", Get(tt, "forward")));
                    if (a == null || d == null) continue;
                    for (int i = 0; i < 3; i++) { pos[i] += a[i]; dir[i] += d[i]; }
                    n++;
                }
                if (n > 0)
                {
                    double len = Math.Sqrt(dir[0] * dir[0] + dir[1] * dir[1] + dir[2] * dir[2]);
                    c.Num["px"] = pos[0] / n; c.Num["py"] = pos[1] / n; c.Num["pz"] = pos[2] / n;
                    if (len > 1e-6) { c.Num["dx"] = dir[0] / len; c.Num["dy"] = dir[1] / len; c.Num["dz"] = dir[2] / len; c.Num["aimed"] = 1; }
                }
            }
            var props = Get(m, "propellants") as IEnumerable;
            double thrust = c.N("thrust"), isp = c.N("ispVac");
            if (props == null || thrust <= 0 || isp <= 0) return;
            double mix = 0, ec = 0;
            foreach (var p in props)
            {
                double ratio = Num(p, "ratio"), density = Num(Get(p, "resourceDef"), "density");
                mix += ratio * density;
                if (Str(p, "name") == CraftSheet.EC) ec = ratio;
            }
            if (ec > 0 && mix > 0) c.Num["ec"] = ec * (thrust / (isp * 9.80665)) / mix;
        }

        /// <summary>What the part costs as it stands: its price with full tanks, any module extras, less the fuel left out.</summary>
        static double Cost(Part p, AvailablePart info)
        {
            double list = Num(info, "cost"), cost = list;
            try { object extra = Call(p, "GetModuleCosts", (float)list); if (extra != null) cost += Convert.ToDouble(extra); } catch { }
            try
            {
                if (p.Resources != null)
                    foreach (PartResource r in p.Resources)
                        if (r != null) cost -= Math.Max(0, r.maxAmount - r.amount) * Num(Get(r, "info"), "unitCost");
            }
            catch { }
            return Math.Max(0, cost);
        }

        /// <summary>Drag area of the part's parachute once fully open, from the game's own drag cubes.</summary>
        public const double ChuteScale = 0.0156;

        static double ChuteArea(Part p)
        {
            bool chute = false;
            if (p.Modules != null) foreach (PartModule m in p.Modules) if (m != null && Kind(m.GetType()) == "chute") { chute = true; break; }
            if (!chute) return 0;
            var cubes = Get(Get(p, "DragCubes"), "Cubes") as IEnumerable;
            if (cubes == null) return 0;
            // Stock parachutes multiply the open canopy's drag with a ModuleDragModifier on the same part.
            double modifier = 1;
            foreach (PartModule m in p.Modules)
                if (m != null && m.GetType().Name == "ModuleDragModifier" && string.Equals(Str(m, "dragCubeName"), "DEPLOYED", StringComparison.OrdinalIgnoreCase))
                { double d = Num(m, "dragModifier"); if (d > 0) modifier = d; }
            double best = 0;
            foreach (var cube in cubes)
            {
                if (!string.Equals(Str(cube, "Name"), "DEPLOYED", StringComparison.OrdinalIgnoreCase)) continue;
                var area = Get(cube, "Area") as float[]; var drag = Get(cube, "Drag") as float[];
                if (area == null || drag == null) continue;
                for (int i = 0; i < area.Length && i < drag.Length; i++) best = Math.Max(best, area[i] * drag[i]);
            }
            // Calibrated against measured landings: one Mk16-XL (64112 by this sum) brings 3.65 t down at about 8 m/s
            // at Kerbin sea level. CraftChecks.ChuteK expects the area on that scale.
            return best * modifier * ChuteScale;
        }

        static readonly Dictionary<string, MethodInfo> methods = new Dictionary<string, MethodInfo>();
        const BindingFlags AnyMethod = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

        /// <summary>Calls a method by name (the overload with this many arguments whose types fit). Null when it is not there.</summary>
        public static object Call(object o, string name, params object[] args)
        {
            if (o == null) return null;
            var type = o as Type ?? o.GetType();
            string key = type.FullName + "." + name + "/" + args.Length + "/" + string.Join(",", args.Select(a => a == null ? "?" : a.GetType().Name).ToArray());
            MethodInfo mi;
            if (!methods.TryGetValue(key, out mi))
            {
                for (var at = type; at != null && mi == null; at = at.BaseType)
                    mi = at.GetMethods(AnyMethod).FirstOrDefault(x => x.Name == name && !x.IsGenericMethodDefinition && x.GetParameters().Length >= args.Length
                        && x.GetParameters().Select((pi, i) => i >= args.Length ? pi.IsOptional : args[i] == null || pi.ParameterType.IsInstanceOfType(args[i])).All(ok => ok));
                methods[key] = mi;
            }
            if (mi == null) return null;
            try
            {
                var ps = mi.GetParameters();
                var full = ps.Length == args.Length ? args : args.Concat(ps.Skip(args.Length).Select(pi => pi.DefaultValue)).ToArray();
                return mi.Invoke(mi.IsStatic ? null : o, full);
            }
            catch { return null; }
        }

        static readonly Dictionary<string, Type> types = new Dictionary<string, Type>();
        /// <summary>A type by full name from any loaded assembly (a mod's, or the game's), or null.</summary>
        public static Type FindType(string full)
        {
            Type t;
            if (types.TryGetValue(full, out t)) return t;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { t = a.GetType(full, false); } catch { t = null; }
                if (t != null) break;
            }
            types[full] = t;
            return t;
        }

        /// <summary>A static field or property by name.</summary>
        public static object Static(Type t, string name)
        {
            if (t == null) return null;
            const BindingFlags f = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
            try
            {
                var fi = t.GetField(name, f);
                if (fi != null) return fi.GetValue(null);
                var pi = t.GetProperty(name, f);
                if (pi != null && pi.CanRead && pi.GetIndexParameters().Length == 0) return pi.GetValue(null, null);
            }
            catch { }
            return null;
        }

        static double ToNum(object v, double def = 0)
        {
            if (v == null || v is string || v is bool) return def;
            try { double d = Convert.ToDouble(v); return double.IsNaN(d) || double.IsInfinity(d) ? def : d; } catch { return def; }
        }

        /// <summary>A difficulty setting from one of the game's settings pages, by the page's type name.</summary>
        static object GameParam(string typeName, string field)
        {
            var t = FindType(typeName);
            var game = HighLogic.CurrentGame;
            if (t == null || game == null) return null;
            object pars = Get(game, "Parameters");
            if (pars == null) return null;
            var mi = pars.GetType().GetMethods().FirstOrDefault(x => x.Name == "CustomParams" && x.IsGenericMethodDefinition && x.GetParameters().Length == 0);
            if (mi == null) return null;
            try { return Get(mi.MakeGenericMethod(t).Invoke(pars, null), field); } catch { return null; }
        }

        /// <summary>Facts about the save that the checks need. Each is read on its own, so one that is missing costs only itself.</summary>
        public static void Facts(CraftSheet sheet, object ship, bool sph)
        {
            var f = sheet.Facts;
            Action<string, Func<double>> put = (key, get) => { try { double v = get(); if (!double.IsNaN(v) && !double.IsInfinity(v)) f[key] = v; } catch { } };
            put("day", () => ToNum(Get(Static(FindType("KSPUtil"), "dateTimeFormatter"), "Day"), 21600));
            put("year", () => ToNum(Get(Static(FindType("KSPUtil"), "dateTimeFormatter"), "Year"), 9203400));
            var career = HighLogic.CurrentGame != null && HighLogic.CurrentGame.Mode == Game.Modes.CAREER;
            f["career"] = career ? 1 : 0;

            // CommNet: is it on, how far the tracking station reaches, and whether a probe needs a link to be flown.
            var cn = FindType("CommNet.CommNetScenario");
            object on = Static(cn, "CommNetEnabled");
            if (on is bool) f["commnet"] = (bool)on ? 1 : 0;
            put("rangeMod", () => ToNum(Static(cn, "RangeModifier"), 1));
            object need = GameParam("CommNet.CommNetParams", "requireSignalForControl");
            if (need is bool) f["signalForControl"] = (bool)need ? 1 : 0;
            put("dsnMod", () => ToNum(GameParam("CommNet.CommNetParams", "DSNModifier"), 1));
            var vars = Static(FindType("GameVariables"), "Instance");
            var suf = FindType("ScenarioUpgradeableFacilities");
            var fac = FindType("SpaceCenterFacility");
            Func<string, double> level = name =>
            {
                if (suf == null || fac == null) return -1;
                object lv = Call(suf, "GetFacilityLevel", Enum.Parse(fac, name));
                return lv == null ? -1 : ToNum(lv, -1);
            };
            double ts = level("TrackingStation");
            if (vars != null && ts >= 0)
            {
                put("dsnPower", () => ToNum(Call(vars, "GetDSNRange", (float)ts), 0));
                f["trackingLevel"] = Math.Round(ts * 2) + 1;
            }

            // What the launch site and the building can take, in a career.
            if (vars != null && career && !sheet.Flying)
            {
                double site = level(sph ? "Runway" : "LaunchPad"), hall = level(sph ? "SpaceplaneHangar" : "VehicleAssemblyBuilding");
                if (site >= 0)
                {
                    put("massLimit", () => ToNum(Call(vars, "GetCraftMassLimit", (float)site, !sph), 0));
                    var size = Vec(Call(vars, "GetCraftSizeLimit", (float)site, !sph));
                    if (size != null) { f["limitX"] = size[0]; f["limitY"] = size[1]; f["limitZ"] = size[2]; }
                    f["siteLevel"] = Math.Round(site * 2) + 1;
                }
                if (hall >= 0) { put("partLimit", () => ToNum(Call(vars, "GetPartCountLimit", (float)hall, !sph), 0)); f["hallLevel"] = Math.Round(hall * 2) + 1; }
            }
            if (ship != null)
            {
                var sz = Vec(Call(ship, "CalculateCraftSize")) ?? Vec(Get(ship, "shipSize"));
                if (sz != null && sz[0] + sz[1] + sz[2] > 0) { f["sizeX"] = sz[0]; f["sizeY"] = sz[1]; f["sizeZ"] = sz[2]; }
            }
            f["sph"] = sph ? 1 : 0;
        }

        /// <summary>
        /// Every kind of module that mods add to the parts in this install, with its class line and config values: written to
        /// a file by the self-test so the mod calculators can be checked against what is really installed.
        /// </summary>
        public static string Census()
        {
            var sb = new System.Text.StringBuilder();
            var stock = typeof(PartModule).Assembly;
            var seen = new Dictionary<Type, int>();
            var sample = new Dictionary<Type, string>();
            var loaded = PartLoader.LoadedPartsList;
            if (loaded == null) return "";
            foreach (var ap in loaded)
            {
                var prefab = Get(ap, "partPrefab") as Part;
                if (prefab == null || prefab.Modules == null) continue;
                foreach (PartModule m in prefab.Modules)
                {
                    if (m == null) continue;
                    var t = m.GetType();
                    if (t.Assembly == stock) continue;
                    int n; seen.TryGetValue(t, out n); seen[t] = n + 1;
                    if (sample.ContainsKey(t)) continue;
                    var line = new System.Text.StringBuilder();
                    for (var at = t.BaseType; at != null && at.Name != "PartModule" && at != typeof(object); at = at.BaseType) line.Append(" : " + at.Name);
                    line.Append(" [" + t.Assembly.GetName().Name + "] e.g. " + ap.name + " {");
                    try
                    {
                        for (var at = t; at != null && at.Assembly != stock && at != typeof(object); at = at.BaseType)
                            foreach (var fi in at.GetFields(Any))
                            {
                                if (!fi.GetCustomAttributes(true).Any(a => a.GetType().Name == "KSPField") && fi.FieldType.Name != "FloatCurve") continue;
                                object v = null; try { v = fi.GetValue(m); } catch { }
                                string text;
                                if (fi.FieldType.Name == "FloatCurve") { double mx; text = "curve(" + CurvePoints(v, out mx) + ")"; }
                                else text = v == null ? "null" : Clean(v.ToString());
                                if (text.Length > 60) text = text.Substring(0, 60) + "...";
                                line.Append(" " + fi.Name + "=" + text + ";");
                            }
                    }
                    catch (Exception e) { line.Append(" (" + e.GetType().Name + ")"); }
                    line.Append(" }");
                    sample[t] = line.ToString();
                }
            }
            foreach (var kv in seen.OrderBy(k => k.Key.Assembly.GetName().Name).ThenBy(k => k.Key.Name))
                sb.AppendLine(kv.Key.Name + " x" + kv.Value + " kind=" + Kind(kv.Key) + sample[kv.Key]);
            return sb.ToString();
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
                    e.AtmoDensity = b.atmosphere ? Num(b, "atmDensityASL") : 0;
                    e.Soi = double.IsInfinity(b.sphereOfInfluence) ? 0 : b.sphereOfInfluence;
                    e.RotPeriod = Num(b, "rotationPeriod");
                    object solid = Get(b, "hasSolidSurface");
                    e.HasSurface = !(solid is bool) || (bool)solid;
                    var parent = Get(b, "referenceBody") as CelestialBody;
                    object orbit = Get(b, "orbit");
                    e.IsStar = orbit == null || parent == null || parent == b;
                    if (!e.IsStar) { e.Parent = parent.bodyName; e.Sma = Num(orbit, "semiMajorAxis"); e.Period = Num(orbit, "period"); }
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
