# Derp.Doc Asset Column Types Plan (TextureAsset + MeshAsset)

Status: revised against current codebase as of 2026-02-11 and implemented in this branch.

## Goals

1. Add two new column kinds: `TextureAsset` and `MeshAsset`.
2. Store cell values as relative paths under `{GameRoot}/Assets`.
3. Show visual previews for texture assets in grid cells.
4. Provide modal asset browser selection UX.
5. Keep export and MCP support first-class.

## Non-Goals (for this milestone)

1. No runtime fallback behavior.
2. No GPU readback-based 3D mesh preview rendering yet.
3. No dynamic GPU texture eviction in-session (engine does not expose texture slot release/reuse).

## Codebase Constraints That Change the Plan

1. Post-SDF thumbnail drawing must happen inside a render pass.
Current loop is `Im -> RenderSdf -> EndDrawing` in `Derp.Doc/Editor/DocEditorApp.cs`.
`Derp.DrawTexturePro` queues into `IndirectBatcher` and only flushes during render-pass end.

2. `TextureArray` is append-only today.
`Engine/src/Engine/Rendering/TextureArray.cs` has no unload/free/reuse API.
A strict LRU cache with GPU eviction cannot be implemented correctly right now.

3. Mesh preview readback path is missing.
`RenderTexture` is not created for transfer-source readback, and Derp API does not expose render target pixel readback.

4. `DocWorkspace` already tracks `GameRoot`.
Only `AssetsRoot` convenience resolution is needed.

## Architectural Decisions

1. Use existing engine camera pass APIs for post-SDF texture overlay draws.
`RenderSdf()` stays first, then run a short 2D camera pass and flush thumbnail draws there.

2. Use bounded thumbnail loading budget instead of GPU LRU eviction.
Cap loaded preview textures per app session to stay below engine texture limits.

3. Ship MeshAsset as path + browser + placeholder preview first.
Add real mesh thumbnail rendering only after engine readback and texture-slot lifecycle support are available.

## Cell Value Contract

1. `StringValue` stores relative path from `{GameRoot}/Assets`.
Examples: `Textures/hero.png`, `Meshes/tree.glb`.
2. Empty string means no asset selected.
3. If no game root exists, values remain plain strings and browser is disabled.

## Revised Implementation Plan

### Phase 0: Rendering Foundation (Required Before Thumbnails)

#### Step 0.1: Create deferred thumbnail draw list
Create `Derp.Doc/Assets/ThumbnailDrawList.cs`.

Responsibilities:
1. Store draw commands as structs: texture, source, dest, tint.
2. `Clear()` at frame start.
3. `Add(...)` during cell rendering.
4. `Flush()` to issue `Derp.DrawTexturePro(...)` calls.

#### Step 0.2: Use explicit overlay pass in editor loop
Modify `Derp.Doc/Editor/DocEditorApp.cs`.

New frame order:
1. `ThumbnailDrawList.Clear()` before `Im.Begin(...)`.
2. Build UI and cell content as today.
3. `DerpEngine.RenderSdf()`.
4. Begin overlay pass:
`DerpEngine.BeginCamera2D(Camera2D.Default(DerpEngine.GetScreenWidth(), DerpEngine.GetScreenHeight()))`.
5. `ThumbnailDrawList.Flush()`.
6. `DerpEngine.EndCamera2D()`.
7. `DerpEngine.EndDrawing()`.

Acceptance criteria:
1. Existing UI still renders correctly.
2. Draw calls in `ThumbnailDrawList` appear on top of SDF UI.

### Phase 1: Data Model and Core Integrations

#### Step 1.1: Add enum kinds
Update `Derp.Doc.Core/Model/DocColumnKind.cs`:
1. Add `TextureAsset`.
2. Add `MeshAsset`.

#### Step 1.2: Default cell value
Update `Derp.Doc.Core/Model/DocCellValue.cs`:
1. `TextureAsset => Text("")`.
2. `MeshAsset => Text("")`.

