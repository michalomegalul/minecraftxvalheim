using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace Valcraft
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "michal.valcraft";
        public const string Name = "Valcraft";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            new Harmony(Guid).PatchAll();
            Log.LogInfo($"{Name} {Version} loaded");
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
