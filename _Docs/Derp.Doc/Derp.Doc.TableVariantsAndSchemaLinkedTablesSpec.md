# Derp.Doc Table Variants + Schema-Linked Tables Spec (v1)

Status: Draft (implementation target).

This document defines two related editor/runtime features:

- Table variants: project-global variant selection with per-table overlay/delta storage (variants can add/remove rows).
- Schema-linked tables: tables that reuse another table's schema (columns) but own their own row data.

Goals:

- Variants are available in-engine (exported, engine-readable) with no runtime formula/join execution.
- Default behavior is deterministic and explicit: `VariantId = 0` (base).
- Overlay/delta model: variants store only changes relative to base.
- No runtime "try X else Y" fallbacks; behaviors are defined semantics.

Non-goals:

- Runtime authoring/editing of variants.
- Runtime evaluation of derived tables, joins, or formulas.

## 1. Terminology

- Base: `VariantId = 0`.
- Variant: `VariantId > 0`, project-global.
- Variant delta: a per-table patch applied on top of base for a specific `VariantId`.
- Materialized table@variant: the concrete row set + baked formula values for `(TableId, VariantId)`.
- Schema source: the table that owns the schema (columns).
- Schema-linked table: a table whose columns are always synced from a schema source table and cannot be edited locally.

## 2. Data Model (Editor Source-of-Truth)

### 2.1 Project variants (global)

`DocProject` defines the global set of variants:

- `Variants[]` where each entry has:
  - `Id` (int; `0` is reserved for base and is always present)
  - `Name` (string; for UI/debug)

Constraints:

- `Id` values are dense enough for efficient runtime switching, but do not need to be contiguous.
- `Name` is unique case-insensitively.

### 2.1.1 On-disk storage (project.json)

`project.json` stores the variant list:

- `variants[]`: `{ id, name }`

`id = 0` is reserved and always present.

### 2.2 Table variant delta (overlay/delta)

Each `DocTable` may define a delta per `VariantId`:

- `TableVariantDelta` (per `VariantId`):
  - `DeletedBaseRowIds[]` (tombstones)
  - `AddedRows[]` (full row records with stable `RowId`)
  - `CellOverrides[]` (sparse overrides; `(RowId, ColumnId) -> DocCellValue`)

Notes:

- A table is allowed to have no delta for a variant. In that case the materialized table@variant is identical to base for that table. This is not a fallback path; it is the defined identity delta.
- `AddedRows[]` order is meaningful and defines deterministic "append order" for new rows in that variant.
- `RowId` is globally unique within a table across base and all variants.

Constraints:

- `DeletedBaseRowIds[]` may only refer to base rows (rows that exist in base).
- `CellOverrides[]` may refer to:
  - base rows that are not tombstoned in that variant, or
  - added rows in that variant.
- Overrides for a tombstoned row are ignored and should be surfaced as editor diagnostics.

### 2.2.1 On-disk storage (tables/*.variants.jsonl)

Each non-derived table persists its variant deltas in:

- `tables/{fileName}.variants.jsonl`

Format:

- One JSON object per line.
- Each line carries `variantId` and one operation.

Operation kinds (v1):

- `row_add`: adds a full row record (includes stable `rowId` + sparse `cells`).
- `row_delete`: tombstones a base row id.
- `cell_set`: overrides a single cell (`rowId`, `columnId`, value payload).

Determinism:

- The order of `row_add` operations defines deterministic append order for that variant.

### 2.3 Schema-linked tables

Each `DocTable` may be schema-linked:

- `SchemaSourceTableId` (string; non-null means this table is schema-linked)

Semantics:

- Columns are always the schema source's columns (same IDs, names, kinds, options, plugin settings).
- The schema-linked table may not add/remove/reorder columns locally.
- The schema source is the single point of truth for schema changes.

On schema source changes (always auto-update):

- Added columns appear immediately in schema-linked tables with default cell values.
- Removed columns are removed from schema-linked tables and any orphaned per-row cell data for those columns is dropped.
- Renames/type changes are reflected immediately (column IDs stay stable; behavior derives from the schema source column).

### 2.3.1 On-disk storage (schema-linked)

Schema-linked tables still write:

- `tables/{fileName}.schema.json` (table metadata + `schemaSourceTableId`)
- `tables/{fileName}.rows.jsonl` (base rows)
- `tables/{fileName}.variants.jsonl` (optional; variant delta ops)

They do not persist local columns because local columns do not exist for schema-linked tables.

## 3. Materialization Semantics

Materialization is a pure function:

`Materialize(Table, VariantId) -> ConcreteRowSet`

Inputs:

- Base rows (`Table.Rows`).
- Optional variant delta for `VariantId`.

Output row order (deterministic):

1. All base rows in base order, excluding tombstoned row IDs for that variant.
2. All `AddedRows[]` in delta order.

