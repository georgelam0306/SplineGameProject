# Simulation Systems Architecture

This document describes all simulation systems in `DerpTech2D/Systems/Simulation/`, their purpose, dependencies, data flow, and execution order.

## Overview

The simulation runs through `SimulationRoot`, which executes `RegionSystem` instances per-region. Each region owns a set of spatial hash chunks. Non-region systems run via Friflo's `BaseSystem`/`QuerySystem`.

### Execution Model

```
SimulationRoot.Update(region, deltaTime)
  └─ For each RegionSystem:
       └─ system.Update(ref region, deltaTime, simTime)
```

Region systems iterate over `region.OwnedChunkGridIndices` and process entities in those chunks via `SpatialHashService`.

---

## Movement Systems

### UnitMovementSystem

**Purpose**: Moves player-controlled units toward their destination using flow field pathfinding.

**Dependencies**:
- `ZoneFlowService` - provides flow directions
- `ZoneGraph` - wall distance for collision
- `SpatialHashService` - entity positions
- `DeferredCommandBuffer` - remove MoveCommand on arrival

**Data Flow**:
1. For each unit with `MoveCommand`, `SimVelocity`, `UnitFlowCache`
2. Check if within arrival distance of destination
3. If tile changed or velocity is zero, query flow direction:
   - Attack move: `GetFlowDirection()` (toward seeds/targets)
   - Normal move: `GetFlowDirectionForDestination()` (toward specific tile)
4. Set velocity = direction × speed
5. Apply velocity to position with wall collision checks
6. Update `SpatialHashService.PositionsX/Y` directly

**Key Constants**:
- `DefaultSpeed`: 30 (Fixed64)
- `ArrivalDistanceTiles`: 1.5 tiles

**Components Read**: `MoveCommand`, `SimVelocity`, `UnitFlowCache`
**Components Written**: `SimVelocity` (velocity), positions via `SpatialHashService`

---

### EnemyMovementSystem

**Purpose**: Placeholder for enemy-specific movement logic.

**Status**: Currently empty stub. Enemy movement likely handled elsewhere or pending implementation.

---

### UnitIdleSystem

**Purpose**: Applies separation forces to idle units to prevent stacking. Uses local spatial grid for O(1) neighbor lookup within chunks.

**Dependencies**:
- `SpatialHashService` - entity positions
- `ZoneGraph` - wall distance for repulsion

**Data Flow**:
1. Build local 32×32 cell grid within each chunk (cell size = 32px)
2. For each unit (staggered by `FrameSpread` = 4):
   - Accumulate separation forces from neighbors in 3×3 cell neighborhood
   - Accumulate wall repulsion from gradient of wall distance field
3. Apply forces with speed clamping
4. Collision check against walls before applying

**Key Constants**:
- `SeparationRadius`: 32 (Fixed64)
- `MicroSeparationStrength`: 500
- `SteeringSpeed`: 50
- `FrameSpread`: 4 (process 1/4 of entities each frame)

**Algorithm**: Linked-list spatial hashing within chunk for O(1) neighbor iteration.

---

## Combat Systems

### UnitAttackSystem

**Purpose**: Player units attack nearby enemies or walls when in attack mode.

**Dependencies**:
- `SpatialHashService` - find nearby targets
- `StructureService` - get wall entities
- `ZoneGraph` - wall distance for wall targeting
- `DeferredCommandBuffer` - queue damage

**Data Flow**:
1. For each unit with `AttackIntent` or `MoveCommand.IsAttackMove`
2. Search 3×3 chunk neighborhood for `IsAttackTarget` entities
3. Find nearest valid target within `AttackRange` (20px)
4. If no entity target, try attacking nearest wall using wall distance gradient
5. Queue damage via `DeferredCommandBuffer.AddCombatDamage()`

**Key Constants**:
- `UnitDamagePerSecond`: 15
- `AttackRange`: 20px
- `WallAttackRangeTiles`: 1.5 tiles

---

### CombatAttackSystem

**Purpose**: Towers and structures attack enemies by firing projectiles.

**Dependencies**:
- `ProjectileService` - fire projectiles
- `SpatialHashService` - find targets

**Data Flow**:
1. For each entity with `CombatStats`, `Selectable`, `IsAttackTarget`
2. Decrement attack cooldown
3. Search 3×3 chunk neighborhood for non-`IsAttackTarget` entities (enemies)
4. Find closest enemy within `CombatStats.Range`
5. Fire projectile via `ProjectileService.Fire()`
6. Reset cooldown = `1 / AttackSpeed`

**Projectile Color**: Based on tower type (Basic=blue, Heavy=red, HomeBase=gold)

---

### EnemyAttackSystem

**Purpose**: Enemies attack player structures.

**Location**: `DerpTech2D/Systems/Simulation/EnemyAttackSystem.cs`

---

### ProjectileUpdateSystem

**Purpose**: Updates projectile positions and applies damage on hit.

**Dependencies**:
- `ProjectileService` - manages projectile list
- `EntityStore` - get target entities

**Data Flow**:
1. Flush pending projectiles from concurrent queue to active list
2. For each projectile:
   - Update elapsed time
   - Track target position if target still exists
   - If elapsed >= travelTime, apply damage to `EnemyHealth` and remove projectile

---

## Lifecycle Systems

### EnemyDeathSystem

**Purpose**: Deletes enemy entities when health <= 0 or marked for death.

**Dependencies**:
- `EntityStore` - delete entities

**Data Flow**:
1. Query entities with `EnemyHealth`, `UnitTag`
2. If `health.Current <= 0`, queue deletion
3. Query entities with `MarkedForDeath`, `UnitTag`
4. Queue all for deletion
5. Playback command buffer

---

### StructureDeathSystem

