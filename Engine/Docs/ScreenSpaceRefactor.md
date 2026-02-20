# Screen-Space Multi-Viewport Refactor Plan

## Executive Summary

Our multi-viewport ImGUI system has fundamental architectural issues causing bugs that are difficult to fix without breaking other functionality. This document analyzes the root causes and proposes a refactor to match Dear ImGui's proven architecture.

Constraints:
- The per-frame UI hot path must be allocation-free (WASM stutter risk). No LINQ, no per-frame `new` collections, no closure allocations.
- Multi-viewport (multiple OS windows) is desktop-only; WASM builds should compile this feature out rather than falling back at runtime.

---

## Part 1: Problems We've Encountered

### Bug 1: Primary Viewport Blocked After Extraction

**Symptom:** After dragging a window out to create a secondary viewport, windows in the primary viewport cannot be clicked or dragged. Resizing works, but only after resizing can you drag.

**Location:** `ImWindowManager.cs:315-361`

```csharp
// Current code - the problem
public void Update(ImContext ctx, float titleBarHeight, float resizeGrabSize, ...)
{
    // Handle ongoing drag
    if (DraggingWindow != null)  // ← Global shared state
    {
        bool mouseDown = globalInput?.IsMouseButtonDown(...) ?? input.MouseDown;

        if (mouseDown)
        {
            if (DraggingWindow.ViewportId != -1)
            {
                return;  // ← BLOCKS ENTIRE PRIMARY VIEWPORT
            }
            // ... handle drag
        }
        else
        {
            DraggingWindow.IsDragging = false;
            DraggingWindow = null;
        }
        return;  // ← Early return blocks all other interactions
    }

    // ... rest of interactions never reached while DraggingWindow is set
}
```

**Root Cause:** The `DraggingWindow` property is shared across all viewports. When a window is extracted to a secondary viewport while being dragged:
1. `DraggingWindow` still references that window
2. The mouse release happens in the secondary viewport's input context
3. Primary viewport's `Update()` sees `DraggingWindow != null` and returns early
4. No window in the primary viewport can receive input

**Failed Fix Attempts:**
1. Check global input for mouse release → Didn't reliably detect release
2. Check both global AND local input → Made dragging erratic
3. Don't return early for secondary viewport windows → Broke drag continuity

---

### Bug 2: Window Disappears on Extraction

**Symptom:** When dragging a window, it would disappear before becoming a secondary viewport.

**Location:** `ImWindowManager.cs:494-542` (CheckExtraction)

**Root Cause:** Extraction threshold was too aggressive. Window was being extracted while still partially visible in primary viewport.

**Fix Applied:** Changed `ExtractionThreshold` from 50f to 0f - extract only when edge leaves viewport.

---

### Bug 3: Flicker on Re-dock

**Symptom:** When dragging a secondary viewport window back into the primary viewport, it would flicker (disappear for 1 frame).

**Location:** `ViewportManager.cs:259-325` (CheckRedock) and disposal flow

**Root Cause:** The secondary viewport was disposed before the primary viewport rendered the window:
1. Frame N: Redock detected, `ViewportId` set to -1, viewport disposed
2. Frame N: Primary viewport renders - but window wasn't in its list yet
3. Frame N+1: Window appears in primary viewport

**Fix Applied:** Two-phase deferred disposal:
```csharp
// Phase 1: Transfer window to primary (ViewportId = -1)
_pendingTransfer → _pendingDispose

// Phase 2: Dispose viewport after primary renders
DisposePendingViewports() called at end of frame
```

This added complexity but fixed the flicker.

---

### Bug 4: State Synchronization Issues

**Symptom:** Various edge cases where `ViewportId`, `IsDragging`, and viewport existence get out of sync.

**Examples:**
- Window thinks it's in viewport 0, but viewport 0 was destroyed
- Window is dragging but `DraggingWindow` was cleared
- Viewport exists but has no windows assigned

**Root Cause:** The `ViewportId` on `ImWindow` is an implicit contract that must stay synchronized with:
- `ViewportManager._secondaryViewports` list
- `ImContext.Viewports` list
- `ImWindowManager.DraggingWindow` reference
- The actual OS window existence

Any operation that changes one must carefully update all others.

---

## Part 2: Root Cause Analysis

### The Fundamental Problem: Viewport-Local Coordinates + Explicit Assignment

