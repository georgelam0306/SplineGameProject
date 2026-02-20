# GPU-Driven Rendering Plan

## Goal
Move from CPU-bound instance processing to fully GPU-driven rendering, targeting 1M+ instances at 60fps.

## Current Architecture (CPU-bound)

```
CPU per frame:
  For each instance:
    1. Compute matrix (Scale × Rotation × Translation)  ← CPU bound
    2. Allocate transform slot
    3. Add to batcher (mesh grouping)

  Flush:
    4. Count sort by mesh type                          ← CPU bound
    5. Build DrawIndirectCommands
    6. Upload transforms (64 bytes × N)
    7. Upload instances (20 bytes × N)

GPU:
    DrawIndexedIndirect
```

**Bottlenecks at 150k instances:**
- 450k matrix operations on CPU
- 150k iterations for counting sort
- 84MB upload per frame

## Target Architecture (GPU-driven)

```
CPU per frame:
    Upload InstanceParams[] (32 bytes × N)              ← Only upload

GPU Compute:
    Pass 1: Count instances per mesh (atomics)
    Pass 2: Prefix sum for offsets
    Pass 3: Scatter + generate matrices
    Pass 4: Build DrawIndirectCommands

GPU Render:
    DrawIndexedIndirectCount
```

**At 1M instances:**
- 32MB upload (vs 84MB)
- Zero CPU iteration over instances
- All sorting/matrix math on GPU

---

## Data Structures

### InstanceParams (CPU → GPU input)
```csharp
[StructLayout(LayoutKind.Sequential)]
public struct InstanceParams
{
    public Vector3 Position;    // 12 bytes
    public float RotationY;     // 4 bytes
    public float Scale;         // 4 bytes
    public uint MeshId;         // 4 bytes
    public uint TextureId;      // 4 bytes
    public uint PackedColor;    // 4 bytes
}                               // 32 bytes total
```

### GPU Buffers
| Buffer | Size (1M instances) | Usage |
|--------|---------------------|-------|
| InstanceParams | 32 MB | Input from CPU |
| Transforms | 64 MB | Generated matrices |
| SortedInstances | 20 MB | Scattered instance data |
| MeshCounts | 1 KB | Atomic counters per mesh |
| MeshOffsets | 1 KB | Prefix sum results |
| DrawCommands | 5 KB | Indirect draw commands |

---

## Derp Compute API

Expose compute shaders through the Raylib-style API for both internal use and user compute shaders.

### Public API
```csharp
// Load compute shader
ComputeShader shader = Derp.LoadComputeShader("matrix_gen");

// Create GPU storage buffer
StorageBuffer buffer = Derp.CreateStorageBuffer(sizeInBytes);

// Write data to buffer
Derp.UpdateStorageBuffer(buffer, data);

// Read data from buffer (for debugging/readback)
Derp.ReadStorageBuffer(buffer, destination);

// Dispatch compute shader
Derp.BeginCompute();
Derp.BindStorageBuffer(0, buffer);        // binding = 0
Derp.BindStorageBuffer(1, otherBuffer);   // binding = 1
Derp.SetComputePushConstants(pushData);   // optional
Derp.DispatchCompute(shader, groupsX, groupsY, groupsZ);
Derp.EndCompute();  // inserts memory barrier before render
```

### Internal Types
```csharp
/// <summary>
/// Handle to a GPU storage buffer (SSBO).
/// </summary>
public readonly struct StorageBuffer
{
    internal readonly int Index;
    public readonly int SizeInBytes;
}
```

### Synchronization
- `Derp.EndCompute()` inserts a memory barrier (compute → vertex/fragment)
- Multiple dispatches between Begin/End share the same barrier
- Barrier ensures compute writes are visible to subsequent render pass

### Files to Create
- `src/Engine/Rendering/StorageBuffer.cs` - Handle + registry
- `src/Engine/Rendering/ComputeDispatcher.cs` - Dispatch state machine

### Files to Modify
- `src/Engine/Derp.cs` - Add compute API methods
- `src/Engine/Engine.cs` - Create compute resources

---

## Implementation Phases

### Phase 1: Compute Infrastructure ✅
**Goal:** Get a compute shader running

**Files created:**
- `src/Engine/Shaders/ComputeShader.cs` - Runtime loader
- `src/Engine/Assets/CompiledComputeShader.cs` - Asset type
- `Engine/Content/Shaders/test_compute.comp` - Test shader

**Files modified:**
- `src/Engine/Shaders/PipelineCache.cs` - GetOrCreateCompute()
- `src/Engine.Build/ShaderImporter.cs` - .comp support
- `src/Engine.Build/ShaderCompiler.cs` - compute stage
- `src/Engine.Build/Program.cs` - compile .comp files

