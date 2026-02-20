# SDF-Based ImGUI System Architecture

## Overview

This document describes the architecture for a new ImGUI system in Engine that:
- Renders UI using the existing SDF compute shader pipeline
- Supports windows with drag/resize/focus
- Implements full Dear ImGui-style docking (nested splits, tabs, extraction)
- Enables true multi-viewport (windows can become native OS windows)

---

## System Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     ViewportManager (Singleton)                  │
│  - VkInstance, VkDevice (shared)                                │
│  - PipelineCache, TextureArray, MeshRegistry (shared)           │
│  - Coordinates multiple Viewports                               │
└────────────────────────────┬────────────────────────────────────┘
                             │
          ┌──────────────────┼──────────────────┐
          ▼                  ▼                  ▼
   ┌─────────────┐    ┌─────────────┐    ┌─────────────┐
   │ Viewport 0  │    │ Viewport 1  │    │ Viewport N  │
   │ (Primary)   │    │ (Extracted) │    │ (Extracted) │
   ├─────────────┤    ├─────────────┤    ├─────────────┤
   │ Window      │    │ Window      │    │ Window      │
   │ VkSurface   │    │ VkSurface   │    │ VkSurface   │
   │ Swapchain   │    │ Swapchain   │    │ Swapchain   │
   │ SdfBuffer   │    │ SdfBuffer   │    │ SdfBuffer   │
   │ SdfRenderer │    │ SdfRenderer │    │ SdfRenderer │
   │ ImContext   │    │ ImContext   │    │ ImContext   │
   │ DockSpace   │    │ (floating)  │    │ (floating)  │
   └─────────────┘    └─────────────┘    └─────────────┘
```

---

## Implementation Phases

### Phase 1: Core ImGUI Infrastructure

**Goal:** Basic ImGUI foundation with SDF rendering

#### File Structure
```
Engine/src/Engine/ImGui/
├── Im.cs                           # Static facade API
├── Core/
│   ├── ImContext.cs                # Per-viewport UI state
│   ├── ImStyle.cs                  # Theme configuration
│   ├── ImInput.cs                  # Input snapshot
│   ├── ImId.cs                     # ID hashing (FNV-1a)
│   ├── ImRect.cs                   # Rectangle utilities
│   └── ImFormatHandler.cs          # Zero-allocation string formatting
└── Rendering/
    ├── ImSdfDrawList.cs            # SDF command buffer
    └── ImDrawLayer.cs              # Z-order layers enum
```

#### ImContext (Per-Viewport State)

Central state management for each viewport:

```csharp
public class ImContext
{
    // Widget interaction state
    public int HotId;        // Widget under cursor
    public int ActiveId;     // Widget being dragged/interacted
    public int FocusId;      // Widget with keyboard focus

    // Stacks (fixed-size, no allocation)
    public int[] IdStack = new int[64];
    public ImStyle[] StyleStack = new ImStyle[32];
    public ImRect[] ClipStack = new ImRect[32];

    // Input state
    public ImInput Input;

    // Frame state
    public int FrameCount;
    public bool AnyHovered, AnyActive;
    public bool WantCaptureKeyboard, WantCaptureMouse;

    // API
    public int GetId(string label);
    public void PushId(string id);
    public void PopId();
    public bool IsHot(int id);
    public bool IsActive(int id);
    public void SetHot(int id);
    public void SetActive(int id);
}
```

#### ImStyle (Theme Configuration)

Immutable theme data:

```csharp
public struct ImStyle
{
    // Colors (packed ARGB uint)
    public uint Primary, Secondary, Background, Surface;
    public uint TextPrimary, TextSecondary, TextDisabled;
    public uint Hover, Active, Border;
    public uint TitleBar, TabActive, TabInactive;
    public uint DockPreview, ShadowColor;

    // Sizing
    public float FontSize;
    public float CornerRadius;
    public float BorderWidth;
    public float Padding, Spacing;
    public float TitleBarHeight;
    public float TabHeight;

    // SDF Effects
    public float HoverGlow;
    public float ActiveGlow;
    public float SoftEdge;
    public float ShadowRadius;
    public float ShadowOffsetX, ShadowOffsetY;

    public static ImStyle Default { get; }
}
```

#### ImSdfDrawList (Command Buffer)

Collects widget draw commands for SDF rendering:

```csharp
public class ImSdfDrawList
{
    private ImSdfCommand[] _commands;
    private int _count;
    private ImRect _currentClipRect;

