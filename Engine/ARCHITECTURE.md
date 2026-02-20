# DerpLib Architecture Reference

This document describes the architecture of DerpLib, which Engine aims to replicate.

## Overview

DerpLib is a **Raylib-style API** built on **Vulkan** with:
- Simple static API facade (`Derp.Init()`, `Derp.BeginDrawing()`, etc.)
- Vulkan backend via Silk.NET
- SDF (Signed Distance Field) rendering for anti-aliased vector graphics
- Support for both discrete GPUs and unified memory (Apple Silicon)

---

## Layer Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    API Layer (Derp.cs)                      │
│  Static facade: Init, BeginDrawing, EndDrawing, DrawRect... │
└────────────────────────────┬────────────────────────────────┘
                             │
┌────────────────────────────┴────────────────────────────────┐
│                   Rendering Layer                            │
│  RenderPassManager, CommandRecorder, Batchers, SyncManager   │
└────────────────────────────┬────────────────────────────────┘
                             │
┌────────────────────────────┴────────────────────────────────┐
│                    Shader Layer                              │
│  GraphicsShader, TexturedShader, Mesh3DShader, SDF Shaders   │
└────────────────────────────┬────────────────────────────────┘
                             │
┌────────────────────────────┴────────────────────────────────┐
│                     Core Layer                               │
│  VkContext, Swapchain, Window, DeviceCapabilities, Memory    │
└─────────────────────────────────────────────────────────────┘
```

---

## Core Layer

### VkContext (Instance + Device)
- Creates Vulkan instance with platform extensions
- MoltenVK support for macOS (portability extensions)
- Physical device selection with scoring (discrete > integrated > CPU)
- Queue family discovery (graphics + presentation, compute)
- Logical device creation with required extensions

### Swapchain
- Triple buffering (3 images max)
- Image format: BGRA8 SRGB preferred
- Present modes: FIFO (vsync), Immediate (uncapped), Mailbox (adaptive)
- **ImageViews created and managed here**
- AcquireNextImage / Present with OUT_OF_DATE detection

### Window (DerpWindow)
- Silk.NET GLFW wrapper
- Input state tracking: keys (down vs pressed-this-frame), mouse, scroll, text
- Frame-based state clearing at BeginDrawing()

### DeviceCapabilities
- Detects unified vs discrete memory architecture
- Memory type indices for allocation strategy
- Feature detection (BDA, descriptor indexing)

---

## Rendering Layer

### RenderPassManager
- **Two render passes:**
  1. DepthPass - 3D rendering (clears, writes depth)
  2. ColorPass - 2D overlay (loads existing, no depth)
- Creates framebuffers from swapchain ImageViews
- Manages shared depth buffer

### CommandRecorder
- Command pool + buffer per frame in flight
- BeginRecording / EndRecording
- Helper: TransitionImageLayout, ClearColorImage

### SyncManager
- 2 frames in flight
- Per-frame: Fence + ImageAvailable semaphore + RenderFinished semaphore
- WaitForCurrentFrame / AdvanceFrame

### Batchers

**Batcher (2D):**
- 60,000 vertices, 100,000 indices max
- Vertex2D: Position (8) + TexCoord (8) + Color (4) = 20 bytes
- AddQuad, AddTriangle, AddTriangleFan, AddLineQuad

**MeshBatcher (3D):**
- 10,000 instances max
- Sorts by mesh handle for efficient draw calls
- Per-instance: mat4 transform + MeshId + MaterialId

**MeshRegistry:**
- Concatenated global vertex/index buffers
- Generational handles for safe mesh management
- RegisterMesh / FreeMesh

---

## Shader Layer

### Pipeline Types

| Shader | Purpose | Vertex Input | Push Constants |
|--------|---------|--------------|----------------|
| GraphicsShader | 2D colored | Vertex2D | mat4 projection |
| TexturedShader | 2D textured | Vertex2D + TexIndex | mat4 projection |
| Mesh3DShader | 3D instanced | Vertex3D + InstanceData | mat4 view-proj |
| SdfShader | Screen SDF | None (fullscreen) | vec4 screen/tile |
| WorldSdfShader | World SDF | Quad + PlaneInstance | mat4 view-proj |

### Hot Reload System
- Frame-based polling (no FileSystemWatcher)
- GLSL → SPIR-V compilation via glslc
- Pipeline recreation at frame boundary after GPU idle

---

## SDF Layer

### Screen-Space (SdfRenderer)
- Tile-based culling (8x8 pixel tiles)
- Commands assigned to tiles by bounding box
- Curve tile walking for lines/beziers
- Single fullscreen triangle draw

### World-Space (WorldSdfRenderer)
- Instanced quad rendering in 3D
- Per-plane: transform, virtual resolution, command range
- Depth testing for proper occlusion

### Command System
- SdfCommand: 64 bytes
- Shapes: Circle, Rect, RoundedRect, Line, Bezier, Polyline, Glyph
- Boolean operations: Union, Intersect, Subtract (smooth variants)
- BeginGroup / EndGroup for CSG

---

## Frame Loop

```
BeginDrawing():
  1. Window.PollEvents()
  2. Window.UpdateInput()           ← Clear frame-specific input
  3. SyncManager.WaitForCurrentFrame()
  4. Swapchain.AcquireNextImage()
  5. CommandRecorder.BeginRecording()
  6. Reset batchers

