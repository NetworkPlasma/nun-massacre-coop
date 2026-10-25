using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NunMassacre.Multiplayer
{
    // Development aid (test driver only): writes the live UI hierarchy and a rendered frame,
    // including screen-space overlay canvases, so menu work can be checked in batch mode.
    internal static class UiInspector
    {
        internal static void DumpHierarchy(string path)
        {
            var report = new StringBuilder();
            foreach (var canvas in Resources.FindObjectsOfTypeAll<Canvas>().Where(c => c && c.gameObject.scene.IsValid() && c.isRootCanvas))
            {
                report.AppendLine("CANVAS " + GraphDump.Path(canvas.transform) + " mode=" + canvas.renderMode + " sort="
                    + canvas.sortingOrder + " active=" + canvas.gameObject.activeInHierarchy);
                Write(report, canvas.transform, 1);
            }
            File.WriteAllText(path, report.ToString());
        }

        private static void Write(StringBuilder report, Transform t, int depth)
        {
            var line = new StringBuilder(new string(' ', depth * 2));
            line.Append(t.gameObject.activeSelf ? "+ " : "- ").Append(t.name);
            var rect = t as RectTransform;
            if (rect)
                line.Append(" @(" + rect.anchoredPosition.x.ToString("0") + "," + rect.anchoredPosition.y.ToString("0")
                    + ") size(" + rect.sizeDelta.x.ToString("0") + "," + rect.sizeDelta.y.ToString("0") + ") anchor("
                    + rect.anchorMin + "-" + rect.anchorMax + ") pivot" + rect.pivot);
            foreach (var c in t.GetComponents<Component>())
            {
                if (!c || c is Transform) continue;
                line.Append(" [" + c.GetType().Name);
                var text = c as Text;
                if (text)
                    line.Append(" \"" + text.text.Replace("\n", "\\n") + "\" font=" + (text.font ? text.font.name : "null")
                        + " size=" + text.fontSize + " color=" + text.color + " align=" + text.alignment);
                var image = c as Image;
                if (image) line.Append(" sprite=" + (image.sprite ? image.sprite.name : "null") + " color=" + image.color + " type=" + image.type);
                var button = c as Button;
                if (button)
                {
                    line.Append(" transition=" + button.transition + " normal=" + button.colors.normalColor + " highlight="
                        + button.colors.highlightedColor + " selected=" + button.colors.selectedColor);
                    Listeners(line, button.onClick);
                }
                var layout = c as HorizontalOrVerticalLayoutGroup;
                if (layout) line.Append(" spacing=" + layout.spacing + " pad=" + layout.padding + " align=" + layout.childAlignment);
                var tmp = c.GetType().GetProperty("text");
                if (!text && tmp != null && tmp.PropertyType == typeof(string) && c.GetType().Name.Contains("TextMeshPro"))
                    line.Append(" \"" + tmp.GetValue(c, null) + "\"");
                line.Append("]");
            }
            report.AppendLine(line.ToString());
            for (int i = 0; i < t.childCount; i++) Write(report, t.GetChild(i), depth + 1);
        }

        private static void Listeners(StringBuilder line, UnityEventBase value)
        {
            for (int i = 0; i < value.GetPersistentEventCount(); i++)
            {
                var target = value.GetPersistentTarget(i);
                line.Append(" onClick=" + (target ? target.name + "/" + target.GetType().Name : "null") + "." + value.GetPersistentMethodName(i));
            }
        }

        internal static void Screenshot(string path, int width, int height)
        {
            var target = new RenderTexture(width, height, 24);
            var output = new Texture2D(width, height, TextureFormat.RGB24, false);
            var uiCameraObject = new GameObject("NMM_UiCaptureCamera");
            var uiCamera = uiCameraObject.AddComponent<Camera>();
            var changed = new List<KeyValuePair<Canvas, Camera>>();
            var prior = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                GL.Clear(true, true, Color.black);
                foreach (var camera in Camera.allCameras.Where(c => c && c != uiCamera && c.targetTexture == null).OrderBy(c => c.depth))
                {
                    var previous = camera.targetTexture;
                    camera.targetTexture = target; camera.Render(); camera.targetTexture = previous;
                }
                uiCamera.clearFlags = CameraClearFlags.Depth;
                uiCamera.cullingMask = ~0;
                uiCamera.targetTexture = target;
                uiCamera.transform.position = new Vector3(0, -5000, 0);
                foreach (var canvas in Object.FindObjectsOfType<Canvas>().Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).OrderBy(c => c.sortingOrder))
                {
                    changed.Add(new KeyValuePair<Canvas, Camera>(canvas, canvas.worldCamera));
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = uiCamera;
                    canvas.planeDistance = 1f;
                }
                Canvas.ForceUpdateCanvases();
                uiCamera.Render();
                RenderTexture.active = target;
                output.ReadPixels(new Rect(0, 0, width, height), 0, 0); output.Apply();
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(output));
            }
            finally
            {
                foreach (var pair in changed) if (pair.Key) { pair.Key.renderMode = RenderMode.ScreenSpaceOverlay; pair.Key.worldCamera = pair.Value; }
                RenderTexture.active = prior;
                Object.Destroy(uiCameraObject); Object.Destroy(output); target.Release(); Object.Destroy(target);
            }
        }
    }
}
