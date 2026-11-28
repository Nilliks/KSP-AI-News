using System;
using System.Collections.Generic;
using System.Text;
using KerbalNews.Web;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KerbalNews.KSP
{
    /// <summary>
    /// Selecting page text with the mouse, like a real browser: drag across any text to highlight it, double-click
    /// for a word, triple-click for a paragraph, Ctrl+A for the whole page, and Ctrl+C copies it to the system
    /// clipboard. The selection runs across paragraphs in page order and survives live-page refreshes.
    /// Typing boxes keep their own selection, copy and paste (TextMeshPro's input field handles those).
    /// </summary>
    public partial class Browser
    {
        const string SelectLock = "KerbalNewsBrowserSelect";
        static readonly Color SelectionColor = new Color(0.2f, 0.5f, 1f, 0.33f);

        /// <summary>A place in the page's text: before character Char of drawn text Text.</summary>
        struct TextPos
        {
            public int Text, Char;
            public TextPos(int t, int c) { Text = t; Char = c; }
            public int CompareTo(TextPos o) { return Text != o.Text ? Text.CompareTo(o.Text) : Char.CompareTo(o.Char); }
        }

        TextPos selAnchor, selFocus;
        bool hasSelection, selecting, mayDrag;
        int clickCount;
        float lastClickTime;
        Vector2 pressScreen, lastClickScreen;
        Transform selectionLayer;
        readonly List<GameObject> selectionObjects = new List<GameObject>();
        readonly HashSet<TextMeshProUGUI> meshed = new HashSet<TextMeshProUGUI>();

        // What was selected before a redraw, to find it again in the new page.
        string keepUrl, keepAnchorText, keepFocusText;
        int keepAnchorNth, keepFocusNth, keepAnchorChar, keepFocusChar;

        bool SelectionEmpty { get { return !hasSelection || selAnchor.CompareTo(selFocus) == 0; } }

        static bool Ctrl { get { return Input.GetKey("left ctrl") || Input.GetKey("right ctrl") || Input.GetKey("left cmd") || Input.GetKey("right cmd"); } }

        // ------------------------------------------------------------------ input

        void SelectionInput(bool overWindow, bool typing)
        {
            if (layout == null || contentRt == null) return;
            Vector2 mouse = Input.mousePosition;
            bool overPage = overWindow && RectTransformUtility.RectangleContainsScreenPoint(viewportRt, mouse, null);

            if (Input.GetMouseButtonDown(0))
            {
                mayDrag = false;
                if (overPage && CanStartSelection(mouse))
                {
                    float now = Time.realtimeSinceStartup;
                    clickCount = now - lastClickTime < 0.4f && (mouse - lastClickScreen).sqrMagnitude < 36 ? clickCount + 1 : 1;
                    lastClickTime = now;
                    lastClickScreen = mouse;
                    pressScreen = mouse;
                    TextPos p;
                    if (HitText(mouse, out p))
                    {
                        if (clickCount == 2) SelectWord(p);
                        else if (clickCount >= 3) SelectParagraph(p.Text);
                        else { selAnchor = selFocus = p; hasSelection = false; mayDrag = true; RedrawSelection(); }
                    }
                    else { hasSelection = false; RedrawSelection(); }
                    if (clickCount == 1) mayDrag = true;
                }
                else if (overPage) { hasSelection = false; RedrawSelection(); }   // clicked into a typing box, a slider...
            }

            if (mayDrag && Input.GetMouseButton(0))
            {
                if (!selecting && (mouse - pressScreen).sqrMagnitude > 25) { selecting = true; hasSelection = true; }
                if (selecting)
                {
                    AutoScroll(mouse);
                    TextPos p;
                    if (HitText(mouse, out p) && p.CompareTo(selFocus) != 0) { selFocus = p; RedrawSelection(); }
                }
            }
            else if (mayDrag || selecting)
            {
                mayDrag = selecting = false;
                if (SelectionEmpty && clickCount == 1) { hasSelection = false; RedrawSelection(); }
            }

            if (!typing && Ctrl)
            {
                if (Input.GetKeyDown("c") && !SelectionEmpty) Copy();
                if (Input.GetKeyDown("a") && overWindow) SelectAll();
            }
            // While Ctrl is held over the window, C, A and friends are for the page: not IVA view, not yaw.
            if (overWindow && !typing && Ctrl)
                InputLockManager.SetControlLock(ControlTypes.KEYBOARDINPUT | ControlTypes.ALL_SHIP_CONTROLS | ControlTypes.CAMERAMODES | ControlTypes.STAGING, SelectLock);
            else InputLockManager.RemoveControlLock(SelectLock);
        }

        /// <summary>Presses on typing boxes, the scrollbar and the stream's sliders and picture belong to them.</summary>
        bool CanStartSelection(Vector2 mouse)
        {
            foreach (var f in fields.Values)
                if (f != null && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)f.transform, mouse, null)) return false;
            if (hBar != null && hBar.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)hBar.transform, mouse, null)) return false;
            if (spellMenu != null && RectTransformUtility.RectangleContainsScreenPoint(Rt(spellMenu), mouse, null)) return false;
            Vector2 p;
            if (!PagePoint(mouse, out p)) return false;
            foreach (var im in layout.Images)
                if (im.Src != null && im.Src.StartsWith("kb") && im.Src.Contains(":") && !im.Src.StartsWith("kbimg:") && !im.Src.StartsWith("kbshot:")
                    && p.x >= im.X && p.x <= im.X + im.W && p.y >= im.Y && p.y <= im.Y + im.H) return false;
            return true;
        }

        /// <summary>Dragging past the top or bottom of the page view scrolls it, faster the further out the mouse is.</summary>
        void AutoScroll(Vector2 mouse)
        {
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(viewportRt, mouse, null, out local)) return;
            var r = viewportRt.rect;
            float top = r.y + r.height, bottom = r.y;
            float over = local.y > top ? local.y - top : local.y < bottom ? local.y - bottom : 0;
            if (over == 0) return;
            smoothing = false;
            var pos = contentRt.anchoredPosition;
            float speed = Mathf.Clamp(Mathf.Abs(over) * 12f, 60f, 3000f) * Time.unscaledDeltaTime;
            pos.y = Mathf.Clamp(pos.y + (over > 0 ? -speed : speed), 0, MaxY);
            contentRt.anchoredPosition = pos;
        }

        // ------------------------------------------------------------------ hit testing

        /// <summary>A screen point in page layout units: x to the right, y down from the top of the page.</summary>
        bool PagePoint(Vector2 screen, out Vector2 p)
        {
            Vector2 local;
            p = Vector2.zero;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(contentRt, screen, null, out local)) return false;
            p = new Vector2(local.x, -local.y);
            return true;
        }

        TMP_TextInfo Info(TextMeshProUGUI tmp)
        {
            if (tmp == null) return null;
            if (!meshed.Contains(tmp)) { tmp.ForceMeshUpdate(); meshed.Add(tmp); }
            return tmp.textInfo;
        }

        int CharCount(int i)
        {
            var info = Info(drawnTexts[i].Key);
            return info != null ? info.characterCount : 0;
        }

        /// <summary>The text place nearest a screen point: the paragraph under it, or the nearest one, then the
        /// gap between letters closest to it on the nearest line.</summary>
        bool HitText(Vector2 screen, out TextPos pos)
        {
            pos = new TextPos(0, 0);
            Vector2 p;
            if (drawnTexts.Count == 0 || !PagePoint(screen, out p)) return false;
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < drawnTexts.Count; i++)
            {
                var t = drawnTexts[i].Value;
                if (drawnTexts[i].Key == null || t.Runs.Count == 0) continue;
                float dx = Mathf.Max(0, Mathf.Max(t.X - p.x, p.x - (t.X + t.W)));
                float dy = Mathf.Max(0, Mathf.Max(t.Y - p.y, p.y - (t.Y + t.H)));
                float score = dy * 3 + dx;   // the same line matters more than the same column
                if (score < bestScore - 0.01f) { bestScore = score; best = i; }
            }
            if (best < 0) return false;
            var item = drawnTexts[best].Value;
            var info = Info(drawnTexts[best].Key);
            if (info == null || info.characterCount == 0 || info.lineCount == 0) { pos = new TextPos(best, 0); return true; }
            // Text-local coordinates: the text's pivot is its top-left corner, y goes up.
            float lx = p.x - item.X, ly = -(p.y - item.Y);
            if (ly > info.lineInfo[0].ascender + item.FontSize && p.x < item.X + item.W) { pos = new TextPos(best, 0); return true; }
            int line = info.lineCount - 1;
            for (int l = 0; l < info.lineCount; l++)
            {
                float bottom = l + 1 < info.lineCount ? (info.lineInfo[l].descender + info.lineInfo[l + 1].ascender) / 2 : float.MinValue;
                if (ly >= bottom) { line = l; break; }
            }
            var li = info.lineInfo[line];
            int first = Mathf.Clamp(li.firstCharacterIndex, 0, info.characterCount - 1), last = Mathf.Clamp(li.lastCharacterIndex, 0, info.characterCount - 1);
            int caret = last + 1;
            for (int i = first; i <= last; i++)
            {
                var c = info.characterInfo[i];
                if (c.character == '\n') { caret = i; break; }
                if (lx < (c.origin + c.xAdvance) / 2) { caret = i; break; }
            }
            pos = new TextPos(best, Mathf.Clamp(caret, 0, info.characterCount));
            return true;
        }

        void SelectWord(TextPos p)
        {
            var info = Info(drawnTexts[p.Text].Key);
            int n = info != null ? info.characterCount : 0;
            if (n == 0) return;
            Func<int, bool> word = i => i >= 0 && i < n && (char.IsLetterOrDigit(info.characterInfo[i].character) || info.characterInfo[i].character == '\'' || info.characterInfo[i].character == '-' || info.characterInfo[i].character == '_');
            int a = Mathf.Clamp(p.Char, 0, n - 1);
            if (!word(a) && word(a - 1)) a--;
            int s = a, e = a;
            if (word(a)) { while (word(s - 1)) s--; while (word(e)) e++; }
            else e = a + 1;   // a space or a symbol: just that one
            selAnchor = new TextPos(p.Text, s);
            selFocus = new TextPos(p.Text, e);
            hasSelection = true;
            RedrawSelection();
        }

        void SelectParagraph(int text)
        {
            selAnchor = new TextPos(text, 0);
            selFocus = new TextPos(text, CharCount(text));
            hasSelection = true;
            RedrawSelection();
        }

        void SelectAll()
        {
            if (drawnTexts.Count == 0) return;
            selAnchor = new TextPos(0, 0);
            selFocus = new TextPos(drawnTexts.Count - 1, CharCount(drawnTexts.Count - 1));
            hasSelection = true;
            RedrawSelection();
        }

        // ------------------------------------------------------------------ drawing

        void ClearSelectionObjects()
        {
            // The highlight boxes live under the selection layer, which Clear destroys with the rest of the page.
            selectionObjects.Clear();
            meshed.Clear();
        }

        void RedrawSelection()
        {
            foreach (var go in selectionObjects) if (go != null) Destroy(go);
            selectionObjects.Clear();
            if (SelectionEmpty || selectionLayer == null) return;
            TextPos a = selAnchor, b = selFocus;
            if (a.CompareTo(b) > 0) { var x = a; a = b; b = x; }
            for (int ti = a.Text; ti <= b.Text && ti < drawnTexts.Count; ti++)
            {
                var item = drawnTexts[ti].Value;
                var info = Info(drawnTexts[ti].Key);
                if (info == null || info.characterCount == 0) continue;
                int from = ti == a.Text ? a.Char : 0, to = ti == b.Text ? b.Char : info.characterCount;
                for (int l = 0; l < info.lineCount; l++)
                {
                    var li = info.lineInfo[l];
                    int s = Math.Max(from, li.firstCharacterIndex), e = Math.Min(to - 1, li.lastCharacterIndex);
                    if (s > e) continue;
                    float x0 = float.MaxValue, x1 = float.MinValue;
                    for (int i = s; i <= e; i++)
                    {
                        var c = info.characterInfo[i];
                        if (c.character == '\n' || c.character == '\r') continue;
                        x0 = Mathf.Min(x0, Mathf.Min(c.origin, c.xAdvance));
                        x1 = Mathf.Max(x1, Mathf.Max(c.origin, c.xAdvance));
                    }
                    // A selection that carries on past the line's end shows a little past the last letter, like a browser.
                    if (x1 < x0) { if (e < to - 1 || to > li.lastCharacterIndex) { x0 = x1 = LineEnd(info, l); } else continue; }
                    if (to - 1 > li.lastCharacterIndex) x1 += item.FontSize * 0.3f;
                    float top = li.ascender, bottom = li.descender;
                    var go = New("sel", selectionLayer);
                    TopLeft(Rt(go), item.X + x0, item.Y - top, Mathf.Max(1, x1 - x0), Mathf.Max(1, top - bottom));
                    var img = go.AddComponent<Image>();
                    img.color = SelectionColor;
                    img.raycastTarget = false;
                    selectionObjects.Add(go);
                }
            }
        }

        static float LineEnd(TMP_TextInfo info, int line)
        {
            var li = info.lineInfo[line];
            int i = Mathf.Clamp(li.lastCharacterIndex, 0, info.characterCount - 1);
            return info.characterInfo[i].xAdvance;
        }

        // ------------------------------------------------------------------ copying

        void Copy()
        {
            string text = SelectedText();
            if (text == "") return;
            GUIUtility.systemCopyBuffer = text;
            if (statusText != null)
            {
                statusText.text = "Copied " + text.Length + " character" + (text.Length == 1 ? "" : "s");
                nextStatus = Time.realtimeSinceStartup + 1.5f;
            }
        }

        /// <summary>The selected text as plain text: line breaks between paragraphs, a blank line between ones
        /// further apart, and a tab between pieces side by side (table cells, label and value).</summary>
        string SelectedText()
        {
            if (SelectionEmpty) return "";
            TextPos a = selAnchor, b = selFocus;
            if (a.CompareTo(b) > 0) { var x = a; a = b; b = x; }
            var pieces = new List<SelectionText.Piece>();
            for (int ti = a.Text; ti <= b.Text && ti < drawnTexts.Count; ti++)
            {
                var item = drawnTexts[ti].Value;
                var info = Info(drawnTexts[ti].Key);
                if (info == null) continue;
                int from = ti == a.Text ? a.Char : 0, to = Math.Min(ti == b.Text ? b.Char : info.characterCount, info.characterCount);
                var sb = new StringBuilder();
                for (int i = from; i < to; i++) sb.Append(info.characterInfo[i].character);
                pieces.Add(new SelectionText.Piece { Text = sb.ToString(), X = item.X, Y = item.Y, W = item.W, H = item.H, FontSize = item.FontSize });
            }
            return SelectionText.Join(pieces);
        }

        // ------------------------------------------------------------------ surviving redraws

        void RememberSelection()
        {
            keepUrl = null;
            if (SelectionEmpty || selecting) { if (!selecting) hasSelection = false; return; }
            if (!Key(selAnchor, out keepAnchorText, out keepAnchorNth) || !Key(selFocus, out keepFocusText, out keepFocusNth)) { hasSelection = false; return; }
            keepAnchorChar = selAnchor.Char;
            keepFocusChar = selFocus.Char;
            keepUrl = renderedUrl;
        }

        /// <summary>A text is found again by its content and which copy of that content it is.</summary>
        bool Key(TextPos p, out string text, out int nth)
        {
            text = null; nth = 0;
            if (p.Text < 0 || p.Text >= drawnTexts.Count) return false;
            text = drawnTexts[p.Text].Value.Build(-1);
            for (int i = 0; i < p.Text; i++) if (drawnTexts[i].Value.Build(-1) == text) nth++;
            return true;
        }

        int Find(string text, int nth)
        {
            for (int i = 0; i < drawnTexts.Count; i++)
                if (drawnTexts[i].Value.Build(-1) == text && nth-- == 0) return i;
            return -1;
        }

        void RestoreSelection()
        {
            if (selecting)
            {
                // The page was redrawn mid-drag (rare: redraws wait for the mouse button). Start over cleanly.
                selecting = mayDrag = hasSelection = false;
                return;
            }
            if (keepUrl == null || keepUrl != url) { hasSelection = false; keepUrl = null; return; }
            int ai = Find(keepAnchorText, keepAnchorNth), fi = Find(keepFocusText, keepFocusNth);
            keepUrl = null;
            if (ai < 0 || fi < 0) { hasSelection = false; return; }
            selAnchor = new TextPos(ai, Math.Min(keepAnchorChar, CharCount(ai)));
            selFocus = new TextPos(fi, Math.Min(keepFocusChar, CharCount(fi)));
            hasSelection = true;
            RedrawSelection();
        }
    }

    /// <summary>The page's scroll area without drag-to-scroll: a drag on the page selects text. The wheel, keys and
    /// scrollbars still scroll.</summary>
    public class PageScrollRect : ScrollRect
    {
        public override void OnBeginDrag(PointerEventData e) { }
        public override void OnDrag(PointerEventData e) { }
        public override void OnEndDrag(PointerEventData e) { }
    }
}
