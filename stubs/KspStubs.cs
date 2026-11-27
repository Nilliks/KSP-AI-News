// Compile-check stubs for the subset of Unity and KSP 1.12 APIs the mod uses.
// They let CI and the cloud build check the KSP layer without game DLLs.
// Signatures mirror KSP 1.12.5; the real build links against KSP_x64_Data/Managed instead.
#pragma warning disable 0067, 0649, 0169, 0414, 0626
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public static class Physics { public static bool Linecast(Vector3 a, Vector3 b, int mask) { return false; } public static RaycastHit[] RaycastAll(Vector3 o, Vector3 d, float len, int mask) { return new RaycastHit[0]; } }
    public struct RaycastHit { public Vector3 point; public float distance; public Collider collider; }
    public class Collider : Component { }
    public class Object { public string name; public static void Destroy(Object o) { } public static void DontDestroyOnLoad(Object o) { } public static T Instantiate<T>(T o) where T : Object { return o; } }
    public class Font : Object { public static Font CreateDynamicFontFromOSFont(string[] names, int size) { return null; } }
    public class Component : Object { public Transform transform; public GameObject gameObject; public T GetComponent<T>() { return default(T); } public T GetComponentInParent<T>() { return default(T); } }
    public sealed class GameObject : Object
    {
        public GameObject(string name, params Type[] components) { }
        public Transform transform;
        public T AddComponent<T>() where T : Component { return null; }
        public T GetComponent<T>() { return default(T); }
        public void SetActive(bool v) { }
        public bool activeSelf;
    }
    public class RectTransform : Transform
    {
        public Vector2 anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta, offsetMin, offsetMax;
        public Rect rect;
    }
    public static class RectTransformUtility { public static bool RectangleContainsScreenPoint(RectTransform rt, Vector2 p, Camera c) { return false; } public static bool ScreenPointToLocalPointInRectangle(RectTransform rt, Vector2 p, Camera c, out Vector2 local) { local = new Vector2(); return false; } }
    public class Camera : Behaviour { public static Camera[] allCameras; public float fieldOfView, depth; public RenderTexture targetTexture; public void Render() { } public void CopyFrom(Camera other) { } }
    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }
    public sealed class Canvas : Behaviour { public RenderMode renderMode; public int sortingOrder; }
    public static class Resources { public static T[] FindObjectsOfTypeAll<T>() where T : Object { return new T[0]; } }
    public static class ImageConversion { public static bool LoadImage(Texture2D t, byte[] data) { return true; } public static byte[] EncodeToJPG(Texture2D t, int quality) { return null; } }
    public class Behaviour : Component { public bool enabled; }
    public class AudioClip : Object { public static AudioClip Create(string name, int samples, int channels, int frequency, bool stream) { return new AudioClip(); } public bool SetData(float[] data, int offset) { return true; } }
    public class AudioSource : Behaviour { public AudioClip clip; public float volume, spatialBlend; public bool playOnAwake, loop, isPlaying; public void Play() { } public void Stop() { } }
    public class MonoBehaviour : Behaviour { public Coroutine StartCoroutine(System.Collections.IEnumerator e) { return null; } }
    public class Coroutine { }
    public class YieldInstruction { }
    public class WaitForSeconds : YieldInstruction { public WaitForSeconds(float s) { } }
    public class WaitForEndOfFrame : YieldInstruction { }
    public static class ScreenCapture { public static void CaptureScreenshot(string file) { } public static Texture2D CaptureScreenshotAsTexture() { return null; } }
    public enum KeyCode { F6, F7, F8, F9, Insert, ScrollLock, Pause, LeftShift, RightShift, PageUp, PageDown, UpArrow, DownArrow, Home, End, Space }
    public class RenderTexture : Texture { public RenderTexture(int w, int h, int d) { } public void Release() { } public bool Create() { return true; } public static RenderTexture active; public static RenderTexture GetTemporary(int w, int h) { return null; } public static void ReleaseTemporary(RenderTexture t) { } }
    public static class Graphics { public static void Blit(Texture src, RenderTexture dst) { } }
    public static class Application { public static void Quit() { } }
    public class Transform : Component { public Vector3 position, localScale; public Vector3 up, right, forward; public Quaternion rotation; public void SetParent(Transform p, bool worldPositionStays) { } }
    public struct Quaternion { public static Quaternion LookRotation(Vector3 f, Vector3 u) { return new Quaternion(); } public static Quaternion Euler(float x, float y, float z) { return new Quaternion(); } public static Quaternion operator *(Quaternion a, Quaternion b) { return a; } }
    public struct Bounds { public Vector3 size, max, min, center; }
    public struct Vector3 { public float magnitude { get { return 0; } } public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } public static Vector3 up; public static float Dot(Vector3 a, Vector3 b) { return 0; } }
    public struct Vector2
    {
        public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } public static Vector2 zero, one; public float sqrMagnitude { get { return 0; } }
        public static Vector2 operator +(Vector2 a, Vector2 b) { return a; } public static Vector2 operator -(Vector2 a, Vector2 b) { return a; }
        public static Vector2 operator /(Vector2 a, float d) { return a; } public static Vector2 operator *(Vector2 a, float d) { return a; }
        public static implicit operator Vector3(Vector2 v) { return new Vector3(v.x, v.y, 0); }
        public static implicit operator Vector2(Vector3 v) { return new Vector2(v.x, v.y); }
    }
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
        public float xMin { get { return x; } }
        public bool Contains(Vector2 p) { return false; }
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white, black, grey; public static Color Lerp(Color a, Color b, float t) { return a; }
    }
    public static class ColorUtility { public static bool TryParseHtmlString(string s, out Color c) { c = new Color(); return true; } public static string ToHtmlStringRGB(Color c) { return ""; } }
    public enum TextureFormat { ARGB32, RGB24, RGBA32 }
    public class Texture : Object { public int width, height; }
    public struct Color32 { public byte r, g, b, a; public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; } }
    public class Texture2D : Texture { public Texture2D(int w, int h) { } public Texture2D(int w, int h, TextureFormat f, bool mip) { } public void SetPixel(int x, int y, Color c) { } public void SetPixels32(Color32[] c) { } public Color32[] GetPixels32() { return new Color32[0]; } public void Apply() { } public void ReadPixels(Rect r, int x, int y) { } }
    public static class Debug { public static void Log(object o) { } public static void LogWarning(object o) { } public static void LogError(object o) { } }
    public static class Time { public static float realtimeSinceStartup, deltaTime, unscaledDeltaTime, timeScale; }
    public static class Mathf { public static float Clamp(float v, float a, float b) { return v; } public static int Clamp(int v, int a, int b) { return v; } public static int RoundToInt(float f) { return 0; } public static float Max(float a, float b) { return a; } public static float Min(float a, float b) { return a; } public static float Ceil(float f) { return f; } public static float Exp(float f) { return f; } public static float Lerp(float a, float b, float t) { return a; } public static float Clamp01(float v) { return v; } public static float Sqrt(float v) { return v; } public static float Abs(float v) { return v; } }
    public static class Input { public static Vector3 mousePosition; public static Vector2 mouseScrollDelta; public static bool GetKeyDown(KeyCode k) { return false; } public static bool GetKey(KeyCode k) { return false; } public static bool GetMouseButtonDown(int b) { return false; } public static bool GetMouseButton(int b) { return false; } }
    public static class Screen { public static int width, height; }
    public enum FontStyle { Normal, Bold, Italic }
    public class RectOffset { public RectOffset(int l, int r, int t, int b) { } }
    public class GUIStyleState { public Color textColor; public Texture2D background; }
    public class GUIStyle
    {
        public GUIStyle(GUIStyle other) { }
        public int fontSize; public FontStyle fontStyle; public bool wordWrap, richText; public RectOffset padding;
        public GUIStyleState normal = new GUIStyleState(), hover = new GUIStyleState(), active = new GUIStyleState(), focused = new GUIStyleState(), onNormal = new GUIStyleState(), onHover = new GUIStyleState();
        public RectOffset margin;
    }
    public class GUISkin : Object { public GUIStyle label, button, box, window, textField; }
    public struct Matrix4x4 { public static Matrix4x4 Scale(Vector3 v) { return new Matrix4x4(); } }
    public enum EventType { MouseDown, MouseUp, MouseDrag }
    public class Event { public static Event current; public EventType type; public Vector2 mousePosition; public void Use() { } }
    public class GUILayoutOption { }
    public static class GUI
    {
        public static GUISkin skin; public static bool enabled; public static Matrix4x4 matrix;
        public delegate void WindowFunction(int id);
        public static void DragWindow(Rect r) { }
        public static void Label(Rect r, string s, GUIStyle st) { }
    }
    public static class GUILayout
    {
        public static Rect Window(int id, Rect r, GUI.WindowFunction f, string title, params GUILayoutOption[] o) { return r; }
        public static Rect Window(int id, Rect r, GUI.WindowFunction f, string title, GUIStyle s, params GUILayoutOption[] o) { return r; }
        public static GUILayoutOption MaxWidth(float f) { return null; }
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
        public static string TextField(string s, GUIStyle st, params GUILayoutOption[] o) { return s; }
        public static string TextArea(string s, GUIStyle st, params GUILayoutOption[] o) { return s; }
        public static string PasswordField(string s, char mask, GUIStyle st, params GUILayoutOption[] o) { return s; }
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
    public class DownloadHandler : IDisposable { public string text; public byte[] data; public void Dispose() { } }
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
[AttributeUsage(AttributeTargets.Assembly)] public class KSPAssembly : Attribute { public KSPAssembly(string name, int major, int minor) { } }
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
    public void AddValue(string k, string v) { } public void AddValue(string k, object v) { } public string GetValue(string k) { return null; }
    public ConfigNode AddNode(string name) { return null; }
    public ConfigNode AddNode(ConfigNode n) { return n; }
    public ConfigNode GetNode(string name) { return null; } public ConfigNode[] GetNodes(string name) { return new ConfigNode[0]; }
    public bool Save(string path) { return true; }
    public static ConfigNode Load(string path) { return null; }
}

