using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NunMassacre.Multiplayer.Ui
{
    /// <summary>Brief notices (a player joined or left, a lost connection) in the game's VCR font.</summary>
    internal sealed class Toasts : MonoBehaviour
    {
        private sealed class Entry { public string Text; public float Until; }
        private const float Duration = 5f;
        private readonly List<Entry> entries = new List<Entry>();
        private GUIStyle style, shadow;
        private Font font;
        private float nextFontSearch;

        internal void Show(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            entries.Add(new Entry { Text = text.ToUpperInvariant(), Until = Time.unscaledTime + Duration });
            if (entries.Count > 4) entries.RemoveAt(0);
        }

        private void OnGUI()
        {
            if (entries.Count == 0 || Event.current.type != EventType.Repaint) return;
            entries.RemoveAll(e => Time.unscaledTime > e.Until);
            if (style == null || (!font && Time.unscaledTime >= nextFontSearch)) BuildStyles();
            float scale = Screen.height / 720f;
            int size = Mathf.Max(12, Mathf.RoundToInt(22f * scale));
            style.fontSize = shadow.fontSize = size;
            float y = 24f * scale;
            bool layered = VhsLayer.Begin(); // Under the game's VHS and CRT effects.
            try
            {
            foreach (var entry in entries)
            {
                float alpha = Mathf.Clamp01(entry.Until - Time.unscaledTime); // Fades over the final second.
                var rect = new Rect(28f * scale, y, Screen.width, size * 1.6f);
                shadow.normal.textColor = new Color(0f, 0f, 0f, 0.85f * alpha);
                style.normal.textColor = new Color(0.934f, 0.846f, 1f, alpha);
                GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), entry.Text, shadow);
                GUI.Label(rect, entry.Text, style);
                y += size * 1.6f;
            }
            }
            finally { VhsLayer.End(layered); }
        }

        private void BuildStyles()
        {
            nextFontSearch = Time.unscaledTime + 2f;
            font = Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(f => f && f.name == "VCR_OSD_MONO");
            style = new GUIStyle { font = font, alignment = TextAnchor.UpperLeft, richText = false, wordWrap = false };
            shadow = new GUIStyle(style);
        }
    }
}
