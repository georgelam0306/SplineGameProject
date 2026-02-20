using System.Numerics;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Rendering;
using DerpLib.ImGui.Windows;

namespace DerpLib.ImGui.Docking;

/// <summary>
/// Root container for the dock tree. Manages the tree structure,
/// handles docking/undocking operations, and renders dock previews.
/// </summary>
public class ImDockSpace
{
    /// <summary>Ratio of dock space used by root-level docks (25%).</summary>
    private const float RootDockRatio = 0.25f;

    /// <summary>Pixels from edge to trigger root-level docking.</summary>
    private const float RootDockEdgeThreshold = 30f;

    /// <summary>Dock space ID.</summary>
    public int Id;

    /// <summary>Root node of the dock tree (null when empty).</summary>
    public ImDockNode? Root;

    /// <summary>Dock space bounds.</summary>
    public ImRect Rect;

    /// <summary>Window ID being dragged for docking (0 = none).</summary>
    public int DraggingWindowId;

    /// <summary>Current dock preview zone.</summary>
    public ImDockZone PreviewZone;

    /// <summary>Leaf being previewed for docking.</summary>
    public ImDockLeaf? PreviewLeaf;

    /// <summary>Whether preview zone is at dock space boundary (root-level dock).</summary>
    public bool IsRootLevelDock;

    /// <summary>Window ID being ghost-dragged from a dock tab (0 = none).</summary>
    public int GhostDraggingWindowId;

    /// <summary>Source leaf where ghost drag started.</summary>
    public ImDockLeaf? GhostSourceLeaf;

    /// <summary>Whether layout is currently pushed for preview.</summary>
    private bool _isPushedForPreview;

    /// <summary>The rect where the preview overlay should be drawn.</summary>
    private ImRect _previewRect;

    public ImDockSpace(int id = 0)
    {
        Id = id != 0 ? id : HashId("##DockSpace");
    }

    /// <summary>
    /// Update layout for the entire dock tree.
    /// </summary>
    public void UpdateLayout(ImRect rect)
    {
        Rect = rect;
        Root?.UpdateLayout(rect);
    }

    /// <summary>
    /// Draw the dock space background and all dock chrome (tabs, splitters).
    /// Call this BEFORE rendering window content.
    /// </summary>
    public void Draw()
    {
        Im.SetDrawLayer(ImDrawLayer.Background);

        // Draw dock space background
        Im.DrawRect(Rect.X, Rect.Y, Rect.Width, Rect.Height, Im.Style.Background);

        // Draw dock tree (tabs, splitters)
        Root?.Draw();
    }

    /// <summary>
    /// Draw the dock preview overlay.
    /// Call this AFTER rendering window content.
    /// </summary>
    public void DrawPreviewOverlay()
    {
        if (DraggingWindowId != 0 && PreviewZone != ImDockZone.None)
        {
            Im.SetDrawLayer(ImDrawLayer.Overlay);

            ImRect previewRect;
            if (_isPushedForPreview)
            {
                previewRect = _previewRect;
            }
            else if (PreviewZone == ImDockZone.Center)
            {
                previewRect = PreviewLeaf?.Rect ?? Rect;
            }
            else
            {
                previewRect = Rect;
            }

            // Draw semi-transparent preview overlay
            Im.DrawRoundedRect(previewRect.X, previewRect.Y, previewRect.Width, previewRect.Height,
                Im.Style.CornerRadius, Im.Style.DockPreview);
        }

        DrawGhostPreview();
    }

    /// <summary>
    /// Draw ghost window preview at mouse position (for tab drag).
    /// </summary>
    private void DrawGhostPreview()
    {
        if (GhostDraggingWindowId == 0)
            return;

        var window = Im.WindowManager.FindWindowById(GhostDraggingWindowId);
        if (window == null)
            return;

        Im.SetDrawLayer(ImDrawLayer.Overlay);

        var input = Im.Context.Input;
        float ghostWidth = Math.Min(window.Rect.Width, 200);
        float ghostHeight = Math.Min(window.Rect.Height, 150);
        var ghostRect = new ImRect(
            input.MousePos.X - ghostWidth * 0.5f,
            input.MousePos.Y - 10,
            ghostWidth,
            ghostHeight);

        // Semi-transparent window preview
        var style = Im.Style;
        Im.DrawRoundedRect(ghostRect.X, ghostRect.Y, ghostRect.Width, ghostRect.Height,
            style.CornerRadius, style.Background & 0x80FFFFFF);
        Im.DrawRoundedRectStroke(ghostRect.X, ghostRect.Y, ghostRect.Width, ghostRect.Height,
            style.CornerRadius, style.Border, style.BorderWidth);

        // Title
        float textX = ghostRect.X + style.Padding;
        float textY = ghostRect.Y + style.Padding;
        Im.LabelText(window.Title, textX, textY);
    }

