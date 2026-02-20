# Derp.Ecs Architecture

Source-generated deterministic ECS for simulation with rollback support. Located in `Shared/DerpLib.Ecs/`.

## Overview

```
DerpLib.Ecs
├── Runtime/          # EntityHandle, IEcsSystem, EcsSystemPipeline, setup builders
├── Generator/        # Source generator (parses Setup() → generates tables + world)
└── Smoke/            # Smoke tests
```

Derp.Ecs generates strongly-typed archetype tables from a declarative `Setup()` method. Each archetype stores components in structure-of-arrays layout for cache-friendly iteration. All structural changes (spawn/destroy) are queued and played back deterministically.

## Defining a World

Create a `partial` class with a `[Conditional]` `Setup` method:

```csharp
using System.Diagnostics;
using DerpLib.Ecs.Setup;

public sealed partial class SimEcsWorld
{
    [Conditional(DerpEcsSetupConstants.ConditionalSymbol)]
    private static void Setup(DerpEcsSetupBuilder b)
    {
        b.Archetype<Enemy>()
            .Capacity(1000)
            .With<TransformComponent>()
            .With<CombatComponent>()
            .Spatial(position: nameof(TransformComponent.Position),
                     cellSize: 32, gridSize: 64, originX: -1024, originY: -1024)
            .QueryRadius(position: nameof(TransformComponent.Position), maxResults: 256);

        b.Archetype<Projectile>()
            .Capacity(512)
            .With<ProjectileDataComponent>()
            .SpawnQueueCapacity(256)
            .DestroyQueueCapacity(128);
    }
}
```

The generator produces:
- `EnemyTable` / `ProjectileTable` classes with SoA arrays
- `EnemyPendingSpawn` / `ProjectilePendingSpawn` ref structs
- `ref` component accessors (name = component name minus `Component` suffix)
- `TryQueueSpawn()` / `QueueDestroy()` structural command APIs
- `QueryRadius()` / `QueryAabb()` spatial query methods (if declared)
- `PlaybackStructuralChanges()` on the world

## Components

Plain structs implementing a marker interface:

```csharp
public struct TransformComponent : ISimComponent    // Snapshotted for rollback
{
    public Fixed64Vec2 Position;
    public Fixed64Vec2 Velocity;
}

public struct RenderComponent : IViewComponent      // Never snapshotted
{
    public float Alpha;
}
```

| Interface | Snapshotted | Use |
|-----------|-------------|-----|
| `ISimComponent` | Yes | Deterministic game state |
| `ISimDerivedComponent` | No (recomputed) | Derived/cached values |
| `IViewComponent` | No | Presentation only |

## Systems

Implement `IEcsSystem<TWorld>`:

```csharp
public sealed class MovementSystem : IEcsSystem<SimEcsWorld>
{
    public void Update(SimEcsWorld world)
    {
        for (int row = 0; row < world.Enemy.Count; row++)
        {
            ref var t = ref world.Enemy.Transform(row);
            t.Position += t.Velocity * world.DeltaTime;
        }
    }
}
```

Run via `EcsSystemPipeline<TWorld>`:

```csharp
var pipeline = new EcsSystemPipeline<SimEcsWorld>(new IEcsSystem<SimEcsWorld>[]
{
    new SpawnSystem(),
    new MovementSystem(),
    new CollisionSystem(),
});

// Each frame:
pipeline.RunFrame(world);  // Runs each system, calls PlaybackStructuralChanges() between
```

## Spawn / Destroy

Queued per system, played back after:

```csharp
// Spawn
if (world.Enemy.TryQueueSpawn(out var spawn))
{
    spawn.Transform.Position = pos;
    spawn.Combat.Health = Fixed64.FromInt(100);
}

// Destroy
world.Enemy.QueueDestroy(entityHandle);
```

Entities spawned/destroyed are not visible until next system runs.

## EntityHandle

64-bit packed identifier: `[Flags:8 | Generation:16 | RawId:24 | KindId:16]`

- Survives row compaction (indices change, handle stays valid)
- Generation increments on reuse (old handles become invalid)
- Use `TryGetRow(handle, out int row)` to resolve

## Spatial Queries

Declare in `Setup()` with `.Spatial()` + `.QueryRadius()` / `.QueryAabb()`:

```csharp
// Rebuild once per frame after positions change
world.Enemy.RebuildSpatialIndex();

// Query (zero allocation)
Span<EntityHandle> hits = stackalloc EntityHandle[256];
int count = world.Enemy.QueryRadius(center, radius, hits);
```

## Project Setup (csproj)

```xml
<ProjectReference Include="..\..\Shared\DerpLib.Ecs\Runtime\Derp.Ecs.Runtime.csproj" />
<ProjectReference Include="..\..\Shared\DerpLib.Ecs\Generator\Derp.Ecs.Generator.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

## Key Rules

1. **Iterate `.Count`, never `.Capacity`** - rows are densely packed
2. **Use `ref` accessors** - `ref var t = ref table.Transform(row)` for zero-copy mutation
3. **Fixed64 for all sim math** - no float/double in deterministic code
4. **No structural changes during iteration** - use `TryQueueSpawn` / `QueueDestroy`
5. **Deterministic iteration order** - row index order, guaranteed

## Reference Example

See `Games/DerpTanks/Simulation/` for a complete working example with archetypes, components, systems, and spatial queries.
