using System.Linq;
using NunMassacre.Multiplayer.Gameplay;
using NunMassacre.Multiplayer.Voice;
using UnityEngine;

namespace NunMassacre.Multiplayer.Ui
{
    /// <summary>Downed/revive/spectator prompts, the hiding voice meter and the hold-Tab team roster, in the game's VCR font.</summary>
    internal sealed class CoopHud : MonoBehaviour
    {
        private static readonly Color Text = new Color(0.934f, 0.846f, 1f, 1f);
        private static readonly Color Danger = new Color(1f, 0.22f, 0.18f, 1f);
        private Plugin plugin;
        private GUIStyle style, shadow;
        private Font font;
        private float nextFontSearch;
        private Texture2D pixel, meter, feather;
        private bool meterLoaded;
        private float meterLevel, meterVelocity, meterAlpha;

        internal void Initialize(Plugin owner) { plugin = owner; }

        private void OnGUI()
        {
            long start = Perf.Begin();
            bool layered = VhsLayer.Begin(); // Under the game's VHS and CRT effects, as its own prompts are.
            try { Draw(); }
            finally { VhsLayer.End(layered); }
            Perf.End(Perf.Gui, start);
        }

        private void Draw()
        {
            if (plugin == null || Event.current.type != EventType.Repaint) return;
            var session = plugin.Session;
            if (session != null && session.WaitingForPlayers)
            {
                if (style == null || (!font && Time.unscaledTime >= nextFontSearch)) Build();
                float s = Screen.height / 720f;
                Label(new Rect(0, Screen.height - 70 * s, Screen.width, 30 * s), "WAITING FOR THE OTHER PLAYERS...", 18,
                    new Color(Text.r, Text.g, Text.b, 0.55f + 0.35f * Mathf.Sin(Time.unscaledTime * 3f)));
                return;
            }
            if (session == null || !session.Active || !session.Ready || !session.Local) return;
            var life = session.Life;
            if (style == null || (!font && Time.unscaledTime >= nextFontSearch)) Build();
            float scale = Screen.height / 720f;
            float w = Screen.width, h = Screen.height;

            switch (life.State)
            {
                case LifeState.Downed:
                {
                    Label(new Rect(0, h * 0.62f, w, 50 * scale), "YOU'RE DOWN", 34, Danger);
                    int reviver = session.ReviverOf(session.LocalId);
                    float progress = session.ReviveProgressFor(session.LocalId);
                    if (reviver != 0)
                    {
                        Label(new Rect(0, h * 0.62f + 46 * scale, w, 30 * scale), session.NameOf(reviver).ToUpperInvariant() + " IS REVIVING YOU", 20, Text);
                        Bar(new Rect(w * 0.35f, h * 0.62f + 80 * scale, w * 0.3f, 10 * scale), progress, Text);
                    }
                    else
                    {
                        Label(new Rect(0, h * 0.62f + 46 * scale, w, 30 * scale),
                            "BLEEDING OUT IN " + CoopLife.Clock(life.Bleed) + " - A TEAMMATE CAN REVIVE YOU", 20, Text);
                        Bar(new Rect(w * 0.35f, h * 0.62f + 80 * scale, w * 0.3f, 10 * scale), life.Bleed / CoopLife.BleedSeconds(), Danger);
                    }
                    Label(new Rect(0, h * 0.62f + 100 * scale, w, 26 * scale), "HOLD JUMP TO GIVE UP", 14, new Color(Text.r, Text.g, Text.b, 0.6f));
                    break;
                }
                case LifeState.Dying:
                    Label(new Rect(0, h * 0.62f, w, 50 * scale), "YOU BLED OUT", 34, Danger);
                    break;
                case LifeState.Spectating:
                {
                    string name = life.WatchingName;
                    var hint = new Color(Text.r, Text.g, Text.b, 0.65f);
                    if (life.FreeCamera)
                    {
                        Label(new Rect(0, h * 0.82f, w, 34 * scale), "OUT OF LIVES  -  FREE CAMERA", 22, Text);
                        Label(new Rect(0, h * 0.82f + 32 * scale, w, 26 * scale),
                            "MOVE AND LOOK TO FLY   -   RUN: FASTER   -   JUMP OR INTERACT: BACK TO THE PLAYERS", 14, hint);
                    }
                    else
                    {
                        Label(new Rect(0, h * 0.82f, w, 34 * scale),
                            "OUT OF LIVES" + (name.Length > 0 ? "  -  " + (life.WatchingEyes ? "THROUGH " + name.ToUpperInvariant() + "'S EYES" : "SPECTATING " + name.ToUpperInvariant()) : ""),
                            22, Text);
                        Label(new Rect(0, h * 0.82f + 32 * scale, w, 26 * scale),
                            "INTERACT: NEXT VIEW   -   JUMP: FREE CAMERA   -   YOU REJOIN IN THE NEXT LEVEL", 14, hint);
                    }
                    break;
                }
                case LifeState.Alive:
                    if (life.Reviving != 0)
                    {
                        Label(new Rect(0, h * 0.58f, w, 30 * scale), "REVIVING " + session.NameOf(life.Reviving).ToUpperInvariant(), 22, Text);
                        Bar(new Rect(w * 0.35f, h * 0.58f + 34 * scale, w * 0.3f, 10 * scale), life.ReviveProgress, Text);
                    }
                    else if (life.ReviveCandidate != 0)
                    {
                        bool kit = session.Local.inventory.EquippedItem is GrannyClone.FirstAidInteractable;
                        Label(new Rect(0, h * 0.58f, w, 30 * scale),
                            "HOLD INTERACT TO REVIVE " + session.NameOf(life.ReviveCandidate).ToUpperInvariant() + (kit ? " (FIRST AID KIT)" : ""),
                            20, Text);
                    }
                    break;
            }

            HidingMeter(session, scale);
            if (Input.GetKey(KeyCode.Tab) || RuntimeTestDriver.TabHeld) Roster(session, scale);
        }