    /// <summary>
    /// Process docking input - detect drags, update preview state, handle drops.
    /// </summary>
    public void ProcessInput()
    {
        var input = Im.Context.Input;
        var windowManager = Im.WindowManager;

        // Restore layout from previous frame's push
        if (_isPushedForPreview)
        {
            Root?.UpdateLayout(Rect);
            _isPushedForPreview = false;
        }

        // Check for dragging window (floating or ghost)
        ImWindow? draggingWindow = GetDraggingFloatingWindow(windowManager);
        if (draggingWindow == null && GhostDraggingWindowId != 0)
            draggingWindow = windowManager.FindWindowById(GhostDraggingWindowId);

        if (draggingWindow != null)
        {
            DraggingWindowId = draggingWindow.Id;

            // Find leaf under mouse
            PreviewLeaf = FindLeafAt(input.MousePos);

            if (PreviewLeaf != null)
            {
                PreviewZone = PreviewLeaf.GetDockZone(input.MousePos.X, input.MousePos.Y);

                // Check for root-level dock (near dock space edge)
                IsRootLevelDock = PreviewZone switch
                {
                    ImDockZone.Left => MathF.Abs(PreviewLeaf.Rect.X - Rect.X) < 1f
                                       && input.MousePos.X < Rect.X + RootDockEdgeThreshold,
                    ImDockZone.Right => MathF.Abs(PreviewLeaf.Rect.Right - Rect.Right) < 1f
                                        && input.MousePos.X > Rect.Right - RootDockEdgeThreshold,
                    ImDockZone.Top => MathF.Abs(PreviewLeaf.Rect.Y - Rect.Y) < 1f
                                      && input.MousePos.Y < Rect.Y + RootDockEdgeThreshold,
                    ImDockZone.Bottom => MathF.Abs(PreviewLeaf.Rect.Bottom - Rect.Bottom) < 1f
                                         && input.MousePos.Y > Rect.Bottom - RootDockEdgeThreshold,
                    _ => false
                };
            }
            else if (Root == null && Rect.Contains(input.MousePos))
            {
                // Empty dock space - first dock uses Center
                PreviewZone = ImDockZone.Center;
            }
            else
            {
                PreviewZone = ImDockZone.None;
            }

            // Check for "return to source" (dragging only window back to its leaf)
            bool isReturnToSource = GhostDraggingWindowId != 0
                && PreviewLeaf == GhostSourceLeaf
                && GhostSourceLeaf != null
                && GhostSourceLeaf.WindowIds.Count == 1;

            if (isReturnToSource)
            {
                PreviewZone = ImDockZone.None;
                IsRootLevelDock = false;
            }

            // Handle drop
            if (input.MouseReleased)
            {
                if (GhostDraggingWindowId != 0)
                {
                    HandleGhostDrop(isReturnToSource, windowManager);
                }
                else if (PreviewZone != ImDockZone.None)
                {
                    DockWindow(draggingWindow.Id, PreviewLeaf, PreviewZone, windowManager);
                }

                ResetDragState();
            }
            else
            {
                ApplyPushedLayoutForPreview();
            }
        }
        else
        {
            ResetDragState();
        }
    }

    private void HandleGhostDrop(bool isReturnToSource, ImWindowManager windowManager)
    {
        var input = Im.Context.Input;

        if (isReturnToSource)
        {
            // Dropped back on source - do nothing, window stays docked
        }
        else if (PreviewZone != ImDockZone.None)
        {
            // Valid dock zone - undock and re-dock to new location
            UndockWindow(GhostDraggingWindowId, windowManager);
            DockWindow(GhostDraggingWindowId, PreviewLeaf, PreviewZone, windowManager);
        }
        else
        {
            // Null space - undock to floating at mouse position
            var window = windowManager.FindWindowById(GhostDraggingWindowId);
            if (window != null)
            {
                var oldRect = window.Rect;
                window.Rect = new ImRect(
                    input.MousePos.X - oldRect.Width * 0.5f,
                    input.MousePos.Y - 10,
                    oldRect.Width,
                    oldRect.Height);
            }
            UndockWindow(GhostDraggingWindowId, windowManager);
        }
    }