public interface IDateTimeFormatter { int Day { get; } int Year { get; } }
public static class KSPUtil { public static IDateTimeFormatter dateTimeFormatter; public static string ApplicationRootPath; }

public class EventReport { public string sender, other; public Part origin; public string msg; }

public class EventVoid { public void Add(Callback c) { } public void Remove(Callback c) { } }
public class EventData<T> { public delegate void OnEvent(T data); public void Add(OnEvent e) { } public void Remove(OnEvent e) { } public void Fire(T d) { } }
public class EventData<T, U> { public delegate void OnEvent(T a, U b); public void Add(OnEvent e) { } public void Remove(OnEvent e) { } }
public class EventData<T, U, V, W> { public delegate void OnEvent(T a, U b, V c, W d); public void Add(OnEvent e) { } public void Remove(OnEvent e) { } }
public class ScienceSubject { public string id, title; public float science, scienceCap, dataScale, subjectValue; }
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
    public static EventData<Part> onPartDie;
    public static EventData<int> onStageActivate;
    public static EventData<EventReport> onCrash, onCrashSplashdown, onOverheat;
    public static EventData<PartJoint, float> onPartJointBreak;
    public static EventData<ProtoVessel, bool> onVesselRecovered;
    public static EventData<ProtoVessel, KSP.UI.Screens.MissionRecoveryDialog, float> onVesselRecoveryProcessing;
    public static EventData<ProtoVessel> onVesselTerminated;
    public static EventData<ProgressNode> OnProgressComplete;
    public static EventData<float, ScienceSubject, ProtoVessel, bool> OnScienceRecieved;
    public static EventData<GameScenes> onGameSceneLoadRequested, onLevelWasLoadedGUIReady, onGUIApplicationLauncherUnreadifying;
    public static EventVoid onGUIApplicationLauncherReady;
    public static class Contract { public static EventData<Contracts.Contract> onCompleted, onFailed, onAccepted, onDeclined, onCancelled, onOffered; }
}

