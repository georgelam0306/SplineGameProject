using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Core;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.Sdf;
using Pooled.Runtime;
using Property;
using Property.Runtime;

namespace Derp.UI;

internal sealed class CanvasInteractions
{
    private readonly UiWorkspace _workspace;

    private bool _isPanning;
    private Vector2 _panLastMouseViewport;

    private bool _isCreatingFrame;
    private Vector2 _createFrameStartWorld;
    private Vector2 _createFrameCurrentWorld;

    private bool _isCreatingShape;
    private ShapeKind _createShapeKind;
    private int _createShapeParentFrameId;
    private Vector2 _createShapeStartWorld;
    private Vector2 _createShapeCurrentWorld;

    private bool _isCreatingText;
    private int _createTextParentFrameId;
    private Vector2 _createTextStartWorld;
    private Vector2 _createTextCurrentWorld;

    private bool _isCreatingPolygon;
    private int _createPolygonParentFrameId;
    private readonly List<Vector2> _createPolygonPointsWorld = new(capacity: 64);
    private readonly List<Vector2> _createPolygonTangentsInLocal = new(capacity: 64);
    private readonly List<Vector2> _createPolygonTangentsOutLocal = new(capacity: 64);
    private readonly List<int> _createPolygonVertexKind = new(capacity: 64);
    private bool _isDraggingTangent;
    private int _dragTangentVertexIndex = -1;

    internal CanvasInteractions(UiWorkspace workspace)
    {
        _workspace = workspace;
    }

    internal bool IsCreatingFrame => _isCreatingFrame;

    internal bool IsCreatingShape => _isCreatingShape;

    internal bool IsCreatingText => _isCreatingText;

    internal bool IsCreatingPolygon => _isCreatingPolygon;

    internal void CancelCreationInteractions()
    {
        _isCreatingFrame = false;
        _isCreatingShape = false;
        _isCreatingText = false;
        _isCreatingPolygon = false;
        _createPolygonPointsWorld.Clear();
        _createPolygonTangentsInLocal.Clear();
        _createPolygonTangentsOutLocal.Clear();
        _createPolygonVertexKind.Clear();
        _createPolygonParentFrameId = 0;
        _isDraggingTangent = false;
        _dragTangentVertexIndex = -1;
    }

    internal void CancelPolygonCreationIfNotActiveTool(UiWorkspace.CanvasTool activeTool)
    {
        if (activeTool == UiWorkspace.CanvasTool.Pen)
        {
            return;
        }

        _isCreatingPolygon = false;
        _createPolygonPointsWorld.Clear();
        _createPolygonTangentsInLocal.Clear();
        _createPolygonTangentsOutLocal.Clear();
        _createPolygonVertexKind.Clear();
        _createPolygonParentFrameId = 0;
        _isDraggingTangent = false;
        _dragTangentVertexIndex = -1;
    }

    internal void HandleZoom(DerpLib.ImGui.Input.ImInput input, Vector2 canvasOrigin, Vector2 mouseCanvas)
    {
        if (!input.KeyCtrl)
        {
            return;
        }

        float scrollDelta = input.ScrollDelta;
        if (scrollDelta == 0f)
        {
            return;
        }

        float previousZoom = _workspace.Zoom;
        float zoomMultiplier = scrollDelta > 0f ? 1.1f : (1f / 1.1f);
        float newZoom = previousZoom * zoomMultiplier;
        newZoom = Math.Clamp(newZoom, 0.01f, 8f);

        if (Math.Abs(newZoom - previousZoom) < 0.00001f)
        {
            return;
        }

        Vector2 worldUnderMouse = _workspace.CanvasToWorld(mouseCanvas, canvasOrigin, previousZoom);
        _workspace.Zoom = newZoom;
        _workspace.PanWorld = worldUnderMouse - (mouseCanvas - canvasOrigin) / _workspace.Zoom;
    }

