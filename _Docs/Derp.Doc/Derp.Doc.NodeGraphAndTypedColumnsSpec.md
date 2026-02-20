# Derp.Doc Node Graph + Native Vec/Color + Per-Cell Formula Spec

Status: Approved design  
Scope: Derp.Doc.Core + Derp.Doc editor (bundled node graph renderer, native column types, formula/runtime updates)

## 1) Goals

1. Add a bundled table view that edits a table as a node graph.
2. Add native vector/color column kinds (not plugin-only type aliases).
3. Add per-cell formula overrides for any column kind.
4. Keep deterministic save/load behavior with no numeric drift in fixed variants.
5. Keep formula recalculation incremental and dependency-graph driven.

---

## 2) Native Column Kinds and Type Variants

### 2.1 New built-in column kinds

- `Vec2`
- `Vec3`
- `Vec4`
- `Color`

These are first-class column kinds alongside existing built-ins.

### 2.2 Variant model (via `ColumnTypeId`)

Column kind defines the shape; `ColumnTypeId` defines storage/quantization variant.

Vector variants:
- `core.vec2.f32`
- `core.vec2.f64`
- `core.vec2.fixed32`
- `core.vec2.fixed64`
- `core.vec3.f32`
- `core.vec3.f64`
- `core.vec3.fixed32`
- `core.vec3.fixed64`
- `core.vec4.f32`
- `core.vec4.f64`
- `core.vec4.fixed32`
- `core.vec4.fixed64`

Color variants:
- `core.color.ldr` (non-HDR)
- `core.color.hdr` (HDR-capable)

### 2.3 UI editing model

Vectors:
- Use separate labeled fields (`X`, `Y`, `Z`, `W`) based on dimensionality.
- Fixed variants quantize on every write (manual edit and formula result write).

Colors:
- `LDR`: clamps/stores in normalized 0..1 linear channels.
- `HDR`: stores linear channels without LDR clamp.

---

## 3) Core Cell Value Representation

### 3.1 `DocCellValue` becomes typed for vec/color

`DocCellValue` remains a struct and gains typed payload for:
- Vec2 (4 scalar slots possible; vec2 uses X/Y)
- Vec3 (X/Y/Z)
- Vec4 (X/Y/Z/W)
- Color (R/G/B/A)

Existing scalar fields remain for compatibility:
- Text / Number / Bool / model-preview metadata.

### 3.2 Per-cell formula override on cell value

Each cell may carry:
- `FormulaExpression` (string, nullable)
- `FormulaError` (string, nullable, as today semantics for formula result errors)

This makes per-cell formulas a property of the specific cell payload.

### 3.3 Serialization shape

For scalar-like cells with no per-cell formula: keep existing compact shape.

For cells with per-cell formula override:
- Store as object containing both formula and current value snapshot:

```json
{
  "f": "col(\"A\") + 5",
  "v": { ... typed payload ... }
}
```

Rationale:
- Stable and explicit.
- Avoids ambiguity when a plain string cell value might be mistaken for formula text.
- Supports fast load with last-known value snapshot + re-eval on dependency invalidation.

---

## 4) Formula Engine Extensions

### 4.1 New formula value kinds

Add first-class formula runtime types:
- `Vec2`
- `Vec3`
- `Vec4`
- `Color`

No string fallback for vector/color math in v1.

### 4.2 Constructors and accessors

Introduce formula constructors and member access:

- `vec2(x, y)`
- `vec3(x, y, z)`
- `vec4(x, y, z, w)`
- `color(r, g, b, a)` (linear channels)

Member access:
- `value.x`, `value.y`, `value.z`, `value.w`
- `value.r`, `value.g`, `value.b`, `value.a`

### 4.3 Operators

Support at minimum:
- vec +/- vec
- vec * scalar
- scalar * vec
- vec / scalar
- color +/- color
- color * scalar

Dimension-mismatched operations are formula errors.

### 4.4 Conversion to/from cell by column kind

When assigning formula result to cell:
- Conversion depends on target column kind + `ColumnTypeId`.
- Fixed variants quantize on write.
- Color LDR variants clamp on write.
- Color HDR variants do not clamp to LDR range.

---

## 5) Per-Cell Formula Override Semantics

### 5.1 Override precedence

For a given cell:

1. If `cell.FormulaExpression` is present and non-empty, evaluate it.
2. Else if column-level formula exists, evaluate column formula.
3. Else use literal cell value.

### 5.2 Applicability

Per-cell formula override is allowed on **any** column kind.

### 5.3 Dependency graph integration (option A)

Use table-level dependency graph nodes as the primary graph.  
Track formula-bearing cells in side lists per table/column to avoid full-table scans.

Update flow:

1. Dependency invalidation identifies affected formula columns/tables.
2. For each affected formula-bearing column:
   - evaluate column formula rows (existing behavior)
   - then evaluate per-cell override rows for that column
3. Apply results via existing command/evaluation pathways.

This keeps graph scale manageable while still incremental.

### 5.4 UI command surface

Spreadsheet:
- Right-click cell menu:
  - `Set Cell Formula...`
  - `Clear Cell Formula`

Node graph:
- Inspector for selected node should expose per-cell formula for relevant fields.

---

## 6) Node Graph Renderer (Bundled Plugin)

### 6.1 Renderer identity

- Bundled custom table view renderer registered in plugin host.
- Visible in view picker as a custom renderer option.

### 6.2 Node table schema (auto scaffold)

On scaffold, create required columns if missing:
- `Type` (`Select`) - node type discriminator.
- `Pos` (`Vec2`, default fixed64 variant for deterministic positioning).

