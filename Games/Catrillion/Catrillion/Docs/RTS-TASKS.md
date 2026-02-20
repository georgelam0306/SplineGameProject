# RTS Implementation Tasks

This document provides a detailed implementation plan for transforming Catrillion into a TAB-style co-op RTS. Tasks are organized into phases with dependencies.

**Reference**: See `RTS-SPEC.md` for detailed specifications.

---

## Phase 0: Cleanup & Preparation

### 0.1 Delete Puck System Files ✅
**Estimated Effort**: Low (file deletion)

Delete the following files:
- [x] `Catrillion/Simulation/Components/PuckRow.cs`
- [x] `Catrillion/Simulation/Components/PickupRow.cs`
- [x] `Catrillion/Simulation/Components/SuperPickupRow.cs`
- [x] `Catrillion/Simulation/Systems/Puck/PuckMovementSystem.cs`
- [x] `Catrillion/Simulation/Systems/Puck/PuckPlayerCollisionSystem.cs`
- [x] `Catrillion/Simulation/Systems/Puck/PuckWallCollisionSystem.cs`
- [x] `Catrillion/Simulation/Systems/Puck/PuckPuckCollisionSystem.cs`
- [x] `Catrillion/Simulation/Systems/Puck/PuckEnemyCollisionSystem.cs`
- [x] `Catrillion/Simulation/Systems/Pickup/PickupCollisionSystem.cs`
- [x] `Catrillion/Simulation/Systems/Pickup/SuperPickupSystem.cs`
- [x] `Catrillion/Simulation/Abilities/PuckAbilitySystem.cs`
- [x] `Catrillion/Simulation/Abilities/AbilityDefinitions.cs`
- [x] `Catrillion/Simulation/Systems/Rules/PuckSpawnRulesSystem.cs`
- [x] `Catrillion/Simulation/Systems/Rules/PickupSpawnRulesSystem.cs`
- [x] Delete `Catrillion/Simulation/Systems/Puck/` directory
- [x] Delete `Catrillion/Simulation/Systems/Pickup/` directory
- [x] Delete `Catrillion/Simulation/Abilities/` directory

### 0.2 Delete Player Direct-Control Files ✅
**Estimated Effort**: Low (file deletion)

- [x] Delete `Catrillion/Simulation/Components/PlayerRow.cs`
- [x] Delete `Catrillion/Simulation/Systems/PlayerMovementSystem.cs`
- [x] Delete `Catrillion/Simulation/Systems/PlayerEnemyCollisionSystem.cs`
- [x] Delete `Catrillion/Simulation/Systems/PlayerRevivalSystem.cs`
- [x] Delete `Catrillion/Camera/Systems/PlayerFollowCameraSystem.cs` *(also removed)*

### 0.3 Rename UnitRow -> ZombieRow ✅
**Estimated Effort**: Low

- [x] Rename `UnitRow.cs` to `ZombieRow.cs` *(ZombieRow.cs exists with new struct)*
- [x] Rename struct from `UnitRow` to `ZombieRow`
- [x] Update all references in existing code *(UnitRow.cs deleted, systems updated)*
- [x] Build and fix compilation errors

**Also deleted legacy systems:**
- `RandomMovementSystem.cs`
- `SimTableVelocityResetSystem.cs`
- `UnitDeathSystem.cs`

---

## Phase 1: Core SimRow Infrastructure

### 1.1 Create Type Enumerations ✅
**Estimated Effort**: Low
**Dependencies**: None
**File**: `Catrillion/Simulation/Components/UnitTypes.cs`

Create enums:
- [x] `UnitTypeId` (Soldier, Ranger, Heavy, Sniper, Engineer) *(auto-generated from UnitTypeData)*
- [x] `BuildingTypeId` (CommandCenter, Barracks, Wall, Gate, Turret, etc.) *(auto-generated from BuildingTypeData)*
- [x] `ZombieTypeId` (Walker, Runner, Fatty, Spitter, Doom) *(auto-generated from ZombieTypeData)*
- [x] `ProjectileTypeId` (Bullet, Arrow, Shell, Rocket, AcidSpit) *(ProjectileType enum in ProjectileRow.cs)*
- [x] `ResourceTypeId` (Gold, Energy) *(ResourceTypeId.cs)*
- [x] `OrderType` (None, Move, AttackMove, Hold) *(in CombatUnitRow.cs)*

### 1.2 Create CombatUnitRow ✅
**Estimated Effort**: Medium
**Dependencies**: 1.1
**File**: `Catrillion/Simulation/Components/CombatUnitRow.cs`

Fields implemented:
- [x] Transform fields (Position, Velocity, SmoothedSeparation)
- [x] Identity fields (TypeId, OwnerPlayerId)
- [x] Core stats (Health, MaxHealth, MoveSpeed) *(via ConfigRefresh/CachedStat)*
- [x] Combat stats (Damage, AttackRange, BaseAttackCooldown, Armor) *(via CachedStat)*
- [x] Combat state (AttackTimer, TargetHandle)
- [x] Orders (CurrentOrder, OrderTarget, GroupId)
- [x] Veterancy (VeterancyLevel, KillCount)
- [x] State flags (IsActive, IsDead, DeathFrame) *(via MortalFlags)*
- [x] Selection (SelectedByPlayerId)

Verify:
- [x] Build project - SimWorld.g.cs should regenerate with CombatUnitRowTable

### 1.2.5 Create MapConfigData (Replaces TileConstants) ✅
**Estimated Effort**: Low
**Dependencies**: None
**Files**:
- `Catrillion.GameData/Schemas/MapConfigData.cs`
- `Catrillion.GameData/Data/MapConfig.json`

