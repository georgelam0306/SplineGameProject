# Derp.Ecs — Planning Doc (V0)

**Status:** Draft (planning)
**Purpose:** Define a new ECS/component system intended to replace **SimTable** (deterministic simulation + rollback snapshots) and **Friflo** (general ECS/query + structural changes).

This folder also contains archived prior Derp.UI “V2 Property System” design notes copied from a worktree:
- `_Docs/Derp.Ecs/_Conversations/` (reference material; not normative)

---

## 1) Goals

### 1.1 Primary goals
- **Determinism-first simulation** suitable for rollback netcode:
  - Identical outputs given identical inputs.
  - Deterministic iteration order.
  - No floating-point in simulation math (Fixed64 only).
- **Zero allocations on hot paths** (per-frame/per-entity loops).
- **Stable entity identity** across frames and across rollback restore.
- **Fast snapshots** (restore/rewind is the core feature SimTable currently provides).
- **Source-generated storage and dispatch** (explicit, predictable, AOT-friendly).
- **Replace Friflo for the view domain**:
  - Archetype storage + fast queries
  - Structural changes (add/remove components) with a predictable/safe pipeline

### 1.3 Scope split: `Derp.Ecs` vs `Derp.Ecs.Editor`
This plan is intentionally split into two layers:

- **`Derp.Ecs` (core runtime)**:
  - stable entity IDs (`EntityHandle`)
  - archetype-table storage (SoA)
  - allocation-free deterministic queries (sim + view)
  - simulation snapshot/restore + derived recompute
  - view structural changes via command buffer (playback after each system)

- **`Derp.Ecs.Editor` (tooling layer, later)**:
  - property-edit proxy modes (`BeginPreview` / `BeginChange` / `CommitPreview`)
  - editor command bus + processors (Undo/AutoKey)
  - animation authoring UX + baking pipeline details
  - bindings + expression DSL tooling
  - UGC schema authoring + bake pipeline
  - inspector/drawer generation

The `_Docs/Derp.Ecs/_Conversations/` notes are primarily `Derp.Ecs.Editor` scope.

### 1.2 Secondary goals
- A unified component model usable by:
  - Simulation (rollback/deterministic)
  - Presentation/render (non-deterministic allowed)
  - Editor tooling (allocations OK)
- A path to integrate property metadata (inspector/animation/bindings) without reflection.

---

## 2) Non-goals (initially)

- A “fully dynamic” ECS where you can freely add/remove components inside deterministic simulation loops.
- Runtime reflection-based query/property dispatch.
- Unbounded dictionaries/strings in simulation hot paths.
- Runtime fallbacks (“try X, else Y”) instead of generator-enforced correctness.

---

## 3) Terminology

- **Simulation**: Deterministic rollback domain (Fixed64 math, snapshot-able).
- **Non-simulation**: Presentation/editor domain (float/strings ok, not rollback).
- **Derived / computed**: State that can be recomputed from authoritative state (not serialized/snapshotted; recompute after restore).
- **EntityHandle**: Proposed stable entity identifier (single value; generational; globally unique within a world).
- **Archetype**: A fixed set/layout of components for an entity kind.

---

## 4) Hard requirements (from current codebase constraints)

### 4.1 Determinism constraints
- No non-deterministic container iteration in simulation (`Dictionary`, `HashSet`).
- Deterministic system ordering and deterministic entity iteration within each system.
- Structural changes must be deterministic and applied at deterministic times.

### 4.2 Allocation constraints
- Per-frame/per-entity simulation loops must allocate **zero**.
- Any editor-only DSL/expression machinery must be compile-time only or tooling-only (never executed in runtime simulation).

### 4.3 Snapshot/rollback constraints
- Snapshot/restore must be efficient enough to remain the default architecture (not a debug tool).
- Restore must produce identical derived state after recomputation.

---

## 5) Core design proposal (high-level)

### 5.0 Separate authoring API from storage (the “hybrid” we want)
Derp.Ecs should treat these as two independent decisions:

1) **Authoring / API model (what developers write)**
- We want **components as types** (Derp.UI ergonomics): `Position`, `Velocity`, `SpriteRenderer`, etc.

