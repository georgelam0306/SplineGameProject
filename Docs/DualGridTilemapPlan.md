# Dual-Grid Tilemap Plan (Jess-style) for DerpTech2D

## Goals

- Add a Jess-style dual-grid tilemap system:
  - Author only on the world grid in Tiled.
  - Auto-derive an offset grid (dual grid) from world-grid terrain types.
  - Render a debug overlay first, then optional offset-grid tiles.
- Keep:
  - Tiled metadata and any future collision authoring on the world grid.
  - Existing `Tilemap` / `Tileset` / render systems unchanged in behavior.
- Respect repo rules:
  - No runtime “try X, otherwise Y” fallbacks.
  - No platform-specific branches; pure data/logic.

---

## 1. Tiled Authoring Conventions

- **World terrain layer**
  - Continue using the existing Tiled map (`Assets/testmap.tmj`), with one main tile layer that we treat as the **world grid**.
  - You paint fully-filled terrain tiles (grass/dirt/sand/water) here; no corner/transition tiles required.
- **Terrain semantics**
  - We interpret tile indices from that layer as **terrain types** via a mapping in code (see below).
- **Collision**
  - For this dual-grid work, we do **not** change how collision is authored; you can keep any collision layers/metadata world-grid-based and we’ll integrate collision separately later if desired.

No Tiled format changes are required for the first implementation.

---

## 2. New Concepts and Enums

Checklist:
- [x] Implement `TerrainType` enum.
- [x] Implement `TerrainPalette` mapping.
- [x] Implement `DualGridLookup` mapping.

Add a terrain enum and mapping config in C# (under `DerpTech2D` or `DerpTech2D/Components`):

- `TerrainType` enum
  - Values: `None = 0, Grass, Dirt, Sand, Water` (extensible later).

- `TerrainPalette` static class
  - Holds:
    - `static TerrainType GetTerrainForTileIndex(int tileIndex)` – maps `Tilemap.Tiles[]` indices to `TerrainType`.
  - Implementation for now: a few `switch` / range checks matching the current tileset layout.
  - No runtime fallback: if an index is not in any configured range and is not explicitly `None`, throw during map load (misconfigured palette) rather than guess.

- `DualGridLookup` static class
  - Defines a struct or helper representing a 4-tuple of `TerrainType` (TL, TR, BL, BR).
  - Maintains a dictionary or static table mapping `(TL, TR, BL, BR)` → atlas tile index for the **offset grid**.
  - Returns:
    - A valid atlas index for known combinations.
    - `-1` for “no overlay tile” in combinations you deliberately choose not to support yet.
  - No implicit heuristics like “if unknown, pick Grass”; unknown combos either map to `-1` (explicit “no tile”) or are treated as configuration errors, depending on what you decide.

---

## 3. New Components

Checklist:
- [x] Implement `TerrainGrid` component.
- [x] Implement `DualGrid` component.

All new components are structs implementing `IComponent`, so the AOT generator picks them up automatically.

### 3.1 TerrainGrid

File: `DerpTech2D/Components/TerrainGrid.cs`

- Fields:
  - `int Width;`
  - `int Height;`
  - `TerrainType[]? Cells;` // length = Width * Height, in world-grid coordinates
- Semantics:
  - Each world-grid cell is classified to a single `TerrainType` using `TerrainPalette`.

### 3.2 DualGrid

File: `DerpTech2D/Components/DualGrid.cs`

- Fields:
  - `int Width;`   // world grid width
  - `int Height;`  // world grid height
  - `int[]? CornerTileIndices;`
    - Size `(Width + 1) * (Height + 1)`.
    - Each entry is an atlas index for the **offset-grid** tile at the corresponding world-space corner.
    - Value `-1` means “no overlay tile here”.
- Semantics:
  - `CornerTileIndices` is derived at load time using `DualGridLookup` and `TerrainGrid`.

No platform branches or conditionals; both components are purely data.

---

## 4. Changes to `TiledMapLoadSystem`

Checklist:
- [x] Build `TerrainGrid` from `Tilemap.Tiles`.
- [x] Build `DualGrid` corner tiles.

File: `DerpTech2D/Systems/Load/TiledMapLoadSystem.cs`

Keep the current behavior, and extend it.

### 4.1 World-grid tilemap (unchanged)

- Leave as-is:
  - `Tilemap.Width`, `Height`, `TileSize`, `Tiles` filled from the first tile layer.
- Continue to treat the first tile layer as the **world grid**.

### 4.2 Build TerrainGrid

