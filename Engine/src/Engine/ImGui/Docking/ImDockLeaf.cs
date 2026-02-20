using System;
using System.Numerics;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Input;
using DerpLib.ImGui.Windows;

namespace DerpLib.ImGui.Docking;

/// <summary>
/// Leaf node in the dock tree - contains docked windows displayed as tabs.
/// Multiple windows in one leaf appear as a tab bar.
/// </summary>
public class ImDockLeaf : ImDockNode
{
    /// <summary>Window IDs docked in this leaf (displayed as tabs).</summary>
    public List<int> WindowIds = new();

    /// <summary>Index of the currently active/visible tab.</summary>
    public int ActiveTabIndex;

    // Tab drag state for undocking
    private int _draggingTabId;
    private Vector2 _dragStartPos;
    private const float DragThreshold = 5f;

    public override bool IsLeaf => true;

    public override bool IsEmpty => WindowIds.Count == 0;

    /// <summary>Height of the tab bar.</summary>
    public float TabBarHeight
    {
        get
        {
            var dockController = Im.Context.DockController;
            if (dockController.IsMainViewportLeaf(this))
            {
                // Main viewport shows tabs only when there are other docked windows.
                // The main viewport host window itself is not a tab.
                return GetDisplayedTabCount(dockController) > 0 ? Im.Style.TabHeight : 0f;
            }

            return Im.Style.TabHeight;
        }
    }

    /// <summary>Tab bar bounds (top of the leaf).</summary>
    public ImRect TabBarRect => new(Rect.X, Rect.Y, Rect.Width, TabBarHeight);

    /// <summary>Content area bounds (below tab bar, where window content renders).</summary>
    public ImRect ContentRect => new(Rect.X, Rect.Y + TabBarHeight, Rect.Width, Rect.Height - TabBarHeight);

    /// <summary>
    /// Add a window to this leaf as a new tab.
    /// </summary>
    public void AddWindow(int windowId, ImWindowManager windowManager)
    {
        if (WindowIds.Contains(windowId))
            return;

        WindowIds.Add(windowId);
        ActiveTabIndex = WindowIds.Count - 1;

        // Mark window as docked
        var window = windowManager.FindWindowById(windowId);
        if (window != null)
        {
            window.IsDocked = true;
            window.DockNodeId = Id;
        }
    }

    /// <summary>
    /// Remove a window from this leaf.
    /// </summary>
    public void RemoveWindow(int windowId, ImWindowManager windowManager)
    {
        int index = WindowIds.IndexOf(windowId);
        if (index < 0)
            return;

        WindowIds.RemoveAt(index);

        // Adjust active tab if needed
        if (ActiveTabIndex >= WindowIds.Count)
            ActiveTabIndex = Math.Max(0, WindowIds.Count - 1);

        // Mark window as undocked
        var window = windowManager.FindWindowById(windowId);
        if (window != null)
        {
            window.IsDocked = false;
            window.DockNodeId = -1;
        }
    }

    /// <summary>
    /// Get the currently active window (visible tab).
    /// </summary>
    public ImWindow? GetActiveWindow(ImWindowManager windowManager)
    {
        if (ActiveTabIndex >= 0 && ActiveTabIndex < WindowIds.Count)
            return windowManager.FindWindowById(WindowIds[ActiveTabIndex]);
        return null;
    }

    public override void UpdateLayout(ImRect rect)
    {
        Rect = rect;

        // All docked windows in this leaf share the same content rect (below tab bar).
        // Only the active one is rendered, but all need correct bounds for viewport assignment.
        var contentRect = ContentRect;
        for (int i = 0; i < WindowIds.Count; i++)
        {
            int windowId = WindowIds[i];
            var window = Im.WindowManager.FindWindowById(windowId);
            if (window != null)
            {
                window.Rect = contentRect;
                window.IsDocked = true;
                window.DockNodeId = Id;
            }
        }
    }

    public override void Draw()
    {
        if (WindowIds.Count == 0)
            return;

        var dockController = Im.Context.DockController;

        // Leaf background behind docked content (skipped for NoBackground tabs like the canvas).
        var activeWindow = GetActiveWindow(Im.WindowManager);
        if (activeWindow != null && (activeWindow.Flags & ImWindowFlags.NoBackground) == 0)
        {
            var style = Im.Style;
            var viewport = Im.CurrentViewport ?? throw new InvalidOperationException("No current viewport set");
            var previousLayer = viewport.CurrentLayer;
            Im.SetDrawLayer(dockController.DockBackgroundLayer);
            Im.DrawRect(Rect.X, Rect.Y, Rect.Width, Rect.Height, style.Background);
            Im.SetDrawLayer(previousLayer);
        }

        if (!dockController.IsMainViewportLeaf(this) || GetDisplayedTabCount(dockController) > 0)
        {
            DrawTabBar();
        }
    }

