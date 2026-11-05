// Compile-check stubs for the subset of Unity and KSP 1.12 APIs the mod uses.
// They let CI and the cloud build check the KSP layer without game DLLs.
// Signatures mirror KSP 1.12.5; the real build links against KSP_x64_Data/Managed instead.
#pragma warning disable 0067, 0649, 0169, 0414, 0626
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object { public string name; public static void Destroy(Object o) { } public static void DontDestroyOnLoad(Object o) { } }
    public class Component : Object { public Transform transform; }
    public class Behaviour : Component { public bool enabled; }
    public class MonoBehaviour : Behaviour { }
    public class Transform : Component { public Vector3 position; public Vector3 up; }
    public struct Vector3 { public float x, y, z; public static Vector3 up; public static float Dot(Vector3 a, Vector3 b) { return 0; } }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } public static Vector2 zero; }
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
        public bool Contains(Vector2 p) { return false; }
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white; public static Color Lerp(Color a, Color b, float t) { return a; }
    }
    public static class ColorUtility { public static bool TryParseHtmlString(string s, out Color c) { c = new Color(); return true; } public static string ToHtmlStringRGB(Color c) { return ""; } }
    public enum TextureFormat { ARGB32 }
    public class Texture : Object { }
    public class Texture2D : Texture { public Texture2D(int w, int h, TextureFormat f, bool mip) { } public void SetPixel(int x, int y, Color c) { } public void Apply() { } }
    public static class Debug { public static void Log(object o) { } public static void LogWarning(object o) { } public static void LogError(object o) { } }
    public static class Time { public static float realtimeSinceStartup; }
    public static class Mathf { public static float Clamp(float v, float a, float b) { return v; } public static int Clamp(int v, int a, int b) { return v; } public static int RoundToInt(float f) { return 0; } }
    public static class Input { public static Vector3 mousePosition; }
    public static class Screen { public static int width, height; }
    public enum FontStyle { Normal, Bold, Italic }
    public class RectOffset { public RectOffset(int l, int r, int t, int b) { } }
    public class GUIStyleState { public Color textColor; }
    public class GUIStyle
    {
        public GUIStyle(GUIStyle other) { }
        public int fontSize; public FontStyle fontStyle; public bool wordWrap, richText; public RectOffset padding;
        public GUIStyleState normal = new GUIStyleState(), hover = new GUIStyleState();
    }
    public class GUISkin : Object { public GUIStyle label, button, box; }
    public enum EventType { MouseDown, MouseUp, MouseDrag }
    public class Event { public static Event current; public EventType type; public Vector2 mousePosition; public void Use() { } }
    public class GUILayoutOption { }
    public static class GUI
    {
        public static GUISkin skin; public static bool enabled;
        public delegate void WindowFunction(int id);
        public static void DragWindow(Rect r) { }
        public static void Label(Rect r, string s, GUIStyle st) { }
    }
    public static class GUILayout
    {
        public static Rect Window(int id, Rect r, GUI.WindowFunction f, string title, params GUILayoutOption[] o) { return r; }
        public static void BeginHorizontal(params GUILayoutOption[] o) { }
        public static void BeginHorizontal(GUIStyle s, params GUILayoutOption[] o) { }
        public static void EndHorizontal() { }
        public static void BeginVertical(params GUILayoutOption[] o) { }
        public static void BeginVertical(GUIStyle s, params GUILayoutOption[] o) { }
        public static void EndVertical() { }
        public static Vector2 BeginScrollView(Vector2 v, params GUILayoutOption[] o) { return v; }
        public static void EndScrollView() { }
        public static void Label(string s, params GUILayoutOption[] o) { }
        public static void Label(string s, GUIStyle st, params GUILayoutOption[] o) { }
        public static bool Button(string s, params GUILayoutOption[] o) { return false; }
        public static bool Button(string s, GUIStyle st, params GUILayoutOption[] o) { return false; }
        public static bool Toggle(bool v, string s, params GUILayoutOption[] o) { return v; }
        public static bool Toggle(bool v, string s, GUIStyle st, params GUILayoutOption[] o) { return v; }
        public static string TextField(string s, params GUILayoutOption[] o) { return s; }
        public static string TextArea(string s, params GUILayoutOption[] o) { return s; }
        public static string PasswordField(string s, char mask, params GUILayoutOption[] o) { return s; }
        public static float HorizontalSlider(float v, float a, float b, params GUILayoutOption[] o) { return v; }
        public static void Space(float f) { }
        public static void FlexibleSpace() { }
        public static GUILayoutOption Width(float f) { return null; }
        public static GUILayoutOption MinWidth(float f) { return null; }
        public static GUILayoutOption MinHeight(float f) { return null; }
    }
}

