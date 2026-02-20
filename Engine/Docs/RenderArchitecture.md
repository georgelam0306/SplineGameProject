# Render Architecture

## Overview

The rendering system uses dynamic render passes triggered by camera changes. 3D content renders to an offscreen scene texture, post-processing happens in between, and 2D UI composites on top before presenting.

## Render Pass Flow

```
BeginCamera3D()         BeginCamera2D()
      │                       │
      ▼                       ▼
┌─────────────┐         ┌─────────────┐
│  3D Pass    │         │  2D Pass    │
│ color+depth │  ────►  │ color only  │
│ scene tex   │         │ swapchain   │
└─────────────┘         └─────────────┘
                  │
           Post-Processing
           (compute or blit)
```

## Pass Transitions

| Current State | Call | Action |
|---------------|------|--------|
| None | `BeginCamera3D()` | Start 3D pass (clear color+depth) |
| 3D pass | `BeginCamera3D()` | Same pass, new projection |
| 3D pass | `BeginCamera2D()` | End 3D, start 2D pass |
| None | `BeginCamera2D()` | Start 2D pass |
| 2D pass | `BeginCamera2D()` | Same pass, new projection |
| 2D pass | `BeginCamera3D()` | End 2D, start 3D pass (clear) |
| Any | `BeginTextureMode(rt)` | End current, start pass to rt |
| TextureMode | `EndTextureMode()` | End pass, return to previous state |

## Texture Types

### RenderTexture
Color + depth attachments. For offscreen 3D rendering, portals, mirrors.
```csharp
var rt = Derp.CreateRenderTexture(width, height);
```

### DepthTexture
Depth-only attachment. For single shadow maps.
```csharp
var shadow = Derp.CreateDepthTexture(2048, 2048);
```

### DepthTextureArray
Array of depth textures with comparison sampler. For shadow cascades, point light shadows, virtual shadow maps.
```csharp
var shadows = Derp.CreateDepthTextureArray(2048, 2048, layers: 16);
```

### StorageTexture
Compute shader read/write. For post-processing, GPU particle output.
```csharp
var storage = Derp.CreateStorageTexture(width, height);
```

## Descriptor Layout

### Set 0 (Engine-managed, bindless)
| Binding | Type | Description |
|---------|------|-------------|
| 0 | `sampler2D[256]` | Color textures (sprites, materials) |
| 1 | `buffer` | Transform SSBO (mat4[]) |
| 2 | `sampler2DShadow[16]` | Depth textures with comparison sampler |

### Set 1 (User-managed)
User creates and binds their own descriptor sets for custom data.

```csharp
var mySet = Derp.CreateDescriptorSet(layout);
mySet.BindBuffer(0, mySSBO);
mySet.BindTextureArray(1, myTextures);

Derp.BindDescriptorSet(1, mySet);
```

## Post-Processing

### Render-to-Texture (Fragment Shader)
```csharp
EndCamera3D();  // 3D pass ends, scene texture readable

Derp.BeginTextureMode(bloomTex);
    Derp.BeginShaderMode(thresholdShader);
        Derp.DrawTexture(sceneTexture, 0, 0);
    Derp.EndShaderMode();
Derp.EndTextureMode();

// More passes...

BeginCamera2D(uiCam);  // Composite + UI
```

### Compute Shader
```csharp
EndCamera3D();

Derp.BeginCompute(blurCompute);
    Derp.BindTexture(0, sceneTexture);  // Read
    Derp.BindImage(1, blurredTex);       // Write
    Derp.Dispatch(width/16, height/16, 1);
Derp.EndCompute();

BeginCamera2D(uiCam);
```

## Shadow Maps

Shadow maps use a separate depth texture array with comparison sampling.

```csharp
// Setup
var shadowMaps = Derp.CreateDepthTextureArray(2048, 2048, cascades: 4);

// Render shadow cascades
for (int i = 0; i < 4; i++)
{
    Derp.BeginTextureMode(shadowMaps, layer: i);
        BeginCamera3D(lightCams[i]);
            DrawMesh(world, ...);  // Shadow casters
        EndCamera3D();
    Derp.EndTextureMode();
}

// Main scene samples shadows
BeginCamera3D(mainCam);
    // Shader reads: texture(shadowArray, vec4(uv, cascade, compareDepth))
    DrawMesh(world, ...);
EndCamera3D();
```

## RenderPassManager

Manages render pass lifecycle and resources.

### Responsibilities
- Scene texture (color + depth) for 3D rendering
- Depth buffer creation and resize
- Pass creation with appropriate attachments
- Automatic pass transitions on camera changes
- Swapchain framebuffers for final output

### Resources Owned
```
sceneTexture     - RenderTexture for 3D pass
depthBuffer      - VkImage + VkImageView for depth
colorPass        - VkRenderPass (color only, for 2D)
depthPass        - VkRenderPass (color + depth, for 3D)
framebuffers     - Per-swapchain-image for final output
```

## Camera Structs

### Camera3D
```csharp
public struct Camera3D
{
    public Vector3 Position;
    public Vector3 Target;
    public Vector3 Up;
    public float FovY;       // Radians
    public float Near;
    public float Far;

    public Matrix4x4 GetViewMatrix();
    public Matrix4x4 GetProjectionMatrix(float aspect);
    public Matrix4x4 GetViewProjection(float aspect);
}
```

### Camera2D
```csharp
public struct Camera2D
{
    public Vector2 Offset;   // Screen offset
    public Vector2 Target;   // World target
    public float Rotation;   // Radians
    public float Zoom;

    public Matrix4x4 GetProjection(float width, float height);
}
```

## Implementation Order

1. **RenderPassManager** - depth buffer, pass creation
2. **Camera3D / Camera2D** - structs with matrix methods
3. **BeginCamera3D / EndCamera3D** - pass transitions, projection
4. **RenderTexture** - offscreen rendering
5. **DepthTexture / DepthTextureArray** - shadow maps
6. **StorageTexture + Compute** - compute dispatch
7. **DrawMesh** - 3D mesh rendering with depth test
