using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KerbalNews.KSP
{
    /// <summary>
    /// Lives for the whole session (DontDestroyOnLoad). Owns the engine, the HTTP transport and the
    /// event hooks, so an AI request survives a scene change. The news itself lives in the save,
    /// through NewsScenario, which hands its state to the engine on every load.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class NewsSystem : MonoBehaviour
    {
        public static NewsSystem Instance { get; private set; }
        public static GlobalSettings Settings;

        public NewsEngine Engine { get; private set; }
        public KerbalNews.Web.Sites Sites { get; private set; }
        public bool HasGame { get; private set; }
        readonly UnityHttpTransport transport = new UnityHttpTransport();
        readonly KspClock clock = new KspClock();
        EventCollector collector;
        BlackBox blackBox;

        void Awake()
        {
            if (Instance != null) { Destroy(this); return; }
            Instance = this;
            DontDestroyOnLoad(this);
            Settings = SettingsStore.Load();
            Engine = new NewsEngine(new NewsState(), Settings, transport, clock);
            Engine.LocalizeTag = Names.Of;
            Engine.Digest = () => Snapshots.Digest(Engine.State);
            Engine.Bodies = GameBodies;
            Engine.PhotoJpeg = GalleryStore.ForVision;
            Engine.StreamFrameJpeg = GalleryStore.StreamFrame;
            Engine.WorldScan = ut => HasGame ? WorldScanner.Scan(Engine.State, ut, clock.DaySeconds) : new System.Collections.Generic.List<WorldFact>();
            Engine.EngineeringData = EngineeringCapture.Snapshot;
            Engine.SideTimeline = ModCompat.InFmrsSideFlight;
            Engine.SuppressEvent = e => e != null && e.VesselId != "" && ModCompat.VesselMoverBusy(e.VesselId);
            Engine.PostContract = KnnContract.Post;
            Engine.ContractsAvailable = KnnContract.Available;
            // Career money for the assistant's deals. Sandbox and science games have none.
            Engine.FundsAvailable = () => HighLogic.CurrentGame != null && HighLogic.CurrentGame.Mode == Game.Modes.CAREER && Funding.Instance != null ? Funding.Instance.Funds : double.NaN;
            Engine.ChangeFunds = (d, why) =>
            {
                if (Funding.Instance == null) return;
                Funding.Instance.AddFunds(d, TransactionReasons.None);
                ScreenMessages.PostScreenMessage("Kerbal News: " + why + " (" + (d >= 0 ? "+" : "-") + "√" + NewsEngine.Money(Math.Abs(d)) + ")", 4f, ScreenMessageStyle.UPPER_RIGHT);
            };
            Engine.ArticlesPublished += OnPublished;
            Engine.MailArrived += OnMail;
            Engine.SaveImage = SaveImage;
            Engine.PressShotRemoved += file =>
            {
                try { var path = System.IO.Path.Combine(GalleryStore.Dir, System.IO.Path.GetFileName(file)); if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
                catch (System.Exception e) { Debug.LogWarning("[KerbalNews] Could not delete old press photo: " + e.Message); }
            };
            string sitesDir = System.IO.Path.Combine(KSPUtil.ApplicationRootPath, "GameData/KerbalNews/Sites");
            Sites = new KerbalNews.Web.Sites(Engine, rel =>
            {
                var p = System.IO.Path.Combine(sitesDir, rel);
                return System.IO.File.Exists(p) ? System.IO.File.ReadAllText(p) : null;
            });
            Sites.Now = () => Planetarium.GetUniversalTime();
            Sites.ImageExists = Browser.ImageExists;
            Sites.Digest = () => HasGame ? Snapshots.Digest(Engine.State) : null;
            Sites.UiScale = () => Browser.Scale;
            Sites.SaveSettings = SaveSettings;
            Sites.RescanMods = () => ScanMods(false);
            Sites.ImportScreenshots = GalleryStore.ImportStock;
            Sites.ActiveVesselId = () => HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.persistentId.ToString() : null;
            Sites.ActiveVesselName = () => HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null ? Names.Of(FlightGlobals.ActiveVessel) : null;
            collector = new EventCollector(() => HasGame ? Engine : null);
            collector.Register();
            blackBox = new BlackBox(() => HasGame ? Engine : null);
            blackBox.Register();
            GameEvents.onGameSceneLoadRequested.Add(OnSceneRequested);
            ScanMods(true);
            Debug.Log("[KerbalNews] Kerbal News Network loaded");
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            collector.Unregister();
            if (blackBox != null) blackBox.Unregister();
            GameEvents.onGameSceneLoadRequested.Remove(OnSceneRequested);
            Instance = null;
        }

        void OnSceneRequested(GameScenes s)
        {
            if (s == GameScenes.MAINMENU || s == GameScenes.SETTINGS || s == GameScenes.CREDITS) HasGame = false;
        }

        /// <summary>Called by the scenario when a save (or quicksave, or revert) is loaded.</summary>
        public void Attach(NewsState state)
        {
            Engine.SetState(state);
            HasGame = true;
        }

        public void SaveSettings() { SettingsStore.Save(Settings); }

        /// <summary>Finds mods added since last time (all of them on the first run) and saves the list.</summary>
        string ScanMods(bool startup)
        {
            try
            {
                bool first = !Settings.ModsScanned;
                var added = ModCatalog.Refresh(Settings, System.IO.Path.Combine(KSPUtil.ApplicationRootPath, "GameData"), System.DateTime.Now.ToString("yyyy-MM-dd"));
                SaveSettings();
                string msg = first ? "Found " + Settings.Mods.Count + " mods. The AI now knows about " + Settings.Mods.Count(m => m.Include) + " of them." : ModCatalog.Describe(added);
                if (startup && (first || added.Count > 0)) Debug.Log("[KerbalNews] " + msg);
                return msg;
            }
            catch (System.Exception e) { Debug.LogWarning("[KerbalNews] Mod scan failed: " + e.Message); return "Scan failed: " + e.Message; }
        }

        static void SaveImage(string file, byte[] bytes)
        {
            try
            {
                string dir = System.IO.Path.Combine(KSPUtil.ApplicationRootPath, "GameData/KerbalNews/PluginData/Images");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, System.IO.Path.GetFileName(file)), bytes);
            }
            catch (Exception e) { Debug.LogError("[KerbalNews] Could not save a press photo: " + e.Message); }
        }

        static bool InGameScene
        {
            get
            {
                var s = HighLogic.LoadedScene;
                return s == GameScenes.FLIGHT || s == GameScenes.SPACECENTER || s == GameScenes.EDITOR || s == GameScenes.TRACKSTATION;
            }
        }

        void Update()
        {
            transport.Poll();
            if (!HasGame || !InGameScene || HighLogic.CurrentGame == null) return;
            if (Input.GetKeyDown(KeyCode.F8)) StartCoroutine(GalleryStore.Capture());
            if (HighLogic.LoadedSceneIsFlight && blackBox != null) blackBox.Update();
            try
            {
                // Four times a second is plenty for news; checking every frame only costs frame time.
                if (Time.realtimeSinceStartup < nextTick) return;
                nextTick = Time.realtimeSinceStartup + 0.25f;
                bool warping = TimeWarp.CurrentRate > 1.01f;
                Engine.Tick(Planetarium.GetUniversalTime(), Time.realtimeSinceStartup, Settings.GenerateDuringWarp || !warping);
                if (Time.realtimeSinceStartup > nextCrewSync) { nextCrewSync = Time.realtimeSinceStartup + 15; SyncRoster(); }
                // A few seconds of flight data for the engineering team, so a crash can be investigated afterwards.
                if (HighLogic.LoadedSceneIsFlight && Settings.EngineeringOn && FlightGlobals.ActiveVessel != null && Time.realtimeSinceStartup > nextEngRecord)
                { nextEngRecord = Time.realtimeSinceStartup + 4; Engine.EngRecord(EngineeringCapture.Snapshot(), Planetarium.GetUniversalTime()); }
            }
            catch (Exception e)
            {
                Debug.LogError("[KerbalNews] Tick failed: " + e);
                Engine.LastError = e.Message;
            }
        }

        float nextCrewSync, nextTick, nextEngRecord;

        /// <summary>Copies each kerbal's stats and whereabouts into the engine, for crew chat and profiles.</summary>
        void SyncRoster()
        {
            var roster = HighLogic.CurrentGame.CrewRoster;
            if (roster == null || roster.Crew == null) return;
            var aboard = new Dictionary<string, Vessel>();
            if (FlightGlobals.Vessels != null)
                foreach (var v in FlightGlobals.Vessels)
                {
                    if (v == null) continue;
                    var crew = v.GetVesselCrew();
                    if (crew != null) foreach (var c in crew) if (c != null && c.name != null) aboard[c.name] = v;
                }
            var frozen = ModCompat.FrozenKerbals();
            foreach (var k in roster.Crew)
            {
                if (k == null || string.IsNullOrEmpty(k.name)) continue;
                if (frozen.Contains(k.name)) { Engine.SyncCrew(new CrewProfile { Name = k.name, Trait = k.trait ?? "", Level = k.experienceLevel, Courage = k.courage, Stupidity = k.stupidity, Badass = k.isBadass, Veteran = k.veteran, Gender = k.gender == ProtoCrewMember.Gender.Female ? "female" : "male", Status = "frozen", Where = "frozen in a DeepFreeze pod" }); continue; }
                if (k.type == ProtoCrewMember.KerbalType.Applicant || k.type == ProtoCrewMember.KerbalType.Unowned) continue;
                var p = new CrewProfile
                {
                    Name = k.name, Trait = k.trait ?? "", Level = k.experienceLevel, Courage = k.courage, Stupidity = k.stupidity,
                    Badass = k.isBadass, Veteran = k.veteran, Gender = k.gender == ProtoCrewMember.Gender.Female ? "female" : "male",
                    Status = k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned ? "assigned" : k.rosterStatus == ProtoCrewMember.RosterStatus.Dead ? "dead"
                        : k.rosterStatus == ProtoCrewMember.RosterStatus.Missing ? "missing" : "available",
                };
                if (k.type == ProtoCrewMember.KerbalType.Tourist) p.Trait = "Tourist";
                Vessel ves;
                if (aboard.TryGetValue(k.name, out ves) && ves.mainBody != null)
                {
                    p.VesselName = Names.Of(ves);
                    p.Where = WhereText(ves);
                }
                Engine.SyncCrew(p);
            }
        }

        static string WhereText(Vessel v)
        {
            string b = v.mainBody.bodyName;
            switch (v.situation)
            {
                case Vessel.Situations.PRELAUNCH: return "on the launch pad";
                case Vessel.Situations.LANDED: return "landed on " + b;
                case Vessel.Situations.SPLASHED: return "splashed down on " + b;
                case Vessel.Situations.FLYING: return "flying over " + b;
                case Vessel.Situations.SUB_ORBITAL: return "on a suborbital arc above " + b;
                case Vessel.Situations.ORBITING: return "in orbit of " + b;
                case Vessel.Situations.ESCAPING: return "leaving " + b + "'s sphere of influence";
                case Vessel.Situations.DOCKED: return "docked in orbit of " + b;
                default: return "near " + b;
            }
        }

        /// <summary>The solar system as the press sees it (Kopernicus planets included), for the speculative "what would it take" pieces.</summary>
        static List<BodyInfo> GameBodies()
        {
            var list = new List<BodyInfo>();
            if (FlightGlobals.Bodies == null) return list;
            foreach (var b in FlightGlobals.Bodies)
            {
                if (b == null) continue;
                bool star = b.referenceBody == null || b.referenceBody == b || b.orbit == null;
                list.Add(new BodyInfo
                {
                    Name = b.bodyName, Parent = star ? "" : b.referenceBody.bodyName, Star = star,
                    RadiusKm = b.Radius / 1000, Gravity = b.GeeASL, Atmosphere = b.atmosphere, Ocean = b.ocean,
                    OrbitKm = star ? 0 : b.orbit.semiMajorAxis / 1000, PeriodDays = star ? 0 : b.orbit.period / KspClockDay(),
                });
            }
            return list;
        }

        static double KspClockDay() { return new KspClock().DaySeconds; }

        float lastMailPopup = -100;
        int mailHeld;

        /// <summary>The in-game pop-up for new email. A burst of mail shows one line for the rest.</summary>
        void OnMail(MailMsg m)
        {
            if (!Settings.NotifyOnPublish) return;
            if (Time.realtimeSinceStartup - lastMailPopup < 3f)
            {
                mailHeld++;
                if (mailHeld == 1) ScreenMessages.PostScreenMessage("<color=#8ab4ff>Kmail:</color> more new email in your inbox", 5f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }
            lastMailPopup = Time.realtimeSinceStartup;
            mailHeld = 0;
            string tail = m.Important && m.DueUT >= 0 && m.Open ? " (reply by " + Engine.Clock.FormatDate(m.DueUT) + ")" : "";
            ScreenMessages.PostScreenMessage("<color=#8ab4ff>Kmail:</color> " + m.From + ": " + m.Subject + tail, m.Important ? 8f : 5f, ScreenMessageStyle.UPPER_CENTER);
        }

        void OnPublished(List<Article> fresh)
        {
            if (!Settings.NotifyOnPublish) return;
            foreach (var a in fresh.OrderByDescending(a => a.Significance).Take(3))
                ScreenMessages.PostScreenMessage("<color=#ffd27f>" + Outlets.NameOf(a.Outlet) + ":</color> " + a.Headline, 7f, ScreenMessageStyle.UPPER_CENTER);
            if (fresh.Count > 3)
                ScreenMessages.PostScreenMessage("Kerbal News Network: " + (fresh.Count - 3) + " more new stories", 5f, ScreenMessageStyle.UPPER_CENTER);
        }
    }

    /// <summary>
    /// Stores all news inside persistent.sfs (and every quicksave), so loading, reverting or
    /// crashing rolls the public's knowledge back exactly like the rest of the game.
    /// </summary>
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.SPACECENTER, GameScenes.FLIGHT, GameScenes.EDITOR, GameScenes.TRACKSTATION)]
    public class NewsScenario : ScenarioModule
    {
        const string NodeName = "KERBALNEWS";
        bool loaded;

        public override void OnLoad(ConfigNode node)
        {
            NewsState state;
            try
            {
                state = NewsState.Load(ConfigBridge.ToData(node.GetNode(NodeName)));
            }
            catch (Exception e)
            {
                Debug.LogError("[KerbalNews] Could not read news from the save, starting fresh: " + e);
                state = new NewsState();
            }
            loaded = true;
            if (NewsSystem.Instance != null) NewsSystem.Instance.Attach(state);
        }

        void Start()
        {
            // A brand new save may never call OnLoad with our node; make sure we have a state.
            if (!loaded && NewsSystem.Instance != null) NewsSystem.Instance.Attach(new NewsState());
        }

        public override void OnSave(ConfigNode node)
        {
            if (NewsSystem.Instance == null || !NewsSystem.Instance.HasGame) return;
            try
            {
                var d = new DataNode(NodeName);
                NewsSystem.Instance.Engine.State.Save(d);
                node.AddNode(ConfigBridge.ToConfig(d));
            }
            catch (Exception e)
            {
                Debug.LogError("[KerbalNews] Could not save news: " + e);
            }
        }
    }
}
