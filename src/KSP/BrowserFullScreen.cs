using UnityEngine;
using UnityEngine.UI;

namespace KerbalNews.KSP
{
    /// <summary>
    /// The title bar's full-screen button (between minimize and close): the window fills the screen, and a second
    /// press puts it back where it was. Double-clicking the title bar does the same, and dragging the title bar or
    /// an edge while full screen drops back to a normal window, like a desktop window.
    /// </summary>
    public partial class Browser
    {
        static bool fullScreen;
        static Vector2 normalPos, normalSize;
        RawImage fullIcon;
        float lastTitleClick;

        void ToggleFullScreen()
        {
            if (canvasGo == null) return;
            if (!fullScreen)
            {
                normalPos = winPos;
                normalSize = winSize;
                fullScreen = true;
                FitFullScreen();
            }
            else
            {
                fullScreen = false;
                winPos = normalPos;
                winSize = normalSize;
                ClampWindow();
            }
            UpdateFullIcon();
            wantRender = true;
        }

        /// <summary>Full screen follows the screen and UI scale if they change.</summary>
        void FitFullScreen()
        {
            float sw = Screen.width / Scale, sh = Screen.height / Scale;
            if (Mathf.Abs(winPos.x) < 0.5f && Mathf.Abs(winPos.y) < 0.5f && Mathf.Abs(winSize.x - sw) < 0.5f && Mathf.Abs(winSize.y - sh) < 0.5f) return;
            winPos = Vector2.zero;
            winSize = new Vector2(sw, sh);
            ClampWindow();
        }

        void UpdateFullIcon()
        {
            if (fullIcon == null) return;
            var tex = IconTexture(fullScreen ? "unmaximize" : "maximize");
            if (tex != null) fullIcon.texture = tex;
        }

        void TitleClicked()
        {
            float now = Time.realtimeSinceStartup;
            if (now - lastTitleClick < 0.4f) { lastTitleClick = 0; ToggleFullScreen(); }
            else lastTitleClick = now;
        }

        /// <summary>Grabbing the title bar of a full-screen window: back to the normal size, still under the mouse.</summary>
        void LeaveFullScreenByDrag()
        {
            if (!fullScreen) return;
            fullScreen = false;
            float mouseX = Input.mousePosition.x / Scale, fraction = winSize.x > 0 ? mouseX / winSize.x : 0.5f;
            winSize = normalSize;
            winPos = new Vector2(mouseX - winSize.x * fraction, 0);
            ClampWindow();
            UpdateFullIcon();
            wantRender = true;
        }
    }
}
