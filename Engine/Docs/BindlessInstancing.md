# Bindless Instanced Rendering

GPU-driven bindless instancing supporting millions of instances with rotation, scale, and hierarchies.

## Architecture

```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│ Transform SSBO  │     │ Instance Buffer  │     │ Texture Array   │
│ mat4[] (64MB)   │     │ 20 bytes/inst    │     │ sampler2D[64]   │
│ binding=1       │     │ vertex attribs   │     │ binding=0       │
└────────┬────────┘     └────────┬─────────┘     └────────┬────────┘
         │                       │                        │
         └───────────────────────┼────────────────────────┘
                                 ▼
                    ┌────────────────────────┐
                    │   Instanced Shader     │
                    │ transforms[idx] * pos  │
                    │ textures[texIdx]       │
                    └────────────────────────┘
                                 │
                                 ▼
                    ┌────────────────────────┐
                    │    Push Constants      │
                    │  mat4 viewProjection   │
                    └────────────────────────┘
```

## Data Structures

### InstanceData (20 bytes, per-instance vertex attributes)

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct InstanceData
{
    public uint TransformIndex;   // 4 bytes - index into transform SSBO
    public uint TextureIndex;     // 4 bytes - index into texture array
    public uint PackedColor;      // 4 bytes - RGBA tint (packed)
    public Vector2 UVOffset;      // 8 bytes - sprite sheet frame offset

    public const int SizeInBytes = 20;
}
```

### Transform Buffer (SSBO)

- **Layout:** Array of `mat4` (64 bytes each)
- **Max capacity:** 1M transforms = 64MB per buffer
- **Double buffered:** One buffer per frame-in-flight
- **Hierarchies:** Pre-multiply parent × child on CPU before upload

```csharp
public sealed class TransformBuffer : IDisposable
{
    public const int MaxTransforms = 1_000_000;

    // Allocate a slot, returns transform index
    public int Allocate();

    // Set transform at index (marks dirty for upload)
    public void SetTransform(int index, in Matrix4x4 transform);

    // Upload dirty range to GPU for current frame
    public void Sync(int frameIndex);

    // Get GPU buffer handle for binding
    public Buffer GetBuffer(int frameIndex);
}
```

### Descriptor Layout (Set 0)

| Binding | Type | Count | Stage | Description |
|---------|------|-------|-------|-------------|
| 0 | `CombinedImageSampler` | 64 | Fragment | Bindless texture array |
| 1 | `StorageBuffer` | 1 | Vertex | Transform SSBO |

### Push Constants (64 bytes)

| Field | Size | Description |
|-------|------|-------------|
| `mat4 viewProjection` | 64 bytes | Ortho (2D) or perspective (3D) |

**Why push constants for projection?**
- ONE value for ALL instances (not per-instance)
- Fastest access (embedded in command buffer)
- Changes rarely (only on resize or camera move)
- Fits in 128-byte minimum guarantee

## Vertex Input Layout

### Binding 0: Per-Vertex (Vertex2D)

| Location | Format | Offset | Field |
|----------|--------|--------|-------|
| 0 | R32G32_SFLOAT | 0 | Position (vec2) |
| 1 | R32G32_SFLOAT | 8 | TexCoord (vec2) |
| 2 | R8G8B8A8_UNORM | 16 | Color (vec4) |

**InputRate:** Vertex (advances per vertex)

### Binding 1: Per-Instance (InstanceData)

| Location | Format | Offset | Field |
|----------|--------|--------|-------|
| 3 | R32_UINT | 0 | TransformIndex |
| 4 | R32_UINT | 4 | TextureIndex |
| 5 | R32_UINT | 8 | PackedColor |
| 6 | R32G32_SFLOAT | 12 | UVOffset (vec2) |

**InputRate:** Instance (advances per instance)

## Shaders

### instanced.vert

```glsl
#version 450

// Per-vertex (binding 0)
layout(location = 0) in vec2 inPosition;
layout(location = 1) in vec2 inTexCoord;
layout(location = 2) in vec4 inColor;

// Per-instance (binding 1)
layout(location = 3) in uint inTransformIndex;
layout(location = 4) in uint inTextureIndex;
layout(location = 5) in uint inPackedColor;
layout(location = 6) in vec2 inUVOffset;

// Outputs to fragment
layout(location = 0) out vec2 fragTexCoord;
layout(location = 1) out vec4 fragColor;
layout(location = 2) out flat uint fragTextureIndex;

// Push constant: camera matrix
layout(push_constant) uniform PushConstants {
    mat4 viewProjection;
} pc;

// Transform SSBO
layout(std430, set = 0, binding = 1) readonly buffer TransformBuffer {
    mat4 transforms[];
};

