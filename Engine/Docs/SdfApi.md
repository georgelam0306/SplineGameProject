# SDF (Signed Distance Field) API

GPU-accelerated 2D vector graphics via compute shader.

## Overview

The SDF system renders shapes by evaluating signed distance functions on the GPU. Each pixel calculates its distance to shapes and renders with anti-aliasing, effects, and compositing.

## Architecture

```
┌─────────────┐     ┌─────────────┐     ┌──────────────┐
│  SdfBuffer  │────▶│ StorageBuffer│────▶│ Compute Shader│
│  (CPU)      │     │   (GPU)     │     │  (per-pixel) │
└─────────────┘     └─────────────┘     └──────────────┘
      │                                        │
      │ Commands, Lattices,                    │
      │ Polyline Headers/Points                ▼
      │                                 ┌──────────────┐
      └────────────────────────────────▶│ Output Image │
                                        └──────────────┘
```

## Quick Start

```csharp
// Initialize
Derp.InitSdf();

// In render loop
Derp.DrawSdfCircle(x, y, radius, r, g, b, a);
Derp.DrawSdfRect(x, y, halfW, halfH, r, g, b, a);
Derp.DrawSdfRoundedRect(x, y, halfW, halfH, cornerRadius, r, g, b, a);

// Render all shapes
Derp.RenderSdf();
```

### Compositing (Optional)

To composite the SDF result via the normal render pass system (instead of blitting to the swapchain), dispatch into the output texture and then draw it like any other texture:

```csharp
Derp.DispatchSdfToTexture();
Derp.DrawTexture(Derp.SdfOutputTexture, 0, 0);
```

## Shape Types

| Type | Factory Method | Description |
|------|----------------|-------------|
| Circle | `SdfCommand.Circle(center, radius, color)` | Filled circle |
| Rect | `SdfCommand.Rect(center, halfSize, color)` | Axis-aligned rectangle |
| RoundedRect | `SdfCommand.RoundedRect(center, halfSize, cornerRadius, color)` | Rounded corners |
| Line | `SdfCommand.Line(a, b, thickness, color)` | Capsule/line segment |
| Bezier | `SdfCommand.Bezier(p0, p1, p2, thickness, color)` | Quadratic bezier curve |
| Polyline | `SdfCommand.Polyline(headerIndex, thickness, color)` | Multi-segment line |
| FilledPolygon | `SdfCommand.FilledPolygon(headerIndex, color)` | Filled polygon (winding) |
| Glyph | `SdfCommand.Glyph(center, halfSize, uvRect, color)` | Glyph quad sampling a bound font atlas |

### Glyph Atlas Binding

Glyph commands require a font atlas to be bound before dispatch:

```csharp
var font = Derp.LoadFont("my_font");
Derp.DrawText(font, "Hello", noteX, noteY, fontSizePixels: 24f);
```

## Effects (Fluent API)

Chain effects on any shape:

```csharp
var cmd = SdfCommand.Circle(center, 50f, color)
    .WithStroke(strokeColor, strokeWidth)      // Outline
    .WithGlow(glowRadius)                       // Soft glow
    .WithLinearGradient(endColor, angle)        // Linear gradient fill
    .WithRadialGradient(endColor, centerX, centerY);  // Radial gradient fill
```

### Modifiers (Offset + Feather)

Modifiers operate on the signed-distance / coverage evaluation (vector feathering). They’re applied via the `SdfBuffer` modifier stack:

```csharp
buffer.PushModifierOffset(offsetX, offsetY); // optional
buffer.PushModifierFeather(radiusPx, SdfFeatherDirection.Both); // optional (Both/Outside/Inside)
buffer.Add(cmd);
buffer.PopModifier();
buffer.PopModifier();
```

### Effect Parameters

| Effect | Parameters | Description |
|--------|------------|-------------|
| Stroke | `color`, `width` | Outline around shape edge |
| Glow | `radius`, `softEdge` | Soft outer glow using fill color |
| LinearGradient | `endColor`, `angle` | Gradient from fill color to end color |
| RadialGradient | `endColor`, `centerX`, `centerY` | Radial gradient from center |
| Offset (modifier) | `offsetX`, `offsetY` | Shift SDF evaluation point |
| Feather (modifier) | `radiusPx`, `direction` | Feather/blur vector coverage (Gaussian-CDF style), optionally constrained to inside/outside |

