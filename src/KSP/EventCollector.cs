using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KSP.UI.Screens;
using UnityEngine;

namespace KerbalNews.KSP
{
    /// <summary>
    /// Turns stock GameEvents into news events. Uses only stock events and patches nothing,
    /// so it coexists with other mods.
    /// </summary>
    public class EventCollector
    {
        readonly Func<NewsEngine> engine;
        bool sceneChanging;
        // Vessels that are leaving the world for reasons that are not disasters.
        readonly Dictionary<Guid, float> benignDestroy = new Dictionary<Guid, float>();
        readonly HashSet<string> killedReported = new HashSet<string>();

        public EventCollector(Func<NewsEngine> engine) { this.engine = engine; }

        static double UT { get { return Planetarium.GetUniversalTime(); } }
        static string Id(Vessel v) { return v.persistentId.ToString(); }

        public void Register()
        {
            GameEvents.onLaunch.Add(OnLaunch);
            GameEvents.onVesselSituationChange.Add(OnSituation);
            GameEvents.onVesselSOIChanged.Add(OnSoi);
            GameEvents.onPartCouple.Add(OnCouple);
            GameEvents.onCrewKilled.Add(OnCrewKilled);
            GameEvents.onVesselWillDestroy.Add(OnVesselWillDestroy);
            GameEvents.onVesselRecovered.Add(OnRecovered);
            GameEvents.onVesselRecoveryProcessing.Add(OnRecoveryProcessing);
            GameEvents.onVesselTerminated.Add(OnTerminated);
            GameEvents.OnProgressComplete.Add(OnProgress);
            GameEvents.OnScienceRecieved.Add(OnScience);
            GameEvents.Contract.onCompleted.Add(OnContractCompleted);
            GameEvents.Contract.onFailed.Add(OnContractFailed);
            GameEvents.Contract.onAccepted.Add(OnContractAccepted);
            GameEvents.Contract.onDeclined.Add(OnContractDeclined);
            GameEvents.Contract.onCancelled.Add(OnContractCancelled);
            GameEvents.onGameSceneLoadRequested.Add(OnSceneLoadRequested);
            GameEvents.onLevelWasLoadedGUIReady.Add(OnSceneLoaded);
        }

        public void Unregister()
        {
            GameEvents.onLaunch.Remove(OnLaunch);
            GameEvents.onVesselSituationChange.Remove(OnSituation);
            GameEvents.onVesselSOIChanged.Remove(OnSoi);
            GameEvents.onPartCouple.Remove(OnCouple);
            GameEvents.onCrewKilled.Remove(OnCrewKilled);
            GameEvents.onVesselWillDestroy.Remove(OnVesselWillDestroy);
            GameEvents.onVesselRecovered.Remove(OnRecovered);
            GameEvents.onVesselRecoveryProcessing.Remove(OnRecoveryProcessing);
            GameEvents.onVesselTerminated.Remove(OnTerminated);
            GameEvents.OnProgressComplete.Remove(OnProgress);
            GameEvents.OnScienceRecieved.Remove(OnScience);
            GameEvents.Contract.onCompleted.Remove(OnContractCompleted);
            GameEvents.Contract.onFailed.Remove(OnContractFailed);
            GameEvents.Contract.onDeclined.Remove(OnContractDeclined);
            GameEvents.Contract.onCancelled.Remove(OnContractCancelled);
            GameEvents.Contract.onAccepted.Remove(OnContractAccepted);
            GameEvents.onGameSceneLoadRequested.Remove(OnSceneLoadRequested);
            GameEvents.onLevelWasLoadedGUIReady.Remove(OnSceneLoaded);
        }

        void OnSceneLoadRequested(GameScenes s) { sceneChanging = true; }
        void OnSceneLoaded(GameScenes s) { sceneChanging = false; benignDestroy.Clear(); }

        void Safe(string what, Action a)
        {
            try
            {
                if (engine() == null || HighLogic.CurrentGame == null) return;
                a();
            }
            catch (Exception e) { Debug.LogError("[KerbalNews] " + what + " failed: " + e); }
        }

        NewsEvent Make(string type, Vessel v)
        {
            var e = new NewsEvent { Type = type, UT = UT };
            if (v != null)
            {
                e.VesselId = Id(v);
                e.VesselName = Names.Of(v);
                e.Body = v.mainBody != null ? v.mainBody.bodyName : "";
                e.Situation = v.situation.ToString();
                foreach (var c in v.GetVesselCrew()) e.Crew.Add(c.name);
            }
            return e;
        }

