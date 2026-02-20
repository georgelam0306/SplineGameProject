# Rollback Architecture

Full snapshot approach for 100k entities with memcpy-speed serialization.

---

## Performance Targets

| Metric | Target |
|--------|--------|
| 7-frame rollback | < 10ms total |
| Full state serialize (100k entities) | < 1ms |
| Full state deserialize (100k entities) | < 1ms |
| Single frame re-simulation | < 1.3ms |
| Snapshot memory per frame | < 10 MB |

---

## Core Architecture

### Snapshot Ring Buffer

Store N most recent simulation states in a circular buffer:

```
Frame:    [F-7] [F-6] [F-5] [F-4] [F-3] [F-2] [F-1] [F-0]
Index:      0     1     2     3     4     5     6     7
                                                      ^
                                                  current
```

Configuration:
- **Buffer depth**: 8 frames (7 rollback + current)
- **Storage**: Pre-allocated byte arrays
- **Index**: `frameNumber % bufferDepth`

### Rollback Flow

```
Rollback(targetFrame):
  1. Assert targetFrame >= currentFrame - 7
  2. Find snapshot index: targetFrame % bufferDepth
  3. Deserialize snapshot into ECS
  4. Re-simulate from targetFrame to currentFrame
     - For each frame: apply stored inputs, tick simulation
  5. Update currentFrame pointer
```

---

## Memcpy-Speed Serialization

