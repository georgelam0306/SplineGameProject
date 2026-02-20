# Derp.Ecs World Setup (Source-Generated)

Derp.Ecs uses a **syntax-only** `Setup(...)` method to declare archetypes, components, and generated queries.

The method exists so you can write a readable declaration, and the source generator parses the fluent call chain at compile time.

## Goals

- **No manual IDs** at the callsite (the generator assigns `KindId`/`ArchetypeId` deterministically).
- **SoA storage** (structure-of-arrays) for cache-friendly hot paths.
- **Zero allocations** in per-frame iteration and queries (use `Span<T>`).
- **Deterministic iteration order** (row index order).

## Declaring a World

Create a `partial` world type and add a `Setup` method:

```csharp
using System.Diagnostics;
using DerpLib.Ecs;
using DerpLib.Ecs.Setup;

namespace MyGame.Simulation;

public sealed partial class SimEcsWorld
{
    [Conditional(DerpEcsSetupConstants.ConditionalSymbol)]
    private static void Setup(DerpEcsSetupBuilder b)
    {
        b.Archetype<Horde>()
            .Capacity(1000)
            .With<TransformComponent>()
            .With<CombatComponent>()
            .Spatial(position: nameof(TransformComponent.Position), cellSize: 32, gridSize: 32, originX: -512, originY: -512)
            .QueryRadius(position: nameof(TransformComponent.Position), maxResults: 256)
            .QueryAabb(position: nameof(TransformComponent.Position), maxResults: 256);
    }
}
```

Notes:
- `DerpEcsSetupBuilder` and `DerpEcsArchetypeSetupBuilder<TKind>` are **syntax-only**. They throw if used at runtime.
- The generator only cares about fluent chains rooted at the `Setup` parameter (e.g. `b.Archetype<...>()...`).
- `Spatial(...)` enables **cell/grid spatial indexing** so radius/AABB queries don't scan all rows.

## Project Setup (csproj)

Reference the runtime normally and the generator as an analyzer:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\Shared\DerpLib.Ecs\Runtime\Derp.Ecs.Runtime.csproj" />
  <ProjectReference Include="..\..\Shared\DerpLib.Ecs\Generator\Derp.Ecs.Generator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

## What Gets Generated

For each `Archetype<TKind>()`:
- A `TKindTable` class with SoA arrays for the declared components.
- `TryQueueSpawn(out TKindPendingSpawn)` / `QueueDestroy(EntityHandle)` structural command buffer APIs.
- A `TKindPendingSpawn` `ref struct` ticket used to initialize queued spawns.
- Optional queue sizing overrides: `.SpawnQueueCapacity(n)` / `.DestroyQueueCapacity(n)` (default is `max(64, Capacity/4)`).
- `ref` accessors per component (name is the component name with `Component` trimmed).
- Optional query methods declared from `.QueryRadius(...)` / `.QueryAabb(...)`.
- Optional spatial indexing declared from `.Spatial(...)` with `RebuildSpatialIndex()`.

The world also gets:
- `public EcsEntityIndex EntityIndex { get; }`
- `public TKindTable TKind { get; }` properties
- A generated parameterless constructor that initializes tables.
- `PlaybackStructuralChanges()` that plays back structural changes in deterministic `KindId` order.

## Using Generated Tables

Spawn:

```csharp
if (world.Horde.TryQueueSpawn(out var spawn))
{
    spawn.Transform.Position = spawnPos;
    spawn.Combat.Health = 100;
}
```

Iterate (hot path):

```csharp
for (int row = 0; row < world.Horde.Count; row++)
{
    ref var transform = ref world.Horde.Transform(row);
    // mutate transform.Position / transform.Velocity ...
}
```

Query (zero allocation):

```csharp
Span<EntityHandle> results = stackalloc EntityHandle[HordeTable.QueryRadiusMaxResults];
int count = world.Horde.QueryRadius(center, radius, results);
for (int i = 0; i < count; i++)
{
    EntityHandle entity = results[i];
    // ...
}
```

Spatial indexing:

```csharp
// Call once per frame after positions change, before spatial queries.
world.Horde.RebuildSpatialIndex();
```

`Spatial(...)` parameters:
- `cellSize`: world units per cell
- `gridSize`: number of cells per axis (total cells = `gridSize * gridSize`)
- `originX` / `originY`: world-space position of cell (0,0)

## Caveat: IDs and Renames

The generator assigns `KindId`/`ArchetypeId` deterministically from the declared kinds.
If you rename/move kind types, the assigned IDs may change. If you later persist IDs across sessions/snapshots, plan for an explicit stable-ID strategy.
