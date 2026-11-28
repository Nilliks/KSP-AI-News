using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace KerbalNews.KSP
{
    /// <summary>
    /// Soft links to other mods, found by reflection so KNN works with or without them.
    /// FMRS: FMRS.FMRS_Core.Instance (an IFMRS) has Active and SwitchedToDropped; while switched to a dropped stage
    /// the game is a side timeline that FMRS will throw away. FMRS sets SwitchedToDropped about a second BEFORE it loads
    /// the other save (FlightDriver.StartAndFocusVessel, a full flight-scene reload), so for that second the flag already
    /// describes the scene that is coming. StreamDriver.SceneSide is the flag as it was when this scene started.
    /// </summary>
    static class ModCompat
    {
        static bool looked;
        static PropertyInfo fmrsInstance;
        static float nextCheck;
        static bool cachedSide;

        internal static Type FindType(string fullName)
        {
            foreach (var a in AssemblyLoader.loadedAssemblies)
            {
                if (a == null || a.assembly == null) continue;
                try { var t = a.assembly.GetType(fullName, false); if (t != null) return t; } catch { }
            }
            return null;
        }

        static void Look()
        {
            if (looked) return;
            looked = true;
            try
            {
                var core = FindType("FMRS.FMRS_Core");
                if (core != null) fmrsInstance = core.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                if (fmrsInstance != null) Debug.Log("[KerbalNews] FMRS found: dropped-stage flights are kept out of the news.");
            }
            catch (Exception e) { Debug.LogWarning("[KerbalNews] FMRS check failed: " + e.Message); }
        }

        public static bool FmrsInstalled { get { Look(); return fmrsInstance != null; } }

        /// <summary>True while FMRS is flying a dropped stage. Checked at most twice a second.</summary>
        public static bool InFmrsSideFlight()
        {
            Look();
            if (fmrsInstance == null || !HighLogic.LoadedSceneIsFlight) return false;
            if (Time.realtimeSinceStartup < nextCheck) return cachedSide;
            nextCheck = Time.realtimeSinceStartup + 0.5f;
            try
            {
                var inst = fmrsInstance.GetValue(null, null);
                cachedSide = inst != null && Bool(inst, "SwitchedToDropped");
            }
            catch { cachedSide = false; }
            return cachedSide;
        }

        /// <summary>FMRS's flag this instant, uncached and in any scene: true once a jump to a dropped stage has been ordered.</summary>
        public static bool FmrsSwitchedRaw()
        {
            Look();
            if (fmrsInstance == null) return false;
            try
            {
                var inst = fmrsInstance.GetValue(null, null);
                return inst != null && Bool(inst, "SwitchedToDropped");
            }
            catch { return false; }
        }

        /// <summary>The vessel FMRS calls the main mission, when it is in the current scene. Null without FMRS.</summary>
        public static Vessel FmrsMainVessel()
        {
            Look();
            if (fmrsInstance == null || FlightGlobals.Vessels == null) return null;
            try
            {
                var inst = fmrsInstance.GetValue(null, null);
                if (inst == null) return null;
                object id = null;
                for (var t = inst.GetType(); t != null && id == null; t = t.BaseType)
                {
                    var f = t.GetField("_SAVE_Main_Vessel", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    if (f != null) id = f.GetValue(inst);
                }
                if (!(id is Guid)) return null;
                var g = (Guid)id;
                foreach (var v in FlightGlobals.Vessels) if (v != null && v.id == g) return v;
            }
            catch { }
            return null;
        }

        // ------------------------------------------------------------------ VesselMover

        static bool vmLooked;
        static Type vmType;
        static float vmLastMoving = -100, vmNext;
        static uint vmVessel;

        static object Member(Type t, object o, string name, bool isStatic)
        {
            var flags = BindingFlags.Public | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            var f = t.GetField(name, flags);
            if (f != null) return f.GetValue(o);
            var p = t.GetProperty(name, flags);
            return p != null ? p.GetValue(o, null) : null;
        }

        /// <summary>True while VesselMover is moving a vessel, and for a few seconds after, so a teleport is not reported as a
        /// launch, landing or orbit. Null id means any vessel.</summary>
        public static bool VesselMoverBusy(string persistentId)
        {
            if (!vmLooked) { vmLooked = true; try { vmType = FindType("VesselMover.VesselMove"); } catch { } if (vmType != null) Debug.Log("[KerbalNews] VesselMover found: moved vessels make no news."); }
            if (vmType == null || !HighLogic.LoadedSceneIsFlight) return false;
            if (Time.realtimeSinceStartup >= vmNext)
            {
                vmNext = Time.realtimeSinceStartup + 0.25f;
                try
                {
                    var inst = Member(vmType, null, "Instance", true);
                    if (inst != null && Member(vmType, inst, "IsMovingVessel", false) is bool && (bool)Member(vmType, inst, "IsMovingVessel", false))
                    {
                        vmLastMoving = Time.realtimeSinceStartup;
                        var v = Member(vmType, inst, "MovingVessel", false) as Vessel;
                        vmVessel = v != null ? v.persistentId : 0;
                    }
                }
                catch { }
            }
            if (Time.realtimeSinceStartup - vmLastMoving > 8) return false;
            return persistentId == null || vmVessel == 0 || persistentId == vmVessel.ToString();
        }

        // ------------------------------------------------------------------ DeepFreeze

        static bool dfLooked;
        static Type dfType;

        /// <summary>Names of kerbals frozen in DeepFreeze pods (empty without DeepFreeze). Frozen is not dead.</summary>
        public static System.Collections.Generic.HashSet<string> FrozenKerbals()
        {
            var set = new System.Collections.Generic.HashSet<string>();
            if (!dfLooked) { dfLooked = true; try { dfType = FindType("DF.DeepFreeze"); } catch { } }
            if (dfType == null) return set;
            try
            {
                var inst = Member(dfType, null, "Instance", true);
                var dict = inst == null ? null : Member(dfType, inst, "FrozenKerbals", false) as System.Collections.IDictionary;
                if (dict != null) foreach (var k in dict.Keys) if (k is string) set.Add((string)k);
            }
            catch { }
            return set;
        }

        static bool Bool(object o, string name)
        {
            var t = o.GetType();
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p == null)
                foreach (var i in t.GetInterfaces()) { p = i.GetProperty(name); if (p != null) break; }
            if (p == null) return false;
            var v = p.GetValue(o, null);
            return v is bool && (bool)v;
        }
    }
}