Position is explicitly 2D in v1.  
`Vec3`/`Vec4` remain native column kinds for authored data and future workflows, but are not used for node canvas coordinates.

Optional helper columns (scaffold if missing):
- `Title` (`Text`) for display label.

### 6.3 Edge storage

Edges live in a subtable column on the node table (or dedicated edge subtable created by scaffold).

Each edge row stores stable endpoints:
- `FromNode` (`Relation` -> node table row id)
- `FromPinId` (`Text`) stable pin id
- `ToNode` (`Relation` -> node table row id)
- `ToPinId` (`Text`) stable pin id

Row IDs remain stable and are the canonical relation anchor.

### 6.4 Type-specific field/pin presentation

`Type` controls which columns appear as:
- settings fields
- input pins
- output pins

Design contract:
- Maintain per-type UI layout config in renderer settings (view/plugin settings blob).
- Each type config lists columns and display modes.
- Each setting field may optionally include a widget hint to drive inspector/editor behavior per type
  (examples: default input, multiline text area, number slider).

Example per-type conceptual config:
- `MathAdd`: inputs `A`,`B` (number), output `Result`.
- `Comment`: settings `TextArea`, no data pins.
- `ColorLerp`: inputs `A`,`B`,`T`, output `OutColor`.

ASCII shape of the per-type layout map:

```text
TypeLayouts
┌─────────────────────────────────────────────────────────────┐
│ "MathAdd"                                                   │
│   A       -> InputPin                                       │
│   B       -> InputPin                                       │
│   Result  -> OutputPin                                      │
│                                                             │
│ "Comment"                                                   │
│   Body    -> Setting(widget=TextArea)                      │
│                                                             │
│ "Noise"                                                     │
│   Seed    -> Setting(widget=Slider, min=0, max=9999)       │
│   Scale   -> InputPin                                       │
│   Out     -> OutputPin                                      │
└─────────────────────────────────────────────────────────────┘
```

### 6.5 Selection and inspector

Selecting a node in graph maps to table row selection:
- node click sets `SelectedRowIndex` (and row id) in workspace.
- inspector continues to use existing row inspector pipeline.

Graph view adds any type-aware grouping/visualization but does not bypass core row data model.

---

## 7) Dataflow and Recalc Diagram

```text
                 ┌───────────────────────────────┐
                 │  User edits input cell/value  │
                 └──────────────┬────────────────┘
                                │
                                v
                 ┌───────────────────────────────┐
                 │ Invalidate dependency sources │
                 └──────────────┬────────────────┘
                                │
                                v
          ┌────────────────────────────────────────────────┐
          │ Re-eval affected formula-bearing columns only  │
          │ 1) column formulas                             │
          │ 2) per-cell override formulas                  │
          └──────────────┬─────────────────────────────────┘
                         │
                         v
          ┌────────────────────────────────────────────────┐
          │ Convert formula result -> cell by column kind  │
          │ - vec/color typed conversion                   │
          │ - fixed quantize on write                      │
          │ - color LDR clamp on write                     │
          └──────────────┬─────────────────────────────────┘
                         │
                         v
                 ┌───────────────────────────────┐
                 │ Persist + redraw inspectors   │
                 └───────────────────────────────┘
```

---

## 8) Node Graph Data Model Diagram

```text
Node Table (rows = nodes)
┌────────┬────────────┬──────────────┬──────────────┐
│ RowId  │ Type       │ Pos (Vec2)   │ ...fields... │
├────────┼────────────┼──────────────┼──────────────┤
│ n_1001 │ MathAdd    │ (120, 220)   │ A,B,Result   │
│ n_1002 │ ConstFloat │ ( 40, 220)   │ Value        │
└────────┴────────────┴──────────────┴──────────────┘
            │
            │ subtable relation per parent row or shared edge table
            v
Edge Table (rows = edges)
┌────────┬──────────┬───────────┬────────┬─────────┐
│ RowId  │ FromNode │ FromPinId │ ToNode │ ToPinId │
├────────┼──────────┼───────────┼────────┼─────────┤
│ e_2001 │ n_1002   │ out       │ n_1001 │ A       │
└────────┴──────────┴───────────┴────────┴─────────┘
```

---

## 9) Save/Load Stability Guarantees

1. Fixed variants quantize on write and serialize quantized values.
2. Load uses same quantized representation; no additional drift.
3. Re-save without edits is byte-stable for numeric payloads (modulo normal JSON formatting policy).

---

## 10) Export and Runtime Notes

1. New kinds are native in editor/model and available for formula math.
2. Export may opt-in/out per column through existing export controls.
3. Hiding a column from export does not remove editor/runtime formula support inside Derp.Doc.

---

## 11) Implementation Checklist

1. `DocColumnKind` + `DocColumnTypeIds` + mapper/registry updates for vec/color variants.
2. `DocCellValue` typed payload + per-cell formula fields + clone/equality helpers.
3. Storage serializers/codecs updated for new typed payload and `{f,v}` cell formula envelope.
4. `FormulaValueKind`/`FormulaValue`/engine/parser/function registry for vec/color math.
5. Spreadsheet UI:
   - new editors/renderers for vec/color columns
   - context menu actions for per-cell formula override
   - formula inspector reflects cell override status
6. Dependency engine changes to reevaluate per-cell formula overrides incrementally.
7. Bundled `NodeGraphTableViewRenderer` + plugin registration.
8. Node graph scaffold action (required columns + edge subtable schema).
9. Node selection wiring to row selection + inspector reuse.
10. Tests covering:
   - fixed quantize write/load stability
   - vec/color formula operations
   - per-cell formula precedence
   - node graph scaffold invariants