    internal void HandlePrefabInstanceCreation(DerpLib.ImGui.Input.ImInput input, Vector2 canvasOrigin, Vector2 mouseCanvas)
    {
        if (!input.MousePressed)
        {
            return;
        }

        uint sourcePrefabStableId = _workspace._prefabInstanceSourcePrefabStableId;
        if (sourcePrefabStableId == 0)
        {
            _workspace.ShowToast("Select a source prefab");
            return;
        }

        if (!_workspace.HasActivePrefab)
        {
            _workspace.ShowToast("No active prefab");
            return;
        }

        Vector2 mouseWorld = _workspace.CanvasToWorld(mouseCanvas, canvasOrigin);
        uint created = _workspace.Commands.CreatePrefabInstance(mouseWorld, sourcePrefabStableId);
        if (created == 0)
        {
            _workspace.ShowToast("Failed to create instance");
        }
    }

    internal void HandlePan(DerpLib.ImGui.Input.ImInput input, bool canvasHovered)
    {
        if (!_isPanning)
        {
            if (!canvasHovered)
            {
                return;
            }

            if (!input.MouseMiddlePressed)
            {
                return;
            }

            _isPanning = true;
            _panLastMouseViewport = Im.MousePosViewport;
            return;
        }

        if (input.MouseMiddleDown)
        {
            Vector2 mouseViewport = Im.MousePosViewport;
            Vector2 mouseDeltaViewport = mouseViewport - _panLastMouseViewport;
            _panLastMouseViewport = mouseViewport;

            if (_workspace.Zoom > 0.0001f)
            {
                _workspace.PanWorld -= mouseDeltaViewport / _workspace.Zoom;
            }
        }

        if (input.MouseMiddleReleased)
        {
            _isPanning = false;
        }
    }

    internal void HandleFrameCreation(DerpLib.ImGui.Input.ImInput input, Vector2 canvasOrigin, Vector2 mouseCanvas)
    {
        if (!_isCreatingFrame)
        {
            if (!input.MousePressed)
            {
                return;
            }

            _isCreatingFrame = true;
            _createFrameStartWorld = _workspace.CanvasToWorld(mouseCanvas, canvasOrigin);
            _createFrameCurrentWorld = _createFrameStartWorld;
            _workspace.ClearSelection();
            return;
        }

        if (input.MouseDown)
        {
            _createFrameCurrentWorld = _workspace.CanvasToWorld(mouseCanvas, canvasOrigin);
        }

        if (!input.MouseReleased)
        {
            return;
        }

        var rectWorld = UiWorkspace.MakeRectFromTwoPoints(_createFrameStartWorld, _createFrameCurrentWorld);

        const float minSize = 20f;
        if (rectWorld.Width >= minSize && rectWorld.Height >= minSize)
        {
            int frameId = _workspace.Commands.CreatePrefab(rectWorld);
            if (frameId != 0)
            {
                _workspace.ClearSelection();
                _workspace._selectedPrefabIndex = _workspace._prefabs.Count - 1;
            }
        }

        _isCreatingFrame = false;
    }