Cell resolution:

- If a cell override exists for `(RowId, ColumnId)` in the variant delta, it is used.
- Else, if the row is a base row, the base cell value is used.
- Else, the row is an added row and uses its stored cell value (or column default if missing).

## 4. Formula + Derived Tables (Variant-Aware Export-Time Compute)

### 4.1 Variant evaluation context

All export-time compute runs with an explicit `VariantId`:

- Formulas read from `table@variant` (materialized view).
- Cross-table references resolve to the referenced table's `table@sameVariantId`.
- Derived tables materialize from their sources' `table@sameVariantId`.

Derived local (non-projected) columns:

- Derived tables may include local (non-projected) columns that are user-edited.
- Local derived cell data is variant-aware: local cells are stored and restored within the active `VariantId` context.

### 4.2 Dependency graphs (review)

Existing dependency planning is table-level (one node per `TableId`).

That remains correct because `VariantId` is project-global and cross-table reads always use the same `VariantId`.

Execution becomes 2D:

1. Build table-level dependency plan once (toposorted table nodes).
2. For each `VariantId` in project order:
   - For each table node in the table-level topo order:
     - materialize derived tables in the current `VariantId` context
     - bake formula columns for the current `VariantId` context

Required additional structural edges:

- `SchemaSource -> SchemaLinked` is a structural dependency (schema sync must occur before compilation/materialization/baking).

Cycle rules:

- Cycles in derived dependencies are hard errors (existing).
- Formula cycles are hard errors (existing).
- Schema-linked cycles (A links to B links to A) are hard errors.

### 4.3 On-disk storage (derived table local data)

Derived tables do not persist `rows.jsonl` (rows are computed), but they persist local cell data per variant:

- Base: `tables/{fileName}.own-data.jsonl` (existing behavior)
- Variant: `tables/{fileName}.own-data@v{variantId}.jsonl` for `variantId > 0`

Rows are keyed by derived `rowId` (OutRowKey). This enables deterministic restoration of local edits across
re-materialization for a given variant.

## 5. Export Contract (Engine-Readable Variants)

Export must emit a fully materialized dataset per `(TableId, VariantId)`:

- Row sets are materialized according to Section 3.
- Derived tables are materialized according to Section 4.
- Formula columns are baked into exported row records (no runtime evaluation).

### 5.1 Binary naming convention

Binary table sections use deterministic names:

- Base: `BinaryTableName` (existing behavior)
- Variant: `BinaryTableName + \"@v\" + VariantId`

Indexes follow the same suffixing rule.

Example:

- `Weapons` (base)
- `Weapons@v1` (variant 1)
- `Weapons__pk_sorted` (base)
- `Weapons@v1__pk_sorted` (variant 1)

### 5.2 Storage deduplication (optional optimization)

If a table has no delta for a `VariantId`, export may alias `BinaryTableName@vX` to the same underlying record blobs as base.

This optimization must preserve determinism and must not affect observable row ordering or key/index behavior.

### 5.3 Manifest additions (debug/UI)

The `*.manifest.json` output includes:

- `Variants[]`: `{ Id, Name }` (including base id 0)
- Per table entry includes either:
  - a list of variant table names and row counts, or
  - multiple entries keyed by `(BaseTableName, VariantId)`

Exact JSON shape is an implementation detail, but it must be stable and sufficient for:

- editor/debug UI to show available variants
- engine debug UI to display the current variant name

## 6. Runtime API Shape (Variant Selection)

Variant selection is explicit and defaults to base.

Recommended API (allocation-free on hot paths):

- `GameDataDb.WithVariant(int variantId)` returns a lightweight view wrapper that resolves table accessors for that variant.
- The returned view uses base (`0`) if `variantId == 0`.

Validation:

- If `variantId` is not present in the exported `Variants[]` list, fail fast (throw/assert) at variant selection time.

Per-table delta presence:

- If a table has no delta for the chosen valid `variantId`, it is identical to base by definition.

## 7. Diagnostics (Editor + Export)

Editor-time diagnostics:

- Invalid `SchemaSourceTableId` (missing source table).
- Schema-linked cycle path.
- Variant delta refers to missing base row ID.
- Duplicate row IDs across base and variant added rows.
- Overrides targeting tombstoned rows (warn/error; spec recommends warning).

Export-time hard errors:

- Any derived MultiMatch (existing Phase 5 rule).
- Any derived type mismatch (existing).
- Any formula error cell (`#ERR`) (existing).
- Any invalid variant list (`Id` collision, missing base id 0, name collision).

## 8. Determinism Requirements

Export determinism must hold per variant:

- Stable table evaluation order (topological, stable tie-breaking).
- Stable row order as specified in Section 3.
- Stable column ordering (existing).
- Stable binary outputs for identical inputs, including variant metadata.