Our architecture stores window position in **viewport-local coordinates** and uses an explicit **ViewportId** to track which viewport owns each window:

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Current Architecture                          │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  ImWindow A                    ImWindow B                            │
│  ┌────────────────┐           ┌────────────────┐                    │
│  │ Rect: (50, 50) │           │ Rect: (0, 0)   │  ← Local coords!   │
│  │ ViewportId: -1 │           │ ViewportId: 0  │  ← Explicit ID     │
│  │ IsDragging: F  │           │ IsDragging: T  │                    │
│  └────────────────┘           └────────────────┘                    │
│         │                            │                               │
│         ▼                            ▼                               │
│  ┌─────────────┐              ┌─────────────┐                       │
│  │  Primary    │              │ Secondary   │                       │
│  │  Viewport   │              │ Viewport 0  │                       │
│  │ (0,0)-(1280,│              │ at (1400,   │                       │
│  │  720)       │              │  200)       │                       │
│  └─────────────┘              └─────────────┘                       │
│                                                                      │
│  ImWindowManager (shared)                                            │
│  ┌──────────────────────────────────────────┐                       │
│  │ DraggingWindow = Window B  ← GLOBAL!     │                       │
│  │ ResizingWindow = null                    │                       │
│  │                                          │                       │
│  │ Update() {                               │                       │
│  │   if (DraggingWindow != null) {          │                       │
│  │     // Handle drag...                    │                       │
│  │     return; ← BLOCKS EVERYTHING ELSE     │                       │
│  │   }                                      │                       │
│  │   // Other interactions never reached    │                       │
│  │ }                                        │                       │
│  └──────────────────────────────────────────┘                       │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

**Problems with this approach:**

1. **Coordinate Translation Required:** Every operation must translate between viewport-local and screen coordinates
2. **State Must Stay Synchronized:** ViewportId, viewport existence, and drag state must all match
3. **Global Drag State Blocks:** Only one window can drag at a time, and it blocks all viewports
4. **Complex Extraction/Redock:** Must manually detect when to move windows between viewports
5. **Deferred Disposal Required:** Can't destroy viewport until after rendering to avoid flicker

---

## Part 3: How Dear ImGui Solves This

Dear ImGui uses a fundamentally different approach:

### Screen-Space Coordinates

Windows are ALWAYS positioned in screen coordinates:

```cpp
// Dear ImGui internal
struct ImGuiWindow {
    ImVec2 Pos;      // Screen-space position (not viewport-local)
    ImVec2 Size;
    // NO ViewportId stored - computed each frame
};
```

### Viewport Assignment Computed Each Frame

```cpp
// Simplified from imgui.cpp
void ImGui::UpdateWindowViewport(ImGuiWindow* window) {
    // Find which viewport contains this window
    for (ImGuiViewport* viewport : g.Viewports) {
        if (viewport->GetMainRect().Contains(window->Rect())) {
            window->Viewport = viewport;
            return;
        }
    }
    // Window outside all viewports - create new one
    window->Viewport = CreateNewViewport(window);
}
```

### Moving/Resizing State (Per-Window + Single Active Window)

Dear ImGui stores interaction flags on the window, but the context still tracks which *single* window is being moved/resized this frame:

```cpp
// Simplified conceptually from imgui.cpp
struct ImGuiContext {
    ImGuiWindow* MovingWindow;   // Only one window can be moved
    ImGuiWindow* SizingWindow;   // Only one window can be resized
};

struct ImGuiWindow {
    bool Moving;
    bool Resizing;
};
```

The key is that input is handled once per frame in screen space (not once per viewport), so there is no per-viewport early-return that blocks other viewports.

### Viewports Are Just Render Targets