2) **Storage model (what the engine stores/iterates/snapshots)**
- We want **archetype tables** (SoA per archetype), because that preserves SimTable-style determinism + snapshot performance.

So: **components-as-types authoring**, **tables-as-archetype storage**.

### 5.1 Two domains, one design language
Derp.Ecs should explicitly support two domains:

1) **Simulation domain**
- Strict deterministic rules.
- Snapshot-able authoritative state.
- Derived state recomputed after restore.

2) **Non-simulation domain**
- Rendering/editor convenience.
- Can use floats/strings/managed allocations (outside hot paths).
- Not snapshotted by rollback.

This separation must be **compile-time** (attributes / separate assemblies / generator options), not runtime fallbacks.

### 5.2 “Tables as archetypes” (recommended v1)
To replace SimTable cleanly and keep rollback cheap, prefer **archetype tables** as the storage unit:
- Entities are allocated in a specific archetype table.
- That archetype table owns SoA columns for its component data.
- Queries iterate tables (and optionally multiple tables via generated unions / generated query plans).

This is conceptually close to SimTable’s current model and avoids the hardest “general ECS” problem: fast, arbitrary archetype transitions with stable IDs.

#### 5.2.1 Archetype definition strategy (v1)
We will support two ways to define which archetypes exist:

- **Simulation domain:** explicit and boring (fixed archetypes; no runtime add/remove). Archetypes are defined by codegen/config, not by runtime behavior.
- **View domain:** inferred registry from explicit spawn tags using Option A:
  - `Spawn<ArchetypeTag>(init => init.With<TComponent>())`
  - generator emits the archetype table layouts and query plans.

### 5.3 Optional future: general archetype ECS
Friflo replacement is a core goal. The “optional future” part is not *whether* we replace it, but *how far we go* beyond a pragmatic v1:
- v1: view-domain archetypes + queries + structural change pipeline (command buffer)
- v2+: deeper parity items (advanced scheduling, change filtering, etc.) if needed

---

## 6) Stable entity identity (EntityHandle)

### 6.1 Properties required of `EntityHandle`
- A `EntityHandle` uniquely identifies an entity **within a world**.
- Handles must be **generational** (stale handle detection after free/reuse).
- `EntityHandle` must remain stable across:
  - Rollback restore (state rewind/fast-forward)
  - Serialization/deserialization (save/load/replay)

### 6.2 Proposed shape
Define `EntityHandle` as a **single packed integer** (likely `ulong`) containing:
- **KindId** (stable schema/entity kind identity)
- **RawId/Slot** (index into indirection tables / free lists)
- **Generation** (to detect stale references)
- Optional: **Domain bit** (simulation vs non-simulation) if we want cross-domain references.

This is a generalization of `SimTable.SimHandle` (which packs `generation` + `rawId` into a 32-bit stableId).

### 6.3 Indirection rule (critical for rollback + stable IDs)
Entity identity must not “be” the physical slot in a densely packed array unless the allocator is guaranteed deterministic under rollback.
Recommended:
- Keep an indirection layer: `rawId -> slot` and `slot -> rawId` with generations.
- Snapshot includes the allocator/generation state so restore reproduces the same handle validity.

### 6.4 Concrete v1 proposal (subject to change)
Start with a single packed `ulong` intended to generalize `SimTable.SimHandle`:

- `KindId` (16 bits): up to 65535 stable kinds per world.
- `RawId` (24 bits): up to ~16 million stable IDs per table.
- `Generation` (16 bits): stale detection.
- `Domain + flags` (8 bits): optional; can be `0` for v1.

---

## 7) Query model (deterministic, allocation-free)

### 7.1 Requirements
- Zero allocations during iteration.
- Deterministic iteration order.
- Avoid dictionaries and virtual dispatch in hot paths.

### 7.2 Recommended v1 query approach: generated multi-table queries
Use a SimTable-like approach:
- A query is a compile-time declaration (interface/struct) that describes required fields/components.
- Codegen generates:
  - Which tables/archetypes participate
  - A tight loop that iterates each participating table in a deterministic order
  - Ref accessors for columns

This is already a proven pattern in `Docs/MultiTableQuery.md` and avoids runtime archetype filtering.

