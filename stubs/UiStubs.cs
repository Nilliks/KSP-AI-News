// Compile-check stubs for uGUI and TextMeshPro, mirroring the APIs in KSP 1.12.5 (Unity 2019.4, TMP 2.x).
#pragma warning disable 0067, 0649, 0169, 0414, 0626
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.Events
{
    public class UnityEvent<T> { public void AddListener(Action<T> a) { } public void RemoveAllListeners() { } }
}

namespace UnityEngine.EventSystems
{
    public class BaseEventData { }
    public class PointerEventData : BaseEventData
    {
        public enum InputButton { Left, Right, Middle }
        public InputButton button; public Vector2 position, delta; public bool dragging; public Camera pressEventCamera;
    }
    public interface IEventSystemHandler { }
    public interface IPointerClickHandler : IEventSystemHandler { void OnPointerClick(PointerEventData e); }
    public interface IPointerEnterHandler : IEventSystemHandler { void OnPointerEnter(PointerEventData e); }
    public interface IPointerExitHandler : IEventSystemHandler { void OnPointerExit(PointerEventData e); }
    public interface IBeginDragHandler : IEventSystemHandler { void OnBeginDrag(PointerEventData e); }
    public interface IDragHandler : IEventSystemHandler { void OnDrag(PointerEventData e); }
    public class UIBehaviour : MonoBehaviour { }
}

namespace UnityEngine.UI
{
    public class Graphic : EventSystems.UIBehaviour { public Color color; public bool raycastTarget; }
    public class MaskableGraphic : Graphic { }
    public class Image : MaskableGraphic { }
    public class RawImage : MaskableGraphic { public Texture texture; }
    public class RectMask2D : EventSystems.UIBehaviour { }
    public class GraphicRaycaster : EventSystems.UIBehaviour { }
    public class CanvasScaler : EventSystems.UIBehaviour { public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize } public ScaleMode uiScaleMode; public float scaleFactor; }
    public class ScrollRect : EventSystems.UIBehaviour
    {
        public enum MovementType { Unrestricted, Elastic, Clamped }
        public bool horizontal, vertical, inertia; public MovementType movementType; public float scrollSensitivity; public RectTransform viewport, content;
    }
    public class Selectable : EventSystems.UIBehaviour { }
}

namespace TMPro
{
    [Flags] public enum FontStyles { Normal = 0, Bold = 1, Italic = 2 }
    public enum TextAlignmentOptions { TopLeft, Top, TopRight, TopJustified, MidlineLeft, MidlineRight, Center }
    public enum TextOverflowModes { Overflow, Ellipsis }
    public class TMP_FontAsset : UnityEngine.Object { }
    public static class TMP_Settings { public static TMP_FontAsset defaultFontAsset; }
    public struct TMP_LinkInfo { public string GetLinkID() { return ""; } }
    public class TMP_TextInfo { public int linkCount; public TMP_LinkInfo[] linkInfo; }
    public class TMP_Text : UnityEngine.UI.MaskableGraphic
    {
        public string text; public TMP_FontAsset font; public float fontSize, lineSpacing; public FontStyles fontStyle;
        public bool richText, enableWordWrapping; public TextAlignmentOptions alignment; public TextOverflowModes overflowMode;
        public TMP_TextInfo textInfo;
        public Vector2 GetPreferredValues(string text, float width, float height) { return Vector2.zero; }
    }
    public class TextMeshProUGUI : TMP_Text { }
    public static class TMP_TextUtilities { public static int FindIntersectingLink(TMP_Text t, Vector3 position, Camera camera) { return -1; } }
    public class TMP_InputField : UnityEngine.UI.Selectable
    {
        public enum LineType { SingleLine, MultiLineSubmit, MultiLineNewline }
        public enum ContentType { Standard, Password }
        public class SubmitEvent : UnityEngine.Events.UnityEvent<string> { }
        public RectTransform textViewport; public TMP_Text textComponent; public UnityEngine.UI.Graphic placeholder;
        public TMP_FontAsset fontAsset; public float pointSize; public bool richText, customCaretColor, isFocused; public Color caretColor;
        public LineType lineType; public ContentType contentType; public string text;
        public SubmitEvent onSubmit = new SubmitEvent();
    }
}
