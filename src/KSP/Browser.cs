using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KerbalNews.Web;
using KSP.UI.Screens;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KerbalNews.KSP
{
    /// <summary>
    /// The in-game web browser. Pages come from Sites (HTML + CSS files under GameData/KerbalNews/Sites),
    /// are laid out by the core Layout engine, and are drawn here with uGUI and TextMeshPro, so text stays
    /// sharp at any resolution. Opened from the toolbar in every game scene.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    public class Browser : MonoBehaviour
    {
        // Remembered across scenes.
        static bool visible;
        static Vector2 winPos = new Vector2(120, 70);    // canvas units, from the top-left
        static Vector2 winSize = new Vector2(1000, 680);
        static string url = Sites.Home;
        static readonly List<string> back = new List<string>(), forward = new List<string>();
        static readonly Dictionary<string, float> scrollMemory = new Dictionary<string, float>();

        const float BarH = 40, StatusH = 24;
        const string HoverLock = "KerbalNewsBrowserHover", TypeLock = "KerbalNewsBrowserTyping";

        ApplicationLauncherButton button;
        GameObject canvasGo;
        Canvas canvas;
        CanvasScaler scaler;
        RectTransform windowRt, viewportRt, contentRt;
        ScrollRect scroll;
        Image pageBg;
        TMP_InputField address;
        TextMeshProUGUI titleText, statusText, measurer;
        TMP_InputField[] chromeFields = new TMP_InputField[0];

        PageResult page;
        PageLayout layout;
        readonly List<GameObject> pageObjects = new List<GameObject>();
        readonly Dictionary<string, TMP_InputField> fields = new Dictionary<string, TMP_InputField>();
        readonly Dictionary<int, List<KeyValuePair<Image, RectItem>>> linkRects = new Dictionary<int, List<KeyValuePair<Image, RectItem>>>();
        readonly Dictionary<int, List<KeyValuePair<TextMeshProUGUI, TextItem>>> linkTexts = new Dictionary<int, List<KeyValuePair<TextMeshProUGUI, TextItem>>>();
        int hoverLink = -1;
        bool dirty, wantRender;
        float lastWidth, nextStatus;
        string renderedUrl;

        static NewsSystem Sys { get { return NewsSystem.Instance; } }
        static Sites Sites { get { return Sys != null ? Sys.Sites : null; } }

        // ------------------------------------------------------------------ lifecycle

        public static Browser Instance { get; private set; }

        /// <summary>Opens the window on a page (used by the self-test).</summary>
        public void Open(string to)
        {
            Show();
            if (canvasGo == null) return;
            Navigate(to);
        }

        /// <summary>Scrolls to a fraction of the page, 0 = top, 1 = bottom (self-test).</summary>
        public void ScrollTo(float f)
        {
            if (layout == null) return;
            contentRt.anchoredPosition = new Vector2(0, Mathf.Max(0, layout.Height - viewportRt.rect.height) * Mathf.Clamp(f, 0, 1));
        }

        /// <summary>Hides the window for a screenshot. Returns true if it was showing.</summary>
        public bool HideForCapture(bool hide)
        {
            if (canvasGo == null) return false;
            if (hide) { bool was = canvasGo.activeSelf; canvasGo.SetActive(false); return was; }
            canvasGo.SetActive(visible);
            return true;
        }

        public PageLayout CurrentLayout { get { return layout; } }
        public PageResult CurrentPage { get { return page; } }

        /// <summary>Runs a page action the way a click would (self-test).</summary>
        public void Act(string action, Dictionary<string, string> typed)
        {
            if (typed != null) foreach (var kv in typed) { TMP_InputField f; if (fields.TryGetValue(kv.Key, out f)) f.text = kv.Value; }
            RunAction(action);
        }

        void Awake() { Instance = this; }

        void Start()
        {
            GameEvents.onGUIApplicationLauncherReady.Add(AddButton);
            GameEvents.onGUIApplicationLauncherUnreadifying.Add(RemoveButton);
            if (ApplicationLauncher.Ready) AddButton();
            if (Sys != null)
            {
                Sys.Engine.ArticlesPublished += OnPublished;
                Sys.Engine.CommunityChanged += MarkDirty;
                Sys.Sites.Changed += MarkDirty;
            }
            if (visible) Show();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            GameEvents.onGUIApplicationLauncherReady.Remove(AddButton);
            GameEvents.onGUIApplicationLauncherUnreadifying.Remove(RemoveButton);
            RemoveButton(GameScenes.MAINMENU);
            if (Sys != null)
            {
                Sys.Engine.ArticlesPublished -= OnPublished;
                Sys.Engine.CommunityChanged -= MarkDirty;
                Sys.Sites.Changed -= MarkDirty;
            }
            InputLockManager.RemoveControlLock(HoverLock);
            InputLockManager.RemoveControlLock(TypeLock);
            if (canvasGo != null) Destroy(canvasGo);
        }

        void OnPublished(List<Article> fresh) { MarkDirty(); }
        void MarkDirty() { dirty = true; }

        void AddButton()
        {
            if (button != null || ApplicationLauncher.Instance == null) return;
            var icon = GameDatabase.Instance.GetTexture("KerbalNews/Icons/button", false);
            button = ApplicationLauncher.Instance.AddModApplication(Show, Hide, null, null, null, null,
                ApplicationLauncher.AppScenes.SPACECENTER | ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW |
                ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.SPH | ApplicationLauncher.AppScenes.TRACKSTATION,
                icon ?? new Texture2D(38, 38, TextureFormat.ARGB32, false));
            if (visible) button.SetTrue(false);
        }

        void RemoveButton(GameScenes s)
        {
            if (button != null && ApplicationLauncher.Instance != null) ApplicationLauncher.Instance.RemoveModApplication(button);
            button = null;
        }

        void Show()
        {
            if (Sys == null || !Sys.HasGame) return;
            visible = true;
            if (canvasGo == null) Build();
            canvasGo.SetActive(true);
            wantRender = true;
        }

        void Hide()
        {
            visible = false;
            if (canvasGo != null) canvasGo.SetActive(false);
            InputLockManager.RemoveControlLock(HoverLock);
            InputLockManager.RemoveControlLock(TypeLock);
        }

        void Close()
        {
            Hide();
            if (button != null) button.SetFalse(false);
        }

        public static float Scale
        {
            get
            {
                var g = NewsSystem.Settings;
                if (g != null && g.UiScale > 0.4f) return g.UiScale;
                // Auto: 1x at 1080p, 2x at 4K, times KSP's own UI scale.
                return Mathf.Max(1f, Screen.height / 1080f) * GameSettings.UI_SCALE;
            }
        }

        void Update()
        {
            if (!visible || canvasGo == null) return;
            if (Sys == null || !Sys.HasGame) { Hide(); return; }
            if (Math.Abs(scaler.scaleFactor - Scale) > 0.001f) { scaler.scaleFactor = Scale; ClampWindow(); wantRender = true; }

            float width = viewportRt.rect.width;
            if (Math.Abs(width - lastWidth) > 1f) wantRender = true;
            bool typing = fields.Values.Any(f => f != null && f.isFocused) || chromeFields.Any(f => f != null && f.isFocused);
            // Live pages refresh when news arrives; never while the player is typing into the page.
            if (dirty && !typing) { dirty = false; wantRender = true; }
            if (wantRender && width > 10) { wantRender = false; Render(true); }

            foreach (var f in fields.Values.Concat(chromeFields)) ResetFieldScroll(f);

            if (Time.realtimeSinceStartup > nextStatus) { nextStatus = Time.realtimeSinceStartup + 0.5f; UpdateStatus(); }

            bool over = RectTransformUtility.RectangleContainsScreenPoint(windowRt, Input.mousePosition, null);
            if (over) InputLockManager.SetControlLock(ControlTypes.CAMERACONTROLS | ControlTypes.KSC_ALL | ControlTypes.EDITOR_SOFT_LOCK | ControlTypes.TRACKINGSTATION_ALL, HoverLock);
            else InputLockManager.RemoveControlLock(HoverLock);
            // Typing must not stage the rocket or warp time.
            if (typing) InputLockManager.SetControlLock(ControlTypes.KEYBOARDINPUT | ControlTypes.ALL_SHIP_CONTROLS | ControlTypes.STAGING | ControlTypes.TIMEWARP | ControlTypes.MAP | ControlTypes.QUICKSAVE | ControlTypes.QUICKLOAD | ControlTypes.PAUSE | ControlTypes.CAMERACONTROLS | ControlTypes.MISC, TypeLock);
            else InputLockManager.RemoveControlLock(TypeLock);
        }

        /// <summary>
        /// A field laid out while still zero-width scrolls its text as if the box were tiny, showing only the end
        /// ("e/a2"). When nobody is typing in it, put the text back at the start.
        /// </summary>
        static void ResetFieldScroll(TMP_InputField f)
        {
            if (f == null || f.isFocused) return;
            foreach (var g in new Graphic[] { f.textComponent, f.placeholder })
            {
                if (g == null) continue;
                var rt = g.rectTransform;
                if (rt.anchoredPosition.x * rt.anchoredPosition.x + rt.anchoredPosition.y * rt.anchoredPosition.y > 0.01f || rt.offsetMin.x != 0 || rt.offsetMax.x != 0)
                    Stretch(rt, 0, 0, 0, 0);
            }
        }

        void UpdateStatus()
        {
            var e = Sys.Engine;
            var g = NewsSystem.Settings;
            string s = (g.CanCallAi ? g.Preset.Name + " / " + g.WriterModel : "Offline templates") + "  ·  " + e.Status;
            if (e.PendingCount > 0) s += "  ·  " + e.PendingCount + " stories in the works";
            if (e.CommunityStatus != "" && e.State.CommunityJobs.Count > 0) s += "  ·  " + e.CommunityStatus;
            if (e.LastError != "") s += "  ·  <color=#ff8a80>" + TextItem.Escape(e.LastError.Length > 100 ? e.LastError.Substring(0, 100) + "..." : e.LastError) + "</color>";
            statusText.text = s;
            int unread = e.UnreadCount(Sites.Now());
            titleText.text = (unread > 0 ? "(" + unread + ") " : "") + TextItem.Escape(page != null ? page.Title : "Kerbal News Network");
        }

        // ------------------------------------------------------------------ chrome

        static GameObject New(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            // TextMeshPro swaps a 100x100 rect for its own default size when it is added; start from nothing instead.
            go.GetComponent<RectTransform>().sizeDelta = Vector2.zero;
            return go;
        }

        static RectTransform Rt(GameObject go) { return go.GetComponent<RectTransform>(); }

        static void Stretch(RectTransform rt, float left, float top, float right, float bottom)
        {
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        static void TopLeft(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        static Color Col(string hex, Color def)
        {
            Color c;
            return hex != null && ColorUtility.TryParseHtmlString(hex, out c) ? c : def;
        }

        void Build()
        {
            canvasGo = new GameObject("KerbalNewsBrowser", typeof(RectTransform));
            DontDestroyOnLoad(canvasGo);
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 400;
            scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = Scale;
            canvasGo.AddComponent<GraphicRaycaster>();

            var win = New("Window", canvasGo.transform);
            windowRt = Rt(win);
            TopLeft(windowRt, winPos.x, winPos.y, winSize.x, winSize.y);
            var frame = win.AddComponent<Image>();
            frame.color = Col("#23272e", Color.black);

            // Toolbar: back, forward, reload, home, address, text size, close. Dragging it moves the window.
            var bar = New("Toolbar", win.transform);
            var barRt = Rt(bar);
            barRt.anchorMin = new Vector2(0, 1); barRt.anchorMax = new Vector2(1, 1); barRt.pivot = new Vector2(0.5f, 1);
            barRt.anchoredPosition = Vector2.zero; barRt.sizeDelta = new Vector2(0, BarH);
            var barImg = bar.AddComponent<Image>();
            barImg.color = Col("#2f343c", Color.black);
            bar.AddComponent<DragHandler>().OnDragged = d => { winPos += new Vector2(d.x, -d.y) / Scale; ClampWindow(); };

            float x = 6;
            ChromeButton(bar.transform, ref x, "back", GoBack);
            ChromeButton(bar.transform, ref x, "forward", GoForward);
            ChromeButton(bar.transform, ref x, "reload", () => Render(true));
            ChromeButton(bar.transform, ref x, "home", () => Navigate(Sites.Home));
            address = MakeField(bar.transform, false, false, 15, "#e8eaed", "");
            var aRt = Rt(address.gameObject);
            aRt.anchorMin = new Vector2(0, 0.5f); aRt.anchorMax = new Vector2(1, 0.5f); aRt.pivot = new Vector2(0, 0.5f);
            aRt.offsetMin = new Vector2(x + 4, -14); aRt.offsetMax = new Vector2(-136, 14);
            var aBg = address.gameObject.GetComponent<Image>();
            aBg.color = Col("#1b1e23", Color.black);
            address.onSubmit.AddListener(v => Navigate(v));
            chromeFields = new[] { address };
            float rx = -128;
            RightButton(bar.transform, ref rx, "A−", () => Sites.Handle("action:scale/-", null));
            RightButton(bar.transform, ref rx, "A+", () => Sites.Handle("action:scale/+", null));
            RightButton(bar.transform, ref rx, "close", Close);

            // Title strip above the status bar would waste space; the title shows in the status bar instead.
            var vp = New("Viewport", win.transform);
            viewportRt = Rt(vp);
            Stretch(viewportRt, 0, BarH, 0, StatusH);
            pageBg = vp.AddComponent<Image>();
            pageBg.color = Color.white;
            vp.AddComponent<RectMask2D>();
            scroll = vp.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
            scroll.inertia = false;
            scroll.viewport = viewportRt;
            var content = New("Content", vp.transform);
            contentRt = Rt(content);
            contentRt.anchorMin = new Vector2(0, 1); contentRt.anchorMax = new Vector2(1, 1); contentRt.pivot = new Vector2(0, 1);
            contentRt.anchoredPosition = Vector2.zero; contentRt.sizeDelta = new Vector2(0, 100);
            scroll.content = contentRt;

            var status = New("Status", win.transform);
            var sRt = Rt(status);
            sRt.anchorMin = new Vector2(0, 0); sRt.anchorMax = new Vector2(1, 0); sRt.pivot = new Vector2(0.5f, 0);
            sRt.anchoredPosition = Vector2.zero; sRt.sizeDelta = new Vector2(0, StatusH);
            var titleGo = New("Title", status.transform);
            titleText = Label(titleGo, 12, "#ffd27f", true);
            Stretch(Rt(titleGo), 8, 0, 0, 0);
            Rt(titleGo).anchorMax = new Vector2(0.35f, 1);
            var statusGo = New("Text", status.transform);
            statusText = Label(statusGo, 12, "#aab2bd", false);
            Stretch(Rt(statusGo), 0, 0, 30, 0);
            Rt(statusGo).anchorMin = new Vector2(0.35f, 0);
            statusText.alignment = TextAlignmentOptions.MidlineRight;

            var grip = New("Resize", win.transform);
            var gRt = Rt(grip);
            gRt.anchorMin = new Vector2(1, 0); gRt.anchorMax = new Vector2(1, 0); gRt.pivot = new Vector2(1, 0);
            gRt.anchoredPosition = Vector2.zero; gRt.sizeDelta = new Vector2(22, 22);
            var gImg = grip.AddComponent<Image>();
            gImg.color = Col("#4a515c", Color.grey);
            grip.AddComponent<DragHandler>().OnDragged = d =>
            {
                winSize += new Vector2(d.x, -d.y) / Scale;
                ClampWindow();
            };

            var m = New("Measure", canvasGo.transform);
            measurer = m.AddComponent<TextMeshProUGUI>();
            measurer.enableWordWrapping = true;
            measurer.richText = true;
            measurer.color = new Color(0, 0, 0, 0);
            measurer.raycastTarget = false;
            TopLeft(Rt(m), -10000, -10000, 100, 100);

            LogFontsOnce();
            SetupSymbols();
        }

        void ClampWindow()
        {
            float sw = Screen.width / Scale, sh = Screen.height / Scale;
            winSize.x = Mathf.Clamp(winSize.x, 520, Mathf.Max(520, sw));
            winSize.y = Mathf.Clamp(winSize.y, 360, Mathf.Max(360, sh));
            winPos.x = Mathf.Clamp(winPos.x, -winSize.x + 120, sw - 120);
            winPos.y = Mathf.Clamp(winPos.y, 0, sh - BarH);
            if (windowRt != null) TopLeft(windowRt, winPos.x, winPos.y, winSize.x, winSize.y);
        }

        TextMeshProUGUI Label(GameObject go, float size, string color, bool bold)
        {
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = Font("sans");
            t.fontSize = size;
            t.color = Col(color, Color.white);
            t.richText = true;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.raycastTarget = false;
            if (bold) t.fontStyle = FontStyles.Bold;
            return t;
        }

        void ChromeButton(Transform parent, ref float x, string label, Action onClick)
        {
            var go = MakeButton(parent, label, onClick);
            var rt = Rt(go);
            rt.anchorMin = new Vector2(0, 0.5f); rt.anchorMax = new Vector2(0, 0.5f); rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0); rt.sizeDelta = new Vector2(30, 28);
            x += 33;
        }

        void RightButton(Transform parent, ref float x, string label, Action onClick)
        {
            var go = MakeButton(parent, label, onClick);
            var rt = Rt(go);
            rt.anchorMin = new Vector2(1, 0.5f); rt.anchorMax = new Vector2(1, 0.5f); rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0); rt.sizeDelta = new Vector2(38, 28);
            x += 41;
        }

        GameObject MakeButton(Transform parent, string label, Action onClick)
        {
            var go = New("Button", parent);
            var img = go.AddComponent<Image>();
            img.color = Col("#3b414b", Color.grey);
            var h = go.AddComponent<ClickHandler>();
            h.OnClick = () => { onClick(); MarkDirty(); };
            h.Target = img; h.Normal = img.color; h.Hover = Col("#525a66", Color.grey);
            var tex = IconTexture(label);
            if (tex != null)
            {
                var ig = New("Icon", go.transform);
                var raw = ig.AddComponent<RawImage>();
                raw.texture = tex;
                raw.color = Col("#e8eaed", Color.white);
                raw.raycastTarget = false;
                var rt = Rt(ig);
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(18, 18);
                return go;
            }
            var t = New("Label", go.transform);
            var tmp = Label(t, 16, "#e8eaed", true);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.text = label;
            Stretch(Rt(t), 0, 0, 0, 0);
            return go;
        }

        // ------------------------------------------------------------------ toolbar icons

        static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();

        /// <summary>Toolbar icons drawn as anti-aliased strokes, since KSP's fonts lack arrow and cross glyphs. Null for plain labels.</summary>
        static Texture2D IconTexture(string name)
        {
            Texture2D tex;
            if (icons.TryGetValue(name, out tex)) return tex;
            var lines = IconStrokes(name);
            if (lines == null) return null;
            const int S = 48;
            tex = new Texture2D(S, S, TextureFormat.ARGB32, false);
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = float.MaxValue;
                    // Strokes are written with y pointing down; textures count rows from the bottom.
                    var p = new Vector2(x + 0.5f, S - y - 0.5f);
                    foreach (var l in lines) d = Mathf.Min(d, SegDist(p, l[0], l[1]));
                    float a = Mathf.Clamp(2.6f - d, 0f, 1f);
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            icons[name] = tex;
            return tex;
        }

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float len2 = ab.x * ab.x + ab.y * ab.y;
            float t = len2 > 0 ? Mathf.Clamp(((p.x - a.x) * ab.x + (p.y - a.y) * ab.y) / len2, 0f, 1f) : 0f;
            float dx = p.x - (a.x + ab.x * t), dy = p.y - (a.y + ab.y * t);
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        static List<Vector2[]> IconStrokes(string name)
        {
            var l = new List<Vector2[]>();
            Action<float, float, float, float> seg = (x1, y1, x2, y2) => l.Add(new[] { new Vector2(x1, y1), new Vector2(x2, y2) });
            switch (name)
            {
                case "back": seg(30, 10, 16, 24); seg(16, 24, 30, 38); return l;
                case "forward": seg(18, 10, 32, 24); seg(32, 24, 18, 38); return l;
                case "close": seg(13, 13, 35, 35); seg(35, 13, 13, 35); return l;
                case "home": seg(9, 24, 24, 10); seg(24, 10, 39, 24); seg(14, 21, 14, 38); seg(14, 38, 34, 38); seg(34, 38, 34, 21); return l;
                case "reload":
                {
                    float r = 13, cx = 24, cy = 25;
                    float prevX = 0, prevY = 0;
                    for (int i = 0; i <= 20; i++)
                    {
                        double ang = (40 + i * 14) * Math.PI / 180;
                        float x = cx + r * (float)Math.Cos(ang), y = cy - r * (float)Math.Sin(ang);
                        if (i > 0) seg(prevX, prevY, x, y);
                        prevX = x; prevY = y;
                    }
                    // Arrowhead at the open end (upper right).
                    float ex = cx + r * (float)Math.Cos(40 * Math.PI / 180), ey = cy - r * (float)Math.Sin(40 * Math.PI / 180);
                    seg(ex, ey, ex - 1, ey - 9); seg(ex, ey, ex - 9, ey + 1);
                    return l;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ fonts

        static List<TMP_FontAsset> fonts;
        static readonly Dictionary<string, TMP_FontAsset> fontByFamily = new Dictionary<string, TMP_FontAsset>();
        static bool fontsLogged;

        static TMP_FontAsset Font(string family)
        {
            family = (family ?? "sans").Trim().Trim('"', '\'').ToLowerInvariant();
            if (family.Contains("serif") && !family.Contains("sans")) family = "serif";
            else if (family.Contains("mono") || family.Contains("courier")) family = "mono";
            else family = "sans";
            TMP_FontAsset f;
            if (fontByFamily.TryGetValue(family, out f) && f != null) return f;
            if (fonts == null) fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().ToList();
            string[] wanted = family == "serif" ? new[] { "serif", "georgia", "times", "noto serif", "liberation serif" }
                : family == "mono" ? new[] { "mono", "consola", "inconsolata", "courier" }
                : new[] { "notosans-regular", "noto sans", "calibri", "arial", "liberationsans", "sans" };
            foreach (var w in wanted)
            {
                f = fonts.FirstOrDefault(x => x != null && x.name.ToLowerInvariant().Contains(w) && !x.name.ToLowerInvariant().Contains("bold"));
                if (f != null) break;
            }
            if (f == null) f = family == "sans" ? (TMP_Settings.defaultFontAsset ?? fonts.FirstOrDefault()) : Font("sans");
            f = WithSymbols(f);
            fontByFamily[family] = f;
            return f;
        }

        /// <summary>Symbols the sites and the toolbar use. Text fonts often lack them.</summary>
        const string Symbols = "\u25C0\u25B6\u21BB\u2302\u2715\u2665\u2661\u2605\u2606\u2713\u2192\u2190\u25B2\u25BC\u2022\u00B7\u2014\u2026\u25CF";
        static readonly Dictionary<TMP_FontAsset, TMP_FontAsset> withSymbols = new Dictionary<TMP_FontAsset, TMP_FontAsset>();

        /// <summary>
        /// A copy of the font that falls back to whichever loaded fonts have the symbols it lacks. KSP's own font
        /// assets are left as they are, so other windows are not affected.
        /// </summary>
        static TMP_FontAsset WithSymbols(TMP_FontAsset f)
        {
            if (f == null) return null;
            TMP_FontAsset copy;
            if (withSymbols.TryGetValue(f, out copy)) return copy;
            if (withSymbols.ContainsValue(f)) return f; // already our copy
            copy = f;
            try
            {
                var extra = new List<TMP_FontAsset>();
                var missing = new List<char>();
                foreach (char c in Symbols)
                {
                    if (f.HasCharacter(c)) continue;
                    var other = fonts.FirstOrDefault(x => x != null && x != f && x.HasCharacter(c));
                    if (other == null) { missing.Add(c); continue; }
                    if (!extra.Contains(other)) extra.Add(other);
                }
                foreach (var e in extra) symbolSources.Add(e);
                symbolSources.Add(f);
                if (extra.Count > 0)
                {
                    copy = UnityEngine.Object.Instantiate(f);
                    copy.name = f.name + " (Kerbal News)";
                    var list = FallbackList(copy);
                    if (list != null) { foreach (var e in extra) if (!list.Contains(e)) list.Add(e); }
                    else copy = f;
                }
                Debug.Log("[KerbalNews] Font " + f.name + ": symbol fallbacks " + (extra.Count > 0 ? string.Join(", ", extra.Select(e => e.name).ToArray()) : "none needed")
                    + (missing.Count > 0 ? "; no font has " + new string(missing.ToArray()) : ""));
            }
            catch (Exception e) { Debug.LogWarning("[KerbalNews] Font fallback: " + e.Message); copy = f; }
            withSymbols[f] = copy;
            return copy;
        }

        /// <summary>The fallback list, whose name differs between TextMeshPro versions. A fresh list, so the original's isn't shared.</summary>
        static List<TMP_FontAsset> FallbackList(TMP_FontAsset f)
        {
            foreach (var name in new[] { "fallbackFontAssetTable", "fallbackFontAssets", "m_FallbackFontAssetTable" })
            {
                var t = f.GetType();
                var prop = t.GetProperty(name);
                var field = t.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                object cur = prop != null && prop.CanRead ? prop.GetValue(f, null) : field != null ? field.GetValue(f) : null;
                if (prop == null && field == null) continue;
                var list = cur is List<TMP_FontAsset> ? new List<TMP_FontAsset>((List<TMP_FontAsset>)cur) : new List<TMP_FontAsset>();
                if (prop != null && prop.CanWrite) prop.SetValue(f, list, null);
                else if (field != null) field.SetValue(f, list);
                else continue;
                return list;
            }
            return null;
        }

        static readonly HashSet<TMP_FontAsset> symbolSources = new HashSet<TMP_FontAsset>();

        /// <summary>Stand-ins for symbols no loaded font has, best first. KSP ships no font with hearts or triangles.</summary>
        static readonly Dictionary<char, string[]> SymbolAlternatives = new Dictionary<char, string[]>
        {
            { '\u25C0', new[] { "\u2190", "<" } }, { '\u25B6', new[] { "\u2192", ">" } }, { '\u21BB', new[] { "\u27F3", "\u21BA", "R" } },
            { '\u2302', new[] { "Home" } }, { '\u2715', new[] { "\u00D7", "x" } }, { '\u2665', new[] { "\u2764", "\u2191", "+" } },
            { '\u2661', new[] { "\u2191", "+" } }, { '\u2605', new[] { "*" } }, { '\u2606', new[] { "*" } }, { '\u2713', new[] { "\u221A", "v" } },
            { '\u25B2', new[] { "\u2191", "^" } }, { '\u25BC', new[] { "\u2193", "v" } }, { '\u25CF', new[] { "\u2022", "o" } },
            { '\u2022', new[] { "\u00B7", "-" } }, { '\u2014', new[] { "-" } }, { '\u2026', new[] { "..." } },
        };
        static Dictionary<char, string> symbolMap;

        static bool CanDraw(string s) { return s.All(ch => ch < 128 || symbolSources.Any(f => f != null && f.HasCharacter(ch))); }

        /// <summary>Builds the replacement table once the fonts are known, and hands it to the text builder.</summary>
        static void SetupSymbols()
        {
            if (symbolMap != null) return;
            Font("sans");
            symbolMap = new Dictionary<char, string>();
            foreach (var kv in SymbolAlternatives)
            {
                if (CanDraw(kv.Key.ToString())) continue;
                symbolMap[kv.Key] = kv.Value.FirstOrDefault(CanDraw) ?? kv.Value.Last();
            }
            var map = symbolMap;
            TextItem.SymbolFilter = map.Count == 0 ? (Func<string, string>)null : s =>
            {
                if (s == null || s.All(ch => ch < 128)) return s;
                var sb = new System.Text.StringBuilder(s.Length);
                foreach (char ch in s) { string r; if (map.TryGetValue(ch, out r)) sb.Append(r); else sb.Append(ch); }
                return sb.ToString();
            };
            TextItem.MonoSpacing = Font("mono") != Font("sans");
            if (map.Count > 0) Debug.Log("[KerbalNews] Symbol stand-ins: " + string.Join(" ", map.Select(kv => kv.Key + "=" + kv.Value).ToArray()));
        }

        static void LogFontsOnce()
        {
            if (fontsLogged) return;
            fontsLogged = true;
            if (fonts == null) fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().ToList();
            Debug.Log("[KerbalNews] TextMeshPro fonts: " + string.Join(", ", fonts.Where(f => f != null).Select(f => f.name).Distinct().ToArray())
                + ". Using sans=" + Name(Font("sans")) + ", serif=" + Name(Font("serif")) + ", mono=" + Name(Font("mono")));
        }

        static string Name(TMP_FontAsset f) { return f != null ? f.name : "none"; }

        // ------------------------------------------------------------------ navigation

        void Navigate(string to)
        {
            to = (to ?? "").Trim();
            if (to == "") return;
            if (to.StartsWith("action:")) { RunAction(to); return; }
            if (url != to)
            {
                RememberScroll();
                back.Add(url);
                if (back.Count > 50) back.RemoveAt(0);
                forward.Clear();
                url = to;
            }
            Render(false);
        }

        void GoBack()
        {
            if (back.Count == 0) return;
            RememberScroll();
            forward.Add(url);
            url = back[back.Count - 1];
            back.RemoveAt(back.Count - 1);
            Render(false, true);
        }

        void GoForward()
        {
            if (forward.Count == 0) return;
            RememberScroll();
            back.Add(url);
            url = forward[forward.Count - 1];
            forward.RemoveAt(forward.Count - 1);
            Render(false, true);
        }

        void RememberScroll() { if (contentRt != null) scrollMemory[url] = contentRt.anchoredPosition.y; }

        void RunAction(string action)
        {
            var form = fields.ToDictionary(kv => kv.Key, kv => kv.Value != null ? kv.Value.text : "");
            ActionResult r;
            try { r = Sites.Handle(action, form); }
            catch (Exception e) { Debug.LogError("[KerbalNews] Action " + action + " failed: " + e); return; }
            if (r.Navigate != null) Navigate(r.Navigate);
            else if (r.Refresh) Render(true, false, false);
        }

        void Click(int link)
        {
            if (layout == null || link < 0 || link >= layout.Links.Count) return;
            var t = layout.Links[link];
            if (t.Action != "") RunAction(t.Action.StartsWith("action:") ? t.Action : "action:" + t.Action);
            else if (t.Href != "") Navigate(t.Href);
        }

        // ------------------------------------------------------------------ rendering

        /// <summary>Renders the current URL. keepScroll keeps the reader's place (live refresh, actions);
        /// restoreScroll returns to where they were on that page (back/forward). keepForm keeps typed text.</summary>
        void Render(bool keepScroll, bool restoreScroll = false, bool keepForm = true)
        {
            if (Sites == null || contentRt == null) return;
            float y = contentRt.anchoredPosition.y;
            var typed = keepForm && renderedUrl == url ? fields.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value.text) : null;
            try
            {
                page = Sites.Render(url);
                var root = Html.Parse(page.Html);
                Css.Apply(root, Css.Parse(page.Css));
                lastWidth = viewportRt.rect.width;
                layout = Layout.Run(root, lastWidth, Measure);
            }
            catch (Exception e)
            {
                Debug.LogError("[KerbalNews] Page " + url + " failed: " + e);
                page = new PageResult { Url = url, Title = "Error", Html = "", Css = "" };
                layout = new PageLayout { Width = lastWidth, Height = 200 };
                layout.Texts.Add(new TextItem { X = 20, Y = 20, W = lastWidth - 40, H = 160, FontSize = 16, Runs = { new Run { Text = "This page could not be drawn: " + e.Message } } });
            }
            Draw();
            if (typed != null)
                foreach (var kv in typed)
                {
                    TMP_InputField f;
                    if (fields.TryGetValue(kv.Key, out f)) f.text = kv.Value;
                }
            if (!address.isFocused) { address.text = url; address.MoveTextStart(false); }
            float target = keepScroll ? y : restoreScroll && scrollMemory.ContainsKey(url) ? scrollMemory[url] : 0;
            float max = Mathf.Max(0, layout.Height - viewportRt.rect.height);
            contentRt.anchoredPosition = new Vector2(0, Mathf.Clamp(target, 0, max));
            renderedUrl = url;
            UpdateStatus();
        }

        readonly Dictionary<string, Vector2> measureCache = new Dictionary<string, Vector2>();

        void Measure(TextItem t, float maxWidth, out float w, out float h)
        {
            string rich = t.Build(-1);
            string key = rich + "|" + t.FontSize + "|" + t.Family + "|" + t.Bold + t.Italic + "|" + t.LineHeight + "|" + (int)maxWidth;
            Vector2 v;
            if (!measureCache.TryGetValue(key, out v))
            {
                v = MeasureNow(t, rich, maxWidth);
                if (measureCache.Count > 4000) measureCache.Clear();
                measureCache[key] = v;
            }
            w = Mathf.Min(v.x, maxWidth);
            h = v.y + 1;
        }

        /// <summary>
        /// Measures by laying the text out for real in a hidden text object of the given width, so the numbers come from
        /// the same code path as the drawing. TextMeshPro's GetPreferredValues measured some fonts far too narrow in KSP,
        /// which made wrapped lines overlap.
        /// </summary>
        Vector2 MeasureNow(TextItem t, string rich, float maxWidth)
        {
            ApplyStyle(measurer, t);
            measurer.rectTransform.sizeDelta = new Vector2(Mathf.Min(maxWidth, 20000f), 20000f);
            measurer.text = rich;
            measurer.ForceMeshUpdate();
            var info = measurer.textInfo;
            int n = info != null ? info.lineCount : 0;
            if (n <= 0) return new Vector2(0, t.FontSize * t.LineHeight);
            float w = 0;
            for (int i = 0; i < n; i++) w = Mathf.Max(w, info.lineInfo[i].maxAdvance);
            var b = measurer.textBounds;
            if (b.size.x > 0) w = Mathf.Max(w, b.max.x);
            float h = info.lineInfo[0].ascender - info.lineInfo[n - 1].descender;
            return new Vector2(Mathf.Ceil(w + 1), Mathf.Max(h, t.FontSize));
        }

        /// <summary>Texts whose drawn lines don't fit the box the layout gave them. The self-test counts these.</summary>
        public int CountOverflows(List<string> examples)
        {
            int bad = 0;
            foreach (var kv in drawnTexts)
            {
                var tmp = kv.Key;
                if (tmp == null) continue;
                tmp.ForceMeshUpdate();
                var info = tmp.textInfo;
                int n = info != null ? info.lineCount : 0;
                if (n == 0) continue;
                float h = info.lineInfo[0].ascender - info.lineInfo[n - 1].descender;
                if (h > kv.Value.H + 6 || n > Mathf.Max(1, Mathf.RoundToInt(kv.Value.H / (kv.Value.FontSize * 1.05f))) + 1)
                {
                    bad++;
                    if (examples != null && examples.Count < 5) examples.Add("\"" + ResponseParser.Clip(tmp.text, 40) + "\" " + n + " lines in " + kv.Value.W.ToString("0") + "x" + kv.Value.H.ToString("0"));
                }
            }
            return bad;
        }

        readonly List<KeyValuePair<TextMeshProUGUI, TextItem>> drawnTexts = new List<KeyValuePair<TextMeshProUGUI, TextItem>>();

        static void ApplyStyle(TextMeshProUGUI tmp, TextItem t)
        {
            tmp.font = Font(t.Family);
            tmp.fontSize = t.FontSize;
            tmp.fontStyle = (t.Bold ? FontStyles.Bold : FontStyles.Normal) | (t.Italic ? FontStyles.Italic : FontStyles.Normal);
            tmp.enableWordWrapping = true;
            tmp.richText = true;
            // TextMeshPro's default line height is about 1.2em; CSS line-height is the whole line.
            tmp.lineSpacing = Mathf.Clamp((t.LineHeight - 1.2f) * 100f, -30f, 120f);
            tmp.alignment = t.Align == "center" ? TextAlignmentOptions.Top : t.Align == "right" ? TextAlignmentOptions.TopRight : t.Align == "justify" ? TextAlignmentOptions.TopJustified : TextAlignmentOptions.TopLeft;
        }

        void Clear()
        {
            foreach (var go in pageObjects) if (go != null) Destroy(go);
            pageObjects.Clear();
            drawnTexts.Clear();
            fields.Clear();
            linkRects.Clear();
            linkTexts.Clear();
            hoverLink = -1;
        }

        GameObject Item(string name, float x, float y, float w, float h)
        {
            var go = New(name, contentRt);
            TopLeft(Rt(go), x, y, Mathf.Max(0, w), Mathf.Max(0, h));
            pageObjects.Add(go);
            return go;
        }

        void Draw()
        {
            Clear();
            pageBg.color = Col(layout.Background, Color.white);
            contentRt.sizeDelta = new Vector2(0, Mathf.Max(layout.Height, viewportRt.rect.height));

            foreach (var r in layout.Rects)
            {
                if (r.Color == null && r.HoverColor == null) continue;
                var img = Item("box", r.X, r.Y, r.W, r.H).AddComponent<Image>();
                img.color = Col(r.Color, new Color(0, 0, 0, 0));
                img.raycastTarget = false;
                if (r.Link >= 0 && r.HoverColor != null) Add(linkRects, r.Link, new KeyValuePair<Image, RectItem>(img, r));
            }
            foreach (var im in layout.Images)
            {
                var go = Item("img", im.X, im.Y, im.W, im.H);
                var raw = go.AddComponent<RawImage>();
                raw.raycastTarget = false;
                var tex = LoadImage(im.Src);
                if (tex != null) raw.texture = tex;
                else raw.color = Col("#d9d2c1", Color.grey);
            }
            foreach (var t in layout.Texts)
            {
                var go = Item("text", t.X, t.Y, t.W + 2, t.H + 2);
                var tmp = go.AddComponent<TextMeshProUGUI>();
                TopLeft(Rt(go), t.X, t.Y, t.W + 2, t.H + 2); // adding TextMeshPro can reset the rect to its default size
                ApplyStyle(tmp, t);
                drawnTexts.Add(new KeyValuePair<TextMeshProUGUI, TextItem>(tmp, t));
                tmp.color = Col(t.Color, Color.black);
                tmp.overflowMode = TextOverflowModes.Overflow;
                tmp.text = t.Build(-1);
                var ids = t.LinkIds.ToList();
                tmp.raycastTarget = ids.Count > 0;
                foreach (var id in ids) Add(linkTexts, id, new KeyValuePair<TextMeshProUGUI, TextItem>(tmp, t));
                if (ids.Count > 0)
                {
                    var tl = go.AddComponent<TextLinks>();
                    tl.Owner = this;
                    tl.Text = tmp;
                }
            }
            foreach (var reg in layout.Regions)
            {
                var go = Item("link", reg.X, reg.Y, reg.W, reg.H);
                var img = go.AddComponent<Image>();
                img.color = new Color(0, 0, 0, 0);
                var h = go.AddComponent<ClickHandler>();
                int link = reg.Link;
                h.OnClick = () => Click(link);
                h.OnEnter = () => SetHover(link);
                h.OnExit = () => { if (hoverLink == link) SetHover(-1); };
            }
            foreach (var inp in layout.Inputs)
            {
                var f = MakeField(contentRt, inp.Multiline, inp.Password, inp.FontSize, inp.Color, inp.Placeholder);
                var go = f.gameObject;
                pageObjects.Add(go);
                TopLeft(Rt(go), inp.X, inp.Y, inp.W, inp.H);
                go.GetComponent<Image>().color = new Color(0, 0, 0, 0);
                f.textComponent.font = Font(inp.Family);
                f.text = inp.Value ?? "";
                f.MoveTextStart(false);
                if (inp.Name != "") fields[inp.Name] = f;
                if (!inp.Multiline)
                {
                    var me = inp;
                    f.onSubmit.AddListener(v => SubmitFrom(me));
                }
            }
        }

        /// <summary>Enter in a one-line field presses the nearest button after it, like a form submit.</summary>
        void SubmitFrom(InputItem inp)
        {
            var next = layout.Regions.Where(r => layout.Links[r.Link].Action != "" && (r.Y > inp.Y - 2 || (Math.Abs(r.Y - inp.Y) < 30 && r.X > inp.X)))
                .OrderBy(r => Math.Abs(r.Y - inp.Y) * 3 + Math.Abs(r.X - inp.X)).FirstOrDefault();
            if (next != null) Click(next.Link);
        }

        static void Add<T>(Dictionary<int, List<T>> d, int k, T v)
        {
            List<T> l;
            if (!d.TryGetValue(k, out l)) d[k] = l = new List<T>();
            l.Add(v);
        }

        public void SetHover(int link)
        {
            if (link == hoverLink) return;
            int old = hoverLink;
            hoverLink = link;
            foreach (var id in new[] { old, link })
            {
                if (id < 0) continue;
                List<KeyValuePair<Image, RectItem>> rects;
                if (linkRects.TryGetValue(id, out rects))
                    foreach (var kv in rects) if (kv.Key != null) kv.Key.color = Col(id == link ? kv.Value.HoverColor ?? kv.Value.Color : kv.Value.Color, new Color(0, 0, 0, 0));
                List<KeyValuePair<TextMeshProUGUI, TextItem>> texts;
                if (linkTexts.TryGetValue(id, out texts))
                    foreach (var kv in texts) if (kv.Key != null) kv.Key.text = kv.Value.Build(link);
            }
        }

        public void ClickLink(int link) { Click(link); }

        TMP_InputField MakeField(Transform parent, bool multi, bool password, float size, string color, string placeholder)
        {
            var go = New("field", parent);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0);
            var area = New("area", go.transform);
            Stretch(Rt(area), 6, 2, 6, 2);
            area.AddComponent<RectMask2D>();
            var phGo = New("placeholder", area.transform);
            Stretch(Rt(phGo), 0, 0, 0, 0);
            var ph = phGo.AddComponent<TextMeshProUGUI>();
            ph.font = Font("sans"); ph.fontSize = size; ph.fontStyle = FontStyles.Italic;
            ph.color = Col("#8a8478", Color.grey);
            ph.text = placeholder ?? "";
            ph.enableWordWrapping = multi;
            ph.raycastTarget = false;
            var txGo = New("text", area.transform);
            Stretch(Rt(txGo), 0, 0, 0, 0);
            var tx = txGo.AddComponent<TextMeshProUGUI>();
            tx.font = Font("sans"); tx.fontSize = size;
            tx.color = Col(color, Color.black);
            tx.enableWordWrapping = multi;
            tx.richText = false;
            if (!multi) { ph.alignment = TextAlignmentOptions.MidlineLeft; tx.alignment = TextAlignmentOptions.MidlineLeft; }
            var f = go.AddComponent<TMP_InputField>();
            f.textViewport = Rt(area);
            f.textComponent = tx;
            f.placeholder = ph;
            f.fontAsset = Font("sans");
            f.pointSize = size;
            f.richText = false;
            f.lineType = multi ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            if (password) f.contentType = TMP_InputField.ContentType.Password;
            f.caretColor = Col(color, Color.black);
            f.customCaretColor = true;
            return f;
        }

        // ------------------------------------------------------------------ images

        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();

        static Texture2D LoadImage(string src)
        {
            if (string.IsNullOrEmpty(src)) return null;
            Texture2D tex;
            if (textures.TryGetValue(src, out tex)) return tex;
            string root = Path.Combine(KSPUtil.ApplicationRootPath, "GameData/KerbalNews");
            string path = src.StartsWith("kbimg:") ? Path.Combine(root, "PluginData/Images/" + Path.GetFileName(src.Substring(6)))
                : src.StartsWith("kbshot:") ? Path.Combine(GalleryStore.Dir, Path.GetFileName(src.Substring(7)))
                : Path.Combine(root, src.TrimStart('/'));
            try
            {
                if (File.Exists(path))
                {
                    tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                    if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path))) tex = null;
                }
            }
            catch (Exception e) { Debug.LogWarning("[KerbalNews] Could not load image " + path + ": " + e.Message); tex = null; }
            if (textures.Count > 60) textures.Clear();
            textures[src] = tex;
            return tex;
        }
    }

    /// <summary>Clicks and hover for a block link, a button, or a chrome button.</summary>
    public class ClickHandler : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Action OnClick, OnEnter, OnExit;
        public Image Target;
        public Color Normal, Hover;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || e.dragging) return;
            if (OnClick != null) OnClick();
        }

        public void OnPointerEnter(PointerEventData e) { if (Target != null) Target.color = Hover; if (OnEnter != null) OnEnter(); }
        public void OnPointerExit(PointerEventData e) { if (Target != null) Target.color = Normal; if (OnExit != null) OnExit(); }
    }

    /// <summary>Inline links inside a paragraph: finds the link under the mouse with TextMeshPro's link info.</summary>
    public class TextLinks : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Browser Owner;
        public TextMeshProUGUI Text;
        bool over;
        int hover = -1;

        int LinkAt(Vector3 screen)
        {
            int i = TMP_TextUtilities.FindIntersectingLink(Text, screen, null);
            if (i < 0 || i >= Text.textInfo.linkCount) return -1;
            int id;
            return int.TryParse(Text.textInfo.linkInfo[i].GetLinkID(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id) ? id : -1;
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || e.dragging) return;
            int id = LinkAt(e.position);
            if (id >= 0) Owner.ClickLink(id);
        }

        public void OnPointerEnter(PointerEventData e) { over = true; }
        public void OnPointerExit(PointerEventData e) { over = false; if (hover >= 0) { Owner.SetHover(-1); hover = -1; } }

        void Update()
        {
            if (!over) return;
            int id = LinkAt(Input.mousePosition);
            if (id == hover) return;
            hover = id;
            Owner.SetHover(id);
        }
    }

    /// <summary>Moving and resizing the window.</summary>
    public class DragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        public Action<Vector2> OnDragged;
        public void OnBeginDrag(PointerEventData e) { }
        public void OnDrag(PointerEventData e) { if (OnDragged != null) OnDragged(e.delta); }
    }
}