    private int GetDisplayedTabCount(DockController dockController)
    {
        if (!dockController.IsMainViewportLeaf(this))
        {
            return WindowIds.Count;
        }

        int count = 0;
        for (int i = 0; i < WindowIds.Count; i++)
        {
            if (WindowIds[i] == dockController.MainViewportWindowId)
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private int GetFirstDisplayedTabIndex(DockController dockController, int excludedWindowId)
    {
        for (int i = 0; i < WindowIds.Count; i++)
        {
            int windowId = WindowIds[i];
            if (windowId == excludedWindowId)
            {
                continue;
            }

            if (windowId != dockController.MainViewportWindowId)
            {
                return i;
            }
        }

        return -1;
    }

    private void DrawTabBar()
    {
        var style = Im.Style;
        var tabBarRect = TabBarRect;
        var windowManager = Im.WindowManager;
        var dockController = Im.Context.DockController;

        int displayedTabCount = GetDisplayedTabCount(dockController);
        if (displayedTabCount <= 0)
        {
            return;
        }

        // Keep active tab valid (avoid selecting the main viewport host window).
        if (dockController.IsMainViewportLeaf(this) && ActiveTabIndex >= 0 && ActiveTabIndex < WindowIds.Count
            && WindowIds[ActiveTabIndex] == dockController.MainViewportWindowId)
        {
            int ghostWindowId = dockController.GhostDraggingWindowId;
            int firstDisplayedIndex = GetFirstDisplayedTabIndex(dockController, ghostWindowId);
            if (firstDisplayedIndex >= 0)
            {
                ActiveTabIndex = firstDisplayedIndex;
            }
        }

        GetMouseState(out var mousePosScreen, out bool mouseDown, out bool mousePressed);

        // Draw tab bar background
        var viewport = Im.CurrentViewport ?? throw new InvalidOperationException("No current viewport set");
        var previousLayer = viewport.CurrentLayer;
        Im.SetDrawLayer(DerpLib.ImGui.Rendering.ImDrawLayer.FloatingWindows);

        int oldSortKey = viewport.GetDrawList(DerpLib.ImGui.Rendering.ImDrawLayer.FloatingWindows).GetSortKey();
        viewport.GetDrawList(DerpLib.ImGui.Rendering.ImDrawLayer.FloatingWindows).SetSortKey(dockController.DockOverlaySortKey);

        Im.PushClipRect(tabBarRect);
        Im.DrawRect(tabBarRect.X, tabBarRect.Y, tabBarRect.Width, tabBarRect.Height, style.TitleBar);

        float tabX = tabBarRect.X;
        float tabWidth = Math.Min(150f, tabBarRect.Width / displayedTabCount);

        // Clear drag state on mouse up
        if (!mouseDown)
            _draggingTabId = 0;

        for (int i = 0; i < WindowIds.Count; i++)
        {
            if (dockController.IsMainViewportLeaf(this) && WindowIds[i] == dockController.MainViewportWindowId)
            {
                continue;
            }

            var window = windowManager.FindWindowById(WindowIds[i]);
            if (window == null)
                continue;

            var tabRect = new ImRect(tabX, tabBarRect.Y, tabWidth, tabBarRect.Height);
            bool isActive = i == ActiveTabIndex;
            bool hovered = tabRect.Contains(mousePosScreen);

            // Check for drag threshold - start ghost drag
            if (_draggingTabId == WindowIds[i] && mouseDown)
            {
                var delta = mousePosScreen - _dragStartPos;
                if (delta.Length() > DragThreshold)
                {
                    dockController.BeginGhostTabDrag(WindowIds[i], this);
                    _draggingTabId = 0;
                    Im.PopClipRect();
                    viewport.GetDrawList(DerpLib.ImGui.Rendering.ImDrawLayer.FloatingWindows).SetSortKey(oldSortKey);
                    Im.SetDrawLayer(previousLayer);
                    return;
                }
            }

            // Start drag on mouse press (but not on close button area)
            if (hovered && mousePressed)
            {
                float closeSize = tabBarRect.Height * 0.5f;
                float closeX = tabRect.Right - closeSize - 4;
                float closeY = tabRect.Y + (tabRect.Height - closeSize) * 0.5f;
                var closeRect = new ImRect(closeX, closeY, closeSize, closeSize);

                if (!closeRect.Contains(mousePosScreen))
                {
                    _draggingTabId = WindowIds[i];
                    _dragStartPos = mousePosScreen;
                    ActiveTabIndex = i;
                    windowManager.BringToFront(window);
                }
            }

            // Draw empty slot for window being ghost-dragged
            if (WindowIds[i] == dockController.GhostDraggingWindowId)
            {
                Im.DrawRoundedRect(tabRect.X, tabRect.Y, tabRect.Width, tabRect.Height,
                    style.CornerRadius, style.TabInactive & 0x40FFFFFF);
                tabX += tabWidth;
                continue;
            }

            // Draw tab background
            uint tabColor = isActive
                ? style.TabActive
                : (hovered ? ImStyle.Lerp(style.TabInactive, style.Hover, 0.65f) : style.TabInactive);
            float tabRadius = style.CornerRadius;
            float maxRadius = tabRect.Height * 0.5f;
            if (tabRadius > maxRadius)
            {
                tabRadius = maxRadius;
            }
            Im.DrawRoundedRectPerCorner(tabRect.X, tabRect.Y, tabRect.Width, tabRect.Height,
                tabRadius, tabRadius, 0f, 0f, tabColor);

            // Draw tab title
            float textX = tabRect.X + style.Padding;
            float textY = tabRect.Y + (tabRect.Height - style.FontSize) * 0.5f;
            float closeSizeForClip = tabBarRect.Height * 0.5f;
            float closeXForClip = tabRect.Right - closeSizeForClip - 4f;
            float textClipRight = hovered ? (closeXForClip - style.Padding) : (tabRect.Right - style.Padding);
            if (textClipRight < textX)
            {
                textClipRight = textX;
            }
            Im.PushClipRect(new ImRect(textX, tabRect.Y, textClipRight - textX, tabRect.Height));
            Im.LabelText(window.Title, textX, textY);
            Im.PopClipRect();

            // Draw close button on hover
            if (hovered)
            {
                float closeSize = tabBarRect.Height * 0.5f;
                float closeX = tabRect.Right - closeSize - 4;
                float closeY = tabRect.Y + (tabRect.Height - closeSize) * 0.5f;
                var closeRect = new ImRect(closeX, closeY, closeSize, closeSize);

                if (closeRect.Contains(mousePosScreen))
                {
                    Im.DrawRoundedRect(closeRect.X, closeRect.Y, closeRect.Width, closeRect.Height, 2, style.Hover);
                    if (mousePressed)
                    {
                        dockController.RequestCloseDockedTab(WindowIds[i], windowManager);
                        Im.PopClipRect();
                        viewport.GetDrawList(DerpLib.ImGui.Rendering.ImDrawLayer.FloatingWindows).SetSortKey(oldSortKey);
                        Im.SetDrawLayer(previousLayer);
                        return; // List modified, exit loop
                    }
                }

                // Draw X
                float pad = closeSize * 0.3f;
                Im.DrawLine(closeRect.X + pad, closeRect.Y + pad,
                    closeRect.X + closeSize - pad, closeRect.Y + closeSize - pad, 1.5f, style.TextPrimary);
                Im.DrawLine(closeRect.X + closeSize - pad, closeRect.Y + pad,
                    closeRect.X + pad, closeRect.Y + closeSize - pad, 1.5f, style.TextPrimary);
            }

            tabX += tabWidth;
        }

        Im.PopClipRect();
        viewport.GetDrawList(DerpLib.ImGui.Rendering.ImDrawLayer.FloatingWindows).SetSortKey(oldSortKey);
        Im.SetDrawLayer(previousLayer);
    }

    public override ImDockLeaf? FindLeafWithWindow(int windowId)
    {
        return WindowIds.Contains(windowId) ? this : null;
    }

    public override ImDockNode? FindNode(int nodeId)
    {
        return Id == nodeId ? this : null;
    }

    private static void GetMouseState(out Vector2 mousePosScreen, out bool mouseDown, out bool mousePressed)
    {
        var ctx = Im.Context;
        var globalInput = ctx.GlobalInput;

        if (globalInput != null)
        {
            mousePosScreen = globalInput.GlobalMousePosition;
            mouseDown = globalInput.IsMouseButtonDown(MouseButton.Left);
            mousePressed = globalInput.IsMouseButtonPressed(MouseButton.Left);
            return;
        }

        var viewport = ctx.CurrentViewport ?? throw new InvalidOperationException("No current viewport set");
        mousePosScreen = viewport.ScreenPosition + ctx.Input.MousePos;
        mouseDown = ctx.Input.MouseDown;
        mousePressed = ctx.Input.MousePressed;
    }
}