// Unpack RGBA from uint
vec4 unpackColor(uint packed) {
    return vec4(
        float((packed >>  0) & 0xFFu) / 255.0,
        float((packed >>  8) & 0xFFu) / 255.0,
        float((packed >> 16) & 0xFFu) / 255.0,
        float((packed >> 24) & 0xFFu) / 255.0
    );
}

void main() {
    // Fetch instance transform from SSBO
    mat4 model = transforms[inTransformIndex];

    // Transform to clip space
    vec4 worldPos = model * vec4(inPosition, 0.0, 1.0);
    gl_Position = pc.viewProjection * worldPos;

    // Apply UV offset for sprite sheets
    fragTexCoord = inTexCoord + inUVOffset;

    // Combine vertex color with instance tint
    vec4 instanceColor = unpackColor(inPackedColor);
    fragColor = inColor * instanceColor;

    // Pass texture index to fragment shader
    fragTextureIndex = inTextureIndex;
}
```

### instanced.frag

```glsl
#version 450
#extension GL_EXT_nonuniform_qualifier : enable

layout(location = 0) in vec2 fragTexCoord;
layout(location = 1) in vec4 fragColor;
layout(location = 2) in flat uint fragTextureIndex;

layout(location = 0) out vec4 outColor;

// Bindless texture array
layout(set = 0, binding = 0) uniform sampler2D textures[64];

void main() {
    // Sample texture using per-instance index
    vec4 texColor = texture(textures[nonuniformEXT(fragTextureIndex)], fragTexCoord);

    // Apply color tint
    outColor = texColor * fragColor;
}
```

## Draw Call Pattern

### Single Instanced Draw

```csharp
// Bind pipeline
vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, pipeline);

// Bind descriptor set (textures + transforms)
vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, layout,
    0, 1, &descriptorSet, 0, null);

// Push view-projection matrix
var vp = camera.ViewProjection;
vk.CmdPushConstants(cmd, layout, ShaderStageFlags.VertexBit, 0, 64, &vp);

// Bind mesh vertices (binding 0)
vk.CmdBindVertexBuffers(cmd, 0, 1, &meshVertexBuffer, &vertexOffset);

// Bind instance data (binding 1)
vk.CmdBindVertexBuffers(cmd, 1, 1, &instanceBuffer, &instanceOffset);

// Bind indices
vk.CmdBindIndexBuffer(cmd, indexBuffer, 0, IndexType.Uint16);

// Draw ALL instances of this mesh in ONE call
vk.CmdDrawIndexed(cmd,
    indexCount,           // indices per mesh (e.g., 6 for quad)
    instanceCount,        // number of instances (e.g., 100,000)
    0,                    // first index
    0,                    // vertex offset
    firstInstance);       // instance offset in buffer
```

### Batching by Mesh

For multiple mesh types, group instances and issue one draw per mesh:

```csharp
foreach (var batch in batcher.GetBatches())
{
    var mesh = meshRegistry.Get(batch.MeshId);

    // Bind mesh geometry
    vk.CmdBindVertexBuffers(cmd, 0, 1, &mesh.VertexBuffer, &offset);
    vk.CmdBindIndexBuffer(cmd, mesh.IndexBuffer, 0, IndexType.Uint16);

    // Draw all instances of this mesh
    vk.CmdDrawIndexed(cmd, mesh.IndexCount, batch.InstanceCount,
        0, 0, batch.FirstInstance);
}
```

## Memory Budget

### Per-Instance Cost

| Component | Size |
|-----------|------|
| InstanceData (vertex buffer) | 20 bytes |
| Transform (SSBO, if unique) | 64 bytes |
| **Total per unique instance** | **84 bytes** |

### Scale Examples

| Instance Count | Instance Buffer | Transform SSBO | Total |
|----------------|-----------------|----------------|-------|
| 10,000 | 200 KB | 640 KB | 840 KB |
| 100,000 | 2 MB | 6.4 MB | 8.4 MB |
| 1,000,000 | 20 MB | 64 MB | 84 MB |

## Usage Example

```csharp
// Setup (once)
var transforms = new TransformBuffer(allocator, framesInFlight);
var batcher = new InstanceBatcher(allocator, framesInFlight);
var textures = new TextureArray(vkDevice, MaxTextures);

// Load a texture
int texIdx = textures.Load("sprites/player.png");

// Per-frame: add instances
batcher.Reset();