## Warp System

Warps distort coordinate space before SDF evaluation.

Warps are applied via the warp stack (Push/Pop). Warps can be stacked arbitrarily; the last pushed warp applies first (i.e. `A(B(p))` for `Push(A)` then `Push(B)`).

### Warp Types

| Type | Factory | Parameters | Description |
|------|---------|------------|-------------|
| Wave | `SdfWarp.Wave(freq, amp, phase)` | frequency, amplitude, phase offset | Sinusoidal distortion |
| Twist | `SdfWarp.Twist(strength)` | radians per 100px from center | Rotational twist |
| Bulge | `SdfWarp.Bulge(strength, radius)` | positive=bulge, negative=pinch | Fisheye/pinch effect |
| Lattice | `SdfWarp.Lattice(index, scaleX, scaleY)` | 4x4 FFD grid index | Free-form deformation |

### Warp Stack API

```csharp
// Push warp - all subsequent shapes use this warp
Derp.PushSdfWave(frequency: 4f, amplitude: 10f, phase: time);
Derp.DrawSdfCircle(...);  // Warped
Derp.DrawSdfRect(...);    // Warped
Derp.PopSdfWarp();        // Remove warp

// Stack multiple warps (last pushed applies first)
Derp.PushSdfWave(frequency: 3f, amplitude: 8f, phase: time);   // A
Derp.PushSdfTwist(strength: 2.5f);                             // B
Derp.DrawSdfRoundedRect(...); // A(B(p))
Derp.PopSdfWarp();
Derp.PopSdfWarp();

// Lattice warp
var lattice = SdfLattice.Wave(amplitude: 0.3f, frequency: 2f, phase: time);
Derp.PushNewSdfLattice(lattice, scaleX: 200f, scaleY: 200f);
// ... shapes ...
Derp.PopSdfWarp();
```

### Lattice Presets

```csharp
SdfLattice.Identity()                    // No deformation
SdfLattice.Wave(amplitude, frequency, phase)  // Animated wave
SdfLattice.Bulge(strength)               // Center bulge/pinch
SdfLattice.Perspective(amount)           // Perspective tilt
```

## Boolean Operations

Combine shapes using CSG (Constructive Solid Geometry):

```csharp
var buffer = Derp.SdfBuffer;

// Union - combine shapes
buffer.BeginGroup(SdfBooleanOp.Union);
buffer.AddCircle(x1, y, r, ...);
buffer.AddCircle(x2, y, r, ...);
buffer.EndGroup(r, g, b, a);  // Final color

// Subtract - cut hole
buffer.BeginGroup(SdfBooleanOp.Subtract);
buffer.AddRect(...);    // Base shape
buffer.AddCircle(...);  // Hole
buffer.EndGroup(r, g, b, a);

// Intersect - overlap only
buffer.BeginGroup(SdfBooleanOp.Intersect);
buffer.AddCircle(...);
buffer.AddCircle(...);
buffer.EndGroup(r, g, b, a);
```

### Boolean Operations

| Op | Formula | Description |
|----|---------|-------------|
| `Union` | `min(a, b)` | Combine shapes |
| `Intersect` | `max(a, b)` | Overlap only |
| `Subtract` | `max(a, -b)` | Cut second from first |
| `SmoothUnion` | `smin(a, b, k)` | Blobby/metaball blend |
| `SmoothIntersect` | `smax(a, b, k)` | Smooth overlap |
| `SmoothSubtract` | `smax(a, -b, k)` | Soft carved edge |

### Smooth Operations

The `smoothness` parameter controls blend radius (in pixels):

```csharp
// Metaballs effect
buffer.BeginGroup(SdfBooleanOp.SmoothUnion, smoothness: 15f);
buffer.AddCircle(x1, y, r, ...);
buffer.AddCircle(x2, y, r, ...);
buffer.EndGroup(r, g, b, a);
```

### Nested Groups

Groups can be nested up to 8 levels:

```csharp
// (A ∪ B) - C
buffer.BeginGroup(SdfBooleanOp.Subtract);
    buffer.BeginGroup(SdfBooleanOp.SmoothUnion, 10f);
    buffer.AddCircle(...);  // A
    buffer.AddCircle(...);  // B
    buffer.EndGroup(r, g, b, a);
    buffer.AddRect(...);    // C (subtracted)
buffer.EndGroup(r, g, b, a);
```