    internal void HandleShapeCreation(DerpLib.ImGui.Input.ImInput input, Vector2 canvasOrigin, Vector2 mouseCanvas, ShapeKind kind)
    {
        if (!_isCreatingShape)
        {
            if (!input.MousePressed)
            {
                return;
            }

            Vector2 startWorld = _workspace.CanvasToWorld(mouseCanvas, canvasOrigin);
            int parentFrameIndex = _workspace.FindTopmostPrefabIndex(startWorld);
            if (parentFrameIndex < 0)
            {
                parentFrameIndex = _workspace._selectedPrefabIndex;
            }

            if (parentFrameIndex < 0 || parentFrameIndex >= _workspace._prefabs.Count)
            {
                _workspace.ShowToast("No active prefab");
                return;
            }

            _isCreatingShape = true;
            _createShapeKind = kind;
            _createShapeParentFrameId = _workspace._prefabs[parentFrameIndex].Id;
            _createShapeStartWorld = startWorld;
            _createShapeCurrentWorld = _createShapeStartWorld;
            _workspace.ClearSelection();
            return;
        }

        if (_createShapeKind != kind)
        {
            return;
        }

        if (input.MouseDown)
        {
            _createShapeCurrentWorld = _workspace.CanvasToWorld(mouseCanvas, canvasOrigin);
        }

        if (!input.MouseReleased)
        {
            return;
        }

        var rectWorld = UiWorkspace.MakeRectFromTwoPoints(_createShapeStartWorld, _createShapeCurrentWorld);

        const float minSize = 6f;
        if (rectWorld.Width >= minSize && rectWorld.Height >= minSize)
        {
            int shapeId = _workspace.Commands.CreateShape(kind, rectWorld, _createShapeParentFrameId);
            if (shapeId != 0)
            {
                if (UiWorkspace.TryGetEntityById(_workspace._shapeEntityById, shapeId, out EntityId shapeEntity))
                {
                    _workspace.SelectSingleEntity(shapeEntity);
                }
            }
        }

        _isCreatingShape = false;
    }

    internal void HandleTextCreation(DerpLib.ImGui.Input.ImInput input, Vector2 canvasOrigin, Vector2 mouseCanvas)
    {
        if (!_isCreatingText)
        {
            if (!input.MousePressed)
            {
                return;
            }

            Vector2 startWorld = _workspace.CanvasToWorld(mouseCanvas, canvasOrigin);
            int parentFrameIndex = _workspace.FindTopmostPrefabIndex(startWorld);
            if (parentFrameIndex < 0)
            {
                parentFrameIndex = _workspace._selectedPrefabIndex;
            }

            if (parentFrameIndex < 0 || parentFrameIndex >= _workspace._prefabs.Count)
            {
                _workspace.ShowToast("No active prefab");
                return;
            }

            _isCreatingText = true;
            _createTextParentFrameId = _workspace._prefabs[parentFrameIndex].Id;
            _createTextStartWorld = startWorld;
            _createTextCurrentWorld = _createTextStartWorld;
            _workspace.ClearSelection();
            return;
        }

        if (input.MouseDown)
        {
            _createTextCurrentWorld = _workspace.CanvasToWorld(mouseCanvas, canvasOrigin);
        }

        if (!input.MouseReleased)
        {
            return;
        }

        var rectWorld = UiWorkspace.MakeRectFromTwoPoints(_createTextStartWorld, _createTextCurrentWorld);

        const float minSize = 6f;
        if (rectWorld.Width >= minSize && rectWorld.Height >= minSize)
        {
            int textId = _workspace.Commands.CreateText(rectWorld, _createTextParentFrameId);
            if (textId != 0)
            {
                if (UiWorkspace.TryGetEntityById(_workspace._textEntityById, textId, out EntityId textEntity))
                {
                    _workspace.SelectSingleEntity(textEntity);
                }
            }
        }

        _isCreatingText = false;
        _createTextParentFrameId = 0;
    }

