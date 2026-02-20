using System;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Input;

namespace DerpLib.ImGui.Docking;

/// <summary>
/// Split node in the dock tree - divides space between two child nodes.
/// </summary>
public class ImDockSplit : ImDockNode
{
    /// <summary>First child (left or top, depending on Direction).</summary>
    public ImDockNode First;

    /// <summary>Second child (right or bottom, depending on Direction).</summary>
    public ImDockNode Second;

    /// <summary>How this split divides space.</summary>
    public ImSplitDirection Direction;

    /// <summary>Position of the divider (0.0 to 1.0).</summary>
    public float SplitRatio = 0.5f;

    /// <summary>Whether the splitter handle is currently being dragged.</summary>
    public bool IsDragging;

    public ImDockSplit(ImDockNode first, ImDockNode second, ImSplitDirection direction, float ratio = 0.5f)
    {
        First = first;
        Second = second;
        Direction = direction;
        SplitRatio = ratio;
    }

    public override bool IsLeaf => false;

    public override bool IsEmpty => First.IsEmpty && Second.IsEmpty;

    /// <summary>
    /// Get the splitter handle rect (the draggable divider between children).
    /// </summary>
    public ImRect SplitterRect
    {
        get
        {
            float size = Im.Style.SplitterSize;
            if (Direction == ImSplitDirection.Horizontal)
            {
                float x = Rect.X + Rect.Width * SplitRatio - size * 0.5f;
                return new ImRect(x, Rect.Y, size, Rect.Height);
            }
            else
            {
                float y = Rect.Y + Rect.Height * SplitRatio - size * 0.5f;
                return new ImRect(Rect.X, y, Rect.Width, size);
            }
        }
    }

    public override void UpdateLayout(ImRect rect)
    {
        Rect = rect;
        float splitterSize = Im.Style.SplitterSize;

        if (Direction == ImSplitDirection.Horizontal)
        {
            float splitX = rect.X + rect.Width * SplitRatio;
            var firstRect = new ImRect(
                rect.X,
                rect.Y,
                splitX - rect.X - splitterSize * 0.5f,
                rect.Height);
            var secondRect = new ImRect(
                splitX + splitterSize * 0.5f,
                rect.Y,
                rect.Right - splitX - splitterSize * 0.5f,
                rect.Height);
            First.UpdateLayout(firstRect);
            Second.UpdateLayout(secondRect);
        }
        else // Vertical
        {
            float splitY = rect.Y + rect.Height * SplitRatio;
            var firstRect = new ImRect(
                rect.X,
                rect.Y,
                rect.Width,
                splitY - rect.Y - splitterSize * 0.5f);
            var secondRect = new ImRect(
                rect.X,
                splitY + splitterSize * 0.5f,
                rect.Width,
                rect.Bottom - splitY - splitterSize * 0.5f);
            First.UpdateLayout(firstRect);
            Second.UpdateLayout(secondRect);
        }
    }

    public override void Draw()
    {
        // Draw children first
        First.Draw();
        Second.Draw();

        // Draw splitter handle
        var splitterRect = SplitterRect;
        GetMouseState(out var mousePosScreen, out bool mouseDown, out bool mousePressed);
        bool hovered = splitterRect.Contains(mousePosScreen);

        // Handle splitter drag
        if (hovered && mousePressed)
            IsDragging = true;

        if (IsDragging)
        {
            if (mouseDown)
            {
                // Update split ratio based on mouse position
                if (Direction == ImSplitDirection.Horizontal)
                    SplitRatio = (mousePosScreen.X - Rect.X) / Rect.Width;
                else
                    SplitRatio = (mousePosScreen.Y - Rect.Y) / Rect.Height;

                // Clamp to prevent collapse
                SplitRatio = Math.Clamp(SplitRatio, 0.1f, 0.9f);

                // Re-layout with new ratio
                UpdateLayout(Rect);
            }
            else
            {
                IsDragging = false;
            }
        }

        // Draw splitter visual
        var style = Im.Style;
        uint color = (hovered || IsDragging) ? style.Hover : style.Border;
        var viewport = Im.CurrentViewport ?? throw new InvalidOperationException("No current viewport set");
        var previousLayer = viewport.CurrentLayer;
        var dockController = Im.Context.DockController;
        int oldSortKey = viewport.GetDrawList(DerpLib.ImGui.Rendering.ImDrawLayer.FloatingWindows).GetSortKey();
        Im.SetDrawLayer(DerpLib.ImGui.Rendering.ImDrawLayer.FloatingWindows);
        viewport.GetDrawList(DerpLib.ImGui.Rendering.ImDrawLayer.FloatingWindows).SetSortKey(dockController.DockOverlaySortKey);
        Im.DrawRect(splitterRect.X, splitterRect.Y, splitterRect.Width, splitterRect.Height, color);
        viewport.GetDrawList(DerpLib.ImGui.Rendering.ImDrawLayer.FloatingWindows).SetSortKey(oldSortKey);
        Im.SetDrawLayer(previousLayer);
    }

    public override ImDockLeaf? FindLeafWithWindow(int windowId)
    {
        return First.FindLeafWithWindow(windowId) ?? Second.FindLeafWithWindow(windowId);
    }

    public override ImDockNode? FindNode(int nodeId)
    {
        if (Id == nodeId)
            return this;
        return First.FindNode(nodeId) ?? Second.FindNode(nodeId);
    }

    private static void GetMouseState(out System.Numerics.Vector2 mousePosScreen, out bool mouseDown, out bool mousePressed)
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
