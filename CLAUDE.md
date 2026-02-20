# CLAUDE.md
You are an elite Staff Software Engineer, who has elite knowledge of the software architecture and development. You are here to assign me in developing this service for my company. You assist me in shipping quickly, with minimal risk to our production systems.

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Quick Start

### Creating a New Game

Use the `/new-game` command or the bash script to create a new game from BaseTemplate:

```bash
# Claude Code command
/new-game MyGame --description "A cool game" --author "DerpTech"

# Or via bash script
bash scripts/new-game.sh MyGame --description "A cool game" --author "DerpTech"
```

This will:
1. Copy `Games/BaseTemplate/` to `Games/MyGame/`
2. Rename all namespaces from `BaseTemplate` to `MyGame`
3. Generate `ProjectConfig.json` with metadata
4. Add all projects to the solution

Then build and run:
```bash
bash scripts/run-game.sh MyGame
```

### Common Scripts

| Script | Description |
|--------|-------------|
| `scripts/new-game.sh <Name>` | Create new game from BaseTemplate |
| `scripts/run-game.sh <Name>` | Build and run a game |
| `scripts/build-game.sh <Name>` | Build a game |
| `scripts/test-game.sh <Name>` | Run game tests |
| `scripts/publish-game.sh <Name>` | Create release builds |

## Architecture Documentation

For detailed architecture information, see:
- **`Docs/MultiTableQuery.md`** - Multi-table query API for iterating across table types
- **`Engine/Docs/SdfApi.md`** - SDF rendering API (shapes, effects, warps, boolean ops)

## Tech Stack

- .NET 9, Raylib-cs, Friflo.Engine.ECS, Derp.DI
- Custom source generators: SimTable.Generator, Derp.Ecs, Pooled.Generator, GpuStruct.Generator, Profiling.Generator
- **Vulkan** renderer via Silk.NET

## GpuStruct Generator

Use `[GpuStruct]` for any struct that will be uploaded to GPU (SSBOs, uniform buffers). The generator handles std430 layout alignment automatically.

```csharp
using GpuStruct;

[GpuStruct]
public partial struct SdfCommand
{
    public partial uint Type { get; set; }
    public partial Vector2 Position { get; set; }
    public partial Vector2 Size { get; set; }
    public partial Vector4 Color { get; set; }
}
// Generates: [FieldOffset] attributes, SizeInBytes constant, Alignment constant
```

**Key rules:**
- Struct must be `partial`
- Use `partial` properties (not fields)
- No `bool` (use `uint` instead)
- vec3 gets 16-byte alignment (std430 rule)

## Vulkan Coordinate System

**CRITICAL:** Vulkan uses Y-down coordinates, which differs from OpenGL's Y-up convention.

**Current configuration:**
- Projection matrix flips Y (`proj.M22 *= -1` in `Camera3D.GetProjectionMatrix`)
- Screen-to-NDC conversion does NOT flip Y: `ndcY = (screenY / height) * 2 - 1`
- World-space UI meshes use flipped UVs so UV(0,0) = top-left

**Key rules:**
- **Screen/NDC coordinates**: Y=0 is at the TOP, Y increases DOWNWARD
- **When converting screen coords to NDC**: Do NOT flip Y
- **UV coordinates**: For UI rendering, UV(0,0) should be at TOP-LEFT of the mesh
- **Camera controls**: Dragging mouse DOWN should increase pitch (positive delta.Y → positive pitch)

**Screen to NDC conversion:**
```csharp
float ndcX = (screenX / width) * 2.0f - 1.0f;
float ndcY = (screenY / height) * 2.0f - 1.0f;  // No flip
```

**Quad mesh UVs for UI (Y-flipped for screen coords):**
```csharp
// UV(0,0) at top-left, UV(1,1) at bottom-right
new(new Vector3(-0.5f, -0.5f, 0f), new Vector2(0f, 1f)),  // bottom-left vertex
new(new Vector3( 0.5f, -0.5f, 0f), new Vector2(1f, 1f)),  // bottom-right vertex
new(new Vector3( 0.5f,  0.5f, 0f), new Vector2(1f, 0f)),  // top-right vertex
new(new Vector3(-0.5f,  0.5f, 0f), new Vector2(0f, 0f)),  // top-left vertex
```

## Matrix Convention (Row-Major to Column-Major)

**VERIFIED CORRECT - DO NOT CHANGE:**

C# `System.Numerics.Matrix4x4` is row-major. GLSL `mat4` is column-major. The memory layout difference acts as an **implicit transpose** when copying raw bytes to GPU.

**Convention (matches DerpLib):**
- C# computes `viewProj = view * proj` with NO explicit transpose
- Shader uses column-major multiplication: `gl_Position = viewProjection * model * vec4(pos, 1.0)`
- The implicit transpose from memory layout makes this work correctly

**DO NOT:**
- Add `Matrix4x4.Transpose()` calls to camera matrices
- Change shader multiplication order to row-major style (`pos * model * viewProjection`)

**Reference:** See `Engine/DerpLib/DerpLib.cs:1714` and `Engine/DerpLib.Examples/Shaders/mesh3d.vert:22-23`

