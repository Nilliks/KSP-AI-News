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

        void Awake()
        {
            if (Instance != null) { Destroy(this); return; }
            Instance = this;
            DontDestroyOnLoad(this);
            Settings = SettingsStore.Load();
            Engine = new NewsEngine(new NewsState(), Settings, transport, clock);
            Engine.Digest = () => Snapshots.Digest(Engine.State);
            Engine.ArticlesPublished += OnPublished;
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
            Sites.Digest = () => HasGame ? Snapshots.Digest(Engine.State) : null;
            Sites.UiScale = () => Browser.Scale;
            Sites.SaveSettings = SaveSettings;
            Sites.ImportScreenshots = GalleryStore.ImportStock;
            Sites.ActiveVesselId = () => HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.persistentId.ToString() : null;
            Sites.ActiveVesselName = () => HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.vesselName : null;
            collector = new EventCollector(() => HasGame ? Engine : null);
            collector.Register();
            GameEvents.onGameSceneLoadRequested.Add(OnSceneRequested);
            Debug.Log("[KerbalNews] Kerbal News Network loaded");
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            collector.Unregister();
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
            try
            {
                bool warping = TimeWarp.CurrentRate > 1.01f;
                Engine.Tick(Planetarium.GetUniversalTime(), Time.realtimeSinceStartup, Settings.GenerateDuringWarp || !warping);
            }
            catch (Exception e)
            {
                Debug.LogError("[KerbalNews] Tick failed: " + e);
                Engine.LastError = e.Message;
            }
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
