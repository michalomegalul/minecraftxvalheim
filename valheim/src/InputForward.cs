using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Valcraft
{
    /// <summary>
    /// Sends keyboard/mouse from the Valheim window to the (hidden) Minecraft client.
    ///
    /// Valheim owns the look direction: it turns mouse movement into yaw/pitch with Minecraft's
    /// sensitivity formula, uses it for the camera straight away, and tells Minecraft. That keeps
    /// mouse look instant instead of waiting for Minecraft's 20 Hz ticks.
    ///
    /// Gameplay keys go to Minecraft. Valheim keeps E (interact), Tab (inventory), Esc, M, chat.
    /// </summary>
    internal static class InputForward
    {
        private static bool _wasForwarding;
        private static bool _haveLook;
        public static float Yaw, Pitch;
        public static double Sensitivity = 0.5; // Minecraft's default, updated from MC samples

        private static readonly (string name, KeyCode key)[] Keys =
        {
            ("forward", KeyCode.W), ("back", KeyCode.S), ("left", KeyCode.A), ("right", KeyCode.D),
            ("jump", KeyCode.Space), ("sneak", KeyCode.LeftShift), ("sprint", KeyCode.LeftControl),
            ("drop", KeyCode.Q),
        };

        public static bool Forwarding => Follow.Active && !ValheimUiOpen();

        /// <summary>Use our own look direction instead of Minecraft's (only while forwarding).</summary>
        public static bool OwnsLook => _haveLook && Forwarding;

        public static bool ValheimUiOpen()
        {
            return InventoryGui.IsVisible() || Menu.IsVisible() || Minimap.IsOpen() || global::Console.IsVisible()
                   || (Chat.instance != null && Chat.instance.HasFocus()) || TextInput.IsVisible() || StoreGui.IsVisible()
                   || Hud.IsPieceSelectionVisible() || Hud.InRadial();
        }

        /// <summary>Keep our look in sync with Minecraft while we're not the ones steering.</summary>
        public static void OnMcLook(float yaw, float pitch)
        {
            if (Forwarding && _haveLook) return;
            Yaw = yaw;
            Pitch = pitch;
            _haveLook = true;
        }

        public static void Reset()
        {
            _haveLook = false;
            _wasForwarding = false;
        }

        public static void Update(Link link)
        {
            bool forwarding = Forwarding;
            if (!forwarding)
            {
                if (_wasForwarding) link.Send(ReleaseAll());
                _wasForwarding = false;
                return;
            }
            _wasForwarding = true;
            if (!_haveLook) return;

            // Same curve as Minecraft's MouseHandler: pixels -> degrees.
            double f = Sensitivity * 0.6 + 0.2;
            float degPerPixel = (float)(f * f * f * 8.0 * 0.15);
            Vector2 d = ZInput.GetMouseDelta();
            Yaw += d.x * degPerPixel;
            Pitch = Mathf.Clamp(Pitch - d.y * degPerPixel, -90f, 90f);

            var keys = new JObject();
            foreach (var (name, key) in Keys) keys[name] = ZInput.GetKey(key, false);
            keys["attack"] = ZInput.GetMouseButton(0);
            keys["use"] = ZInput.GetMouseButton(1);

            var msg = new JObject { ["t"] = "input", ["keys"] = keys, ["yaw"] = Yaw, ["pitch"] = Pitch };
            for (int i = 0; i < 9; i++)
            {
                if (ZInput.GetKeyDown(KeyCode.Alpha1 + i, false)) msg["slot"] = i;
            }
            float scroll = ZInput.GetMouseScrollWheel();
            if (scroll != 0f) msg["scroll"] = scroll > 0f ? 1 : -1;
            link.Send(msg);
        }

        private static JObject ReleaseAll()
        {
            var keys = new JObject();
            foreach (var (name, _) in Keys) keys[name] = false;
            keys["attack"] = false;
            keys["use"] = false;
            return new JObject { ["t"] = "input", ["keys"] = keys };
        }
    }

    /// <summary>Valheim's own movement/attack controls are ignored while Minecraft drives.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    internal static class SetControlsPatch
    {
        private static bool Prefix(Player __instance) => !(Follow.Active && __instance == Player.m_localPlayer);
    }

    /// <summary>Number keys select Minecraft's hotbar, so don't also use Valheim hotbar items.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.UseHotbarItem))]
    internal static class UseHotbarItemPatch
    {
        private static bool Prefix() => !InputForward.Forwarding;
    }
}
