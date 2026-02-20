# Docking Architecture

## Overview

The docking system uses a **layout-based architecture**. `DockingLayout` owns the dock tree. Windows are just content containers that render where they're told.

## Core Types

### DockingLayout

A container that manages a dock tree.

```csharp
class DockingLayout
{
    ImRect Rect;           // Where this layout renders
    bool IsFloating;       // true = has chrome, false = main viewport
    DockNode? RootNode;    // The split/leaf tree (null if empty)
}
```

- **Main layout**: `IsFloating = false`, fills viewport, no chrome
- **Floating layout**: `IsFloating = true`, has title bar + borders

### DockNode

Abstract base for tree nodes.

```csharp
abstract class DockNode
{
    ImRect Rect;           // Calculated during layout
    void UpdateLayout(ImRect rect);
    void Draw();
}
```

### SplitNode

Binary split with two children.

```csharp
class SplitNode : DockNode
{
    DockNode First;
    DockNode Second;
    SplitDirection Direction;  // Horizontal or Vertical
    float Ratio;               // 0-1, how much space First gets
}
```

### LeafNode

Holds windows as tabs.

```csharp
class LeafNode : DockNode
{
    List<int> WindowIds;    // Window IDs in this leaf
    int ActiveTab;          // Which tab is visible
}
```

### DockController

Central manager for all layouts.

```csharp
class DockController
{
    List<DockingLayout> Layouts;  // [0] = main, [1+] = floating

    DockingLayout MainLayout => Layouts[0];

    void Initialize(ImRect viewportRect);
    DockingLayout CreateFloatingLayout(ImRect rect);
    void RemoveLayout(DockingLayout layout);

    void AcceptDock(DockingLayout layout, int windowId, DockZone zone, LeafNode? target);
    void UndockWindow(int windowId);

    DockingLayout? FindLayoutContainingWindow(int windowId);
    bool ContainsWindow(int windowId);

    void ProcessInput();   // Drag detection, preview, drop handling
    void DrawPreview();
    void DrawGhostPreview();
}
```

### ImWindow

Windows are dumb content. They don't know about docking.

```csharp
class ImWindow
{
    int Id;
    string Title;
    ImRect Rect;           // Only used when floating (not in any layout)

    // NO docking fields - window doesn't know if it's docked
}
```

## Tree Structure

### Empty Layout
```
Layout.RootNode = null
```

### One Window
```
Layout.RootNode = Leaf [Window A]
```

### Two Windows as Tabs
```
Layout.RootNode = Leaf [Window A, Window B]
```

### Two Windows Side by Side
```
Layout.RootNode = Split (Horizontal)
├── First: Leaf [Window A]
└── Second: Leaf [Window B]
```

### Complex Layout
```
Layout.RootNode = Split (Horizontal)
├── First: Split (Vertical)
│   ├── First: Leaf [Window A]
│   └── Second: Leaf [Window B]
└── Second: Leaf [Window C, Window D]
```

Renders as:
```
┌─────────┬───────────┐
│    A    │           │
├─────────┤   C | D   │
│    B    │           │
└─────────┴───────────┘
```

## Visual Examples

### Main Layout (no chrome)

```
┌──────────────────┬────────────────────┐
│ [Tab A] [Tab B]  │ [Tab C]            │
├──────────────────┼────────────────────┤
│                  │                    │
│   Window A       │   Window C         │
│   content        │   content          │
│                  │                    │
└──────────────────┴────────────────────┘
```

### Floating Layout (has chrome)

```
┌─────────────────────────────┐
│ Window D             [─][x] │  ← Title bar (active tab's title)
├─────────────────────────────┤
│ [Tab D] [Tab E]             │  ← Tab bar from leaf
├─────────────────────────────┤
│                             │
│   Window D content          │
│                             │
└─────────────────────────────┘
```

## Rendering Flow

```
ImDocking.BeginDockSpace(rect)
│
├── Update MainLayout.Rect
├── MainLayout.UpdateLayout()
├── DockController.ProcessInput()
│
├── Draw main layout background
├── MainLayout.RootNode.Draw()      // Recurse tree
│
└── For each floating layout:
    ├── Draw chrome (title bar, borders)
    └── layout.RootNode.Draw()

// Windows render their content
Im.BeginWindow("A"); ... Im.EndWindow();
Im.BeginWindow("B"); ... Im.EndWindow();

ImDocking.EndDockSpace()
│
├── DrawPreview()       // Dock zone highlight
└── DrawGhostPreview()  // Dragged tab preview
```

