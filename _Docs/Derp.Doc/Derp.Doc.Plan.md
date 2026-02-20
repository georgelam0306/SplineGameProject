# Derp.Doc - Design Plan

A standalone Coda/Notion-like productivity tool built on DerpTech's ImGUI system (`Im.cs`), designed for creating rich documents, relational databases, and game data pipelines. Replaces GameDoc as the canonical data authoring tool for future games.

## Table of Contents

- [Goals](#goals)
- [Architecture Overview](#architecture-overview)
- [Project Structure](#project-structure)
- [Table System](#table-system)
  - [DocTable Core](#doctable-core)
  - [Column Types](#column-types)
  - [Derived Tables](#derived-tables-append--join)
  - [Export Tables](#export-tables)
- [Document System](#document-system)
  - [Block Model](#block-model)
  - [Rich Text](#rich-text)
  - [Editing UX](#editing-ux)
- [Formula Engine](#formula-engine)
  - [Syntax](#syntax)
  - [Type System](#type-system)
  - [Evaluation Architecture](#evaluation-architecture)
  - [Dependency Tracking](#dependency-tracking)
- [Storage Format](#storage-format)
  - [Project Layout](#project-layout)
  - [Version Control Mergeability](#version-control-mergeability)
  - [Document Ordering](#document-ordering)
- [Export Pipeline](#export-pipeline)
  - [What Gets Generated](#what-gets-generated)
  - [Binary Format](#binary-format)
  - [MSBuild Integration](#msbuild-integration)
  - [NativeAOT Compatibility](#nativeaot-compatibility)
- [Development Pipeline](#development-pipeline)
  - [Hot-Reload (Data Changes)](#hot-reload-data-changes)
  - [Schema Change Workflow](#schema-change-workflow)
  - [CI Integration](#ci-integration)
- [View System](#view-system)
- [Automation System](#automation-system)
- [Build Phases](#build-phases)
- [MCP Server (Stretch Goal)](#mcp-server-stretch-goal)

---

## Goals

1. **Rich document editor** with block-based editing, rich text (bold/italic/headings/code/links), markdown shortcuts, and toolbar formatting.
2. **Relational database** with typed columns, cross-table relations, formula columns, filtering, sorting, and multiple views.
3. **Derived tables** (novel) that compute rows/columns from other tables via Append/Join pipelines, while still allowing local columns.
4. **Export tables** that generate C# structs + binary data for zero-copy runtime consumption in NativeAOT games. Full replacement of GameDoc for future titles.
5. **Coda-level formula engine** with collection operations, table references, and method chaining. Independent from the Derp.Ecs expression DSL.
6. **Version control friendly** file format (JSONL) where concurrent edits to different rows/blocks auto-merge in git.
7. **Seamless development pipeline** with hot-reload for data changes and automated export in the build system.

---

## Architecture Overview

```
Derp.Doc
├── Workspace
│   └── Project[]
│       ├── Document[]                 Rich text pages
│       │   └── Block[]                Paragraph, Heading, List, TableEmbed, CodeBlock...
│       │       └── RichSpan[]         Formatted text runs within a block
│       ├── Table[]                    Relational database tables
│       │   ├── Column[]               Typed columns (Text, Number, Select, Relation, Formula...)
│       │   ├── Row[]                  Data rows
│       │   ├── View[]                 TableView, BoardView, CalendarView, FormView...
    │       │   ├── DerivedConfig?         If derived table: append/join pipeline + projections + local columns
│       │   └── ExportConfig?          If exported: type mappings + keys + export enablement (namespace fixed; struct derived)
│       └── Automation[]               C# scripts triggered by events
│
├── Formula Engine (NEW, independent from Derp.Ecs)
│   ├── Table references               Tasks.Filter(@Status == "Done")
│   ├── Collection operations           .Sum(), .Count(), .Sort(), .First()
│   ├── Row context                     thisRow.Price * thisRow.Quantity
│   └── Cross-table lookups             Lookup(Users, @Id == thisRow.OwnerId, @Name)
│
├── Export Pipeline
│   ├── derpdoc CLI tool                Export tables → C# structs + binary
│   ├── MSBuild integration             Auto-export on build
│   └── Hot-reload server               Push data deltas to running game
│
└── Renderer (ImGUI-based)
    ├── DocumentPanel                   Block-by-block rich text editing
    ├── TablePanel                      Spreadsheet grid view
    ├── BoardPanel                      Kanban view
    ├── InspectorPanel                  Row/block properties
    ├── SidebarPanel                    Project tree + search
    └── FormulaBar                      Expression editor with autocomplete
```

---

## Project Structure

Follows the Derp.Ui pattern as a standalone tool:

```
Derp.Doc/
├── Program.cs                         Minimal entry point
├── Derp.Doc.csproj                    Exe, refs Engine + Shared
├── Derp.Doc.sln
├── Directory.Build.props              Custom output paths
│
├── Editor/
│   ├── DocEditorComposition.cs        Derp.DI composition root
│   ├── DocEditorApp.cs                Main loop (Init → ImGUI → Render)
│   └── DocWorkspace.cs                Open project state, active doc/table
│
├── Model/
│   ├── DocProject.cs                  Project root (docs + tables + automations)
│   ├── DocDocument.cs                 Document (ordered list of blocks)
│   ├── DocBlock.cs                    Block (heading, paragraph, list, table embed...)
│   ├── RichText.cs                    Plain text + formatting spans
    │   ├── DocTable.cs                    Table (schema + rows + views + derived/export config)
│   ├── DocColumn.cs                   Column definition (name, type, config)
│   ├── DocRow.cs                      Row data (cell values by column ID)
│   ├── DocView.cs                     View config (visible columns, filter, sort, group)
    │   ├── DocDerivedTableConfig.cs       Derived table config (append/join steps, projections, local columns)
│   ├── DocExportConfig.cs             Export config (namespace, type mappings, keys)
│   └── DocAutomation.cs               Automation definition (trigger + script)
│
├── Storage/
│   ├── ProjectSerializer.cs           JSONL read/write for tables + documents
│   └── ProjectLoader.cs              Load project from disk
│
    ├── Tables/
│   ├── DocTableEngine.cs              Query, filter, sort at runtime
│   ├── DocRelationIndex.cs            Cross-table relation tracking
    │   ├── DocDerivedResolver.cs          Materialize derived table rows from sources (append/join)
│   └── DocFormulaEngine.cs            Formula evaluation + dependency graph
│
├── Formula/                           Independent formula engine
│   ├── FormulaLexer.cs                Tokenizer
│   ├── FormulaParser.cs               AST builder (dot-access, method chains)
│   ├── FormulaAst.cs                  Node types (literals, ops, table refs, methods)
│   ├── FormulaTypeChecker.cs          Type validation + annotation
│   ├── FormulaFlattener.cs            AST → linear opcodes
│   ├── FormulaEvaluator.cs            Execute opcodes against IFormulaContext
│   ├── FormulaContext.cs              Table/row access interface
│   └── FormulaFunctions.cs            Built-in function library
│
├── Panels/
│   ├── SidebarPanel.cs                Project tree (docs + tables)
│   ├── DocumentPanel.cs               Rich text block editor
│   ├── TablePanel.cs                  Spreadsheet grid view
│   ├── BoardPanel.cs                  Kanban view (group by Select column)
│   ├── InspectorPanel.cs              Row/block/column properties
│   ├── FormulaBar.cs                  Expression editor with autocomplete
│   └── ViewSwitcher.cs                View selector + config
│
├── RichText/
│   ├── RichTextRenderer.cs            Span-aware text rendering via Im.Text
│   ├── RichTextEditor.cs              Editing logic (extends ImTextArea patterns)
│   ├── MarkdownShortcuts.cs           Detect **bold**, *italic*, # heading, etc.
│   └── SelectionToolbar.cs            Floating format toolbar on text selection
│
└── Automation/
    ├── AutomationRunner.cs            C# script execution (Roslyn scripting)
    └── AutomationTrigger.cs           Event matching (OnRowChanged, OnSchedule, etc.)
```

### Dependencies

```xml
<!-- Derp.Doc.csproj -->
<ProjectReference Include="../Engine/src/Engine/Engine.csproj" />
<ProjectReference Include="../Shared/Pooled/Annotations/Pooled.Annotations.csproj" />
<ProjectReference Include="../Shared/Pooled/Runtime/Pooled.Runtime.csproj" />
<ProjectReference Include="../Shared/Pooled/Generator/Pooled.Generator.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

### Bootstrap (following Derp.Ui pattern)

```csharp
// Program.cs
public static class Program
{
    public static void Main(string[] args)
    {
        var composition = new DocEditorComposition();
        composition.Editor.Run();
    }
}

// DocEditorComposition.cs
[Composition]
internal partial class DocEditorComposition
{
    static void Setup() => DI.Setup()
        .Bind<DocWorkspace>().As(Singleton).To<DocWorkspace>()
        .Bind<DocEditorApp>().As(Singleton).To<DocEditorApp>()
        .Root<DocEditorApp>("Editor");
}

// DocEditorApp.Run()
public void Run()
{
    DerpEngine.InitWindow(1400, 900, "Derp.Doc");
    DerpEngine.InitSdf();
    Im.Initialize(enableMultiViewport: true);
    Im.SetFonts(font, iconFont);
    SetupDefaultDockLayout();

    while (!DerpEngine.WindowShouldClose())
    {
        DerpEngine.PollEvents();
        if (!DerpEngine.BeginDrawing()) continue;

        Im.Begin(deltaTime);
        DrawWorkspace();  // Sidebar, Document/Table panel, Inspector
        Im.End();

        DerpEngine.DispatchSdfToTexture();
        Im.UpdateSecondaryViewports(deltaTime);
        DerpEngine.EndDrawing();
    }

    DerpEngine.CloseWindow();
}
```

---

## Table System

### DocTable Core

A DocTable is the central data structure. It stores schema, rows, views, and optional derived/export configuration.

```csharp
public class DocTable
{
    public string Id;                          // Stable GUID
    public string Name;                        // Display name
    public List<DocColumn> Columns;            // Schema
    public List<DocRow> Rows;                  // Data (AoS)
    public List<DocView> Views;                // Saved view configs
    public DocDerivedTableConfig? DerivedConfig; // Non-null if derived table
    public DocExportConfig? ExportConfig;      // Non-null if exported
}
```

**Table taxonomy** (not separate types — flags on a single type):

```
DocTable
├── Regular?        DerivedConfig == null
├── Derived?        DerivedConfig != null
├── Exported?       ExportConfig != null
└── Derived+Exported? Both != null  (common pattern for game data)
```

At runtime, column indexes are built on load for query performance. Storage is AoS (one object per row) for clean JSONL serialization.

### Column Types

```csharp
public enum DocColumnKind : byte
{
    Text,              // string
    Number,            // double
    Checkbox,          // bool
    Select,            // Single choice from options list
    MultiSelect,       // Multiple choices from options list
    Date,              // DateTime
    Person,            // User reference
    Relation,          // RowHandle[] pointing to another DocTable
    Formula,           // Expression DSL -> computed value
    Url,               // string (validated URL)
    Email,             // string (validated email)
    CreatedTime,       // Auto-set on row creation
    LastEditedTime,    // Auto-set on row edit
    LastEditedBy,      // Auto-set to current user
}
```

**Column definition:**

```csharp
public class DocColumn
{
    public string Id;                   // Stable GUID
    public string Name;                 // Display name
    public DocColumnKind Kind;
    public DocColumnConfig Config;      // Type-specific config
}

public class DocColumnConfig
{
    // Select / MultiSelect
    public List<string>? Options;

    // Relation
    public string? RelationTableId;

    // Formula
    public string? FormulaText;             // Expression DSL source

    // Number
    public string? NumberFormat;            // "integer", "decimal", "percent", "currency"

    // Date
    public string? DateFormat;              // "date", "datetime", "relative"
}
```

**Row data:**

```csharp
public class DocRow
{
    public string Id;                              // Stable GUID
    public Dictionary<string, DocCellValue> Cells; // columnId -> value
}

public struct DocCellValue
{
    public DocColumnKind Kind;
    public string? StringValue;
    public double NumberValue;
    public bool BoolValue;
    public DateTime DateValue;
    public string[]? MultiValue;          // MultiSelect, Relation (row IDs)
    public DocCellValue? ComputedValue;   // Formula result (cached)
}
```

### Derived Tables (Append + Join)

A derived table is a table whose rows and some columns are computed from other tables via a deterministic pipeline of steps. It can also have **local columns** whose values are stored on the derived table (never written back to source tables).

This replaces the overloaded “merge table” concept with two explicit primitives:

- **Append (union-like):** stack rows from multiple sources into one output row set.
- **Join (enrich):** keep a base table’s rows and pull in columns from additional sources by matching keys.

**Why derived tables matter for games:**

- **All Purchasable Items** = Append(Weapons, Armor, Consumables) for shop UI
- **Inventory View** = Join(InventoryItems -> Weapons/Armor/Consumables) for UI and balancing
- **All Spawnable Entities** = Append(Units, Buildings, Traps) for level editor
- **All Stat Modifiers** = Append(Buffs, Debuffs, Passives) for stat calculation

**Non-negotiable requirement: stable output row identity**

If a derived table has local data columns (editable, stored on the derived table), those values must be keyed by a stable output row key (`OutRowKey`), or they will drift when sources change.

- **Append:** `OutRowKey = (sourceTableId, sourceRowId)`
- **Join (base-driven):** `OutRowKey = baseRowId`

**Core step types (Phase 4 ships these concepts; UI may initially expose a safe subset):**

```text
Append step:
  output rows = all rows from all sources (in deterministic order)
  OutRowKey = (sourceTableId, sourceRowId)

Join step:
  output rows = rows from base table (deterministic base order)
  OutRowKey = baseRowId
  projected cells = match by key mapping (and optional guard condition)
```

**Diagnostics states (consistent across UI, evaluation, and export):**

- `NoMatch`: join key not found.
- `MultiMatch`: join key matched multiple rows.
  - Phase 4 policy: **strict error** (no silent “pick one”).
  - Planned path: allow an explicit deterministic resolver (aggregator) per join step.
- `TypeMismatch`: projected/formula value does not match the column’s enforced type.

**Facet aliases (optional, UX/codegen sugar):**

Derived tables will often have sparse columns (append or type-guarded joins). Internally we can represent presence, but for codegen/UX we may want “facets”.

- Default: auto-name facets mechanically (example: `Facet_Weapon_Armor`).
- Optional: user can provide an alias (example: `Equipable`) in derived table settings.

**Derived tables in formulas:** derived tables are indistinguishable from regular tables to the formula engine. `AllItems.Filter(@Kind == "Weapon").Count()` works.

### Export Tables

Any table (regular or merge) can be marked for export. Export config specifies how columns map to C# types and what gets generated.

```csharp
public class DocExportConfig
{
    public string Namespace;                // e.g. "MyGame.Data"
    public string StructName;              // e.g. "UnitData"
    public string? PrimaryKeyColumn;       // Column for O(1) lookup
    public List<DocExportColumnMapping> ColumnMappings;
    public List<DocExportForeignKey> ForeignKeys;
}

public struct DocExportColumnMapping
{
    public string ColumnId;
    public string ExportFieldName;         // C# field name
    public string ExportType;              // "int", "float", "Fixed64", "StringHandle", "enum", etc.
}

public struct DocExportForeignKey
{
    public string ColumnId;               // Relation column in this table
    public string TargetTableId;          // Target export table
    public string TargetPrimaryKey;       // PK column in target
}
```

**Column type mapping:**

| DocColumn Type | C# Export Type | Notes |
|---|---|---|
| Text | `StringHandle` | Interned in string registry |
| Number | `int` / `float` / `Fixed64` | User picks precision in export config |
| Checkbox | `bool` (as `byte`) | |
| Select | Generated `enum` | Enum codegen'd from options list |
| MultiSelect | Generated `[Flags] enum` | |
| Date | `int` (unix days) | |
| Relation | `int` (foreign key) | Primary key of target table |
| Formula | Baked value | Pre-computed at export time |
| Url / Email | `StringHandle` | |
| CreatedTime | Excluded | Editor-only metadata |
| LastEditedTime | Excluded | Editor-only metadata |
| LastEditedBy | Excluded | Editor-only metadata |

---

## Document System

### Block Model

Documents are ordered lists of blocks. Each block has a type, content, and properties.

```csharp
public class DocDocument
{
    public string Id;
    public string Title;
    public List<DocBlock> Blocks;           // Ordered list
}

public class DocBlock
{
    public string Id;                       // Stable GUID
    public string Order;                    // Fractional index for merge-friendly ordering
    public DocBlockType Type;
    public RichText? Text;                  // For text-bearing blocks
    public DocBlockProperties Properties;   // Type-specific properties
}

public enum DocBlockType : byte
{
    Paragraph,
    Heading1,
    Heading2,
    Heading3,
    BulletList,
    NumberedList,
    CheckboxList,
    CodeBlock,
    Quote,
    Divider,
    TableEmbed,        // Inline view of a DocTable
    Image,
    Callout,
}
```

### Rich Text

Text content within blocks uses a span-based model for formatting:

```csharp
public class RichText
{
    public string PlainText;               // Raw text content
    public List<RichSpan> Spans;           // Formatting overlays
}

public struct RichSpan
{
    public int Start;                      // Char offset in PlainText
    public int Length;
    public RichSpanStyle Style;
}

[Flags]
public enum RichSpanStyle : ushort
{
    None          = 0,
    Bold          = 1 << 0,
    Italic        = 1 << 1,
    Code          = 1 << 2,
    Strikethrough = 1 << 3,
    Underline     = 1 << 4,
    Link          = 1 << 5,        // URL stored in span metadata
    Highlight     = 1 << 6,
}
```

### Editing UX

Two complementary input modes:

**Markdown shortcuts** (detect while typing in ImTextArea):

| Input | Result |
|---|---|
| `**text**` | Bold |
| `*text*` | Italic |
| `` `code` `` | Inline code |
| `# ` at line start | Heading 1 |
| `## ` at line start | Heading 2 |
| `### ` at line start | Heading 3 |
| `- ` at line start | Bullet list |
| `1. ` at line start | Numbered list |
| `[] ` at line start | Checkbox |
| `> ` at line start | Quote |
| `/` at line start | Slash command menu (block insertion) |

**Selection toolbar** (floating above selected text, Notion-style):

- Appears on text selection
- Buttons: **B**, *I*, ~~S~~, `Code`, Link, Color, Highlight
- Uses `Im.Button` + hit testing against selection bounds

---

## Formula Engine

Independent from the Derp.Ecs expression DSL. The Ecs DSL handles scalar property bindings evaluated every frame with `Fixed64`. The Doc formula engine handles collection operations, table references, and string/date ops, evaluated on data change.

### Syntax

**Scalar operations (baseline):**

```
thisRow.Price * thisRow.Quantity
clamp(thisRow.Health, 0, 100)
thisRow.Status == "Done" ? "Complete" : "Pending"
```

**Table references and collection operations:**

```
Tasks.Filter(@Status == "Done")
Tasks.Filter(@Assignee == thisRow.Owner).Count()
Orders.Filter(@CustomerId == thisRow.Id).Sum(@Total)
Orders.Filter(@CustomerId == thisRow.Id).Average(@Rating)
Customers.Find(@Id == thisRow.CustomerId).Name
Tasks.CountIf(@Priority == "High" && @Status != "Done")
```

**Method chains:**

```
Tasks
  .Filter(@Project == thisRow.Name)
  .Sort(@DueDate)
  .First()
  .Name
```

**Date operations:**

```
Today()
DateDiff(thisRow.DueDate, Today(), "days")
DateAdd(Today(), 7, "days")
thisRow.CreatedAt.Month()
```

**String operations:**

```
thisRow.Name.Contains("urgent")
thisRow.Name.StartsWith("DRAFT")
thisRow.Name.Length()
thisRow.Name.Lower()
thisRow.Name.Upper()
thisRow.Name.Trim()
Concatenate(thisRow.FirstName, " ", thisRow.LastName)
Format("Item #{0}: {1}", thisRow.Id, thisRow.Name)
```

**Aggregation shortcuts:**

```
CountIf(Tasks, @Done && @Priority > 2)
SumIf(Orders, @Total, @Year == 2025)
Lookup(Users, @Id == thisRow.OwnerId, @Name)
```

**Conditionals and null handling:**

```
If(@Stock > 0, "Available", "Sold Out")
Switch(@Status, "A", 1, "B", 2, 0)
IsBlank(thisRow.Email)
IfBlank(thisRow.Nickname, thisRow.Name)
```

### Type System

```csharp
public enum FormulaType : byte
{
    Number,             // double
    Integer,            // int (for counts, IDs)
    Bool,
    String,
    Date,
    RowRef,             // Reference to a single row
    RowCollection,      // Result of Table or .Filter()
    Null,               // For IsBlank/IfBlank
    Enum,               // Select column value
}
```

### Evaluation Architecture

Formulas need table access during evaluation. The evaluator takes a context object rather than a flat value array:

```csharp
public interface IFormulaContext
{
    ITableAccessor GetTable(string name);
    RowHandle ThisRow { get; }
    ITableAccessor ThisTable { get; }
    DocCellValue ReadCell(ITableAccessor table, RowHandle row, string column);
}

public interface ITableAccessor
{
    int RowCount { get; }
    RowHandle GetRow(int index);
    DocCellValue ReadCell(RowHandle row, string column);
    DocColumnKind GetColumnType(string column);
}
```

**Pipeline:**

```
Source text (string)
    |
    v
FormulaLexer -> Tokens
    |             (add: '.', 'thisRow', table names as identifiers)
    v
FormulaParser -> FormulaNode[] (AST)
    |             (add: dot-access chains, method calls, lambda predicates)
    v
FormulaTypeChecker -> Validates types, resolves table/column refs
    |
    v
FormulaFlattener -> FlatFormula (linear opcodes)
    |
    v
FormulaEvaluator -> DocCellValue (result)
                     (uses IFormulaContext for table/row access)
```

**AST node types:**

```csharp
public enum FormulaNodeKind : byte
{
    // Literals
    NumberLiteral, StringLiteral, BoolLiteral, DateLiteral,

    // Operators
    Add, Sub, Mul, Div, Mod,
    Eq, Neq, Lt, Gt, Lte, Gte,
    And, Or, Not,
    Ternary,

    // References
    TableRef,           // "Tasks" -> resolves to row collection
    ThisRow,            // "thisRow" -> current row context
    DotAccess,          // expr.fieldName -> field access
    ColumnRef,          // "@ColumnName" inside Filter/Sort predicates

    // Collection methods
    Filter, Sort, First, Last,
    Count, Sum, Average, Min, Max,
    Contains, ForEach, Unique,

    // Scalar functions
    Clamp, Abs, Floor, Ceil, Round, Lerp,
    If, Switch,
    IsBlank, IfBlank,
    Concatenate, Format,
    Today, DateDiff, DateAdd,
    StringContains, StartsWith, EndsWith,
    Length, Lower, Upper, Trim,

    // Shortcuts
    CountIf, SumIf, Lookup,
}
```

**Flat opcodes:**

```csharp
public enum FlatFormulaOp : byte
{
    // Existing scalar ops
    LoadConst, LoadThisRowField, Add, Sub, Mul, Div, // etc.

    // Table operations
    LoadTable,          // Push table reference onto stack
    LoadThisRow,        // Push thisRow reference
    DotAccess,          // Pop row/result, push field value

    // Collection operations
    Filter,             // Pop collection + predicate -> filtered collection
    Sort,               // Pop collection + field + direction -> sorted collection
    First, Last,        // Pop collection -> single row
    Count,              // Pop collection -> int
    Sum, Average,       // Pop collection + field -> number
    Min, Max,           // Pop collection + field -> value
    Contains,           // Pop collection + value -> bool
    ForEach,            // Pop collection + expr -> mapped collection
    Unique,             // Pop collection -> deduplicated collection

    // Utility
    IsBlank,            // Pop value -> bool
    IfBlank,            // Pop value + fallback -> value
    Today,              // Push current date
    DateDiff,           // Pop date, date, unit -> number
    Concatenate,        // Pop N strings -> string
    StringContains,     // Pop string, substring -> bool
    // ... etc.
}
```

### Dependency Tracking

When a formula in table A references table B (`Orders.Sum(@Total)`), changes to table B must trigger re-evaluation of the formula in table A. The dependency graph operates at two levels:

```
Table-level dependencies:
  ShopItems.TotalRevenue  depends on ->  Orders (any row's Total column)
  TaskSummary.Count       depends on ->  Tasks (row count)

Cell-level dependencies (within a table):
  Row.Profit  depends on ->  same Row.Revenue, same Row.Cost
```

Topological sort ensures evaluation order. Circular dependencies are detected and reported as errors.

---

## Storage Format

### Project Layout

```
MyProject.derpdoc/
├── project.json                       Project metadata
├── docs/
│   ├── getting-started.blocks.jsonl   One block per line
│   ├── getting-started.meta.json      Doc metadata
│   ├── api-reference.blocks.jsonl
│   └── api-reference.meta.json
├── tables/
│   ├── tasks.schema.json              Column definitions + derived/export config
│   ├── tasks.rows.jsonl               One row per line
│   ├── tasks.views.json               Saved view configs
│   ├── customers.schema.json
│   ├── customers.rows.jsonl
│   ├── customers.views.json
│   ├── shop-items.schema.json         Derived table config
│   └── shop-items.own-data.jsonl      Local-column data for derived rows (keyed by OutRowKey)
└── automations/
    ├── on-task-complete.cs            C# scripts
    └── daily-summary.cs
```

### Version Control Mergeability

**JSONL (JSON Lines)** is the key format choice. Each row or block is one line. Git diffs become row-level:

```diff
# tasks.rows.jsonl - clean merge when different rows edited
 {"id":"r1","cells":{"name":"Fix bug","status":"Todo","priority":1}}
-{"id":"r2","cells":{"name":"Add auth","status":"Todo","priority":2}}
+{"id":"r2","cells":{"name":"Add auth","status":"Done","priority":2}}
 {"id":"r3","cells":{"name":"Write docs","status":"In Progress","priority":3}}
```

Two people editing different rows -> **auto-merge succeeds**. Same row -> conflict, but the conflict is one readable line, easy to resolve.

### Document Ordering

Blocks use **fractional indexing** so ordering is per-block (not a central array). This merges cleanly for concurrent insertions:

```jsonl
{"id":"b1","order":"a0","type":"heading1","text":"Introduction","spans":[]}
{"id":"b2","order":"a1","type":"paragraph","text":"Welcome to...","spans":[]}
{"id":"b5","order":"a1V","type":"paragraph","text":"(inserted between b2 and b3)","spans":[]}
{"id":"b3","order":"a2","type":"tableEmbed","tableId":"tasks","spans":[]}
```

Two people inserting blocks at different positions produce different `order` values and merge without conflict.

---

## Export Pipeline

### Overview

```
Derp.Doc Project
|
v  derpdoc export MyProject/ --generated Generated/ --bin Resources/Database/MyGame.derpdoc
|
+-- Step 1: Read all Export Tables (+ resolve Derived Tables)
+-- Step 2: Evaluate all Formula columns (bake values)
+-- Step 3: Validate foreign keys, types, constraints
+-- Step 4: Generate C# structs (one per export table)
+-- Step 5: Generate enums (from Select columns)
+-- Step 6: Generate GameDataDb + GameDataBinaryLoader
+-- Step 7: Write <GameName>.derpdoc (compiled database container)
|
v
Generated/
├── UnitData.g.cs                  C# struct
├── BuildingData.g.cs              C# struct
├── ShopItemData.g.cs              C# struct (from derived table, fully materialized)
├── UnitTypeEnum.g.cs              Enum from Select column options
├── ShopItemSourceType.g.cs        Enum from derived source labels
├── GameDataDb.g.cs                Typed container (db.Units, db.Buildings, etc.)
├── GameDataBinaryLoader.g.cs      Binary -> GameDataDb (zero-copy, memory-mapped)
└── <GameName>.derpdoc             Compiled database container
```

### What Gets Generated

**Example: Units table in Derp.Doc**

| Name | Health | Speed | Type | BuildingRef | DPS (formula) |
|---|---|---|---|---|---|
| Marine | 100 | 3.5 | Infantry | Barracks | @Health * @Speed |
| Tank | 500 | 1.2 | Vehicle | Factory | @Health * @Speed |

Export config: enabled, PK = `Id` (namespace is fixed: `DerpDocDatabase`; struct defaults to table name)

Generated C#:

```csharp
// UnitData.g.cs
[StructLayout(LayoutKind.Sequential)]
public struct UnitData
{
    public int Id;
    public StringHandle Name;
    public int Health;
    public Fixed64 Speed;
    public UnitType Type;           // Generated enum
    public int BuildingId;          // FK -> BuildingData
    public Fixed64 Dps;             // Baked from formula: @Health * @Speed
}

// UnitTypeEnum.g.cs
public enum UnitType : byte
{
    Infantry = 0,
    Vehicle = 1,
    Air = 2,
}

// GameDataDb.g.cs
public readonly struct GameDataDb
{
    public readonly UnitDataTable Units;
    public readonly BuildingDataTable Buildings;
    public readonly ShopItemDataTable ShopItems;
}

// GameDataBinaryLoader.g.cs
public sealed class GameDataBinaryLoader : IDisposable
{
    public static GameDataBinaryLoader Load(string filePath) { /* ... */ }
    public GameDataDb Db { get; }
}
```

Game code (NativeAOT safe):

```csharp
using var loader = GameDataBinaryLoader.Load("<GameName>.derpdoc");
var db = loader.Db;

ref readonly var marine = ref db.Units.FindById(0);
int hp = marine.Health;

foreach (ref readonly var unit in db.Units.All)
{
    Console.WriteLine($"{unit.Name}: {unit.Dps}");
}
```

### Binary Format

Derp.Doc-native binary format optimized for generated query APIs:

```
[Header]              Magic, version, checksum, table count
[TableDirectory]      (offset, size, recordCount) per table
[StringTable]         Null-terminated table names
[TableData]           Raw binary structs (16-byte aligned)
[SlotArray per table] int[] for O(1) PrimaryKey lookup
[StringRegistry]      Shared string pool (StringHandle mappings)
```

Zero-copy via `ReadOnlySpan<T>`. Memory-mappable. O(1) primary key lookups via slot arrays.

### MSBuild Integration

In the game's `.csproj`:

```xml
<PropertyGroup>
  <DerpDocProject>../MyGame.DerpDoc</DerpDocProject>
  <DerpDocOutputDir>$(IntermediateOutputPath)DerpDocGenerated/</DerpDocOutputDir>
  <DerpDocBinaryName>$(AssemblyName).derpdoc</DerpDocBinaryName>
</PropertyGroup>

<!-- Pre-build: export tables -> C# + binary -->
<Target Name="DerpDocExport"
        BeforeTargets="CoreCompile"
        Inputs="$(DerpDocProject)/tables/**/*.jsonl;$(DerpDocProject)/tables/**/*.json"
        Outputs="$(DerpDocOutputDir)GameDataDb.g.cs;$(DerpDocOutputDir)$(DerpDocBinaryName)">

  <Exec Command="derpdoc export &quot;$(DerpDocProject)&quot; --generated &quot;$(DerpDocOutputDir)&quot; --bin &quot;$(DerpDocOutputDir)$(DerpDocBinaryName)&quot;" />
</Target>

<!-- Include generated C# in compilation -->
<ItemGroup>
  <Compile Include="$(DerpDocOutputDir)*.g.cs" />
</ItemGroup>

<!-- Copy binary to output -->
<ItemGroup>
  <Content Include="$(DerpDocOutputDir)$(DerpDocBinaryName)" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

`Inputs`/`Outputs` ensures MSBuild **only re-exports when JSONL or schema files change**. Editing game code does not trigger re-export.

### NativeAOT Compatibility

All generated code is NativeAOT-safe:

- No `Activator.CreateInstance` or runtime type creation
- No reflection (`typeof`, `GetType()`, `GetMethod()`)
- All struct layouts are `[StructLayout(LayoutKind.Sequential)]`
- All loaders use direct struct construction via `MemoryMarshal`
- String interning uses `StringHandle` (pre-computed uint IDs)

---

## Development Pipeline

### Hot-Reload (Data Changes)

During development, data changes (editing cell values, adding/removing rows) should reflect in the running game without recompilation. Uses a **shared memory-mapped file with double buffering** for zero-copy, instant hot-reload.

**How it works:**

Derp.Doc and the game both memory-map the same file (`.derpdoc-live.bin`). The file contains two copies of the export data (double buffer). When Derp.Doc updates data, it writes to the inactive slot and atomically flips the active slot index. The game reads from the active slot — zero copy, zero serialization.

```
Derp.Doc Process                     Game Process
┌─────────────────┐                  ┌─────────────────┐
│ DocTable (AoS)  │                  │ GameDataDb      │
│ edit cell       │                  │ db.Units[3].Hp  │
│      |          │                  │      ^          │
│      v          │                  │      |          │
│ write to        │                  │ read from       │
│ inactive slot   │                  │ active slot     │
└──────┬──────────┘                  └──────┬──────────┘
       │                                    │
       ▼              OS Page Cache         ▼
  ┌────────────────────────────────────────────┐
  │    .derpdoc-live.bin (memory-mapped)        │
  │                                            │
  │  [Header]                                  │
  │    activeSlot: 0 or 1  ← atomic int        │
  │    generation: uint    ← bumped on write   │
  │    tableCount, slotSize, ...               │
  │                                            │
  │  [Slot 0]                                  │
  │    [TableDirectory: offsets, sizes]        │
  │    [UnitData: struct bytes row-by-row]     │
  │    [BuildingData: struct bytes row-by-row] │
  │    [SlotArrays: PK -> row index]           │
  │    [StringRegistry: StringHandle pool]     │
  │                                            │
  │  [Slot 1]                                  │
  │    [TableDirectory: offsets, sizes]        │
  │    [UnitData: struct bytes row-by-row]     │
  │    [BuildingData: struct bytes row-by-row] │
  │    [SlotArrays: PK -> row index]           │
  │    [StringRegistry: StringHandle pool]     │
  │                                            │
  │  Same binary layout as <GameName>.derpdoc  │
  │  per slot. Double-buffered to prevent      │
  │  torn reads.                               │
  └────────────────────────────────────────────┘
```

**Write flow (Derp.Doc side):**

1. Determine inactive slot (`1 - activeSlot`)
2. Write updated table data into inactive slot (full re-export of changed tables)
3. Atomic write: flip `activeSlot` and bump `generation`
4. Game sees new slot on next frame read

**Read flow (Game side):**

```csharp
// Once per frame — one uint read on most frames (no change = no work)
var header = _view.Read<SharedDataHeader>(0);
if (header.Generation != _lastGeneration)
{
    _lastGeneration = header.Generation;
    long offset = header.ActiveSlot == 0 ? header.Slot0Offset : header.Slot1Offset;
    _loader?.Dispose();
    _loader = GameDataBinaryLoader.Load(_livePath, offset, header.SlotSize);
    _db = _loader.Db;
    OnDataReloaded?.Invoke();
}
```

**Header structure:**

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct SharedDataHeader
{
    public uint Magic;           // "DDLV" (Derp.Doc Live)
    public uint Generation;      // Bumped on every write
    public int ActiveSlot;       // 0 or 1 (atomic)
    public int TableCount;
    public long Slot0Offset;     // Byte offset to slot 0 data
    public long Slot1Offset;     // Byte offset to slot 1 data
    public long SlotSize;        // Size of each slot's data
}
```

**Derp.Doc side — create and write:**

```csharp
// On project open: create the shared memory file
var livePath = Path.Combine(projectDir, ".derpdoc-live.bin");
var mmf = MemoryMappedFile.CreateFromFile(livePath, FileMode.CreateOrNew,
    mapName: null, capacity: maxSize);
_liveView = mmf.CreateViewAccessor();

// On cell edit: write to inactive slot, flip
int inactive = 1 - _liveView.ReadInt32(offset: 8); // 1 - activeSlot
WriteSlotData(_liveView, inactive, exportedTableData);
_liveView.Write(8, inactive);                        // flip activeSlot
_liveView.Write(4, ++_generation);                   // bump generation
_liveView.Flush();
```

**Game side — open and read:**

```csharp
public class GameDataManager
{
    private GameDataDb _db;
    private GameDataBinaryLoader? _loader;
    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _liveView;
    private uint _lastGeneration;
    private bool _isLive;
    private string _livePath = "";

    public ref readonly GameDataDb Db => ref _db;
    public event Action? OnDataReloaded;

    // Production: load baked binary
    public void LoadBaked(string binPath)
    {
        _loader?.Dispose();
        _loader = GameDataBinaryLoader.Load(binPath);
        _db = _loader.Db;
    }

    // Development: connect to Derp.Doc shared memory
    public void ConnectLive(string derpDocProjectDir)
    {
        _livePath = Path.Combine(derpDocProjectDir, ".derpdoc-live.bin");
        _mmf = MemoryMappedFile.CreateFromFile(_livePath, FileMode.Open,
            mapName: null, capacity: 0, MemoryMappedFileAccess.Read);
        _liveView = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        _isLive = true;
    }

    // Called once per frame
    public void Update()
    {
        if (!_isLive || _liveView == null) return;

        var header = _liveView.Read<SharedDataHeader>(0);
        if (header.Generation != _lastGeneration)
        {
            _lastGeneration = header.Generation;
            long offset = header.ActiveSlot == 0
                ? header.Slot0Offset
                : header.Slot1Offset;
            _loader?.Dispose();
            _loader = GameDataBinaryLoader.Load(_livePath, offset, header.SlotSize);
            _db = _loader.Db;
            OnDataReloaded?.Invoke();
        }
    }

    public void Dispose()
    {
        _loader?.Dispose();
        _liveView?.Dispose();
        _mmf?.Dispose();
    }
}
```

**Key properties:**

| Property | Value |
|---|---|
| Latency | 0 (next frame reads new data) |
| Copies | Zero (both processes read/write same memory pages) |
| Per-frame cost | One `uint` read when no change |
| Torn reads | Impossible (double buffering) |
| NativeAOT safe | Yes (no reflection, same struct layout as <GameName>.derpdoc) |
| File location | `{projectDir}/.derpdoc-live.bin` (gitignored) |
| Memory cost | 2x table data size (double buffer, typically KB-low MB) |

**When types change** (add/remove column, change type): the shared memory format changes too, so the game must be recompiled. This is expected — schema changes are infrequent. The live file is regenerated when Derp.Doc detects a schema change.

**Shipped game** (no Derp.Doc): loads `<GameName>.derpdoc` via `LoadBaked()`. No shared memory, no hot-reload. The `ConnectLive` path is dev-only.

### Schema Change Workflow

When a designer adds/removes a column or changes a column type, the generated C# types change. This requires recompilation:

```
1. Designer adds "Armor" column to Units table in Derp.Doc
2. Derp.Doc auto-saves schema + rows (JSONL)
3. Developer runs: dotnet build
   -> MSBuild target detects schema change
   -> derpdoc export regenerates UnitData.g.cs (now has Armor field)
   -> Compiler rebuilds
4. Game code can now reference: unit.Armor
```

Schema changes are infrequent compared to value changes. Most iteration is tweaking numbers, which hot-reloads without recompiling.

### CI Integration

```yaml
# Build pipeline
- name: Build and publish (NativeAOT)
  run: dotnet publish src/MyGame.csproj -c Release -r osx-arm64
  # MSBuild target auto-runs derpdoc export before compile
```

Or explicitly:

```yaml
- name: Export Derp.Doc tables
  run: derpdoc export MyGame.DerpDoc/ --generated src/Generated/ --bin Resources/Database/MyGame.derpdoc

- name: Build game (NativeAOT)
  run: dotnet publish src/MyGame.csproj -c Release -r osx-arm64
```

**Full development + build picture:**

```
                    DESIGN TIME
                    Derp.Doc UI
                    +-- Edit tables, write formulas
                    +-- Configure derived tables
                    +-- Mark tables for export
                    +-- Save (JSONL to disk)
                    +-- Write to .derpdoc-live.bin (shared memory)
                         |
          +--------------+--------------+
          |              |              |
     DEV PIPELINE   BUILD PIPELINE   CI PIPELINE
     Shared memory   MSBuild target   Same as build
     (double-buf)         |              |
          |          derpdoc export  derpdoc export
     .derpdoc-       (full)          (full)
     live.bin             |              |
     (zero-copy)     *.g.cs +        *.g.cs +
          |          <GameName>.derpdoc    <GameName>.derpdoc
     Game reads           |              |
     active slot     dotnet build    dotnet publish
     next frame      (debug)         -c Release
          |                            (NativeAOT)
          |              |              |
     Instant value   Debug binary    Ship binary
     tweaking        + data          + data
```

---

## View System

Each table can have multiple saved views. A view is a **presentation config** over the same underlying data. Views persist to `{fileName}.views.json` and are fully undoable.

```csharp
public class DocView
{
    public string Id;
    public string Name;
    public DocViewType Type;
    public List<string>? VisibleColumnIds;     // Ordered column IDs to show (null = show all)
    public List<DocViewSort> Sorts;            // Sort rules
    public List<DocViewFilter> Filters;        // Filter rules (column + op + value)
    public string? GroupByColumnId;            // Board: Select column for lanes
    public string? CalendarDateColumnId;       // Calendar: text column with date values
}

public enum DocViewType : byte
{
    Grid,        // Spreadsheet grid (default)
    Board,       // Kanban (requires Select column for grouping)
    Calendar,    // Calendar (requires text column with date values)
}

public enum DocViewFilterOp : byte
{
    Equals, NotEquals, Contains, NotContains,
    GreaterThan, LessThan, IsEmpty, IsNotEmpty,
}
```

**Grid view** (default): Spreadsheet grid with filter/sort applied to rows, column visibility/ordering per view.

**Board view**: Kanban lanes grouped by a Select column. Drag-and-drop cards between lanes to change the cell value. Per-lane vertical scroll, horizontal scroll for many lanes.

**Calendar view**: Month grid with navigation bar (`[<] February 2026 [>] [Today]`). Rows placed on days based on a text column parsed as dates (`yyyy-MM-dd`, `MM/dd/yyyy`). Up to 3 cards per day with "+N more" overflow.

**Per-instance views**: Embedded table blocks in documents support per-instance views via `DocBlock.ViewId`. Each embedded instance can have its own display type, filters, sorts, and column visibility without affecting other instances or the table-level views. Inspector auto-creates per-block views when modifying embedded block display settings.

**View switcher bar**: Horizontal tab bar above table content (`[Grid view] [Board view] [Calendar] | [+]`). Click tab to switch, right-click for rename/duplicate/delete, `+` button creates new view. Tables with 0 views show implicit "All" tab.

---

## Automation System

Automations are C# scripts triggered by data events. Uses Roslyn scripting for runtime compilation.

```csharp
public class DocAutomation
{
    public string Id;
    public string Name;
    public DocAutomationTrigger Trigger;
    public string ScriptPath;               // Path to .cs file
    public bool Enabled;
}

public class DocAutomationTrigger
{
    public DocTriggerKind Kind;
    public string? TableId;                 // For row-based triggers
    public string? ColumnId;                // For column-change triggers
    public string? ColumnValue;             // Optional value match
    public string? CronExpression;          // For scheduled triggers
}

public enum DocTriggerKind : byte
{
    OnRowCreated,
    OnRowUpdated,
    OnRowDeleted,
    OnColumnChanged,       // Specific column + optional value match
    OnSchedule,            // Cron-like
    OnButtonPress,         // Manual trigger from UI
}
```

Example automation script:

```csharp
// on-task-complete.cs
public class OnTaskComplete : IDocAutomation
{
    public void Execute(DocAutomationContext ctx)
    {
        var row = ctx.TriggerRow;
        var assignee = row.Get<string>("Assignee");
        var taskName = row.Get<string>("Name");

        ctx.Log($"Task '{taskName}' completed by {assignee}");

        // Update a summary table
        var summaryTable = ctx.GetTable("CompletedSummary");
        summaryTable.AddRow(new {
            TaskName = taskName,
            CompletedBy = assignee,
            CompletedAt = DateTime.UtcNow,
        });
    }
}
```

---

## Build Phases

### Phase 1 - Project Skeleton + Table Engine ✓ COMPLETE

- ~~Derp.Doc project scaffold (following Derp.Ui pattern: Program.cs, Derp.DI, main loop)~~
- ~~DocTable with typed columns (Text, Number, Checkbox, Select)~~
- ~~AoS row storage + JSONL persistence (project.json + schema.json + rows.jsonl)~~
- ~~Table panel (spreadsheet grid)~~ — replaced `ImTable` with custom `SpreadsheetRenderer` (~650 lines)
- ~~Inline cell editing (click cell -> text input)~~ — seamless styling (NoBackground/NoBorder), dropdowns for Select columns
- ~~Add/remove rows and columns via UI~~ — via right-click context menus (Coda-style), not toolbar buttons
- ~~Project sidebar (tree of docs + tables using Im.Tree)~~ — with Font Awesome icons (`IconChar.Table`, `IconChar.Plus`)
- ~~Undo/redo command stream for cell edits~~

**What was actually built (beyond original plan):**

- **Custom SpreadsheetRenderer** (`Panels/SpreadsheetRenderer.cs`): Owns the full grid layout — draw, hit-test, and edit overlay all use the same `GetCellRect()` (single source of truth), eliminating the cell edit misalignment bug that ImTable had.
- **Multi-row selection**: Click row number area to select rows. Shift+click for range, Ctrl+click to toggle.
- **Cell range selection**: Click+drag for rectangular selection across cells. Shift+click to extend from anchor.
- **Right-click context menus**: Row context menu (insert/delete rows, clear cells), column header context menu (rename/delete column), empty area context menu (add row/column). Uses `ImContextMenu` API.
- **Row number column**: 24px column at left edge showing 1-based row index, serves as row-select hit area.
- **Row culling**: Only visible rows are drawn (based on scroll offset), for performance.
- **Scrollbar**: `ImScrollbar.DrawVertical` when content overflows body area.
- **Icon font loading**: `DocEditorApp` loads `fa-solid-900` via `SetSdfSecondaryFontAtlas` + `Im.SetFonts(font, iconFont)`.
- **Sidebar layout fix**: Moved "+New Table" button above the tree (was overlapping).
- **Engine: dropdown drop shadow**: Modified `RenderDeferredDropdown` in `Im.cs` to use `AddRoundedRectWithShadow`.
- **Engine: `Im.IsAnyDropdownOpen`**: New property exposing persistent `_openDropdownId != 0` state, fixing frame-ordering issue where `HandleInput` ran before `Im.Dropdown()` set `OverlayCaptureMouse`.

### Phase 2 - Document Editor ✓ COMPLETE

- ~~Block model (Heading, Paragraph, BulletList, Checkbox, CodeBlock, Quote, Divider)~~
- ~~RichText spans + rendering (bold, italic, code, strikethrough)~~
- ~~Markdown shortcuts while typing (**bold**, *italic*, # heading, - list)~~
- ~~Slash commands for block insertion (/ menu)~~
- ~~Selection toolbar (floating format buttons on text selection)~~
- ~~JSONL block persistence with fractional ordering~~

**What was built (beyond original plan):**

- **Embedded table blocks**: render the spreadsheet renderer inside documents as a block with content-hugging height and horizontal scroll.
- **`/table` command**: insert an embedded table block and either connect an existing table or create a new one (Coda-style).
- **Popover + modal input capture fixes**: prevent click-through (especially in embedded tables) and keep the intended popover interactive until dismissed.
- **Hit-testing + scrolling correctness**: document scroll offset is applied consistently for hover/click selection and embedded table interactions.
- **Per-embedded-instance UI state**: hover/selection/edit states do not leak across multiple embedded tables in the same document.

### Phase 3 - Formula Engine + Relations ✓ COMPLETE

- ~~New formula engine (lexer, parser, type checker, flattener, evaluator)~~
- ~~Core syntax: thisRow.Field, arithmetic, comparisons, conditionals~~
- ~~Table references: Tasks.Filter(@Status == "Done")~~
- ~~Collection methods: .Sum(), .Count(), .Average(), .First(), .Sort()~~
- ~~Lookup and aggregation shortcuts: Lookup(), CountIf(), SumIf()~~
- ~~String and date operations~~
- ~~IFormulaContext for table access during evaluation~~
- ~~Formula column type in DocTable~~
- ~~Relation column type (references rows in another table)~~
- ~~Cross-table dependency tracking + topological evaluation order~~

**What was built:**

- **Tables engine** (`Derp.Doc/Tables/DocFormulaEngine.cs`): full compile/evaluate pipeline with lexer, parser, type checker, flattener, evaluator, dependency extraction, and table/column topological ordering.
- **Formula syntax**: supports `thisRow.<Field>`, arithmetic/logical/comparison ops, ternary conditionals, `@Field` predicate variables, table refs, method chains (`Filter`, `Sort`, `Count`, `Sum`, `Average`, `First`), and built-ins (`Lookup`, `CountIf`, `SumIf`, `Upper`, `Lower`, `Contains`, `Concat`, `Date`, `Today`, `AddDays`, `DaysBetween`).
- **Context layer** (`IFormulaContext`, `ProjectFormulaContext`): resolves tables by name/id, columns by name, rows by id, and relation display labels.
- **Model/schema**: added `DocColumnKind.Formula` + `DocColumnKind.Relation` and column metadata (`FormulaExpression`, `RelationTableId`) with persistence in schema JSON.
- **Editor integration**: formula columns are read-only computed cells; relation columns edit via dropdown of target table rows; add-column dialog supports formula expressions and relation targets; column context menu supports formula editing.
- **Evaluation lifecycle**: formulas auto-recompute on execute/undo/redo/load via `DocWorkspace`, including cross-table recalculation ordering.
- **Chain-aware formula completion/docs**: completion now infers the receiver in `Table.Function.Chain` expressions and surfaces relevant members/keywords (including row-context fields after `First()`/row-returning chains).
- **Row index semantics (1-based)**: added `thisRowIndex`, `@rowIndex`, and `rowIndex` on row references to match table UI indexing and remove the need for explicit index columns.
- **Formula authoring UX polish**: inline token pills/icons, cursor-context documentation, and row-aware result context in the formula editor.
- **Verification**: added smoke tests and completion tests in `Derp.Doc.Tests` covering chain completions and row-index behavior.

### Phase 4 - Derived Tables ✓ COMPLETE

- **V1 model:** derived tables are **derived views** (not copied data) defined by a deterministic pipeline of steps (Append and/or Join).
- **Row identity:** derived tables have explicit `OutRowKey` rules so local editable columns do not drift.
  - Append: `(sourceTableId, sourceRowId)`
  - Base-driven Join: `baseRowId`
- **Join semantics (Phase 4 UI):** left-join only in UI for now, deterministic output row order (always base table order).
- **MultiMatch policy (Phase 4):** strict errors for `MultiMatch` (no silent “pick one”). Plan explicitly paves the path for an explicit resolver later.
- **Projected column edit policy (V1):** projected columns are read-only. Users can add local data columns and local formula columns that reference projected columns.
- **Inspector-first authoring UX:** right panel owns source list, join keys, source conditions, available columns, rename/order, and conflict handling.
- **Inline table UX:** header `+` supports `New local column`, `Add from source`, and `New formula column` for fast authoring.
- **Storage:** derived config lives on table config; local columns/cells persist keyed by `OutRowKey`; projected columns recompute from sources.
- **Rendering:** projected columns show source-origin indicators and read-only affordance.
- **Formula engine:** derived tables are queryable exactly like regular tables.

**Derived architecture contract (must be true before broad UX rollout):**

- **Table graph model:** treat all tables as nodes in a dependency DAG. Derived tables can depend on regular tables or other derived tables.
- **Acyclic guarantee:** source changes that introduce cycles are rejected at config time with explicit cycle paths.
- **Step strategy abstraction:** represent execution via step descriptors (`Append`, `Join`) rather than hardcoding “merge”.
- **Join kinds in schema now:** add schema support for `Left`, `Inner`, `FullOuter` even if only `Left` is enabled in Phase 4 UI.
- **Stable IDs, not names:** derived config references source table IDs and source column IDs so renames do not break joins.
- **Deterministic evaluation:** use topological order across derived dependencies and formula dependencies; recompute descendants on source mutations.
- **Row identity model:** support base-driven output keys (left joins), append output keys (`(tableId,rowId)`), and a forward-compatible plan for synthetic output keys (inner/full outer) without changing local storage semantics later.
- **Lineage/provenance:** each projected cell can resolve its source table, source row, source column, and hop path for diagnostics.
- **Missing/ambiguous states:** unify engine-level states (`NoMatch`, `MultiMatch`, `TypeMismatch`) so UI can render consistent diagnostics.
- **MultiMatch policy contract:** Phase 4 treats `MultiMatch` as a strict error; later we can add an explicit deterministic resolver per join step (aggregator) without changing the core pipeline model.
- **Write policy contract:** projected cells are read-only in Phase 4; editable behavior only for local/non-projected columns.

**Golden UX scenarios (Phase 4 acceptance examples):**

- **Inventory derived join happy-path:** Base `InventoryItems` + sources (`Weapons`, `Armor`, `Consumables`) with type-based join conditions. Add source columns via `+`; rows remain base-driven and sparse fields render empty where source does not apply.
- **All-items append happy-path:** Append(`Weapons`, `Armor`, `Consumables`) into `AllItems` with a `Kind`/`SourceLabel` column, then add local formula columns (for example `PowerScore`) over sparse projected fields.
- **Computed columns on projected data:** add local formula columns (for example `PowerScore`, `SellValue`) referencing projected columns; formula results update live; type mismatches show clear error state.
- **Naming/conflict flow:** adding same-named source columns auto-prefixes (for example `Weapons.Name`, `Armor.Name`) with inline rename available.
- **Diagnostics flow:** inspector exposes unresolved join keys (`No match`) and ambiguous joins (`Multiple matches`) with row counts and quick navigation.
- **Inline speed flow:** user can build most of a derived table from header `+` without leaving table, while inspector stays source-of-truth for schema details.

**V1 acceptance checklist:**

- ~~Create derived view from base table + multiple source tables (join-based derived view).~~
- ~~Create derived view by appending multiple source tables (append-based derived view).~~
- ~~Configure per-source join key mapping and optional source condition (join steps).~~
- ~~Pick source columns to expose and reorder/rename output columns.~~
- ~~Enforce read-only projected cells; allow editable local data columns and local formula columns.~~
- ~~Preserve deterministic output ordering and stable behavior after undo/redo.~~
- ~~Persist derived schema and round-trip cleanly through project storage.~~
- ~~Support derived tables in formula references, filters, sorting, and typeahead.~~

**What was built (high level):**

- **Model + storage:** `DocDerivedConfig` (base table, ordered steps, projections, suppressions) persisted and round-trips cleanly.
- **Execution engine:** derived tables materialize deterministically and surface diagnostics (`NoMatch`, `MultiMatch`, `TypeMismatch`) for UI and export.
- **Inspector authoring:** base table selection, join/append steps, key mapping, diagnostics, and a unified column list with visibility + reorder + rename.
- **Inline table UX:** projected columns are read-only, local columns remain editable; header `+` supports adding local columns and source projections.

**Out of scope for Phase 4 (defer):**

- Bi-directional writeback to source tables from merged cells.
- Non-left join modes enabled in UI (schema supports `Inner`/`FullOuter`, but UI keeps them disabled until engine/export behavior is locked).
- Many-to-many materialization policies.

**Join UX plan (Inspector + inline table):**

- **Entry point:** creating a "Derived view" opens the inspector in derived setup mode (base table first, then join steps).
- **Base table section:** choose base table, show row count, and warn when changing base would invalidate existing projections.
- **Join steps list:** ordered steps, each with source table picker, join kind selector, key mapping builder, and optional row filter condition.
- **Condition builder:** start with simple equality mapping (`Base.Col == Source.Col`), with optional advanced expression mode for complex predicates.
- **Source type guard (inventory case):** support per-step source condition (for example `ItemType == "Weapon"`) to keep sparse typed joins predictable.
- **Projection picker:** searchable grouped column picker per source table; selected projections appear as read-only output columns with origin badges.
- **Conflict handling UX:** auto-prefix duplicate names and show inline rename controls before applying.
- **Inline `+` fast path:** in-table `+` menu supports `Add from source...` and writes into the same projection schema as inspector edits.
- **Diagnostics panel:** show `NoMatch`, `MultiMatch`, and join-type-specific warnings with clickable row links.
- **Chain visibility:** when a join source is itself a derived view, inspector shows dependency breadcrumb and depth indicator.
- **Progressive enablement:** UI includes join kind control from day one, but non-left modes are disabled with "coming next" until engine validation is complete.

**Join authoring flow (builder-first, formula-advanced):**

1. Select **base table** (defines row set and ordering).
2. Add one or more **join steps** (source table, join kind, key mapping, optional condition).
3. Inspect **match diagnostics** (`Matched`, `NoMatch`, `MultiMatch`) before projecting columns.
4. Select **projection columns** via checkboxes per source column.
5. Rename/reorder output columns in inspector.
6. Use inline `+` as fast projection/formula/local-column entry points.
7. Render projected columns as **read-only**; local/formula columns remain editable.

```text
JOIN SETUP FLOW

Base table -> Join steps -> Match diagnostics -> Column projections -> Output table

┌────────────┐   ┌───────────────┐   ┌──────────────┐   ┌────────────────┐
│ Inventory  │-->| Weapons LEFT  |-->| Matched: 210 │-->| [x] Damage     │
│ Items      │   │ ON RefId=Id   |   │ NoMatch: 12  │   │ [x] Element    │
└────────────┘   └───────────────┘   │ Multi: 1     │   │ [ ] CritChance │
                    + Armor LEFT      └──────────────┘   └────────────────┘
```

```text
INSPECTOR LAYOUT (DERIVED VIEW SELECTED)

┌──────────────────────────────────────────────────────────────┐
│ Derived View: InventoryView                                 │
│ Base table: [ InventoryItems v ]   Rows: 2431   Synced ✓    │
├──────────────────────────────────────────────────────────────┤
│ Join Steps                                                   │
│ [1] LEFT  Source:[Weapons v]                                │
│     ON   InventoryItems.ItemRefId == Weapons.WeaponId       │
│     WHEN InventoryItems.ItemType == "Weapon"                │
│ [2] LEFT  Source:[Armor v]                                  │
│     ON   InventoryItems.ItemRefId == Armor.ArmorId          │
│     WHEN InventoryItems.ItemType == "Armor"                 │
│ [ + Add Join Step ]                                         │
├──────────────────────────────────────────────────────────────┤
│ Output Columns                                               │
│ [x] Name (base)                                              │
│ [x] Qty  (base)                                              │
│ [x] Weapons.Damage    rename:[Damage_____]   🔒              │
│ [x] Armor.Defense     rename:[Defense____]   🔒              │
│ [x] PowerScore        (local formula)                        │
├──────────────────────────────────────────────────────────────┤
│ Diagnostics: NoMatch 37 | MultiMatch 2 | TypeMismatch 0      │
└──────────────────────────────────────────────────────────────┘
```

```text
TABLE SURFACE (FAST PATH)

┌───────────────────────────────────────────────────────────────────────────┐
│ InventoryView                                                            │
│ Name         Qty   Damage🔒[Wpn]   Defense🔒[Arm]   PowerScore    +      │
├───────────────────────────────────────────────────────────────────────────┤
│ Iron Sword    1         42               .             84                │
│ Leather Vest  2          .              12             18                │
└───────────────────────────────────────────────────────────────────────────┘

"+" menu:
  - New local column
  - Add from source...
  - New formula column
```

**Future planning: derived export strategy (Phase 5 input):**

- **Editor storage model:** only base/source tables + derived schema persist to JSON/JSONL.
- **No persisted derived rows in source files:** derived rows are recomputed views.
- **Export materialization:** `derpdoc export` evaluates derived DAG and writes concrete output rows into baked binary.
- **Bake-time guarantees:** deterministic row order, resolved projections, formula-evaluated local columns, diagnostics surfaced as export warnings/errors.

```text
EDITOR STORAGE VS EXPORT MATERIALIZATION

Editor files:
  tables/*.schema.json
  tables/*.rows.jsonl
  derived spec (table config)
          │
          ▼
Export pipeline:
  load tables -> evaluate derived DAG -> project columns -> bake binary rows
```

**Proposed baked C# schema shape for derived outputs:**

- Generate derived output struct exactly like regular export tables (no runtime joins needed in game).
- Include only selected output columns (base + projections + local formula columns).
- Column origin metadata remains editor-only (optional debug export sidecar).

```csharp
public struct InventoryViewRow
{
    public FixedString32 Name;      // base
    public int Qty;                 // base
    public int Damage;              // projected from Weapons.Damage
    public int Defense;             // projected from Armor.Defense
    public int PowerScore;          // local formula column
}
```

```text
RUNTIME MODEL (BAKED)

Game does NOT execute joins at runtime.
Game reads precomputed rows:

InventoryViewRow[0] { Name="Iron Sword", Qty=1, Damage=42, Defense=0, PowerScore=84 }
InventoryViewRow[1] { Name="Leather Vest", Qty=2, Damage=0, Defense=12, PowerScore=18 }
```

### Phase 5 - Export Pipeline + Build Integration ✓ COMPLETE

- ~~Export table flag + config UI (type mappings, PKs, FKs; namespace fixed; struct derived from table name)~~
- ~~Table key metadata + UX (PK/unique constraints)~~
- ~~`derpdoc export` CLI tool (reads project, generates C# + binary)~~
- ~~C# struct codegen (from table schema + export config)~~
- ~~Enum codegen (from Select column options)~~
- ~~Formula baking (pre-compute all Formula values at export time)~~
- ~~Binary builder (Derp.Doc runtime format)~~
- ~~GameDataDb + GameDataBinaryLoader codegen~~
- ~~MSBuild integration targets (auto-export on build, incremental)~~
- ~~NativeAOT validation (ensure all generated code is AOT-safe)~~
- ~~Shared memory-mapped hot-reload (.derpdoc-live.bin with double buffering)~~
- ~~GameDataManager with LoadBaked (production) and ConnectLive (development) paths~~
- ~~DerpDoc.Runtime NuGet package with auto-discovery GameDatabase~~
- ~~DerpLib.DI integration for TestGame dependency injection~~

**Asset layout convention (Phase 5):**

- `Assets/` = hand-authored source content
- `Resources/` = compiled/runtime-ready outputs (safe to delete + regenerate)
- Derp.Doc export writes the baked database into the game’s `Resources/Database/` folder (for example `Resources/Database/<GameName>.derpdoc`),
  and MSBuild copies it next to the executable for runtime loading.
- Game project discovery, DB root, and export paths are defined in `_Docs/Derp.Doc/Derp.Doc.GameProjectLayout.md`.
- Export pipeline, determinism contract, and generated query API requirements are defined in `_Docs/Derp.Doc/Derp.Doc.ExportPipeline.md`.

**Keys (Phase 5):**

- Add schema support for:
  - Primary key (single-column in V1)
  - Secondary key (single-column in V1; additional unique lookup index)
  - Unique constraints (optional; can defer if not needed for first export)
- UX entry points:
  - Table view: right-click a column header and toggle `Set as key` (or `Set as primary key`).
  - Inspector: show and edit key state in the Columns section (same capability as table view).
- Validation:
  - Enforce uniqueness for key columns (diagnostic in editor, hard error on export).
  - Derived joins can use declared keys to suggest defaults and reduce MultiMatch surprises.

**Generated query APIs (Phase 5):**

- Generate lookup methods with feature parity to the legacy GameDoc query surface:
  - Primary key: `FindBy<Key>()` and `TryFindBy<Key>()` (O(1) index where applicable).
  - Secondary key (unique): `FindBy<Key>()` returning a single record.
  - Secondary key (non-unique): `FindBy<Key>()` returning a zero-allocation range view of records.
  - Primary key range query: `FindRangeBy<Key>(min, max)` returning a range view (O(log n + k)).
- Optional name registry:
  - If a table has a `Name`-like key, generate `TryGetId(name)` / `GetName(id)` helpers and an enum of known IDs (compile-time convenience).
  - See `_Docs/Derp.Doc/Derp.Doc.ExportPipeline.md` for performance rationale and implementation constraints.

### Phase 6 - View System ✓ COMPLETE

- ~~View model (`DocView`) with Grid/Board/Calendar types, filters, sorts, visible columns, groupBy, calendarDate~~
- ~~View persistence (`{fileName}.views.json`) with round-trip serialization~~
- ~~View commands (AddView, RemoveView, RenameView, UpdateViewConfig) with full undo/redo~~
- ~~View switcher tab bar (`ViewSwitcherBar`) above table content~~
- ~~SpreadsheetRenderer filter/sort integration (`ComputeViewRowIndices`, `_viewRowIndices`, `GetSourceRowIndex`)~~
- ~~Inspector view config sections (filters, sorts, column visibility, groupBy, calendarDate)~~
- ~~Board view (`BoardRenderer`): Kanban lanes grouped by Select column, drag-and-drop between lanes, per-lane scroll~~
- ~~Calendar view (`CalendarRenderer`): month grid with navigation, date parsing from text columns, row cards on days~~
- ~~Per-instance views: `DocBlock.ViewId` for embedded table blocks, `EnsurePerBlockView` auto-creates views~~
- ~~Embedded Board/Calendar rendering with title row, options button, clip rect~~
- ~~Column removal auto-cleans view references (filters/sorts/groupBy/calendarDate)~~
- ~~MCP view tools (view.list, view.create, view.update, view.delete)~~
- ~~MCP document/block tools (document.list, block.list, block.view.set, block.view.create)~~
- ~~Fix: `WantCaptureKeyboard` draw-order independence (init from `FocusId != 0` in BeginFrame)~~
- ~~Fix: pinned column right border always drawn (not just when scrolled)~~

**What was built:**

- **View model + storage:** `DocView.cs` with `VisibleColumnIds`, `Filters` (rule-based: column + op + value), `Sorts`, `GroupByColumnId`, `CalendarDateColumnId`. Stored in separate `{fileName}.views.json` files per table. Full round-trip through `DocJsonContext`, `ProjectSerializer`, `ProjectLoader`.
- **View commands:** `AddView`, `RemoveView`, `RenameView`, `UpdateViewConfig` (snapshot swap for batch changes = one undo step). All fully reversible.
- **SpreadsheetRenderer integration:** `_viewRowIndices` array maps display indices to source row indices via `ComputeViewRowIndices()`. Column visibility driven by `view.VisibleColumnIds`. Embedded tables use `_embeddedView` field to override `ActiveTableView`.
- **BoardRenderer:** Static class rendering Kanban lanes from Select column options. Cards show title (first Text column) + subtitle. Drag-and-drop flow: mouse-down → threshold (4px) → track target lane → draw ghost on overlay → mouse-up executes `SetCell` command. Per-lane vertical scroll + horizontal scroll for many lanes.
- **CalendarRenderer:** Static class rendering month grid (Mon-Sun, 5-6 week rows). Navigation bar with `<`/`>` arrows, month/year label, Today button. Date parsing from text column values (`yyyy-MM-dd`, `MM/dd/yyyy`, general parse). Day-to-row mapping cached with invalidation. Up to 3 cards per day with "+N more" overflow. Click card to select row.
- **ViewSwitcherBar:** Horizontal tab bar. Active tab has accent underline. Right-click for rename/duplicate/delete. Double-click for inline rename. `+` button with dropdown for Grid/Board/Calendar creation.
- **Inspector view config:** Filter section (column dropdown, operator dropdown, value input, add/remove). Sort section (column dropdown, asc/desc toggle, reorder). Column visibility checkboxes. GroupBy dropdown for Board. CalendarDate dropdown for Calendar. All changes create `UpdateViewConfig` commands with snapshot swap.
- **Per-instance views:** `DocBlock.ViewId` references a specific `DocView`. `InspectedBlockId` on workspace tracks which block opened inspector. `ApplyViewConfigChange` auto-creates per-block views via `EnsurePerBlockView` for embedded block contexts. `ResolveBlockView` in DocumentRenderer resolves block → view. Embedded Board/Calendar render with title row + options button + clip rect via `DrawEmbeddedTitleRow`.
- **MCP tools:** Enhanced `view.create`/`view.update` with `visibleColumnIds`, `filters`, `sorts`. `view.list` returns full detail. Added `document.list`, `block.list`, `block.view.set`, `block.view.create` for per-instance view management.
- **Engine fix:** `WantCaptureKeyboard` initialized from `FocusId != 0` in `ImContext.BeginFrame()` for draw-order independence (DocumentRenderer draws before InspectorPanel).

**Not yet implemented (deferred):**

- Automation scripting runtime (Roslyn)
- Automation trigger system (OnRowChanged, OnSchedule, OnButtonPress)
- Automation editor panel

**Polish progress (completed across phases):**

- ~~Column header hover/selection visuals and drag-reorder interactions~~
- ~~Column resize interactions in spreadsheet header~~
- ~~Inline column rename flow (single-click name activation + select-all for new columns)~~
- ~~Formula editor typeahead, token pills, and context description panel~~
- ~~Spreadsheet horizontal scrolling (wheel/trackpad) and reachability of trailing column UI (add-column slot)~~
- ~~Embedded table selection/edit isolation (no shared hover/selection between multiple embedded tables)~~
- ~~Inspector drag-handle gutters (hover-only handles; delete buttons only on hover)~~
- ~~Formula badge removed from column headers (was showing `</>` icon)~~
- ~~Pinned column (primary key) right border always visible~~

### Phase 7 - MCP Server ✓ COMPLETE

- ~~MCP server exposing Derp.Doc operations as tools~~
- ~~AI agents (Claude Code, etc.) can create and modify projects programmatically~~
- ~~See [MCP Server](#mcp-server-stretch-goal) section for full design~~

---

## MCP Server (Stretch Goal)

An MCP server that exposes Derp.Doc's data model as tools, enabling AI agents to create docs, tables, formulas, and trigger exports programmatically.

**Use case:** A designer tells Claude Code "create a weapons table with 20 fantasy weapons, add a DPS formula, merge weapons and armor into a shop table, and mark it for export" — Claude does it all via MCP tools.

**Status:** Implemented as `derpdoc mcp` (stdio-only). See `_Docs/Derp.Doc/Derp.Doc.McpServer.md` for usage and the current tool surface.

### Architecture

```
Claude Code (or any MCP client)
    |
    v  MCP protocol (stdio)
    |
Derp.Doc MCP Server (separate process or embedded in Derp.Doc app)
    |
    v  Reads/writes JSONL files directly
    |
MyProject.derpdoc/
├── tables/*.schema.json
├── tables/*.rows.jsonl
├── docs/*.blocks.jsonl
└── ...
    |
    v  File watcher picks up changes
    |
Derp.Doc UI (if open, live-updates)
```

The MCP server operates on the same JSONL files as the UI. Since storage is file-based, both can coexist — the UI watches for file changes and refreshes when the MCP server writes.

### Tools (29 total)

**Project (3):** `derpdoc.project.create`, `derpdoc.project.open`, `derpdoc.project.get`

**Tables (6):** `derpdoc.table.list`, `derpdoc.table.create`, `derpdoc.table.delete`, `derpdoc.table.schema.get`, `derpdoc.table.export.set`, `derpdoc.table.keys.set`

**Columns (3):** `derpdoc.column.add`, `derpdoc.column.update`, `derpdoc.column.delete`

**Rows (6):** `derpdoc.row.add`, `derpdoc.row.add.batch`, `derpdoc.row.update`, `derpdoc.row.update.batch`, `derpdoc.row.delete`, `derpdoc.row.delete.batch`

**Query (2):** `derpdoc.table.query`, `derpdoc.table.query.batch`

**Export (1):** `derpdoc.export`

**Views (4):** `derpdoc.view.list`, `derpdoc.view.create`, `derpdoc.view.update`, `derpdoc.view.delete`
- `view.create`/`view.update` support `visibleColumnIds`, `filters` (columnId + op + value), `sorts` (columnId + descending), `groupByColumnId`, `calendarDateColumnId`
- `view.list` returns full detail including filters, sorts, and visible columns

**Documents (1):** `derpdoc.document.list`

**Blocks (3):** `derpdoc.block.list`, `derpdoc.block.view.set`, `derpdoc.block.view.create`
- `block.list` shows block type, tableId, viewId, and text preview per block
- `block.view.set` assigns a viewId to an embedded table block for per-instance views
- `block.view.create` creates a new view on the table AND assigns it to a block in one step (with full config)
- `FindDocument` matches by id, title, or fileName for convenience

**Not yet implemented:**

- Derived table operations (append/join DAG and projections)
- Formula validate/evaluate endpoints

### Resources

Not implemented yet (V1 is tools-only; no MCP resources).

### Example MCP Session

```
User: "Create a weapons table with Name, Damage, Speed, Element,
       and a DPS formula. Add 5 fantasy weapons."

Claude Code calls:
  0. MCP handshake: initialize -> notifications/initialized
  1. derpdoc.table.create(name: "Weapons")
  2. derpdoc.column.add(kind: "Text"|"Number"|"Select"|"Formula", ...)
  3. derpdoc.row.add(...) (repeat per weapon)
  4. derpdoc.table.keys.set(...) + derpdoc.table.export.set(...)
  5. derpdoc.export(...)

Result: Weapons table created, populated, formula working,
        export configured. Designer sees it all in Derp.Doc UI.
```

### Implementation Notes

- The MCP server shares the `Model/` and `Storage/` layers with the Derp.Doc UI — same serialization, same validation.
- Implemented as a CLI subcommand: `derpdoc mcp` (no ImGUI/Engine dependency).
- File locking: MCP server and UI both write JSONL. Use file-level locks or atomic writes (write to temp, rename) to prevent corruption.
- The MCP server does NOT need ImGUI or the Engine — it's headless, just data operations.

---

## Design Decisions Log

| Decision | Choice | Rationale |
|---|---|---|
| Formula engine | Independent from Derp.Ecs DSL | Different type system, collection ops, evaluation model |
| Table storage | AoS (one object per row) | Clean JSONL serialization; build column indexes at runtime for query perf |
| File format | JSONL (one line per row/block) | Git auto-merge for concurrent edits to different rows |
| Document ordering | Fractional indexing | Per-block ordering merges cleanly vs central order array |
| Table taxonomy | Single DocTable type with optional flags | Simpler than inheritance; any table can be merge and/or exported |
| Export format | Derp.Doc-native binary loader/runtime | Zero-copy, NativeAOT safe, O(1) key lookups, tailored to Derp.Doc export guarantees |
| GameDoc relationship | Full replacement for future games | Single source of truth, designers self-serve, less code to maintain |
| Build integration | MSBuild pre-build target | Automated, incremental, works with CI and NativeAOT publish |
| Hot-reload | Shared memory-mapped file with double buffering | Zero-copy, zero-latency, no torn reads; NativeAOT safe (data-only reload) |
| Rich text editing | Markdown shortcuts + selection toolbar | Both modes for different user preferences |
| Automation scripts | C# via Roslyn | Consistent with codebase, full language power, typed access to tables |
