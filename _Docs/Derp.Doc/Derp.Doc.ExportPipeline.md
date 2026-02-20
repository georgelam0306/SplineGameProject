# Derp.Doc Export Pipeline (Phase 5) Proposal

This document defines the runtime export contract for Derp.Doc: how editor data becomes compiled outputs for a game.

## Goals

- Runtime does not parse editor JSON/JSONL, does not evaluate formulas, and does not execute joins.
- Export output is deterministic given identical inputs.
- Generated APIs are allocation-free on hot paths and AOT-safe.
- Export is anchored to a game root discovered via `derpgame`.

See `_Docs/Derp.Doc/Derp.Doc.GameProjectLayout.md` for game root discovery and on-disk layout.

## Inputs and Outputs

### Inputs (editor source-of-truth)

From `DbRoot = <GameRoot>/Database`:

- `project.json`
- `tables/*.schema.json`
- `tables/*.rows.jsonl` (non-derived tables)
- `tables/*.own-data.jsonl` (derived tables: local/non-projected cell data)
- Variants: see `_Docs/Derp.Doc/Derp.Doc.TableVariantsAndSchemaLinkedTablesSpec.md`
- `docs/*` (optional; usually not exported to runtime in V1)

### Outputs (compiled/runtime-ready)

To `ResourcesRoot = <GameRoot>/Resources`:

```text
Resources/Database/<GameName>.derpdoc   (compiled database container)
```

Optional sidecars:

```text
Resources/Database/<GameName>.derpdoc.manifest.json
Resources/Database/<GameName>.derpdoc.debug.json
```

## High-Level Pipeline

```text
LOAD -> VALIDATE -> COMPUTE -> MATERIALIZE -> WRITE
```

1. Load JSON + JSONL into an in-memory model.
2. Validate schema and dependency graphs.
3. Compute all derived tables (materialize) in a topological order.
4. Bake formula columns (including formulas on derived outputs).
5. Write compiled container + generated code (if codegen is part of export).

Variants:

- Export is variant-aware and emits engine-readable outputs for all project variants.
- See `_Docs/Derp.Doc/Derp.Doc.TableVariantsAndSchemaLinkedTablesSpec.md` for the materialization/baking rules.

## Determinism Contract

Export must guarantee:

- Stable derived evaluation order (topological over derived table dependencies).
- Stable formula evaluation order (topological over formula dependencies).
- Stable row ordering in output tables:
  - Non-derived: schema row order.
  - Derived join pipeline: base table order for left joins; filtered base order for inner joins.
  - Derived append pipeline: step order, then source row order.
- Stable column ordering in output tables (as configured + persisted).
- Stable behavior across variants (per-variant determinism): row order, derived materialization, and formula baking are deterministic for each `VariantId`.

Errors must be explicit and deterministic (no runtime fallbacks, no silent multi-match selection).

## StringHandle ID Stability

`StringHandle` values in exported binaries use **stable 32-bit IDs derived from the string content** (not per-export sequential IDs). This enables hot-reload and repeated loads without ID reuse causing incorrect string lookups.

## Keys, Indexes, and Generated Query APIs

Phase 5 adds explicit per-table key metadata to power export validation and runtime queries:

- Primary key (single-column in V1)
- Secondary keys (single-column in V1; can be unique or non-unique)

### Validation

- Primary and unique secondary keys must be unique (editor diagnostics; export is a hard error).
- Joins should prefer declared keys to reduce MultiMatch surprises.

### Generated query APIs (legacy feature parity)

Generate runtime accessors that preserve the same lookup capabilities as the legacy system:

- Primary key:
  - `FindBy<Key>(...)` / `TryFindBy<Key>(...)`
  - Optional overloads for `int`, `enum`, and an `Id` wrapper type
- Secondary key (unique):
  - `FindBy<Key>(...)` returning a single record
