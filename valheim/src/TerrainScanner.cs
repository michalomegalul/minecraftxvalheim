using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Valcraft
{
    /// <summary>
    /// Scans Valheim's world around the player into Minecraft-chunk-sized pieces and sends them,
    /// nearest first, one chunk per frame. For each 1x1 column: the terrain height, plus which
    /// 1-metre cells up to <see cref="ObjectHeight"/> above it hold something solid (rocks, trees,
    /// buildings). Minecraft builds that as blocks so its physics collide with Valheim's world.
    /// </summary>
    internal static class TerrainScanner
    {
        public const int RadiusChunks = 3;  // 7x7 chunks = 112 m across
        public const int ObjectHeight = 12; // keep in sync with Terrain.OBJECT_HEIGHT (Minecraft)
        private const float CellHalf = 0.49f;

        private static readonly HashSet<long> Sent = new HashSet<long>();
        private static readonly List<(int cx, int cz)> Queue = new List<(int, int)>();
        private static readonly Dictionary<long, int> Retries = new Dictionary<long, int>();
        private static int _centerX = int.MinValue, _centerZ;
        private static int _solidMask = -1;
        public static int ChunksSent => Sent.Count;
        public static int Pending => Queue.Count;

        public static void Reset()
        {
            Sent.Clear();
            Queue.Clear();
            Retries.Clear();
            _centerX = int.MinValue;
        }

        public static bool IsSent(int cx, int cz) => Sent.Contains(Key(cx, cz));

        public static void Update(Link link, Vector3 valheimPos)
        {
            if (ZoneSystem.instance == null) return;
            if (_solidMask == -1)
                _solidMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "blocker", "vehicle");

            var mc = Mapping.ToMc(valheimPos.x, valheimPos.y, valheimPos.z);
            int cx = Mapping.ChunkOf(mc.x), cz = Mapping.ChunkOf(mc.z);
            if (cx != _centerX || cz != _centerZ)
            {
                _centerX = cx;
                _centerZ = cz;
                Queue.Clear();
                for (int dz = -RadiusChunks; dz <= RadiusChunks; dz++)
                    for (int dx = -RadiusChunks; dx <= RadiusChunks; dx++)
                        if (!Sent.Contains(Key(cx + dx, cz + dz))) Queue.Add((cx + dx, cz + dz));
                Queue.Sort((a, b) => Dist2(a, cx, cz).CompareTo(Dist2(b, cx, cz)));
            }
            if (Queue.Count == 0 || !link.Connected) return;

            var (qx, qz) = Queue[0];
            Queue.RemoveAt(0);
            var msg = Scan(qx, qz, out bool complete);
            long key = Key(qx, qz);
            if (complete || Retries.TryGetValue(key, out int r) && r >= 30)
            {
                link.Send(msg);
                Sent.Add(key);
            }
            else
            {
                // Valheim hasn't loaded the terrain there yet; try again after the others.
                Retries[key] = Retries.TryGetValue(key, out int n) ? n + 1 : 1;
                Queue.Add((qx, qz));
            }
        }

        private static JObject Scan(int cx, int cz, out bool complete)
        {
            complete = true;
            var top = new JArray();
            var solid = new JArray();
            for (int lz = 0; lz < 16; lz++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    int bx = cx * 16 + lx, bz = cz * 16 + lz;
                    var col = Mapping.ToValheim(bx + 0.5, 0, bz + 0.5);
                    if (!ZoneSystem.instance.GetGroundHeight(new Vector3((float)col.x, 0f, (float)col.z), out float ground))
                    {
                        top.Add(JValue.CreateNull());
                        complete = false;
                        continue;
                    }
                    double surface = ground - Mapping.YOffset;
                    top.Add(System.Math.Round(surface, 3));

                    int n = (int)System.Math.Floor(surface);
                    for (int by = n; by <= n + ObjectHeight; by++)
                    {
                        var c = Mapping.BlockCenterInValheim(bx, by, bz);
                        var center = new Vector3((float)c.x, (float)c.y, (float)c.z);
                        if (Physics.CheckBox(center, Vector3.one * CellHalf, Quaternion.identity, _solidMask, QueryTriggerInteraction.Ignore))
                        {
                            solid.Add(bx);
                            solid.Add(by);
                            solid.Add(bz);
                        }
                    }
                }
            }
            return new JObject { ["t"] = "chunk", ["cx"] = cx, ["cz"] = cz, ["top"] = top, ["solid"] = solid };
        }

        private static int Dist2((int x, int z) c, int cx, int cz) => (c.x - cx) * (c.x - cx) + (c.z - cz) * (c.z - cz);

        private static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
    }
}