        static bool Newsworthy(Vessel v)
        {
            return v != null && v.vesselType != VesselType.Debris && v.vesselType != VesselType.EVA && v.vesselType != VesselType.Flag
                && v.vesselType != VesselType.SpaceObject && v.vesselType != VesselType.Unknown && v.vesselType != VesselType.DeployedScienceController
                && v.vesselType != VesselType.DeployedSciencePart;
        }

        /// <summary>FMRS: the dropped stage being flown counts even when the game files it as debris (no probe core, only parachutes).</summary>
        static bool Covered(Vessel v)
        {
            return Newsworthy(v) || v != null && v.vesselType == VesselType.Debris && StreamDriver.SideBoosterId != "" && v.persistentId.ToString() == StreamDriver.SideBoosterId;
        }

        /// <summary>
        /// The press follows the active vessel even if its launch predates the mod or it was born
        /// from a docking or undocking, so start tracking it lazily.
        /// </summary>
        TrackedVessel Track(Vessel v)
        {
            var eng = engine();
            var t = eng.Tracked(Id(v));
            if (t != null) return t;
            if (v != FlightGlobals.ActiveVessel || !Covered(v)) return null;
            eng.TrackLaunch(Id(v), Snapshots.Craft(v), UT);
            return eng.Tracked(Id(v));
        }

        static bool Flag(TrackedVessel t, string flag)
        {
            if (t.Flags.Contains(flag)) return false;
            t.Flags.Add(flag);
            return true;
        }

        // -------------------------------------------------------------- handlers

        void OnLaunch(EventReport r)
        {
            Safe("launch", () =>
            {
                var v = FlightGlobals.ActiveVessel;
                if (!Newsworthy(v)) return;
                var craft = Snapshots.Craft(v);
                engine().TrackLaunch(Id(v), craft, UT);
                var e = Make(EventTypes.Launch, v);
                e.Detail = craft.LaunchSite;
                engine().Ingest(e);
            });
        }

        void OnSituation(GameEvents.HostedFromToAction<Vessel, Vessel.Situations> d)
        {
            Safe("situation", () =>
            {
                var v = d.host;
                if (!Covered(v)) return;
                var from = d.from;
                var to = d.to;
                if (from == Vessel.Situations.PRELAUNCH) return; // launch is reported by onLaunch
                var t = Track(v);
                if (t == null) return;
                string body = v.mainBody != null ? v.mainBody.bodyName : "";
                if (to == Vessel.Situations.ORBITING)
                {
                    t.Flags.RemoveAll(f => f.StartsWith("landed:") || f.StartsWith("splashed:"));
                    if (Flag(t, "orbit:" + body)) engine().Ingest(Make(EventTypes.Orbit, v));
                }
                else if (to == Vessel.Situations.LANDED && (from == Vessel.Situations.FLYING || from == Vessel.Situations.SUB_ORBITAL))
                {
                    if (UT - t.LaunchUT < 30) return; // hop off the pad
                    if (Flag(t, "landed:" + body))
                    {
                        var e = Make(EventTypes.Landing, v);
                        e.Detail = v.horizontalSrfSpeed > 30 ? "The craft was moving fast at touchdown." : "";
                        engine().Ingest(e);
                    }
                }
                else if (to == Vessel.Situations.SPLASHED && (from == Vessel.Situations.FLYING || from == Vessel.Situations.SUB_ORBITAL))
                {
                    if (Flag(t, "splashed:" + body)) engine().Ingest(Make(EventTypes.Splashdown, v));
                }
            });
        }

        void OnSoi(GameEvents.HostedFromToAction<Vessel, CelestialBody> d)
        {
            Safe("soi", () =>
            {
                var v = d.host;
                if (!Newsworthy(v) || d.to == null) return;
                var t = Track(v);
                if (t == null) return;
                // Leaving a moon back into Kerbin's SOI is not news; arriving somewhere new is.
                if (d.to.bodyName == "Kerbin" || d.to == Planetarium.fetch.Sun) return;
                if (!Flag(t, "soi:" + d.to.bodyName)) return;
                var e = Make(EventTypes.Soi, v);
                e.Body = d.to.bodyName;
                engine().Ingest(e);
            });
        }