```
┌─────────────────────────────────────────────────────────────────────┐
│                      Dear ImGui Architecture                         │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  All windows in SCREEN COORDINATES:                                  │
│                                                                      │
│  ImWindow A                    ImWindow B                            │
│  ┌────────────────┐           ┌────────────────┐                    │
│  │ Pos: (100,100) │           │ Pos: (1450,250)│  ← Screen coords!  │
│  │ Size: (300,200)│           │ Size: (400,300)│                    │
│  │ Moving: false  │           │ Moving: true   │  ← Per-window      │
│  └────────────────┘           └────────────────┘                    │
│         │                            │                               │
│         │                            │                               │
│         ▼                            ▼                               │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │              ViewportAssigner (runs each frame)              │    │
│  │                                                              │    │
│  │  for each window:                                            │    │
│  │    if window.Rect inside primaryViewport.Bounds:             │    │
│  │      assign to primary                                       │    │
│  │    else:                                                     │    │
│  │      ensure secondary viewport exists at window position     │    │
│  │                                                              │    │
│  └─────────────────────────────────────────────────────────────┘    │
│         │                            │                               │
│         ▼                            ▼                               │
│  ┌─────────────┐              ┌─────────────┐                       │
│  │  Primary    │              │ Secondary   │                       │
│  │  Viewport   │              │ Viewport    │                       │
│  │ Renders A   │              │ Renders B   │                       │
│  │ at local    │              │ at local    │                       │
│  │ (100,100)   │              │ (0,0)       │  ← Transformed at     │
│  └─────────────┘              └─────────────┘    render time        │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

**Benefits:**

1. **No State Synchronization:** Window position is the single source of truth
2. **No Blocking:** Each window handles its own drag independently
3. **No Manual Extraction:** Viewports created automatically when window leaves primary
4. **No Flicker-Driven Deferral:** Window ownership changes don't require 2-phase transfer to avoid flicker (GPU/resource destruction may still be queued for frames-in-flight safety)
5. **Simple Mental Model:** Windows exist in screen space, viewports are just render targets

### Bidirectional Sync (OS Window <-> ImWindow)

For secondary viewports, the OS window can be moved/resized externally (by the window manager, not just by our title-bar drag code).
To match Dear ImGui behavior, the architecture must support both directions:

- **ImWindow -> OS viewport:** when our UI moves/resizes a window, update the secondary OS window position/size.
- **OS viewport -> ImWindow:** when the OS window is moved/resized (events), update the corresponding screen-space `ImWindow.Rect`.

This keeps screen-space window rects authoritative while still reflecting platform actions.

---

## Part 4: Target Architecture

### ImWindow (screen-space)

```csharp
public class ImWindow
{
    public int Id;
    public string Title;

    /// <summary>
    /// Window bounds in SCREEN-SPACE coordinates.
    /// (screenX, screenY, width, height)
    /// </summary>
    public ImRect Rect;

    public ImWindowFlags Flags;
    public bool IsOpen;
    public bool IsCollapsed;

    // Per-window interaction state (NOT global)
    public bool IsDragging;
    public bool IsResizing;
    public int ResizeEdge;
    public Vector2 DragOffset;
    internal ImRect ResizeStartRect;
    internal Vector2 ResizeStartMouse;

    public int ZOrder;
    public Vector2 MinSize;
    public Vector2 MaxSize;
    public Vector2 ScrollOffset;
    public Vector2 ContentSize;

    // REMOVED: public int ViewportId = -1;
}
```

### ImWindowManager (no global drag state)

```csharp
public class ImWindowManager
{
    private readonly ImWindow?[] _windows = new ImWindow?[MaxWindows];
    private readonly int[] _zOrderedIndices = new int[MaxWindows];
    private int _windowCount;
    private int _nextZOrder;

    public ImWindow? FocusedWindow { get; private set; }

    // REMOVED: public ImWindow? DraggingWindow { get; private set; }
    // REMOVED: public ImWindow? ResizingWindow { get; private set; }
    // REMOVED: _pendingExtractions, _pendingRedocks

