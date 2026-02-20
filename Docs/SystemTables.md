# Derp.Doc System Tables Spec (V1)

This document specifies "System Tables" in Derp.Doc: plugin-defined tables that always exist in every project and are structurally immutable. System Tables provide a stable asset registry and an addressable/package export model.

## Goals

- Every Derp.Doc project contains a fixed set of System Tables.
- System Tables are *structurally immutable*:
  - cannot be deleted
  - cannot be renamed
  - cannot add/remove/reorder/rename columns
  - cannot change derived/export/keys/schema-link configuration
- System Tables appear in a *virtual* `Systems/` grouping in the UI (not a persisted folder).
- `Systems/assets` is fully locked:
  - rows/cells are not user-editable
  - values are owned by the system (filesystem scan + compiler metadata)
- Assets get stable integer IDs (`int`) and deterministic, path-namespaced names.
- Derived tables support filtering as part of the derived pipeline, enabling tables like `textures/models/audio/materials` to be derived subsets of `assets`.
- Packaging/addressables support:
  - an asset can be present in multiple packages
  - exported roots pull in transitive dependencies (dependency closure)

## Non-Goals (V1)

- Solving all dependency discovery for all formats on day one.
- Perfect rename/move detection without a content hash.
- Runtime fallbacks for old project versions (migration is explicit).

## Terms

- **System Table**: A table whose schema is defined by a plugin/bundled system and enforced on load/export.
- **Schema-locked**: no schema mutations allowed (table name, columns, config).
- **Data-locked**: no row/cell mutations allowed (add/remove rows, set cell).
- **Reconcile**: make on-disk project match the system-defined desired state.

## Overview

### Conceptual architecture

```
------------------+        +-------------------+
 Assets/ folder    |        | Plugins (bundled) |
 (filesystem)      |        | define System     |
------------------+        | Tables + reconcile |
            |              +-------------------+
            | scan/reconcile         |
            v                       v
------------------------------------------------+
 Derp.Doc project (on disk + in memory)         |
 - Systems/assets (registry)                    |
 - Systems/packages                             |
 - Systems/exports (package membership)         |
 - Systems/textures/models/audio/materials      |
 - Systems/asset_deps (dependency graph)        |
------------------------------------------------+
            |
            | export pipeline reads Systems tables
            v
-----------------------------+
 Generated outputs           |
 - C# addressables code      |
 - package manifests         |
 - per-platform outputs      |
-----------------------------+
```

### Why System Tables

Derp.Doc currently stores asset references as strings (relative paths). Paths are brittle. System Tables introduce a stable integer identity:

```
Before (brittle):
  Texture = "Env/Tree.png"

After (stable):
  TextureAssetId = 123   (AssetId in Systems/assets)
```

## System Tables (V1 set)

All of these tables are schema-locked. Some are also data-locked.

### `Systems/assets` (data-locked)

Purpose: canonical registry for files under `Assets/`.

Properties:
- One row per discovered asset file (plus tombstones; see below).
- `AssetId` is stable and unique.
- `Name` is deterministic and unique, derived from relative path.
- Cells are system-owned and not user-editable.

Suggested columns (schema-locked):
- `AssetId` (Number, exported as `int`) [primary key]
- `Name` (Text) [unique, case-insensitive]
- `RelativePath` (Text) [normalized with `/`]
- `Kind` (Select: `Texture|Model|Audio|Material|Other`)
- `Extension` (Text)
- `SizeBytes` (Number, exported as `int` or `Fixed64` depending on engine needs)
- `ContentHash` (Text) [recommended; enables rename/move detection]
- `Missing` (Checkbox) [tombstone marker]
- `LastSeenUtc` (Text or Number) [optional]

Name derivation (deterministic):
```
RelativePath: "Env/Trees/Oak.png"
Name:         "Env_Trees_Oak_png"
```
Rules:
- replace `/` and `.` with `_`
- sanitize to C# identifier-safe characters (`[A-Za-z0-9_]`)
- preserve path segments to namespace names by path
- treat uniqueness as case-insensitive (Windows-safe)

### Tombstones (missing assets)

Keep rows for missing assets instead of deleting them.

Why:
- preserves `AssetId` stability across renames/moves
- preserves export/package configuration that references the asset

Behavior:
- When a file is not found, mark `Missing=true`.
- When a file reappears, reattach and mark `Missing=false`.
- Prefer reattachment by `ContentHash` if available; otherwise fall back to `RelativePath`.

### `Systems/packages` (editable rows)

Purpose: define packages and per-package metadata (delivery and output).

Rows are user-editable; schema is locked.

Suggested columns:
- `PackageId` (Number, exported as `int`) [primary key]
- `Name` (Text) [unique, case-insensitive]
- `DefaultLoadFrom` (Select: `Disk|CDN`) [optional]
- `CdnBaseUri` (Text) [optional]
- `OutputPath` (Text) [optional]

Variants:
- `Systems/packages` may use variants to override package metadata per platform.
- Example: `DefaultLoadFrom=Disk` on consoles, `CDN` on PC.

### `Systems/exports` (editable rows, many-to-many membership)

Purpose: declare *package membership* and per-export addressable metadata.

Each row represents: "include AssetId in PackageId" (many-to-many).

Suggested columns:
- `PackageId` (Relation -> `Systems/packages` or Number) [required]
- `AssetId` (Relation -> `Systems/assets` or Number) [required]
- `Enabled` (Checkbox) [default true]
- `Address` (Text) [optional; default can be derived from `Systems/assets.Name`]
- `LoadFromOverride` (Select: `Default|Disk|CDN`) [optional]

