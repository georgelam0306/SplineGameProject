# Determinism Plan

Audit of non-deterministic code paths and remediation strategy for lockstep multiplayer and replay systems.

---

## What is Determinism?

A deterministic simulation produces identical output given identical input, regardless of:
- Machine architecture (x86, ARM, WASM)
- Operating system
- Thread scheduling
- Execution timing
- Number of runs

---

## Current Determinism Foundation

### Fixed64 Math ✓

The `Fixed64` type provides deterministic arithmetic:
- 48.16 fixed-point using `long` internally
- Integer-only operations (no IEEE 754 floating-point)
- Deterministic overflow handling (saturates to min/max)
- Deterministic square root (integer algorithm)

### Simulation Components ✓

Core simulation state uses Fixed64:
- `SimTransform2D` - position (X, Y as Fixed64)
- `SimVelocity` - velocity (X, Y as Fixed64)

### Render Separation ✓

Render state (`Transform2D` with float) is separate from simulation state, synced via `SimToRenderSyncSystem`.

---

## Non-Deterministic Code Audit

### 1. Random Number Generation 🔴

**Location**: `UnitSpawnSystem.cs`
```csharp
private readonly Random _random;
_random = new Random(seed);
float spawnX = ((float)_random.NextDouble() - 0.5f) * spawnAreaSize;
```

**Problem**: `System.Random` implementation varies across .NET versions and platforms.

**Remediation**:
- Implement custom deterministic RNG (e.g., xorshift128+)
- Use Fixed64 output instead of float
- Ensure same seed produces same sequence everywhere

```csharp
public struct DeterministicRng {
    private ulong _state0;
    private ulong _state1;
    
    public DeterministicRng(ulong seed) {
        _state0 = seed;
        _state1 = seed ^ 0x9E3779B97F4A7C15UL;
    }
    
    public ulong Next() {
        ulong s0 = _state0;
        ulong s1 = _state1;
        ulong result = s0 + s1;
        s1 ^= s0;
        _state0 = ((s0 << 55) | (s0 >> 9)) ^ s1 ^ (s1 << 14);
        _state1 = (s1 << 36) | (s1 >> 28);
        return result;
    }
    
    public Fixed64 NextFixed64(Fixed64 min, Fixed64 max) {
        ulong range = (ulong)(max.Raw - min.Raw);
        return Fixed64.FromRaw(min.Raw + (long)(Next() % range));
    }
}
```

---

### 2. ConcurrentQueue/Dictionary Iteration 🔴

**Locations**:
- `DeferredCommandBuffer.cs` - `ConcurrentQueue<T>`
- `ZoneFlowService.cs` - `ConcurrentDictionary<K,V>`
- `ZoneGraph.cs` - `ConcurrentDictionary<K,V>`

**Problem**: Dequeue order from `ConcurrentQueue` is FIFO but insertion order from multiple threads is non-deterministic. Dictionary enumeration order is undefined.

**Remediation for DeferredCommandBuffer**:

Already partially addressed - `ApplyDeferredCommandsSystem` sorts commands by entity ID:
```csharp
_combatDamageBuffer.Sort(CombatDamageComparison);
```

Ensure ALL deferred command types are sorted before application.

**Remediation for Dictionaries**:

Option A: Replace with sorted structures
```csharp
// Instead of ConcurrentDictionary
private readonly SortedDictionary<int, ZoneFlowDataHandle> _flowCache;
```

Option B: Sort keys before iteration
```csharp
var sortedKeys = _flowCache.Keys.ToList();
sortedKeys.Sort();
foreach (var key in sortedKeys) { ... }
```

Option C: Use deterministic iteration via arrays
```csharp
// Pre-allocate array, use indices instead of dictionary
private readonly ZoneFlowDataHandle[] _flowsByZoneId;
```

---

### 3. Float Math in Combat 🔴

**Locations**:
- `CombatAttackSystem.cs` - distance calculations, damage
- `UnitAttackSystem.cs` - distance calculations
- `ProjectileService.cs` - position interpolation, damage
- `CombatStats` component - health, damage, range (all float)

**Problem**: IEEE 754 floating-point results can vary across platforms due to:
- x87 vs SSE differences
- FMA instruction availability
- Compiler optimizations
- Different rounding modes

**Remediation**:

Convert all combat math to Fixed64:

```csharp
// Before
public struct CombatStats : IComponent {
    public float Health;
    public float Damage;
    public float Range;
    public float AttackSpeed;
    public float AttackCooldown;
}

// After
public struct CombatStats : IComponent {
    public Fixed64 Health;
    public Fixed64 Damage;
    public Fixed64 Range;
    public Fixed64 AttackSpeed;
    public Fixed64 AttackCooldown;
}
```

---

### 4. HashSet Iteration Order 🔴

**Locations**:
- `ZoneFlowService.cs` - `_currentSeedTiles` HashSet
- `ZoneGraph.cs` - `_sectorsWithNeighborsBuilt` HashSet
- Various other locations

**Problem**: HashSet enumeration order is undefined and may vary.

**Remediation**:

Option A: Use `SortedSet<T>` where order matters
```csharp
private readonly SortedSet<(int, int)> _currentSeedTiles;
```