Reference: [MemoryPack approach](https://neuecc.medium.com/how-to-make-the-fastest-net-serializer-with-net-7-c-11-case-of-memorypack-ad28c0366516)

### Key Principles

1. **Blittable structs only**: No managed references, no pointers
2. **Direct memory copy**: Use `Unsafe.CopyBlock` or `MemoryMarshal.AsBytes`
3. **No reflection**: All type info known at compile time
4. **No allocation**: Reuse pre-allocated buffers
5. **No boxing**: Generic constraints to prevent boxing

### Component Requirements

All simulation components must be `unmanaged`:

```csharp
// Valid - all fields are unmanaged
public struct SimTransform2D : IComponent {
    public Fixed64 X;  // Contains long
    public Fixed64 Y;  // Contains long
}

// Invalid - contains reference type
public struct BadComponent : IComponent {
    public string Name;  // Reference type!
}
```

### Serialization API

```csharp
public static class SnapshotSerializer {
    
    public static int Serialize<T>(ReadOnlySpan<T> components, Span<byte> buffer) 
        where T : unmanaged 
    {
        int byteCount = components.Length * sizeof(T);
        var sourceBytes = MemoryMarshal.AsBytes(components);
        sourceBytes.CopyTo(buffer);
        return byteCount;
    }
    
    public static void Deserialize<T>(ReadOnlySpan<byte> buffer, Span<T> components) 
        where T : unmanaged 
    {
        var targetBytes = MemoryMarshal.AsBytes(components);
        buffer.Slice(0, targetBytes.Length).CopyTo(targetBytes);
    }
}
```

### Unsafe Block Copy

For maximum performance, use `Unsafe.CopyBlock`:

```csharp
unsafe void CopyComponents<T>(T* source, T* dest, int count) where T : unmanaged {
    Unsafe.CopyBlock(dest, source, (uint)(count * sizeof(T)));
}
```

---

## Friflo ECS Integration

### Archetype Chunk Layout

Friflo stores components in archetype chunks with Structure-of-Arrays (SoA) layout:

```
Archetype [SimTransform2D, SimVelocity, UnitTag]:
  Chunk 0: { SimTransform2D[1024], SimVelocity[1024], EntityIds[1024] }
  Chunk 1: { SimTransform2D[1024], SimVelocity[1024], EntityIds[1024] }
  ...
```

This layout is ideal for bulk serialization - each component type is contiguous.

### Serializing Archetypes

```csharp
void SerializeArchetype(ArchetypeQuery query, BinaryWriter writer) {
    foreach (var chunk in query.Chunks) {
        // Write chunk metadata
        writer.Write(chunk.Length);
        
        // Write entity IDs (for reconstruction)
        var ids = chunk.Entities.Ids;
        WriteSpan(writer, ids.AsSpan());
        
        // Write each component type
        WriteComponentSpan(writer, chunk.Chunk1.Span);  // SimTransform2D
        WriteComponentSpan(writer, chunk.Chunk2.Span);  // SimVelocity
    }
}

void WriteComponentSpan<T>(BinaryWriter writer, ReadOnlySpan<T> span) where T : unmanaged {
    var bytes = MemoryMarshal.AsBytes(span);
    writer.Write(bytes);
}
```

### Deserializing into ECS

Option A: **Clear and recreate** (simpler, slower)
```csharp
void DeserializeWorld(byte[] snapshot) {
    // Delete all entities
    foreach (var entity in _store.Entities) {
        entity.DeleteEntity();
    }
    
    // Recreate from snapshot
    var reader = new BinaryReader(new MemoryStream(snapshot));
    while (reader.BaseStream.Position < reader.BaseStream.Length) {
        int entityId = reader.ReadInt32();
        var entity = _store.CreateEntity(entityId);
        // Read and add components...
    }
}
```

Option B: **In-place update** (complex, faster)
```csharp
void DeserializeInPlace(byte[] snapshot) {
    var reader = new BinaryReader(new MemoryStream(snapshot));
    
    // Update existing entities, create missing, delete extras
    var snapshotEntityIds = ReadEntityIds(reader);
    var currentEntityIds = GetCurrentEntityIds();
    
    // Diff and sync...
}
```

---

## Snapshot Format

### Header

```
[4 bytes] Magic number: 0x44455250 ("DERP")
[4 bytes] Version: 1
[4 bytes] Frame number
[4 bytes] Entity count
[4 bytes] Archetype count
[4 bytes] Total byte size
[8 bytes] State hash (for verification)
```

### Per-Archetype Block

```
[4 bytes] Archetype ID (hash of component types)
[4 bytes] Entity count in archetype
[4 bytes] Component type count
For each component type:
  [4 bytes] Component type ID
  [4 bytes] Component size
  [N bytes] Component data (count × size)
[N bytes] Entity IDs (count × 4)
```

### Example Size Calculation

For 100k entities with common components:

| Component | Size | Total |
|-----------|------|-------|
| SimTransform2D | 16 bytes | 1.6 MB |
| SimVelocity | 16 bytes | 1.6 MB |
| UnitFlowCache | 24 bytes | 2.4 MB |
| EnemyHealth | 8 bytes | 0.8 MB |
| Entity IDs | 4 bytes | 0.4 MB |
| Overhead | ~100 bytes | 0.01 MB |
| **Total** | | **~6.8 MB** |

For 8-frame buffer: **~55 MB** total memory

---

## Memory Management

### Pre-allocation

Allocate all snapshot buffers at startup:

```csharp
public class SnapshotBuffer {
    private readonly byte[][] _frames;
    private readonly int _frameCapacity;
    private int _currentIndex;
    
    public SnapshotBuffer(int frameCount, int bytesPerFrame) {
        _frames = new byte[frameCount][];
        _frameCapacity = bytesPerFrame;
        
        for (int i = 0; i < frameCount; i++) {
            _frames[i] = new byte[bytesPerFrame];
        }
    }
    
    public Span<byte> GetWriteBuffer(int frameNumber) {
        _currentIndex = frameNumber % _frames.Length;
        return _frames[_currentIndex];
    }
    
    public ReadOnlySpan<byte> GetReadBuffer(int frameNumber) {
        int index = frameNumber % _frames.Length;
        return _frames[index];
    }
}
```

### Avoiding GC

- **No allocations** during serialize/deserialize
- Pre-allocate scratch buffers
- Use `stackalloc` for small temporary buffers
- Pool large temporary buffers

---

## Rollback Execution

### Full Rollback Sequence

```csharp
public void Rollback(int targetFrame) {
    if (targetFrame < _currentFrame - MaxRollbackFrames) {
        throw new InvalidOperationException("Cannot rollback that far");
    }
    
    // 1. Deserialize target state
    var snapshot = _snapshotBuffer.GetReadBuffer(targetFrame);
    DeserializeWorld(snapshot);
    
    // 2. Re-simulate to current frame
    for (int frame = targetFrame; frame < _currentFrame; frame++) {
        var inputs = _inputBuffer.GetInputs(frame);
        ApplyInputs(inputs);
        SimulationTick(frame);
    }
}
```

### Performance Breakdown Target

| Phase | Budget |
|-------|--------|
| Deserialize | 1.0 ms |
| Frame 1 re-sim | 1.3 ms |
| Frame 2 re-sim | 1.3 ms |
| Frame 3 re-sim | 1.3 ms |
| Frame 4 re-sim | 1.3 ms |
| Frame 5 re-sim | 1.3 ms |
| Frame 6 re-sim | 1.3 ms |
| Frame 7 re-sim | 1.3 ms |
| **Total** | **10.1 ms** |

### Optimization Strategies

1. **Partial state**: Only serialize components that change
2. **Delta compression**: Store diffs from previous frame
3. **Lazy deserialization**: Only restore entities that are accessed
4. **Parallel re-sim**: Re-simulate independent regions in parallel

---

## Input Storage

### Input Buffer

Store inputs for each frame to enable re-simulation:

```csharp
public struct FrameInput {
    public int FrameNumber;
    public int PlayerInputCount;
    public fixed byte InputData[256];  // Blittable!
}

public class InputBuffer {
    private readonly FrameInput[] _inputs;
    
    public void StoreInput(int frame, ReadOnlySpan<byte> inputData) {
        int index = frame % _inputs.Length;
        _inputs[index].FrameNumber = frame;
        inputData.CopyTo(_inputs[index].InputData);
    }
    
    public ReadOnlySpan<byte> GetInputs(int frame) {
        int index = frame % _inputs.Length;
        return _inputs[index].InputData;
    }
}
```

### Input Types

- Unit selection changes
- Move commands
- Attack commands
- Build commands
- Pause/unpause

All inputs must be serializable and replayable.

---

## Verification

### State Hash

Compute rolling hash of simulation state for desync detection:

```csharp
public ulong ComputeStateHash() {
    ulong hash = 14695981039346656037UL;  // FNV offset
    
    foreach (var (transforms, entities) in _simQuery.Chunks) {
        for (int i = 0; i < entities.Length; i++) {
            hash ^= (ulong)entities.Ids[i];
            hash *= 1099511628211UL;  // FNV prime
            hash ^= (ulong)transforms.Span[i].X.Raw;
            hash *= 1099511628211UL;
            hash ^= (ulong)transforms.Span[i].Y.Raw;
            hash *= 1099511628211UL;
        }
    }
    
    return hash;
}
```

### Desync Detection

In multiplayer, compare state hashes periodically:

```csharp
void OnReceiveRemoteHash(int frame, ulong remoteHash) {
    ulong localHash = _stateHashes[frame % HashBufferSize];
    
    if (localHash != remoteHash) {
        // Desync detected!
        RequestFullStateFromHost();
        // Or trigger rollback to last known good state
    }
}
```

---

## Implementation Checklist

- [ ] Define snapshot format and header
- [ ] Implement SnapshotSerializer with memcpy semantics
- [ ] Create SnapshotBuffer with pre-allocated frames
- [ ] Implement SerializeWorld() for all simulation components
- [ ] Implement DeserializeWorld() with entity reconstruction
- [ ] Create InputBuffer for input storage
- [ ] Implement Rollback() with re-simulation loop
- [ ] Add state hash computation
- [ ] Benchmark serialize/deserialize performance
- [ ] Benchmark 7-frame rollback total time
- [ ] Add desync detection for multiplayer