        void OnCouple(GameEvents.FromToAction<Part, Part> d)
        {
            Safe("docking", () =>
            {
                if (d.from == null || d.to == null || d.from.vessel == null || d.to.vessel == null) return;
                var a = d.from.vessel;
                var b = d.to.vessel;
                benignDestroy[a.id] = Time.realtimeSinceStartup;
                benignDestroy[b.id] = Time.realtimeSinceStartup;
                if (!Newsworthy(a) || !Newsworthy(b)) return; // claw grabbing debris etc. is not a docking story
                var tracked = engine().Tracked(Id(b)) != null ? b : engine().Tracked(Id(a)) != null ? a : FlightGlobals.ActiveVessel;
                if (tracked == null) return;
                var other = tracked == a ? b : a;
                if (Track(tracked) == null && engine().Tracked(Id(tracked)) == null) return;
                var e = Make(EventTypes.Docking, tracked);
                e.Detail = Names.Of(other);
                engine().Ingest(e);
            });
        }

        void OnCrewKilled(EventReport r)
        {
            Safe("crew killed", () =>
            {
                string name = r.sender;
                if (string.IsNullOrEmpty(name) || !killedReported.Add(name + "@" + Math.Floor(UT))) return;
                var v = r.origin != null ? r.origin.vessel : FlightGlobals.ActiveVessel;
                var e = Make(EventTypes.CrewKilled, v);
                e.Crew.Clear();
                e.Crew.Add(name);
                if (v != null && engine().Tracked(Id(v)) == null)
                {
                    var t = Track(v);
                    if (t == null) e.VesselId = "";
                }
                engine().Ingest(e);
            });
        }

        void OnRecoveryProcessing(ProtoVessel pv, MissionRecoveryDialog dialog, float f)
        {
            if (pv != null) benignDestroy[pv.vesselID] = Time.realtimeSinceStartup;
        }

        void OnTerminated(ProtoVessel pv)
        {
            if (pv != null) benignDestroy[pv.vesselID] = Time.realtimeSinceStartup;
        }

        /// <summary>Science reaching Kerbin, transmitted or recovered. Kept for the journal; it makes no story by itself.</summary>
        void OnScience(float amount, ScienceSubject subject, ProtoVessel pv, bool reverseEngineered)
        {
            Safe("science", () =>
            {
                if (subject == null || amount <= 0 || reverseEngineered) return;
                string exp, body, sit, biome;
                ScienceResult.ParseSubject(subject.id, FlightGlobals.Bodies != null ? FlightGlobals.Bodies.Select(b => b.bodyName) : null, out exp, out body, out sit, out biome);
                var r = new ScienceResult
                {
                    UT = UT, SubjectId = subject.id ?? "", Title = subject.title ?? "", Experiment = exp, Body = body, Situation = sit, Biome = biome,
                    Amount = amount, Completion = subject.scienceCap > 0 ? Math.Min(1, subject.science / subject.scienceCap) : 0,
                };
                // The game's flavour text for this result (stock ScienceDefs and Kerbalism's both feed this table).
                try { r.ResultText = Names.Of(ResearchAndDevelopment.GetResults(subject.id) ?? ""); } catch (Exception) { }
                if (pv != null)
                {
                    r.VesselId = pv.persistentId.ToString();
                    r.VesselName = Names.Of(pv.vesselName);
                    var crew = pv.GetVesselCrew();
                    if (crew != null) foreach (var c in crew) r.Crew.Add(c.name);
                }
                else if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null)
                {
                    // Transmissions come with no ProtoVessel: credit the craft in flight.
                    r.Transmitted = true;
                    r.VesselId = Id(FlightGlobals.ActiveVessel);
                    r.VesselName = Names.Of(FlightGlobals.ActiveVessel);
                }
                engine().RecordScience(r);
            });
        }

        void OnRecovered(ProtoVessel pv, bool quick)
        {
            Safe("recovery", () =>
            {
                if (pv == null) return;
                benignDestroy[pv.vesselID] = Time.realtimeSinceStartup;
                string id = pv.persistentId.ToString();
                var t = engine().Tracked(id);
                MiningLink.NoteRecovery(pv, t);   // any ore aboard reaches the warehouse a moment later
                if (t == null) return;
                if (!Flag(t, "recovered")) return;
                var e = new NewsEvent { Type = EventTypes.Recovery, UT = UT, VesselId = id, VesselName = Names.Of(pv.vesselName), Body = "Kerbin" };
                foreach (var c in pv.GetVesselCrew()) e.Crew.Add(c.name);
                engine().Ingest(e);
            });
        }

