# Derp.UI architecture (WIP)

## Goals
- Build a Figma-like editor on top of `Engine` + `Engine.ImGui` (multi-viewport enabled).
- Keep the per-frame render loop allocation-free.

## Coordinate spaces
- **Viewport-local**: `Im.Context.Input.MousePos` / `Im.MousePosViewport` (0,0 = viewport top-left).
- **Window-local**: `Im.MousePos` (current transform removed), `Im.WindowContentRect` (window content bounds).
- **Canvas-world**: persistent editor space for documents (prefabs, shapes, etc).

## Canvas camera
The Canvas window owns a camera:
- `panWorld`: the world position mapped to the canvas origin.
- `zoom`: world-to-canvas scale factor.
- `canvasOrigin`: computed each frame from `Im.WindowContentRect` (typically center).

Conversions:
- `world -> canvas`: `(world - panWorld) * zoom + canvasOrigin`
- `canvas -> world`: `(canvas - canvasOrigin) / zoom + panWorld`

We intentionally keep `Im`'s transform stack translation-only; the canvas uses explicit math (and can use `Matrix3x2`)
without changing core widget/input/clip behavior.

## Tools
- `Select`: select and drag prefabs.
- `Prefab`: click-drag on the canvas to create a new Prefab.
- `Rect` / `Circle`: click-drag to create primitive shapes (stored in world space, owned by a Prefab).

## Data model (initial)
- `Prefab`: `id` + `rectWorld` (world-space).
- `Shape`: `id` + `kind` + `rectWorld` + `parentFrameId` (+ later: `parentGroupId` for hierarchy).
- `BooleanGroup`: a node that owns child shapes and combines them via `SdfBooleanOp` (Union/Subtract/Intersect/Exclude).
- `UiWorkspace`: owns tool state, camera state, and all editor nodes.

## Hierarchy (current)
- Prefabs own a hierarchy of **nodes**:
  - `Shape` nodes (`Rect`, `Circle`)
  - `BooleanGroup` nodes (can contain `Shape` and `BooleanGroup` children to allow stacking)
- Parentage is stored as `parentFrameId` + `parentGroupId` (0 = direct child of the frame).
- Boolean groups store an ordered child list as packed node refs: `(id << 1) | typeBit` (`typeBit: 0=shape, 1=group`).
- Prefabs also store an ordered child list. Rendering + hit testing follow this order (last = topmost).

## Boolean ops (Figma-like)
- The boolean operation is a property of the `BooleanGroup`, not individual shapes.
- A group can be used as an operand of another group by selecting multiple nodes and wrapping them into a new `BooleanGroup`.
