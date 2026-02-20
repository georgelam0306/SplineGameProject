using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Core;
using Pooled.Runtime;
using Property;
using Property.Runtime;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;
using DerpLib.ImGui.Windows;
using DerpLib.ImGui.Widgets;
using DerpLib.Rendering;
using DerpLib.Sdf;
using static Derp.UI.UiColor32;
using static Derp.UI.UiFillGradient;

namespace Derp.UI;

public sealed partial class UiWorkspace
{
    public void DrawCanvas()
    {
        if (IsRuntimeMode)
        {
            TickRuntimeMode(Im.Context.DeltaTime);
        }
        else
        {
            AnimationEditorWindow.TickPlayback(Im.Context.DeltaTime, this);
            AnimationEditorWindow.ApplyPreviewForCanvasBeforeCanvasDraw(this);
        }
        ResolveLayout();
        CanvasPanel.DrawCanvas(this);
    }

    internal void RebuildCanvasSurfaceFromLastSize()
    {
        if (!_hasLastCanvasSurfaceSize)
        {
            return;
        }

        var rect = new ImRect(0f, 0f, _lastCanvasTargetWidth, _lastCanvasTargetHeight);
        BuildCanvasSurface(rect);
    }

    internal void BuildCanvasSurface(ImRect contentRectScreen)
    {
        if (_canvasSurface == null)
        {
            return;
        }

        int targetWidth = (int)MathF.Max(1f, MathF.Round(contentRectScreen.Width));
        int targetHeight = (int)MathF.Max(1f, MathF.Round(contentRectScreen.Height));

        _lastCanvasTargetWidth = targetWidth;
        _lastCanvasTargetHeight = targetHeight;
        _hasLastCanvasSurfaceSize = true;

        _canvasSurface.BeginFrame(targetWidth, targetHeight);
        var draw = new CanvasSdfDrawList(_canvasSurface.Buffer);

        draw.PushClipRect(0f, 0f, targetWidth, targetHeight);
        draw.DrawRect(0f, 0f, targetWidth, targetHeight, Im.Style.Background);

        var canvasRectLocal = new ImRect(0f, 0f, targetWidth, targetHeight);
        var canvasOriginLocal = new Vector2(targetWidth * 0.5f, targetHeight * 0.5f);

        if (_prefabs.Count > 0)
        {
            BuildGrid(draw, canvasRectLocal, canvasOriginLocal);
        }
        BuildPrefabsAndContentsEcs(draw, canvasOriginLocal);
        draw.PopClipRect();
    }

    private void BuildGrid(CanvasSdfDrawList draw, ImRect contentRect, Vector2 canvasOrigin)
    {
        float zoom = Zoom;
        if (zoom <= 0.0001f)
        {
            return;
        }

        float gridWorld = 100f;
        float gridCanvas = gridWorld * zoom;
        while (gridCanvas < 40f)
        {
            gridWorld *= 2f;
            gridCanvas = gridWorld * zoom;
        }

        uint gridColor = ImStyle.WithAlphaF(Im.Style.Border, 0.20f);

        Vector2 worldMin = CanvasToWorld(new Vector2(contentRect.X, contentRect.Y), canvasOrigin);
        Vector2 worldMax = CanvasToWorld(new Vector2(contentRect.Right, contentRect.Bottom), canvasOrigin);

        if (worldMin.X > worldMax.X)
        {
            float temp = worldMin.X;
            worldMin.X = worldMax.X;
            worldMax.X = temp;
        }

        if (worldMin.Y > worldMax.Y)
        {
            float temp = worldMin.Y;
            worldMin.Y = worldMax.Y;
            worldMax.Y = temp;
        }

        int minX = (int)MathF.Floor(worldMin.X / gridWorld) - 1;
        int maxX = (int)MathF.Floor(worldMax.X / gridWorld) + 1;
        int minY = (int)MathF.Floor(worldMin.Y / gridWorld) - 1;
        int maxY = (int)MathF.Floor(worldMax.Y / gridWorld) + 1;

        for (int xIndex = minX; xIndex <= maxX; xIndex++)
        {
            float xWorld = xIndex * gridWorld;
            float xCanvas = WorldToCanvasX(xWorld, canvasOrigin);
            draw.DrawLine(xCanvas, contentRect.Y, xCanvas, contentRect.Bottom, 1f, gridColor);
        }

        for (int yIndex = minY; yIndex <= maxY; yIndex++)
        {
            float yWorld = yIndex * gridWorld;
            float yCanvas = WorldToCanvasY(yWorld, canvasOrigin);
            draw.DrawLine(contentRect.X, yCanvas, contentRect.Right, yCanvas, 1f, gridColor);
        }

        uint axisColor = ImStyle.WithAlphaF(Im.Style.Border, 0.45f);
        float originX = WorldToCanvasX(0f, canvasOrigin);
        float originY = WorldToCanvasY(0f, canvasOrigin);
        draw.DrawLine(originX, contentRect.Y, originX, contentRect.Bottom, 1.5f, axisColor);
        draw.DrawLine(contentRect.X, originY, contentRect.Right, originY, 1.5f, axisColor);
    }

}