    /// <summary>
    /// Update all window interactions using global mouse state.
    /// Each window handles its own drag/resize independently.
    /// </summary>
    public void Update(IGlobalInput globalInput, float titleBarHeight, float resizeGrabSize)
    {
        var mousePos = globalInput.GlobalMousePosition;
        bool mouseDown = globalInput.IsMouseButtonDown(MouseButton.Left);
        bool mousePressed = globalInput.IsMouseButtonPressed(MouseButton.Left);
        bool mouseReleased = globalInput.IsMouseButtonReleased(MouseButton.Left);

        // Update cursor based on hover state
        DesiredCursor = StandardCursor.Default;

        // Process ALL windows - no early returns
        foreach (var window in GetWindowsFrontToBack())
        {
            // Handle ongoing drag for THIS window
            if (window.IsDragging)
            {
                if (mouseDown)
                {
                    window.Rect = new ImRect(
                        mousePos.X - window.DragOffset.X,
                        mousePos.Y - window.DragOffset.Y,
                        window.Rect.Width,
                        window.Rect.Height);
                }
                if (mouseReleased)
                {
                    window.IsDragging = false;
                }
                continue;
            }

            // Handle ongoing resize for THIS window
            if (window.IsResizing)
            {
                if (mouseDown)
                {
                    var delta = mousePos - window.ResizeStartMouse;
                    window.Rect = window.ResizeStartRect;
                    window.ApplyResize(window.ResizeEdge, delta);
                }
                if (mouseReleased)
                {
                    window.IsResizing = false;
                    window.ResizeEdge = 0;
                }
                DesiredCursor = GetResizeCursor(window.ResizeEdge);
                continue;
            }

            // Check hover for resize cursor
            int hoverEdge = window.HitTestResize(mousePos, resizeGrabSize);
            if (hoverEdge > 0)
            {
                DesiredCursor = GetResizeCursor(hoverEdge);
            }
        }

        // Start new interactions on mouse press
        if (mousePressed)
        {
            foreach (var window in GetWindowsFrontToBack())
            {
                // Skip windows already in an interaction
                if (window.IsDragging || window.IsResizing)
                    continue;

                // Check resize
                int resizeEdge = window.HitTestResize(mousePos, resizeGrabSize);
                if (resizeEdge > 0)
                {
                    window.IsResizing = true;
                    window.ResizeEdge = resizeEdge;
                    window.ResizeStartRect = window.Rect;
                    window.ResizeStartMouse = mousePos;
                    BringToFront(window);
                    return;
                }

                // Check title bar drag
                var titleRect = window.GetTitleBarRect(titleBarHeight);
                if (titleRect.Contains(mousePos) && !window.Flags.HasFlag(ImWindowFlags.NoMove))
                {
                    window.IsDragging = true;
                    window.DragOffset = mousePos - new Vector2(window.Rect.X, window.Rect.Y);
                    BringToFront(window);
                    return;
                }

                // Check body click
                if (window.Rect.Contains(mousePos))
                {
                    BringToFront(window);
                    return;
                }
            }
        }
    }

    // REMOVED: CheckExtraction, RequestRedock
    // REMOVED: ConsumePendingExtractions, ConsumePendingRedocks
}
```

### ViewportAssigner (new class)

```csharp
/// <summary>
/// Computes which viewport each window should render to based on screen position.
/// Creates/destroys secondary viewports automatically.
/// </summary>
public class ViewportAssigner
{
    private readonly ViewportManager _viewportManager;
    private readonly WindowViewportLink[] _links;
    private int _linkCount;
    private int _frameTag;

    private struct WindowViewportLink
    {
        public ImWindow Window;
        public ViewportResources Viewport;
        public int LastSeenFrameTag;
    }

    public ViewportAssigner(ViewportManager viewportManager, int maxWindows)
    {
        _viewportManager = viewportManager;
        _links = new WindowViewportLink[maxWindows];
    }

    /// <summary>
    /// Assign windows to viewports. Call once per frame before rendering.
    /// Allocation-free: uses preallocated storage, no LINQ/dictionaries.
    /// </summary>
    public void AssignViewports(
        ImWindowManager windowManager,
        ImRect primaryBounds,
        Vector2 primaryScreenPos)
    {
        _frameTag++;

        var fullPrimaryBounds = new ImRect(
            primaryScreenPos.X,
            primaryScreenPos.Y,
            primaryBounds.Width,
            primaryBounds.Height);

        foreach (var window in windowManager.GetWindowsBackToFront())
        {
            if (!window.IsOpen)
            {
                RemoveViewportForWindow(window);
                continue;
            }

            // Is window fully inside primary viewport?
            bool insidePrimary = fullPrimaryBounds.Contains(window.Rect);

            if (insidePrimary)
            {
                // Window renders in primary - destroy any secondary viewport
                RemoveViewportForWindow(window);
            }
            else
            {
                // Window needs secondary viewport
                ref var link = ref GetOrCreateLink(window);
                link.LastSeenFrameTag = _frameTag;

                // Keep platform viewport aligned to the window's screen-space rect.
                link.Viewport.UpdatePositionAndSize(
                    (int)window.Rect.X,
                    (int)window.Rect.Y,
                    (int)window.Rect.Width,
                    (int)window.Rect.Height);
            }
        }

        // Clean up viewports for windows no longer present.
        for (int linkIndex = _linkCount - 1; linkIndex >= 0; linkIndex--)
        {
            if (_links[linkIndex].LastSeenFrameTag != _frameTag)
            {
                DestroyLinkAt(linkIndex);
            }
        }
    }