[User draws: DrawRect, DrawTexture, DrawMesh3D...]
  → Enqueued to DrawCommandQueue (thread-safe)

EndDrawing():
  1. Drain DrawQueue → feed batchers
  2. DepthPass: Render 3D (clears)
  3. ColorPass: Render 2D (loads)
  4. CommandRecorder.EndRecording()
  5. QueueSubmit with semaphores
  6. Swapchain.Present()
  7. SyncManager.AdvanceFrame()
```

---

## Synchronization Flow

```
AcquireNextImage ──→ ImageAvailableSemaphore ──→ QueueSubmit
                                                      │
                    RenderFinishedSemaphore ←─────────┘
                           │
                           └──→ Present

Fence ──→ CPU waits at BeginDrawing for previous frame
```

---

## Memory Architecture

### Discrete GPU (PC)
- Transfer staging buffers (64 MB arena)
- Copy from host-visible → device-local

### Unified Memory (Apple Silicon)
- Direct host-visible + device-local buffers
- Smaller transfer arena (4 MB)
- No staging copies needed

---

## Key Design Patterns

1. **Static Facade** - All API through static Derp class
2. **Frame-in-Flight Isolation** - Per-frame resources (buffers, command pools)
3. **Batching** - Collect draws, render in bulk
4. **Bindless** - All meshes in concatenated buffers, texture arrays
5. **Two-Pass Rendering** - 3D depth pass, then 2D overlay
6. **Generational Handles** - Safe resource management
7. **Tile-Based SDF** - Reduce SDF evaluation per fragment

---

---

## Design Divergence: Public Compute API

**DerpLib keeps compute internal.** Engine will expose it to users.

### Rationale

Enables users to implement advanced rendering techniques (clustered forward, GPU particles, custom post-processing) without modifying the library.

### Target API

```csharp
// Storage Buffers
StorageBuffer Derp.CreateStorageBuffer<T>(int count, BufferUsage usage);
Span<T> Derp.MapStorageBuffer<T>(StorageBuffer buffer);  // Direct GPU memory access
void Derp.DestroyStorageBuffer(StorageBuffer buffer);

// Compute Shaders
ComputeShader Derp.LoadComputeShader(string path);
void Derp.SetComputeBuffer(ComputeShader shader, int binding, StorageBuffer buffer);
void Derp.DispatchCompute(ComputeShader shader, int groupsX, int groupsY, int groupsZ);
void Derp.ComputeBarrier();  // Sync compute → graphics

// Shader Buffer Binding (for fragment/vertex access)
void Derp.SetShaderBuffer(int binding, StorageBuffer buffer);
```

### Full Example: Clustered Forward Lighting

```csharp
// ============================================
// Clustered Forward - User Implementation
// ============================================

// GPU resources (created once at init)
static StorageBuffer lightBuffer;
static StorageBuffer clusterCounts;
static StorageBuffer clusterIndices;
static ComputeShader cullShader;
static Shader forwardShader;

// Cluster grid
static int tilesX, tilesY;
const int MaxLights = 256;
const int TileSize = 64;
const int DepthSlices = 24;
const int MaxLightsPerCluster = 32;

struct Light
{
    public Vector3 Position;
    public float Radius;
    public Vector3 Color;
    public uint Type;
}