### 7.3 Deterministic iteration order definition
Within a table:
- Iterate by `slot` order `0..Count-1`, skipping freed slots, or use a generated sorted index if we need spatial locality.
Across tables:
- Iterate tables/archetypes in deterministic order (e.g., ascending archetype id / kind id).

### 7.4 Arrays and resizable data (simulation vs view)
Derp.Ecs needs two different answers for “arrays” because simulation and view have different constraints.

#### 7.4.1 Simulation: fixed-size arrays only (v1)
Simulation components must be snapshot-friendly and allocation-free. For v1:

- Allowed: **fixed-size arrays** implemented as inline array structs (`[InlineArray(N)]`) with optional `Count`.
  - Pattern: `Count + Max` (bounded variable length) is deterministic and snapshot-friendly.
- Allowed: 2D fixed arrays as a single inline array with row-major indexing.
- Not allowed: per-entity variable-length arrays whose size differs per instance (no per-instance blobs, no dynamic buffers).

#### 7.4.2 View/editor/runtime-data: `EditorResizable` baked to per-instance blobs
For view components we want true per-instance sizes. The chosen design is:

- Editor time: resizable arrays are `List<T>` (allocations acceptable).
- Bake time: each instance is packed into an immutable **per-instance blob** with offset tables.
- Runtime: ECS stores a **blob handle**; generated accessors expose `Span<T>` over blob memory (zero allocations).

This avoids “archetypes by size” and supports instances that each have their own sizes.

##### 7.4.2.1 Blob layout sketch (normative shape, not exact bytes)
Each blob contains:
- Header: schema id/version + counts + offsets to variable sections
- Fixed section: fixed fields and fixed-size structs
- Variable sections: contiguous payloads for arrays; nested arrays represented via offset tables

Accessors are generated so that `Span<T>` creation is pointer math using stored offsets/counts; no parsing, no allocations.

##### 7.4.2.2 What the ECS stores for blob-backed components
Blob-backed components store:
- a blob handle (id into a blob store) OR a pointer+length provided by a blob store
- optional debug schema/version fields

Blobs are immutable at runtime; “editing” creates a new blob (tooling path).

---

## 8) Structural changes and archetype transitions

### 8.1 Simulation stance (v1): no runtime archetype transitions
For deterministic simulation:
- Entities do **not** add/remove components at runtime.
- Changing “shape” of an entity is modeled as:
  - (A) mutate fields, or
  - (B) despawn + spawn in a different archetype (with explicit migration logic), or
  - (C) fixed conversions at safe points (explicit “convert archetype” operation).

This keeps rollback snapshots simple and fast.

### 8.2 View stance (v1): allow add/remove via a safe structural pipeline
For the view domain (Friflo replacement scope):
- Allow add/remove components and archetype transitions.
- Enforce a predictable rule: **no structural changes inside query iteration**.
- **Command-buffer only:** structural changes must be expressed via a command buffer API (no “immediate-mode” Add/Remove on the world).
- **Playback policy (v1):** apply the command buffer **after each system** completes (and before the next system runs).
  - Rationale: keeps behavior simple to reason about, mirrors common ECS staging, and bounds how far structural changes can “lag”.
  - Note: we can add a “batched per phase/frame” option later if performance demands it.

### 8.3 Structural changes still exist (both domains)
Even without simulation transitions, we still need structural operations:
- Spawn/despawn (allocate/free)
- Parent/child relationships (if needed)
- Optional: moving between fixed archetypes (explicit “convert” operation)

Simulation still follows the existing “no structural change during query” constraint if we ever add more structural ops later.

---

## 9) Codegen: simulation vs non-simulation vs derived state

### 9.1 Why we need explicit categories
We need the generator to enforce:
- Simulation-only restrictions (Fixed64, no floats/strings, deterministic collections).
- Snapshot inclusion/exclusion (authoritative vs derived).
- Evaluation order for derived state (flush/toposort).

### 9.1.1 Summary of storage rules by domain (v1)
- Simulation:
  - snapshot/restore required
  - authoritative + derived split (derived excluded from snapshots, recomputed)
  - fixed-size arrays only (see 7.4.1)
- View:
  - snapshot/restore not required
  - may use blob-backed resizable data (see 7.4.2)
  - may allow archetype transitions later (out of v1 scope)

