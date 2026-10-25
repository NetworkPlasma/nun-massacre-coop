using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NunMassacre.Multiplayer.Ui
{
    /// <summary>
    /// A MULTIPLAYER page in the game's own SETTINGS screen (the main menu's and the pause menu's): voice chat, talk mode,
    /// push-to-talk key, microphone, microphone level with a live meter, voice sensitivity, teammate volume and whether the
    /// Nun hears voices. Built from the screen's own rows (toggles, choices, sliders), so it looks and moves like the rest.
    /// </summary>
    internal sealed class MultiplayerSettings : MonoBehaviour
    {
        private Plugin plugin;
        private float nextScan;

        internal void Initialize(Plugin owner) { plugin = owner; }

        private void Update()
        {
            if (plugin == null || plugin.Session == null || Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 0.5f;
            foreach (var handler in Resources.FindObjectsOfTypeAll<ControlsMenuHandler>())
            {
                if (!handler || !handler.gameObject.scene.IsValid()) continue;
                Transform list = handler.transform.Find("Holder/VideoSettings");
                if (!list || list.Find(EntryName)) continue;
                try { MultiplayerSettingsPage.Install(plugin, list); plugin.Log("Settings: MULTIPLAYER page added (" + handler.gameObject.scene.name + ")"); }
                catch (Exception ex) { plugin.Log("Settings: MULTIPLAYER page unavailable: " + ex.Message); }
            }
        }

        internal const string EntryName = "NMM_MultiplayerEntry";
    }

    /// <summary>How the voice settings read on screen, the same in SETTINGS and in the co-op lobby.</summary>
    internal static class VoiceText
    {
        internal static readonly KeyCode[] TalkKeys = { KeyCode.V, KeyCode.B, KeyCode.T, KeyCode.G, KeyCode.CapsLock,
            KeyCode.LeftAlt, KeyCode.Mouse3, KeyCode.Mouse4 };

        /// <summary>The microphone in use, as a player knows it. Windows names them "Microphone (2- USB Audio Device)" or
        /// "Headset Microphone (HyperX Cloud II)": the device is the part in brackets (without the number Windows puts on a
        /// second one of the same kind), in capitals, cut short with "..." past <paramref name="most"/> characters.</summary>
        /// <summary>The microphone in use, by its full name as Windows gives it, for the rows where it is chosen.</summary>
        internal static string MicrophoneName(Voice.VoiceChat voice)
        {
            string[] devices = UnityEngine.Microphone.devices;
            if (devices.Length == 0) return "NONE";
            string device = devices.Contains(voice.Device.Value) ? voice.Device.Value : devices[0];
            return device.Trim().ToUpperInvariant();
        }

        /// <summary>A value that may be long (a microphone's full name): the lettering shrinks to fit, never cut short.</summary>
        internal static void FitText(Text text)
        {
            if (!text) return;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextMaxSize = text.fontSize;
            text.resizeTextMinSize = Mathf.Max(8, text.fontSize / 2);
            text.resizeTextForBestFit = true;
        }

        internal static string TalkMode(Voice.VoiceChat voice) { return voice.Mode.Value == Voice.VoiceMode.OpenMic ? "OPEN MIC" : "PUSH TO TALK"; }

        internal static string Gate(Voice.VoiceChat voice) { return voice.Sensitivity.Value <= 0 ? "LOW" : voice.Sensitivity.Value >= 2 ? "HIGH" : "MEDIUM"; }

        internal static string Percent(float value) { return Mathf.RoundToInt(value * 100f) + "%"; }

        /// <summary>The live microphone meter: ten bars, and ON while the voice is going out (open mic: over the gate).</summary>
        internal static string Meter(Voice.VoiceChat voice)
        {
            if (!voice.Enabled.Value) return "OFF";
            if (UnityEngine.Microphone.devices.Length == 0 && !voice.HasMicrophone) return "NO MIC";
            float level = Mathf.Clamp01((20f * Mathf.Log10(Mathf.Max(voice.InputLevel, 1e-5f)) + 60f) / 60f);
            int bars = Mathf.RoundToInt(level * 10f);
            bool speaking = voice.Mode.Value == Voice.VoiceMode.OpenMic ? voice.GateOpen : Input.GetKey(voice.PushToTalkKey.Value);
            return new string('|', bars) + new string('.', 10 - bars) + (speaking ? " ON" : "");
        }

        internal static void NextKey(Voice.VoiceChat voice)
        {
            int index = Array.IndexOf(TalkKeys, voice.PushToTalkKey.Value);
            voice.PushToTalkKey.Value = TalkKeys[(index + 1) % TalkKeys.Length];
        }

        internal static string KeyName(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.Mouse3: return "MOUSE 4";
                case KeyCode.Mouse4: return "MOUSE 5";
                case KeyCode.CapsLock: return "CAPS LOCK";
                case KeyCode.LeftAlt: return "ALT";
                default: return key.ToString().ToUpperInvariant();
            }
        }
    }

    internal sealed class MultiplayerSettingsPage : MonoBehaviour
    {
        private Plugin plugin;
        private GameObject list;
        private Toggle entry, voiceToggle;
        private Button modeRow, keyRow, micRow, sensitivityRow, meterRow, backRow;
        private Slider levelSlider, volumeSlider;
        private Text levelLabel, volumeLabel;
        private float nextRefresh;
        private bool quiet; // Setting values in code: ignore the controls' change events.

        private NunMassacre.Multiplayer.Voice.VoiceChat Voice { get { return plugin.Session.Voice; } }

        internal static void Install(Plugin plugin, Transform list)
        {
            Transform holder = list.parent;
            Transform toggleTemplate = list.Find("CRT"), choiceTemplate = list.Find("Video"), sliderTemplate = list.Find("NunVolume");
            Transform titleTemplate = list.Find("Title"), entryTemplate = list.Find("AdvanceSettings"), backTemplate = list.Find("Back");
            if (!toggleTemplate || !choiceTemplate || !sliderTemplate || !titleTemplate || !entryTemplate || !backTemplate)
                throw new InvalidOperationException("settings layout differs from the supported game build");

            // Clones are made under an inactive parent so the game's own scripts on them never wake with the templates' data.
            var staging = new GameObject("NMM_Staging");
            staging.SetActive(false);
            try
            {
                var pageObject = new GameObject("NMM_MultiplayerSettings", typeof(RectTransform));
                pageObject.transform.SetParent(staging.transform, false);
                var rect = (RectTransform)pageObject.transform;
                var source = (RectTransform)list;
                rect.anchorMin = source.anchorMin;
                rect.anchorMax = source.anchorMax;
                rect.offsetMin = source.offsetMin;
                rect.offsetMax = source.offsetMax;
                rect.pivot = source.pivot;
                var sourceLayout = list.GetComponent<VerticalLayoutGroup>();
                var layout = pageObject.AddComponent<VerticalLayoutGroup>();
                if (sourceLayout)
                {
                    layout.spacing = Mathf.Max(6f, sourceLayout.spacing - 6f); // A few more rows than the game's page: a little tighter.
                    layout.padding = sourceLayout.padding; layout.childAlignment = sourceLayout.childAlignment;
                    layout.childControlWidth = sourceLayout.childControlWidth; layout.childControlHeight = sourceLayout.childControlHeight;
                    layout.childForceExpandWidth = sourceLayout.childForceExpandWidth; layout.childForceExpandHeight = sourceLayout.childForceExpandHeight;
                }
                var page = pageObject.AddComponent<MultiplayerSettingsPage>();
                page.plugin = plugin;
                page.list = list.gameObject;

                Clone(titleTemplate, pageObject.transform, "Title", "MULTIPLAYER").GetComponent<Selectable>().interactable = false;
                page.voiceToggle = ToggleRow(toggleTemplate, pageObject.transform, "VOICE CHAT", on => { page.Voice.Enabled.Value = on; });
                page.modeRow = ChoiceRow(choiceTemplate, pageObject.transform, "TALK", page.CycleMode);
                page.keyRow = ChoiceRow(choiceTemplate, pageObject.transform, "TALK KEY", page.CycleKey);
                page.micRow = ChoiceRow(choiceTemplate, pageObject.transform, "MICROPHONE", page.CycleMicrophone);
                var micName = page.micRow.transform.Find("Selection");
                if (micName) VoiceText.FitText(micName.GetComponent<Text>());
                Transform micValue = page.micRow.transform.Find("Selection");
                // A long name runs on to the right, not onto a second line.
                if (micValue && micValue.GetComponent<Text>()) micValue.GetComponent<Text>().horizontalOverflow = HorizontalWrapMode.Overflow;
                page.levelSlider = SliderRow(sliderTemplate, pageObject.transform, "MIC LEVEL", 0.25f, 4f,
                    value => { page.Voice.MicLevel.Value = Mathf.Round(value * 20f) / 20f; }, out page.levelLabel);
                page.meterRow = ChoiceRow(choiceTemplate, pageObject.transform, "MIC TEST", () => { });
                page.meterRow.interactable = false;
                page.sensitivityRow = ChoiceRow(choiceTemplate, pageObject.transform, "VOICE GATE", page.CycleSensitivity);
                page.volumeSlider = SliderRow(sliderTemplate, pageObject.transform, "VOICES", 0.25f, 2f,
                    value => { page.Voice.Volume.Value = Mathf.Round(value * 20f) / 20f; }, out page.volumeLabel);
                page.backRow = Clone(backTemplate, pageObject.transform, "Back", "BACK").GetComponent<Button>();
                page.backRow.onClick.AddListener(page.Close);

                // The way in: MULTIPLAYER, just above BACK on the game's page.
                var entryTransform = Clone(entryTemplate, staging.transform, MultiplayerSettings.EntryName, "MULTIPLAYER");
                page.entry = entryTransform.GetComponent<Toggle>();
                page.entry.isOn = false;
                page.entry.onValueChanged.AddListener(on => { if (on && !page.quiet) page.Open(); });

                entryTransform.SetParent(list, false);
                entryTransform.SetSiblingIndex(backTemplate.GetSiblingIndex());
                pageObject.transform.SetParent(holder, false);
                pageObject.transform.SetSiblingIndex(list.GetSiblingIndex() + 1);
                pageObject.SetActive(false);
                foreach (var selectable in pageObject.GetComponentsInChildren<Selectable>(true).Concat(new Selectable[] { page.entry }))
                    selectable.navigation = new Navigation { mode = Navigation.Mode.Vertical };
            }
            finally { Object.Destroy(staging); }
        }

        internal void Open()
        {
            quiet = true; entry.isOn = false; quiet = false;
            list.SetActive(false);
            gameObject.SetActive(true);
            Voice.MicTest = true; // Show the microphone level while the page is open.
            Refresh();
            Select(voiceToggle);
        }

        internal void Close()
        {
            gameObject.SetActive(false);
            list.SetActive(true);
            Select(entry);
        }

        private void OnDisable()
        {
            if (plugin != null && plugin.Session != null) Voice.MicTest = false;
        }

        private void Update()
        {
            // The game shows its own page whenever SETTINGS opens; this one then steps aside.
            if (list && list.activeSelf) { gameObject.SetActive(false); return; }
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (Time.unscaledTime >= nextRefresh) Refresh();
        }

        private void Refresh()
        {
            nextRefresh = Time.unscaledTime + 0.1f;
            var voice = Voice;
            quiet = true;
            voiceToggle.isOn = voice.Enabled.Value;
            levelSlider.value = Mathf.Clamp(voice.MicLevel.Value, 0.25f, 4f);
            volumeSlider.value = Mathf.Clamp(voice.Volume.Value, 0.25f, 2f);
            quiet = false;
            Choice(modeRow, VoiceText.TalkMode(voice));
            Choice(keyRow, VoiceText.KeyName(voice.PushToTalkKey.Value));
            keyRow.interactable = voice.Mode.Value == Multiplayer.Voice.VoiceMode.PushToTalk;
            foreach (var text in keyRow.GetComponentsInChildren<Text>(true)) { var c = text.color; c.a = keyRow.interactable ? 1f : 0.45f; text.color = c; }
            Choice(micRow, VoiceText.MicrophoneName(voice));
            Choice(sensitivityRow, VoiceText.Gate(voice));
            levelLabel.text = "LEVEL " + VoiceText.Percent(voice.MicLevel.Value);
            volumeLabel.text = "VOICES " + VoiceText.Percent(voice.Volume.Value);
            Choice(meterRow, VoiceText.Meter(voice));
        }

        private void CycleMode()
        {
            var voice = Voice;
            voice.Mode.Value = voice.Mode.Value == Multiplayer.Voice.VoiceMode.OpenMic
                ? Multiplayer.Voice.VoiceMode.PushToTalk : Multiplayer.Voice.VoiceMode.OpenMic;
            Refresh();
        }

        private void CycleKey() { VoiceText.NextKey(Voice); Refresh(); }

        private void CycleMicrophone() { Voice.CycleDevice(); Refresh(); }

        private void CycleSensitivity() { Voice.Sensitivity.Value = (Voice.Sensitivity.Value + 1) % 3; Refresh(); }

        // ---------- Rows made from the game's own ----------
        private static Toggle ToggleRow(Transform template, Transform parent, string label, Action<bool> changed)
        {
            var row = Clone(template, parent, "Toggle " + label, label);
            var toggle = row.GetComponent<Toggle>();
            toggle.group = null;
            var page = parent.GetComponent<MultiplayerSettingsPage>();
            toggle.onValueChanged.AddListener(on => { if (!page.quiet) changed(on); });
            return toggle;
        }

        private static Button ChoiceRow(Transform template, Transform parent, string label, Action pressed)
        {
            var row = Clone(template, parent, "Choice " + label, label);
            var button = row.GetComponent<Button>();
            button.onClick.AddListener(() => pressed());
            // The selection arrow is the button's graphic: the game's disabled tint would leave it showing on a row that
            // cannot be picked (the talk key with an open mic, the mic meter). Disabled rows are dimmed in Refresh instead.
            var colors = button.colors; colors.disabledColor = colors.normalColor; button.colors = colors;
            return button;
        }

        private static Slider SliderRow(Transform template, Transform parent, string label, float min, float max, Action<float> changed, out Text caption)
        {
            var row = Clone(template, parent, "Slider " + label, label);
            var slider = row.GetComponent<Slider>();
            slider.minValue = min; slider.maxValue = max; slider.wholeNumbers = false;
            var page = parent.GetComponent<MultiplayerSettingsPage>();
            slider.onValueChanged.AddListener(value => { if (!page.quiet) changed(value); });
            Transform labelTransform = row.Find("Label");
            caption = labelTransform ? labelTransform.GetComponent<Text>() : row.GetComponentInChildren<Text>(true);
            return slider;
        }

        private static void Choice(Button row, string value)
        {
            Transform selection = row.transform.Find("Selection");
            var text = selection ? selection.GetComponent<Text>() : null;
            if (text) text.text = value;
        }

        /// <summary>A copy of one of the game's rows with this caption, its game listeners and localisation removed.</summary>
        internal static Transform Clone(Transform template, Transform parent, string name, string caption)
        {
            var copy = Object.Instantiate(template.gameObject, parent, false);
            copy.name = name;
            foreach (var localizable in copy.GetComponentsInChildren<LocalizableText>(true)) Object.DestroyImmediate(localizable);
            Transform labelTransform = copy.transform.Find("Label") ?? copy.transform.Find("Text");
            var label = labelTransform ? labelTransform.GetComponent<Text>() : copy.GetComponentInChildren<Text>(true);
            if (label) { label.text = caption; label.horizontalOverflow = HorizontalWrapMode.Overflow; }
            Transform second = copy.transform.Find("Label (1)");
            if (second && second.GetComponent<Text>()) second.GetComponent<Text>().text = caption;
            var button = copy.GetComponent<Button>();
            if (button) button.onClick = new Button.ButtonClickedEvent();
            var toggle = copy.GetComponent<Toggle>();
            if (toggle) toggle.onValueChanged = new Toggle.ToggleEvent();
            var slider = copy.GetComponent<Slider>();
            if (slider) slider.onValueChanged = new Slider.SliderEvent();
            if (!copy.activeSelf) copy.SetActive(true);
            return copy.transform;
        }

        private static void Select(Selectable selectable)
        {
            if (selectable && EventSystem.current && selectable.gameObject.activeInHierarchy) EventSystem.current.SetSelectedGameObject(selectable.gameObject);
        }
    }
}