    // Add commands (captures current clip state)
    public void AddRoundedRect(float x, float y, float w, float h,
        float radius, uint color, uint strokeColor = 0, float strokeWidth = 0);
    public void AddRoundedRectWithGlow(float x, float y, float w, float h,
        float radius, uint color, float glowRadius);
    public void AddRoundedRectWithShadow(...);
    public void AddCircle(float cx, float cy, float radius, uint color);
    public void AddLine(float x1, float y1, float x2, float y2, float thickness, uint color);

    // Clip management
    public void PushClipRect(ImRect rect);
    public void PopClipRect();

    // Flush to SdfBuffer
    public void Flush(SdfBuffer target);
    public void Clear();
}
```

#### SDF Modification Required

Add `ClipRect` field to `SdfCommand` for per-command clipping:

```csharp
// In SdfCommand.cs - add property
public partial Vector4 ClipRect { get; set; } // x,y,w,h; w<0 = no clip
```

Update compute shader (`sdf_compute.compute`) for early-out:

```glsl
// In per-command loop
if (cmd.clipRect.z >= 0.0) {
    vec2 clipMin = cmd.clipRect.xy;
    vec2 clipMax = clipMin + cmd.clipRect.zw;
    if (pixelPos.x < clipMin.x || pixelPos.x > clipMax.x ||
        pixelPos.y < clipMin.y || pixelPos.y > clipMax.y)
        continue; // Skip this command for this pixel
}
```

---

### Phase 2: Layout System

**Goal:** Flexible widget positioning with two modes

#### File Structure
```
Engine/src/Engine/ImGui/Layout/
├── ImLayout.cs                     # Unified API
├── ImLayoutContext.cs              # Cursor-based (single-pass)
├── ImLayoutGroup.cs                # Group-based (2-pass flex)
├── ImLayoutElement.cs              # Element descriptor
└── ImConstraints.cs                # Flex grow/shrink
```

#### Cursor Mode (Single-Pass Immediate)

For simple linear layouts:

```csharp
public class ImLayoutContext
{
    public void BeginVertical(ImRect bounds, float padding = 0, float spacing = 4);
    public void BeginHorizontal(ImRect bounds, float padding = 0, float spacing = 4);
    public void End();

    public ImRect AllocateRect(float width, float height);
    public void Space(float amount);
    public float RemainingWidth();
    public float RemainingHeight();
}
```

Usage:
```csharp
ImLayout.BeginVertical(panelBounds);
    if (ImButton.Draw("Save")) { ... }
    ImLabel.Draw("Status: OK");
    ImSlider.Draw("Volume", ref volume, 0, 100);
ImLayout.End();
```

#### Group Mode (Two-Pass Flex)

For flex-like layouts with deferred processing:

```csharp
public class ImLayoutGroupContext
{
    public void BeginGroup(ImRect bounds, ImLayoutDirection dir,
        ImAlign alignMain, ImAlign alignCross, float spacing, float padding);
    public int AddElement(ImLayoutElement elem);
    public ImLayoutGroupInfo EndGroup(); // Computes layout
}
```

Usage:
```csharp
ImLayout.BeginGroup(bounds, ImLayoutDirection.Horizontal, ImAlign.Start, ImAlign.Stretch);
    var btn1 = ImLayout.Button("Cancel");
    ImLayout.FlexSpace();
    var btn2 = ImLayout.Button("OK");
ImLayout.EndGroup();

if (btn1.Clicked) { ... }
if (btn2.Clicked) { ... }
```

---

### Phase 3: Core Widgets

**Goal:** Essential UI controls

#### File Structure
```
Engine/src/Engine/ImGui/Widgets/
├── ImButton.cs                     # Click interaction
├── ImLabel.cs                      # Text display
├── ImSlider.cs                     # Value dragging
├── ImCheckbox.cs                   # Toggle
├── ImPanel.cs                      # Container with background
└── ImScrollbar.cs                  # Scroll regions
```

#### Widget Pattern

All widgets follow the same pattern:

```csharp
public static class ImButton
{
    // Layout mode (uses current layout context)
    public static bool Draw(string label);
    public static bool Draw(string label, float width);