**Completed:**
1. ✅ Add `CreateComputePipeline()` to PipelineCache
2. ✅ Add compute shader loading (`.comp` → `.spv`)
3. ✅ Create test compute shader (writes idx*2)

**Validation:** Shader compiles to 1224 bytes SPIR-V

---

### Phase 1.5: Derp Compute API ✅
**Goal:** Expose compute through Raylib-style API

**Files created:**
- `src/Engine/Rendering/ComputeDispatcher.cs` - Manages compute state, descriptor sets, barriers

**Files modified:**
- `src/Engine/Derp.cs` - Added compute API methods
- `src/Engine/Engine.cs` - Created ComputeDispatcher, initialized it
- `src/Engine/EngineComposition.cs` - Added ComputeDispatcher binding

**Completed:**
1. ✅ Reused existing `StorageBuffer<T>` and `Derp.CreateBuffer<T>()`
2. ✅ Created ComputeDispatcher with Begin/End/Dispatch pattern
3. ✅ Added `Derp.LoadComputeShader(name)`
4. ✅ Added `Derp.BeginCompute()` / `Derp.EndCompute()`
5. ✅ Added `Derp.BindStorageBuffer(binding, buffer)`
6. ✅ Added `Derp.DispatchCompute(shader, groupsX, groupsY, groupsZ)`
7. ✅ Added `Derp.SetComputePushConstants<T>(shader, data)`
8. ✅ Memory barrier in EndCompute (compute → vertex/fragment/indirect)

**Public API:**
```csharp
// Load compute shader
ComputeShader shader = Derp.LoadComputeShader("test_compute");

// Create GPU storage buffer (reuses existing API)
StorageBuffer<uint> buffer = Derp.CreateBuffer<uint>(1024);
buffer.Upload(data);

// Dispatch compute shader
Derp.BeginCompute();
Derp.BindStorageBuffer(0, buffer);
Derp.SetComputePushConstants(shader, new PushConstants { count = 1024 });
Derp.DispatchCompute(shader, groupsX: 4, pushConstantSize: 4);
Derp.EndCompute();  // inserts memory barrier before render
```

**Validation:** Build succeeds, compute shader compiles to 1224 bytes SPIR-V

---

### Phase 2: Matrix Generation on GPU (In Progress)
**Goal:** Move matrix computation to GPU, keep CPU sorting

**Files created:**
- `src/Engine/Rendering/ComputeTransforms.cs` ✅
- `Engine/Content/Shaders/matrix_gen.comp` ✅

**Files modified:**
- `src/Engine/Engine.cs` - added ComputeTransforms initialization ✅
- `src/Engine/EngineComposition.cs` - DI binding ✅
- `src/Engine/Derp.cs` - exposed ComputeTransforms ✅

**Completed:**
1. ✅ Created `InstanceParams` struct (32 bytes per instance)
2. ✅ Created `ComputeTransforms` class with Add/Dispatch/Reset
3. ✅ Created `matrix_gen.comp` shader (4622 bytes SPIR-V)
4. ✅ Integrated into Engine lifecycle
5. ✅ Test dispatches 441 instances without errors

**Pending:**
- [ ] Rebind descriptor set to use GPU-generated transforms
- [ ] Visual parity test (render with GPU matrices, compare with CPU)

**Compute shader (matrix_gen.comp):**
```glsl
#version 450
layout(local_size_x = 256) in;

struct InstanceParams {
    vec3 position;
    float rotationY;
    float scale;
    uint meshId;
    uint textureId;
    uint packedColor;
};

layout(std430, set = 0, binding = 0) readonly buffer Params {
    InstanceParams params[];
};

layout(std430, set = 0, binding = 1) writeonly buffer Transforms {
    mat4 transforms[];
};

layout(push_constant) uniform PC {
    uint instanceCount;
};

void main() {
    uint i = gl_GlobalInvocationID.x;
    if (i >= instanceCount) return;

    InstanceParams p = params[i];

    float c = cos(p.rotationY);
    float s = sin(p.rotationY);
    float sc = p.scale;

    // Scale * RotationY * Translation combined
    transforms[i] = mat4(
        sc * c,  0.0, sc * s, 0.0,
        0.0,     sc,  0.0,    0.0,
       -sc * s,  0.0, sc * c, 0.0,
        p.position.x, p.position.y, p.position.z, 1.0
    );
}
```