for (int i = 0; i < 100_000; i++)
{
    // Allocate or reuse transform slot
    int transformIdx = transforms.Allocate();

    // Set world transform (position, rotation, scale)
    var transform = Matrix4x4.CreateScale(32, 32, 1)
                  * Matrix4x4.CreateRotationZ(angle)
                  * Matrix4x4.CreateTranslation(x, y, 0);
    transforms.SetTransform(transformIdx, transform);

    // Add instance
    batcher.Add(
        mesh: QuadMesh.Handle,
        transformIdx: transformIdx,
        textureIdx: texIdx,
        color: 0xFFFFFFFF,  // White (no tint)
        uvOffset: new Vector2(0, 0)  // First frame
    );
}

// Upload to GPU
transforms.Sync(frameIndex);
batcher.Build(frameIndex);

// Render
batcher.Render(cmd, frameIndex);
```

## Hierarchy Example

Pre-multiply parent × child transforms on CPU:

```csharp
// Parent at world position (100, 200)
var parentWorld = Matrix4x4.CreateTranslation(100, 200, 0);
transforms.SetTransform(parentIdx, parentWorld);

// Child offset (50, 0) from parent, rotated
var childLocal = Matrix4x4.CreateRotationZ(angle)
               * Matrix4x4.CreateTranslation(50, 0, 0);
var childWorld = childLocal * parentWorld;  // Pre-multiply!
transforms.SetTransform(childIdx, childWorld);
```

## DescriptorCache (Central Management)

Centralized caching for descriptor layouts, pools, and sets.

### Layout Caching

Cache layouts by binding signature - same bindings = same layout:

```csharp
public sealed class DescriptorCache : IDisposable
{
    // Layout cache: signature → layout
    private readonly Dictionary<LayoutSignature, DescriptorSetLayout> _layouts = new();

    // Pool per frame-in-flight (reset each frame)
    private readonly DescriptorPool[] _framePools;

    // Persistent pool for long-lived sets (bindless resources)
    private DescriptorPool _persistentPool;

    /// <summary>
    /// Get or create a layout matching the given bindings.
    /// </summary>
    public DescriptorSetLayout GetOrCreateLayout(ReadOnlySpan<DescriptorBinding> bindings);

    /// <summary>
    /// Allocate a descriptor set from the per-frame pool (reset each frame).
    /// </summary>
    public DescriptorSet AllocateTransient(int frameIndex, DescriptorSetLayout layout);

    /// <summary>
    /// Allocate a persistent descriptor set (for bindless resources).
    /// </summary>
    public DescriptorSet AllocatePersistent(DescriptorSetLayout layout);

    /// <summary>
    /// Reset per-frame pool at start of frame.
    /// </summary>
    public void ResetFramePool(int frameIndex);
}
```

### DescriptorBinding (Layout Signature)

```csharp
public readonly struct DescriptorBinding : IEquatable<DescriptorBinding>
{
    public uint Binding { get; init; }
    public DescriptorType Type { get; init; }
    public uint Count { get; init; }
    public ShaderStageFlags Stages { get; init; }
    public DescriptorBindingFlags Flags { get; init; }  // PartiallyBound, UpdateAfterBind
}

public readonly struct LayoutSignature : IEquatable<LayoutSignature>
{
    private readonly int _hash;
    private readonly DescriptorBinding[] _bindings;

    // Two signatures are equal if bindings match
}
```

### Usage Pattern

```csharp
// At init: create layout for bindless resources
var bindings = new DescriptorBinding[]
{
    new() { Binding = 0, Type = DescriptorType.CombinedImageSampler,
            Count = 64, Stages = ShaderStageFlags.FragmentBit,
            Flags = DescriptorBindingFlags.PartiallyBoundBit },
    new() { Binding = 1, Type = DescriptorType.StorageBuffer,
            Count = 1, Stages = ShaderStageFlags.VertexBit }
};
var layout = _descriptorCache.GetOrCreateLayout(bindings);
var set = _descriptorCache.AllocatePersistent(layout);

// Per-frame: reset transient pool
_descriptorCache.ResetFramePool(frameIndex);

// Per-draw (if needed): allocate transient set
var transientSet = _descriptorCache.AllocateTransient(frameIndex, someLayout);
```

## File Structure

```
src/Engine/
├── Core/
│   └── DescriptorCache.cs      // Central descriptor management
├── Rendering/
│   ├── TransformBuffer.cs      // Transform SSBO management
│   ├── InstanceBatcher.cs      // Instance collection + draw calls
│   ├── MeshRegistry.cs         // Shared geometry buffers
│   └── DrawCall.cs             // Draw call struct
├── Shaders/
│   ├── InstanceData.cs         // 20-byte instance struct
│   └── (modify) PipelineCache.cs
└── Resources/
    └── TextureArray.cs         // Bindless texture array

Engine/Content/Shaders/
├── instanced.vert              // Instanced vertex shader
└── instanced.frag              // Bindless texture fragment
```
