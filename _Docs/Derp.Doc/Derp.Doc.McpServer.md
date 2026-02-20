# Derp.Doc MCP Server

Derp.Doc exposes a headless MCP server over **stdio** so tools/agents can create and modify Derp.Doc databases on disk (the same `project.json` + `tables/*.schema.json` + `tables/*.rows.jsonl` files the UI uses).

## Client setup

### Claude Code CLI

Project-scoped config is checked into this repo as `.mcp.json`, so you can enable it with:

```bash
cd /Users/georgelam/wkspaces/derp-doc
claude mcp add derpdoc --scope project
claude mcp list
```

During a conversation, use `/mcp` to see server status and available tools.

### Codex CLI

Register the server once:

```bash
codex mcp add derpdoc -- dotnet run --project /Users/georgelam/wkspaces/derp-doc/Tools/DerpDoc.Cli -- mcp --workspace /Users/georgelam/wkspaces/derp-doc
codex mcp list
```

## Run

```bash
derpdoc mcp
```

Optional:

```bash
derpdoc mcp --workspace /path/to/repo
```

## Protocol

- Transport: stdio (newline-delimited JSON-RPC 2.0 messages).
- Requires `initialize`, then `notifications/initialized`, then tool calls.

## Tools

### Project

| Tool | Description |
|------|-------------|
| `derpdoc.project.create` | Create a DB root directory |
| `derpdoc.project.open` | Open a game root (`derpgame`) or DB root (`project.json`) |
| `derpdoc.project.get` | Get active project metadata (includes variants) |
| `derpdoc.variant.list` | List project variants (`id=0` is Base) |
| `derpdoc.variant.set` | Create or rename a variant (`variantId > 0`) |
| `derpdoc.variant.delete` | Delete a variant and remove its deltas (`id=0` is not deletable) |

### Folders

| Tool | Description |
|------|-------------|
| `derpdoc.folder.list` | List folders (optionally by scope: Tables/Documents) |
| `derpdoc.folder.create` | Create a folder in Tables or Documents scope |

### Tables

| Tool | Description |
|------|-------------|
| `derpdoc.table.list` | List all tables (includes `isSubtable`, `parentTableId` fields) |
| `derpdoc.table.create` | Create a new table |
| `derpdoc.table.delete` | Delete a table |
| `derpdoc.table.folder.set` | Move a table into a folder (or clear folder) |
| `derpdoc.table.schema.get` | Get table schema, columns, keys, export config, and schema-link metadata |
| `derpdoc.table.schema.link.set` | Set or clear `schemaSourceTableId` for schema-linked tables |
| `derpdoc.table.query` | Query rows from a table (`variantId` optional, defaults to base) |
| `derpdoc.table.query.batch` | Query multiple tables in one call (`variantId` per query) |

### Columns

| Tool | Description |
|------|-------------|
| `derpdoc.column.add` | Add a column (`Text`, `Number`, `Checkbox`, `Select`, `Formula`, `Relation`, `Subtable`) |
| `derpdoc.column.update` | Update column properties (name, options, formula, export settings) |
| `derpdoc.column.delete` | Delete a column (cascade-deletes child table for Subtable columns) |

### Rows

| Tool | Description |
|------|-------------|
| `derpdoc.row.add` | Add a row (`variantId` optional, defaults to base) |
| `derpdoc.row.add.batch` | Add multiple rows in one call (`variantId` optional) |
| `derpdoc.row.update` | Update cells in a row (`variantId` optional) |
| `derpdoc.row.update.batch` | Update cells in multiple rows (`variantId` optional) |
| `derpdoc.row.delete` | Delete a row (`variantId` optional) |
| `derpdoc.row.delete.batch` | Delete multiple rows (`variantId` optional) |

### Keys & Export

| Tool | Description |
|------|-------------|
| `derpdoc.table.keys.set` | Set primary and secondary keys |
| `derpdoc.table.export.set` | Enable/disable export for a table |
| `derpdoc.export` | Run the export pipeline (writes `.derpdoc` + generated `.g.cs`) |

### Views

