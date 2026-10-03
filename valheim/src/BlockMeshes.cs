using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Valcraft
{
    /// <summary>
    /// Minecraft's blocks drawn in Valheim. Minecraft renders each 16x16x16 section with its own
    /// block and fluid renderers and sends the quads; we build one mesh per section and layer,
    /// textured with Minecraft's block atlas but using Valheim materials, so blocks get Valheim's
    /// lighting, shadows and fog.
    /// </summary>
    internal static class BlockMeshes
    {
        private static Texture2D _atlas;
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static readonly Dictionary<long, GameObject> Sections = new Dictionary<long, GameObject>();
        private static GameObject _root;
        private static readonly List<JObject> WaitingForAtlas = new List<JObject>();

        public static int SectionCount => Sections.Count;
        public static bool HaveAtlas => _atlas != null;

        public static void Reset()
        {
            foreach (var go in Sections.Values) if (go != null) UnityEngine.Object.Destroy(go);
            Sections.Clear();
            WaitingForAtlas.Clear();
        }

        public static void OnAtlas(JObject msg)
        {
            var png = Convert.FromBase64String((string)msg["png"]);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (!tex.LoadImage(png))
            {
                Plugin.Log.LogError("could not decode Minecraft's block atlas");
                return;
            }
            tex.filterMode = FilterMode.Point; // crisp Minecraft pixels
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.anisoLevel = 0;
            _atlas = tex;
            foreach (var m in Materials.Values) SetTexture(m);
            Plugin.Log.LogInfo($"Minecraft block atlas {tex.width}x{tex.height}");
            foreach (var w in WaitingForAtlas) OnMesh(w);
            WaitingForAtlas.Clear();
        }

        public static void OnMesh(JObject msg)
        {
            if (_atlas == null)
            {
                WaitingForAtlas.Add(msg);
                return;
            }
            int sx = (int)msg["sx"], sy = (int)msg["sy"], sz = (int)msg["sz"];
            long key = Key(sx, sy, sz);
            if (Sections.TryGetValue(key, out var old) && old != null) UnityEngine.Object.Destroy(old);
            Sections.Remove(key);

            var layers = (JObject)msg["layers"];
            var lights = (JArray)msg["lights"];
            if ((layers == null || !layers.HasValues) && (lights == null || lights.Count == 0)) return;

            if (_root == null) _root = new GameObject("Valcraft blocks");
            var v = Mapping.ToValheim(sx * 16, sy * 16, sz * 16);
            var section = new GameObject($"section {sx},{sy},{sz}");
            section.transform.SetParent(_root.transform, false);
            section.transform.position = new Vector3((float)v.x, (float)v.y, (float)v.z);
            AddLights(section, lights);
            foreach (var layer in layers?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                var mesh = BuildMesh((JObject)layer.Value);
                if (mesh == null) continue;
                var go = new GameObject(layer.Name);
                go.transform.SetParent(section.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = MaterialFor(layer.Name);
                r.shadowCastingMode = layer.Name == "translucent" ? ShadowCastingMode.Off : ShadowCastingMode.On;
                r.receiveShadows = true;
            }
            Sections[key] = section;
        }

        private const int MaxLightsPerSection = 8;

        /// <summary>
        /// Light-emitting blocks (torches, lava, glowstone) light Valheim too. Minecraft merges them
        /// per 4x4x4 cell; the brightest few per section become warm point lights.
        /// </summary>
        private static void AddLights(GameObject section, JArray lights)
        {
            if (lights == null) return;
            var list = new List<(Vector3 pos, float level)>();
            for (int i = 0; i + 3 < lights.Count; i += 4)
                list.Add((new Vector3((float)lights[i], (float)lights[i + 1], -(float)lights[i + 2]), (float)lights[i + 3]));
            foreach (var (pos, level) in list.OrderByDescending(l => l.level).Take(MaxLightsPerSection))
            {
                var go = new GameObject("light");
                go.transform.SetParent(section.transform, false);
                go.transform.localPosition = pos;
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                // Minecraft light reaches level-1 blocks; fall off a bit sooner so it doesn't wash out.
                light.range = Mathf.Max(2f, level * 0.8f);
                light.intensity = 0.6f + level / 15f;
                light.color = new Color(1f, 0.72f, 0.42f);
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForcePixel;
            }
        }

        /// <summary>Quads in section-local Minecraft coordinates -> a Unity mesh.</summary>
        private static Mesh BuildMesh(JObject data)
        {
            var p = (JArray)data["p"];
            var uv = (JArray)data["uv"];
            var c = (JArray)data["c"];
            int n = p.Count / 3;
            if (n < 4) return null;
            var verts = new Vector3[n];
            var uvs = new Vector2[n];
            var colors = new Color32[n];
            for (int i = 0; i < n; i++)
            {
                // Same mapping as everywhere: z flips. That reflection also turns Minecraft's
                // counter-clockwise front faces into Unity's clockwise ones, so winding stays.
                verts[i] = new Vector3((float)p[i * 3], (float)p[i * 3 + 1], -(float)p[i * 3 + 2]);
                // Minecraft's atlas v runs top-down; Unity's runs bottom-up.
                uvs[i] = i * 2 + 1 < uv.Count ? new Vector2((float)uv[i * 2], 1f - (float)uv[i * 2 + 1]) : Vector2.zero;
                int argb = i < c.Count ? (int)(long)c[i] : -1;
                colors[i] = new Color32((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, 255);
            }
            int quads = n / 4;
            var tris = new int[quads * 6];
            for (int q = 0; q < quads; q++)
            {
                int b = q * 4, t = q * 6;
                tris[t] = b; tris[t + 1] = b + 1; tris[t + 2] = b + 2;
                tris[t + 3] = b; tris[t + 4] = b + 2; tris[t + 5] = b + 3;
            }
            var mesh = new Mesh { indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors32 = colors;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A Valheim material per layer, borrowed from Valheim's own prefabs so blocks are lit like
        /// the rest of the world. Logged, so a wrong pick is easy to spot.
        /// </summary>
        private static Material MaterialFor(string layer)
        {
            if (Materials.TryGetValue(layer, out var m)) return m;
            var source = FindSourceMaterial(layer);
            m = source != null ? new Material(source) : new Material(Shader.Find("Standard"));
            m.name = "Valcraft " + layer;
            if (m.HasProperty("_Color")) m.color = Color.white;
            foreach (var prop in new[] { "_BumpMap", "_MetallicGlossMap", "_EmissionMap", "_DetailAlbedoMap" })
                if (m.HasProperty(prop)) m.SetTexture(prop, null);
            if (layer != "solid" && m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.5f);
            SetTexture(m);
            Materials[layer] = m;
            Plugin.Log.LogInfo($"blocks '{layer}': shader {m.shader.name} (from {(source != null ? source.name : "fallback")}), " +
                               $"textures [{string.Join(", ", m.GetTexturePropertyNames())}]");
            return m;
        }

        private static Material FindSourceMaterial(string layer)
        {
            var scene = ZNetScene.instance;
            if (scene == null) return null;
            // Opaque: a plain wooden building piece. Cutout: a plant with alpha-tested leaves.
            string[] candidates = layer == "solid"
                ? new[] { "wood_floor", "wood_wall_half", "stone_floor_2x2" }
                : new[] { "Pickable_Dandelion", "Bush01", "Beech_small1", "wood_floor" };
            foreach (var name in candidates)
            {
                var prefab = scene.GetPrefab(name);
                var r = prefab != null ? prefab.GetComponentsInChildren<Renderer>(true).FirstOrDefault(x => x.sharedMaterial != null) : null;
                if (r != null) return r.sharedMaterial;
            }
            return null;
        }

        private static void SetTexture(Material m)
        {
            if (_atlas == null) return;
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", _atlas);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", _atlas);
        }

        private static long Key(int x, int y, int z) => ((long)(x & 0x3FFFFF) << 42) | ((long)(z & 0x3FFFFF) << 20) | (long)(y & 0xFFFFF);
    }
}