    // Explicit positioning
    public static bool DrawAt(string label, float x, float y, float w, float h);
}
```

#### Widget State Machine

```
Idle (ActiveId = 0)
  │
  └─ Mouse pressed over widget W
     │
     └─ SetActive(W.id) → Active state
        │
        ├─ Mouse held → Only W processes drag
        │
        └─ Mouse released
           │
           ├─ Still over W → "clicked" = true
           │
           └─ ClearActive() → Idle
```

#### SDF Shape Mapping

| Widget Element | SDF Shape | Effects |
|---------------|-----------|---------|
| Button background | RoundedRect | Stroke, Glow (hover) |
| Panel/Window | RoundedRect | Shadow, Stroke |
| Checkbox box | RoundedRect (small radius) | Stroke, Fill toggle |
| Slider track | RoundedRect | Subtle fill |
| Slider thumb | RoundedRect | Stroke, Glow (active) |
| Scrollbar thumb | RoundedRect | Glow (hover) |
| Text caret | Line | Animated alpha |

---

### Phase 4: Window System

**Goal:** Virtual windows within viewports

#### File Structure
```
Engine/src/Engine/ImGui/Windows/
├── ImWindow.cs                     # Window state struct
├── ImWindowManager.cs              # Manages windows per viewport
└── ImTitleBar.cs                   # Title bar rendering/drag
```

#### ImWindow State

```csharp
public struct ImWindow
{
    // Identity
    public int Id;
    public string Title;
    public ImWindowFlags Flags;

    // Geometry
    public ImRect Rect;
    public Vector2 MinSize, MaxSize;
    public Vector2 ScrollOffset;
    public Vector2 ContentSize;

    // State
    public bool IsOpen;
    public bool IsCollapsed;
    public bool IsDragging;
    public bool IsResizing;
    public ResizeDirection ResizeDir;
    public int ZOrder;

    // Viewport association
    public int ViewportId;
    public bool IsExtracted => ViewportId != 0;

    // Docking
    public bool IsDocked;
    public int DockNodeId;
}

[Flags]
public enum ImWindowFlags
{
    None = 0,
    NoTitleBar = 1 << 0,
    NoResize = 1 << 1,
    NoMove = 1 << 2,
    NoClose = 1 << 3,
    NoCollapse = 1 << 4,
    NoScroll = 1 << 5,
    NoBackground = 1 << 6,
    Dockable = 1 << 7,
}
```

#### Features

- **Title bar:** Drag to move, double-click to collapse
- **Resize handles:** Corner and edge detection
- **Focus:** Click brings to front (z-order management)
- **Close/collapse buttons:** In title bar
- **Content scissoring:** Clip widgets to window bounds

---

### Phase 5: Docking System

**Goal:** Full Dear ImGui-style docking with nested splits and tabs

#### File Structure
```
Engine/src/Engine/ImGui/Docking/
├── DockNode.cs                     # Base node class
├── DockSplit.cs                    # Split node (H/V with ratio)
├── DockLeaf.cs                     # Leaf node (tabs)
├── DockSpace.cs                    # Root container + operations
├── DockZone.cs                     # Preview zone enum
└── DockSerializer.cs               # Layout save/load (JSON)
```

#### Dock Tree Structure

Binary tree with two node types:

```
DockSpace
└── Root: DockNode
    ├── DockSplit
    │   ├── Direction: Horizontal | Vertical
    │   ├── Ratio: float (0.0-1.0)
    │   ├── First: DockNode
    │   └── Second: DockNode
    │
    └── DockLeaf
        ├── WindowIds: int[] (up to 16)
        ├── ActiveTabIndex: int
        └── TabBarBounds, ContentBounds
```

#### DockNode Base Class

```csharp
public abstract class DockNode
{
    public int Id;
    public ImRect Bounds;
    public DockNode? Parent;

    public abstract bool IsLeaf { get; }
    public abstract bool IsEmpty { get; }

    public abstract void UpdateLayout(ImRect bounds);
    public abstract DockLeaf? FindLeafAt(Vector2 point);
    public abstract DockLeaf? FindLeafWithWindow(int windowId);
}
```

#### DockSplit (Split Node)

```csharp
public sealed class DockSplit : DockNode
{
    public DockNode First;      // Left or Top
    public DockNode Second;     // Right or Bottom
    public DockSplitDirection Direction;
    public float Ratio;         // 0.1 to 0.9