**CPU changes:**
```csharp
// Instead of:
var transform = Matrix4x4.CreateScale(scale) *
                Matrix4x4.CreateRotationY(rot) *
                Matrix4x4.CreateTranslation(pos);
int idx = transformBuffer.Allocate();
transformBuffer.SetTransform(idx, transform);

// Now:
int idx = paramsBuffer.Add(pos, rot, scale, meshId, texId, color);
```

**Validation:** Rendering looks identical, CPU usage drops

---

### Phase 3: GPU Counting
**Goal:** Move instance counting to GPU

**Files to create:**
- `Engine/Content/Shaders/count_instances.comp`

**Files to modify:**
- `src/Engine/Rendering/IndirectBatcher.cs` - GPU count mode

**Compute shader (count_instances.comp):**
```glsl
#version 450
layout(local_size_x = 256) in;

struct InstanceParams {
    vec3 position;
    float rotationY;
    float scale;
    uint meshId;
    uint textureId;
    uint packedColor;
};

layout(std430, set = 0, binding = 0) readonly buffer Params {
    InstanceParams params[];
};

layout(std430, set = 0, binding = 1) buffer MeshCounts {
    uint counts[];  // 256 mesh slots
};

layout(push_constant) uniform PC {
    uint instanceCount;
};

void main() {
    uint i = gl_GlobalInvocationID.x;
    if (i >= instanceCount) return;

    uint meshId = params[i].meshId;
    atomicAdd(counts[meshId], 1);
}
```

**Validation:** Counts match CPU counting

---

### Phase 4: GPU Prefix Sum
**Goal:** Compute offsets on GPU

**Files to create:**
- `Engine/Content/Shaders/prefix_sum.comp`

**Compute shader (prefix_sum.comp):**
```glsl
#version 450
layout(local_size_x = 256) in;

layout(std430, set = 0, binding = 0) buffer MeshCounts {
    uint counts[];  // Input: counts per mesh
};

layout(std430, set = 0, binding = 1) buffer MeshOffsets {
    uint offsets[];  // Output: exclusive prefix sum
};

shared uint temp[512];

void main() {
    // Blelloch parallel prefix sum
    // (implementation for 256 mesh types max)
    uint tid = gl_LocalInvocationID.x;
    uint n = 256;

    // Load
    temp[tid] = (tid < n) ? counts[tid] : 0;
    barrier();

    // Up-sweep (reduce)
    for (uint stride = 1; stride < n; stride *= 2) {
        uint idx = (tid + 1) * stride * 2 - 1;
        if (idx < n) {
            temp[idx] += temp[idx - stride];
        }
        barrier();
    }

    // Clear last
    if (tid == 0) temp[n - 1] = 0;
    barrier();

    // Down-sweep
    for (uint stride = n / 2; stride > 0; stride /= 2) {
        uint idx = (tid + 1) * stride * 2 - 1;
        if (idx < n) {
            uint t = temp[idx - stride];
            temp[idx - stride] = temp[idx];
            temp[idx] += t;
        }
        barrier();
    }

    // Store
    if (tid < n) {
        offsets[tid] = temp[tid];
    }
}
```

**Validation:** Offsets match CPU prefix sum

---

### Phase 5: GPU Scatter
**Goal:** Scatter instances to sorted order + generate matrices in one pass

**Files to create:**
- `Engine/Content/Shaders/scatter_instances.comp`

**Compute shader (scatter_instances.comp):**
```glsl
#version 450
layout(local_size_x = 256) in;

struct InstanceParams {
    vec3 position;
    float rotationY;
    float scale;
    uint meshId;
    uint textureId;
    uint packedColor;
};

struct InstanceData {
    uint transformIndex;
    uint textureIndex;
    uint packedColor;
    uint packedUVOffset;
};

layout(std430, set = 0, binding = 0) readonly buffer Params {
    InstanceParams params[];
};

layout(std430, set = 0, binding = 1) buffer MeshOffsets {
    uint offsets[];  // Atomically incremented during scatter
};

layout(std430, set = 0, binding = 2) writeonly buffer SortedInstances {
    InstanceData sorted[];
};

layout(std430, set = 0, binding = 3) writeonly buffer Transforms {
    mat4 transforms[];
};

layout(push_constant) uniform PC {
    uint instanceCount;
};

void main() {
    uint i = gl_GlobalInvocationID.x;
    if (i >= instanceCount) return;

    InstanceParams p = params[i];

    // Get sorted position via atomic increment
    uint destIdx = atomicAdd(offsets[p.meshId], 1);

    // Generate matrix
    float c = cos(p.rotationY);
    float s = sin(p.rotationY);
    float sc = p.scale;

    transforms[destIdx] = mat4(
        sc * c,  0.0, sc * s, 0.0,
        0.0,     sc,  0.0,    0.0,
       -sc * s,  0.0, sc * c, 0.0,
        p.position.x, p.position.y, p.position.z, 1.0
    );

    // Write sorted instance data
    sorted[destIdx] = InstanceData(
        destIdx,        // transformIndex (identity after sorting)
        p.textureId,
        p.packedColor,
        0               // uvOffset
    );
}
```