- Secondary key (non-unique):
  - `FindBy<Key>(...)` returning a zero-allocation range view over matching records
- Primary-key range query:
  - `FindRangeBy<Key>(min, max)` returning a range view (`O(log n + k)`)
- Optional name registry:
  - `TryGetId(string name)` / `GetName(id)` helpers and a generated enum of known IDs when appropriate

### Performance notes (why this is fast)

The Derp.Doc runtime query model is fast because:

- Prefer O(1) slot arrays for integer/enum keys over dictionaries (cache-friendly, allocation-free).
- Return `ReadOnlySpan<T>` (or a `RangeView<T>` wrapper) over contiguous memory for iteration (no per-query allocations).
- Provide a cached `TableAccessor<T>` (or equivalent) so tight loops avoid repeated table-name dictionary lookups.
- Keep APIs AOT-safe and avoid virtual/interface dispatch in hot paths.

## Derived Tables at Export Time

Derived tables are materialized during export:

- Projected columns are computed from sources (read-only).
- Local (non-projected) columns are merged from `own-data.jsonl` keyed by derived row identity.
- Local formula columns are baked like any other formula column.

Join policies:

- Left join: preserve base row set; unmatched source columns are empty; `NoMatch` is tracked for diagnostics.
- Inner join: filter base rows when `NoMatch` occurs at that join step.
- MultiMatch: strict error in Phase 5 (pave path for explicit deterministic resolvers later).

Full outer join is deferred until row-identity and synthetic key policy is locked down.

## Build Integration

Export should be invokable in three ways:

1. `derpdoc export <GameRoot>`
2. MSBuild target (Release/SteamRelease) that runs export and copies the compiled container to the output directory.
3. Dev hot reload loop: file watcher triggers export and runtime reload (later: double-buffered live file).

## Test Plan

Tests live in `Derp.Doc.Tests/Phase5ExportTests.cs` (and related files as needed). All tests are headless — no ImGUI, no Engine, no rendering. They exercise the export pipeline from in-memory model through binary output and back through the loader.

### Test Infrastructure

**Test fixture pattern:** Follow the Phase 3/4 pattern — build `DocProject` + `DocTable` + `DocColumn` + `DocRow` objects in-memory, run the pipeline, assert on outputs. Use temp directories for round-trip tests, cleaned up in `finally` blocks.

**Shared test helpers:**

- `TestProjectBuilder` — fluent builder for quickly assembling multi-table projects with rows, formulas, derived configs, and export configs.
- `ExportPipelineRunner` — wraps the full LOAD → VALIDATE → COMPUTE → MATERIALIZE → WRITE pipeline in a single call, returning the binary output + generated source strings + diagnostics.
- `BinaryRoundTripHelper` — writes binary output to disk, loads it back via `GameDataBinaryLoader`, and returns the typed accessor for assertion.

---

### 1. Schema and Export Config

| # | Test | Description |
|---|------|-------------|
| 1.1 | `ExportConfig_RoundTrips_ThroughProjectStorage` | Create a table with `DocExportConfig` (enabled flag, PK, column mappings, FKs). Save to disk, reload, assert config survives. |
| 1.2 | `ExportConfig_UsesFixedNamespace` | Generated source uses `namespace DerpDocDatabase;` (namespace is not user-configurable in Phase 5). |
| 1.3 | `ExportConfig_DerivesStructNameFromTableName` | Exported row struct name defaults from table name (sanitized PascalCase). |
| 1.5 | `ExportConfig_Validates_UnmappedColumns` | Columns with no export mapping are silently excluded (not an error). Verify they don't appear in output. |
| 1.6 | `ExportConfig_Validates_InvalidExportType` | Column mapping with an unrecognized export type (e.g., `"foo"`) produces a validation error. |
| 1.7 | `ExportConfig_Validates_PKColumnMissing` | PK column ID that doesn't exist on the table produces a validation error. |

### 2. Key System