    internal void HandlePolygonCreation(DerpLib.ImGui.Input.ImInput input, Vector2 canvasOrigin, Vector2 mouseCanvas)
    {
        if (input.KeyEscape || input.MouseRightPressed)
        {
            _isCreatingPolygon = false;
            _createPolygonPointsWorld.Clear();
            _createPolygonTangentsInLocal.Clear();
            _createPolygonTangentsOutLocal.Clear();
            _createPolygonVertexKind.Clear();
            _createPolygonParentFrameId = 0;
            _isDraggingTangent = false;
            _dragTangentVertexIndex = -1;
            return;
        }

        if (_isCreatingPolygon && input.KeyBackspace && _createPolygonPointsWorld.Count > 0)
        {
            _createPolygonPointsWorld.RemoveAt(_createPolygonPointsWorld.Count - 1);
            _createPolygonTangentsInLocal.RemoveAt(_createPolygonTangentsInLocal.Count - 1);
            _createPolygonTangentsOutLocal.RemoveAt(_createPolygonTangentsOutLocal.Count - 1);
            _createPolygonVertexKind.RemoveAt(_createPolygonVertexKind.Count - 1);

            _isDraggingTangent = false;
            _dragTangentVertexIndex = -1;

            if (_createPolygonPointsWorld.Count == 0)
            {
                _isCreatingPolygon = false;
                _createPolygonParentFrameId = 0;
            }
            return;
        }

        Vector2 mouseWorld = _workspace.CanvasToWorld(mouseCanvas, canvasOrigin);

        if (_isCreatingPolygon && _isDraggingTangent)
        {
            UpdateActiveTangentDrag(input, mouseWorld);
        }

        if (!_isCreatingPolygon)
        {
            if (!input.MousePressed)
            {
                return;
            }

            int parentFrameIndex = _workspace.FindTopmostPrefabIndex(mouseWorld);
            if (parentFrameIndex < 0)
            {
                parentFrameIndex = _workspace._selectedPrefabIndex;
            }

            if (parentFrameIndex < 0 || parentFrameIndex >= _workspace._prefabs.Count)
            {
                _workspace.ShowToast("No active prefab");
                return;
            }

            _isCreatingPolygon = true;
            _createPolygonParentFrameId = _workspace._prefabs[parentFrameIndex].Id;
            _createPolygonPointsWorld.Clear();
            _createPolygonTangentsInLocal.Clear();
            _createPolygonTangentsOutLocal.Clear();
            _createPolygonVertexKind.Clear();

            _createPolygonPointsWorld.Add(mouseWorld);
            _createPolygonTangentsInLocal.Add(Vector2.Zero);
            _createPolygonTangentsOutLocal.Add(Vector2.Zero);
            _createPolygonVertexKind.Add((int)PathVertexKind.Corner);

            _isDraggingTangent = true;
            _dragTangentVertexIndex = 0;
            UpdateActiveTangentDrag(input, mouseWorld);

            _workspace.ClearSelection();
            return;
        }

        bool commit = false;
        bool addPoint = false;

        if (input.KeyEnter)
        {
            commit = true;
        }

        if (input.MousePressed)
        {
            if (input.IsDoubleClick)
            {
                commit = true;
            }
            else
            {
                addPoint = true;
            }
        }

        if (commit)
        {
            _workspace.CommitPolygonIfValid();
            return;
        }

        if (!addPoint)
        {
            return;
        }

        if (_createPolygonPointsWorld.Count >= 3)
        {
            Vector2 first = _createPolygonPointsWorld[0];
            float dx = mouseWorld.X - first.X;
            float dy = mouseWorld.Y - first.Y;
            float closeThresholdWorld = 12f / MathF.Max(_workspace.Zoom, 0.0001f);
            if (dx * dx + dy * dy <= closeThresholdWorld * closeThresholdWorld)
            {
                _workspace.CommitPolygonIfValid();
                return;
            }
        }

        int newIndex = _createPolygonPointsWorld.Count;
        _createPolygonPointsWorld.Add(mouseWorld);
        _createPolygonTangentsInLocal.Add(Vector2.Zero);
        _createPolygonTangentsOutLocal.Add(Vector2.Zero);
        _createPolygonVertexKind.Add((int)PathVertexKind.Corner);

        _isDraggingTangent = true;
        _dragTangentVertexIndex = newIndex;
        UpdateActiveTangentDrag(input, mouseWorld);
    }