## Input System

**Always use `InputManager` (`Shared/Core/Input/`) for all input handling.** Never use raw Raylib input calls directly.

**Key components:**
- `InputManager` - Central coordinator with action maps, context stack, and input buffering
- `InputAction` - Action-based input with phases (Started, Performed, Canceled)
- `InputHistoryBuffer` - Ring buffer storing action state history for frame-rate decoupling
- `DiscreteActionBuffer` - "Last wins" buffer for consuming discrete events in simulation ticks

**Input buffering pattern** (for render/simulation frame decoupling):
```csharp
// Setup
inputManager.EnableBuffering(historyFrames: 16);
inputManager.RegisterDiscreteAction("Jump");

// Render loop (60 FPS)
inputManager.Update(deltaTime);
inputManager.CaptureSnapshot(renderFrame);

// Simulation tick (30 FPS)
if (inputManager.DiscreteBuffer.ConsumeIfStarted("Jump", out var value))
    player.Jump();
inputManager.DiscreteBuffer.Clear();
```

**Lenient input timing** (input buffering for player forgiveness):
```csharp
// Accept input if pressed within last 5 frames
if (inputManager.WasActionStartedWithinFrames("Dash", withinFrames: 5))
    player.Dash();
```

## Derp.Ecs (Deterministic Simulation ECS)

Source-generated ECS for deterministic simulation with rollback support.

**See `Docs/DerpEcs.md` for full architecture.**

**Quick start:** Create a `partial` world class with a `Setup` method:
```csharp
using System.Diagnostics;
using DerpLib.Ecs.Setup;

public sealed partial class SimEcsWorld
{
    [Conditional(DerpEcsSetupConstants.ConditionalSymbol)]
    private static void Setup(DerpEcsSetupBuilder b)
    {
        b.Archetype<Enemy>()
            .Capacity(1000)
            .With<TransformComponent>()
            .With<CombatComponent>()
            .Spatial(position: nameof(TransformComponent.Position), cellSize: 32, gridSize: 32, originX: -512, originY: -512)
            .QueryRadius(position: nameof(TransformComponent.Position), maxResults: 256);
    }
}
```

**Components** are plain structs implementing `ISimComponent`:
```csharp
public struct TransformComponent : ISimComponent
{
    public Fixed64Vec2 Position;
    public Fixed64Vec2 Velocity;
}
```

**Systems** implement `IEcsSystem<TWorld>` and iterate by `.Count`:
```csharp
for (int row = 0; row < world.Enemy.Count; row++)
{
    ref var transform = ref world.Enemy.Transform(row);
    transform.Position += transform.Velocity * world.DeltaTime;
}
```

**Spawning/destroying** is queued and played back after each system:
```csharp
if (world.Enemy.TryQueueSpawn(out var spawn))
{
    spawn.Transform.Position = spawnPos;
    spawn.Combat.Health = Fixed64.FromInt(100);
}
world.Enemy.QueueDestroy(entityHandle);
```

See `Games/DerpTanks/Simulation/Ecs/` for a working example.

## Grid Generator

Generates type-safe coordinate types for multi-resolution grid systems (`Shared/Grid.Generator/`).

- **`[WorldGrid]`** (assembly-level): Defines the base tile grid. Generates `WorldTile` struct.
- **`[DataGrid]`** (struct-level): Defines coarser overlay grids. Each cell covers multiple tiles.

**Current grids** (`Catrillion/Simulation/Grids/GridDefinitions.cs`):
- `WorldTile` - 32px tiles, 256x256 (base grid)
- `NoiseCell` - 256px cells, 32x32 (zombie attraction)
- `ThreatCell` - 128px cells, 64x64 (zombie state/pathfinding)
- `PowerCell` - 32px cells, 256x256 (building power range)

All types have `FromPixel()`, `ToIndex()`, bounds checking, and cross-grid conversions (e.g., `tile.ToNoiseCell()`).

## Testing

**Replay files location:** `Outputs/Catrillion/bin/Release/net9.0/osx-arm64/publish/Logs/`

**Rollback determinism test (always use Release mode):**
```bash
cd Catrillion.Headless
dotnet run -c Release -- <replay.bin> --rollback
```

**Normal determinism test (multiple iterations):**
```bash
cd Catrillion.Headless
dotnet run -c Release -- <replay.bin> 3
```

**Quick test with latest replay:**
```bash
cd Catrillion.Headless
dotnet run -c Release -- "$(ls -t ../Outputs/Catrillion/bin/Release/net9.0/osx-arm64/publish/Logs/replay_*_p0.bin | head -1)" --rollback
```

## StringHandle (Zero-Allocation String Comparisons)

Use `StringHandle` (`Shared/Core/StringHandle.cs`) for strings that need comparison in hot paths. Converts strings to uint IDs for zero-allocation equality checks.

```csharp
using Core;

// Implicit conversion from string (allocates once on first use, then cached)
StringHandle handle = "MyAction";

// Zero-allocation comparison
if (handle == otherHandle) { }

// Implicit conversion back to string when needed
string str = handle;
```