    private void ResetDragState()
    {
        DraggingWindowId = 0;
        GhostDraggingWindowId = 0;
        GhostSourceLeaf = null;
        PreviewLeaf = null;
        PreviewZone = ImDockZone.None;
        IsRootLevelDock = false;
    }

    /// <summary>
    /// Apply pushed layout for preview (shows gap where window would dock).
    /// </summary>
    private void ApplyPushedLayoutForPreview()
    {
        if (PreviewZone == ImDockZone.None || PreviewZone == ImDockZone.Center)
            return;

        if (IsRootLevelDock && Root != null)
        {
            var pushedRect = GetPushedOverRect(PreviewZone);
            _previewRect = GetPreviewRectFromPush(Rect, pushedRect, PreviewZone);
            Root.UpdateLayout(pushedRect);
            _isPushedForPreview = true;
        }
        else if (PreviewLeaf != null)
        {
            var originalRect = PreviewLeaf.Rect;
            var pushedRect = GetLeafPushedOverRect(PreviewLeaf, PreviewZone);
            _previewRect = GetPreviewRectFromPush(originalRect, pushedRect, PreviewZone);
            PreviewLeaf.UpdateLayout(pushedRect);
            _isPushedForPreview = true;
        }
    }

    /// <summary>
    /// Dock a window into a leaf at a zone.
    /// </summary>
    public void DockWindow(int windowId, ImDockLeaf? targetLeaf, ImDockZone zone, ImWindowManager windowManager)
    {
        var window = windowManager.FindWindowById(windowId);
        if (window == null)
            return;

        // Remove from current dock if already docked
        if (window.IsDocked)
        {
            var currentLeaf = Root?.FindLeafWithWindow(windowId);
            currentLeaf?.RemoveWindow(windowId, windowManager);
        }

        // Case 1: Empty dock space or no target - first dock
        if (Root == null || targetLeaf == null)
        {
            var newLeaf = new ImDockLeaf();
            newLeaf.AddWindow(windowId, windowManager);
            Root = newLeaf;
        }
        // Case 2: Center zone - add as tab
        else if (zone == ImDockZone.Center)
        {
            targetLeaf.AddWindow(windowId, windowManager);
        }
        // Case 3: Edge zone - create split
        else
        {
            var newLeaf = new ImDockLeaf();
            newLeaf.AddWindow(windowId, windowManager);

            var direction = zone is ImDockZone.Left or ImDockZone.Right
                ? ImSplitDirection.Horizontal
                : ImSplitDirection.Vertical;

            ImDockNode nodeToSplit = IsRootLevelDock ? Root : targetLeaf;

            ImDockNode first, second;
            if (zone is ImDockZone.Left or ImDockZone.Top)
            {
                first = newLeaf;
                second = nodeToSplit;
            }
            else
            {
                first = nodeToSplit;
                second = newLeaf;
            }

            var split = new ImDockSplit(first, second, direction);

            if (IsRootLevelDock)
            {
                split.SplitRatio = zone is ImDockZone.Left or ImDockZone.Top
                    ? RootDockRatio
                    : 1f - RootDockRatio;
                Root = split;
            }
            else
            {
                ReplaceNode(targetLeaf, split);
            }
        }

        UpdateLayout(Rect);
    }

    /// <summary>
    /// Undock a window (remove from dock tree, becomes floating).
    /// </summary>
    public void UndockWindow(int windowId, ImWindowManager windowManager)
    {
        var leaf = Root?.FindLeafWithWindow(windowId);
        if (leaf == null)
            return;

        leaf.RemoveWindow(windowId, windowManager);
        PruneTree();
        UpdateLayout(Rect);
    }

    /// <summary>
    /// Find the leaf at a point.
    /// </summary>
    public ImDockLeaf? FindLeafAt(Vector2 point)
    {
        if (!Rect.Contains(point))
            return null;
        return FindLeafAtRecursive(Root, point);
    }

