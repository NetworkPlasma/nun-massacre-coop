using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NunMassacre.Multiplayer.Network;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NunMassacre.Multiplayer.Ui
{
    /// <summary>
    /// Adds MULTIPLAYER to the main menu (scene "Nun Bootup") and builds the lobby panel from the
    /// menu's own widgets, so it inherits the game's font, CRT jitter and selection arrows.
    /// </summary>
    internal sealed class CoopMenu : MonoBehaviour
    {
        // Paths below "Main menu" in Nun Bootup, Windows 2022.06.08.
        private const string MainPath = "Menu Canvas OFF/Jitter Parent/Main";
        private Plugin plugin;
        private MultiplayerPanel panel;
        private int attemptedScene = -1;
        private float nextCheck;

        internal void Initialize(Plugin owner) { plugin = owner; }

        private void Update()
        {
            if (panel) { panel.Guard(); return; }
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.5f;
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != Session.MainMenuScene || scene.handle == attemptedScene) return;
            GameObject root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Main menu");
            Transform main = root ? root.transform.Find(MainPath) : null;
            if (!main) return;
            attemptedScene = scene.handle;
            try
            {
                panel = MultiplayerPanel.Install(plugin, main);
                if (plugin.Session.Active || plugin.Session.ShowLobbyOnMenu) { plugin.Session.ShowLobbyOnMenu = false; panel.Open(); }
            }
            catch (Exception ex) { plugin.Log("Main menu co-op option unavailable: " + ex); }
        }
    }

    /// <summary>
    /// The co-op screens, built from the game's own rows (the VCR arrow on the selected one, the name on the left, its
    /// value on the right) over the half-dimmed menu room. The lobby is two framed boxes side by side: who is here (and how
    /// friends join), and the game the host sets up (the mode, who is the Nun, how the Nun hears) with each player's own
    /// character and voice; START, LEAVE, MOD CREDITS and BACK along the foot, one line of description under them.
    /// </summary>
    internal sealed class MultiplayerPanel : MonoBehaviour
    {
        private static readonly Color TextColor = new Color(0.934f, 0.846f, 1f, 1f);
        private const float RowHeight = 0.058f, Gap = 0.025f, Top = 0.85f;
        private Plugin plugin;
        private GameObject main, playSettings;
        private Button entry, hostButton, startButton, leaveButton, backButton, inviteRow, characterButton, creditsButton;
        private Text leaveLabel, status, lanTitle, hostIntro, hostNote, upnpNote, joinIntro, joinTitle, localNote, playersTitle,
            settingsIntro, inviteValue, characterValue;
        private readonly Button[] lanButtons = new Button[3];
        private readonly Text[] lanValues = new Text[3];
        private readonly Network.LanDiscovery.Game[] lanGames = new Network.LanDiscovery.Game[3];
        private InputField nameField, addressField;
        private Button hearingRow;
        private Text hearingValue, hearingTitle, hearingNote;
        private GameObject nameRow, addressRow;
        // Four pages: the connect/lobby page, the character page, voice and microphone (as in SETTINGS > MULTIPLAYER) and the credits.
        private Transform buildParent, content, customizePage, creditsPage, voicePage;
        private Button voiceButton, voiceBack, voiceChatRow, voiceTalkRow, voiceKeyRow, voiceMicRow, voiceLevelRow,
            voiceTestRow, voiceGateRow, voiceVolumeRow;
        private Text voiceValue, voiceChatValue, voiceTalkValue, voiceKeyValue, voiceMicValue, voiceLevelValue,
            voiceTestValue, voiceGateValue, voiceVolumeValue, voiceStatus;
        private float nextVoiceRefresh;
        /// <summary>The column ends above the description line at the foot: a longer column packs its rows closer.</summary>
        private const float Floor = 0.17f;
        private Button creditsBack, lookRow;
        private Text lookValue;
        private string copiedInvite; // The invite code last put on the clipboard.
        private string joiningGame; // Whose game on this network is being joined (its address is never shown).
        private RawImage previewImage;
        private Players.RemotePlayers.Preview preview;
        private string message = "";
        private float nextRefresh;
        private Transform labelTemplate, buttonTemplate, valueTemplate;
        private readonly Dictionary<RectTransform, Vector2> columns = new Dictionary<RectTransform, Vector2>(); // Each row's left and right edge.
        private sealed class Box { public RectTransform Root; public Text Header; }
        private Box leftBox, rightBox;
        private readonly Button[] slots = new Button[4];
        private readonly Text[] slotValues = new Text[4];

        internal static MultiplayerPanel Install(Plugin plugin, Transform main)
        {
            Transform jitter = main.parent;
            Transform playSettings = jitter.Find("PlaySettings");
            Transform bigButton = main.Find("GoToPlaySettings");
            Transform rowButton = playSettings ? playSettings.Find("Holder (1)/Skip") : null;
            Transform label = playSettings ? playSettings.Find("Holder (1)/Difficulty/Label") : null;
            Transform valueRow = playSettings ? playSettings.Find("Holder (1)/SelectSkin") : null; // SELECT SCENARIO ...... NUN
            if (!bigButton || !rowButton || !label || !valueRow) throw new InvalidOperationException("Main menu layout differs from the supported game build.");

            // Clones are configured under an inactive parent so their Awake/OnEnable (localisation
            // and selection scripts) never run with the template's text or listeners.
            var staging = new GameObject("NMM_Staging");
            staging.SetActive(false);
            try
            {
                var panelObject = new GameObject("NMM_MultiplayerPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                panelObject.transform.SetParent(staging.transform, false);
                Stretch((RectTransform)panelObject.transform);
                var dim = playSettings.GetComponent<Image>();
                // The room dimmed as the PLAY screen dims it (a shade more: the menu camera pans past the bright windows).
                panelObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, Mathf.Max(0.6f, dim ? dim.color.a : 0f));
                var panel = panelObject.AddComponent<MultiplayerPanel>();
                panel.plugin = plugin;
                panel.main = main.gameObject;
                panel.playSettings = playSettings.gameObject;
                panel.labelTemplate = label; panel.buttonTemplate = rowButton; panel.valueTemplate = valueRow;
                panel.Build();

                var entryTransform = Clone(bigButton, staging.transform, "NMM_Multiplayer", "MULTIPLAYER");
                Text entryText = entryTransform.GetComponentInChildren<Text>(true);
                if (entryText) entryText.horizontalOverflow = HorizontalWrapMode.Overflow;
                panel.entry = entryTransform.GetComponent<Button>();
                panel.entry.onClick.AddListener(panel.Open);
                entryTransform.SetParent(main, false);
                entryTransform.SetSiblingIndex(bigButton.GetSiblingIndex() + 1);
                var layout = main.GetComponent<LayoutGroup>();
                if (!layout || !layout.enabled)
                {
                    // The buttons are placed by anchors (PLAY 0.40, SETTINGS 0.25, QUIT 0.15), not by the
                    // layout group. Move PLAY up a step and put MULTIPLAYER in its place: even spacing.
                    ShiftY((RectTransform)bigButton, 0.05f);
                    ShiftY(entryTransform, -0.05f);
                    // SETTINGS renders slightly high in its 0.2-0.3 band; centre it between MULTIPLAYER and QUIT.
                    foreach (Transform child in main)
                        if (child.name == "Controls" && child.gameObject.activeSelf) ShiftY((RectTransform)child, -0.025f);
                }
                var navigation = main.GetComponent<MenuNavigation>();
                if (navigation && navigation._buttons != null && navigation._buttons.Length > 0)
                {
                    var buttons = navigation._buttons.ToList();
                    int index = buttons.FindIndex(b => b && b.transform == bigButton);
                    buttons.Insert(index < 0 ? buttons.Count : index + 1, panel.entry);
                    navigation._buttons = buttons.ToArray();
                }

                panelObject.transform.SetParent(jitter, false);
                panelObject.transform.SetSiblingIndex(playSettings.GetSiblingIndex() + 1);
                panelObject.SetActive(false);
                plugin.Log("Main menu co-op option installed.");
                return panel;
            }
            finally { Object.Destroy(staging); }
        }

        private Transform Page(string name)
        {
            var page = new GameObject(name, typeof(RectTransform)).transform;
            page.SetParent(transform, false);
            Stretch((RectTransform)page);
            return page;
        }

        private void Build()
        {
            content = Page("NMM_Content");
            customizePage = Page("NMM_CustomizePage");
            creditsPage = Page("NMM_CreditsPage");
            buildParent = content;
            Title("- MULTIPLAYER -");
            nameField = FieldRow("YOUR NAME", plugin.PlayerName.Value, 24, InputField.ContentType.Standard, out nameRow);
            if (!IsInvite(plugin.Address.Value)) plugin.Address.Value = ""; // Only an invite code is kept, never an address.
            nameField.onEndEdit.AddListener(SaveName);
            Boxed(nameField, 0.5f, 0.98f); // Drawn as a box to type in, so it reads as one.
            // Each box says what it asks of the player.
            hostIntro = Guide("HostIntro", "BE THE HOST AND INVITE FRIENDS:");
            hostButton = Row("HOST GAME", HostGame);
            Framed(hostButton);
            hostNote = Guide("HostNote", "YOU GET AN INVITE CODE TO SEND THEM.");
            upnpNote = Guide("UpnpNote", "ONLINE NEEDS UPNP ON IN YOUR ROUTER.");
            joinIntro = Guide("JoinIntro", "PASTE YOUR FRIEND'S INVITE CODE:");
            addressField = InviteBox(out addressRow);
            lanTitle = SubLabel("LanTitle", "OR JOIN A GAME ON YOUR NETWORK:");
            for (int i = 0; i < lanButtons.Length; i++)
            {
                int slot = i;
                lanButtons[i] = ValueRow("GAME " + (i + 1), "", () => JoinLan(slot), out lanValues[i]);
            }
            // In a session: what to tell friends, and who is here.
            inviteRow = ValueRow("INVITE CODE", "", CopyInvite, out inviteValue);
            // The code alone, centred in a frame like the box friends paste it into (the line above says what it is).
            var inviteLabel = inviteRow.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t != inviteValue);
            if (inviteLabel) inviteLabel.text = "";
            Anchor(inviteValue.rectTransform, 0.08f, 0f, 0.92f, 1f);
            inviteValue.alignment = TextAnchor.MiddleCenter;
            Frame(inviteRow.transform, 0.05f, 0.95f);
            joinTitle = Guide("JoinTitle", "SEND YOUR FRIENDS THIS INVITE CODE:");
            localNote = Guide("LocalNote", "FRIENDS ON YOUR NETWORK CAN JOIN.");
            playersTitle = SubLabel("PlayersTitle", "PLAYERS");
            settingsIntro = Guide("SettingsIntro", "HOW YOUR TEAMMATES SEE AND HEAR YOU:");
            for (int i = 0; i < slots.Length; i++) slots[i] = InfoRow("SLOT " + (i + 1), out slotValues[i]);
            leftBox = MakeBox("LeftBox", "LOBBY");
            rightBox = MakeBox("RightBox", "GAME");
            startButton = Row("START GAME", StartGame);
            leaveButton = Row("LEAVE", Leave);
            leaveLabel = leaveButton.GetComponentInChildren<Text>(true);
            characterButton = ValueRow("CHARACTER", "", OpenCustomize, out characterValue);
            voiceButton = ValueRow("VOICE & MIC", "", OpenVoice, out voiceValue);
            voiceButton.name = "NMM_VOICE"; // Its label has an ampersand, awkward in a name passed about.
            // How the Nun hears: the host chooses, guests see the choice.
            hearingTitle = Guide("HearingTitle", "");
            hearingRow = ValueRow("NUN HEARING", "", ToggleHearing, out hearingValue);
            var hearingColors = hearingRow.colors; hearingColors.disabledColor = hearingColors.normalColor; hearingRow.colors = hearingColors; // Not greyed for guests.
            hearingNote = Guide("HearingNote", "");
            Anchor(voiceValue.rectTransform, 0.5f, 0f, 1f, 1f); // The microphone's full name, shrunk to fit beside the label.
            VoiceText.FitText(voiceValue);
            creditsButton = Row("MOD CREDITS", OpenCredits);
            backButton = Row("BACK", Back);
            // The game's description line, under the column (as the PLAY screen describes a difficulty).
            status = Description("Status");
            BuildCredits();
            BuildCustomize();
            BuildVoice();
        }

        // Character: what teammates see. One framed box: the character turning in it, their name to choose under them.
        private void BuildCustomize()
        {
            buildParent = customizePage;
            Title("- CHARACTER -");
            const float boxBottom = BoxBottom - 0.05f, chooser = 0.115f; // The chooser's share of the box, at its foot.
            var box = MakeBox("PreviewBox", "WHO TEAMMATES SEE");
            Anchor(box.Root, 0.3f, boxBottom, 0.7f, BoxTop);
            box.Root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f); // Dark behind the character, so they stand out.
            var area = new GameObject("NMM_PreviewArea", typeof(RectTransform)).GetComponent<RectTransform>(); // Under the heading, over the chooser.
            area.SetParent(box.Root, false);
            area.anchorMin = new Vector2(0f, chooser); area.anchorMax = Vector2.one; area.offsetMin = new Vector2(8f, 0f); area.offsetMax = new Vector2(-8f, -40f);
            var image = new GameObject("NMM_Preview", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(AspectRatioFitter));
            image.transform.SetParent(area, false);
            var fit = image.GetComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent; fit.aspectRatio = 400f / 560f; // The turntable's shape: never stretched, never cut.
            previewImage = image.GetComponent<RawImage>();
            previewImage.color = Color.white;
            previewImage.raycastTarget = false;
            // "< MIKE >": select it, or press left and right, to step through the characters.
            lookRow = ValueRow("LOOK", "", () => StepCharacter(1), out lookValue);
            lookRow.GetComponentInChildren<Text>(true).text = "";
            Anchor(lookValue.rectTransform, 0.05f, 0f, 0.95f, 1f);
            lookValue.alignment = TextAnchor.MiddleCenter;
            float chooserTop = boxBottom + (BoxTop - boxBottom) * chooser;
            Place(lookRow, 0.31f, 0.69f, chooserTop - RowHeight - 0.008f, chooserTop - 0.008f);
            var back = Row("LOOK BACK", CloseCustomize);
            back.GetComponentInChildren<Text>(true).text = "BACK";
            Place(back, 0.36f, 0.64f, 0.04f, 0.04f + RowHeight);
            Chain(lookRow, back);
            customizePage.gameObject.SetActive(false);
            buildParent = content;
        }

        // ---------- Voice and microphone ----------
        /// <summary>The same settings as SETTINGS > MULTIPLAYER, in the lobby: whether voice chat is on, open mic or push to
        /// talk (and its key), the microphone, its level with a live test meter, the voice gate, teammates' voices and whether
        /// the Nun hears you. Selecting a row changes it; left and right step the levels.</summary>
        private void BuildVoice()
        {
            voicePage = Page("NMM_VoicePage");
            buildParent = voicePage;
            Title("- VOICE & MIC -");
            var voice = plugin.Session.Voice;
            voiceChatRow = VoiceRow("VOICE CHAT", () => voice.Enabled.Value = !voice.Enabled.Value, out voiceChatValue);
            voiceTalkRow = VoiceRow("TALK",
                () => voice.Mode.Value = voice.Mode.Value == Voice.VoiceMode.OpenMic ? Voice.VoiceMode.PushToTalk : Voice.VoiceMode.OpenMic,
                out voiceTalkValue);
            voiceKeyRow = VoiceRow("TALK KEY", () => VoiceText.NextKey(voice), out voiceKeyValue);
            voiceMicRow = VoiceRow("MICROPHONE", voice.CycleDevice, out voiceMicValue);
            Anchor(voiceMicValue.rectTransform, 0.3f, 0f, 1f, 1f); // More room than other values: the microphone's full name.
            VoiceText.FitText(voiceMicValue);
            voiceLevelRow = VoiceRow("MIC LEVEL", () => StepLevel(voiceLevelRow, 1, true), out voiceLevelValue);
            voiceTestRow = InfoRow("MIC TEST", out voiceTestValue);
            columns[(RectTransform)voiceTestRow.transform] = new Vector2(0.14f, 0.86f);
            voiceGateRow = VoiceRow("VOICE GATE", () => voice.Sensitivity.Value = (voice.Sensitivity.Value + 1) % 3, out voiceGateValue);
            voiceVolumeRow = VoiceRow("TEAMMATE VOICES", () => StepLevel(voiceVolumeRow, 1, true), out voiceVolumeValue);
            voiceBack = Row("VOICE BACK", CloseVoice);
            voiceBack.GetComponentInChildren<Text>(true).text = "BACK";
            voiceStatus = Description("VoiceStatus");
            LayVoice();
            voicePage.gameObject.SetActive(false);
            buildParent = content;
        }

        private void LayVoice()
        {
            Stack(Top, new List<KeyValuePair<Component, float>>
            {
                Item(voiceChatRow), Item(voiceTalkRow), Item(voiceKeyRow), Spacer(), Item(voiceMicRow), Item(voiceLevelRow), Item(voiceTestRow),
                Spacer(), Item(voiceGateRow), Item(voiceVolumeRow), Spacer(), Item(voiceBack),
            });
        }

        private Button VoiceRow(string label, Action change, out Text value)
        {
            var row = ValueRow(label, "", () => { change(); RefreshVoice(); }, out value);
            columns[(RectTransform)row.transform] = new Vector2(0.14f, 0.86f); // Wider than the lobby's rows: a microphone's name fits.
            return row;
        }

        /// <summary>The mic level (25% to 400%) or teammates' voices (25% to 200%), a quarter at a time; selecting the row
        /// goes up and round again.</summary>
        private void StepLevel(Button row, int way, bool wrap)
        {
            var voice = plugin.Session.Voice;
            var setting = row == voiceLevelRow ? voice.MicLevel : voice.Volume;
            float most = row == voiceLevelRow ? 4f : 2f, next = Mathf.Round(setting.Value * 4f) / 4f + 0.25f * way;
            setting.Value = next > most + 0.001f ? (wrap ? 0.25f : most) : Mathf.Max(0.25f, next);
            RefreshVoice();
        }

        private void OpenVoice()
        {
            content.gameObject.SetActive(false);
            voicePage.gameObject.SetActive(true);
            plugin.Session.Voice.MicTest = true; // The meter shows the microphone while the page is open.
            RefreshVoice();
            Select(voiceMicRow);
        }

        private void CloseVoice()
        {
            plugin.Session.Voice.MicTest = false;
            voicePage.gameObject.SetActive(false);
            content.gameObject.SetActive(true);
            Refresh();
            Select(voiceButton);
        }

        private void UpdateVoice()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { CloseVoice(); return; }
            var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            bool left = Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A),
                right = Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D);
            if ((left || right) && selected && (selected == voiceLevelRow.gameObject || selected == voiceVolumeRow.gameObject))
                StepLevel(selected == voiceLevelRow.gameObject ? voiceLevelRow : voiceVolumeRow, right ? 1 : -1, false);
            if (Time.unscaledTime >= nextVoiceRefresh) RefreshVoice();
        }

        private void RefreshVoice()
        {
            nextVoiceRefresh = Time.unscaledTime + 0.1f;
            var voice = plugin.Session.Voice;
            voiceChatValue.text = voice.Enabled.Value ? "ON" : "OFF";
            voiceTalkValue.text = VoiceText.TalkMode(voice);
            voiceKeyValue.text = VoiceText.KeyName(voice.PushToTalkKey.Value);
            voiceKeyRow.gameObject.SetActive(voice.Mode.Value == Voice.VoiceMode.PushToTalk);
            voiceMicValue.text = "< " + VoiceText.MicrophoneName(voice) + " >";
            voiceLevelValue.text = "< " + VoiceText.Percent(voice.MicLevel.Value) + " >";
            voiceTestValue.text = VoiceText.Meter(voice);
            voiceGateValue.text = VoiceText.Gate(voice);
            voiceVolumeValue.text = "< " + VoiceText.Percent(voice.Volume.Value) + " >";
            LayVoice();
            voiceStatus.text = UnityEngine.Microphone.devices.Length == 0 ? "NO MICROPHONE FOUND. PLUG ONE IN, THEN SELECT MICROPHONE."
                : "SPEAK TO TEST YOUR MIC. RAISE THE MIC LEVEL IF TEAMMATES HEAR YOU TOO QUIETLY. LEFT AND RIGHT CHANGE THE LEVELS.";
        }

        // ---------- Credits ----------
        private void BuildCredits()
        {
            buildParent = creditsPage;
            Title("- MOD CREDITS -");
            // Each line, and whether a new section starts there (a breath above it).
            var lines = new[]
            {
                new { Text = "NUN MASSACRE CO-OP " + Plugin.Version + ": THE MULTIPLAYER MOD", Url = (string)null, Size = 17, Section = false },
                new { Text = "BY NETWORKPLASMA", Url = (string)null, Size = 20, Section = false },
                new { Text = "CHARACTER MODELS", Url = (string)null, Size = 20, Section = true },
                new { Text = "MIKE, MADDY, MARK AND MAVERICK BY VINRAX (CC BY 4.0)", Url = (string)null, Size = 17, Section = false },
                new { Text = "SKETCHFAB.COM/VINRAX", Url = "https://sketchfab.com/vinrax", Size = 16, Section = false },
                new { Text = "X.COM/VINRAX", Url = "https://x.com/Vinrax", Size = 16, Section = false },
                new { Text = "MOTION CAPTURE: MIXAMO", Url = (string)null, Size = 17, Section = true },
                new { Text = "RAYTRACED AUDIO", Url = (string)null, Size = 20, Section = true },
                new { Text = "POWERED BY VERCIDIUM AUDIO", Url = (string)null, Size = 17, Section = false },
                new { Text = "VERCIDIUM.COM", Url = "https://vercidium.com", Size = 16, Section = false },
                new { Text = "MOD LOADER: BEPINEX AND HARMONY", Url = (string)null, Size = 17, Section = true },
                new { Text = "NUN MASSACRE BY PUPPET COMBO", Url = (string)null, Size = 17, Section = true },
            };
            const float step = 0.052f; // A little closer than the menus' rows: the list ends above BACK.
            var links = new List<Selectable>();
            float top = Top;
            foreach (var line in lines)
            {
                if (line.Section) top -= Gap * 0.6f;
                if (line.Url == null)
                {
                    Text text = Label("Credit " + line.Text, line.Text, line.Size, TextAnchor.MiddleCenter);
                    Anchor(text.rectTransform, 0.08f, top - step, 0.92f, top);
                }
                else
                {
                    string url = line.Url;
                    var link = Row(line.Text, () => Application.OpenURL(url)); // Opens their page in the browser.
                    Place(link, 0.3f, 0.7f, top - step, top);
                    var caption = link.GetComponentInChildren<Text>(true);
                    if (caption) caption.fontSize = line.Size;
                    links.Add(link);
                }
                top -= step;
            }
            creditsBack = Row("CREDITS BACK", CloseCredits);
            creditsBack.GetComponentInChildren<Text>(true).text = "BACK";
            Place(creditsBack, 0.36f, 0.64f, 0.04f, 0.04f + RowHeight);
            links.Add(creditsBack);
            Chain(links.ToArray());
            creditsPage.gameObject.SetActive(false);
            buildParent = content;
        }

        private void OpenCredits()
        {
            content.gameObject.SetActive(false);
            creditsPage.gameObject.SetActive(true);
            Select(creditsBack);
        }

        private void CloseCredits()
        {
            creditsPage.gameObject.SetActive(false);
            content.gameObject.SetActive(true);
            Refresh();
            Select(creditsButton);
        }

        private void OpenCustomize()
        {
            content.gameObject.SetActive(false);
            customizePage.gameObject.SetActive(true);
            if (preview == null)
            {
                preview = plugin.Session.Avatars.CreatePreview(400, 560);
                previewImage.texture = preview.Texture;
            }
            RefreshCustomize();
            Select(lookRow);
        }

        private void CloseCustomize()
        {
            customizePage.gameObject.SetActive(false);
            content.gameObject.SetActive(true);
            DisposePreview();
            Refresh();
            Select(characterButton);
        }

        private void DisposePreview()
        {
            if (preview == null) return;
            previewImage.texture = null;
            preview.Dispose();
            preview = null;
        }

        private void UpdateCustomize()
        {
            if (preview != null) preview.Animate(Mathf.Min(Time.unscaledDeltaTime, 0.1f));
            var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            if (selected != lookRow.gameObject) return;
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) StepCharacter(-1);
            else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) StepCharacter(1);
        }

        private void StepCharacter(int step)
        {
            var ids = Players.RemotePlayers.CharacterIds;
            int i = Array.IndexOf(ids, Players.RemotePlayers.ValidCharacter(plugin.Character.Value));
            plugin.Character.Value = ids[(i + step + ids.Length) % ids.Length];
            RefreshCustomize();
        }

        private void RefreshCustomize()
        {
            string character = Players.RemotePlayers.ValidCharacter(plugin.Character.Value);
            int characterIndex = Array.IndexOf(Players.RemotePlayers.CharacterIds, character);
            lookValue.text = "< " + Players.RemotePlayers.CharacterNames[characterIndex] + " >";
            if (preview != null) preview.Show(character);
        }

        internal void Open()
        {
            message = "";
            main.SetActive(false);
            playSettings.SetActive(false);
            gameObject.SetActive(true);
            Refresh();
            Select(plugin.Session.Active ? (plugin.Session.IsHost && plugin.Session.Connected ? startButton : leaveButton) : (Selectable)hostButton);
        }

        /// <summary>Guests wait for the host: the game's own PLAY settings open the lobby instead.</summary>
        internal void Guard()
        {
            if (playSettings && playSettings.activeSelf && plugin.Session.Active && !plugin.Session.IsHost)
            {
                Open();
                message = "THE HOST STARTS THE GAME. YOU WILL FOLLOW AUTOMATICALLY.";
            }
        }

        private void Update()
        {
            if (customizePage.gameObject.activeSelf) { UpdateCustomize(); return; }
            if (voicePage.gameObject.activeSelf) { UpdateVoice(); return; }
            var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            if (selected == hearingRow.gameObject && (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow)
                || Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.D))) ToggleHearing();
            if (Time.unscaledTime >= nextRefresh) Refresh();
        }

        private void Refresh()
        {
            nextRefresh = Time.unscaledTime + 0.25f;
            if (customizePage.gameObject.activeSelf || creditsPage.gameObject.activeSelf || voicePage.gameObject.activeSelf) return;
            Session session = plugin.Session;
            bool active = session.Active, connected = session.Connected, host = session.IsHost;
            bool invite = host && !string.IsNullOrEmpty(session.Invite);
            nameRow.SetActive(!active || connected); addressRow.SetActive(!active); // The name can be changed in the lobby too.
            hostButton.gameObject.SetActive(!active);
            foreach (var note in new[] { hostIntro, hostNote, upnpNote, joinIntro }) note.gameObject.SetActive(!active);
            RefreshLan(!active && gameObject.activeInHierarchy);
            inviteRow.gameObject.SetActive(invite);
            if (invite) inviteValue.text = session.Invite;
            // Ready to paste to a friend.
            if (invite && copiedInvite != session.Invite) { GUIUtility.systemCopyBuffer = session.Invite; copiedInvite = session.Invite; }
            // The host is told what to send friends, or that the code is coming, or why there is none.
            joinTitle.gameObject.SetActive(host && connected);
            joinTitle.text = invite ? "SEND YOUR FRIENDS THIS INVITE CODE:"
                : session.AskingRouter ? "GETTING YOUR INVITE CODE..." : "NO CODE: TURN ON UPNP IN YOUR ROUTER.";
            localNote.gameObject.SetActive(host && connected && !invite && !session.AskingRouter);
            playersTitle.gameObject.SetActive(connected);
            settingsIntro.gameObject.SetActive(connected);
            // Character and voice belong to the lobby: once in a session, before the game starts.
            characterButton.gameObject.SetActive(connected);
            voiceButton.gameObject.SetActive(connected);
            startButton.gameObject.SetActive(host && connected);
            leaveButton.gameObject.SetActive(active);
            leaveLabel.text = host ? "STOP HOSTING" : connected ? "LEAVE" : "CANCEL";
            characterValue.text = Players.RemotePlayers.CharacterNames[Array.IndexOf(Players.RemotePlayers.CharacterIds,
                Players.RemotePlayers.ValidCharacter(plugin.Character.Value))];
            var voice = plugin.Session.Voice;
            voiceValue.text = !voice.Enabled.Value ? "OFF" : VoiceText.MicrophoneName(voice);
            bool coop = session.CoopHearing;
            foreach (var part in new Component[] { hearingTitle, hearingRow, hearingNote }) part.gameObject.SetActive(connected);
            hearingRow.interactable = host;
            hearingTitle.text = host ? "YOU CHOOSE THIS AS THE HOST:" : "THE HOST CHOOSES THIS:";
            hearingValue.text = host ? (coop ? "< CO-OP >" : "< ORIGINAL >") : (coop ? "CO-OP" : "ORIGINAL");
            hearingNote.text = coop ? "VOICES AND FOOTSTEPS CARRY TO HER." : "SHE HEARS AS IN THE GAME ALONE.";

            RefreshSlots(session, connected);
            LayOut(active, connected);

            string text;
            if (!active)
                text = !string.IsNullOrEmpty(message) ? message
                    : session.Status != "Offline" && !string.IsNullOrEmpty(session.Status) ? session.Status.ToUpperInvariant()
                    : "TYPE YOUR NAME, THEN HOST A GAME OR JOIN A FRIEND'S WITH THEIR INVITE CODE.";
            else if (!connected)
                text = joiningGame != null ? ("CONNECTING TO " + joiningGame + "'S GAME...").ToUpperInvariant()
                    : IsInvite(plugin.Address.Value) ? "CONNECTING WITH THE INVITE CODE..." : "CONNECTING...";
            else if (host)
                text = invite ? (copiedInvite == session.Invite ? "THE INVITE CODE IS COPIED: PASTE IT TO YOUR FRIENDS." : "SELECT THE INVITE CODE TO COPY IT.")
                        + " SELECT START GAME WHEN EVERYONE IS HERE."
                    : session.AskingRouter ? "GETTING YOUR INVITE CODE FROM YOUR ROUTER..."
                    : (session.OnlineStatus ?? "").ToUpperInvariant();
            else text = "YOU'RE IN. THE HOST STARTS THE GAME WHEN EVERYONE IS HERE.";
            if (connected && !string.IsNullOrEmpty(message)) text = message;
            status.text = text;
            var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            if (!selected || !selected.activeInHierarchy || !selected.transform.IsChildOf(transform))
                Select(active ? (connected && host ? startButton : leaveButton) : (Selectable)hostButton);
        }

        // ---------- The lobby's boxes ----------
        private const float BoxTop = 0.855f, BoxBottom = 0.21f, Header = 0.07f, Inner = 0.015f;

        /// <summary>A framed box as the pause menu's PLAYERS list: the tape's dark behind, a thin frame in the menu's colour,
        /// its heading at the top with a rule under it.</summary>
        private Box MakeBox(string name, string heading)
        {
            var root = new GameObject("NMM_" + name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>();
            root.SetParent(buildParent, false);
            root.SetAsFirstSibling(); // Behind the rows.
            var fill = root.GetComponent<Image>(); fill.color = new Color(0f, 0f, 0f, 0.45f); fill.raycastTarget = false;
            Bar(root, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1.5f), Vector2.zero, 1f);
            Bar(root, "Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 1.5f), 1f);
            Bar(root, "Left", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(1.5f, 0f), 1f);
            Bar(root, "Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-1.5f, 0f), Vector2.zero, 1f);
            Bar(root, "Rule", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(10f, -34f), new Vector2(-10f, -33f), 0.45f);
            var header = Label(name + "Heading", heading, 22, TextAnchor.MiddleLeft);
            header.transform.SetParent(root, false);
            var rect = header.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = new Vector2(1f, 1f); rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = new Vector2(14f, -32f); rect.offsetMax = new Vector2(-10f, -4f);
            return new Box { Root = root, Header = header };
        }

        private static void Bar(RectTransform box, string name, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax, float alpha)
        {
            var bar = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
            bar.transform.SetParent(box, false);
            bar.color = new Color(TextColor.r, TextColor.g, TextColor.b, alpha); bar.raycastTarget = false;
            var rect = bar.rectTransform; rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax;
        }

        /// <summary>Everyone here, one slot each (four at most): their name (red while they talk) with what they are (HOST,
        /// YOU), and their character.</summary>
        private void RefreshSlots(Session session, bool connected)
        {
            var roster = connected ? session.Roster() : new List<KeyValuePair<int, string[]>>();
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].gameObject.SetActive(connected);
                var label = slots[i].GetComponentInChildren<Text>(true);
                if (i >= roster.Count)
                {
                    label.text = "WAITING..."; label.color = new Color(TextColor.r, TextColor.g, TextColor.b, 0.4f);
                    slotValues[i].text = "";
                    continue;
                }
                var player = roster[i];
                string name = (player.Value[0] ?? "").ToUpperInvariant();
                var tags = new List<string>();
                if (player.Key == 1) tags.Add("HOST");
                if (player.Value[2] == "1") tags.Add("YOU");
                label.text = (name.Length > 11 ? name.Substring(0, 10) + "." : name) + (tags.Count > 0 ? " (" + string.Join(", ", tags.ToArray()) + ")" : "");
                label.color = session.Voice.IsTalking(player.Key) ? new Color(0.86f, 0.18f, 0.16f) : TextColor;
                slotValues[i].text = Players.RemotePlayers.CharacterNames[Array.IndexOf(Players.RemotePlayers.CharacterIds,
                    Players.RemotePlayers.ValidCharacter(player.Value[1]))];
            }
        }

        /// <summary>
        /// The page as it stands. Before a session: the player's name over both boxes, HOST A GAME (the host button) and JOIN
        /// A GAME (the invite code box and the games found on this network), each saying what it asks. In a session: the
        /// LOBBY (the invite code to send, for the host, then the four players) and YOUR SETTINGS (character, voice and
        /// microphone).
        /// The page's buttons along the foot. Up and down go along a box, left and right across.
        /// </summary>
        private void LayOut(bool active, bool connected)
        {
            const float leftMin = 0.04f, leftMax = 0.485f, rightMin = 0.515f, rightMax = 0.96f;
            float top = active ? BoxTop : BoxTop - RowHeight - 0.02f;
            if (!active) Anchor((RectTransform)nameRow.transform, 0.25f, BoxTop - RowHeight, 0.75f, BoxTop);
            Anchor(leftBox.Root, leftMin, BoxBottom, leftMax, top);
            Anchor(rightBox.Root, rightMin, BoxBottom, rightMax, top);
            leftBox.Header.text = connected ? "LOBBY  " + plugin.Session.Roster().Count + "/4" : active ? "CONNECTING" : "HOST A GAME";
            rightBox.Header.text = connected ? "YOUR SETTINGS" : "JOIN A GAME";
            var left = connected
                ? new List<Component> { joinTitle, inviteRow, localNote, null, playersTitle, slots[0], slots[1], slots[2], slots[3] }
                : new List<Component> { hostIntro, hostButton, hostNote, null, upnpNote, active ? leaveButton : null };
            var right = connected
                ? new List<Component> { settingsIntro, nameRow.transform, characterButton, voiceButton, null, hearingTitle, hearingRow, hearingNote }
                : new List<Component> { joinIntro, addressRow.transform, null, lanTitle, lanButtons[0], lanButtons[1], lanButtons[2] };
            var leftOrder = Fill(left, leftMin, leftMax, top);
            var rightOrder = Fill(right, rightMin, rightMax, top);
            // Along the foot: what this page can do.
            var foot = new List<Button>();
            if (connected && plugin.Session.IsHost) foot.Add(startButton);
            if (connected) foot.Add(leaveButton);
            foot.Add(creditsButton); foot.Add(backButton);
            foreach (var hidden in new[] { startButton, leaveButton })
                if (!foot.Contains(hidden) && !(left.Contains(hidden) && hidden.gameObject.activeSelf)) hidden.gameObject.SetActive(false);
            if (!connected && active) leaveButton.gameObject.SetActive(true);
            float width = 0.9f / Mathf.Max(1, foot.Count);
            for (int i = 0; i < foot.Count; i++)
            {
                foot[i].gameObject.SetActive(true);
                Anchor((RectTransform)foot[i].transform, 0.05f + i * width + 0.01f, 0.125f, 0.05f + (i + 1) * width - 0.01f, 0.125f + RowHeight);
            }
            Anchor(status.rectTransform, 0.06f, 0.015f, 0.94f, 0.11f);
            Link(leftOrder, rightOrder, foot.Cast<Selectable>().ToList());
        }

        /// <summary>Lays a box's rows down from under its heading (a null is a breath between groups); returns the ones that
        /// can be selected, top to bottom.</summary>
        private List<Selectable> Fill(List<Component> items, float xMin, float xMax, float boxTop)
        {
            float top = boxTop - Header, bottom = BoxBottom + Inner;
            var shown = items.Where(c => c == null || c.gameObject.activeSelf).ToList();
            float length = shown.Sum(c => c == null ? Gap * 0.6f : RowHeight);
            float scale = length > top - bottom ? (top - bottom) / length : 1f;
            float y = top;
            var order = new List<Selectable>();
            foreach (var item in shown)
            {
                if (item == null) { y -= Gap * 0.6f * scale; continue; }
                float height = RowHeight * scale;
                var rect = (RectTransform)item.transform;
                Anchor(rect, xMin + 0.012f, y - height, xMax - 0.018f, y);
                if (item is Text) rect.offsetMin = new Vector2(30f, 0f); // Under the rows' names, not the arrow.
                y -= height;
                var selectable = item.GetComponentInChildren<Selectable>(true);
                if (selectable && selectable.interactable) order.Add(selectable);
            }
            return order;
        }

        /// <summary>Up and down along a box (from its last row down to the foot); left and right between the boxes, to the
        /// row at the same place in the other; along the foot, left and right; up from the foot, back into the left box.</summary>
        private static void Link(List<Selectable> left, List<Selectable> right, List<Selectable> foot)
        {
            Func<List<Selectable>, int, List<Selectable>,
                Selectable> across = (from, i, to) => to.Count == 0 ? null : to[Mathf.Clamp(Mathf.RoundToInt(i * (to.Count - 1f) / Mathf.Max(1, from.Count - 1)), 0, to.Count - 1)];
            for (int i = 0; i < left.Count; i++)
                left[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnUp = i > 0 ? left[i - 1] : foot.LastOrDefault(),
                    selectOnDown = i + 1 < left.Count ? left[i + 1] : foot.FirstOrDefault(),
                    selectOnRight = across(left, i, right) };
            for (int i = 0; i < right.Count; i++)
                right[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnUp = i > 0 ? right[i - 1] : foot.LastOrDefault(),
                    selectOnDown = i + 1 < right.Count ? right[i + 1] : foot.LastOrDefault(),
                    selectOnLeft = across(right, i, left) };
            for (int i = 0; i < foot.Count; i++)
                foot[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnLeft = i > 0 ? foot[i - 1] : null, selectOnRight = i + 1 < foot.Count ? foot[i + 1] : null,
                    selectOnUp = (i < foot.Count / 2f ? left : right).LastOrDefault() ?? left.LastOrDefault() ?? right.LastOrDefault() };
        }

        private static KeyValuePair<Component, float> Item(Component c) { return new KeyValuePair<Component, float>(c, RowHeight); }
        private static KeyValuePair<Component, float> Spacer() { return new KeyValuePair<Component, float>(null, Gap); }

        /// <summary>Lays the shown rows down the column from the top; a gap only between groups that both show something. A
        /// column too long to end above the description line (more games on the network, a full lobby) packs closer.</summary>
        private void Stack(float top, List<KeyValuePair<Component, float>> items)
        {
            float length = 0f;
            bool counted = false, gapNext = false;
            foreach (var item in items)
            {
                if (item.Key == null) { if (counted) gapNext = true; continue; }
                if (!item.Key.gameObject.activeSelf) continue;
                if (gapNext) { length += Gap; gapNext = false; }
                length += item.Value; counted = true;
            }
            float scale = length > top - Floor ? (top - Floor) / length : 1f;
            float y = top;
            bool any = false, pendingGap = false;
            var order = new List<Selectable>();
            foreach (var item in items)
            {
                if (item.Key == null) { if (any) pendingGap = true; continue; }
                if (!item.Key.gameObject.activeSelf) continue;
                if (pendingGap) { y -= Gap * scale; pendingGap = false; }
                var rect = (RectTransform)item.Key.transform;
                Vector2 x;
                if (!columns.TryGetValue(rect, out x)) x = new Vector2(0.2f, 0.8f);
                Anchor(rect, x.x, y - item.Value * scale, x.y, y);
                y -= item.Value * scale;
                any = true;
                var selectable = item.Key.GetComponentInChildren<Selectable>(true);
                if (selectable && selectable.interactable) order.Add(selectable);
            }
            // Up and down go row to row in this order, top to bottom and round again.
            for (int i = 0; i < order.Count; i++)
                order[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnUp = order[(i + order.Count - 1) % order.Count], selectOnDown = order[(i + 1) % order.Count] };
        }

        private void HostGame()
        {
            SaveName(nameField.text);
            message = "";
            plugin.Session.Host(Port);
            Refresh();
        }

        private const string WrongCode = "THAT INVITE CODE IS NOT RIGHT. CHECK IT WITH THE HOST.";

        /// <summary>Joins with what is in the invite code box: an invite code, or an address (a VPN's) typed there.</summary>
        private void JoinGame()
        {
            string address = addressField.text.Trim();
            if (address.Length == 0) { message = "PASTE THE HOST'S INVITE CODE IN THE BOX FIRST."; Refresh(); return; }
            SaveName(nameField.text);
            plugin.Address.Value = IsInvite(address) ? address : "";
            message = "";
            joiningGame = null;
            IPAddress invited; int invitePort, key;
            if (InviteCode.TryParse(address, out invited, out invitePort, out key)) plugin.Session.Join(invited.ToString(), invitePort, key);
            else if (LooksLikeCode(address))
            {
                message = WrongCode;
                Refresh();
                return;
            }
            else plugin.Session.Join(address, Port);
            Refresh();
        }

        /// <summary>Sixteen letters and digits, with or without dashes: meant as an invite code, not an address.</summary>
        private static bool LooksLikeCode(string text) { return text.Replace("-", "").Replace(" ", "").Length == 16 && text.IndexOf('.') < 0; }

        /// <summary>The port in the settings file (the router forwards it; nobody needs to see it here).</summary>
        private int Port { get { int port = plugin.Port.Value; return port >= 1024 && port <= 65535 ? port : 27845; } }

        // LAN browser: games announced by hosts on this network, one click to join.
        private void RefreshLan(bool show)
        {
            var discovery = plugin.Session.Discovery;
            lanTitle.gameObject.SetActive(show);
            if (show) discovery.Listen(); else discovery.StopListening();
            var games = show ? discovery.Games(plugin.BuildHash) : new List<Network.LanDiscovery.Game>();
            lanTitle.text = games.Count == 0 ? "LOOKING FOR GAMES ON YOUR NETWORK..." : "OR JOIN A GAME ON YOUR NETWORK:";
            for (int i = 0; i < lanButtons.Length; i++)
            {
                lanGames[i] = i < games.Count ? games[i] : null;
                lanButtons[i].gameObject.SetActive(lanGames[i] != null);
                if (lanGames[i] == null) continue;
                var game = lanGames[i];
                lanButtons[i].GetComponentInChildren<Text>(true).text = (game.Name + "'S GAME").ToUpperInvariant();
                lanValues[i].text = game.Players + "/" + game.Max + "  " + (game.InGame ? "PLAYING" : "LOBBY");
            }
        }

        private void JoinLan(int slot)
        {
            var game = lanGames[slot];
            if (game == null) return;
            SaveName(nameField.text);
            message = "";
            joiningGame = game.Name;
            plugin.Session.Join(game.Address, game.Port);
            Refresh();
        }

        private void OnDisable()
        {
            if (plugin == null || plugin.Session == null) return;
            plugin.Session.Discovery.StopListening();
            if (customizePage && customizePage.gameObject.activeSelf) { customizePage.gameObject.SetActive(false); content.gameObject.SetActive(true); }
            if (creditsPage && creditsPage.gameObject.activeSelf) { creditsPage.gameObject.SetActive(false); content.gameObject.SetActive(true); }
            if (voicePage && voicePage.gameObject.activeSelf) { voicePage.gameObject.SetActive(false); content.gameObject.SetActive(true); }
            plugin.Session.Voice.MicTest = false;
            DisposePreview();
        }

        /// <summary>Host: CO-OP or ORIGINAL hearing; every guest is told.</summary>
        private void ToggleHearing()
        {
            if (!plugin.Session.IsHost) return;
            plugin.OriginalHearing.Value = !plugin.OriginalHearing.Value;
            plugin.Session.HearingChanged();
            Refresh();
        }

        private void StartGame()
        {
            gameObject.SetActive(false);
            playSettings.SetActive(true); // The game's PLAY screen: difficulty, items, intro or skip.
        }

        private static bool IsInvite(string text) { IPAddress address; int port, key; return InviteCode.TryParse(text, out address, out port, out key); }

        private void CopyInvite()
        {
            if (string.IsNullOrEmpty(plugin.Session.Invite)) return;
            GUIUtility.systemCopyBuffer = plugin.Session.Invite;
            copiedInvite = plugin.Session.Invite;
            message = "";
            Refresh();
        }

        private void Leave()
        {
            plugin.Session.Disconnect(plugin.Session.IsHost ? "Stopped hosting." : "Left the co-op game.");
            Refresh();
        }

        private void Back()
        {
            gameObject.SetActive(false);
            main.SetActive(true);
            Select(entry);
        }

        private void SaveName(string value)
        {
            string name = new string((value ?? "").Where(c => !char.IsControl(c) && c != '<' && c != '>').Take(24).ToArray()).Trim();
            if (name.Length > 0) plugin.PlayerName.Value = name;
            nameField.text = plugin.PlayerName.Value;
        }

        // ---------- The game's widgets ----------
        /// <summary>A page heading, as the game's SETTINGS headings ("- CONTROLS -").</summary>
        private void Title(string value)
        {
            Text title = Label("Title", value, 36, TextAnchor.MiddleCenter);
            Anchor(title.rectTransform, 0.1f, 0.88f, 0.9f, 0.97f);
        }

        /// <summary>The page's description line at the foot (as the PLAY screen describes a difficulty), its top fixed so a
        /// longer one runs down, never off the screen.</summary>
        private Text Description(string name)
        {
            Text text = Label(name, "", 16, TextAnchor.UpperCenter);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            Anchor(text.rectTransform, 0.12f, 0.03f, 0.88f, 0.15f);
            return text;
        }

        /// <summary>A line inside a box telling the player what it asks of them: as a small heading, but brighter.</summary>
        private Text Guide(string name, string value)
        {
            var text = SubLabel(name, value);
            text.color = new Color(TextColor.r, TextColor.g, TextColor.b, 0.85f);
            return text;
        }

        /// <summary>A small heading inside a box, over the rows it names.</summary>
        private Text SubLabel(string name, string value)
        {
            var text = Label(name, value, 14, TextAnchor.MiddleLeft);
            text.color = new Color(TextColor.r, TextColor.g, TextColor.b, 0.6f);
            VoiceText.FitText(text); // Never past the box's edge.
            return text;
        }

        /// <summary>An action rather than a setting: its name centred in a thin frame, so it reads as something to press.</summary>
        private static void Framed(Button button)
        {
            Frame(button.transform, 0.15f, 0.85f);
            var label = button.GetComponentInChildren<Text>(true);
            if (!label) return;
            label.alignment = TextAnchor.MiddleCenter;
            label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        }

        /// <summary>A thin frame across part of a row (from left to right of its width), behind what the row holds.</summary>
        private static void Frame(Transform row, float left, float right)
        {
            var frame = new GameObject("NMM_Frame", typeof(RectTransform)).GetComponent<RectTransform>();
            frame.SetParent(row, false);
            frame.SetAsFirstSibling();
            frame.anchorMin = new Vector2(left, 0.06f); frame.anchorMax = new Vector2(right, 0.94f);
            frame.offsetMin = frame.offsetMax = Vector2.zero;
            Bar(frame, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1.5f), Vector2.zero, 0.8f);
            Bar(frame, "Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 1.5f), 0.8f);
            Bar(frame, "Left", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(1.5f, 0f), 0.8f);
            Bar(frame, "Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-1.5f, 0f), Vector2.zero, 0.8f);
        }

        /// <summary>A text field drawn as a box to type in: what is typed, centred in a thin frame across the row from left to
        /// right.</summary>
        private static void Boxed(InputField field, float left, float right)
        {
            foreach (var text in new[] { field.textComponent, (Text)field.placeholder })
            {
                Anchor(text.rectTransform, left + 0.03f, 0f, right - 0.03f, 1f);
                text.alignment = TextAnchor.MiddleCenter;
            }
            Frame(field.transform, left, right);
        }

        /// <summary>
        /// The invite code box: one framed field and nothing else to press. A whole, valid code joins as soon as it is in the
        /// box (pasted or typed); a mistyped one says so. Enter joins with whatever is there, so a VPN address works too.
        /// </summary>
        private InputField InviteBox(out GameObject row)
        {
            var field = FieldRow("INVITE CODE", "", 64, InputField.ContentType.Standard, out row);
            var label = field.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t != field.textComponent && t != field.placeholder);
            if (label) label.gameObject.SetActive(false);
            Boxed(field, 0.05f, 0.95f);
            ((Text)field.placeholder).text = "INVITE CODE";
            field.onValueChanged.AddListener(value =>
            {
                string code = value.Trim();
                if (plugin.Session.Active) return;
                if (IsInvite(code)) { JoinGame(); return; }
                string said = LooksLikeCode(code) ? WrongCode : message == WrongCode ? "" : message;
                if (said != message) { message = said; Refresh(); }
            });
            field.onEndEdit.AddListener(value =>
            {
                bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
                if (enter && !plugin.Session.Active && value.Trim().Length > 0) JoinGame();
            });
            return field;
        }

        private Text Label(string name, string value, int size, TextAnchor alignment)
        {
            var copy = Clone(labelTemplate, buildParent, "NMM_" + name, value);
            var text = copy.GetComponent<Text>();
            text.fontSize = size; text.alignment = alignment; text.color = TextColor;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            return text;
        }

        /// <summary>A button, as the PLAY screen's PLAY INTRO and SKIP AHEAD.</summary>
        private Button Row(string label, UnityEngine.Events.UnityAction action)
        {
            var copy = Clone(buttonTemplate, buildParent, "NMM_" + label.Replace(' ', '_'), label);
            columns[copy] = new Vector2(0.36f, 0.64f);
            var button = copy.GetComponent<Button>();
            button.onClick.AddListener(action);
            return button;
        }

        /// <summary>A setting row, as SELECT SCENARIO ...... NUN: the name on the left, its value on the right.</summary>
        private Button ValueRow(string label, string value, UnityEngine.Events.UnityAction action, out Text valueText)
        {
            var copy = Clone(valueTemplate, buildParent, "NMM_" + label.Replace(' ', '_'), label);
            columns[copy] = new Vector2(0.2f, 0.8f);
            valueText = ValueTextOf(copy);
            valueText.text = value;
            var button = copy.GetComponent<Button>();
            button.onClick.AddListener(action);
            return button;
        }

        /// <summary>A row that only shows something (no arrow, skipped when moving up and down).</summary>
        private Button InfoRow(string label, out Text valueText)
        {
            var button = ValueRow(label, "", () => { }, out valueText);
            button.interactable = false;
            var colors = button.colors; colors.disabledColor = colors.normalColor; button.colors = colors; // No greyed arrow.
            return button;
        }

        /// <summary>
        /// A text field in a setting row: the name on the left, what is typed on the right, the arrow on it while selected.
        /// </summary>
        private InputField FieldRow(string label, string value, int limit, InputField.ContentType type, out GameObject row)
        {
            row = new GameObject("NMM_Row_" + label.Replace(' ', '_'), typeof(RectTransform));
            row.transform.SetParent(buildParent, false);
            columns[(RectTransform)row.transform] = new Vector2(0.2f, 0.8f);
            var copy = Clone(valueTemplate, row.transform, "Field", label);
            Stretch(copy);
            var button = copy.GetComponent<Button>();
            ColorBlock colors = button.colors;
            Graphic arrow = button.targetGraphic;
            if (!arrow || arrow.gameObject == copy.gameObject) arrow = copy.Find("Arrow") ? copy.Find("Arrow").GetComponent<Graphic>() : null;
            Object.DestroyImmediate(button);
            Text text = ValueTextOf(copy);
            text.supportRichText = false;
            text.text = "";
            Text placeholder = Object.Instantiate(text.gameObject, copy, false).GetComponent<Text>();
            placeholder.name = "Placeholder";
            placeholder.text = label == "YOUR NAME" ? "TYPE YOUR NAME" : "...";
            placeholder.color = new Color(TextColor.r, TextColor.g, TextColor.b, 0.35f);

            var input = copy.gameObject.AddComponent<InputField>();
            input.textComponent = text;
            input.placeholder = placeholder;
            input.characterLimit = limit;
            input.contentType = type;
            input.lineType = InputField.LineType.SingleLine;
            input.customCaretColor = true;
            input.caretColor = TextColor;
            input.caretWidth = 2;
            input.selectionColor = new Color(0.55f, 0.45f, 1f, 0.45f);
            input.transition = Selectable.Transition.ColorTint;
            input.targetGraphic = arrow; // The VCR arrow shows on the selected row, as on every game row.
            input.colors = colors;
            input.text = value;
            return input;
        }

        /// <summary>The value half of a setting row (the template's "NUN"), stretched over the row's right side.</summary>
        private static Text ValueTextOf(Transform row)
        {
            var texts = row.GetComponentsInChildren<Text>(true);
            Text label = texts.FirstOrDefault(), value = texts.Skip(1).FirstOrDefault();
            if (label) { label.horizontalOverflow = HorizontalWrapMode.Overflow; Anchor(label.rectTransform, 0.1f, 0f, 0.62f, 1f); }
            if (!value) return label;
            Anchor(value.rectTransform, 0.42f, 0f, 1f, 1f);
            value.alignment = TextAnchor.MiddleRight;
            value.horizontalOverflow = HorizontalWrapMode.Overflow;
            return value;
        }

        private static RectTransform Clone(Transform template, Transform parent, string name, string value)
        {
            var copy = Object.Instantiate(template.gameObject, parent, false);
            copy.name = name;
            foreach (var localizable in copy.GetComponentsInChildren<LocalizableText>(true)) Object.DestroyImmediate(localizable);
            Text text = copy.GetComponent<Text>();
            if (!text) text = copy.GetComponentsInChildren<Text>(true).FirstOrDefault();
            if (text) text.text = value;
            var button = copy.GetComponent<Button>();
            if (button) button.onClick = new Button.ButtonClickedEvent();
            if (!copy.activeSelf) copy.SetActive(true);
            return (RectTransform)copy.transform;
        }

        private static void Chain(params Selectable[] items)
        {
            foreach (var item in items) item.navigation = new Navigation { mode = Navigation.Mode.Vertical };
        }

        private static void Select(Selectable selectable)
        {
            if (selectable && EventSystem.current && selectable.gameObject.activeInHierarchy) EventSystem.current.SetSelectedGameObject(selectable.gameObject);
        }

        private static void ShiftY(RectTransform rect, float amount)
        {
            rect.anchorMin += new Vector2(0f, amount); rect.anchorMax += new Vector2(0f, amount);
        }
        private static void Place(Selectable item, float xMin, float xMax, float yMin, float yMax)
        {
            Anchor((RectTransform)item.transform, xMin, yMin, xMax, yMax);
        }
        private static void Anchor(RectTransform rect, float xMin, float yMin, float xMax, float yMax)
        {
            rect.anchorMin = new Vector2(xMin, yMin); rect.anchorMax = new Vector2(xMax, yMax);
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero; rect.pivot = new Vector2(0.5f, 0.5f);
        }
        private static void Stretch(RectTransform rect) { Anchor(rect, 0f, 0f, 1f, 1f); }
    }
}
