# RTS Transformation Specification

## Overview

This document specifies the transformation of Catrillion from a cooperative puck-bouncing survival game into a "They Are Billions"-style multiplayer RTS with rollback netcode.

**Target Game**: Co-op defense RTS where players work together to build a base and survive against zombie hordes.

### Core Features
- **Factions**: Human players (co-op) vs AI zombie hordes
- **Combat Units**: 5 types (Soldier, Ranger, Heavy, Sniper, Engineer)
- **Buildings**: Full TAB-style (Command Center, Barracks, Walls, Gates, Turrets, Sniper Towers, Resource Generators, Power Plants, Tech Labs)
- **Controls**: Mouse-based RTS (box selection, right-click move/attack)
- **Zombie Spawning**: Constant trickle + periodic massive hordes

---

## Part 0: Tile Grid System

### Tile Constants

```csharp
public static class TileConstants
{
    public const int TileSize = 32;           // Pixels per tile
    public const int MapWidthTiles = 256;     // Map width in tiles
    public const int MapHeightTiles = 256;    // Map height in tiles
    public const int TotalTiles = 65536;      // 256 x 256

    // Conversion helpers
    public static int WorldToTileX(Fixed64 worldX) => (int)(worldX / TileSize);
    public static int WorldToTileY(Fixed64 worldY) => (int)(worldY / TileSize);
    public static int TileToIndex(int tileX, int tileY) => tileY * MapWidthTiles + tileX;
    public static Fixed64 TileToWorldX(int tileX) => Fixed64.FromInt(tileX * TileSize + TileSize / 2);
    public static Fixed64 TileToWorldY(int tileY) => Fixed64.FromInt(tileY * TileSize + TileSize / 2);
}
```

### Why Tile-Based?

1. **Buildings are static** - No need for sub-pixel precision, int tile coords save space
2. **O(1) Grid Queries** - Units can look up noise/power/occupancy by tile index instantly
3. **Simplified Pathfinding** - Flow fields align naturally to tiles
4. **TAB-style Gameplay** - Buildings snap to grid, walls connect cleanly

### Grid Data Arrays (in MapGridRow)

Instead of iterating entities to compute noise/power, store pre-computed values per tile:

| Grid | Type | Description |
|------|------|-------------|
| NoiseLevel | byte[65536] | Noise intensity at each tile (for zombie attraction) |
| PowerLevel | byte[65536] | Power availability at each tile (0=unpowered, 255=max) |
| BuildingOccupancy | ushort[65536] | StableId of building at tile (0=empty) |
| Passability | byte[65536] | 0=blocked, 1=passable, 2=gate (opens for friendlies) |

---

## Part 1: SimRow Specifications

### 1.1 CombatUnitRow

Player-controlled military units (soldiers, rangers, etc.).

**Table Configuration**:
```csharp
[SimTable(Capacity = 10_000, CellSize = 16, GridSize = 256)]
```

**Fields**:

| Field | Type | Description |
|-------|------|-------------|
| **Transform** |
| Position | Fixed64Vec2 | World position |
| Velocity | Fixed64Vec2 | Current movement vector |
| FacingAngle | Fixed64 | Direction unit is facing (radians) |
| **Identity** |
| TypeId | UnitTypeId (byte) | Soldier, Ranger, Heavy, Sniper, Engineer |
| OwnerPlayerId | byte | Player slot (0-7) |
| StableId | ushort | Unique identifier for targeting |
| **Core Combat Stats** |
| Health | int | Current health points |
| MaxHealth | int | Maximum health points |
| Damage | int | Base damage per attack |
| AttackRange | Fixed64 | Attack range in world units |
| AttackSpeed | Fixed64 | Attacks per second |
| MoveSpeed | Fixed64 | Movement speed (units per frame) |
| Armor | int | Damage reduction (flat) |
| VisionRange | Fixed64 | Sight radius for fog of war |
| NoiseRadius | int | How far zombies can hear this unit |
| **Combat State** |
| AttackCooldown | int | Frames until next attack allowed |
| TargetStableId | int | Entity being targeted (-1 = none) |
| AccumulatedDamage | Fixed64 | Damage taken (for infection calc) |
| **Orders** |
| CurrentOrder | OrderType (byte) | Move, AttackMove, Patrol, Hold |
| OrderTarget | Fixed64Vec2 | Position target for current order |
| OrderTargetStableId | int | Entity target for attack orders |
| **Veterancy** |
| VeterancyLevel | byte | 0-3 (Rookie, Regular, Veteran, Elite) |
| KillCount | int | Kills toward next level |
| **Infection** |
| InfectionTimer | int | Frames until death/conversion |
| **State** |
| Flags | CombatUnitFlags (byte) | IsActive, IsDead, IsInfected |
| DeathFrame | int | Frame when died (0 = alive) |

