# EditorResizable Var-Heap + Proxy Plan (Immutable Runtime)

## Problem Statement

We need variable-length arrays in ECS components (e.g., waypoints, keyframes, gradient stops) that support:

1. **Editor/tooling time**: Authoring can resize/reorder arbitrarily
2. **Runtime**: Fast, allocation-free read access + fast serialization for rollback

### Why This Is Hard

ECS components are stored in Structure of Arrays (SoA). Every component of the same type must have the **same size**.

```
NavComponent[] column:
┌──────────────┬──────────────┬──────────────┐
│ Entity A     │ Entity B     │ Entity C     │
│ 3 waypoints  │ 10 waypoints │ 5 waypoints  │
│ ??? bytes    │ ??? bytes    │ ??? bytes    │  ← Can't have variable sizes
└──────────────┴──────────────┴──────────────┘
```

### Approaches Considered

| Approach | Pros | Cons |
|----------|------|------|
| **Per-size archetypes** | Exact fit | Explosion of types, complex queries |
| **Max-capacity inline array** | Memcpy works, direct access | Hard cap, can get huge, wasted memory |
| **Var-heap + handle** | True variable length, no per-entity allocations | Needs heap bytes in snapshots; access via proxy/view |

### Chosen Solution: Var-Heap + Handle (Immutable Runtime)

At bake time, pack all resizable data into a single byte buffer (the var-heap). Each ECS component stores a small blittable `ListHandle<T>` that points into the heap (offset + count). Runtime code reads resizables through generated proxy APIs that know how to resolve handles.

**Why this works for our use case:**
- No hard cap; supports very large arrays without inflating component size
- Handles are blittable and cheap to copy
- Runtime access is allocation-free (views are `ref struct`)
- Rollback snapshots can bulk-copy SoA columns **plus** the heap bytes

---

## Design Overview

### Schema (Runtime Struct)

User writes one definition:

```csharp
public partial struct NavComponent : IEcsComponent
{
    [Property]
    public float Speed;

    [EditorResizable]
    public ListHandle<Waypoint> Waypoints; // immutable at runtime
}
```

Code generation produces:
- **Runtime table proxies** that expose `Waypoints` as a `ResizableReadOnlyView<Waypoint>` so callsites feel like normal indexing
- (Future) authoring-side helpers can live outside ECS (assets/UGC tools), then bake into heap + handles

### Lifecycle

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         AUTHORING / EDITOR TOOLING                        │
│                                                                         │
│  User edits instances in authoring data (not the ECS component):        │
│    Instance A: [wp0, wp1, wp2]           (3 elements)                   │
│    Instance B: [wp0, wp1, ..., wp9]      (10 elements)                  │
│    Instance C: [wp0, wp1, wp2, wp3, wp4] (5 elements)                   │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                              BAKE TIME                                   │
│                                                                         │
│  1. Append all resizable arrays into a single byte heap                 │
│  2. Store ListHandle<T> (offsetBytes + count) into each component       │
│  3. Serialize: SoA columns + heap bytes                                 │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                              RUNTIME                                     │
│                                                                         │
│  NavComponent[] SoA column (fixed-size, includes ListHandle only):      │
│  ┌─────────────────────────────────────────────────────────┐            │
│  │ Instance A: Speed, Waypoints=(offset=0,   count=3)      │            │
│  ├─────────────────────────────────────────────────────────┤            │
│  │ Instance B: Speed, Waypoints=(offset=64,  count=10)     │            │
│  ├─────────────────────────────────────────────────────────┤            │
│  │ Instance C: Speed, Waypoints=(offset=184, count=5)      │            │
│  └─────────────────────────────────────────────────────────┘            │
│                                                                         │
│  Rollback snapshot = memcpy columns + memcpy heap bytes ✓               │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Storage Types

### ListHandle<T>

Blittable handle into the baked heap:

```csharp
public readonly struct ListHandle<T> where T : unmanaged
{
    public readonly int OffsetBytes;
    public readonly int Count;
    public bool IsValid => OffsetBytes >= 0 && Count > 0;
}
```

### EcsVarHeap

World-level immutable heap buffer that stores all baked bytes:

```csharp
public sealed class EcsVarHeap
{
    public ReadOnlySpan<byte> Bytes { get; }
    public void SetBytes(byte[] bytes, int usedBytes = -1);
}
```

### ResizableReadOnlyView<T>

Read-only view over `ListHandle<T>` inside the heap:

```csharp
public readonly ref struct ResizableReadOnlyView<T> where T : unmanaged
{
    public int Count { get; }
    public ref readonly T this[int index] { get; }
}
```

---

## Bake Process

Tooling uses `EcsVarHeapBuilder` to append spans and get handles:

```csharp
var heapBuilder = new EcsVarHeapBuilder();
ListHandle<Waypoint> handle = heapBuilder.Add(CollectionsMarshal.AsSpan(authoringWaypoints));
component.Waypoints = handle;
world.VarHeap.SetBytes(heapBuilder.ToArray());
```

---

## Runtime Access (Generated Proxies)

The world/table generator emits per-table row proxies when a table contains any `ListHandle<T>` fields.

Example callsite:

```csharp
var row = world.Nav.Row(rowIndex);
Waypoint wp = row.Nav.Waypoints[100];
```

---

## Open Questions

1. **Editor authoring storage:** best place to store editable lists (assets vs. editor-world sidecars)?
2. **Heap segmentation:** do we want per-asset heaps vs. per-world heap (for streaming)?
3. **Snapshot strategy:** per-frame snapshot copies heap bytes; if heaps are large, consider chunking/dedup.

---

## Summary

| Aspect | Editor | Runtime |
|--------|--------|---------|
| Storage | authoring data | `EcsVarHeap` + `ListHandle<T>` |
| Resize | ✓ Add/Remove/Insert | ✗ Immutable |
| Access | tooling UI | `ResizableReadOnlyView<T>` via proxies |
| Serialization | asset build output | bulk-copy columns + heap bytes |
| Allocations | tool-side only | zero in hot paths |
