using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using KerbalNews.Web;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KerbalNews.KSP
{
    /// <summary>
    /// Spell check in the page's text boxes, like a real browser: misspelled words get a red wavy underline as you
    /// type, and right-clicking one opens a small menu of suggestions, "Add to dictionary" and "Ignore". All offline:
    /// a bundled word list plus the game's own names (planets, parts, kerbals, craft, outlets, mods). No AI calls.
    /// </summary>
    public partial class Browser
    {
        static SpellCheck speller;
        static bool spellerLoading;
        float nextGameWords;
        GameObject spellMenu;
        int spellMenuFrame;
        internal readonly HashSet<string> spellAwake = new HashSet<string>();

        /// <summary>The checker, once its word lists have loaded in the background (null until then, or when switched off).</summary>
        internal static SpellCheck Speller
        {
            get
            {
                var g = NewsSystem.Settings;
                if (g != null && !g.SpellCheck) return null;
                if (speller == null && !spellerLoading) LoadSpeller();
                return speller;
            }
        }

        static string DictDir { get { return Path.Combine(KSPUtil.ApplicationRootPath, "GameData/KerbalNews/Dictionary"); } }

        /// <summary>Reads the 90,000-word list off the main thread so opening the browser never hitches.</summary>
        static void LoadSpeller()
        {
            spellerLoading = true;
            string dir = DictDir, user = Path.Combine(KSPUtil.ApplicationRootPath, "GameData/KerbalNews/PluginData/spelling-words.txt");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var sc = new SpellCheck();
                    sc.LoadFile(Path.Combine(dir, "en.txt"), false);
                    sc.LoadFile(Path.Combine(dir, "kerbal.txt"), true);
                    sc.LoadUserFile(user);
                    if (sc.Loaded) speller = sc;   // published whole, so the main thread never reads a half-built list
                    else Debug.LogWarning("[KerbalNews] Spell check: no word list in " + dir);
                }
                catch (Exception e) { Debug.LogWarning("[KerbalNews] Spell check could not load: " + e.Message); }
            });
        }

        /// <summary>
        /// Names the game knows right now. Runs on the main thread (it reads the game), now and then while the browser
        /// is open, so a new kerbal or craft stops being flagged without a restart.
        /// </summary>
        static bool partsAdded;

        internal void AddGameWords()
        {
            var sc = speller;
            if (sc == null || Time.realtimeSinceStartup < nextGameWords) return;
            nextGameWords = Time.realtimeSinceStartup + 30f;
            try
            {
                if (FlightGlobals.Bodies != null) foreach (var b in FlightGlobals.Bodies) if (b != null) sc.AddGameWord(b.bodyName);
                if (FlightGlobals.Vessels != null) foreach (var v in FlightGlobals.Vessels) if (v != null) sc.AddGameWord(v.vesselName);
                if (HighLogic.CurrentGame != null && HighLogic.CurrentGame.CrewRoster != null && HighLogic.CurrentGame.CrewRoster.Crew != null)
                    foreach (var k in HighLogic.CurrentGame.CrewRoster.Crew) if (k != null) sc.AddGameWord(k.name);
                // Parts never change during a game session: once is enough (with 140 mods that is thousands of titles).
                if (!partsAdded && PartLoader.LoadedPartsList != null && (partsAdded = true))
                    foreach (var p in PartLoader.LoadedPartsList) if (p != null) { sc.AddGameWord(p.title); sc.AddGameWord(p.manufacturer); }
                foreach (var o in Outlets.All) sc.AddGameWord(o.Name);
                var g = NewsSystem.Settings;
                if (g != null) foreach (var m in g.Mods) sc.AddGameWord(m.Name);
                if (Sys != null && Sys.Engine != null && Sys.Engine.State != null)
                {
                    var st = Sys.Engine.State;
                    sc.AddGameWord(st.Profile.Name);
                    sc.AddGameWord(st.Profile.ShortName);
                    sc.AddGameWords(st.Crew.Keys);
                    foreach (var t in st.Tracked.Values) if (t.Craft != null) { sc.AddGameWord(t.Craft.VesselName); sc.AddGameWords(t.Craft.Crew); }
                    foreach (var r in st.Reporters.Values) sc.AddGameWords(r);
                }
            }
            catch (Exception e) { Debug.LogWarning("[KerbalNews] Spell check game words: " + e.Message); nextGameWords = Time.realtimeSinceStartup + 300f; }
        }

        /// <summary>Hooks a page text box up to the checker (called as the page is drawn).</summary>
        void AttachSpelling(TMP_InputField f, InputItem inp)
        {
            if (!inp.SpellCheck || inp.Password || f == null) return;
            var g = NewsSystem.Settings;
            if (g != null && !g.SpellCheck) return;
            // Settings boxes hold keys, model names, addresses and numbers: nothing there to spell check.
            if (url != null && url.StartsWith("settings.kb")) return;
            var sf = f.gameObject.AddComponent<SpellField>();
            sf.Owner = this;
            sf.Key = inp.Name != "" ? url + "#" + inp.Name : null;
            sf.Awake0 = sf.Key != null && spellAwake.Contains(sf.Key);   // a box already typed in stays checked after a redraw
            sf.Field = f;
            sf.Size = inp.FontSize;
        }

        void SpellingUpdate()
        {
            if (spellMenu == null || Time.frameCount == spellMenuFrame) return;   // the right-click that opened it is not a click away
            bool overMenu = RectTransformUtility.RectangleContainsScreenPoint(Rt(spellMenu), Input.mousePosition, null);
            if (Input.GetKeyDown("escape") || Math.Abs(Input.mouseScrollDelta.y) > 0.01f
                || ((Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) && !overMenu)) CloseSpellMenu();
        }

        void CloseSpellMenu()
        {
            if (spellMenu != null) Destroy(spellMenu);
            spellMenu = null;
        }

        static readonly Color MenuBg = new Color(1, 1, 1, 1), MenuEdge = new Color(0.62f, 0.62f, 0.62f, 1), MenuHover = new Color(0.86f, 0.91f, 1f, 1),
            MenuText = new Color(0.1f, 0.1f, 0.1f, 1), MenuGrey = new Color(0.45f, 0.45f, 0.45f, 1), MenuLine = new Color(0.85f, 0.85f, 0.85f, 1);

        /// <summary>The right-click menu on a flagged word: suggestions first, then Add to dictionary and Ignore.</summary>
        internal void OpenSpellMenu(SpellField sf, Misspelling m, Vector2 screen)
        {
            CloseSpellMenu();
            var sc = speller;
            if (sc == null || sf == null || sf.Field == null || windowRt == null) return;
            string text = sf.Field.text ?? "";
            if (m.Start < 0 || m.Start + m.Length > text.Length) return;
            string word = text.Substring(m.Start, m.Length);
            var sugg = sc.Suggest(word, 5);

            float fs = 14 * Mathf.Max(0.8f, Zoom), rowH = fs * 1.75f, padX = fs * 0.9f;
            var rows = new List<KeyValuePair<string, Action>>();
            foreach (var s in sugg) { var rep = s; rows.Add(new KeyValuePair<string, Action>("<b>" + TextItem.Escape(s) + "</b>", () => sf.Replace(m, rep))); }
            if (sugg.Count == 0) rows.Add(new KeyValuePair<string, Action>("<color=#777777>No suggestions</color>", null));
            rows.Add(new KeyValuePair<string, Action>(null, null));   // separator
            rows.Add(new KeyValuePair<string, Action>("Add \"" + TextItem.Escape(word) + "\" to dictionary", () => { sc.AddUserWord(word); sf.Recheck(); }));
            rows.Add(new KeyValuePair<string, Action>("Ignore", () => { sc.Ignore(word); sf.Recheck(); }));

            float w = fs * 10;
            foreach (var r in rows) if (r.Key != null) w = Mathf.Max(w, System.Text.RegularExpressions.Regex.Replace(r.Key, "<[^>]*>", "").Length * fs * 0.56f + padX * 2);
            float h = 6;
            foreach (var r in rows) h += r.Key == null ? 7 : rowH;

            spellMenu = New("spellmenu", windowRt);
            var edge = spellMenu.AddComponent<Image>();
            edge.color = MenuEdge;
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(windowRt, screen, null, out local);
            var wr = windowRt.rect;
            float x = local.x - wr.x, y = wr.y + wr.height - local.y;
            x = Mathf.Clamp(x, 0, Mathf.Max(0, wr.width - w - 2));
            if (y + h > wr.height) y = Mathf.Max(0, y - h);
            TopLeft(Rt(spellMenu), x, y, w + 2, h + 2);
            var inner = New("bg", spellMenu.transform);
            Stretch(Rt(inner), 1, 1, 1, 1);
            inner.AddComponent<Image>().color = MenuBg;

            float cy = 3;
            foreach (var r in rows)
            {
                if (r.Key == null)
                {
                    var line = New("sep", inner.transform);
                    TopLeft(Rt(line), 6, cy + 3, w - 12, 1);
                    line.AddComponent<Image>().color = MenuLine;
                    cy += 7;
                    continue;
                }
                var row = New("item", inner.transform);
                TopLeft(Rt(row), 0, cy, w, rowH);
                var img = row.AddComponent<Image>();
                img.color = MenuBg;
                var txGo = New("label", row.transform);
                Stretch(Rt(txGo), padX, 0, padX * 0.5f, 0);
                var tx = txGo.AddComponent<TextMeshProUGUI>();
                tx.font = Font("sans");
                tx.fontSize = fs;
                tx.richText = true;
                tx.enableWordWrapping = false;
                tx.alignment = TextAlignmentOptions.MidlineLeft;
                tx.color = r.Value != null ? MenuText : MenuGrey;
                tx.text = r.Key;
                tx.raycastTarget = false;
                if (r.Value != null)
                {
                    var h2 = row.AddComponent<ClickHandler>();
                    h2.Target = img; h2.Normal = MenuBg; h2.Hover = MenuHover;
                    var act = r.Value;
                    h2.OnClick = () => { CloseSpellMenu(); try { act(); } catch (Exception e) { Debug.LogWarning("[KerbalNews] Spell menu: " + e.Message); } };
                }
                cy += rowH;
            }
            spellMenu.transform.SetAsLastSibling();
            spellMenuFrame = Time.frameCount;
        }

        internal bool SpellMenuOpen { get { return spellMenu != null; } }
    }

    /// <summary>One text box's spell check: finds the misspelled words when the text changes and draws their underlines.</summary>
    public class SpellField : MonoBehaviour
    {
        public Browser Owner;
        public TMP_InputField Field;
        public float Size = 16;
        public string Key;
        public bool Awake0;

        string checkedText;
        int checkedSkip = -2;
        bool drawPending, forceCheck;
        float lastEdit;
        Vector2 drawnSize;
        int pendingCaret = -1;
        bool awake;
        List<Misspelling> bad = new List<Misspelling>();
        readonly List<GameObject> marks = new List<GameObject>();
        // Each flagged word's boxes in the text's own coordinates, for the right-click test.
        readonly List<KeyValuePair<Misspelling, Rect>> hits = new List<KeyValuePair<Misspelling, Rect>>();

        static Texture2D wave;

        /// <summary>A small red zigzag that repeats along the word: the familiar squiggly underline.</summary>
        static Texture2D Wave()
        {
            if (wave != null) return wave;
            const int W = 8, H = 4;
            var px = new Color32[W * H];
            var red = new Color32(222, 40, 32, 255);
            int[] ys = { 0, 1, 2, 3, 3, 2, 1, 0 };
            for (int x = 0; x < W; x++)
            {
                px[ys[x] * W + x] = red;
                int next = ys[(x + 1) % W];   // a softer pixel joins each step to the next, so the line is unbroken
                if (next != ys[x]) px[next * W + x] = new Color32(222, 40, 32, 140);
            }
            wave = new Texture2D(W, H, TextureFormat.RGBA32, false);
            wave.wrapMode = TextureWrapMode.Repeat;
            wave.filterMode = FilterMode.Bilinear;
            wave.SetPixels32(px);
            wave.Apply();
            return wave;
        }

        /// <summary>Checks again now (after Add to dictionary or Ignore).</summary>
        public void Recheck() { forceCheck = true; }

        /// <summary>Puts a suggestion in place of the flagged word and returns the caret to just after it.</summary>
        public void Replace(Misspelling m, string with)
        {
            if (Field == null) return;
            string t = Field.text ?? "";
            if (m.Start < 0 || m.Start + m.Length > t.Length) return;
            Field.text = t.Substring(0, m.Start) + with + t.Substring(m.Start + m.Length);
            pendingCaret = m.Start + with.Length;
            Field.onFocusSelectAll = false;   // or focusing again would select the whole text
            Field.ActivateInputField();
            forceCheck = true;
        }

        void Update()
        {
            if (Field == null || Owner == null) return;
            if (Input.GetMouseButtonDown(1) && hits.Count > 0 && Field.textViewport != null
                && RectTransformUtility.RectangleContainsScreenPoint(Field.textViewport, Input.mousePosition, null))
            {
                Vector2 p;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(Field.textComponent.rectTransform, Input.mousePosition, null, out p))
                    foreach (var h in hits)
                        if (p.x >= h.Value.x && p.x <= h.Value.x + h.Value.width && p.y >= h.Value.y && p.y <= h.Value.y + h.Value.height)
                        {
                            Owner.OpenSpellMenu(this, h.Key, Input.mousePosition);
                            break;
                        }
            }
        }

        void LateUpdate()
        {
            if (Field == null) return;
            // Nothing at all happens for a box until the player clicks into it: no word list, no check, no underlines.
            // A page with many boxes (Settings) costs nothing extra to show.
            if (!awake)
            {
                if (!Field.isFocused && !Awake0) return;
                awake = true;
                if (Key != null && Owner != null) Owner.spellAwake.Add(Key);
                if (Owner != null) Owner.AddGameWords();
            }
            else if (Field.isFocused && Owner != null) Owner.AddGameWords();   // throttled to every 30 s inside
            if (pendingCaret >= 0 && Field.isFocused)
            {
                Field.caretPosition = Math.Min(pendingCaret, (Field.text ?? "").Length);
                Field.onFocusSelectAll = true;
                pendingCaret = -1;
            }
            var sc = Browser.Speller;
            if (sc == null) { if (marks.Count > 0) { ClearMarks(); bad.Clear(); checkedText = null; } return; }

            string t = Field.text ?? "";
            float now = Time.realtimeSinceStartup;
            if (t != checkedText && checkedText != null) lastEdit = now;
            // The word being typed right now is left alone until the caret leaves it or typing pauses.
            int skip = Field.isFocused && now - lastEdit < 1.2f ? Field.caretPosition : -1;
            if (forceCheck || t != checkedText || skip != checkedSkip)
            {
                forceCheck = false;
                var found = sc.Check(t, skip);
                if (t != checkedText || !Same(found, bad)) drawPending = true;
                bad = found;
                checkedText = t;
                checkedSkip = skip;
            }
            var tx = Field.textComponent;
            if (tx == null) return;
            var size = tx.rectTransform.rect.size;
            if ((size - drawnSize).sqrMagnitude > 0.25f) drawPending = true;
            if (drawPending) Draw(tx, t);
        }

        static bool Same(List<Misspelling> a, List<Misspelling> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i].Start != b[i].Start || a[i].Length != b[i].Length) return false;
            return true;
        }

        void ClearMarks()
        {
            foreach (var g in marks) if (g != null) Destroy(g);
            marks.Clear();
            hits.Clear();
        }

        void Draw(TMP_Text tx, string t)
        {
            // The input field copies its text into the text component during its own update; wait until it has.
            string shown = tx.text ?? "";
            if (!shown.StartsWith(t, StringComparison.Ordinal)) return;
            drawPending = false;
            drawnSize = tx.rectTransform.rect.size;
            ClearMarks();
            if (bad.Count == 0) return;
            tx.ForceMeshUpdate();
            var info = tx.textInfo;
            if (info == null || info.characterInfo == null) return;
            var rt = tx.rectTransform;
            float thick = Mathf.Max(2f, Size * 0.16f);
            var tex = Wave();
            foreach (var m in bad)
            {
                // A word can wrap across lines in a big box: one underline per line it sits on.
                int line = -1; float x0 = 0, x1 = 0, baseY = 0, top = 0;
                for (int i = 0; i < info.characterCount && i < info.characterInfo.Length; i++)
                {
                    var ci = info.characterInfo[i];
                    if (ci.index < m.Start || ci.index >= m.Start + m.Length) continue;
                    if (ci.lineNumber != line)
                    {
                        if (line >= 0) Mark(rt, m, x0, x1, baseY, top, thick, tex);
                        line = ci.lineNumber; x0 = ci.bottomLeft.x; x1 = ci.topRight.x; baseY = ci.baseLine; top = ci.topRight.y;
                    }
                    else { x0 = Mathf.Min(x0, ci.bottomLeft.x); x1 = Mathf.Max(x1, ci.topRight.x); top = Mathf.Max(top, ci.topRight.y); }
                }
                if (line >= 0) Mark(rt, m, x0, x1, baseY, top, thick, tex);
            }
        }

        void Mark(RectTransform parent, Misspelling m, float x0, float x1, float baseY, float top, float thick, Texture2D tex)
        {
            if (x1 - x0 < 1) return;
            var go = new GameObject("spell", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = parent.pivot;   // child positions are then in the text's own coordinates
            r.pivot = new Vector2(0, 1);
            float y = baseY - Size * 0.06f;
            r.anchoredPosition = new Vector2(x0, y);
            r.sizeDelta = new Vector2(x1 - x0, thick);
            var img = go.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            // Repeat the zigzag every 2.2 underline-heights, so it looks the same at any size.
            img.uvRect = new Rect(0, 0, (x1 - x0) / (thick * 2.2f), 1);
            marks.Add(go);
            hits.Add(new KeyValuePair<Misspelling, Rect>(m, new Rect(x0, y - thick, x1 - x0, top - y + thick)));
        }

        void OnDestroy() { ClearMarks(); }
    }
}