namespace Contracts
{
    public class Contract
    {
        public enum ContractPrestige { Trivial, Significant, Exceptional }
        public enum State { Generated, Offered, OfferExpired, Declined, Cancelled, Active, Completed, DeadlineExpired, Failed, Withdrawn }
        public string Title; public Guid ContractGuid; public Agents.Agent Agent; public ContractPrestige Prestige; public State ContractState; public double FundsAdvance, FundsCompletion; public float ReputationCompletion; public int ParameterCount; public double DateExpire; public void Decline() { } public void Offer() { }
        public static Contract Generate(Type t, ContractPrestige p, int seed, State s) { return null; }
        protected virtual bool Generate() { return false; }
        public virtual bool CanBeCancelled() { return true; } public virtual bool CanBeDeclined() { return true; } public virtual bool MeetRequirements() { return true; }
        protected virtual string GetHashString() { return ""; } protected virtual string GetTitle() { return ""; } protected virtual string GetDescription() { return ""; }
        protected virtual string GetSynopsys() { return ""; } protected virtual string MessageCompleted() { return ""; }
        protected virtual void OnSave(ConfigNode n) { } protected virtual void OnLoad(ConfigNode n) { }
        protected void SetFunds(float a, float c, float f, CelestialBody b = null) { } protected void SetReputation(float c, float f, CelestialBody b = null) { } protected void SetScience(float c, CelestialBody b = null) { }
        protected void SetExpiry(float min, float max) { } protected void SetDeadlineDays(float d, CelestialBody b = null) { }
        public ContractParameter AddParameter(ContractParameter p, string id = null) { return p; }
    }
    public class ContractParameter
    {
        protected virtual string GetTitle() { return ""; } protected virtual string GetHashString() { return ""; }
        protected virtual void OnUpdate() { } protected virtual void OnRegister() { } protected virtual void OnUnregister() { }
        protected virtual void OnSave(ConfigNode n) { } protected virtual void OnLoad(ConfigNode n) { }
        protected void SetComplete() { } protected void SetFailed() { }
    }
    public class ContractSystem { public static ContractSystem Instance; public List<Contract> Contracts; }
    namespace Agents { public class Agent { public string Name; } }
}
public static class AssemblyLoader { public class LoadedAssembly { public System.Reflection.Assembly assembly; } public static List<LoadedAssembly> loadedAssemblies; }