- After computing `indices` (the `int[]` atlas indices currently stored in `Tilemap.Tiles`):
  - Allocate `TerrainType[] terrainCells = new TerrainType[mapW * mapH];`
  - For each cell `i`:
    - If `indices[i] < 0` (empty), set `terrainCells[i] = TerrainType.None`.
    - Otherwise, call `TerrainPalette.GetTerrainForTileIndex(indices[i])`.
  - Fail fast if `GetTerrainForTileIndex` throws (misconfigured mapping).
- Attach to entity via:
  - `CommandBuffer.AddComponent(id, new TerrainGrid { Width = mapW, Height = mapH, Cells = terrainCells });`

### 4.3 Build DualGrid (offset grid)

- Allocate `int[] cornerTiles = new int[(mapW + 1) * (mapH + 1)];`
- For each corner coordinate `(cx, cy)` where `0 ≤ cx ≤ mapW`, `0 ≤ cy ≤ mapH`:
  - Look up up to four world cells:
    - TL: `(cx - 1, cy - 1)`
    - TR: `(cx,     cy - 1)`
    - BL: `(cx - 1, cy)`
    - BR: `(cx,     cy)`
  - If any of these are out of bounds, treat them as `TerrainType.None`.
  - Pack `(TL, TR, BL, BR)` into a key and query `DualGridLookup`.
    - If the lookup returns a valid index (0+): assign that to `cornerTiles[cx + cy * (mapW + 1)]`.
    - If the lookup returns `-1`, set `cornerTiles[...] = -1` (explicit “no tile”).
- Attach to entity:
  - `CommandBuffer.AddComponent(id, new DualGrid { Width = mapW, Height = mapH, CornerTileIndices = cornerTiles });`

### 4.4 Invariants and errors

- If `terrainCells` or `cornerTiles` cannot be built (e.g., invalid tile indices), throw `InvalidOperationException` during map load.
- Do not silently ignore misconfigurations.

All dual-grid data (terrain classification and corner lookup) is computed once in `TiledMapLoadSystem`, not in the render loop.

---

## 5. Dual-Grid Debug Render System (Phase 1)

Checklist:
- [x] Implement `DualGridDebugRenderSystem`.

File: `DerpTech2D/Systems/Render/World/DualGridDebugRenderSystem.cs`

Purpose: draw a simple overlay to visualize the dual grid and terrain combinations before full dual-grid tiles are used.

- System type:
  - `public sealed class DualGridDebugRenderSystem : QuerySystem<DualGrid, Tilemap, Transform2D>`
- Behavior:
  - For each entity with `DualGrid`, `Tilemap`, `Transform2D`:
    - System runs between `WorldCameraBeginSystem` and `WorldCameraEndSystem`, so it draws in world space.
    - Compute visible range:
      - Similar to `RenderTilemapSystem`, using `CameraFollowShared` and `Tilemap.TileSize`, but over corners:
        - `cx` in `[0..Width]`, `cy` in `[0..Height]`.
    - For each visible corner:
      - Read `cornerTiles[idx]`.
      - Convert `(cx, cy)` to world position:
        - `float worldX = tf.X + cx * tileSize;`
        - `float worldY = tf.Y + cy * tileSize;`
      - Draw:
        - If `cornerTiles[idx] >= 0`, draw a small square or circle (few pixels) at that world position.
        - Optionally color code based on the underlying terrain combination (via `TerrainGrid`) if needed.
- Config:
  - Initially always on; later can be gated behind a compile-time symbol or `const bool` flag.

This system is allocation-free per frame and uses no platform checks.

---

## 6. Dual-Grid Tile Rendering (Phase 2)

Checklist:
- [x] Implement `RenderDualGridSystem`.

File: `DerpTech2D/Systems/Render/World/RenderDualGridSystem.cs`

Purpose: draw actual dual-grid tiles (rounded corners / transitions) from the same atlas, on top of the world grid tiles.

- System type:
  - `public sealed class RenderDualGridSystem : QuerySystem<DualGrid, Tileset, Transform2D>`
- Behavior:
  - For each entity with `DualGrid`, `Tileset`, `Transform2D`:
    - Early-out if `Tileset.Loaded` is false or dimensions invalid.
    - Iterate visible corners `(cx, cy)` as in the debug system.
    - For each corner:
      - Read `CornerTileIndices[idx]`; if `< 0`, `continue`.
      - Convert atlas index to source rectangle:
        - Use the same math as `RenderTilemapSystem` with `Columns`, `SrcTileWidth`, `SrcTileHeight`, `SrcMargin`, `SrcSpacing`.
      - Destination rectangle:
        - Center at `(tf.X + cx * tileSize, tf.Y + cy * tileSize)`.
        - Size: one tile wide/high (`tileSize x tileSize`), adjusted as needed to match the atlas layout.
      - Draw via `Raylib.DrawTexturePro`.