#### Step 1.3: Storage verification
No structural serializer changes required.
`ProjectSerializer` and `ProjectLoader` already store unknown non-number/non-checkbox kinds as strings.

#### Step 1.4: Export mapping
Update `Derp.Doc.Core/Export/ExportColumnModel.cs`:
1. Map `TextureAsset` to `StringHandle`.
2. Map `MeshAsset` to `StringHandle`.

No skip behavior needed in `DocExportPipeline` because only `Subtable` and `Spline` are skipped.

#### Step 1.5: MCP parser and MCP tool docs
Update `Derp.Doc.Core/Mcp/DerpDocMcpServer.cs`:
1. Extend `TryParseColumnKind` for `TextureAsset` and `MeshAsset`.

Update `Derp.Doc.Core/Mcp/DerpDocMcpTools.cs`:
1. Extend `kind` description string to include new kinds.

#### Step 1.6: Spreadsheet kind metadata
Update `Derp.Doc/Panels/SpreadsheetRenderer.cs`:
1. Add to `_columnKindNames` and `_columnKinds`.
2. Add default names in `GenerateDefaultColumnName`.
3. Add header icon mapping in `GetColumnKindIconText`.

#### Step 1.7: Basic placeholder rendering/editing
Update `SpreadsheetRenderer` render and edit switches:
1. Render filename/path text for both asset kinds.
2. Allow text editing as temporary fallback before browser wiring.

### Phase 2: Texture Thumbnail Pipeline

#### Step 2.1: Add image decoder dependency
Update `Derp.Doc/Derp.Doc.csproj`:
1. Add `StbImageSharp` package reference.

#### Step 2.2: Add thumbnail cache
Create `Derp.Doc/Assets/AssetThumbnailCache.cs`.

Responsibilities:
1. Decode image files to RGBA.
2. Downscale to max 128x128 with aspect preserved.
3. Upload via `Derp.LoadTexture(ReadOnlySpan<byte>, width, height)`.
4. Lazy queue processing (`ProcessLoadQueue(maxPerFrame)`).

Important budget rule:
1. Because engine texture slots are append-only, use session cap instead of GPU LRU eviction.
2. Example constants: `MaxLoadedPreviewTexturesPerSession = 96`, `MaxLoadsPerFrame = 2`.
3. If cap reached, return placeholder state and stop loading additional unique thumbnails.

#### Step 2.3: Wire cache into rendering
Update `SpreadsheetRenderer`:
1. For `TextureAsset`, request thumbnail from cache.
2. If loaded, compute fit rect and add draw request to `ThumbnailDrawList`.
3. If loading, draw loading placeholder.
4. If missing path/file, draw warning text.
5. Keep filename text visible.

#### Step 2.4: Resolve assets root
Update `Derp.Doc/Editor/DocWorkspace.cs`:
1. Add read-only `AssetsRoot` property:
`GameRoot != null ? Path.Combine(GameRoot, "Assets") : null`.
2. Use this value for cache and browser.

### Phase 3: Asset Browser Modal

#### Step 3.1: Add scanner
Create `Derp.Doc/Assets/AssetScanner.cs`.

Responsibilities:
1. Enumerate recursively under assets root.
2. Return `AssetEntry` list with relative path and metadata.
3. Cache scan results with explicit refresh and timeout refresh.
4. Filter by extension set.

Supported extensions:
1. TextureAsset: `.png`, `.jpg`, `.jpeg`, `.bmp`, `.tga`.
2. MeshAsset: `.obj`, `.glb`, `.gltf`, `.fbx`, `.mesh`.

#### Step 3.2: Add modal UI
Create `Derp.Doc/Panels/AssetBrowserModal.cs`.

Responsibilities:
1. `Open(kind, currentValue)`.
2. Search input.
3. Virtualized thumbnail grid.
4. Single selection with highlight.
5. Confirm/Cancel.
6. Double-click or Enter confirms.

Use existing modal pattern (`ImModal.Begin/End`) like `SlashCommandMenu`.

