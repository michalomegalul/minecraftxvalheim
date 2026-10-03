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
| 2 | **Blocks**: blocks placed/broken in MC appear in Valheim (rendered by MC, lit by Valheim) | built, needs in-game check |
| 3 | **Ground**: Valheim terrain and objects built into the MC world, so MC physics collides with Valheim | ✅ works |
| 3b | **Digging**: pickaxe/shovel/TNT in Minecraft digs Valheim's terrain; sneaking = Valheim stealth | built, needs in-game check |
| 2b | **Minecraft GUI in Valheim**: hand, hotbar, hearts, inventory screens over Valheim; Valheim HUD hidden | built, needs in-game check |
| 4 | **Combat**: MC hits damage Valheim creatures | |
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
| Tab | Minecraft inventory (mouse works in it; Esc or Tab closes) |
| E (interact), Shift+Tab (Valheim inventory), Esc, M (map), Enter (chat) | Valheim |
| F10 | flip the Minecraft overlay if it's upside down |
| F8 | toggle following Minecraft on/off |
| F9 | debug overlay |

While a Valheim menu is open, nothing is forwarded and all Minecraft keys are released.

How the world gets into Minecraft: Valheim scans a 112 m square around you in 16×16 chunks
(nearest first, 4 ms per frame). Coordinates are 1:1: Minecraft (x, y, z) = Valheim (x, y − 40, −z).

- **Ground** becomes real Minecraft blocks, topped with a snow layer whose height matches Valheim's
  to 1/8 block, so slopes walk smoothly. Real blocks, so Minecraft tools and TNT can dig them.
- **Objects** (rocks, trees, buildings, stairs) become 1/8-block collision boxes, like SkyCraft's
  CollisionField: a quick 1 m check per cell, then 8×8 vertical ray pairs through occupied
  columns. A mixin feeds them into Minecraft's collision for players, so walls keep their real
  thickness and stairs are small steps. The 3×3 chunks around you are rescanned every 3 s, so
  opened doors and felled trees update.

- **Digging**: when ground blocks disappear in Minecraft (pickaxe, shovel, TNT, creepers), the
  column's highest remaining ground block is re-measured and Valheim lowers its terrain to match
  with its own terrain system (a registered `valcraft_dig` TerrainOp), so it's saved with the
  world like a Valheim pickaxe dig. Pits and craters show up in Valheim; tunnelling sideways
  doesn't change Valheim's surface. Ground is 9 blocks deep with bedrock below (Valheim allows
  digging 8 m down).
- **Blocks** you place in Minecraft show up in Valheim. Minecraft renders each changed 16×16×16
  section with its own block and fluid renderers into quads, and sends them with its block atlas
  (exported from the GPU once). Valheim builds meshes from them using its own materials, so
  stairs, glass, torches, water and lava look like Minecraft but get Valheim's light and shadows.
  Light-emitting blocks (torches, lava, glowstone, lanterns) also light Valheim: Minecraft merges
  them per 4×4×4 cell and Valheim adds a warm point light for the brightest few per section.
- **Minecraft's hand and GUI** are drawn over Valheim (like SkyCraft's hand/GUI layers). While
  linked, Minecraft skips drawing its world and clears to transparent, so its frame is just the
  hand, hotbar, hearts, crosshair and any open screen. Each frame is read back from the GPU into
  shared memory (`/dev/shm/valcraft_gui`), which Valheim maps and draws on top. Valheim's HUD and
  small minimap are hidden meanwhile. Minecraft's inventory and other screens get Valheim's mouse
  through Minecraft's own mouse handler, so dragging, shift-click and tooltips work.
- **Sneaking** in Minecraft crouches in Valheim: stealth, quieter footsteps, Sneak skill.

- **Water**: below Valheim's sea level (which is also its rivers and lakes) columns are filled
  with Minecraft water up to the sea surface, so you swim. It's placed without a flow tick, so it
  stays still instead of pouring off the edge of the built area. Valheim draws its own ocean, so
  this water isn't mirrored back.

Known limits: changes further than one chunk away are only picked up when you get
close.

Override the game path with `dotnet build -c Release -p:GameDir=/path/to/Valheim`.
