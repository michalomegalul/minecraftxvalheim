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
        public double X, Y, Z;
        public float Yaw, Pitch, Eye, Fov;
        public bool Ground, Sneak, Sprint, Swim, Elytra, Frozen;
        public double Arrived;

        public static McSample From(JObject o, double now) => new McSample
        {
            Tick = (long)o["tick"],
            X = (double)o["x"], Y = (double)o["y"], Z = (double)o["z"],
            Yaw = (float)o["yaw"], Pitch = (float)o["pitch"], Eye = (float)o["eye"],
            Fov = o["fov"] != null ? (float)o["fov"] : 70f,
            Ground = (bool)o["ground"], Sneak = (bool)o["sneak"], Sprint = (bool)o["sprint"],
            Swim = (bool)o["swim"], Elytra = (bool)o["elytra"], Frozen = o["frozen"] != null && (bool)o["frozen"],
            Arrived = now,
        };
    }

    /// <summary>
    /// Makes the local Valheim player follow the Minecraft player, 1:1 (see <see cref="Mapping"/>).
    ///
    /// Sync: when following starts, Valheim teleports the Minecraft player to the Valheim player's
    /// position; Minecraft holds it there until the scanned ground under it is built, then says
    /// "ready". If the two ever drift apart (Minecraft respawn, Valheim portal), we sync again.
    /// </summary>
    internal static class Follow
    {
        private const float TickSeconds = 0.05f;
        // Which Minecraft tick to show right now (see Playback); advanced once per frame.
        private static readonly Playback Clock = new Playback();
        private static double _playTick, _lastFrame;
        private const int MaxSamples = 16;

        public static bool Enabled = true;
        private static bool _haveSamples;
        private static McSample _latest;
        private static readonly System.Collections.Generic.List<McSample> Samples = new System.Collections.Generic.List<McSample>();

        private enum Sync { None, Pending, Synced }
        private static Sync _sync;
        private static double _syncSentAt;
        private const double SyncTimeout = 30;
        /// <summary>A Minecraft jump bigger than this between ticks means it moved on its own.</summary>
        private const double MaxTickMove = 20;

        private static bool _wasActive;

        private static readonly AccessTools.FieldRef<Character, Rigidbody> Body = AccessTools.FieldRefAccess<Character, Rigidbody>("m_body");
        private static readonly AccessTools.FieldRef<Character, GameObject> Visual = AccessTools.FieldRefAccess<Character, GameObject>("m_visual");
        private static readonly AccessTools.FieldRef<Character, Quaternion> LookYaw = AccessTools.FieldRefAccess<Character, Quaternion>("m_lookYaw");
        private static readonly AccessTools.FieldRef<Player, float> LookPitch = AccessTools.FieldRefAccess<Player, float>("m_lookPitch");
        private static readonly AccessTools.FieldRef<GameCamera, Camera> MainCamera = AccessTools.FieldRefAccess<GameCamera, Camera>("m_camera");
        private static readonly AccessTools.FieldRef<GameCamera, Camera> SkyCamera = AccessTools.FieldRefAccess<GameCamera, Camera>("m_skyCamera");
        private static readonly AccessTools.FieldRef<Character, float> MaxAirAltitude = AccessTools.FieldRefAccess<Character, float>("m_maxAirAltitude");

        public static bool Linked;

        /// <summary>We'd like to follow Minecraft (whether or not we're synced yet).</summary>
        private static bool Wanted
        {
            get
            {
                var p = Player.m_localPlayer;
                return Linked && Enabled && p != null && !p.IsDead() && !p.IsTeleporting() && !p.IsAttached();
            }
        }

        public static bool Active => Wanted && _haveSamples && _sync == Sync.Synced && !_latest.Frozen;

        public static string SyncState => _sync.ToString();

        public static void OnReady()
        {
            if (_sync != Sync.Pending) return;
            // Drop samples from before the teleport so we don't glide from the old position.
            _haveSamples = false;
            Samples.Clear();
            Clock.Reset();
            _sync = Sync.Synced;
        }

        public static void OnSample(JObject o)
        {
            var s = McSample.From(o, o["_rx"] != null ? (double)o["_rx"] : Link.Now);
            if (_haveSamples && s.Tick < _latest.Tick - 100) Samples.Clear(); // Minecraft restarted
            else if (_haveSamples && s.Tick <= _latest.Tick) return;
            if (_haveSamples && _sync == Sync.Synced && !s.Frozen)
            {
                double dx = s.X - _latest.X, dy = s.Y - _latest.Y, dz = s.Z - _latest.Z;
                if (dx * dx + dy * dy + dz * dz > MaxTickMove * MaxTickMove)
                {
                    Plugin.Log.LogInfo("Minecraft player moved on its own (respawn?); syncing again");
                    _sync = Sync.None;
                }
            }
            Clock.OnSample(s.Tick, s.Arrived);
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
            Clock.Reset();
            _sync = Sync.None;
        }

        /// <summary>Minecraft state at the current playback tick, interpolated between the ticks around it.</summary>
        private static McSample Current()
        {
            double tick = _playTick;
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

        public static Vector3 ToValheim(McSample s)
        {
            var v = Mapping.ToValheim(s.X, s.Y, s.Z);
            return new Vector3((float)v.x, (float)v.y, (float)v.z);
        }

        /// <summary>Replaces Character.UpdateMotion for the local player while following.</summary>
        public static bool DriveBody(Character c)
        {
            var p = c as Player;
            if (p == null || p != Player.m_localPlayer || !Active) return true;

            var s = Current();
            var body = Body(c);
            body.isKinematic = true;
            body.MovePosition(ToValheim(s));
            var yaw = Quaternion.Euler(0f, Mapping.ToUnityYaw(s.Yaw), 0f);
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
            cam.transform.position = ToValheim(s) + Vector3.up * s.Eye;
            cam.transform.rotation = Quaternion.Euler(s.Pitch, Mapping.ToUnityYaw(s.Yaw), 0f);
            // Both games use vertical FOV, so Minecraft's setting carries over directly.
            MainCamera(cam).fieldOfView = s.Fov;
            SkyCamera(cam).fieldOfView = s.Fov;
        }

        /// <summary>Put the Minecraft player where the Valheim player is.</summary>
        private static void SendTeleport(Link link, Player p)
        {
            var pos = p.transform.position;
            var mc = Mapping.ToMc(pos.x, pos.y + 0.05, pos.z);
            float yaw = GameCamera.instance != null ? GameCamera.instance.transform.eulerAngles.y : p.transform.eulerAngles.y;
            link.Send(new JObject
            {
                ["t"] = "teleport", ["x"] = mc.x, ["y"] = mc.y, ["z"] = mc.z,
                ["yaw"] = Mapping.ToMcYaw(yaw), ["pitch"] = 0f,
            });
            Plugin.Log.LogInfo($"sync: teleporting Minecraft to {mc.x:F1} {mc.y:F1} {mc.z:F1}");
        }

        /// <summary>Numbers for the F9 debug overlay.</summary>
        public static string DebugText()
        {
            var p = Player.m_localPlayer;
            if (!_haveSamples) return $"Valcraft: linked={Linked} sync={_sync}, no Minecraft samples yet";
            var s = Current();
            string target = ToValheim(s).ToString("F2");
            string actual = p != null ? p.transform.position.ToString("F2") : "(no player)";
            return $"Valcraft  active={Active} enabled={Enabled} sync={_sync} forwarding={InputForward.Forwarding}\n" +
                   $"MC   x={s.X:F2} y={s.Y:F2} z={s.Z:F2} ground={s.Ground} frozen={s.Frozen} fov={s.Fov}\n" +
                   $"VH target {target}\n" +
                   $"VH actual {actual}  kinematic={(p != null && Body(p).isKinematic)}\n" +
                   $"buffer {Clock.Buffered * 50:F0}/{Clock.TargetTicks * 50:F0} ms  jitter {Clock.Jitter * 50:F0} ms  " +
                   $"MC speed {Clock.McSpeed * 20:F1} t/s  ran dry {Clock.Starved} frames\n" +
                   $"terrain: {TerrainScanner.ChunksSent} chunks sent, {TerrainScanner.Pending} queued, {TerrainScanner.BoxesLastChunk} boxes in last";
        }

        /// <summary>Called every frame: syncing, and switching between following and normal play.</summary>
        public static void Update(Link link)
        {
            double now = Link.Now;
            _playTick = Clock.Advance(_lastFrame > 0 ? now - _lastFrame : 0);
            _lastFrame = now;

            if (!Wanted)
            {
                _sync = Sync.None; // sync again when following resumes (F8, portal, respawn)
            }
            else if (_sync == Sync.None || _sync == Sync.Pending && now - _syncSentAt > SyncTimeout)
            {
                SendTeleport(link, Player.m_localPlayer);
                _sync = Sync.Pending;
                _syncSentAt = now;
            }

            bool active = Active;
            if (active == _wasActive) return;
            _wasActive = active;
            var p = Player.m_localPlayer;
            if (p == null) return;
            if (!active) Body(p).isKinematic = false;
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