| # | Test | Description |
|---|------|-------------|
| 2.1 | `PrimaryKey_Unique_EnforcedAtExport` | Table with PK column, two rows with duplicate PK values → export hard error. |
| 2.2 | `PrimaryKey_Unique_PassesWhenAllDistinct` | Table with unique PK values → export succeeds. |
| 2.3 | `SecondaryKey_Unique_EnforcedAtExport` | Secondary key marked unique, duplicate values → export hard error. |
| 2.4 | `SecondaryKey_NonUnique_AllowsDuplicates` | Secondary key NOT marked unique, duplicate values → export succeeds. |
| 2.5 | `PrimaryKey_IntType_GeneratesSlotArray` | Table with integer PK → binary output contains a slot array, and `FindById(id)` resolves to the correct row. |
| 2.6 | `PrimaryKey_EnumType_GeneratesSlotArray` | Table with Select PK (exported as enum) → slot array works via enum cast to int. |
| 2.7 | `SecondaryKey_NonUnique_GeneratesSortedIndex` | Secondary key (non-unique) → binary output contains a sorted index, and `FindBy<Key>(value)` returns a correct range view of matching rows. |
| 2.8 | `PrimaryKey_RangeQuery_ReturnsCorrectSlice` | `FindRangeBy<Key>(min, max)` returns exactly the rows in `[min, max]`, in order. |

### 3. Column Type Mapping

| # | Test | Description |
|---|------|-------------|
| 3.1 | `Export_TextColumn_AsStringHandle` | Text column exported as `StringHandle` → value round-trips through string registry. |
| 3.2 | `Export_NumberColumn_AsInt` | Number column with `"int"` mapping → truncates to int, round-trips correctly. |
| 3.3 | `Export_NumberColumn_AsFloat` | Number column with `"float"` mapping → round-trips correctly. |
| 3.4 | `Export_NumberColumn_AsFixed64` | Number column with `"Fixed64"` mapping → round-trips correctly (use `Fixed64.FromDouble`). |
| 3.5 | `Export_CheckboxColumn_AsByte` | Checkbox column → exported as byte (0/1), round-trips. |
| 3.6 | `Export_SelectColumn_AsEnum` | Select column with 3 options → generates enum with correct members, row values map to correct enum value. |
| 3.7 | `Export_MultiSelectColumn_AsFlagsEnum` | MultiSelect column → generates `[Flags]` enum, row values encode correct flag combinations. |
| 3.8 | `Export_DateColumn_AsUnixDays` | Date column → exported as `int` (unix days since epoch), round-trips. |
| 3.9 | `Export_RelationColumn_AsForeignKey` | Relation column → exported as `int` (PK of target table), resolves correctly. |
| 3.10 | `Export_EditorOnlyColumns_Excluded` | `CreatedTime`, `LastEditedTime`, `LastEditedBy` columns are not present in exported struct. |
| 3.11 | `Export_FormulaColumn_BakedValue` | Formula column → result is pre-computed at export time. Verify the baked value matches the formula engine's evaluation. |

### 4. Formula Baking

| # | Test | Description |
|---|------|-------------|
| 4.1 | `FormulaBaking_IntraTable_CorrectValues` | Table with `Price * Quantity` formula → baked values match per-row computation. |
| 4.2 | `FormulaBaking_CrossTable_CorrectValues` | Formula referencing another table (`Orders.Sum(@Total)`) → baked value matches runtime evaluation. |
| 4.3 | `FormulaBaking_DependencyOrder_TopologicallyCorrect` | Formula A depends on formula B → B is baked first, A uses B's baked result. |
| 4.4 | `FormulaBaking_ErrorFormula_ProducesExportError` | Formula with a type error or circular dependency → export produces a hard error (not a silent zero). |
| 4.5 | `FormulaBaking_OnDerivedTable_UsesProjectedValues` | Local formula column on a derived table references projected columns → baked value is correct. |

