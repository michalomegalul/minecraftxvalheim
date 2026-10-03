using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Valcraft
{
    /// <summary>One player-state sample from Minecraft (sent every MC tick, 20 Hz).</summary>
    internal struct McSample
    {
        public double X, Y, Z, Air;
        public float Yaw, Pitch, Eye;
        public bool Ground, Sneak, Sprint, Swim, Elytra;
        public float Arrived;

        public static McSample From(JObject o, float now) => new McSample
        {
            X = (double)o["x"], Y = (double)o["y"], Z = (double)o["z"], Air = (double)o["air"],
            Yaw = (float)o["yaw"], Pitch = (float)o["pitch"], Eye = (float)o["eye"],
            Ground = (bool)o["ground"], Sneak = (bool)o["sneak"], Sprint = (bool)o["sprint"],
            Swim = (bool)o["swim"], Elytra = (bool)o["elytra"], Arrived = now,
        };
    }

    /// <summary>
    /// Makes the local Valheim player follow the Minecraft player.
    ///
    /// Coordinates: 1 MC block = 1 Valheim metre. Minecraft is +X east, +Z south; Unity is
    /// +X right, +Z forward. Mapping MC z to -Unity z keeps the world un-mirrored, which turns
    /// MC yaw (0 = facing +Z, clockwise) into Unity yaw = MC yaw + 180.
    /// </summary>
    internal static class Follow
    {
        private const float TickSeconds = 0.05f;

        public static bool Enabled = true;
        private static bool _haveSamples;
        private static McSample _prev, _latest;

        // Valheim position and MC x/z at the moment we linked; movement is relative to these.
        private static bool _anchored;
        private static Vector3 _anchorV;
        private static double _anchorX, _anchorZ;

        private static bool _wasActive;

        private static readonly AccessTools.FieldRef<Character, Rigidbody> Body = AccessTools.FieldRefAccess<Character, Rigidbody>("m_body");
        private static readonly AccessTools.FieldRef<Character, GameObject> Visual = AccessTools.FieldRefAccess<Character, GameObject>("m_visual");
        private static readonly AccessTools.FieldRef<Character, Quaternion> LookYaw = AccessTools.FieldRefAccess<Character, Quaternion>("m_lookYaw");
        private static readonly AccessTools.FieldRef<Player, float> LookPitch = AccessTools.FieldRefAccess<Player, float>("m_lookPitch");
        private static readonly AccessTools.FieldRef<Character, float> MaxAirAltitude = AccessTools.FieldRefAccess<Character, float>("m_maxAirAltitude");

        public static bool Linked;

        public static bool Active
        {
            get
            {
                var p = Player.m_localPlayer;
                return Linked && Enabled && _haveSamples && p != null && !p.IsDead() && !p.IsTeleporting() && !p.IsAttached();
            }
        }

        public static void OnSample(JObject o)
        {
            var s = McSample.From(o, Time.time);
            _prev = _haveSamples ? _latest : s;
            _latest = s;
            _haveSamples = true;
        }

        public static void Reset()
        {
            _haveSamples = false;
            _anchored = false;
        }

        /// <summary>Current Minecraft state, interpolated between the last two ticks.</summary>
        private static McSample Current()
        {
            float a = Mathf.Clamp01((Time.time - _latest.Arrived) / TickSeconds);
            var s = _latest;
            s.X = _prev.X + (_latest.X - _prev.X) * a;
            s.Y = _prev.Y + (_latest.Y - _prev.Y) * a;
            s.Z = _prev.Z + (_latest.Z - _prev.Z) * a;
            s.Air = _prev.Air + (_latest.Air - _prev.Air) * a;
            s.Yaw = Mathf.LerpAngle(_prev.Yaw, _latest.Yaw, a);
            s.Pitch = Mathf.Lerp(_prev.Pitch, _latest.Pitch, a);
            s.Eye = Mathf.Lerp(_prev.Eye, _latest.Eye, a);
            return s;
        }

        public static float ToUnityYaw(float mcYaw) => mcYaw + 180f;

        public static Vector3 ToValheim(McSample s)
        {
            float x = _anchorV.x + (float)(s.X - _anchorX);
            float z = _anchorV.z - (float)(s.Z - _anchorZ);
            // Until Valheim's terrain is fed into Minecraft (roadmap step 3), Minecraft walks on
            // its own floor, so we stand on Valheim's ground and only take the jump height.
            float ground = ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(new Vector3(x, 0f, z)) : _anchorV.y;
            return new Vector3(x, ground + (float)s.Air, z);
        }

        private static void EnsureAnchor(Player p)
        {
            if (_anchored) return;
            _anchorV = p.transform.position;
            _anchorX = _latest.X;
            _anchorZ = _latest.Z;
            _anchored = true;
            Plugin.Log.LogInfo($"anchored MC ({_anchorX:F1}, {_anchorZ:F1}) to Valheim {_anchorV}");
        }

        /// <summary>Replaces Character.UpdateMotion for the local player while following.</summary>
        public static bool DriveBody(Character c)
        {
            var p = c as Player;
            if (p == null || p != Player.m_localPlayer || !Active) return true;

            EnsureAnchor(p);
            var s = Current();
            var body = Body(c);
            body.isKinematic = true;
            body.MovePosition(ToValheim(s));
            var yaw = Quaternion.Euler(0f, ToUnityYaw(s.Yaw), 0f);
            body.MoveRotation(yaw);
            LookYaw(c) = yaw;
            LookPitch(p) = s.Pitch;
            // We move the body directly, so Valheim must not see this as falling.
            MaxAirAltitude(c) = body.position.y;
            return false;
        }

        /// <summary>First-person camera at the Minecraft eye position.</summary>
        public static void DriveCamera(GameCamera cam)
        {
            var p = Player.m_localPlayer;
            if (!Active || p == null) return;
            var s = Current();
            cam.transform.position = p.transform.position + Vector3.up * s.Eye;
            cam.transform.rotation = Quaternion.Euler(s.Pitch, ToUnityYaw(s.Yaw), 0f);
        }

        /// <summary>Called every frame: handles switching between following and normal play.</summary>
        public static void Update()
        {
            bool active = Active;
            if (active == _wasActive) return;
            _wasActive = active;
            var p = Player.m_localPlayer;
            if (p == null) return;
            if (!active)
            {
                Body(p).isKinematic = false;
                _anchored = false; // re-anchor wherever we are next time
            }
            // Hide our own body in first person but keep its shadow.
            var visual = Visual(p);
            if (visual != null)
            {
                foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
                    r.shadowCastingMode = active ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            }
            p.Message(MessageHud.MessageType.TopLeft, active ? "Following Minecraft" : "Valheim controls");
        }
    }

    [HarmonyPatch(typeof(Character), "UpdateMotion")]
    internal static class UpdateMotionPatch
    {
        private static bool Prefix(Character __instance) => Follow.DriveBody(__instance);
    }

    [HarmonyPatch(typeof(Player), nameof(Player.SetMouseLook))]
    internal static class SetMouseLookPatch
    {
        private static bool Prefix() => !Follow.Active;
    }

    [HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
    internal static class UpdateCameraPatch
    {
        private static void Postfix(GameCamera __instance) => Follow.DriveCamera(__instance);
    }
}
