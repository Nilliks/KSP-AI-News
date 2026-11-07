using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KerbalNews.KSP
{
    /// <summary>
    /// The on-screen stats drawn over the stream video, like a real launch broadcast: LIVE and the camera at the top left,
    /// the mission clock at the top right, speed and altitude at the bottom left, stage, throttle and fuel at the bottom right,
    /// and the flight's milestones along the bottom. It lives on the video image and updates itself four times a second.
    /// Sizes are laid out for a 1280x720 frame and scaled to the image.
    /// </summary>
    public class StreamOverlayUi : MonoBehaviour
    {
        float w, h, s, next;
        TextMeshProUGUI vessel, camera, clock, clockHead, speed, alt, ap, apHead, stage, throttle, fuelText;
        GameObject live, apBox, fuelRow;
        RectTransform fuelFill, stripDone;
        Image clockBox;
        readonly List<KeyValuePair<Image, TextMeshProUGUI>> marks = new List<KeyValuePair<Image, TextMeshProUGUI>>();
        Transform strip;
        readonly List<GameObject> tops = new List<GameObject>();
        string marksFor = "";
        static readonly Color Panel = new Color(10 / 255f, 16 / 255f, 24 / 255f, 0.72f), Head = new Color(0.62f, 0.70f, 0.78f), Sub = new Color(0.78f, 0.83f, 0.88f);

        public static void Attach(GameObject video, float width, float height)
        {
            var o = video.AddComponent<StreamOverlayUi>();
            o.w = width; o.h = height; o.s = height / 720f;
            o.Build();
        }

        // ------------------------------------------------------------------ building

        GameObject Box(string name, Transform parent, float x, float y, float bw, float bh, Color? fill)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x * s, -y * s);
            rt.sizeDelta = new Vector2(bw * s, bh * s);
            if (fill != null) { var im = go.AddComponent<Image>(); im.color = fill.Value; im.raycastTarget = false; }
            if (parent == transform) tops.Add(go);
            return go;
        }

        TextMeshProUGUI Text(Transform parent, float x, float y, float tw, float th, float size, bool mono, Color col, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var go = Box("text", parent, x, y, tw, th, null);
            var t = go.AddComponent<TextMeshProUGUI>();
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(x * s, -y * s); rt.sizeDelta = new Vector2(tw * s, th * s);   // TMP resets the rect when added
            t.font = Browser.Font(mono ? "mono" : "sans");
            t.fontSize = size * s;
            t.fontStyle = FontStyles.Bold;
            t.color = col;
            t.alignment = align;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            t.richText = true;
            return t;
        }

        void Build()
        {
            var root = transform;
            float W = w / s;   // design width for this aspect (1280 at 16:9)

            // Top left: LIVE, the craft, the camera.
            live = Box("live", root, 28, 26, 62, 28, new Color(0.83f, 0.16f, 0.16f));
            Text(live.transform, 0, 3, 62, 24, 18, false, Color.white, TextAlignmentOptions.Top).text = "LIVE";
            vessel = Text(root, 104, 20, 600, 30, 24, false, Color.white);
            camera = Text(root, 104, 48, 600, 22, 15, false, new Color(0.9f, 0.93f, 0.97f));

            // Top right: the mission clock.
            var cb = Box("clock", root, W - 28 - 230, 22, 230, 66, Panel);
            clockBox = cb.GetComponent<Image>();
            clockHead = Text(cb.transform, 0, 6, 214, 18, 13, false, Head, TextAlignmentOptions.TopRight);
            clock = Text(cb.transform, 0, 24, 214, 38, 30, true, Color.white, TextAlignmentOptions.TopRight);

            // Bottom left: speed, altitude, apoapsis.
            var tele = Box("tele", root, 28, 720 - 74 - 82, 470, 82, Panel);
            Text(tele.transform, 18, 12, 140, 18, 13, false, Head).text = "SPEED";
            speed = Text(tele.transform, 18, 32, 150, 40, 32, true, Color.white);
            Text(tele.transform, 168, 12, 140, 18, 13, false, Head).text = "ALTITUDE";
            alt = Text(tele.transform, 168, 32, 150, 40, 32, true, Color.white);
            apBox = new GameObject("ap", typeof(RectTransform)); apBox.transform.SetParent(tele.transform, false);
            apHead = Text(apBox.transform, 318, 12, 140, 18, 13, false, Head); apHead.text = "APOAPSIS";
            ap = Text(apBox.transform, 318, 32, 150, 40, 32, true, Color.white);

            // Bottom right: stage, throttle, fuel.
            var right = Box("stage", root, W - 28 - 260, 720 - 74 - 82, 260, 82, Panel);
            stage = Text(right.transform, 18, 12, 224, 18, 13, false, Head);
            throttle = Text(right.transform, 18, 12, 224, 18, 13, false, Head, TextAlignmentOptions.TopRight);
            fuelRow = Box("fuel", right.transform, 18, 46, 224, 20, null);
            Text(fuelRow.transform, 0, 2, 50, 18, 13, false, Head).text = "FUEL";
            var track = Box("track", fuelRow.transform, 50, 6, 124, 8, new Color(1, 1, 1, 0.18f));
            fuelFill = Box("fill", track.transform, 0, 0, 124, 8, new Color(0.95f, 0.69f, 0.20f)).GetComponent<RectTransform>();
            fuelText = Text(fuelRow.transform, 180, 0, 44, 20, 14, true, Color.white, TextAlignmentOptions.TopRight);

            // Bottom strip: milestones.
            strip = Box("strip", root, 28, 720 - 22 - 36, W - 56, 36, null).transform;
            Box("line", strip, 0, 9, W - 56, 3, new Color(1, 1, 1, 0.35f));
            stripDone = Box("done", strip, 0, 9, 0, 3, Color.white).GetComponent<RectTransform>();
            Refresh();
        }

        void BuildMarks(OverlayModel m)
        {
            foreach (var k in marks) { if (k.Key != null) Destroy(k.Key.gameObject); if (k.Value != null) Destroy(k.Value.gameObject); }
            marks.Clear();
            float span = w / s - 56;
            int n = m.Milestones.Count;
            for (int i = 0; i < n; i++)
            {
                float x = n == 1 ? 0 : span * i / (n - 1);
                var dot = Box("dot", strip, x - (i == 0 ? 0 : i == n - 1 ? 14 : 7), 3, 14, 14, Color.white).GetComponent<Image>();
                var align = i == 0 ? TextAlignmentOptions.TopLeft : i == n - 1 ? TextAlignmentOptions.TopRight : TextAlignmentOptions.Top;
                float lx = i == 0 ? 0 : i == n - 1 ? x - 160 : x - 80;
                var label = Text(strip, lx, 18, 160, 18, 13, false, Color.white, align);
                label.text = m.Milestones[i].Label;
                marks.Add(new KeyValuePair<Image, TextMeshProUGUI>(dot, label));
            }
        }

        // ------------------------------------------------------------------ updating

        void Update()
        {
            if (Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + 0.25f;
            Refresh();
        }

        void Refresh()
        {
            OverlayModel m;
            try { m = StreamDriver.Overlay(); } catch (System.Exception) { m = null; }
            foreach (var c in tops) if (c != null) c.SetActive(m != null);
            if (m == null) return;
            vessel.text = m.Vessel;
            camera.text = m.Camera;
            clockHead.text = m.Scrubbed ? "SCRUBBED" : m.Hold ? "HOLD" : m.Clock.StartsWith("T-") ? "COUNTDOWN" : "MISSION TIME";
            clockHead.color = m.Hold || m.Scrubbed ? new Color(0.95f, 0.69f, 0.20f) : Head;
            clock.text = m.Clock;
            clockBox.color = m.Hold ? new Color(0.35f, 0.24f, 0.05f, 0.8f) : Panel;
            speed.text = m.Speed + "<size=50%><color=#c8d4e0> m/s</color></size>";
            alt.text = Unit(m.Altitude);
            apBox.SetActive(m.ShowApoapsis);
            ap.text = Unit(m.Apoapsis);
            stage.text = m.Stage;
            throttle.text = m.Throttle;
            fuelRow.SetActive(m.Fuel >= 0);
            if (m.Fuel >= 0)
            {
                fuelFill.sizeDelta = new Vector2(124 * s * (float)m.Fuel, 8 * s);
                fuelText.text = (int)System.Math.Round(m.Fuel * 100) + "%";
            }
            string key = string.Join("|", m.Milestones.ConvertAll(x => x.Label).ToArray());
            if (key != marksFor) { marksFor = key; BuildMarks(m); }
            int last = -1;
            for (int i = 0; i < marks.Count && i < m.Milestones.Count; i++)
            {
                bool done = m.Milestones[i].Done;
                if (done) last = i;
                marks[i].Key.color = done ? Color.white : new Color(1, 1, 1, 0.35f);
            }
            float span = w - 56 * s;
            int n = System.Math.Max(2, marks.Count);
            stripDone.sizeDelta = new Vector2(last <= 0 ? 0 : span * last / (n - 1), 3 * s);
        }

        static string Unit(string v)
        {
            int sp = v.LastIndexOf(' ');
            return sp < 0 ? v : v.Substring(0, sp) + "<size=50%><color=#c8d4e0> " + v.Substring(sp + 1) + "</color></size>";
        }
    }
}
