# Relational SimTable Architecture Design

**Purpose**: Architecture documentation for adding relational features to SimTable while maintaining zero-allocation, deterministic, high-performance characteristics.

**Status**: Design only (not yet implemented)

---

## Table of Contents

1. [Current Architecture Summary](#current-architecture-summary)
2. [Feature 1: Typed Handles](#feature-1-typed-handles)
3. [Feature 2: Reverse Lookup Indexes](#feature-2-reverse-lookup-indexes)
4. [Feature 3: Cascade Invalidation](#feature-3-cascade-invalidation)
5. [Feature Interdependencies](#feature-interdependencies)
6. [Implementation Roadmap](#implementation-roadmap)
7. [Critical Files](#critical-files)

---

## Current Architecture Summary

### SimHandle (Existing)

```csharp
public readonly struct SimHandle {
    public int TableId;      // Which table (0-17)
    public int StableId;     // (Generation << 16) | RawId
}
```

- **Generational validation**: When entity freed, generation increments → old handles become stale
- **GetSlot()** returns -1 if generation mismatch (stale reference detected)
- **No compile-time type safety**: `SimHandle` can reference any table

### Current Reference Patterns

```csharp
// In CombatUnitRow
public SimHandle TargetHandle;           // Could be ZombieRow
public SimHandle GarrisonedInHandle;     // Could be BuildingRow

// Resolution requires runtime checks
int slot = zombies.GetSlot(unit.TargetHandle);  // Validates TableId + generation
if (slot < 0) { /* stale or wrong table */ }
```

---

## Feature 1: Typed Handles

### Problem

`SimHandle` is untyped - a field can accidentally hold a reference to the wrong table type. Compile-time safety is missing.

### Solution

Zero-overhead wrapper `SimHandle<TRow>` with identical memory layout:

```csharp
[StructLayout(LayoutKind.Sequential)]
public readonly struct SimHandle<TRow> where TRow : struct
{
    private readonly SimHandle _handle;  // Same 8-byte layout

    public int TableId => _handle.TableId;
    public int StableId => _handle.StableId;
    public bool IsValid => _handle.IsValid;

    // Implicit upcast to untyped (always safe)
    public static implicit operator SimHandle(SimHandle<TRow> typed) => typed._handle;

    // NO implicit downcast - must validate explicitly
    public static SimHandle<TRow> Invalid => new(SimHandle.Invalid);
}
```

### Usage

```csharp
[SimTable]
public partial struct CombatUnitRow
{
    public SimHandle<ZombieRow> TargetHandle;      // Compile-time: only zombies
    public SimHandle<BuildingRow> GarrisonedIn;    // Compile-time: only buildings
}

// Type-safe resolution - no runtime TableId check needed!
int slot = zombies.GetSlot(unit.TargetHandle);  // Only validates generation
```

### Polymorphic References (Union Types)

For handles that can reference multiple table types:

```csharp
// Define valid target tables
[TableUnion(typeof(BuildingRow), typeof(CombatUnitRow))]
public partial struct Attackable { }

// Use union-typed handle
public SimHandle<Attackable> TargetHandle;  // Can reference Building OR CombatUnit

// Resolution requires switch on TableId
switch (handle.TableId) {
    case BuildingTable.TableIdConst: /* ... */ break;
    case CombatUnitTable.TableIdConst: /* ... */ break;
}
```

### Generator Changes

1. Generate typed `GetSlot(SimHandle<TRow>)` that skips TableId validation
2. Generate `AllocateTyped()` returning `SimHandle<TRow>`
3. Generate union handle specializations for `[TableUnion]` types

### Performance: Zero overhead

- Identical memory layout (8 bytes)
- JIT inlines all methods
- Typed GetSlot is faster (no TableId check)

---

## Feature 2: Reverse Lookup Indexes

### Problem

To find "all units garrisoned in building B", you must scan ALL units:

```csharp
for (int i = 0; i < units.Count; i++) {
    if (units.GarrisonedInHandle(i) == buildingHandle) { ... }  // O(n) scan!
}
```

### Solution

Opt-in `[IndexedRef]` attribute generates a reverse lookup index:

```csharp
[SimTable]
public partial struct CombatUnitRow
{
    [IndexedRef] public SimHandle GarrisonedInHandle;  // Generates reverse index
}

// Query: O(k) where k = number of referencing entities
foreach (int slot in units.QueryRefsTo_GarrisonedInHandle(buildingHandle)) {
    var unit = units.GetRow(slot);
    // ...
}
```

### Index Data Structure

Doubly-linked list per target, using parallel arrays (no allocation):

```csharp
// Generated fields in table class
private int[] _idx_GarrisonedInHandle_targetHead;  // [targetRawId] -> first slot
private int[] _idx_GarrisonedInHandle_slotNext;    // [slot] -> next in chain
private int[] _idx_GarrisonedInHandle_slotPrev;    // [slot] -> prev in chain
```

Memory overhead: `3 × Capacity × 4 bytes` per indexed field
(Example: 10,000 units = 120KB per indexed field)

### Mutation Tracking

Indexed fields use setter method instead of ref return:

```csharp
// Instead of: units.GarrisonedInHandle(slot) = newHandle;
// Use:
units.SetGarrisonedInHandle(slot, newHandle);  // Updates index automatically
```

### Index Maintenance on Free()

When freeing a slot, the swap-and-pop must update the moved entity's position in the index:

```csharp
public void Free(int packedStableId) {
    // 1. Remove freed slot from its target's chain
    // 2. If swapping with lastSlot, update lastSlot's chain position to new slot
    // 3. Perform swap-and-pop as usual
}
```

### Snapshot/Rollback Strategy

Index is **not serialized** - it's rebuilt from data after LoadFrom():

```csharp
public void RebuildIndexes() {
    Array.Fill(_idx_GarrisonedInHandle_targetHead, -1);
    for (int slot = 0; slot < Count; slot++) {
        var handle = GarrisonedInHandle(slot);
        if (handle.IsValid) {
            // Insert at head of target's chain
        }
    }
}
```

### Query API (Zero-Allocation)

```csharp
public RefQueryEnumerable_GarrisonedInHandle QueryRefsTo_GarrisonedInHandle(SimHandle target)
    => new(this, target);

public readonly ref struct RefQueryEnumerable_GarrisonedInHandle {
    // Zero-allocation struct enumerator pattern
}
```

---

## Feature 3: Cascade Invalidation

### Problem

When an entity is freed, other entities still hold handles that look valid:

```csharp
// Building B is freed
buildings.Free(buildingHandle);

// Unit still thinks it's garrisoned!
if (unit.GarrisonedInHandle.IsValid) { ... }  // Still true!
// GetSlot() returns -1, but code must check manually everywhere
```

### Solution Options

#### Option A: Lazy Cleanup (Recommended)

Since `GetSlot()` already returns -1 for stale handles, this is essentially optional. Systems already check `if (slot < 0) return;`. Lazy cleanup is about proactively setting `handle = Invalid` so `IsValid` also returns false.

Scan for stale references once per frame:

```csharp
// Generated per table with [CascadeInvalidate] fields
public void CleanupStaleReferences(SimWorld world) {
    for (int slot = 0; slot < Count; slot++) {
        ref var handle = ref GarrisonedInHandle(slot);
        if (handle.IsValid && world.BuildingRows.GetSlot(handle) < 0) {
            handle = SimHandle.Invalid;
            OnGarrisonInvalidated?.Invoke(world, slot);  // Optional callback
        }
    }
}

// Call at frame start
simWorld.CleanupAllStaleReferences();
```

**Performance**: O(n × m) where n = entities, m = indexed fields
For 10,000 units with 2 fields: ~20,000 checks per frame (~0.1ms)

#### Option B: Reverse Index Cascade (Complex, Faster)

Immediately clear references when entity freed:

```csharp
public void Free(int packedStableId, SimWorld? world = null) {
    // Before freeing: cascade to all referencers
    if (world != null) {
        foreach (int slot in units.QueryRefsTo_GarrisonedInHandle(this.GetHandle(rawId))) {
            units.SetGarrisonedInHandle(slot, SimHandle.Invalid);
        }
    }
    // Normal free...
}
```

**Requires**: Reverse index (Feature 2) to be efficient
**Performance**: O(k) per free, where k = number of references

### Attribute Definition

```csharp
[AttributeUsage(AttributeTargets.Field)]
public sealed class CascadeInvalidateAttribute : Attribute
{
    public Type? TargetTable { get; init; }      // Which table this references
    public string? OnInvalidate { get; init; }   // Optional callback method
}
```

### Usage

```csharp
[SimTable]
public partial struct CombatUnitRow
{
    [CascadeInvalidate(TargetTable = typeof(BuildingRow))]
    public SimHandle GarrisonedInHandle;
}
```

### Recommendation

Start with **Option A (Lazy Cleanup)** or even skip it entirely since handles are already easy to validate via `GetSlot()`:
- Simpler implementation
- No reverse index dependency
- Acceptable performance for typical entity counts
- Add Option B later if profiling shows cleanup is a bottleneck

---

## Feature Interdependencies

```
┌─────────────────┐
│  Typed Handles  │  ← Standalone, implement first
│  SimHandle<T>   │
└────────┬────────┘
         │ (optional: typed indexes)
         ▼
┌─────────────────┐
│ Reverse Indexes │  ← Enables efficient cascade
│  [IndexedRef]   │
└────────┬────────┘
         │ (required for Option B)
         ▼
┌─────────────────────────────┐
│    Cascade Invalidation     │
│  [CascadeInvalidate]        │
│  Option A: Lazy (no deps)   │
│  Option B: Reverse index    │
└─────────────────────────────┘
```

**Implementation Order**:
1. **Typed Handles** - Zero risk, pure type safety improvement
2. **Lazy Cascade** - Simple stale ref cleanup without indexes (optional)
3. **Reverse Indexes** - When reverse lookups needed for game logic
4. **Indexed Cascade** - If lazy cleanup becomes a bottleneck

---

## Implementation Roadmap

### Phase 1: Typed Handles (Low Risk)

- Add `SimHandle<TRow>` to SimTable.Annotations
- Generate typed `GetSlot()` and `AllocateTyped()` in tables
- Add union handle support for `[TableUnion]`
- Backward compatible - existing `SimHandle` still works

### Phase 2: Lazy Cascade (Moderate, Optional)

- Add `[CascadeInvalidate]` attribute
- Generate `CleanupStaleReferences()` per table
- Generate `CleanupAllStaleReferences()` in SimWorld
- Integrate into game loop

### Phase 3: Reverse Indexes (Complex)

- Add `[IndexedRef]` attribute
- Generate index arrays and maintenance code
- Modify field accessors to use setters
- Add `RebuildIndexes()` for rollback
- Generate query enumerables

### Phase 4: Indexed Cascade (Optional)

- Wire cascade to use reverse index when available
- Add optional callback hooks

---

## Critical Files

### Annotations (New attributes)

- `SimTable.Annotations/SimHandle.cs` - Add `SimHandle<TRow>`
- `SimTable.Annotations/SimTableAttribute.cs` - Add `[IndexedRef]`, `[CascadeInvalidate]`

### Generator Core

- `SimTable.Generator/SchemaModel.cs` - Add `IsIndexed`, `CascadeInfo` to ColumnInfo
- `SimTable.Generator/SimTableGenerator.cs` - Parse new attributes in PopulateColumns

### Generator Renderers

- `SimTable.Generator/TableRenderer.Lifecycle.cs` - Typed Allocate/GetSlot, cascade in Free
- `SimTable.Generator/TableRenderer.FieldAccessors.cs` - Setter methods for indexed fields
- `SimTable.Generator/TableRenderer.ReverseIndex.cs` (new) - Index data structures and queries
- `SimTable.Generator/SimWorldRenderer.cs` - CleanupAllStaleReferences, RebuildAllIndexes

### Example Row Structs (for testing)

- `Catrillion/Simulation/Components/CombatUnitRow.cs` - Has GarrisonedInHandle, TargetHandle
- `Catrillion/Simulation/Components/BuildingRow.cs` - Has GarrisonSlot array
- `Catrillion/Simulation/Components/ZombieRow.cs` - Has TargetHandle, AggroHandle

---

## Performance Summary

| Feature | Memory Overhead | Hot Path Cost | Complexity |
|---------|-----------------|---------------|------------|
| Typed Handles | 0 | Faster (skip TableId check) | Low |
| Lazy Cascade | 0 | O(n×m) per frame | Low |
| Reverse Index | 120KB per 10k entities per field | O(1) mutation overhead | High |
| Indexed Cascade | (uses reverse index) | O(k) per free | Medium |

---

## Open Questions for Implementation

1. **Array fields with [IndexedRef]**: Should each array element have separate index entry?
2. **Multi-table unions**: How to handle `[IndexedRef]` on polymorphic handles?
3. **Computed fields**: Should indexed fields trigger recomputation?
4. **Rollback verification**: Need determinism tests for index rebuild
