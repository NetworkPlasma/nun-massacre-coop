using System;
using System.IO;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using NunMassacre.Multiplayer.Ui;
using UnityEngine;

namespace NunMassacre.Multiplayer
{
    [BepInPlugin("local.nunmassacre.multiplayer", "Nun Massacre Co-op", Version)]
    [BepInProcess("Nun Massacre.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal const string Version = "1.0.0";
        internal static Plugin Instance;
        internal Session Session;
        internal ConfigEntry<string> PlayerName, Address;
        internal ConfigEntry<int> Port;
        internal ConfigEntry<bool> OpenRouterPort;
        internal CoopConsole Console;
        internal ConfigEntry<string> Character;
        internal ConfigEntry<bool> NunDiagnostics;
        internal Toasts Toasts;
        private Harmony harmony;
        internal string BuildHash;
        private string testLog;

        private void Awake()
        {
            Instance = this;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-nmm-report") testLog = Path.ChangeExtension(args[i + 1], ".events.log");
            if (testLog != null) { File.WriteAllText(testLog, ""); Application.targetFrameRate = 30; }
            PlayerName = Config.Bind("Connection", "PlayerName", Environment.UserName, "Name visible to teammates.");
            Address = Config.Bind("Connection", "Address", "", "The host's invite code (or their LAN or VPN address).");
            Port = Config.Bind("Connection", "Port", 27845, "TCP port, 1024-65535.");
            OpenRouterPort = Config.Bind("Connection", "OpenRouterPort", true,
                "When hosting, ask the router (UPnP) to open the port so friends can join over the internet with an invite code. The port is closed again when hosting stops.");
            NunDiagnostics = Config.Bind("Debug", "NunDiagnostics", false,
                "Write every sound the Nun hears, and what she does about it, to BepInEx/LogOutput.log.");
            Players.EnemyDirector.Diagnostics = NunDiagnostics.Value;
            NunDiagnostics.SettingChanged += (sender, e) => Players.EnemyDirector.Diagnostics = NunDiagnostics.Value;
            Character = Config.Bind("Appearance", "Character", "male_casual",
                "Character teammates see: male_casual (Mike), secretary (Maddy) or scientist (Mark).");
            using (var sha = SHA256.Create())
                BuildHash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(typeof(GrannyClone.Inventory).Assembly.Location))).Replace("-", "");
            Session = new Session(this);
            Toasts = gameObject.AddComponent<Toasts>();
            Session.Notice += Toasts.Show;
            gameObject.AddComponent<CoopMenu>().Initialize(this);
            gameObject.AddComponent<MultiplayerSettings>().Initialize(this);
            gameObject.AddComponent<PausePlayers>().Initialize(this);
            gameObject.AddComponent<CoopHud>().Initialize(this);
            Console = gameObject.AddComponent<CoopConsole>(); Console.Initialize(this);
            gameObject.AddComponent<RuntimeTestDriver>().Initialize(this);
            harmony = new Harmony("local.nunmassacre.multiplayer");
            GameHooks.Install(harmony);
            Application.runInBackground = true;
            Logger.LogInfo("NMM " + Version + " loaded. Co-op is in the main menu. Game SHA256 " + BuildHash);
            Session.SceneLoaded(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), UnityEngine.SceneManagement.LoadSceneMode.Single);
            // Command-line hosting/joining is used by the automated multi-process tests.
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-nmm-name" && i + 1 < args.Length) PlayerName.Value = args[++i];
                else if (args[i] == "-nmm-port" && i + 1 < args.Length) Port.Value = int.Parse(args[++i]);
            }
            // Automated runs never touch the real router unless told to: "-nmm-router live", or a fake router's URL.
            if (System.Linq.Enumerable.Any(args, a => a.StartsWith("-nmm-", StringComparison.Ordinal))) Session.TestRouter = "off";
            int joinKey = 0;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-nmm-router" && i + 1 < args.Length) Session.TestRouter = args[++i];
                else if (args[i] == "-nmm-join-key" && i + 1 < args.Length) { int.TryParse(args[++i], out joinKey); Session.TestJoinKey = joinKey; }
                else if (args[i] == "-nmm-require-invite") Session.TestAllRemote = true;
                else if (args[i] == "-nmm-invite-key" && i + 1 < args.Length) int.TryParse(args[++i], out Session.TestInviteKey);
            }
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-nmm-host") Session.Host(Port.Value);
                else if (args[i] == "-nmm-join" && i + 1 < args.Length) Session.Join(args[++i], Port.Value, joinKey);
            }
        }
        private void Update()
        {
            long start = Perf.Begin();
            try { Session.Update(); }
            catch (Exception ex) { Log("Session error: " + ex); }
            Perf.End(Perf.Mod, start);
        }
        private void LateUpdate() { long start = Perf.Begin(); Session.LateUpdate(); Perf.End(Perf.Mod, start); }
        internal void Log(string text)
        {
            Logger.LogInfo(text);
            if (testLog != null) File.AppendAllText(testLog, DateTime.UtcNow.ToString("HH:mm:ss.fff") + " " + text + Environment.NewLine);
        }
        private void OnDestroy()
        {
            if (Session != null) Session.Dispose();
            if (harmony != null) harmony.UnpatchSelf();
            Instance = null;
        }
    }
}
