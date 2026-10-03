# Minecraft × Valheim

Play Valheim as a Minecraft player: Minecraft movement, blocks, TNT and elytra inside Valheim's world.
Inspired by [SkyCraft](https://github.com/chasmlol/SkyCraft), which does this for Skyrim.

## How it works (planned)

Both games run at once. **Minecraft runs the gameplay, Valheim runs the world and draws the picture.**

```
 Minecraft (Fabric mod, hidden)              Valheim (BepInEx plugin)
   your movement/physics  ── position ───▶   player puppet + first-person camera
   placed/broken blocks   ── blocks ─────▶   real Unity blocks with MC textures
   collision queries      ◀── terrain ────   Valheim ground scanned into a block grid
   sword hits / TNT       ── hits ───────▶   damage creatures, dig craters
```

Unlike SkyCraft, we don't paste Minecraft's picture into the other game. Valheim is a Unity game,
so blocks become real objects in its world and get its lighting, shadows and fog for free.

## Roadmap

| Step | Goal | Status |
|---|---|---|
| 0 | BepInEx loads our plugin in Valheim | built, needs in-game check |
| 1 | **Link**: Fabric mod ⇄ Valheim plugin over localhost; walking in MC moves the Valheim player; first-person camera | |
| 2 | **Blocks**: blocks placed/broken in MC appear in Valheim | |
| 3 | **Ground**: Valheim terrain scanned into MC collision, so you walk Valheim hills in MC physics; digging | |
| 4 | **Combat**: MC hits damage Valheim creatures; TNT craters | |
| 5 | **Elytra** and polish | |

## Layout

| Path | What |
|---|---|
| `valheim/` | BepInEx plugin (C#, netstandard2.1) |
| `fabric/` | Minecraft 26.2 Fabric mod (Java), from step 1 |

## Setup (Linux, native Valheim)

1. Install [BepInExPack_Valheim](https://thunderstore.io/package/denikson/BepInExPack_Valheim/) into the Valheim folder
   (`BepInEx/`, `doorstop_libs/`, `doorstop_config.ini`, `start_game_bepinex.sh`).
2. Steam → Valheim → Properties → Launch options:
   ```
   ./start_game_bepinex.sh %command%
   ```
3. Build and deploy the plugin (copies into `BepInEx/plugins/Valcraft/`):
   ```
   cd valheim && dotnet build -c Release
   ```
4. Launch Valheim and load a world. You should see **"Valcraft loaded"** in the middle of the screen, and
   `Valcraft 0.1.0 loaded` in `BepInEx/LogOutput.log`.

Override the game path with `dotnet build -c Release -p:GameDir=/path/to/Valheim`.
