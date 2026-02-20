# Test Strategy

Testing approach for determinism, performance benchmarks, and experimental algorithms.

---

## Test Categories

1. **Determinism Tests**: Verify same input produces same output
2. **Serialization Benchmarks**: Measure serialize/deserialize performance
3. **Rollback Tests**: Verify rollback correctness and performance
4. **Experimental Suite**: Compare alternative algorithm implementations

---

## Determinism Tests

### Same-Machine Determinism

Run simulation twice with identical inputs, compare state:

```csharp
[Test]
public void SameInputProducesSameOutput() {
    var inputs = LoadTestInputs("scenario_100k_units.inputs");
    
    var sim1 = new Simulation(seed: 42);
    var sim2 = new Simulation(seed: 42);
    
    for (int frame = 0; frame < 1000; frame++) {
        sim1.ApplyInputs(inputs[frame]);
        sim2.ApplyInputs(inputs[frame]);
        
        sim1.Tick();
        sim2.Tick();
        
        Assert.Equal(sim1.StateHash, sim2.StateHash, 
            $"Desync at frame {frame}");
    }
}
```

### Parallel Execution Determinism

Run simulation in parallel threads, verify identical results:

```csharp
[Test]
public void ParallelExecutionIsDeterministic() {
    var inputs = LoadTestInputs("scenario_combat.inputs");
    
    var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() => {
        var sim = new Simulation(seed: 42);
        for (int frame = 0; frame < 1000; frame++) {
            sim.ApplyInputs(inputs[frame]);
            sim.Tick();
        }
        return sim.StateHash;
    })).ToArray();
    
    var hashes = Task.WhenAll(tasks).Result;
    
    Assert.True(hashes.All(h => h == hashes[0]), 
        "Parallel executions produced different results");
}
```

### Cross-Build Determinism

Compare state hashes between Debug and Release builds:

```csharp
[Test]
public void DebugAndReleaseMatch() {
    // Run test binary in Debug mode, save hashes
    var debugHashes = RunSimulationExternal("DerpTech2D_Debug.exe", "scenario.inputs");
    
    // Run test binary in Release mode, save hashes
    var releaseHashes = RunSimulationExternal("DerpTech2D_Release.exe", "scenario.inputs");
    
    Assert.Equal(debugHashes.Length, releaseHashes.Length);
    for (int i = 0; i < debugHashes.Length; i++) {
        Assert.Equal(debugHashes[i], releaseHashes[i], 
            $"Debug/Release diverge at frame {i}");
    }
}
```

### Cross-Platform Determinism

Run on multiple platforms, compare results:

- Windows x64
- macOS ARM64
- Linux x64
- WASM (browser)

```csharp
[Test]
public void AllPlatformsMatch() {
    var platformHashes = new Dictionary<string, ulong[]> {
        ["windows"] = LoadHashes("windows_hashes.bin"),
        ["macos"] = LoadHashes("macos_hashes.bin"),
        ["linux"] = LoadHashes("linux_hashes.bin"),
        ["wasm"] = LoadHashes("wasm_hashes.bin"),
    };
    
    var reference = platformHashes["windows"];
    foreach (var (platform, hashes) in platformHashes) {
        for (int i = 0; i < hashes.Length; i++) {
            Assert.Equal(reference[i], hashes[i], 
                $"{platform} diverges from reference at frame {i}");
        }
    }
}
```

---

## Serialization Benchmarks

### Setup

```csharp
[GlobalSetup]
public void Setup() {
    _simulation = new Simulation(seed: 42);
    SpawnUnits(100_000);
    _snapshotBuffer = new byte[20_000_000];  // 20 MB buffer
}
```

### Serialize Benchmark

```csharp
[Benchmark]
public int SerializeFullState() {
    return _serializer.Serialize(_simulation, _snapshotBuffer);
}
```

**Target**: < 1ms for 100k entities

### Deserialize Benchmark

```csharp
[Benchmark]
public void DeserializeFullState() {
    _serializer.Deserialize(_snapshotBuffer, _simulation);
}
```