Option B: Sort before iteration
```csharp
var sortedTiles = _currentSeedTiles.OrderBy(t => t.Item1).ThenBy(t => t.Item2);
```

Option C: Use `List<T>` with manual deduplication
```csharp
private readonly List<(int, int)> _currentSeedTiles;
// Sort after modifications, binary search for contains
```

---

### 5. ThreadLocal State 🟡

**Locations**:
- `ZoneFlowService.cs` - Dijkstra data structures
- `UnitIdleSystem.cs` - Cell buffers
- `SimulationRoot.cs` - Stopwatch

**Problem**: ThreadLocal allocates separate instances per thread. If threads process different data on different runs, results could vary.

**Assessment**: Currently acceptable IF:
- Each entity is always processed by the same thread
- ThreadLocal is only used for scratch buffers, not state

**Remediation** (if needed):
- Use region-owned buffers instead of ThreadLocal
- Pass buffers explicitly through call chain

---

### 6. Frame Timing 🔴

**Location**: `Application.cs`
```csharp
float deltaTime = Raylib.GetFrameTime();
_game?.Update(deltaTime);
```

**Problem**: Variable deltaTime causes different simulation results based on frame rate.

**Remediation**: Fixed timestep simulation

```csharp
const float FixedTimestep = 1f / 60f;  // 60 Hz simulation
float _accumulator = 0f;
int _simFrame = 0;

void Update(float deltaTime) {
    _accumulator += deltaTime;
    
    while (_accumulator >= FixedTimestep) {
        SimulationTick(_simFrame, FixedTimestep);
        _simFrame++;
        _accumulator -= FixedTimestep;
    }
    
    // Render with interpolation
    float alpha = _accumulator / FixedTimestep;
    Render(alpha);
}
```

For simulation, use integer frame count instead of float time:
```csharp
void SimulationTick(int frameNumber, float fixedDt) {
    // fixedDt is always the same constant
    // frameNumber is deterministic
}
```

---

### 7. PriorityQueue Tie-Breaking 🟡

**Locations**:
- `ZoneFlowService.cs` - Dijkstra queue
- `ZoneGraph.cs` - A* queue

**Problem**: When two items have equal priority, dequeue order is undefined.

**Assessment**: Usually acceptable for pathfinding (produces valid but potentially different paths).

**Remediation** (if strict determinism needed):
```csharp
// Use tuple priority with deterministic tie-breaker
_queue.Enqueue((tileX, tileY), (distance.Raw, tileX * 10000L + tileY));
```

---

### 8. Entity Creation Order 🟡

**Location**: Various systems that create entities

**Problem**: If entities are created in non-deterministic order, entity IDs will differ.

**Assessment**: Entity IDs from `EntityStore.CreateEntity()` are sequential. As long as creation order is deterministic, IDs are deterministic.

**Remediation**:
- Ensure entity creation happens in deterministic system execution order
- Never create entities from input callbacks or async code

---

## Remediation Priority

### Phase 1: Critical (Must Fix)
1. Fixed timestep simulation loop
2. Float → Fixed64 in combat systems
3. Sort all deferred command buffers
4. Deterministic RNG implementation

### Phase 2: Important (Should Fix)
5. Replace ConcurrentDictionary iteration with sorted iteration
6. Replace HashSet with SortedSet where iteration matters
7. Ensure entity creation order is deterministic

### Phase 3: Hardening (Nice to Have)
8. PriorityQueue tie-breaking
9. ThreadLocal audit
10. Comprehensive determinism test suite

---

## Verification Strategy

### State Hashing

After each simulation frame, compute hash of all simulation state:

```csharp
ulong ComputeStateHash() {
    ulong hash = 0;
    foreach (var (transforms, entities) in _simTransformQuery.Chunks) {
        for (int i = 0; i < entities.Length; i++) {
            hash ^= (ulong)entities.Ids[i] * 31;
            hash ^= (ulong)transforms.Span[i].X.Raw * 37;
            hash ^= (ulong)transforms.Span[i].Y.Raw * 41;
        }
    }
    // Hash all simulation components...
    return hash;
}
```

### Replay Comparison

1. Record initial state + all inputs
2. Play back on different machine/build
3. Compare state hashes at each frame
4. First divergence indicates non-determinism source

### Parallel Execution Test

Run same simulation twice in parallel:
```csharp
var sim1 = new Simulation(initialState, inputs);
var sim2 = new Simulation(initialState, inputs);

for (int frame = 0; frame < 1000; frame++) {
    sim1.Tick();
    sim2.Tick();
    Assert.Equal(sim1.StateHash, sim2.StateHash);
}
```

---

## Component Blittability Requirements

For memcpy-speed serialization, all simulation components must be unmanaged (blittable):

### Allowed Types
- `int`, `long`, `uint`, `ulong`
- `bool` (1 byte)
- `Fixed64` (contains `long`)
- Fixed-size buffers: `fixed int buffer[N]`
- Other unmanaged structs

### Forbidden Types
- `string`
- `class` references
- `object`
- Arrays (use fixed buffers instead)
- `Span<T>`, `Memory<T>`

### Validation
```csharp
static void AssertBlittable<T>() where T : unmanaged { }

// Will fail to compile if T is not blittable
AssertBlittable<SimTransform2D>();
AssertBlittable<CombatStats>();
```