    internal void DrawPolygonPreview(Vector2 canvasOrigin, Vector2 mouseWorld)
    {
        int pointCount = _createPolygonPointsWorld.Count;
        if (pointCount <= 0)
        {
            return;
        }

        uint stroke = ImStyle.WithAlphaF(Im.Style.Primary, 0.95f);
        uint pointColor = 0xFFFFFFFF;

        Vector2 prevCanvas = Vector2.Zero;
        for (int i = 0; i < pointCount; i++)
        {
            Vector2 pWorld = _createPolygonPointsWorld[i];
            Vector2 pCanvas = new Vector2(
                WorldToCanvasX(pWorld.X, canvasOrigin),
                WorldToCanvasY(pWorld.Y, canvasOrigin));

            if (i > 0)
            {
                int prevIndex = i - 1;
                Vector2 p0 = _createPolygonPointsWorld[prevIndex];
                Vector2 p3 = pWorld;

                Vector2 tOut = _createPolygonVertexKind[prevIndex] == (int)PathVertexKind.Corner ? Vector2.Zero : _createPolygonTangentsOutLocal[prevIndex];
                Vector2 tIn = _createPolygonVertexKind[i] == (int)PathVertexKind.Corner ? Vector2.Zero : _createPolygonTangentsInLocal[i];

                bool hasCurve = (tOut.X != 0f || tOut.Y != 0f) || (tIn.X != 0f || tIn.Y != 0f);
                if (!hasCurve)
                {
                    Im.DrawLine(prevCanvas.X, prevCanvas.Y, pCanvas.X, pCanvas.Y, 2f, stroke);
                }
                else
                {
                    Vector2 p1 = p0 + tOut;
                    Vector2 p2 = p3 + tIn;
                    DrawCubicCurve(canvasOrigin, p0, p1, p2, p3, stroke);
                }
            }

            Im.DrawCircle(pCanvas.X, pCanvas.Y, 3f, pointColor);
            prevCanvas = pCanvas;
        }

        Vector2 mouseCanvas = new Vector2(
            WorldToCanvasX(mouseWorld.X, canvasOrigin),
            WorldToCanvasY(mouseWorld.Y, canvasOrigin));
        Im.DrawLine(prevCanvas.X, prevCanvas.Y, mouseCanvas.X, mouseCanvas.Y, 1.5f, stroke);

        DrawTangentHandles(canvasOrigin, stroke);

        if (pointCount >= 3)
        {
            Vector2 firstWorld = _createPolygonPointsWorld[0];
            Vector2 firstCanvas = new Vector2(
                WorldToCanvasX(firstWorld.X, canvasOrigin),
                WorldToCanvasY(firstWorld.Y, canvasOrigin));
            Im.DrawCircleStroke(firstCanvas.X, firstCanvas.Y, 8f, stroke, 1.2f);
        }
    }

    internal void DrawFramePreview(Vector2 canvasOrigin, ImStyle style)
    {
        var rectWorld = UiWorkspace.MakeRectFromTwoPoints(_createFrameStartWorld, _createFrameCurrentWorld);
        var rectCanvas = WorldRectToCanvas(rectWorld, canvasOrigin);

        uint previewFill = ImStyle.WithAlphaF(style.DockPreview, 0.30f);
        uint previewStroke = ImStyle.WithAlphaF(style.DockPreview, 0.65f);

        Im.DrawRect(rectCanvas.X, rectCanvas.Y, rectCanvas.Width, rectCanvas.Height, previewFill);
        Im.DrawRoundedRectStroke(rectCanvas.X, rectCanvas.Y, rectCanvas.Width, rectCanvas.Height, 0f, previewStroke, 1.5f);
    }