    private ImDockLeaf? FindLeafAtRecursive(ImDockNode? node, Vector2 point)
    {
        if (node == null)
            return null;

        if (node is ImDockLeaf leaf && leaf.Rect.Contains(point))
            return leaf;

        if (node is ImDockSplit split)
            return FindLeafAtRecursive(split.First, point) ?? FindLeafAtRecursive(split.Second, point);

        return null;
    }

    /// <summary>
    /// Replace a node in the tree with another.
    /// </summary>
    private void ReplaceNode(ImDockNode oldNode, ImDockNode newNode)
    {
        if (Root == oldNode)
        {
            Root = newNode;
            return;
        }

        var parent = FindParentOf(oldNode);
        if (parent != null)
        {
            if (parent.First == oldNode)
                parent.First = newNode;
            else if (parent.Second == oldNode)
                parent.Second = newNode;
        }
    }

    private ImDockSplit? FindParentOf(ImDockNode target)
    {
        return FindParentRecursive(Root, target);
    }

    private ImDockSplit? FindParentRecursive(ImDockNode? node, ImDockNode target)
    {
        if (node is not ImDockSplit split)
            return null;

        if (split.First == target || split.Second == target)
            return split;

        return FindParentRecursive(split.First, target) ?? FindParentRecursive(split.Second, target);
    }

    /// <summary>
    /// Remove empty nodes from the tree.
    /// </summary>
    private void PruneTree()
    {
        Root = PruneNode(Root);
    }

    private ImDockNode? PruneNode(ImDockNode? node)
    {
        if (node == null)
            return null;

        if (node is ImDockLeaf leaf)
            return leaf.IsEmpty ? null : leaf;

        if (node is ImDockSplit split)
        {
            split.First = PruneNode(split.First)!;
            split.Second = PruneNode(split.Second)!;

            if (split.First == null || split.First.IsEmpty)
                return split.Second;
            if (split.Second == null || split.Second.IsEmpty)
                return split.First;

            return split;
        }

        return node;
    }

    private ImWindow? GetDraggingFloatingWindow(ImWindowManager windowManager)
    {
        // Check if any floating window is being dragged
        // This requires integration with ImWindowManager's drag state
        // For now, return null - will be connected later
        return null;
    }

    private ImRect GetRootDockZoneRect(ImDockZone zone)
    {
        return zone switch
        {
            ImDockZone.Left => new ImRect(Rect.X, Rect.Y, Rect.Width * RootDockRatio, Rect.Height),
            ImDockZone.Right => new ImRect(Rect.Right - Rect.Width * RootDockRatio, Rect.Y, Rect.Width * RootDockRatio, Rect.Height),
            ImDockZone.Top => new ImRect(Rect.X, Rect.Y, Rect.Width, Rect.Height * RootDockRatio),
            ImDockZone.Bottom => new ImRect(Rect.X, Rect.Bottom - Rect.Height * RootDockRatio, Rect.Width, Rect.Height * RootDockRatio),
            _ => Rect
        };
    }

    private ImRect GetPushedOverRect(ImDockZone zone)
    {
        float offset = zone is ImDockZone.Left or ImDockZone.Right
            ? Rect.Width * RootDockRatio
            : Rect.Height * RootDockRatio;

        return zone switch
        {
            ImDockZone.Left => new ImRect(Rect.X + offset, Rect.Y, Rect.Width - offset, Rect.Height),
            ImDockZone.Right => new ImRect(Rect.X, Rect.Y, Rect.Width - offset, Rect.Height),
            ImDockZone.Top => new ImRect(Rect.X, Rect.Y + offset, Rect.Width, Rect.Height - offset),
            ImDockZone.Bottom => new ImRect(Rect.X, Rect.Y, Rect.Width, Rect.Height - offset),
            _ => Rect
        };
    }

    private ImRect GetLeafPushedOverRect(ImDockLeaf leaf, ImDockZone zone)
    {
        const float ratio = 0.5f;
        return zone switch
        {
            ImDockZone.Left => new ImRect(leaf.Rect.X + leaf.Rect.Width * ratio, leaf.Rect.Y,
                leaf.Rect.Width * (1 - ratio), leaf.Rect.Height),
            ImDockZone.Right => new ImRect(leaf.Rect.X, leaf.Rect.Y,
                leaf.Rect.Width * (1 - ratio), leaf.Rect.Height),
            ImDockZone.Top => new ImRect(leaf.Rect.X, leaf.Rect.Y + leaf.Rect.Height * ratio,
                leaf.Rect.Width, leaf.Rect.Height * (1 - ratio)),
            ImDockZone.Bottom => new ImRect(leaf.Rect.X, leaf.Rect.Y,
                leaf.Rect.Width, leaf.Rect.Height * (1 - ratio)),
            _ => leaf.Rect
        };
    }

