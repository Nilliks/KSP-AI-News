using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KerbalNews.KSP
{
    /// <summary>
    /// The replay's seek bar, like a video site's: a thin track that grows when the mouse is over it, the watched part in
    /// red, a round handle, and the time under the mouse in a bubble. Click anywhere to jump, or drag the handle and the
    /// picture follows; playback carries on from where it is let go. It follows the replay every frame by itself, so the
    /// page doesn't need redrawing while it plays or while it's dragged.
    /// </summary>
    public class SeekBar : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IEndDragHandler
    {
        float w, h;
        RectTransform track, played, hoverFill, knob, bubble;
        TextMeshProUGUI bubbleText;
        bool over, dragging;
        float hoverX = -1, dragFrac;
        static readonly Color Red = new Color(0.90f, 0.18f, 0.18f);

        static NewsEngine Eng { get { var s = NewsSystem.Instance; return s != null && s.HasGame ? s.Engine : null; } }

        public static void Attach(GameObject go, float width, float height)
        {
            var b = go.AddComponent<SeekBar>();
            b.w = width; b.h = height;
            b.Build();
        }

        RectTransform Rect(string name, Transform parent, Color c, bool round = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var im = go.AddComponent<Image>();
            im.color = c;
            im.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
            return rt;
        }

        void Build()
        {
            track = Rect("track", transform, new Color(1, 1, 1, 0.22f));
            hoverFill = Rect("hover", transform, new Color(1, 1, 1, 0.35f));
            played = Rect("played", transform, Red);
            knob = Rect("knob", transform, Red);
            knob.pivot = new Vector2(0.5f, 0.5f);
            var go = new GameObject("bubble", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            go.AddComponent<Image>().color = new Color(0.05f, 0.06f, 0.08f, 0.9f);
            go.GetComponent<Image>().raycastTarget = false;
            bubble = go.GetComponent<RectTransform>();
            bubble.anchorMin = bubble.anchorMax = new Vector2(0, 1);
            bubble.pivot = new Vector2(0.5f, 0);
            bubble.sizeDelta = new Vector2(64, 22);
            var tgo = new GameObject("t", typeof(RectTransform));
            tgo.transform.SetParent(go.transform, false);
            bubbleText = tgo.AddComponent<TextMeshProUGUI>();
            bubbleText.font = Browser.Font("mono");
            bubbleText.fontSize = 13;
            bubbleText.color = Color.white;
            bubbleText.alignment = TextAlignmentOptions.Center;
            bubbleText.raycastTarget = false;
            var trt = tgo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            go.SetActive(false);
            Layout(0);
        }

        float Frac(PointerEventData e)
        {
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, e.position, e.pressEventCamera ?? e.enterEventCamera, out local);
            var r = ((RectTransform)transform).rect;
            return Mathf.Clamp01((local.x - r.xMin) / Mathf.Max(1, r.width));
        }

        double Length { get { var e = Eng; return e != null && e.Replay != null ? e.Replay.Data.Length : 0; } }

        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || Eng == null || Eng.Replay == null) return;
            dragging = true;
            dragFrac = Frac(e);
            Eng.ReplayScrub(dragFrac * Length);
        }

        public void OnBeginDrag(PointerEventData e) { }   // keeps the page from scrolling while the handle is dragged

        public void OnDrag(PointerEventData e)
        {
            if (!dragging || Eng == null) return;
            dragFrac = Frac(e);
            hoverX = dragFrac;
            Eng.ReplayScrub(dragFrac * Length);
        }

        public void OnEndDrag(PointerEventData e) { }

        public void OnPointerUp(PointerEventData e)
        {
            if (!dragging) return;
            dragging = false;
            if (Eng != null) Eng.ReplayScrubEnd(dragFrac * Length);   // redraws the page: chat and clock catch up
        }

        public void OnPointerEnter(PointerEventData e) { over = true; }
        public void OnPointerExit(PointerEventData e) { over = false; hoverX = -1; }

        void Update()
        {
            var eng = Eng;
            if (eng == null || eng.Replay == null) return;
            if (over && !dragging)
            {
                Vector2 local;
                var rt = (RectTransform)transform;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, Input.mousePosition, null, out local))
                    hoverX = Mathf.Clamp01((local.x - rt.rect.xMin) / Mathf.Max(1, rt.rect.width));
            }
            double len = Length;
            float frac = dragging ? dragFrac : len > 0 ? (float)(eng.Replay.Pos / len) : 0;
            Layout(frac);
        }

        void Layout(float frac)
        {
            bool big = over || dragging;
            float th = big ? 6 : 3;
            track.anchoredPosition = new Vector2(0, 0); track.sizeDelta = new Vector2(w, th);
            played.anchoredPosition = new Vector2(0, 0); played.sizeDelta = new Vector2(w * frac, th);
            bool showHover = hoverX >= 0 && big;
            hoverFill.gameObject.SetActive(showHover && !dragging);
            if (showHover) { hoverFill.anchoredPosition = new Vector2(0, 0); hoverFill.sizeDelta = new Vector2(w * hoverX, th); }
            float k = big ? 14 : 0;
            knob.gameObject.SetActive(big);
            knob.anchoredPosition = new Vector2(w * frac, 0); knob.sizeDelta = new Vector2(k, k);
            bubble.gameObject.SetActive(showHover);
            if (showHover)
            {
                bubble.anchoredPosition = new Vector2(Mathf.Clamp(w * hoverX, 34, w - 34), 2);
                bubbleText.text = Mmss(hoverX * Length);
            }
        }

        static string Mmss(double s)
        {
            int t = (int)System.Math.Max(0, s);
            return t >= 3600 ? (t / 3600) + ":" + (t % 3600 / 60).ToString("00") + ":" + (t % 60).ToString("00") : (t / 60).ToString("00") + ":" + (t % 60).ToString("00");
        }
    }
}
