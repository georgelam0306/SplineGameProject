# Indirect Rendering Architecture

## Overview

Indirect rendering allows the GPU to read draw parameters from a buffer, enabling:
- Single draw call for all meshes
- GPU-driven culling (future)
- Minimal CPU overhead

## The Problem: Instance Grouping

When the user submits draw calls in arbitrary order:

```csharp
Derp.DrawMesh(cube, ...);    // mesh 0
Derp.DrawMesh(sphere, ...);  // mesh 1
Derp.DrawMesh(cube, ...);    // mesh 0
Derp.DrawMesh(player, ...);  // mesh 2
Derp.DrawMesh(cube, ...);    // mesh 0
```

We need to group them by mesh type for the indirect buffer:

```
Instance Buffer (must be contiguous per mesh):
[cube0][cube1][cube2][sphere0][player0]
 ←─── mesh 0 ───→    ←mesh 1→ ←mesh 2→

Indirect Commands (one per mesh type):
[0]: indexCount=36,   instanceCount=3, firstInstance=0
[1]: indexCount=2880, instanceCount=1, firstInstance=3
[2]: indexCount=4800, instanceCount=1, firstInstance=4
```

## Sorting Strategies

### Option A: Sort In-Place

**How it works:**
1. Collect all (meshId, instanceData) pairs into a single array
2. At flush time, sort the array by meshId
3. Upload sorted array to GPU
4. Build indirect commands by scanning for mesh boundaries

```csharp
struct PendingInstance
{
    public int MeshId;
    public InstanceData Data;
}

// Collection (unordered)
PendingInstance[] _pending = new PendingInstance[MaxInstances];
int _count;

void Add(int meshId, InstanceData data)
{
    _pending[_count++] = new PendingInstance { MeshId = meshId, Data = data };
}

void Flush()
{
    // Sort by mesh ID
    Array.Sort(_pending, 0, _count, MeshIdComparer.Instance);

    // Upload and build commands
    int commandCount = 0;
    int currentMesh = -1;
    int firstInstance = 0;

    for (int i = 0; i < _count; i++)
    {
        if (_pending[i].MeshId != currentMesh)
        {
            if (currentMesh >= 0)
            {
                // Emit command for previous mesh
                EmitCommand(currentMesh, i - firstInstance, firstInstance);
                commandCount++;
            }
            currentMesh = _pending[i].MeshId;
            firstInstance = i;
        }
        _instanceBuffer[i] = _pending[i].Data;
    }

    // Emit final command
    if (currentMesh >= 0)
        EmitCommand(currentMesh, _count - firstInstance, firstInstance);
}
```

**Pros:**
- Simple memory layout (single array)
- Cache-friendly sequential access during upload
- No wasted memory

**Cons:**
- O(n log n) sort every frame
- Comparison-based sort has branch mispredictions
- Moves 20+ bytes per swap (meshId + instanceData)

**Best for:** Moderate instance counts (< 10K), varying mesh types per frame

---

### Option B: Bucket Collection

**How it works:**
1. Pre-allocate a list per possible mesh type
2. Add directly routes to the correct bucket
3. At flush, iterate buckets in order, upload contiguously

```csharp
const int MaxMeshTypes = 256;

struct MeshBucket
{
    public InstanceData[] Instances;
    public int Count;
}

MeshBucket[] _buckets = new MeshBucket[MaxMeshTypes];
int[] _usedMeshes = new int[MaxMeshTypes];  // Which buckets have data
int _usedMeshCount;

void Add(int meshId, InstanceData data)
{
    ref var bucket = ref _buckets[meshId];

    // Track first use of this mesh
    if (bucket.Count == 0)
        _usedMeshes[_usedMeshCount++] = meshId;

    bucket.Instances[bucket.Count++] = data;
}

void Flush()
{
    int instanceOffset = 0;

    // Optional: sort _usedMeshes for deterministic ordering
    Array.Sort(_usedMeshes, 0, _usedMeshCount);

    for (int i = 0; i < _usedMeshCount; i++)
    {
        int meshId = _usedMeshes[i];
        ref var bucket = ref _buckets[meshId];

        // Upload this bucket's instances
        UploadInstances(bucket.Instances, bucket.Count, instanceOffset);

        // Emit indirect command
        EmitCommand(meshId, bucket.Count, instanceOffset);

        instanceOffset += bucket.Count;
        bucket.Count = 0;  // Reset for next frame
    }

    _usedMeshCount = 0;
}
```

**Pros:**
- O(1) add (no sorting needed)
- Already grouped, just concatenate
- Predictable memory access pattern

**Cons:**
- Memory overhead: MaxMeshTypes × MaxInstancesPerMesh
- If MaxMeshTypes = 256, MaxInstancesPerMesh = 1000: 256 × 1000 × 16 bytes = 4MB
- Wasted memory for unused mesh slots
- Cache misses if buckets are spread in memory

**Best for:** Known/limited mesh types, high instance counts, predictable workloads

---

### Option C: Counting Sort / Radix Sort

