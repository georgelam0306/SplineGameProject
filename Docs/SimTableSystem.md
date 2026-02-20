# SimTable Simulation System

This document describes the SimTable system - a data-oriented simulation framework designed for deterministic rollback netcode.

See also: `Docs/DerpEcsSetup.md` for the analogous "syntax-only setup + codegen" pattern used by Derp.Ecs.

## Architecture Overview

The simulation uses a **hybrid architecture**:

- **Simulation** (SimTable): Owns all game state (positions, velocities, health, etc.)
  - Deterministic, blittable, single-memcpy snapshots
  - Spatial sorting for cache-friendly iteration
  - Unmanaged memory via `NativeMemory`
  
- **Rendering** (Friflo ECS): Owns render entities (Transform2D, SpriteRenderer, etc.)
  - Friflo entities reference simulation via `SimUnitRef { int StableId }`
  - `SimToRenderSyncSystem` copies sim state → Friflo each frame

## Why Determinism Matters

This game uses **rollback netcode** for multiplayer. When we receive late input from a remote player:

1. Restore simulation state to N frames ago (rollback)
2. Re-simulate with the corrected inputs
3. Continue from current frame

For this to work, **every client must compute identical results given identical inputs**. Non-determinism causes desyncs.

### Sources of Non-Determinism (Avoid These)

- **Floating-point math**: Use `Fixed64` instead
- **Non-deterministic iteration order**: `HashSet`, `Dictionary` iteration order is undefined
- **Random without seed**: Use seeded RNG
- **Entity ID non-determinism**: Friflo creates entities in non-deterministic order

The SimTable system eliminates the entity ID problem by using **stable IDs via indirection**.

## Creating New Schemas

### Step 1: Define the Schema

Create a new file in `DerpTech2D/Simulation/`:

```csharp
using SimTable;

namespace DerpTech2D.Simulation;

[SimTable(Capacity = 512, CellSize = 16, GridSize = 256)]
public partial struct TowerRow
{
    public Fixed64 X;
    public Fixed64 Y;
    public int Damage;
    public int Range;
    public int FireCooldown;
    public int TargetStableId;
    public bool IsActive;
}
```

### Step 2: Build

The source generator automatically:
- Creates `TowerRowTable` class with SoA storage
- Adds it to `SimWorld` as `SimWorld.TowerRows`
- Includes it in `SimWorld.BeginFrame()` spatial sorting
- Includes it in `SimWorld.SaveTo()`/`LoadFrom()` snapshots

### Attribute Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `Capacity` | 1024 | Max number of entities in this table |
| `CellSize` | 16 | Spatial grid cell size in pixels |
| `GridSize` | 256 | Grid dimensions (GridSize × GridSize cells) |

## Working with Tables

### Allocating Entities

```csharp
var units = simWorld.UnitRows;

// Allocate returns a stable ID
int stableId = units.Allocate();

// Get slot (array index) from stable ID
int slot = units.GetSlot(stableId);

// Set initial values
units.X(slot) = Fixed64.FromInt(100);
units.Y(slot) = Fixed64.FromInt(200);
units.IsActive(slot) = true;
```

### Iterating Entities

**Direct iteration (all entities):**
```csharp
var units = simWorld.UnitRows;
for (int slot = 0; slot < units.Count; slot++)
{
    if (units.GetStableId(slot) < 0) continue; // skip freed slots
    
    units.X(slot) = units.X(slot) + units.VX(slot);
}
```

**Spatial iteration (entities in cells):**
```csharp
for (int cell = 0; cell < UnitRowTable.TotalCells; cell++)
{
    int start = units.GetCellStart(cell);
    int count = units.GetCellCount(cell);
    
    for (int i = start; i < start + count; i++)
    {
        int slot = units.GetSortedSlot(i);
        // Process entity at slot
    }
}
```

### Using RowRef (AoS-style)

```csharp
var row = units.GetRow(stableId);
if (row.IsValid)
{
    row.X = row.X + row.VX;
    row.Y = row.Y + row.VY;
}
```

### Freeing Entities

```csharp
units.Free(stableId);
```

