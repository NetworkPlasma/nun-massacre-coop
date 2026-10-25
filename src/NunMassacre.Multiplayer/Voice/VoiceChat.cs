using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace NunMassacre.Multiplayer.Voice
{
    internal enum VoiceMode { OpenMic, PushToTalk }

    /// <summary>
    /// Proximity voice chat. Captures the microphone at 16 kHz, gates it by voice activity or
    /// push-to-talk, and sends ADPCM packets through the session (the host relays them). Each
    /// teammate's voice plays in 3D from their character: it fades with distance and is muffled
    /// through walls. In menus everyone hears everyone; spectators talk only among themselves.
    /// Loud voices can draw the Nun.
    /// </summary>
    internal sealed class VoiceChat : IDisposable
    {
        internal readonly ConfigEntry<bool> Enabled;
        internal readonly ConfigEntry<VoiceMode> Mode;
        internal readonly ConfigEntry<string> Device;
        internal readonly ConfigEntry<int> Sensitivity;
        internal readonly ConfigEntry<float> MicLevel;
        internal readonly ConfigEntry<float> Volume;
        internal readonly ConfigEntry<KeyCode> PushToTalkKey;
        internal readonly ConfigEntry<string> PlayerVolumes;
        internal static bool TestNoOwnEcho;      // Automated tests: recordings without the space's answer to your voice.
        internal static bool TestPlainAcoustics; // Automated tests: the mod's own acoustics instead of the rays, to compare.
        internal readonly RaytracedAcoustics Raytraced;
        internal readonly WorldSounds World;

        private readonly Session session;
        private readonly Action<string> log;
        private readonly Dictionary<int, VoicePlayback> players = new Dictionary<int, VoicePlayback>();
        private readonly Dictionary<int, float> lastHeard = new Dictionary<int, float>();
        private readonly Dictionary<int, Room> rooms = new Dictionary<int, Room>();
        private readonly Dictionary<int, Route> routes = new Dictionary<int, Route>();
        private readonly UnityEngine.AI.NavMeshPath navPath = new UnityEngine.AI.NavMeshPath();
        private readonly Vector3[] corners = new Vector3[48];
        private readonly float[] frame = new float[VoiceCodec.FrameSamples];
        private readonly float[] pending = new float[VoiceCodec.FrameSamples * 2];
        private readonly float[] decoded = new float[VoiceCodec.MaxSamples];
        private readonly List<float> captured = new List<float>(4096);
        private AudioClip mic;
        private string micDevice;
        private int micRate, readPosition;
        private double resample;
        private VoiceCodec.State encoder;
        private int sequence, pendingCount;
        private float gateUntil, nextNoise, testClock;
        private bool pendingLoud, transmitting, wasTransmitting;
        internal bool TestTone; // Automated tests: a synthetic voice instead of a microphone.
        internal static bool TestOldVent; // Tests: the vent sound as it was (a fast ring), to compare.
        internal float[] TestClip; // Tests: a recorded phrase (16 kHz) in place of the tone, spoken once on Speak().
        private float clipStart = -1000f;
        internal void Speak() { clipStart = testClock; }
        internal bool MicTest;  // The settings page is open: capture to show the level, send nothing.
        internal bool NoMicrophone; // Headless test processes never open the real microphone.

        /// <summary>Voice level (RMS) the Nun hears from a player hiding under a bed: talking out loud, not a whisper.</summary>
        internal const float HidingThreshold = 0.09f;

        internal float InputLevel { get; private set; }
        internal bool Live { get; private set; } // The microphone is on: open mic, or push-to-talk held.
        internal float LastAlert = -100f;        // Unscaled time this player's voice last drew the Nun.
        internal bool GateOpen { get { return Time.unscaledTime < gateUntil; } }
        internal bool Transmitting { get { return transmitting; } }
        internal bool HasMicrophone { get { return TestTone || Microphone.devices.Length > 0; } }
        internal string ActiveDevice { get { return TestTone ? "TEST TONE" : micDevice ?? ""; } }
        internal int PacketsSent, PacketsReceived;

        internal VoiceChat(ConfigFile config, Session session, Action<string> log)
        {
            this.session = session;
            this.log = log ?? delegate { };
            Enabled = config.Bind("Voice", "Enabled", true, "Proximity voice chat.");
            Mode = config.Bind("Voice", "Mode", VoiceMode.OpenMic, "OpenMic (voice activation) or PushToTalk.");
            PushToTalkKey = config.Bind("Voice", "PushToTalkKey", KeyCode.V, "Key held to talk in PushToTalk mode.");
            Device = config.Bind("Voice", "Microphone", "", "Microphone device name; empty for the system default.");
            Sensitivity = config.Bind("Voice", "Sensitivity", 1, "Voice activation sensitivity: 0 low, 1 medium, 2 high.");
            Volume = config.Bind("Voice", "Volume", 1f, "Teammate voice volume, 0.25 to 2.");
            MicLevel = config.Bind("Voice", "MicLevel", 1f, "Microphone level (gain), 0.25 to 4: raise it if teammates hear you too quietly.");
            PlayerVolumes = config.Bind("Voice", "PlayerVolumes", "",
                "Each teammate's voice volume, set in the pause menu: name=level pairs, separated by |, level 0 (muted) to 2.");
            Raytraced = new RaytracedAcoustics(this.log);
            World = new WorldSounds(Raytraced);
        }

        // ---------- Each teammate's own volume (the pause menu's list) ----------
        private string volumesText;
        private readonly Dictionary<string, float> volumes = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        /// <summary>How loud this teammate's voice is to this player, 0 (muted) to 2; 1 unless set.</summary>
        internal float VolumeOf(string name)
        {
            Volumes();
            float level;
            return name != null && volumes.TryGetValue(name, out level) ? level : 1f;
        }

        /// <summary>A teammate's name as the pause menu lists it.</summary>
        internal string NameOf(int id)
        {
            PlayerFrame frame;
            return session.Players.TryGetValue(id, out frame) && frame != null && !string.IsNullOrEmpty(frame.name) ? frame.name : "PLAYER " + id;
        }

        internal void SetVolumeOf(string name, float level)
        {
            if (string.IsNullOrEmpty(name)) return;
            Volumes();
            level = Mathf.Clamp(level, 0f, 2f);
            if (Mathf.Abs(level - 1f) < 0.001f) volumes.Remove(name); else volumes[name] = level;
            volumesText = string.Join("|",
                volumes.Select(p => p.Key.Replace("|", "").Replace("=", "") + "=" + p.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).ToArray());
            PlayerVolumes.Value = volumesText;
        }

        private void Volumes()
        {
            if (volumesText == PlayerVolumes.Value) return;
            volumesText = PlayerVolumes.Value ?? "";
            volumes.Clear();
            foreach (string pair in volumesText.Split('|'))
            {
                int equals = pair.LastIndexOf('=');
                float level;
                if (equals > 0
                    && float.TryParse(pair.Substring(equals + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out level))
                    volumes[pair.Substring(0, equals)] = Mathf.Clamp(level, 0f, 2f);
            }
        }

        internal float Threshold { get { return Sensitivity.Value <= 0 ? 0.035f : Sensitivity.Value >= 2 ? 0.006f : 0.014f; } }

        /// <summary>Main thread, every frame.</summary>
        internal void Update()
        {
            bool wanted = Enabled.Value && (session.Active || MicTest);
            if (!wanted) { StopMic(); transmitting = false; }
            else Capture();
            var ears = Camera.main ? Camera.main.transform : null;
            var self = session.Players.ContainsKey(session.LocalId) ? session.Players[session.LocalId] : null;
            Raytraced.Update(!TestPlainAcoustics && ears && session.Active && session.SceneName != Session.MainMenuScene && self != null && (self.body || session.IsHost),
                ears ? ears.position : Vector3.zero);
            UpdatePlayback();
            UpdateOwnEcho(ears);
            // The game's own sounds: the reverb where you are, and each nearby one muffled as the rays find it.
            var atEars = Raytraced.Running ? Raytraced.ListenerReverb : null;
            World.Update(!TestPlainAcoustics && ears, ears,
                atEars.HasValue && ears ? RaytracedParams(atEars, null, Measure(0, ears.position), null, 0f) : (ReverbParams?)null);
        }

        // ---------- Your own voice, off the space around you ----------
        private VoicePlayback ownRoom, ownEcho; // The room's ring of it; a far wall's (or a passage's) echo of it.
        /// <summary>How loud the space's answer to your own voice is: some 27 dB under your voice in an ordinary room,
        /// 22-26 in a church, a corridor, a duct; there if you listen for it, never in the way.</summary>
        private const float OwnEchoLevel = 0.42f;

        /// <summary>
        /// What a real voice does in a real place: in a tiled bathroom you hear it ring, down a corridor or across a hall
        /// it comes back off the far end, in a duct it rings tinny. Your voice itself you hear in your own head (it is
        /// never played back): only what the space sends back, as it is worked out for teammates' voices there (the rays
        /// at your ears, or the probes), a little later than it would be for the microphone's own delay.
        /// </summary>
        private void UpdateOwnEcho(Transform ears)
        {
            var me = session.Players.ContainsKey(session.LocalId) ? session.Players[session.LocalId] : null;
            bool wanted = !TestNoOwnEcho && Enabled.Value && ears && session.Active && session.SceneName != Session.MainMenuScene && me != null && me.body
                && (session.Life.State == Gameplay.LifeState.Alive || session.Life.State == Gameplay.LifeState.Downed);
            if (!wanted)
            {
                if (ownRoom) UnityEngine.Object.Destroy(ownRoom.gameObject);
                if (ownEcho) UnityEngine.Object.Destroy(ownEcho.gameObject);
                ownRoom = ownEcho = null;
                return;
            }
            if (!ownRoom) ownRoom = OwnPlayback("NMM_OwnVoiceRoom");
            if (!ownEcho) ownEcho = OwnPlayback("NMM_OwnVoiceEcho");
            Room here = Measure(0, ears.position);
            var atEars = Raytraced.Running ? Raytraced.ListenerReverb : null;
            if (atEars.HasValue) RaytracedReverb(ownRoom, atEars, null, here, null, 0f, false);
            else Acoustics(ownRoom, here, false, 0f, null);
            ownRoom.Reverb.dryLevel = -10000f; // The room's answer only.
            // No early reflections: this close behind the voice they would sound like a second voice, not a room.
            ownRoom.Reverb.reflectionsLevel = -10000f;
            ownRoom.Echo.enabled = false;
            // Only a place that really rings answers back much (a church, tiles, a long corridor); a dead room barely.
            float ringing = Mathf.Lerp(0.45f, 1f, Mathf.InverseLerp(0.5f, 2f, ownRoom.Reverb.decayTime));
            Echoes(ownEcho, here, false, 0f, VoicePlayback.PromptDelay, !atEars.HasValue); // In a duct, plainly: its reverb only.
            ownEcho.Echo.dryMix = 0f; // The echo only.
            ownEcho.Gain = ownEcho.Echo.enabled ? OwnEchoLevel * 0.6f : 0f;
            ownRoom.Gain = OwnEchoLevel * ringing;
        }

        private static VoicePlayback OwnPlayback(string name)
        {
            var playback = VoicePlayback.Create(name);
            playback.Prompt();
            playback.Source.spatialBlend = 0f; // In your own head.
            playback.Audibility = 1f;
            return playback;
        }

        /// <summary>A frame of your own voice, as it is sent: to the space's answer.</summary>
        private void OwnVoice(float[] samples)
        {
            if (ownRoom) ownRoom.Push(samples, samples.Length);
            if (ownEcho) ownEcho.Push(samples, samples.Length);
        }

        // ---------- Capture and send ----------

        private void Capture()
        {
            captured.Clear();
            if (TestTone) Synthesize(Time.unscaledDeltaTime);
            else
            {
                if (NoMicrophone || !StartMic()) return;
                int position = Microphone.GetPosition(micDevice);
                if (position < 0 || mic == null) return;
                int length = mic.samples;
                int available = (position - readPosition + length) % length;
                if (available <= 0) return;
                var buffer = new float[available];
                if (readPosition + available <= length) mic.GetData(buffer, readPosition);
                else
                {
                    var first = new float[length - readPosition];
                    var second = new float[available - first.Length];
                    mic.GetData(first, readPosition); mic.GetData(second, 0);
                    Array.Copy(first, buffer, first.Length); Array.Copy(second, 0, buffer, first.Length, second.Length);
                }
                readPosition = position;
                Resample(buffer, micRate);
            }
            // Consume in 20 ms frames.
            int index = 0;
            while (captured.Count - index >= VoiceCodec.FrameSamples)
            {
                captured.CopyTo(index, frame, 0, VoiceCodec.FrameSamples);
                index += VoiceCodec.FrameSamples;
                ProcessFrame();
            }
            if (index > 0) carry = captured.Skip(index).ToArray();
            else carry = captured.ToArray();
        }

        private float[] carry = new float[0];

        private void Resample(float[] input, int rate)
        {
            captured.AddRange(carry);
            if (rate == VoiceCodec.SampleRate) { captured.AddRange(input); return; }
            double step = (double)rate / VoiceCodec.SampleRate;
            for (; resample < input.Length - 1; resample += step)
            {
                int i = (int)resample; float t = (float)(resample - i);
                captured.Add(input[i] + (input[i + 1] - input[i]) * t);
            }
            resample -= input.Length;
            if (resample < 0) resample = 0;
        }

        private void Synthesize(float dt)
        {
            captured.AddRange(carry);
            int samples = Mathf.RoundToInt(dt * VoiceCodec.SampleRate);
            for (int i = 0; i < samples; i++)
            {
                testClock += 1f / VoiceCodec.SampleRate;
                if (TestClip != null)
                {
                    int at = Mathf.FloorToInt((testClock - clipStart) * VoiceCodec.SampleRate);
                    captured.Add(at >= 0 && at < TestClip.Length ? TestClip[at] : 0f);
                    continue;
                }
                // Speech-like bursts: 1.2 s on, 0.8 s off, with a "shout" every 6 s.
                float cycle = testClock % 2f, loud = (testClock % 6f) < 0.5f ? 0.9f : 0.25f;
                float envelope = cycle < 1.2f ? Mathf.Sin(cycle / 1.2f * Mathf.PI) : 0f;
                captured.Add(envelope * loud * (Mathf.Sin(testClock * 2f * Mathf.PI * 220f) * 0.7f + Mathf.Sin(testClock * 2f * Mathf.PI * 470f) * 0.3f));
            }
        }

        /// <summary>Unchanged up to 0.8, then eased towards 1 instead of clipping hard.</summary>
        private static float SoftClip(float v)
        {
            float size = Math.Abs(v);
            if (size <= 0.8f) return v;
            float eased = 0.8f + 0.2f * (float)Math.Tanh((size - 0.8f) / 0.2f);
            return v < 0f ? -eased : eased;
        }

        private void ProcessFrame()
        {
            // The microphone level: boosted (or cut), with soft clipping so a raised quiet mic does not crackle.
            float gain = Mathf.Clamp(MicLevel.Value, 0.25f, 4f);
            if (Math.Abs(gain - 1f) > 0.001f)
                for (int i = 0; i < frame.Length; i++) frame[i] = SoftClip(frame[i] * gain);
            float sum = 0f, peak = 0f;
            for (int i = 0; i < frame.Length; i++) { float v = frame[i]; sum += v * v; if (Math.Abs(v) > peak) peak = Math.Abs(v); }
            float rms = Mathf.Sqrt(sum / frame.Length);
            InputLevel = Mathf.Max(rms, InputLevel * 0.8f);
            bool keyHeld = Mode.Value == VoiceMode.PushToTalk && Input.GetKey(PushToTalkKey.Value) && !Typing();
            if (Mode.Value == VoiceMode.OpenMic && rms >= Threshold) gateUntil = Time.unscaledTime + 0.35f; // Hangover keeps word endings.
            bool open = Mode.Value == VoiceMode.PushToTalk ? keyHeld || TestTone : Time.unscaledTime < gateUntil;
            Live = Mode.Value == VoiceMode.OpenMic || keyHeld || TestTone;
            bool loud = session.LocalHiding ? rms > HidingThreshold : rms > 0.18f || peak > 0.85f; // Hiding, talking is enough to give you away.
            transmitting = open && session.Connected && !MicTest;
            if (!transmitting)
            {
                if (wasTransmitting) Flush(true);
                wasTransmitting = false;
                return;
            }
            wasTransmitting = true;
            OwnVoice(frame);
            Array.Copy(frame, 0, pending, pendingCount, frame.Length);
            pendingCount += frame.Length;
            pendingLoud |= loud;
            if (pendingCount >= pending.Length) Flush(false);
            if (loud) AlertNun(rms);
            if (session.IsHost) session.HearVoice(session.LocalId, rms); // The host's own voice, as the Nun hears it.
        }

        private void Flush(bool end)
        {
            if (pendingCount == 0 && !end) return;
            byte flags = (byte)((pendingLoud ? VoiceCodec.FlagLoud : 0) | (end ? VoiceCodec.FlagEnd : 0));
            var packet = VoiceCodec.Encode(pending, pendingCount, ref encoder, sequence++, flags);
            pendingCount = 0; pendingLoud = false;
            session.SendVoice(packet);
            PacketsSent++;
        }

        /// <summary>Shouting in a co-op level makes noise at the player's position, and the host's Nun may answer the call.</summary>
        private void AlertNun(float level)
        {
            if (Time.unscaledTime < nextNoise || !session.Local
                || session.Life.State != Gameplay.LifeState.Alive && session.Life.State != Gameplay.LifeState.Downed)
                return;
            nextNoise = Time.unscaledTime + 2f;
            LastAlert = Time.unscaledTime;
            session.Shout(level);
        }

        private static bool Typing()
        {
            var selected = UnityEngine.EventSystems.EventSystem.current ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject : null;
            return selected && selected.GetComponent<UnityEngine.UI.InputField>();
        }

        // The device list, looked up every two seconds rather than every frame (each lookup allocates), so a microphone
        // unplugged or plugged in is picked up within a moment.
        private string[] devices = new string[0];
        private float nextDeviceCheck;

        private bool StartMic()
        {
            if (Time.unscaledTime >= nextDeviceCheck) { nextDeviceCheck = Time.unscaledTime + 2f; devices = Microphone.devices; }
            string wantedDevice = devices.Contains(Device.Value) ? Device.Value : devices.FirstOrDefault();
            if (wantedDevice == null) { StopMic(); return false; }
            if (mic != null && micDevice == wantedDevice && Microphone.IsRecording(micDevice)) return true;
            StopMic();
            int min, max;
            Microphone.GetDeviceCaps(wantedDevice, out min, out max);
            micRate = (min == 0 && max == 0) ? VoiceCodec.SampleRate : Mathf.Clamp(VoiceCodec.SampleRate, min, max);
            mic = Microphone.Start(wantedDevice, true, 1, micRate);
            if (mic == null) { log("Voice: could not open microphone " + wantedDevice); return false; }
            micDevice = wantedDevice;
            readPosition = 0; resample = 0; carry = new float[0];
            log("Voice: microphone " + wantedDevice + " at " + micRate + " Hz");
            return true;
        }

        internal void StopMic()
        {
            if (micDevice != null && Microphone.IsRecording(micDevice)) Microphone.End(micDevice);
            if (mic != null) UnityEngine.Object.Destroy(mic);
            mic = null; micDevice = null; InputLevel = 0f; Live = false;
            if (wasTransmitting && session.Connected) Flush(true);
            wasTransmitting = false;
        }

        internal void CycleDevice()
        {
            var devices = Microphone.devices;
            if (devices.Length == 0) return;
            int index = Array.IndexOf(devices, Device.Value);
            Device.Value = devices[(index + 1) % devices.Length];
        }

        // ---------- Receive and play ----------

        internal void Receive(int speaker, byte[] packet)
        {
            byte flags;
            int count = VoiceCodec.Decode(packet, decoded, out flags);
            if (count < 0) return;
            if (session.IsHost && count > 0)
            {
                float sum = 0f;
                for (int i = 0; i < count; i++) sum += decoded[i] * decoded[i];
                session.HearVoice(speaker, Mathf.Sqrt(sum / count)); // A guest's voice, as the Nun hears it.
            }
            if (!Enabled.Value) return;
            PacketsReceived++;
            VoicePlayback playback;
            if (!players.TryGetValue(speaker, out playback) || !playback)
            {
                playback = VoicePlayback.Create("NMM_Voice_" + speaker);
                players[speaker] = playback;
            }
            playback.Push(decoded, count);
            lastHeard[speaker] = Time.unscaledTime;
        }

        internal bool IsTalking(int speaker)
        {
            float time; VoicePlayback playback;
            return lastHeard.TryGetValue(speaker, out time) && Time.unscaledTime - time < 0.4f
                && players.TryGetValue(speaker, out playback) && playback && playback.Level > 0.004f;
        }

        internal VoicePlayback PlaybackOf(int speaker)
        {
            VoicePlayback playback;
            return players.TryGetValue(speaker, out playback) && playback ? playback : null;
        }

        internal long PlayedSamples(int speaker)
        {
            VoicePlayback playback;
            return players.TryGetValue(speaker, out playback) && playback ? playback.PlayedSamples : 0;
        }

        internal void Remove(int speaker)
        {
            VoicePlayback playback;
            if (players.TryGetValue(speaker, out playback) && playback) UnityEngine.Object.Destroy(playback.gameObject);
            players.Remove(speaker); lastHeard.Remove(speaker); rooms.Remove(speaker); paths.Remove(speaker); routes.Remove(speaker); floods.Remove(speaker);
        }

        internal void Clear() { foreach (int id in players.Keys.ToList()) Remove(id); }

        /// <summary>Places each voice: 3D at the speaker in a level, flat in menus, dead talk to dead.</summary>
        private void UpdatePlayback()
        {
            if (players.Count == 0) return;
            var listener = Camera.main ? Camera.main.transform : null;
            var me = session.Players.ContainsKey(session.LocalId) ? session.Players[session.LocalId] : null;
            bool iSpectate = session.Life.State == Gameplay.LifeState.Spectating || session.Life.State == Gameplay.LifeState.GameOver;
            int walls = LayerMask.GetMask("Wall", "Ground", "Ceiling", "Door");
            float volume = Mathf.Clamp(Volume.Value, 0.25f, 2f);
            Room around = listener ? Measure(0, listener.position) : null; // The room the listener is in (id 0: no player).
            foreach (var pair in players)
            {
                var playback = pair.Value;
                if (!playback) continue;
                // Nobody has heard from them for a while: keep the voice where it was, work nothing out (cheap when quiet).
                float last;
                if (lastHeard.TryGetValue(pair.Key, out last) && Time.unscaledTime - last > 1.5f && playback.Level < 0.0005f) continue;
                PlayerFrame speaker;
                session.Players.TryGetValue(pair.Key, out speaker);
                Transform body = speaker != null ? session.Avatars.GetTransform(pair.Key) : null;
                bool inLevel = speaker != null && speaker.body && me != null && me.body && body
                    && body.gameObject.activeInHierarchy && speaker.scene == session.SceneName;
                float gain = volume * VolumeOf(NameOf(pair.Key)), spatial = 1f, cutoff = 22000f, distance = 0f;
                Room room = null; bool vent = false, ring = false; int heardWay = 0;
                if (speaker != null && speaker.spectating && !iSpectate) gain = 0f;              // The dead cannot reach the living.
                else if (!inLevel || (iSpectate && speaker != null && speaker.spectating)) spatial = 0f; // Menus, loading, and dead chat.
                if (spatial > 0f && body)
                {
                    Vector3 mouth = body.position + Vector3.up * (speaker.downed ? 0.6f : speaker.vent || speaker.crouching ? 1.15f : 1.6f);
                    Vector3 from = mouth;
                    bool traced = false;
                    if (listener)
                    {
                        Vector3 facing = Finite(speaker.look) && speaker.look.sqrMagnitude > 0.01f ? speaker.look.normalized : body.forward;
                        var heard = Propagation(pair.Key, listener.position, mouth, facing, walls);
                        // Through the wall or floor, or round by the way a person would walk: whichever is louder. Round
                        // about, the voice comes from where that way comes into view (the open door, the corner, the stairs).
                        float straight = VoicePlayback.Loudness(Vector3.Distance(mouth, listener.position)) * heard.Gain;
                        // On another storey there is no way round (only the murmur through the floor, from right under you).
                        bool sameStorey = Mathf.Abs(body.position.y - (listener.position.y - 1.6f)) < 3f;
                        bool ducts = speaker.vent || (me != null && me.vent);
                        // Raytraced: the rays say how much of the voice gets here and how muffled (round corners, through
                        // doorways and walls, along the ducts); the ways round below still say where it is heard from.
                        var rays = default(VAudio.LowPass);
                        traced = Raytraced.Running && Raytraced.Muffling(pair.Key, out rays);
                        var route = heard.Blocked && sameStorey && !heard.Floor && !ducts ? RouteOf(pair.Key, body.position, listener.position, walls) : null;
                        float roundAbout = route != null && route.Found ? VoicePlayback.Loudness(route.Length) * route.Gain : 0f;
                        // Or out through an opening both can see: a window, a gap, a doorway; along a duct, out of its mouth.
                        // (A duct may open high in a room's wall, at the next storey's height: its mouth counts wherever it is.)
                        var gap = !heard.Blocked ? null
                            : ducts ? DuctOf(pair.Key, speaker.vent ? mouth : listener.position, speaker.vent ? listener.position : mouth, !speaker.vent, walls)
                            : sameStorey ? OpeningOf(pair.Key, mouth, listener.position, walls) : null;
                        float throughGap = gap != null && gap.GapFound ? VoicePlayback.Loudness(gap.GapLength) * gap.GapGain : 0f;
                        int way = roundAbout > straight && roundAbout >= throughGap ? 1 : throughGap > straight ? 2 : 0;
                        heardWay = way;
                        if (traced)
                        {
                            // As in Steam Audio's pathing: heard from where the way it takes comes into view, as far
                            // away as that way is long; as loud and as muffled as the rays found; still facing away or not.
                            float along = way == 1 ? route.Length : way == 2 ? gap.GapLength : Vector3.Distance(mouth, listener.position);
                            if (way == 1) from = route.Apparent; else if (way == 2) from = gap.GapPoint;
                            gain *= Mathf.Clamp01(rays.GainLF) * heard.ShapeGain * VoicePlayback.Loudness(along) / Mathf.Max(0.05f,
                                VoicePlayback.Loudness(Vector3.Distance(from, listener.position)));
                            cutoff = Mathf.Min(RayCutoff(rays), heard.ShapeCutoff);
                        }
                        else if (way == 1)
                        {
                            from = route.Apparent;
                            gain *= roundAbout / Mathf.Max(0.05f, VoicePlayback.Loudness(Vector3.Distance(from, listener.position)));
                            cutoff = route.Cutoff;
                        }
                        else if (way == 2)
                        {
                            from = gap.GapPoint;
                            gain *= throughGap / Mathf.Max(0.05f, VoicePlayback.Loudness(Vector3.Distance(from, listener.position)));
                            cutoff = gap.GapCutoff;
                        }
                        else { gain *= heard.Gain; cutoff = heard.Cutoff; }
                        // Behind the listener a voice is a little duller and softer (the head and ears in the way): front
                        // from back, as a real head hears it.
                        Vector3 toVoice = from - listener.position;
                        float behind = toVoice.sqrMagnitude > 0.01f ? -Vector3.Dot(listener.forward, toVoice.normalized) : 0f;
                        if (behind > 0f) { cutoff = Mathf.Min(cutoff, Mathf.Lerp(16000f, 7500f, behind)); gain *= Mathf.Lerp(1f, 0.9f, behind); }
                        Route place;
                        if (!routes.TryGetValue(pair.Key, out place)) routes[pair.Key] = place = new Route { Heard = from };
                        place.Using = way == 1;
                        place.Way = way;
                        // The voice glides to where it now comes from (a door opening, a corner turned), not jumps.
                        place.Heard = (place.Heard - from).sqrMagnitude > 100f ? from
                            : Vector3.Lerp(place.Heard, from, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
                        from = place.Heard;
                    }
                    playback.transform.position = from;
                    bool hiding = speaker.state == (int)GrannyClone.PlayerStates.States.Hiding;
                    if (hiding) { gain *= 0.72f; cutoff = Mathf.Min(cutoff, 2200f); } // Under a bed: into the mattress.
                    if (speaker.downed) { gain *= 0.8f; cutoff = Mathf.Min(cutoff, 5000f); } // Down and hurt: weaker, breathier.
                    // Where the listener is: under a bed every voice comes through the mattress and the frame; knocked down,
                    // the ears ring and the top of every voice is gone.
                    if (me != null && me.state == (int)GrannyClone.PlayerStates.States.Hiding && !hiding) { gain *= 0.85f; cutoff = Mathf.Min(cutoff, 2600f); }
                    if (me != null && me.downed) cutoff = Mathf.Min(cutoff, 4500f);
                    // A duct sounds like one from outside it: out of its opening, its echo; through its metal (from the room
                    // below), the metal's ring. In the duct with them you hear them plainly, in the duct's own reverb. (Chosen
                    // by ear, blind, in the three places.)
                    vent = speaker.vent && !(me != null && me.vent);
                    ring = heardWay != 2;
                    if (vent && !traced) cutoff = Mathf.Min(cutoff, 2800f); // Thin and hollow (raytraced: as the rays find it).
                    room = Measure(pair.Key, mouth); // In a duct too: its reverb and its echo are the duct's own.
                    Raytraced.Voice(pair.Key, mouth);
                    if (listener) distance = Vector3.Distance(listener.position, mouth);
                }
                else if (listener) playback.transform.position = listener.position;
                playback.Source.spatialBlend = spatial;
                playback.Gain = gain;
                playback.Audibility = spatial <= 0f || !listener ? 1f
                    : gain / volume * VoicePlayback.Loudness(Vector3.Distance(playback.transform.position, listener.position));
                playback.LowPass.cutoffFrequency = Mathf.Lerp(playback.LowPass.cutoffFrequency, cutoff, 0.2f);
                VAudio.EAXReverb? atListener = null, atSpeaker = null;
                if (spatial > 0f && Raytraced.Running) { atListener = Raytraced.ListenerReverb; atSpeaker = Raytraced.ReverbOf(pair.Key); }
                if (atListener.HasValue || atSpeaker.HasValue) RaytracedReverb(playback, atListener, atSpeaker, around, room, distance, vent, ring);
                else Acoustics(playback, room, vent, distance, spatial > 0f ? around : null, ring);
            }
        }

        // ---------- The way round ----------
        internal sealed class Route
        {
            public float NextProbe, Length, Gain = 1f, Cutoff = 16000f;
            public bool Found, Using;
            public Vector3 Apparent, Heard;
            public int Bends, Doors;
            // The opening (OpeningOf); Way: 0 straight, 1 round, 2 opening.
            public float NextGap, GapLength, GapGain = 1f, GapCutoff = 16000f;
            public bool GapFound;
            public Vector3 GapPoint;
            public int Way;
            // Tests: how far the duct search spread, and where it first left the duct.
            public int Cells;
            public Vector3 Furthest, Escape;
        }

        private static readonly Vector3[] Search = BuildSearch();
        private static Vector3[] BuildSearch()
        {
            var ways = new List<Vector3> { Vector3.up, Vector3.down };
            foreach (float pitch in new[] { -30f, 0f, 30f })
                for (int i = 0; i < 10; i++) ways.Add(Quaternion.Euler(pitch, i * 36f + (pitch == 0f ? 0f : 18f), 0f) * Vector3.forward);
            return ways.ToArray();
        }
        private static readonly float[] Along = { 0.3f, 0.6f, 0.92f };

        // ---------- Along a duct ----------
        private const float Cell = 0.6f;
        private const int MostCells = 1600, CellsPerFrame = 100; // About 40 m of duct, worked through a slice a frame.
        // Out of the duct the search goes on only a few steps (3.6 m): out of the mouth and down through a ceiling vent into
        // the room, but a gap where two lengths of duct meet (into the space above a ceiling) does not use up the search.
        private const int OutsideSteps = 6;
        private static readonly Vector3Int[] Steps = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down,
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };
        private readonly List<Vector3Int> trail = new List<Vector3Int>();

        /// <summary>One search along a duct, carried on a slice at a time across frames so the game never stalls.</summary>
        private sealed class Flood
        {
            public readonly Dictionary<Vector3Int, Vector3Int> Came = new Dictionary<Vector3Int, Vector3Int>();
            public readonly Queue<Vector3Int> Frontier = new Queue<Vector3Int>();
            public readonly Dictionary<Vector3Int, int> Out = new Dictionary<Vector3Int, int>(); // Steps since leaving the duct.
            public readonly Dictionary<Vector3Int, int> Depth = new Dictionary<Vector3Int, int>(); // Steps from the one inside.
            public Vector3 Origin, Outside; public bool ListenerInside, Running;
            public Vector3Int Best; public float BestLength; // The shortest way out to where the one outside can see.
            public void Start(Vector3 origin, Vector3 outside, bool listenerInside)
            {
                Came.Clear(); Frontier.Clear(); Out.Clear(); Depth.Clear();
                Origin = origin; Outside = outside; ListenerInside = listenerInside; Running = true; BestLength = float.MaxValue;
                Came[Vector3Int.zero] = Vector3Int.zero; Depth[Vector3Int.zero] = 0; Frontier.Enqueue(Vector3Int.zero);
            }
        }
        private readonly Dictionary<int, Flood> floods = new Dictionary<int, Flood>();

        /// <summary>
        /// Along a duct (a vent the speaker or the listener is crawling in): the sound spreads through the duct in 60 cm
        /// steps, never through its metal, until it comes out somewhere the one outside can see: the vent's mouth. The
        /// voice comes from there (for the one outside), or from as far back along the duct as the one inside can see down
        /// it, as loud as the way along the duct is long. Searched again about once a second while they talk, a slice a
        /// frame; the voice glides to each new answer.
        /// </summary>
        private Route DuctOf(int speaker, Vector3 inside, Vector3 outside, bool listenerInside, int mask)
        {
            Route route;
            if (!routes.TryGetValue(speaker, out route)) routes[speaker] = route = new Route { Heard = inside };
            Flood flood;
            if (!floods.TryGetValue(speaker, out flood)) floods[speaker] = flood = new Flood();
            if (!flood.Running)
            {
                if (Time.unscaledTime < route.NextGap) return route;
                flood.Start(inside, outside, listenerInside);
                route.Escape = Vector3.zero;
            }
            // A duct is a one-sided mesh: from inside, its walls would let the search out unless back faces count too.
            bool backFaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            try { Spread(route, flood, mask); }
            finally { Physics.queriesHitBackfaces = backFaces; }
            return route;
        }

        /// <summary>Between a duct's own floor and top: not just under one (the space below a duct, outside it, is a room).</summary>
        private static bool InsideDuct(Vector3 at, int mask)
        {
            RaycastHit hit;
            return Physics.Raycast(at, Vector3.up, out hit, 1.6f, mask, QueryTriggerInteraction.Ignore) && hit.collider.name.IndexOf("Vent", StringComparison.Ordinal) >= 0
                && Physics.Raycast(at, Vector3.down, out hit, 1.4f, mask, QueryTriggerInteraction.Ignore) && hit.collider.name.IndexOf("Vent", StringComparison.Ordinal) >= 0;
        }

        private void Spread(Route route, Flood flood, int mask)
        {
            int budget = CellsPerFrame;
            while (flood.Frontier.Count > 0 && flood.Came.Count < MostCells && budget-- > 0)
            {
                var cell = flood.Frontier.Dequeue();
                int depth = flood.Depth[cell];
                // Breadth first, so every cell from here on is at least this far along: none can beat the best way out.
                if (depth * Cell >= flood.BestLength) { flood.Frontier.Clear(); break; }
                Vector3 at = flood.Origin + (Vector3)cell * Cell;
                // Only out of the duct can the one outside see it (the long look is the costly one: only there).
                int outside = 0;
                if (!InsideDuct(at, mask))
                {
                    int before;
                    outside = (cell == Vector3Int.zero || !flood.Out.TryGetValue(flood.Came[cell], out before) ? 0 : before) + 1;
                    if (route.Escape == Vector3.zero) route.Escape = at;
                    if (!Physics.Linecast(flood.Outside, at, mask, QueryTriggerInteraction.Ignore))
                    {
                        // A way out: kept if, along the duct and on through the air, it is the shortest yet (a nearer
                        // mouth along the duct can be the longer way round to the listener).
                        float length = depth * Cell + Vector3.Distance(at, flood.Outside);
                        if (length < flood.BestLength) { flood.BestLength = length; flood.Best = cell; }
                        flood.Out[cell] = outside;
                        continue;
                    }
                }
                flood.Out[cell] = outside;
                if (outside >= OutsideSteps) continue;
                foreach (var step in Steps)
                {
                    var next = cell + step;
                    if (flood.Came.ContainsKey(next)) continue;
                    if (Physics.Linecast(at, flood.Origin + (Vector3)next * Cell, mask, QueryTriggerInteraction.Ignore)) continue; // The duct's side.
                    flood.Came[next] = cell; flood.Depth[next] = depth + 1; flood.Frontier.Enqueue(next);
                }
            }
            bool exhausted = flood.Frontier.Count == 0 || flood.Came.Count >= MostCells;
            if (!exhausted) return; // Carries on next frame.
            bool found = flood.BestLength < float.MaxValue;
            var reached = flood.Best;
            flood.Running = false;
            route.NextGap = Time.unscaledTime + 1f;
            route.Cells = flood.Came.Count;
            route.GapFound = found;
            if (!found) return;
            // The way back to the one inside.
            trail.Clear();
            for (var cell = reached; ; cell = flood.Came[cell]) { trail.Add(cell); if (cell == Vector3Int.zero) break; }
            Vector3 mouthOfIt = flood.Origin + (Vector3)reached * Cell;
            route.Furthest = mouthOfIt;
            route.GapLength = (trail.Count - 1) * Cell + Vector3.Distance(mouthOfIt, flood.Outside);
            route.GapGain = 0.8f;
            route.GapCutoff = 3000f;
            if (!flood.ListenerInside) { route.GapPoint = mouthOfIt; return; }
            // Inside the duct, listening: from as far down it towards the mouth as can be seen from here.
            route.GapPoint = flood.Origin;
            for (int i = 0; i < trail.Count; i++)
            {
                Vector3 point = flood.Origin + (Vector3)trail[i] * Cell;
                if (!Physics.Linecast(flood.Origin, point, mask, QueryTriggerInteraction.Ignore)) { route.GapPoint = point; break; }
            }
        }

        /// <summary>Tests: the whole duct search from a point at once, and where it went.</summary>
        internal string DuctSurvey(Vector3 inside, Vector3 outside)
        {
            int mask = LayerMask.GetMask("Wall", "Ground", "Ceiling", "Door");
            var route = new Route();
            var flood = new Flood();
            flood.Start(inside, outside, false);
            bool backFaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            var leaks = new Dictionary<string, int>();
            int insideCells = 0;
            Vector3 low = inside, high = inside;
            try
            {
                for (int i = 0; i < 40 && flood.Running; i++) Spread(route, flood, mask);
                foreach (var pair in flood.Out)
                {
                    Vector3 at = flood.Origin + (Vector3)pair.Key * Cell;
                    if (pair.Value == 0)
                    {
                        insideCells++; low = Vector3.Min(low, at); high = Vector3.Max(high, at);
                        RaycastHit roof;
                        if (Physics.Raycast(at, Vector3.up, out roof, 1.6f, mask, QueryTriggerInteraction.Ignore))
                        {
                            string roofKey = "UNDER " + GraphDump.Path(roof.collider.transform) + " "
                                + roof.collider.bounds.min.ToString("0.0") + ".." + roof.collider.bounds.max.ToString("0.0");
                            int k; leaks.TryGetValue(roofKey, out k); leaks[roofKey] = k + 1;
                        }
                        continue;
                    }
                    if (pair.Value != 1) continue;
                    RaycastHit hit;
                    string above = Physics.Raycast(at, Vector3.up, out hit, 1.6f, mask, QueryTriggerInteraction.Ignore) ? hit.collider.name + "@" + hit.distance.ToString("0.0") : "open";
                    string key = above + " near " + new Vector3(Mathf.Round(at.x), Mathf.Round(at.y), Mathf.Round(at.z));
                    int n; leaks.TryGetValue(key, out n); leaks[key] = n + 1;
                }
            }
            finally { Physics.queriesHitBackfaces = backFaces; }
            return "cells=" + flood.Came.Count + " searched=" + flood.Out.Count + " inside=" + insideCells + " duct box " + low.ToString("0.0") + ".." + high.ToString("0.0")
                + " found=" + route.GapFound + (route.GapFound ? " at " + route.GapPoint.ToString("0.0") + " length " + route.GapLength.ToString("0.0") : "")
                + " | left the duct at: " + string.Join(" ; ", leaks.OrderByDescending(x => x.Value).Take(14).Select(x => x.Key + " x" + x.Value).ToArray());
        }

        /// <summary>
        /// The opening a voice comes out of when the straight line is blocked: of the points the speaker can see (along a
        /// spread of directions from their mouth: down a duct, out of a doorway, through a window or a gap), the one the
        /// listener can see too that makes the way through it shortest. A voice in a vent comes out of the vent's opening;
        /// one behind a window, out of the window. Heard from there, as loud as that way is long, a little duller for the
        /// bend (more out of a duct's small opening). Worked out four times a second.
        /// </summary>
        private Route OpeningOf(int speaker, Vector3 mouth, Vector3 ear, int mask)
        {
            Route route;
            if (!routes.TryGetValue(speaker, out route)) routes[speaker] = route = new Route { Heard = mouth };
            if (Time.unscaledTime < route.NextGap) return route;
            route.NextGap = Time.unscaledTime + 0.25f;
            route.GapFound = false;
            float best = VoicePlayback.Hearing;
            foreach (var way in Search)
            {
                RaycastHit hit;
                float reach = Physics.Raycast(mouth, way, out hit, 20f, mask, QueryTriggerInteraction.Ignore) ? hit.distance - 0.25f : 20f;
                if (reach < 0.3f) continue;
                foreach (float part in Along)
                {
                    Vector3 point = mouth + way * (reach * part);
                    float length = reach * part + Vector3.Distance(point, ear);
                    if (length >= best || Physics.Linecast(ear, point, mask, QueryTriggerInteraction.Ignore)) continue;
                    best = length; route.GapPoint = point; route.GapFound = true;
                }
            }
            route.GapLength = best;
            route.GapGain = 0.85f;
            route.GapCutoff = 7000f;
            return route;
        }

        /// <summary>
        /// The way a voice comes round when the straight line is blocked: along the floor people walk on (the navigation
        /// mesh the Nun walks), through doorways, corridors and stairwells. It is heard from the furthest point of that way
        /// the listener can see (the open door it comes out of, the corridor's corner, the top of the stairs), as loud as
        /// the whole way is long, a little duller for each corner it bends round; a closed door on the way takes most of
        /// it and muffles it. Worked out four times a second.
        /// </summary>
        private Route RouteOf(int speaker, Vector3 feet, Vector3 ear, int mask)
        {
            Route route;
            if (!routes.TryGetValue(speaker, out route)) routes[speaker] = route = new Route { Heard = feet };
            if (Time.unscaledTime < route.NextProbe) return route;
            route.NextProbe = Time.unscaledTime + 0.25f;
            route.Found = false;
            UnityEngine.AI.NavMeshHit start, end;
            if (!UnityEngine.AI.NavMesh.SamplePosition(feet, out start, 1.5f, UnityEngine.AI.NavMesh.AllAreas)
                || !UnityEngine.AI.NavMesh.SamplePosition(ear + Vector3.down * 1.5f, out end, 1.5f, UnityEngine.AI.NavMesh.AllAreas)) return route;
            if (!UnityEngine.AI.NavMesh.CalculatePath(start.position, end.position, UnityEngine.AI.NavMesh.AllAreas, navPath)
                || navPath.status == UnityEngine.AI.NavMeshPathStatus.PathInvalid)
                return route;
            int cornerCount = navPath.GetCornersNonAlloc(corners);
            if (cornerCount < 2 || (corners[cornerCount - 1] - end.position).sqrMagnitude > 4f) return route; // It does not get there (or too far round).
            float length = 0f;
            int doors = 0, doorMask = LayerMask.GetMask("Door");
            for (int i = 1; i < cornerCount; i++)
            {
                length += Vector3.Distance(corners[i - 1], corners[i]);
                if (Physics.Linecast(corners[i - 1] + Vector3.up, corners[i] + Vector3.up, doorMask, QueryTriggerInteraction.Ignore)) doors++;
            }
            // Back from the listener: the last corner still in sight is where the voice comes into the room.
            int seen = cornerCount - 1;
            for (int i = cornerCount - 2; i >= 0; i--)
            {
                if (Physics.Linecast(ear, corners[i] + Vector3.up * 1.3f, mask, QueryTriggerInteraction.Ignore)) break;
                seen = i;
            }
            route.Found = true;
            route.Length = length + Vector3.Distance(end.position, ear) * 0.5f;
            route.Apparent = corners[seen] + Vector3.up * 1.3f;
            route.Bends = seen;
            route.Doors = doors;
            route.Gain = Mathf.Pow(0.93f, Mathf.Min(seen, 6)) * Mathf.Pow(Door, doors);
            route.Cutoff = doors > 0 ? 1100f : Mathf.Max(2500f, 12000f - 1400f * seen);
            return route;
        }

        internal Route RouteFor(int speaker) { Route route; return routes.TryGetValue(speaker, out route) ? route : null; }

        // ---------- How a voice reaches the listener through the building ----------

        internal struct Heard { public float Gain, Cutoff, ShapeGain, ShapeCutoff; public bool Blocked, Floor; }
        /// <summary>What one wall, one closed door and one floor (right under) let through of a voice.</summary>
        internal const float Wall = 0.42f, Door = 0.5f, Floor = 0.22f;
        private sealed class Path { public float NextProbe, Gain = 1f, Cutoff = 16000f, Distance, ShapeGain = 1f, ShapeCutoff = 16000f; public int Walls, Slabs, Doors, Clear; }
        private readonly Dictionary<int, Path> paths = new Dictionary<int, Path>();
        private static readonly Vector3[] Around = { new Vector3(0.45f, 0f, 0f), new Vector3(-0.45f, 0f, 0f),
            new Vector3(0f, 0.35f, 0f), new Vector3(0f, -0.35f, 0f) };

        /// <summary>
        /// Straight through the building, as an ordinary interior wall lets speech through: a voice is heard, not understood
        /// (everything above 800 Hz gone, about 40% of it left; a second wall less again); a closed door is the wall's weak
        /// part (half, muffled to 1.1 kHz), and an open one lets almost all of it through (the way round, RouteOf). A floor
        /// or ceiling lets through only a dull, wordless murmur, and only from about right under or over the listener; two
        /// floors, nothing. On the same floor the way round, through doors and along corridors, may be louder still
        /// (RouteOf). Sound bending round the edge of a doorway (a slightly offset path that is clear) brings much of it
        /// back, dulled. Far away the air takes the top off a voice. Talking away from the listener is quieter and duller
        /// too. Measured 15 times a second and eased, so it does not flicker.
        /// </summary>
        private Heard Propagation(int speaker, Vector3 ear, Vector3 mouth, Vector3 facing, int mask)
        {
            Path path;
            if (!paths.TryGetValue(speaker, out path)) paths[speaker] = path = new Path();
            if (Time.unscaledTime >= path.NextProbe)
            {
                path.NextProbe = Time.unscaledTime + 1f / 15f;
                float gain = 1f, cutoff = 16000f;
                Vector3 line = mouth - ear;
                float length = line.magnitude;
                if (length > 0.05f)
                {
                    Vector3 way = line / length;
                    int walls = 0, slabs = 0, doors = 0, wallsBack = 0, slabsBack = 0, doorsBack = 0;
                    Count(Physics.RaycastNonAlloc(ear, way, crossed, length, mask, QueryTriggerInteraction.Ignore), ref walls, ref slabs, ref doors);
                    Count(Physics.RaycastNonAlloc(mouth, -way, crossed, length, mask, QueryTriggerInteraction.Ignore),
                        ref wallsBack, ref slabsBack, ref doorsBack);
                    walls = Math.Max(walls, wallsBack); slabs = Math.Max(slabs, slabsBack); doors = Math.Max(doors, doorsBack);
                    path.Walls = walls; path.Slabs = slabs; path.Doors = doors; path.Distance = length; path.Clear = -1;
                    if (walls + slabs + doors > 0)
                    {
                        if (slabs > 0)
                        {
                            // Another floor: only straight through it, from about right under (or over) the listener, as a dull,
                            // wordless murmur (nothing of speech above 260 Hz) that fades out within a few steps to the side.
                            // Two floors apart, nothing.
                            float across = new Vector2(line.x, line.z).magnitude;
                            gain *= slabs == 1 ? Floor * (1f - Mathf.InverseLerp(1.5f, 4f, across)) : 0f;
                            cutoff = 260f;
                        }
                        if (walls > 0) { gain *= Wall * Mathf.Pow(0.45f, walls - 1); cutoff = Mathf.Min(cutoff, walls == 1 ? 800f : 600f); }
                        if (doors > 0) { gain *= Mathf.Pow(Door, doors); cutoff = Mathf.Min(cutoff, 1100f); }
                        // Round the edge of an opening: some offset paths clear.
                        int clear = 0;
                        foreach (var offset in Around)
                        {
                            Vector3 side = Quaternion.LookRotation(way) * offset;
                            if (!Physics.Linecast(ear + side, mouth + side, mask, QueryTriggerInteraction.Ignore)) clear++;
                        }
                        path.Clear = clear;
                        if (clear > 0 && slabs == 0) // Round a doorway's edge, not round a floor.
                        {
                            float bend = clear / (float)Around.Length;
                            gain = Mathf.Max(gain, 0.5f * bend + 0.15f);
                            cutoff = Mathf.Max(cutoff, 2200f + 1800f * bend);
                        }
                    }
                    // Voices are directional: talking away from the listener is quieter and loses its edge.
                    float towards = Vector3.Dot(facing, -way);
                    float shapeGain = 1f, shapeCutoff = 16000f;
                    if (towards < 0f) { shapeGain = Mathf.Lerp(1f, 0.72f, -towards); shapeCutoff = Mathf.Lerp(16000f, 6000f, -towards); }
                    // The air: past 10 m a voice loses its top, more the further it goes.
                    shapeCutoff = Mathf.Min(shapeCutoff, Mathf.Lerp(16000f, 4500f, Mathf.InverseLerp(10f, VoicePlayback.Hearing, length)));
                    gain *= shapeGain; cutoff = Mathf.Min(cutoff, shapeCutoff);
                    path.ShapeGain = Mathf.Lerp(path.ShapeGain, shapeGain, 0.5f);
                    path.ShapeCutoff = Mathf.Lerp(path.ShapeCutoff, shapeCutoff, 0.5f);
                }
                path.Gain = Mathf.Lerp(path.Gain, gain, 0.5f);
                path.Cutoff = Mathf.Lerp(path.Cutoff, cutoff, 0.5f);
            }
            return new Heard { Gain = path.Gain, Cutoff = path.Cutoff, ShapeGain = path.ShapeGain,
                ShapeCutoff = path.ShapeCutoff, Blocked = path.Walls + path.Slabs + path.Doors > 0, Floor = path.Slabs > 0 };
        }

        /// <summary>How a sound from this teammate reaches the local player (a whistle): the same walls, floors and doors
        /// as their voice.</summary>
        internal Heard HearFrom(int speaker, Vector3 mouth, Vector3 facing)
        {
            var listener = Camera.main ? Camera.main.transform : null;
            if (!listener) return new Heard { Gain = 1f, Cutoff = 16000f };
            return Propagation(speaker, listener.position, mouth, facing, LayerMask.GetMask("Wall", "Ground", "Ceiling", "Door"));
        }

        /// <summary>Tests: how each teammate is heard here (distance, what is in between, loudness, muffling).</summary>
        internal string DescribeHearing()
        {
            return string.Join(" ; ", paths.Select(p =>
            {
                VoicePlayback playback;
                players.TryGetValue(p.Key, out playback);
                float falloff = playback
                    ? playback.Source.GetCustomCurve(AudioSourceCurveType.CustomRolloff).Evaluate(Mathf.Clamp01(p.Value.Distance / VoicePlayback.Hearing))
                    : -1f;
                return "peer " + p.Key + " " + p.Value.Distance.ToString("0.0") + "m walls=" + p.Value.Walls + " floors=" + p.Value.Slabs + " doors=" + p.Value.Doors + " roundEdge=" + p.Value.Clear
                    + " path=" + p.Value.Gain.ToString("0.000") + " distance=" + falloff.ToString("0.000") + " total=" + (p.Value.Gain * Mathf.Max(0f, falloff)).ToString("0.000")
                    + " (" + (20f * Mathf.Log10(Mathf.Max(1e-4f, p.Value.Gain * Mathf.Max(0f, falloff)))).ToString("0") + " dB) cutoff=" + p.Value.Cutoff.ToString("0") + "Hz"
                    + (playback ? " echo=" + playback.Echo.enabled + " lowpass=" + playback.LowPass.cutoffFrequency.ToString("0") + " audibility=" + playback.Audibility.ToString("0.000") : "")
                    + RouteText(p.Key, playback);
            }).ToArray());
        }

        private string RouteText(int speaker, VoicePlayback playback)
        {
            Route route;
            if (!routes.TryGetValue(speaker, out route)) return " route=-";
            return (route.Found ? " route=" + route.Length.ToString("0.0") + "m bends=" + route.Bends + " doors=" + route.Doors + " gain=" + route.Gain.ToString("0.00") : " route=none")
                + (route.GapFound ? " opening=" + route.GapPoint.ToString("0.0") + " " + route.GapLength.ToString("0.0") + "m" : " opening=none")
                + (route.Cells > 0 ? " ductCells=" + route.Cells + " furthest=" + route.Furthest.ToString("0.0") + " leftTheDuctAt=" + route.Escape.ToString("0.0") : "")
                + " HEARD " + (route.Way == 1 ? "ROUND FROM " + route.Apparent.ToString("0.0") : route.Way == 2 ? "OUT OF THE OPENING AT " + route.GapPoint.ToString("0.0") : "STRAIGHT")
                + (playback ? " source=" + playback.transform.position.ToString("0.0") : "");
        }

        private static readonly RaycastHit[] crossed = new RaycastHit[24];

        /// <summary>How much of a voice gets from one point to another straight through the building (walls, closed doors,
        /// a floor right under), by the same rules players hear by. For the Nun's ears.</summary>
        internal static float Through(Vector3 from, Vector3 to)
        {
            Vector3 line = to - from;
            float length = line.magnitude;
            if (length < 0.05f) return 1f;
            int mask = LayerMask.GetMask("Wall", "Ground", "Ceiling", "Door");
            int walls = 0, slabs = 0, doors = 0, wallsBack = 0, slabsBack = 0, doorsBack = 0;
            Count(Physics.RaycastNonAlloc(from, line / length, crossed, length, mask, QueryTriggerInteraction.Ignore), ref walls, ref slabs, ref doors);
            Count(Physics.RaycastNonAlloc(to, -line / length, crossed, length, mask, QueryTriggerInteraction.Ignore),
                ref wallsBack, ref slabsBack, ref doorsBack);
            walls = Math.Max(walls, wallsBack); slabs = Math.Max(slabs, slabsBack); doors = Math.Max(doors, doorsBack);
            float gain = 1f;
            if (slabs > 0) gain *= slabs == 1 ? Floor * (1f - Mathf.InverseLerp(1.5f, 4f, new Vector2(line.x, line.z).magnitude)) : 0f;
            if (walls > 0) gain *= Wall * Mathf.Pow(0.45f, walls - 1);
            if (doors > 0) gain *= Mathf.Pow(Door, doors);
            return gain;
        }

        /// <summary>Surfaces a straight line crosses: floors and ceilings (lying flat), doors, and walls.</summary>
        private static void Count(int count, ref int walls, ref int slabs, ref int doors)
        {
            int door = LayerMask.NameToLayer("Door");
            for (int i = 0; i < count; i++)
            {
                var hit = crossed[i];
                if (hit.collider.gameObject.layer == door) doors++;
                else if (hit.collider.name.IndexOf("Vent", StringComparison.Ordinal) >= 0) walls++; // A duct's thin metal, not a floor.
                else if (Mathf.Abs(hit.normal.y) > 0.7f) slabs++;
                else walls++;
            }
        }

        private static bool Finite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        }

        // ---------- Acoustics: the space around each speaker ----------

        internal sealed class Room { public float NextProbe, Size, Enclosure, Height, Width, Length, Nearest, Farthest; public bool Sky; }
        private readonly float[] level = new float[13]; // One measure's probe distances.
        private static readonly Vector3[] Probes = BuildProbes();
        private static Vector3[] BuildProbes()
        {
            var probes = new List<Vector3>();
            for (int i = 0; i < 8; i++) probes.Add(Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward);            // Walls.
            for (int i = 0; i < 4; i++) probes.Add(Quaternion.Euler(-40f, 22.5f + i * 90f, 0f) * Vector3.forward);  // Upper corners.
            probes.Add(Vector3.up);                                                                                 // Ceiling.
            return probes.ToArray();
        }

        /// <summary>How enclosed (share of probes that hit something) and how big (mean hit distance) the
        /// space around a speaker is, re-measured twice a second.</summary>
        private Room Measure(int speaker, Vector3 mouth)
        {
            Room room;
            if (!rooms.TryGetValue(speaker, out room)) rooms[speaker] = room = new Room();
            if (Time.unscaledTime < room.NextProbe) return room;
            room.NextProbe = Time.unscaledTime + 0.5f;
            Fill(room, mouth);
            return room;
        }

        private void Fill(Room room, Vector3 mouth)
        {
            int mask = LayerMask.GetMask("Wall", "Ground", "Ceiling", "Door", "Furniture");
            int hits = 0; float total = 0f;
            room.Height = 30f;
            for (int i = 0; i < Probes.Length; i++)
            {
                RaycastHit hit;
                level[i] = 30f;
                if (!Physics.Raycast(mouth, Probes[i], out hit, 30f, mask, QueryTriggerInteraction.Ignore)) continue;
                hits++; total += hit.distance;
                level[i] = hit.distance;
                if (Probes[i] == Vector3.up) room.Height = hit.distance;
            }
            room.Enclosure = hits / (float)Probes.Length;
            room.Size = hits == 0 ? 30f : total / hits;
            // Across and along: wall to wall through the speaker, the narrowest and the longest way (the first eight probes
            // go round at 45 degrees, each opposite the one four on).
            room.Width = 60f; room.Length = 0f;
            for (int i = 0; i < 4; i++)
            {
                float span = level[i] + level[i + 4];
                room.Width = Mathf.Min(room.Width, span); room.Length = Mathf.Max(room.Length, span);
            }
            // The nearest and farthest walls round about (30 m: none), and whether there is open sky overhead.
            room.Nearest = 30f; room.Farthest = 0f;
            for (int i = 0; i < 8; i++)
            {
                if (level[i] >= 29.9f) continue;
                room.Nearest = Mathf.Min(room.Nearest, level[i]); room.Farthest = Mathf.Max(room.Farthest, level[i]);
            }
            room.Sky = room.Height >= 29.9f;
        }

        /// <summary>Tests: how a voice spoken here (a mouth this high) would sound from this far away: the space measured,
        /// and the effects it settles on.</summary>
        internal string DescribeSpace(Vector3 mouth, float distance)
        {
            var room = new Room();
            Fill(room, mouth);
            var playback = VoicePlayback.Create("NMM_SpaceTest");
            try
            {
                for (int i = 0; i < 120; i++) Acoustics(playback, room, false, distance, null);
                var reverb = playback.Reverb; var echo = playback.Echo;
                return "size=" + room.Size.ToString("0.0") + " width=" + room.Width.ToString("0.0") + " length=" + room.Length.ToString("0.0") + " height=" + room.Height.ToString("0.0")
                    + " enclosed=" + room.Enclosure.ToString("0.00") + " nearest=" + room.Nearest.ToString("0.0") + " farthest=" + room.Farthest.ToString("0.0") + " sky=" + room.Sky
                    + " | room=" + reverb.room.ToString("0") + " refl=" + reverb.reflectionsLevel.ToString("0") + "@" + (reverb.reflectionsDelay * 1000f).ToString("0") + "ms tail=" + reverb.reverbLevel.ToString("0") + "@" + (reverb.reverbDelay * 1000f).ToString("0") + "ms decay=" + reverb.decayTime.ToString("0.0") + "s hf=" + reverb.roomHF.ToString("0")
                    + " | echo=" + (echo.enabled ? echo.delay.ToString("0") + "ms wet=" + echo.wetMix.ToString("0.00") + " fb=" + echo.decayRatio.ToString("0.00") : "off")
                    + " | hall=" + Hall(room).ToString("0.00") + " open=" + Outdoors(room).ToString("0.00") + " | hits " + string.Join(", ", Probes.Select(d =>
                    {
                        RaycastHit hit;
                        if (!Physics.Raycast(mouth, d, out hit, 30f,
                            LayerMask.GetMask("Wall", "Ground", "Ceiling", "Door", "Furniture"),
                            QueryTriggerInteraction.Ignore))
                            return "-";
                        var shape = hit.collider.GetComponent<Renderer>() ?? hit.collider.GetComponentInParent<Renderer>();
                        return hit.collider.name + "[" + LayerMask.LayerToName(hit.collider.gameObject.layer)
                            + (hit.collider.sharedMaterial ? " pm=" + hit.collider.sharedMaterial.name : "")
                            + (shape && shape.sharedMaterial ? " m=" + shape.sharedMaterial.name : "") + "]";
                    }).Distinct().ToArray());
            }
            finally { UnityEngine.Object.Destroy(playback.gameObject); }
        }

        internal Room RoomOf(int speaker) { Room room; return rooms.TryGetValue(speaker, out room) ? room : null; }

        // Big, bare, enclosed rooms ring; small or cluttered ones barely; open air stays dry. A small hard room (a
        // bathroom, a stall, a closet) is boxy: strong, bright, early reflections. A stairwell (tall and narrow) rings on
        // up the shaft. The further away someone is, the more of what reaches you is the room rather than their voice.
        // In a long narrow passage (a corridor, a tunnel) a voice comes back off its far end: an echo, louder the further
        // away the speaker is. A voice in a vent is thin and metallic with a tight slap-back echo.
        private static void Acoustics(VoicePlayback playback, Room room, bool vent, float distance, Room around, bool ring = false)
        {
            float big = room == null ? 0f : Mathf.InverseLerp(2f, 12f, room.Size);
            float enclosed = room == null ? 0f : Mathf.Clamp01((room.Enclosure - 0.35f) / 0.65f);
            float far = Mathf.InverseLerp(4f, 25f, distance);
            float wet = Mathf.Clamp01(enclosed * Mathf.Lerp(0.3f, 1f, big) + enclosed * far * 0.35f);
            // The listener's own room rings with whatever comes into it, too (a voice from the next room into a big hall).
            if (around != null && room != null && distance > 4f)
                wet = Mathf.Max(wet, Mathf.Clamp01((around.Enclosure - 0.35f) / 0.65f) * Mathf.Lerp(0.3f, 1f, Mathf.InverseLerp(2f, 12f, around.Size)) * 0.6f);
            float boxy = room == null ? 0f
                : enclosed * Mathf.InverseLerp(3f, 1.4f, room.Width) * Mathf.InverseLerp(9f, 4f, room.Length) * Mathf.InverseLerp(3.5f, 2.2f, room.Height);
            float shaft = room == null ? 0f : enclosed * Mathf.InverseLerp(3.5f, 2f, room.Width) * Mathf.InverseLerp(4f, 8f, room.Height);
            var reverb = playback.Reverb;
            float targetRoom = wet < 0.02f && boxy < 0.05f && shaft < 0.05f ? -10000f : Mathf.Lerp(-2600f, -250f, Mathf.Max(wet, boxy * 0.8f, shaft));
            reverb.room = Mathf.Lerp(reverb.room, targetRoom, 0.15f);
            reverb.roomHF = Mathf.Lerp(Mathf.Lerp(-1400f, -350f, big), -150f, boxy);
            reverb.decayTime = Mathf.Lerp(reverb.decayTime, Mathf.Lerp(Mathf.Lerp(0.45f, 3.2f, big), 2.4f, shaft), 0.15f);
            reverb.reflectionsLevel = Mathf.Lerp(Mathf.Lerp(-1800f, -150f, wet), 400f, boxy);
            reverb.reflectionsDelay = Mathf.Lerp(Mathf.Lerp(0.004f, 0.045f, big), 0.002f, boxy);
            reverb.reverbLevel = Mathf.Lerp(-1600f, 250f, Mathf.Max(wet * big, shaft * 0.8f));
            reverb.reverbDelay = Mathf.Lerp(Mathf.Lerp(0.008f, 0.07f, big), 0.03f, shaft);
            Echoes(playback, room, vent, distance, 0f, true, ring);
        }

        /// <summary>The echoes the probes find (back off the end of a passage, a big hall's far wall, a building's wall
        /// out in the open), a vent's ring, and a vent's thinness: with either reverb.</summary>
        private static void Echoes(VoicePlayback playback, Room room, bool vent, float distance, float lead = 0f, bool thin = true, bool ring = false)
        {
            float enclosed = room == null ? 0f : Mathf.Clamp01((room.Enclosure - 0.35f) / 0.65f);
            float far = Mathf.InverseLerp(4f, 25f, distance);
            // A duct thins a voice a little (its bass leaks out through the metal); not to a telephone's.
            playback.HighPass.cutoffFrequency = Mathf.Lerp(playback.HighPass.cutoffFrequency, vent && thin ? (ring || TestOldVent ? 720f : 300f) : 10f, 0.2f);
            float tunnel = room == null || vent ? 0f : enclosed * Mathf.InverseLerp(3.5f, 2f, room.Width) * Mathf.InverseLerp(8f, 20f, room.Length);
            var echo = playback.Echo;
            if (vent && (ring || TestOldVent))
            {
                // Through a duct's sheet metal: the metal rings with the voice (a short ring that feeds back).
                echo.enabled = true;
                echo.delay = 11f; echo.decayRatio = 0.62f; echo.wetMix = 0.6f; echo.dryMix = 0.85f;
            }
            else if (vent)
            {
                // In a duct the voice comes back off its bends and far end: a sparse echo, as long after as the duct is
                // long, that soon dies. (Its tinny ring is the reverb's: small, hard metal. A short delay fed back on itself,
                // as this was, rings at a pitch and turns a voice into a robot's.)
                float duct = room != null ? Mathf.Clamp(room.Length, 2f, 20f) : 6f;
                echo.enabled = true;
                Delay(echo, duct * 2f / 343f * 1000f, 25f, 120f, lead);
                echo.decayRatio = 0.2f; echo.dryMix = 1f;
                echo.wetMix = Mathf.Lerp(echo.wetMix, 0.22f, 0.2f);
            }
            else if (tunnel > 0.1f)
            {
                // Down a passage: back off the far end, there and back at the speed of sound.
                echo.enabled = true;
                Delay(echo, room.Length * 2f / 343f * 1000f, 40f, 180f, lead);
                echo.decayRatio = 0.3f; echo.dryMix = 1f;
                echo.wetMix = Mathf.Lerp(echo.wetMix, tunnel * Mathf.Lerp(0.12f, 0.45f, far), 0.2f);
            }
            else if (room != null && Hall(room) > 0.1f)
            {
                // A big bare hall (the church, the big rooms): a shout comes back off the far wall, a beat after the voice.
                float hall = Hall(room);
                echo.enabled = true;
                Delay(echo, room.Farthest * 2f / 343f * 1000f, 50f, 200f, lead);
                echo.decayRatio = 0.22f; echo.dryMix = 1f;
                echo.wetMix = Mathf.Lerp(echo.wetMix, hall * Mathf.Lerp(0.08f, 0.22f, far), 0.2f);
            }
            else if (room != null && Outdoors(room) > 0.1f)
            {
                // Out in the open with a building near (the courtyard, outside the school at night): one clean slap back
                // off its wall, the further the wall the later, and nothing else (no room to ring).
                float open = Outdoors(room);
                echo.enabled = true;
                Delay(echo, room.Nearest * 2f / 343f * 1000f, 35f, 180f, lead);
                echo.decayRatio = 0.12f; echo.dryMix = 1f;
                echo.wetMix = Mathf.Lerp(echo.wetMix, open * Mathf.Lerp(0.14f, 0.28f, far), 0.2f);
            }
            else echo.enabled = false;
        }

        /// <summary>An echo's delay, in steps of 5 ms: a delay that slides warbles the voice (and the filter keeps whole
        /// milliseconds, so easing towards it stalls short).</summary>
        private static void Delay(AudioEchoFilter echo, float ms, float least, float most, float lead)
        {
            float delay = Mathf.Round(Mathf.Max(10f, Mathf.Clamp(ms, least, most) - lead) / 5f) * 5f;
            if (Mathf.Abs(echo.delay - delay) > 4f) echo.delay = delay;
        }

        /// <summary>
        /// The reverb the rays found, as the voice's reverb: the space the listener is in and the one the speaker is in,
        /// more of the speaker's the further away they are (a voice from the far end of the church brings the church
        /// with it). EAX/EFX levels are linear gains; Unity's filter takes millibels. The rays give no early reflections,
        /// so those follow the tail, first back off the nearest wall; where the rays run out before a small hard room has
        /// died away (a tiled bathroom), its decay is worked out from its size and what its surfaces soak up (Eyring).
        /// The reverb is kept up as the voice fades with distance (in a real room it fades less than the voice does).
        /// </summary>
        private static void RaytracedReverb(VoicePlayback playback, VAudio.EAXReverb? atListener,
            VAudio.EAXReverb? atSpeaker, Room around, Room room, float distance, bool vent, bool ring = false)
        {
            var p = RaytracedParams(atListener, atSpeaker, around, room, distance);
            var reverb = playback.Reverb;
            const float k = 0.15f;
            reverb.dryLevel = 0f;
            reverb.room = Mathf.Lerp(reverb.room, p.Room, k);
            reverb.roomHF = Mathf.Lerp(reverb.roomHF, p.RoomHF, k);
            reverb.roomLF = Mathf.Lerp(reverb.roomLF, p.RoomLF, k);
            reverb.decayTime = Mathf.Lerp(reverb.decayTime, p.Decay, k);
            reverb.decayHFRatio = Mathf.Lerp(reverb.decayHFRatio, p.DecayHFRatio, k);
            reverb.reflectionsLevel = Mathf.Lerp(reverb.reflectionsLevel, p.Reflections, k);
            reverb.reflectionsDelay = Mathf.Lerp(reverb.reflectionsDelay, p.ReflectionsDelay, k);
            reverb.reverbLevel = Mathf.Lerp(reverb.reverbLevel, p.Reverb, k);
            reverb.reverbDelay = Mathf.Lerp(reverb.reverbDelay, p.ReverbDelay, k);
            reverb.diffusion = p.Diffusion; reverb.density = p.Density;
            reverb.hfReference = p.HFReference; reverb.lfReference = p.LFReference;
            // A duct's echo or ring, as the rays muffle it (not thinned).
            Echoes(playback, atSpeaker.HasValue ? room : around, vent, distance, 0f, false, ring);
        }

        /// <summary>A reverb's settings, in Unity's units (millibels, seconds, percent).</summary>
        internal struct ReverbParams { public float Room, RoomHF, RoomLF, Decay, DecayHFRatio, Reflections, ReflectionsDelay, Reverb, ReverbDelay, Diffusion, Density, HFReference, LFReference; }

        /// <summary>
        /// The reverb the rays found, as settings: the space the listener is in and the one the speaker is in, more of the
        /// speaker's the further away they are (a voice from the far end of the church brings the church with it). EAX/EFX
        /// levels are linear gains; Unity's take millibels. The rays give no early reflections, so those follow the tail,
        /// first back off the nearest wall; where the rays run out before a small hard room has died away (a tiled
        /// bathroom), its decay is taken from the other end, or worked out from its size and surfaces (Eyring). The reverb
        /// is kept up as a voice fades with distance (in a real room it fades less than the voice does).
        /// </summary>
        internal static ReverbParams RaytracedParams(VAudio.EAXReverb? atListener, VAudio.EAXReverb? atSpeaker, Room around, Room room, float distance)
        {
            var a = atListener ?? atSpeaker.Value; var b = atSpeaker ?? a;
            Room roomA = atListener.HasValue ? around : room, roomB = atSpeaker.HasValue ? room : roomA;
            float w = atListener.HasValue && atSpeaker.HasValue ? Mathf.InverseLerp(2f, 12f, distance) * 0.5f : 0f;
            Func<Func<VAudio.EAXReverb, float>, float> mix = f => Mathf.Lerp(f(a), f(b), w);
            float lasting = -1000f * Mathf.Log10(Mathf.Max(0.1f, VoicePlayback.Loudness(distance)));
            Room near = roomB ?? roomA;
            bool open = near != null && near.Sky && near.Enclosure < 0.75f; // Out under the sky: what rings is only the walls about.
            float late = Mathf.Min(10f, mix(e => e.LateReverbGain) * LateBoost * (open ? 0.7f : 1f));
            float reflections = Mathf.Min(3.16f, Mathf.Max(mix(e => e.ReflectionsGain), late * 0.6f));
            float firstBack = near == null || near.Nearest >= 29.9f ? 0f : near.Nearest * 2f / 343f;
            // Where the rays could not measure one end's decay (0.1 s), the other end's (the same building, near by), and
            // only where neither measured, worked out.
            float decayA = a.DecayTime > 0.15f ? a.DecayTime : b.DecayTime > 0.15f ? b.DecayTime : Decay(a, roomA);
            float decayB = b.DecayTime > 0.15f ? b.DecayTime : a.DecayTime > 0.15f ? a.DecayTime : Decay(b, roomB);
            return new ReverbParams
            {
                Room = Millibels(mix(e => e.Gain), 0f),
                RoomHF = Millibels(mix(e => e.GainHF), 0f),
                RoomLF = Millibels(mix(e => e.GainLF), 0f),
                Decay = Mathf.Clamp(Mathf.Lerp(decayA, decayB, w), 0.1f, 20f),
                DecayHFRatio = Mathf.Clamp(mix(e => e.DecayHFRatio), 0.1f, 2f),
                Reflections = Mathf.Clamp(Millibels(reflections, 1000f) + lasting, -10000f, 1000f),
                ReflectionsDelay = Mathf.Clamp(Mathf.Max(mix(e => e.ReflectionsDelay), firstBack), 0f, 0.3f),
                Reverb = Mathf.Clamp(Millibels(late, 2000f) + lasting, -10000f, 2000f),
                ReverbDelay = Mathf.Clamp(mix(e => e.LateReverbDelay), 0f, 0.1f),
                Diffusion = Mathf.Clamp(mix(e => e.Diffusion) * 100f, 0f, 100f),
                Density = Mathf.Clamp(mix(e => e.Density) * 100f, 0f, 100f),
                HFReference = Mathf.Clamp(mix(e => e.HFReference), 1000f, 20000f),
                LFReference = Mathf.Clamp(mix(e => e.LFReference), 20f, 1000f),
            };
        }

        /// <summary>The rays' late reverb runs some 6 dB under the EAX room presets for the same rooms (a tiled bathroom,
        /// a corridor): brought up to them.</summary>
        private const float LateBoost = 2f;

        /// <summary>The rays' decay time; where they could not measure one (they ran out first: 0.1 s), Eyring's, from the
        /// mean free path (the probes) and the absorption the rays met, sound escaping through openings counted as
        /// absorbed.</summary>
        private static float Decay(VAudio.EAXReverb e, Room room)
        {
            if (e.DecayTime > 0.15f || room == null) return Mathf.Clamp(e.DecayTime, 0.1f, 20f);
            float freePath = room.Size * 1.2f;
            float absorbed = Mathf.Clamp((e.MaterialAbsorptionLF + e.MaterialAbsorptionHF) * 0.5f + (1f - room.Enclosure) * 0.5f, 0.02f, 0.95f);
            return Mathf.Clamp(13.8f * freePath / (343f * -Mathf.Log(1f - absorbed)), 0.2f, 4f);
        }

        /// <summary>The rays' high-frequency loss (relative to the low) as a low-pass cutoff: through a plaster wall and a
        /// shut door (high a tenth of the low) about 900 Hz; clear, the whole range.</summary>
        internal static float RayCutoff(VAudio.LowPass rays)
        {
            float ratio = Mathf.Clamp01(rays.GainHF / Mathf.Max(0.001f, rays.GainLF));
            return Mathf.Clamp(22000f * Mathf.Pow(ratio, 1.4f), 500f, 22000f);
        }

        private static float Millibels(float gain, float most) { return gain <= 0.00001f ? -10000f : Mathf.Clamp(2000f * Mathf.Log10(gain), -10000f, most); }

        /// <summary>Tests: what the rays found for the listener and each voice, against the mod's own hearing.</summary>
        internal string DescribeRaytraced()
        {
            return DescribeVoices() + " ; " + World.Describe();
        }

        private string DescribeVoices()
        {
            Func<VAudio.EAXReverb?, string> eax = e => !e.HasValue ? "-" : "gain=" + e.Value.Gain.ToString("0.00") + " hf=" + e.Value.GainHF.ToString("0.00") + " decay=" + e.Value.DecayTime.ToString("0.00") + "s refl=" + e.Value.ReflectionsGain.ToString("0.00") + "@" + (e.Value.ReflectionsDelay * 1000f).ToString("0") + "ms late=" + e.Value.LateReverbGain.ToString("0.00") + "@" + (e.Value.LateReverbDelay * 1000f).ToString("0") + "ms echo=" + (e.Value.EchoTime * 1000f).ToString("0") + "ms/" + e.Value.EchoDepth.ToString("0.00") + " returned=" + e.Value.ReturnedPercent.ToString("0.00") + " outside=" + e.Value.OutsidePercent.ToString("0.00") + " absorb=" + e.Value.MaterialAbsorptionLF.ToString("0.00") + "/" + e.Value.MaterialAbsorptionHF.ToString("0.00");
            return Raytraced.Describe() + " ; listener " + eax(Raytraced.ListenerReverb) + " outside=" + Raytraced.ListenerOutside.ToString("0.00") + " ; " + string.Join(" ; ", players.Keys.Select(id =>
            {
                VAudio.LowPass filter; VoicePlayback playback; Path path;
                bool heard = Raytraced.Muffling(id, out filter);
                players.TryGetValue(id, out playback); paths.TryGetValue(id, out path);
                return "peer " + id + " rays: " + (heard ? "LF=" + filter.GainLF.ToString("0.000") + " HF=" + filter.GainHF.ToString("0.000") : "not yet")
                    + " own: " + (path != null ? "gain=" + path.Gain.ToString("0.000") + " cutoff=" + path.Cutoff.ToString("0") + " walls=" + path.Walls + " floors=" + path.Slabs + " doors=" + path.Doors + " d=" + path.Distance.ToString("0.0") : "-")
                    + " | speaker " + eax(Raytraced.ReverbOf(id))
                    + (playback ? " | filter room=" + playback.Reverb.room.ToString("0") + " hf=" + playback.Reverb.roomHF.ToString("0") + " decay=" + playback.Reverb.decayTime.ToString("0.00") + " refl=" + playback.Reverb.reflectionsLevel.ToString("0") + " late=" + playback.Reverb.reverbLevel.ToString("0") + " echo=" + (playback.Echo.enabled ? playback.Echo.delay.ToString("0") + "ms/" + playback.Echo.wetMix.ToString("0.00") : "off") + " gain=" + playback.Gain.ToString("0.000") + " lowpass=" + playback.LowPass.cutoffFrequency.ToString("0") : "");
            }).ToArray());
        }

        /// <summary>How much a hall: enclosed, big and long, not a narrow passage.</summary>
        private static float Hall(Room room)
        {
            float enclosed = Mathf.Clamp01((room.Enclosure - 0.35f) / 0.65f);
            return enclosed * Mathf.InverseLerp(10f, 20f, room.Length) * Mathf.InverseLerp(3.5f, 6f, room.Width) * Mathf.InverseLerp(3.5f, 5f, room.Height);
        }

        /// <summary>How much out in the open beside a wall: sky overhead, a wall within reach on one side, open on others.</summary>
        private static float Outdoors(Room room)
        {
            if (!room.Sky || room.Nearest >= 29.9f) return 0f;
            float open = Mathf.InverseLerp(0.75f, 0.4f, room.Enclosure);
            return open * Mathf.InverseLerp(3f, 8f, room.Nearest) * Mathf.InverseLerp(30f, 15f, room.Nearest);
        }

        public void Dispose() { StopMic(); Clear(); World.Update(false, null, null); Raytraced.Dispose(); }
    }
}