## Docking Flow

### Dock Window to Layout

```
User drags Window B over a layout
        │
        ▼
ProcessInput()
        │
        ├── Find layout under mouse
        ├── Find leaf under mouse (if any)
        ├── Determine zone: Center / Left / Right / Top / Bottom
        ├── Show preview highlight
        │
        ▼ (on drop)
AcceptDock(layout, windowId, zone, targetLeaf)
        │
        ├── If layout.RootNode == null:
        │   └── Create new Leaf with window
        │
        ├── If zone == Center:
        │   └── Add window to leaf's tab list
        │
        └── If zone == Edge:
            ├── Create new Leaf with window
            ├── Create Split with (target, newLeaf)
            └── Replace target with split in tree
```

### Undock Window

```
User drags tab out of leaf
        │
        ▼
UndockWindow(windowId)
        │
        ├── Find layout containing window
        ├── Find leaf containing window
        ├── Remove window from leaf
        ├── Prune empty nodes from tree
        │
        └── If floating layout is now empty:
            └── Remove layout from Layouts list
```

## Zone Detection

When hovering over a leaf, divide into 5 zones:

```
┌─────────────────────────┐
│         TOP             │
├────┬───────────────┬────┤
│    │               │    │
│ L  │    CENTER     │ R  │
│    │               │    │
├────┴───────────────┴────┤
│        BOTTOM           │
└─────────────────────────┘
```

- **Center**: Add as tab to existing leaf
- **Edge (L/R/T/B)**: Create split

### Root-Level Docking

When near the edge of the entire layout (not just a leaf), dock at root level with 25% ratio:

```
Before:                      After docking D to left:
┌───────────────────┐       ┌─────┬─────────────┐
│        A          │       │     │      A      │
├───────────────────┤  →    │  D  ├─────────────┤
│        B          │       │     │      B      │
└───────────────────┘       └─────┴─────────────┘
                             25%      75%
```

## Key Principles

1. **Layouts own trees** - Windows don't know about docking
2. **One tree per layout** - Each layout has one RootNode
3. **Splits are binary** - Always two children
4. **Leaves hold IDs** - Reference windows by ID, not direct pointer
5. **Main layout = no chrome** - Just content
6. **Floating layout = chrome** - Title bar shows active tab's title
7. **Controller is source of truth** - All operations go through it

## File Structure

```
ImGui/Docking/
├── DockingLayout.cs      // Layout container
├── DockController.cs     // Central manager
├── DockNode.cs           // Abstract base
├── SplitNode.cs          // Binary split
├── LeafNode.cs           // Tabs container
├── DockZone.cs           // Enum
└── ImDocking.cs          // Static API
```

## Static API

```csharp
public static class ImDocking
{
    DockingLayout MainLayout { get; }

    void Initialize();
    void BeginDockSpace();
    void BeginDockSpace(ImRect rect);
    void EndDockSpace();

    void DockWindow(string title, DockZone zone);
    void UndockWindow(string title);
}
```

## Determining if Window is Docked

A window is docked if it's in any layout:

```csharp
bool IsWindowDocked(int windowId)
{
    return DockController.ContainsWindow(windowId);
}
```

The window itself doesn't track this. It's derived from the layout state.

## Window Rendering

Windows don't know where they render. The system tells them:

1. **Floating window** (not in any layout): Renders at its own `Rect`
2. **Docked window**: Leaf calculates content rect, passes to window

```csharp
// In LeafNode.Draw():
var contentRect = GetContentRect();
var window = WindowManager.FindById(WindowIds[ActiveTab]);
window.RenderContent(contentRect);
```

## Ghost Dragging

When dragging a tab out of a leaf:

1. Leaf detects drag threshold exceeded
2. Sets `controller.GhostDraggingWindowId`
3. Sets `controller.GhostSourceLayout`
4. Controller draws ghost preview at mouse position
5. On drop:
   - If over valid zone: undock from source, dock to target
   - If over empty space: undock, window becomes floating