Recommended uniqueness constraint (enforced by system, not user schema):
- unique `(PackageId, AssetId)` in Base variant

Variants:
- `Systems/exports` may use variants to override `Enabled`, `Address`, or delivery per platform.
- Examples:
  - Disable large textures on Switch
  - Different address root per platform

### Typed asset tables (data-locked, derived)

Purpose: convenience views with type-specific metadata.

These are derived from `Systems/assets` and are schema-locked + data-locked:
- `Systems/textures`
- `Systems/models`
- `Systems/audio`
- `Systems/materials` (optional)

Key requirement: derived tables must support filtering.

Example derived definition conceptually:
```
textures = assets WHERE assets.Kind == "Texture"
models   = assets WHERE assets.Kind == "Model"
audio    = assets WHERE assets.Kind == "Audio"
```

### `Systems/asset_deps` (data-locked)

Purpose: store the asset dependency graph for packaging/export.

Rows are system-owned.

Suggested columns:
- `AssetId` (Number/Relation -> `Systems/assets`)
- `DependsOnAssetId` (Number/Relation -> `Systems/assets`)
- `Reason` (Text) [optional; importer/provider-specific]

Dependency discovery is plugin-provided and can be incremental. V1 can start with partial coverage and improve over time, but packaging must never silently omit known dependencies.

## Derived Filtering (required feature)

Derived tables currently support Append/Join. V1 adds a Filter step (or equivalent config) to remove rows based on an expression.

Conceptual pipeline:
```
Append/Join materialization -> Working rows -> Filter -> Output rows
```

Filter expression semantics:
- Evaluated per working row.
- References projected columns in the derived output row (e.g. `thisRow.Kind`).
- Deterministic; no runtime fallbacks.

Minimal filter needs for typed tables:
- equality comparison on Select/Text (`Kind == "Texture"`)
- boolean operators (`&&`, `||`, `!`) as needed

## Packaging + Dependencies

### Package roots

For a given package `P` and platform variant `V`:
- Roots are `Systems/exports` rows where:
  - `PackageId == P`
  - `Enabled == true` in the effective variant (Base + V overlay)

### Dependency closure

For each root `AssetId`, include transitive dependencies from `Systems/asset_deps`:

```
Included(P) = Roots(P) U Deps(Roots(P)) U Deps(Deps(Roots(P))) ...
```

V1 policy: **DuplicateDependencies**
- Each package is self-contained: if a root depends on `X`, `X` is included in the same package output, even if `X` is also present in other packages.
- This avoids load-order and "shared core" complexity in V1.

### Diagnostics

Export should surface:
- missing assets referenced by exports rows (`assets.Missing == true`)
- missing dependency edges (if dependency provider declares an unresolved reference)
- cycles in dependency graph (handled deterministically; warn)

## UI/Editor Requirements

### Virtual Systems grouping

The sidebar should render System Tables under a dedicated `Systems` section that is not persisted as a folder.

Example:
```
Systems
  assets
  packages
  exports
  textures
  models
  audio
  asset_deps

Tables
  (user tables and folders)
```

### Disallowed actions

For all System Tables:
- hide/disable table context menu items: Rename, Delete, Duplicate, Schema-link, Move to folder
- hide/disable schema mutation actions: add/remove/rename/reorder columns, set keys, set export config, set derived config

For `Systems/assets`, typed derived tables, and `Systems/asset_deps`:
- hide/disable data mutation actions: add/remove rows, set cells

For `Systems/packages` and `Systems/exports`:
- allow row edits (add/remove rows and edit allowed cells)
- schema remains locked

### Asset-centric package editing (optional but recommended)

Provide an asset-centric UI lens that edits `Systems/exports` membership without allowing edits to `Systems/assets`.

Example "matrix" view (conceptual):
```
Assets              base    dlc1    sfx
Env/Tree.png        [x]     [x]     [ ]
SFX/Hit.wav         [ ]     [ ]     [x]
```

This is implemented as a custom renderer/view, not as a user-editable table.

## Enforcement Surface Area

System Table constraints must be enforced in both mutation paths:

1. Editor command pipeline (undo/redo):
   - table rename/delete commands
   - column add/remove/rename/move commands
   - cell/row edits for data-locked tables

2. MCP server:
   - `table.create/delete/folder_set/schema_link_set/schema_get/keys_set/export_set/derived_set`
   - `column.add/update/delete`
   - `row.add/update/delete`

If either path misses a guard, System Tables become mutable through that interface.

## Reconcile Triggers

Reconcile should run at least:
- on project scaffold (new project)
- on project load/open (editor and MCP)
- optionally on export (to guarantee required system state)

Reconcile responsibilities:
- ensure System Tables exist (on disk and in memory)
- ensure schema matches exactly (add missing columns, remove unknown columns, fix names)
- enforce locks
- update `Systems/assets` and other generated tables based on current `Assets/`

## Storage/Migration Notes

Projects persist tables via:
- `project.json` (table refs)
- `tables/*.schema.json` (schema)
- `tables/*.rows.jsonl` (rows)

System Table metadata must survive save/load:
- The project must remember which tables are System Tables.
- The system must be able to re-identify them reliably on load (not by display name).

Suggested identity strategy:
- each System Table has a stable internal key (e.g. `"system.assets"`, `"system.exports"`)
- reconcile uses the key to find or create the correct table

## Open Questions

- Should `Systems/exports` store `PackageId` and `AssetId` as numeric columns or Relations?
- Should platform variants be fixed (system-defined) or user-extendable for System Tables?
- Should `Address` be required, or can it be derived (and stored) from `assets.Name`?
- How should `Missing` assets behave in export:
  - hard error
  - warning + omit
```
