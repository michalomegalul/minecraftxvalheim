using System;
using System.Collections.Generic;

namespace Valcraft
{
    /// <summary>
    /// Turns vertical ray hits into 1/8-block collision boxes for Minecraft. Unity-free so it can be
    /// unit tested. All y values here are Minecraft y; "y8" values are in 1/8 blocks.
    /// </summary>
    internal static class Voxelize
    {
        public const int Sub = 8;

        /// <summary>
        /// Solid spans along one vertical line. <paramref name="tops"/> are where a downward ray
        /// entered each collider (its top), <paramref name="bottoms"/> where an upward ray entered it
        /// (its bottom). A collider seen only from above extends below the ray range, and vice versa.
        /// </summary>
        public static List<(int lo, int hi)> Spans(IEnumerable<(int id, double y)> tops, IEnumerable<(int id, double y)> bottoms,
            double yMin, double yMax)
        {
            var top = new Dictionary<int, double>();
            foreach (var (id, y) in tops) top[id] = top.TryGetValue(id, out var t) ? Math.Max(t, y) : y;
            var bottom = new Dictionary<int, double>();
            foreach (var (id, y) in bottoms) bottom[id] = bottom.TryGetValue(id, out var b) ? Math.Min(b, y) : y;

            var spans = new List<(int, int)>();
            foreach (var kv in top)
            {
                double lo = bottom.TryGetValue(kv.Key, out var b) ? b : yMin;
                if (lo < kv.Value) spans.Add(Quantize(lo, kv.Value));
            }
            foreach (var kv in bottom)
            {
                if (!top.ContainsKey(kv.Key) && kv.Value < yMax) spans.Add(Quantize(kv.Value, yMax));
            }
            return Merge(spans);
        }

        /// <summary>Round outward to 1/8 block, so thin things never vanish.</summary>
        public static (int lo, int hi) Quantize(double bottom, double top)
        {
            int lo = (int)Math.Floor(bottom * Sub), hi = (int)Math.Ceiling(top * Sub);
            return (lo, hi > lo ? hi : lo + 1);
        }

        /// <summary>Sort and join overlapping or touching spans.</summary>
        public static List<(int lo, int hi)> Merge(List<(int lo, int hi)> spans)
        {
            spans.Sort((a, b) => a.lo.CompareTo(b.lo));
            var merged = new List<(int lo, int hi)>();
            foreach (var s in spans)
            {
                if (merged.Count > 0 && s.lo <= merged[merged.Count - 1].hi)
                {
                    var last = merged[merged.Count - 1];
                    merged[merged.Count - 1] = (last.lo, Math.Max(last.hi, s.hi));
                }
                else merged.Add(s);
            }
            return merged;
        }

        /// <summary>
        /// Emit boxes for one 1x1 column at block (bx, bz) from an 8x8 grid of spans (grid[sx, sz]).
        /// Neighbouring sub-columns along x with the same span are joined into one box.
        /// Output: flat ints [x8Start, x8End, z8, y8Bottom, y8Top] per box (end exclusive).
        /// </summary>
        public static void EmitBoxes(int bx, int bz, List<(int lo, int hi)>[,] grid, List<int> output)
        {
            for (int sz = 0; sz < Sub; sz++)
            {
                // Spans already used by a run starting further left in this row.
                var used = new HashSet<(int sx, int lo, int hi)>();
                for (int sx = 0; sx < Sub; sx++)
                {
                    var spans = grid[sx, sz];
                    if (spans == null) continue;
                    foreach (var span in spans)
                    {
                        if (used.Contains((sx, span.lo, span.hi))) continue;
                        int end = sx + 1;
                        while (end < Sub && grid[end, sz] != null && grid[end, sz].Contains(span))
                        {
                            used.Add((end, span.lo, span.hi));
                            end++;
                        }
                        output.Add(bx * Sub + sx);
                        output.Add(bx * Sub + end);
                        output.Add(bz * Sub + sz);
                        output.Add(span.lo);
                        output.Add(span.hi);
                    }
                }
            }
        }
    }
}
