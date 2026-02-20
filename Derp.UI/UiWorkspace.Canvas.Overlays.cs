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
    private static int FormatPrefabLabel(int prefabId, Span<char> buffer)
    {
        const string prefix = "Prefab ";
        if (buffer.Length < prefix.Length + 1)
        {
            return 0;
        }

        prefix.AsSpan().CopyTo(buffer);
        int written = prefix.Length;

        if (!prefabId.TryFormat(buffer.Slice(written), out int idWritten))
        {
            return written;
        }

        return written + idWritten;
    }

    private static float MeasureTextWidth(ReadOnlySpan<char> text, float fontSize)
    {
        var font = Im.Context.Font;
        if (font != null)
        {
            return font.MeasureText(text, fontSize).X;
        }

        const float fallbackCharWidth = 7f;
        int len = text.Length;
        if (len > 60)
        {
            len = 60;
        }

        return len * fallbackCharWidth;
    }

    private static ImRect GetPrefabLabelRectCanvas(ImRect rectCanvas, ReadOnlySpan<char> labelText)
    {
        float fontSize = Im.Style.FontSize;
        const float paddingX = 8f;
        const float paddingY = 4f;
        const float offsetY = 6f;

        float textWidth = MeasureTextWidth(labelText, fontSize);
        float width = textWidth + paddingX * 2f;
        float height = fontSize + paddingY * 2f;
        float x = rectCanvas.X;
        float y = rectCanvas.Y - height - offsetY;
        return new ImRect(x, y, width, height);
    }

    internal void DrawFramesOverlay(Vector2 canvasOrigin)
    {
            UpdatePropertyGizmosFromInspectorState();
            Span<char> labelBuffer = stackalloc char[32];
	        for (int frameIndex = 0; frameIndex < _prefabs.Count; frameIndex++)
	        {
	            var frame = _prefabs[frameIndex];
	            var rectCanvas = WorldRectToCanvas(frame.RectWorld, canvasOrigin);
	            DrawSelectedPrefabTransformGizmo(frameIndex, rectCanvas);

            if (TryGetEntityById(_prefabEntityById, frame.Id, out EntityId prefabEntity))
            {
                uint stableId = _world.GetStableId(prefabEntity);
                if (TryGetLayerName(stableId, out string prefabName))
                {
                    ImRect labelRect = GetPrefabLabelRectCanvas(rectCanvas, prefabName.AsSpan());
                    uint bg = ImStyle.WithAlphaF(Im.Style.Surface, 0.92f);
                    Im.DrawRoundedRect(labelRect.X, labelRect.Y, labelRect.Width, labelRect.Height, 6f, bg);
                    Im.LabelText(prefabName, labelRect.X + 8f, labelRect.Y + 4f);
                }
                else
                {
                    int len = FormatPrefabLabel(frame.Id, labelBuffer);
                    ReadOnlySpan<char> labelText = labelBuffer.Slice(0, len);
                    ImRect labelRect = GetPrefabLabelRectCanvas(rectCanvas, labelText);
                    uint bg = ImStyle.WithAlphaF(Im.Style.Surface, 0.92f);
                    Im.DrawRoundedRect(labelRect.X, labelRect.Y, labelRect.Width, labelRect.Height, 6f, bg);
                    Im.Text(labelText, labelRect.X + 8f, labelRect.Y + 4f, Im.Style.FontSize, Im.Style.TextPrimary);
                }
            }
            else
            {
                int len = FormatPrefabLabel(frame.Id, labelBuffer);
                ReadOnlySpan<char> labelText = labelBuffer.Slice(0, len);
                ImRect labelRect = GetPrefabLabelRectCanvas(rectCanvas, labelText);
                uint bg = ImStyle.WithAlphaF(Im.Style.Surface, 0.92f);
                Im.DrawRoundedRect(labelRect.X, labelRect.Y, labelRect.Width, labelRect.Height, 6f, bg);
                Im.Text(labelText, labelRect.X + 8f, labelRect.Y + 4f, Im.Style.FontSize, Im.Style.TextPrimary);
            }

		            Im.PushClipRect(rectCanvas);
		            DrawMarqueeOverlay(frame.Id, canvasOrigin);
		            DrawSelectedGroupOutlines(frame.Id, canvasOrigin);
		            DrawSelectedShapeOutlines(frame.Id, canvasOrigin);
		            DrawSelectedPrefabInstanceOutlines(frame.Id, canvasOrigin);
		            DrawEditingPathHandles(frame.Id, canvasOrigin);
		            DrawSelectedShapeAnchors(frame.Id, canvasOrigin);
		            DrawSelectedLayoutConstraintGizmos(frame.Id, canvasOrigin);
		            DrawSelectedGroupHandles(frame.Id, canvasOrigin);
		            DrawSelectedShapeHandles(frame.Id, canvasOrigin);
		            DrawSelectedPrefabInstanceHandles(frame.Id, canvasOrigin);
		            DrawPropertyGizmosForFrame(frame.Id, canvasOrigin);
		            Im.PopClipRect();
		        }
		    }

    private void DrawSelectedPrefabTransformGizmo(int frameIndex, ImRect rectCanvas)
    {
        if (frameIndex < 0 || frameIndex >= _prefabs.Count)
        {
            return;
        }

        if (_selectedEntities.Count != 0)
        {
            return;
        }

        if (!_isPrefabTransformSelectedOnCanvas)
        {
            return;
        }

        if (!TryGetEntityById(_prefabEntityById, _prefabs[frameIndex].Id, out EntityId prefabEntity) || prefabEntity.IsNull)
        {
            return;
        }

        if (_selectedPrefabEntity.IsNull || prefabEntity.Value != _selectedPrefabEntity.Value)
        {
            return;
        }

        uint stroke = ImStyle.WithAlphaF(Im.Style.Primary, 0.95f);
        uint handleFill = ImStyle.WithAlphaF(0xFFFFFFFF, 0.92f);
        uint handleStroke = ImStyle.WithAlphaF(Im.Style.Primary, 0.95f);

        const float thickness = 1.5f;
        float left = rectCanvas.X;
        float top = rectCanvas.Y;
        float right = rectCanvas.Right;
        float bottom = rectCanvas.Bottom;
        Im.DrawLine(left, top, right, top, thickness, stroke);
        Im.DrawLine(right, top, right, bottom, thickness, stroke);
        Im.DrawLine(right, bottom, left, bottom, thickness, stroke);
        Im.DrawLine(left, bottom, left, top, thickness, stroke);

        DrawRectSelectionHandlesNoRotation(rectCanvas.Center, rectCanvas.Width * 0.5f, rectCanvas.Height * 0.5f, handleFill, handleStroke);
    }

    private static void DrawRectSelectionHandlesNoRotation(Vector2 centerCanvas, float halfWidth, float halfHeight, uint handleFill, uint handleStroke)
    {
        const float handleSize = 10f;
        float half = handleSize * 0.5f;

        float hw = halfWidth;
        float hh = halfHeight;

        Vector2 p = centerCanvas + new Vector2(-hw, -hh);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + new Vector2(hw, -hh);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + new Vector2(hw, hh);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + new Vector2(-hw, hh);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + new Vector2(0f, -hh);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + new Vector2(hw, 0f);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + new Vector2(0f, hh);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + new Vector2(-hw, 0f);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);
    }

    private void DrawSelectedPrefabInstanceOutlines(int frameId, Vector2 canvasOrigin)
    {
        if (_selectedEntities.Count <= 0)
        {
            return;
        }

        uint stroke = ImStyle.WithAlphaF(Im.Style.Primary, 0.95f);
        for (int selectionIndex = 0; selectionIndex < _selectedEntities.Count; selectionIndex++)
        {
            EntityId instanceEntity = _selectedEntities[selectionIndex];
            if (_world.GetNodeType(instanceEntity) != UiNodeType.PrefabInstance)
            {
                continue;
            }

            EntityId owningPrefab = FindOwningPrefabEntity(instanceEntity);
            if (owningPrefab.IsNull || !TryGetLegacyPrefabId(owningPrefab, out int prefabId) || prefabId != frameId)
            {
                continue;
            }

            if (!TryGetPrefabInstanceRectCanvasForOutlineEcs(instanceEntity, canvasOrigin, out var centerCanvas, out var halfSizeCanvas, out float rotationRadians, out _))
            {
                continue;
            }

            if (rotationRadians == 0f)
            {
                float x = centerCanvas.X - halfSizeCanvas.X;
                float y = centerCanvas.Y - halfSizeCanvas.Y;
                Im.DrawRoundedRectStroke(x, y, halfSizeCanvas.X * 2f, halfSizeCanvas.Y * 2f, 0f, stroke, 2f);
                continue;
            }

            Vector2 tl = centerCanvas + RotateVector(new Vector2(-halfSizeCanvas.X, -halfSizeCanvas.Y), rotationRadians);
            Vector2 tr = centerCanvas + RotateVector(new Vector2(halfSizeCanvas.X, -halfSizeCanvas.Y), rotationRadians);
            Vector2 br = centerCanvas + RotateVector(new Vector2(halfSizeCanvas.X, halfSizeCanvas.Y), rotationRadians);
            Vector2 bl = centerCanvas + RotateVector(new Vector2(-halfSizeCanvas.X, halfSizeCanvas.Y), rotationRadians);

            Im.DrawLine(tl.X, tl.Y, tr.X, tr.Y, 2f, stroke);
            Im.DrawLine(tr.X, tr.Y, br.X, br.Y, 2f, stroke);
            Im.DrawLine(br.X, br.Y, bl.X, bl.Y, 2f, stroke);
            Im.DrawLine(bl.X, bl.Y, tl.X, tl.Y, 2f, stroke);
        }
    }

    private void DrawSelectedPrefabInstanceHandles(int frameId, Vector2 canvasOrigin)
    {
        if (_selectedEntities.Count != 1)
        {
            return;
        }

        EntityId instanceEntity = _selectedEntities[0];
        if (_world.GetNodeType(instanceEntity) != UiNodeType.PrefabInstance)
        {
            return;
        }

        if (_world.HasComponent(instanceEntity, ListGeneratedComponent.Api.PoolIdConst))
        {
            return;
        }

        EntityId owningPrefab = FindOwningPrefabEntity(instanceEntity);
        if (owningPrefab.IsNull || !TryGetLegacyPrefabId(owningPrefab, out int prefabId) || prefabId != frameId)
        {
            return;
        }

        if (!TryGetPrefabInstanceRectCanvasForOutlineEcs(instanceEntity, canvasOrigin, out var centerCanvas, out var halfSizeCanvas, out float rotationRadians, out _))
        {
            return;
        }

        uint handleFill = ImStyle.WithAlphaF(0xFFFFFFFF, 0.92f);
        uint handleStroke = ImStyle.WithAlphaF(Im.Style.Primary, 0.95f);

        if (rotationRadians == 0f)
        {
            DrawRectSelectionHandlesNoRotation(centerCanvas, halfSizeCanvas.X, halfSizeCanvas.Y, handleFill, handleStroke);
            return;
        }

        const float handleSize = 10f;
        float half = handleSize * 0.5f;
        float hw = halfSizeCanvas.X;
        float hh = halfSizeCanvas.Y;

        Vector2 p = centerCanvas + RotateVector(new Vector2(-hw, -hh), rotationRadians);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + RotateVector(new Vector2(0f, -hh), rotationRadians);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + RotateVector(new Vector2(hw, -hh), rotationRadians);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + RotateVector(new Vector2(hw, 0f), rotationRadians);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + RotateVector(new Vector2(hw, hh), rotationRadians);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + RotateVector(new Vector2(0f, hh), rotationRadians);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + RotateVector(new Vector2(-hw, hh), rotationRadians);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + RotateVector(new Vector2(-hw, 0f), rotationRadians);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);
    }

    private bool TryGetPrefabInstanceRectCanvasForOutlineEcs(
        EntityId instanceEntity,
        Vector2 canvasOrigin,
        out Vector2 centerCanvas,
        out Vector2 halfSizeCanvas,
        out float rotationRadians,
        out WorldTransform instanceWorldTransform)
    {
        centerCanvas = Vector2.Zero;
        halfSizeCanvas = Vector2.Zero;
        rotationRadians = 0f;
        instanceWorldTransform = IdentityWorldTransform;

        if (instanceEntity.IsNull)
        {
            return false;
        }

        WorldTransform parentWorldTransform = IdentityWorldTransform;
        if (!TryGetTransformParentWorldTransformEcs(instanceEntity, out parentWorldTransform))
        {
            parentWorldTransform = IdentityWorldTransform;
        }

        if (!TryGetGroupWorldTransformEcs(instanceEntity, parentWorldTransform, out instanceWorldTransform, out _, out _))
        {
            return false;
        }

        if (!instanceWorldTransform.IsVisible)
        {
            return false;
        }

        if (!TryGetComputedSize(instanceEntity, out Vector2 sizeLocal) || sizeLocal.X <= 0f || sizeLocal.Y <= 0f)
        {
            return false;
        }

        Vector2 anchor01 = Vector2.Zero;
        if (_world.TryGetComponent(instanceEntity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny) && transformAny.IsValid)
        {
            var transformHandle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
            var transform = TransformComponent.Api.FromHandle(_propertyWorld, transformHandle);
            if (transform.IsAlive)
            {
                anchor01 = transform.Anchor;
            }
        }

        ImRect boundsLocal = RectTransformMath.GetRectFromPivot(Vector2.Zero, anchor01, sizeLocal);
        rotationRadians = instanceWorldTransform.RotationRadians;

        Vector2 localCenter = new Vector2(boundsLocal.X + boundsLocal.Width * 0.5f, boundsLocal.Y + boundsLocal.Height * 0.5f);
        Vector2 centerWorld = TransformPoint(instanceWorldTransform, localCenter);

        Vector2 scale = instanceWorldTransform.Scale;
        float scaleX = MathF.Abs(scale.X == 0f ? 1f : scale.X);
        float scaleY = MathF.Abs(scale.Y == 0f ? 1f : scale.Y);
        Vector2 halfSizeWorld = new Vector2(boundsLocal.Width * 0.5f * scaleX, boundsLocal.Height * 0.5f * scaleY);

        centerCanvas = new Vector2(WorldToCanvasX(centerWorld.X, canvasOrigin), WorldToCanvasY(centerWorld.Y, canvasOrigin));
        halfSizeCanvas = new Vector2(halfSizeWorld.X * Zoom, halfSizeWorld.Y * Zoom);
        return halfSizeCanvas.X > 0f && halfSizeCanvas.Y > 0f;
    }

    private void DrawMarqueeOverlay(int prefabId, Vector2 canvasOrigin)
    {
        if (_marqueeState != MarqueeState.Active || _marqueePrefabId != prefabId)
        {
            return;
        }

        ImRect rect = MakeRectFromTwoPoints(_marqueeStartCanvas, _marqueeCurrentCanvas);
        uint stroke = ImStyle.WithAlphaF(Im.Style.Primary, 0.95f);
        uint fill = ImStyle.WithAlphaF(Im.Style.Primary, 0.12f);
        Im.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, fill);
        const float thickness = 1.5f;
        float left = rect.X;
        float top = rect.Y;
        float right = rect.Right;
        float bottom = rect.Bottom;
        Im.DrawLine(left, top, right, top, thickness, stroke);
        Im.DrawLine(right, top, right, bottom, thickness, stroke);
        Im.DrawLine(right, bottom, left, bottom, thickness, stroke);
        Im.DrawLine(left, bottom, left, top, thickness, stroke);

        _ = canvasOrigin;
    }

    private static SelectionModifyMode GetSelectionModifyMode(DerpLib.ImGui.Input.ImInput input)
    {
        if (input.KeyAlt)
        {
            return SelectionModifyMode.Subtract;
        }

        if (input.KeyCtrl)
        {
            return SelectionModifyMode.Toggle;
        }

        if (input.KeyShift)
        {
            return SelectionModifyMode.Add;
        }

        return SelectionModifyMode.Replace;
    }

    private void HandleMarqueeSelection(DerpLib.ImGui.Input.ImInput input, Vector2 canvasOrigin, Vector2 mouseCanvas)
    {
        if (_marqueeState == MarqueeState.None)
        {
            return;
        }

        if (!input.MouseDown)
        {
            if (input.MouseReleased)
            {
                if (_marqueeState == MarqueeState.Active)
                {
                    ApplyMarqueeSelection(canvasOrigin);
                }
                else if (_marqueeState == MarqueeState.Pending && !_marqueePrefabEntity.IsNull)
                {
                    SelectPrefabEntity(_marqueePrefabEntity);
                    if (TryGetLegacyPrefabIndex(_marqueePrefabEntity, out int prefabIndex))
                    {
                        _selectedPrefabIndex = prefabIndex;
                    }
                }
            }

            _marqueeState = MarqueeState.None;
            _marqueePrefabEntity = EntityId.Null;
            _marqueePrefabId = 0;
            return;
        }

        _marqueeCurrentCanvas = mouseCanvas;

        if (_marqueeState == MarqueeState.Pending)
        {
            Vector2 d = _marqueeCurrentCanvas - _marqueeStartCanvas;
            float dist2 = d.X * d.X + d.Y * d.Y;
            const float thresholdPx = 4f;
            if (dist2 >= thresholdPx * thresholdPx)
            {
                _marqueeState = MarqueeState.Active;
            }
        }
    }

    private void ApplyMarqueeSelection(Vector2 canvasOrigin)
    {
        if (_marqueePrefabEntity.IsNull || _marqueePrefabId <= 0)
        {
            return;
        }

        Vector2 worldA = CanvasToWorld(_marqueeStartCanvas, canvasOrigin);
        Vector2 worldB = CanvasToWorld(_marqueeCurrentCanvas, canvasOrigin);
        ImRect marqueeWorld = MakeRectFromTwoPoints(worldA, worldB);

        WorldTransform prefabWorldTransform = IdentityWorldTransform;
        if (!TryGetEntityWorldTransformEcs(_marqueePrefabEntity, out prefabWorldTransform))
        {
            prefabWorldTransform = IdentityWorldTransform;
        }

        _marqueeHits.Clear();

        ReadOnlySpan<EntityId> children = _world.GetChildren(_marqueePrefabEntity);
        for (int i = 0; i < children.Length; i++)
        {
            CollectMarqueeHitsRecursiveEcs(children[i], marqueeWorld, prefabWorldTransform, _marqueeHits);
        }

        NormalizeSelectionToRoots(_marqueeHits);

        _selectedPrefabEntity = _marqueePrefabEntity;
        if (TryGetLegacyPrefabIndex(_marqueePrefabEntity, out int prefabIndex))
        {
            _selectedPrefabIndex = prefabIndex;
        }

        ApplySelectionModifyMode(_marqueeModifyMode, _marqueeHits);
    }

    private void CollectMarqueeHitsRecursiveEcs(EntityId entity, in ImRect marqueeWorld, in WorldTransform parentWorldTransform, List<EntityId> hits)
    {
        uint stableId = _world.GetStableId(entity);
        if (GetLayerHidden(stableId) || GetLayerLocked(stableId))
        {
            return;
        }

        if (TryGetEntityPrimitiveBoundsWorldEcs(entity, parentWorldTransform, out ImRect boundsWorld))
        {
            if (boundsWorld.Overlaps(marqueeWorld))
            {
                hits.Add(entity);
            }
        }

        UiNodeType type = _world.GetNodeType(entity);
        if (type == UiNodeType.BooleanGroup)
        {
            if (!TryGetGroupWorldTransformEcs(entity, parentWorldTransform, out WorldTransform groupWorldTransform, out _, out _))
            {
                return;
            }

            if (!groupWorldTransform.IsVisible)
            {
                return;
            }

            ReadOnlySpan<EntityId> groupChildren = _world.GetChildren(entity);
            for (int i = 0; i < groupChildren.Length; i++)
            {
                CollectMarqueeHitsRecursiveEcs(groupChildren[i], marqueeWorld, groupWorldTransform, hits);
            }

            return;
        }

        if (type != UiNodeType.Shape)
        {
            return;
        }

        if (!TryGetShapeWorldTransformEcs(entity, parentWorldTransform, out ShapeWorldTransform shapeWorldTransform))
        {
            return;
        }

        if (!shapeWorldTransform.IsVisible)
        {
            return;
        }

        var shapeParentWorldTransform = new WorldTransform(
            shapeWorldTransform.PositionWorld,
            shapeWorldTransform.ScaleWorld,
            shapeWorldTransform.RotationRadians,
            shapeWorldTransform.IsVisible);

        ReadOnlySpan<EntityId> children = _world.GetChildren(entity);
        for (int i = 0; i < children.Length; i++)
        {
            CollectMarqueeHitsRecursiveEcs(children[i], marqueeWorld, shapeParentWorldTransform, hits);
        }
    }

    private void NormalizeSelectionToRoots(List<EntityId> entities)
    {
        for (int i = 0; i < entities.Count; i++)
        {
            EntityId candidate = entities[i];

            bool shouldRemove = false;
            EntityId current = _world.GetParent(candidate);
            int guard = 512;
            while (!current.IsNull && guard-- > 0)
            {
                if (ListContainsEntity(entities, current))
                {
                    shouldRemove = true;
                    break;
                }

                current = _world.GetParent(current);
            }

            if (shouldRemove)
            {
                entities.RemoveAt(i);
                i--;
            }
        }
    }

    private static bool ListContainsEntity(List<EntityId> entities, EntityId entity)
    {
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i].Value == entity.Value)
            {
                return true;
            }
        }

        return false;
    }

    private void ApplySelectionModifyMode(SelectionModifyMode mode, List<EntityId> hits)
    {
        NotifyInspectorSelectionInteraction();

        if (mode == SelectionModifyMode.Replace)
        {
            _selectedEntities.Clear();
            for (int i = 0; i < hits.Count; i++)
            {
                _selectedEntities.Add(hits[i]);
            }
            return;
        }

        if (mode == SelectionModifyMode.Add)
        {
            for (int i = 0; i < hits.Count; i++)
            {
                EntityId entity = hits[i];
                if (!IsEntitySelected(entity))
                {
                    _selectedEntities.Add(entity);
                }
            }
            return;
        }

        if (mode == SelectionModifyMode.Subtract)
        {
            for (int i = 0; i < hits.Count; i++)
            {
                EntityId entity = hits[i];
                for (int j = 0; j < _selectedEntities.Count; j++)
                {
                    if (_selectedEntities[j].Value == entity.Value)
                    {
                        _selectedEntities.RemoveAt(j);
                        break;
                    }
                }
            }
            return;
        }

        for (int i = 0; i < hits.Count; i++)
        {
            ToggleSelectedEntity(hits[i]);
        }
    }

	    private void DrawEditingPathHandles(int frameId, Vector2 canvasOrigin)
	    {
	        if (!_isEditingPath || _editingPathEntity.IsNull)
	        {
	            return;
	        }

            EntityId shapeEntity = _editingPathEntity;
            if (_world.GetNodeType(shapeEntity) != UiNodeType.Shape)
            {
                return;
            }

            EntityId owningPrefab = FindOwningPrefabEntity(shapeEntity);
            if (owningPrefab.IsNull || !TryGetLegacyPrefabId(owningPrefab, out int prefabId) || prefabId != frameId)
            {
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
                return;
            }

            var view = PathComponent.Api.FromHandle(_propertyWorld, pathHandle);
            if (!view.IsAlive)
            {
                return;
            }

            int vertexCount = view.VertexCount;
            if (vertexCount < 3 || vertexCount > PathComponent.MaxVertices)
            {
                return;
            }

            ReadOnlySpan<Vector2> positionsLocal = view.PositionLocalSpan().Slice(0, vertexCount);
            ReadOnlySpan<Vector2> tangentsInLocal = view.TangentInLocalSpan().Slice(0, vertexCount);
            ReadOnlySpan<Vector2> tangentsOutLocal = view.TangentOutLocalSpan().Slice(0, vertexCount);
            ReadOnlySpan<int> vertexKind = view.VertexKindSpan().Slice(0, vertexCount);

	        if (vertexCount < 3)
	        {
	            return;
	        }

        GetBounds(positionsLocal, out float minLocalX, out float minLocalY, out float maxLocalX, out float maxLocalY);
        Vector2 boundsMinLocal = new Vector2(minLocalX, minLocalY);
        Vector2 boundsSizeLocal = new Vector2(maxLocalX - minLocalX, maxLocalY - minLocalY);

        Vector2 positionWorld = worldTransform.PositionWorld;
        Vector2 anchor = worldTransform.Anchor;
        float rotationRadians = worldTransform.RotationRadians;
        Vector2 pivotLocal = GetPolygonPivotLocalEcs(pathHandle, anchor, boundsMinLocal, boundsSizeLocal);

        Vector2 scaleWorld = worldTransform.ScaleWorld;
        float scaleX = scaleWorld.X == 0f ? 1f : scaleWorld.X;
        float scaleY = scaleWorld.Y == 0f ? 1f : scaleWorld.Y;

	        uint stroke = ImStyle.WithAlphaF(Im.Style.Primary, 0.95f);
	        uint handleStroke = ImStyle.WithAlphaF(Im.Style.Primary, 0.75f);
	        uint handleFill = ImStyle.WithAlphaF(Im.Style.Primary, 0.10f);
	        uint pointFill = ImStyle.WithAlphaF(0xFFFFFFFF, 0.92f);

        for (int i = 0; i < vertexCount; i++)
        {
            Vector2 vertexWorld = TransformPolygonPointToWorld(positionWorld, pivotLocal, rotationRadians, scaleX, scaleY, positionsLocal[i]);
            float vx = WorldToCanvasX(vertexWorld.X, canvasOrigin);
            float vy = WorldToCanvasY(vertexWorld.Y, canvasOrigin);

	            Im.DrawCircle(vx, vy, 4.2f, pointFill);
	            Im.DrawCircleStroke(vx, vy, 4.2f, stroke, 1.5f);

	            if (vertexKind[i] == (int)PathVertexKind.Corner && tangentsInLocal[i] == Vector2.Zero && tangentsOutLocal[i] == Vector2.Zero)
	            {
	                continue;
	            }

            Vector2 inWorld = TransformPolygonPointToWorld(positionWorld, pivotLocal, rotationRadians, scaleX, scaleY, positionsLocal[i] + tangentsInLocal[i]);
            float inX = WorldToCanvasX(inWorld.X, canvasOrigin);
            float inY = WorldToCanvasY(inWorld.Y, canvasOrigin);
	            Im.DrawLine(vx, vy, inX, inY, 1.25f, handleStroke);
	            Im.DrawCircle(inX, inY, 3.4f, handleFill);
	            Im.DrawCircleStroke(inX, inY, 3.4f, handleStroke, 1.25f);

            Vector2 outWorld = TransformPolygonPointToWorld(positionWorld, pivotLocal, rotationRadians, scaleX, scaleY, positionsLocal[i] + tangentsOutLocal[i]);
            float outX = WorldToCanvasX(outWorld.X, canvasOrigin);
            float outY = WorldToCanvasY(outWorld.Y, canvasOrigin);
	            Im.DrawLine(vx, vy, outX, outY, 1.25f, handleStroke);
	            Im.DrawCircle(outX, outY, 3.4f, handleFill);
	            Im.DrawCircleStroke(outX, outY, 3.4f, handleStroke, 1.25f);
	        }
	    }

    private void DrawSelectedShapeAnchors(int frameId, Vector2 canvasOrigin)
    {
        if (_selectedEntities.Count <= 0)
        {
            return;
        }

        uint stroke = ImStyle.WithAlphaF(Im.Style.Primary, 0.95f);
        uint fill = ImStyle.WithAlphaF(0xFFFFFFFF, 0.92f);

        const float ringRadius = 5.5f;
        const float ringInnerRadius = 3.2f;
        const float crossHalf = 7.5f;
        const float crossThickness = 1.5f;

        for (int selectionIndex = 0; selectionIndex < _selectedEntities.Count; selectionIndex++)
        {
            EntityId shapeEntity = _selectedEntities[selectionIndex];
            if (_world.GetNodeType(shapeEntity) != UiNodeType.Shape)
            {
                continue;
            }

            EntityId owningPrefab = FindOwningPrefabEntity(shapeEntity);
            if (owningPrefab.IsNull || !TryGetLegacyPrefabId(owningPrefab, out int prefabId) || prefabId != frameId)
            {
                continue;
            }

            if (!TryGetTransformParentWorldTransformEcs(shapeEntity, out WorldTransform parentWorldTransform))
            {
                parentWorldTransform = IdentityWorldTransform;
            }

            if (!TryGetShapeWorldTransformEcs(shapeEntity, parentWorldTransform, out ShapeWorldTransform worldTransform))
            {
                continue;
            }

            Vector2 anchorWorld = worldTransform.PositionWorld;
            float ax = WorldToCanvasX(anchorWorld.X, canvasOrigin);
            float ay = WorldToCanvasY(anchorWorld.Y, canvasOrigin);

            Im.DrawLine(ax - crossHalf, ay, ax + crossHalf, ay, crossThickness, stroke);
            Im.DrawLine(ax, ay - crossHalf, ax, ay + crossHalf, crossThickness, stroke);

            Im.DrawCircle(ax, ay, ringRadius, stroke);
            Im.DrawCircle(ax, ay, ringInnerRadius, fill);
        }
    }

    private void DrawSelectedLayoutConstraintGizmos(int frameId, Vector2 canvasOrigin)
    {
        if (_selectedEntities.Count != 1)
        {
            return;
        }

        EntityId targetEntity = _selectedEntities[0];
        if (targetEntity.IsNull || _world.GetNodeType(targetEntity) == UiNodeType.None)
        {
            return;
        }

        EntityId owningPrefab = FindOwningPrefabEntity(targetEntity);
        if (owningPrefab.IsNull || !TryGetLegacyPrefabId(owningPrefab, out int prefabId) || prefabId != frameId)
        {
            return;
        }

        if (!_world.TryGetComponent(targetEntity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny))
        {
            return;
        }

        var transformHandle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
        var transform = TransformComponent.Api.FromHandle(_propertyWorld, transformHandle);
        if (!transform.IsAlive || !transform.LayoutChildIgnoreLayout || !transform.LayoutConstraintEnabled)
        {
            return;
        }

        EntityId parentEntity = _world.GetParent(targetEntity);
        if (parentEntity.IsNull)
        {
            return;
        }

        if (!TryGetLayoutIntrinsicSize(parentEntity, out Vector2 parentSize, out Vector2 parentPivot01))
        {
            return;
        }

        if (!TryGetEntityWorldTransformEcs(parentEntity, out WorldTransform parentWorldTransform))
        {
            parentWorldTransform = IdentityWorldTransform;
        }

        var parentRectLocal = RectTransformMath.GetRectFromPivot(Vector2.Zero, parentPivot01, parentSize);
        Vector2 parentMinLocal = new Vector2(parentRectLocal.X, parentRectLocal.Y);
        Vector2 sizeLocal = new Vector2(parentRectLocal.Width, parentRectLocal.Height);
        if (sizeLocal.X <= 0f || sizeLocal.Y <= 0f)
        {
            return;
        }

        Vector2 anchorMin = Clamp01(transform.LayoutConstraintAnchorMin);
        Vector2 anchorMax = Clamp01(transform.LayoutConstraintAnchorMax);
        bool isStretch = anchorMin.X != anchorMax.X || anchorMin.Y != anchorMax.Y;

        Vector2 pMinLocal = parentMinLocal + new Vector2(sizeLocal.X * anchorMin.X, sizeLocal.Y * anchorMin.Y);
        Vector2 pMaxLocal = parentMinLocal + new Vector2(sizeLocal.X * anchorMax.X, sizeLocal.Y * anchorMax.Y);

        Vector2 pMinWorld = TransformPoint(parentWorldTransform, pMinLocal);
        Vector2 pMaxWorld = TransformPoint(parentWorldTransform, pMaxLocal);
        Vector2 pMinCanvas = new Vector2(WorldToCanvasX(pMinWorld.X, canvasOrigin), WorldToCanvasY(pMinWorld.Y, canvasOrigin));
        Vector2 pMaxCanvas = new Vector2(WorldToCanvasX(pMaxWorld.X, canvasOrigin), WorldToCanvasY(pMaxWorld.Y, canvasOrigin));

        uint stroke = ImStyle.WithAlphaF(Im.Style.Primary, 0.95f);
        uint fill = ImStyle.WithAlphaF(0xFFFFFFFF, 0.92f);
        uint regionFill = ImStyle.WithAlphaF(Im.Style.Primary, 0.08f);

        const float handleRadius = 6f;
        const float handleStrokeWidth = 1.5f;

        if (isStretch)
        {
            ImRect region = MakeRectFromTwoPoints(pMinCanvas, pMaxCanvas);
            Im.DrawRect(region.X, region.Y, region.Width, region.Height, regionFill);
            const float thickness = 1.25f;
            float left = region.X;
            float top = region.Y;
            float right = region.Right;
            float bottom = region.Bottom;
            Im.DrawLine(left, top, right, top, thickness, stroke);
            Im.DrawLine(right, top, right, bottom, thickness, stroke);
            Im.DrawLine(right, bottom, left, bottom, thickness, stroke);
            Im.DrawLine(left, bottom, left, top, thickness, stroke);
        }

        Im.DrawCircle(pMinCanvas.X, pMinCanvas.Y, handleRadius, fill);
        Im.DrawCircleStroke(pMinCanvas.X, pMinCanvas.Y, handleRadius, stroke, handleStrokeWidth);

        if (isStretch)
        {
            Im.DrawCircle(pMaxCanvas.X, pMaxCanvas.Y, handleRadius, fill);
            Im.DrawCircleStroke(pMaxCanvas.X, pMaxCanvas.Y, handleRadius, stroke, handleStrokeWidth);
        }
    }

    internal bool HandleLayoutConstraintGizmoInput(DerpLib.ImGui.Input.ImInput input, Vector2 canvasOrigin, Vector2 mouseCanvas)
    {
        if (_layoutConstraintGizmoDrag.Active != 0)
        {
            if (!input.MouseDown)
            {
                if (input.MouseReleased)
                {
                    Commands.NotifyPropertyWidgetState(_layoutConstraintGizmoDrag.WidgetId, isEditing: false);
                    _layoutConstraintGizmoDrag = default;
                }
                return true;
            }

            LayoutConstraintGizmoDragState drag = _layoutConstraintGizmoDrag;
            Commands.SetAnimationTargetOverride(drag.TargetEntity);
            Commands.NotifyPropertyWidgetState(drag.WidgetId, isEditing: true);

            Vector2 mouseWorld = CanvasToWorld(mouseCanvas, canvasOrigin);
            if (!TryGetEntityWorldTransformEcs(drag.ParentEntity, out WorldTransform parentWorldTransform))
            {
                parentWorldTransform = IdentityWorldTransform;
            }

            Vector2 mouseParentLocal = InverseTransformPoint(parentWorldTransform, mouseWorld);
            Vector2 parentSizeSafe = new Vector2(drag.ParentSizeLocal.X == 0f ? 1f : drag.ParentSizeLocal.X, drag.ParentSizeLocal.Y == 0f ? 1f : drag.ParentSizeLocal.Y);
            Vector2 normalized = new Vector2(
                (mouseParentLocal.X - drag.ParentMinLocal.X) / parentSizeSafe.X,
                (mouseParentLocal.Y - drag.ParentMinLocal.Y) / parentSizeSafe.Y);
            normalized = Clamp01(normalized);

            Vector2 nextAnchorMin = drag.StartAnchorMin;
            Vector2 nextAnchorMax = drag.StartAnchorMax;

            if (!drag.StartIsStretch)
            {
                nextAnchorMin = normalized;
                nextAnchorMax = normalized;
            }
            else if (drag.Kind == LayoutConstraintGizmoDragKind.AnchorMin)
            {
                nextAnchorMin = new Vector2(
                    MathF.Min(normalized.X, nextAnchorMax.X),
                    MathF.Min(normalized.Y, nextAnchorMax.Y));
            }
            else if (drag.Kind == LayoutConstraintGizmoDragKind.AnchorMax)
            {
                nextAnchorMax = new Vector2(
                    MathF.Max(normalized.X, nextAnchorMin.X),
                    MathF.Max(normalized.Y, nextAnchorMin.Y));
            }

            Vector2 nextOffsetMin = drag.StartOffsetMin;
            Vector2 nextOffsetMax = drag.StartOffsetMax;

            if (drag.StartIsStretch)
            {
                Vector2 anchoredMinNew = drag.ParentMinLocal + new Vector2(parentSizeSafe.X * nextAnchorMin.X, parentSizeSafe.Y * nextAnchorMin.Y);
                Vector2 anchoredMaxNew = drag.ParentMinLocal + new Vector2(parentSizeSafe.X * nextAnchorMax.X, parentSizeSafe.Y * nextAnchorMax.Y);
                nextOffsetMin = drag.StartRectMinLocal - anchoredMinNew;
                nextOffsetMax = drag.StartRectMaxLocal - anchoredMaxNew;
            }
            else
            {
                Vector2 anchorPointNew = drag.ParentMinLocal + new Vector2(parentSizeSafe.X * nextAnchorMin.X, parentSizeSafe.Y * nextAnchorMin.Y);
                nextOffsetMin = drag.StartPivotPosLocal - anchorPointNew;
            }

            Commands.SetPropertyValue(drag.WidgetId, isEditing: true, drag.TargetEntity, drag.AnchorMinSlot, PropertyValue.FromVec2(nextAnchorMin));
            Commands.SetPropertyValue(drag.WidgetId, isEditing: true, drag.TargetEntity, drag.AnchorMaxSlot, PropertyValue.FromVec2(nextAnchorMax));
            Commands.SetPropertyValue(drag.WidgetId, isEditing: true, drag.TargetEntity, drag.OffsetMinSlot, PropertyValue.FromVec2(nextOffsetMin));
            if (drag.StartIsStretch && drag.OffsetMaxSlot.PropertyId != 0)
            {
                Commands.SetPropertyValue(drag.WidgetId, isEditing: true, drag.TargetEntity, drag.OffsetMaxSlot, PropertyValue.FromVec2(nextOffsetMax));
            }

            return true;
        }

        if (!input.MousePressed || _selectedEntities.Count != 1)
        {
            return false;
        }

        if (!TryGetActivePrefabId(out int activePrefabId))
        {
            return false;
        }

        EntityId targetEntity = _selectedEntities[0];
        EntityId owningPrefab = FindOwningPrefabEntity(targetEntity);
        if (owningPrefab.IsNull || !TryGetLegacyPrefabId(owningPrefab, out int prefabId) || prefabId != activePrefabId)
        {
            return false;
        }

        if (!_world.TryGetComponent(targetEntity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny))
        {
            return false;
        }

        var transformHandle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
        var transform = TransformComponent.Api.FromHandle(_propertyWorld, transformHandle);
        if (!transform.IsAlive || !transform.LayoutChildIgnoreLayout || !transform.LayoutConstraintEnabled)
        {
            return false;
        }

        EntityId parentEntity = _world.GetParent(targetEntity);
        if (parentEntity.IsNull)
        {
            return false;
        }

        if (!TryGetLayoutIntrinsicSize(parentEntity, out Vector2 parentSize, out Vector2 parentPivot01))
        {
            return false;
        }

        if (!TryGetEntityWorldTransformEcs(parentEntity, out WorldTransform parentWorldTransformStart))
        {
            parentWorldTransformStart = IdentityWorldTransform;
        }

        if (!TryGetTransformConstraintSlots(transformAny, out PropertySlot anchorMinSlot, out PropertySlot anchorMaxSlot, out PropertySlot offsetMinSlot, out PropertySlot offsetMaxSlot))
        {
            return false;
        }

        Vector2 anchorMin = Clamp01(transform.LayoutConstraintAnchorMin);
        Vector2 anchorMax = Clamp01(transform.LayoutConstraintAnchorMax);
        Vector2 offsetMin = transform.LayoutConstraintOffsetMin;
        Vector2 offsetMax = transform.LayoutConstraintOffsetMax;
        bool isStretch = anchorMin.X != anchorMax.X || anchorMin.Y != anchorMax.Y;

        var parentRectLocal = RectTransformMath.GetRectFromPivot(Vector2.Zero, parentPivot01, parentSize);
        Vector2 parentMinLocal = new Vector2(parentRectLocal.X, parentRectLocal.Y);
        Vector2 sizeLocal = new Vector2(parentRectLocal.Width, parentRectLocal.Height);
        if (sizeLocal.X <= 0f || sizeLocal.Y <= 0f)
        {
            return false;
        }

        Vector2 pMinLocal = parentMinLocal + new Vector2(sizeLocal.X * anchorMin.X, sizeLocal.Y * anchorMin.Y);
        Vector2 pMaxLocal = parentMinLocal + new Vector2(sizeLocal.X * anchorMax.X, sizeLocal.Y * anchorMax.Y);

        Vector2 pMinWorld = TransformPoint(parentWorldTransformStart, pMinLocal);
        Vector2 pMaxWorld = TransformPoint(parentWorldTransformStart, pMaxLocal);
        Vector2 pMinCanvas = new Vector2(WorldToCanvasX(pMinWorld.X, canvasOrigin), WorldToCanvasY(pMinWorld.Y, canvasOrigin));
        Vector2 pMaxCanvas = new Vector2(WorldToCanvasX(pMaxWorld.X, canvasOrigin), WorldToCanvasY(pMaxWorld.Y, canvasOrigin));

        const float hitRadius = 10f;
        float hitRadiusSq = hitRadius * hitRadius;

        float dMin = Vector2.DistanceSquared(mouseCanvas, pMinCanvas);
        float dMax = Vector2.DistanceSquared(mouseCanvas, pMaxCanvas);

        LayoutConstraintGizmoDragKind kind = LayoutConstraintGizmoDragKind.None;
        if (dMin <= hitRadiusSq)
        {
            kind = LayoutConstraintGizmoDragKind.AnchorMin;
        }
        else if (isStretch && dMax <= hitRadiusSq)
        {
            kind = LayoutConstraintGizmoDragKind.AnchorMax;
        }

        if (kind == LayoutConstraintGizmoDragKind.None)
        {
            return false;
        }

        int widgetId = kind == LayoutConstraintGizmoDragKind.AnchorMin
            ? Im.Context.GetId("layout_anchor_min_gizmo")
            : Im.Context.GetId("layout_anchor_max_gizmo");

        var dragState = new LayoutConstraintGizmoDragState
        {
            Active = 1,
            Kind = kind,
            TargetEntity = targetEntity,
            ParentEntity = parentEntity,
            WidgetId = widgetId,
            TransformAny = transformAny,
            AnchorMinSlot = anchorMinSlot,
            AnchorMaxSlot = anchorMaxSlot,
            OffsetMinSlot = offsetMinSlot,
            OffsetMaxSlot = offsetMaxSlot,
            ParentMinLocal = parentMinLocal,
            ParentSizeLocal = sizeLocal,
            StartAnchorMin = anchorMin,
            StartAnchorMax = anchorMax,
            StartOffsetMin = offsetMin,
            StartOffsetMax = offsetMax,
            StartIsStretch = isStretch,
        };

        if (isStretch)
        {
            Vector2 anchoredMinStart = parentMinLocal + new Vector2(sizeLocal.X * anchorMin.X, sizeLocal.Y * anchorMin.Y);
            Vector2 anchoredMaxStart = parentMinLocal + new Vector2(sizeLocal.X * anchorMax.X, sizeLocal.Y * anchorMax.Y);
            dragState.StartRectMinLocal = anchoredMinStart + offsetMin;
            dragState.StartRectMaxLocal = anchoredMaxStart + offsetMax;
        }
        else
        {
            Vector2 anchorPointStart = parentMinLocal + new Vector2(sizeLocal.X * anchorMin.X, sizeLocal.Y * anchorMin.Y);
            dragState.StartPivotPosLocal = anchorPointStart + offsetMin;
        }

        _layoutConstraintGizmoDrag = dragState;
        Commands.NotifyPropertyWidgetState(widgetId, isEditing: true);
        return true;
    }

    private static bool TryGetTransformConstraintSlots(AnyComponentHandle transformAny, out PropertySlot anchorMinSlot, out PropertySlot anchorMaxSlot, out PropertySlot offsetMinSlot, out PropertySlot offsetMaxSlot)
    {
        anchorMinSlot = default;
        anchorMaxSlot = default;
        offsetMinSlot = default;
        offsetMaxSlot = default;

        int count = PropertyDispatcher.GetPropertyCount(transformAny);
        if (count <= 0)
        {
            return false;
        }

        StringHandle group = "Layout Constraint";
        StringHandle nameAnchorMin = "Anchor Min";
        StringHandle nameAnchorMax = "Anchor Max";
        StringHandle nameOffsetMin = "Offset Min";
        StringHandle nameOffsetMax = "Offset Max";

        ushort anchorMinIndex = 0;
        ushort anchorMaxIndex = 0;
        ushort offsetMinIndex = 0;
        ushort offsetMaxIndex = 0;
        ulong anchorMinId = 0;
        ulong anchorMaxId = 0;
        ulong offsetMinId = 0;
        ulong offsetMaxId = 0;

        for (ushort propertyIndex = 0; propertyIndex < count; propertyIndex++)
        {
            if (!PropertyDispatcher.TryGetInfo(transformAny, propertyIndex, out PropertyInfo info))
            {
                continue;
            }

            if (info.Group != group)
            {
                continue;
            }

            if (info.Name == nameAnchorMin)
            {
                anchorMinIndex = propertyIndex;
                anchorMinId = info.PropertyId;
                continue;
            }

            if (info.Name == nameAnchorMax)
            {
                anchorMaxIndex = propertyIndex;
                anchorMaxId = info.PropertyId;
                continue;
            }

            if (info.Name == nameOffsetMin)
            {
                offsetMinIndex = propertyIndex;
                offsetMinId = info.PropertyId;
                continue;
            }

            if (info.Name == nameOffsetMax)
            {
                offsetMaxIndex = propertyIndex;
                offsetMaxId = info.PropertyId;
            }
        }

        if (anchorMinId == 0 || anchorMaxId == 0 || offsetMinId == 0)
        {
            return false;
        }

        anchorMinSlot = new PropertySlot(transformAny, anchorMinIndex, anchorMinId, PropertyKind.Vec2);
        anchorMaxSlot = new PropertySlot(transformAny, anchorMaxIndex, anchorMaxId, PropertyKind.Vec2);
        offsetMinSlot = new PropertySlot(transformAny, offsetMinIndex, offsetMinId, PropertyKind.Vec2);
        if (offsetMaxId != 0)
        {
            offsetMaxSlot = new PropertySlot(transformAny, offsetMaxIndex, offsetMaxId, PropertyKind.Vec2);
        }

        return true;
    }

	    private struct GroupEffectBounds
	    {
	        public float MaxStrokeWidthCanvas;
	        public float MaxGlowRadiusCanvas;

	        public void AccumulateFrom(in GroupEffectBounds other)
	        {
	            if (other.MaxStrokeWidthCanvas > MaxStrokeWidthCanvas)
	            {
	                MaxStrokeWidthCanvas = other.MaxStrokeWidthCanvas;
	            }
	            if (other.MaxGlowRadiusCanvas > MaxGlowRadiusCanvas)
	            {
	                MaxGlowRadiusCanvas = other.MaxGlowRadiusCanvas;
	            }
	        }

	        public void Accumulate(float strokeWidthCanvas, float glowRadiusCanvas)
	        {
            if (strokeWidthCanvas > MaxStrokeWidthCanvas)
            {
                MaxStrokeWidthCanvas = strokeWidthCanvas;
            }
            if (glowRadiusCanvas > MaxGlowRadiusCanvas)
            {
                MaxGlowRadiusCanvas = glowRadiusCanvas;
            }
        }
    }

			    private void BuildBooleanGroupNode(CanvasSdfDrawList draw, int groupIndex, Vector2 canvasOrigin, in WorldTransform parentWorldTransform, ref GroupEffectBounds effectBounds)
			    {
			        // Legacy SDF render path removed (canvas rendering is ECS-driven).
			        return;
		    }

	    private void EmitBooleanOperand(CanvasSdfDrawList draw, Shape shape, Vector2 canvasOrigin, in WorldTransform parentWorldTransform, ref GroupEffectBounds effectBounds)
{
    // Legacy SDF render path removed (canvas rendering is ECS-driven).
}

    private void BuildShapePrimitive(CanvasSdfDrawList draw, Shape shape, Vector2 canvasOrigin, in WorldTransform parentWorldTransform)
{
    // Legacy SDF render path removed (canvas rendering is ECS-driven).
}

    private static bool TryGetStrokeTrimPercent(
        bool enabled,
        float startPercent,
        float lengthPercent,
        float offsetPercent,
        int capValue,
        float capSoftnessWorld,
        float capSoftnessScalePx,
        float pathLengthPx,
        out SdfStrokeTrim strokeTrim)
    {
        strokeTrim = default;

        if (!enabled)
        {
            return false;
        }

        if (pathLengthPx <= 0.0001f)
        {
            return false;
        }

        if (lengthPercent <= 0f)
        {
            return false;
        }

        float startT = Math.Clamp(startPercent, 0f, 100f) / 100f;
        float lengthT = Math.Clamp(lengthPercent, 0f, 100f) / 100f;
        float offsetT = offsetPercent / 100f;

        float startPx = startT * pathLengthPx;
        float lengthPx = lengthT * pathLengthPx;
        float offsetPx = offsetT * pathLengthPx;

        float softnessPx = Math.Max(0f, capSoftnessWorld) * capSoftnessScalePx;
        if (capValue < 0)
        {
            capValue = 0;
        }
        else if (capValue > 3)
        {
            capValue = 3;
        }

        strokeTrim = new SdfStrokeTrim(
            startPx: startPx,
            lengthPx: lengthPx,
            offsetPx: offsetPx,
            cap: (SdfStrokeCap)capValue,
            capSoftnessPx: softnessPx);
        return true;
    }

    private static float ComputePolylineLength(ReadOnlySpan<Vector2> points)
    {
        float length = 0f;
        for (int i = 1; i < points.Length; i++)
        {
            Vector2 d = points[i] - points[i - 1];
            length += MathF.Sqrt(d.X * d.X + d.Y * d.Y);
        }
        return length;
    }

    private static float ComputeClosedPolylineLength(ReadOnlySpan<Vector2> points)
    {
        float length = ComputePolylineLength(points);
        if (points.Length >= 2)
        {
            Vector2 d = points[0] - points[points.Length - 1];
            length += MathF.Sqrt(d.X * d.X + d.Y * d.Y);
        }
        return length;
    }

    private static float ComputeRoundedRectPerimeter(float width, float height, float radiusTL, float radiusTR, float radiusBR, float radiusBL)
    {
        float w = Math.Max(0f, width);
        float h = Math.Max(0f, height);
        float basePerimeter = 2f * (w + h);

        float sumR = 0f;
        if (radiusTL > 0f) sumR += radiusTL;
        if (radiusTR > 0f) sumR += radiusTR;
        if (radiusBR > 0f) sumR += radiusBR;
        if (radiusBL > 0f) sumR += radiusBL;

        if (sumR <= 0f)
        {
            return basePerimeter;
        }

        return basePerimeter + (MathF.PI * 0.5f - 2f) * sumR;
    }

    private static bool TryGetStrokeDash(
        bool enabled,
        float dashLengthWorld,
        float gapLengthWorld,
        float offsetWorld,
        int capValue,
        float capSoftnessWorld,
        float scalePx,
        out SdfStrokeDash strokeDash)
    {
        strokeDash = default;

        if (!enabled)
        {
            return false;
        }

        if (dashLengthWorld <= 0f)
        {
            return false;
        }

        float dashLengthPx = Math.Max(0f, dashLengthWorld) * scalePx;
        float gapLengthPx = Math.Max(0f, gapLengthWorld) * scalePx;
        float offsetPx = offsetWorld * scalePx;

        float softnessPx = Math.Max(0f, capSoftnessWorld) * scalePx;
        if (capValue < 0)
        {
            capValue = 0;
        }
        else if (capValue > 3)
        {
            capValue = 3;
        }

        strokeDash = new SdfStrokeDash(
            dashLengthPx: dashLengthPx,
            gapLengthPx: gapLengthPx,
            offsetPx: offsetPx,
            cap: (SdfStrokeCap)capValue,
            capSoftnessPx: softnessPx);
        return true;
    }

    private void EnsurePolygonScratchCapacity(int needed)
    {
        if (_polygonCanvasScratch.Length >= needed)
        {
            return;
        }

        int size = _polygonCanvasScratch.Length;
        if (size < 1)
        {
            size = 1;
        }

        while (size < needed)
        {
            size *= 2;
        }

        _polygonCanvasScratch = new Vector2[size];
    }

    private void DrawSelectedGroupOutlines(int frameId, Vector2 canvasOrigin)
    {
        if (_selectedEntities.Count <= 0)
        {
            return;
        }

        uint stroke = ImStyle.WithAlphaF(Im.Style.Primary, 0.95f);
        for (int selectionIndex = 0; selectionIndex < _selectedEntities.Count; selectionIndex++)
        {
            EntityId groupEntity = _selectedEntities[selectionIndex];
            if (_world.GetNodeType(groupEntity) != UiNodeType.BooleanGroup)
            {
                continue;
            }

            EntityId owningPrefab = FindOwningPrefabEntity(groupEntity);
            if (owningPrefab.IsNull || !TryGetLegacyPrefabId(owningPrefab, out int prefabId) || prefabId != frameId)
            {
                continue;
            }

            if (!TryGetLegacyGroupId(groupEntity, out int groupId) || !TryGetGroupIndexById(groupId, out int groupIndex))
            {
                continue;
            }

            if (!TryGetGroupRectCanvasForOutlineEcs(groupEntity, canvasOrigin, out var centerCanvas, out var halfSizeCanvas, out float rotationRadians, out _, out _))
            {
                continue;
            }

            if (rotationRadians == 0f)
            {
                float x = centerCanvas.X - halfSizeCanvas.X;
                float y = centerCanvas.Y - halfSizeCanvas.Y;
                Im.DrawRoundedRectStroke(x, y, halfSizeCanvas.X * 2f, halfSizeCanvas.Y * 2f, 0f, stroke, 2f);
                continue;
            }

            Vector2 tl = centerCanvas + RotateVector(new Vector2(-halfSizeCanvas.X, -halfSizeCanvas.Y), rotationRadians);
            Vector2 tr = centerCanvas + RotateVector(new Vector2(halfSizeCanvas.X, -halfSizeCanvas.Y), rotationRadians);
            Vector2 br = centerCanvas + RotateVector(new Vector2(halfSizeCanvas.X, halfSizeCanvas.Y), rotationRadians);
            Vector2 bl = centerCanvas + RotateVector(new Vector2(-halfSizeCanvas.X, halfSizeCanvas.Y), rotationRadians);

            Im.DrawLine(tl.X, tl.Y, tr.X, tr.Y, 2f, stroke);
            Im.DrawLine(tr.X, tr.Y, br.X, br.Y, 2f, stroke);
            Im.DrawLine(br.X, br.Y, bl.X, bl.Y, 2f, stroke);
            Im.DrawLine(bl.X, bl.Y, tl.X, tl.Y, 2f, stroke);
        }
    }

    private void DrawSelectedGroupHandles(int frameId, Vector2 canvasOrigin)
    {
        if (_selectedEntities.Count != 1)
        {
            return;
        }

        EntityId groupEntity = _selectedEntities[0];
        if (_world.GetNodeType(groupEntity) != UiNodeType.BooleanGroup)
        {
            return;
        }

        EntityId owningPrefab = FindOwningPrefabEntity(groupEntity);
        if (owningPrefab.IsNull || !TryGetLegacyPrefabId(owningPrefab, out int prefabId) || prefabId != frameId)
        {
            return;
        }

        if (!TryGetLegacyGroupId(groupEntity, out int groupId) || !TryGetGroupIndexById(groupId, out int groupIndex))
        {
            return;
        }

        var group = _groups[groupIndex];

        EnsureGroupTransformInitialized(ref group);
        _groups[groupIndex] = group;

        if (!TryGetGroupRectCanvasForOutlineEcs(groupEntity, canvasOrigin, out var centerCanvas, out var halfSizeCanvas, out float rotationRadians, out _, out _))
        {
            return;
        }

        const float handleSize = 10f;
        float half = handleSize * 0.5f;

        uint handleFill = 0xFFFFFFFF;
        uint handleStroke = ImStyle.WithAlphaF(Im.Style.Primary, 1f);

        float hw = halfSizeCanvas.X;
        float hh = halfSizeCanvas.Y;

        // Unrolled handle drawing (no lambdas/closures).
        Vector2 p = centerCanvas + RotateVector(new Vector2(-hw, -hh), rotationRadians);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + RotateVector(new Vector2(0f, -hh), rotationRadians);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + RotateVector(new Vector2(hw, -hh), rotationRadians);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + RotateVector(new Vector2(hw, 0f), rotationRadians);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + RotateVector(new Vector2(hw, hh), rotationRadians);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + RotateVector(new Vector2(0f, hh), rotationRadians);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + RotateVector(new Vector2(-hw, hh), rotationRadians);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + RotateVector(new Vector2(-hw, 0f), rotationRadians);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        // Rotation handle (circle above top-center).
        const float rotationHandleRadius = 7f;
        const float rotationHandleOffset = 26f;
        Vector2 topCenter = centerCanvas + RotateVector(new Vector2(0f, -hh), rotationRadians);
        Vector2 rotationCenter = centerCanvas + RotateVector(new Vector2(0f, -hh - rotationHandleOffset), rotationRadians);
        Im.DrawLine(topCenter.X, topCenter.Y, rotationCenter.X, rotationCenter.Y, 2f, handleStroke);
        Im.DrawCircle(rotationCenter.X, rotationCenter.Y, rotationHandleRadius, handleFill);
        Im.DrawCircleStroke(rotationCenter.X, rotationCenter.Y, rotationHandleRadius, handleStroke, 1.5f);
    }

    private bool TryGetGroupRectCanvasForOutlineEcs(
        EntityId groupEntity,
        Vector2 canvasOrigin,
        out Vector2 centerCanvas,
        out Vector2 halfSizeCanvas,
        out float rotationRadians,
        out WorldTransform groupWorldTransform,
        out ImRect boundsLocal)
    {
        centerCanvas = Vector2.Zero;
        halfSizeCanvas = Vector2.Zero;
        rotationRadians = 0f;
        groupWorldTransform = IdentityWorldTransform;
        boundsLocal = ImRect.Zero;

        if (groupEntity.IsNull)
        {
            return false;
        }

        WorldTransform parentWorldTransform = IdentityWorldTransform;
        if (!TryGetTransformParentWorldTransformEcs(groupEntity, out parentWorldTransform))
        {
            parentWorldTransform = IdentityWorldTransform;
        }

        if (!TryGetGroupWorldTransformEcs(groupEntity, parentWorldTransform, out groupWorldTransform, out _, out _))
        {
            return false;
        }

        if (!groupWorldTransform.IsVisible)
        {
            return false;
        }

        bool hasMask = _world.TryGetComponent(groupEntity, MaskGroupComponent.Api.PoolIdConst, out _);
        bool hasBoolean = _world.TryGetComponent(groupEntity, BooleanGroupComponent.Api.PoolIdConst, out _);
        bool isPlainGroup = !hasMask && !hasBoolean;

        bool hasBounds = false;
        if (isPlainGroup &&
            _world.TryGetComponent(groupEntity, RectGeometryComponent.Api.PoolIdConst, out AnyComponentHandle rectAny) &&
            _world.TryGetComponent(groupEntity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny))
        {
            var rectHandle = new RectGeometryComponentHandle(rectAny.Index, rectAny.Generation);
            var rect = RectGeometryComponent.Api.FromHandle(_propertyWorld, rectHandle);

            var transformHandle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
            var transform = TransformComponent.Api.FromHandle(_propertyWorld, transformHandle);

            Vector2 sizeLocal = rect.IsAlive ? rect.Size : Vector2.Zero;
            if (TryGetComputedSize(groupEntity, out Vector2 computedSize) && computedSize.X > 0f && computedSize.Y > 0f)
            {
                sizeLocal = computedSize;
            }

            if (transform.IsAlive && sizeLocal.X > 0f && sizeLocal.Y > 0f)
            {
                boundsLocal = RectTransformMath.GetRectFromPivot(Vector2.Zero, transform.Anchor, sizeLocal);
                hasBounds = boundsLocal.Width > 0f && boundsLocal.Height > 0f;
            }
        }

        if (!hasBounds && !TryGetGroupSubtreeBoundsLocalEcs(groupEntity, groupWorldTransform, out boundsLocal))
        {
            return false;
        }

        rotationRadians = groupWorldTransform.RotationRadians;

        Vector2 localCenter = new Vector2(boundsLocal.X + boundsLocal.Width * 0.5f, boundsLocal.Y + boundsLocal.Height * 0.5f);
        Vector2 centerWorld = TransformPoint(groupWorldTransform, localCenter);

        Vector2 scale = groupWorldTransform.Scale;
        float scaleX = MathF.Abs(scale.X == 0f ? 1f : scale.X);
        float scaleY = MathF.Abs(scale.Y == 0f ? 1f : scale.Y);
        Vector2 halfSizeWorld = new Vector2(boundsLocal.Width * 0.5f * scaleX, boundsLocal.Height * 0.5f * scaleY);

        centerCanvas = new Vector2(WorldToCanvasX(centerWorld.X, canvasOrigin), WorldToCanvasY(centerWorld.Y, canvasOrigin));
        halfSizeCanvas = new Vector2(halfSizeWorld.X * Zoom, halfSizeWorld.Y * Zoom);
        return true;
    }

    private bool TryGetGroupSubtreeBoundsLocalEcs(EntityId groupEntity, in WorldTransform groupWorldTransform, out ImRect boundsLocal)
    {
        boundsLocal = ImRect.Zero;

        if (groupEntity.IsNull || !groupWorldTransform.IsVisible)
        {
            return false;
        }

        bool hasBounds = false;
        float minX = 0f;
        float minY = 0f;
        float maxX = 0f;
        float maxY = 0f;

        ReadOnlySpan<EntityId> children = _world.GetChildren(groupEntity);
        for (int i = 0; i < children.Length; i++)
        {
            EntityId child = children[i];
            AccumulateGroupSubtreeBoundsLocalEcs(child, groupWorldTransform, groupWorldTransform, ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
        }

        if (!hasBounds)
        {
            return false;
        }

        boundsLocal = ImRect.FromMinMax(minX, minY, maxX, maxY);
        return true;
    }

	    private void AccumulateGroupGeometryLocalBounds(int groupIndex, in WorldTransform groupWorldTransform, Vector2 pivotWorld, float invRotationRadians,
	        ref bool hasBounds, ref float minX, ref float minY, ref float maxX, ref float maxY)
	    {
        if (!groupWorldTransform.IsVisible)
        {
            return;
        }

        if (groupIndex < 0 || groupIndex >= _groups.Count)
        {
            return;
        }

        int groupId = _groups[groupIndex].Id;
        if (!TryGetEntityById(_groupEntityById, groupId, out EntityId groupEntity) || groupEntity.IsNull)
        {
            return;
        }

        if (!TryGetGroupSubtreeBoundsLocalEcs(groupEntity, groupWorldTransform, out ImRect boundsLocal))
        {
            return;
        }

        float lx0 = boundsLocal.X;
        float ly0 = boundsLocal.Y;
        float lx1 = boundsLocal.Right;
        float ly1 = boundsLocal.Bottom;

        if (!hasBounds)
        {
            minX = lx0;
            minY = ly0;
            maxX = lx1;
            maxY = ly1;
            hasBounds = true;
            return;
        }

        if (lx0 < minX) minX = lx0;
        if (ly0 < minY) minY = ly0;
        if (lx1 > maxX) maxX = lx1;
        if (ly1 > maxY) maxY = ly1;
	    }

	    private void AccumulateShapeSubtreeGeometryLocalBounds(Shape shape, in ShapeWorldTransform worldTransform, Vector2 pivotWorld, float invRotationRadians,
	        ref bool hasBounds, ref float minX, ref float minY, ref float maxX, ref float maxY)
	    {
	        AccumulateShapeGeometryLocalBounds(shape, worldTransform, pivotWorld, invRotationRadians, ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
	    }

	    private void AccumulateShapeGeometryLocalBounds(Shape shape, in ShapeWorldTransform worldTransform, Vector2 pivotWorld, float invRotationRadians,
	        ref bool hasBounds, ref float minX, ref float minY, ref float maxX, ref float maxY)
	    {
        if (!worldTransform.IsVisible)
        {
            return;
        }

        Vector2 positionWorld = worldTransform.PositionWorld;
        Vector2 anchor = worldTransform.Anchor;
        float rotationShapeRadians = worldTransform.RotationRadians;

        Vector2 scaleWorld = worldTransform.ScaleWorld;
        float scaleX = scaleWorld.X == 0f ? 1f : scaleWorld.X;
        float scaleY = scaleWorld.Y == 0f ? 1f : scaleWorld.Y;

        ShapeKind kind = GetShapeKind(shape);
	        if (kind == ShapeKind.Polygon)
	        {
	            if (!TryGetPolygonPointsLocal(shape, out ReadOnlySpan<Vector2> pointsLocal))
	            {
	                return;
	            }

	            GetBounds(pointsLocal, out float minLocalX, out float minLocalY, out float maxLocalX, out float maxLocalY);
	            Vector2 boundsMinLocal = new Vector2(minLocalX, minLocalY);
	            Vector2 boundsSizeLocal = new Vector2(maxLocalX - minLocalX, maxLocalY - minLocalY);
	            Vector2 pivotLocal = GetPolygonPivotLocal(shape, anchor, boundsMinLocal, boundsSizeLocal);

	            for (int i = 0; i < pointsLocal.Length; i++)
	            {
	                Vector2 pWorld = TransformPolygonPointToWorld(positionWorld, pivotLocal, rotationShapeRadians, scaleX, scaleY, pointsLocal[i]);
	                AccumulateRotatedBounds(pWorld, pivotWorld, invRotationRadians, ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
	            }

	            return;
	        }

        if (kind == ShapeKind.Circle)
        {
            var circleGeometry = CircleGeometryComponent.Api.FromHandle(_propertyWorld, shape.CircleGeometry);
            float radiusWorld = circleGeometry.Radius * ((scaleX + scaleY) * 0.5f);
            float diameterWorld = radiusWorld * 2f;

            Vector2 anchorOffset = new Vector2(
                (anchor.X - 0.5f) * diameterWorld,
                (anchor.Y - 0.5f) * diameterWorld);
            Vector2 centerWorld = positionWorld - RotateVector(anchorOffset, rotationShapeRadians);

            Vector2 centerLocal = RotateVector(new Vector2(centerWorld.X - pivotWorld.X, centerWorld.Y - pivotWorld.Y), invRotationRadians);
            float lx0 = centerLocal.X - radiusWorld;
            float ly0 = centerLocal.Y - radiusWorld;
            float lx1 = centerLocal.X + radiusWorld;
            float ly1 = centerLocal.Y + radiusWorld;

            if (!hasBounds)
            {
                hasBounds = true;
                minX = lx0;
                minY = ly0;
                maxX = lx1;
                maxY = ly1;
            }
            else
            {
                if (lx0 < minX) minX = lx0;
                if (ly0 < minY) minY = ly0;
                if (lx1 > maxX) maxX = lx1;
                if (ly1 > maxY) maxY = ly1;
            }

            return;
        }

        // Rect (or any other non-polygon treated as rect bounds).
        var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, shape.RectGeometry);
        float widthWorld = rectGeometry.Size.X * scaleX;
        float heightWorld = rectGeometry.Size.Y * scaleY;

        Vector2 anchorOffsetRect = new Vector2(
            (anchor.X - 0.5f) * widthWorld,
            (anchor.Y - 0.5f) * heightWorld);
        Vector2 centerWorldRect = positionWorld - RotateVector(anchorOffsetRect, rotationShapeRadians);

        float hw = widthWorld * 0.5f;
        float hh = heightWorld * 0.5f;

        Vector2 c0 = centerWorldRect + RotateVector(new Vector2(-hw, -hh), rotationShapeRadians);
        Vector2 c1 = centerWorldRect + RotateVector(new Vector2(hw, -hh), rotationShapeRadians);
        Vector2 c2 = centerWorldRect + RotateVector(new Vector2(hw, hh), rotationShapeRadians);
        Vector2 c3 = centerWorldRect + RotateVector(new Vector2(-hw, hh), rotationShapeRadians);

        AccumulateRotatedBounds(c0, pivotWorld, invRotationRadians, ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
        AccumulateRotatedBounds(c1, pivotWorld, invRotationRadians, ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
        AccumulateRotatedBounds(c2, pivotWorld, invRotationRadians, ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
        AccumulateRotatedBounds(c3, pivotWorld, invRotationRadians, ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
    }

    private static void AccumulateRotatedBounds(Vector2 pointWorld, Vector2 pivotWorld, float invRotationRadians,
        ref bool hasBounds, ref float minX, ref float minY, ref float maxX, ref float maxY)
    {
        Vector2 local = RotateVector(new Vector2(pointWorld.X - pivotWorld.X, pointWorld.Y - pivotWorld.Y), invRotationRadians);
        if (!hasBounds)
        {
            minX = local.X;
            minY = local.Y;
            maxX = local.X;
            maxY = local.Y;
            hasBounds = true;
            return;
        }

        if (local.X < minX) minX = local.X;
        if (local.Y < minY) minY = local.Y;
        if (local.X > maxX) maxX = local.X;
        if (local.Y > maxY) maxY = local.Y;
    }

    private bool TryGetGroupGeometryLocalBounds(int groupIndex, in WorldTransform groupWorldTransform, Vector2 pivotWorld, float invRotationRadians, out ImRect boundsLocal)
    {
        float minX = 0f;
        float minY = 0f;
        float maxX = 0f;
        float maxY = 0f;
        bool hasBounds = false;

        AccumulateGroupGeometryLocalBounds(groupIndex, groupWorldTransform, pivotWorld, invRotationRadians, ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
        if (!hasBounds)
        {
            boundsLocal = ImRect.Zero;
            return false;
        }

        boundsLocal = new ImRect(minX, minY, maxX - minX, maxY - minY);
        return true;
    }

    private void DrawSelectedShapeOutlines(int frameId, Vector2 canvasOrigin)
    {
        if (_selectedEntities.Count <= 0)
        {
            return;
        }

        uint stroke = ImStyle.WithAlphaF(Im.Style.Primary, 0.95f);
        for (int selectionIndex = 0; selectionIndex < _selectedEntities.Count; selectionIndex++)
        {
            EntityId shapeEntity = _selectedEntities[selectionIndex];
            UiNodeType nodeType = _world.GetNodeType(shapeEntity);
            if (nodeType != UiNodeType.Shape && nodeType != UiNodeType.Text)
            {
                continue;
            }

            EntityId owningPrefab = FindOwningPrefabEntity(shapeEntity);
            if (owningPrefab.IsNull || !TryGetLegacyPrefabId(owningPrefab, out int prefabId) || prefabId != frameId)
            {
                continue;
            }

            if (!TryGetTransformParentWorldTransformEcs(shapeEntity, out WorldTransform parentWorldTransform))
            {
                parentWorldTransform = IdentityWorldTransform;
            }

            if (nodeType == UiNodeType.Text)
            {
                if (!TryGetTextWorldTransformEcs(shapeEntity, parentWorldTransform, out ShapeWorldTransform worldTransform))
                {
                    continue;
                }

                if (!_world.TryGetComponent(shapeEntity, RectGeometryComponent.Api.PoolIdConst, out AnyComponentHandle rectAny))
                {
                    continue;
                }

                var textRectGeometryHandle = new RectGeometryComponentHandle(rectAny.Index, rectAny.Generation);
                ImRect textRectCanvas = GetShapeRectCanvasForDrawEcs(shapeEntity, ShapeKind.Rect, textRectGeometryHandle, default, canvasOrigin, worldTransform, out Vector2 centerCanvas, out float rotationRadiansRect);
                DrawRectOutline(textRectCanvas, centerCanvas, rotationRadiansRect, stroke);
                continue;
            }

            if (!TryGetShapeWorldTransformEcs(shapeEntity, parentWorldTransform, out ShapeWorldTransform shapeWorldTransform))
            {
                continue;
            }

            ShapeKind kind = GetShapeKindEcs(shapeEntity, out RectGeometryComponentHandle rectGeometryHandle, out CircleGeometryComponentHandle circleGeometryHandle, out PathComponentHandle pathHandle);
            if (kind == ShapeKind.Polygon)
            {
                if (!TryGetPolygonPointsLocalEcs(pathHandle, out ReadOnlySpan<Vector2> pointsLocal, out int pointCount) || pointCount < 2)
                {
                    continue;
                }

                GetBounds(pointsLocal, out float minLocalX, out float minLocalY, out float maxLocalX, out float maxLocalY);
                Vector2 boundsMinLocal = new Vector2(minLocalX, minLocalY);
                Vector2 boundsSizeLocal = new Vector2(maxLocalX - minLocalX, maxLocalY - minLocalY);

                Vector2 positionWorld = shapeWorldTransform.PositionWorld;
                Vector2 anchor = shapeWorldTransform.Anchor;
                float rotationRadians = shapeWorldTransform.RotationRadians;
                Vector2 pivotLocal = GetPolygonPivotLocalEcs(pathHandle, anchor, boundsMinLocal, boundsSizeLocal);

                Vector2 scaleWorld = shapeWorldTransform.ScaleWorld;
                float scaleX = scaleWorld.X == 0f ? 1f : scaleWorld.X;
                float scaleY = scaleWorld.Y == 0f ? 1f : scaleWorld.Y;

                Vector2 firstWorld = TransformPolygonPointToWorld(positionWorld, pivotLocal, rotationRadians, scaleX, scaleY, pointsLocal[0]);
                Vector2 firstCanvas = new Vector2(
                    WorldToCanvasX(firstWorld.X, canvasOrigin),
                    WorldToCanvasY(firstWorld.Y, canvasOrigin));
                Vector2 prevCanvas = firstCanvas;

                for (int i = 1; i < pointCount; i++)
                {
                    Vector2 pWorld = TransformPolygonPointToWorld(positionWorld, pivotLocal, rotationRadians, scaleX, scaleY, pointsLocal[i]);
                    Vector2 pCanvas = new Vector2(
                        WorldToCanvasX(pWorld.X, canvasOrigin),
                        WorldToCanvasY(pWorld.Y, canvasOrigin));
                    Im.DrawLine(prevCanvas.X, prevCanvas.Y, pCanvas.X, pCanvas.Y, 2f, stroke);
                    prevCanvas = pCanvas;
                }

                Im.DrawLine(prevCanvas.X, prevCanvas.Y, firstCanvas.X, firstCanvas.Y, 2f, stroke);
                continue;
            }

            ImRect shapeRectCanvas = GetShapeRectCanvasForDrawEcs(shapeEntity, kind, rectGeometryHandle, circleGeometryHandle, canvasOrigin, shapeWorldTransform, out Vector2 shapeCenterCanvas, out float rotationRadiansShape);
            if (kind == ShapeKind.Rect)
            {
                DrawRectOutline(shapeRectCanvas, shapeCenterCanvas, rotationRadiansShape, stroke);
            }
            else
            {
                float radius = Math.Min(shapeRectCanvas.Width, shapeRectCanvas.Height) * 0.5f;
                Im.DrawCircleStroke(shapeCenterCanvas.X, shapeCenterCanvas.Y, radius, stroke, 2f);
            }
        }
    }

    private static void DrawRectOutline(ImRect rectCanvas, Vector2 centerCanvas, float rotationRadians, uint stroke)
    {
        float hw = rectCanvas.Width * 0.5f;
        float hh = rectCanvas.Height * 0.5f;

        Vector2 tl = centerCanvas + RotateVector(new Vector2(-hw, -hh), rotationRadians);
        Vector2 tr = centerCanvas + RotateVector(new Vector2(hw, -hh), rotationRadians);
        Vector2 br = centerCanvas + RotateVector(new Vector2(hw, hh), rotationRadians);
        Vector2 bl = centerCanvas + RotateVector(new Vector2(-hw, hh), rotationRadians);

        Im.DrawLine(tl.X, tl.Y, tr.X, tr.Y, 2f, stroke);
        Im.DrawLine(tr.X, tr.Y, br.X, br.Y, 2f, stroke);
        Im.DrawLine(br.X, br.Y, bl.X, bl.Y, 2f, stroke);
        Im.DrawLine(bl.X, bl.Y, tl.X, tl.Y, 2f, stroke);
    }

    private void DrawSelectedShapeHandles(int frameId, Vector2 canvasOrigin)
    {
        if (_selectedEntities.Count != 1)
        {
            return;
        }

        EntityId targetEntity = _selectedEntities[0];
        UiNodeType nodeType = _world.GetNodeType(targetEntity);
        if (nodeType != UiNodeType.Shape && nodeType != UiNodeType.Text)
        {
            return;
        }

        EntityId owningPrefab = FindOwningPrefabEntity(targetEntity);
        if (owningPrefab.IsNull || !TryGetLegacyPrefabId(owningPrefab, out int prefabId) || prefabId != frameId)
        {
            return;
        }

        if (!TryGetTransformParentWorldTransformEcs(targetEntity, out WorldTransform parentWorldTransform))
        {
            parentWorldTransform = IdentityWorldTransform;
        }

        uint handleFill = 0xFFFFFFFF;
        uint handleStroke = ImStyle.WithAlphaF(Im.Style.Primary, 1f);

        if (nodeType == UiNodeType.Text)
        {
            if (!TryGetTextWorldTransformEcs(targetEntity, parentWorldTransform, out ShapeWorldTransform worldTransform))
            {
                return;
            }

            if (!_world.TryGetComponent(targetEntity, RectGeometryComponent.Api.PoolIdConst, out AnyComponentHandle rectAny))
            {
                return;
            }

            var rectGeometryHandle = new RectGeometryComponentHandle(rectAny.Index, rectAny.Generation);
            ImRect rectCanvas = GetShapeRectCanvasForDrawEcs(targetEntity, ShapeKind.Rect, rectGeometryHandle, default, canvasOrigin, worldTransform, out Vector2 centerCanvas, out float rotationRadians);
            DrawRectSelectionHandles(centerCanvas, rectCanvas.Width * 0.5f, rectCanvas.Height * 0.5f, rotationRadians, handleFill, handleStroke);
            return;
        }

        if (!TryGetShapeWorldTransformEcs(targetEntity, parentWorldTransform, out ShapeWorldTransform shapeWorldTransform))
        {
            return;
        }

        ShapeKind kind = GetShapeKindEcs(targetEntity, out RectGeometryComponentHandle shapeRectGeometryHandle, out CircleGeometryComponentHandle shapeCircleGeometryHandle, out _);
        if (kind == ShapeKind.Polygon)
        {
            return;
        }

        ImRect shapeRectCanvas = GetShapeRectCanvasForDrawEcs(targetEntity, kind, shapeRectGeometryHandle, shapeCircleGeometryHandle, canvasOrigin, shapeWorldTransform, out Vector2 shapeCenterCanvas, out float rotationRadiansShape);
        DrawRectSelectionHandles(shapeCenterCanvas, shapeRectCanvas.Width * 0.5f, shapeRectCanvas.Height * 0.5f, rotationRadiansShape, handleFill, handleStroke);
    }

    private static void DrawRectSelectionHandles(Vector2 centerCanvas, float halfWidth, float halfHeight, float rotationRadians, uint handleFill, uint handleStroke)
    {
        const float handleSize = 10f;
        float half = handleSize * 0.5f;

        float hw = Math.Max(1f, halfWidth);
        float hh = Math.Max(1f, halfHeight);

        // Unrolled handle drawing (no lambdas/closures).
        Vector2 p = centerCanvas + RotateVector(new Vector2(-hw, -hh), rotationRadians);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + RotateVector(new Vector2(0f, -hh), rotationRadians);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + RotateVector(new Vector2(hw, -hh), rotationRadians);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + RotateVector(new Vector2(hw, 0f), rotationRadians);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + RotateVector(new Vector2(hw, hh), rotationRadians);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + RotateVector(new Vector2(0f, hh), rotationRadians);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + RotateVector(new Vector2(-hw, hh), rotationRadians);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        p = centerCanvas + RotateVector(new Vector2(-hw, 0f), rotationRadians);
        Im.DrawRect(p.X - half, p.Y - half, handleSize, handleSize, handleFill);
        Im.DrawRoundedRectStroke(p.X - half, p.Y - half, handleSize, handleSize, 0f, handleStroke, 1.5f);

        // Rotation handle (circle above top-center).
        const float rotationHandleRadius = 7f;
        const float rotationHandleOffset = 26f;
        Vector2 topCenter = centerCanvas + RotateVector(new Vector2(0f, -hh), rotationRadians);
        Vector2 rotationCenter = centerCanvas + RotateVector(new Vector2(0f, -hh - rotationHandleOffset), rotationRadians);
        Im.DrawLine(topCenter.X, topCenter.Y, rotationCenter.X, rotationCenter.Y, 2f, handleStroke);
        Im.DrawCircle(rotationCenter.X, rotationCenter.Y, rotationHandleRadius, handleFill);
        Im.DrawCircleStroke(rotationCenter.X, rotationCenter.Y, rotationHandleRadius, handleStroke, 1.5f);
    }

    private struct PaintGradientGizmoInfo
    {
        public EntityId TargetEntity;
        public PaintComponentHandle PaintHandle;
        public int LayerIndex;
        public int GradientKind;
        public Vector2 GradientDirection;
        public Vector2 GradientCenterNorm;
        public float GradientRadiusScale;
        public float GradientAngleOffset;
        public Vector2 ShapeCenterCanvas;
        public Vector2 ShapeSizeCanvas;
        public float RotationRadians;
    }

    private bool TryGetActivePaintGradientGizmoInfo(int frameId, Vector2 canvasOrigin, out PaintGradientGizmoInfo info)
    {
        info = default;
        if (!PropertyInspector.TryGetActivePaintFillGradientEditor(out EntityId targetEntity, out PaintComponentHandle paintHandle, out int layerIndex))
        {
            return false;
        }

        if (targetEntity.IsNull)
        {
            return false;
        }

        UiNodeType nodeType = _world.GetNodeType(targetEntity);
        if (nodeType != UiNodeType.Shape && nodeType != UiNodeType.Text)
        {
            return false;
        }

        EntityId owningPrefab = FindOwningPrefabEntity(targetEntity);
        if (owningPrefab.IsNull || !TryGetLegacyPrefabId(owningPrefab, out int prefabId) || prefabId != frameId)
        {
            return false;
        }

        var paintView = PaintComponent.Api.FromHandle(_propertyWorld, paintHandle);
        if (!paintView.IsAlive || paintView.LayerCount <= 0)
        {
            return false;
        }

        int layerCount = paintView.LayerCount;
        if ((uint)layerIndex >= (uint)layerCount)
        {
            return false;
        }

        ReadOnlySpan<bool> fillUseGradient = PaintComponentProperties.FillUseGradientArray(_propertyWorld, paintHandle);
        if ((uint)layerIndex >= (uint)fillUseGradient.Length || !fillUseGradient[layerIndex])
        {
            return false;
        }

        if (!TryGetGradientGizmoSpaceCanvas(targetEntity, canvasOrigin, out Vector2 shapeCenterCanvas, out Vector2 shapeSizeCanvas, out float rotationRadians))
        {
            return false;
        }

        ReadOnlySpan<int> fillGradientType = PaintComponentProperties.FillGradientTypeArray(_propertyWorld, paintHandle);
        ReadOnlySpan<Vector2> fillGradientDirection = PaintComponentProperties.FillGradientDirectionArray(_propertyWorld, paintHandle);
        ReadOnlySpan<Vector2> fillGradientCenter = PaintComponentProperties.FillGradientCenterArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> fillGradientRadius = PaintComponentProperties.FillGradientRadiusArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> fillGradientAngle = PaintComponentProperties.FillGradientAngleArray(_propertyWorld, paintHandle);

        int gradientKind = (uint)layerIndex < (uint)fillGradientType.Length ? fillGradientType[layerIndex] : 0;
        gradientKind = Math.Clamp(gradientKind, 0, 2);

        info = new PaintGradientGizmoInfo
        {
            TargetEntity = targetEntity,
            PaintHandle = paintHandle,
            LayerIndex = layerIndex,
            GradientKind = gradientKind,
            GradientDirection = (uint)layerIndex < (uint)fillGradientDirection.Length ? fillGradientDirection[layerIndex] : new Vector2(1f, 0f),
            GradientCenterNorm = (uint)layerIndex < (uint)fillGradientCenter.Length ? fillGradientCenter[layerIndex] : Vector2.Zero,
            GradientRadiusScale = (uint)layerIndex < (uint)fillGradientRadius.Length ? fillGradientRadius[layerIndex] : 1f,
            GradientAngleOffset = (uint)layerIndex < (uint)fillGradientAngle.Length ? fillGradientAngle[layerIndex] : 0f,
            ShapeCenterCanvas = shapeCenterCanvas,
            ShapeSizeCanvas = shapeSizeCanvas,
            RotationRadians = rotationRadians
        };

        return true;
    }

    private bool TryGetGradientGizmoSpaceCanvas(EntityId shapeEntity, Vector2 canvasOrigin, out Vector2 centerCanvas, out Vector2 sizeCanvas, out float rotationRadians)
    {
        centerCanvas = default;
        sizeCanvas = default;
        rotationRadians = 0f;

        UiNodeType nodeType = _world.GetNodeType(shapeEntity);
        if (nodeType == UiNodeType.Text)
        {
            if (!TryGetTransformParentWorldTransformEcs(shapeEntity, out WorldTransform textParentWorldTransform))
            {
                textParentWorldTransform = IdentityWorldTransform;
            }

            if (!TryGetTextWorldTransformEcs(shapeEntity, textParentWorldTransform, out ShapeWorldTransform textWorldTransform))
            {
                return false;
            }

            if (!_world.TryGetComponent(shapeEntity, RectGeometryComponent.Api.PoolIdConst, out AnyComponentHandle rectAny))
            {
                return false;
            }

            var textRectGeometryHandle = new RectGeometryComponentHandle(rectAny.Index, rectAny.Generation);
            ImRect textRectCanvas = GetShapeRectCanvasForDrawEcs(shapeEntity, ShapeKind.Rect, textRectGeometryHandle, default, canvasOrigin, textWorldTransform, out centerCanvas, out rotationRadians);
            sizeCanvas = new Vector2(Math.Max(1f, textRectCanvas.Width * 0.5f), Math.Max(1f, textRectCanvas.Height * 0.5f));
            return true;
        }

        if (!TryGetTransformParentWorldTransformEcs(shapeEntity, out WorldTransform parentWorldTransform))
        {
            parentWorldTransform = IdentityWorldTransform;
        }

        if (!TryGetShapeWorldTransformEcs(shapeEntity, parentWorldTransform, out ShapeWorldTransform worldTransform))
        {
            return false;
        }

        ShapeKind kind = GetShapeKindEcs(shapeEntity, out RectGeometryComponentHandle rectGeometryHandle, out CircleGeometryComponentHandle circleGeometryHandle, out PathComponentHandle pathHandle);
        rotationRadians = worldTransform.RotationRadians;

        if (kind == ShapeKind.Polygon)
        {
            if (!TryGetPolygonPointsLocalEcs(pathHandle, out ReadOnlySpan<Vector2> pointsLocal, out int pointCount))
            {
                return false;
            }

            Vector2 positionWorld = worldTransform.PositionWorld;
            Vector2 scaleWorld = worldTransform.ScaleWorld;
            float scaleX = scaleWorld.X == 0f ? 1f : scaleWorld.X;
            float scaleY = scaleWorld.Y == 0f ? 1f : scaleWorld.Y;

            GetBounds(pointsLocal, out float minLocalX, out float minLocalY, out float maxLocalX, out float maxLocalY);
            Vector2 boundsMinLocal = new Vector2(minLocalX, minLocalY);
            Vector2 boundsSizeLocal = new Vector2(maxLocalX - minLocalX, maxLocalY - minLocalY);
            Vector2 pivotLocal = GetPolygonPivotLocalEcs(pathHandle, worldTransform.Anchor, boundsMinLocal, boundsSizeLocal);

            float minCanvasX = float.MaxValue;
            float minCanvasY = float.MaxValue;
            float maxCanvasX = float.MinValue;
            float maxCanvasY = float.MinValue;

            for (int i = 0; i < pointCount; i++)
            {
                Vector2 pWorld = TransformPolygonPointToWorld(positionWorld, pivotLocal, rotationRadians, scaleX, scaleY, pointsLocal[i]);
                float canvasX = WorldToCanvasX(pWorld.X, canvasOrigin);
                float canvasY = WorldToCanvasY(pWorld.Y, canvasOrigin);

                if (canvasX < minCanvasX) minCanvasX = canvasX;
                if (canvasY < minCanvasY) minCanvasY = canvasY;
                if (canvasX > maxCanvasX) maxCanvasX = canvasX;
                if (canvasY > maxCanvasY) maxCanvasY = canvasY;
            }

            centerCanvas = new Vector2((minCanvasX + maxCanvasX) * 0.5f, (minCanvasY + maxCanvasY) * 0.5f);
            sizeCanvas = new Vector2(Math.Max(1f, (maxCanvasX - minCanvasX) * 0.5f), Math.Max(1f, (maxCanvasY - minCanvasY) * 0.5f));
            return true;
        }

        ImRect rectCanvas = GetShapeRectCanvasForDrawEcs(shapeEntity, kind, rectGeometryHandle, circleGeometryHandle, canvasOrigin, worldTransform, out centerCanvas, out rotationRadians);
        if (kind == ShapeKind.Rect)
        {
            sizeCanvas = new Vector2(Math.Max(1f, rectCanvas.Width * 0.5f), Math.Max(1f, rectCanvas.Height * 0.5f));
            return true;
        }

        float radiusCanvas = Math.Min(rectCanvas.Width, rectCanvas.Height) * 0.5f;
        radiusCanvas = Math.Max(1f, radiusCanvas);
        sizeCanvas = new Vector2(radiusCanvas, radiusCanvas);
        return true;
    }

    internal void DrawActivePaintGradientGizmo(int frameId, Vector2 canvasOrigin)
    {
        if (!TryGetActivePaintGradientGizmoInfo(frameId, canvasOrigin, out PaintGradientGizmoInfo info))
        {
            return;
        }

        uint stroke = ImStyle.WithAlphaF(Im.Style.Primary, 0.95f);
        uint fill = 0xFFFFFFFF;
        uint line = ImStyle.WithAlphaF(Im.Style.Primary, 0.85f);

        const float handleRadius = 7f;
        const float lineWidth = 2f;

        Vector2 shapeCenterCanvas = info.ShapeCenterCanvas;
        Vector2 sizeCanvas = info.ShapeSizeCanvas;
        float rotationRadians = info.RotationRadians;

        float baseDist = Math.Max(sizeCanvas.X, sizeCanvas.Y);
        baseDist = Math.Max(1f, baseDist);

        if (info.GradientKind == 0)
        {
            Vector2 dir = info.GradientDirection;
            float lenSq = dir.LengthSquared();
            if (lenSq < 0.0001f)
            {
                dir = new Vector2(1f, 0f);
            }
            else
            {
                dir /= MathF.Sqrt(lenSq);
            }

            float handleDist = Math.Max(20f, baseDist);
            Vector2 handleLocal = dir * handleDist;
            Vector2 handleCanvas = shapeCenterCanvas + RotateVector(handleLocal, rotationRadians);

            Im.DrawLine(shapeCenterCanvas.X, shapeCenterCanvas.Y, handleCanvas.X, handleCanvas.Y, lineWidth, line);
            Im.DrawCircle(handleCanvas.X, handleCanvas.Y, handleRadius, fill);
            Im.DrawCircleStroke(handleCanvas.X, handleCanvas.Y, handleRadius, stroke, 1.5f);
            return;
        }

        Vector2 centerNorm = info.GradientCenterNorm;
        Vector2 centerLocalPx = new Vector2(centerNorm.X * sizeCanvas.X, centerNorm.Y * sizeCanvas.Y);
        Vector2 centerCanvas = shapeCenterCanvas + RotateVector(centerLocalPx, rotationRadians);

        Im.DrawCircle(centerCanvas.X, centerCanvas.Y, handleRadius, fill);
        Im.DrawCircleStroke(centerCanvas.X, centerCanvas.Y, handleRadius, stroke, 1.5f);

        if (info.GradientKind == 1)
        {
            float radiusPx = baseDist * Math.Max(0.0001f, info.GradientRadiusScale);
            Im.DrawCircleStroke(centerCanvas.X, centerCanvas.Y, radiusPx, line, 1.5f);

            Vector2 radiusHandleCanvas = centerCanvas + RotateVector(new Vector2(radiusPx, 0f), rotationRadians);
            Im.DrawLine(centerCanvas.X, centerCanvas.Y, radiusHandleCanvas.X, radiusHandleCanvas.Y, lineWidth, line);
            Im.DrawCircle(radiusHandleCanvas.X, radiusHandleCanvas.Y, handleRadius, fill);
            Im.DrawCircleStroke(radiusHandleCanvas.X, radiusHandleCanvas.Y, handleRadius, stroke, 1.5f);
            return;
        }

        float angle = info.GradientAngleOffset;
        float handleDistAngle = Math.Max(20f, baseDist * 0.75f);
        Vector2 angleDirLocal = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        Vector2 angleHandleCanvas = centerCanvas + RotateVector(angleDirLocal * handleDistAngle, rotationRadians);
        Im.DrawLine(centerCanvas.X, centerCanvas.Y, angleHandleCanvas.X, angleHandleCanvas.Y, lineWidth, line);
        Im.DrawCircle(angleHandleCanvas.X, angleHandleCanvas.Y, handleRadius, fill);
        Im.DrawCircleStroke(angleHandleCanvas.X, angleHandleCanvas.Y, handleRadius, stroke, 1.5f);
    }

    internal bool HandlePaintGradientGizmoInput(DerpLib.ImGui.Input.ImInput input, Vector2 canvasOrigin, Vector2 mouseCanvas)
    {
        if (_gradientGizmoDrag.Active != 0)
        {
            if (!input.MouseDown)
            {
                if (input.MouseReleased)
                {
                    Commands.NotifyPropertyWidgetState(_gradientGizmoDrag.WidgetId, isEditing: false);
                    _gradientGizmoDrag = default;
                }
                return true;
            }

            GradientGizmoDragState drag = _gradientGizmoDrag;
            Commands.SetAnimationTargetOverride(drag.TargetEntity);

            Vector2 deltaCanvas = mouseCanvas - drag.ShapeCenterCanvas;
            Vector2 deltaLocal = RotateVector(deltaCanvas, -drag.RotationRadians);
            Vector2 normalized = new Vector2(deltaLocal.X / drag.ShapeSizeCanvas.X, deltaLocal.Y / drag.ShapeSizeCanvas.Y);

            AnyComponentHandle paintAny = PaintComponentProperties.ToAnyHandle(drag.PaintHandle);
            Commands.NotifyPropertyWidgetState(drag.WidgetId, isEditing: true);

            if (drag.Kind == GradientGizmoDragKind.LinearDirection)
            {
                float lenSq = normalized.LengthSquared();
                Vector2 dir = lenSq < 0.0001f ? new Vector2(1f, 0f) : normalized / MathF.Sqrt(lenSq);
                PropertySlot slot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientDirection", drag.LayerIndex, PropertyKind.Vec2);
                Commands.SetPropertyValue(drag.WidgetId, isEditing: true, drag.TargetEntity, slot, PropertyValue.FromVec2(dir));
                return true;
            }

            if (drag.Kind == GradientGizmoDragKind.Center)
            {
                PropertySlot slot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientCenter", drag.LayerIndex, PropertyKind.Vec2);
                Commands.SetPropertyValue(drag.WidgetId, isEditing: true, drag.TargetEntity, slot, PropertyValue.FromVec2(normalized));
                return true;
            }

            Vector2 centerNorm = PropertyDispatcher.ReadVec2(_propertyWorld,
                PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientCenter", drag.LayerIndex, PropertyKind.Vec2));
            Vector2 centerLocalPx = new Vector2(centerNorm.X * drag.ShapeSizeCanvas.X, centerNorm.Y * drag.ShapeSizeCanvas.Y);
            Vector2 offsetLocal = deltaLocal - centerLocalPx;

            if (drag.Kind == GradientGizmoDragKind.RadialRadius)
            {
                float radiusBaseDist = Math.Max(drag.ShapeSizeCanvas.X, drag.ShapeSizeCanvas.Y);
                radiusBaseDist = Math.Max(1f, radiusBaseDist);
                float radiusScale = offsetLocal.Length() / radiusBaseDist;
                PropertySlot slot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientRadius", drag.LayerIndex, PropertyKind.Float);
                Commands.SetPropertyValue(drag.WidgetId, isEditing: true, drag.TargetEntity, slot, PropertyValue.FromFloat(radiusScale));
                return true;
            }

            if (drag.Kind == GradientGizmoDragKind.AngularAngle)
            {
                float angle = MathF.Atan2(offsetLocal.Y, offsetLocal.X);
                PropertySlot slot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientAngle", drag.LayerIndex, PropertyKind.Float);
                Commands.SetPropertyValue(drag.WidgetId, isEditing: true, drag.TargetEntity, slot, PropertyValue.FromFloat(angle));
                return true;
            }

            return true;
        }

        if (!input.MousePressed)
        {
            return false;
        }

        if (!TryGetActivePrefabId(out int prefabId))
        {
            return false;
        }

        if (!TryGetActivePaintGradientGizmoInfo(prefabId, canvasOrigin, out PaintGradientGizmoInfo info))
        {
            return false;
        }

        const float hitRadius = 10f;
        float hitRadiusSq = hitRadius * hitRadius;

        Vector2 shapeCenterCanvas = info.ShapeCenterCanvas;
        Vector2 sizeCanvas = info.ShapeSizeCanvas;
        float rotationRadians = info.RotationRadians;
        float baseDist = Math.Max(sizeCanvas.X, sizeCanvas.Y);
        baseDist = Math.Max(1f, baseDist);

        GradientGizmoDragKind beginKind = GradientGizmoDragKind.None;

        if (info.GradientKind == 0)
        {
            Vector2 dir = info.GradientDirection;
            float lenSq = dir.LengthSquared();
            if (lenSq < 0.0001f)
            {
                dir = new Vector2(1f, 0f);
            }
            else
            {
                dir /= MathF.Sqrt(lenSq);
            }

            float handleDist = Math.Max(20f, baseDist);
            Vector2 handleLocal = dir * handleDist;
            Vector2 handleCanvas = shapeCenterCanvas + RotateVector(handleLocal, rotationRadians);
            if (Vector2.DistanceSquared(mouseCanvas, handleCanvas) <= hitRadiusSq)
            {
                beginKind = GradientGizmoDragKind.LinearDirection;
            }
        }
        else
        {
            Vector2 centerNorm = info.GradientCenterNorm;
            Vector2 centerLocalPx = new Vector2(centerNorm.X * sizeCanvas.X, centerNorm.Y * sizeCanvas.Y);
            Vector2 centerCanvas = shapeCenterCanvas + RotateVector(centerLocalPx, rotationRadians);

            if (Vector2.DistanceSquared(mouseCanvas, centerCanvas) <= hitRadiusSq)
            {
                beginKind = GradientGizmoDragKind.Center;
            }
            else if (info.GradientKind == 1)
            {
                float radiusPx = baseDist * Math.Max(0.0001f, info.GradientRadiusScale);
                Vector2 radiusHandleCanvas = centerCanvas + RotateVector(new Vector2(radiusPx, 0f), rotationRadians);
                if (Vector2.DistanceSquared(mouseCanvas, radiusHandleCanvas) <= hitRadiusSq)
                {
                    beginKind = GradientGizmoDragKind.RadialRadius;
                }
            }
            else
            {
                float handleDistAngle = Math.Max(20f, baseDist * 0.75f);
                float a = info.GradientAngleOffset;
                Vector2 angleDirLocal = new Vector2(MathF.Cos(a), MathF.Sin(a));
                Vector2 angleHandleCanvas = centerCanvas + RotateVector(angleDirLocal * handleDistAngle, rotationRadians);
                if (Vector2.DistanceSquared(mouseCanvas, angleHandleCanvas) <= hitRadiusSq)
                {
                    beginKind = GradientGizmoDragKind.AngularAngle;
                }
            }
        }

        if (beginKind == GradientGizmoDragKind.None)
        {
            return false;
        }

        PropertyInspector.SuppressColorPopoverCloseThisFrame();

        Im.Context.PushId(unchecked((int)info.TargetEntity.Value));
        Im.Context.PushId(info.LayerIndex);
        int widgetId = beginKind switch
        {
            GradientGizmoDragKind.Center => Im.Context.GetId("gradient_center"),
            GradientGizmoDragKind.LinearDirection => Im.Context.GetId("gradient_direction"),
            GradientGizmoDragKind.RadialRadius => Im.Context.GetId("gradient_radius"),
            GradientGizmoDragKind.AngularAngle => Im.Context.GetId("gradient_angle"),
            _ => Im.Context.GetId("gradient")
        };
        Im.Context.PopId();
        Im.Context.PopId();

        _gradientGizmoDrag = new GradientGizmoDragState
        {
            Active = 1,
            Kind = beginKind,
            TargetEntity = info.TargetEntity,
            PaintHandle = info.PaintHandle,
            LayerIndex = info.LayerIndex,
            WidgetId = widgetId,
            ShapeCenterCanvas = info.ShapeCenterCanvas,
            ShapeSizeCanvas = info.ShapeSizeCanvas,
            RotationRadians = info.RotationRadians
        };

        Commands.SetAnimationTargetOverride(info.TargetEntity);
        Commands.NotifyPropertyWidgetState(widgetId, isEditing: true);
        return true;
    }

	    private void DrawUngroupedShapes(int frameId, Vector2 canvasOrigin)
{
    // Legacy immediate-mode render path removed.
}

	    private void DrawBooleanGroups(int frameId, Vector2 canvasOrigin)
{
    // Legacy immediate-mode render path removed.
}

    private void DrawBooleanGroupNode(int groupIndex, Vector2 canvasOrigin, bool renderResult)
{
    // Legacy immediate-mode render path removed.
}

    private void EmitBooleanOperand(Shape shape, Vector2 canvasOrigin)
{
    // Legacy immediate-mode render path removed.
}

    private void DrawShapePrimitive(Shape shape, Vector2 canvasOrigin, bool drawLabel)
{
    // Legacy immediate-mode render path removed.
}

    internal void DrawFramePreview(Vector2 canvasOrigin, ImStyle style)
    {
        _canvasInteractions.DrawFramePreview(canvasOrigin, style);
    }

    internal void DrawShapePreview(Vector2 canvasOrigin, ImStyle style)
    {
        _canvasInteractions.DrawShapePreview(canvasOrigin, style);
    }

    internal void DrawTextPreview(Vector2 canvasOrigin, ImStyle style)
    {
        _canvasInteractions.DrawTextPreview(canvasOrigin, style);
    }

    internal void DrawCanvasHud(Vector2 canvasOrigin)
    {
        _canvasInteractions.DrawCanvasHud(canvasOrigin);
    }

    internal bool TryGetGroupCreateCandidate(out EntityId parentEntity)
    {
        parentEntity = EntityId.Null;

        if (_selectedEntities.Count < 1)
        {
            return false;
        }

        EntityId firstEntity = _selectedEntities[0];
        parentEntity = _world.GetParent(firstEntity);
        if (parentEntity.IsNull)
        {
            parentEntity = EntityId.Null;
            return false;
        }

        for (int i = 1; i < _selectedEntities.Count; i++)
        {
            if (_world.GetParent(_selectedEntities[i]).Value != parentEntity.Value)
            {
                parentEntity = EntityId.Null;
                return false;
            }
        }

        return true;
    }

    internal bool TryGetBooleanCreateCandidate(out int parentFrameId, out int parentGroupId, out int parentShapeId)
    {
        parentFrameId = 0;
        parentGroupId = 0;
        parentShapeId = 0;

        if (_selectedEntities.Count < 1)
        {
            return false;
        }

        EntityId firstEntity = _selectedEntities[0];
        EntityId parentEntity = _world.GetParent(firstEntity);
        if (parentEntity.IsNull)
        {
            return false;
        }

        for (int i = 1; i < _selectedEntities.Count; i++)
        {
            if (_world.GetParent(_selectedEntities[i]).Value != parentEntity.Value)
            {
                return false;
            }
        }

        EntityId owningPrefab = FindOwningPrefabEntity(firstEntity);
        if (owningPrefab.IsNull || !TryGetLegacyPrefabId(owningPrefab, out parentFrameId) || parentFrameId <= 0)
        {
            parentFrameId = 0;
            return false;
        }

        UiNodeType parentType = _world.GetNodeType(parentEntity);
        if (parentType == UiNodeType.BooleanGroup)
        {
            if (!TryGetLegacyGroupId(parentEntity, out parentGroupId))
            {
                parentGroupId = 0;
            }
        }
        else if (parentType == UiNodeType.Shape)
        {
            if (!TryGetLegacyShapeId(parentEntity, out parentShapeId))
            {
                parentShapeId = 0;
            }
        }

        return true;
    }

    internal bool TryGetMaskCreateCandidate(out int parentFrameId, out int parentGroupId, out int parentShapeId)
    {
        return TryGetBooleanCreateCandidate(out parentFrameId, out parentGroupId, out parentShapeId);
    }

    internal void CreateBooleanGroup(int parentFrameId, int parentGroupId, int parentShapeId, int opIndex)
    {
        Commands.CreateBooleanGroupFromSelection(opIndex);
    }

    internal void EnsureGroupTransformInitialized(ref BooleanGroup group)
    {
        if (!group.Transform.IsNull)
        {
            if (group.Blend.IsNull)
            {
                var blendView = BlendComponent.Api.Create(_propertyWorld, new BlendComponent
                {
                    IsVisible = true,
                    Opacity = 1f,
                    BlendMode = (int)PaintBlendMode.Normal
                });
                group.Blend = blendView.Handle;
            }
            return;
        }

        if (!TryGetEntityById(_groupEntityById, group.Id, out EntityId groupEntity) || groupEntity.IsNull)
        {
            return;
        }

        WorldTransform parentWorldTransform = IdentityWorldTransform;
        if (!TryGetTransformParentWorldTransformEcs(groupEntity, out parentWorldTransform))
        {
            parentWorldTransform = IdentityWorldTransform;
        }

        Vector2 pivotLocal = Vector2.Zero;
        if (TryGetGroupSubtreeBoundsLocalEcs(groupEntity, parentWorldTransform, out ImRect boundsLocal))
        {
            pivotLocal = new Vector2(boundsLocal.X + boundsLocal.Width * 0.5f, boundsLocal.Y + boundsLocal.Height * 0.5f);
        }

        var transformView = TransformComponent.Api.Create(_propertyWorld, new TransformComponent
        {
            Position = pivotLocal,
            Scale = new Vector2(1f, 1f),
            Rotation = 0f,
            Anchor = new Vector2(0.5f, 0.5f),
            Depth = 0f
        });

        group.Transform = transformView.Handle;
        SetComponentWithStableId(groupEntity, TransformComponentProperties.ToAnyHandle(group.Transform));

        ReadOnlySpan<EntityId> children = _world.GetChildren(groupEntity);
        for (int i = 0; i < children.Length; i++)
        {
            EntityId child = children[i];
            if (!_world.TryGetComponent(child, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny))
            {
                continue;
            }

            var childTransformHandle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
            var childTransform = TransformComponent.Api.FromHandle(_propertyWorld, childTransformHandle);
            if (!childTransform.IsAlive)
            {
                continue;
            }

            Vector2 pos = childTransform.Position;
            childTransform.Position = new Vector2(pos.X - pivotLocal.X, pos.Y - pivotLocal.Y);
        }

        if (group.Blend.IsNull)
        {
            var blendView = BlendComponent.Api.Create(_propertyWorld, new BlendComponent
            {
                IsVisible = true,
                Opacity = 1f,
                BlendMode = (int)PaintBlendMode.Normal
                });
            group.Blend = blendView.Handle;
        }

        if (!group.Blend.IsNull)
        {
            SetComponentWithStableId(groupEntity, BlendComponentProperties.ToAnyHandle(group.Blend));
        }
    }

    private static SdfBooleanOp GetSdfBooleanOp(int opIndex, float smoothness)
    {
        bool useSmooth = smoothness > 0.001f;
        return opIndex switch
        {
            0 => useSmooth ? SdfBooleanOp.SmoothUnion : SdfBooleanOp.Union,
            1 => useSmooth ? SdfBooleanOp.SmoothSubtract : SdfBooleanOp.Subtract,
            2 => useSmooth ? SdfBooleanOp.SmoothIntersect : SdfBooleanOp.Intersect,
            3 => SdfBooleanOp.Exclude,
            _ => SdfBooleanOp.Union
        };
    }

    internal void ClearSelection()
    {
        NotifyInspectorSelectionInteraction();
        _selectedEntities.Clear();
    }

    internal Vector2 CanvasToWorld(Vector2 canvasPos, Vector2 canvasOrigin)
    {
        return CanvasToWorld(canvasPos, canvasOrigin, Zoom);
    }

    internal Vector2 CanvasToWorld(Vector2 canvasPos, Vector2 canvasOrigin, float zoom)
    {
        if (zoom <= 0.0001f)
        {
            return PanWorld;
        }

        return (canvasPos - canvasOrigin) / zoom + PanWorld;
    }

    private float WorldToCanvasX(float worldX, Vector2 canvasOrigin)
    {
        return (worldX - PanWorld.X) * Zoom + canvasOrigin.X;
    }

    private float WorldToCanvasY(float worldY, Vector2 canvasOrigin)
    {
        return (worldY - PanWorld.Y) * Zoom + canvasOrigin.Y;
    }

    private ImRect WorldRectToCanvas(ImRect worldRect, Vector2 canvasOrigin)
    {
        float x = WorldToCanvasX(worldRect.X, canvasOrigin);
        float y = WorldToCanvasY(worldRect.Y, canvasOrigin);
        float w = worldRect.Width * Zoom;
        float h = worldRect.Height * Zoom;
        return new ImRect(x, y, w, h);
    }

    internal static ImRect MakeRectFromTwoPoints(Vector2 a, Vector2 b)
    {
        float x = Math.Min(a.X, b.X);
        float y = Math.Min(a.Y, b.Y);
        float w = Math.Abs(b.X - a.X);
        float h = Math.Abs(b.Y - a.Y);
        return new ImRect(x, y, w, h);
    }
}