public class ProgressNode { public string Id; }
public struct Vector3d
{
    public double x, y, z;
    public Vector3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
    public static Vector3d zero;
    public static double Angle(Vector3d a, Vector3d b) { return 0; }
    public Vector3d normalized { get { return this; } }
    public double magnitude { get { return 0; } }
    public static Vector3d Exclude(Vector3d a, Vector3d b) { return b; }
    public static Vector3d operator +(Vector3d a, Vector3d b) { return a; } public static Vector3d operator -(Vector3d a, Vector3d b) { return a; }
    public static Vector3d operator *(Vector3d a, double d) { return a; } public static Vector3d operator /(Vector3d a, double d) { return a; }
    public static Vector3d Cross(Vector3d a, Vector3d b) { return a; } public static double Dot(Vector3d a, Vector3d b) { return 0; }
    public static implicit operator UnityEngine.Vector3(Vector3d v) { return new UnityEngine.Vector3(); }
    public static implicit operator Vector3d(UnityEngine.Vector3 v) { return new Vector3d(); }
}
public static class ScaledSpace { public static Vector3d LocalToScaledSpace(Vector3d v) { return v; } }
public static class MapView { public static bool MapIsEnabled; }
public static class FlightDriver { public static bool Pause; public static void SetPause(bool p) { } public static void StartWithNewLaunch(string path, string flag, string site, VesselCrewManifest m) { } }
public class CelestialBody : UnityEngine.MonoBehaviour, ITargetable
{
    public string GetName() { return bodyName; } public Orbit GetOrbit() { return orbit; }
    public string bodyName; public Vector3d position; public double Radius; public bool ocean; public bool isHomeWorld;
    public double gravParameter, GeeASL, atmosphereDepth, sphereOfInfluence; public bool atmosphere; public Orbit orbit; public CelestialBody referenceBody;
    public double GetLatitude(Vector3d p) { return 0; } public double GetLongitude(Vector3d p) { return 0; } public double GetAltitude(Vector3d p) { return 0; }
    public double TerrainAltitude(double lat, double lon, bool allowNegative = false) { return 0; }
    public Vector3d GetWorldSurfacePosition(double lat, double lon, double alt) { return new Vector3d(); }
}
public class Planetarium { public static Planetarium fetch; public CelestialBody Sun; public static double GetUniversalTime() { return 0; } }

