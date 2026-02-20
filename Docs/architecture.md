# DerpTech2D Workspace Architecture

High-level architecture overview for context rebuilding on session reload.

---

## Workspace Overview

This workspace contains multiple game projects sharing core infrastructure:

| Project | Description | Status |
|---------|-------------|--------|
| **DerpTech2D** | Main tower defense game | Active development |
| **Catrillion** | Spatial ECS prototype for rollback netcode | Experimental |
| **SurvivorArena** | Gameplay variant (fork of Catrillion) | Experimental |
| **Core** | Shared math utilities (Fixed64, vectors, random) | Stable |

---

## Tech Stack

- **.NET 9** - Runtime
- **Raylib-cs** - Graphics/input
- **Friflo.Engine.ECS** - Entity Component System
- **Derp.DI** - Dependency injection
- **Custom Source Generators** - SimTable, Derp.Ecs, Pooled, GpuStruct, Profiling

---

## Project Structure

```
DerpTech2dRaylibCSharp/
├── Core/                       # Shared math utilities
│   ├── Fixed64.cs              # 64-bit fixed-point math
│   ├── Fixed64Vec2.cs          # 2D vector with Fixed64
│   ├── Fixed32.cs              # 32-bit fixed-point math
│   ├── Fixed32Vec2.cs          # 2D vector with Fixed32
│   ├── FixedMathLUT.cs         # Trig lookup tables
│   ├── DeterministicRandom.cs  # Seeded random for simulation
│   └── ArrayUtils.cs           # Zero-allocation array helpers
│
├── DerpTech2D/                 # Main game
│   ├── Core/                   # Entry point, DI composition
│   ├── Services/               # Game services (flow fields, spawning, networking)
│   ├── Systems/                # ECS systems by category
│   │   ├── Input/
│   │   ├── Camera/
│   │   ├── Render/
│   │   └── Simulation/
│   ├── Components/             # ECS components
│   └── Docs/                   # Project-specific docs
│
├── Catrillion/                 # Rollback netcode prototype
│   ├── Core/                   # App lifecycle, DI composition
│   ├── Simulation/             # Deterministic game logic
│   ├── Rollback/               # Snapshot/rollback infrastructure
│   ├── OnlineServices/         # P2P networking
│   └── Docs/                   # Project-specific docs
│
├── SurvivorArena/              # Gameplay variant (fork of Catrillion)
│   ├── Core/                   # App lifecycle, DI composition
│   ├── Simulation/             # Deterministic game logic
│   ├── Rollback/               # Snapshot/rollback infrastructure
│   ├── OnlineServices/         # P2P networking
│   └── Docs/                   # Project-specific docs
│
├── DerpTech2D.MatchmakingServer/  # Multiplayer matchmaking
├── DerpTech2D.Tests/              # Test suite
├── Catrillion.Tests/              # Catrillion test suite
│
├── DerpLib.Ecs/                # Source-generated deterministic ECS
│   ├── Runtime/                # ECS runtime (EntityHandle, systems, tables)
│   └── Generator/              # Source generator for world/table types
├── Pooled.Generator/           # Source generator for object pools
├── Pooled.Annotations/         # [Pooled] attribute
├── Pooled.Runtime/             # Pool runtime support
│
└── Docs/                       # Workspace-wide documentation
    ├── architecture.md         # This file
    ├── RollbackArchitecture.md
    ├── DerpEcs.md
    ├── DeterminismPlan.md
    └── ...
```

---

## Core Architecture Concepts

### 1. Dual Simulation Architecture

Both projects use separation between simulation and rendering:

```
┌──────────────────┐     ┌──────────────────┐
│    SimWorld      │────>│    ECS World     │
│  (Deterministic) │sync │   (Rendering)    │
│  Fixed64 math    │     │   float/visual   │
└──────────────────┘     └──────────────────┘
```

- **SimWorld**: Contains all gameplay state using Fixed64 math
- **ECS World**: Friflo entities with visual components, synced from SimWorld each frame

### 2. Derp.Ecs System

Source-generated ECS creates typed archetype tables from a `Setup()` declaration:

```csharp
public sealed partial class SimEcsWorld
{
    [Conditional(DerpEcsSetupConstants.ConditionalSymbol)]
    private static void Setup(DerpEcsSetupBuilder b)
    {
        b.Archetype<Unit>()
            .Capacity(1000)
            .With<TransformComponent>()
            .With<HealthComponent>();
    }
}
// Generates: UnitTable with TryQueueSpawn(), QueueDestroy(), ref accessors
```

See `Docs/DerpEcs.md` for full architecture.

### 3. Rollback Netcode (Catrillion)

- Ring buffer stores N frames of simulation snapshots
- Late inputs trigger rollback to last correct state
- Resimulate with corrected inputs to catch up
- Target: 7-frame rollback < 10ms

### 4. Zero-Allocation Policy

Hot paths must avoid GC:
- No LINQ in per-frame code
- No lambdas/closures
- No string interpolation
- Use object pools and pre-allocated buffers

---

## Platform Support

| Platform | Build Command |
|----------|---------------|
| macOS (Apple Silicon) | `dotnet publish -r osx-arm64` |
| macOS (Intel) | `dotnet publish -r osx-x64` |
| Windows | `dotnet publish -r win-x64` |
| Linux | `dotnet publish -r linux-x64` |
| WebAssembly | `dotnet publish -r browser-wasm` |

---

## Key Files Reference

| File | Purpose |
|------|---------|
| `CLAUDE.md` | Build commands, architecture summary, critical rules |
| `AGENTS.md` | Detailed coding guidelines |
| `Core/Fixed64.cs` | Deterministic 64-bit fixed-point math |
| `Core/DeterministicRandom.cs` | Seeded random for simulation |
| `Catrillion/Rollback/WorldSnapshot.cs` | Snapshot serialization |
| `DerpLib.Ecs/` | Source-generated deterministic ECS |

---

## Documentation Index

**Workspace Docs** (in `Docs/`):
- **Rollback System**: `RollbackArchitecture.md`
- **Derp.Ecs**: `DerpEcs.md`
- **Determinism Strategy**: `DeterminismPlan.md`
- **Test Strategy**: `TestStrategy.md`
- **Simulation Systems**: `SimulationSystems.md`
- **Replay System**: `ReplaySystem.md`

**Project-Specific Docs**:
- **DerpTech2D**: `DerpTech2D/Docs/architecture.md`
- **Catrillion**: `Catrillion/Docs/architecture.md`
- **SurvivorArena**: `SurvivorArena/Docs/architecture.md`

---

## Quick Context Rebuild

When starting a new session:

1. Read `CLAUDE.md` for build commands and critical rules
2. Check this file for high-level architecture
3. Check `Catrillion/Docs/architecture.md` if working on rollback netcode
4. Check specific docs (e.g., `DerpEcs.md`) for deep dives

---

*Last updated: December 2024*

---

## Project Relationships

```
┌─────────────┐
│    Core     │  Shared math utilities
└──────┬──────┘
       │ references
       ▼
┌──────────────────────────────────────────────┐
│  DerpTech2D  │  Catrillion  │  SurvivorArena │
│  (game)      │  (prototype) │  (fork)        │
└──────────────────────────────────────────────┘
       │
       │ uses
       ▼
┌──────────────────────────────────────────────┐
│  Derp.Ecs.Generator  │  Pooled.Generator     │
│  (code gen)          │  (code gen)           │
└──────────────────────────────────────────────┘
```
