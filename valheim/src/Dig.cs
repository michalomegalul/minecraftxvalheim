using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Valcraft
{
    /// <summary>
    /// Minecraft dug ground (pickaxe, shovel, TNT): lower Valheim's terrain to match, the same
    /// way Valheim's own pickaxe does, with a TerrainOp. Saved with the world like any dig.
    ///
    /// Valheim sends terrain ops over the network as a prefab-name hash and looks the settings up
    /// in ObjectDB, so we register our own "valcraft_dig" prefab there. Its fixed settings lower
    /// the vertices around a point to the point's height, so placing it sets the depth.
    /// </summary>
    internal static class Dig
    {
        private const string PrefabName = "valcraft_dig";
        private static GameObject _prefab;
        private static ObjectDB _registeredIn;

        public static void OnDig(JObject msg)
        {
            if (!EnsureRegistered() || ZoneSystem.instance == null) return;
            var cols = (JArray)msg["cols"];
            for (int i = 0; i + 2 < cols.Count; i += 3)
            {
                int x = (int)cols[i], z = (int)cols[i + 1];
                double surface = (double)cols[i + 2];
                var v = Mapping.ToValheim(x + 0.5, surface, z + 0.5);
                var pos = new Vector3((float)v.x, (float)v.y, (float)v.z);
                if (ZoneSystem.instance.GetGroundHeight(pos, out float ground) && ground <= pos.y + 0.02f) continue;

                var op = Object.Instantiate(_prefab, pos + Vector3.up * 0.01f, Quaternion.identity);
                op.SetActive(true); // TerrainOp.Awake applies the op and destroys the object
                TerrainScanner.RequestTerrainRescan(Mapping.ChunkOf(x), Mapping.ChunkOf(z));
            }
        }

        private static bool EnsureRegistered()
        {
            var db = ObjectDB.instance;
            if (db == null) return false;
            if (_prefab == null)
            {
                _prefab = new GameObject(PrefabName);
                _prefab.SetActive(false); // inactive, so Awake only runs on spawned copies
                Object.DontDestroyOnLoad(_prefab);
                var op = _prefab.AddComponent<TerrainOp>();
                op.m_settings = new TerrainOp.Settings
                {
                    m_level = false,
                    m_smooth = false,
                    // Lower (never raise) vertices within the radius to the op's height. A Minecraft
                    // column is 1x1 and Valheim's vertices sit on its corners, ~0.71 m from the centre.
                    m_raise = true,
                    m_raiseRadius = 0.75f,
                    m_raisePower = 0f,
                    m_raiseDelta = -0.01f,
                    m_square = false,
                    m_paintCleared = true,
                    m_paintType = TerrainModifier.PaintType.Dirt,
                    m_paintRadius = 0.75f,
                };
            }
            if (_registeredIn != db)
            {
                db.m_terrainOpsByHash[PrefabName.GetStableHashCode()] = _prefab.GetComponent<TerrainOp>();
                _registeredIn = db;
            }
            return true;
        }
    }
}
