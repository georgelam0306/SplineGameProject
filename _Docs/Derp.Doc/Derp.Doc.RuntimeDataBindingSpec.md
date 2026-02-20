# Derp.Doc Runtime DataBinding + Table Instance Variables Spec (v1)

Status: Accepted design baseline for implementation.

## 1. Goals

- Keep exported table rows/indexes immutable and memory-mapped.
- Add per-instance mutable table variable state.
- Add per-instance table variant selection (default base).
- Support binding view properties (filter/sort/group-by/chart) to variables or formulas.
- Compile formulas/bindings to generated C# with incremental recompute.
- Keep hot paths allocation-free (no per-frame lambdas/LINQ/dictionary lookups).

## 2. Non-Goals

- No runtime editing of formula source in shipped builds.
- No runtime fallback paths for unsupported plugin operations.

## 3. Data Model Semantics

### 3.1 Table variables (template-level)

Each table variable defines:

- `Id`
- `Name`
- `Kind` + `ColumnTypeId`
- `DefaultExpression`

Variable types match column types, including plugin `ColumnTypeId`.

### 3.2 Per-instance variable state

- Every runtime table instance has independent variable values.
- Template variable expression is the default behavior.
- Reset restores default behavior/value from template definition.

### 3.2.1 Per-instance variant selection

Policy:

- Every runtime table instance has an explicit `VariantId` (integer).
- `VariantId = 0` selects base.
- `VariantId > 0` selects a named project variant (export-time defined).

Variant selection affects only which immutable exported rows/indexes the instance reads.
It does not introduce any runtime execution of joins or formulas.

See `_Docs/Derp.Doc/Derp.Doc.TableVariantsAndSchemaLinkedTablesSpec.md` for export/runtime variant semantics.

### 3.3 Computed variable overrides

Policy:

- Overrides are allowed for computed variables.
- Override disables formula evaluation for that variable in that instance.
- Clearing/resetting override re-enables formula evaluation.

### 3.4 Bindings

Every bindable property stores one expression source at runtime:

- Unbound: literal value from view config.
- Bound: formula expression.

Normalization rule:

- "Bind to variable" is stored as formula `thisTable.<variableName>`.

This removes ambiguity when a property is "bound to variable with formula".

## 4. Cross-Table Variable Resolution

Policy: explicit instance links.

- `tables.OtherTable.variable` resolves through generated link fields on the current instance.
- Example: `PeopleInstance.Links.Settings` chooses which `Settings` instance is read.

## 5. Runtime Architecture

Two layers:

1. Immutable static data:
   - Existing generated table wrappers and `TableAccessor<T>`.
2. Mutable runtime state:
   - Per-table instance stores for vars, links, resolved binding outputs, and derived caches.

### 5.1 Generated runtime API shape

For each exported table `Foo`:

- `FooTable` (existing immutable wrapper).
- `FooRuntime` (instance manager, dirty propagation).
- `FooInstance` proxy.
- `FooInstance.Vars` typed property proxy.
- `FooInstance.Links` typed link proxy.
- `FooInstance.View` resolved outputs/cache proxy.

Primary callsite:

```csharp
var foo = db.FooRuntime.CreateInstance();
foo.Vars.Mode = "hard";
foo.Links.Settings = settings.Id;
var rows = foo.View.RowIndices;
```

## 6. Expression Compilation Pipeline

Pipeline:

1. Parse formula text to AST.
2. Lower to flat IR.
3. Type-check and collect dependencies.
4. Emit generated C# `Eval_*` methods.
5. Emit graph metadata arrays.

AST/IR is mandatory for deterministic dependency extraction, diagnostics, and type checking.

## 7. Runtime Binding Graph

Node kinds:

- Source variable slot (entrypoint only)
- Computed variable node
- Binding output node
- Derived cache node (for example row indices)

Edges represent dependencies.

Example:

```text
mode (source slot)
  -> filter_value (computed var)
  -> Filter0.Value (binding output)
  -> RowIndices (derived cache)
```

### 7.1 Emitted metadata shape

- `slotToNodesStart[]`, `slotToNodes[]`
- `dependentsStart[]`, `dependents[]`
- `topoOrder[]`
- `nodeKind[]`
- node dispatch id (maps node -> generated `Eval_*`)

### 7.2 Incremental execution

On source var setter:

1. Write slot.
2. Mark dirty slot.
3. Seed worklist from slot-to-node mapping.
4. Recompute affected nodes in topological order.
5. Update resolved outputs and derived caches.

## 8. View Runtime Resolution

Runtime maintains resolved view properties per instance:

- resolved filters
- resolved sorts
- resolved group-by

Derived cache:

- row index list produced from resolved filters/sorts.

Resolved outputs are cached and updated incrementally, not re-evaluated every draw.

## 9. Type Safety Rules

Binding outputs must match target property type at export-time:

- `Filter.Column` -> column slot type
- `Filter.Op` -> enum
- `Filter.Value` -> filter value type
- `Sort.Column` -> column slot type
- `Sort.Descending` -> bool
- `GroupBy.Column` -> optional column slot type

Type mismatch is export error.

## 10. Plugin Type Rules

Variables preserve plugin `ColumnTypeId`.

If expression/filter/sort requires plugin operations:

- required plugin ops must be available in export metadata/provider.
- otherwise export fails with explicit diagnostic.

No runtime substitution/fallback behavior.

## 11. Recompute Timing

- Editor mode: immediate recompute on setter.
- Runtime mode: supports batched recompute (`UpdateBindings`) with dirty accumulation.

Both modes use the same generated graph semantics.

## 12. Determinism + Performance Constraints

- No per-frame allocations in recompute path.
- No delegate/lambda-based runtime dispatch in hot path.
- Deterministic update order:
  - topological node order is stable,
  - linked-instance propagation iteration order is stable by instance id.

## 13. Diagnostics (Export-Time)

Required errors:

- unknown variable/table/column reference
- invalid cross-table link resolution
- formula cycle in variable/binding graph
- type mismatch against binding target
- unsupported plugin op requirement

## 14. Implementation Milestones

1. Add generated runtime state/proxy scaffolding (`FooRuntime`, `FooInstance`, `Vars`, `Links`, `View`).
2. Emit slot metadata for table vars and bindable outputs.
3. Implement AST -> flat IR dependency extraction for vars/bindings.
4. Emit compiled `Eval_*` C# and graph metadata arrays.
5. Implement incremental runtime executor.
6. Wire resolved output caches to view row-index cache recompute.
7. Add tests for:
   - per-instance isolation,
   - computed override semantics,
   - cross-table linked propagation,
   - binding output correctness,
   - plugin diagnostics.