    public const float SplitterWidth = 4f;

    public ImRect GetSplitterBounds();
}
```

#### DockLeaf (Leaf Node)

```csharp
public sealed class DockLeaf : DockNode
{
    private int[] _windowIds = new int[16];
    private int _windowCount;
    public int ActiveTabIndex;

    public const float TabBarHeight = 24f;

    public ImRect TabBarBounds => new(Bounds.X, Bounds.Y, Bounds.Width, TabBarHeight);
    public ImRect ContentBounds => new(Bounds.X, Bounds.Y + TabBarHeight,
                                        Bounds.Width, Bounds.Height - TabBarHeight);

    public void AddWindow(int windowId);
    public void RemoveWindow(int windowId);
    public void ReorderTab(int fromIndex, int toIndex);
}
```

#### Preview Zones

During drag, show where window will dock:

```
+------------------+
|       TOP        |  (25% from top edge)
|    +--------+    |
|    |        |    |
| L  | CENTER | R  |  (25% from each edge)
|    |        |    |
|    +--------+    |
|      BOTTOM      |  (25% from bottom edge)
+------------------+
```

```csharp
public enum DockZone
{
    None = 0,
    Center = 1,  // Add as tab to existing leaf
    Left = 2,    // Split horizontal, new on left
    Right = 3,   // Split horizontal, new on right
    Top = 4,     // Split vertical, new on top
    Bottom = 5   // Split vertical, new on bottom
}
```

#### Root-Level vs Node-Level Docking

- **Root-level:** Mouse near dock space edge AND target leaf touches that edge → splits entire tree
- **Node-level:** Splits only the target leaf

#### Dock Operations

```csharp
public class DockSpace
{
    public DockNode? Root;

    // Main operations
    public void DockWindow(int windowId, DockLeaf? target, DockZone zone);
    public void UndockWindow(int windowId);

    // Tree operations
    public DockLeaf? FindLeafAt(Vector2 point);
    public void UpdateLayout(ImRect bounds);
    public void PruneEmptyNodes();

    // Preview
    public DockZone PreviewZone;
    public DockLeaf? PreviewLeaf;
    public bool IsRootLevelDock;
    public ImRect PreviewRect;

    // Serialization
    public string SaveToJson();
    public void LoadFromJson(string json);
}
```

---

### Phase 6: Multi-Viewport

**Goal:** Windows can become native OS windows

#### File Structure
```
Engine/src/Engine/ImGui/Viewport/
├── Viewport.cs                     # Per-native-window resources
├── ViewportManager.cs              # Coordinates all viewports
├── ViewportInputRouter.cs          # Routes input to viewports
└── WindowExtractionHandler.cs      # Drag-out detection
```

#### Viewport (Per-Native-Window Resources)

```csharp
public sealed class Viewport : IDisposable
{
    // Identity
    public int Id { get; }
    public bool IsPrimary { get; }

    // Native window and Vulkan
    public Window Window { get; }
    public VkSurface Surface { get; }
    public Swapchain Swapchain { get; }

    // Rendering
    public CommandRecorder CommandRecorder { get; }
    public SyncManager SyncManager { get; }
    public RenderPassManager RenderPassManager { get; }

    // SDF
    public SdfBuffer SdfBuffer { get; }
    public SdfRenderer SdfRenderer { get; }

    // ImGUI
    public ImContext ImContext { get; }
    public DockSpace? DockSpace { get; }  // Primary only
    public List<ImWindow> FloatingWindows { get; }

    // Frame state
    public uint CurrentImageIndex { get; }
    public int FrameIndex => SyncManager.CurrentFrame;

    // Screen bounds
    public ImRect ScreenBounds => new(
        Window.Position.X, Window.Position.Y,
        Window.Width, Window.Height);
}
```

#### ViewportManager (Singleton)

```csharp
public sealed class ViewportManager : IDisposable
{
    // Shared Vulkan infrastructure
    private readonly VkInstance _vkInstance;
    private readonly VkDevice _vkDevice;
    private readonly MemoryAllocator _memoryAllocator;
    private readonly DescriptorCache _descriptorCache;
    private readonly PipelineCache _pipelineCache;
    private readonly TextureArray _textureArray;
    private readonly MeshRegistry _meshRegistry;