**Purpose**: Deletes structures when health <= 0 and invalidates flow fields.

**Dependencies**:
- `ZoneFlowService` - invalidate tiles
- `StructureService` - clear grid cells

**Data Flow**:
1. Query entities with `CombatStats`, `Transform2D`
2. If `health <= 0`:
   - If tower: `InvalidateTile()` on flow service
   - If wall: `ClearGridCell()` and `InvalidateSeedTile()`
   - Delete entity via command buffer

---

### DamageTimeoutSystem

**Purpose**: Removes `RecentlyDamaged` component after timeout.

**Location**: `DerpTech2D/Systems/Simulation/DamageTimeoutSystem.cs`

---

### UnitSpawnSystem

**Purpose**: Spawns test units when Space is pressed.

**Dependencies**:
- `EntityStore` - create entities

**Data Flow**:
1. On Space key press, spawn 100,000 units
2. Each unit gets:
   - `UnitTag`, `Transform2D`, `SimTransform2D`, `SimVelocity`
   - `UnitFlowCache`, `EnemyHealth`, `EnemyMovement`
3. Positions randomized in 16000×16000 area using seeded `Random(42)`

**Determinism Note**: Uses seeded RNG but `Random` class is not deterministic across platforms.

---

## Infrastructure Systems

### ApplyDeferredCommandsSystem

**Purpose**: Applies queued commands from concurrent buffers in deterministic order.

**Dependencies**:
- `DeferredCommandBuffer` - source of deferred commands
- `EntityStore` - apply changes

**Data Flow**:
1. Drain `CombatDamage` queue, sort by entity ID
2. Apply damage to `CombatStats.Health`, add/update `RecentlyDamaged`
3. Drain `DamageMarkers` queue, sort by entity ID
4. Apply damage markers
5. Drain `RemoveMoveCommandIds` queue, sort by entity ID
6. Remove `MoveCommand` components

**Determinism**: Sorting by entity ID ensures deterministic application order regardless of queue insertion order.

---

### SpatialHashPopulateSystem

**Purpose**: Populates spatial hash with entity positions each frame.

**Location**: `DerpTech2D/Systems/Simulation/SpatialHashPopulateSystem.cs`

**Data Flow**:
1. Call `SpatialHashService.BeginFrame()` to clear
2. For each entity with position components
3. Call `InsertEntity()` with entity data

---

### SpatialHashFlushSystem

**Purpose**: Finalizes spatial hash state after all insertions.

**Location**: `DerpTech2D/Systems/Simulation/SpatialHashFlushSystem.cs`

---

### SimToRenderSyncSystem

**Purpose**: Copies simulation transforms (Fixed64) to render transforms (float).

**Location**: `DerpTech2D/Systems/Simulation/SimToRenderSyncSystem.cs`

**Data Flow**:
1. For each entity with `SimTransform2D` and `Transform2D`
2. Copy: `transform.X = simTransform.X.ToFloat()`

---

## Pathfinding Systems

### ZoneFlowUpdateSystem

**Purpose**: Pre-computes flow fields when dirty flag is set.

**Dependencies**:
- `ZoneFlowService` - compute flows

**Data Flow**:
1. If `FlowsDirty` is false, skip
2. Query all entities with `SimTransform2D`, `MoveCommand`, `UnitFlowCache`, `UnitTag`
3. Invalidate each unit's flow cache (`LastTileX/Y = int.MinValue`)
4. Request flow direction to trigger computation:
   - Attack move: `GetFlowDirection()`
   - Normal move: `GetFlowDirectionForDestination()`
5. Clear dirty flag

---

### HierarchicalFlowUpdateSystem

**Purpose**: Same as ZoneFlowUpdateSystem but for hierarchical flow service.

**Dependencies**:
- `HierarchicalFlowService`

**Data Flow**: Identical pattern to ZoneFlowUpdateSystem.

---

## System Execution Order

The recommended execution order for deterministic simulation:

1. **Input Processing** (external)
2. `SpatialHashPopulateSystem` - build spatial hash
3. `SpatialHashFlushSystem` - finalize spatial hash
4. `ZoneFlowUpdateSystem` / `HierarchicalFlowUpdateSystem` - update pathfinding
5. `UnitMovementSystem` - move units
6. `UnitIdleSystem` - separation forces
7. `EnemyMovementSystem` - move enemies
8. `UnitAttackSystem` - unit combat
9. `CombatAttackSystem` - structure combat
10. `EnemyAttackSystem` - enemy combat
11. `ProjectileUpdateSystem` - projectile movement
12. `ApplyDeferredCommandsSystem` - apply queued commands
13. `EnemyDeathSystem` - cleanup dead enemies
14. `StructureDeathSystem` - cleanup dead structures
15. `DamageTimeoutSystem` - cleanup damage markers
16. `SimToRenderSyncSystem` - sync to render state

---

## Component Summary

### Simulation State (Fixed64)
- `SimTransform2D` - position (X, Y)
- `SimVelocity` - velocity (X, Y)

### Render State (float)
- `Transform2D` - position for rendering

### Movement
- `MoveCommand` - destination tile, attack move flag
- `UnitFlowCache` - cached tile position for flow lookup
- `EnemyMovement` - speed

### Combat
- `CombatStats` - health, damage, range, attack speed, cooldown
- `EnemyHealth` - current/max health
- `AttackIntent` - tag for attack mode
- `AttackTarget` - tag for targetable entities
- `RecentlyDamaged` - damage timestamp
- `MarkedForDeath` - pending deletion tag

### Tags
- `UnitTag` - player unit
- `EnemyTag` - enemy unit
- `TowerTag` - tower structure
- `WallTag` - wall structure
- `HomeBaseTag` - home base structure

