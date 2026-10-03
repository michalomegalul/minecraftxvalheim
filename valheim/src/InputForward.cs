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
        private const float ValheimMouseScale = 0.05f; // ZInput: scaleVector2(x=0.05,y=0.05)

        private static readonly (string name, KeyCode key)[] Keys =
        {
            ("forward", KeyCode.W), ("back", KeyCode.S), ("left", KeyCode.A), ("right", KeyCode.D),
            ("jump", KeyCode.Space), ("sneak", KeyCode.LeftShift), ("sprint", KeyCode.LeftControl),
            ("drop", KeyCode.Q),
        };

        // Only while the Valheim window has focus, so you can still play Minecraft directly.
        public static bool Forwarding => Follow.Active && Application.isFocused && !ValheimUiOpen() && !Follow.McScreenOpen;

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
            // ZInput scales the raw mouse delta by 0.05 for Valheim's own camera; undo that.
            Vector2 d = ZInput.GetMouseDelta() / ValheimMouseScale;
            Yaw += d.x * degPerPixel;
            Pitch = Mathf.Clamp(Pitch - d.y * degPerPixel, -90f, 90f);

            var keys = new JObject();
            foreach (var (name, key) in Keys) keys[name] = ZInput.GetKey(key, false);
            keys["attack"] = ZInput.GetMouseButton(0);
            keys["use"] = ZInput.GetMouseButton(1);
            // Tab opens Minecraft's inventory; Shift+Tab is Valheim's (see InventoryShowPatch).
            keys["inventory"] = ZInput.GetKey(KeyCode.Tab, false) && !ZInput.GetKey(KeyCode.LeftShift, false);

            // Pressed through Minecraft's own hotbar key bindings, so it syncs the slot to the server.
            for (int i = 0; i < 9; i++) keys["hotbar" + (i + 1)] = ZInput.GetKey(KeyCode.Alpha1 + i, false);

            var msg = new JObject { ["t"] = "input", ["keys"] = keys, ["yaw"] = Yaw, ["pitch"] = Pitch };
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
            keys["inventory"] = false;
            for (int i = 0; i < 9; i++) keys["hotbar" + (i + 1)] = false;
            return new JObject { ["t"] = "input", ["keys"] = keys };
        }
    }

    /// <summary>
    /// While a Minecraft screen is open: free cursor, and its mouse goes to that screen, in
    /// coordinates relative to where the overlay is drawn. Esc or Tab closes it.
    /// </summary>
    internal static class ScreenInput
    {
        public static void Update(Link link)
        {
            if (!Follow.McScreenOpen || !Application.isFocused || InputForward.ValheimUiOpen()) return;
            var rect = GuiOverlay.ScreenRect;
            if (rect.width <= 0) return;
            Vector3 p = ZInput.pointerPosition;
            float x = (p.x - rect.x) / rect.width;
            float y = ((Screen.height - p.y) - rect.y) / rect.height; // GUI rect is top-left based
            var msg = new JObject
            {
                ["t"] = "screen_input",
                ["x"] = Mathf.Clamp01(x), ["y"] = Mathf.Clamp01(y),
                ["b0"] = ZInput.GetMouseButton(0), ["b1"] = ZInput.GetMouseButton(1), ["b2"] = ZInput.GetMouseButton(2),
                ["shift"] = ZInput.GetKey(KeyCode.LeftShift, false) || ZInput.GetKey(KeyCode.RightShift, false),
                ["close"] = ZInput.GetKeyDown(KeyCode.Escape, false) || ZInput.GetKeyDown(KeyCode.Tab, false),
            };
            float scroll = ZInput.GetMouseScrollWheel();
            if (scroll != 0f) msg["scroll"] = scroll > 0f ? 1 : -1;
            link.Send(msg);
        }
    }

    [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
    internal static class MouseCapturePatch
    {
        private static void Postfix()
        {
            if (!Follow.McScreenOpen) return;
            ZCursor.LockState = CursorLockMode.None;
            ZCursor.Show();
        }
    }

    /// <summary>Esc closes the Minecraft screen instead of opening Valheim's menu.</summary>
    [HarmonyPatch(typeof(Menu), "Update")]
    internal static class MenuUpdatePatch
    {
        private static bool Prefix() => !Follow.McScreenOpen;
    }

    /// <summary>Tab is Minecraft's inventory while following; Shift+Tab (and chests) stay Valheim's.</summary>
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
    internal static class InventoryShowPatch
    {
        private static bool Prefix(Container container) =>
            container != null || !Follow.Active || ZInput.GetKey(KeyCode.LeftShift, false);
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
