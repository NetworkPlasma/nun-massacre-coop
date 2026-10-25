using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NunMassacre.Multiplayer.Gameplay;
using Rewired;
using UnityEngine;

namespace NunMassacre.Multiplayer.Ui
{
    /// <summary>
    /// The host's console (F2 or `): fix a stuck player without restarting. Moves players, sends them back to safe
    /// ground or the respawn point, revives and removes them. Guests can only free themselves (unstuck). The host's
    /// game also runs commands written to BepInEx/nmm-commands.txt, one per line, so they can be issued from outside.
    /// </summary>
    internal sealed class CoopConsole : MonoBehaviour
    {
        private Plugin plugin;
        private bool open;
        private string line = "";
        private readonly List<string> output = new List<string>();
        private GUIStyle style, box;
        private Font font;
        private bool focus;
        private CursorLockMode priorLock;
        private bool priorVisible;
        private float nextFileCheck;
        private DateTime fileStamp;

        internal void Initialize(Plugin owner) { plugin = owner; }
        internal bool IsOpen { get { return open; } }

        private Session Session { get { return plugin != null ? plugin.Session : null; } }

        private void Update()
        {
            var session = Session;
            if (session == null) return;
            if (Input.GetKeyDown(KeyCode.F2) || (!open && Input.GetKeyDown(KeyCode.BackQuote))) Toggle(!open);
            if (open && (!session.Active || !session.Local)) Toggle(false);
            if (session.Active && session.IsHost && Time.unscaledTime >= nextFileCheck) { nextFileCheck = Time.unscaledTime + 1f; RunFileCommands(); }
        }

        private void Toggle(bool show)
        {
            var session = Session;
            if (show && (session == null || !session.Active || !session.Local)) return;
            open = show;
            var player = ReInput.isReady ? ReInput.players.GetPlayer(0) : null;
            if (open)
            {
                // Typing must not walk, drop items or open the inventory: the game's controls are off meanwhile.
                if (player != null) player.controllers.maps.SetAllMapsEnabled(false);
                priorLock = Cursor.lockState; priorVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
                focus = true;
                if (output.Count == 0) Print(session.IsHost ? "Host console. Type help for the commands." : "Console. Guests can use: unstuck, players.");
            }
            else
            {
                if (player != null) player.controllers.maps.SetMapsEnabled(true, "Default");
                Cursor.lockState = priorLock; Cursor.visible = priorVisible;
            }
        }

        private void OnDisable() { if (open) Toggle(false); }