    private ImRect GetPreviewRectFromPush(ImRect original, ImRect pushed, ImDockZone zone)
    {
        return zone switch
        {
            ImDockZone.Left => new ImRect(original.X, original.Y, pushed.X - original.X, original.Height),
            ImDockZone.Right => new ImRect(pushed.Right, original.Y, original.Right - pushed.Right, original.Height),
            ImDockZone.Top => new ImRect(original.X, original.Y, original.Width, pushed.Y - original.Y),
            ImDockZone.Bottom => new ImRect(original.X, pushed.Bottom, original.Width, original.Bottom - pushed.Bottom),
            _ => original
        };
    }

    /// <summary>
    /// Get the first leaf in the tree (for programmatic docking).
    /// </summary>
    public ImDockLeaf? GetFirstLeaf()
    {
        return GetFirstLeafRecursive(Root);
    }

    private ImDockLeaf? GetFirstLeafRecursive(ImDockNode? node)
    {
        if (node == null)
            return null;
        if (node is ImDockLeaf leaf)
            return leaf;
        if (node is ImDockSplit split)
            return GetFirstLeafRecursive(split.First) ?? GetFirstLeafRecursive(split.Second);
        return null;
    }

    /// <summary>
    /// Clear the dock space (undock all windows).
    /// </summary>
    public void Clear()
    {
        Root = null;
    }

    private static int HashId(string str)
    {
        unchecked
        {
            const int fnvPrime = 16777619;
            int hash = (int)2166136261;
            foreach (char c in str)
            {
                hash ^= c;
                hash *= fnvPrime;
            }
            return hash;
        }
    }
}

/// <summary>
/// Static API for dock space management.
/// </summary>
public static class ImDocking
{
    private static ImDockSpace? _mainDockSpace;

    /// <summary>Main dock space (singleton).</summary>
    public static ImDockSpace MainDockSpace => _mainDockSpace ??= new ImDockSpace();

    /// <summary>
    /// Initialize the docking system.
    /// </summary>
    public static void Initialize()
    {
        _mainDockSpace = new ImDockSpace();
    }

    /// <summary>
    /// Begin a dock space with explicit bounds.
    /// Call this BEFORE rendering windows.
    /// </summary>
    public static void BeginDockSpace(ImRect rect)
    {
        MainDockSpace.UpdateLayout(rect);
        MainDockSpace.ProcessInput();
        MainDockSpace.Draw();
    }

    /// <summary>
    /// Begin a dock space that fills the viewport.
    /// </summary>
    public static void BeginDockSpace()
    {
        var size = Im.Context.CurrentViewport?.Size ?? new Vector2(800, 600);
        BeginDockSpace(new ImRect(0, 0, size.X, size.Y));
    }

    /// <summary>
    /// End the dock space.
    /// Call this AFTER rendering windows.
    /// </summary>
    public static void EndDockSpace()
    {
        MainDockSpace.DrawPreviewOverlay();
    }

    /// <summary>
    /// Dock a window programmatically.
    /// </summary>
    public static void DockWindow(string windowTitle, ImDockZone zone)
    {
        int windowId = HashTitle(windowTitle);
        var targetLeaf = MainDockSpace.GetFirstLeaf();
        MainDockSpace.DockWindow(windowId, targetLeaf, zone, Im.WindowManager);
    }

    /// <summary>
    /// Undock a window programmatically.
    /// </summary>
    public static void UndockWindow(string windowTitle)
    {
        int windowId = HashTitle(windowTitle);
        MainDockSpace.UndockWindow(windowId, Im.WindowManager);
    }

    private static int HashTitle(string title)
    {
        unchecked
        {
            const int fnvPrime = 16777619;
            int hash = (int)2166136261;
            foreach (char c in title)
            {
                hash ^= c;
                hash *= fnvPrime;
            }
            return hash;
        }
    }
}
