using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Valcraft
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "michal.valcraft";
        public const string Name = "Valcraft";
        public const string Version = "0.6.0";

        internal static ManualLogSource Log;
        private readonly Link _link = new Link();
        private bool _debug;

        private void Awake()
        {
            Log = Logger;
            // Keep simulating and rendering while the Minecraft window has focus.
            Application.runInBackground = true;
            new Harmony(Guid).PatchAll();
            _link.Start();
            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            while (_link.Inbox.TryDequeue(out var msg))
                Handle(msg);

            if (ZInput.GetKeyDown(KeyCode.F8, false))
            {
                Follow.Enabled = !Follow.Enabled;
                Log.LogInfo($"follow {(Follow.Enabled ? "on" : "off")}");
            }
            if (ZInput.GetKeyDown(KeyCode.F9, false)) _debug = !_debug;
            if (Follow.Linked && Player.m_localPlayer != null)
                TerrainScanner.Update(_link, Player.m_localPlayer.transform.position);
            Follow.Update(_link);
            InputForward.Update(_link);
        }

        private void OnGUI()
        {
            if (!_debug) return;
            GUI.Box(new Rect(10, 10, 620, 120), "");
            GUI.Label(new Rect(16, 14, 610, 116), Follow.DebugText());
        }

        private void Handle(JObject msg)
        {
            switch ((string)msg["t"])
            {
                case "_link":
                    bool up = (string)msg["state"] == "connected";
                    Follow.Linked = up;
                    Follow.Reset();
                    InputForward.Reset();
                    TerrainScanner.Reset();
                    Log.LogInfo($"Minecraft {(up ? "connected" : "disconnected")}");
                    Player.m_localPlayer?.Message(MessageHud.MessageType.Center, up ? "Minecraft linked" : "Minecraft link lost");
                    if (up) _link.Send(new JObject { ["t"] = "hello", ["version"] = Version });
                    break;
                case "player":
                    Follow.OnSample(msg);
                    break;
                case "dig":
                    Dig.OnDig(msg);
                    break;
                case "ready":
                    Follow.OnReady();
                    Log.LogInfo("sync: Minecraft is standing on Valheim's ground");
                    break;
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class PlayerSpawnedPatch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            Plugin.Log.LogInfo("local player spawned");
            __instance.Message(MessageHud.MessageType.Center, "Valcraft loaded");
        }
    }
}