**Stat Scaling by Type**:

| Type | HP | Damage | Range | Speed | Armor | Special |
|------|-----|--------|-------|-------|-------|---------|
| Soldier | 100 | 10 | 3 | 2.0 | 0 | Balanced |
| Ranger | 80 | 12 | 6 | 2.5 | 0 | Long range |
| Heavy | 200 | 25 | 2 | 1.2 | 5 | Tank |
| Sniper | 60 | 50 | 10 | 1.8 | 0 | Very long range, slow attack |
| Engineer | 70 | 5 | 2 | 2.0 | 0 | Can repair buildings |

---

### 1.2 BuildingRow

All static structures (walls, turrets, production buildings).

**Table Configuration**:
```csharp
[SimTable(Capacity = 2_000, CellSize = 32, GridSize = 256)]
```

**Fields**:

| Field | Type | Description |
|-------|------|-------------|
| **Tile Position (NOT world coords!)** |
| TileX | ushort | Tile X coordinate (0-255) |
| TileY | ushort | Tile Y coordinate (0-255) |
| Width | byte | Building width in tiles (1-4) |
| Height | byte | Building height in tiles (1-4) |
| FacingAngle | Fixed64 | For turrets (rotation toward target) |
| **Identity** |
| TypeId | BuildingTypeId (byte) | CommandCenter, Barracks, Wall, etc. |
| OwnerPlayerId | byte | Player slot (0-7), 255 = neutral |
| StableId | ushort | Unique identifier |
| **Stats** |
| Health | int | Current health |
| MaxHealth | int | Maximum health |
| Armor | int | Damage reduction |
| **Combat (Turrets only)** |
| Damage | int | Attack damage |
| AttackRangeTiles | byte | Firing range in tiles (cheaper than Fixed64) |
| AttackSpeed | byte | Frames between attacks |
| AttackCooldown | int | Frames until next attack |
| TargetStableId | int | Target zombie (-1 = none) |
| VisionRangeTiles | byte | Detection range in tiles |
| **Production (Barracks/Factory)** |
| ProductionQueue[4] | byte | Production queue (4 slots, uses array syntax) |
| ProductionProgress | int | Frames toward current unit |
| RallyTileX | ushort | Rally point tile X |
| RallyTileY | ushort | Rally point tile Y |
| **Power Grid** |
| PowerProduction | byte | Energy produced (generators) |
| PowerConsumption | byte | Energy consumed |
| **Construction** |
| ConstructionProgress | byte | 0-100 percentage |
| **Noise** |
| NoiseRadius | byte | Noise radius in tiles (written to NoiseGrid) |
| **State** |
| Flags | BuildingFlags (byte) | IsActive, RequiresPower, IsPowered, IsUnderConstruction |

