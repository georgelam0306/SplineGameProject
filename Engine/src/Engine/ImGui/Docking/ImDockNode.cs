using DerpLib.ImGui.Core;

namespace DerpLib.ImGui.Docking;

/// <summary>
/// Base class for dock tree nodes.
/// The dock tree is a binary tree where leaves contain windows (as tabs)
/// and splits divide space between two children.
/// </summary>
public abstract class ImDockNode
{
    private static int _nextId = 1;

    /// <summary>Unique node ID.</summary>
    public int Id;

    /// <summary>Node bounds in dock space coordinates.</summary>
    public ImRect Rect;

    /// <summary>Whether this node is visible.</summary>
    public bool IsVisible = true;

    protected ImDockNode()
    {
        Id = _nextId++;
    }

    /// <summary>
    /// True if this node is a leaf (contains windows), false if it's a split.
    /// </summary>
    public abstract bool IsLeaf { get; }

    /// <summary>
    /// True if this node has no windows (leaf with no windows, or split with empty children).
    /// Used for pruning empty nodes from the tree.
    /// </summary>
    public abstract bool IsEmpty { get; }

    /// <summary>
    /// Determine which dock zone a point falls into within this node's bounds.
    /// Returns None if the point is outside the bounds.
    /// </summary>
    public ImDockZone GetDockZone(float x, float y)
    {
        if (!Rect.Contains(x, y))
            return ImDockZone.None;

        var style = Im.Style;

        float edgeFraction = style.DockZoneEdgeFraction;
        if (edgeFraction <= 0f)
        {
            edgeFraction = 0.25f;
        }

        float gap = style.DockZoneGap;
        if (gap < 0f)
        {
            gap = 0f;
        }

        float edgeX = Rect.Width * edgeFraction;
        float edgeY = Rect.Height * edgeFraction;

        // Compute zone rectangles with gaps between them.
        var centerRect = new ImRect(
            Rect.X + edgeX + gap,
            Rect.Y + edgeY + gap,
            Rect.Width - 2f * (edgeX + gap),
            Rect.Height - 2f * (edgeY + gap));

        var leftRect = new ImRect(
            Rect.X + gap,
            Rect.Y + edgeY + gap,
            edgeX - 2f * gap,
            Rect.Height - 2f * (edgeY + gap));

        var rightRect = new ImRect(
            Rect.Right - edgeX + gap,
            Rect.Y + edgeY + gap,
            edgeX - 2f * gap,
            Rect.Height - 2f * (edgeY + gap));

        var topRect = new ImRect(
            Rect.X + edgeX + gap,
            Rect.Y + gap,
            Rect.Width - 2f * (edgeX + gap),
            edgeY - 2f * gap);

        var bottomRect = new ImRect(
            Rect.X + edgeX + gap,
            Rect.Bottom - edgeY + gap,
            Rect.Width - 2f * (edgeX + gap),
            edgeY - 2f * gap);

        // If a rect is degenerate, treat it as non-hit-testable.
        if (leftRect.Width > 0f && leftRect.Height > 0f && leftRect.Contains(x, y)) return ImDockZone.Left;
        if (rightRect.Width > 0f && rightRect.Height > 0f && rightRect.Contains(x, y)) return ImDockZone.Right;
        if (topRect.Width > 0f && topRect.Height > 0f && topRect.Contains(x, y)) return ImDockZone.Top;
        if (bottomRect.Width > 0f && bottomRect.Height > 0f && bottomRect.Contains(x, y)) return ImDockZone.Bottom;
        if (centerRect.Width > 0f && centerRect.Height > 0f && centerRect.Contains(x, y)) return ImDockZone.Center;

        return ImDockZone.None;
    }

    /// <summary>
    /// Get the preview/commit rect for a dock zone (matches docking split behavior).
    /// </summary>
    public ImRect GetDockZoneRect(ImDockZone zone)
    {
        float halfWidth = Rect.Width * 0.5f;
        float halfHeight = Rect.Height * 0.5f;
        return zone switch
        {
            ImDockZone.Center => Rect,
            ImDockZone.Left => new ImRect(Rect.X, Rect.Y, halfWidth, Rect.Height),
            ImDockZone.Right => new ImRect(Rect.Right - halfWidth, Rect.Y, halfWidth, Rect.Height),
            ImDockZone.Top => new ImRect(Rect.X, Rect.Y, Rect.Width, halfHeight),
            ImDockZone.Bottom => new ImRect(Rect.X, Rect.Bottom - halfHeight, Rect.Width, halfHeight),
            _ => ImRect.Zero
        };
    }

    /// <summary>
    /// Update layout for this node and all descendants.
    /// Called when the dock space rect changes or when the tree structure changes.
    /// </summary>
    public abstract void UpdateLayout(ImRect rect);

    /// <summary>
    /// Draw this node (backgrounds, tab bars, splitters).
    /// Window content is drawn separately by BeginWindow/EndWindow.
    /// </summary>
    public abstract void Draw();

    /// <summary>
    /// Find the leaf node containing a specific window.
    /// Returns null if the window is not in this subtree.
    /// </summary>
    public abstract ImDockLeaf? FindLeafWithWindow(int windowId);

    /// <summary>
    /// Find a node by its ID.
    /// </summary>
    public abstract ImDockNode? FindNode(int nodeId);
}