Create GameDocDb schema for map configuration (singleton with Id=0):
- [x] `TileSize = 32` (pixels per tile)
- [x] `WidthTiles = 256`
- [x] `HeightTiles = 256`
- [x] `ChunkSize = 64` (tiles per chunk)
- [x] DualGrid settings: `AtlasTileWidth`, `AtlasTileHeight`, `AtlasColumns`, `ChunkLoadRadius`
- [x] Accessed via `gameData.Db.MapConfigData.FindById(0)`

### 1.3 Create BuildingRow (Tile-Based) ✅
**Estimated Effort**: Medium
**Dependencies**: 1.1, 1.2.5
**File**: `Catrillion/Simulation/Components/BuildingRow.cs`

Fields implemented:
- [x] **Tile position** (TileX, TileY as ushort)
- [x] Size fields (Width, Height as byte)
- [x] Identity fields (TypeId via BuildingTypeId, OwnerPlayerId)
- [x] Stats (Health, MaxHealth via CachedStat from BuildingTypeData)
- [x] Combat - turrets (Damage, AttackRange, AttackCooldown, TargetHandle, AttackTimer)
- [x] Production (ProductionQueue0-1, ProductionProgress, RallyPoint)
- ~~[x] Construction (ConstructionProgress via BuildingFlags)~~ *(N/A - buildings are instant)*
- [x] State (BuildingFlags: IsActive, RequiresPower, IsPowered)

Verify:
- [x] Build project - SimWorld.g.cs includes BuildingRowTable

### 1.4 Update ZombieRow (Previously UnitRow) ✅
**Estimated Effort**: Medium
**Dependencies**: 1.1, 0.3
**File**: `Catrillion/Simulation/Components/ZombieRow.cs`

Fields implemented:
- [x] TypeId (ZombieTypeId)
- [x] MoveSpeed, InfectionDamage *(via ConfigRefresh/CachedStat)*
- [x] Health, MaxHealth, Damage, AttackRange *(via CachedStat)*
- [x] Position, Velocity, SmoothedSeparation, FacingAngle
- [x] AI State: State, StateTimer, WanderDirectionSeed, AttackCooldown, TargetHandle, TargetType
- [x] NoiseAttraction, AggroLevel
- [x] ZoneId, Flow (for pathfinding)
- [x] Flags, DeathFrame

Verify:
- [x] Build and ensure no regressions

### 1.5 Create ProjectileRow ✅
**Estimated Effort**: Low
**Dependencies**: 1.1
**File**: `Catrillion/Simulation/Components/ProjectileRow.cs`

Fields implemented:
- [x] Transform (Position, Velocity)
- [x] Identity (Type, OwnerPlayerId, SourceHandle)
- [x] Combat (Damage, SplashRadius, PierceCount)
- [x] Targeting (TargetHandle, HomingStrength)
- [x] Lifetime (LifetimeFrames, MaxRange, DistanceTraveled)
- [x] State (Flags with IsActive, IsHoming)

ProjectileType enum extended: Bullet, Arrow, Shell, Rocket, AcidSpit

### 1.6 Create PlayerStateRow ✅
**Estimated Effort**: Low
**Dependencies**: 1.1
**File**: `Catrillion/Simulation/Components/PlayerStateRow.cs`

Fields implemented (SimDataTable):
- [x] Identity (PlayerSlot as byte)
- [x] Resources (Gold, Energy, MaxEnergy)
- [x] Population (Population, MaxPopulation)
- [x] Tech tree (UnlockedTech as ulong bitflags)
- [x] Map state (CameraPosition as Fixed64Vec2)
- [x] State (PlayerFlags: IsActive, IsReady, IsAlive)

### 1.7 Create ResourceNodeRow (Tile-Based) ✅
**Estimated Effort**: Low
**Dependencies**: 1.1, 1.2.5
**File**: `Catrillion/Simulation/Components/ResourceNodeRow.cs`

Fields implemented:
- [x] **Tile position** (TileX, TileY as ushort)
- [x] Identity (TypeId as ResourceTypeId)
- [x] Resources (RemainingAmount, MaxAmount, HarvestRate)
- [x] Harvesting (HarvesterCount as byte)
- [x] State (ResourceNodeFlags: IsActive, IsDepleted)

### 1.8 Create CommandQueueRow ✅
**Estimated Effort**: Medium
**Dependencies**: 1.1
**File**: `Catrillion/Simulation/Components/CommandQueueRow.cs`

Fields implemented:
- [x] Owner (OwnerHandle as SimHandle)
- [x] 4 order slots (Order0-3: Type, Target, TargetHandle)
- [x] Queue state (QueueLength, CurrentIndex as byte)
- [x] State (CommandQueueFlags: IsActive)

### 1.9 Update WaveStateRow ✅
**Estimated Effort**: Low
**Dependencies**: 1.1
**File**: `Catrillion/Simulation/Components/WaveStateRow.cs`

Fields implemented (SimDataTable):
- [x] Wave tracking (CurrentWave, NextSpawnFrame, ActiveZombieCount)
- [x] Trickle spawning (TrickleSpawnBudget, TrickleSpawnCooldown, TrickleSpawnRate)
- [x] Horde spawning (HordeZombieCount, HordeSpawnedCount, HordeDirection)
- [x] Population (MaxZombieCount)
- [x] State (WaveStateFlags: IsActive, HordeActive, WaveInProgress)

### 1.10 Create MapGridRow (Tile Grid Data) ✅
**Estimated Effort**: Medium
**Dependencies**: 1.2.5
**Files**:
- `Catrillion/Simulation/Components/NoiseGridStateRow.cs`
- `Catrillion/Simulation/Components/ThreatGridStateRow.cs`