public enum VesselType { Debris, SpaceObject, Unknown, Probe, Relay, Rover, Lander, Ship, Plane, Station, Base, EVA, Flag, DeployedScienceController, DeployedSciencePart, DroppedPart, DeployedGroundPart }
public enum AttachModes { STACK, SRF_ATTACH }

public class VesselDeltaV { public double TotalDeltaVVac, TotalDeltaVASL, TotalDeltaVActual; public bool IsReady; public List<DeltaVStageInfo> OperatingStageInfo; }
public class DeltaVStageInfo { public int stage; public double deltaVinVac, deltaVatASL, deltaVActual, TWRVac, TWRASL, TWRActual, stageBurnTime; public float startMass, endMass, thrustVac, ispVac; }
public class ShipConstruct { public string shipName; public List<Part> parts; public VesselDeltaV vesselDeltaV; }
public class EditorLogic : UnityEngine.MonoBehaviour { public static EditorLogic fetch; public ShipConstruct ship; }
public enum EditorFacility { None, VAB, SPH }
public class EditorDriver : UnityEngine.MonoBehaviour { public static EditorFacility editorFacility; }
public interface ITargetable { string GetName(); Orbit GetOrbit(); }
public class ProtoCrewMember { public enum RosterStatus { Available, Assigned, Dead, Missing } public enum Gender { Male, Female } public enum KerbalType { Crew, Applicant, Unowned, Tourist } public string name, trait; public RosterStatus rosterStatus; public KerbalType type; public Gender gender; public int experienceLevel; public float courage, stupidity; public bool isBadass, veteran; }

public class Vessel : UnityEngine.MonoBehaviour
{
    public enum Situations { LANDED = 1, SPLASHED = 2, PRELAUNCH = 4, FLYING = 8, SUB_ORBITAL = 16, ORBITING = 32, ESCAPING = 64, DOCKED = 128 }
    public string vesselName, landedAt;
    public FlightCtrlState ctrlState; public bool packed;
    public FlightInputCallback OnFlyByWire;
    public System.Collections.Generic.List<T> FindPartModulesImplementing<T>() where T : class { return new System.Collections.Generic.List<T>(); }
    public VesselType vesselType;
    public Situations situation;
    public CelestialBody mainBody;
    public List<Part> parts;
    public Guid id;
    public uint persistentId;
    public double latitude, longitude, altitude, horizontalSrfSpeed, srfSpeed, obt_speed, geeForce;
    public int currentStage;
    public bool loaded;
    public Orbit orbit; public double launchTime, missionTime; public ProtoVessel protoVessel; public DiscoveryInfo DiscoveryInfo;
    public Part rootPart;
    public VesselDeltaV VesselDeltaV;
    public PatchedConicSolver patchedConicSolver; public double verticalSpeed, radarAltitude, dynamicPressurekPa, mach; public Vector3d upAxis, srf_velocity; public UnityEngine.Vector3 angularVelocity; public UnityEngine.Transform ReferenceTransform; public ActionGroupList ActionGroups; public VesselAutopilot Autopilot;
    public float GetTotalMass() { return 0; }
    public Vector3d GetWorldPos3D() { return new Vector3d(); }
    public List<ProtoCrewMember> GetVesselCrew() { return null; }
    public void Die() { } public int GetCrewCount() { return 0; } public int GetCrewCapacity() { return 0; }
}