    // Viewports
    private readonly List<Viewport> _viewports;
    private Viewport _primaryViewport;
    private Viewport? _focusedViewport;

    // API
    public Viewport PrimaryViewport => _primaryViewport;
    public IReadOnlyList<Viewport> AllViewports => _viewports;

    public void Initialize(int width, int height, string title);
    public Viewport CreateViewport(ImWindow extractedWindow, Vector2 screenPos);
    public void DestroyViewport(Viewport viewport);

    // Frame loop
    public void BeginFrame();
    public void EndFrame();

    // Input
    public Viewport? GetViewportAtScreenPosition(Vector2 screenPos);
    public void RouteInput();
}
```

#### Window Extraction Flow

1. **Detect:** Window dragged outside viewport bounds (20px threshold)
2. **Ghost drag:** Show translucent preview following mouse
3. **On release:** Create new viewport at mouse position
4. **Transfer:** Move ImWindow to new viewport

```csharp
public class WindowExtractionHandler
{
    private const float ExtractionThreshold = 20f;

    public bool CheckExtraction(ImWindow window, Vector2 mouseScreenPos, Viewport viewport)
    {
        var bounds = viewport.ScreenBounds;
        return mouseScreenPos.X < bounds.X - ExtractionThreshold ||
               mouseScreenPos.X > bounds.Right + ExtractionThreshold ||
               mouseScreenPos.Y < bounds.Y - ExtractionThreshold ||
               mouseScreenPos.Y > bounds.Bottom + ExtractionThreshold;
    }

    public Viewport ExtractToNewViewport(ImWindow window, Vector2 screenPos);
}
```

#### Re-docking Flow

1. Drag window into another viewport
2. Hit-test dock zones in target viewport
3. On drop: dock window and destroy source viewport if empty

#### Engine.cs Refactor

Current `Engine` class will be split:
- **ViewportManager:** Shared resources, viewport coordination
- **Viewport:** Per-window resources (extracted from Engine)

---

## Resource Sharing Strategy

### Shared (Singleton, Owned by ViewportManager)

| Resource | Reason |
|----------|--------|
| VkInstance | One per application |
| VkDevice | One logical device |
| PipelineCache | Pipelines are device-specific |
| DescriptorCache | Layouts shared, sets per-viewport |
| TextureArray | Textures are device memory |
| MeshRegistry | Mesh data is device memory |
| ComputeShader instances | Modules are device-specific |

### Per-Viewport

| Resource | Reason |
|----------|--------|
| Window | OS window handle |
| VkSurface | Bound to specific window |
| Swapchain | Bound to specific surface |
| CommandRecorder | Commands reference swapchain |
| SyncManager | Fences/semaphores per frame-in-flight |
| SdfBuffer | Per-frame command buffer |
| SdfRenderer | Storage image sized to viewport |
| ImContext | UI state isolated per viewport |

---

## Frame Loop (Multi-Viewport)

```csharp
while (!ViewportManager.ShouldClose)
{
    // 1. Poll ALL windows
    ViewportManager.PollAllEvents();

    // 2. Route input to correct viewport
    ViewportManager.RouteInput();

    // 3. Process each viewport
    foreach (var viewport in ViewportManager.AllViewports)
    {
        if (!viewport.BeginFrame()) continue; // Skip if minimized

        Im.Ctx = viewport.ImContext;
        Im.Begin(deltaTime, viewport.Size);

        // Draw UI for this viewport
        if (viewport.IsPrimary)
        {
            ImDocking.BeginDockSpace(viewport.Bounds);
        }

        foreach (var window in viewport.Windows)
        {
            ImWindows.DrawWindow(window);
        }

        if (viewport.IsPrimary)
        {
            ImDocking.EndDockSpace();
        }

        Im.End();

        // Render SDF
        viewport.SdfBuffer.Build(viewport.Width, viewport.Height);
        viewport.SdfBuffer.Flush(viewport.FrameIndex);

        var cmd = viewport.CommandRecorder.CurrentCommandBuffer;
        viewport.SdfRenderer.Dispatch(cmd, viewport.FrameIndex);
        viewport.SdfRenderer.BlitToSwapchain(cmd, ...);

        viewport.SdfBuffer.Reset();
    }

    // 4. Present all viewports
    foreach (var viewport in ViewportManager.AllViewports)
    {
        viewport.EndFrame();
    }
}
```

---

## Zero-Allocation Patterns

### ImFormatHandler

Zero-allocation string interpolation for widgets:

```csharp
[InterpolatedStringHandler]
public ref struct ImFormatHandler
{
    [ThreadStatic] private static char[]? s_buffer;
    private Span<char> _buffer;
    private int _pos;

    public void AppendLiteral(string s);
    public void AppendFormatted(float value, string? format = null);
    public void AppendFormatted(int value, string? format = null);
    // ... other types

    public ReadOnlySpan<char> AsSpan();
}