### Warped Groups

Apply warp to entire group (all shapes warp uniformly):

```csharp
Derp.PushSdfWave(3f, 8f, time);
buffer.BeginGroup(SdfBooleanOp.SmoothUnion, 12f);
buffer.AddCircle(...);
buffer.AddCircle(...);
buffer.EndGroup(r, g, b, a);
Derp.PopSdfWarp();
```

## Polylines & Polygons

For complex paths with many points:

```csharp
var buffer = Derp.SdfBuffer;

// Add points and get header index
Span<Vector2> points = stackalloc Vector2[] { p0, p1, p2, p3 };
int headerIdx = buffer.AddPolyline(points);
buffer.AddPolylineCommand(headerIdx, thickness, r, g, b, a);

// Or one-liner
buffer.AddPolyline(points, thickness, r, g, b, a);

// Built-in shapes
buffer.AddPolygon(cx, cy, radius, sides, thickness, r, g, b, a);
buffer.AddStar(cx, cy, outerR, innerR, points, thickness, r, g, b, a);
buffer.AddWaveform(samples, startX, width, baseY, amplitude, thickness, r, g, b, a);
buffer.AddGraph(values, x, y, width, height, minVal, maxVal, thickness, r, g, b, a);

// Filled versions
buffer.AddFilledPolygon(cx, cy, radius, sides, r, g, b, a);
buffer.AddFilledStar(cx, cy, outerR, innerR, points, r, g, b, a);
```

## Performance Notes

- **Command limit**: 8192 per frame (configurable)
- **Polyline points**: 16K shared pool
- **Lattices**: 64 max per frame
- **Group nesting**: 8 levels max
- **Workgroup size**: 8x8 pixels (tile size)
- **Max commands per tile**: 512

### Tile-Based Optimization

The compute shader uses a single-dispatch tile-based approach:
1. **Phase 1**: 64 threads per 8x8 tile cooperatively scan all commands
2. Conservative bounding boxes computed in shader (no CPU bbox work)
3. Commands overlapping tile are added to shared memory list via `atomicAdd`
4. **Phase 1.5**: Sort tile command list for correct boolean ordering
5. **Phase 2**: Each thread renders its pixel using only tile-relevant commands

This reduces per-pixel iterations from O(N) to O(M) where M is commands in tile.

### Memory Layout

| Buffer | Binding | Contents |
|--------|---------|----------|
| Commands | 1 | SdfCommand[] (208 bytes each) |
| Lattices | 2 | SdfLattice[] (128 bytes each) |
| Headers | 3 | SdfPolylineHeader[] (24 bytes each) |
| Points | 4 | Vector2[] (8 bytes each) |
| ModifierNodes | 11 | SdfModifierNode[] (32 bytes each) |

## SdfCommand Struct (208 bytes)

```
Offset  Field           Type      Description
0       Type            uint      Shape type enum
4       Flags           uint      Warp type in bits 8-15
8       Position        vec2      Center/start point
16      Size            vec2      Half-extents/end point
24      Rotation        vec2      x=rotation radians, y=warp chain head pointer (uint-as-float)
32      Color           vec4      Fill color (RGBA 0-1)
48      Params          vec4      Shape params
64      StrokeColor     vec4      Stroke color
80      Effects         vec4      strokeWidth, glowRadius, softEdge, gradientType
96      GradientColor   vec4      Gradient end color
112     GradientParams  vec4      angle, centerX, centerY, reserved
128     WarpParams      vec4      x=gradientStopCount, y=modifier chain head pointer (uint-as-float)
144     BooleanParams   vec2      booleanOp, smoothness
152     [padding]       8 bytes   Alignment to 16 bytes
160     ClipRect        vec4      x,y,w,h (w < 0 disables)
176     TrimParams      vec4      stroke trim params
192     DashParams      vec4      stroke dash params
```

## Stress Test

Run the stress test to benchmark tile-based rendering with many shapes:

```bash
dotnet run --project examples/Engine.Examples -- stress
```

Controls:
- **P**: Add more shapes (+1 multiplier)
- **M**: Reduce shapes (-1 multiplier)

Each multiplier level adds ~170 shapes across all shape types with effects.

## Future (Phase 8)

- **Tweens**: Animate properties over time, shape morphing (`mix(sdfA, sdfB, t)`)