public class PartModule : UnityEngine.MonoBehaviour { public Part part; public string moduleName; }
public class ModuleCommand : PartModule { }
public class ModuleEngines : PartModule { public bool EngineIgnited, flameout; public float currentThrottle, finalThrust; public string status; }
public class ModuleLiftingSurface : PartModule { }
public class AvailablePart { public string title, name, manufacturer, TechRequired; }
public static class PartLoader { public static List<AvailablePart> LoadedPartsList; }
public class RDTech { public enum State { Unavailable, Available } }
public enum DiscoveryLevels { None = 0, Presence = 1, Name = 4, StateVectors = 8, Appearance = 16, Owned = 29 }
public enum UntrackedObjectClass { A, B, C, D, E, I }
public class DiscoveryInfo { public UntrackedObjectClass objectSize; public bool HaveKnowledgeAbout(DiscoveryLevels l) { return false; } }
public class ProtoPartSnapshot { public string partName; public AvailablePart partInfo; }
public class PartJoint { public Part Host, Child, Parent; }
public class ManeuverNode { public Vector3d DeltaV; public double UT; public Orbit nextPatch; public void RemoveSelf() { } }
public class PatchedConicSolver { public List<ManeuverNode> maneuverNodes; public ManeuverNode AddManeuverNode(double ut) { return null; } public void UpdateFlightPlan() { } }
public class PartModuleList : IEnumerable<PartModule> { public IEnumerator<PartModule> GetEnumerator() { yield break; } System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return GetEnumerator(); } }
public class Orbit { public enum PatchTransitionType { INITIAL, FINAL, ENCOUNTER, ESCAPE, MANEUVER, IMPACT } public PatchTransitionType patchEndTransition; public Orbit nextPatch; public double ApA, PeA, inclination, eccentricity, period, semiMajorAxis; public CelestialBody referenceBody; public Vector3d getRelativePositionAtUT(double ut) { return new Vector3d(); } public Vector3d getPositionAtUT(double ut) { return new Vector3d(); } public Vector3d GetOrbitNormal() { return new Vector3d(); } public Vector3d getOrbitalVelocityAtUT(double ut) { return new Vector3d(); } }
public class PartResource { public string resourceName; public double amount, maxAmount; }
public class PartResourceList : IEnumerable<PartResource> { public IEnumerator<PartResource> GetEnumerator() { yield break; } System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return GetEnumerator(); } }
public class Part : UnityEngine.MonoBehaviour
{
    public PartResourceList Resources;
    public float mass; public float GetResourceMass() { return 0; } public PartModuleList Modules;
    public Vessel vessel; public int inverseStage; public int CrewCapacity; public AvailablePart partInfo; public AttachModes attachMode;
    public T FindModuleImplementing<T>() where T : class { return null; }
}

public class OrbitSnapshot { public int ReferenceBodyIndex; }
public class ProtoVessel
{
    public VesselType vesselType; public Vessel.Situations situation; public OrbitSnapshot orbitSnapShot;
    public Guid vesselID; public uint persistentId; public string vesselName;
    public List<ProtoCrewMember> GetVesselCrew() { return null; } public List<ProtoPartSnapshot> protoPartSnapshots;
}

