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
| 0 | BepInEx loads our plugin in Valheim | ✅ works |
| 1a | **Link**: Fabric mod ⇄ Valheim plugin over localhost; walking in MC moves the Valheim player; first-person camera | ✅ works |
| 1b | **Input**: play through the Valheim window, keys/mouse forwarded to a hidden Minecraft | ✅ works |
| 2 | **Blocks**: blocks placed/broken in MC appear in Valheim | |
| 3 | **Ground**: Valheim terrain and objects built into the MC world as blocks, so MC physics collides with Valheim; digging | collision built, needs in-game check |
| 4 | **Combat**: MC hits damage Valheim creatures; TNT craters | |
| 5 | **Elytra** and polish | |

## Layout

| Path | What |
|---|---|
| `valheim/` | BepInEx plugin (C#, netstandard2.1) |
| `fabric/` | Minecraft 26.2 Fabric mod (Java), client-only |
| `valheim.Tests/` | Unit tests for Unity-free plugin code (`cd valheim.Tests && dotnet test`) |

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

5. Build the Minecraft mod and copy it into the Prism `26.2` instance:
   ```
   cd fabric && ./gradlew deploy
   ```

### Playing
Valheim's world is built into the Minecraft world as (invisible) blocks, so Minecraft needs an empty
singleplayer world: **Create New World → World Type: Superflat → Customize → Presets → The Void**.

Start Valheim and load a world, then start Minecraft, open that void world and minimize it.
Minecraft says `Syncing with Valheim...`, holds you in place for a moment while the ground around you
is built, then `Synced with Valheim`. Minecraft chat says `[Valcraft] Linked to Valheim`, and Valheim switches to
first person. Play in the Valheim window:

| Key | Goes to |
|---|---|
| WASD, Space, Shift (sneak), Ctrl (sprint), Q (drop) | Minecraft |
| Mouse look, left/right click, 1–9, scroll | Minecraft |
| E (interact), Tab (inventory), Esc, M (map), Enter (chat) | Valheim |
| F8 | toggle following Minecraft on/off |
| F9 | debug overlay |

While a Valheim menu is open, nothing is forwarded and all Minecraft keys are released.

How the world gets into Minecraft: Valheim scans a 112 m square around you, one 16×16 chunk per
frame. For every 1 m column it sends the ground height, and which 1 m cells up to 12 m above it hold
something solid (rocks, trees, buildings). Minecraft fills the ground with blocks topped by a snow
layer whose height matches Valheim's to 1/8 block (so slopes walk smoothly), and puts barrier blocks
in the solid cells. Coordinates are 1:1: Minecraft (x, y, z) = Valheim (x, y − 40, −z).

Known limits: thin walls become 1–2 blocks thick, doors are solid, no water yet, and changes to the
Valheim world after a chunk was scanned (felled trees, new buildings) aren't picked up yet.

Override the game path with `dotnet build -c Release -p:GameDir=/path/to/Valheim`.