Create singleton for pre-computed tile data (see RTS-SPEC.md Section 1.9):
- [x] NoiseGrid *(NoiseGridStateRow - 32x32 Fixed64 grid, 256px per cell)*
- [x] ThreatGrid *(ThreatGridStateRow - 64x64 Fixed64 grid + PeakThreat memory, 128px per cell)*

**Not serialized** (derived state, rebuild at runtime from BuildingRows):
- ~~PowerGrid~~ - derived from power plant BFS
- ~~BuildingGrid~~ - derived from BuildingRows tile positions
- ~~PassabilityGrid~~ - not needed
- ~~FlowFieldDirty~~ - not needed

**Note**: Only NoiseGrid and ThreatGrid are serialized because they have temporal state (decay over time). Spatial lookups like power/building grids are cheap to rebuild.

### 1.11 Build Verification ✅
**Estimated Effort**: Low
**Dependencies**: 1.1-1.9

- [x] Run `dotnet build Catrillion/Catrillion.csproj`
- [x] Verify all new tables appear in `SimWorld.g.cs` (BuildingRowTable, PlayerStateRowTable, ResourceNodeRowTable, CommandQueueRowTable)
- [x] Verify no compilation errors (0 errors, 156 warnings)
- [ ] Run basic smoke test

---

## Phase 2: Input System Changes

### 2.1 Extend GameInput Struct (Partial)
**Estimated Effort**: Medium
**Dependencies**: Phase 1 complete
**File**: `Catrillion/Rollback/GameInput.cs`

Changes (see RTS-SPEC.md Part 3):
- [x] Add camera control fields (CameraX, CameraY, CameraZoom)
- [x] Add command input (CommandX, CommandY, CommandType, CommandModifiers) *(MoveTarget, HasMoveCommand exist)*
- [x] Add selection fields (SelectionX1/Y1/X2/Y2, IsBoxSelecting, ClickedEntityId) *(SelectStart, SelectEnd, IsSelecting exist)*
- [x] Add building placement (BuildingTypeToBuild, BuildingPlacementX/Y, ConfirmBuildPlacement)
- [ ] Add control group support (ControlGroupAction, ControlGroupNumber)
- [ ] Add UI actions (CancelAction)
- [x] Update `Equals()` and `GetHashCode()` for new fields
- [x] Update serialization for rollback

### 2.2 Create RTSInputManager (Merged into GameInputManager)
**Estimated Effort**: High
**Dependencies**: 2.1
**File**: `Catrillion/Input/RTSInputManager.cs`

Implement:
- [x] Mouse position to world coordinate conversion *(in GameInputManager)*
- [x] Box selection input (click and drag) *(in GameInputManager)*
- [x] Right-click command detection (move/attack) *(in GameInputManager)*
- [x] Building placement mode
- [ ] Control group hotkeys (Ctrl+1-9 assign, 1-9 select)
- [x] Camera pan with edge-of-screen or WASD *(handled in CameraManager)*
- [x] Camera zoom with scroll wheel *(handled in CameraManager)*
- [x] Return populated `GameInput` struct *(in GameInputManager)*

### 2.3 Update GameInputManager or Replace ✅
**Estimated Effort**: Medium
**Dependencies**: 2.2
**File**: `Catrillion/Input/GameInputManager.cs`

- [x] Remove old WASD movement polling
- [x] Integrate RTSInputManager *(merged into GameInputManager directly)*
- [x] Ensure deterministic input capture

### 2.4 Building Placement System (TAB-Style)
**Estimated Effort**: High
**Dependencies**: 2.1, Phase 4.1 (BuildingRow exists)
**Design**: Command Center at (0,0) always selected by default. When CC selected, shows buildable buildings in UI. Click build option → enter placement mode → click map to place.

#### Phase 2.4.A: Get Something Visible (UI First) ✅
| Step | File | Status | Description |
|------|------|--------|-------------|
| A1 | `BuildingRow.cs` | [x] | Add `SelectedByPlayerId` field |
| A2 | `BuildingSpawnSystem.cs` | [x] | Spawn Command Center at (0,0) on frame 0 |
| A3 | `BuildingRenderSystem.cs` | [x] | Render buildings using sprites from assets |
| A3b | `Assets/Buildings/` | [x] | Generate placeholder sprites with ImageMagick |
| A4 | `SelectionSystem.cs` | [x] | Add building selection (iterate BuildingRows) |
| A5 | `SelectionSystem.cs` | [x] | Default to Command Center if nothing selected |
| A6 | `GameplayStore.cs` | [x] | Create with BuildModeType reactive property |
| A7 | `RootStore.cs` | [x] | Add GameplayStore |

#### Phase 2.4.B: Make It Functional ✅
| Step | File | Status | Description |
|------|------|--------|-------------|
| B1 | `GameInput.cs` | [x] | Add placement fields (TileX/Y, TypeToBuild, HasPlacement) |
| B2 | `GameInput.cs` | [x] | Update Equals() and GetHashCode() |
| B3 | `GameInputManager.cs` | [x] | Handle build mode (LMB confirm, ESC cancel) |
| B4 | `GameInputManager.cs` | [x] | Convert mouse to tile coords |
| B5 | `BuildingPlacementSystem.cs` | [x] | Process HasBuildingPlacement, allocate BuildingRow |
| B6 | `BuildingPreviewRenderSystem.cs` | [x] | Ghost building at cursor |