### 5. Derived Table Materialization

| # | Test | Description |
|---|------|-------------|
| 5.1 | `DerivedExport_JoinLeft_MaterializesAllBaseRows` | Left join derived table → export output has one row per base row, unmatched projected columns are default/zero. |
| 5.2 | `DerivedExport_JoinInner_FiltersUnmatchedRows` | Inner join derived table → export output excludes base rows with `NoMatch`. |
| 5.3 | `DerivedExport_Append_ConcatenatesSourceRows` | Append derived table → export output has `sum(source row counts)` rows in step-then-source order. |
| 5.4 | `DerivedExport_MixedPipeline_AppendThenJoin` | Append + Join pipeline → rows are appended first, then join enriches all rows. Verify count and projected values. |
| 5.5 | `DerivedExport_MultiMatch_IsHardError` | Join step with multi-match key → export fails with explicit error (not silent pick). |
| 5.6 | `DerivedExport_LocalColumns_MergedFromOwnData` | Derived table with local (non-projected) column + `own-data.jsonl` values → local values appear in baked output keyed by `OutRowKey`. |
| 5.7 | `DerivedExport_ChainedDerived_EvaluatesInTopologicalOrder` | Derived table B depends on derived table A → A is materialized first, B sees A's output. |
| 5.8 | `DerivedExport_CyclicDependency_IsHardError` | Derived A depends on derived B depends on A → export fails with cycle path in error message. |
| 5.9 | `DerivedExport_RowOrder_Stable` | Run export twice on the same input → output bytes are identical (deterministic ordering). |
| 5.10 | `DerivedExport_ProjectedAndLocal_ColumnOrder_MatchesConfig` | Output column order matches the projection + local column ordering from derived config. |
| 5.11 | `DerivedExport_WithFK_ResolvesToSourcePK` | Derived table that is also exported has a Relation column pointing to another exported table. After materialization, the FK column in the baked output resolves to the correct PK of the target table (FK resolution works through the derived+export path, not just regular tables). |

### 6. Binary Format

| # | Test | Description |
|---|------|-------------|
| 6.1 | `Binary_RoundTrip_SingleTable` | Single table with 3 rows → write binary, load via `GameDataBinaryLoader`, iterate all rows, assert field values. |
| 6.2 | `Binary_RoundTrip_MultiTable` | 3 tables (regular + derived + FK-linked) → write binary, load, verify all tables are accessible and cross-table FK lookups resolve. |
| 6.3 | `Binary_Header_MagicAndVersion` | Verify the header contains correct magic bytes and version number. |
| 6.4 | `Binary_Checksum_DetectsCorruption` | Flip a byte in the binary output → loader rejects with checksum error. |
| 6.5 | `Binary_Alignment_16Byte` | Each table data section starts at a 16-byte aligned offset. |
| 6.6 | `Binary_StringRegistry_SharedAcrossTables` | Two tables with the same string value → only one entry in string registry. `StringHandle` equality holds across tables. |
| 6.10 | `Binary_StringHandle_StableIdsAcrossReExports` | Export a project, record all StringHandle IDs. Add a new row with a new string value, re-export. Assert: all previously-existing StringHandle IDs are unchanged (IDs are content-derived, not sequential). The new string gets a new stable ID. |
| 6.7 | `Binary_EmptyTable_RoundTrips` | Table with 0 rows → binary still contains the table directory entry, loads successfully with `Count == 0`. |
| 6.8 | `Binary_SlotArray_O1Lookup` | Table with int PK and 1000 rows → `FindById(id)` returns the correct row for every id. Verify no linear scan (slot array offset is non-zero in directory). |
| 6.9 | `Binary_LargeTable_Stress` | Table with 10,000 rows, 8 columns → write + load completes without error, spot-check 100 random rows. |

### 7. Codegen (C# Source Generation)