## Tick Order

The simulation tick must follow this order:

```csharp
public void SimulateTick()
{
    // 1. Spatial sort (uses last frame's positions)
    simWorld.BeginFrame();
    
    // 2. Systems execute
    velocityResetSystem.Tick();
    playerInputSystem.Tick();
    enemyFlowSystem.Tick();
    applyMovementSystem.Tick();
    separationSystem.Tick();  // Uses GetCellStart/GetCellCount
    damageSystem.Tick();
    applyForcesSystem.Tick();
    
    // 3. Snapshot for rollback
    snapshot.SaveSnapshot(currentFrame);
}
```

`BeginFrame()` performs counting sort on all tables by spatial cell. This must happen **before** any spatial queries.

## Snapshots and Rollback

### Saving State

```csharp
// SimWorldSnapshot handles slab + metadata serialization
var snapshot = new SimWorldSnapshot(simWorld, maxRollbackFrames);

// Save each frame
snapshot.SaveSnapshot(frameNumber);
```

### Restoring State

```csharp
// Roll back to a previous frame
snapshot.LoadSnapshot(targetFrame);
```

The snapshot includes:
- All column data (single memcpy per table)
- Allocation metadata (free list, stable ID mappings)

## Linking Simulation to Render

### Create Render Entity with SimRef

```csharp
// In simulation: allocate unit
int stableId = simWorld.UnitRows.Allocate();

// In render: create Friflo entity with reference
var entity = store.CreateEntity();
entity.AddComponent(new Transform2D { X = 0, Y = 0 });
entity.AddComponent(new SimUnitRef { StableId = stableId });
entity.AddComponent(new SpriteRenderer { ... });
```

### Sync Each Frame

```csharp
// After simulation tick, before render
syncSystem.Sync();
```

`SimToRenderSyncSystem` iterates all Friflo entities with `SimUnitRef` and copies positions from the simulation table.

## Testing Requirements

Before committing changes to simulation code:

```bash
dotnet test DerpTech2D.Tests/DerpTech2D.Tests.csproj
```

Key test categories:
- `SimTableDeterminismTests`: Spatial sort, snapshot restore, stable IDs
- Existing `Fixed64` determinism tests

## Performance Notes

### Why SoA (Structure of Arrays)?

When iterating positions to compute separation:
- **AoS**: Each entity's data scattered in memory → cache misses
- **SoA**: All X values contiguous → cache hits

### Why Spatial Sorting?

After sorting, entities in the same cell have adjacent array indices:
- Cell (5,5) entities: slots 40, 41, 42, 43
- Iterating neighbors = sequential memory access

### Counting Sort

O(n + cells) complexity, faster than comparison sort for integer keys.

## File Organization

```
SimTable.Annotations/        Attributes for source generator
SimTable.Generator/          Source generator
DerpTech2D/Simulation/       Schema definitions (UnitRow.cs, etc.)
DerpTech2D/Systems/Simulation/  SimTable-based systems
DerpTech2D/Systems/Render/   SimToRenderSyncSystem
```

Generated files appear in `obj/Generated/SimTable.Generator/`:
- `UnitRowTable.g.cs`
- `SimWorld.g.cs`
- etc.

## Migration Status

The SimTable system is now available alongside the existing Friflo-based simulation. New systems prefixed with `SimTable` (e.g., `SimTableSeparationSystem`) demonstrate the new patterns.

### Files to Remove After Full Migration

Once `GameSimulation.cs` is migrated to use `SimWorld`:
- `DerpTech2D/Services/Rollback/WorldSnapshot.cs` → replaced by `SimWorldSnapshot.cs`
- `DerpTech2D/Services/ChunkCellSeparation.cs` → spatial sorting built into tables
- Old Friflo-based simulation systems (non-SimTable prefixed ones)
- Friflo simulation components: `SimTransform2D`, `SimVelocity2D`, `SimHealth`, etc.

The new SimTable-based systems to use:
- `SimTableVelocityResetSystem`
- `SimTableApplyMovementSystem`
- `SimTablePlayerInputSystem`
- `SimTableSeparationSystem`