**Target**: < 1ms for 100k entities

### Component-Level Benchmarks

```csharp
[Benchmark]
[Arguments(10_000)]
[Arguments(50_000)]
[Arguments(100_000)]
public int SerializeTransforms(int entityCount) {
    return SerializeComponentType<SimTransform2D>(entityCount);
}
```

### Memory Allocation Test

```csharp
[Test]
public void SerializeDoesNotAllocate() {
    // Warm up
    _serializer.Serialize(_simulation, _snapshotBuffer);
    
    // Measure
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 100; i++) {
        _serializer.Serialize(_simulation, _snapshotBuffer);
    }
    long after = GC.GetAllocatedBytesForCurrentThread();
    
    Assert.Equal(0, after - before, "Serialization allocated memory");
}
```

---

## Rollback Tests

### Correctness Test

```csharp
[Test]
public void RollbackRestoresExactState() {
    var sim = new Simulation(seed: 42);
    var rollbackManager = new RollbackManager(sim, maxFrames: 8);
    
    // Run 10 frames
    for (int i = 0; i < 10; i++) {
        sim.Tick();
        rollbackManager.SaveSnapshot(i);
    }
    
    ulong stateAtFrame5 = GetStateHashAtSnapshot(5);
    
    // Rollback to frame 5
    rollbackManager.Rollback(5);
    
    Assert.Equal(stateAtFrame5, sim.StateHash);
}
```

### Performance Test

```csharp
[Benchmark]
public void SevenFrameRollback() {
    // Setup: run to frame 100, save snapshots
    for (int i = 0; i < 100; i++) {
        _simulation.Tick();
        _rollbackManager.SaveSnapshot(i);
    }
    
    // Benchmark: rollback 7 frames and re-simulate
    _rollbackManager.Rollback(93);  // 100 - 7
    
    for (int i = 93; i < 100; i++) {
        _simulation.ApplyInputs(_inputs[i]);
        _simulation.Tick();
    }
}
```

**Target**: < 10ms total (deserialize + 7 frame re-sim)

### Stress Test

```csharp
[Test]
public void RepeatedRollbacksAreStable() {
    for (int iteration = 0; iteration < 1000; iteration++) {
        // Run forward
        for (int i = 0; i < 10; i++) {
            _simulation.Tick();
            _rollbackManager.SaveSnapshot(_frame++);
        }
        
        // Rollback randomly 1-7 frames
        int rollbackAmount = 1 + (iteration % 7);
        int targetFrame = _frame - rollbackAmount;
        _rollbackManager.Rollback(targetFrame);
        _frame = targetFrame;
    }
    
    // Verify simulation is still valid
    Assert.NotEqual(0UL, _simulation.StateHash);
}
```

---

## Experimental Coding Suite

### Experiment: Graph Coloring for Parallel Separation

**Hypothesis**: Graph coloring can enable parallel separation force calculation while maintaining determinism by ensuring non-conflicting entities are processed simultaneously.

#### Algorithm

```
1. Build conflict graph:
   - Node = entity
   - Edge = entities within separation radius
   
2. Color graph (greedy, deterministic order):
   For each entity in ID order:
     Assign lowest color not used by neighbors
   
3. Process by color:
   For each color (sequential):
     Process all entities of this color (parallel)
```

#### Structure

```csharp
public class GraphColoringSeparation {
    private readonly int[] _entityColors;  // Color per entity
    private readonly List<int>[] _entitiesByColor;  // Entities grouped by color
    
    public void RebuildGraph(SpatialHash spatialHash) {
        // Build adjacency from spatial queries
        // Greedy color assignment
    }
    
    public void ProcessSeparation() {
        foreach (var colorGroup in _entitiesByColor) {
            // Process all entities in colorGroup in parallel
            Parallel.ForEach(colorGroup, entity => {
                ComputeSeparationForce(entity);
            });
            // Barrier between colors
        }
    }
}
```

#### Determinism Guarantee

- Graph built in entity ID order (deterministic)
- Greedy coloring in entity ID order (deterministic)
- Colors processed in fixed order (deterministic)
- Entities within color processed independently (no conflicts)