    internal void DrawShapePreview(Vector2 canvasOrigin, ImStyle style)
    {
        if (!TryGetFrameRectForId(_createShapeParentFrameId, out var frameWorldRect))
        {
            return;
        }

        var rectWorld = UiWorkspace.MakeRectFromTwoPoints(_createShapeStartWorld, _createShapeCurrentWorld);
        var rectCanvas = WorldRectToCanvas(rectWorld, canvasOrigin);
        var frameCanvas = WorldRectToCanvas(frameWorldRect, canvasOrigin);

        uint previewFill = ImStyle.WithAlphaF(style.DockPreview, 0.22f);
        uint previewStroke = ImStyle.WithAlphaF(style.DockPreview, 0.75f);

        Im.PushClipRect(frameCanvas);

        if (_createShapeKind == ShapeKind.Rect)
        {
            Im.DrawRect(rectCanvas.X, rectCanvas.Y, rectCanvas.Width, rectCanvas.Height, previewFill);
            Im.DrawRoundedRectStroke(rectCanvas.X, rectCanvas.Y, rectCanvas.Width, rectCanvas.Height, 0f, previewStroke, 1.5f);
        }
        else
        {
            float radius = Math.Min(rectCanvas.Width, rectCanvas.Height) * 0.5f;
            float cx = rectCanvas.X + rectCanvas.Width * 0.5f;
            float cy = rectCanvas.Y + rectCanvas.Height * 0.5f;
            Im.DrawCircle(cx, cy, radius, previewFill);
            Im.DrawCircleStroke(cx, cy, radius, previewStroke, 1.5f);
        }

        Im.PopClipRect();
    }

    internal void DrawTextPreview(Vector2 canvasOrigin, ImStyle style)
    {
        if (!TryGetFrameRectForId(_createTextParentFrameId, out var frameWorldRect))
        {
            return;
        }

        var rectWorld = UiWorkspace.MakeRectFromTwoPoints(_createTextStartWorld, _createTextCurrentWorld);
        var rectCanvas = WorldRectToCanvas(rectWorld, canvasOrigin);
        var frameCanvas = WorldRectToCanvas(frameWorldRect, canvasOrigin);

        uint previewFill = ImStyle.WithAlphaF(style.DockPreview, 0.10f);
        uint previewStroke = ImStyle.WithAlphaF(style.DockPreview, 0.75f);

        Im.PushClipRect(frameCanvas);
        Im.DrawRect(rectCanvas.X, rectCanvas.Y, rectCanvas.Width, rectCanvas.Height, previewFill);
        Im.DrawRoundedRectStroke(rectCanvas.X, rectCanvas.Y, rectCanvas.Width, rectCanvas.Height, 0f, previewStroke, 1.5f);
        Im.PopClipRect();
    }

    internal void DrawCanvasHud(Vector2 canvasOrigin)
    {
        var contentRect = Im.WindowContentRect;
        float hudX = contentRect.X + 12f;
        float hudY = contentRect.Y + 12f;

        Im.Label($"Zoom: {_workspace.Zoom:F2}", hudX, hudY);
        Im.Label($"Pan: ({_workspace.PanWorld.X:F0}, {_workspace.PanWorld.Y:F0})", hudX, hudY + 18f);

        if (_workspace.ActiveTool == UiWorkspace.CanvasTool.Pen)
        {
            if (_isCreatingPolygon)
            {
                Im.LabelText("Pen: click to add points, click+drag for tangents, double-click/Enter to commit, Backspace removes, Esc cancels", hudX, hudY + 38f);
            }
            else
            {
                Im.LabelText("Pen: click to start polygon", hudX, hudY + 38f);
            }
        }

        if (_workspace.ActiveTool == UiWorkspace.CanvasTool.Text)
        {
            Im.LabelText("Text: click+drag to create a text box", hudX, hudY + 38f);
        }
    }