**Validation:** Rendering still correct, full GPU sorting working

---

### Phase 6: GPU Command Generation
**Goal:** Build DrawIndirectCommands on GPU

**Files to create:**
- `Engine/Content/Shaders/build_commands.comp`

**Compute shader (build_commands.comp):**
```glsl
#version 450
layout(local_size_x = 64) in;

struct DrawCommand {
    uint indexCount;
    uint instanceCount;
    uint firstIndex;
    int  vertexOffset;
    uint firstInstance;
};

struct MeshInfo {
    uint indexCount;
    uint firstIndex;
    int  vertexOffset;
    uint padding;
};

layout(std430, set = 0, binding = 0) readonly buffer MeshCounts {
    uint counts[];
};

layout(std430, set = 0, binding = 1) readonly buffer MeshOffsetsFinal {
    uint offsets[];  // After scatter, these are end positions
};

layout(std430, set = 0, binding = 2) readonly buffer MeshRegistry {
    MeshInfo meshes[];
};

layout(std430, set = 0, binding = 3) writeonly buffer Commands {
    DrawCommand commands[];
};

layout(std430, set = 0, binding = 4) buffer CommandCount {
    uint commandCount;
};

layout(push_constant) uniform PC {
    uint maxMeshTypes;
};

void main() {
    uint meshId = gl_GlobalInvocationID.x;
    if (meshId >= maxMeshTypes) return;

    uint count = counts[meshId];
    if (count == 0) return;

    // Atomically allocate command slot
    uint cmdIdx = atomicAdd(commandCount, 1);

    MeshInfo mesh = meshes[meshId];
    uint firstInstance = offsets[meshId] - count;  // Offset was incremented during scatter

    commands[cmdIdx] = DrawCommand(
        mesh.indexCount,
        count,
        mesh.firstIndex,
        int(mesh.vertexOffset),
        firstInstance
    );
}
```

**Final render call:**
```csharp
vk.CmdDrawIndexedIndirectCount(
    cmd,
    commandBuffer, 0,
    countBuffer, 0,
    maxCommands,
    sizeof(DrawCommand)
);
```

**Validation:** Full GPU-driven pipeline, 1M instances at 60fps

---

## Pipeline Summary

```
Frame N:

CPU (minimal):
  ├─ Upload InstanceParams[N] to staging buffer
  └─ Record command buffer

GPU Compute:
  ├─ Clear MeshCounts buffer
  ├─ Pass 1: count_instances.comp    (N/256 dispatches)
  ├─ Pass 2: prefix_sum.comp         (1 dispatch)
  ├─ Copy offsets for scatter
  ├─ Pass 3: scatter_instances.comp  (N/256 dispatches)
  └─ Pass 4: build_commands.comp     (meshTypes/64 dispatches)

GPU Render:
  └─ DrawIndexedIndirectCount
```

## Memory Layout (1M instances)

| Buffer | Size | Notes |
|--------|------|-------|
| InstanceParams | 32 MB | CPU → GPU each frame |
| Transforms | 64 MB | GPU generated |
| SortedInstances | 20 MB | GPU generated |
| MeshCounts | 1 KB | Cleared each frame |
| MeshOffsets | 1 KB | Prefix sum result |
| MeshOffsetsCopy | 1 KB | Copy for scatter atomics |
| DrawCommands | 5 KB | GPU generated |
| CommandCount | 4 B | Atomic counter |

**Total GPU memory:** ~117 MB for 1M instances
**Per-frame upload:** 32 MB (vs 84 MB current)

## Milestones

- [x] Phase 1: Compute infrastructure (dispatch works)
- [x] Phase 1.5: Derp Compute API (public API exposed)
- [~] Phase 2: GPU matrix generation (infrastructure done, visual parity pending)
- [ ] Phase 3: GPU counting (counts match)
- [ ] Phase 4: GPU prefix sum (offsets match)
- [ ] Phase 5: GPU scatter (full GPU sort)
- [ ] Phase 6: GPU commands (DrawIndirectCount)
- [ ] Benchmark: 1M cubes at 60fps