| # | Test | Description |
|---|------|-------------|
| 7.1 | `Codegen_Struct_HasCorrectFields` | Generated struct source contains all mapped fields with correct C# types, in column order. |
| 7.2 | `Codegen_Struct_HasStructLayout` | Generated struct has `[StructLayout(LayoutKind.Sequential)]` attribute. |
| 7.3 | `Codegen_Enum_FromSelectColumn` | Select column with `["Fire","Ice","Lightning"]` → generates `enum` with those members. |
| 7.4 | `Codegen_FlagsEnum_FromMultiSelect` | MultiSelect column → generates `[Flags] enum` with power-of-2 values. |
| 7.14 | `Codegen_Enum_SanitizesInvalidIdentifiers` | Select column with options containing spaces, special chars, and leading digits (e.g., `"High Priority"`, `"N/A"`, `"3rd Place"`) → generated enum members are valid C# identifiers (e.g., `HighPriority`, `NA`, `_3rdPlace`). Verify the generated source compiles via Roslyn. |
| 7.5 | `Codegen_GameDataDb_ContainsAllTables` | Generated `GameDataDb` struct has a typed table property for every exported table. |
| 7.6 | `Codegen_FindById_Generated` | Table with PK → generated table accessor has `FindById()` / `TryFindById()` methods. |
| 7.7 | `Codegen_FindBySecondaryKey_Generated` | Table with secondary key → generated accessor has `FindBy<Key>()` method. |
| 7.8 | `Codegen_RangeQuery_Generated` | Table with PK → generated accessor has `FindRangeBy<Key>(min, max)` method. |
| 7.9 | `Codegen_NameRegistry_Generated` | Table with a Name-like key column → generated `TryGetId(name)` / `GetName(id)` and enum of known IDs. |
| 7.10 | `Codegen_Namespace_Fixed` | Generated source uses `namespace DerpDocDatabase;` (not user-configurable in Phase 5). |
| 7.11 | `Codegen_ForeignKey_IntField` | Relation column → generated struct field is `int` (FK), not a relation/string. |
| 7.12 | `Codegen_DerivedTable_LooksLikeRegular` | Derived table struct looks identical to a regular table struct (no runtime join artifacts). |
| 7.13 | `Codegen_Compiles_WithRoslyn` | Feed all generated `.g.cs` sources + runtime assembly references into Roslyn in-memory compilation → no errors. This is the ultimate integration gate. |

### 8. Determinism

| # | Test | Description |
|---|------|-------------|
| 8.1 | `Export_Deterministic_IdenticalInputProducesIdenticalOutput` | Run full export twice on the same project → output binary bytes are `SequenceEqual`. |
| 8.2 | `Export_Deterministic_RowOrderStable` | Reorder source table rows in memory, re-export → output rows are in schema-defined order (not insertion order). |
| 8.3 | `Export_Deterministic_ColumnOrderStable` | Reorder column mappings in export config, re-export → output fields are in the configured order. |
| 8.4 | `Export_Deterministic_DerivedEvalOrder` | Project with 3 derived tables forming a chain → evaluation order is always topological. Verified by checking that intermediate values are identical across runs. |
| 8.5 | `Export_Deterministic_FormulaEvalOrder` | Two formula columns where B depends on A → A is always baked before B, result is consistent. |
| 8.6 | `Export_Deterministic_CultureInvariant` | Set `CultureInfo.CurrentCulture` to `fr-FR` (comma decimal), run export → output is byte-identical to export under `en-US`. |

### 9. Validation and Error Reporting