    internal void CommitPolygonIfValid()
    {
        if (!_isCreatingPolygon || _createPolygonParentFrameId == 0)
        {
            _isCreatingPolygon = false;
            _createPolygonPointsWorld.Clear();
            _createPolygonTangentsInLocal.Clear();
            _createPolygonTangentsOutLocal.Clear();
            _createPolygonVertexKind.Clear();
            _createPolygonParentFrameId = 0;
            _isDraggingTangent = false;
            _dragTangentVertexIndex = -1;
            return;
        }

        int pointCount = _createPolygonPointsWorld.Count;
        if (pointCount < 3)
        {
            _isCreatingPolygon = false;
            _createPolygonPointsWorld.Clear();
            _createPolygonTangentsInLocal.Clear();
            _createPolygonTangentsOutLocal.Clear();
            _createPolygonVertexKind.Clear();
            _createPolygonParentFrameId = 0;
            _isDraggingTangent = false;
            _dragTangentVertexIndex = -1;
            return;
        }

        _isDraggingTangent = false;
        _dragTangentVertexIndex = -1;

        ReadOnlySpan<Vector2> pointsWorld = CollectionsMarshal.AsSpan(_createPolygonPointsWorld);
        ReadOnlySpan<Vector2> tangentsInLocal = CollectionsMarshal.AsSpan(_createPolygonTangentsInLocal);
        ReadOnlySpan<Vector2> tangentsOutLocal = CollectionsMarshal.AsSpan(_createPolygonTangentsOutLocal);
        ReadOnlySpan<int> vertexKind = CollectionsMarshal.AsSpan(_createPolygonVertexKind);

        int shapeId = _workspace.Commands.CreatePath(pointsWorld, tangentsInLocal, tangentsOutLocal, vertexKind, _createPolygonParentFrameId);
        if (shapeId != 0)
        {
            if (UiWorkspace.TryGetEntityById(_workspace._shapeEntityById, shapeId, out EntityId shapeEntity))
            {
                _workspace.SelectSingleEntity(shapeEntity);
            }
        }

        _isCreatingPolygon = false;
        _createPolygonPointsWorld.Clear();
        _createPolygonTangentsInLocal.Clear();
        _createPolygonTangentsOutLocal.Clear();
        _createPolygonVertexKind.Clear();
        _createPolygonParentFrameId = 0;
    }

    private void UpdateActiveTangentDrag(DerpLib.ImGui.Input.ImInput input, Vector2 mouseWorld)
    {
        if (_dragTangentVertexIndex < 0 || _dragTangentVertexIndex >= _createPolygonPointsWorld.Count)
        {
            _isDraggingTangent = false;
            _dragTangentVertexIndex = -1;
            return;
        }

        Vector2 vertexWorld = _createPolygonPointsWorld[_dragTangentVertexIndex];
        Vector2 delta = mouseWorld - vertexWorld;

        float thresholdWorld = 3f / MathF.Max(_workspace.Zoom, 0.0001f);
        float threshold2 = thresholdWorld * thresholdWorld;
        float d2 = delta.X * delta.X + delta.Y * delta.Y;

        if (d2 <= threshold2)
        {
            _createPolygonTangentsInLocal[_dragTangentVertexIndex] = Vector2.Zero;
            _createPolygonTangentsOutLocal[_dragTangentVertexIndex] = Vector2.Zero;
            _createPolygonVertexKind[_dragTangentVertexIndex] = (int)PathVertexKind.Corner;
        }
        else
        {
            _createPolygonTangentsOutLocal[_dragTangentVertexIndex] = delta;
            _createPolygonTangentsInLocal[_dragTangentVertexIndex] = -delta;
            _createPolygonVertexKind[_dragTangentVertexIndex] = (int)PathVertexKind.Symmetric;
        }

        if (input.MouseReleased || !input.MouseDown)
        {
            _isDraggingTangent = false;
            _dragTangentVertexIndex = -1;
        }
    }

