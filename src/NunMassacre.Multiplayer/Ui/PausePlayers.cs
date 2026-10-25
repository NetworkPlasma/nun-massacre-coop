using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NunMassacre.Multiplayer.Ui
{
    /// <summary>
    /// The pause menu (Escape) in a co-op game lists everyone else playing, each with how loud their voice is to you: the
    /// game's own slider (the one on its SETTINGS page), from muted to twice as loud, kept by name for later games. Beside
    /// each: the level, and whether they are down or out of lives; a name lights up red while that player talks. Built
    /// inside the menu itself, so it shakes and looks like the rest of it. Mouse, keyboard or controller: right from the
    /// menu's buttons into the list, up and down along it, left and right to change a voice, up past the first back to
    /// RESUME.
    /// </summary>
    internal sealed class PausePlayers : MonoBehaviour
    {
        private sealed class Row { public int Id; public string Player; public Slider Slider; public Text Name, Status; public Color Plain; }
        private static readonly Color Talking = new Color(0.86f, 0.18f, 0.16f); // The game's blood red: whoever is talking now.

        private Plugin plugin;
        private GameObject panel;
        private Transform menuMain, template;
        private readonly Dictionary<int, Row> rows = new Dictionary<int, Row>();
        private float nextLook;
        private bool quiet, wasRight;
        private static GameObject current;

        internal void Initialize(Plugin owner) { plugin = owner; }

        /// <summary>One of the list's sliders is selected: the menu's own navigation, which would take the selection back to
        /// its buttons, steps aside.</summary>
        internal static bool OwnsSelection
        {
            get
            {
                var events = EventSystem.current;
                var selected = events ? events.currentSelectedGameObject : null;
                return selected && current && current.activeInHierarchy && selected.transform.IsChildOf(current.transform);
            }
        }

        private void Update()
        {
            var session = plugin == null ? null : plugin.Session;
            if (session == null) return;
            if (Time.unscaledTime >= nextLook) { nextLook = Time.unscaledTime + 0.25f; Look(session); }
            if (!panel) return;
            bool show = session.Active && session.SceneName != Session.MainMenuScene && menuMain && menuMain.gameObject.activeInHierarchy && rows.Count > 0;
            if (panel.activeSelf != show) panel.SetActive(show);
            Center(show);
            if (!show) return;
            foreach (var row in rows.Values) Status(session, row);
            Navigate();
        }

        // ---------- Building and keeping the list ----------
        private void Look(Session session)
        {
            if (!menuMain || !panel)
            {
                rows.Clear();
                homes.Clear();
                menuMain = FindMenu();
                if (menuMain) Build();
                if (!panel) return;
            }
            var others = session.Players.Values.Where(p => p != null && p.id != session.LocalId).OrderBy(p => p.id).ToList();
            foreach (int gone in rows.Keys.Where(id => others.All(p => p.id != id)).ToList())
            {
                if (rows[gone].Slider) Destroy(rows[gone].Slider.gameObject);
                rows.Remove(gone);
            }
            bool changed = false;
            foreach (var frame in others)
            {
                Row row;
                if (!rows.TryGetValue(frame.id, out row) || !row.Slider)
                {
                    row = AddRow(frame.id);
                    if (row == null) return;
                    rows[frame.id] = row;
                    changed = true;
                }
                string name = session.Voice.NameOf(frame.id);
                if (row.Player != name) { row.Player = name; row.Name.text = Short(name.ToUpperInvariant(), 14); }
                // Follows the saved level (set here or elsewhere), unless it is being moved right now.
                bool held = EventSystem.current && EventSystem.current.currentSelectedGameObject == row.Slider.gameObject && Input.GetMouseButton(0);
                float saved = session.Voice.VolumeOf(name);
                if (!held && Mathf.Abs(row.Slider.value - saved) > 0.001f) { quiet = true; row.Slider.value = saved; quiet = false; }
            }
            if (changed) { Link(); Arrange(); }
        }

        private static Transform FindMenu()
        {
            // The pause menu's page: the menu canvas's MenuNavigation called Main, beside its CONTROLS screen.
            foreach (var navigation in Resources.FindObjectsOfTypeAll<MenuNavigation>())
            {
                if (!navigation || !navigation.gameObject.scene.IsValid() || navigation.name != "Main") continue;
                Transform jitter = navigation.transform.parent;
                if (!jitter || jitter.name != "Jitter Parent" || !jitter.parent || jitter.parent.name != "Menu Canvas") continue;
                if (!navigation.transform.Find("Resume")) continue;
                return navigation.transform;
            }
            return null;
        }

        private void Build()
        {
            Transform jitter = menuMain.parent, canvas = jitter.parent;
            var controls = canvas.GetComponentInChildren<ControlsMenuHandler>(true);
            template = controls ? controls.transform.Find("Holder/VideoSettings/NunVolume") : null;
            if (!template) { plugin.Log("Pause menu: no slider to copy; no player list"); return; }
            var staging = new GameObject("NMM_Staging");
            staging.SetActive(false); // Copies of the game's rows never wake with the template's data.
            panel = new GameObject("NMM_Players", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(staging.transform, false);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(BoxLeft, BoxTop); rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = Vector2.zero;
            var label = template.Find("Label") ? template.Find("Label").GetComponent<Text>() : null;
            Color ink = label ? label.color : Color.white;
            // A box as Lethal Company's player list: the tape's dark behind it, a thin frame in the menu's own colour.
            var fill = panel.GetComponent<Image>();
            fill.color = new Color(0f, 0f, 0f, 0.5f); fill.raycastTarget = false;
            frame = new[] { Bar("Top", ink), Bar("Bottom", ink), Bar("Left", ink), Bar("Right", ink),
                Bar("Under title", new Color(ink.r, ink.g, ink.b, ink.a * 0.45f)) };
            title = new GameObject("Title", typeof(RectTransform)).AddComponent<Text>();
            title.transform.SetParent(panel.transform, false);
            Put((RectTransform)title.transform, Pad + 4f, -Pad, BoxWidth - Pad, -Pad - HeaderHeight);
            title.text = "PLAYERS";
            title.alignment = TextAnchor.MiddleLeft;
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            title.raycastTarget = false;
            if (label) { title.font = label.font; title.color = ink; title.fontSize = Mathf.RoundToInt(label.fontSize * 1.35f); }
            panel.transform.SetParent(jitter, false);
            panel.SetActive(false);
            Destroy(staging);
            current = panel;
            Arrange();
            plugin.Log("Pause menu: player list added (" + canvas.gameObject.scene.name + ")");
        }
        private Text title;
        private Image[] frame;

        private Image Bar(string name, Color colour)
        {
            var bar = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
            bar.transform.SetParent(panel.transform, false);
            bar.color = colour; bar.raycastTarget = false;
            return bar;
        }

        /// <summary>A part of the box, by its edges in the box's units from its top-left corner (y down is negative).</summary>
        private static void Put(RectTransform rect, float left, float top, float right, float bottom)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(left, top);
            rect.sizeDelta = new Vector2(right - left, top - bottom);
            rect.localScale = Vector3.one;
        }

        /// <summary>The box as tall as its players (three at most), the frame round it, each player a block: name and level,
        /// the slider under them.</summary>
        private void Arrange()
        {
            if (!panel) return;
            var ordered = rows.Values.Where(r => r.Slider).OrderBy(r => r.Id).ToList();
            int shown = Mathf.Min(ordered.Count, 3);
            float height = Pad + HeaderHeight + Gap + shown * RowHeight + Pad;
            ((RectTransform)panel.transform).sizeDelta = new Vector2(BoxWidth, height);
            if (frame != null)
            {
                Put((RectTransform)frame[0].transform, 0f, 0f, BoxWidth, -Line);
                Put((RectTransform)frame[1].transform, 0f, -height + Line, BoxWidth, -height);
                Put((RectTransform)frame[2].transform, 0f, 0f, Line, -height);
                Put((RectTransform)frame[3].transform, BoxWidth - Line, 0f, BoxWidth, -height);
                Put((RectTransform)frame[4].transform, Pad, -Pad - HeaderHeight - Gap * 0.5f, BoxWidth - Pad, -Pad - HeaderHeight - Gap * 0.5f - 1f);
            }
            for (int i = 0; i < ordered.Count; i++)
            {
                var row = (RectTransform)ordered[i].Slider.transform;
                row.gameObject.SetActive(i < shown);
                float top = -Pad - HeaderHeight - Gap - i * RowHeight;
                Put(row, Pad, top, BoxWidth - Pad, top - RowHeight + 4f);
            }
        }
        // The box, in the menu canvas's units (800 x 450): right of the menu's buttons (moved left), under the title.
        private const float BoxLeft = 0.575f, BoxTop = 0.44f, BoxWidth = 300f, Pad = 10f, HeaderHeight = 26f, Gap = 10f, RowHeight = 50f, Line = 1.5f;

        private Row AddRow(int id)
        {
            if (!template || !panel) return null;
            var copy = MultiplayerSettingsPage.Clone(template, panel.transform, "NMM_Player " + id, "");
            var slider = copy.GetComponent<Slider>();
            if (!slider) { Destroy(copy.gameObject); return null; }
            slider.minValue = 0f; slider.maxValue = 2f; slider.wholeNumbers = false;
            // Name and level on top (the game's selection arrow before the name), the game's slider the width of the box below.
            Place(copy.Find("Arrow"), 0f, 0.52f, 0.06f, 1f);
            var name = copy.Find("Label") ? copy.Find("Label").GetComponent<Text>() : copy.GetComponentInChildren<Text>(true);
            if (name)
            {
                Place(name.transform, 0.07f, 0.52f, 0.7f, 1f);
                name.alignment = TextAnchor.MiddleLeft; name.horizontalOverflow = HorizontalWrapMode.Overflow;
                name.fontSize = Mathf.RoundToInt(name.fontSize * 1.15f);
            }
            var track = copy.Find("Background");
            Place(track, 0.07f, 0.02f, 1f, 0.46f);
            var dashes = track ? track.GetComponentInChildren<Text>(true) : null;
            if (dashes)
            {
                // The game's <----> track, as long as the box is wide.
                Place(dashes.transform, 0f, 0f, 1f, 1f);
                dashes.alignment = TextAnchor.MiddleCenter;
                int count = Mathf.Max(8, Mathf.FloorToInt((BoxWidth - 2f * Pad) * 0.93f * 0.93f / (dashes.fontSize * 0.6f)) - 2);
                dashes.text = "<" + new string('-', count) + ">";
            }
            var slide = copy.Find("Handle Slide Area");
            if (slide)
            {
                Place(slide, 0.07f, 0.02f, 1f, 0.46f);
                ((RectTransform)slide).offsetMin = new Vector2(10f, 0f);
                ((RectTransform)slide).offsetMax = new Vector2(-10f, 0f);
            }
            var status = new GameObject("Status", typeof(RectTransform)).AddComponent<Text>();
            status.transform.SetParent(copy, false);
            Place(status.transform, 0.62f, 0.52f, 1f, 1f);
            status.alignment = TextAnchor.MiddleRight; status.horizontalOverflow = HorizontalWrapMode.Overflow; status.raycastTarget = false;
            if (name) { status.font = name.font; status.fontSize = name.fontSize; status.color = name.color; }
            var row = new Row { Id = id, Slider = slider, Name = name, Status = status, Plain = name ? name.color : Color.white };
            slider.onValueChanged.AddListener(value =>
            {
                if (quiet || string.IsNullOrEmpty(row.Player)) return;
                plugin.Session.Voice.SetVolumeOf(row.Player, Mathf.Round(value * 20f) / 20f);
            });
            return row;
        }

        private static void Place(Transform part, float left, float bottom, float right, float top)
        {
            var rect = part as RectTransform;
            if (!rect) return;
            rect.anchorMin = new Vector2(left, bottom); rect.anchorMax = new Vector2(right, top);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        /// <summary>With the list showing, the menu's title and buttons move left, so the two sit together in the middle of
        /// the screen rather than the list hanging off to the right; back where they were without it. The buttons in the
        /// corners (CRT) stay put.</summary>
        private void Center(bool show)
        {
            var canvas = panel.transform.parent as RectTransform;
            if (!menuMain || !canvas) return;
            float shift = show ? -canvas.rect.width * MenuShift : 0f;
            foreach (Transform child in menuMain)
            {
                var part = child as RectTransform;
                if (!part || part.anchorMax.x < 0.15f || part.anchorMin.x > 0.85f) continue; // A corner button.
                float home;
                if (!homes.TryGetValue(part, out home)) homes[part] = home = part.anchoredPosition.x;
                if (Mathf.Abs(part.anchoredPosition.x - (home + shift)) > 0.01f) part.anchoredPosition = new Vector2(home + shift, part.anchoredPosition.y);
            }
        }
        private readonly Dictionary<RectTransform, float> homes = new Dictionary<RectTransform, float>(); // Where each part of the menu sits without the list.
        private const float MenuShift = 0.1f; // A tenth of the screen's width.

        /// <summary>Up and down along the list (up from the first back to RESUME); left and right are the sliders' own.</summary>
        private void Link()
        {
            var ordered = rows.Values.Where(r => r.Slider).OrderBy(r => r.Id).Select(r => r.Slider).ToList();
            Selectable resume = menuMain ? menuMain.Find("Resume").GetComponent<Selectable>() : null;
            for (int i = 0; i < ordered.Count; i++)
                ordered[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = i > 0 ? ordered[i - 1] : resume,
                    selectOnDown = i + 1 < ordered.Count ? ordered[i + 1] : null,
                };
        }

        private void Navigate()
        {
            var events = EventSystem.current;
            if (!events) return;
            var selected = events.currentSelectedGameObject;
            bool right = Input.GetKey(KeyCode.RightArrow) || HorizontalAxis() > 0.6f;
            bool pressed = right && !wasRight;
            wasRight = right;
            if (!pressed || !selected || selected.transform.parent != menuMain) return; // From the menu's own buttons only.
            var first = rows.Values.Where(r => r.Slider && r.Slider.gameObject.activeSelf).OrderBy(r => r.Id).FirstOrDefault();
            if (first != null) events.SetSelectedGameObject(first.Slider.gameObject);
        }

        private static float HorizontalAxis()
        {
            try { return Rewired.ReInput.isReady ? Rewired.ReInput.players.GetPlayer(0).GetAxis("Move Horizontal") : 0f; }
            catch { return 0f; }
        }

        private void Status(Session session, Row row)
        {
            if (!row.Status) return;
            PlayerFrame frame;
            session.Players.TryGetValue(row.Id, out frame);
            float level = row.Slider ? row.Slider.value : 1f;
            string state = frame == null ? "" : frame.spectating ? "OUT" : frame.downed ? "DOWN" : "";
            // Down or out says so (the slider still shows their level); otherwise the level.
            string text = state.Length > 0 ? state : level < 0.025f ? "MUTED" : Mathf.RoundToInt(level * 100f) + "%";
            if (row.Status.text != text) row.Status.text = text;
            if (row.Name) row.Name.color = session.Voice.IsTalking(row.Id) ? Talking : row.Plain; // Their name lights up while they talk.
        }

        private static string Short(string text, int most) { return text.Length > most ? text.Substring(0, most - 1) + "." : text; }
    }
}