**How it works:**
1. Collect (meshId, instanceData) into unsorted array (like Option A)
2. First pass: count instances per mesh
3. Compute prefix sums (starting offset per mesh)
4. Second pass: scatter instances to their final positions

```csharp
PendingInstance[] _pending = new PendingInstance[MaxInstances];
InstanceData[] _sorted = new InstanceData[MaxInstances];  // Output buffer
int[] _counts = new int[MaxMeshTypes];      // Count per mesh
int[] _offsets = new int[MaxMeshTypes];     // Prefix sums
int _count;

void Add(int meshId, InstanceData data)
{
    _pending[_count++] = new PendingInstance { MeshId = meshId, Data = data };
}

void Flush()
{
    // Pass 1: Count instances per mesh
    Array.Clear(_counts, 0, MaxMeshTypes);
    for (int i = 0; i < _count; i++)
        _counts[_pending[i].MeshId]++;

    // Compute prefix sums (exclusive)
    int total = 0;
    int commandCount = 0;
    for (int m = 0; m < MaxMeshTypes; m++)
    {
        _offsets[m] = total;
        if (_counts[m] > 0)
        {
            EmitCommand(m, _counts[m], total);
            commandCount++;
        }
        total += _counts[m];
    }

    // Pass 2: Scatter to sorted positions
    int[] writeHead = (int[])_offsets.Clone();  // Copy for writing
    for (int i = 0; i < _count; i++)
    {
        int meshId = _pending[i].MeshId;
        int dest = writeHead[meshId]++;
        _sorted[dest] = _pending[i].Data;
    }

    // Upload _sorted buffer
    UploadInstances(_sorted, _count);
}
```

**Pros:**
- O(n) time complexity (two passes over data)
- No comparison/branching in the sort
- Stable sort (preserves submission order within mesh type)
- Memory efficient (just two arrays + small count/offset arrays)