| Tool | Description |
|------|-------------|
| `derpdoc.view.list` | List views for a table (returns full config: filters, sorts, chart settings) |
| `derpdoc.view.create` | Create a view (`Grid`, `Board`, `Calendar`, `Chart`, `Custom`) |
| `derpdoc.view.update` | Update view config (filters, sorts, groupBy, chart kind/columns, visible columns) |
| `derpdoc.view.delete` | Delete a view |

### Node Graph

| Tool | Description |
|------|-------------|
| `derpdoc.nodegraph.ensure` | Create/reuse a node-graph view and scaffold required schema (`Type`, `Pos`, `Title`, `ExecNext`, `Edges`, edge subtable columns) |
| `derpdoc.nodegraph.get` | Read node-graph schema bindings and per-type layout settings |
| `derpdoc.nodegraph.layout.set` | Modify per-type layout (`nodeWidth`, field `mode`: `Hidden`/`Setting`/`InputPin`/`OutputPin`) |

Use `derpdoc.nodegraph.ensure` first, then use `derpdoc.row.*` tools to add/edit node rows and edge rows.

### Documents & Blocks

| Tool | Description |
|------|-------------|
| `derpdoc.document.list` | List all documents |
| `derpdoc.document.create` | Create a document |
| `derpdoc.document.update` | Update document title/folder |
| `derpdoc.document.delete` | Delete a document |
| `derpdoc.document.folder.set` | Move a document into a folder (or clear folder) |
| `derpdoc.block.list` | List blocks in a document (table blocks include `tableVariantId`) |
| `derpdoc.block.add` | Insert a block (`Table` blocks support `tableVariantId`) |
| `derpdoc.block.update` | Update block fields (type/text/indent/check/language/table/tableVariantId/view) |
| `derpdoc.block.delete` | Delete a block |
| `derpdoc.block.move` | Move a block by index |
| `derpdoc.block.view.set` | Set which view a block uses |
| `derpdoc.block.view.create` | Create a per-block view instance |

### Derived Tables & Formulas

| Tool | Description |
|------|-------------|
| `derpdoc.derived.get` | Get derived table config (base table, steps, projections) |
| `derpdoc.derived.set` | Set derived table config |
| `derpdoc.formula.validate` | Validate a formula expression |

### Nanobanana Images

| Tool | Description |
|------|-------------|
| `derpdoc.nanobanana.generate` | Call Gemini `generateContent`, save image under `Assets/`, optionally assign path into a `TextureAsset` cell |
| `derpdoc.nanobanana.edit` | Call Gemini `generateContent` for image edits, save image under `Assets/`, optionally assign path into a `TextureAsset` cell |

These tools read global user preferences from `preferences.json`:

- `pluginSettings.nanobanana.apiKey` (required)
- `pluginSettings.nanobanana.apiBaseUrl` (optional override; defaults to `https://generativelanguage.googleapis.com/v1beta/models/gemini-3-pro-image-preview`)

Transport details:

- Endpoint: `${apiBaseUrl}:generateContent`
- Auth header: `x-goog-api-key: <api key>`
- Request:
  - Full Gemini body: pass `request.contents` directly (and optional `generationConfig`/`safetySettings`/etc)
  - Shorthand: `{ prompt, imageBase64|imageDataUrl, mimeType? }` (plus optional `generationConfig`, `safetySettings`, `systemInstruction`, `tools`, `toolConfig`, `cachedContent`)
  - For `derpdoc.nanobanana.edit` shorthand, `imageBase64` or `imageDataUrl` is required.

### ElevenLabs Audio

| Tool | Description |
|------|-------------|
| `derpdoc.elevenlabs.generate` | Call ElevenLabs text-to-speech, save audio under `Assets/`, optionally assign path into an `AudioAsset` cell |
| `derpdoc.elevenlabs.edit` | Call ElevenLabs speech-to-speech, save audio under `Assets/`, optionally assign path into an `AudioAsset` cell |

These tools read global user preferences from `preferences.json`:

- `pluginSettings.elevenlabs.apiKey` (required)
- `pluginSettings.elevenlabs.apiBaseUrl` (optional override; defaults to `https://api.elevenlabs.io`)

