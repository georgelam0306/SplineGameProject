# Derp.Doc Game Project Layout and Discovery Contract

This document defines how Derp.Doc associates an editor database with a game project, and where export outputs go.

## Goals

- Derp.Doc opens a *game*, not a loose folder of JSON files.
- A game has a stable root directory that future systems can reference (asset columns, previews, export, build integration).
- `Assets/` is hand-authored source. `Resources/` is compiled/runtime-ready output.

## Game Root Marker

A directory is treated as a Derp game root if it contains a file named:

```text
derpgame
```

The marker file is intentionally content-free. Its presence alone defines the root.

## Directory Layout (Canonical)

```text
Games/<GameName>/
  derpgame
  Assets/                       (hand-authored source)
  Database/                     (Derp.Doc editable source-of-truth)
    project.json
    tables/
      *.schema.json
      *.rows.jsonl
      *.own-data.jsonl          (derived tables: local-only cells)
    docs/
      *.meta.json
      *.blocks.jsonl
  Resources/                    (compiled/runtime-ready outputs)
    Database/
      <GameName>.derpdoc         (compiled database container)
```

Notes:

- `Database/` is the Derp.Doc DB root for the editor.
- `Resources/Database/<GameName>.derpdoc` is the export output for runtime consumption.
- `Resources/` should be safe to delete and regenerate.

## Open Project (Discovery Rules)

Derp.Doc must support two opening modes.

### Mode A: Game Root (preferred)

- User selects any folder or file inside a game directory tree.
- Derp.Doc walks upward from the selected path to find the nearest `derpgame`.
- The containing directory is the `GameRoot`.
- `DbRoot = GameRoot/Database`
- `ResourcesRoot = GameRoot/Resources`

If `DbRoot` does not exist, Derp.Doc can create it on first save or immediately on open.

### Mode B: Standalone DB Root (tools/testing)

- If no `derpgame` marker is found, Derp.Doc may open a standalone database folder directly.
- A standalone DB root is a folder containing `project.json` plus `tables/` (and optionally `docs/`).

Mode B is optional long-term; Mode A is the intended workflow.

## Export Contract

Export is a deterministic compilation step:

```text
DbRoot (JSON/JSONL) -> compute derived + formulas -> write compiled database container
```

Outputs:

```text
Resources/Database/<GameName>.derpdoc
```

Constraints:

- Export must be deterministic given identical inputs.
- Derived rows are not persisted in the editor DB; derived rows are materialized for export.
- Formula results are baked into the export output; runtime does not evaluate formulas.

## Assets vs Resources

- `Assets/` contains hand-authored sources (png/gltf/json authoring).
- `Resources/` contains compiled outputs (baked DB, compiled textures/meshes, etc).

This split is a repository convention and a build contract. Runtime should read from compiled outputs.