**When to use:**
- Action names, state names, or any string compared repeatedly in per-frame code
- Dictionary keys that would otherwise require string hashing per lookup

**When NOT to use:**
- Strings that change every frame (e.g., formatted numbers) - use static labels or cache the formatted result

## ImFormatHandler (Zero-Allocation String Interpolation)

Use `ImFormatHandler` (`Engine/DerpLib/ImGUI/Core/ImFormatHandler.cs`) for zero-allocation string formatting in ImGUI. It's an `[InterpolatedStringHandler]` that writes directly to a scratch buffer.

```csharp
using ImGUI;

// Zero-allocation - uses ImFormatHandler automatically
Im.Label($"Value: {x:F2}", px, py);
Im.Text($"Score: {score}", px, py);

// Also works with sliders and other widgets
Im.Slider($"Speed: {speed:F1}", x, y, width, ref speed, 0, 100);
```

**Supported types:** `int`, `uint`, `long`, `float`, `double`, `bool`, `char`, `string`, `ReadOnlySpan<char>`, and any `ISpanFormattable`.

**WARNING - Enums allocate!** Enums are NOT directly supported. Using `$"{myEnum}"` calls `.ToString()` which boxes and allocates. Use `.ToName()` helpers instead:
```csharp
// BAD - allocates every call
Im.Label($"Zone: {dockSpace.PreviewZone}", x, y);

// GOOD - zero allocation (uses cached string array)
Im.Label($"Zone: {dockSpace.PreviewZone.ToName()}", x, y);
```
See `ImDockZoneExtensions.ToName()` for the pattern.

**How it works:**
- Methods accepting `ImFormatHandler` get the interpolated string handler automatically
- Uses a thread-static 256-char scratch buffer (no allocation)
- Format specifiers like `:F2`, `:N0` work as expected

**When to use:**
- All ImGUI text rendering with dynamic values (`Im.Label`, `Im.Text`, `Im.Button`, etc.)
- Debug overlays and per-frame displays

**When NOT to use:**
- Outside of ImGUI (use `string.Create` or `StringBuilder` pooling instead)
- For strings longer than 256 characters (will truncate)

## ImLayout (Prefer Group-Based Layout)

ImLayout has two layout systems. **Prefer the new group-based system** (`BeginGroup`/`EndGroup`) over the legacy cursor-based system (`CursorLayout`).

**New system (preferred):** 2-pass deferred layout with flex support
```csharp
// Widgets are measured, laid out, then drawn at EndGroup
ImLayout.BeginGroup(bounds, ImLayoutDirection.Vertical, alignCross: ImAlign.Stretch);
    var btn = ImLayout.Button("Click Me");
    var slider = ImLayout.Slider("Speed", speed, 0, 100);
    ImLayout.Label("Status: OK");
ImLayout.EndGroup();

// Query results after EndGroup
if (btn.Clicked) { /* handle click */ }
if (slider.Changed) speed = slider.Value;
```

**Legacy system (use sparingly):** Single-pass cursor-based layout
```csharp
// Immediate mode - widgets draw immediately at cursor position
ImLayout.BeginVertical(bounds);
    Im.Button("Click", 100, 30);  // Uses absolute positioning with AllocateRect
    Im.Slider("Speed", ref speed, 0, 100, 150);
ImLayout.EndVertical();
```

**When to use group-based:**
- Windows with auto-sizing content (uses `ImAlign.Stretch`)
- Any layout needing flex distribution
- Scroll views (`BeginScrollView`/`EndScrollView`)
- New UI code

**When cursor-based is acceptable:**
- Simple absolute-positioned UI
- Legacy code not worth migrating
- Performance-critical code where 2-pass overhead matters (rare)

## Critical Rules

**See AGENTS.md for complete guidelines**—the key points:

1. **Zero allocations in hot paths**: No lambdas, LINQ, or `new` in per-frame code. Use `StringHandle` for repeated string comparisons. String interpolation is OK in ImGUI methods (uses `ImFormatHandler`).
2. **Fixed64 for simulation**: Never use float/double in simulation logic (use `Catrillion.Utilities.Fixed64`)
3. **Deterministic iteration**: Never iterate HashSet/Dictionary in simulation code
4. **CommandBuffer for ECS**: No structural changes (Add/Remove component) inside query loops
5. **Iterate Count, not Capacity**: Always use `.Count` for Derp.Ecs table iteration. Rows are densely packed - never iterate over Capacity.
6. **Derp.Ecs Spatial Queries**: Use `QueryRadius(center, radius, results)` and `QueryAabb(min, max, results)` for spatial queries - zero-allocation via `Span<EntityHandle>`. Call `RebuildSpatialIndex()` once per frame after positions change.
7. **Prefer flat arrays over managed collections**: For caches and lookups in hot paths, prefer open-addressed arrays or linear scans over `Dictionary<>`. For small N (<64), a flat array with linear probe is faster and zero-allocation. Use `MemoryMarshal.AsBytes()` + `SequenceEqual()` for blittable struct comparison, `HashCode.AddBytes()` for hashing. Only allocate on cache miss (init-time), never on cache hit.
