using UnityEngine;
using UnityEngine.UI;

namespace NunMassacre.Multiplayer.Ui
{
    /// <summary>
    /// The mod's in-game HUD under the game's own screen effects. The game draws its prompts on a canvas its "Crt Under"
    /// camera renders, and the VHS and CRT cameras after it run their effects over everything (scanlines, colour bleed,
    /// the tape's wobble). The HUD's drawing (OnGUI) goes into a texture instead of straight to the screen, and that
    /// texture is shown full screen on a canvas of the same camera: the revive bars, the hiding meter, the down and
    /// spectator prompts, the team roster and the notices get the same effects as everything else. With no such camera
    /// (a loading screen), the HUD draws straight to the screen as before.
    /// </summary>
    internal static class VhsLayer
    {
        private static RenderTexture target;
        private static Canvas canvas;
        private static RawImage image;
        private static Camera owner;
        private static int clearedFrame = -1, lookedFrame = -1;
        private static RenderTexture previous;

        /// <summary>From an OnGUI repaint: true when what is drawn next goes under the game's effects (call End after).</summary>
        internal static bool Begin()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint || !Ready()) return false;
            previous = RenderTexture.active;
            RenderTexture.active = target;
            if (clearedFrame != Time.frameCount) { clearedFrame = Time.frameCount; GL.Clear(true, true, Color.clear); }
            return true;
        }

        internal static void End(bool begun) { if (begun) RenderTexture.active = previous; }

        private static bool Ready()
        {
            if (lookedFrame != Time.frameCount)
            {
                lookedFrame = Time.frameCount;
                if (!owner || !owner.isActiveAndEnabled) Attach();
                if (canvas) canvas.gameObject.SetActive(owner && owner.isActiveAndEnabled);
            }
            if (!owner || !canvas) return false;
            if (!target || target.width != Screen.width || target.height != Screen.height)
            {
                if (target) { target.Release(); Object.Destroy(target); }
                target = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32) { name = "NMM_Hud", filterMode = FilterMode.Point };
                target.Create();
                image.texture = target;
            }
            return true;
        }

        private static void Attach()
        {
            owner = null;
            foreach (var camera in Camera.allCameras)
                if (camera && camera.name == "Crt Under") { owner = camera; break; }
            if (!owner) return;
            if (!canvas)
            {
                var go = new GameObject("NMM_HudCanvas", typeof(RectTransform), typeof(Canvas));
                Object.DontDestroyOnLoad(go);
                canvas = go.GetComponent<Canvas>();
                var picture = new GameObject("NMM_Hud", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                picture.transform.SetParent(go.transform, false);
                var rect = (RectTransform)picture.transform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
                image = picture.GetComponent<RawImage>();
                image.raycastTarget = false;
                image.texture = target;
            }
            // As the game's own prompts: drawn by that camera, just in front of it, on the layer it draws.
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = owner;
            canvas.planeDistance = Mathf.Max(owner.nearClipPlane + 0.01f, 0.2f);
            canvas.sortingOrder = 30000;
            canvas.gameObject.layer = LayerMask.NameToLayer("Player");
            canvas.transform.GetChild(0).gameObject.layer = canvas.gameObject.layer;
        }
    }
}
