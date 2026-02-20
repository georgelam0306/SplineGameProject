using System.Numerics;
using DerpLib.ImGui.Input;

namespace Derp.UI;

internal static class PaintFillGradientGizmo
{
    public static void DrawHover(UiWorkspace workspace, int frameId, Vector2 canvasOrigin, in PropertyGizmoRequest request)
    {
        workspace.DrawActivePaintGradientGizmo(frameId, canvasOrigin);
    }

    public static void DrawActive(UiWorkspace workspace, int frameId, Vector2 canvasOrigin, in PropertyGizmoRequest request)
    {
        workspace.DrawActivePaintGradientGizmo(frameId, canvasOrigin);
    }

    public static bool HandleInput(UiWorkspace workspace, in ImInput input, Vector2 canvasOrigin, Vector2 mouseCanvas, in PropertyGizmoRequest request)
    {
        return workspace.HandlePaintGradientGizmoInput(input, canvasOrigin, mouseCanvas);
    }
}