| # | Test | Description |
|---|------|-------------|
| 9.1 | `Validate_MissingBaseTable_ForDerived` | Derived table references a base table ID that doesn't exist → hard error with table name. |
| 9.2 | `Validate_MissingSourceTable_ForJoinStep` | Join step references a source table ID that doesn't exist → hard error. |
| 9.3 | `Validate_MissingFK_TargetTable` | FK references a target table that isn't exported → hard error. |
| 9.4 | `Validate_MissingFK_TargetPK` | FK references a target PK column that doesn't exist → hard error. |
| 9.5 | `Validate_CyclicDerived_ReportsPath` | Cycle in derived dependencies → error message includes the full cycle path (e.g., `A -> B -> C -> A`). |
| 9.6 | `Validate_CyclicFormula_ReportsPath` | Circular formula dependency → error message includes the cycle path. |
| 9.7 | `Validate_MultiMatch_ReportsRowCounts` | Join multi-match → error includes the join step, source table, and count of ambiguous rows. |
| 9.8 | `Validate_AllErrorsCollected_NotFirstOnly` | Project with 3 independent errors → all 3 are reported (not fail-fast on first). |

### 10. Build Integration

| # | Test | Description |
|---|------|-------------|
| 10.1 | `CLI_ExportCommand_ProducesOutputFiles` | Invoke `derpdoc export <GameRoot>` on a temp game root with `derpgame` marker + populated `Database/` → verify `Resources/Database/<GameName>.derpdoc` exists and is valid binary. |
| 10.2 | `CLI_ExportCommand_WritesManifest` | Export produces `.derpdoc.manifest.json` sidecar with table list, row counts, and column schemas. |
| 10.3 | `CLI_ExportCommand_InvalidGameRoot_FailsCleanly` | Invoke on a directory without `derpgame` → non-zero exit code + clear error message. |
| 10.4 | `CLI_ExportCommand_ValidationErrors_NonZeroExit` | Project with validation errors → non-zero exit code, errors on stderr. |
| 10.5 | `CLI_ExportCommand_Incremental_SkipsWhenUpToDate` | Run export twice without source changes → second run is a no-op (output file timestamp unchanged). |
| 10.6 | `GameRootDiscovery_WalksUpToMarker` | Given a path deep inside `GameRoot/Database/tables/`, discovery correctly finds the `derpgame` marker and returns the game root. |
| 10.7 | `GameRootDiscovery_StandaloneDbRoot_Fallback` | Directory with `project.json` + `tables/` but no `derpgame` → opens in standalone mode (Mode B). |

### 11. Hot-Reload (Shared Memory)

**Basics:**

| # | Test | Description |
|---|------|-------------|
| 11.1 | `SharedMemory_Header_MagicAndLayout` | Create shared memory file → header has correct magic (`"DDLV"`), two slot offsets, and valid slot size. |
| 11.2 | `SharedMemory_WriteRead_RoundTrip` | Write table data to inactive slot, flip active slot, read from active slot → data matches. |
| 11.3 | `SharedMemory_Generation_BumpsOnWrite` | Each write increments generation counter. Reader detects change via generation comparison. |
| 11.4 | `SharedMemory_NoChange_SingleUintRead` | When generation unchanged, reader does zero additional work (verified by asserting db reference identity is unchanged — same object ref). |
| 11.5 | `SharedMemory_SchemaChange_RegeneratesFile` | After a column add (schema change), shared memory file is regenerated with new slot layout and the old file is replaced. |

**No torn reads:**

While the writer is exporting, the reader must either see the old slot or the new slot — never half-written data.

| # | Test | Description |
|---|------|-------------|
| 11.6 | `SharedMemory_NoTornReads_ConcurrentWriterReader` | Writer thread continuously writes increasing row values to the inactive slot, flips, and repeats. Reader thread polls in a tight loop, loading the active slot and verifying an embedded per-slot checksum (CRC32 over the slot data). Test runs for N iterations (e.g., 10,000). Failure = any read where the checksum doesn't match (indicates partial write was visible). |
| 11.7 | `SharedMemory_NoTornReads_LargePayload` | Same as 11.6 but with a large table (10,000 rows) so the write is non-trivial in duration. Exercises the case where a flip could happen mid-read. |
| 11.8 | `SharedMemory_NoTornReads_ReaderMidWrite_SeesOldSlot` | Writer begins writing to inactive slot (simulated slow write via artificial delay between row writes). Reader reads during the write. Assert reader sees the previous slot's complete, consistent data — not the in-progress slot. |

