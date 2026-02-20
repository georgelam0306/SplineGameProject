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
    internal void HandleCanvasInteractions(DerpLib.ImGui.Input.ImInput input, bool canvasHovered, Vector2 canvasOrigin, Vector2 mouseCanvas)
    {
        if (canvasHovered)
        {
            _lastCanvasMouseWorld = CanvasToWorld(mouseCanvas, canvasOrigin);
            _hasLastCanvasMouseWorld = true;
        }

        if (_isEditingPath)
        {
            if (input.KeyEscape)
            {
                ExitPathEditMode(commitChanges: false);
                return;
            }

            if (input.KeyEnter)
            {
                ExitPathEditMode(commitChanges: true);
                return;
            }
        }
        else if (input.KeyEnter && ActiveTool == CanvasTool.Select)
        {
            if (TryEnterPathEditMode())
            {
                return;
            }
        }

        if (_inlineTextEditActive)
        {
            HandleCanvasInlineTextEditorInput(input, canvasOrigin, mouseCanvas);
            return;
        }

        // Allow marquee selection to complete even if the mouse leaves the canvas region.
        if (_marqueeState != MarqueeState.None)
        {
            HandleSelectionAndDrag(input, canvasOrigin, mouseCanvas);
            return;
        }

        if (!canvasHovered)
        {
            if (input.MouseReleased)
            {
                CancelTransientInteractions();
            }
            return;
        }

        _canvasInteractions.CancelPolygonCreationIfNotActiveTool(ActiveTool);

        if (ActiveTool == CanvasTool.Prefab)
        {
            HandleFrameCreation(input, canvasOrigin, mouseCanvas);
            return;
        }

        if (ActiveTool == CanvasTool.Instance)
        {
            _canvasInteractions.HandlePrefabInstanceCreation(input, canvasOrigin, mouseCanvas);
            return;
        }

        if (ActiveTool == CanvasTool.Rect)
        {
            HandleShapeCreation(input, canvasOrigin, mouseCanvas, ShapeKind.Rect);
            return;
        }

        if (ActiveTool == CanvasTool.Circle)
        {
            HandleShapeCreation(input, canvasOrigin, mouseCanvas, ShapeKind.Circle);
            return;
        }

        if (ActiveTool == CanvasTool.Text)
        {
            if (input.MousePressed && input.IsDoubleClick)
            {
                Vector2 mouseWorld = CanvasToWorld(mouseCanvas, canvasOrigin);
                EntityId doubleClickPrefabEntity = FindTopmostPrefabEntity(mouseWorld);
                EntityId doubleClickNodeEntity = !doubleClickPrefabEntity.IsNull
                    ? FindTopmostShapeEntityInPrefab(doubleClickPrefabEntity, mouseWorld, includeGroupedShapes: true)
                    : EntityId.Null;

                if (!doubleClickNodeEntity.IsNull && _world.GetNodeType(doubleClickNodeEntity) == UiNodeType.Text)
                {
                    SelectSingleEntity(doubleClickNodeEntity);
                    BeginCanvasInlineTextEdit(doubleClickNodeEntity);
                    return;
                }
            }

            HandleTextCreation(input, canvasOrigin, mouseCanvas);
            return;
        }

        if (ActiveTool == CanvasTool.Pen)
        {
            HandlePolygonCreation(input, canvasOrigin, mouseCanvas);
            return;
        }

        if (_isEditingPath)
        {
            if (_selectedEntities.Count != 1 || _selectedEntities[0].Value != _editingPathEntity.Value)
            {
                ExitPathEditMode(commitChanges: true);
                return;
            }

            HandlePathEditInteractions(input, canvasOrigin, mouseCanvas);
            return;
        }

        if (IsRuntimeMode && _runtimeConsumedPrimaryThisFrame)
        {
            return;
        }

        HandleSelectionAndDrag(input, canvasOrigin, mouseCanvas);
    }

    private void CancelTransientInteractions()
    {
        _canvasInteractions.CancelCreationInteractions();
        _marqueeState = MarqueeState.None;
        _marqueePrefabEntity = EntityId.Null;
        _marqueePrefabId = 0;
        CommitCanvasEditCapture();
        _dragKind = DragKind.None;
        _dragPrefabIndex = -1;
        _dragShapeIndex = -1;
        _dragGroupIndex = -1;
        _dragTextIndex = -1;
        _dragEntity = EntityId.Null;
        _resizeHandle = ShapeHandle.None;
    }

    private void ExitPathEditMode(bool commitChanges)
    {
        if (commitChanges)
        {
            Commands.FlushPendingEdits();
        }
        else
        {
            Commands.DiscardPendingEdits();
        }

        if (!commitChanges && _pathEditHasSnapshot && !_editingPathEntity.IsNull)
        {
            if (_world.TryGetComponent(_editingPathEntity, PathComponent.Api.PoolIdConst, out AnyComponentHandle pathAny))
            {
                var pathHandle = new PathComponentHandle(pathAny.Index, pathAny.Generation);
                var view = PathComponent.Api.FromHandle(_propertyWorld, pathHandle);
                if (view.IsAlive)
                {
                    using var edit = view.Edit();

                    int vertexCount = _pathEditSnapshotVertexCount;
                    edit.VertexCount = _pathEditSnapshotVertexCount;
                    edit.PivotLocal = _pathEditSnapshotPivotLocal;

                    Span<Vector2> positionsLocal = edit.PositionLocalSpan();
                    Span<Vector2> tangentsInLocal = edit.TangentInLocalSpan();
                    Span<Vector2> tangentsOutLocal = edit.TangentOutLocalSpan();
                    Span<int> vertexKind = edit.VertexKindSpan();

                    for (int i = 0; i < vertexCount; i++)
                    {
                        positionsLocal[i] = _pathEditSnapshotPositionLocal[i];
                        tangentsInLocal[i] = _pathEditSnapshotTangentInLocal[i];
                        tangentsOutLocal[i] = _pathEditSnapshotTangentOutLocal[i];
                        vertexKind[i] = _pathEditSnapshotVertexKind[i];
                    }
                }
            }
        }

        _isEditingPath = false;
        _editingPathEntity = EntityId.Null;
        _pathEditDragKind = PathEditDragKind.None;
        _pathEditDragVertexIndex = -1;
        _pathEditDragWidgetId = 0;
        _pathEditVertexCount = 0;
        _pathEditHasSnapshot = false;
        _pathEditSnapshotVertexCount = 0;
        _pathEditSnapshotPivotLocal = default;
    }

    private bool TryEnterPathEditMode()
    {
        if (_selectedEntities.Count != 1)
        {
            return false;
        }

        EntityId shapeEntity = _selectedEntities[0];
        if (_world.GetNodeType(shapeEntity) != UiNodeType.Shape)
        {
            return false;
        }

        ShapeKind shapeKind = GetShapeKindEcs(shapeEntity, out _, out _, out PathComponentHandle pathHandle);
        if (shapeKind != ShapeKind.Polygon)
        {
            return false;
        }

        if (pathHandle.IsNull)
        {
            return false;
        }

        var view = PathComponent.Api.FromHandle(_propertyWorld, pathHandle);
        if (!view.IsAlive)
        {
            return false;
        }

        int vertexCount = view.VertexCount;
        if (vertexCount < 3 || vertexCount > PathComponent.MaxVertices)
        {
            return false;
        }

        _pathEditHasSnapshot = true;
        _pathEditSnapshotVertexCount = (byte)vertexCount;
        _pathEditSnapshotPivotLocal = view.PivotLocal;

        ReadOnlySpan<Vector2> positionsLocal = view.PositionLocalReadOnlySpan();
        ReadOnlySpan<Vector2> tangentsInLocal = view.TangentInLocalReadOnlySpan();
        ReadOnlySpan<Vector2> tangentsOutLocal = view.TangentOutLocalReadOnlySpan();
        ReadOnlySpan<int> vertexKind = view.VertexKindReadOnlySpan();
        for (int i = 0; i < vertexCount; i++)
        {
            _pathEditSnapshotPositionLocal[i] = positionsLocal[i];
            _pathEditSnapshotTangentInLocal[i] = tangentsInLocal[i];
            _pathEditSnapshotTangentOutLocal[i] = tangentsOutLocal[i];
            _pathEditSnapshotVertexKind[i] = vertexKind[i];
        }

        _isEditingPath = true;
        _editingPathEntity = shapeEntity;
        _pathEditDragKind = PathEditDragKind.None;
        _pathEditDragVertexIndex = -1;
        _pathEditDragWidgetId = 0;
        _pathEditVertexCount = vertexCount;
        return true;
    }

    internal void TogglePathEditModeFromInspector(EntityId shapeEntity)
    {
        if (shapeEntity.IsNull)
        {
            return;
        }

        if (_isEditingPath)
        {
            bool isSameShape = _editingPathEntity.Value == shapeEntity.Value;
            ExitPathEditMode(commitChanges: true);
            if (isSameShape)
            {
                return;
            }
        }

        SelectSingleEntity(shapeEntity);
        TryEnterPathEditMode();
    }

    internal void TogglePathEditModeFromInspector(int shapeId)
    {
        if (shapeId <= 0)
        {
            return;
        }

        if (!TryGetEntityById(_shapeEntityById, shapeId, out EntityId shapeEntity))
        {
            return;
        }

        TogglePathEditModeFromInspector(shapeEntity);
    }

    internal bool IsEditingPathForEntity(EntityId shapeEntity)
    {
        return _isEditingPath && _editingPathEntity.Value == shapeEntity.Value;
    }

    internal bool IsEditingPathForShape(int shapeId)
    {
        if (shapeId <= 0)
        {
            return false;
        }

        if (!TryGetEntityById(_shapeEntityById, shapeId, out EntityId shapeEntity))
        {
            return false;
        }

        return IsEditingPathForEntity(shapeEntity);
    }

    private void HandlePathEditInteractions(DerpLib.ImGui.Input.ImInput input, Vector2 canvasOrigin, Vector2 mouseCanvas)
    {
        if (!_isEditingPath || _editingPathEntity.IsNull)
        {
            return;
        }

        EntityId shapeEntity = _editingPathEntity;
        if (_world.GetNodeType(shapeEntity) != UiNodeType.Shape)
        {
            ExitPathEditMode(commitChanges: true);
            return;
        }

        if (!TryGetTransformParentWorldTransformEcs(shapeEntity, out WorldTransform parentWorldTransform))
        {
            parentWorldTransform = IdentityWorldTransform;
        }

        if (!TryGetShapeWorldTransformEcs(shapeEntity, parentWorldTransform, out ShapeWorldTransform worldTransform))
        {
            return;
        }

        ShapeKind shapeKind = GetShapeKindEcs(shapeEntity, out _, out _, out PathComponentHandle pathHandle);
        if (shapeKind != ShapeKind.Polygon || pathHandle.IsNull)
        {
            ExitPathEditMode(commitChanges: true);
            return;
        }

        var view = PathComponent.Api.FromHandle(_propertyWorld, pathHandle);
        if (!view.IsAlive)
        {
            ExitPathEditMode(commitChanges: true);
            return;
        }

        int vertexCount = view.VertexCount;
        if (vertexCount < 3 || vertexCount > PathComponent.MaxVertices)
        {
            ExitPathEditMode(commitChanges: true);
            return;
        }

        ReadOnlySpan<Vector2> positionsLocal = view.PositionLocalSpan().Slice(0, vertexCount);
        ReadOnlySpan<Vector2> tangentsInLocal = view.TangentInLocalSpan().Slice(0, vertexCount);
        ReadOnlySpan<Vector2> tangentsOutLocal = view.TangentOutLocalSpan().Slice(0, vertexCount);
        ReadOnlySpan<int> vertexKind = view.VertexKindSpan().Slice(0, vertexCount);

        if (vertexCount != _pathEditVertexCount)
        {
            ExitPathEditMode(commitChanges: true);
            return;
        }

        Vector2 mouseWorld = CanvasToWorld(mouseCanvas, canvasOrigin);

        GetBounds(positionsLocal, out float minLocalX, out float minLocalY, out float maxLocalX, out float maxLocalY);
        Vector2 boundsMinLocal = new Vector2(minLocalX, minLocalY);
        Vector2 boundsSizeLocal = new Vector2(maxLocalX - minLocalX, maxLocalY - minLocalY);

        Vector2 positionWorld = worldTransform.PositionWorld;
        Vector2 anchor = worldTransform.Anchor;
        float rotationRadians = worldTransform.RotationRadians;
        Vector2 pivotLocal = GetPolygonPivotLocalEcs(pathHandle, anchor, boundsMinLocal, boundsSizeLocal);

        Vector2 scale = worldTransform.ScaleWorld;
        float scaleX = scale.X == 0f ? 1f : scale.X;
        float scaleY = scale.Y == 0f ? 1f : scale.Y;

        if (input.MousePressed)
        {
            const float vertexHitRadius = 7f;
            const float tangentHitRadius = 6f;
            float bestDist2 = float.MaxValue;
            PathEditDragKind bestKind = PathEditDragKind.None;
            int bestIndex = -1;

            for (int i = 0; i < vertexCount; i++)
            {
                Vector2 vertexWorld = TransformPolygonPointToWorld(positionWorld, pivotLocal, rotationRadians, scaleX, scaleY, positionsLocal[i]);
                float vertexCanvasX = WorldToCanvasX(vertexWorld.X, canvasOrigin);
                float vertexCanvasY = WorldToCanvasY(vertexWorld.Y, canvasOrigin);

                float dx = mouseCanvas.X - vertexCanvasX;
                float dy = mouseCanvas.Y - vertexCanvasY;
                float d2 = dx * dx + dy * dy;
                if (d2 <= vertexHitRadius * vertexHitRadius && d2 < bestDist2)
                {
                    bestDist2 = d2;
                    bestKind = PathEditDragKind.Vertex;
                    bestIndex = i;
                }

                if (vertexKind[i] != (int)PathVertexKind.Corner || tangentsInLocal[i] != Vector2.Zero || tangentsOutLocal[i] != Vector2.Zero)
                {
                    Vector2 tangentInWorld = TransformPolygonPointToWorld(positionWorld, pivotLocal, rotationRadians, scaleX, scaleY, positionsLocal[i] + tangentsInLocal[i]);
                    float tinX = WorldToCanvasX(tangentInWorld.X, canvasOrigin);
                    float tinY = WorldToCanvasY(tangentInWorld.Y, canvasOrigin);
                    dx = mouseCanvas.X - tinX;
                    dy = mouseCanvas.Y - tinY;
                    d2 = dx * dx + dy * dy;
                    if (d2 <= tangentHitRadius * tangentHitRadius && d2 < bestDist2)
                    {
                        bestDist2 = d2;
                        bestKind = PathEditDragKind.TangentIn;
                        bestIndex = i;
                    }

                    Vector2 tangentOutWorld = TransformPolygonPointToWorld(positionWorld, pivotLocal, rotationRadians, scaleX, scaleY, positionsLocal[i] + tangentsOutLocal[i]);
                    float toutX = WorldToCanvasX(tangentOutWorld.X, canvasOrigin);
                    float toutY = WorldToCanvasY(tangentOutWorld.Y, canvasOrigin);
                    dx = mouseCanvas.X - toutX;
                    dy = mouseCanvas.Y - toutY;
                    d2 = dx * dx + dy * dy;
                    if (d2 <= tangentHitRadius * tangentHitRadius && d2 < bestDist2)
                    {
                        bestDist2 = d2;
                        bestKind = PathEditDragKind.TangentOut;
                        bestIndex = i;
                    }
                }
            }

            if (bestKind != PathEditDragKind.None && bestIndex >= 0)
            {
                PathEditDragKind dragKind = bestKind == PathEditDragKind.Vertex && input.KeyAlt
                    ? PathEditDragKind.TangentOut
                    : bestKind;
                _pathEditDragKind = dragKind;
                _pathEditDragVertexIndex = bestIndex;
                _pathEditDragWidgetId = 9_000_000 + (bestIndex * 8) + (int)dragKind;
            }
        }

        if (_pathEditDragKind == PathEditDragKind.None)
        {
            return;
        }

        if (!input.MouseDown)
        {
            if (input.MouseReleased)
            {
                Commands.NotifyPropertyWidgetState(_pathEditDragWidgetId, isEditing: false);
                _pathEditDragKind = PathEditDragKind.None;
                _pathEditDragVertexIndex = -1;
                _pathEditDragWidgetId = 0;
            }
            return;
        }

        int vertexIndex = _pathEditDragVertexIndex;
        if ((uint)vertexIndex >= (uint)vertexCount)
        {
            return;
        }

        AnyComponentHandle component = PathComponentProperties.ToAnyHandle(pathHandle);

        if (_pathEditDragKind == PathEditDragKind.Vertex)
        {
            Vector2 newLocal = TransformWorldToPolygonPointLocal(positionWorld, pivotLocal, rotationRadians, scaleX, scaleY, mouseWorld);
            var positionSlot = new PropertySlot(
                component,
                ushort.MaxValue,
                ComputePathComponentArrayElementPropertyId("PositionLocal", vertexIndex),
                PropertyKind.Vec2);

            Commands.SetPropertyValue(_pathEditDragWidgetId, isEditing: true, shapeEntity, positionSlot, PropertyValue.FromVec2(newLocal));
            return;
        }

        Vector2 vertexLocal = positionsLocal[vertexIndex];
        Vector2 controlLocal = TransformWorldToPolygonPointLocal(positionWorld, pivotLocal, rotationRadians, scaleX, scaleY, mouseWorld);
        Vector2 newTangentLocal = controlLocal - vertexLocal;

        bool editOut = _pathEditDragKind == PathEditDragKind.TangentOut;
        bool breakTangents = input.KeyShift;

        Vector2 tangentInLocal = tangentsInLocal[vertexIndex];
        Vector2 tangentOutLocal = tangentsOutLocal[vertexIndex];

        Vector2 updatedTangentInLocal = tangentInLocal;
        Vector2 updatedTangentOutLocal = tangentOutLocal;

        if (editOut)
        {
            updatedTangentOutLocal = newTangentLocal;
            if (!breakTangents)
            {
                updatedTangentInLocal = -newTangentLocal;
            }
        }
        else
        {
            updatedTangentInLocal = newTangentLocal;
            if (!breakTangents)
            {
                updatedTangentOutLocal = -newTangentLocal;
            }
        }

        int newKind;
        if (updatedTangentInLocal == Vector2.Zero && updatedTangentOutLocal == Vector2.Zero)
        {
            newKind = (int)PathVertexKind.Corner;
        }
        else
        {
            newKind = breakTangents ? (int)PathVertexKind.Smooth : (int)PathVertexKind.Symmetric;
        }

        var tangentInSlot = new PropertySlot(component, ushort.MaxValue, ComputePathComponentArrayElementPropertyId("TangentInLocal", vertexIndex), PropertyKind.Vec2);
        var tangentOutSlot = new PropertySlot(component, ushort.MaxValue, ComputePathComponentArrayElementPropertyId("TangentOutLocal", vertexIndex), PropertyKind.Vec2);
        var kindSlot = new PropertySlot(component, ushort.MaxValue, ComputePathComponentArrayElementPropertyId("VertexKind", vertexIndex), PropertyKind.Int);

        Commands.SetPropertyBatch3(
            _pathEditDragWidgetId,
            isEditing: true,
            shapeEntity,
            tangentInSlot,
            PropertyValue.FromVec2(updatedTangentInLocal),
            tangentOutSlot,
            PropertyValue.FromVec2(updatedTangentOutLocal),
            kindSlot,
            PropertyValue.FromInt(newKind));
    }

    private static Vector2 TransformWorldToPolygonPointLocal(
        Vector2 positionWorld,
        Vector2 pivotLocal,
        float rotationRadians,
        float scaleX,
        float scaleY,
        Vector2 pointWorld)
    {
        Vector2 relWorld = new Vector2(pointWorld.X - positionWorld.X, pointWorld.Y - positionWorld.Y);
        Vector2 unrotated = RotateVector(relWorld, -rotationRadians);

        float invScaleX = scaleX == 0f ? 1f : 1f / scaleX;
        float invScaleY = scaleY == 0f ? 1f : 1f / scaleY;
        Vector2 unscaled = new Vector2(unrotated.X * invScaleX, unrotated.Y * invScaleY);

        return new Vector2(pivotLocal.X + unscaled.X, pivotLocal.Y + unscaled.Y);
    }

    private const ulong PropertyFnvOffset = 14695981039346656037UL;
    private const ulong PropertyFnvPrime = 1099511628211UL;
    private const string PathComponentTypeName = "global::Derp.UI.PathComponent";

    private static ulong ComputePathComponentArrayElementPropertyId(string fieldName, int index)
    {
        ulong hash = PropertyFnvOffset;
        AppendAscii(ref hash, PathComponentTypeName);
        AppendByte(ref hash, (byte)'.');
        AppendAscii(ref hash, fieldName);
        AppendByte(ref hash, (byte)'[');
        AppendSmallNonNegativeInt(ref hash, index);
        AppendByte(ref hash, (byte)']');
        return hash;
    }

    private static void AppendAscii(ref ulong hash, string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            hash ^= (byte)text[i];
            hash *= PropertyFnvPrime;
        }
    }

    private static void AppendByte(ref ulong hash, byte value)
    {
        hash ^= value;
        hash *= PropertyFnvPrime;
    }

    private static void AppendSmallNonNegativeInt(ref ulong hash, int value)
    {
        if (value < 0)
        {
            value = 0;
        }

        if (value < 10)
        {
            AppendByte(ref hash, (byte)('0' + value));
            return;
        }

        if (value < 100)
        {
            int tens = value / 10;
            int ones = value - (tens * 10);
            AppendByte(ref hash, (byte)('0' + tens));
            AppendByte(ref hash, (byte)('0' + ones));
            return;
        }

        int hundreds = value / 100;
        int rem = value - (hundreds * 100);
        int tens2 = rem / 10;
        int ones2 = rem - (tens2 * 10);
        AppendByte(ref hash, (byte)('0' + hundreds));
        AppendByte(ref hash, (byte)('0' + tens2));
        AppendByte(ref hash, (byte)('0' + ones2));
    }

    private void HandlePolygonCreation(DerpLib.ImGui.Input.ImInput input, Vector2 canvasOrigin, Vector2 mouseCanvas)
    {
        _canvasInteractions.HandlePolygonCreation(input, canvasOrigin, mouseCanvas);
    }

    internal void CommitPolygonIfValid()
    {
        _canvasInteractions.CommitPolygonIfValid();
    }

    private void HandleFrameCreation(DerpLib.ImGui.Input.ImInput input, Vector2 canvasOrigin, Vector2 mouseCanvas)
    {
        _canvasInteractions.HandleFrameCreation(input, canvasOrigin, mouseCanvas);
    }

    private void HandleShapeCreation(DerpLib.ImGui.Input.ImInput input, Vector2 canvasOrigin, Vector2 mouseCanvas, ShapeKind kind)
    {
        _canvasInteractions.HandleShapeCreation(input, canvasOrigin, mouseCanvas, kind);
    }

    private void HandleTextCreation(DerpLib.ImGui.Input.ImInput input, Vector2 canvasOrigin, Vector2 mouseCanvas)
    {
        _canvasInteractions.HandleTextCreation(input, canvasOrigin, mouseCanvas);
    }

    internal Shape CreateShape(ShapeKind kind, ImRect rectWorld, int parentFrameId)
    {
        var shapeView = ShapeComponent.Api.Create(_propertyWorld, new ShapeComponent { Kind = kind });

        Vector2 positionWorld = new Vector2(rectWorld.X, rectWorld.Y);
        Vector2 positionLocal = positionWorld;
        if (parentFrameId > 0 && TryGetEntityById(_prefabEntityById, parentFrameId, out EntityId prefabEntity) && !prefabEntity.IsNull)
        {
            if (TryGetEntityWorldTransformEcs(prefabEntity, out WorldTransform prefabWorldTransform))
            {
                positionLocal = InverseTransformPoint(prefabWorldTransform, positionWorld);
            }
        }

        var transformView = TransformComponent.Api.Create(_propertyWorld, new TransformComponent
        {
            Position = positionLocal,
            Scale = Vector2.One,
            Rotation = 0f,
            Anchor = Vector2.Zero,
            Depth = 0f
        });

        var blendView = BlendComponent.Api.Create(_propertyWorld, new BlendComponent
        {
            IsVisible = true,
            Opacity = 1f,
            BlendMode = (int)PaintBlendMode.Normal
        });

        RectGeometryComponentHandle rectGeometryHandle = default;
        CircleGeometryComponentHandle circleGeometryHandle = default;
        if (kind == ShapeKind.Rect)
        {
            rectGeometryHandle = RectGeometryComponent.Api.Create(_propertyWorld, new RectGeometryComponent
            {
                Size = new Vector2(rectWorld.Width, rectWorld.Height),
                CornerRadius = Vector4.Zero
            }).Handle;
        }
        else
        {
            float radius = Math.Min(rectWorld.Width, rectWorld.Height) * 0.5f;
            circleGeometryHandle = CircleGeometryComponent.Api.Create(_propertyWorld, new CircleGeometryComponent
            {
                Radius = radius
            }).Handle;
        }

        bool useGradient = kind == ShapeKind.Circle;

        var paintView = PaintComponent.Api.Create(_propertyWorld, new PaintComponent
        {
            LayerCount = 1
        });

        Span<int> layerKind = PaintComponentProperties.LayerKindArray(_propertyWorld, paintView.Handle);
        Span<bool> layerIsVisible = PaintComponentProperties.LayerIsVisibleArray(_propertyWorld, paintView.Handle);
        Span<bool> layerInheritBlend = PaintComponentProperties.LayerInheritBlendModeArray(_propertyWorld, paintView.Handle);
	        Span<int> layerBlendMode = PaintComponentProperties.LayerBlendModeArray(_propertyWorld, paintView.Handle);
	        Span<float> layerOpacity = PaintComponentProperties.LayerOpacityArray(_propertyWorld, paintView.Handle);
	        Span<Vector2> layerOffset = PaintComponentProperties.LayerOffsetArray(_propertyWorld, paintView.Handle);
	        Span<float> layerBlur = PaintComponentProperties.LayerBlurArray(_propertyWorld, paintView.Handle);
	        Span<int> layerBlurDirection = PaintComponentProperties.LayerBlurDirectionArray(_propertyWorld, paintView.Handle);

        layerKind[0] = (int)PaintLayerKind.Fill;
        layerIsVisible[0] = true;
        layerInheritBlend[0] = true;
        layerBlendMode[0] = (int)PaintBlendMode.Normal;
	        layerOpacity[0] = 1f;
	        layerOffset[0] = Vector2.Zero;
	        layerBlur[0] = 0f;
	        layerBlurDirection[0] = 0;

        Span<Color32> fillColor = PaintComponentProperties.FillColorArray(_propertyWorld, paintView.Handle);
        Span<bool> fillUseGradient = PaintComponentProperties.FillUseGradientArray(_propertyWorld, paintView.Handle);
        Span<Color32> fillGradientColorA = PaintComponentProperties.FillGradientColorAArray(_propertyWorld, paintView.Handle);
        Span<Color32> fillGradientColorB = PaintComponentProperties.FillGradientColorBArray(_propertyWorld, paintView.Handle);
        Span<int> fillGradientType = PaintComponentProperties.FillGradientTypeArray(_propertyWorld, paintView.Handle);
        Span<float> fillGradientMix = PaintComponentProperties.FillGradientMixArray(_propertyWorld, paintView.Handle);
        Span<Vector2> fillGradientDirection = PaintComponentProperties.FillGradientDirectionArray(_propertyWorld, paintView.Handle);
        Span<Vector2> fillGradientCenter = PaintComponentProperties.FillGradientCenterArray(_propertyWorld, paintView.Handle);
        Span<float> fillGradientRadius = PaintComponentProperties.FillGradientRadiusArray(_propertyWorld, paintView.Handle);
        Span<float> fillGradientAngle = PaintComponentProperties.FillGradientAngleArray(_propertyWorld, paintView.Handle);
        Span<int> fillStopCount = PaintComponentProperties.FillGradientStopCountArray(_propertyWorld, paintView.Handle);

        fillColor[0] = new Color32(210, 210, 220, 255);
        fillUseGradient[0] = useGradient;
        fillGradientColorA[0] = new Color32(255, 170, 140, 255);
        fillGradientColorB[0] = new Color32(120, 190, 255, 255);
        fillGradientType[0] = 0;
        fillGradientMix[0] = 0.5f;
        fillGradientDirection[0] = new Vector2(1f, 0f);
        fillGradientCenter[0] = Vector2.Zero;
        fillGradientRadius[0] = 1f;
        fillGradientAngle[0] = 0f;
        fillStopCount[0] = 0;

        return new Shape
        {
            Id = _nextShapeId++,
            ParentFrameId = parentFrameId,
            ParentGroupId = 0,
            Component = shapeView.Handle,
            Transform = transformView.Handle,
            Blend = blendView.Handle,
            Paint = paintView.Handle,
            RectGeometry = rectGeometryHandle,
            CircleGeometry = circleGeometryHandle,
            Path = default
        };
    }

    internal Text CreateText(ImRect rectWorld, int parentFrameId)
    {
        Vector2 positionWorld = new Vector2(rectWorld.X, rectWorld.Y);
        Vector2 positionLocal = positionWorld;
        if (parentFrameId > 0 && TryGetEntityById(_prefabEntityById, parentFrameId, out EntityId prefabEntity) && !prefabEntity.IsNull)
        {
            if (TryGetEntityWorldTransformEcs(prefabEntity, out WorldTransform prefabWorldTransform))
            {
                positionLocal = InverseTransformPoint(prefabWorldTransform, positionWorld);
            }
        }

        var transformView = TransformComponent.Api.Create(_propertyWorld, new TransformComponent
        {
            Position = positionLocal,
            Scale = Vector2.One,
            Rotation = 0f,
            Anchor = Vector2.Zero,
            Depth = 0f
        });

        var blendView = BlendComponent.Api.Create(_propertyWorld, new BlendComponent
        {
            IsVisible = true,
            Opacity = 1f,
            BlendMode = (int)PaintBlendMode.Normal
        });

        var rectGeometryView = RectGeometryComponent.Api.Create(_propertyWorld, new RectGeometryComponent
        {
            Size = new Vector2(rectWorld.Width, rectWorld.Height),
            CornerRadius = Vector4.Zero
        });

        var paintView = PaintComponent.Api.Create(_propertyWorld, new PaintComponent
        {
            LayerCount = 1
        });

        Span<int> layerKind = PaintComponentProperties.LayerKindArray(_propertyWorld, paintView.Handle);
        Span<bool> layerIsVisible = PaintComponentProperties.LayerIsVisibleArray(_propertyWorld, paintView.Handle);
        Span<bool> layerInheritBlend = PaintComponentProperties.LayerInheritBlendModeArray(_propertyWorld, paintView.Handle);
        Span<int> layerBlendMode = PaintComponentProperties.LayerBlendModeArray(_propertyWorld, paintView.Handle);
        Span<float> layerOpacity = PaintComponentProperties.LayerOpacityArray(_propertyWorld, paintView.Handle);
        Span<Vector2> layerOffset = PaintComponentProperties.LayerOffsetArray(_propertyWorld, paintView.Handle);
        Span<float> layerBlur = PaintComponentProperties.LayerBlurArray(_propertyWorld, paintView.Handle);
        Span<int> layerBlurDirection = PaintComponentProperties.LayerBlurDirectionArray(_propertyWorld, paintView.Handle);

        layerKind[0] = (int)PaintLayerKind.Fill;
        layerIsVisible[0] = true;
        layerInheritBlend[0] = true;
        layerBlendMode[0] = (int)PaintBlendMode.Normal;
        layerOpacity[0] = 1f;
        layerOffset[0] = Vector2.Zero;
        layerBlur[0] = 0f;
        layerBlurDirection[0] = 0;

        Span<Color32> fillColor = PaintComponentProperties.FillColorArray(_propertyWorld, paintView.Handle);
        Span<bool> fillUseGradient = PaintComponentProperties.FillUseGradientArray(_propertyWorld, paintView.Handle);
        Span<Color32> fillGradientColorA = PaintComponentProperties.FillGradientColorAArray(_propertyWorld, paintView.Handle);
        Span<Color32> fillGradientColorB = PaintComponentProperties.FillGradientColorBArray(_propertyWorld, paintView.Handle);
        Span<int> fillGradientType = PaintComponentProperties.FillGradientTypeArray(_propertyWorld, paintView.Handle);
        Span<float> fillGradientMix = PaintComponentProperties.FillGradientMixArray(_propertyWorld, paintView.Handle);
        Span<Vector2> fillGradientDirection = PaintComponentProperties.FillGradientDirectionArray(_propertyWorld, paintView.Handle);
        Span<Vector2> fillGradientCenter = PaintComponentProperties.FillGradientCenterArray(_propertyWorld, paintView.Handle);
        Span<float> fillGradientRadius = PaintComponentProperties.FillGradientRadiusArray(_propertyWorld, paintView.Handle);
        Span<float> fillGradientAngle = PaintComponentProperties.FillGradientAngleArray(_propertyWorld, paintView.Handle);
        Span<int> fillStopCount = PaintComponentProperties.FillGradientStopCountArray(_propertyWorld, paintView.Handle);
        fillColor[0] = new Color32(10, 10, 10, 255);
        fillUseGradient[0] = false;
        fillGradientColorA[0] = new Color32(255, 170, 140, 255);
        fillGradientColorB[0] = new Color32(120, 190, 255, 255);
        fillGradientType[0] = 0;
        fillGradientMix[0] = 0.5f;
        fillGradientDirection[0] = new Vector2(1f, 0f);
        fillGradientCenter[0] = Vector2.Zero;
        fillGradientRadius[0] = 1f;
        fillGradientAngle[0] = 0f;
        fillStopCount[0] = 0;

        var textView = TextComponent.Api.Create(_propertyWorld, new TextComponent
        {
            Text = "Text",
            Font = "arial",
            FontSizePx = 24f,
            LineHeightScale = 1f,
            LetterSpacingPx = 0f,
            Multiline = false,
            Wrap = false,
            Overflow = (int)TextOverflowMode.Visible,
            AlignX = (int)TextHorizontalAlign.Left,
            AlignY = (int)TextVerticalAlign.Top
        });

        return new Text
        {
            Id = _nextTextId++,
            ParentFrameId = parentFrameId,
            ParentGroupId = 0,
            ParentShapeId = 0,
            Component = textView.Handle,
            Transform = transformView.Handle,
            Blend = blendView.Handle,
            Paint = paintView.Handle,
            RectGeometry = rectGeometryView.Handle
        };
    }

    private void HandleSelectionAndDrag(DerpLib.ImGui.Input.ImInput input, Vector2 canvasOrigin, Vector2 mouseCanvas)
    {
        if (_marqueeState != MarqueeState.None)
        {
            HandleMarqueeSelection(input, canvasOrigin, mouseCanvas);
            return;
        }

        Vector2 mouseWorld = CanvasToWorld(mouseCanvas, canvasOrigin);

        UpdatePropertyGizmosFromInspectorState();
        if (HandlePropertyGizmosInput(input, canvasOrigin, mouseCanvas))
        {
            return;
        }

        if (HandleLayoutConstraintGizmoInput(input, canvasOrigin, mouseCanvas))
        {
            return;
        }

        if (input.MousePressed)
        {
            if (input.IsDoubleClick && TryBeginCanvasInlineTextEditFromSelection(mouseWorld))
            {
                return;
            }

            if (input.IsDoubleClick)
            {
                EntityId doubleClickPrefabEntity = FindTopmostPrefabEntity(mouseWorld);
                EntityId doubleClickNodeEntity = !doubleClickPrefabEntity.IsNull
                    ? FindTopmostShapeEntityInPrefab(doubleClickPrefabEntity, mouseWorld, includeGroupedShapes: false)
                    : EntityId.Null;

                if (!doubleClickNodeEntity.IsNull && _world.GetNodeType(doubleClickNodeEntity) == UiNodeType.Text)
                {
                    SelectSingleEntity(doubleClickNodeEntity);
                    BeginCanvasInlineTextEdit(doubleClickNodeEntity);
                    return;
                }
            }

            if (!input.KeyShift && TryBeginTransformSelectedGroup(mouseWorld, mouseCanvas, canvasOrigin))
            {
                return;
            }

            if (!input.KeyShift && TryBeginRotateSelectedShape(mouseWorld, mouseCanvas, canvasOrigin))
            {
                return;
            }

            if (!input.KeyShift && TryBeginResizeSelectedShape(mouseWorld, mouseCanvas, canvasOrigin))
            {
                return;
            }

            if (!input.KeyShift && TryBeginResizeSelectedPrefab(mouseWorld, mouseCanvas, canvasOrigin))
            {
                return;
            }

            if (!input.KeyShift && TryBeginResizeSelectedPrefabInstance(mouseWorld, mouseCanvas, canvasOrigin))
            {
                return;
            }

            if (!input.KeyShift && TryBeginDragSelectedNode(mouseWorld))
            {
                return;
            }

            if (!input.KeyShift && !input.KeyCtrl && !input.KeyAlt && TryBeginSelectAndDragPrefabFromLabel(mouseWorld, mouseCanvas, canvasOrigin))
            {
                return;
            }

            if (!input.KeyShift && !input.KeyCtrl && !input.KeyAlt && TryBeginDragSelectedPrefabAnywhere(mouseWorld, mouseCanvas, canvasOrigin))
            {
                return;
            }

            if (_isPrefabTransformSelectedOnCanvas && _selectedEntities.Count == 0 && !_selectedPrefabEntity.IsNull)
            {
                if (!IsMouseOverSelectedPrefabOrLabel(mouseCanvas, canvasOrigin))
                {
                    _isPrefabTransformSelectedOnCanvas = false;
                }
            }

            _marqueeModifyMode = GetSelectionModifyMode(input);
            if (_marqueeModifyMode == SelectionModifyMode.Replace)
            {
                ClearSelection();
            }

            EntityId hitPrefabEntity = FindTopmostPrefabEntity(mouseWorld);
            int hitFrameIndex = -1;
            int hitFrameId = 0;
            if (!hitPrefabEntity.IsNull)
            {
                if (TryGetLegacyPrefabIndex(hitPrefabEntity, out hitFrameIndex) && hitFrameIndex >= 0 && hitFrameIndex < _prefabs.Count)
                {
                    hitFrameId = _prefabs[hitFrameIndex].Id;
                }
                else
                {
                    hitFrameId = (int)_world.GetStableId(hitPrefabEntity);
                }
            }

            if (input.KeyAlt)
            {
                EntityId hitGroupedShapeEntity = !hitPrefabEntity.IsNull
                    ? FindTopmostShapeEntityInPrefab(hitPrefabEntity, mouseWorld, includeGroupedShapes: true)
                    : EntityId.Null;

                if (!hitGroupedShapeEntity.IsNull &&
                    TryGetLegacyShapeId(hitGroupedShapeEntity, out int hitGroupedShapeId) &&
                    TryGetShapeIndexById(hitGroupedShapeId, out int hitGroupedShapeIndex))
                {
                    if (input.KeyShift)
                    {
                        ToggleSelectedEntity(hitGroupedShapeEntity);
                        return;
                    }

                    SelectSingleEntity(hitGroupedShapeEntity);
                    BeginDrag(DragKind.Shape, mouseWorld, frameIndex: -1, shapeIndex: hitGroupedShapeIndex, groupIndex: -1, textIndex: -1);
                    return;
                }
            }

            EntityId hitGroupEntity = !hitPrefabEntity.IsNull ? FindTopmostGroupEntityInPrefab(hitPrefabEntity, mouseWorld) : EntityId.Null;
            if (!hitGroupEntity.IsNull &&
                TryGetLegacyGroupId(hitGroupEntity, out int hitGroupId) &&
                TryGetGroupIndexById(hitGroupId, out int hitGroupIndex))
            {
                if (input.KeyShift)
                {
                    ToggleSelectedEntity(hitGroupEntity);
                    return;
                }

                SelectSingleEntity(hitGroupEntity);
                BeginDrag(DragKind.Group, mouseWorld, frameIndex: -1, shapeIndex: -1, groupIndex: hitGroupIndex, textIndex: -1);
                return;
            }

            EntityId hitShapeEntity = !hitPrefabEntity.IsNull
                ? FindTopmostShapeEntityInPrefab(hitPrefabEntity, mouseWorld, includeGroupedShapes: false)
                : EntityId.Null;

            if (!hitShapeEntity.IsNull && _world.GetNodeType(hitShapeEntity) == UiNodeType.PrefabInstance)
            {
                if (input.KeyShift)
                {
                    ToggleSelectedEntity(hitShapeEntity);
                    return;
                }

                SelectSingleEntity(hitShapeEntity);
                BeginDragNode(hitShapeEntity, mouseWorld);
                return;
            }

            if (!hitShapeEntity.IsNull && _world.GetNodeType(hitShapeEntity) == UiNodeType.Text &&
                TryGetLegacyTextId(hitShapeEntity, out int hitTextId) &&
                TryGetTextIndexById(hitTextId, out int hitTextIndex))
            {
                if (input.KeyShift)
                {
                    ToggleSelectedEntity(hitShapeEntity);
                    return;
                }

                SelectSingleEntity(hitShapeEntity);
                if (input.IsDoubleClick)
                {
                    BeginCanvasInlineTextEdit(hitShapeEntity);
                    return;
                }

                BeginDrag(DragKind.Text, mouseWorld, frameIndex: -1, shapeIndex: -1, groupIndex: -1, textIndex: hitTextIndex);
                return;
            }

            if (!hitShapeEntity.IsNull && _world.GetNodeType(hitShapeEntity) == UiNodeType.Shape &&
                TryGetLegacyShapeId(hitShapeEntity, out int hitShapeId) &&
                TryGetShapeIndexById(hitShapeId, out int hitShapeIndex))
            {
                if (input.KeyShift)
                {
                    ToggleSelectedEntity(hitShapeEntity);
                    return;
                }

                SelectSingleEntity(hitShapeEntity);
                BeginDrag(DragKind.Shape, mouseWorld, frameIndex: -1, shapeIndex: hitShapeIndex, groupIndex: -1, textIndex: -1);

                return;
            }

            if (hitFrameIndex >= 0)
            {
                _isPrefabTransformSelectedOnCanvas = false;
                _marqueeState = MarqueeState.Pending;
                _marqueeStartCanvas = mouseCanvas;
                _marqueeCurrentCanvas = mouseCanvas;
                _marqueePrefabId = hitFrameId;
                _marqueePrefabEntity = hitPrefabEntity;
                return;
            }

            _dragKind = DragKind.None;
        }

        if (_dragKind == DragKind.None)
        {
            return;
        }

        if (!input.MouseDown)
        {
            if (input.MouseReleased)
            {
                CommitCanvasEditCapture();
                _dragKind = DragKind.None;
            }
            return;
        }

        Vector2 deltaWorld = mouseWorld - _dragLastMouseWorld;
        _dragLastMouseWorld = mouseWorld;

        if (_dragKind == DragKind.ResizeShape && _dragShapeIndex >= 0 && _dragShapeIndex < _shapes.Count)
        {
            var shape = _shapes[_dragShapeIndex];
            WorldTransform parentWorldTransform = IdentityWorldTransform;
            EntityId shapeEntity = EntityId.Null;
            if (TryGetEntityById(_shapeEntityById, shape.Id, out shapeEntity))
            {
                if (!TryGetTransformParentWorldTransformEcs(shapeEntity, out parentWorldTransform))
                {
                    parentWorldTransform = IdentityWorldTransform;
                }
            }

            Vector2 mouseParentLocal = InverseTransformPoint(parentWorldTransform, mouseWorld);
            ResizeShape(_dragShapeIndex, _resizeHandle, _resizeStartRectWorld, mouseParentLocal);
            return;
        }

        if (_dragKind == DragKind.ResizePrefab && _dragPrefabIndex >= 0 && _dragPrefabIndex < _prefabs.Count)
        {
            ResizePrefab(_dragPrefabIndex, _resizeHandle, _resizeStartRectWorld, mouseWorld, dispatchAnimation: true);
            return;
        }

        if (_dragKind == DragKind.ResizeNode && !_dragEntity.IsNull)
        {
            WorldTransform parentWorldTransform = IdentityWorldTransform;
            if (!TryGetTransformParentWorldTransformEcs(_dragEntity, out parentWorldTransform))
            {
                parentWorldTransform = IdentityWorldTransform;
            }

            Vector2 mouseParentLocal = InverseTransformPoint(parentWorldTransform, mouseWorld);
            ResizePrefabInstance(_dragEntity, _resizeHandle, _resizeStartRectWorld, mouseParentLocal);
            return;
        }

        if (_dragKind == DragKind.ResizeGroup && _dragGroupIndex >= 0 && _dragGroupIndex < _groups.Count)
        {
            ResizeGroup(_dragGroupIndex, _resizeHandle, mouseWorld);
            return;
        }

        if (_dragKind == DragKind.RotateGroup && _dragGroupIndex >= 0 && _dragGroupIndex < _groups.Count)
        {
            RotateGroup(_dragGroupIndex, mouseWorld);
            return;
        }

        if (_dragKind == DragKind.ResizeText && _dragTextIndex >= 0 && _dragTextIndex < _texts.Count)
        {
            var text = _texts[_dragTextIndex];
            WorldTransform parentWorldTransform = IdentityWorldTransform;
            EntityId textEntity = EntityId.Null;
            if (TryGetEntityById(_textEntityById, text.Id, out textEntity))
            {
                if (!TryGetTransformParentWorldTransformEcs(textEntity, out parentWorldTransform))
                {
                    parentWorldTransform = IdentityWorldTransform;
                }
            }

            Vector2 mouseParentLocal = InverseTransformPoint(parentWorldTransform, mouseWorld);
            ResizeText(_dragTextIndex, _resizeHandle, _resizeStartRectWorld, mouseParentLocal);
            return;
        }

        if (_dragKind == DragKind.RotateText && _dragTextIndex >= 0 && _dragTextIndex < _texts.Count)
        {
            var text = _texts[_dragTextIndex];
            WorldTransform parentWorldTransform = IdentityWorldTransform;
            EntityId textEntity = EntityId.Null;
            if (TryGetEntityById(_textEntityById, text.Id, out textEntity))
            {
                if (!TryGetTransformParentWorldTransformEcs(textEntity, out parentWorldTransform))
                {
                    parentWorldTransform = IdentityWorldTransform;
                }
            }

            Vector2 mouseParentLocal = InverseTransformPoint(parentWorldTransform, mouseWorld);
            RotateText(_dragTextIndex, mouseParentLocal);
            return;
        }

        if (_dragKind == DragKind.RotateShape && _dragShapeIndex >= 0 && _dragShapeIndex < _shapes.Count)
        {
            var shape = _shapes[_dragShapeIndex];
            WorldTransform parentWorldTransform = IdentityWorldTransform;
            if (TryGetEntityById(_shapeEntityById, shape.Id, out EntityId shapeEntity))
            {
                if (!TryGetTransformParentWorldTransformEcs(shapeEntity, out parentWorldTransform))
                {
                    parentWorldTransform = IdentityWorldTransform;
                }
            }

            Vector2 mouseParentLocal = InverseTransformPoint(parentWorldTransform, mouseWorld);
            RotateShape(_dragShapeIndex, mouseParentLocal);
            return;
        }

        if (_dragKind == DragKind.Node && !_dragEntity.IsNull)
        {
            TranslateEntityPositionEcs(_dragEntity, deltaWorld);
            return;
        }

        if (_dragKind == DragKind.Shape && _dragShapeIndex >= 0 && _dragShapeIndex < _shapes.Count)
        {
            MoveShape(_dragShapeIndex, deltaWorld, dispatchAnimation: true);
            return;
        }

        if (_dragKind == DragKind.Text && _dragTextIndex >= 0 && _dragTextIndex < _texts.Count)
        {
            MoveText(_dragTextIndex, deltaWorld, dispatchAnimation: true);
            return;
        }

        if (_dragKind == DragKind.Group && _dragGroupIndex >= 0 && _dragGroupIndex < _groups.Count)
        {
            MoveGroup(_dragGroupIndex, deltaWorld, dispatchAnimation: true);
            return;
        }

        if (_dragKind == DragKind.Prefab && _dragPrefabIndex >= 0 && _dragPrefabIndex < _prefabs.Count)
        {
            MovePrefab(_dragPrefabIndex, deltaWorld, dispatchAnimation: true);
        }
    }

    private bool TryBeginDragSelectedNode(Vector2 mouseWorld)
    {
        if (_selectedEntities.Count != 1)
        {
            return false;
        }

        EntityId entity = _selectedEntities[0];
        if (IsEntityLockedInEditor(entity) || IsEntityHiddenInEditor(entity))
        {
            return false;
        }

        UiNodeType type = _world.GetNodeType(entity);
        if (type == UiNodeType.BooleanGroup)
        {
            if (!TryGetLegacyGroupId(entity, out int groupId) || !TryGetGroupIndexById(groupId, out int groupIndex))
            {
                return false;
            }

            WorldTransform parentWorldTransform = IdentityWorldTransform;
            if (!TryGetTransformParentWorldTransformEcs(entity, out parentWorldTransform))
            {
                parentWorldTransform = IdentityWorldTransform;
            }

            if (!IsPointInsideGroupBoundsEcs(entity, mouseWorld, parentWorldTransform))
            {
                return false;
            }

            BeginDrag(DragKind.Group, mouseWorld, frameIndex: -1, shapeIndex: -1, groupIndex: groupIndex, textIndex: -1);
            return true;
        }

        if (type == UiNodeType.Text)
        {
            if (!TryGetLegacyTextId(entity, out int textId) || !TryGetTextIndexById(textId, out int textIndex))
            {
                return false;
            }

            WorldTransform parentWorldTransform = IdentityWorldTransform;
            if (!TryGetTransformParentWorldTransformEcs(entity, out parentWorldTransform))
            {
                parentWorldTransform = IdentityWorldTransform;
            }

            if (!IsPointInsideTextEcs(entity, mouseWorld, parentWorldTransform))
            {
                return false;
            }

            BeginDrag(DragKind.Text, mouseWorld, frameIndex: -1, shapeIndex: -1, groupIndex: -1, textIndex: textIndex);
            return true;
        }

        if (type == UiNodeType.PrefabInstance)
        {
            WorldTransform parentWorldTransform = IdentityWorldTransform;
            if (!TryGetTransformParentWorldTransformEcs(entity, out parentWorldTransform))
            {
                parentWorldTransform = IdentityWorldTransform;
            }

            if (!IsPointInsidePrefabInstanceBoundsEcs(entity, mouseWorld, parentWorldTransform))
            {
                return false;
            }

            BeginDragNode(entity, mouseWorld);
            return true;
        }

        if (type != UiNodeType.Shape)
        {
            return false;
        }

        if (!TryGetLegacyShapeId(entity, out int shapeId) || !TryGetShapeIndexById(shapeId, out int shapeIndex))
        {
            return false;
        }

        WorldTransform parentWorldTransformShape = IdentityWorldTransform;
        if (!TryGetTransformParentWorldTransformEcs(entity, out parentWorldTransformShape))
        {
            parentWorldTransformShape = IdentityWorldTransform;
        }

        if (!IsPointInsideShapeEcs(entity, mouseWorld, parentWorldTransformShape))
        {
            return false;
        }

        BeginDrag(DragKind.Shape, mouseWorld, frameIndex: -1, shapeIndex: shapeIndex, groupIndex: -1, textIndex: -1);
        return true;
    }

    private void BeginDrag(DragKind kind, Vector2 startMouseWorld, int frameIndex, int shapeIndex, int groupIndex, int textIndex)
    {
        _dragKind = kind;
        _dragLastMouseWorld = startMouseWorld;
        _dragPrefabIndex = frameIndex;
        _dragShapeIndex = shapeIndex;
        _dragGroupIndex = groupIndex;
        _dragTextIndex = textIndex;
        _dragEntity = EntityId.Null;

        if (kind == DragKind.Prefab)
        {
            BeginCanvasEditCaptureForFrame(frameIndex, kind);
        }
        else if (kind == DragKind.Shape)
        {
            BeginCanvasEditCaptureForShape(shapeIndex, DragKind.Shape, includeGeometry: false);
        }
        else if (kind == DragKind.Group)
        {
            BeginCanvasEditCaptureForGroup(groupIndex, DragKind.Group);
        }
        else if (kind == DragKind.Text)
        {
            BeginCanvasEditCaptureForText(textIndex, DragKind.Text, includeGeometry: false);
        }
    }

    private void BeginDragNode(EntityId entity, Vector2 startMouseWorld)
    {
        if (entity.IsNull)
        {
            return;
        }

        if (_canvasEditCapture.Active != 0)
        {
            return;
        }

        if (!_world.TryGetComponent(entity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny))
        {
            return;
        }

        var transformHandle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
        if (!TrySnapshotTransform(transformHandle, out TransformComponent transformBefore))
        {
            return;
        }

        _dragKind = DragKind.Node;
        _dragLastMouseWorld = startMouseWorld;
        _dragPrefabIndex = -1;
        _dragShapeIndex = -1;
        _dragGroupIndex = -1;
        _dragTextIndex = -1;
        _dragEntity = entity;

        _canvasEditCapture = new CanvasEditCapture
        {
            Active = 1,
            Kind = DragKind.Node,
            TransformHandle = transformHandle,
            TransformBefore = transformBefore,
            HasTransform = 1
        };
    }

    private void BeginCanvasEditCaptureForFrame(int frameIndex)
    {
        BeginCanvasEditCaptureForFrame(frameIndex, DragKind.Prefab);
    }

    private void BeginCanvasEditCaptureForFrame(int frameIndex, DragKind kind)
    {
        if (_canvasEditCapture.Active != 0 || frameIndex < 0 || frameIndex >= _prefabs.Count)
        {
            return;
        }

        var frame = _prefabs[frameIndex];
        _canvasEditCapture = new CanvasEditCapture
        {
            Active = 1,
            Kind = kind,
            PrefabId = frame.Id,
            PrefabRectWorldBefore = frame.RectWorld
        };
    }

    private void BeginCanvasEditCaptureForShape(int shapeIndex, DragKind kind, bool includeGeometry)
    {
        if (_canvasEditCapture.Active != 0 || shapeIndex < 0 || shapeIndex >= _shapes.Count)
        {
            return;
        }

        var shape = _shapes[shapeIndex];
        if (!TrySnapshotTransform(shape.Transform, out TransformComponent transformBefore))
        {
            return;
        }

        _canvasEditCapture = new CanvasEditCapture
        {
            Active = 1,
            Kind = kind,
            TransformHandle = shape.Transform,
            TransformBefore = transformBefore,
            HasTransform = 1
        };

        if (!includeGeometry)
        {
            return;
        }

        ShapeKind shapeKind = GetShapeKind(shape);
        if (shapeKind == ShapeKind.Rect && !shape.RectGeometry.IsNull && TrySnapshotRectGeometry(shape.RectGeometry, out RectGeometryComponent geometryBefore))
        {
            _canvasEditCapture.RectGeometryHandle = shape.RectGeometry;
            _canvasEditCapture.RectGeometryBefore = geometryBefore;
            _canvasEditCapture.HasRectGeometry = 1;
            return;
        }

        if (shapeKind == ShapeKind.Circle && !shape.CircleGeometry.IsNull && TrySnapshotCircleGeometry(shape.CircleGeometry, out CircleGeometryComponent circleBefore))
        {
            _canvasEditCapture.CircleGeometryHandle = shape.CircleGeometry;
            _canvasEditCapture.CircleGeometryBefore = circleBefore;
            _canvasEditCapture.HasCircleGeometry = 1;
        }
    }

    private void BeginCanvasEditCaptureForText(int textIndex, DragKind kind, bool includeGeometry)
    {
        if (_canvasEditCapture.Active != 0 || textIndex < 0 || textIndex >= _texts.Count)
        {
            return;
        }

        var text = _texts[textIndex];
        if (!TrySnapshotTransform(text.Transform, out TransformComponent transformBefore))
        {
            return;
        }

        _canvasEditCapture = new CanvasEditCapture
        {
            Active = 1,
            Kind = kind,
            TransformHandle = text.Transform,
            TransformBefore = transformBefore,
            HasTransform = 1
        };

        if (!includeGeometry)
        {
            return;
        }

        if (!text.RectGeometry.IsNull && TrySnapshotRectGeometry(text.RectGeometry, out RectGeometryComponent geometryBefore))
        {
            _canvasEditCapture.RectGeometryHandle = text.RectGeometry;
            _canvasEditCapture.RectGeometryBefore = geometryBefore;
            _canvasEditCapture.HasRectGeometry = 1;
        }
    }

    private void BeginCanvasEditCaptureForGroup(int groupIndex, DragKind kind)
    {
        if (_canvasEditCapture.Active != 0 || groupIndex < 0 || groupIndex >= _groups.Count)
        {
            return;
        }

        var group = _groups[groupIndex];
        EnsureGroupTransformInitialized(ref group);
        _groups[groupIndex] = group;

        if (group.Transform.IsNull || !TrySnapshotTransform(group.Transform, out TransformComponent transformBefore))
        {
            return;
        }

        _canvasEditCapture = new CanvasEditCapture
        {
            Active = 1,
            Kind = kind,
            TransformHandle = group.Transform,
            TransformBefore = transformBefore,
            HasTransform = 1
        };

        if (kind != DragKind.ResizeGroup)
        {
            return;
        }

        if (!TryGetEntityById(_groupEntityById, group.Id, out EntityId groupEntity) || groupEntity.IsNull)
        {
            return;
        }

        if (_world.TryGetComponent(groupEntity, MaskGroupComponent.Api.PoolIdConst, out _) ||
            _world.TryGetComponent(groupEntity, BooleanGroupComponent.Api.PoolIdConst, out _))
        {
            return;
        }

        if (!_world.TryGetComponent(groupEntity, RectGeometryComponent.Api.PoolIdConst, out AnyComponentHandle rectAny))
        {
            return;
        }

        var rectHandle = new RectGeometryComponentHandle(rectAny.Index, rectAny.Generation);
        if (TrySnapshotRectGeometry(rectHandle, out RectGeometryComponent geometryBefore))
        {
            _canvasEditCapture.RectGeometryHandle = rectHandle;
            _canvasEditCapture.RectGeometryBefore = geometryBefore;
            _canvasEditCapture.HasRectGeometry = 1;
        }
    }

    private void CommitCanvasEditCapture()
    {
        if (_canvasEditCapture.Active == 0)
        {
            return;
        }

        CanvasEditCapture capture = _canvasEditCapture;
        _canvasEditCapture = default;

        if (capture.Kind == DragKind.Prefab)
        {
            if (!TryGetPrefabIndexById(capture.PrefabId, out int frameIndex))
            {
                return;
            }

            var frame = _prefabs[frameIndex];
            Vector2 deltaWorld = frame.RectWorld.Position - capture.PrefabRectWorldBefore.Position;
            Commands.RecordMovePrefab(capture.PrefabId, deltaWorld);
            return;
        }

        if (capture.Kind == DragKind.ResizePrefab)
        {
            if (!TryGetPrefabIndexById(capture.PrefabId, out int frameIndex))
            {
                return;
            }

            var frame = _prefabs[frameIndex];
            Commands.RecordResizePrefab(capture.PrefabId, capture.PrefabRectWorldBefore, frame.RectWorld);
            return;
        }

        if (capture.Kind == DragKind.ResizeNode)
        {
            if (capture.HasTransform == 0 || capture.TransformHandle.IsNull || capture.HasPrefabCanvas == 0 || capture.PrefabCanvasHandle.IsNull)
            {
                return;
            }

            if (!TrySnapshotTransform(capture.TransformHandle, out TransformComponent transformAfterResize))
            {
                return;
            }

            if (!TrySnapshotPrefabCanvas(capture.PrefabCanvasHandle, out PrefabCanvasComponent canvasAfter))
            {
                return;
            }

            Commands.RecordResizePrefabInstanceEdit(_dragEntity, capture.TransformHandle, capture.TransformBefore, transformAfterResize, capture.PrefabCanvasHandle, capture.PrefabCanvasBefore, canvasAfter);
            return;
        }

        if (capture.HasTransform == 0 || capture.TransformHandle.IsNull)
        {
            return;
        }

        if (!TrySnapshotTransform(capture.TransformHandle, out TransformComponent transformAfter))
        {
            return;
        }

        if (capture.HasRectGeometry != 0 && !capture.RectGeometryHandle.IsNull)
        {
            if (TrySnapshotRectGeometry(capture.RectGeometryHandle, out RectGeometryComponent rectAfter))
            {
                Commands.RecordResizeRectShapeEdit(capture.TransformHandle, capture.TransformBefore, transformAfter, capture.RectGeometryHandle, capture.RectGeometryBefore, rectAfter);
            }
            return;
        }

        if (capture.HasCircleGeometry != 0 && !capture.CircleGeometryHandle.IsNull)
        {
            if (TrySnapshotCircleGeometry(capture.CircleGeometryHandle, out CircleGeometryComponent circleAfter))
            {
                Commands.RecordResizeCircleShapeEdit(capture.TransformHandle, capture.TransformBefore, transformAfter, capture.CircleGeometryHandle, capture.CircleGeometryBefore, circleAfter);
            }
            return;
        }

        Commands.RecordTransformComponentEdit(capture.TransformHandle, capture.TransformBefore, transformAfter);
    }

    private bool TrySnapshotTransform(TransformComponentHandle handle, out TransformComponent snapshot)
    {
        snapshot = default;
        if (handle.IsNull)
        {
            return false;
        }

        var view = TransformComponent.Api.FromHandle(_propertyWorld, handle);
        if (!view.IsAlive)
        {
            return false;
        }

        snapshot = new TransformComponent
        {
            Position = view.Position,
            Scale = view.Scale,
            Rotation = view.Rotation,
            Anchor = view.Anchor,
            Depth = view.Depth
        };
        return true;
    }

    private bool TrySnapshotRectGeometry(RectGeometryComponentHandle handle, out RectGeometryComponent snapshot)
    {
        snapshot = default;
        if (handle.IsNull)
        {
            return false;
        }

        var view = RectGeometryComponent.Api.FromHandle(_propertyWorld, handle);
        if (!view.IsAlive)
        {
            return false;
        }

        snapshot = new RectGeometryComponent
        {
            Size = view.Size,
            CornerRadius = view.CornerRadius
        };
        return true;
    }

    private bool TrySnapshotPrefabCanvas(PrefabCanvasComponentHandle handle, out PrefabCanvasComponent snapshot)
    {
        snapshot = default;
        if (handle.IsNull)
        {
            return false;
        }

        var view = PrefabCanvasComponent.Api.FromHandle(_propertyWorld, handle);
        if (!view.IsAlive)
        {
            return false;
        }

        snapshot = new PrefabCanvasComponent
        {
            Size = view.Size
        };
        return true;
    }

    private bool TryBeginResizeSelectedPrefabInstance(Vector2 mouseWorld, Vector2 mouseCanvas, Vector2 canvasOrigin)
    {
        if (_selectedEntities.Count != 1)
        {
            return false;
        }

        EntityId instanceEntity = _selectedEntities[0];
        if (IsEntityLockedInEditor(instanceEntity) || IsEntityHiddenInEditor(instanceEntity))
        {
            return false;
        }

        if (_world.GetNodeType(instanceEntity) != UiNodeType.PrefabInstance)
        {
            return false;
        }

        if (_world.HasComponent(instanceEntity, ListGeneratedComponent.Api.PoolIdConst))
        {
            return false;
        }

        if (!_world.TryGetComponent(instanceEntity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny) || !transformAny.IsValid)
        {
            return false;
        }

        if (!_world.TryGetComponent(instanceEntity, PrefabCanvasComponent.Api.PoolIdConst, out AnyComponentHandle canvasAny) || !canvasAny.IsValid)
        {
            return false;
        }

        var transformHandle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
        if (!TrySnapshotTransform(transformHandle, out TransformComponent transformBefore))
        {
            return false;
        }

        var canvasHandle = new PrefabCanvasComponentHandle(canvasAny.Index, canvasAny.Generation);
        if (!TrySnapshotPrefabCanvas(canvasHandle, out PrefabCanvasComponent canvasBefore))
        {
            return false;
        }

        if (!TryGetPrefabInstanceRectCanvasForOutlineEcs(instanceEntity, canvasOrigin, out var centerCanvas, out var halfSizeCanvas, out float rotationRadians, out _))
        {
            return false;
        }

        var rectCanvas = new ImRect(centerCanvas.X - halfSizeCanvas.X, centerCanvas.Y - halfSizeCanvas.Y, halfSizeCanvas.X * 2f, halfSizeCanvas.Y * 2f);
        if (!TryGetHandleAtMouse(rectCanvas, centerCanvas, rotationRadians, mouseCanvas, out ShapeHandle handle))
        {
            return false;
        }

        if (!TryGetTransformParentWorldTransformEcs(instanceEntity, out WorldTransform parentWorldTransform))
        {
            parentWorldTransform = IdentityWorldTransform;
        }

        Vector2 mouseParentLocal = InverseTransformPoint(parentWorldTransform, mouseWorld);
        _resizeAnchorWorld = transformBefore.Position;
        _resizeRotationRadians = transformBefore.Rotation * (MathF.PI / 180f);
        _resizeStartMouseLocal = RotateVector(mouseParentLocal - _resizeAnchorWorld, -_resizeRotationRadians);

        _dragKind = DragKind.ResizeNode;
        _dragEntity = instanceEntity;
        _dragPrefabIndex = -1;
        _dragShapeIndex = -1;
        _dragGroupIndex = -1;
        _dragTextIndex = -1;
        _resizeHandle = handle;
        _resizeStartRectWorld = RectTransformMath.GetRectFromPivot(transformBefore.Position, transformBefore.Anchor, canvasBefore.Size);
        _resizeStartMouseWorld = mouseParentLocal;
        _dragLastMouseWorld = mouseWorld;

        _canvasEditCapture = new CanvasEditCapture
        {
            Active = 1,
            Kind = DragKind.ResizeNode,
            TransformHandle = transformHandle,
            TransformBefore = transformBefore,
            HasTransform = 1,
            PrefabCanvasHandle = canvasHandle,
            PrefabCanvasBefore = canvasBefore,
            HasPrefabCanvas = 1
        };

        return true;
    }

    private void ResizePrefabInstance(EntityId instanceEntity, ShapeHandle handle, ImRect startRectParentLocal, Vector2 currentMouseParentLocal)
    {
        if (handle == ShapeHandle.None || instanceEntity.IsNull)
        {
            return;
        }

        if (!_world.TryGetComponent(instanceEntity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny) || !transformAny.IsValid)
        {
            return;
        }

        if (!_world.TryGetComponent(instanceEntity, PrefabCanvasComponent.Api.PoolIdConst, out AnyComponentHandle canvasAny) || !canvasAny.IsValid)
        {
            return;
        }

        var transformHandle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
        var transform = TransformComponent.Api.FromHandle(_propertyWorld, transformHandle);
        if (!transform.IsAlive)
        {
            return;
        }

        var canvasHandle = new PrefabCanvasComponentHandle(canvasAny.Index, canvasAny.Generation);
        var canvas = PrefabCanvasComponent.Api.FromHandle(_propertyWorld, canvasHandle);
        if (!canvas.IsAlive)
        {
            return;
        }

        Vector2 currentMouseLocal = RotateVector(currentMouseParentLocal - _resizeAnchorWorld, -_resizeRotationRadians);
        Vector2 delta = currentMouseLocal - _resizeStartMouseLocal;

        float left = startRectParentLocal.X;
        float top = startRectParentLocal.Y;
        float right = startRectParentLocal.Right;
        float bottom = startRectParentLocal.Bottom;

        if (handle == ShapeHandle.TopLeft || handle == ShapeHandle.Left || handle == ShapeHandle.BottomLeft)
        {
            left += delta.X;
        }
        if (handle == ShapeHandle.TopRight || handle == ShapeHandle.Right || handle == ShapeHandle.BottomRight)
        {
            right += delta.X;
        }
        if (handle == ShapeHandle.TopLeft || handle == ShapeHandle.Top || handle == ShapeHandle.TopRight)
        {
            top += delta.Y;
        }
        if (handle == ShapeHandle.BottomLeft || handle == ShapeHandle.Bottom || handle == ShapeHandle.BottomRight)
        {
            bottom += delta.Y;
        }

        if (left > right)
        {
            float temp = left;
            left = right;
            right = temp;
        }

        if (top > bottom)
        {
            float temp = top;
            top = bottom;
            bottom = temp;
        }

        const float minSize = 1f;
        float width = right - left;
        float height = bottom - top;
        if (width < minSize)
        {
            right = left + minSize;
            width = minSize;
        }
        if (height < minSize)
        {
            bottom = top + minSize;
            height = minSize;
        }

        var rect = new ImRect(left, top, width, height);
        Vector2 pivotPos = RectTransformMath.GetPivotPosFromRect(rect, transform.Anchor);
        transform.Position = pivotPos;
        canvas.Size = new Vector2(width, height);
    }

    private bool TrySnapshotCircleGeometry(CircleGeometryComponentHandle handle, out CircleGeometryComponent snapshot)
    {
        snapshot = default;
        if (handle.IsNull)
        {
            return false;
        }

        var view = CircleGeometryComponent.Api.FromHandle(_propertyWorld, handle);
        if (!view.IsAlive)
        {
            return false;
        }

        snapshot = new CircleGeometryComponent
        {
            Radius = view.Radius
        };
        return true;
    }

    private bool TryBeginResizeSelectedShape(Vector2 mouseWorld, Vector2 mouseCanvas, Vector2 canvasOrigin)
    {
        if (_selectedEntities.Count != 1)
        {
            return false;
        }

        EntityId shapeEntity = _selectedEntities[0];
        if (IsEntityLockedInEditor(shapeEntity) || IsEntityHiddenInEditor(shapeEntity))
        {
            return false;
        }

        UiNodeType nodeType = _world.GetNodeType(shapeEntity);
        if (nodeType == UiNodeType.Text)
        {
            if (!TryGetLegacyTextId(shapeEntity, out int textId) || !TryGetTextIndexById(textId, out int textIndex))
            {
                return false;
            }

            var text = _texts[textIndex];

            if (!TryGetTransformParentWorldTransformEcs(shapeEntity, out WorldTransform textParentWorldTransform))
            {
                textParentWorldTransform = IdentityWorldTransform;
            }

            if (!TryGetTextWorldTransformEcs(shapeEntity, textParentWorldTransform, out ShapeWorldTransform textWorldTransform))
            {
                return false;
            }

            ImRect textRectCanvas = GetShapeRectCanvasForDrawEcs(shapeEntity, ShapeKind.Rect, text.RectGeometry, default, canvasOrigin, textWorldTransform, out Vector2 textCenterCanvas, out float textRotationRadiansWorld);
            if (!TryGetHandleAtMouse(textRectCanvas, textCenterCanvas, textRotationRadiansWorld, mouseCanvas, out ShapeHandle textHandle))
            {
                return false;
            }

            var textTransform = TransformComponent.Api.FromHandle(_propertyWorld, text.Transform);
            if (!textTransform.IsAlive)
            {
                return false;
            }

            Vector2 mouseTextParentLocal = InverseTransformPoint(textParentWorldTransform, mouseWorld);
            _resizeAnchorWorld = textTransform.Position;
            _resizeRotationRadians = textTransform.Rotation * (MathF.PI / 180f);
            _resizeStartMouseLocal = RotateVector(mouseTextParentLocal - _resizeAnchorWorld, -_resizeRotationRadians);

            _dragKind = DragKind.ResizeText;
            _dragTextIndex = textIndex;
            _dragPrefabIndex = -1;
            _dragShapeIndex = -1;
            _dragGroupIndex = -1;
            _resizeHandle = textHandle;
            _resizeStartRectWorld = GetTextRectWorld(text);
            _resizeStartMouseWorld = mouseTextParentLocal;
            _dragLastMouseWorld = mouseWorld;
            BeginCanvasEditCaptureForText(textIndex, DragKind.ResizeText, includeGeometry: true);
            return true;
        }

        if (nodeType != UiNodeType.Shape)
        {
            return false;
        }

        if (!TryGetLegacyShapeId(shapeEntity, out int shapeId) || !TryGetShapeIndexById(shapeId, out int shapeIndex))
        {
            return false;
        }

        var shape = _shapes[shapeIndex];
        ShapeKind kind = GetShapeKind(shape);
        if (kind == ShapeKind.Polygon)
        {
            return false;
        }

        if (!TryGetTransformParentWorldTransformEcs(shapeEntity, out WorldTransform parentWorldTransform))
        {
            parentWorldTransform = IdentityWorldTransform;
        }

        if (!TryGetShapeWorldTransform(shape, parentWorldTransform, out ShapeWorldTransform worldTransform))
        {
            return false;
        }

        ImRect rectCanvas = GetShapeRectCanvasForDraw(shape, canvasOrigin, worldTransform, out var centerCanvas, out float rotationRadiansWorld);
        if (!TryGetHandleAtMouse(rectCanvas, centerCanvas, rotationRadiansWorld, mouseCanvas, out ShapeHandle handle))
        {
            return false;
        }

        var transform = TransformComponent.Api.FromHandle(_propertyWorld, shape.Transform);
        Vector2 mouseParentLocal = InverseTransformPoint(parentWorldTransform, mouseWorld);
        _resizeAnchorWorld = transform.Position;
        _resizeRotationRadians = transform.Rotation * (MathF.PI / 180f);
        _resizeStartMouseLocal = RotateVector(mouseParentLocal - _resizeAnchorWorld, -_resizeRotationRadians);

        _dragKind = DragKind.ResizeShape;
        _dragShapeIndex = shapeIndex;
        _dragPrefabIndex = -1;
        _dragGroupIndex = -1;
        _resizeHandle = handle;
        _resizeStartRectWorld = GetShapeRectWorld(shape);
        _resizeStartMouseWorld = mouseParentLocal;
        _dragLastMouseWorld = mouseWorld;
        BeginCanvasEditCaptureForShape(shapeIndex, DragKind.ResizeShape, includeGeometry: true);
        return true;
    }

    private bool TryBeginRotateSelectedShape(Vector2 mouseWorld, Vector2 mouseCanvas, Vector2 canvasOrigin)
    {
        if (_selectedEntities.Count != 1)
        {
            return false;
        }

        EntityId shapeEntity = _selectedEntities[0];
        if (IsEntityLockedInEditor(shapeEntity) || IsEntityHiddenInEditor(shapeEntity))
        {
            return false;
        }

        UiNodeType nodeType = _world.GetNodeType(shapeEntity);
        if (nodeType == UiNodeType.Text)
        {
            if (!TryGetLegacyTextId(shapeEntity, out int textId) || !TryGetTextIndexById(textId, out int textIndex))
            {
                return false;
            }

            var text = _texts[textIndex];

            if (!TryGetTransformParentWorldTransformEcs(shapeEntity, out WorldTransform textParentWorldTransform))
            {
                textParentWorldTransform = IdentityWorldTransform;
            }

            if (!TryGetTextWorldTransformEcs(shapeEntity, textParentWorldTransform, out ShapeWorldTransform textWorldTransform))
            {
                return false;
            }

            ImRect textRectCanvas = GetShapeRectCanvasForDrawEcs(shapeEntity, ShapeKind.Rect, text.RectGeometry, default, canvasOrigin, textWorldTransform, out Vector2 textCenterCanvas, out float textRotationRadians);
            float textHw = textRectCanvas.Width * 0.5f;
            float textHh = textRectCanvas.Height * 0.5f;

            if (!TryGetGroupRotationHandleAtMouse(textCenterCanvas, new Vector2(textHw, textHh), textRotationRadians, mouseCanvas))
            {
                return false;
            }

            var textTransform = TransformComponent.Api.FromHandle(_propertyWorld, text.Transform);
            if (!textTransform.IsAlive)
            {
                return false;
            }

            _dragKind = DragKind.RotateText;
            _dragTextIndex = textIndex;
            _dragPrefabIndex = -1;
            _dragShapeIndex = -1;
            _dragGroupIndex = -1;
            _dragLastMouseWorld = mouseWorld;
            BeginCanvasEditCaptureForText(textIndex, DragKind.RotateText, includeGeometry: false);

            Vector2 mouseParentLocalRotate = InverseTransformPoint(textParentWorldTransform, mouseWorld);
            _rotateTextPivotWorld = textTransform.Position;
            _rotateTextStartRotationDegrees = textTransform.Rotation;
            _rotateTextStartMouseAngleRadians = MathF.Atan2(mouseParentLocalRotate.Y - _rotateTextPivotWorld.Y, mouseParentLocalRotate.X - _rotateTextPivotWorld.X);
            return true;
        }

        if (nodeType != UiNodeType.Shape)
        {
            return false;
        }

        if (!TryGetLegacyShapeId(shapeEntity, out int shapeId) || !TryGetShapeIndexById(shapeId, out int shapeIndex))
        {
            return false;
        }

        var shape = _shapes[shapeIndex];
        ShapeKind kind = GetShapeKind(shape);
        if (kind == ShapeKind.Polygon)
        {
            return false;
        }

        if (!TryGetTransformParentWorldTransformEcs(shapeEntity, out WorldTransform parentWorldTransform))
        {
            parentWorldTransform = IdentityWorldTransform;
        }

        if (!TryGetShapeWorldTransform(shape, parentWorldTransform, out ShapeWorldTransform worldTransform))
        {
            return false;
        }

        ImRect rectCanvas = GetShapeRectCanvasForDraw(shape, canvasOrigin, worldTransform, out var centerCanvas, out float rotationRadians);
        float hw = rectCanvas.Width * 0.5f;
        float hh = rectCanvas.Height * 0.5f;

        if (!TryGetGroupRotationHandleAtMouse(centerCanvas, new Vector2(hw, hh), rotationRadians, mouseCanvas))
        {
            return false;
        }

        var transform = TransformComponent.Api.FromHandle(_propertyWorld, shape.Transform);
        if (!transform.IsAlive)
        {
            return false;
        }

        _dragKind = DragKind.RotateShape;
        _dragShapeIndex = shapeIndex;
        _dragPrefabIndex = -1;
        _dragGroupIndex = -1;
        _dragLastMouseWorld = mouseWorld;
        BeginCanvasEditCaptureForShape(shapeIndex, DragKind.RotateShape, includeGeometry: false);

        Vector2 mouseParentLocal = InverseTransformPoint(parentWorldTransform, mouseWorld);
        _rotateShapePivotWorld = transform.Position;
        _rotateShapeStartRotationDegrees = transform.Rotation;
        _rotateShapeStartMouseAngleRadians = MathF.Atan2(mouseParentLocal.Y - _rotateShapePivotWorld.Y, mouseParentLocal.X - _rotateShapePivotWorld.X);
        return true;
    }

    private bool TryBeginTransformSelectedGroup(Vector2 mouseWorld, Vector2 mouseCanvas, Vector2 canvasOrigin)
    {
        if (_selectedEntities.Count != 1)
        {
            return false;
        }

        EntityId groupEntity = _selectedEntities[0];
        if (IsEntityLockedInEditor(groupEntity) || IsEntityHiddenInEditor(groupEntity))
        {
            return false;
        }

        if (_world.GetNodeType(groupEntity) != UiNodeType.BooleanGroup)
        {
            return false;
        }

        if (!TryGetLegacyGroupId(groupEntity, out int groupId) || !TryGetGroupIndexById(groupId, out int groupIndex))
        {
            return false;
        }

        var group = _groups[groupIndex];
        EnsureGroupTransformInitialized(ref group);
        _groups[groupIndex] = group;

        if (group.Transform.IsNull)
        {
            return false;
        }

        if (!TryGetGroupRectCanvasForOutlineEcs(groupEntity, canvasOrigin, out var centerCanvas, out var halfSizeCanvas, out float rotationRadians, out WorldTransform groupWorldTransform, out var boundsLocal))
        {
            return false;
        }

        var transform = TransformComponent.Api.FromHandle(_propertyWorld, group.Transform);
        if (!transform.IsAlive)
        {
            return false;
        }

        if (TryGetGroupRotationHandleAtMouse(centerCanvas, halfSizeCanvas, rotationRadians, mouseCanvas))
        {
            _dragKind = DragKind.RotateGroup;
            _dragGroupIndex = groupIndex;
            _dragPrefabIndex = -1;
            _dragShapeIndex = -1;
            _dragLastMouseWorld = mouseWorld;
            BeginCanvasEditCaptureForGroup(groupIndex, DragKind.RotateGroup);

            _rotateGroupPivotWorld = groupWorldTransform.OriginWorld;
            _rotateGroupStartRotationDegrees = transform.Rotation;
            _rotateGroupStartMouseAngleRadians = MathF.Atan2(mouseWorld.Y - _rotateGroupPivotWorld.Y, mouseWorld.X - _rotateGroupPivotWorld.X);
            return true;
        }

        var rectCanvas = new ImRect(
            centerCanvas.X - halfSizeCanvas.X,
            centerCanvas.Y - halfSizeCanvas.Y,
            halfSizeCanvas.X * 2f,
            halfSizeCanvas.Y * 2f);

        if (!TryGetHandleAtMouse(rectCanvas, centerCanvas, rotationRadians, mouseCanvas, out ShapeHandle handle))
        {
            return false;
        }

        _dragKind = DragKind.ResizeGroup;
        _dragGroupIndex = groupIndex;
        _dragPrefabIndex = -1;
        _dragShapeIndex = -1;
        _resizeHandle = handle;
        _dragLastMouseWorld = mouseWorld;
        BeginCanvasEditCaptureForGroup(groupIndex, DragKind.ResizeGroup);

        _resizeGroupUsesRectSize = false;

        bool hasMask = _world.TryGetComponent(groupEntity, MaskGroupComponent.Api.PoolIdConst, out _);
        bool hasBoolean = _world.TryGetComponent(groupEntity, BooleanGroupComponent.Api.PoolIdConst, out _);
        bool isPlainGroup = !hasMask && !hasBoolean;

        if (isPlainGroup &&
            _world.TryGetComponent(groupEntity, RectGeometryComponent.Api.PoolIdConst, out AnyComponentHandle rectAny))
        {
            var rectHandle = new RectGeometryComponentHandle(rectAny.Index, rectAny.Generation);
            var rect = RectGeometryComponent.Api.FromHandle(_propertyWorld, rectHandle);
            if (rect.IsAlive && rect.Size.X > 0f && rect.Size.Y > 0f)
            {
                WorldTransform parentWorldTransform = IdentityWorldTransform;
                if (!TryGetTransformParentWorldTransformEcs(groupEntity, out parentWorldTransform))
                {
                    parentWorldTransform = IdentityWorldTransform;
                }

                Vector2 mouseParentLocal = InverseTransformPoint(parentWorldTransform, mouseWorld);

                _resizeGroupUsesRectSize = true;
                _resizeGroupAnchorParentLocal = transform.Position;
                _resizeGroupRotationRadians = transform.Rotation * (MathF.PI / 180f);
                _resizeGroupStartRectParentLocal = RectTransformMath.GetRectFromPivot(_resizeGroupAnchorParentLocal, transform.Anchor, rect.Size);
                _resizeGroupStartMouseLocal = RotateVector(mouseParentLocal - _resizeGroupAnchorParentLocal, -_resizeGroupRotationRadians);
                _resizeGroupStartScale = Vector2.One;
                _resizeGroupPivotWorld = groupWorldTransform.OriginWorld;
                _resizeGroupStartBoundsLocal = boundsLocal;
                return true;
            }
        }

        _resizeGroupStartBoundsLocal = boundsLocal;
        _resizeGroupPivotWorld = groupWorldTransform.OriginWorld;
        _resizeGroupRotationRadians = rotationRadians;
        _resizeGroupStartMouseLocal = RotateVector(mouseWorld - _resizeGroupPivotWorld, -_resizeGroupRotationRadians);

        Vector2 scale = transform.Scale;
        float scaleX = scale.X == 0f ? 1f : scale.X;
        float scaleY = scale.Y == 0f ? 1f : scale.Y;
        _resizeGroupStartScale = new Vector2(scaleX, scaleY);
        return true;
    }

    private static bool TryGetGroupRotationHandleAtMouse(Vector2 centerCanvas, Vector2 halfSizeCanvas, float rotationRadians, Vector2 mouseCanvas)
    {
        const float handleRadius = 7f;
        const float handleOffset = 26f;

        Vector2 pos = centerCanvas + RotateVector(new Vector2(0f, -halfSizeCanvas.Y - handleOffset), rotationRadians);
        float dx = mouseCanvas.X - pos.X;
        float dy = mouseCanvas.Y - pos.Y;
        return dx * dx + dy * dy <= handleRadius * handleRadius;
    }

    private static bool TryGetHandleAtMouse(ImRect rectCanvas, Vector2 centerCanvas, float rotationRadians, Vector2 mouseCanvas, out ShapeHandle handle)
    {
        // Handle hit targets are in canvas space (pixels).
        const float handleSize = 10f;
        float half = handleSize * 0.5f;

        float hw = rectCanvas.Width * 0.5f;
        float hh = rectCanvas.Height * 0.5f;

        Vector2 offset = RotateVector(new Vector2(-hw, -hh), rotationRadians);
        float x = centerCanvas.X + offset.X;
        float y = centerCanvas.Y + offset.Y;
        if (new ImRect(x - half, y - half, handleSize, handleSize).Contains(mouseCanvas))
        {
            handle = ShapeHandle.TopLeft;
            return true;
        }

        offset = RotateVector(new Vector2(hw, -hh), rotationRadians);
        x = centerCanvas.X + offset.X;
        y = centerCanvas.Y + offset.Y;
        if (new ImRect(x - half, y - half, handleSize, handleSize).Contains(mouseCanvas))
        {
            handle = ShapeHandle.TopRight;
            return true;
        }

        offset = RotateVector(new Vector2(hw, hh), rotationRadians);
        x = centerCanvas.X + offset.X;
        y = centerCanvas.Y + offset.Y;
        if (new ImRect(x - half, y - half, handleSize, handleSize).Contains(mouseCanvas))
        {
            handle = ShapeHandle.BottomRight;
            return true;
        }

        offset = RotateVector(new Vector2(-hw, hh), rotationRadians);
        x = centerCanvas.X + offset.X;
        y = centerCanvas.Y + offset.Y;
        if (new ImRect(x - half, y - half, handleSize, handleSize).Contains(mouseCanvas))
        {
            handle = ShapeHandle.BottomLeft;
            return true;
        }

        offset = RotateVector(new Vector2(0f, -hh), rotationRadians);
        x = centerCanvas.X + offset.X;
        y = centerCanvas.Y + offset.Y;
        if (new ImRect(x - half, y - half, handleSize, handleSize).Contains(mouseCanvas))
        {
            handle = ShapeHandle.Top;
            return true;
        }

        offset = RotateVector(new Vector2(hw, 0f), rotationRadians);
        x = centerCanvas.X + offset.X;
        y = centerCanvas.Y + offset.Y;
        if (new ImRect(x - half, y - half, handleSize, handleSize).Contains(mouseCanvas))
        {
            handle = ShapeHandle.Right;
            return true;
        }

        offset = RotateVector(new Vector2(0f, hh), rotationRadians);
        x = centerCanvas.X + offset.X;
        y = centerCanvas.Y + offset.Y;
        if (new ImRect(x - half, y - half, handleSize, handleSize).Contains(mouseCanvas))
        {
            handle = ShapeHandle.Bottom;
            return true;
        }

        offset = RotateVector(new Vector2(-hw, 0f), rotationRadians);
        x = centerCanvas.X + offset.X;
        y = centerCanvas.Y + offset.Y;
        if (new ImRect(x - half, y - half, handleSize, handleSize).Contains(mouseCanvas))
        {
            handle = ShapeHandle.Left;
            return true;
        }

        handle = ShapeHandle.None;
        return false;
    }

    private void ResizeShape(int shapeIndex, ShapeHandle handle, ImRect startRectWorld, Vector2 currentMouseWorld)
    {
        if (handle == ShapeHandle.None)
        {
            return;
        }

        var shape = _shapes[shapeIndex];
        ShapeKind kind = GetShapeKind(shape);
        if (kind == ShapeKind.Polygon)
        {
            return;
        }

        var transform = TransformComponent.Api.FromHandle(_propertyWorld, shape.Transform);
        if (!transform.IsAlive)
        {
            return;
        }

        Vector2 beforePosition = transform.Position;
        Vector2 beforeRectSize = default;
        float beforeCircleRadius = 0f;
        if (kind == ShapeKind.Rect)
        {
            var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, shape.RectGeometry);
            beforeRectSize = rectGeometry.Size;
        }
        else if (kind == ShapeKind.Circle)
        {
            var circleGeometry = CircleGeometryComponent.Api.FromHandle(_propertyWorld, shape.CircleGeometry);
            beforeCircleRadius = circleGeometry.Radius;
        }

        Vector2 currentMouseLocal = RotateVector(currentMouseWorld - _resizeAnchorWorld, -_resizeRotationRadians);
        Vector2 delta = currentMouseLocal - _resizeStartMouseLocal;

        float left = startRectWorld.X;
        float top = startRectWorld.Y;
        float right = startRectWorld.Right;
        float bottom = startRectWorld.Bottom;

        if (handle == ShapeHandle.TopLeft || handle == ShapeHandle.Left || handle == ShapeHandle.BottomLeft)
        {
            left += delta.X;
        }
        if (handle == ShapeHandle.TopRight || handle == ShapeHandle.Right || handle == ShapeHandle.BottomRight)
        {
            right += delta.X;
        }
        if (handle == ShapeHandle.TopLeft || handle == ShapeHandle.Top || handle == ShapeHandle.TopRight)
        {
            top += delta.Y;
        }
        if (handle == ShapeHandle.BottomLeft || handle == ShapeHandle.Bottom || handle == ShapeHandle.BottomRight)
        {
            bottom += delta.Y;
        }

        // Normalize and enforce min size.
        if (left > right)
        {
            float temp = left;
            left = right;
            right = temp;
        }

        if (top > bottom)
        {
            float temp = top;
            top = bottom;
            bottom = temp;
        }

        const float minSize = 6f;
        float width = right - left;
        float height = bottom - top;

        if (kind == ShapeKind.Circle)
        {
            float size = Math.Max(width, height);
            if (size < minSize)
            {
                size = minSize;
            }

            // Keep the opposite corner/edge anchored by expanding towards the dragged side.
            if (handle == ShapeHandle.TopLeft || handle == ShapeHandle.Left || handle == ShapeHandle.Top)
            {
                right = left + size;
                bottom = top + size;
            }
            else
            {
                left = right - size;
                top = bottom - size;
            }
        }
        else
        {
            if (width < minSize)
            {
                right = left + minSize;
            }
            if (height < minSize)
            {
                bottom = top + minSize;
            }
        }

        ImRect rectWorld = new ImRect(left, top, right - left, bottom - top);
        SetShapeRectWorld(ref shape, rectWorld);
        _shapes[shapeIndex] = shape;

        Vector2 afterPosition = transform.Position;
        if (beforePosition.X != afterPosition.X || beforePosition.Y != afterPosition.Y)
        {
            if (TryGetTransformPropertySlot(shape.Transform, TransformPositionNameHandle, PropertyKind.Vec2, out PropertySlot slot) &&
                TryGetEntityById(_shapeEntityById, shape.Id, out EntityId shapeEntity))
            {
                Commands.NotifyCanvasPropertyEdited(shapeEntity, slot, PropertyValue.FromVec2(beforePosition), PropertyValue.FromVec2(afterPosition));
            }
        }

        if (kind == ShapeKind.Rect)
        {
            var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, shape.RectGeometry);
            Vector2 afterSize = rectGeometry.Size;
            if (beforeRectSize.X != afterSize.X || beforeRectSize.Y != afterSize.Y)
            {
                AnyComponentHandle component = RectGeometryComponentProperties.ToAnyHandle(shape.RectGeometry);
                PropertySlot slot = PropertyDispatcher.GetSlot(component, 0);
                if (TryGetEntityById(_shapeEntityById, shape.Id, out EntityId shapeEntity))
                {
                    Commands.NotifyCanvasPropertyEdited(shapeEntity, slot, PropertyValue.FromVec2(beforeRectSize), PropertyValue.FromVec2(afterSize));
                }
            }
        }
        else if (kind == ShapeKind.Circle)
        {
            var circleGeometry = CircleGeometryComponent.Api.FromHandle(_propertyWorld, shape.CircleGeometry);
            float afterRadius = circleGeometry.Radius;
            if (beforeCircleRadius != afterRadius)
            {
                AnyComponentHandle component = CircleGeometryComponentProperties.ToAnyHandle(shape.CircleGeometry);
                PropertySlot slot = PropertyDispatcher.GetSlot(component, 0);
                if (TryGetEntityById(_shapeEntityById, shape.Id, out EntityId shapeEntity))
                {
                    Commands.NotifyCanvasPropertyEdited(shapeEntity, slot, PropertyValue.FromFloat(beforeCircleRadius), PropertyValue.FromFloat(afterRadius));
                }
            }
        }
    }

    private void ResizeGroup(int groupIndex, ShapeHandle handle, Vector2 currentMouseWorld)
    {
        if (handle == ShapeHandle.None)
        {
            return;
        }

        var group = _groups[groupIndex];
        EnsureGroupTransformInitialized(ref group);
        _groups[groupIndex] = group;

        if (group.Transform.IsNull)
        {
            return;
        }

        EntityId groupEntity = EntityId.Null;
        TryGetEntityById(_groupEntityById, group.Id, out groupEntity);

        var transform = TransformComponent.Api.FromHandle(_propertyWorld, group.Transform);
        if (!transform.IsAlive)
        {
            return;
        }

        if (_resizeGroupUsesRectSize && !groupEntity.IsNull &&
            !_world.TryGetComponent(groupEntity, MaskGroupComponent.Api.PoolIdConst, out _) &&
            !_world.TryGetComponent(groupEntity, BooleanGroupComponent.Api.PoolIdConst, out _) &&
            _world.TryGetComponent(groupEntity, RectGeometryComponent.Api.PoolIdConst, out AnyComponentHandle rectAny))
        {
            var rectHandle = new RectGeometryComponentHandle(rectAny.Index, rectAny.Generation);
            var rect = RectGeometryComponent.Api.FromHandle(_propertyWorld, rectHandle);
            if (!rect.IsAlive)
            {
                return;
            }

            Vector2 beforePosition = transform.Position;
            Vector2 beforeSize = rect.Size;

            WorldTransform parentWorldTransform = IdentityWorldTransform;
            if (!TryGetTransformParentWorldTransformEcs(groupEntity, out parentWorldTransform))
            {
                parentWorldTransform = IdentityWorldTransform;
            }

            Vector2 mouseParentLocal = InverseTransformPoint(parentWorldTransform, currentMouseWorld);
            Vector2 mouseParentLocalRotated = RotateVector(mouseParentLocal - _resizeGroupAnchorParentLocal, -_resizeGroupRotationRadians);
            Vector2 deltaLocal = mouseParentLocalRotated - _resizeGroupStartMouseLocal;

            float left = _resizeGroupStartRectParentLocal.X;
            float top = _resizeGroupStartRectParentLocal.Y;
            float right = _resizeGroupStartRectParentLocal.Right;
            float bottom = _resizeGroupStartRectParentLocal.Bottom;

            if (handle == ShapeHandle.TopLeft || handle == ShapeHandle.Left || handle == ShapeHandle.BottomLeft)
            {
                left += deltaLocal.X;
            }
            if (handle == ShapeHandle.TopRight || handle == ShapeHandle.Right || handle == ShapeHandle.BottomRight)
            {
                right += deltaLocal.X;
            }
            if (handle == ShapeHandle.TopLeft || handle == ShapeHandle.Top || handle == ShapeHandle.TopRight)
            {
                top += deltaLocal.Y;
            }
            if (handle == ShapeHandle.BottomLeft || handle == ShapeHandle.Bottom || handle == ShapeHandle.BottomRight)
            {
                bottom += deltaLocal.Y;
            }

            if (left > right)
            {
                float temp = left;
                left = right;
                right = temp;
            }

            if (top > bottom)
            {
                float temp = top;
                top = bottom;
                bottom = temp;
            }

            const float minSize = 6f;
            if (right - left < minSize)
            {
                right = left + minSize;
            }

            if (bottom - top < minSize)
            {
                bottom = top + minSize;
            }

            var rectParentLocal = new ImRect(left, top, right - left, bottom - top);
            Vector2 newSize = new Vector2(rectParentLocal.Width, rectParentLocal.Height);
            rect.Size = newSize;
            transform.Position = RectTransformMath.GetPivotPosFromRect(rectParentLocal, transform.Anchor);

            Vector2 afterPosition = transform.Position;
            if (beforePosition.X != afterPosition.X || beforePosition.Y != afterPosition.Y)
            {
                if (TryGetTransformPropertySlot(group.Transform, TransformPositionNameHandle, PropertyKind.Vec2, out PropertySlot slot))
                {
                    Commands.NotifyCanvasPropertyEdited(groupEntity, slot, PropertyValue.FromVec2(beforePosition), PropertyValue.FromVec2(afterPosition));
                }
            }

            Vector2 afterSize = rect.Size;
            if (beforeSize.X != afterSize.X || beforeSize.Y != afterSize.Y)
            {
                AnyComponentHandle component = RectGeometryComponentProperties.ToAnyHandle(rectHandle);
                PropertySlot slot = PropertyDispatcher.GetSlot(component, 0);
                Commands.NotifyCanvasPropertyEdited(groupEntity, slot, PropertyValue.FromVec2(beforeSize), PropertyValue.FromVec2(afterSize));
            }

            return;
        }

        Vector2 beforeScale = transform.Scale;

        Vector2 currentMouseLocal = RotateVector(currentMouseWorld - _resizeGroupPivotWorld, -_resizeGroupRotationRadians);

        float startLeft = _resizeGroupStartBoundsLocal.X;
        float startTop = _resizeGroupStartBoundsLocal.Y;
        float startRight = _resizeGroupStartBoundsLocal.Right;
        float startBottom = _resizeGroupStartBoundsLocal.Bottom;

        float startCenterX = (startLeft + startRight) * 0.5f;
        float startCenterY = (startTop + startBottom) * 0.5f;

        float startHalfW = (startRight - startLeft) * 0.5f;
        float startHalfH = (startBottom - startTop) * 0.5f;
        if (startHalfW <= 0.0001f || startHalfH <= 0.0001f)
        {
            return;
        }

        float halfW = startHalfW;
        float halfH = startHalfH;

        bool affectsX =
            handle == ShapeHandle.TopLeft || handle == ShapeHandle.Left || handle == ShapeHandle.BottomLeft ||
            handle == ShapeHandle.TopRight || handle == ShapeHandle.Right || handle == ShapeHandle.BottomRight;
        bool affectsY =
            handle == ShapeHandle.TopLeft || handle == ShapeHandle.Top || handle == ShapeHandle.TopRight ||
            handle == ShapeHandle.BottomLeft || handle == ShapeHandle.Bottom || handle == ShapeHandle.BottomRight;

        if (affectsX)
        {
            halfW = MathF.Abs(currentMouseLocal.X - startCenterX);
        }
        if (affectsY)
        {
            halfH = MathF.Abs(currentMouseLocal.Y - startCenterY);
        }

        const float minHalf = 3f;
        if (halfW < minHalf) halfW = minHalf;
        if (halfH < minHalf) halfH = minHalf;

        float ratioX = halfW / startHalfW;
        float ratioY = halfH / startHalfH;
        transform.Scale = new Vector2(_resizeGroupStartScale.X * ratioX, _resizeGroupStartScale.Y * ratioY);

        Vector2 afterScale = transform.Scale;
        if (beforeScale.X != afterScale.X || beforeScale.Y != afterScale.Y)
        {
            if (!groupEntity.IsNull && TryGetTransformPropertySlot(group.Transform, TransformScaleNameHandle, PropertyKind.Vec2, out PropertySlot slot))
            {
                Commands.NotifyCanvasPropertyEdited(groupEntity, slot, PropertyValue.FromVec2(beforeScale), PropertyValue.FromVec2(afterScale));
            }
        }
    }

    private void RotateGroup(int groupIndex, Vector2 currentMouseWorld)
    {
        var group = _groups[groupIndex];
        EnsureGroupTransformInitialized(ref group);
        _groups[groupIndex] = group;

        if (group.Transform.IsNull)
        {
            return;
        }

        var transform = TransformComponent.Api.FromHandle(_propertyWorld, group.Transform);
        if (!transform.IsAlive)
        {
            return;
        }

        float beforeRotation = transform.Rotation;
        float currentAngle = MathF.Atan2(currentMouseWorld.Y - _rotateGroupPivotWorld.Y, currentMouseWorld.X - _rotateGroupPivotWorld.X);
        float deltaRadians = currentAngle - _rotateGroupStartMouseAngleRadians;
        float deltaDegrees = deltaRadians * (180f / MathF.PI);
        transform.Rotation = WrapDegrees(_rotateGroupStartRotationDegrees + deltaDegrees);

        float afterRotation = transform.Rotation;
        if (beforeRotation != afterRotation)
        {
            if (TryGetTransformPropertySlot(group.Transform, TransformRotationNameHandle, PropertyKind.Float, out PropertySlot slot) &&
                TryGetEntityById(_groupEntityById, group.Id, out EntityId groupEntity))
            {
                Commands.NotifyCanvasPropertyEdited(groupEntity, slot, PropertyValue.FromFloat(beforeRotation), PropertyValue.FromFloat(afterRotation));
            }
        }
    }

    private void RotateShape(int shapeIndex, Vector2 currentMouseParentLocal)
    {
        var shape = _shapes[shapeIndex];
        var transform = TransformComponent.Api.FromHandle(_propertyWorld, shape.Transform);
        if (!transform.IsAlive)
        {
            return;
        }

        float beforeRotation = transform.Rotation;
        float currentAngle = MathF.Atan2(currentMouseParentLocal.Y - _rotateShapePivotWorld.Y, currentMouseParentLocal.X - _rotateShapePivotWorld.X);
        float deltaRadians = currentAngle - _rotateShapeStartMouseAngleRadians;
        float deltaDegrees = deltaRadians * (180f / MathF.PI);
        transform.Rotation = WrapDegrees(_rotateShapeStartRotationDegrees + deltaDegrees);

        float afterRotation = transform.Rotation;
        if (beforeRotation != afterRotation)
        {
            if (TryGetTransformPropertySlot(shape.Transform, TransformRotationNameHandle, PropertyKind.Float, out PropertySlot slot) &&
                TryGetEntityById(_shapeEntityById, shape.Id, out EntityId shapeEntity))
            {
                Commands.NotifyCanvasPropertyEdited(shapeEntity, slot, PropertyValue.FromFloat(beforeRotation), PropertyValue.FromFloat(afterRotation));
            }
        }
    }

    private void RotateText(int textIndex, Vector2 currentMouseParentLocal)
    {
        var text = _texts[textIndex];
        var transform = TransformComponent.Api.FromHandle(_propertyWorld, text.Transform);
        if (!transform.IsAlive)
        {
            return;
        }

        float beforeRotation = transform.Rotation;
        float currentAngle = MathF.Atan2(currentMouseParentLocal.Y - _rotateTextPivotWorld.Y, currentMouseParentLocal.X - _rotateTextPivotWorld.X);
        float deltaRadians = currentAngle - _rotateTextStartMouseAngleRadians;
        float deltaDegrees = deltaRadians * (180f / MathF.PI);
        transform.Rotation = WrapDegrees(_rotateTextStartRotationDegrees + deltaDegrees);

        float afterRotation = transform.Rotation;
        if (beforeRotation != afterRotation)
        {
            if (TryGetTransformPropertySlot(text.Transform, TransformRotationNameHandle, PropertyKind.Float, out PropertySlot slot) &&
                TryGetEntityById(_textEntityById, text.Id, out EntityId textEntity))
            {
                Commands.NotifyCanvasPropertyEdited(textEntity, slot, PropertyValue.FromFloat(beforeRotation), PropertyValue.FromFloat(afterRotation));
            }
        }
    }

    private static float WrapDegrees(float degrees)
    {
        float wrapped = degrees;
        while (wrapped > 180f)
        {
            wrapped -= 360f;
        }
        while (wrapped < -180f)
        {
            wrapped += 360f;
        }
        return wrapped;
    }

    private bool IsPointInsideShape(Shape shape, Vector2 pointWorld)
    {
        if (!TryGetNodeParentWorldTransform(shape.ParentGroupId, shape.ParentShapeId, out WorldTransform parentWorldTransform))
        {
            parentWorldTransform = IdentityWorldTransform;
        }

        if (!TryGetShapeWorldTransform(shape, parentWorldTransform, out ShapeWorldTransform worldTransform))
        {
            return false;
        }

        ShapeKind kind = GetShapeKind(shape);
        if (kind == ShapeKind.Circle)
        {
            var circleGeometry = CircleGeometryComponent.Api.FromHandle(_propertyWorld, shape.CircleGeometry);
            Vector2 scale = worldTransform.ScaleWorld;
            float scaleX = scale.X == 0f ? 1f : scale.X;
            float scaleY = scale.Y == 0f ? 1f : scale.Y;
            float radiusWorld = circleGeometry.Radius * ((MathF.Abs(scaleX) + MathF.Abs(scaleY)) * 0.5f);

            float diameterWorld = radiusWorld * 2f;
            Vector2 anchorOffset = new Vector2(
                (worldTransform.Anchor.X - 0.5f) * diameterWorld,
                (worldTransform.Anchor.Y - 0.5f) * diameterWorld);
            Vector2 centerWorld = worldTransform.PositionWorld - RotateVector(anchorOffset, worldTransform.RotationRadians);

            Vector2 d = pointWorld - centerWorld;
            return d.X * d.X + d.Y * d.Y <= radiusWorld * radiusWorld;
        }

        if (kind == ShapeKind.Rect)
        {
            var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, shape.RectGeometry);
            Vector2 scale = worldTransform.ScaleWorld;
            float scaleX = scale.X == 0f ? 1f : scale.X;
            float scaleY = scale.Y == 0f ? 1f : scale.Y;

            float widthWorld = rectGeometry.Size.X * MathF.Abs(scaleX);
            float heightWorld = rectGeometry.Size.Y * MathF.Abs(scaleY);

            Vector2 anchorOffset = new Vector2(
                (worldTransform.Anchor.X - 0.5f) * widthWorld,
                (worldTransform.Anchor.Y - 0.5f) * heightWorld);
            Vector2 centerWorld = worldTransform.PositionWorld - RotateVector(anchorOffset, worldTransform.RotationRadians);
            Vector2 halfSizeWorld = new Vector2(widthWorld * 0.5f, heightWorld * 0.5f);
            return IsPointInsideOrientedRect(centerWorld, halfSizeWorld, worldTransform.RotationRadians, pointWorld);
	        }

	        // Polygon
	        if (!TryGetPolygonPointsLocal(shape, out ReadOnlySpan<Vector2> pointsLocal))
	        {
	            return false;
	        }

	        int pointCount = pointsLocal.Length;
	        GetBounds(pointsLocal, out float minLocalX, out float minLocalY, out float maxLocalX, out float maxLocalY);
	        Vector2 boundsMinLocal = new Vector2(minLocalX, minLocalY);
	        Vector2 boundsSizeLocal = new Vector2(maxLocalX - minLocalX, maxLocalY - minLocalY);

        Vector2 positionWorld = worldTransform.PositionWorld;
        Vector2 anchor = worldTransform.Anchor;
        float rotationRadians = worldTransform.RotationRadians;
        Vector2 pivotLocal = GetPolygonPivotLocal(shape, anchor, boundsMinLocal, boundsSizeLocal);

        Vector2 scaleWorld = worldTransform.ScaleWorld;
        float polygonScaleX = scaleWorld.X == 0f ? 1f : scaleWorld.X;
        float polygonScaleY = scaleWorld.Y == 0f ? 1f : scaleWorld.Y;

        EnsurePolygonScratchCapacity(pointCount);
        for (int i = 0; i < pointCount; i++)
        {
            _polygonCanvasScratch[i] = TransformPolygonPointToWorld(positionWorld, pivotLocal, rotationRadians, polygonScaleX, polygonScaleY, pointsLocal[i]);
        }

        var pointsWorld = _polygonCanvasScratch.AsSpan(0, pointCount);
        bool inside = false;

        float px = pointWorld.X;
        float py = pointWorld.Y;

        int j = pointCount - 1;
        for (int i = 0; i < pointCount; i++)
        {
            Vector2 a = pointsWorld[i];
            Vector2 b = pointsWorld[j];

            bool intersects = (a.Y > py) != (b.Y > py);
            if (intersects)
            {
                float xIntersect = (b.X - a.X) * (py - a.Y) / (b.Y - a.Y) + a.X;
                if (px < xIntersect)
                {
                    inside = !inside;
                }
            }

            j = i;
        }

        return inside;
    }

    private void MoveShape(int shapeIndex, Vector2 deltaWorld, bool dispatchAnimation)
    {
        var shape = _shapes[shapeIndex];
        var transform = TransformComponent.Api.FromHandle(_propertyWorld, shape.Transform);
        if (!transform.IsAlive)
        {
            return;
        }

        WorldTransform parentWorldTransform = IdentityWorldTransform;
        if (TryGetEntityById(_shapeEntityById, shape.Id, out EntityId shapeEntity))
        {
            if (!TryGetTransformParentWorldTransformEcs(shapeEntity, out parentWorldTransform))
            {
                parentWorldTransform = IdentityWorldTransform;
            }
        }

        Vector2 deltaLocal = InverseTransformVector(parentWorldTransform, deltaWorld);
        Vector2 before = transform.Position;
        Vector2 after = new Vector2(before.X + deltaLocal.X, before.Y + deltaLocal.Y);
        transform.Position = after;

        if (dispatchAnimation && (before.X != after.X || before.Y != after.Y))
        {
            if (!shapeEntity.IsNull && TryGetTransformPropertySlot(shape.Transform, TransformPositionNameHandle, PropertyKind.Vec2, out PropertySlot slot))
            {
                Commands.NotifyCanvasPropertyEdited(shapeEntity, slot, PropertyValue.FromVec2(before), PropertyValue.FromVec2(after));
            }
        }

        _shapes[shapeIndex] = shape;
    }

    private void MoveText(int textIndex, Vector2 deltaWorld, bool dispatchAnimation)
    {
        var text = _texts[textIndex];
        var transform = TransformComponent.Api.FromHandle(_propertyWorld, text.Transform);
        if (!transform.IsAlive)
        {
            return;
        }

        WorldTransform parentWorldTransform = IdentityWorldTransform;
        EntityId textEntity = EntityId.Null;
        if (TryGetEntityById(_textEntityById, text.Id, out textEntity))
        {
            if (!TryGetTransformParentWorldTransformEcs(textEntity, out parentWorldTransform))
            {
                parentWorldTransform = IdentityWorldTransform;
            }
        }

        Vector2 deltaLocal = InverseTransformVector(parentWorldTransform, deltaWorld);
        Vector2 before = transform.Position;
        Vector2 after = new Vector2(before.X + deltaLocal.X, before.Y + deltaLocal.Y);
        transform.Position = after;

        if (dispatchAnimation && (before.X != after.X || before.Y != after.Y))
        {
            if (!textEntity.IsNull && TryGetTransformPropertySlot(text.Transform, TransformPositionNameHandle, PropertyKind.Vec2, out PropertySlot slot))
            {
                Commands.NotifyCanvasPropertyEdited(textEntity, slot, PropertyValue.FromVec2(before), PropertyValue.FromVec2(after));
            }
        }

        _texts[textIndex] = text;
    }

    private void MoveGroup(int groupIndex, Vector2 deltaWorld, bool dispatchAnimation)
    {
        var group = _groups[groupIndex];
        EnsureGroupTransformInitialized(ref group);
        if (!group.Transform.IsNull)
        {
            var transform = TransformComponent.Api.FromHandle(_propertyWorld, group.Transform);
            if (transform.IsAlive)
            {
                WorldTransform parentWorldTransform = IdentityWorldTransform;
                EntityId groupEntity = EntityId.Null;
                if (TryGetEntityById(_groupEntityById, group.Id, out groupEntity))
                {
                    if (!TryGetTransformParentWorldTransformEcs(groupEntity, out parentWorldTransform))
                    {
                        parentWorldTransform = IdentityWorldTransform;
                    }
                }

                Vector2 deltaLocal = InverseTransformVector(parentWorldTransform, deltaWorld);
                Vector2 before = transform.Position;
                Vector2 after = new Vector2(before.X + deltaLocal.X, before.Y + deltaLocal.Y);
                transform.Position = after;

                if (dispatchAnimation && (before.X != after.X || before.Y != after.Y))
                {
                    if (!groupEntity.IsNull && TryGetTransformPropertySlot(group.Transform, TransformPositionNameHandle, PropertyKind.Vec2, out PropertySlot slot))
                    {
                        Commands.NotifyCanvasPropertyEdited(groupEntity, slot, PropertyValue.FromVec2(before), PropertyValue.FromVec2(after));
                    }
                }
            }
        }
        _groups[groupIndex] = group;
    }

    private void ResizeText(int textIndex, ShapeHandle handle, ImRect startRectWorld, Vector2 currentMouseParentLocal)
    {
        if (handle == ShapeHandle.None)
        {
            return;
        }

        var text = _texts[textIndex];

        var transform = TransformComponent.Api.FromHandle(_propertyWorld, text.Transform);
        if (!transform.IsAlive)
        {
            return;
        }

        var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, text.RectGeometry);
        if (!rectGeometry.IsAlive)
        {
            return;
        }

        Vector2 beforePosition = transform.Position;
        Vector2 beforeSize = rectGeometry.Size;

        Vector2 currentMouseLocal = RotateVector(currentMouseParentLocal - _resizeAnchorWorld, -_resizeRotationRadians);
        Vector2 delta = currentMouseLocal - _resizeStartMouseLocal;

        float left = startRectWorld.X;
        float top = startRectWorld.Y;
        float right = startRectWorld.Right;
        float bottom = startRectWorld.Bottom;

        if (handle == ShapeHandle.TopLeft || handle == ShapeHandle.Left || handle == ShapeHandle.BottomLeft)
        {
            left += delta.X;
        }
        if (handle == ShapeHandle.TopRight || handle == ShapeHandle.Right || handle == ShapeHandle.BottomRight)
        {
            right += delta.X;
        }
        if (handle == ShapeHandle.TopLeft || handle == ShapeHandle.Top || handle == ShapeHandle.TopRight)
        {
            top += delta.Y;
        }
        if (handle == ShapeHandle.BottomLeft || handle == ShapeHandle.Bottom || handle == ShapeHandle.BottomRight)
        {
            bottom += delta.Y;
        }

        if (left > right)
        {
            float tmp = left;
            left = right;
            right = tmp;
        }

        if (top > bottom)
        {
            float tmp = top;
            top = bottom;
            bottom = tmp;
        }

        const float minSize = 6f;
        float width = right - left;
        float height = bottom - top;
        if (width < minSize)
        {
            right = left + minSize;
        }
        if (height < minSize)
        {
            bottom = top + minSize;
        }

        var rectWorld = new ImRect(left, top, right - left, bottom - top);
        SetTextRectWorld(ref text, rectWorld);
        _texts[textIndex] = text;

        Vector2 afterPosition = transform.Position;
        EntityId textEntity = EntityId.Null;
        TryGetEntityById(_textEntityById, text.Id, out textEntity);

        if (beforePosition.X != afterPosition.X || beforePosition.Y != afterPosition.Y)
        {
            if (!textEntity.IsNull && TryGetTransformPropertySlot(text.Transform, TransformPositionNameHandle, PropertyKind.Vec2, out PropertySlot slot))
            {
                Commands.NotifyCanvasPropertyEdited(textEntity, slot, PropertyValue.FromVec2(beforePosition), PropertyValue.FromVec2(afterPosition));
            }
        }

        Vector2 afterSize = rectGeometry.Size;
        if (beforeSize.X != afterSize.X || beforeSize.Y != afterSize.Y)
        {
            AnyComponentHandle component = RectGeometryComponentProperties.ToAnyHandle(text.RectGeometry);
            PropertySlot slot = PropertyDispatcher.GetSlot(component, 0);
            if (!textEntity.IsNull)
            {
                Commands.NotifyCanvasPropertyEdited(textEntity, slot, PropertyValue.FromVec2(beforeSize), PropertyValue.FromVec2(afterSize));
            }
        }
    }

    private void MovePrefab(int prefabIndex, Vector2 deltaWorld, bool dispatchAnimation)
    {
        var prefab = _prefabs[prefabIndex];
        prefab.RectWorld = new ImRect(
            prefab.RectWorld.X + deltaWorld.X,
            prefab.RectWorld.Y + deltaWorld.Y,
            prefab.RectWorld.Width,
            prefab.RectWorld.Height);

        if (!prefab.Transform.IsNull)
        {
            var transform = TransformComponent.Api.FromHandle(_propertyWorld, prefab.Transform);
            if (transform.IsAlive)
            {
                Vector2 before = transform.Position;
                Vector2 after = new Vector2(before.X + deltaWorld.X, before.Y + deltaWorld.Y);
                transform.Position = after;

                if (dispatchAnimation && (before.X != after.X || before.Y != after.Y))
                {
                    if (TryGetTransformPropertySlot(prefab.Transform, TransformPositionNameHandle, PropertyKind.Vec2, out PropertySlot slot) &&
                        TryGetEntityById(_prefabEntityById, prefab.Id, out EntityId prefabEntityForDispatch))
                    {
                        Commands.NotifyCanvasPropertyEdited(prefabEntityForDispatch, slot, PropertyValue.FromVec2(before), PropertyValue.FromVec2(after));
                    }
                }
            }
        }

	        _prefabs[prefabIndex] = prefab;
    }

    private bool TryBeginResizeSelectedPrefab(Vector2 mouseWorld, Vector2 mouseCanvas, Vector2 canvasOrigin)
    {
        if (!_isPrefabTransformSelectedOnCanvas || _selectedEntities.Count != 0 || _selectedPrefabEntity.IsNull)
        {
            return false;
        }

        if (_selectedPrefabIndex < 0 || _selectedPrefabIndex >= _prefabs.Count)
        {
            return false;
        }

        var frame = _prefabs[_selectedPrefabIndex];
        if (!TryGetEntityById(_prefabEntityById, frame.Id, out EntityId prefabEntity) || prefabEntity.IsNull)
        {
            return false;
        }

        if (prefabEntity.Value != _selectedPrefabEntity.Value)
        {
            return false;
        }

        var rectCanvas = WorldRectToCanvas(frame.RectWorld, canvasOrigin);
        Vector2 centerCanvas = rectCanvas.Center;
        if (!TryGetHandleAtMouse(rectCanvas, centerCanvas, rotationRadians: 0f, mouseCanvas, out ShapeHandle handle))
        {
            return false;
        }

        _dragKind = DragKind.ResizePrefab;
        _dragPrefabIndex = _selectedPrefabIndex;
        _resizeHandle = handle;
        _resizeStartRectWorld = frame.RectWorld;
        _dragLastMouseWorld = mouseWorld;
        BeginCanvasEditCaptureForFrame(_selectedPrefabIndex, DragKind.ResizePrefab);
        return true;
    }

    private bool TryBeginDragSelectedPrefabAnywhere(Vector2 mouseWorld, Vector2 mouseCanvas, Vector2 canvasOrigin)
    {
        if (!_isPrefabTransformSelectedOnCanvas || _selectedEntities.Count != 0 || _selectedPrefabEntity.IsNull)
        {
            return false;
        }

        if (_selectedPrefabIndex < 0 || _selectedPrefabIndex >= _prefabs.Count)
        {
            return false;
        }

        var frame = _prefabs[_selectedPrefabIndex];
        if (!TryGetEntityById(_prefabEntityById, frame.Id, out EntityId prefabEntity) || prefabEntity.IsNull)
        {
            return false;
        }

        if (prefabEntity.Value != _selectedPrefabEntity.Value)
        {
            return false;
        }

        var rectCanvas = WorldRectToCanvas(frame.RectWorld, canvasOrigin);
        if (!rectCanvas.Contains(mouseCanvas))
        {
            return false;
        }

        BeginDrag(DragKind.Prefab, mouseWorld, frameIndex: _selectedPrefabIndex, shapeIndex: -1, groupIndex: -1, textIndex: -1);
        return true;
    }

    private bool TryBeginSelectAndDragPrefabFromLabel(Vector2 mouseWorld, Vector2 mouseCanvas, Vector2 canvasOrigin)
    {
        if (!TryHitPrefabLabel(mouseCanvas, canvasOrigin, out EntityId prefabEntity, out int frameIndex))
        {
            return false;
        }

        SelectPrefabEntity(prefabEntity);
        _selectedPrefabIndex = frameIndex;
        _isPrefabTransformSelectedOnCanvas = true;
        BeginDrag(DragKind.Prefab, mouseWorld, frameIndex: frameIndex, shapeIndex: -1, groupIndex: -1, textIndex: -1);
        return true;
    }

    private bool IsMouseOverSelectedPrefabOrLabel(Vector2 mouseCanvas, Vector2 canvasOrigin)
    {
        if (_selectedPrefabIndex < 0 || _selectedPrefabIndex >= _prefabs.Count || _selectedPrefabEntity.IsNull)
        {
            return false;
        }

        var frame = _prefabs[_selectedPrefabIndex];
        if (!TryGetEntityById(_prefabEntityById, frame.Id, out EntityId prefabEntity) || prefabEntity.IsNull)
        {
            return false;
        }

        if (prefabEntity.Value != _selectedPrefabEntity.Value)
        {
            return false;
        }

        var rectCanvas = WorldRectToCanvas(frame.RectWorld, canvasOrigin);
        if (rectCanvas.Contains(mouseCanvas))
        {
            return true;
        }

        uint stableId = _world.GetStableId(prefabEntity);
        if (TryGetLayerName(stableId, out string prefabName))
        {
            ImRect labelRect = GetPrefabLabelRectCanvas(rectCanvas, prefabName.AsSpan());
            return labelRect.Contains(mouseCanvas);
        }

        Span<char> labelBuffer = stackalloc char[32];
        int len = FormatPrefabLabel(frame.Id, labelBuffer);
        ImRect fallbackLabelRect = GetPrefabLabelRectCanvas(rectCanvas, labelBuffer.Slice(0, len));
        return fallbackLabelRect.Contains(mouseCanvas);
    }

    private bool TryHitPrefabLabel(Vector2 mouseCanvas, Vector2 canvasOrigin, out EntityId prefabEntity, out int frameIndex)
    {
        prefabEntity = EntityId.Null;
        frameIndex = -1;

        Span<char> labelBuffer = stackalloc char[32];

        for (int i = _prefabs.Count - 1; i >= 0; i--)
        {
            var frame = _prefabs[i];
            if (!TryGetEntityById(_prefabEntityById, frame.Id, out EntityId entity) || entity.IsNull)
            {
                continue;
            }

            uint stableId = _world.GetStableId(entity);
            var rectCanvas = WorldRectToCanvas(frame.RectWorld, canvasOrigin);

            if (TryGetLayerName(stableId, out string prefabName))
            {
                ImRect labelRect = GetPrefabLabelRectCanvas(rectCanvas, prefabName.AsSpan());
                if (labelRect.Contains(mouseCanvas))
                {
                    prefabEntity = entity;
                    frameIndex = i;
                    return true;
                }

                continue;
            }

            int len = FormatPrefabLabel(frame.Id, labelBuffer);
            ImRect fallbackLabelRect = GetPrefabLabelRectCanvas(rectCanvas, labelBuffer.Slice(0, len));
            if (fallbackLabelRect.Contains(mouseCanvas))
            {
                prefabEntity = entity;
                frameIndex = i;
                return true;
            }
        }

        return false;
    }

    private void ResizePrefab(int prefabIndex, ShapeHandle handle, ImRect startRectWorld, Vector2 currentMouseWorld, bool dispatchAnimation)
    {
        if (handle == ShapeHandle.None || prefabIndex < 0 || prefabIndex >= _prefabs.Count)
        {
            return;
        }

        var prefab = _prefabs[prefabIndex];
        if (!TryGetEntityById(_prefabEntityById, prefab.Id, out EntityId prefabEntity) || prefabEntity.IsNull)
        {
            return;
        }

        float left = startRectWorld.Left;
        float top = startRectWorld.Top;
        float right = startRectWorld.Right;
        float bottom = startRectWorld.Bottom;

        float mx = currentMouseWorld.X;
        float my = currentMouseWorld.Y;

        if (handle == ShapeHandle.Left || handle == ShapeHandle.TopLeft || handle == ShapeHandle.BottomLeft)
        {
            left = mx;
        }
        if (handle == ShapeHandle.Right || handle == ShapeHandle.TopRight || handle == ShapeHandle.BottomRight)
        {
            right = mx;
        }
        if (handle == ShapeHandle.Top || handle == ShapeHandle.TopLeft || handle == ShapeHandle.TopRight)
        {
            top = my;
        }
        if (handle == ShapeHandle.Bottom || handle == ShapeHandle.BottomLeft || handle == ShapeHandle.BottomRight)
        {
            bottom = my;
        }

        const float minSize = 64f;
        if (right < left + minSize)
        {
            if (handle == ShapeHandle.Left || handle == ShapeHandle.TopLeft || handle == ShapeHandle.BottomLeft)
            {
                left = right - minSize;
            }
            else
            {
                right = left + minSize;
            }
        }

        if (bottom < top + minSize)
        {
            if (handle == ShapeHandle.Top || handle == ShapeHandle.TopLeft || handle == ShapeHandle.TopRight)
            {
                top = bottom - minSize;
            }
            else
            {
                bottom = top + minSize;
            }
        }

        var rectWorld = new ImRect(left, top, right - left, bottom - top);
        prefab.RectWorld = rectWorld;
        _prefabs[prefabIndex] = prefab;

        // Keep transform + prefab canvas size in sync.
        if (!prefab.Transform.IsNull)
        {
            var transform = TransformComponent.Api.FromHandle(_propertyWorld, prefab.Transform);
            if (transform.IsAlive)
            {
                Vector2 before = transform.Position;
                Vector2 after = new Vector2(rectWorld.X, rectWorld.Y);
                transform.Position = after;

                if (dispatchAnimation && (before.X != after.X || before.Y != after.Y))
                {
                    if (TryGetTransformPropertySlot(prefab.Transform, TransformPositionNameHandle, PropertyKind.Vec2, out PropertySlot slot))
                    {
                        Commands.NotifyCanvasPropertyEdited(prefabEntity, slot, PropertyValue.FromVec2(before), PropertyValue.FromVec2(after));
                    }
                }
            }
        }

        if (_world.TryGetComponent(prefabEntity, PrefabCanvasComponent.Api.PoolIdConst, out AnyComponentHandle canvasAny))
        {
            var canvasHandle = new PrefabCanvasComponentHandle(canvasAny.Index, canvasAny.Generation);
            var canvas = PrefabCanvasComponent.Api.FromHandle(_propertyWorld, canvasHandle);
            if (canvas.IsAlive)
            {
                Vector2 before = canvas.Size;
                Vector2 after = new Vector2(rectWorld.Width, rectWorld.Height);
                canvas.Size = after;

                if (dispatchAnimation && (before.X != after.X || before.Y != after.Y))
                {
                    AnyComponentHandle canvasComponent = PrefabCanvasComponentProperties.ToAnyHandle(canvasHandle);
                    if (TryGetPropertySlotByGroupAndName(canvasComponent, "Prefab", "Size", PropertyKind.Vec2, out PropertySlot slot))
                    {
                        Commands.NotifyCanvasPropertyEdited(prefabEntity, slot, PropertyValue.FromVec2(before), PropertyValue.FromVec2(after));
                    }
                }
            }
        }
    }

    internal bool TryMovePrefabById(int prefabId, Vector2 deltaWorld)
    {
        if (prefabId <= 0)
        {
            return false;
        }

        if (!TryGetPrefabIndexById(prefabId, out int prefabIndex))
        {
            return false;
        }

        MovePrefab(prefabIndex, deltaWorld, dispatchAnimation: false);
        return true;
    }

    internal bool TrySetPrefabRectWorldById(int prefabId, ImRect rectWorld)
    {
        if (prefabId <= 0)
        {
            return false;
        }

        if (!TryGetPrefabIndexById(prefabId, out int prefabIndex))
        {
            return false;
        }

        if ((uint)prefabIndex >= (uint)_prefabs.Count)
        {
            return false;
        }

        var prefab = _prefabs[prefabIndex];
        prefab.RectWorld = rectWorld;
        _prefabs[prefabIndex] = prefab;

        // Keep transform + prefab canvas size in sync.
        if (!prefab.Transform.IsNull)
        {
            var transform = TransformComponent.Api.FromHandle(_propertyWorld, prefab.Transform);
            if (transform.IsAlive)
            {
                transform.Position = new Vector2(rectWorld.X, rectWorld.Y);
            }
        }

        if (TryGetEntityById(_prefabEntityById, prefabId, out EntityId prefabEntity) && !prefabEntity.IsNull)
        {
            if (_world.TryGetComponent(prefabEntity, PrefabCanvasComponent.Api.PoolIdConst, out AnyComponentHandle canvasAny))
            {
                var canvasHandle = new PrefabCanvasComponentHandle(canvasAny.Index, canvasAny.Generation);
                var canvas = PrefabCanvasComponent.Api.FromHandle(_propertyWorld, canvasHandle);
                if (canvas.IsAlive)
                {
                    canvas.Size = new Vector2(rectWorld.Width, rectWorld.Height);
                }
            }
        }

        return true;
    }

	    internal int FindTopmostPrefabIndex(Vector2 mouseWorld)
	    {
	        for (int frameIndex = _prefabs.Count - 1; frameIndex >= 0; frameIndex--)
	        {
            if (_prefabs[frameIndex].RectWorld.Contains(mouseWorld))
            {
                return frameIndex;
            }
        }

	        return -1;
	    }

    // Legacy SDF render path removed (canvas rendering is ECS-driven via `BuildPrefabsAndContentsEcs`).
}