**Notes**:
- World position can be computed: `WorldX = TileX * TileSize + (Width * TileSize / 2)`
- Using `byte` for ranges/speeds saves memory (buildings don't need Fixed64 precision)
- Noise/Power are WRITTEN to MapGridRow grids, then units READ from grids

**Building Types**:

| Type | HP | Armor | Size | Power | Special |
|------|-----|-------|------|-------|---------|
| CommandCenter | 2000 | 10 | 4x4 | +50 | Main building, lose = defeat |
| Barracks | 800 | 5 | 2x2 | -5 | Trains Soldier, Ranger, Heavy |
| Wall | 500 | 10 | 1x1 | 0 | Blocks zombies |
| Gate | 400 | 5 | 1x1 | 0 | Opens for friendlies |
| Turret | 300 | 3 | 1x1 | -2 | Auto-attacks zombies |
| SniperTower | 200 | 0 | 1x1 | -3 | Long range, slow attack |
| ResourceGenerator | 400 | 2 | 2x2 | -5 | Produces gold |
| PowerPlant | 300 | 2 | 2x2 | +30 | Provides power |
| TechLab | 500 | 3 | 2x2 | -10 | Unlocks upgrades |

---

### 1.3 ZombieRow

AI-controlled enemy units.

**Table Configuration**:
```csharp
[SimTable(Capacity = 50_000, CellSize = 16, GridSize = 256)]
```

**Fields**:

| Field | Type | Description |
|-------|------|-------------|
| **Transform** |
| Position | Fixed64Vec2 | World position |
| Velocity | Fixed64Vec2 | Current movement vector |
| FacingAngle | Fixed64 | Direction facing |
| **Identity** |
| TypeId | ZombieTypeId (byte) | Walker, Runner, Fatty, Spitter, Doom |
| StableId | ushort | Unique identifier |
| **Stats** |
| Health | int | Current health |
| MaxHealth | int | Maximum health |
| Damage | int | Attack damage |
| AttackRange | Fixed64 | Attack range |
| AttackSpeed | Fixed64 | Attacks per second |
| MoveSpeed | Fixed64 | Movement speed |
| InfectionDamage | int | Infection dealt to units |
| **AI State** |
| AttackCooldown | int | Frames until next attack |
| TargetStableId | int | Current target (-1 = none) |
| TargetType | byte | 0=Building, 1=Unit |
| AggroLevel | byte | 0=passive, 1=active, 2=enraged |
| NoiseAttraction | int | Accumulated noise attraction |
| **Pathfinding** |
| ZoneId | int | Flow field zone |
| Flow | Fixed64Vec2 | Flow field direction |
| **State** |
| Flags | ZombieFlags (byte) | IsActive, IsDead |
| DeathFrame | int | Frame when died |

**Zombie Types**:

| Type | HP | Damage | Speed | Special |
|------|-----|--------|-------|---------|
| Walker | 50 | 5 | 1.0 | Standard zombie |
| Runner | 30 | 3 | 3.0 | Fast but weak |
| Fatty | 500 | 20 | 0.5 | Slow tank |
| Spitter | 40 | 15 | 1.2 | Ranged attack |
| Doom | 2000 | 50 | 0.8 | Boss zombie |

---

### 1.4 ProjectileRow

Bullets, arrows, and explosions.

**Table Configuration**:
```csharp
[SimTable(Capacity = 2_000, CellSize = 16, GridSize = 256)]
```

**Fields**:

| Field | Type | Description |
|-------|------|-------------|
| **Transform** |
| Position | Fixed64Vec2 | Current position |
| Velocity | Fixed64Vec2 | Movement vector |
| FacingAngle | Fixed64 | Direction of travel |
| **Identity** |
| TypeId | ProjectileTypeId (byte) | Bullet, Arrow, Shell, Rocket |
| OwnerPlayerId | byte | Who fired this |
| SourceStableId | int | Firing unit/building |
| **Combat** |
| Damage | int | Damage on impact |
| SplashRadius | Fixed64 | 0 = single target |
| PierceCount | int | Targets before despawn |
| **Targeting** |
| TargetStableId | int | Homing target (-1 = dumb) |
| HomingStrength | Fixed64 | Turn rate for homing |
| **Lifetime** |
| LifetimeFrames | int | Remaining frames |
| MaxRange | Fixed64 | Max travel distance |
| DistanceTraveled | Fixed64 | Distance traveled |
| **State** |
| Flags | ProjectileFlags (byte) | IsActive |

---

### 1.5 PlayerStateRow

Per-player resources, tech, and status.

**Table Configuration**:
```csharp
[SimTable(Capacity = 8, CellSize = 16, GridSize = 1)]
```

**Fields**:

| Field | Type | Description |
|-------|------|-------------|
| **Identity** |
| PlayerSlot | byte | Player index (0-7) |
| **Resources** |
| Gold | int | Primary currency |
| Energy | int | Current available power |
| MaxEnergy | int | Total power capacity |
| **Population** |
| Population | int | Current unit count |
| MaxPopulation | int | Housing capacity |
| **Tech Tree** |
| UnlockedTech | ulong | Bitflags for 64 techs |
| **Map State** |
| CommandCenterPos | Fixed64Vec2 | Main base location |
| CameraPosition | Fixed64Vec2 | Player's camera |
| **State** |
| Flags | PlayerFlags (byte) | IsActive, IsReady, IsAlive |

---

### 1.6 ResourceNodeRow

Harvestable map resources (gold mines, stone deposits).

**Table Configuration**:
```csharp
[SimTable(Capacity = 500, CellSize = 32, GridSize = 256)]
```

**Fields**:

| Field | Type | Description |
|-------|------|-------------|
| **Transform** |
| Position | Fixed64Vec2 | World position |
| **Identity** |
| TypeId | ResourceTypeId (byte) | Gold, Stone, Oil |
| StableId | ushort | Unique identifier |
| **Resources** |
| RemainingAmount | int | Resources left |
| MaxAmount | int | Initial amount |
| HarvestRate | int | Gold per harvest tick |
| **Harvesting** |
| HarvesterCount | int | Workers currently harvesting |
| **State** |
| Flags | ResourceNodeFlags (byte) | IsActive, IsDepleted |

---

### 1.7 CommandQueueRow

Queued orders for units (shift-click waypoints).

**Table Configuration**:
```csharp
[SimTable(Capacity = 1_000, CellSize = 16, GridSize = 1)]
```

**Fields**:

| Field | Type | Description |
|-------|------|-------------|
| **Owner** |
| OwnerStableId | int | Unit owning this queue |
| **Queue Slots (8 max, uses array syntax)** |
| OrderTypes[8] | byte | Order type for each slot (OrderType enum) |
| OrderTargets[8] | Fixed64Vec2 | Position target for each order |
| OrderTargetStableIds[8] | int | Entity target for each attack order |
| **Queue State** |
| QueueLength | byte | Number of queued orders |
| CurrentIndex | byte | Currently executing order |
| **State** |
| Flags | CommandQueueFlags (byte) | IsActive |

---

### 1.8 WaveStateRow (Singleton)

Zombie wave management.

**Table Configuration**:
```csharp
[SimTable(Capacity = 1, CellSize = 16, GridSize = 1)]
```

**Fields**:

| Field | Type | Description |
|-------|------|-------------|
| **Wave Progression** |
| CurrentWave | int | Current wave number |
| NextWaveFrame | int | Frame when next wave spawns |
| WaveIntensity | int | Difficulty multiplier (1-100) |
| **Trickle Spawning** |
| TrickleSpawnBudget | int | Zombies to spawn gradually |
| TrickleSpawnCooldown | int | Frames between spawns |
| TrickleSpawnRate | int | Zombies per spawn event |
| **Horde Spawning** |
| HordeZombieCount | int | Total zombies in horde |
| HordeSpawnedCount | int | How many spawned |
| HordeDirection | byte | Which edge (0-3) |
| **Population** |
| ActiveZombieCount | int | Current living zombies |
| MaxZombieCount | int | Performance cap |
| **State** |
| Flags | WaveStateFlags (byte) | IsActive, HordeActive |

---

### 1.9 MapGridRow (Singleton) - Tile-Based Data

Pre-computed per-tile data for O(1) queries. Updated by systems, read by units/zombies.

**Table Configuration**:
```csharp
[SimTable(Capacity = 1, CellSize = 16, GridSize = 1)]
```

**Fields**:

| Field | Type | Description |
|-------|------|-------------|
| **Noise Grid** |
| NoiseGrid | byte[65536] | Noise level at each tile (0-255). Zombies attracted to higher values. |
| **Power Grid** |
| PowerGrid | byte[65536] | Power level at each tile. 0=unpowered, >0=powered. Connected to power plants. |
| **Building Occupancy** |
| BuildingGrid | ushort[65536] | StableId of building occupying tile. 0=empty. Multi-tile buildings mark all occupied tiles. |
| **Passability** |
| PassabilityGrid | byte[65536] | 0=blocked (building/wall), 1=passable, 2=gate (context-dependent) |
| **State** |
| Flags | MapGridFlags (byte) | FlowFieldDirty |

**Grid Index Calculation**:
```csharp
int index = tileY * 256 + tileX;
byte noise = mapGrid.NoiseGrid[index];
```

**Update Systems**:
- **NoiseGridUpdateSystem**: Iterates buildings with NoiseRadius > 0, writes to NoiseGrid. Clears first, then adds.
- **PowerGridUpdateSystem**: Flood-fill from PowerPlants, marks connected tiles. Uses BuildingGrid to find connections.
- **PassabilityUpdateSystem**: Writes 0 for tiles in BuildingGrid, 1 for empty, 2 for gates.

**Query Pattern for Units**:
```csharp
// Zombie checking noise at its position
int tileX = TileConstants.WorldToTileX(zombie.Position.X);
int tileY = TileConstants.WorldToTileY(zombie.Position.Y);
int idx = TileConstants.TileToIndex(tileX, tileY);
byte noise = mapGrid.NoiseGrid[idx];
zombie.NoiseAttraction += noise;
```

**Memory Size**:
- 4 grids × 65536 bytes = 256 KB + 1 bool = ~256 KB per snapshot
- Much cheaper than iterating entities

---

### 1.10 ResourceNodeRow (Tile-Based)

Harvestable map resources (gold mines, stone deposits). Now tile-based.

**Table Configuration**:
```csharp
[SimTable(Capacity = 500, CellSize = 32, GridSize = 256)]
```

**Fields**:

| Field | Type | Description |
|-------|------|-------------|
| **Tile Position** |
| TileX | ushort | Tile X coordinate |
| TileY | ushort | Tile Y coordinate |
| **Identity** |
| TypeId | ResourceTypeId (byte) | Gold, Stone, Oil |
| StableId | ushort | Unique identifier |
| **Resources** |
| RemainingAmount | int | Resources left |
| MaxAmount | int | Initial amount |
| HarvestRate | int | Gold per harvest tick |
| **Harvesting** |
| HarvesterCount | byte | Workers currently harvesting (max 255) |
| **State** |
| Flags | ResourceNodeFlags (byte) | IsActive, IsDepleted |

---

## Part 1b: SimTable Array Syntax

### Array Field Attribute

For fields that represent fixed-size arrays (like production queues, order queues, etc.), use the `[Array(n)]` attribute:

```csharp
[SimTable(Capacity = 2_000)]
public partial struct BuildingRow
{
    public ushort TileX;
    public ushort TileY;

    [Array(4)]
    public byte ProductionQueue;  // Expands to 4 contiguous bytes per slot
}
```

### Generated Code

The generator produces:

**1. Individual named accessors:**
```csharp
public ref byte ProductionQueue0(int slot) => ...;
public ref byte ProductionQueue1(int slot) => ...;
public ref byte ProductionQueue2(int slot) => ...;
public ref byte ProductionQueue3(int slot) => ...;
```

**2. Indexed accessor:**
```csharp
public ref byte ProductionQueue(int slot, int index) => ...;
```

**3. Span accessor (for iteration/bulk ops):**
```csharp
public Span<byte> ProductionQueueArray(int slot) => ...; // Returns Span<byte> of length 4
```

**4. RowRef properties:**
```csharp
public ref byte ProductionQueue0 => ref _table.ProductionQueue0(_slot);
public ref byte ProductionQueue1 => ref _table.ProductionQueue1(_slot);
// ... etc
public Span<byte> ProductionQueueArray => _table.ProductionQueueArray(_slot);
```

### Memory Layout

Array elements are stored **contiguously per-slot** (AoS within the field):

```
Offset: [slot0_e0, slot0_e1, slot0_e2, slot0_e3, slot1_e0, slot1_e1, ...]
```

This ensures cache-friendly access when reading/writing all elements for one entity, which is the common case for production queues, order queues, etc.

### Bitpacking Considerations

For boolean flags and small integers, consider grouping into a single `uint` or `ulong` with manual bit manipulation:

```csharp
public uint BuildingFlags;  // Pack: RequiresPower(1), IsPowered(1), IsUnderConstruction(1), ConstructionProgress(7), NoiseRadius(4) = 14 bits
```

Or use the array syntax for multiple small values:
```csharp
[Array(8)]
public byte OrderTypes;  // 8 order types in queue
```

---

## Part 2: Type Enumerations

### 2.1 Flag Enums

```csharp
namespace Catrillion.Simulation.Components;

[Flags]
public enum CombatUnitFlags : byte
{
    None = 0,
    IsActive = 1 << 0,
    IsDead = 1 << 1,
    IsInfected = 1 << 2,
}

[Flags]
public enum BuildingFlags : byte
{
    None = 0,
    IsActive = 1 << 0,
    RequiresPower = 1 << 1,
    IsPowered = 1 << 2,
    IsUnderConstruction = 1 << 3,
}

[Flags]
public enum ZombieFlags : byte
{
    None = 0,
    IsActive = 1 << 0,
    IsDead = 1 << 1,
}

[Flags]
public enum ProjectileFlags : byte
{
    None = 0,
    IsActive = 1 << 0,
}

[Flags]
public enum PlayerFlags : byte
{
    None = 0,
    IsActive = 1 << 0,
    IsReady = 1 << 1,
    IsAlive = 1 << 2,
}

[Flags]
public enum ResourceNodeFlags : byte
{
    None = 0,
    IsActive = 1 << 0,
    IsDepleted = 1 << 1,
}

[Flags]
public enum CommandQueueFlags : byte
{
    None = 0,
    IsActive = 1 << 0,
}

[Flags]
public enum WaveStateFlags : byte
{
    None = 0,
    IsActive = 1 << 0,
    HordeActive = 1 << 1,
}

[Flags]
public enum MapGridFlags : byte
{
    None = 0,
    FlowFieldDirty = 1 << 0,
}
```

### Generated Extension Methods

The `[GenerateFlags]` attribute triggers source generation of ergonomic extension methods:

```csharp
// For CombatUnitFlags with IsActive, IsDead, IsInfected:

// Check flags
if (unit.Flags.IsActive()) { ... }
if (unit.Flags.IsDead()) { ... }
if (unit.Flags.IsInfected()) { ... }

// Set flags (returns new value for assignment)
unit.Flags = unit.Flags.WithDead();           // Set IsDead
unit.Flags = unit.Flags.WithoutInfected();    // Clear IsInfected
unit.Flags = unit.Flags.SetActive(false);     // Conditional

// For flags without "Is" prefix (e.g., HordeActive):
if (wave.Flags.IsHordeActive()) { ... }
wave.Flags = wave.Flags.WithHordeActive();
wave.Flags = wave.Flags.SetHordeActive(true);
```

**Generated methods per flag:**
| Flag Name | Check | Set | Clear | Conditional |
|-----------|-------|-----|-------|-------------|
| `IsXxx` | `IsXxx()` | `WithXxx()` | `WithoutXxx()` | `SetXxx(bool)` |
| `Xxx` | `IsXxx()` | `WithXxx()` | `WithoutXxx()` | `SetXxx(bool)` |

---

### 2.2 UnitTypes.cs

```csharp
namespace Catrillion.Simulation.Components;

public enum UnitTypeId : byte
{
    Soldier = 0,
    Ranger = 1,
    Heavy = 2,
    Sniper = 3,
    Engineer = 4,
}

public enum BuildingTypeId : byte
{
    CommandCenter = 0,
    Barracks = 1,
    Wall = 2,
    Gate = 3,
    Turret = 4,
    SniperTower = 5,
    ResourceGenerator = 6,
    PowerPlant = 7,
    TechLab = 8,
    HousingComplex = 9,
}

public enum ZombieTypeId : byte
{
    Walker = 0,
    Runner = 1,
    Fatty = 2,
    Spitter = 3,
    Doom = 4,
}

public enum ProjectileTypeId : byte
{
    Bullet = 0,
    Arrow = 1,
    Shell = 2,
    Rocket = 3,
    AcidSpit = 4,
}

public enum ResourceTypeId : byte
{
    Gold = 0,
    Stone = 1,
    Oil = 2,
}

public enum OrderType : byte
{
    None = 0,
    Move = 1,
    AttackMove = 2,
    Patrol = 3,
    Hold = 4,
    Harvest = 5,
    Repair = 6,
}
```

---

## Part 3: Input System Changes

### 3.1 GameInput Struct

Replace current WASD-focused input with RTS commands:

```csharp
public struct GameInput : IGameInput<GameInput>, IEquatable<GameInput>
{
    // Camera control
    public Fixed64Vec2 CameraPosition;
    public Fixed64 CameraZoom;

    // Command input
    public Fixed64Vec2 CommandPosition; // World position of click
    public byte CommandType;            // OrderType enum
    public byte CommandModifiers;       // Shift=1, Ctrl=2, Alt=4

    // Selection
    public Fixed64Vec2 SelectionStart;  // Box selection start
    public Fixed64Vec2 SelectionEnd;    // Box selection end
    public bool IsBoxSelecting;
    public int ClickedEntityId;         // Single-click selection

    // Building placement
    public byte BuildingTypeToBuild;
    public Fixed64Vec2 BuildingPlacementPosition;
    public bool ConfirmBuildPlacement;

    // Control groups (1-9)
    public byte ControlGroupAction;     // 0=none, 1=select, 2=assign
    public byte ControlGroupNumber;

    // UI actions
    public bool CancelAction;
    public bool PausePressed;
}
```

---

## Part 4: Systems Overview

### Core Systems (Must Implement)

| System | Description | Priority |
|--------|-------------|----------|
| **UnitCommandSystem** | Processes player commands, assigns orders | P0 |
| **UnitMovementSystem** | Executes move orders, pathfinding | P0 |
| **UnitCombatSystem** | Attack cooldowns, damage dealing | P0 |
| **ZombieAISystem** | Flow field pathfinding, target selection | P0 |
| **ZombieCombatSystem** | Zombie attacks on buildings/units | P0 |
| **BuildingTurretSystem** | Auto-targeting and firing | P0 |
| **BuildingProductionSystem** | Train units from barracks | P0 |
| **ProjectileMovementSystem** | Move projectiles, check collisions | P0 |
| **DeathCleanupSystem** | Free dead entities | P0 |
| **ZombieSpawnSystem** | Trickle and horde spawning | P0 |

### Secondary Systems (Important)

| System | Description | Priority |
|--------|-------------|----------|
| **SelectionSystem** | Handle box/click selection | P1 |
| **ResourceGenerationSystem** | Income from generators | P1 |
| **PowerGridSystem** | Calculate power connectivity | P1 |
| **ConstructionSystem** | Building placement and progress | P1 |
| **VeterancySystem** | XP, level ups, stat bonuses | P1 |
| **InfectionSystem** | Infection spread and death | P1 |

### Polish Systems (Nice to Have)

| System | Description | Priority |
|--------|-------------|----------|
| **NoiseAttractionSystem** | Zombies attracted to noise | P2 |
| **TechTreeSystem** | Research and unlocks | P2 |
| **FormationSystem** | Unit formations | P2 |
| **FogOfWarSystem** | Vision-based fog | P2 |

---

## Part 5: Files to Delete

### Puck-Related Files
```
Catrillion/Simulation/Components/PuckRow.cs
Catrillion/Simulation/Components/PickupRow.cs
Catrillion/Simulation/Components/SuperPickupRow.cs
Catrillion/Simulation/Systems/Puck/PuckMovementSystem.cs
Catrillion/Simulation/Systems/Puck/PuckPlayerCollisionSystem.cs
Catrillion/Simulation/Systems/Puck/PuckWallCollisionSystem.cs
Catrillion/Simulation/Systems/Puck/PuckPuckCollisionSystem.cs
Catrillion/Simulation/Systems/Puck/PuckEnemyCollisionSystem.cs
Catrillion/Simulation/Systems/Pickup/PickupCollisionSystem.cs
Catrillion/Simulation/Systems/Pickup/SuperPickupSystem.cs
Catrillion/Simulation/Abilities/PuckAbilitySystem.cs
Catrillion/Simulation/Abilities/AbilityDefinitions.cs
Catrillion/Simulation/Systems/Rules/PuckSpawnRulesSystem.cs
Catrillion/Simulation/Systems/Rules/PickupSpawnRulesSystem.cs
```

### Player Direct-Control Files (Replace)
```
Catrillion/Simulation/Components/PlayerRow.cs (-> PlayerStateRow)
Catrillion/Simulation/Components/UnitRow.cs (-> ZombieRow)
Catrillion/Simulation/Systems/PlayerMovementSystem.cs (-> UnitMovementSystem)
Catrillion/Simulation/Systems/PlayerEnemyCollisionSystem.cs (replace with combat systems)
Catrillion/Simulation/Systems/PlayerRevivalSystem.cs (remove - no revival in RTS)
```

---

## Part 6: Data Relationships

```
PlayerStateRow (8)
  └── owns → CombatUnitRow (many)
  └── owns → BuildingRow (many)

CombatUnitRow
  └── targets → ZombieRow (via TargetStableId)
  └── has → CommandQueueRow (via OwnerStableId)
  └── fires → ProjectileRow

BuildingRow
  └── produces → CombatUnitRow (Barracks)
  └── targets → ZombieRow (Turrets)
  └── fires → ProjectileRow (Turrets)

ZombieRow
  └── targets → BuildingRow OR CombatUnitRow (via TargetStableId + TargetType)
  └── uses → Flow field (existing service)

ProjectileRow
  └── damages → ZombieRow OR CombatUnitRow OR BuildingRow

WaveStateRow (singleton)
  └── spawns → ZombieRow

ResourceNodeRow
  └── harvested by → CombatUnitRow (Engineers)
  └── generates → PlayerStateRow.Gold
```

---

## Part 7: Determinism Considerations

### Critical Rules

1. **All positions/velocities use Fixed64** - never float/double in simulation
2. **No LINQ or lambdas** in hot paths - use explicit loops
3. **Deterministic iteration order** - SimTable provides sorted iteration via spatial grid
4. **No allocations** in tick methods - pre-allocate all buffers
5. **StableIds for references** - slots change during free, stable IDs don't
6. **Frame-based timing** - all cooldowns in frames, not seconds/milliseconds

### Snapshot Size Estimates

| Table | Rows | Size/Row | Total |
|-------|------|----------|-------|
| CombatUnitRow | 10,000 | ~80 bytes | ~800 KB |
| BuildingRow | 2,000 | ~50 bytes | ~100 KB |
| ZombieRow | 50,000 | ~60 bytes | ~3 MB |
| ProjectileRow | 2,000 | ~50 bytes | ~100 KB |
| PlayerStateRow | 8 | ~60 bytes | ~0.5 KB |
| ResourceNodeRow | 500 | ~20 bytes | ~10 KB |
| CommandQueueRow | 1,000 | ~200 bytes | ~200 KB |
| WaveStateRow | 1 | ~40 bytes | ~0.04 KB |
| **MapGridRow** | **1** | **~262 KB** | **~262 KB** |
| **Total** | | | **~4.5 MB** |

**MapGridRow Breakdown**:
- NoiseGrid: 65,536 bytes
- PowerGrid: 65,536 bytes
- BuildingGrid: 131,072 bytes (ushort)
- PassabilityGrid: 65,536 bytes
- Total: ~320 KB (plus overhead)

Current rollback buffer: 10MB per frame, 7 frames = 70MB. Comfortable fit.

**Tile-Based Savings**:
- BuildingRow: Fixed64Vec2 Position (16 bytes) → TileX/TileY (4 bytes) = 12 bytes saved × 2000 = 24 KB
- ResourceNodeRow: Same savings = ~6 KB
- Offset by MapGridRow, but grid enables O(1) queries - worth the trade

---

## Part 8: Open Questions

1. **Fog of War**: Should we implement vision-based fog, or keep full map visibility for co-op?
2. **Shared Resources**: Do all co-op players share resources, or track separately?
3. **Repair Mechanic**: Should Engineers be able to repair buildings?
4. **Rally Points**: Should production buildings have configurable rally points?
5. **Control Groups**: Support traditional 1-9 hotkeys for unit groups?
6. **Minimap**: Should selection show on minimap?

---

## Revision History

| Date | Version | Changes |
|------|---------|---------|
| 2025-12-17 | 1.0 | Initial specification |
| 2025-12-17 | 1.1 | Added tile-based architecture: TileConstants, BuildingRow uses TileX/TileY, MapGridRow for NoiseGrid/PowerGrid/BuildingGrid/PassabilityGrid, ResourceNodeRow tile-based |
| 2025-12-17 | 1.2 | Added [Array(n)] attribute for fixed-size arrays in SimTable generator |
| 2025-12-17 | 1.3 | Consolidated X/Y pairs to Fixed64Vec2 (GameInput), consolidated bools to flag enums (CombatUnitFlags, BuildingFlags, ZombieFlags, etc.) |
| 2025-12-17 | 1.4 | Added [GenerateFlags] source generator for ergonomic flag extension methods |
