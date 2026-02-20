using System;
using System.Numerics;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Input;
using DerpLib.ImGui.Rendering;
using DerpLib.ImGui.Viewport;
using DerpLib.ImGui.Windows;
using StandardCursor = Silk.NET.Input.StandardCursor;

namespace DerpLib.ImGui.Docking;

/// <summary>
/// Central manager for all docking layouts.
/// Maintains 1-1 mapping between floating windows and their layouts.
/// Windows know nothing about docking - this controller manages everything externally.
/// </summary>
public class DockController
{
    /// <summary>Main viewport layout (fills screen, always exists).</summary>
    public DockingLayout MainLayout { get; } = new() { IsMainLayout = true };

    /// <summary>Floating layouts - one per floating window initially.</summary>
    public List<DockingLayout> FloatingLayouts { get; } = new();

    //=== Rendering State (set during DrawLayouts) ===

    internal ImDrawLayer DockBackgroundLayer { get; private set; } = ImDrawLayer.Background;
    internal int DockOverlaySortKey { get; private set; }

    //=== Main Viewport Host (non-tab) ===

    /// <summary>
    /// Special window ID that represents the main viewport host content in the main layout.
    /// This window should not appear as a draggable/closable tab.
    /// </summary>
    public int MainViewportWindowId { get; private set; }

    /// <summary>
    /// Leaf ID that hosts <see cref="MainViewportWindowId"/> in <see cref="MainLayout"/>.
    /// Used to suppress tab UI without allocations/search.
    /// </summary>
    internal int MainViewportLeafId { get; private set; }

    //=== Drag Preview State ===

    /// <summary>ID of the window currently being dragged (0 = none).</summary>
    public int DraggingWindowId;

    /// <summary>Layout the dragged window is being dragged from.</summary>
    public DockingLayout? DraggingSourceLayout;

    /// <summary>Target layout the mouse is over (null = will stay floating).</summary>
    public DockingLayout? PreviewTargetLayout;

    /// <summary>Target leaf within the layout (for zone detection).</summary>
    public ImDockLeaf? PreviewTargetLeaf;

    /// <summary>Dock zone within the target (Left/Right/Top/Bottom/Center).</summary>
    public ImDockZone PreviewZone;

    /// <summary>Rectangle to highlight for the preview.</summary>
    public ImRect PreviewRect;

    /// <summary>Viewport the preview should be drawn to.</summary>
    public ImViewport? PreviewViewport;

    /// <summary>
    /// True if the preview should split the dock root instead of the hovered leaf (super border / root-level dock).
    /// </summary>
    public bool PreviewIsRootLevelDock;

    //=== Drag Tracking (drop commit) ===

    /// <summary>
    /// The last window we observed as dragging (persists across the release frame, when ImWindowManager clears IsDragging).
    /// </summary>
    private int _activeDragWindowId;

    /// <summary>
    /// Layout the drag originated from (cached alongside <see cref="_activeDragWindowId"/>).
    /// </summary>
    private DockingLayout? _activeDragSourceLayout;

    // Only attempt docking if the user actually dragged (not just click+release).
    private Vector2 _activeDragStartMousePos;
    private bool _activeDragMoved;
    private const float DockDragThreshold = 5f;

    // Root-level docking behavior (matches the older ImDockSpace implementation).
    private const float RootDockRatio = 0.25f;
    private const float RootDockEdgeThreshold = 30f;

    //=== Ghost Tab Drag (undock) ===

    /// <summary>Window currently being ghost-dragged from a tab bar (0 = none).</summary>
    public int GhostDraggingWindowId;

    /// <summary>Layout the ghost drag originated from.</summary>
    private DockingLayout? _ghostSourceLayout;

    /// <summary>Leaf the ghost drag originated from.</summary>
    private ImDockLeaf? _ghostSourceLeaf;

    //=== Floating Group Chrome Drag ===

    private DockingLayout? _movingLayout;
    private Vector2 _movingLayoutDragOffset;

    //=== Floating Group Resize ===

    private DockingLayout? _resizingLayout;
    private int _resizingLayoutEdge;
    private ImRect _resizingLayoutStartRect;
    private Vector2 _resizingLayoutStartMouse;

    /// <summary>
    /// Find the layout containing a specific window.
    /// Returns null if the window is not in any layout.
    /// </summary>
    public DockingLayout? FindLayoutForWindow(int windowId)
    {
        // Check main layout first
        if (MainLayout.ContainsWindow(windowId))
            return MainLayout;

        // Check floating layouts
        for (int i = 0; i < FloatingLayouts.Count; i++)
        {
            if (FloatingLayouts[i].ContainsWindow(windowId))
                return FloatingLayouts[i];
        }

        return null;
    }

    /// <summary>
    /// Find the leaf containing a specific window across all layouts.
    /// </summary>
    public ImDockLeaf? FindLeafForWindow(int windowId)
    {
        var leaf = MainLayout.FindLeafWithWindow(windowId);
        if (leaf != null) return leaf;

        for (int i = 0; i < FloatingLayouts.Count; i++)
        {
            leaf = FloatingLayouts[i].FindLeafWithWindow(windowId);
            if (leaf != null) return leaf;
        }

        return null;
    }