// ============================================
// Init
// ============================================
void InitClusteredRenderer(int width, int height)
{
    tilesX = (width + TileSize - 1) / TileSize;
    tilesY = (height + TileSize - 1) / TileSize;
    int clusterCount = tilesX * tilesY * DepthSlices;

    lightBuffer = Derp.CreateStorageBuffer<Light>(MaxLights, BufferUsage.CpuToGpu);
    clusterCounts = Derp.CreateStorageBuffer<uint>(clusterCount, BufferUsage.GpuOnly);
    clusterIndices = Derp.CreateStorageBuffer<uint>(clusterCount * MaxLightsPerCluster, BufferUsage.GpuOnly);

    cullShader = Derp.LoadComputeShader("light_cull.comp.spv");
    forwardShader = Derp.LoadShader("clustered.vert.spv", "clustered.frag.spv");
}

// ============================================
// Render
// ============================================
void RenderScene(Camera3D camera, Model scene, ReadOnlySpan<Torch> torches)
{
    // Write lights directly to GPU memory (no CPU array, no copy)
    Span<Light> lights = Derp.MapStorageBuffer<Light>(lightBuffer);
    int lightCount = 0;

    lights[lightCount++] = new Light { Position = new(10, 5, 0), Color = new(1, 0, 0), Radius = 15f };
    lights[lightCount++] = new Light { Position = new(-5, 3, 8), Color = new(0, 0, 1), Radius = 10f };

    foreach (var torch in torches)
    {
        if (lightCount >= MaxLights) break;
        lights[lightCount++] = new Light { Position = torch.Pos, Color = torch.Color, Radius = torch.Radius };
    }

    // Cull lights to clusters
    Derp.SetComputeBuffer(cullShader, 0, lightBuffer);
    Derp.SetComputeBuffer(cullShader, 1, clusterCounts);
    Derp.SetComputeBuffer(cullShader, 2, clusterIndices);
    Derp.DispatchCompute(cullShader, tilesX, tilesY, DepthSlices);
    Derp.ComputeBarrier();

    // Draw with clustered shader
    Derp.SetShaderBuffer(0, lightBuffer);
    Derp.SetShaderBuffer(1, clusterCounts);
    Derp.SetShaderBuffer(2, clusterIndices);

    Derp.BeginShaderMode(forwardShader);
    Derp.DrawModel(scene);
    Derp.EndShaderMode();
}

// ============================================
// Game Loop
// ============================================
void Main()
{
    Derp.Init(1920, 1080, "Clustered Forward");
    InitClusteredRenderer(1920, 1080);

    var scene = Derp.LoadModel("scene.gltf");
    var camera = new Camera3D { /* ... */ };

    while (!Derp.WindowShouldClose())
    {
        Derp.BeginDrawing();
        Derp.ClearBackground(0x1a1a1aff);

        Derp.BeginMode3D(camera);
        RenderScene(camera, scene, torches);
        Derp.EndMode3D();

        Derp.DrawFPS(10, 10);
        Derp.EndDrawing();
    }

    Derp.Close();
}
```

---

## Design Divergence: Render Textures

**DerpLib renders directly to swapchain.** Engine will add off-screen render targets for post-processing.

### Target API

```csharp
// Render Textures
RenderTexture Derp.LoadRenderTexture(int width, int height);
RenderTexture Derp.LoadRenderTexture(int width, int height, TextureFormat format);
void Derp.UnloadRenderTexture(RenderTexture target);

// Render to texture
void Derp.BeginTextureMode(RenderTexture target);
void Derp.EndTextureMode();

// Shader texture binding
void Derp.SetShaderTexture(Shader shader, int binding, Texture texture);
```

### Full Example: Bloom Post-Processing

```csharp
// ============================================
// Bloom - User Implementation
// ============================================

// Render targets
static RenderTexture sceneRT;
static RenderTexture brightRT;
static RenderTexture blurRT;

// Shaders
static Shader thresholdShader;
static Shader blurHShader;
static Shader blurVShader;
static Shader compositeShader;

// ============================================
// Init
// ============================================
void InitBloom(int width, int height)
{
    // HDR scene target
    sceneRT = Derp.LoadRenderTexture(width, height, TextureFormat.RGBA16F);

    // Bloom targets (can be lower res)
    brightRT = Derp.LoadRenderTexture(width / 2, height / 2, TextureFormat.RGBA16F);
    blurRT = Derp.LoadRenderTexture(width / 2, height / 2, TextureFormat.RGBA16F);

    // Load shaders
    thresholdShader = Derp.LoadShader("fullscreen.vert.spv", "threshold.frag.spv");
    blurHShader = Derp.LoadShader("fullscreen.vert.spv", "blur_h.frag.spv");
    blurVShader = Derp.LoadShader("fullscreen.vert.spv", "blur_v.frag.spv");
    compositeShader = Derp.LoadShader("fullscreen.vert.spv", "composite.frag.spv");
}