        private void Roster(Session session, float scale)
        {
            var frames = session.Players.Values.OrderBy(p => p.id).ToList();
            int rows = frames.Count + 1;
            float rowHeight = 30 * scale, width = 520 * scale;
            float x = (Screen.width - width) / 2f, y = Screen.height * 0.25f;
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(new Rect(x - 16 * scale, y - 12 * scale, width + 32 * scale, rowHeight * rows + 24 * scale), Pixel());
            GUI.color = Color.white;
            Label(new Rect(x, y, width, rowHeight), "- TEAM -", 20, Text, TextAnchor.MiddleCenter);
            foreach (var frame in frames)
            {
                y += rowHeight;
                string status = frame.spectating ? "OUT" : frame.downed ? (frame.bleed > 0 ? "DOWN " + CoopLife.Clock(frame.bleed) : "DYING")
                    : !frame.body ? "LOADING" : frame.health >= 70 ? "OK" : frame.health >= 35 ? "HURT" : "CRITICAL";
                Color color = frame.spectating ? new Color(0.6f, 0.6f, 0.6f) : frame.downed || frame.health < 35 ? Danger : Text;
                string name = (frame.name ?? "?").ToUpperInvariant() + (frame.id == 1 ? " (HOST)" : "");
                Label(new Rect(x, y, width * 0.55f, rowHeight), name, 18, Text, TextAnchor.MiddleLeft);
                Label(new Rect(x + width * 0.55f, y, width * 0.25f, rowHeight), status, 18, color, TextAnchor.MiddleLeft);
                Label(new Rect(x + width * 0.8f, y, width * 0.2f, rowHeight), frame.body ? "LIVES " + frame.lives : "", 18, Text, TextAnchor.MiddleRight);
            }
        }