    /// <summary>
    /// Get the viewport a window should render to.
    /// Returns null for primary viewport.
    /// </summary>
    public ViewportResources? GetViewportForWindow(ImWindow window)
    {
        int linkIndex = FindLinkIndex(window);
        if (linkIndex < 0)
        {
            return null;
        }

        return _links[linkIndex].Viewport;
    }

    private int FindLinkIndex(ImWindow window)
    {
        for (int linkIndex = 0; linkIndex < _linkCount; linkIndex++)
        {
            if (ReferenceEquals(_links[linkIndex].Window, window))
            {
                return linkIndex;
            }
        }

        return -1;
    }

    private ref WindowViewportLink GetOrCreateLink(ImWindow window)
    {
        int existingIndex = FindLinkIndex(window);
        if (existingIndex >= 0)
        {
            return ref _links[existingIndex];
        }

        if (_linkCount >= _links.Length)
        {
            throw new InvalidOperationException("Maximum window count exceeded");
        }

        var viewport = _viewportManager.CreateViewportForWindow(window);

        _links[_linkCount] = new WindowViewportLink
        {
            Window = window,
            Viewport = viewport,
            LastSeenFrameTag = _frameTag
        };

        _linkCount++;
        return ref _links[_linkCount - 1];
    }

    private void RemoveViewportForWindow(ImWindow window)
    {
        int existingIndex = FindLinkIndex(window);
        if (existingIndex < 0)
        {
            return;
        }

        DestroyLinkAt(existingIndex);
    }

    private void DestroyLinkAt(int linkIndex)
    {
        var viewport = _links[linkIndex].Viewport;
        _viewportManager.DestroyViewport(viewport);

        int lastIndex = _linkCount - 1;
        _links[linkIndex] = _links[lastIndex];
        _links[lastIndex] = default;
        _linkCount--;
    }
}
```

### ViewportManager (simplified)

```csharp
public class ViewportManager : IDisposable
{
    // Keep: _primaryViewport, _secondaryViewports
    // Keep: Constructor, SetPrimaryViewport, AllViewports

    // REMOVED: ProcessExtractions, CreateSecondaryViewport (extraction path)
    // REMOVED: CheckRedock, _pendingTransfer, _pendingDispose
    // REMOVED: TransferWindowsFromPendingClosures, DisposePendingViewports
    // REMOVED: GetWindowForViewport (viewports don't own windows)

    /// <summary>
    /// Create a secondary viewport for a window at its screen position.
    /// </summary>
    public ViewportResources CreateViewportForWindow(ImWindow window)
    {
        var viewport = new ViewportResources(
            _log, _vkInstance, _vkDevice, _memoryAllocator,
            _descriptorCache, _pipelineCache, _framesInFlight,
            (int)window.Rect.Width, (int)window.Rect.Height,
            window.Title);

        viewport.Initialize((int)window.Rect.X, (int)window.Rect.Y);
        viewport.InitializeSdf(_sdfShader);

        _secondaryViewports.Add(viewport);
        return viewport;
    }

    /// <summary>
    /// Destroy a secondary viewport immediately.
    /// </summary>
    public void DestroyViewport(ViewportResources viewport)
    {
        _secondaryViewports.Remove(viewport);
        viewport.Dispose();
    }

    /// <summary>
    /// Update secondary viewports (poll events, handle resize).
    /// NO extraction/redock logic - that's handled by ViewportAssigner.
    /// </summary>
    public void UpdateSecondaryViewports()
    {
        for (int i = _secondaryViewports.Count - 1; i >= 0; i--)
        {
            var viewport = _secondaryViewports[i];
            viewport.Window.PollEvents();

            if (viewport.Window.ShouldClose)
            {
                // Just destroy - window keeps its screen position
                DestroyViewport(viewport);
                continue;
            }

            if (viewport.Window.ConsumeResized())
            {
                viewport.HandleResize();
            }
        }
    }
}
```

### Im.cs (simplified flow)

```csharp
public static class Im
{
    private static ViewportAssigner? _viewportAssigner;