#### Step 3.3: Wire spreadsheet edit behavior
Update `SpreadsheetRenderer` `BeginCellEdit`/edit overlay flow:
1. For `TextureAsset` and `MeshAsset`, open modal instead of inline text edit.
2. On confirm, execute `SetCell` command with selected path.
3. On cancel, keep previous value.
4. If no `AssetsRoot`, fall back to text editing.

### Phase 4: MeshAsset MVP (No GPU Mesh Preview Yet)

#### Step 4.1: Support MeshAsset path flow end-to-end
1. Add `MeshAsset` in all same model/export/MCP/UI paths as `TextureAsset`.
2. Browser filters to mesh extensions.
3. Cell rendering shows mesh placeholder icon + filename.

#### Step 4.2: Cached disk lookup for pre-generated preview images
Use `MeshPreviewGenerator` to resolve preview PNGs from:
`{GameRoot}/Database/.derpdoc-cache/mesh-previews/{hash}.png`.
If not present, keep placeholder.

### Phase 5: Engine Prerequisites for True Mesh Thumbnails (Future)

Required before implementing `MeshPreviewGenerator`:
1. RenderTexture color image transfer-source support.
2. RenderTexture readback API in engine.
3. TextureArray slot reuse/unload API or reusable preview texture pool.

Only after these exist:
1. Implement `Derp.Doc/Assets/MeshPreviewGenerator.cs`.
2. Render mesh to offscreen target.
3. Read back pixels and write PNG cache.
4. Load cached PNG through `AssetThumbnailCache`.

## Files To Create

1. `Derp.Doc/Assets/ThumbnailDrawList.cs`
2. `Derp.Doc/Assets/AssetThumbnailCache.cs`
3. `Derp.Doc/Assets/AssetScanner.cs`
4. `Derp.Doc/Assets/MeshPreviewGenerator.cs`
5. `Derp.Doc/Assets/DocAssetServices.cs`
6. `Derp.Doc/Panels/AssetBrowserModal.cs`

## Files To Modify

1. `Derp.Doc.Core/Model/DocColumnKind.cs`
2. `Derp.Doc.Core/Model/DocCellValue.cs`
3. `Derp.Doc.Core/Export/ExportColumnModel.cs`
4. `Derp.Doc.Core/Mcp/DerpDocMcpServer.cs`
5. `Derp.Doc.Core/Mcp/DerpDocMcpTools.cs`
6. `Derp.Doc/Panels/SpreadsheetRenderer.cs`
7. `Derp.Doc/Panels/InspectorPanel.cs` (kind icon mapping)
8. `Derp.Doc/Editor/DocEditorApp.cs`
9. `Derp.Doc/Editor/DocWorkspace.cs`
10. `Derp.Doc/Derp.Doc.csproj`

## Edge Cases

1. Standalone DB root with no game root: show text mode, disable browser.
2. Missing file: show warning style and keep stored path.
3. Texture decode failure: show `(invalid image)` placeholder.
4. Session preview cap reached: show `(preview budget reached)` placeholder.
5. Undo/redo: no special handling, standard `SetCell` command path.

## Verification Checklist

1. Add `TextureAsset` column from UI and MCP.
2. Add `MeshAsset` column from UI and MCP.
3. Save/reload project preserves both kinds and values.
4. Export emits both kinds as `StringHandle`.
5. Texture cells display thumbnails and remain responsive while loading.
6. Browser search/filter/select works for both asset kinds.
7. No crash when `GameRoot` is null.
8. App still renders correctly with SDF + overlay pass ordering.
9. Mesh cells show cached preview when available; otherwise placeholder text.

## Test Updates

Add tests in `Derp.Doc.Tests`:
1. `DocCellValue.Default` for new kinds returns empty string.
2. Project storage roundtrip for new column kinds.
3. MCP `column.add` accepts `TextureAsset` and `MeshAsset`.
4. Export pipeline maps new kinds to `StringHandle` without diagnostics.
