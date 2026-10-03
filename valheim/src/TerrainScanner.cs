using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Valcraft
{
    /// <summary>
    /// Scans Valheim's world around the player in Minecraft-chunk-sized pieces and sends them,
    /// nearest first, within a per-frame time budget.
    ///
    /// Per 1x1 column: the terrain height (Minecraft builds it as blocks), and objects (rocks,
    /// trees, buildings, stairs) as 1/8-block collision boxes like SkyCraft's CollisionField.
    /// Objects: a cheap 1 m CheckBox per cell first; only occupied columns get 8x8 vertical ray
    /// pairs (down finds tops, up finds bottoms), see <see cref="Voxelize"/>.
    /// Nearby chunks are rescanned for objects every few seconds (doors, felled trees).
    /// </summary>
    internal static class TerrainScanner
    {
        public const int RadiusChunks = 3;  // 7x7 chunks = 112 m across
        public const int ObjectHeight = 12; // metres above the ground scanned for objects
        private const double FrameBudgetMs = 4.0;
        private const float RescanInterval = 3f;

        private static readonly HashSet<long> Sent = new HashSet<long>();
        private static readonly List<(int cx, int cz)> Queue = new List<(int, int)>();
        private static readonly Dictionary<long, int> Retries = new Dictionary<long, int>();
        private static readonly RaycastHit[] Hits = new RaycastHit[32];
        private static int _centerX = int.MinValue, _centerZ;
        private static int _solidMask = -1;
        private static IEnumerator _scan;
        private static float _nextRescan;
        // Chunks whose terrain changed in Valheim (after a dig), rescanned once the heightmap updates.
        private static readonly Dictionary<long, (int cx, int cz, float at)> TerrainRescans = new Dictionary<long, (int, int, float)>();
        private const float DigSettleSeconds = 1.5f;
        private static int _rescanIndex;
        public static int ChunksSent => Sent.Count;
        public static int Pending => Queue.Count;
        public static int BoxesLastChunk;

        public static void RequestTerrainRescan(int cx, int cz)
        {
            TerrainRescans[Key(cx, cz)] = (cx, cz, Time.time + DigSettleSeconds);
        }

        public static void Reset()
        {
            TerrainRescans.Clear();
            Sent.Clear();
            Queue.Clear();
            Retries.Clear();
            _scan = null;
            _centerX = int.MinValue;
        }

        public static void Update(Link link, Vector3 valheimPos)
        {
            if (ZoneSystem.instance == null || !link.Connected) return;
            if (_solidMask == -1)
                _solidMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "blocker", "vehicle");

            var mc = Mapping.ToMc(valheimPos.x, valheimPos.y, valheimPos.z);
            int cx = Mapping.ChunkOf(mc.x), cz = Mapping.ChunkOf(mc.z);
            if (cx != _centerX || cz != _centerZ) Recenter(cx, cz);

            var clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalMilliseconds < FrameBudgetMs)
            {
                if (_scan == null && !StartNext(link)) break;
                if (!_scan.MoveNext()) _scan = null;
            }
        }

        private static void Recenter(int cx, int cz)
        {
            _centerX = cx;
            _centerZ = cz;
            Queue.Clear();
            for (int dz = -RadiusChunks; dz <= RadiusChunks; dz++)
                for (int dx = -RadiusChunks; dx <= RadiusChunks; dx++)
                    if (!Sent.Contains(Key(cx + dx, cz + dz))) Queue.Add((cx + dx, cz + dz));
            Queue.Sort((a, b) => Dist2(a, cx, cz).CompareTo(Dist2(b, cx, cz)));
        }

        private static bool StartNext(Link link)
        {
            foreach (var kv in TerrainRescans)
            {
                if (Time.time < kv.Value.at) continue;
                TerrainRescans.Remove(kv.Key);
                _scan = Scan(link, kv.Value.cx, kv.Value.cz, withTerrain: true);
                return true;
            }
            if (Queue.Count > 0)
            {
                var (qx, qz) = Queue[0];
                Queue.RemoveAt(0);
                _scan = Scan(link, qx, qz, withTerrain: true);
                return true;
            }
            // Nothing new: keep the 3x3 chunks around the player fresh (objects only).
            if (Time.time < _nextRescan) return false;
            _nextRescan = Time.time + RescanInterval / 9f;
            int i = _rescanIndex++ % 9;
            _scan = Scan(link, _centerX + i % 3 - 1, _centerZ + i / 3 - 1, withTerrain: false);
            return true;
        }

        private static IEnumerator Scan(Link link, int cx, int cz, bool withTerrain)
        {
            long key = Key(cx, cz);
            if (!withTerrain && !Sent.Contains(key)) yield break;

            bool complete = true;
            var top = new JArray();
            var boxes = new List<int>();
            var cellSolid = new bool[ObjectHeight + 1];
            var grid = new List<(int lo, int hi)>[Voxelize.Sub, Voxelize.Sub];
            var tops = new List<(int, double)>();
            var bottoms = new List<(int, double)>();

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

                    // Cheap pass: anything at all in each 1 m cell above the ground?
                    bool any = false;
                    for (int i = 0; i <= ObjectHeight; i++)
                    {
                        var c = Mapping.BlockCenterInValheim(bx, n + i, bz);
                        cellSolid[i] = Physics.CheckBox(new Vector3((float)c.x, (float)c.y, (float)c.z), Vector3.one * 0.5f,
                            Quaternion.identity, _solidMask, QueryTriggerInteraction.Ignore);
                        any |= cellSolid[i];
                    }
                    if (!any) continue;

                    // Fine pass: 8x8 vertical ray pairs through this column.
                    double yMin = n - 1, yMax = n + ObjectHeight + 1;
                    for (int sz = 0; sz < Voxelize.Sub; sz++)
                    {
                        for (int sx = 0; sx < Voxelize.Sub; sx++)
                        {
                            var p = Mapping.ToValheim(bx + (sx + 0.5) / Voxelize.Sub, 0, bz + (sz + 0.5) / Voxelize.Sub);
                            Cast(new Vector3((float)p.x, (float)(yMax + Mapping.YOffset), (float)p.z), Vector3.down, yMax - yMin, tops);
                            Cast(new Vector3((float)p.x, (float)(yMin + Mapping.YOffset), (float)p.z), Vector3.up, yMax - yMin, bottoms);
                            var spans = tops.Count + bottoms.Count > 0 ? Voxelize.Spans(tops, bottoms, yMin, yMax) : null;
                            grid[sx, sz] = spans;
                            tops.Clear();
                            bottoms.Clear();
                        }
                    }
                    // A cell the cheap pass saw but no ray found (a collider bigger than the ray
                    // range, which rays starting inside it can't see): fill the whole cell.
                    for (int i = 0; i <= ObjectHeight; i++)
                    {
                        if (!cellSolid[i] || AnySpanIn(grid, (n + i) * Voxelize.Sub, (n + i + 1) * Voxelize.Sub)) continue;
                        for (int sz = 0; sz < Voxelize.Sub; sz++)
                            for (int sx = 0; sx < Voxelize.Sub; sx++)
                            {
                                grid[sx, sz] = grid[sx, sz] ?? new List<(int, int)>();
                                grid[sx, sz].Add(((n + i) * Voxelize.Sub, (n + i + 1) * Voxelize.Sub));
                                grid[sx, sz] = Voxelize.Merge(grid[sx, sz]);
                            }
                    }
                    Voxelize.EmitBoxes(bx, bz, grid, boxes);
                    System.Array.Clear(grid, 0, grid.Length);
                    yield return null; // fine columns are the expensive part; let the frame budget decide
                }
            }

            if (withTerrain && !complete && (!Retries.TryGetValue(key, out int r) || r < 30))
            {
                // Valheim hasn't loaded the terrain there yet; try again after the others.
                Retries[key] = Retries.TryGetValue(key, out int n2) ? n2 + 1 : 1;
                Queue.Add((cx, cz));
                yield break;
            }

            var flat = new JArray();
            foreach (int v in boxes) flat.Add(v);
            var msg = new JObject { ["t"] = "chunk", ["cx"] = cx, ["cz"] = cz, ["boxes"] = flat };
            if (withTerrain) msg["top"] = top;
            link.Send(msg);
            Sent.Add(key);
            BoxesLastChunk = boxes.Count / 5;
        }

        private static void Cast(Vector3 origin, Vector3 dir, double length, List<(int, double)> into)
        {
            int count = Physics.RaycastNonAlloc(origin, dir, Hits, (float)length, _solidMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                into.Add((Hits[i].colliderInstanceID, Hits[i].point.y - Mapping.YOffset));
        }

        private static bool AnySpanIn(List<(int lo, int hi)>[,] grid, int lo, int hi)
        {
            foreach (var spans in grid)
            {
                if (spans == null) continue;
                foreach (var s in spans)
                    if (s.lo < hi && s.hi > lo) return true;
            }
            return false;
        }

        private static int Dist2((int x, int z) c, int cx, int cz) => (c.x - cx) * (c.x - cx) + (c.z - cz) * (c.z - cz);

        private static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
    }
}