Transport details:

- Generate endpoint: `${apiBaseUrl}/v1/text-to-speech/{voiceId}`
- Edit endpoint: `${apiBaseUrl}/v1/speech-to-speech/{voiceId}`
- Auth header: `xi-api-key: <api key>`
- Query params: `output_format` (default `mp3_44100_128`), optional `enable_logging`
- Generate shorthand request: `request` must include `voiceId` (or `voice_id`) and `text`
- Edit shorthand request: `request` must include `voiceId` (or `voice_id`) and one of `audioBase64`/`audioDataUrl`

## Subtable Columns

Subtable columns let a parent table own nested child data per row (e.g., a "Weapons" table where each weapon has its own "Damage Distribution" subtable).

### How it works

1. **Add a Subtable column** to a parent table via `derpdoc.column.add` with `kind: "Subtable"`.
2. The server **auto-creates a child table** with:
   - A hidden `_parentRowId` column (Text, links each child row to its parent row)
   - A default `Item` column (Text)
3. The response includes `childTableId` — the ID of the new child table.
4. The child table appears in `derpdoc.table.list` with `isSubtable: true` and `parentTableId` set.

### Adding data to subtables

Each child row must have its `_parentRowId` cell set to the parent row's ID so the parent knows which child rows belong to which parent row.

```
# 1. Create parent table + subtable column
derpdoc.column.add { tableId: "parent", name: "Stats", kind: "Subtable" }
  → returns { columnId: "col_abc", childTableId: "child_xyz" }

# 2. Add columns to the child table schema
derpdoc.column.add { tableId: "child_xyz", name: "Value", kind: "Number" }
derpdoc.column.add { tableId: "child_xyz", name: "Probability", kind: "Number" }

# 3. Get the child table schema to find the _parentRowId column ID
derpdoc.table.schema.get { tableId: "child_xyz" }
  → columns: [{ id: "col_pr", name: "_parentRowId", hidden: true }, ...]

# 4. Add child rows, setting _parentRowId to the parent row's ID
derpdoc.row.add { tableId: "child_xyz", cells: { "col_pr": "parent_row_1", "col_val": 80, "col_prob": 0.25 } }
derpdoc.row.add { tableId: "child_xyz", cells: { "col_pr": "parent_row_1", "col_val": 90, "col_prob": 0.30 } }
```

### Querying subtable data

Use `derpdoc.table.query` on the child table. All rows are returned; filter client-side by `_parentRowId` to get rows for a specific parent row.

### Deleting a Subtable column

`derpdoc.column.delete` on a Subtable column cascade-deletes the child table and all its rows.

### Nesting

Subtables can be nested up to 3 levels deep (a child table can have its own Subtable columns).

## Chart Views

Chart views visualize table data as Bar, Line, Pie, or Area charts.

### Creating a chart view

```
derpdoc.view.create {
  tableId: "tbl_123",
  type: "Chart",
  name: "Revenue Chart",
  chartKind: "Area",                    # Bar | Line | Pie | Area
  chartCategoryColumnId: "col_month",   # X-axis (text/select/number column)
  chartValueColumnId: "col_revenue"     # Y-axis (number column)
}
```

### Composite charts (subtable data)

When a Chart view's value column is a **Subtable column**, it renders a **multi-series chart** — one series per parent row, overlaid on the same axes:

1. Create a Chart view on the **child table** first (configures which columns are X/Y axes)
2. Create a Chart view on the **parent table** with `chartValueColumnId` set to the Subtable column
3. The parent chart reads the child table's Chart view config and renders one series per parent row

This is useful for comparing distributions across rows (e.g., overlaying damage curves for different weapons).

## Notes

- Paths are resolved relative to `--workspace` unless absolute.
- If Derp.Doc UI is running, it writes `.derpdoc-active.json` into its launch directory; `derpdoc mcp --workspace <that dir>` will automatically target the currently-open project.
- Export requires at least one table with `ExportConfig.Enabled=true` and a primary key.
- Subtable columns are auto-skipped during export.