namespace UnityEngine.Networking
{
    public class UploadHandler : IDisposable { public void Dispose() { } }
    public class UploadHandlerRaw : UploadHandler { public UploadHandlerRaw(byte[] b) { } }
    public class DownloadHandler : IDisposable { public string text; public void Dispose() { } }
    public class DownloadHandlerBuffer : DownloadHandler { }
    public class UnityWebRequestAsyncOperation { }
    public class UnityWebRequest : IDisposable
    {
        public UnityWebRequest(string url, string method) { }
        public UploadHandler uploadHandler; public DownloadHandler downloadHandler;
        public int timeout; public bool isDone, isNetworkError, isHttpError; public long responseCode; public string error;
        public void SetRequestHeader(string k, string v) { }
        public UnityWebRequestAsyncOperation SendWebRequest() { return null; }
        public void Abort() { }
        public void Dispose() { }
    }
}

public delegate void Callback();

public enum GameScenes { LOADING, LOADINGBUFFER, MAINMENU, SETTINGS, CREDITS, SPACECENTER, EDITOR, FLIGHT, TRACKSTATION, PSYSTEM, MISSIONBUILDER }
public enum ScenarioCreationOptions { None, AddToAllGames }

[AttributeUsage(AttributeTargets.Class)]
public class KSPAddon : Attribute
{
    public enum Startup { Instantly = -2, EveryScene = -1, MainMenu = 2, Settings = 3, Credits = 4, SpaceCentre = 5, EditorAny = 6, Flight = 7, TrackingStation = 8, PSystemSpawn = 9, AllGameScenes = 10, FlightEditorAndKSC = 11, FlightAndKSC = 12, FlightAndEditor = 13 }
    public KSPAddon(Startup startup, bool once) { }
}

[AttributeUsage(AttributeTargets.Class)]
public class KSPScenario : Attribute { public KSPScenario(ScenarioCreationOptions o, params GameScenes[] scenes) { } }

public class ScenarioModule : UnityEngine.MonoBehaviour
{
    public virtual void OnLoad(ConfigNode node) { }
    public virtual void OnSave(ConfigNode node) { }
}

public class ConfigNode
{
    public class Value { public string name, value; }
    public class ValueList : IEnumerable<Value> { public IEnumerator<Value> GetEnumerator() { yield break; } System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { yield break; } }
    public class ConfigNodeList : IEnumerable<ConfigNode> { public IEnumerator<ConfigNode> GetEnumerator() { yield break; } System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { yield break; } }
    public string name;
    public ValueList values;
    public ConfigNodeList nodes;
    public ConfigNode() { }
    public ConfigNode(string name) { }
    public void AddValue(string k, string v) { }
    public ConfigNode AddNode(string name) { return null; }
    public ConfigNode AddNode(ConfigNode n) { return n; }
    public ConfigNode GetNode(string name) { return null; }
    public bool Save(string path) { return true; }
    public static ConfigNode Load(string path) { return null; }
}

public interface IDateTimeFormatter { int Day { get; } int Year { get; } }
public static class KSPUtil { public static IDateTimeFormatter dateTimeFormatter; public static string ApplicationRootPath; }

public class EventReport { public string sender; public Part origin; public string msg; }

public class EventVoid { public void Add(Callback c) { } public void Remove(Callback c) { } }
public class EventData<T> { public delegate void OnEvent(T data); public void Add(OnEvent e) { } public void Remove(OnEvent e) { } }
public class EventData<T, U> { public delegate void OnEvent(T a, U b); public void Add(OnEvent e) { } public void Remove(OnEvent e) { } }
public class EventData<T, U, V> { public delegate void OnEvent(T a, U b, V c); public void Add(OnEvent e) { } public void Remove(OnEvent e) { } }

public static class GameEvents
{
    public struct HostedFromToAction<A, B> { public A host; public B from; public B to; }
    public struct FromToAction<A, B> { public A from; public B to; }
    public static EventData<EventReport> onLaunch, onCrewKilled;
    public static EventData<HostedFromToAction<Vessel, Vessel.Situations>> onVesselSituationChange;
    public static EventData<HostedFromToAction<Vessel, CelestialBody>> onVesselSOIChanged;
    public static EventData<FromToAction<Part, Part>> onPartCouple;
    public static EventData<Vessel> onVesselWillDestroy;
    public static EventData<ProtoVessel, bool> onVesselRecovered;
    public static EventData<ProtoVessel, KSP.UI.Screens.MissionRecoveryDialog, float> onVesselRecoveryProcessing;
    public static EventData<ProtoVessel> onVesselTerminated;
    public static EventData<ProgressNode> OnProgressComplete;
    public static EventData<GameScenes> onGameSceneLoadRequested, onLevelWasLoadedGUIReady, onGUIApplicationLauncherUnreadifying;
    public static EventVoid onGUIApplicationLauncherReady;
    public static class Contract { public static EventData<Contracts.Contract> onCompleted, onFailed, onAccepted; }
}

namespace Contracts { public class Contract { public string Title; } }

