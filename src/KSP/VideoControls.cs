using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KerbalNews.KSP
{
    /// <summary>Click a replay's picture to play or pause it. While it's paused a round play button sits in the middle.</summary>
    public class VideoClick : MonoBehaviour, IPointerClickHandler
    {
        GameObject button;
        static Texture2D playTex;

        static NewsEngine Eng { get { var s = NewsSystem.Instance; return s != null && s.HasGame ? s.Engine : null; } }

        public static void Attach(GameObject video, float w, float h)
        {
            var c = video.AddComponent<VideoClick>();
            float size = Mathf.Max(48, Mathf.Min(w, h) * 0.2f);
            var go = new GameObject("play", typeof(RectTransform));
            go.transform.SetParent(video.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(size, size);
            var img = go.AddComponent<RawImage>();
            img.texture = PlayTexture();
            img.raycastTarget = false;
            c.button = go;
            c.Update();
        }

        /// <summary>A dark see-through disc with a white triangle, drawn once.</summary>
        static Texture2D PlayTexture()
        {
            if (playTex != null) return playTex;
            const int n = 128;
            playTex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            float r = n / 2f - 1;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - n / 2f, dy = y + 0.5f - n / 2f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float disc = Mathf.Clamp01(r - d);                     // soft edge
                    // Triangle pointing right, a little right of centre so it looks centred.
                    float tx = (x - n * 0.38f) / (n * 0.34f), ty = Mathf.Abs(y - n / 2f) / (n * 0.21f);
                    bool tri = tx >= 0 && tx <= 1 && ty <= 1 - tx;
                    byte a = (byte)(disc * (tri ? 255 : 150));
                    px[y * n + x] = tri && disc > 0 ? new Color32(255, 255, 255, a) : new Color32(10, 12, 16, a);
                }
            playTex.SetPixels32(px);
            playTex.Apply();
            return playTex;
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || e.dragging) return;
            var eng = Eng;
            if (eng != null && eng.Replay != null) eng.ReplayPlayPause();
        }

        void Update()
        {
            var eng = Eng;
            bool show = eng != null && eng.Replay != null && !eng.Replay.Playing;
            if (button != null && button.activeSelf != show) button.SetActive(show);
        }
    }

    /// <summary>The stream player's volume: a short bar to click or drag, for the announcer and the sounds together.</summary>
    public class VolumeSlider : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IBeginDragHandler
    {
        float w, h;
        RectTransform fill, knob;
        bool dragging;

        static NewsSystem Sys { get { var s = NewsSystem.Instance; return s != null && s.HasGame ? s : null; } }

        public static void Attach(GameObject go, float w, float h)
        {
            var v = go.AddComponent<VolumeSlider>();
            v.w = w; v.h = h;
            var track = v.Bar("track", new Color(1, 1, 1, 0.25f));
            track.sizeDelta = new Vector2(w, 4);
            v.fill = v.Bar("fill", Color.white);
            v.knob = v.Bar("knob", Color.white);
            v.knob.pivot = new Vector2(0.5f, 0.5f);
            v.knob.sizeDelta = new Vector2(12, 12);
            v.Layout();
        }

        RectTransform Bar(string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var im = go.AddComponent<Image>();
            im.color = c;
            im.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
            return rt;
        }

        static float Level { get { var g = NewsSystem.Settings; return g != null ? (float)g.StreamVolume : 1; } }

        void Set(PointerEventData e)
        {
            Vector2 local;
            var rt = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out local);
            float v = Mathf.Clamp01((local.x - rt.rect.xMin) / Mathf.Max(1, rt.rect.width));
            var g = NewsSystem.Settings;
            if (g == null) return;
            g.StreamVolume = v;
            AnnouncerAudio.ApplyVolume();
            StreamSounds.ApplyVolume();
            Layout();
        }

        public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) { dragging = true; Set(e); } }
        public void OnBeginDrag(PointerEventData e) { }   // keeps the page from scrolling
        public void OnDrag(PointerEventData e) { if (dragging) Set(e); }
        public void OnPointerUp(PointerEventData e)
        {
            if (!dragging) return;
            dragging = false;
            var s = Sys;
            if (s != null) s.SaveSettings();
        }

        void Layout()
        {
            float v = Level;
            fill.sizeDelta = new Vector2(w * v, 4);
            knob.anchoredPosition = new Vector2(w * v, 0);
        }
    }
}