    /// <summary>
    /// Sync layouts with the current set of windows (zero allocation).
    /// - Creates layouts for new floating windows
    /// - Removes layouts for closed windows
    /// </summary>
    public void SyncWithWindows(ImWindowManager windowManager)
    {
        // Remove closed windows from layouts (keeps dock trees pruned and avoids stale empty splits).
        RemoveClosedWindowsFromLayout(MainLayout, windowManager);

        for (int i = 0; i < FloatingLayouts.Count; i++)
        {
            RemoveClosedWindowsFromLayout(FloatingLayouts[i], windowManager);
        }

        // Create layouts for floating windows that don't have one yet
        foreach (var window in windowManager.GetWindowsBackToFront())
        {
            if (!window.IsOpen) continue;
            if (IsWindowDocked(window.Id)) continue;
            if (FindLayoutForWindow(window.Id) != null) continue;

            // This window needs a layout
            CreateLayoutForWindow(window.Id, windowManager);
        }

        // Remove layouts for closed windows (backwards iteration for safe removal)
        for (int i = FloatingLayouts.Count - 1; i >= 0; i--)
        {
            var layout = FloatingLayouts[i];
            if (!layout.HasAnyOpenWindow(windowManager))
            {
                FloatingLayouts.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Close a docked tab/window and remove it from its owning layout (zero allocation).
    /// </summary>
    public void CloseDockedTab(int windowId, ImWindowManager windowManager)
    {
        if (windowId == MainViewportWindowId)
        {
            return;
        }

        // Cancel ghost drag if the source tab is being closed.
        if (GhostDraggingWindowId == windowId)
        {
            ClearGhostDrag();
        }

        var layout = FindLayoutForWindow(windowId);
        if (layout != null)
        {
            RemoveWindowFromLayout(layout, windowId, windowManager);
            if (!layout.IsMainLayout && layout.GetWindowCount() == 0)
            {
                FloatingLayouts.Remove(layout);
            }
        }

        var window = windowManager.FindWindowById(windowId);
        if (window != null)
        {
            window.IsOpen = false;
        }

        UpdateDockedWindowFlags(windowManager);
    }

    /// <summary>
    /// Request closing a docked tab during dock layout rendering.
    /// This avoids mutating/pruning the dock tree while it is being traversed for drawing.
    /// The actual removal/prune occurs at the start of the next frame in <see cref="SyncWithWindows"/>.
    /// </summary>
    public void RequestCloseDockedTab(int windowId, ImWindowManager windowManager)
    {
        if (windowId == MainViewportWindowId)
        {
            return;
        }

        if (GhostDraggingWindowId == windowId)
        {
            ClearGhostDrag();
        }

        var window = windowManager.FindWindowById(windowId);
        if (window != null)
        {
            window.IsOpen = false;
        }
    }

    private void RemoveClosedWindowsFromLayout(DockingLayout layout, ImWindowManager windowManager)
    {
        if (layout.Root == null)
        {
            return;
        }

        bool changed = RemoveClosedWindowsRecursive(layout.Root, windowManager);
        if (!changed)
        {
            return;
        }

        layout.Root = PruneNode(layout.Root);
        ApplyLayoutAfterTreeMutation(layout, windowManager);
    }

    private bool RemoveClosedWindowsRecursive(ImDockNode node, ImWindowManager windowManager)
    {
        if (node is ImDockLeaf leaf)
        {
            bool changed = false;
            for (int i = leaf.WindowIds.Count - 1; i >= 0; i--)
            {
                int windowId = leaf.WindowIds[i];
                if (windowId == MainViewportWindowId)
                {
                    continue;
                }

                var window = windowManager.FindWindowById(windowId);
                if (window != null && !window.IsOpen)
                {
                    leaf.WindowIds.RemoveAt(i);
                    changed = true;
                }
            }

            if (changed)
            {
                if (leaf.ActiveTabIndex >= leaf.WindowIds.Count)
                {
                    leaf.ActiveTabIndex = Math.Max(0, leaf.WindowIds.Count - 1);
                }
            }

            return changed;
        }

        if (node is ImDockSplit split)
        {
            bool firstChanged = split.First != null && RemoveClosedWindowsRecursive(split.First, windowManager);
            bool secondChanged = split.Second != null && RemoveClosedWindowsRecursive(split.Second, windowManager);
            return firstChanged || secondChanged;
        }

        return false;
    }

    private static void ApplyLayoutAfterTreeMutation(DockingLayout layout, ImWindowManager windowManager)
    {
        if (layout.IsMainLayout)
        {
            layout.UpdateLayout();
            return;
        }

        int windowCount = layout.GetWindowCount();
        if (windowCount <= 0)
        {
            layout.UpdateLayout();
            return;
        }

        // If a floating group collapsed down to a single window, hand control back to the window.
        // The layout stays as a 1:1 mapping for docking hit-testing, but does not own window sizing/position.
        if (windowCount == 1)
        {
            int windowId = layout.GetFirstWindowId();
            if (windowId != -1)
            {
                var window = windowManager.FindWindowById(windowId);
                if (window != null)
                {
                    window.Rect = layout.Rect;
                }

                if (layout.Root != null)
                {
                    layout.Root.Rect = layout.Rect;
                }
            }

            return;
        }

        layout.UpdateLayout();
    }

    /// <summary>
    /// Update <see cref="ImWindow.IsDocked"/> flags based on the current layout state (zero allocation).
    /// This is the authoritative source for whether move/resize is allowed.
    /// </summary>
    public void UpdateDockedWindowFlags(ImWindowManager windowManager)
    {
        foreach (var window in windowManager.GetWindowsBackToFront())
        {
            if (!window.IsOpen)
            {
                continue;
            }

            window.IsDocked = IsWindowDocked(window.Id);
        }
    }

    /// <summary>
    /// Check if a window is docked (in the main layout or in a layout with other windows).
    /// </summary>
    public bool IsWindowDocked(int windowId)
    {
        // A window is "docked" if:
        // 1. It's in the main layout, OR
        // 2. It's in a floating layout that has more than one window

        if (MainLayout.ContainsWindow(windowId))
            return true;

        var layout = FindLayoutForWindow(windowId);
        if (layout != null && !layout.IsMainLayout)
        {
            // Check if layout has multiple windows (zero allocation)
            if (layout.HasMultipleWindows())
                return true;
        }

        return false;
    }

    /// <summary>
    /// Create a floating layout for a window.
    /// </summary>
    private void CreateLayoutForWindow(int windowId, ImWindowManager windowManager)
    {
        var window = windowManager.FindWindowById(windowId);
        if (window == null) return;

        var leaf = new ImDockLeaf();
        leaf.WindowIds.Add(windowId);
        
        var layout = new DockingLayout
        {
            Rect = window.Rect,
            Root = leaf
        };

        FloatingLayouts.Add(layout);
    }

    /// <summary>
    /// Update rect for floating layouts based on their window (zero allocation).
    /// For 1-1 mapped layouts, the layout rect follows the window rect.
    /// </summary>
    public void UpdateFloatingLayoutRects(ImWindowManager windowManager)
    {
        for (int i = 0; i < FloatingLayouts.Count; i++)
        {
            var layout = FloatingLayouts[i];

            // For single-window layouts, layout follows the window
            if (layout.GetWindowCount() == 1)
            {
                int windowId = layout.GetFirstWindowId();
                // Note: windowId can be negative (FNV hash), -1 is the sentinel for "not found"
                if (windowId != -1)
                {
                    var window = windowManager.FindWindowById(windowId);
                    if (window != null)
                    {
                        layout.Rect = window.Rect;
                        // Set root node rect for zone detection
                        if (layout.Root != null)
                        {
                            layout.Root.Rect = layout.Rect;
                        }
                    }
                }
            }
            else
            {
                // Multi-window layouts: dock tree controls window positions
                layout.UpdateLayout();
            }
        }
    }

    /// <summary>
    /// Update the main layout rect and recalculate all node positions.
    /// </summary>
    public void UpdateMainLayout(ImRect rect)
    {
        MainLayout.Rect = rect;
        MainLayout.UpdateLayout();
    }

    /// <summary>
    /// Initialize the main dock area with a window.
    /// The window will fill the entire main viewport.
    /// </summary>
    public void CreateMainDockArea(int windowId)
    {
        var leaf = new ImDockLeaf();
        leaf.WindowIds.Add(windowId);
        MainLayout.Root = leaf;

        MainViewportWindowId = windowId;
        MainViewportLeafId = leaf.Id;
    }

    internal bool IsMainViewportLeaf(ImDockLeaf leaf)
    {
        return leaf.Id == MainViewportLeafId && MainViewportLeafId != 0;
    }

    /// <summary>
    /// Start ghost dragging a tab out of its leaf.
    /// </summary>
    public void BeginGhostTabDrag(int windowId, ImDockLeaf sourceLeaf)
    {
        if (GhostDraggingWindowId != 0)
        {
            return;
        }

        GhostDraggingWindowId = windowId;
        _ghostSourceLeaf = sourceLeaf;
        _ghostSourceLayout = FindLayoutForWindow(windowId);

        // If the source leaf was showing this tab, switch to a different tab so content doesn't appear frozen under the cursor.
        if (sourceLeaf.ActiveTabIndex >= 0
            && sourceLeaf.ActiveTabIndex < sourceLeaf.WindowIds.Count
            && sourceLeaf.WindowIds[sourceLeaf.ActiveTabIndex] == windowId)
        {
            if (sourceLeaf.WindowIds.Count > 1)
            {
                sourceLeaf.ActiveTabIndex = 0;
            }
        }
    }

    /// <summary>
    /// Process resizing of floating dock group layouts (dragging layout edges/corners).
    /// This is separate from window resizing: once a window is in a group it becomes docked,
    /// so the group layout becomes the resizable "container".
    /// </summary>
    public void ProcessFloatingGroupResize(ImWindowManager windowManager, Vector2 mousePosScreen, bool mouseDown, bool mousePressed, bool mouseReleased, float resizeGrabSize)
    {
        if (_movingLayout != null)
        {
            return;
        }

        // If any normal window interaction is active, don't start layout resizing.
        foreach (var window in windowManager.GetWindowsBackToFront())
        {
            if (window.IsDragging || window.IsResizing)
            {
                return;
            }
        }

        if (_resizingLayout != null)
        {
            if (mouseDown)
            {
                Vector2 delta = mousePosScreen - _resizingLayoutStartMouse;
                var rect = _resizingLayoutStartRect;
                ApplyResize(ref rect, _resizingLayoutEdge, delta);
                _resizingLayout.Rect = rect;
                _resizingLayout.UpdateLayout();
            }
            else if (mouseReleased)
            {
                _resizingLayout = null;
                _resizingLayoutEdge = 0;
            }

            return;
        }

        // Hover cursor (only if no interaction is active).
        foreach (var window in windowManager.GetWindowsFrontToBack())
        {
            if (!window.IsOpen)
            {
                continue;
            }

            var layout = FindLayoutForWindow(window.Id);
            if (layout == null || layout.IsMainLayout || !layout.IsFloatingGroup())
            {
                continue;
            }

            int edge = HitTestResize(layout.Rect, mousePosScreen, resizeGrabSize);
            if (edge > 0)
            {
                windowManager.DesiredCursor = GetResizeCursor(edge);
                break;
            }
        }

        if (!mousePressed)
        {
            return;
        }

        // Start resizing the topmost floating group under the cursor.
        foreach (var window in windowManager.GetWindowsFrontToBack())
        {
            if (!window.IsOpen)
            {
                continue;
            }

            var layout = FindLayoutForWindow(window.Id);
            if (layout == null || layout.IsMainLayout || !layout.IsFloatingGroup())
            {
                continue;
            }

            int edge = HitTestResize(layout.Rect, mousePosScreen, resizeGrabSize);
            if (edge <= 0)
            {
                continue;
            }

            _resizingLayout = layout;
            _resizingLayoutEdge = edge;
            _resizingLayoutStartRect = layout.Rect;
            _resizingLayoutStartMouse = mousePosScreen;
            windowManager.BringToFront(window);
            return;
        }
    }

    /// <summary>
    /// Process dragging of floating dock group chrome (moves the entire layout).
    /// </summary>
    public void ProcessFloatingGroupChromeDrag(ImWindowManager windowManager, Vector2 mousePosScreen, bool mouseDown, bool mousePressed, bool mouseReleased)
    {
        var style = Im.Style;

        if (_movingLayout != null)
        {
            if (mouseDown)
            {
                _movingLayout.Rect = new ImRect(
                    mousePosScreen.X - _movingLayoutDragOffset.X,
                    mousePosScreen.Y - _movingLayoutDragOffset.Y,
                    _movingLayout.Rect.Width,
                    _movingLayout.Rect.Height);
                _movingLayout.UpdateLayout();
            }
            else if (mouseReleased)
            {
                _movingLayout = null;
            }

            return;
        }

        if (_resizingLayout != null)
        {
            return;
        }

        if (!mousePressed)
        {
            return;
        }

        // Hit test topmost window first; if it belongs to a floating group layout and the mouse is in the chrome, start moving that layout.
        foreach (var window in windowManager.GetWindowsFrontToBack())
        {
            if (!window.IsOpen)
            {
                continue;
            }

            var layout = FindLayoutForWindow(window.Id);
            if (layout == null || layout.IsMainLayout || !layout.IsFloatingGroup())
            {
                continue;
            }

            float chromeHeight = layout.GetChromeHeight(style);
            var chromeRect = new ImRect(layout.Rect.X, layout.Rect.Y, layout.Rect.Width, chromeHeight);
            if (!chromeRect.Contains(mousePosScreen))
            {
                continue;
            }

            _movingLayout = layout;
            _movingLayoutDragOffset = mousePosScreen - layout.Rect.Position;
            windowManager.BringToFront(window);
            return;
        }
    }

    //=== Drag Input Processing ===

    /// <summary>
    /// Process drag input to detect dock targets and calculate preview.
    /// Call this each frame after window updates.
    /// </summary>
    public void ProcessDragInput(ImWindowManager windowManager, Vector2 mousePos, bool mouseReleased)
    {
        int draggingWindowIdNow = 0;
        DockingLayout? draggingSourceLayoutNow = null;
        bool isGhostDrag = false;

        // Ghost drag takes precedence over normal window dragging.
        if (GhostDraggingWindowId != 0)
        {
            draggingWindowIdNow = GhostDraggingWindowId;
            draggingSourceLayoutNow = _ghostSourceLayout;
            isGhostDrag = true;
        }
        else
        {
            foreach (var window in windowManager.GetWindowsBackToFront())
            {
                if (window.IsDragging)
                {
                    draggingWindowIdNow = window.Id;
                    draggingSourceLayoutNow = FindLayoutForWindow(window.Id);
                    break;
                }
            }
        }

        // Dragging: update preview and cache drag identity for the release frame.
        if (draggingWindowIdNow != 0)
        {
            DraggingWindowId = draggingWindowIdNow;
            DraggingSourceLayout = draggingSourceLayoutNow;

            if (_activeDragWindowId != draggingWindowIdNow)
            {
                _activeDragStartMousePos = mousePos;
                _activeDragMoved = false;
            }

            _activeDragWindowId = draggingWindowIdNow;
            _activeDragSourceLayout = draggingSourceLayoutNow;

            if (isGhostDrag)
            {
                _activeDragMoved = true;
            }
            else if (!_activeDragMoved)
            {
                Vector2 delta = mousePos - _activeDragStartMousePos;
                float distSq = (delta.X * delta.X) + (delta.Y * delta.Y);
                _activeDragMoved = distSq >= (DockDragThreshold * DockDragThreshold);
            }

            if (_activeDragMoved)
            {
                UpdatePreviewTarget(mousePos, draggingSourceLayoutNow);
            }
            else
            {
                ClearPreviewState();
            }

            if (mouseReleased && GhostDraggingWindowId != 0)
            {
                HandleGhostDrop(windowManager, mousePos);
                ClearGhostDrag();
                ClearPreviewState();
            }

            return;
        }

        // Not currently dragging: if we just released, treat as drop of the last seen drag window.
        if (mouseReleased && _activeDragWindowId != 0)
        {
            if (_activeDragMoved)
            {
                HandleDrop(windowManager, _activeDragWindowId, _activeDragSourceLayout, mousePos);
            }
        }

        // Clear drag + preview state.
        DraggingWindowId = 0;
        DraggingSourceLayout = null;
        ClearPreviewState();
        _activeDragWindowId = 0;
        _activeDragSourceLayout = null;
        _activeDragMoved = false;
    }

    private void ClearPreviewState()
    {
        PreviewTargetLayout = null;
        PreviewTargetLeaf = null;
        PreviewZone = ImDockZone.None;
        PreviewRect = ImRect.Zero;
        PreviewViewport = null;
        PreviewIsRootLevelDock = false;
    }

    /// <summary>
    /// Update the preview target layout and calculate zone/rect for the current mouse position.
    /// </summary>
    private void UpdatePreviewTarget(Vector2 mousePos, DockingLayout? draggingSourceLayout)
    {
        ClearPreviewState();

        // Check floating layouts first (they're on top)
        for (int i = FloatingLayouts.Count - 1; i >= 0; i--)
        {
            var layout = FloatingLayouts[i];
            if (layout == draggingSourceLayout)
            {
                continue;
            }

            if (!layout.Rect.Contains(mousePos))
            {
                continue;
            }

            SetPreviewTargetLayout(layout, mousePos);
            return;
        }

        // Check main layout
        if (MainLayout.Rect.Contains(mousePos))
        {
            SetPreviewTargetLayout(MainLayout, mousePos);
        }
    }

    private void SetPreviewTargetLayout(DockingLayout layout, Vector2 mousePos)
    {
        PreviewTargetLayout = layout;

        if (layout.Root == null)
        {
            // Empty layout: allow initial dock.
            PreviewTargetLeaf = null;
            PreviewZone = ImDockZone.Center;
            PreviewRect = layout.GetDockRect(Im.Style);
            return;
        }

        var dockRect = layout.GetDockRect(Im.Style);
        var leaf = FindLeafAt(layout.Root, mousePos);
        if (leaf == null)
        {
            PreviewTargetLayout = null;
            PreviewTargetLeaf = null;
            PreviewZone = ImDockZone.None;
            PreviewRect = ImRect.Zero;
            return;
        }

        PreviewTargetLeaf = leaf;
        PreviewZone = leaf.GetDockZone(mousePos.X, mousePos.Y);
        PreviewIsRootLevelDock = ShouldRootLevelDock(dockRect, leaf, mousePos, PreviewZone);
        PreviewRect = PreviewIsRootLevelDock
            ? GetRootDockPreviewRect(dockRect, PreviewZone)
            : leaf.GetDockZoneRect(PreviewZone);
    }

    private static bool ShouldRootLevelDock(ImRect dockRect, ImDockLeaf leaf, Vector2 mousePos, ImDockZone zone)
    {
        return zone switch
        {
            ImDockZone.Left => MathF.Abs(leaf.Rect.X - dockRect.X) < 1f && mousePos.X < dockRect.X + RootDockEdgeThreshold,
            ImDockZone.Right => MathF.Abs(leaf.Rect.Right - dockRect.Right) < 1f && mousePos.X > dockRect.Right - RootDockEdgeThreshold,
            ImDockZone.Top => MathF.Abs(leaf.Rect.Y - dockRect.Y) < 1f && mousePos.Y < dockRect.Y + RootDockEdgeThreshold,
            ImDockZone.Bottom => MathF.Abs(leaf.Rect.Bottom - dockRect.Bottom) < 1f && mousePos.Y > dockRect.Bottom - RootDockEdgeThreshold,
            _ => false
        };
    }

    private static ImRect GetRootDockPreviewRect(ImRect dockRect, ImDockZone zone)
    {
        float width = dockRect.Width * RootDockRatio;
        float height = dockRect.Height * RootDockRatio;
        return zone switch
        {
            ImDockZone.Left => new ImRect(dockRect.X, dockRect.Y, width, dockRect.Height),
            ImDockZone.Right => new ImRect(dockRect.Right - width, dockRect.Y, width, dockRect.Height),
            ImDockZone.Top => new ImRect(dockRect.X, dockRect.Y, dockRect.Width, height),
            ImDockZone.Bottom => new ImRect(dockRect.X, dockRect.Bottom - height, dockRect.Width, height),
            _ => ImRect.Zero
        };
    }

    /// <summary>
    /// Draw the dock preview overlay if a target is active.
    /// PreviewRect is in screen-space, so we convert to viewport-local for drawing.
    /// </summary>
    public void DrawPreview(Vector2 viewportScreenPos)
    {
        if (PreviewTargetLayout == null || PreviewZone == ImDockZone.None)
            return;

        // Convert from screen-space to viewport-local
        float x = PreviewRect.X - viewportScreenPos.X;
        float y = PreviewRect.Y - viewportScreenPos.Y;

        Im.DrawRect(x, y, PreviewRect.Width, PreviewRect.Height, Im.Style.DockPreview);
    }

    /// <summary>
    /// Draw main + floating dock layouts (chrome, tab bars, splitters).
    /// Call this before drawing window content.
    /// </summary>
    public void DrawLayouts(ImWindowManager windowManager)
    {
        var ctx = Im.Context;
        var style = Im.Style;
        var previousViewport = ctx.CurrentViewport;
        var previousLayer = previousViewport?.CurrentLayer ?? ImDrawLayer.WindowContent;
        var previousDockBackgroundLayer = DockBackgroundLayer;
        int previousDockOverlaySortKey = DockOverlaySortKey;

        // Clicking anywhere inside a floating group selects it immediately (updates z-order before rendering).
        bool mousePressed = ctx.GlobalInput?.IsMouseButtonPressed(MouseButton.Left) ?? ctx.Input.MousePressed;
        if (mousePressed)
        {
            Vector2 mousePosScreen = ctx.GlobalInput?.GlobalMousePosition
                ?? (ctx.PrimaryViewport != null ? ctx.PrimaryViewport.ScreenPosition + ctx.Input.MousePos : ctx.Input.MousePos);

            foreach (var window in windowManager.GetWindowsFrontToBack())
            {
                if (!window.IsOpen)
                {
                    continue;
                }

                var layout = FindLayoutForWindow(window.Id);
                if (layout == null || layout.IsMainLayout || !layout.IsFloatingGroup())
                {
                    continue;
                }

                if (!layout.Rect.Contains(mousePosScreen))
                {
                    continue;
                }

                windowManager.BringToFront(window);
                break;
            }
        }

        // Main layout (dock backgrounds/tabs/splitters)
        if (MainLayout.Root != null)
        {
            var viewport = FindViewportContainingScreenPos(MainLayout.Rect.Position);
            if (viewport != null)
            {
                Im.SetCurrentViewportForInternalDraw(viewport);
                int oldBackgroundSortKey = viewport.GetDrawList(ImDrawLayer.Background).GetSortKey();
                int oldFloatingSortKey = viewport.GetDrawList(ImDrawLayer.FloatingWindows).GetSortKey();

                viewport.GetDrawList(ImDrawLayer.Background).SetSortKey(0);
                viewport.GetDrawList(ImDrawLayer.FloatingWindows).SetSortKey(0);
                DockBackgroundLayer = ImDrawLayer.Background;
                DockOverlaySortKey = 0;

                Im.PushTransform(-viewport.ScreenPosition);
                MainLayout.Draw();
                Im.PopTransform();

                viewport.GetDrawList(ImDrawLayer.Background).SetSortKey(oldBackgroundSortKey);
                viewport.GetDrawList(ImDrawLayer.FloatingWindows).SetSortKey(oldFloatingSortKey);
            }
        }

        // Floating groups (multi-window layouts only)
        for (int i = 0; i < FloatingLayouts.Count; i++)
        {
            var layout = FloatingLayouts[i];
            if (layout.Root == null || !layout.IsFloatingGroup())
            {
                continue;
            }

            var viewport = FindViewportContainingScreenPos(layout.Rect.Position);
            if (viewport == null)
            {
                continue;
            }

            Im.SetCurrentViewportForInternalDraw(viewport);

            int sortKeyBase = GetLayoutSortKeyBase(layout, windowManager);
            int oldFloatingSortKey = viewport.GetDrawList(ImDrawLayer.FloatingWindows).GetSortKey();

            viewport.GetDrawList(ImDrawLayer.FloatingWindows).SetSortKey(sortKeyBase);
            DockBackgroundLayer = ImDrawLayer.FloatingWindows;
            DockOverlaySortKey = sortKeyBase + 1;

            Im.SetDrawLayer(ImDrawLayer.FloatingWindows);
            Im.PushTransform(-viewport.ScreenPosition);

            // Draw panel background and dock tree background at base sort key (behind docked content in this group).
            DrawFloatingGroupPanelBackground(layout, style);
            layout.Draw();

            // Draw chrome overlays (title bar + border + title) above tabs/content.
            int chromeSortKey = sortKeyBase + 2;
            int oldSortKey = viewport.GetDrawList(ImDrawLayer.FloatingWindows).GetSortKey();
            viewport.GetDrawList(ImDrawLayer.FloatingWindows).SetSortKey(chromeSortKey);
            DrawFloatingGroupChromeOverlay(layout, windowManager, style);
            viewport.GetDrawList(ImDrawLayer.FloatingWindows).SetSortKey(oldSortKey);

            Im.PopTransform();

            viewport.GetDrawList(ImDrawLayer.FloatingWindows).SetSortKey(oldFloatingSortKey);
        }

        Im.RestoreViewportForInternalDraw(previousViewport);
        Im.SetDrawLayer(previousLayer);
        DockBackgroundLayer = previousDockBackgroundLayer;
        DockOverlaySortKey = previousDockOverlaySortKey;
    }

    private static void DrawFloatingGroupPanelBackground(DockingLayout layout, ImStyle style)
    {
        float x = layout.Rect.X;
        float y = layout.Rect.Y;
        float w = layout.Rect.Width;
        float h = layout.Rect.Height;

        var shadowColor = ImStyle.ToVector4(style.ShadowColor);
        var bgColor = ImStyle.ToVector4(style.Background);

        Im.AddRoundedRectWithShadowLocal(
            x, y, w, h,
            style.CornerRadius,
            bgColor,
            style.ShadowOffsetX,
            style.ShadowOffsetY,
            style.ShadowRadius,
            shadowColor);
    }

    private static void DrawFloatingGroupChromeOverlay(DockingLayout layout, ImWindowManager windowManager, ImStyle style)
    {
        float x = layout.Rect.X;
        float y = layout.Rect.Y;
        float w = layout.Rect.Width;
        float h = layout.Rect.Height;

        uint titleBarColor = style.TitleBar;
        Im.DrawRoundedRect(x, y, w, style.TitleBarHeight, style.CornerRadius, titleBarColor);
        Im.DrawRect(x, y + style.TitleBarHeight - style.CornerRadius,
            w, style.CornerRadius, titleBarColor);

        Im.DrawRoundedRectStroke(x, y, w, h, style.CornerRadius, style.Border, style.BorderWidth);

        string title = GetLayoutTitle(layout, windowManager);
        float textX = x + style.Padding;
        float textY = y + (style.TitleBarHeight - 14) * 0.5f;
        Im.LabelTextViewport(title, textX, textY);
    }

    private static string GetLayoutTitle(DockingLayout layout, ImWindowManager windowManager)
    {
        int windowId = GetFirstLeafActiveWindowId(layout.Root);
        if (windowId != 0)
        {
            var window = windowManager.FindWindowById(windowId);
            if (window != null)
            {
                return window.Title;
            }
        }

        return "Dock Group";
    }

    private static int GetFirstLeafActiveWindowId(ImDockNode? node)
    {
        if (node == null)
        {
            return 0;
        }

        if (node is ImDockLeaf leaf)
        {
            if (leaf.WindowIds.Count == 0)
            {
                return 0;
            }

            int index = leaf.ActiveTabIndex;
            if (index < 0 || index >= leaf.WindowIds.Count)
            {
                index = 0;
            }

            return leaf.WindowIds[index];
        }

        if (node is ImDockSplit split)
        {
            int left = GetFirstLeafActiveWindowId(split.First);
            if (left != 0)
            {
                return left;
            }

            return GetFirstLeafActiveWindowId(split.Second);
        }

        return 0;
    }

    public int GetLayoutSortKeyBase(DockingLayout layout, ImWindowManager windowManager)
    {
        if (layout.Root == null)
        {
            return 0;
        }

        return GetNodeMaxZOrder(layout.Root, windowManager);
    }

    private static int GetNodeMaxZOrder(ImDockNode node, ImWindowManager windowManager)
    {
        if (node is ImDockLeaf leaf)
        {
            int max = 0;
            for (int i = 0; i < leaf.WindowIds.Count; i++)
            {
                var window = windowManager.FindWindowById(leaf.WindowIds[i]);
                if (window != null && window.ZOrder > max)
                {
                    max = window.ZOrder;
                }
            }

            return max;
        }

        if (node is ImDockSplit split)
        {
            int first = GetNodeMaxZOrder(split.First, windowManager);
            int second = GetNodeMaxZOrder(split.Second, windowManager);
            return first > second ? first : second;
        }

        return 0;
    }

    /// <summary>
    /// Draw the ghost tab preview (on top of everything).
    /// </summary>
    public void DrawGhostPreview(Vector2 viewportScreenPos, Vector2 mousePosScreen)
    {
        if (GhostDraggingWindowId == 0)
        {
            return;
        }

        var window = Im.WindowManager.FindWindowById(GhostDraggingWindowId);
        if (window == null)
        {
            return;
        }

        float ghostWidth = Math.Min(window.Rect.Width, 220f);
        float ghostHeight = Math.Min(window.Rect.Height, 160f);
        var ghostRect = new ImRect(
            mousePosScreen.X - ghostWidth * 0.5f,
            mousePosScreen.Y - 10f,
            ghostWidth,
            ghostHeight);

        // Convert to viewport-local
        float x = ghostRect.X - viewportScreenPos.X;
        float y = ghostRect.Y - viewportScreenPos.Y;

        var style = Im.Style;
        Im.DrawRoundedRect(x, y, ghostRect.Width, ghostRect.Height, style.CornerRadius, style.Background & 0x80FFFFFF);
        Im.DrawRoundedRectStroke(x, y, ghostRect.Width, ghostRect.Height, style.CornerRadius, style.Border, style.BorderWidth);
        Im.LabelTextViewport(window.Title, x + style.Padding, y + style.Padding);
    }

    private void HandleGhostDrop(ImWindowManager windowManager, Vector2 mousePos)
    {
        if (GhostDraggingWindowId == 0)
        {
            return;
        }

        // Dropped back on the source leaf (no-op).
        if (PreviewTargetLayout == _ghostSourceLayout
            && PreviewTargetLeaf == _ghostSourceLeaf
            && PreviewZone == ImDockZone.Center)
        {
            return;
        }

        if (PreviewZone != ImDockZone.None && PreviewTargetLayout != null)
        {
            // Dock to target
            AcceptDock(PreviewTargetLayout, GhostDraggingWindowId, PreviewZone, PreviewTargetLeaf, windowManager, isRootLevelDock: PreviewIsRootLevelDock);
        }
        else
        {
            // Undock to floating at mouse position
            var window = windowManager.FindWindowById(GhostDraggingWindowId);
            if (window != null)
            {
                window.Rect = new ImRect(
                    mousePos.X - window.Rect.Width * 0.5f,
                    mousePos.Y - 10f,
                    window.Rect.Width,
                    window.Rect.Height);
            }

            UndockWindow(GhostDraggingWindowId, windowManager);
        }
    }

    private void ClearGhostDrag()
    {
        GhostDraggingWindowId = 0;
        _ghostSourceLayout = null;
        _ghostSourceLeaf = null;
    }

    private static ImViewport? FindViewportContainingScreenPos(Vector2 screenPos)
    {
        var ctx = Im.Context;
        for (int i = ctx.Viewports.Count - 1; i >= 0; i--)
        {
            var viewport = ctx.Viewports[i];
            if (viewport.ScreenBounds.Contains(screenPos))
            {
                return viewport;
            }
        }

        return ctx.PrimaryViewport;
    }

    //=== Dock Operations (commit on drop) ===

    private void HandleDrop(ImWindowManager windowManager, int windowId, DockingLayout? sourceLayout, Vector2 mousePos)
    {
        // Recompute target at drop time (avoids one-frame mismatch).
        DockingLayout? targetLayout = null;
        ImDockLeaf? targetLeaf = null;
        ImDockZone targetZone = ImDockZone.None;
        bool isRootLevelDock = false;

        // Floating layouts first
        for (int i = FloatingLayouts.Count - 1; i >= 0; i--)
        {
            var layout = FloatingLayouts[i];
            if (layout == sourceLayout)
            {
                continue;
            }

            if (!layout.Rect.Contains(mousePos))
            {
                continue;
            }

            if (!TryGetTargetLeafAndZone(layout, mousePos, out targetLeaf, out targetZone, out isRootLevelDock))
            {
                targetLayout = null;
                targetLeaf = null;
                targetZone = ImDockZone.None;
                isRootLevelDock = false;
                break;
            }

            targetLayout = layout;
            break;
        }

        // Main layout
        if (targetLayout == null && MainLayout.Rect.Contains(mousePos))
        {
            if (TryGetTargetLeafAndZone(MainLayout, mousePos, out targetLeaf, out targetZone, out isRootLevelDock))
            {
                targetLayout = MainLayout;
            }
        }

        if (targetLayout == null || targetZone == ImDockZone.None)
        {
            return;
        }

        AcceptDock(targetLayout, windowId, targetZone, targetLeaf, windowManager, isRootLevelDock);
    }

    private bool TryGetTargetLeafAndZone(DockingLayout layout, Vector2 mousePos, out ImDockLeaf? leaf, out ImDockZone zone, out bool isRootLevelDock)
    {
        isRootLevelDock = false;
        if (layout.Root == null)
        {
            leaf = null;
            zone = ImDockZone.Center;
            return true;
        }

        var dockRect = layout.GetDockRect(Im.Style);
        leaf = FindLeafAt(layout.Root, mousePos);
        if (leaf == null)
        {
            zone = ImDockZone.None;
            return false;
        }

        zone = leaf.GetDockZone(mousePos.X, mousePos.Y);
        isRootLevelDock = ShouldRootLevelDock(dockRect, leaf, mousePos, zone);
        return zone != ImDockZone.None;
    }

    /// <summary>
    /// Dock a window into a layout (tabs for Center, split for edges).
    /// Also removes it from its source layout if needed.
    /// </summary>
    public void AcceptDock(DockingLayout targetLayout, int windowId, ImDockZone zone, ImDockLeaf? targetLeaf, ImWindowManager windowManager, bool isRootLevelDock = false)
    {
        // Remove from any current layout first.
        var sourceLayout = FindLayoutForWindow(windowId);
        if (sourceLayout != null)
        {
            // Dropping back onto the same leaf center is a no-op.
            if (sourceLayout == targetLayout
                && zone == ImDockZone.Center
                && targetLeaf != null
                && ReferenceEquals(sourceLayout.FindLeafWithWindow(windowId), targetLeaf))
            {
                UpdateDockedWindowFlags(windowManager);
                return;
            }

            RemoveWindowFromLayout(sourceLayout, windowId, windowManager);
            if (sourceLayout != targetLayout && !sourceLayout.IsMainLayout && sourceLayout.GetWindowCount() == 0)
            {
                FloatingLayouts.Remove(sourceLayout);
            }
        }

        // Empty target layout: first dock creates a leaf.
        if (targetLayout.Root == null || targetLeaf == null)
        {
            var leaf = new ImDockLeaf();
            leaf.WindowIds.Add(windowId);
            leaf.ActiveTabIndex = 0;
            targetLayout.Root = leaf;
            targetLayout.UpdateLayout();
            UpdateDockedWindowFlags(windowManager);
            return;
        }

        if (zone == ImDockZone.Center)
        {
            if (!targetLeaf.WindowIds.Contains(windowId))
            {
                targetLeaf.WindowIds.Add(windowId);
                targetLeaf.ActiveTabIndex = targetLeaf.WindowIds.Count - 1;
            }

            targetLayout.UpdateLayout();
            UpdateDockedWindowFlags(windowManager);
            return;
        }

        if (isRootLevelDock && targetLayout.Root != null)
        {
            var rootLeaf = new ImDockLeaf();
            rootLeaf.WindowIds.Add(windowId);
            rootLeaf.ActiveTabIndex = 0;

            var rootDirection = zone is ImDockZone.Left or ImDockZone.Right
                ? ImSplitDirection.Horizontal
                : ImSplitDirection.Vertical;

            ImDockNode rootFirst;
            ImDockNode rootSecond;
            float ratio;
            if (zone is ImDockZone.Left or ImDockZone.Top)
            {
                rootFirst = rootLeaf;
                rootSecond = targetLayout.Root;
                ratio = RootDockRatio;
            }
            else
            {
                rootFirst = targetLayout.Root;
                rootSecond = rootLeaf;
                ratio = 1f - RootDockRatio;
            }

            targetLayout.Root = new ImDockSplit(rootFirst, rootSecond, rootDirection, ratio);
            targetLayout.UpdateLayout();
            UpdateDockedWindowFlags(windowManager);
            return;
        }

        // Edge docking: split target leaf.
        var newLeaf = new ImDockLeaf();
        newLeaf.WindowIds.Add(windowId);
        newLeaf.ActiveTabIndex = 0;

        var direction = zone is ImDockZone.Left or ImDockZone.Right
            ? ImSplitDirection.Horizontal
            : ImSplitDirection.Vertical;

        ImDockNode first;
        ImDockNode second;
        if (zone is ImDockZone.Left or ImDockZone.Top)
        {
            first = newLeaf;
            second = targetLeaf;
        }
        else
        {
            first = targetLeaf;
            second = newLeaf;
        }

        var split = new ImDockSplit(first, second, direction, ratio: 0.5f);
        ReplaceNode(targetLayout, targetLeaf, split);
        targetLayout.UpdateLayout();
        UpdateDockedWindowFlags(windowManager);
    }

    private static int HitTestResize(ImRect rect, Vector2 point, float grabSize)
    {
        float x = rect.X;
        float y = rect.Y;
        float w = rect.Width;
        float h = rect.Height;

        bool nearLeft = point.X >= x - grabSize && point.X < x + grabSize;
        bool nearRight = point.X >= x + w - grabSize && point.X < x + w + grabSize;
        bool nearTop = point.Y >= y - grabSize && point.Y < y + grabSize;
        bool nearBottom = point.Y >= y + h - grabSize && point.Y < y + h + grabSize;

        bool inVerticalSpan = point.Y >= y - grabSize && point.Y < y + h + grabSize;
        bool inHorizontalSpan = point.X >= x - grabSize && point.X < x + w + grabSize;

        if (nearLeft && nearTop) return 5;
        if (nearRight && nearTop) return 6;
        if (nearLeft && nearBottom) return 7;
        if (nearRight && nearBottom) return 8;

        if (nearLeft && inVerticalSpan) return 1;
        if (nearRight && inVerticalSpan) return 2;
        if (nearTop && inHorizontalSpan) return 3;
        if (nearBottom && inHorizontalSpan) return 4;

        return 0;
    }

    private static void ApplyResize(ref ImRect rect, int edge, Vector2 mouseDelta)
    {
        const float minWidth = 200f;
        const float minHeight = 140f;

        switch (edge)
        {
            case 1:
            {
                float newWidth = rect.Width - mouseDelta.X;
                if (newWidth < minWidth)
                {
                    newWidth = minWidth;
                }

                float deltaWidth = rect.Width - newWidth;
                rect = new ImRect(rect.X + deltaWidth, rect.Y, newWidth, rect.Height);
                break;
            }

            case 2:
            {
                float newWidth = rect.Width + mouseDelta.X;
                if (newWidth < minWidth)
                {
                    newWidth = minWidth;
                }

                rect = new ImRect(rect.X, rect.Y, newWidth, rect.Height);
                break;
            }

            case 3:
            {
                float newHeight = rect.Height - mouseDelta.Y;
                if (newHeight < minHeight)
                {
                    newHeight = minHeight;
                }

                float deltaHeight = rect.Height - newHeight;
                rect = new ImRect(rect.X, rect.Y + deltaHeight, rect.Width, newHeight);
                break;
            }

            case 4:
            {
                float newHeight = rect.Height + mouseDelta.Y;
                if (newHeight < minHeight)
                {
                    newHeight = minHeight;
                }

                rect = new ImRect(rect.X, rect.Y, rect.Width, newHeight);
                break;
            }

            case 5:
                ApplyResize(ref rect, 1, mouseDelta);
                ApplyResize(ref rect, 3, mouseDelta);
                break;
            case 6:
                ApplyResize(ref rect, 2, mouseDelta);
                ApplyResize(ref rect, 3, mouseDelta);
                break;
            case 7:
                ApplyResize(ref rect, 1, mouseDelta);
                ApplyResize(ref rect, 4, mouseDelta);
                break;
            case 8:
                ApplyResize(ref rect, 2, mouseDelta);
                ApplyResize(ref rect, 4, mouseDelta);
                break;
        }
    }

    private static StandardCursor GetResizeCursor(int edge)
    {
        return edge switch
        {
            1 or 2 => StandardCursor.HResize,
            3 or 4 => StandardCursor.VResize,
            5 or 6 or 7 or 8 => StandardCursor.Crosshair,
            _ => StandardCursor.Default
        };
    }

    /// <summary>
    /// Undock a window (remove from its current layout). The window becomes floating.
    /// </summary>
    public void UndockWindow(int windowId, ImWindowManager windowManager)
    {
        var sourceLayout = FindLayoutForWindow(windowId);
        if (sourceLayout == null)
        {
            return;
        }

        if (RemoveWindowFromLayout(sourceLayout, windowId, windowManager))
        {
            if (!sourceLayout.IsMainLayout && sourceLayout.GetWindowCount() == 0)
            {
                FloatingLayouts.Remove(sourceLayout);
            }

            UpdateDockedWindowFlags(windowManager);
        }
    }

    private static bool RemoveWindowFromLayout(DockingLayout layout, int windowId, ImWindowManager windowManager)
    {
        var leaf = layout.FindLeafWithWindow(windowId);
        if (leaf == null)
        {
            return false;
        }

        int index = leaf.WindowIds.IndexOf(windowId);
        if (index < 0)
        {
            return false;
        }

        leaf.WindowIds.RemoveAt(index);
        if (index < leaf.ActiveTabIndex)
        {
            leaf.ActiveTabIndex--;
        }

        if (leaf.ActiveTabIndex >= leaf.WindowIds.Count)
        {
            leaf.ActiveTabIndex = Math.Max(0, leaf.WindowIds.Count - 1);
        }

        // Ensure the window is no longer treated as docked until flags are recomputed.
        var window = windowManager.FindWindowById(windowId);
        if (window != null)
        {
            window.IsDocked = false;
            window.DockNodeId = -1;
        }

        layout.Root = PruneNode(layout.Root);
        ApplyLayoutAfterTreeMutation(layout, windowManager);
        return true;
    }

    private static ImDockNode? PruneNode(ImDockNode? node)
    {
        if (node == null)
        {
            return null;
        }

        if (node is ImDockLeaf leaf)
        {
            return leaf.WindowIds.Count == 0 ? null : leaf;
        }

        if (node is ImDockSplit split)
        {
            split.First = PruneNode(split.First)!;
            split.Second = PruneNode(split.Second)!;

            if (split.First == null || split.First.IsEmpty)
            {
                return split.Second;
            }

            if (split.Second == null || split.Second.IsEmpty)
            {
                return split.First;
            }

            return split;
        }

        return node;
    }

    private static void ReplaceNode(DockingLayout layout, ImDockNode oldNode, ImDockNode newNode)
    {
        if (layout.Root == oldNode)
        {
            layout.Root = newNode;
            return;
        }

        var parent = FindParentRecursive(layout.Root, oldNode);
        if (parent != null)
        {
            if (parent.First == oldNode)
            {
                parent.First = newNode;
            }
            else if (parent.Second == oldNode)
            {
                parent.Second = newNode;
            }
        }
    }

    private static ImDockSplit? FindParentRecursive(ImDockNode? node, ImDockNode target)
    {
        if (node is not ImDockSplit split)
        {
            return null;
        }

        if (split.First == target || split.Second == target)
        {
            return split;
        }

        return FindParentRecursive(split.First, target) ?? FindParentRecursive(split.Second, target);
    }

    private static ImDockLeaf? FindLeafAt(ImDockNode? node, Vector2 point)
    {
        if (node == null)
        {
            return null;
        }

        if (!node.Rect.Contains(point))
        {
            return null;
        }

        if (node is ImDockLeaf leaf)
        {
            return leaf;
        }

        if (node is ImDockSplit split)
        {
            return FindLeafAt(split.First, point) ?? FindLeafAt(split.Second, point);
        }

        return null;
    }
}