#### Expected Results

| Metric | Single-threaded | Graph Coloring (4 threads) |
|--------|-----------------|---------------------------|
| 100k separation | ? ms | ? ms |
| Color count | N/A | ~6-10 expected |
| Graph build cost | N/A | ? ms |

#### Test Cases

1. Sparse entities (few conflicts, few colors)
2. Dense cluster (many conflicts, many colors)
3. Grid formation (regular pattern)
4. Mixed density regions

---

## Benchmark Infrastructure

### Test Harness

```csharp
public class SimulationBenchmark {
    private Simulation _simulation;
    private byte[] _snapshotBuffer;
    
    [GlobalSetup]
    public void Setup() {
        _simulation = new Simulation(seed: 42);
        _snapshotBuffer = new byte[20_000_000];
        
        // Spawn test entities
        for (int i = 0; i < 100_000; i++) {
            SpawnUnit(RandomPosition());
        }
        
        // Warm up
        for (int i = 0; i < 10; i++) {
            _simulation.Tick();
        }
    }
    
    [Benchmark(Baseline = true)]
    public void FullSimulationTick() {
        _simulation.Tick();
    }
    
    [Benchmark]
    public void MovementOnly() {
        _simulation.TickMovementOnly();
    }
    
    [Benchmark]
    public void SeparationOnly() {
        _simulation.TickSeparationOnly();
    }
}
```

### Profiling Integration

```csharp
public void RunProfiledSession() {
    using var profiler = new ProfilerSession("benchmarks/profile_100k.etl");
    
    profiler.Start();
    for (int i = 0; i < 1000; i++) {
        _simulation.Tick();
    }
    profiler.Stop();
    
    // Analyze with PerfView, dotTrace, or similar
}
```

### CI Integration

```yaml
# .github/workflows/benchmarks.yml
benchmark:
  runs-on: ubuntu-latest
  steps:
    - uses: actions/checkout@v3
    - run: dotnet run -c Release --project Benchmarks
    - uses: benchmark-action/github-action-benchmark@v1
      with:
        tool: 'benchmarkdotnet'
        output-file-path: BenchmarkDotNet.Artifacts/results/*.json
        fail-on-alert: true
        alert-threshold: '150%'
```

---

## Test Scenarios

### Scenario Files

Pre-built test scenarios for consistent benchmarking:

| Scenario | Entities | Duration | Focus |
|----------|----------|----------|-------|
| `spawn_100k.scenario` | 100k | 1 frame | Entity creation |
| `idle_100k.scenario` | 100k | 100 frames | Separation only |
| `move_100k.scenario` | 100k | 100 frames | Movement + flow |
| `combat_mixed.scenario` | 50k + 100 towers | 500 frames | Full combat |
| `rollback_stress.scenario` | 100k | 1000 frames | Many rollbacks |

### Scenario Format

```json
{
  "name": "idle_100k",
  "seed": 42,
  "initialState": {
    "entities": [
      { "type": "unit", "count": 100000, "region": {"x": -8000, "y": -8000, "w": 16000, "h": 16000} }
    ]
  },
  "frames": [
    { "frame": 0, "inputs": [] },
    { "frame": 100, "verify": { "minEntities": 100000, "maxEntities": 100000 } }
  ]
}
```

---

## Implementation Checklist

### Determinism Tests
- [ ] Same-machine determinism test
- [ ] Parallel execution determinism test
- [ ] Debug/Release comparison
- [ ] Cross-platform hash collection

### Benchmarks
- [ ] Serialization benchmark suite
- [ ] Deserialization benchmark suite
- [ ] Full tick benchmark
- [ ] Per-system breakdown benchmark
- [ ] Memory allocation verification

### Rollback Tests
- [ ] Correctness verification
- [ ] 7-frame rollback performance test
- [ ] Stress test (repeated rollbacks)

### Experimental Suite
- [ ] Graph coloring separation prototype
- [ ] Benchmark vs single-threaded baseline
- [ ] Determinism verification for experiment