### 9.2 Intent without annotation spam (preferred)
We’ve become annotation-heavy. For Derp.Ecs we prefer **implicit defaults** with a small number of “role markers”.

#### 9.2.1 Domain is compile-time, not per-type attributes
Instead of putting `[Domain=...]` attributes on every component, set the domain at the assembly/project level (generator reads it):
- `DerpEcsDomain = Simulation` (strict rules, snapshot/restore)
- `DerpEcsDomain = View` (blob-backed resizables allowed; no rollback requirements)

This keeps intent obvious (“this project is simulation”) without repeating attributes everywhere.

Implementation note (MSBuild): expose the property to the compiler/analyzers so the generator can read it:
```xml
<ItemGroup>
  <CompilerVisibleProperty Include="DerpEcsDomain" />
</ItemGroup>
```

#### 9.2.2 Authoritative vs derived is expressed by type role
We should not require field-level `[ComputedState]` everywhere. Prefer type-level roles:
- `ISimComponent` (authoritative; snapshotted)
- `ISimDerivedComponent` (derived; not snapshotted; recomputed)

This still maps to SimTable’s concept of computed state, but the default is “authoritative unless declared derived”.

#### 9.2.3 Write-safety for derived state (no “wrong place” writes)
Derived state must only be written by derived systems. Enforce this with:
- **capability-based APIs** (normal sim systems can’t obtain a writer for derived components), and/or
- a Roslyn analyzer rule: “writes to `ISimDerivedComponent` are illegal outside derived systems”.

No runtime fallbacks; violations should be compile errors in simulation assemblies.

### 9.2.1 Domain enforcement (generator errors, no runtime fallbacks)
For **simulation-domain** schemas, the generator should reject:
- `float`/`double` authoritative fields (except explicitly tagged as non-sim / presentation-only)
- `string`, `object`, `List<>`, `Dictionary<,>` or any reference type
- any field containing managed references (non-blittable)

Allowed examples:
- `FixedMath.Fixed64`, `Fixed64Vec2/Vec3`
- `int`, `uint`, `short`, `byte`, `bool`
- fixed-size inline arrays (generator-expanded) for deterministic snapshotting

### 9.2.2 Authoritative vs derived layout
Simulation schemas should split fields into:

- **Authoritative** (snapshotted/replicated):
  - included in rollback snapshots
  - only mutated by simulation systems / deterministic inputs

- **Derived** (computed, not snapshotted):
  - stored for cache locality, but excluded from serialization
  - recomputed after restore via generated `RecomputeAll()` / `Flush()`

This mirrors SimTable’s `[ComputedState]` intent, but Derp.Ecs should treat it as first-class.

### 9.3 Derived evaluation model (“Flush”)
Adopt the V2 concept:
- Writes mark dirty (or accumulate in a deterministic staging buffer).
- A single `Flush()` per tick applies:
  1) derived-field recomputation in a baked deterministic order
  2) binding propagation (if we keep that concept)

For simulation:
- `Flush()` must be deterministic and allocation-free.

---

## 10) Relationship to existing systems

### 10.1 SimTable
Derp.Ecs v1 should preserve SimTable’s superpowers:
- stable generational handles
- snapshot/restore speed
- deterministic iteration (including multi-table queries)
- computed/derived state recompute after restore

### 10.2 Friflo
Derp.Ecs is intended to replace Friflo for the view domain by providing:
- Archetype storage and fast queries
- A structural change pipeline (add/remove) that is safe during iteration (command buffer)

---

## 11) Deliverables (what “done” looks like)

### 11.1 Minimum viable Derp.Ecs (to replace SimTable)
- `EntityHandle` stable IDs + deterministic allocate/free.
- Archetype/table storage generated from schemas.
- Snapshot/restore for simulation domain.
- Deterministic, allocation-free query iteration (including multi-table queries).
- Derived-state recompute after restore.

### 11.2 Minimum viable Derp.Ecs (to replace Friflo in view)
- View-domain archetype storage and fast queries.
- Structural change pipeline (add/remove) enforced via command buffer only.
- Command buffer playback after each system (v1 default).
- Optional: inferred archetype registry from explicit spawn tags (`Spawn<ArchetypeTag>(init => init.With<T>())`).

