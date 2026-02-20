# Catrillion Architecture

Minimal spatial ECS prototype for rollback netcode experiments.

---

## Project Overview

Catrillion is a lightweight prototype designed to test deterministic simulation and rollback netcode patterns before integrating them into the main DerpTech2D project.

### Core Design Principles

1. **Deterministic Simulation**: All game logic uses `Fixed64` math to ensure identical results across machines
2. **Rollback Netcode**: Full state snapshots enable rewinding and re-simulating when late inputs arrive
3. **Dual World Architecture**: Separation between simulation state (SimWorld) and rendering state (ECS)
4. **Zero-Allocation Hot Paths**: No GC pressure during gameplay

---

## Directory Structure

```
Catrillion/
├── Core/               # App lifecycle and orchestration
│   ├── Application.cs  # Entry point
│   ├── Game.cs         # Main game loop
│   ├── AppComposition.cs    # DI container setup
│   ├── GameComposition.cs   # Game-specific DI
│   ├── ScreenManager.cs     # Screen state machine
│   └── LoadingManager.cs    # Loading coordinator
│
│   # Note: Math utilities (Fixed64, vectors, random) are in shared Core/ project
│
├── Simulation/         # Deterministic game logic
│   ├── Components/     # SimTable row structs (UnitRow, etc.)
│   ├── Systems/        # Simulation systems (movement, combat)
│   ├── SimWorld.cs     # Auto-generated simulation state container
│   └── GameSimulation.cs
│
├── Rollback/           # Netcode infrastructure
│   ├── WorldSnapshot.cs      # Snapshot serialization
│   ├── SnapshotBuffer.cs     # Ring buffer for state history
│   ├── RollbackManager.cs    # Rollback/resimulation logic
│   ├── GameInput.cs          # Input structure
│   └── MultiPlayerInputBuffer.cs
│
├── OnlineServices/     # Networking
│   ├── NetworkService.cs     # Low-level networking
│   ├── NetworkCoordinator.cs # Peer management
│   └── NetMessages.cs        # Message definitions
│
├── Rendering/          # Visual presentation
├── Entities/           # ECS entity definitions
├── Input/              # Input handling
├── Camera/             # Camera systems
├── UI/                 # User interface
├── AppState/           # State machine
└── Config/             # Configuration
```

---

## Simulation Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                        Frame Update                              │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  1. Receive Network Inputs                                       │
│     └─> Check for late inputs requiring rollback                 │
│                                                                  │
│  2. Rollback (if needed)                                         │
│     ├─> Restore snapshot from SnapshotBuffer                     │
│     └─> Re-simulate frames with corrected inputs                 │
│                                                                  │
│  3. Apply Local Input                                            │
│     └─> Buffer input for current frame                           │
│                                                                  │
│  4. Simulation Tick (SimWorld)                                   │
│     ├─> Run simulation systems                                   │
│     └─> All logic uses Fixed64 math                              │
│                                                                  │
│  5. Save Snapshot                                                │
│     └─> Serialize SimWorld to SnapshotBuffer                     │
│                                                                  │
│  6. Sync to ECS                                                  │
│     └─> Copy SimWorld state to Friflo ECS components             │
│                                                                  │
│  7. Render (ECS World)                                           │
│     └─> ECS systems render based on synced state                 │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## Key Components

### SimWorld (Simulation State)

Auto-generated container holding all simulation tables:

```csharp
// Tables are generated from [SimTable] structs
simWorld.UnitRows      // UnitTable
simWorld.GameRulesRows // GameRulesTable
```

Usage pattern:
```csharp
var units = simWorld.UnitRows;
int slot = units.Allocate();
var row = units.GetRow(slot);
row.X = Fixed64.FromInt(100);
```

### Snapshot System

- **WorldSnapshot**: Serializes SimWorld to byte[]
- **SnapshotBuffer**: Ring buffer storing N frames of history
- **RollbackManager**: Orchestrates rollback and resimulation

### Networking

- **P2P Mesh**: All clients connect to each other
- **Input Sharing**: Clients share inputs, not state
- **Determinism**: Identical inputs produce identical results

---

## Performance Targets

| Metric | Target |
|--------|--------|
| Snapshot serialize | < 1ms |
| Snapshot deserialize | < 1ms |
| 7-frame rollback | < 10ms |
| Simulation tick | < 1.3ms |

---

## Determinism Rules

1. **Fixed64 Only**: Never use float/double in simulation
2. **Ordered Iteration**: Never iterate HashSet/Dictionary
3. **No Time.deltaTime**: Use fixed timestep
4. **Seeded Random**: Use DeterministicRandom with shared seed
5. **No External State**: Simulation reads only from SimWorld

---

## Adding New Simulation State

1. Create a row struct with `[SimTable]` attribute:
   ```csharp
   [SimTable]
   public partial struct MyEntityRow {
       public Fixed64 X;
       public Fixed64 Y;
   }
   ```

2. Build the project - SimWorld.g.cs is auto-updated

3. Access via `simWorld.MyEntityRows`

---

## Related Documentation

- `../Docs/RollbackArchitecture.md` - Detailed rollback implementation
- `../Docs/SimTableSystem.md` - SimTable generator documentation
- `../Docs/DeterminismPlan.md` - Determinism strategy