**Cons:**
- Two passes over the data
- Extra memory for output buffer (can't sort in-place)
- Slightly more complex implementation

**Best for:** High instance counts (10K+), many mesh types, performance critical

---

## Comparison Table

| Aspect | Sort In-Place | Bucket Collection | Counting Sort |
|--------|---------------|-------------------|---------------|
| Time Complexity | O(n log n) | O(n) | O(n) |
| Memory Overhead | Minimal | High (buckets) | 2× instance array |
| Cache Behavior | Good after sort | Poor (scattered) | Good (sequential) |
| Add() Cost | O(1) | O(1) | O(1) |
| Flush() Cost | Sort + scan | Concatenate | Count + scatter |
| Implementation | Simple | Simple | Medium |
| Best Instance Count | < 10K | Any | > 10K |
| Best Mesh Types | Many/varying | Few/fixed | Any |

## Recommendation

**For Engine:** Start with **Option C (Counting Sort)**

Reasons:
1. O(n) scales well as we push instance counts
2. Memory overhead is reasonable (2× instance buffer)
3. Works well with any number of mesh types
4. The algorithm naturally produces the indirect commands during prefix sum
5. Good learning experience for GPU-friendly data structures

The counting sort approach also mirrors how GPU compute culling would work - count, prefix sum, scatter - so it's good preparation for future GPU-driven rendering.

---

## Data Structures

### InstanceData (16 bytes, same for 2D and 3D)

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct InstanceData
{
    public uint TransformIndex;   // Index into TransformBuffer SSBO
    public uint TextureIndex;     // Index into bindless texture array
    public uint PackedColor;      // RGBA8 tint
    public uint Flags;            // UV offset (2D) or material ID (3D)
}
```

### IndirectCommand (20 bytes, matches Vulkan)

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct IndirectCommand
{
    public uint IndexCount;       // From MeshHandle
    public uint InstanceCount;    // Computed during flush
    public uint FirstIndex;       // From MeshHandle
    public int  VertexOffset;     // From MeshHandle (signed!)
    public uint FirstInstance;    // Computed during flush
}
```

### IndirectBatcher

```csharp
public sealed class IndirectBatcher
{
    // Input (unsorted)
    private PendingInstance[] _pending;
    private int _pendingCount;

    // Output (sorted)
    private InstanceData[] _sorted;
    private IndirectCommand[] _commands;
    private int _commandCount;

    // Counting sort workspace
    private int[] _counts;
    private int[] _offsets;

    // GPU buffers
    private BufferAllocation[] _instanceBuffers;  // Per frame-in-flight
    private BufferAllocation _indirectBuffer;

    public void Add(MeshHandle mesh, uint transformIndex, uint textureIndex, uint color, uint flags);
    public void Flush(int frameIndex);
    public void Draw(Vk vk, CommandBuffer cmd, int frameIndex);
    public void Reset();
}
```

---

## Integration

### Quad Mesh

The 2D quad becomes mesh slot 0 in MeshRegistry:

```csharp
// During initialization
MeshHandle _quadMesh = MeshRegistry.RegisterMesh(quadVertices, quadIndices);
// _quadMesh.Id == 0
```

### Unified Draw API

```csharp
// 2D (uses quad mesh implicitly)
public static void DrawTexture(Texture texture, float x, float y, ...)
{
    var transform = Matrix4x4.CreateScale(texture.Width, texture.Height, 1) * ...;
    int transformIndex = TransformBuffer.Allocate();
    TransformBuffer.SetTransform(transformIndex, transform);

    IndirectBatcher.Add(_quadMesh, transformIndex, texture.Index, color, uvOffset);
}

// 3D (explicit mesh)
public static void DrawMesh(MeshHandle mesh, Matrix4x4 transform, Texture texture, ...)
{
    int transformIndex = TransformBuffer.Allocate();
    TransformBuffer.SetTransform(transformIndex, transform);

    IndirectBatcher.Add(mesh, transformIndex, texture.Index, color, flags);
}
```

### Shader Compatibility

The instanced vertex shader doesn't change - it still reads:
- Transform from SSBO via `TransformIndex`
- Texture from bindless array via `TextureIndex`

The only difference is the draw call now uses indirect buffer instead of direct parameters.

---

## Future: GPU Culling & Sorting

### Why Counting/Radix Sort is GPU-Perfect

Radix sort is one of the most parallelizable algorithms:

```
Pass 1: Count (parallel)
┌─────────────────────────────────────────────────────────────┐
│  Thread 0    Thread 1    Thread 2    Thread 3    ...        │
│     ↓           ↓           ↓           ↓                   │
│  inst[0]    inst[1]    inst[2]    inst[3]                  │
│     ↓           ↓           ↓           ↓                   │
│  atomicAdd(counts[meshId], 1)  ← Each thread independent   │
└─────────────────────────────────────────────────────────────┘

Pass 2: Prefix Sum (parallel scan, O(log n) depth)
┌─────────────────────────────────────────────────────────────┐
│  counts:  [3, 1, 2, 0, 4, ...]                              │
│                    ↓ parallel scan                          │
│  offsets: [0, 3, 4, 6, 6, 10, ...]                          │
└─────────────────────────────────────────────────────────────┘

Pass 3: Scatter (parallel)
┌─────────────────────────────────────────────────────────────┐
│  Thread 0    Thread 1    Thread 2    Thread 3    ...        │
│     ↓           ↓           ↓           ↓                   │
│  dest = atomicAdd(writeHead[meshId], 1)                    │
│  output[dest] = input[threadId]  ← Each thread independent │
└─────────────────────────────────────────────────────────────┘
```

**No dependencies between threads** (except atomics) = massive parallelism.

### GPU-Driven Rendering Pipeline

```
┌──────────────────────────────────────────────────────────────┐
│  CPU: Upload ALL instances (unsorted, unculled)              │
│  - Just append to buffer, no sorting                         │
│  - O(n) simple copy                                          │
└──────────────────────────────────────────────────────────────┘
                              ↓
┌──────────────────────────────────────────────────────────────┐
│  Compute Pass 1: Cull + Count                                │
│  - Each thread: test one instance against frustum            │
│  - If visible: atomicAdd(counts[meshId], 1)                  │
│  - Store visibility bit                                      │
└──────────────────────────────────────────────────────────────┘
                              ↓
┌──────────────────────────────────────────────────────────────┐
│  Compute Pass 2: Prefix Sum                                  │
│  - Parallel scan on counts[]                                 │
│  - Produces offsets[] and indirect commands                  │
└──────────────────────────────────────────────────────────────┘
                              ↓
┌──────────────────────────────────────────────────────────────┐
│  Compute Pass 3: Compact (Scatter)                           │
│  - Each thread: if visible, scatter to output[offset++]      │
│  - Result: tightly packed visible instances                  │
└──────────────────────────────────────────────────────────────┘
                              ↓
┌──────────────────────────────────────────────────────────────┐
│  Draw: vkCmdDrawIndexedIndirect                              │
│  - GPU reads commands written by compute                     │
│  - Zero CPU knowledge of what's visible                      │
└──────────────────────────────────────────────────────────────┘
```

### Why This Matters

| Approach | CPU Work | GPU Sync |
|----------|----------|----------|
| CPU sort + cull | O(n log n) | Upload every frame |
| GPU sort + cull | O(1) | Compute → Graphics barrier only |

With GPU-driven:
- CPU just uploads raw instances (memcpy)
- GPU does all the smart work
- Scales to millions of instances

### CPU Counting Sort = Training Wheels

By implementing counting sort on CPU now, we're learning the exact algorithm that will later run on GPU. The code structure maps 1:1:

| CPU | GPU Compute |
|-----|-------------|
| `for (i=0; i<n; i++) counts[mesh]++` | `atomicAdd(counts[meshId], 1)` |
| `for (m=0; m<M; m++) offsets[m] = sum` | Parallel prefix sum |
| `for (i=0; i<n; i++) out[head++] = in[i]` | `out[atomicAdd(head, 1)] = in[threadId]` |

So when we move to GPU, it's a translation, not a rewrite.