#### Phase 2.4.C: Polish & Validation (Partial)
| Step | File | Status | Description |
|------|------|--------|-------------|
| C1 | `BuildingPlacementValidator.cs` | [x] | Check bounds, building/unit overlap *(validation in BuildingPlacementSystem + BuildingPreviewRenderSystem)* |
| C2 | `BuildingPlacementSystem.cs` | [ ] | Validate + deduct gold *(validation done, gold deduction not implemented)* |
| C3 | `BuildingPreviewRenderSystem.cs` | [x] | Red ghost when invalid |

---

## Phase 3: Core Systems - Movement & Combat

### 3.1 Create UnitStatService
**Estimated Effort**: Medium
**Dependencies**: 1.1
**File**: `Catrillion/Simulation/Services/UnitStatService.cs`

Implement static stat lookup:
- [ ] `GetUnitStats(UnitTypeId)` - returns HP, damage, range, speed, etc.
- [ ] `GetBuildingStats(BuildingTypeId)` - returns HP, armor, power, etc.
- [ ] `GetZombieStats(ZombieTypeId)` - returns HP, damage, speed, etc.
- [ ] `GetUnitCost(UnitTypeId)` - gold cost to train
- [ ] `GetBuildingCost(BuildingTypeId)` - gold cost to build
- [ ] `GetUnitBuildTime(UnitTypeId)` - frames to train
~~- [ ] `GetBuildingBuildTime(BuildingTypeId)` - buildings are instant~~

### 3.2 Create UnitCommandSystem (Partial - MoveCommandSystem) ✅
**Estimated Effort**: High
**Dependencies**: Phase 2 complete, 3.1
**File**: `Catrillion/Simulation/Systems/RTS/MoveCommandSystem.cs`

Implement:
- [x] Read `GameInput.HasMoveCommand` and `MoveTarget`
- [x] Find units owned by current player *(via SelectedByPlayerId)*
- [x] Set `CombatUnitRow.CurrentOrder` based on command type
- [x] Set `CombatUnitRow.OrderTarget` position
- [x] Allocate MoveCommandRow and assign GroupId to selected units
- [ ] Handle attack-move (set TargetStableId if enemy clicked)
- [ ] Handle shift-click waypoint queuing (use CommandQueueRow)

### 3.3 Create UnitMovementSystem ✅
**Estimated Effort**: High
**Dependencies**: 3.1, 3.2
**File**: `Catrillion/Simulation/Systems/RTS/CombatUnitMovementSystem.cs`