        // While hiding under a bed: your microphone level in the supplied outline. The fill turns from
        // cream to red as you get louder, fully red where the Nun hears you.
        private void HidingMeter(Session session, float scale)
        {
            var voice = session.Voice;
            bool show = session.LocalHiding && session.Life.State == LifeState.Alive && voice.Enabled.Value && voice.HasMicrophone
                && session.CoopHearing; // With ORIGINAL hearing she does not hear voices.
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            meterAlpha = Mathf.MoveTowards(meterAlpha, show ? 1f : 0f, dt * 4f);
            if (meterAlpha <= 0f) { meterLevel = meterVelocity = 0f; return; }
            float target = voice.Live ? Decibels01(voice.InputLevel) : 0f;
            meterLevel = Mathf.SmoothDamp(meterLevel, target, ref meterVelocity, target > meterLevel ? 0.07f : 0.25f, Mathf.Infinity, dt);
            float audible = Decibels01(VoiceChat.HidingThreshold);
            float heat = Mathf.Pow(Mathf.InverseLerp(0.12f, audible, meterLevel), 1.4f); // Whispers barely tint it.

            Texture2D outline = Meter();
            float height = Screen.height * 0.46f, width = height * (outline ? (float)outline.width / outline.height : 0.1314f);
            var frame = new Rect(Screen.width - width - 48f * scale, (Screen.height - height) * 0.5f, width, height);
            // The outline's interior, as fractions of voice_meter.png, inset so the fill clears the border.
            float inset = 2f * scale;
            var inside = new Rect(frame.x + frame.width * 0.5637f + inset, frame.y + frame.height * 0.0122f + inset,
                frame.width * (0.8971f - 0.5637f) - inset * 2f, frame.height * (0.9858f - 0.0122f) - inset * 2f);
            Color fill = Color.Lerp(new Color(1f, 0.99f, 0.91f), Danger, heat);
            GUI.color = new Color(fill.r, fill.g, fill.b, 0.92f * meterAlpha);
            float filled = meterLevel * inside.height, soft = Mathf.Min(filled, 10f * scale);
            GUI.DrawTexture(new Rect(inside.x, inside.yMax - filled + soft, inside.width, filled - soft), Pixel());
            GUI.DrawTexture(new Rect(inside.x, inside.yMax - filled, inside.width, soft), Feather()); // Soft top edge.
            GUI.color = new Color(1f, 1f, 1f, meterAlpha * (voice.Live ? 1f : 0.5f)); // Dim while push-to-talk is not held.
            if (outline) GUI.DrawTexture(frame, outline, ScaleMode.StretchToFill, true);
            GUI.color = Color.white;
        }

        // Opaque at the bottom, fading out at the top.
        private Texture2D Feather()
        {
            if (feather) return feather;
            feather = new Texture2D(1, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 32; y++) feather.SetPixel(0, y, new Color(1f, 1f, 1f, Mathf.SmoothStep(1f, 0f, y / 31f)));
            feather.Apply();
            return feather;
        }

        private static float Decibels01(float rms) { return Mathf.Clamp01((20f * Mathf.Log10(Mathf.Max(rms, 1e-5f)) + 50f) / 50f); } // -50 to 0 dB.

        private Texture2D Meter()
        {
            if (meterLoaded) return meter;
            meterLoaded = true;
            using (var stream = typeof(CoopHud).Assembly.GetManifestResourceStream("NMM.Ui.VoiceMeter"))
            using (var bytes = new System.IO.MemoryStream())
            {
                if (stream == null) return null;
                stream.CopyTo(bytes);
                var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(image, bytes.ToArray())) { Destroy(image); return null; }
                // Mipmapped and trilinear, so the thin outline stays smooth when drawn smaller than the art.
                meter = new Texture2D(image.width, image.height, TextureFormat.RGBA32,
                    true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
                meter.SetPixels32(image.GetPixels32());
                meter.Apply(true);
                Destroy(image);
            }
            return meter;
        }

        private void Label(Rect rect, string text, int size, Color color, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            float scale = Screen.height / 720f;
            style.fontSize = shadow.fontSize = Mathf.Max(10, Mathf.RoundToInt(size * scale));
            style.alignment = shadow.alignment = alignment;
            style.normal.textColor = color;
            shadow.normal.textColor = new Color(0f, 0f, 0f, 0.85f * color.a);
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, shadow);
            GUI.Label(rect, text, style);
        }

        // Progress drawn as the game draws its sliders (NUN VOLUME "<----------->"), in the VCR font: filled from the left.
        private void Bar(Rect rect, float value, Color color)
        {
            const int cells = 26;
            int filled = Mathf.RoundToInt(Mathf.Clamp01(value) * cells);
            Label(new Rect(rect.x, rect.center.y - rect.height * 1.5f, rect.width, rect.height * 3f),
                "<" + new string('|', filled) + new string('-', cells - filled) + ">", 18, color);
        }

        private Texture2D Pixel()
        {
            if (!pixel) { pixel = new Texture2D(1, 1); pixel.SetPixel(0, 0, Color.white); pixel.Apply(); }
            return pixel;
        }

        private void Build()
        {
            nextFontSearch = Time.unscaledTime + 2f;
            font = Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(f => f && f.name == "VCR_OSD_MONO");
            style = new GUIStyle { font = font, alignment = TextAnchor.MiddleCenter, richText = false, wordWrap = false };
            shadow = new GUIStyle(style);
        }
    }
}