public class FlightGlobals { public static FlightGlobals fetch; public ITargetable VesselTarget; public void SetVesselTarget(ITargetable t, bool o = false) { } public static List<Vessel> Vessels; public static List<CelestialBody> Bodies; public static Vessel ActiveVessel; public static bool ready; public static CelestialBody GetHomeBody() { return null; } }
public class FlightState { public List<ProtoVessel> protoVessels; }
public class KerbalRoster { public IEnumerable<ProtoCrewMember> Crew; public ProtoCrewMember this[string name] { get { return null; } } public static KerbalRoster GenerateInitialCrewRoster(Game.Modes m) { return null; } }
public class Game { public enum Modes { SANDBOX, CAREER, SCIENCE_SANDBOX } public FlightState flightState; public KerbalRoster CrewRoster; public Modes Mode; public string Title, flagURL; public GameParameters Parameters; public GameScenes startScene; public System.Collections.Generic.List<object> scenarios; public void Start() { } public Game Updated() { return this; } }
public class GameParameters { }
public enum SaveMode { OVERWRITE, APPEND, ABORT }
public static class GamePersistence { public static string SaveGame(Game g, string file, string folder, SaveMode m) { return ""; } }
public class VesselCrewManifest { public static VesselCrewManifest FromConfigNode(ConfigNode n) { return null; } }
public class FlightCtrlState { public float mainThrottle, pitch, yaw, roll; }
public delegate void FlightInputCallback(FlightCtrlState st);
public static class FlightInputHandler { public static FlightCtrlState state; }
public static class HighLogic
{
    public static void LoadScene(GameScenes s) { } public static GameScenes LoadedScene; public static bool LoadedSceneIsFlight; public static string SaveFolder; public static Game CurrentGame; public static UnityEngine.GUISkin Skin;
}
public class Funding { public static Funding Instance; public double Funds; public void AddFunds(double v, TransactionReasons r) { } }
public enum TransactionReasons { None, Any }
public class Reputation { public static Reputation Instance; public float reputation; }
public class ResearchAndDevelopment { public static ResearchAndDevelopment Instance; public float Science; public static string GetResults(string subjectId) { return ""; } public static RDTech.State GetTechnologyState(string id) { return RDTech.State.Unavailable; } }

public enum ScreenMessageStyle { UPPER_LEFT, UPPER_CENTER, UPPER_RIGHT, LOWER_CENTER }
public static class ScreenMessages { public static object PostScreenMessage(string m, float d, ScreenMessageStyle s) { return null; } }
public static class GameSettings { public static float UI_SCALE = 1; }
public static class TimeWarp { public static float CurrentRate; public static void SetRate(int i, bool instant, bool msg = true) { } }

[Flags]
public enum ControlTypes : ulong { None = 0, CAMERACONTROLS = 1, KSC_ALL = 2, EDITOR_SOFT_LOCK = 4, TRACKINGSTATION_ALL = 8, KEYBOARDINPUT = 16, ALL_SHIP_CONTROLS = 32, STAGING = 64, TIMEWARP = 128, MAP = 256, QUICKSAVE = 512, QUICKLOAD = 1024, PAUSE = 2048, MISC = 4096 }
public static class InputLockManager { public static void SetControlLock(ControlTypes t, string id) { } public static void RemoveControlLock(string id) { } }

public class GameDatabase { public static GameDatabase Instance; public UnityEngine.Texture2D GetTexture(string url, bool normal) { return null; } }

namespace KSP.UI.Screens
{
    public class MissionRecoveryDialog : UnityEngine.MonoBehaviour { }
    public static class StageManager { public static void ActivateNextStage() { } }
    public class ApplicationLauncherButton : UnityEngine.MonoBehaviour { public void SetTrue(bool makeCall) { } public void SetFalse(bool makeCall) { } }
    public class ApplicationLauncher : UnityEngine.MonoBehaviour
    {
        [Flags] public enum AppScenes { NEVER = 0, SPACECENTER = 1, FLIGHT = 2, MAPVIEW = 4, VAB = 8, SPH = 16, TRACKSTATION = 32, ALWAYS = 63 }
        public static ApplicationLauncher Instance; public static bool Ready;
        public ApplicationLauncherButton AddModApplication(Callback onTrue, Callback onFalse, Callback onHover, Callback onHoverOut, Callback onEnable, Callback onDisable, AppScenes scenes, UnityEngine.Texture texture) { return null; }
        public void RemoveModApplication(ApplicationLauncherButton b) { }
    }
}

namespace KSP.Localization
{
    public static class Localizer { public static string Format(string s, params object[] args) { return s; } }
}

public enum KSPActionGroup { None, SAS, RCS }
public class ActionGroupList { public bool this[KSPActionGroup g] { get { return false; } } }
public class VesselAutopilot { public enum AutopilotMode { StabilityAssist, Prograde } public AutopilotMode Mode; }