        private void OnGUI()
        {
            if (!open) return;
            if (style == null || !font)
            {
                font = Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(f => f && f.name == "VCR_OSD_MONO");
                style = new GUIStyle(GUI.skin.label) { font = font, fontSize = Mathf.Max(12, Screen.height / 45), richText = false, wordWrap = true };
                style.normal.textColor = new Color(0.93f, 0.85f, 1f);
                box = new GUIStyle(GUI.skin.textField) { font = font, fontSize = style.fontSize };
            }
            float w = Screen.width, h = Screen.height * 0.42f;
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            float row = style.fontSize * 1.35f;
            int shown = Mathf.Max(1, (int)((h - row * 2.2f) / row));
            int first = Mathf.Max(0, output.Count - shown);
            for (int i = first; i < output.Count; i++) GUI.Label(new Rect(12, 8 + (i - first) * row, w - 24, row), output[i], style);
            var e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                string command = line.Trim(); line = "";
                if (command.Length > 0) { Print("> " + command); Run(command, true); }
                e.Use();
            }
            else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { Toggle(false); e.Use(); return; }
            else if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.F2 || e.keyCode == KeyCode.BackQuote)) { e.Use(); return; }
            GUI.SetNextControlName("nmm_console");
            line = GUI.TextField(new Rect(12, h - row * 1.6f, w - 24, row * 1.3f), line, 200, box);
            if (focus) { GUI.FocusControl("nmm_console"); focus = false; }
        }

        private void Print(string text)
        {
            output.Add(text);
            if (output.Count > 200) output.RemoveRange(0, output.Count - 200);
        }

        private void RunFileCommands()
        {
            string path = Path.Combine(BepInEx.Paths.BepInExRootPath, "nmm-commands.txt");
            try
            {
                if (!File.Exists(path)) return;
                DateTime stamp = File.GetLastWriteTimeUtc(path);
                if (stamp == fileStamp) return;
                fileStamp = stamp;
                string[] lines = File.ReadAllLines(path);
                if (lines.Length == 0) return;
                File.WriteAllText(path, "");
                fileStamp = File.GetLastWriteTimeUtc(path);
                foreach (string command in lines.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal)))
                {
                    Print("> " + command + "   (from file)");
                    Run(command, true);
                }
            }
            catch (Exception error) { plugin.Log("Console file: " + error.Message); }
        }

        /// <summary>Runs one command; the reply goes to the console and the log.</summary>
        internal void Run(string command, bool fromHostSide)
        {
            string reply;
            try { reply = Execute(command); }
            catch (Exception error) { reply = "Error: " + error.Message; }
            foreach (string text in reply.Split('\n')) Print(text);
            plugin.Log("Console: " + command + " -> " + reply.Replace("\n", " | "));
        }

        private string Execute(string command)
        {
            var session = Session;
            if (session == null || !session.Active) return "Not in a co-op game.";
            string[] words = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string verb = words[0].ToLowerInvariant();
            bool host = session.IsHost;
            switch (verb)
            {
                case "help":
                    return host
                        ? "players                 list everyone: number, name, state, position\n"
                        + "bring <player>          move a player to you\n"
                        + "goto <player>           move yourself to a player\n"
                        + "tp <player> <player>    move the first player to the second\n"
                        + "tp <player> <x> <y> <z> move a player to a spot\n"
                        + "unstuck [player|all]    back to their last safe footing (default: you)\n"
                        + "spawn <player|all>      to the level's respawn point\n"
                        + "revive <player>         get a downed player back up\n"
                        + "kick <player>           remove a player from the game\n"
                        + "Players by name (start of it is enough), number, or me. F2 or Esc closes."
                        : "unstuck   back to your last safe footing\nplayers   list everyone";
                case "players":
                    return string.Join("\n", session.ConsolePlayers().Select(f => "#" + f.id + " " + f.name + (f.id == session.LocalId ? " (you)" : "")
                        + "  " + (f.spectating ? "out" : f.downed ? "down" : f.health > 0f ? "alive " + Mathf.RoundToInt(f.health) + " HP" : "-")
                        + "  at " + (f.body ? f.position.ToString("0.0") : "-") + (f.scene != session.SceneName ? "  (" + f.scene + ")" : "")).ToArray());
                case "unstuck":
                    if (words.Length < 2 || !host) { session.ConsoleTeleport(session.LocalId, "safe", Vector3.zero); return "Back on safe ground."; }
                    return ForEach(words[1], f => session.ConsoleTeleport(f.id, "safe", Vector3.zero), "sent back to safe ground");
            }
            if (!host) return "Only the host can use " + verb + ". You can use: unstuck, players.";
            switch (verb)
            {
                case "bring":
                {
                    var who = Need(words, 1); var me = session.FrameOf(session.LocalId);
                    if (who.id == session.LocalId) return "That is you.";
                    session.ConsoleTeleport(who.id, "pos", Beside(me));
                    return who.name + " brought to you.";
                }
                case "goto":
                {
                    var who = Need(words, 1);
                    session.ConsoleTeleport(session.LocalId, "pos", Beside(who));
                    return "Moved to " + who.name + ".";
                }
                case "tp":
                {
                    var who = Need(words, 1);
                    if (words.Length >= 5)
                    {
                        var spot = new Vector3(Number(words[2]), Number(words[3]), Number(words[4]));
                        session.ConsoleTeleport(who.id, "pos", spot);
                        return who.name + " moved to " + spot.ToString("0.0") + ".";
                    }
                    var to = Need(words, 2);
                    if (to.id == who.id) return "Same player.";
                    session.ConsoleTeleport(who.id, "pos", Beside(to));
                    return who.name + " moved to " + to.name + ".";
                }
                case "spawn":
                    return ForEach(Word(words, 1), f => session.ConsoleTeleport(f.id, "spawn", Vector3.zero), "sent to the respawn point");
                case "revive":
                {
                    var who = Need(words, 1);
                    if (!who.downed) return who.name + " is not down.";
                    session.ConsoleRevive(who.id);
                    return who.name + " revived.";
                }
                case "kick":
                {
                    var who = Need(words, 1);
                    if (who.id == session.LocalId) return "You cannot remove yourself; stop hosting instead.";
                    session.ConsoleKick(who.id);
                    return who.name + " removed from the game.";
                }
            }
            return "Unknown command " + verb + ". Type help.";
        }

        private string ForEach(string who, Action<PlayerFrame> act, string done)
        {
            var session = Session;
            var targets = who.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? session.ConsolePlayers().Where(f => f.body && !f.spectating).ToList()
                : new List<PlayerFrame> { Find(who) };
            foreach (var frame in targets) act(frame);
            return string.Join(", ", targets.Select(f => f.name).ToArray()) + " " + done + ".";
        }

        private static string Word(string[] words, int index)
        {
            if (words.Length <= index) throw new ArgumentException("Missing a player. Type help.");
            return words[index];
        }
        private PlayerFrame Need(string[] words, int index) { return Find(Word(words, index)); }

        /// <summary>A player by number (#2 or 2), "me", or the start of their name.</summary>
        private PlayerFrame Find(string text)
        {
            var session = Session;
            var all = session.ConsolePlayers().ToList();
            if (text.Equals("me", StringComparison.OrdinalIgnoreCase)) return all.FirstOrDefault(f => f.id == session.LocalId) ?? Fail(text);
            int id;
            if (int.TryParse(text.TrimStart('#'), out id)) return all.FirstOrDefault(f => f.id == id) ?? Fail(text);
            var exact = all.Where(f => string.Equals(f.name, text, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count == 1) return exact[0];
            var start = all.Where(f => f.name != null && f.name.StartsWith(text, StringComparison.OrdinalIgnoreCase)).ToList();
            if (start.Count == 1) return start[0];
            if (start.Count > 1)
                throw new ArgumentException("More than one player matches " + text + ": " + string.Join(", ", start.Select(f => f.name).ToArray()));
            return Fail(text);
        }
        private static PlayerFrame Fail(string text) { throw new ArgumentException("No player " + text + ". Type players."); }

        /// <summary>Half a metre beside a player (not inside them), at their feet.</summary>
        private static Vector3 Beside(PlayerFrame frame)
        {
            Vector3 side = frame.rotation * Vector3.right;
            side.y = 0f;
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            return frame.position + side.normalized * 0.7f + Vector3.up * 0.1f;
        }
        private static float Number(string text) { return float.Parse(text, CultureInfo.InvariantCulture); }
    }
}
