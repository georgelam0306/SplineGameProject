using System;
using System.Numerics;
using DerpLib.ImGui.Core;

namespace DerpLib.ImGui.Widgets;

[Flags]
public enum ImPopoverCloseButtons : byte
{
    None = 0,
    Left = 1 << 0,
    Right = 1 << 1,
    Middle = 1 << 2,
}

public ref struct ImPopoverOverlayScope
{
    private readonly bool _active;

    internal ImPopoverOverlayScope(bool active)
    {
        _active = active;
    }

    public void Dispose()
    {
        if (_active)
        {
            Im.Context.PopOverlayScope();
        }
    }
}

public static class ImPopover
{
    public static void EnterOverlayScope(ImRect captureRectViewport)
    {
        var ctx = Im.Context;
        ctx.AddOverlayCaptureRect(captureRectViewport);
        ctx.PushOverlayScope();
    }

    public static void EnterOverlayScopeLocal(ImRect captureRectLocal)
    {
        EnterOverlayScope(Im.TransformRectLocalToViewportAabb(captureRectLocal));
    }

    public static void EnterOverlayScope()
    {
        Im.Context.PushOverlayScope();
    }

    public static void ExitOverlayScope()
    {
        Im.Context.PopOverlayScope();
    }

    public static ImPopoverOverlayScope PushOverlayScope(ImRect captureRectViewport)
    {
        var ctx = Im.Context;
        ctx.AddOverlayCaptureRect(captureRectViewport);
        ctx.PushOverlayScope();
        return new ImPopoverOverlayScope(true);
    }

    public static ImPopoverOverlayScope PushOverlayScopeLocal(ImRect captureRectLocal)
    {
        return PushOverlayScope(Im.TransformRectLocalToViewportAabb(captureRectLocal));
    }

    public static ImPopoverOverlayScope PushOverlayScope()
    {
        Im.Context.PushOverlayScope();
        return new ImPopoverOverlayScope(true);
    }

    public static void AddCaptureRectLocal(ImRect captureRectLocal)
    {
        Im.Context.AddOverlayCaptureRect(Im.TransformRectLocalToViewportAabb(captureRectLocal));
    }

    public static void AddCaptureRect(ImRect captureRectViewport)
    {
        Im.Context.AddOverlayCaptureRect(captureRectViewport);
    }

    public static bool ShouldClose(
        int openedFrame,
        bool closeOnEscape,
        ImPopoverCloseButtons closeOnOutsideButtons,
        bool consumeCloseClick,
        bool requireNoMouseOwner,
        bool useViewportMouseCoordinates,
        ImRect insideRect)
    {
        if (closeOnEscape && Im.Context.Input.KeyEscape)
        {
            return true;
        }

        if (Im.Context.FrameCount == openedFrame)
        {
            return false;
        }

        if (!TryGetCloseClick(closeOnOutsideButtons, requireNoMouseOwner, out bool consumeLeft, out bool consumeRight, out bool consumeMiddle))
        {
            return false;
        }

        Vector2 mousePosition = useViewportMouseCoordinates ? Im.MousePosViewport : Im.MousePos;
        if (insideRect.Contains(mousePosition))
        {
            return false;
        }

        ConsumeCloseClickIfRequested(consumeCloseClick, consumeLeft, consumeRight, consumeMiddle);
        return true;
    }

    public static bool ShouldClose(
        int openedFrame,
        bool closeOnEscape,
        ImPopoverCloseButtons closeOnOutsideButtons,
        bool consumeCloseClick,
        bool requireNoMouseOwner,
        bool useViewportMouseCoordinates,
        ImRect[] insideRects,
        int insideRectCount)
    {
        if (closeOnEscape && Im.Context.Input.KeyEscape)
        {
            return true;
        }

        if (Im.Context.FrameCount == openedFrame)
        {
            return false;
        }

        if (!TryGetCloseClick(closeOnOutsideButtons, requireNoMouseOwner, out bool consumeLeft, out bool consumeRight, out bool consumeMiddle))
        {
            return false;
        }

        Vector2 mousePosition = useViewportMouseCoordinates ? Im.MousePosViewport : Im.MousePos;
        int clampedCount = Math.Clamp(insideRectCount, 0, insideRects.Length);
        for (int index = 0; index < clampedCount; index++)
        {
            if (insideRects[index].Contains(mousePosition))
            {
                return false;
            }
        }

        ConsumeCloseClickIfRequested(consumeCloseClick, consumeLeft, consumeRight, consumeMiddle);
        return true;
    }

    private static bool TryGetCloseClick(
        ImPopoverCloseButtons closeOnOutsideButtons,
        bool requireNoMouseOwner,
        out bool consumeLeft,
        out bool consumeRight,
        out bool consumeMiddle)
    {
        consumeLeft = false;
        consumeRight = false;
        consumeMiddle = false;

        if (closeOnOutsideButtons == ImPopoverCloseButtons.None)
        {
            return false;
        }

        var ctx = Im.Context;
        if (requireNoMouseOwner &&
            (ctx.MouseDownOwnerLeft != 0 ||
             ctx.MouseDownOwnerRight != 0 ||
             ctx.MouseDownOwnerMiddle != 0))
        {
            return false;
        }

        if ((closeOnOutsideButtons & ImPopoverCloseButtons.Left) != 0 && ctx.Input.MousePressed)
        {
            consumeLeft = true;
        }
        if ((closeOnOutsideButtons & ImPopoverCloseButtons.Right) != 0 && ctx.Input.MouseRightPressed)
        {
            consumeRight = true;
        }
        if ((closeOnOutsideButtons & ImPopoverCloseButtons.Middle) != 0 && ctx.Input.MouseMiddlePressed)
        {
            consumeMiddle = true;
        }

        return consumeLeft || consumeRight || consumeMiddle;
    }

    private static void ConsumeCloseClickIfRequested(
        bool consumeCloseClick,
        bool consumeLeft,
        bool consumeRight,
        bool consumeMiddle)
    {
        if (!consumeCloseClick)
        {
            return;
        }

        var ctx = Im.Context;
        if (consumeLeft)
        {
            ctx.ConsumeMouseLeftPress();
        }
        if (consumeRight)
        {
            ctx.ConsumeMouseRightPress();
        }
        if (consumeMiddle)
        {
            if (ctx.CurrentViewport != null)
            {
                ctx.CurrentViewport.Input.MouseMiddlePressed = false;
            }
        }
    }
}