**Atomic switch correctness:**

`ActiveSlot` flip + `Generation` bump must result in a consistent view — the header must point at a fully-written slot.

| # | Test | Description |
|---|------|-------------|
| 11.9 | `SharedMemory_AtomicSwitch_SlotFullyWrittenBeforeFlip` | Instrument the writer to record timestamps: (1) slot write complete, (2) generation bump, (3) active slot flip. Assert (1) < (2) ≤ (3). Reader should never observe a new generation that points to an incomplete slot. |
| 11.10 | `SharedMemory_AtomicSwitch_GenerationAndSlotAgree` | Reader polls and on each generation change, reads `ActiveSlot` and verifies the slot's embedded sequence number matches the generation. If they disagree, the header was inconsistent (caught by per-slot sequence stamp written by the writer). |
| 11.11 | `SharedMemory_AtomicSwitch_RapidFlips` | Writer performs 1,000 rapid flip cycles (write → flip → write → flip). Reader asserts that every observed generation change points to a valid, checksummed slot. No skipped or inconsistent states. |

**Lifetime contract (stale reference safety):**

After `Generation` changes, any previously-held spans/refs into the old slot must be treated as invalid. The writer will overwrite the old slot on its next cycle.

| # | Test | Description |
|---|------|-------------|
| 11.12 | `SharedMemory_Lifetime_OldSlotInvalidatedAfterFlip` | Reader loads db from slot 0 (generation G). Writer writes to slot 1, flips to slot 1 (generation G+1). Writer then writes to slot 0 (the old active slot), flips to slot 0 (generation G+2). Assert: the data captured at generation G is no longer valid — its backing memory has been overwritten. Verify by comparing the old captured bytes to the current slot 0 bytes (they differ). |
| 11.13 | `SharedMemory_Lifetime_ReaderMustReloadOnGenerationChange` | Simulate a game reader that caches a `ReadOnlySpan<T>` over slot data. After two flips (active → inactive → active), the span's backing memory has been overwritten. Assert the reader's `Update()` method re-creates the span/db reference on every generation change, never reusing a stale reference. |
| 11.14 | `SharedMemory_Lifetime_MultiFlip_NeverReadsBeingWrittenSlot` | Reader and writer on separate threads. Writer performs 1,000 flip cycles. After each reader `Update()`, record which slot was read. After each writer begin-write, record which slot is being written. Assert: at no point did the reader read from the slot that the writer was actively writing to. Verified by cross-referencing the two logs with timestamps. |

**Double-buffer overwrite safety (ping-pong discipline):**

The writer must never overwrite the slot the game is currently reading.

| # | Test | Description |
|---|------|-------------|
| 11.15 | `SharedMemory_PingPong_WriterAlwaysTargetsInactiveSlot` | Writer performs 100 write cycles. Before each write, assert `writeTargetSlot == 1 - header.ActiveSlot`. After flip, assert `header.ActiveSlot == writeTargetSlot`. The writer never touches the slot indicated by `header.ActiveSlot` before the flip. |
| 11.16 | `SharedMemory_PingPong_ConcurrentReadWrite_SlotIsolation` | Writer and reader on separate threads. Writer writes a known pattern (e.g., all bytes = generation number) to the inactive slot. Reader continuously reads the active slot and verifies its pattern matches the previously-flipped generation (not the in-progress one). Runs 5,000 iterations. |
| 11.17 | `SharedMemory_PingPong_ReaderHoldsOldSlot_WriterCannotOverwrite` | Reader reads from slot 0 and holds a reference (simulating a slow game frame). Writer writes to slot 1 and flips. Writer must now write to slot 0 — but the reader is still "using" it. This test verifies the contract: the writer's next write targets the now-inactive slot (slot 0) and the reader must have already moved on (called `Update()`) before the writer begins. If the reader hasn't called `Update()`, the writer must wait or the reader accepts that stale reads may occur after two flips (documented contract). |

