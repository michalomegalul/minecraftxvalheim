using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Valcraft
{
    /// <summary>
    /// Minecraft entities that are blocks: lit TNT, falling sand/gravel, dropped block items.
    /// Models arrive once per block ("emodel"); positions every tick ("entities"), smoothed here.
    /// </summary>
    internal static class EntityMeshes
    {
        private static readonly Dictionary<int, List<(string layer, Mesh mesh)>> Models = new Dictionary<int, List<(string, Mesh)>>();
        private static readonly Dictionary<int, (GameObject go, Vector3 target, float scale)> Live = new Dictionary<int, (GameObject, Vector3, float)>();

        public static void Reset()
        {
            foreach (var e in Live.Values) if (e.go != null) Object.Destroy(e.go);
            Live.Clear();
            Models.Clear();
        }

        public static void OnModel(JObject msg)
        {
            var list = new List<(string, Mesh)>();
            foreach (var layer in ((JObject)msg["layers"]).Properties())
            {
                var mesh = BlockMeshes.BuildMesh((JObject)layer.Value);
                if (mesh != null) list.Add((layer.Name, mesh));
            }
            Models[(int)msg["key"]] = list;
        }

        public static void OnEntities(JObject msg)
        {
            var seen = new HashSet<int>();
            var a = (JArray)msg["list"];
            for (int i = 0; i + 6 < a.Count; i += 7)
            {
                int id = (int)a[i], key = (int)a[i + 4];
                var v = Mapping.ToValheim((double)a[i + 1], (double)a[i + 2], (double)a[i + 3]);
                var target = new Vector3((float)v.x, (float)v.y, (float)v.z);
                float scale = (float)a[i + 5];
                seen.Add(id);
                if (!Live.TryGetValue(id, out var e) || e.go == null)
                {
                    var go = Create(key, scale);
                    if (go == null) continue;
                    go.transform.position = target;
                    e = (go, target, scale);
                }
                Live[id] = (e.go, target, scale);
            }
            var gone = new List<int>();
            foreach (var kv in Live) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone)
            {
                if (Live[id].go != null) Object.Destroy(Live[id].go);
                Live.Remove(id);
            }
        }

        /// <summary>Every frame: glide toward the latest position; dropped items spin like in Minecraft.</summary>
        public static void Update()
        {
            float t = 1f - Mathf.Exp(-20f * Time.deltaTime);
            foreach (var e in Live.Values)
            {
                if (e.go == null) continue;
                e.go.transform.position = Vector3.Lerp(e.go.transform.position, e.target, t);
                if (e.scale < 1f) e.go.transform.Rotate(0f, 90f * Time.deltaTime, 0f, Space.World);
            }
        }

        private static GameObject Create(int key, float scale)
        {
            if (!Models.TryGetValue(key, out var model) || !BlockMeshes.HaveAtlas) return null;
            var root = new GameObject("mc entity");
            // Model is 0..1 (z flipped to 0..-1): centre it over the entity's feet.
            var pivot = new GameObject("model");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localScale = Vector3.one * scale;
            pivot.transform.localPosition = new Vector3(-0.5f, 0f, 0.5f) * scale;
            foreach (var (layer, mesh) in model)
            {
                var go = new GameObject(layer);
                go.transform.SetParent(pivot.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = BlockMeshes.MaterialFor(layer);
                r.shadowCastingMode = ShadowCastingMode.On;
            }
            return root;
        }
    }
}