Implement:
- [x] Iterate CombatUnitRow where CurrentOrder != None
- [x] Calculate direction to OrderTarget
- [x] Set Velocity based on MoveSpeed and direction
- [x] Stop when within range of target
- [ ] Handle Hold order (don't move) *(not implemented yet)*
- [ ] Pop from CommandQueueRow when order complete *(clears GroupId on arrival)*

### 3.4 Create UnitCombatSystem ✅
**Estimated Effort**: High
**Dependencies**: 3.1
**File**: `Catrillion/Simulation/Systems/RTS/CombatUnitCombatSystem.cs`

Implement:
- [x] Iterate CombatUnitRow with TargetHandle valid
- [x] Check if target in range
- [x] Decrement AttackTimer each frame
- [x] When timer reaches 0:
  - [x] Spawn homing projectile toward target
  - [x] Calculate impact time based on distance/speed
  - [x] Reset timer based on BaseAttackCooldown
- [x] Clear TargetHandle if target dies or out of range
- [x] Auto-acquire new target (always targets closest in range)

### 3.5 Create TargetAcquisitionSystem ✅
**Estimated Effort**: Medium
**Dependencies**: 3.1
**File**: `Catrillion/Simulation/Systems/RTS/CombatUnitTargetAcquisitionSystem.cs`

Implement:
- [x] Iterate CombatUnitRow (idle or attack-move order)
- [x] Use spatial grid (QueryRadius) to find nearby ZombieRow
- [x] Select closest zombie within AttackRange
- [x] Set TargetHandle
- [x] Re-evaluate every frame to target closest enemy

### 3.6 Update ZombieAISystem ✅
**Estimated Effort**: Medium
**Dependencies**: 3.1
**Files**:
- `Catrillion/Simulation/Systems/ZombieStateTransitionSystem.cs`
- `Catrillion/Simulation/Systems/ZombieMovementSystem.cs`

Implemented as state machine (Idle → Wander → Chase → Attack):
- [x] State transitions based on threat level and target proximity
- [x] Use ZombieTypeId for MoveSpeed lookup *(via CachedStat)*
- [x] Target acquisition via spatial query (QueryRadius)
- [x] Set TargetHandle and TargetType
- [x] Set Velocity based on state (idle=0, wander=slow, chase=full)
- [x] Chase uses threat grid direction + center flow field fallback
- [x] Direct pursuit when target within steering range
- [x] Target buildings first, then units *(Phase 1: buildings via QueryRadius, Phase 2: units if no building found)*
- [ ] Handle different zombie types (Runner = faster, etc.) *(stats loaded but no type-specific behavior)*

### 3.7 Create ZombieCombatSystem ✅
**Estimated Effort**: Medium
**Dependencies**: 3.1
**Files**:
- `Catrillion/Simulation/Systems/ZombieStateTransitionSystem.cs` (Attack state)
- `Catrillion/Simulation/Systems/ZombieCombatSystem.cs` (Damage application)

Implement:
- [x] Attack state entered when target in AttackRange
- [x] AttackCooldown managed via StateTimer
- [x] Deal damage to CombatUnitRow targets (ZombieCombatSystem)
- [x] Deal damage to BuildingRow targets *(ZombieCombatSystem handles both target types)*
- [x] Clear target if destroyed or out of range

### 3.8 Create BuildingTurretSystem
**Estimated Effort**: Medium
**Dependencies**: 3.1
**File**: `Catrillion/Simulation/Systems/RTS/BuildingTurretSystem.cs`

Implement:
- [ ] Iterate BuildingRow where TypeId == Turret or SniperTower
- [ ] Skip if !IsPowered
- [ ] Find closest zombie in range
- [ ] Manage AttackCooldown
- [ ] Spawn projectile when firing
- [ ] Update FacingAngle toward target

### 3.9 Update ProjectileMovementSystem ✅
**Estimated Effort**: Medium
**Dependencies**: 1.5
**File**: `Catrillion/Simulation/Systems/ProjectileSystem.cs`

Implemented:
- [x] Update position based on velocity each frame
- [x] Handle homing projectiles (blend velocity toward target direction)
- [x] Track DistanceTraveled
- [x] Despawn when MaxRange exceeded
- [x] Proximity-based hit detection (within 16px of target)
- [x] Apply damage on hit
- [x] Fallback lifetime expiration (if target dies/invalid)
- [ ] Splash damage *(not yet implemented)*

### 3.10 Create DeathCleanupSystem (Partial) ✅
**Estimated Effort**: Low
**Dependencies**: 3.4, 3.7
**Files**:
- `Catrillion/Simulation/Systems/MortalDeathSystem.cs` (CombatUnit + Zombie)
- `Catrillion/Simulation/Systems/BuildingDeathSystem.cs` (Buildings)

Implement:
- [x] Iterate all entity tables *(MortalDeathSystem uses IMortal multi-table query)*
- [x] Free slots where IsDead == true and DeathFrame is old enough *(10 frames for units, 30 for buildings)*
- [ ] Handle veterancy XP award on zombie kill
- [ ] Handle resource drops if applicable

---

## Phase 4: Buildings & Production

### 4.1 Create BuildingPlacementValidator ✅
**Estimated Effort**: Medium
**Dependencies**: 1.3
**Files**:
- `Catrillion/Simulation/Systems/RTS/BuildingPlacementSystem.cs` (validation + placement)
- `Catrillion/Rendering/Systems/BuildingPreviewRenderSystem.cs` (preview validation)

Implement:
- [x] Check if position is valid (within map bounds)
- [x] Check if area is clear of other buildings
- [ ] Check if area is clear of units *(buildings checked, units not)*
- [ ] Check terrain requirements (if applicable) *(no terrain system)*
- [x] Return validation result *(color-coded preview: green=valid, red=invalid)*

### 4.2 Building Construction ✅ (Instant)
**Note**: Buildings are placed instantly (no construction time). Construction handled by BuildingPlacementSystem.

- [x] Read building placement commands from GameInput *(BuildingPlacementSystem)*
- [x] Validate placement *(BuildingPlacementSystem + BuildingPreviewRenderSystem)*
- [ ] Deduct resources from PlayerStateRow *(not yet implemented)*
- [x] Allocate BuildingRow *(BuildingPlacementSystem - instant, no construction time)*

### 4.3 Create BuildingProductionSystem
**Estimated Effort**: High
**Dependencies**: 3.1
**File**: `Catrillion/Simulation/Systems/RTS/BuildingProductionSystem.cs`

Implement:
- [ ] Iterate BuildingRow where TypeId == Barracks (or other production buildings)
- [ ] Skip if !IsPowered or IsUnderConstruction
- [ ] If ProductionQueue0 != None:
  - [ ] Increment ProductionProgress
  - [ ] Check against unit build time
  - [ ] When complete:
    - [ ] Allocate CombatUnitRow at RallyPoint
    - [ ] Initialize unit stats from UnitStatService
    - [ ] Shift queue (Queue0 = Queue1, etc.)
    - [ ] Deduct population from PlayerStateRow

### 4.4 Create UnitTrainingInputSystem
**Estimated Effort**: Medium
**Dependencies**: 4.3
**File**: `Catrillion/Simulation/Systems/RTS/UnitTrainingInputSystem.cs`

Implement:
- [ ] Read training commands from GameInput
- [ ] Find selected Barracks building
- [ ] Add unit type to production queue (ProductionQueue0-3)
- [ ] Deduct gold cost from PlayerStateRow
- [ ] Handle queue full case

### 4.5 Create ResourceGenerationSystem
**Estimated Effort**: Low
**Dependencies**: 1.6
**File**: `Catrillion/Simulation/Systems/RTS/ResourceGenerationSystem.cs`

Implement:
- [ ] Iterate BuildingRow where TypeId == ResourceGenerator
- [ ] Skip if !IsPowered or IsUnderConstruction
- [ ] Every N frames, add gold to owner's PlayerStateRow.Gold
- [ ] Consider diminishing returns or cap

### 4.6 Create Grid Cache Systems (Non-Serialized)
**Estimated Effort**: Medium
**Dependencies**: 1.3

These maintain **local caches** (not serialized) for O(1) tile lookups. Rebuilt after snapshot load or when buildings change and during rollback.

#### 4.6.1 BuildingGridCache
**File**: TBD (possibly service or transient state)

- [ ] 256x256 ushort array (not in SimWorld, just local cache)
- [ ] Rebuild when buildings placed/destroyed
- [ ] Rebuild after snapshot load
- [ ] O(1) lookup: "what building is at tile X,Y"

#### 4.6.2 PowerGridCache
**File**: TBD

- [ ] 256x256 byte array or just per-building IsPowered flag
- [ ] Rebuild via BFS from power plants when buildings change
- [ ] Mark BuildingRow.IsPowered flag based on connectivity

**Note**: These are caches, not serialized state. They can be class fields or services, not SimDataTable rows.

---

## Phase 5: Zombie Waves & Spawning

### 5.1 Update ZombieSpawnSystem (Partial)
**Estimated Effort**: High
**Dependencies**: 3.1
**File**: `Catrillion/Simulation/Systems/RTS/EnemySpawnSystem.cs`

Current implementation:
- [x] Spawn initial wave of zombies on game start
- [x] Deterministic random positioning across map (excluding safe zone)
- [x] Initialize ZombieRow with stats from GameDocDb (ZombieTypeData)
- [x] Initialize AI state (Idle with random timer)

Still needed:
- [ ] Read WaveStateRow for spawn parameters
- [ ] Trickle spawning:
  - [ ] Every TrickleSpawnCooldown frames, spawn TrickleSpawnRate zombies
  - [ ] Spawn at random positions along map edges
  - [ ] Decrement TrickleSpawnBudget
- [ ] Horde spawning:
  - [ ] When HordeActive, spawn many zombies from HordeDirection edge
  - [ ] Spawn over multiple frames (e.g., 100 per frame)
  - [ ] Increment HordeSpawnedCount until reaches HordeZombieCount
- [ ] Vary zombie types based on wave difficulty

### 5.2 Create WaveManagementSystem
**Estimated Effort**: Medium
**Dependencies**: 5.1
**File**: `Catrillion/Simulation/Systems/RTS/WaveManagementSystem.cs`

Implement:
- [ ] Track CurrentWave progression
- [ ] Trigger trickle budget replenishment
- [ ] Trigger hordes at specific intervals (e.g., every 5 waves)
- [ ] Scale WaveIntensity based on CurrentWave
- [ ] Update NextWaveFrame for wave timing
- [ ] Check win condition (survive N waves)

---

## Phase 6: Selection & UI Systems

### 6.1 Create SelectionSystem ✅
**Estimated Effort**: High
**Dependencies**: Phase 2
**File**: `Catrillion/Simulation/Systems/RTS/SelectionSystem.cs`

Implement:
- [x] Process box selection from GameInput
- [x] Find all CombatUnitRow within selection box *(uses spatial query)*
- [x] Find all BuildingRow within selection box *(manual bounds check)*
- [x] Filter by OwnerPlayerId (can only select own units/buildings)
- [x] Set IsSelected flag on selected entities *(SelectedByPlayerId field)*
- [x] Clear IsSelected on previously selected
- [x] Default to Command Center if nothing selected
- [ ] Handle single-click selection (ClickedEntityId) *(uses box selection instead)*

### 6.2 Create ControlGroupSystem
**Estimated Effort**: Medium
**Dependencies**: 6.1
**File**: `Catrillion/Simulation/Systems/RTS/ControlGroupSystem.cs`

Implement:
- [ ] Read ControlGroupAction from GameInput
- [ ] Assign: save currently selected units to group N
- [ ] Select: select units in group N
- [ ] Store using PlayerStateRow or separate mechanism
- [ ] Consider: this might be client-side only (non-deterministic)

---

## Phase 7: Advanced Features (TAB Parity)

### 7.1 Create VeterancySystem
**Estimated Effort**: Medium
**Dependencies**: 3.4
**File**: `Catrillion/Simulation/Systems/RTS/VeterancySystem.cs`

Implement:
- [ ] Track kills per CombatUnitRow
- [ ] When KillCount threshold reached, increment VeterancyLevel
- [ ] Apply stat bonuses based on level:
  - Level 1: +10% damage
  - Level 2: +20% damage, +10% HP
  - Level 3: +30% damage, +20% HP, +10% speed
- [ ] Cap at level 3 (Elite)

### 7.2 Create InfectionSystem
**Estimated Effort**: Medium
**Dependencies**: 3.7
**File**: `Catrillion/Simulation/Systems/RTS/InfectionSystem.cs`

Implement:
- [ ] Track infection damage dealt by zombies
- [ ] When AccumulatedDamage exceeds threshold, set IsInfected
- [ ] Tick down InfectionTimer
- [ ] When timer reaches 0:
  - Kill the unit
  - Optionally: spawn a zombie at location (TAB style)

### 7.3 Create NoiseAttractionSystem ✅
**Estimated Effort**: Low (simplified by tile grid!)
**Dependencies**: 4.6.3 (NoiseGridUpdateSystem)
**Files**:
- `Catrillion/Simulation/Systems/NoiseAttractionUpdateSystem.cs`
- `Catrillion/Simulation/Services/NoiseGridService.cs`
- `Catrillion/Simulation/Systems/NoiseDecaySystem.cs`

Implement (now trivial thanks to pre-computed NoiseGrid):
- [x] Iterate ZombieRow
- [x] Convert zombie Position to tile index *(via NoiseGridService.FindHighestNoiseNearby)*
- [x] Read noise from NoiseGridStateRow *(32x32 grid)*
- [x] Update zombie.NoiseAttraction if above threshold
- [ ] Influence target selection (higher noise = higher priority) *(stored, not yet used in targeting)*

**Note**: NoiseDecaySystem handles noise level decay. ThreatGridUpdateSystem + ThreatGridDecaySystem handle threat grid for zombie state transitions.

### 7.4 Create TechTreeSystem
**Estimated Effort**: High
**Dependencies**: 1.6
**File**: `Catrillion/Simulation/Systems/RTS/TechTreeSystem.cs`

Implement:
- [ ] Define tech tree (which techs unlock what)
- [ ] Process research commands from GameInput
- [ ] Deduct gold cost for research
- [ ] Track research progress
- [ ] Set bits in PlayerStateRow.UnlockedTech when complete
- [ ] Gate building/unit availability based on unlocked tech

---

## Phase 8: Integration & Polish

### 8.1 Update GameSimulation RegisterSystems
**Estimated Effort**: Medium
**Dependencies**: All previous phases
**File**: `Catrillion/Simulation/GameSimulation.cs`

- [ ] Register all new systems in correct order
- [ ] Order: Input → Movement → Combat → Death → Spawning → Production
- [ ] Remove any remaining old system references
- [ ] Verify system execution order is deterministic


### 8.4 Create RTS UI Components (Partial) ✅
**Estimated Effort**: High
**Dependencies**: Phase 6
**Files**: Various in `Catrillion/UI/`, `Catrillion/Rendering/Systems/`

- [x] Selection box rendering *(SelectionBoxRenderSystem.cs)*
- [x] Unit health bars *(HealthBarRenderSystem.cs)*
- [x] Resource display (gold, energy, population) *(BottomBarUI.cs)*
- [ ] Minimap with unit positions *(placeholder only in BottomBarUI.cs)*
- [x] Building placement preview *(BuildingPreviewRenderSystem.cs)*
- [ ] Production queue display
- [ ] Control group indicators

### 8.5 Determinism Verification
**Estimated Effort**: Medium
**Dependencies**: All previous

- [ ] Create determinism test suite
- [ ] Run same inputs on two instances, compare state hashes
- [ ] Test rollback with new systems
- [ ] Verify no float/double usage in simulation
- [ ] Verify no LINQ/allocations in hot paths

---

## Dependency Graph

```
Phase 0 (Cleanup)
    │
    ▼
Phase 1 (SimRows)
    │
    ├─────────────────┐
    ▼                 ▼
Phase 2 (Input)   Phase 3.1 (StatService)
    │                 │
    └────────┬────────┘
             │
             ▼
    Phase 3 (Movement & Combat)
             │
             ▼
    Phase 4 (Buildings & Production)
             │
             ▼
    Phase 5 (Waves & Spawning)
             │
             ▼
    Phase 6 (Selection & UI)
             │
             ▼
    Phase 7 (Advanced Features)
             │
             ▼
    Phase 8 (Integration & Polish)
```

---

## Testing Milestones

### Milestone 1: Basic Combat ✅
**After Phase 3**

- [x] Can spawn a CombatUnit
- [x] Can spawn a Zombie
- [x] Unit acquires target and attacks from range
- [x] Unit spawns homing projectile that damages zombie
- [x] Zombie attacks and damages unit (melee)
- [x] Health bars render on damaged units

### Milestone 2: Base Building
**After Phase 4**

- [ ] Can place a building
- [ ] Building constructs over time
- [ ] Barracks trains units
- [ ] Resource generator produces gold
- [ ] Power plant powers buildings

### Milestone 3: Survival Gameplay
**After Phase 5**

- [ ] Zombies spawn continuously
- [ ] Hordes spawn periodically
- [ ] Wave progression increases difficulty
- [ ] Game ends when command center destroyed

### Milestone 4: Full RTS
**After Phase 6**

- [ ] Can select units with box selection
- [ ] Can issue move/attack commands
- [ ] Control groups work
- [ ] Full UI functional

### Milestone 5: TAB Parity
**After Phase 7**

- [ ] Veterancy levels work
- [ ] Infection spreads
- [ ] Noise attracts zombies
- [ ] Tech tree unlocks features

---

## Risk Mitigation

### High Risk: Determinism Bugs
**Mitigation**:
- Add per-system hash checks during development
- Test rollback early and often
- Use Fixed64 everywhere in simulation

### High Risk: Performance with 50k Zombies
**Mitigation**:
- Profile early with stress tests
- Use spatial grid queries (SimTable provides this)
- Limit horde spawn rate to spread across frames

### Medium Risk: Input System Complexity
**Mitigation**:
- Start with simple click-to-move
- Add box selection second
- Add control groups last

### Medium Risk: Building Placement Validation
**Mitigation**:
- Start with simple grid-based placement
- Add obstruction checking iteratively
- Consider simplifying (no terrain, just building collision)

---

## Additional Systems (Not in Original Spec)

The following systems exist in the codebase but were not part of the original spec:

**RTS Systems:**
- `CombatUnitApplyMovementSystem.cs` - Applies velocity to position for combat units
- `SeparationSystem.cs` - Prevents unit stacking/overlap (both CombatUnit and Zombie)
- `UnitSpawnSystem.cs` - Spawns player combat units at game start
- `EnemySpawnSystem.cs` - Spawns initial zombie population
- `MoveCommandRow.cs` - Group move command tracking with LRU eviction
- `CombatUnitTargetAcquisitionSystem.cs` - Auto-targets closest zombie in range
- `CombatUnitCombatSystem.cs` - Spawns projectiles when attack timer ready
- `ProjectileSystem.cs` - Moves projectiles, handles homing, applies damage on hit
- `ZombieCombatSystem.cs` - Applies melee damage from zombies to combat units and buildings

**Building Systems:**
- `BuildingSpawnSystem.cs` - Spawns Command Center at game start
- `BuildingPlacementSystem.cs` - Processes placement input, validates, allocates BuildingRow
- `BuildingDeathSystem.cs` - Handles building death and cleanup after delay
- `GameplayStore.cs` - Manages build mode state (reactive BuildModeType property)

**Rendering Systems:**
- `HealthBarRenderSystem.cs` - Renders health bars above damaged entities
- `ProjectileRenderSystem.cs` - Renders projectiles as colored circles
- `BuildingRenderSystem.cs` - Renders buildings from SimWorld with sprites/fallback
- `BuildingPreviewRenderSystem.cs` - Ghost building at cursor with validation colors
- `DebugSlotInfoRenderSystem.cs` - DEBUG: Renders slot/stableId info, detects orphans/desync
- `DebugVisualizationSystem.cs` - DEBUG: Zombie states, combat unit info, spatial queries
- `DebugVisualizationUISystem.cs` - DEBUG: Toggle controls for debug overlays
- `SelectionBoxRenderSystem.cs` - Renders RTS-style selection box during drag selection
- `ResourceNodeRenderSystem.cs` - Renders resource node entities

**Zombie AI Systems:**
- `ZombieStateTransitionSystem.cs` - State machine (Idle/Wander/Chase/Attack)
- `ZombieMovementSystem.cs` - Movement based on state (flow field, threat grid, direct pursuit)

**Noise/Threat Grid Systems:**
- `NoiseGridStateRow.cs` - 32x32 Fixed64 grid for noise levels
- `ThreatGridStateRow.cs` - 64x64 Fixed64 grid for threat + peak threat memory
- `NoiseGridService.cs` - Noise grid query utilities
- `ThreatGridService.cs` - Threat grid query utilities
- `NoiseDecaySystem.cs` - Decays noise over time
- `NoiseAttractionUpdateSystem.cs` - Updates zombie NoiseAttraction from grid
- `ThreatGridUpdateSystem.cs` - Updates threat from CombatUnit positions + noise spillover
- `ThreatGridDecaySystem.cs` - Decays threat over time

**Shared/Utility Systems:**
- `VelocityResetSystem.cs` - Resets velocities before movement calculations
- `MortalDeathSystem.cs` - Handles entity death processing
- `CountdownSystem.cs` - Game countdown/ready state management
- `MoveableApplyMovementSystem.cs` - Multi-table movement application
- `FlowFieldInvalidationSystem.cs` - Invalidates flow fields when buildings change
- `EnemyFlowMovementSystem.cs` - Flow-field-based enemy movement

**Terrain/Resource Systems:**
- `TerrainGenerationSystem.cs` - Procedural terrain generation
- `ResourceNodeSpawnSystem.cs` - Spawns resource nodes at game start

**UI Systems:**
- `BottomBarUI.cs` - TAB-style bottom bar (minimap placeholder, selection info, build buttons, resources)

---

## Revision History

| Date | Version | Changes |
|------|---------|---------|
| 2025-12-17 | 1.0 | Initial task breakdown |
| 2025-12-20 | 1.1 | Audit completed - marked done tasks. Phase 0.1 ✅, Phase 1.4 ✅, Phase 2.3 ✅, Phase 3.3 ✅, Phase 6.1 ✅, Phase 7.3 ✅. Partial progress on 0.2, 0.3, 1.1, 1.2, 1.5, 1.10, 2.1, 2.2, 3.2. Added Noise/Threat grid systems to additional systems list. |
| 2025-12-22 | 1.2 | Deep audit of codebase. Updated: 1.2 CombatUnitRow (added ConfigRefresh/CachedStat details), 1.4 ZombieRow (AI state fields), 1.5 ProjectileRow (detailed field status), 1.10 MapGridRow (NoiseGrid + ThreatGrid done), 3.2 MoveCommandSystem ✅, 3.6 ZombieAISystem ✅ (state machine impl), 3.7 ZombieCombatSystem partial, 5.1 EnemySpawnSystem partial. Added zombie AI systems and utility systems to additional systems list. |
| 2025-12-22 | 1.3 | **Phase 1 Complete!** Created MapConfigData schema (replaces TileConstants), BuildingRow, PlayerStateRow, ResourceNodeRow, CommandQueueRow. Updated WaveStateRow with trickle/horde fields. All TypeId enums now auto-generated from GameDocDb schemas. Migrated EnemySpawnSystem and UnitSpawnSystem to use MapConfigData. Build verified with 0 errors. |
| 2025-12-22 | 1.4 | **Milestone 1: Basic Combat Complete!** Implemented full combat loop: CombatUnitTargetAcquisitionSystem (3.5 ✅), CombatUnitCombatSystem (3.4 ✅), ProjectileSystem (3.9 ✅), ZombieCombatSystem (3.7 ✅). Added HealthBarRenderSystem and ProjectileRenderSystem. Fixed projectile hit detection (proximity-based instead of timing). Fixed target re-evaluation (always targets closest). Changed Range stat to world units (no tile conversion). |
| 2025-12-23 | 1.5 | **Building Placement Complete!** Updated Phase 2.4.A/B to ✅ (all except BuildPanelUI). Phase 2.4.C partial (validation done, gold deduction pending). Phase 3.7 ZombieCombatSystem now handles BuildingRow targets ✅. Phase 3.10 MortalDeathSystem + BuildingDeathSystem handle entity cleanup ✅. Phase 4.1 BuildingPlacementValidator merged into placement systems ✅. Added Building Systems section to Additional Systems. Fixed critical SimTable generator bug: GetTypeSize now handles enum underlying types correctly (was causing MortalFlags memory corruption). |
| 2025-12-24 | 1.6 | **Zombie Pathfinding & Combat Fixes!** Fixed zombie flow field navigation to buildings (seed perimeter tiles, not blocked interior). Fixed attack range calculation to use building edge distance instead of center. **Phase 8.4 UI Partial** ✅: SelectionBoxRenderSystem, HealthBarRenderSystem, BottomBarUI (resource display, build buttons), BuildingPreviewRenderSystem. Added new systems: TerrainGenerationSystem, ResourceNodeSpawnSystem, FlowFieldInvalidationSystem, EnemyFlowMovementSystem, DebugVisualizationUISystem. |
