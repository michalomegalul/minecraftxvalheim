using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Valcraft
{
    /// <summary>One player-state sample from Minecraft (sent every MC tick, 20 Hz).</summary>
    internal struct McSample
    {
        public long Tick;
        public double X, Y, Z, Air;
        public float Yaw, Pitch, Eye, Fov;
        public bool Ground, Sneak, Sprint, Swim, Elytra;
        public double Arrived;

        public static McSample From(JObject o, double now) => new McSample
        {
            Tick = (long)o["tick"],
            X = (double)o["x"], Y = (double)o["y"], Z = (double)o["z"], Air = (double)o["air"],
            Yaw = (float)o["yaw"], Pitch = (float)o["pitch"], Eye = (float)o["eye"],
            Fov = o["fov"] != null ? (float)o["fov"] : 70f,
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
        // Play Minecraft back a little behind the newest tick, so uneven arrival never leaves us
        // without a sample to interpolate toward. The delay adapts to measured jitter.
        private const float MinDelay = 0.05f, MaxDelay = 0.2f;
        private static float _delay = 0.06f, _targetDelay = 0.06f, _jitter;
        private static int _starved;
        private const int MaxSamples = 16;

        public static bool Enabled = true;
        private static bool _haveSamples;
        private static McSample _latest;
        private static readonly System.Collections.Generic.List<McSample> Samples = new System.Collections.Generic.List<McSample>();
        // Estimated (arrival time - tick * TickSeconds): maps Minecraft ticks onto our clock.
        private static double _clockOffset;

        // Valheim position and MC x/z at the moment we linked; movement is relative to these.
        private static bool _anchored;
        private static Vector3 _anchorV;
        private static double _anchorX, _anchorZ;

        private static bool _wasActive;

        private static readonly AccessTools.FieldRef<Character, Rigidbody> Body = AccessTools.FieldRefAccess<Character, Rigidbody>("m_body");
        private static readonly AccessTools.FieldRef<Character, GameObject> Visual = AccessTools.FieldRefAccess<Character, GameObject>("m_visual");
        private static readonly AccessTools.FieldRef<Character, Quaternion> LookYaw = AccessTools.FieldRefAccess<Character, Quaternion>("m_lookYaw");
        private static readonly AccessTools.FieldRef<Player, float> LookPitch = AccessTools.FieldRefAccess<Player, float>("m_lookPitch");
        private static readonly AccessTools.FieldRef<GameCamera, Camera> MainCamera = AccessTools.FieldRefAccess<GameCamera, Camera>("m_camera");
        private static readonly AccessTools.FieldRef<GameCamera, Camera> SkyCamera = AccessTools.FieldRefAccess<GameCamera, Camera>("m_skyCamera");
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
            var s = McSample.From(o, o["_rx"] != null ? (double)o["_rx"] : Link.Now);
            if (_haveSamples && s.Tick <= _latest.Tick) Samples.Clear(); // Minecraft restarted
            double offset = s.Arrived - s.Tick * TickSeconds;
            if (Samples.Count == 0)
            {
                _clockOffset = offset;
                _jitter = 0f;
            }
            else
            {
                // How late this sample is compared with our clock; remember the worst recently.
                float late = (float)(offset - _clockOffset);
                _jitter = Mathf.Max(late, _jitter * 0.98f);
                // Move the clock gradually (never jump), faster toward earlier arrivals.
                _clockOffset += (offset - _clockOffset) * (offset < _clockOffset ? 0.1 : 0.01);
            }
            _targetDelay = Mathf.Clamp(_jitter + 0.03f, MinDelay, MaxDelay);
            Samples.Add(s);
            if (Samples.Count > MaxSamples) Samples.RemoveAt(0);
            _latest = s;
            _haveSamples = true;
            InputForward.OnMcLook(s.Yaw, s.Pitch);
            ShowHeldItem((int?)o["slot"] ?? -1, (string)o["item"] ?? "");
            if (o["sens"] != null) InputForward.Sensitivity = (double)o["sens"];
        }

        private static int _lastSlot = -1;
        private static string _lastItem;

        /// <summary>Minecraft's hotbar isn't drawn in Valheim yet, so say what's in hand when it changes.</summary>
        private static void ShowHeldItem(int slot, string item)
        {
            if (slot == _lastSlot && item == _lastItem) return;
            bool first = _lastItem == null;
            _lastSlot = slot;
            _lastItem = item;
            if (!first) Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, $"[{slot + 1}] {item}");
        }

        public static void Reset()
        {
            _haveSamples = false;
            Samples.Clear();
            _anchored = false;
        }

        /// <summary>Minecraft state at (now - PlaybackDelay), interpolated between the ticks around it.</summary>
        private static McSample Current()
        {
            double tick = (Link.Now - _delay - _clockOffset) / TickSeconds;
            if (tick > Samples[Samples.Count - 1].Tick) _starved++;
            McSample a = Samples[0], b = Samples[0];
            for (int i = 0; i < Samples.Count; i++)
            {
                b = Samples[i];
                if (b.Tick >= tick) break;
                a = b;
            }
            // Past the newest sample: hold it (no extrapolation, so no overshoot on stops).
            float t = b.Tick > a.Tick ? Mathf.Clamp01((float)((tick - a.Tick) / (b.Tick - a.Tick))) : 1f;
            var s = b;
            s.X = a.X + (b.X - a.X) * t;
            s.Y = a.Y + (b.Y - a.Y) * t;
            s.Z = a.Z + (b.Z - a.Z) * t;
            s.Air = a.Air + (b.Air - a.Air) * t;
            s.Yaw = Mathf.LerpAngle(a.Yaw, b.Yaw, t);
            s.Pitch = Mathf.Lerp(a.Pitch, b.Pitch, t);
            s.Eye = Mathf.Lerp(a.Eye, b.Eye, t);
            if (InputForward.OwnsLook)
            {
                s.Yaw = InputForward.Yaw;
                s.Pitch = InputForward.Pitch;
            }
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
            if (!_anchored) return;
            var s = Current();
            cam.transform.position = ToValheim(s) + Vector3.up * s.Eye;
            cam.transform.rotation = Quaternion.Euler(s.Pitch, ToUnityYaw(s.Yaw), 0f);
            // Both games use vertical FOV, so Minecraft's setting carries over directly.
            MainCamera(cam).fieldOfView = s.Fov;
            SkyCamera(cam).fieldOfView = s.Fov;
        }

        /// <summary>Numbers for the F9 debug overlay.</summary>
        public static string DebugText()
        {
            var p = Player.m_localPlayer;
            if (!_haveSamples) return $"Valcraft: linked={Linked}, no Minecraft samples yet";
            var s = Current();
            string target = _anchored ? ToValheim(s).ToString("F2") : "(not anchored)";
            string actual = p != null ? p.transform.position.ToString("F2") : "(no player)";
            return $"Valcraft  active={Active} enabled={Enabled} forwarding={InputForward.Forwarding}\n" +
                   $"MC   x={s.X:F2} y={s.Y:F2} z={s.Z:F2} air={s.Air:F2} ground={s.Ground} fov={s.Fov}\n" +
                   $"VH target {target}\n" +
                   $"VH actual {actual}  kinematic={(p != null && Body(p).isKinematic)}\n" +
                   $"buffer {_delay * 1000f:F0} ms  jitter {_jitter * 1000f:F0} ms  ran dry {_starved} frames";
        }

        /// <summary>Called every frame: handles switching between following and normal play.</summary>
        public static void Update()
        {
            // Ease toward the wanted buffer size so playback speed never visibly jumps.
            _delay = Mathf.MoveTowards(_delay, _targetDelay, 0.05f * Time.deltaTime);
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