// ============================================
// Render with Bloom
// ============================================
void RenderWithBloom(Camera3D camera, Model scene)
{
    // 1. Render scene to HDR target
    Derp.BeginTextureMode(sceneRT);
        Derp.ClearBackground(0x000000ff);
        Derp.BeginMode3D(camera);
            Derp.DrawModel(scene);
        Derp.EndMode3D();
    Derp.EndTextureMode();

    // 2. Extract bright pixels
    Derp.BeginTextureMode(brightRT);
        Derp.BeginShaderMode(thresholdShader);
            Derp.DrawTexture(sceneRT.Texture, 0, 0);
        Derp.EndShaderMode();
    Derp.EndTextureMode();

    // 3. Horizontal blur
    Derp.BeginTextureMode(blurRT);
        Derp.BeginShaderMode(blurHShader);
            Derp.DrawTexture(brightRT.Texture, 0, 0);
        Derp.EndShaderMode();
    Derp.EndTextureMode();

    // 4. Vertical blur (ping-pong back to brightRT)
    Derp.BeginTextureMode(brightRT);
        Derp.BeginShaderMode(blurVShader);
            Derp.DrawTexture(blurRT.Texture, 0, 0);
        Derp.EndShaderMode();
    Derp.EndTextureMode();

    // 5. Composite to screen
    Derp.BeginShaderMode(compositeShader);
        Derp.SetShaderTexture(compositeShader, 1, brightRT.Texture);  // Bloom
        Derp.DrawTexture(sceneRT.Texture, 0, 0);  // Scene (shader combines them)
    Derp.EndShaderMode();
}

// ============================================
// Game Loop
// ============================================
void Main()
{
    Derp.Init(1920, 1080, "Bloom Demo");
    InitBloom(1920, 1080);

    var scene = Derp.LoadModel("scene.gltf");
    var camera = new Camera3D { /* ... */ };

    while (!Derp.WindowShouldClose())
    {
        Derp.BeginDrawing();
        RenderWithBloom(camera, scene);
        Derp.DrawFPS(10, 10);
        Derp.EndDrawing();
    }

    Derp.UnloadRenderTexture(sceneRT);
    Derp.UnloadRenderTexture(brightRT);
    Derp.UnloadRenderTexture(blurRT);
    Derp.Close();
}
```

### Implementation Notes

- `RenderTexture` wraps: Image + ImageView + Framebuffer + RenderPass
- `BeginTextureMode` transitions image layout and begins render pass
- `EndTextureMode` transitions to shader-readable and ends render pass
- Supports HDR formats (RGBA16F, RGBA32F) for proper bloom
- Reuses existing RenderPassManager infrastructure internally

---

### Implementation Notes (Compute)

- DerpLib already has internal compute infrastructure (ComputeShader, ContextPool, DescriptorManager)
- Engine will wrap these as public API
- Keep Raylib-style simplicity: no raw Vulkan handles, no descriptor set management
- Library handles synchronization details behind `ComputeBarrier()`

---

## File Structure (DerpLib)

```
Engine/DerpLib/
├── DerpLib.cs              ← Static API facade
├── Core/
│   ├── VkContext.cs        ← Instance, device, queues
│   ├── Swapchain.cs        ← Swapchain + ImageViews
│   ├── Window.cs           ← GLFW window + input
│   ├── DeviceCapabilities.cs
│   └── VsyncMode.cs
├── Rendering/
│   ├── RenderPassManager.cs
│   ├── CommandRecorder.cs
│   ├── SyncManager.cs
│   ├── Batcher.cs          ← 2D batching
│   ├── MeshBatcher.cs      ← 3D instanced batching
│   ├── MeshRegistry.cs     ← Mesh storage
│   ├── Vertex3D.cs, Camera3D.cs
│   └── Color.cs
├── Shaders/
│   ├── GraphicsShader.cs
│   ├── TexturedShader.cs
│   ├── Mesh3DShader.cs
│   ├── ComputeShader.cs
│   ├── ShaderRegistry.cs
│   └── ShaderHotReloader.cs
├── Sdf/
│   ├── SdfRenderer.cs      ← Screen-space SDF
│   ├── WorldSdfRenderer.cs ← World-space SDF
│   ├── SdfShader.cs, WorldSdfShader.cs
│   ├── SdfCommand.cs       ← Command struct
│   └── CurveTileWalker.cs
└── Memory/
    ├── MemoryAllocator.cs
    └── TransferArena.cs
```