        void OnVesselWillDestroy(Vessel v)
        {
            Safe("vessel destroyed", () =>
            {
                if (v == null || sceneChanging || !HighLogic.LoadedSceneIsFlight) return;
                float when;
                if (benignDestroy.TryGetValue(v.id, out when) && Time.realtimeSinceStartup - when < 10) return;
                var t = engine().Tracked(Id(v));
                if (t == null) return;
                if (v.situation == Vessel.Situations.PRELAUNCH) return; // rolled out and scrapped
                if (!Flag(t, "destroyed")) return;
                var e = Make(EventTypes.Failure, v);
                e.Crew.Clear();
                if (t.Craft != null) e.Crew.AddRange(t.Craft.Crew); // the public knows who was aboard at launch
                e.Detail = "The vessel was destroyed" + (v.mainBody != null ? " " + DescribeWhere(v) : "") + ".";
                string ore = MiningLink.CargoLossText(v);
                if (ore != "") e.Detail += " " + ore;
                engine().Ingest(e);
            });
        }

        static string DescribeWhere(Vessel v)
        {
            switch (v.situation)
            {
                case Vessel.Situations.FLYING: return "in flight at about " + Math.Round(v.altitude / 1000.0, 1) + " km altitude";
                case Vessel.Situations.SUB_ORBITAL: return "on a suborbital trajectory";
                case Vessel.Situations.ORBITING: return "in orbit";
                case Vessel.Situations.LANDED: return "on the surface";
                case Vessel.Situations.SPLASHED: return "on the water";
                default: return "";
            }
        }

        void OnProgress(ProgressNode node)
        {
            Safe("milestone", () =>
            {
                if (node == null) return;
                string body = BodyOf(node);
                string detail = MilestoneText(node.Id, body);
                if (detail == null) return;
                var v = HighLogic.LoadedSceneIsFlight ? FlightGlobals.ActiveVessel : null;
                var e = Make(EventTypes.Milestone, v);
                e.Detail = detail;
                if (body != "") e.Body = body;
                engine().Ingest(e);
            });
        }

        /// <summary>Body-specific progress nodes carry their body in a field; read it without depending on its exact type.</summary>
        static string BodyOf(ProgressNode node)
        {
            for (var t = node.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                var f = t.GetField("body", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && typeof(CelestialBody).IsAssignableFrom(f.FieldType))
                {
                    var b = f.GetValue(node) as CelestialBody;
                    if (b != null) return b.bodyName;
                }
            }
            return "";
        }

        static string MilestoneText(string id, string body)
        {
            string at = body != "" ? " " + body : "";
            switch (id)
            {
                case "FirstLaunch": return "first launch";
                case "ReachedSpace": return "first flight to reach space";
                case "Orbit": return body == "Kerbin" ? "first orbit of Kerbin" : "first orbit of" + at;
                case "Landing": return "first landing on" + at;
                case "Splashdown": return "first splashdown on" + at;
                case "Flyby": return "first flyby of" + at;
                case "Escape": return "first escape from" + at + "'s gravity";
                case "FlagPlant": return "first flag planted on" + at;
                case "SurfaceEVA": return "first kerbonaut to walk on" + at;
                case "Spacewalk": return "first spacewalk" + (body != "" && body != "Kerbin" ? " near" + at : "");
                case "Rendezvous": return "first orbital rendezvous" + (body != "" && body != "Kerbin" ? " around" + at : "");
                case "Docking": return "first docking" + (body != "" && body != "Kerbin" ? " around" + at : "");
                case "Science": return null; // too frequent
                case "ReturnFromOrbit": return "first return from orbit of" + at;
                case "ReturnFromFlyBy": return "first return from a flyby of" + at;
                case "ReturnFromSurface": return "first return from the surface of" + at;
                case "CrewRecovery": return null;
                case "BaseConstruction": return "first surface base on" + at;
                case "StationConstruction": return "first space station around" + at;
                case "POIExploration": return null;
                default:
                    if (id != null && id.StartsWith("Records")) return null; // altitude/speed records tick constantly
                    return null;
            }
        }

        void OnContractCompleted(Contracts.Contract c) { Safe("contract", () => engine().Ingest(new NewsEvent { Type = EventTypes.ContractDone, UT = UT, Detail = c.Title })); }
        void OnContractFailed(Contracts.Contract c) { Safe("contract", () => engine().Ingest(new NewsEvent { Type = EventTypes.ContractFailed, UT = UT, Detail = c.Title })); }
        void OnContractDeclined(Contracts.Contract c) { var k = c as KnnContract; if (k != null) Safe("contract", () => engine().TieInChanged(k.TieId, "declined")); }
        void OnContractCancelled(Contracts.Contract c) { var k = c as KnnContract; if (k != null) Safe("contract", () => engine().TieInChanged(k.TieId, "cancelled")); }
        void OnContractAccepted(Contracts.Contract c) { Safe("contract", () => engine().Ingest(new NewsEvent { Type = EventTypes.ContractAccepted, UT = UT, Detail = c.Title })); }
    }
}