// Usage (zero allocation)
Im.Label($"FPS: {fps:F1}");
Im.Slider($"Volume: {volume:F0}%", ref volume, 0, 100);
```

### StringHandle for IDs

Pre-register common widget IDs:

```csharp
static readonly StringHandle s_SaveButton = "Save";
static readonly StringHandle s_CancelButton = "Cancel";

// Zero-allocation comparison
int id = Im.GetId(s_SaveButton);
```

### Fixed-Size Arrays

All stacks use fixed-size arrays:

```csharp
public int[] IdStack = new int[64];        // No List<int>
public ImStyle[] StyleStack = new ImStyle[32];
public int[] WindowIds = new int[16];      // In DockLeaf
```

---

## File Summary

### New Files (~31)

**Phase 1: Core (7 files)**
- `Im.cs`
- `Core/ImContext.cs`
- `Core/ImStyle.cs`
- `Core/ImInput.cs`
- `Core/ImId.cs`
- `Core/ImRect.cs`
- `Core/ImFormatHandler.cs`
- `Rendering/ImSdfDrawList.cs`
- `Rendering/ImDrawLayer.cs`

**Phase 2: Layout (5 files)**
- `Layout/ImLayout.cs`
- `Layout/ImLayoutContext.cs`
- `Layout/ImLayoutGroup.cs`
- `Layout/ImLayoutElement.cs`
- `Layout/ImConstraints.cs`

**Phase 3: Widgets (6 files)**
- `Widgets/ImButton.cs`
- `Widgets/ImLabel.cs`
- `Widgets/ImSlider.cs`
- `Widgets/ImCheckbox.cs`
- `Widgets/ImPanel.cs`
- `Widgets/ImScrollbar.cs`

**Phase 4: Windows (3 files)**
- `Windows/ImWindow.cs`
- `Windows/ImWindowManager.cs`
- `Windows/ImTitleBar.cs`

**Phase 5: Docking (6 files)**
- `Docking/DockNode.cs`
- `Docking/DockSplit.cs`
- `Docking/DockLeaf.cs`
- `Docking/DockSpace.cs`
- `Docking/DockZone.cs`
- `Docking/DockSerializer.cs`

**Phase 6: Viewport (4 files)**
- `Viewport/Viewport.cs`
- `Viewport/ViewportManager.cs`
- `Viewport/ViewportInputRouter.cs`
- `Viewport/WindowExtractionHandler.cs`

### Modified Files (4)

- `Sdf/SdfCommand.cs` - Add `ClipRect` property
- `Data/shaders/sdf_compute.compute` - Add clip rect early-out
- `Engine.cs` - Refactor into ViewportManager pattern
- `Presentation/Window.cs` - Add position get/set

---

## Dependencies & Build Order

```
Phase 1 (Core) ─────────────────────┐
    │                               │
    │ SDF ClipRect modification ◄───┤ (parallel)
    │                               │
    ├── Phase 2 (Layout) ◄──────────┘
    │       │
    │       └── Phase 3 (Widgets)
    │               │
    │               └── Phase 4 (Windows)
    │                       │
    │                       └── Phase 5 (Docking)
    │                               │
    │                               └── Phase 6 (Multi-Viewport)
```

---

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| One ImContext per viewport | Clean isolation, supports multi-window, no global state corruption |
| Direct SDF integration | ImSdfDrawList writes to SdfBuffer, no intermediate layer, maximum performance |
| Binary dock tree | DockSplit (2 children) + DockLeaf (tabs), simple and efficient |
| Per-command ClipRect | Simplest integration with tile system, no command buffer segmentation |
| Shared GPU resources | VkDevice, pipelines, textures shared across viewports, efficient memory use |
| Zero-allocation hot path | Pre-allocated arrays, StringHandle IDs, ImFormatHandler for strings |