public class ProgressNode { public string Id; }
public class CelestialBody : UnityEngine.MonoBehaviour { public string bodyName; }
public class Planetarium { public static Planetarium fetch; public CelestialBody Sun; public static double GetUniversalTime() { return 0; } }

public enum VesselType { Debris, SpaceObject, Unknown, Probe, Relay, Rover, Lander, Ship, Plane, Station, Base, EVA, Flag, DeployedScienceController, DeployedSciencePart, DroppedPart, DeployedGroundPart }
public enum AttachModes { STACK, SRF_ATTACH }

public class VesselDeltaV { public double TotalDeltaVVac; }
public class ProtoCrewMember { public enum RosterStatus { Available, Assigned, Dead, Missing } public string name; public RosterStatus rosterStatus; }

public class Vessel : UnityEngine.MonoBehaviour
{
    public enum Situations { LANDED = 1, SPLASHED = 2, PRELAUNCH = 4, FLYING = 8, SUB_ORBITAL = 16, ORBITING = 32, ESCAPING = 64, DOCKED = 128 }
    public string vesselName, landedAt;
    public VesselType vesselType;
    public Situations situation;
    public CelestialBody mainBody;
    public List<Part> parts;
    public Guid id;
    public uint persistentId;
    public double altitude, horizontalSrfSpeed;
    public Part rootPart;
    public VesselDeltaV VesselDeltaV;
    public float GetTotalMass() { return 0; }
    public List<ProtoCrewMember> GetVesselCrew() { return null; }
}

public class PartModule : UnityEngine.MonoBehaviour { }
public class ModuleCommand : PartModule { }
public class ModuleEngines : PartModule { }
public class ModuleLiftingSurface : PartModule { }
public class AvailablePart { public string title; }
public class Part : UnityEngine.MonoBehaviour
{
    public Vessel vessel; public int inverseStage; public int CrewCapacity; public AvailablePart partInfo; public AttachModes attachMode;
    public T FindModuleImplementing<T>() where T : class { return null; }
}

public class OrbitSnapshot { public int ReferenceBodyIndex; }
public class ProtoVessel
{
    public VesselType vesselType; public Vessel.Situations situation; public OrbitSnapshot orbitSnapShot;
    public Guid vesselID; public uint persistentId; public string vesselName;
    public List<ProtoCrewMember> GetVesselCrew() { return null; }
}

public static class FlightGlobals { public static List<Vessel> Vessels; public static List<CelestialBody> Bodies; public static Vessel ActiveVessel; }
public class FlightState { public List<ProtoVessel> protoVessels; }
public class KerbalRoster { public IEnumerable<ProtoCrewMember> Crew; }
public class Game { public enum Modes { SANDBOX, CAREER, SCIENCE_SANDBOX } public FlightState flightState; public KerbalRoster CrewRoster; public Modes Mode; }
public static class HighLogic
{
    public static GameScenes LoadedScene; public static bool LoadedSceneIsFlight; public static Game CurrentGame; public static UnityEngine.GUISkin Skin;
}
public class Funding { public static Funding Instance; public double Funds; }
public class Reputation { public static Reputation Instance; public float reputation; }
public class ResearchAndDevelopment { public static ResearchAndDevelopment Instance; public float Science; }

public enum ScreenMessageStyle { UPPER_LEFT, UPPER_CENTER, UPPER_RIGHT, LOWER_CENTER }
public static class ScreenMessages { public static object PostScreenMessage(string m, float d, ScreenMessageStyle s) { return null; } }
public static class TimeWarp { public static float CurrentRate; }

[Flags]
public enum ControlTypes : ulong { None = 0, CAMERACONTROLS = 1, KSC_ALL = 2, EDITOR_SOFT_LOCK = 4, TRACKINGSTATION_ALL = 8 }
public static class InputLockManager { public static void SetControlLock(ControlTypes t, string id) { } public static void RemoveControlLock(string id) { } }

public class GameDatabase { public static GameDatabase Instance; public UnityEngine.Texture2D GetTexture(string url, bool normal) { return null; } }

namespace KSP.UI.Screens
{
    public class MissionRecoveryDialog : UnityEngine.MonoBehaviour { }
    public class ApplicationLauncherButton : UnityEngine.MonoBehaviour { public void SetTrue(bool makeCall) { } public void SetFalse(bool makeCall) { } }
    public class ApplicationLauncher : UnityEngine.MonoBehaviour
    {
        [Flags] public enum AppScenes { NEVER = 0, SPACECENTER = 1, FLIGHT = 2, MAPVIEW = 4, VAB = 8, SPH = 16, TRACKSTATION = 32, ALWAYS = 63 }
        public static ApplicationLauncher Instance; public static bool Ready;
        public ApplicationLauncherButton AddModApplication(Callback onTrue, Callback onFalse, Callback onHover, Callback onHoverOut, Callback onEnable, Callback onDisable, AppScenes scenes, UnityEngine.Texture texture) { return null; }
        public void RemoveModApplication(ApplicationLauncherButton b) { }
    }
}