**GameDataManager dual-mode:**

| # | Test | Description |
|---|------|-------------|
| 11.18 | `GameDataManager_LoadBaked_ReturnsCorrectData` | Create a baked `.derpdoc` binary on disk. Call `GameDataManager.LoadBaked(path)`. Assert `Db` returns the correct tables and rows. Verify `Update()` is a no-op (no shared memory, no generation changes). |
| 11.19 | `GameDataManager_ConnectLive_ReadsSharedMemory` | Create a shared memory file with initial data. Call `GameDataManager.ConnectLive(projectDir)`. Assert `Db` reflects the shared memory contents. Write new data to the inactive slot and flip. Call `Update()`. Assert `Db` now reflects the updated data. |
| 11.20 | `GameDataManager_ConnectLive_FallsBackToBaked_WhenNoLiveFile` | Call `ConnectLive` on a project dir that has no `.derpdoc-live.bin`. Assert it either falls back gracefully (loads baked if available) or returns a clear error — does not crash. |
| 11.21 | `GameDataManager_Dispose_CleansUpMappedFile` | Call `ConnectLive`, then `Dispose()`. Assert the `MemoryMappedFile` and `ViewAccessor` are released (subsequent reads throw `ObjectDisposedException`). |

### 12. End-to-End Integration

| # | Test | Description |
|---|------|-------------|
| 12.1 | `E2E_GameProject_FullPipeline` | Create a game root with `derpgame`, populate `Database/` with 3 tables (regular, derived join, derived append), formulas, export configs, PK + FK. Run full export. Load binary via generated loader. Assert: correct row counts, FK lookups resolve, formula values are baked, derived rows are materialized, string handles resolve. |
| 12.2 | `E2E_LegacyQueryFeatureParity` | For a table schema that exists in the old system, verify Derp.Doc export provides equivalent runtime query capabilities (same lookup behavior and API intent). |
| 12.3 | `E2E_ModifyAndReExport` | Full export → modify a cell value → re-export → load new binary → verify the changed value, all other values unchanged. |
| 12.4 | `E2E_EmptyProject_ExportsCleanly` | Project with no tables → export produces a valid (but empty) binary container. |
| 12.5 | `E2E_AllColumnTypes_RoundTrip` | One table with every exportable column type → export + load → every column value round-trips correctly. |

---

### Test Execution

```bash
# Run all Phase 5 tests
cd Derp.Doc.Tests
dotnet test --filter "FullyQualifiedName~Phase5"

# Run a specific category
dotnet test --filter "FullyQualifiedName~Phase5ExportTests.Binary"
dotnet test --filter "FullyQualifiedName~Phase5ExportTests.Determinism"

# Run with verbose output for debugging
dotnet test --filter "FullyQualifiedName~Phase5" -v detailed
```

### Priority Order

Implementation and testing should proceed in this order:

1. **Schema + Config** (1.x) — foundation, everything else depends on this
2. **Column Type Mapping** (3.x) — needed before binary writing
3. **Formula Baking** (4.x) — needed before derived materialization
4. **Derived Materialization** (5.x) — depends on formula baking
5. **Key System** (2.x) — can develop in parallel with binary format
6. **Binary Format** (6.x) — depends on type mapping + keys
7. **Codegen** (7.x) — depends on binary format + keys
8. **Determinism** (8.x) — cross-cutting, run after each category
9. **Validation** (9.x) — error paths, can develop alongside happy paths
10. **Build Integration** (10.x) — CLI wrapper around pipeline
11. **Hot-Reload** (11.x) — depends on binary format, can develop late
12. **E2E** (12.x) — final integration gate