    private void DrawTangentHandles(Vector2 canvasOrigin, uint stroke)
    {
        uint handleStroke = ImStyle.WithAlphaF(stroke, 0.75f);
        uint handleFill = ImStyle.WithAlphaF(stroke, 0.12f);

        int count = _createPolygonPointsWorld.Count;
        for (int i = 0; i < count; i++)
        {
            if (_createPolygonVertexKind[i] == (int)PathVertexKind.Corner)
            {
                continue;
            }

            Vector2 p = _createPolygonPointsWorld[i];
            Vector2 pCanvas = new Vector2(
                WorldToCanvasX(p.X, canvasOrigin),
                WorldToCanvasY(p.Y, canvasOrigin));

            Vector2 tin = _createPolygonTangentsInLocal[i];
            Vector2 tout = _createPolygonTangentsOutLocal[i];

            Vector2 inWorld = p + tin;
            Vector2 outWorld = p + tout;

            Vector2 inCanvas = new Vector2(
                WorldToCanvasX(inWorld.X, canvasOrigin),
                WorldToCanvasY(inWorld.Y, canvasOrigin));

            Vector2 outCanvas = new Vector2(
                WorldToCanvasX(outWorld.X, canvasOrigin),
                WorldToCanvasY(outWorld.Y, canvasOrigin));

            Im.DrawLine(pCanvas.X, pCanvas.Y, inCanvas.X, inCanvas.Y, 1f, handleStroke);
            Im.DrawLine(pCanvas.X, pCanvas.Y, outCanvas.X, outCanvas.Y, 1f, handleStroke);

            Im.DrawCircle(inCanvas.X, inCanvas.Y, 3f, handleFill);
            Im.DrawCircleStroke(inCanvas.X, inCanvas.Y, 3f, handleStroke, 1f);

            Im.DrawCircle(outCanvas.X, outCanvas.Y, 3f, handleFill);
            Im.DrawCircleStroke(outCanvas.X, outCanvas.Y, 3f, handleStroke, 1f);
        }
    }

    private void DrawCubicCurve(Vector2 canvasOrigin, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, uint stroke)
    {
        const int steps = 16;
        Vector2 prevWorld = p0;
        Vector2 prevCanvas = new Vector2(
            WorldToCanvasX(prevWorld.X, canvasOrigin),
            WorldToCanvasY(prevWorld.Y, canvasOrigin));

        for (int step = 1; step <= steps; step++)
        {
            float t = step / (float)steps;
            Vector2 pointWorld = EvalCubicBezier(p0, p1, p2, p3, t);
            Vector2 pointCanvas = new Vector2(
                WorldToCanvasX(pointWorld.X, canvasOrigin),
                WorldToCanvasY(pointWorld.Y, canvasOrigin));
            Im.DrawLine(prevCanvas.X, prevCanvas.Y, pointCanvas.X, pointCanvas.Y, 2f, stroke);
            prevCanvas = pointCanvas;
        }
    }

    private static Vector2 EvalCubicBezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float u = 1f - t;
        float uu = u * u;
        float tt = t * t;
        float uuu = uu * u;
        float ttt = tt * t;

        Vector2 p = uuu * p0;
        p += (3f * uu * t) * p1;
        p += (3f * u * tt) * p2;
        p += ttt * p3;
        return p;
    }

    private bool TryGetFrameRectForId(int frameId, out ImRect rectWorld)
    {
        for (int i = 0; i < _workspace._prefabs.Count; i++)
        {
            if (_workspace._prefabs[i].Id == frameId)
            {
                rectWorld = _workspace._prefabs[i].RectWorld;
                return true;
            }
        }

        rectWorld = ImRect.Zero;
        return false;
    }

    private float WorldToCanvasX(float worldX, Vector2 canvasOrigin)
    {
        return (worldX - _workspace.PanWorld.X) * _workspace.Zoom + canvasOrigin.X;
    }

    private float WorldToCanvasY(float worldY, Vector2 canvasOrigin)
    {
        return (worldY - _workspace.PanWorld.Y) * _workspace.Zoom + canvasOrigin.Y;
    }

    private ImRect WorldRectToCanvas(ImRect worldRect, Vector2 canvasOrigin)
    {
        float x = WorldToCanvasX(worldRect.X, canvasOrigin);
        float y = WorldToCanvasY(worldRect.Y, canvasOrigin);
        float w = worldRect.Width * _workspace.Zoom;
        float h = worldRect.Height * _workspace.Zoom;
        return new ImRect(x, y, w, h);
    }
}