    public static void Begin(float deltaTime, Vector2 primaryScreenPos)
    {
        var ctx = ImContext.Instance;

        // Update primary viewport position
        ctx.PrimaryViewport.ScreenPosition = primaryScreenPos;

        // Update window interactions (global mouse, per-window state)
        ctx.WindowManager.Update(ctx.GlobalInput, TitleBarHeight, ResizeGrabSize);

        // Assign windows to viewports based on position
        _viewportAssigner.AssignViewports(
            ctx.WindowManager,
            new ImRect(0, 0, ctx.PrimaryViewport.Size.X, ctx.PrimaryViewport.Size.Y),
            primaryScreenPos);

        // REMOVED: ProcessExtractions, ProcessRedockRequests
        // REMOVED: TransferWindowsFromPendingClosures
    }

    public static bool BeginWindow(string title, float width, float height, ...)
    {
        var window = WindowManager.GetOrCreateWindow(title, ...);

        // Determine which viewport renders this window
        var secondaryViewport = _viewportAssigner.GetViewportForWindow(window);
        ImViewport viewport = secondaryViewport != null
            ? GetImViewportFor(secondaryViewport)
            : ctx.PrimaryViewport;

        // Transform screen-space to viewport-local for rendering
        var localRect = new ImRect(
            window.Rect.X - viewport.ScreenPosition.X,
            window.Rect.Y - viewport.ScreenPosition.Y,
            window.Rect.Width,
            window.Rect.Height);

        // Set rendering context
        ctx.SetCurrentViewport(viewport);

        // Render window at local coordinates
        // ...
    }
}
```

---

## Part 5: Implementation Plan

### Phase 1: Non-Breaking Preparation

**Goal:** Add new infrastructure without changing existing behavior.

1. Create `ViewportAssigner.cs` skeleton (doesn't act yet, uses preallocated storage)
2. Verify `IGlobalInput.IsMouseButtonPressed/Released` exists (already implemented) and audit call sites for consistent usage
3. Add helper method `ImRect.Contains(ImRect other)` for bounds checking
4. Test: Everything still works as before

### Phase 2: Convert to Screen-Space Coordinates

**Goal:** Windows store screen-space position, still use ViewportId for now.

1. Change `ImWindow.Rect` semantics to screen-space
2. Update `RegisterWindow` to accept screen position
3. Update rendering to transform screen→local at render time
4. Update extraction/redock to work with screen coords
5. Test: Extraction/redock still work

### Phase 3: Per-Window Drag State

**Goal:** Remove per-viewport early-return + mixed local/global input; keep a single active move/resize interaction.

1. Replace global `DraggingWindow`/`ResizingWindow` used for early-return with a single "active interaction" tracked in WindowManager
2. Update `ImWindowManager.Update()` to run once per frame (global), not once per viewport
3. Use `IGlobalInput` exclusively for move/resize state transitions and coordinates
4. Test: Dragging in one viewport never blocks primary interactions

### Phase 4: Automatic Viewport Assignment

**Goal:** ViewportAssigner controls viewport creation/destruction.

1. Implement `ViewportAssigner.AssignViewports()` (allocation-free: arrays + `for` loops)
2. Remove `CheckExtraction`, `ProcessExtractions` from ViewportManager
3. Remove `CheckRedock`, `RequestRedock` from WindowManager/ViewportManager
4. Remove `_pendingTransfer`, `_pendingDispose` (no longer needed)
5. Test: Windows automatically get viewports when moved outside

### Phase 5: Cleanup

**Goal:** Remove all legacy code.

1. Remove `ViewportId` from `ImWindow`
2. Remove `ExtractionRequest`, `RedockRequest` structs
3. Remove unused methods from ViewportManager
4. Update any remaining code that references ViewportId
5. Final test: Full multi-viewport workflow

---

## Part 6: Risk Mitigation

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Breaking existing functionality | High | High | Phase-by-phase with tests at each step |
| Performance regression (viewport assignment each frame) | Low | Medium | Simple O(n) bounds check, profile if needed |
| Vulkan resource lifecycle issues | Medium | High | Keep viewport creation/destruction in ViewportManager |
| Input timing issues with global input | Medium | Medium | Update global input at start of frame, use consistently |
| Z-order confusion across viewports | Low | Low | Z-order stays in WindowManager, unchanged |

---

## Part 7: Success Criteria

After refactor is complete:

1. **No blocking:** Can drag Window A in primary while Window B exists in secondary
2. **Automatic extraction:** Window automatically gets secondary viewport when moved outside primary
3. **Automatic redock:** Window returns to primary when moved back inside
4. **No flicker:** Smooth transition between viewports
5. **Simpler code:** ~30% less code in WindowManager/ViewportManager
6. **Single source of truth:** Window screen position is authoritative, no ViewportId to sync