### 11.3 Next step (optional): editor/property integration
- Property metadata generation compatible with current `Property.*` pattern:
  - stable `PropertyId` per field path
  - generated read/write dispatch
  - editor tooling can use strings, runtime uses handles

### 11.4 Feature parity with `_Conversations` (audit checklist)
The files in `_Docs/Derp.Ecs/_Conversations/` describe a broader “V2 property system” for Derp.UI (proxies, undo/autokey, animation authoring, bindings/expressions, UGC baking).
Derp.Ecs does not need to implement all of that in v1, but the plan should explicitly track it so we don’t lose scope/intent.

#### 11.4.1 Editing/proxy modes (BeginPreview/BeginChange)
From the conversations:
- `Get<T>()`, `Ref<T>()`, `BeginPreview<T>()`, `BeginChange<T>()`, plus `CommitPreview<T>()`.

In this plan:
- Partially covered conceptually via “derived flush” and “command-buffer structural changes”, but the **property-edit proxy API** is not yet specified.
- Decision needed: do we implement these as a **tooling layer** on top of Derp.Ecs, or as core API (especially for view/editor)?

#### 11.4.2 Command bus + processors (Undo, AutoKey, etc.)
From the conversations:
- A command stream representing **user intent**, processed by independent processors (UndoProcessor, AutoKeyProcessor).

In this plan:
- Not specified yet (currently only covers structural command buffers for view and “derived recompute” for sim).
- If we want parity with Derp.UI workflows, we should add a tooling-level command bus for editor interactions.

#### 11.4.3 Animation authoring + baking
From the conversations:
- Editor representation (resizable) vs runtime representation (packed).
- Pre-sampled keyframes at `SnapFps`, incremental rebake.
- Timeline/state machine integration and frame flow.

In this plan:
- Covered only in the general “EditorResizable → blob” approach; animation-specific pieces are not yet planned.

#### 11.4.4 Bindings + expressions
From the conversations:
- Bindings with multiple inputs + expression DSL.
- Editor interpreted evaluation; runtime compiled/inlined evaluation.
- Toposorted evaluation order.

In this plan:
- We have “Flush()” and derived recompute, but **no binding/expression system** is described yet.

#### 11.4.5 UGC schemas / user-defined data
From the conversations:
- Editor dynamic schemas; bake to runtime fixed/blittable layouts.
- Prefab inheritance flattening; same API as code-defined components.

In this plan:
- Not specified yet.

#### 11.4.6 Property drawers / inspector generation
From the conversations:
- Generated per-component drawers + multi-select, replacing large manual inspectors.

In this plan:
- Not specified yet.

---

## 12) Systems + DI (sketch, non-normative)

We use Derp.DI to wire systems. The goal for Derp.Ecs is to avoid "manual system list plumbing" while keeping ordering explicit and deterministic.

### 12.1 Recommended shape (v1)
- Treat each world runner as a DI product:
  - `SimWorldRunner` owns the sim world + ordered sim systems.
  - `ViewWorldRunner` owns the view world + ordered view systems + the command buffer.
- Ordering is explicit (a single schedule per domain/phase).
- View command buffer playback happens automatically **after each system** (policy enforced by the runner).

Lifecycle/scoping can be revisited later; v1 should keep it simple and explicit.

---

## 13) Open questions (to decide before implementation)

1) **What exactly is EntityHandle’s bit layout?** (How many table ids? how many entities? how many generations?)
2) **Do we want a single world with domains, or separate worlds (SimWorld vs RenderWorld)?**
3) **Do we allow “convert archetype” (explicit) in v1, or only despawn/spawn?**
4) **What is the canonical query declaration format?** (interfaces like SimTable’s `MultiTableQuery`, or explicit `Query<T...>` types)
5) **How do we express derived computations for codegen without runtime execution?** (reuse SimTable’s builder style, or define a smaller DSL)
6) **How are view blobs stored/loaded?** (blob store API, versioning, and how editor produces new blobs)
7) **How do we integrate replication/undo/recording?** (simulation-only command log vs editor-only)
8) **How is the domain selected/enforced?** (MSBuild property, assembly attribute, or convention)
9) **Which `_Conversations` features are in-scope for Derp.Ecs v1?** (proxies, undo/autokey, animation, bindings/DSL, UGC, drawers)