- Ordering:
  - `RenderDualGridSystem` runs after `RenderTilemapSystem` but before `PlayerRenderSystem`, so:
    - World grid base tiles first.
    - Then dual-grid overlay for edges/corners.
    - Then player and other entities.

No additional camera state; relies on the existing Begin/EndMode2D pass.

---

## 7. Wiring into `Game` (SystemRoot)

Checklist:
- [x] Add new systems to `SystemRoot` in `Game`.

---

## 8. Layering and Multiple Terrains (Future Work)

The current implementation operates on a single shared dual-grid per map. A single
corner is "owned" by one primary terrain (currently selected via a fixed priority),
which is sufficient for debugging shapes but not for fully-styled multi-material
worlds (e.g., dirt, sand, grass, water all with their own outlines).

Planned direction:

- Use Tiled's support for multiple layers:
  - Base fill layers (e.g., water, dirt) authored directly in Tiled.
  - One or more overlay layers for "foreground" terrains (e.g., grass) rendered above.
- For dual-grid:
  - Either:
    - Run one dual-grid pass per terrain (or terrain pair) as a separate overlay layer, or
    - Use tilesets that encode specific terrain pairs (e.g., Grass-Dirt, Grass-Sand) and key
      the dual-grid lookup on terrain pairs instead of a single primary terrain.
  - Each layer will have its own tileset, z-order, and culling, but reuse the same
    4-bit pattern lookup logic.
- For now (test setup):
  - Procedural map uses Perlin noise for `Dirt` with a `Water` background.
  - A second Perlin field creates `Grass` patches that override only `Dirt` (never water).
  - Dual-grid lookup is applied once per corner, relative to the highest-priority
    terrain present (currently treating `Grass` > `Dirt` > `Water`), which approximates
    a single overlay layer on top of the base fill.

File: `DerpTech2D/Ecs.cs`

Modify the `SystemRoot` initialization to add the new systems in the right order.

### 7.1 Current order

- `TiledMapLoadSystem`
- `TilesetLoadSystem`
- `SpriteSheetLoadSystem`
- `PlayerInputSystem`
- `PlayerMovementSystem`
- `PlayerCameraFollowSystem`
- `WorldCameraBeginSystem`
- `RenderTilemapSystem`
- `PlayerRenderSystem`
- `WorldCameraEndSystem`

### 7.2 Updated order

- `TiledMapLoadSystem`
- `TilesetLoadSystem`
- `SpriteSheetLoadSystem`
- `PlayerInputSystem`
- `PlayerMovementSystem`
- `PlayerCameraFollowSystem`
- `WorldCameraBeginSystem`
- `RenderTilemapSystem`          // world grid
- `RenderDualGridSystem`         // dual-grid tiles (phase 2)
- `DualGridDebugRenderSystem`    // debug overlay (phase 1 & ongoing)
- `PlayerRenderSystem`
- `WorldCameraEndSystem`

`RenderDualGridSystem` can be introduced only after the debug overlay is validated; wiring is easy to adjust.

---

## 8. Performance and Safety Considerations

- **Per-frame**
  - No allocations: loops are over arrays (`Tiles`, `CornerTileIndices`) and simple arithmetic.
  - Camera culling: both render systems compute minimal visible corner ranges from camera/world bounds, mirroring `RenderTilemapSystem`.
- **Load-time**
  - All dual-grid data (terrain classification and corner lookup) is computed once in `TiledMapLoadSystem`, not in the render loop.
- **Errors**
  - Misconfigured tiles → terrain mapping throws during load.
  - Missing corner patterns:
    - If treated as “no overlay tile”, we use `-1` and skip drawing (explicit behavior).
    - Alternatively, treat missing patterns as `InvalidOperationException` during baking for stricter enforcement.

---

## 9. Things Explicitly Not Changed

- No changes to:
  - `Application` / `Main` / raylib setup.
  - Player input, movement, camera systems.
  - Any Tiled metadata parsing beyond reading the existing tile layer.
- No new collision behavior:
  - Movement remains free for now; dual-grid is purely for terrain visuals and debug rendering in this iteration.
- No runtime fallbacks or platform branches:
  - All behavior is deterministic and configuration-driven.
