using System;
using System.Numerics;
using Core;
using DerpLib.Text;
using DerpLib.Sdf;
using Property.Runtime;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using static Derp.UI.UiColor32;
using static Derp.UI.UiFillGradient;

namespace Derp.UI;

public sealed partial class UiWorkspace
{
    private const int PaintFillGradientKindLinear = 0;
    private const int PaintFillGradientKindRadial = 1;
    private const int PaintFillGradientKindAngular = 2;

    private static SdfCommand WithPaintFillGradient(
        SdfCommand cmd,
        uint endArgb,
        int gradientKind,
        Vector2 direction,
        Vector2 center,
        float radiusScale,
        float angleOffsetRadians,
        float polygonRotationRadians,
        bool polygonManualRotation)
    {
        Vector4 endColor = ImStyle.ToVector4(endArgb);
        gradientKind = Math.Clamp(gradientKind, PaintFillGradientKindLinear, PaintFillGradientKindAngular);

        if (gradientKind == PaintFillGradientKindRadial)
        {
            if (radiusScale <= 0f)
            {
                radiusScale = 1f;
            }

            center = new Vector2(center.X * cmd.Size.X, center.Y * cmd.Size.Y);
            if (polygonManualRotation)
            {
                center = RotateVector(center, polygonRotationRadians);
            }

            return cmd.WithRadialGradient(endColor, center.X, center.Y, radiusScale);
        }

        if (gradientKind == PaintFillGradientKindAngular)
        {
            center = new Vector2(center.X * cmd.Size.X, center.Y * cmd.Size.Y);
            if (polygonManualRotation)
            {
                center = RotateVector(center, polygonRotationRadians);
                angleOffsetRadians -= polygonRotationRadians;
            }

            return cmd.WithAngularGradient(endColor, center.X, center.Y, angleOffsetRadians);
        }

        float angle = ComputeGradientAngleRadians(direction);
        if (polygonManualRotation)
        {
            angle += polygonRotationRadians;
        }

        return cmd.WithLinearGradient(endColor, angle);
    }

    private void BuildPrefabsAndContentsEcs(CanvasSdfDrawList draw, Vector2 canvasOrigin)
    {
        var style = Im.Style;

        uint frameBorder = ImStyle.WithAlphaF(style.TextPrimary, 0.65f);
        uint frameFill = ImStyle.WithAlphaF(style.Surface, 0.10f);
        uint frameSelectedBorder = ImStyle.WithAlphaF(style.Primary, 0.95f);

        ReadOnlySpan<EntityId> rootChildren = _world.GetChildren(EntityId.Null);
        for (int i = 0; i < rootChildren.Length; i++)
        {
            EntityId prefabEntity = rootChildren[i];
            if (_world.GetNodeType(prefabEntity) != UiNodeType.Prefab)
            {
                continue;
            }

            uint prefabStableId = _world.GetStableId(prefabEntity);
            if (GetLayerHidden(prefabStableId))
            {
                continue;
            }

            if (!TryGetPrefabRectWorldEcs(prefabEntity, out ImRect rectWorld))
            {
                continue;
            }

            ImRect rectCanvas = WorldRectToCanvas(rectWorld, canvasOrigin);

            bool isSelected = _selectedPrefabEntity.Value == prefabEntity.Value && _selectedEntities.Count == 0;
            uint stroke = isSelected ? frameSelectedBorder : frameBorder;
            uint fill = frameFill;

            draw.DrawRect(rectCanvas.X, rectCanvas.Y, rectCanvas.Width, rectCanvas.Height, fill);
            draw.DrawRoundedRectStroke(rectCanvas.X, rectCanvas.Y, rectCanvas.Width, rectCanvas.Height, 0f, stroke, 1.5f);

            bool clipChildren = IsClipRectEnabledEcs(prefabEntity, defaultValue: true);
            if (clipChildren)
            {
                draw.PushClipRect(rectCanvas.X, rectCanvas.Y, rectCanvas.Width, rectCanvas.Height);
            }

            int pushedWarps = TryPushEntityWarpStackEcs(draw, prefabEntity);
            BuildPrefabChildrenEcs(draw, prefabEntity, canvasOrigin);
            for (int popIndex = 0; popIndex < pushedWarps; popIndex++)
            {
                draw.PopWarp();
            }

            if (clipChildren)
            {
                draw.PopClipRect();
            }
        }
    }

    private bool IsClipRectEnabledEcs(EntityId entity, bool defaultValue)
    {
        if (_world.TryGetComponent(entity, ClipRectComponent.Api.PoolIdConst, out AnyComponentHandle clipAny) && clipAny.IsValid)
        {
            var clipHandle = new ClipRectComponentHandle(clipAny.Index, clipAny.Generation);
            var clip = ClipRectComponent.Api.FromHandle(_propertyWorld, clipHandle);
            if (clip.IsAlive)
            {
                return clip.Enabled;
            }
        }

        return defaultValue;
    }

    private bool TryGetPrefabRectWorldEcs(EntityId prefabEntity, out ImRect rectWorld)
    {
        rectWorld = default;

        if (!_world.TryGetComponent(prefabEntity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny) || !transformAny.IsValid)
        {
            return false;
        }

        Vector2 positionWorld = Vector2.Zero;
        if (TryGetComputedTransform(prefabEntity, out ComputedTransformComponentHandle computedTransformHandle))
        {
            var computedTransform = ComputedTransformComponent.Api.FromHandle(_propertyWorld, computedTransformHandle);
            if (computedTransform.IsAlive)
            {
                positionWorld = computedTransform.Position;
            }
        }
        else
        {
            var transformHandle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
            var transform = TransformComponent.Api.FromHandle(_propertyWorld, transformHandle);
            if (!transform.IsAlive)
            {
                return false;
            }

            positionWorld = transform.Position;
        }

        if (!TryGetComputedSize(prefabEntity, out Vector2 sizeLocal))
        {
            return false;
        }

        rectWorld = new ImRect(positionWorld.X, positionWorld.Y, sizeLocal.X, sizeLocal.Y);
        return rectWorld.Width > 0f && rectWorld.Height > 0f;
    }

    private void BuildPrefabChildrenEcs(CanvasSdfDrawList draw, EntityId prefabEntity, Vector2 canvasOrigin)
    {
        WorldTransform parentWorldTransform = IdentityWorldTransform;
        if (!TryGetEntityWorldTransformEcs(prefabEntity, out parentWorldTransform))
        {
            parentWorldTransform = IdentityWorldTransform;
        }

        ReadOnlySpan<EntityId> children = _world.GetChildren(prefabEntity);
        for (int childIndex = 0; childIndex < children.Length; childIndex++)
        {
            BuildEntityRecursiveEcs(draw, children[childIndex], canvasOrigin, parentWorldTransform);
        }
    }

    private void BuildEntityRecursiveEcs(CanvasSdfDrawList draw, EntityId entity, Vector2 canvasOrigin, in WorldTransform parentWorldTransform)
    {
        uint stableId = _world.GetStableId(entity);
        if (GetLayerHidden(stableId))
        {
            return;
        }

        bool pushedClipRect = TryPushEntityClipRectEcs(draw, entity, canvasOrigin, parentWorldTransform);
        int pushedWarps = TryPushEntityWarpStackEcs(draw, entity);

        UiNodeType nodeType = _world.GetNodeType(entity);
        if (nodeType == UiNodeType.BooleanGroup)
        {
            if (_world.TryGetComponent(entity, MaskGroupComponent.Api.PoolIdConst, out _))
            {
                BuildMaskGroupEntity(draw, entity, canvasOrigin, parentWorldTransform);
            }
            else if (_world.TryGetComponent(entity, BooleanGroupComponent.Api.PoolIdConst, out _))
            {
                BuildBooleanGroupEntityLayered(draw, entity, canvasOrigin, parentWorldTransform);
            }
            else
            {
                if (!TryGetGroupWorldTransformEcs(entity, parentWorldTransform, out WorldTransform groupWorldTransform, out _, out _))
                {
                    goto Cleanup;
                }

                if (!groupWorldTransform.IsVisible)
                {
                    goto Cleanup;
                }

                ReadOnlySpan<EntityId> groupChildren = _world.GetChildren(entity);
                for (int i = 0; i < groupChildren.Length; i++)
                {
                    BuildEntityRecursiveEcs(draw, groupChildren[i], canvasOrigin, groupWorldTransform);
                }
            }
            goto Cleanup;
        }

        if (nodeType == UiNodeType.Text)
        {
            BuildTextPrimitiveEcs(draw, entity, canvasOrigin, parentWorldTransform);

            if (!TryGetTextWorldTransformEcs(entity, parentWorldTransform, out ShapeWorldTransform textWorldTransform))
            {
                goto Cleanup;
            }

            var textParentWorldTransform = new WorldTransform(
                textWorldTransform.PositionWorld,
                textWorldTransform.ScaleWorld,
                textWorldTransform.RotationRadians,
                textWorldTransform.IsVisible);

            ReadOnlySpan<EntityId> textChildren = _world.GetChildren(entity);
            for (int childIndex = 0; childIndex < textChildren.Length; childIndex++)
            {
                BuildEntityRecursiveEcs(draw, textChildren[childIndex], canvasOrigin, textParentWorldTransform);
            }
            goto Cleanup;
        }

        if (nodeType == UiNodeType.PrefabInstance)
        {
            if (!TryGetGroupWorldTransformEcs(entity, parentWorldTransform, out WorldTransform instanceWorldTransform, out _, out _))
            {
                goto Cleanup;
            }

            if (!instanceWorldTransform.IsVisible)
            {
                goto Cleanup;
            }

            ReadOnlySpan<EntityId> instanceChildren = _world.GetChildren(entity);
            for (int childIndex = 0; childIndex < instanceChildren.Length; childIndex++)
            {
                BuildEntityRecursiveEcs(draw, instanceChildren[childIndex], canvasOrigin, instanceWorldTransform);
            }
            goto Cleanup;
        }

        if (nodeType != UiNodeType.Shape)
        {
            goto Cleanup;
        }

        BuildShapePrimitiveEcs(draw, entity, canvasOrigin, parentWorldTransform);

        if (!TryGetShapeWorldTransformEcs(entity, parentWorldTransform, out ShapeWorldTransform shapeWorldTransform))
        {
            goto Cleanup;
        }

        var shapeParentWorldTransform = new WorldTransform(
            shapeWorldTransform.PositionWorld,
            shapeWorldTransform.ScaleWorld,
            shapeWorldTransform.RotationRadians,
            shapeWorldTransform.IsVisible);

        ReadOnlySpan<EntityId> children = _world.GetChildren(entity);
        for (int childIndex = 0; childIndex < children.Length; childIndex++)
        {
            EntityId child = children[childIndex];
            BuildEntityRecursiveEcs(draw, child, canvasOrigin, shapeParentWorldTransform);
        }

    Cleanup:
        for (int popIndex = 0; popIndex < pushedWarps; popIndex++)
        {
            draw.PopWarp();
        }

        if (pushedClipRect)
        {
            draw.PopClipRect();
        }
    }

    private int TryPushEntityWarpStackEcs(CanvasSdfDrawList draw, EntityId entity)
    {
        if (!_world.TryGetComponent(entity, ModifierStackComponent.Api.PoolIdConst, out AnyComponentHandle modifiersAny) || !modifiersAny.IsValid)
        {
            return 0;
        }

        var modifiersHandle = new ModifierStackComponentHandle(modifiersAny.Index, modifiersAny.Generation);
        var modifiers = ModifierStackComponent.Api.FromHandle(_propertyWorld, modifiersHandle);
        if (!modifiers.IsAlive)
        {
            return 0;
        }

        ushort count = modifiers.Count;
        if (count == 0)
        {
            return 0;
        }

        if (count > ModifierStackComponent.MaxWarps)
        {
            count = ModifierStackComponent.MaxWarps;
        }

        ReadOnlySpan<byte> enabled = modifiers.EnabledValueReadOnlySpan();
        ReadOnlySpan<byte> typeValue = modifiers.TypeValueReadOnlySpan();
        ReadOnlySpan<float> param1 = modifiers.Param1ValueReadOnlySpan();
        ReadOnlySpan<float> param2 = modifiers.Param2ValueReadOnlySpan();
        ReadOnlySpan<float> param3 = modifiers.Param3ValueReadOnlySpan();

        int pushed = 0;
        for (int i = count - 1; i >= 0; i--)
        {
            if (enabled[i] == 0)
            {
                continue;
            }

            byte typeByte = typeValue[i];
            if (typeByte == 0)
            {
                continue;
            }

            if (typeByte > (byte)SdfWarpType.Repeat)
            {
                typeByte = 0;
            }

            if (typeByte == 0)
            {
                continue;
            }

            draw.PushWarp(new SdfWarp((SdfWarpType)typeByte, param1[i], param2[i], param3[i]));
            pushed++;
        }

        return pushed;
    }

    private bool TryPushEntityClipRectEcs(CanvasSdfDrawList draw, EntityId entity, Vector2 canvasOrigin, in WorldTransform parentWorldTransform)
    {
        if (!_world.TryGetComponent(entity, ClipRectComponent.Api.PoolIdConst, out AnyComponentHandle clipAny) || !clipAny.IsValid)
        {
            return false;
        }

        var clipHandle = new ClipRectComponentHandle(clipAny.Index, clipAny.Generation);
        var clip = ClipRectComponent.Api.FromHandle(_propertyWorld, clipHandle);
        if (!clip.IsAlive || !clip.Enabled)
        {
            return false;
        }

        if (!TryGetEntityClipRectCanvasEcs(entity, canvasOrigin, parentWorldTransform, out ImRect clipRectCanvas))
        {
            return false;
        }

        draw.PushClipRect(clipRectCanvas.X, clipRectCanvas.Y, clipRectCanvas.Width, clipRectCanvas.Height);
        return true;
    }

    private bool TryGetEntityClipRectCanvasEcs(EntityId entity, Vector2 canvasOrigin, in WorldTransform parentWorldTransform, out ImRect rectCanvas)
    {
        rectCanvas = default;

        UiNodeType type = _world.GetNodeType(entity);

        if (type == UiNodeType.Prefab)
        {
            if (!TryGetPrefabRectWorldEcs(entity, out ImRect prefabRectWorld))
            {
                return false;
            }

            rectCanvas = WorldRectToCanvas(prefabRectWorld, canvasOrigin);
            return rectCanvas.Width > 0.0001f && rectCanvas.Height > 0.0001f;
        }

        if (type == UiNodeType.Shape)
        {
            if (!TryGetShapeWorldTransformEcs(entity, parentWorldTransform, out ShapeWorldTransform shapeWorldTransform))
            {
                return false;
            }

            Vector2 sizeLocal = Vector2.Zero;
            if (TryGetComputedSize(entity, out Vector2 computedSize) && computedSize.X > 0f && computedSize.Y > 0f)
            {
                sizeLocal = computedSize;
            }
            else
            {
                ShapeKind kind = GetShapeKindEcs(entity, out RectGeometryComponentHandle rectGeometryHandle, out CircleGeometryComponentHandle circleGeometryHandle, out _);
                if (kind == ShapeKind.Rect)
                {
                    var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, rectGeometryHandle);
                    if (!rectGeometry.IsAlive || rectGeometry.Size.X <= 0f || rectGeometry.Size.Y <= 0f)
                    {
                        return false;
                    }

                    sizeLocal = rectGeometry.Size;
                }
                else if (kind == ShapeKind.Circle)
                {
                    var circleGeometry = CircleGeometryComponent.Api.FromHandle(_propertyWorld, circleGeometryHandle);
                    if (!circleGeometry.IsAlive || circleGeometry.Radius <= 0f)
                    {
                        return false;
                    }

                    float d = circleGeometry.Radius * 2f;
                    sizeLocal = new Vector2(d, d);
                }
                else
                {
                    return false;
                }
            }

            var shapeWorld = new WorldTransform(
                shapeWorldTransform.PositionWorld,
                shapeWorldTransform.ScaleWorld,
                shapeWorldTransform.RotationRadians,
                shapeWorldTransform.IsVisible);
            ImRect boundsLocal = RectTransformMath.GetRectFromPivot(Vector2.Zero, shapeWorldTransform.Anchor, sizeLocal);
            return TryComputeClipRectCanvasAabb(shapeWorld, boundsLocal, canvasOrigin, out rectCanvas);
        }

        if (type == UiNodeType.Text)
        {
            if (!TryGetTextWorldTransformEcs(entity, parentWorldTransform, out ShapeWorldTransform textWorldTransform))
            {
                return false;
            }

            Vector2 sizeLocal;
            if (!TryGetComputedSize(entity, out sizeLocal) || sizeLocal.X <= 0f || sizeLocal.Y <= 0f)
            {
                if (!_world.TryGetComponent(entity, RectGeometryComponent.Api.PoolIdConst, out AnyComponentHandle rectAny) || !rectAny.IsValid)
                {
                    return false;
                }

                var rectHandle = new RectGeometryComponentHandle(rectAny.Index, rectAny.Generation);
                var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, rectHandle);
                if (!rectGeometry.IsAlive || rectGeometry.Size.X <= 0f || rectGeometry.Size.Y <= 0f)
                {
                    return false;
                }

                sizeLocal = rectGeometry.Size;
            }

            var textWorld = new WorldTransform(
                textWorldTransform.PositionWorld,
                textWorldTransform.ScaleWorld,
                textWorldTransform.RotationRadians,
                textWorldTransform.IsVisible);
            ImRect boundsLocal = RectTransformMath.GetRectFromPivot(Vector2.Zero, textWorldTransform.Anchor, sizeLocal);
            return TryComputeClipRectCanvasAabb(textWorld, boundsLocal, canvasOrigin, out rectCanvas);
        }

        if (type == UiNodeType.BooleanGroup || type == UiNodeType.PrefabInstance)
        {
            if (!TryGetComputedSize(entity, out Vector2 sizeLocal) || sizeLocal.X <= 0f || sizeLocal.Y <= 0f)
            {
                return false;
            }

            Vector2 anchor01 = Vector2.Zero;
            if (_world.TryGetComponent(entity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny) && transformAny.IsValid)
            {
                var transformHandle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
                var transform = TransformComponent.Api.FromHandle(_propertyWorld, transformHandle);
                if (transform.IsAlive)
                {
                    anchor01 = transform.Anchor;
                }
            }

            if (!TryGetGroupWorldTransformEcs(entity, parentWorldTransform, out WorldTransform worldTransform, out _, out _))
            {
                return false;
            }

            ImRect boundsLocal = RectTransformMath.GetRectFromPivot(Vector2.Zero, anchor01, sizeLocal);
            return TryComputeClipRectCanvasAabb(worldTransform, boundsLocal, canvasOrigin, out rectCanvas);
        }

        return false;
    }

    private bool TryComputeClipRectCanvasAabb(in WorldTransform worldTransform, ImRect boundsLocal, Vector2 canvasOrigin, out ImRect rectCanvas)
    {
        rectCanvas = default;

        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;

        Vector2 p0World = TransformPoint(worldTransform, new Vector2(boundsLocal.X, boundsLocal.Y));
        Vector2 p1World = TransformPoint(worldTransform, new Vector2(boundsLocal.Right, boundsLocal.Y));
        Vector2 p2World = TransformPoint(worldTransform, new Vector2(boundsLocal.Right, boundsLocal.Bottom));
        Vector2 p3World = TransformPoint(worldTransform, new Vector2(boundsLocal.X, boundsLocal.Bottom));

        AccumulateClipRectCanvasPoint(p0World, canvasOrigin, ref minX, ref minY, ref maxX, ref maxY);
        AccumulateClipRectCanvasPoint(p1World, canvasOrigin, ref minX, ref minY, ref maxX, ref maxY);
        AccumulateClipRectCanvasPoint(p2World, canvasOrigin, ref minX, ref minY, ref maxX, ref maxY);
        AccumulateClipRectCanvasPoint(p3World, canvasOrigin, ref minX, ref minY, ref maxX, ref maxY);

        if (!float.IsFinite(minX) || !float.IsFinite(minY) || !float.IsFinite(maxX) || !float.IsFinite(maxY))
        {
            return false;
        }

        float width = maxX - minX;
        float height = maxY - minY;
        if (width <= 0.0001f || height <= 0.0001f)
        {
            return false;
        }

        rectCanvas = new ImRect(minX, minY, width, height);
        return true;
    }

    private void AccumulateClipRectCanvasPoint(Vector2 pointWorld, Vector2 canvasOrigin, ref float minX, ref float minY, ref float maxX, ref float maxY)
    {
        float x = WorldToCanvasX(pointWorld.X, canvasOrigin);
        float y = WorldToCanvasY(pointWorld.Y, canvasOrigin);

        if (x < minX) minX = x;
        if (y < minY) minY = y;
        if (x > maxX) maxX = x;
        if (y > maxY) maxY = y;
    }

    private void BuildMaskGroupEntity(CanvasSdfDrawList draw, EntityId groupEntity, Vector2 canvasOrigin, in WorldTransform parentWorldTransform)
    {
        if (!TryGetGroupWorldTransformEcs(groupEntity, parentWorldTransform, out WorldTransform groupWorldTransform, out _, out _))
        {
            return;
        }

        if (!groupWorldTransform.IsVisible)
        {
            return;
        }

        float softEdgePx = 2f;
        bool invert = false;
        if (_world.TryGetComponent(groupEntity, MaskGroupComponent.Api.PoolIdConst, out AnyComponentHandle maskAny))
        {
            var maskHandle = new MaskGroupComponentHandle(maskAny.Index, maskAny.Generation);
            var mask = MaskGroupComponent.Api.FromHandle(_propertyWorld, maskHandle);
            if (mask.IsAlive)
            {
                softEdgePx = Math.Clamp(mask.SoftEdgePx, 0f, 64f);
                invert = mask.Invert;
            }
        }

        ReadOnlySpan<EntityId> children = _world.GetChildren(groupEntity);
        if (children.IsEmpty)
        {
            return;
        }

        EntityId maskSource = children[0];
        UiNodeType maskSourceType = _world.GetNodeType(maskSource);
        if (maskSourceType != UiNodeType.Shape && maskSourceType != UiNodeType.Text)
        {
            for (int i = 0; i < children.Length; i++)
            {
                BuildEntityRecursiveEcs(draw, children[i], canvasOrigin, groupWorldTransform);
            }
            return;
        }

        if (maskSourceType == UiNodeType.Shape)
        {
            int pushedMaskSourceWarps = TryPushEntityWarpStackEcs(draw, maskSource);
            bool pushedMask = TryPushMaskFromSourcePaintEcs(draw, maskSource, canvasOrigin, groupWorldTransform, softEdgePx, invert);
            for (int popIndex = 0; popIndex < pushedMaskSourceWarps; popIndex++)
            {
                draw.PopWarp();
            }

            if (pushedMask)
            {
                for (int i = 1; i < children.Length; i++)
                {
                    BuildEntityRecursiveEcs(draw, children[i], canvasOrigin, groupWorldTransform);
                }
                draw.PopMask();
                return;
            }
        }

        if (maskSourceType == UiNodeType.Text)
        {
            int pushedMaskSourceWarps = TryPushEntityWarpStackEcs(draw, maskSource);
            bool pushedMask = TryPushMaskFromTextPaintEcs(draw, maskSource, canvasOrigin, groupWorldTransform, softEdgePx, invert);
            for (int popIndex = 0; popIndex < pushedMaskSourceWarps; popIndex++)
            {
                draw.PopWarp();
            }

            if (pushedMask)
            {
                for (int i = 1; i < children.Length; i++)
                {
                    BuildEntityRecursiveEcs(draw, children[i], canvasOrigin, groupWorldTransform);
                }
                draw.PopMask();
                return;
            }
        }

        uint maskCommandIndex = 0u;
        bool hasMaskCommand = false;
        int pushedMaskSourceWarpsForGeometry = TryPushEntityWarpStackEcs(draw, maskSource);
        if (maskSourceType == UiNodeType.Shape)
        {
            int shapeMaskCommandIndex = draw.CommandCount;
            if (TryEmitShapeMaskGeometryCommandEcs(draw, maskSource, canvasOrigin, groupWorldTransform))
            {
                maskCommandIndex = (uint)shapeMaskCommandIndex;
                hasMaskCommand = true;
            }
        }
        else
        {
            if (TryEmitTextMaskGeometryCommandEcs(draw, maskSource, canvasOrigin, groupWorldTransform, out int textMaskCommandIndex))
            {
                maskCommandIndex = (uint)textMaskCommandIndex;
                hasMaskCommand = true;
            }
        }
        for (int popIndex = 0; popIndex < pushedMaskSourceWarpsForGeometry; popIndex++)
        {
            draw.PopWarp();
        }

        if (!hasMaskCommand)
        {
            for (int i = 0; i < children.Length; i++)
            {
                BuildEntityRecursiveEcs(draw, children[i], canvasOrigin, groupWorldTransform);
            }
            return;
        }

        draw.PushMask(SdfMaskShape.CommandRef(maskCommandIndex, softEdgePx, invert));
        for (int i = 1; i < children.Length; i++)
        {
            BuildEntityRecursiveEcs(draw, children[i], canvasOrigin, groupWorldTransform);
        }
        draw.PopMask();
    }

    private bool TryPushMaskFromSourcePaintEcs(CanvasSdfDrawList draw, EntityId shapeEntity, Vector2 canvasOrigin, in WorldTransform parentWorldTransform, float softEdgePx, bool invert)
    {
        if (!TryGetShapeWorldTransformEcs(shapeEntity, parentWorldTransform, out ShapeWorldTransform worldTransform))
        {
            return false;
        }

        float zoom = Zoom;
        Vector2 scaleWorldForStyle = worldTransform.ScaleWorld;
        float styleScale = (MathF.Abs(scaleWorldForStyle.X) + MathF.Abs(scaleWorldForStyle.Y)) * 0.5f;

        if (!TryGetBlendStateEcs(shapeEntity, out bool isVisible, out float shapeOpacity, out _))
        {
            return false;
        }

        if (!isVisible || shapeOpacity <= 0.0001f)
        {
            return false;
        }

        if (!_world.TryGetComponent(shapeEntity, PaintComponent.Api.PoolIdConst, out AnyComponentHandle paintAny))
        {
            return false;
        }

        var paintHandle = new PaintComponentHandle(paintAny.Index, paintAny.Generation);
        var paintView = PaintComponent.Api.FromHandle(_propertyWorld, paintHandle);
        if (!paintView.IsAlive || paintView.LayerCount <= 0)
        {
            return false;
        }

        int layerCount = paintView.LayerCount;
        if (layerCount > PaintComponent.MaxLayers)
        {
            layerCount = PaintComponent.MaxLayers;
        }

        ReadOnlySpan<int> layerKind = PaintComponentProperties.LayerKindArray(_propertyWorld, paintHandle);
        ReadOnlySpan<bool> layerIsVisible = PaintComponentProperties.LayerIsVisibleArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> layerOpacityValues = PaintComponentProperties.LayerOpacityArray(_propertyWorld, paintHandle);
        ReadOnlySpan<int> layerMaskCombineOp = PaintComponentProperties.LayerMaskCombineOpArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> layerBlurWorld = PaintComponentProperties.LayerBlurArray(_propertyWorld, paintHandle);

        ReadOnlySpan<Color32> fillColor = PaintComponentProperties.FillColorArray(_propertyWorld, paintHandle);
        ReadOnlySpan<bool> fillUseGradient = PaintComponentProperties.FillUseGradientArray(_propertyWorld, paintHandle);
        ReadOnlySpan<Color32> fillGradientColorA = PaintComponentProperties.FillGradientColorAArray(_propertyWorld, paintHandle);
        ReadOnlySpan<Color32> fillGradientColorB = PaintComponentProperties.FillGradientColorBArray(_propertyWorld, paintHandle);
        ReadOnlySpan<int> fillGradientType = PaintComponentProperties.FillGradientTypeArray(_propertyWorld, paintHandle);
        ReadOnlySpan<Vector2> fillGradientDirection = PaintComponentProperties.FillGradientDirectionArray(_propertyWorld, paintHandle);
        ReadOnlySpan<Vector2> fillGradientCenter = PaintComponentProperties.FillGradientCenterArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> fillGradientRadius = PaintComponentProperties.FillGradientRadiusArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> fillGradientAngle = PaintComponentProperties.FillGradientAngleArray(_propertyWorld, paintHandle);

        ReadOnlySpan<float> strokeWidth = PaintComponentProperties.StrokeWidthArray(_propertyWorld, paintHandle);
        ReadOnlySpan<Color32> strokeColor = PaintComponentProperties.StrokeColorArray(_propertyWorld, paintHandle);

        ShapeKind kind = GetShapeKindEcs(shapeEntity, out RectGeometryComponentHandle rectGeometryHandle, out CircleGeometryComponentHandle circleGeometryHandle, out PathComponentHandle pathHandle);

        int polygonPointCount = 0;
        ReadOnlySpan<Vector2> polygonStrokePoints = default;
        float polygonMinCanvasX = 0f;
        float polygonMinCanvasY = 0f;
        float polygonMaxCanvasX = 0f;
        float polygonMaxCanvasY = 0f;
        int polygonHeaderIndex = -1;

        Vector2 centerCanvas = default;
        Vector2 halfSizeCanvas = default;
        float rotationDraw = 0f;
        float circleRadiusCanvas = 0f;

        bool hasCornerRadius = false;
        float radiusTL = 0f;
        float radiusTR = 0f;
        float radiusBR = 0f;
        float radiusBL = 0f;

        float rotationRadians = worldTransform.RotationRadians;

        if (kind == ShapeKind.Polygon)
        {
            if (!TryGetPolygonPointsLocalEcs(pathHandle, out ReadOnlySpan<Vector2> pointsLocal, out int pointCount))
            {
                return false;
            }

            polygonPointCount = pointCount;
            EnsurePolygonScratchCapacity(polygonPointCount + 1);

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

            for (int i = 0; i < polygonPointCount; i++)
            {
                Vector2 pWorld = TransformPolygonPointToWorld(positionWorld, pivotLocal, rotationRadians, scaleX, scaleY, pointsLocal[i]);
                float canvasX = WorldToCanvasX(pWorld.X, canvasOrigin);
                float canvasY = WorldToCanvasY(pWorld.Y, canvasOrigin);
                _polygonCanvasScratch[i] = new Vector2(canvasX, canvasY);

                if (canvasX < minCanvasX) minCanvasX = canvasX;
                if (canvasY < minCanvasY) minCanvasY = canvasY;
                if (canvasX > maxCanvasX) maxCanvasX = canvasX;
                if (canvasY > maxCanvasY) maxCanvasY = canvasY;
            }

            _polygonCanvasScratch[polygonPointCount] = _polygonCanvasScratch[0];
            polygonStrokePoints = _polygonCanvasScratch.AsSpan(0, polygonPointCount + 1);
            polygonMinCanvasX = minCanvasX;
            polygonMinCanvasY = minCanvasY;
            polygonMaxCanvasX = maxCanvasX;
            polygonMaxCanvasY = maxCanvasY;
        }
        else
        {
            ImRect rectCanvas = GetShapeRectCanvasForDrawEcs(shapeEntity, kind, rectGeometryHandle, circleGeometryHandle, canvasOrigin, worldTransform, out centerCanvas, out rotationDraw);
            halfSizeCanvas = new Vector2(rectCanvas.Width * 0.5f, rectCanvas.Height * 0.5f);
            circleRadiusCanvas = Math.Min(rectCanvas.Width, rectCanvas.Height) * 0.5f;

            if (kind == ShapeKind.Rect)
            {
                var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, rectGeometryHandle);
                if (!rectGeometry.IsAlive)
                {
                    return false;
                }

                Vector2 scaleWorld = worldTransform.ScaleWorld;
                float radiusScale = (MathF.Abs(scaleWorld.X) + MathF.Abs(scaleWorld.Y)) * 0.5f;
                radiusTL = Math.Max(0f, rectGeometry.CornerRadius.X) * radiusScale * zoom;
                radiusTR = Math.Max(0f, rectGeometry.CornerRadius.Y) * radiusScale * zoom;
                radiusBR = Math.Max(0f, rectGeometry.CornerRadius.Z) * radiusScale * zoom;
                radiusBL = Math.Max(0f, rectGeometry.CornerRadius.W) * radiusScale * zoom;
                hasCornerRadius = radiusTL > 0f || radiusTR > 0f || radiusBR > 0f || radiusBL > 0f;
            }
        }

        Span<SdfCommand> maskCommands = stackalloc SdfCommand[PaintComponent.MaxLayers];
        int maskCommandCount = 0;

        for (int layerIndex = layerCount - 1; layerIndex >= 0; layerIndex--)
        {
            if ((uint)layerIndex >= (uint)layerIsVisible.Length || !layerIsVisible[layerIndex])
            {
                continue;
            }

            if ((uint)layerIndex >= (uint)layerOpacityValues.Length)
            {
                continue;
            }

            float layerOpacity = shapeOpacity * Math.Clamp(layerOpacityValues[layerIndex], 0f, 1f);
            if (layerOpacity <= 0.0001f)
            {
                continue;
            }

            int kindValue = (uint)layerIndex < (uint)layerKind.Length ? layerKind[layerIndex] : (int)PaintLayerKind.Fill;
            if (kindValue == (int)PaintLayerKind.Fill)
            {
                bool useGradient = (uint)layerIndex < (uint)fillUseGradient.Length && fillUseGradient[layerIndex];
                int gradientKind = useGradient && (uint)layerIndex < (uint)fillGradientType.Length
                    ? fillGradientType[layerIndex]
                    : PaintFillGradientKindLinear;
                Vector2 gradientDirection = (uint)layerIndex < (uint)fillGradientDirection.Length
                    ? fillGradientDirection[layerIndex]
                    : new Vector2(1f, 0f);
                Vector2 gradientCenter = (uint)layerIndex < (uint)fillGradientCenter.Length
                    ? fillGradientCenter[layerIndex]
                    : Vector2.Zero;
                float gradientRadiusScale = (uint)layerIndex < (uint)fillGradientRadius.Length
                    ? fillGradientRadius[layerIndex]
                    : 1f;
                float gradientAngleOffset = (uint)layerIndex < (uint)fillGradientAngle.Length
                    ? fillGradientAngle[layerIndex]
                    : 0f;

                uint fillStartArgb;
                uint fillEndArgb;
                int gradientStopCount;
                int gradientStopStartIndex = -1;

                if (useGradient)
                {
                    gradientStopStartIndex = AddFillGradientStops(draw, paintHandle, layerIndex, layerOpacity, out fillStartArgb, out fillEndArgb, out gradientStopCount);
                    if (gradientStopStartIndex < 0)
                    {
                        Color32 startColor = (uint)layerIndex < (uint)fillGradientColorA.Length
                            ? ApplyTintAndOpacity(fillGradientColorA[layerIndex], tint: Color32.White, opacity: layerOpacity)
                            : default;
                        Color32 endColor = (uint)layerIndex < (uint)fillGradientColorB.Length
                            ? ApplyTintAndOpacity(fillGradientColorB[layerIndex], tint: Color32.White, opacity: layerOpacity)
                            : default;
                        fillStartArgb = startColor.A == 0 ? 0u : ToArgb(startColor);
                        fillEndArgb = endColor.A == 0 ? 0u : ToArgb(endColor);
                        gradientStopCount = 0;
                    }
                }
                else
                {
                    Color32 solidColor = (uint)layerIndex < (uint)fillColor.Length
                        ? ApplyTintAndOpacity(fillColor[layerIndex], tint: Color32.White, opacity: layerOpacity)
                        : default;
                    fillStartArgb = solidColor.A == 0 ? 0u : ToArgb(solidColor);
                    fillEndArgb = 0u;
                    gradientStopCount = 0;
                }

                bool hasFill = useGradient
                    ? (gradientStopStartIndex >= 0 || fillStartArgb != 0u || fillEndArgb != 0u)
                    : fillStartArgb != 0u;
                if (!hasFill)
                {
                    continue;
                }

                int refCommandIndex = draw.CommandCount;
                if (kind == ShapeKind.Polygon)
                {
                    if (polygonHeaderIndex < 0)
                    {
                        polygonHeaderIndex = draw.AddPolyline(polygonStrokePoints);
                    }

                    var cmd = SdfCommand.FilledPolygon(polygonHeaderIndex, ImStyle.ToVector4(fillStartArgb)).WithInternalNoRender();
                    if (useGradient)
                    {
                        cmd.Position = new Vector2((polygonMinCanvasX + polygonMaxCanvasX) * 0.5f, (polygonMinCanvasY + polygonMaxCanvasY) * 0.5f);
                        cmd.Size = new Vector2((polygonMaxCanvasX - polygonMinCanvasX) * 0.5f, (polygonMaxCanvasY - polygonMinCanvasY) * 0.5f);
                        cmd = WithPaintFillGradient(
                            cmd,
                            fillEndArgb,
                            gradientKind,
                            gradientDirection,
                            gradientCenter,
                            gradientRadiusScale,
                            gradientAngleOffset,
                            rotationRadians,
                            polygonManualRotation: true);
                        if (gradientStopStartIndex >= 0 && gradientStopCount >= 2)
                        {
                            cmd = cmd.WithGradientStops(gradientStopStartIndex, gradientStopCount);
                        }
                    }
                    draw.Add(cmd);
                }
                else if (kind == ShapeKind.Rect)
                {
                    var cmd = hasCornerRadius
                        ? SdfCommand.RoundedRectPerCorner(centerCanvas, halfSizeCanvas, radiusTL, radiusTR, radiusBR, radiusBL, ImStyle.ToVector4(fillStartArgb))
                        : SdfCommand.Rect(centerCanvas, halfSizeCanvas, ImStyle.ToVector4(fillStartArgb));

                    cmd = cmd.WithRotation(rotationDraw).WithInternalNoRender();
                    if (useGradient)
                    {
                        cmd = WithPaintFillGradient(
                            cmd,
                            fillEndArgb,
                            gradientKind,
                            gradientDirection,
                            gradientCenter,
                            gradientRadiusScale,
                            gradientAngleOffset,
                            polygonRotationRadians: 0f,
                            polygonManualRotation: false);
                        if (gradientStopStartIndex >= 0 && gradientStopCount >= 2)
                        {
                            cmd = cmd.WithGradientStops(gradientStopStartIndex, gradientStopCount);
                        }
                    }
                    draw.Add(cmd);
                }
                else
                {
                    var cmd = SdfCommand.Circle(centerCanvas, circleRadiusCanvas, ImStyle.ToVector4(fillStartArgb))
                        .WithRotation(rotationDraw)
                        .WithInternalNoRender();
                    if (useGradient)
                    {
                        cmd = WithPaintFillGradient(
                            cmd,
                            fillEndArgb,
                            gradientKind,
                            gradientDirection,
                            gradientCenter,
                            gradientRadiusScale,
                            gradientAngleOffset,
                            polygonRotationRadians: 0f,
                            polygonManualRotation: false);
                        if (gradientStopStartIndex >= 0 && gradientStopCount >= 2)
                        {
                            cmd = cmd.WithGradientStops(gradientStopStartIndex, gradientStopCount);
                        }
                    }
                    draw.Add(cmd);
                }

                int combineOpValue = (uint)layerIndex < (uint)layerMaskCombineOp.Length ? layerMaskCombineOp[layerIndex] : 0;
                var combineOp = (SdfMaskCombineOp)Math.Clamp(combineOpValue, (int)SdfMaskCombineOp.Union, (int)SdfMaskCombineOp.Multiply);

                float blurWorld = (uint)layerIndex < (uint)layerBlurWorld.Length ? layerBlurWorld[layerIndex] : 0f;
                float blurCanvas = Math.Max(0f, blurWorld) * zoom * styleScale;
                float layerSoftEdgePx = Math.Max(softEdgePx, blurCanvas);

                maskCommands[maskCommandCount++] = SdfMaskShape.CommandRefPaint((uint)refCommandIndex, layerSoftEdgePx, invert, combineOp);
                continue;
            }

            if (kindValue != (int)PaintLayerKind.Stroke)
            {
                continue;
            }

            float strokeWidthWorld = (uint)layerIndex < (uint)strokeWidth.Length ? strokeWidth[layerIndex] : 0f;
            if (strokeWidthWorld <= 0.0001f)
            {
                continue;
            }

            float strokeWidthCanvas = strokeWidthWorld * zoom * styleScale;

            bool useStrokeGradient = (uint)layerIndex < (uint)fillUseGradient.Length && fillUseGradient[layerIndex];
            int strokeGradientKind = useStrokeGradient && (uint)layerIndex < (uint)fillGradientType.Length
                ? fillGradientType[layerIndex]
                : PaintFillGradientKindLinear;
            Vector2 strokeGradientDirection = (uint)layerIndex < (uint)fillGradientDirection.Length
                ? fillGradientDirection[layerIndex]
                : new Vector2(1f, 0f);
            Vector2 strokeGradientCenter = (uint)layerIndex < (uint)fillGradientCenter.Length
                ? fillGradientCenter[layerIndex]
                : Vector2.Zero;
            float strokeGradientRadiusScale = (uint)layerIndex < (uint)fillGradientRadius.Length
                ? fillGradientRadius[layerIndex]
                : 1f;
            float strokeGradientAngleOffset = (uint)layerIndex < (uint)fillGradientAngle.Length
                ? fillGradientAngle[layerIndex]
                : 0f;

            uint strokeStartArgb;
            uint strokeEndArgb;
            int strokeGradientStopCount;
            int strokeGradientStopStartIndex = -1;

            if (useStrokeGradient)
            {
                strokeGradientStopStartIndex = AddFillGradientStops(draw, paintHandle, layerIndex, layerOpacity, out strokeStartArgb, out strokeEndArgb, out strokeGradientStopCount);
                if (strokeGradientStopStartIndex < 0)
                {
                    Color32 startColor = (uint)layerIndex < (uint)fillGradientColorA.Length
                        ? ApplyTintAndOpacity(fillGradientColorA[layerIndex], tint: Color32.White, opacity: layerOpacity)
                        : default;
                    Color32 endColor = (uint)layerIndex < (uint)fillGradientColorB.Length
                        ? ApplyTintAndOpacity(fillGradientColorB[layerIndex], tint: Color32.White, opacity: layerOpacity)
                        : default;
                    strokeStartArgb = startColor.A == 0 ? 0u : ToArgb(startColor);
                    strokeEndArgb = endColor.A == 0 ? 0u : ToArgb(endColor);
                    strokeGradientStopCount = 0;
                }
            }
            else
            {
                Color32 strokeColor32 = (uint)layerIndex < (uint)strokeColor.Length
                    ? ApplyTintAndOpacity(strokeColor[layerIndex], tint: Color32.White, opacity: layerOpacity)
                    : default;
                strokeStartArgb = strokeColor32.A == 0 ? 0u : ToArgb(strokeColor32);
                strokeEndArgb = 0u;
                strokeGradientStopCount = 0;
            }

            bool hasStroke = useStrokeGradient
                ? (strokeGradientStopStartIndex >= 0 || strokeStartArgb != 0u || strokeEndArgb != 0u)
                : strokeStartArgb != 0u;
            if (!hasStroke)
            {
                continue;
            }

            int strokeRefCommandIndex = draw.CommandCount;
            if (kind == ShapeKind.Polygon)
            {
                if (polygonHeaderIndex < 0)
                {
                    polygonHeaderIndex = draw.AddPolyline(polygonStrokePoints);
                }

                var cmd = SdfCommand.Polyline(polygonHeaderIndex, strokeWidthCanvas, ImStyle.ToVector4(strokeStartArgb)).WithInternalNoRender();
                if (useStrokeGradient)
                {
                    cmd.Position = new Vector2((polygonMinCanvasX + polygonMaxCanvasX) * 0.5f, (polygonMinCanvasY + polygonMaxCanvasY) * 0.5f);
                    cmd.Size = new Vector2((polygonMaxCanvasX - polygonMinCanvasX) * 0.5f, (polygonMaxCanvasY - polygonMinCanvasY) * 0.5f);
                    cmd = WithPaintFillGradient(
                        cmd,
                        strokeEndArgb,
                        strokeGradientKind,
                        strokeGradientDirection,
                        strokeGradientCenter,
                        strokeGradientRadiusScale,
                        strokeGradientAngleOffset,
                        rotationRadians,
                        polygonManualRotation: true);
                    if (strokeGradientStopStartIndex >= 0 && strokeGradientStopCount >= 2)
                    {
                        cmd = cmd.WithGradientStops(strokeGradientStopStartIndex, strokeGradientStopCount);
                    }
                }
                draw.Add(cmd);
            }
            else if (kind == ShapeKind.Rect)
            {
                var cmd = hasCornerRadius
                    ? SdfCommand.RoundedRectPerCorner(centerCanvas, halfSizeCanvas, radiusTL, radiusTR, radiusBR, radiusBL, Vector4.Zero)
                    : SdfCommand.Rect(centerCanvas, halfSizeCanvas, Vector4.Zero);

                cmd = cmd.WithRotation(rotationDraw)
                    .WithStroke(ImStyle.ToVector4(strokeStartArgb), strokeWidthCanvas)
                    .WithInternalNoRender();
                if (useStrokeGradient)
                {
                    cmd = WithPaintFillGradient(
                        cmd,
                        strokeEndArgb,
                        strokeGradientKind,
                        strokeGradientDirection,
                        strokeGradientCenter,
                        strokeGradientRadiusScale,
                        strokeGradientAngleOffset,
                        polygonRotationRadians: 0f,
                        polygonManualRotation: false);
                    if (strokeGradientStopStartIndex >= 0 && strokeGradientStopCount >= 2)
                    {
                        cmd = cmd.WithGradientStops(strokeGradientStopStartIndex, strokeGradientStopCount);
                    }
                }
                draw.Add(cmd);
            }
            else
            {
                var cmd = SdfCommand.Circle(centerCanvas, circleRadiusCanvas, Vector4.Zero)
                    .WithRotation(rotationDraw)
                    .WithStroke(ImStyle.ToVector4(strokeStartArgb), strokeWidthCanvas)
                    .WithInternalNoRender();
                if (useStrokeGradient)
                {
                    cmd = WithPaintFillGradient(
                        cmd,
                        strokeEndArgb,
                        strokeGradientKind,
                        strokeGradientDirection,
                        strokeGradientCenter,
                        strokeGradientRadiusScale,
                        strokeGradientAngleOffset,
                        polygonRotationRadians: 0f,
                        polygonManualRotation: false);
                    if (strokeGradientStopStartIndex >= 0 && strokeGradientStopCount >= 2)
                    {
                        cmd = cmd.WithGradientStops(strokeGradientStopStartIndex, strokeGradientStopCount);
                    }
                }
                draw.Add(cmd);
            }

            int strokeCombineOpValue = (uint)layerIndex < (uint)layerMaskCombineOp.Length ? layerMaskCombineOp[layerIndex] : 0;
            var strokeCombineOp = (SdfMaskCombineOp)Math.Clamp(strokeCombineOpValue, (int)SdfMaskCombineOp.Union, (int)SdfMaskCombineOp.Multiply);

            float strokeBlurWorld = (uint)layerIndex < (uint)layerBlurWorld.Length ? layerBlurWorld[layerIndex] : 0f;
            float strokeBlurCanvas = Math.Max(0f, strokeBlurWorld) * zoom * styleScale;
            float strokeLayerSoftEdgePx = Math.Max(softEdgePx, strokeBlurCanvas);

            maskCommands[maskCommandCount++] = SdfMaskShape.CommandRefPaint((uint)strokeRefCommandIndex, strokeLayerSoftEdgePx, invert, strokeCombineOp);
        }

        if (maskCommandCount <= 0)
        {
            return false;
        }

        maskCommands[0] = maskCommands[0].WithMaskUnionCount(maskCommandCount - 1);
        draw.PushMask(maskCommands.Slice(0, maskCommandCount));
        return true;
    }

    private bool TryPushMaskFromTextPaintEcs(CanvasSdfDrawList draw, EntityId textEntity, Vector2 canvasOrigin, in WorldTransform parentWorldTransform, float softEdgePx, bool invert)
    {
        if (!TryGetTextWorldTransformEcs(textEntity, parentWorldTransform, out ShapeWorldTransform worldTransform))
        {
            return false;
        }

        if (!worldTransform.IsVisible)
        {
            return false;
        }

        if (!TryGetBlendStateEcs(textEntity, out bool isVisible, out float opacity, out _))
        {
            return false;
        }

        if (!isVisible || opacity <= 0.0001f)
        {
            return false;
        }

        if (!_world.TryGetComponent(textEntity, RectGeometryComponent.Api.PoolIdConst, out AnyComponentHandle rectAny))
        {
            return false;
        }

        if (!_world.TryGetComponent(textEntity, PaintComponent.Api.PoolIdConst, out AnyComponentHandle paintAny))
        {
            return false;
        }

        if (!_world.TryGetComponent(textEntity, TextComponent.Api.PoolIdConst, out AnyComponentHandle textAny))
        {
            return false;
        }

        var rectHandle = new RectGeometryComponentHandle(rectAny.Index, rectAny.Generation);
        var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, rectHandle);
        if (!rectGeometry.IsAlive)
        {
            return false;
        }

        var paintHandle = new PaintComponentHandle(paintAny.Index, paintAny.Generation);
        var paintView = PaintComponent.Api.FromHandle(_propertyWorld, paintHandle);
        if (!paintView.IsAlive || paintView.LayerCount <= 0)
        {
            return false;
        }

        var textHandle = new TextComponentHandle(textAny.Index, textAny.Generation);
        var text = TextComponent.Api.FromHandle(_propertyWorld, textHandle);
        if (!text.IsAlive)
        {
            return false;
        }

        float zoom = Zoom;
        Vector2 scaleWorld = worldTransform.ScaleWorld;
        float scaleX = scaleWorld.X == 0f ? 1f : scaleWorld.X;
        float scaleY = scaleWorld.Y == 0f ? 1f : scaleWorld.Y;
        float styleScale = (MathF.Abs(scaleX) + MathF.Abs(scaleY)) * 0.5f;

        Vector2 rectSizeLocal = rectGeometry.Size;
        if (TryGetComputedSize(textEntity, out Vector2 computedSize) && computedSize.X > 0f && computedSize.Y > 0f)
        {
            rectSizeLocal = computedSize;
        }

        if (!TryEmitTextGlyphCommandsEcs(
                draw,
                textAny,
                text,
                rectSizeLocal,
                worldTransform,
                canvasOrigin,
                zoom,
                styleScale,
                out Vector2 centerCanvas,
                out Vector2 halfSizeCanvas,
                out float rotationDraw,
                out bool clipOverflow,
                out int firstGlyphIndex,
                out int glyphCount))
        {
            return false;
        }

        int layerCount = paintView.LayerCount;
        if (layerCount > PaintComponent.MaxLayers)
        {
            layerCount = PaintComponent.MaxLayers;
        }

        ReadOnlySpan<int> layerKind = PaintComponentProperties.LayerKindArray(_propertyWorld, paintHandle);
        ReadOnlySpan<bool> layerIsVisible = PaintComponentProperties.LayerIsVisibleArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> layerOpacityValues = PaintComponentProperties.LayerOpacityArray(_propertyWorld, paintHandle);
        ReadOnlySpan<int> layerMaskCombineOp = PaintComponentProperties.LayerMaskCombineOpArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> layerBlurWorld = PaintComponentProperties.LayerBlurArray(_propertyWorld, paintHandle);

        ReadOnlySpan<Color32> fillColor = PaintComponentProperties.FillColorArray(_propertyWorld, paintHandle);
        ReadOnlySpan<bool> fillUseGradient = PaintComponentProperties.FillUseGradientArray(_propertyWorld, paintHandle);
        ReadOnlySpan<Color32> fillGradientColorA = PaintComponentProperties.FillGradientColorAArray(_propertyWorld, paintHandle);
        ReadOnlySpan<Color32> fillGradientColorB = PaintComponentProperties.FillGradientColorBArray(_propertyWorld, paintHandle);
        ReadOnlySpan<int> fillGradientType = PaintComponentProperties.FillGradientTypeArray(_propertyWorld, paintHandle);
        ReadOnlySpan<Vector2> fillGradientDirection = PaintComponentProperties.FillGradientDirectionArray(_propertyWorld, paintHandle);
        ReadOnlySpan<Vector2> fillGradientCenter = PaintComponentProperties.FillGradientCenterArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> fillGradientRadius = PaintComponentProperties.FillGradientRadiusArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> fillGradientAngle = PaintComponentProperties.FillGradientAngleArray(_propertyWorld, paintHandle);

        ReadOnlySpan<float> strokeWidth = PaintComponentProperties.StrokeWidthArray(_propertyWorld, paintHandle);
        ReadOnlySpan<Color32> strokeColor = PaintComponentProperties.StrokeColorArray(_propertyWorld, paintHandle);

        Span<SdfCommand> maskCommands = stackalloc SdfCommand[PaintComponent.MaxLayers + 1];
        int maskCommandCount = 0;

        for (int layerIndex = layerCount - 1; layerIndex >= 0; layerIndex--)
        {
            if ((uint)layerIndex >= (uint)layerIsVisible.Length || !layerIsVisible[layerIndex])
            {
                continue;
            }

            if ((uint)layerIndex >= (uint)layerOpacityValues.Length)
            {
                continue;
            }

            float layerOpacity = opacity * Math.Clamp(layerOpacityValues[layerIndex], 0f, 1f);
            if (layerOpacity <= 0.0001f)
            {
                continue;
            }

            int kindValue = (uint)layerIndex < (uint)layerKind.Length ? layerKind[layerIndex] : (int)PaintLayerKind.Fill;
            if (kindValue == (int)PaintLayerKind.Fill)
            {
                bool useGradient = (uint)layerIndex < (uint)fillUseGradient.Length && fillUseGradient[layerIndex];
                int gradientKind = useGradient && (uint)layerIndex < (uint)fillGradientType.Length
                    ? fillGradientType[layerIndex]
                    : PaintFillGradientKindLinear;
                Vector2 gradientDirection = (uint)layerIndex < (uint)fillGradientDirection.Length
                    ? fillGradientDirection[layerIndex]
                    : new Vector2(1f, 0f);
                Vector2 gradientCenter = (uint)layerIndex < (uint)fillGradientCenter.Length
                    ? fillGradientCenter[layerIndex]
                    : Vector2.Zero;
                float gradientRadiusScale = (uint)layerIndex < (uint)fillGradientRadius.Length
                    ? fillGradientRadius[layerIndex]
                    : 1f;
                float gradientAngleOffset = (uint)layerIndex < (uint)fillGradientAngle.Length
                    ? fillGradientAngle[layerIndex]
                    : 0f;

                uint fillStartArgb;
                uint fillEndArgb;
                int gradientStopCount;
                int gradientStopStartIndex = -1;

                if (useGradient)
                {
                    gradientStopStartIndex = AddFillGradientStops(draw, paintHandle, layerIndex, layerOpacity, out fillStartArgb, out fillEndArgb, out gradientStopCount);
                    if (gradientStopStartIndex < 0)
                    {
                        Color32 startColor = (uint)layerIndex < (uint)fillGradientColorA.Length
                            ? ApplyTintAndOpacity(fillGradientColorA[layerIndex], tint: Color32.White, opacity: layerOpacity)
                            : default;
                        Color32 endColor = (uint)layerIndex < (uint)fillGradientColorB.Length
                            ? ApplyTintAndOpacity(fillGradientColorB[layerIndex], tint: Color32.White, opacity: layerOpacity)
                            : default;
                        fillStartArgb = startColor.A == 0 ? 0u : ToArgb(startColor);
                        fillEndArgb = endColor.A == 0 ? 0u : ToArgb(endColor);
                        gradientStopCount = 0;
                    }
                }
                else
                {
                    Color32 solidColor = (uint)layerIndex < (uint)fillColor.Length
                        ? ApplyTintAndOpacity(fillColor[layerIndex], tint: Color32.White, opacity: layerOpacity)
                        : default;
                    fillStartArgb = solidColor.A == 0 ? 0u : ToArgb(solidColor);
                    fillEndArgb = 0u;
                    gradientStopCount = 0;
                }

                bool hasFill = useGradient
                    ? (gradientStopStartIndex >= 0 || fillStartArgb != 0u || fillEndArgb != 0u)
                    : fillStartArgb != 0u;
                if (!hasFill)
                {
                    continue;
                }

                int refCommandIndex = draw.CommandCount;
                var cmd = SdfCommand.TextGroup(centerCanvas, halfSizeCanvas, firstGlyphIndex, glyphCount, ImStyle.ToVector4(fillStartArgb))
                    .WithRotation(rotationDraw)
                    .WithInternalNoRender();

                if (useGradient)
                {
                    cmd = WithPaintFillGradient(
                        cmd,
                        fillEndArgb,
                        gradientKind,
                        gradientDirection,
                        gradientCenter,
                        gradientRadiusScale,
                        gradientAngleOffset,
                        polygonRotationRadians: 0f,
                        polygonManualRotation: false);
                    if (gradientStopStartIndex >= 0 && gradientStopCount >= 2)
                    {
                        cmd = cmd.WithGradientStops(gradientStopStartIndex, gradientStopCount);
                    }
                }

                draw.Add(cmd);

                int combineOpValue = (uint)layerIndex < (uint)layerMaskCombineOp.Length ? layerMaskCombineOp[layerIndex] : 0;
                var combineOp = (SdfMaskCombineOp)Math.Clamp(combineOpValue, (int)SdfMaskCombineOp.Union, (int)SdfMaskCombineOp.Multiply);

                float blurWorld = (uint)layerIndex < (uint)layerBlurWorld.Length ? layerBlurWorld[layerIndex] : 0f;
                float blurCanvas = Math.Max(0f, blurWorld) * zoom * styleScale;
                float layerSoftEdgePx = Math.Max(softEdgePx, blurCanvas);

                maskCommands[maskCommandCount++] = SdfMaskShape.CommandRefPaint((uint)refCommandIndex, layerSoftEdgePx, invert, combineOp);
                continue;
            }

            if (kindValue != (int)PaintLayerKind.Stroke)
            {
                continue;
            }

            float strokeWidthWorld = (uint)layerIndex < (uint)strokeWidth.Length ? strokeWidth[layerIndex] : 0f;
            if (strokeWidthWorld <= 0.0001f)
            {
                continue;
            }

            float strokeWidthCanvas = strokeWidthWorld * zoom * styleScale;

            bool useStrokeGradient = (uint)layerIndex < (uint)fillUseGradient.Length && fillUseGradient[layerIndex];
            int strokeGradientKind = useStrokeGradient && (uint)layerIndex < (uint)fillGradientType.Length
                ? fillGradientType[layerIndex]
                : PaintFillGradientKindLinear;
            Vector2 strokeGradientDirection = (uint)layerIndex < (uint)fillGradientDirection.Length
                ? fillGradientDirection[layerIndex]
                : new Vector2(1f, 0f);
            Vector2 strokeGradientCenter = (uint)layerIndex < (uint)fillGradientCenter.Length
                ? fillGradientCenter[layerIndex]
                : Vector2.Zero;
            float strokeGradientRadiusScale = (uint)layerIndex < (uint)fillGradientRadius.Length
                ? fillGradientRadius[layerIndex]
                : 1f;
            float strokeGradientAngleOffset = (uint)layerIndex < (uint)fillGradientAngle.Length
                ? fillGradientAngle[layerIndex]
                : 0f;

            uint strokeStartArgb;
            uint strokeEndArgb;
            int strokeGradientStopCount;
            int strokeGradientStopStartIndex = -1;

            if (useStrokeGradient)
            {
                strokeGradientStopStartIndex = AddFillGradientStops(draw, paintHandle, layerIndex, layerOpacity, out strokeStartArgb, out strokeEndArgb, out strokeGradientStopCount);
                if (strokeGradientStopStartIndex < 0)
                {
                    Color32 startColor = (uint)layerIndex < (uint)fillGradientColorA.Length
                        ? ApplyTintAndOpacity(fillGradientColorA[layerIndex], tint: Color32.White, opacity: layerOpacity)
                        : default;
                    Color32 endColor = (uint)layerIndex < (uint)fillGradientColorB.Length
                        ? ApplyTintAndOpacity(fillGradientColorB[layerIndex], tint: Color32.White, opacity: layerOpacity)
                        : default;
                    strokeStartArgb = startColor.A == 0 ? 0u : ToArgb(startColor);
                    strokeEndArgb = endColor.A == 0 ? 0u : ToArgb(endColor);
                    strokeGradientStopCount = 0;
                }
            }
            else
            {
                Color32 strokeColor32 = (uint)layerIndex < (uint)strokeColor.Length
                    ? ApplyTintAndOpacity(strokeColor[layerIndex], tint: Color32.White, opacity: layerOpacity)
                    : default;
                strokeStartArgb = strokeColor32.A == 0 ? 0u : ToArgb(strokeColor32);
                strokeEndArgb = 0u;
                strokeGradientStopCount = 0;
            }

            bool hasStroke = useStrokeGradient
                ? (strokeGradientStopStartIndex >= 0 || strokeStartArgb != 0u || strokeEndArgb != 0u)
                : strokeStartArgb != 0u;
            if (!hasStroke)
            {
                continue;
            }

            int strokeRefCommandIndex = draw.CommandCount;
            var strokeCmd = SdfCommand.TextGroup(centerCanvas, halfSizeCanvas, firstGlyphIndex, glyphCount, Vector4.Zero)
                .WithRotation(rotationDraw)
                .WithStroke(ImStyle.ToVector4(strokeStartArgb), strokeWidthCanvas)
                .WithInternalNoRender();

            if (useStrokeGradient)
            {
                strokeCmd = WithPaintFillGradient(
                    strokeCmd,
                    strokeEndArgb,
                    strokeGradientKind,
                    strokeGradientDirection,
                    strokeGradientCenter,
                    strokeGradientRadiusScale,
                    strokeGradientAngleOffset,
                    polygonRotationRadians: 0f,
                    polygonManualRotation: false);
                if (strokeGradientStopStartIndex >= 0 && strokeGradientStopCount >= 2)
                {
                    strokeCmd = strokeCmd.WithGradientStops(strokeGradientStopStartIndex, strokeGradientStopCount);
                }
            }

            draw.Add(strokeCmd);

            int strokeCombineOpValue = (uint)layerIndex < (uint)layerMaskCombineOp.Length ? layerMaskCombineOp[layerIndex] : 0;
            var strokeCombineOp = (SdfMaskCombineOp)Math.Clamp(strokeCombineOpValue, (int)SdfMaskCombineOp.Union, (int)SdfMaskCombineOp.Multiply);

            float strokeBlurWorld = (uint)layerIndex < (uint)layerBlurWorld.Length ? layerBlurWorld[layerIndex] : 0f;
            float strokeBlurCanvas = Math.Max(0f, strokeBlurWorld) * zoom * styleScale;
            float strokeLayerSoftEdgePx = Math.Max(softEdgePx, strokeBlurCanvas);

            maskCommands[maskCommandCount++] = SdfMaskShape.CommandRefPaint((uint)strokeRefCommandIndex, strokeLayerSoftEdgePx, invert, strokeCombineOp);
        }

        if (maskCommandCount <= 0)
        {
            return false;
        }

        if (clipOverflow)
        {
            maskCommands[maskCommandCount++] = SdfMaskShape.Rect(centerCanvas, halfSizeCanvas, softEdge: 0.5f, invert: false)
                .WithMaskMeta(usePaint: false, combineOp: SdfMaskCombineOp.Intersect);
        }

        maskCommands[0] = maskCommands[0].WithMaskUnionCount(maskCommandCount - 1);
        draw.PushMask(maskCommands.Slice(0, maskCommandCount));
        return true;
    }

    private bool TryEmitTextMaskGeometryCommandEcs(
        CanvasSdfDrawList draw,
        EntityId textEntity,
        Vector2 canvasOrigin,
        in WorldTransform parentWorldTransform,
        out int maskCommandIndex)
    {
        maskCommandIndex = -1;

        if (!TryGetTextWorldTransformEcs(textEntity, parentWorldTransform, out ShapeWorldTransform worldTransform))
        {
            return false;
        }

        if (!worldTransform.IsVisible)
        {
            return false;
        }

        if (!TryGetBlendStateEcs(textEntity, out bool isVisible, out float opacity, out _))
        {
            return false;
        }

        if (!isVisible || opacity <= 0.0001f)
        {
            return false;
        }

        if (!_world.TryGetComponent(textEntity, RectGeometryComponent.Api.PoolIdConst, out AnyComponentHandle rectAny))
        {
            return false;
        }

        if (!_world.TryGetComponent(textEntity, TextComponent.Api.PoolIdConst, out AnyComponentHandle textAny))
        {
            return false;
        }

        var rectHandle = new RectGeometryComponentHandle(rectAny.Index, rectAny.Generation);
        var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, rectHandle);
        if (!rectGeometry.IsAlive)
        {
            return false;
        }

        var textHandle = new TextComponentHandle(textAny.Index, textAny.Generation);
        var text = TextComponent.Api.FromHandle(_propertyWorld, textHandle);
        if (!text.IsAlive)
        {
            return false;
        }

        float zoom = Zoom;
        Vector2 scaleWorld = worldTransform.ScaleWorld;
        float scaleX = scaleWorld.X == 0f ? 1f : scaleWorld.X;
        float scaleY = scaleWorld.Y == 0f ? 1f : scaleWorld.Y;
        float styleScale = (MathF.Abs(scaleX) + MathF.Abs(scaleY)) * 0.5f;

        Vector2 rectSizeLocal = rectGeometry.Size;
        if (TryGetComputedSize(textEntity, out Vector2 computedSize) && computedSize.X > 0f && computedSize.Y > 0f)
        {
            rectSizeLocal = computedSize;
        }

        if (!TryEmitTextGlyphCommandsEcs(
                draw,
                textAny,
                text,
                rectSizeLocal,
                worldTransform,
                canvasOrigin,
                zoom,
                styleScale,
                out Vector2 centerCanvas,
                out Vector2 halfSizeCanvas,
                out float rotationDraw,
                out _,
                out int firstGlyphIndex,
                out int glyphCount))
        {
            return false;
        }

        maskCommandIndex = draw.CommandCount;
        var cmd = SdfCommand.TextGroup(centerCanvas, halfSizeCanvas, firstGlyphIndex, glyphCount, Vector4.Zero)
            .WithRotation(rotationDraw)
            .WithInternalNoRender();
        draw.Add(cmd);
        return true;
    }

    private bool TryEmitTextGlyphCommandsEcs(
        CanvasSdfDrawList draw,
        AnyComponentHandle textAny,
        in TextComponent.ViewProxy text,
        Vector2 rectSizeLocal,
        in ShapeWorldTransform worldTransform,
        Vector2 canvasOrigin,
        float zoom,
        float styleScale,
        out Vector2 centerCanvas,
        out Vector2 halfSizeCanvas,
        out float rotationDraw,
        out bool clipOverflow,
        out int firstGlyphIndex,
        out int glyphCount)
    {
        float scaleX = worldTransform.ScaleWorld.X == 0f ? 1f : worldTransform.ScaleWorld.X;
        float scaleY = worldTransform.ScaleWorld.Y == 0f ? 1f : worldTransform.ScaleWorld.Y;

        float widthCanvas = rectSizeLocal.X * MathF.Abs(scaleX) * zoom;
        float heightCanvas = rectSizeLocal.Y * MathF.Abs(scaleY) * zoom;
        halfSizeCanvas = new Vector2(widthCanvas * 0.5f, heightCanvas * 0.5f);

        Vector2 anchor = worldTransform.Anchor;
        Vector2 anchorOffset = new Vector2((anchor.X - 0.5f) * (rectSizeLocal.X * MathF.Abs(scaleX)), (anchor.Y - 0.5f) * (rectSizeLocal.Y * MathF.Abs(scaleY)));
        Vector2 centerWorld = worldTransform.PositionWorld - RotateVector(anchorOffset, worldTransform.RotationRadians);

        centerCanvas = new Vector2(WorldToCanvasX(centerWorld.X, canvasOrigin), WorldToCanvasY(centerWorld.Y, canvasOrigin));
        Vector2 boxTopLeftCanvas = new Vector2(centerCanvas.X - halfSizeCanvas.X, centerCanvas.Y - halfSizeCanvas.Y);
        rotationDraw = worldTransform.RotationRadians;

        Font font = GetTextRenderFont();

        float fontSizeCanvasPx = Math.Max(1f, text.FontSizePx) * zoom * styleScale;
        float lineHeightScale = text.LineHeightScale;
        float letterSpacingCanvasPx = text.LetterSpacingPx * zoom * styleScale;

        Vector2 boxSizeCanvas = new Vector2(widthCanvas, heightCanvas);
        TextLayoutCache layout = GetOrCreateTextLayoutCache(textAny);
        layout.Ensure(
            font,
            text.Text,
            text.Font,
            fontSizeCanvasPx,
            lineHeightScale,
            letterSpacingCanvasPx,
            text.Multiline,
            text.Wrap,
            text.Overflow,
            text.AlignX,
            text.AlignY,
            boxSizeCanvas);

        float resolvedFontSizeCanvasPx = layout.ResolvedFontSizePx;
        float scaleFont = resolvedFontSizeCanvasPx / font.BaseSizePixels;
        float lineHeightCanvasPx = font.LineHeightPixels * scaleFont * Math.Clamp(lineHeightScale, 0.25f, 8f);

        int alignXValue = Math.Clamp(text.AlignX, (int)TextHorizontalAlign.Left, (int)TextHorizontalAlign.Right);
        int alignYValue = Math.Clamp(text.AlignY, (int)TextVerticalAlign.Top, (int)TextVerticalAlign.Bottom);
        var overflowMode = (TextOverflowMode)Math.Clamp(text.Overflow, (int)TextOverflowMode.Visible, (int)TextOverflowMode.Fit);

        ReadOnlySpan<TextLayoutCache.Line> lines = layout.Lines;
        ReadOnlySpan<TextLayoutCache.Glyph> glyphs = layout.Glyphs;
        if (glyphs.IsEmpty)
        {
            firstGlyphIndex = 0;
            glyphCount = 0;
            clipOverflow = false;
            return false;
        }

        float totalHeight = lines.Length * lineHeightCanvasPx;
        float yOffset = 0f;
        if (alignYValue == (int)TextVerticalAlign.Middle)
        {
            yOffset = (boxSizeCanvas.Y - totalHeight) * 0.5f;
        }
        else if (alignYValue == (int)TextVerticalAlign.Bottom)
        {
            yOffset = boxSizeCanvas.Y - totalHeight;
        }
        if (yOffset < 0f)
        {
            yOffset = 0f;
        }

        int visibleLineCount = lines.Length;
        if (text.Multiline)
        {
            int maxLines = lineHeightCanvasPx <= 0.0001f ? lines.Length : (int)MathF.Floor(boxSizeCanvas.Y / lineHeightCanvasPx);
            if (maxLines < 0)
            {
                maxLines = 0;
            }
            if (maxLines < visibleLineCount)
            {
                visibleLineCount = maxLines;
            }
        }
        else
        {
            visibleLineCount = Math.Min(1, visibleLineCount);
        }

        bool needsEllipsis = overflowMode == TextOverflowMode.Ellipsis && (layout.MeasuredWidthPx > boxSizeCanvas.X + 0.0001f || layout.MeasuredHeightPx > boxSizeCanvas.Y + 0.0001f);

        clipOverflow = overflowMode == TextOverflowMode.Clipped;

        firstGlyphIndex = draw.CommandCount;

        bool canDrawDots = font.TryGetGlyph('.', out FontGlyph dotGlyph) && dotGlyph.Width > 0 && dotGlyph.Height > 0;
        float dotAdvance = canDrawDots ? (dotGlyph.AdvanceX * scaleFont + letterSpacingCanvasPx) : 0f;
        float ellipsisWidth = canDrawDots ? Math.Max(0f, dotAdvance * 3f - letterSpacingCanvasPx) : 0f;

        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (lineIndex >= visibleLineCount)
            {
                break;
            }

            ref readonly TextLayoutCache.Line line = ref lines[lineIndex];
            float xOffset = 0f;
            if (alignXValue == (int)TextHorizontalAlign.Center)
            {
                xOffset = (boxSizeCanvas.X - line.WidthPx) * 0.5f;
            }
            else if (alignXValue == (int)TextHorizontalAlign.Right)
            {
                xOffset = boxSizeCanvas.X - line.WidthPx;
            }

            if (xOffset < 0f)
            {
                xOffset = 0f;
            }

            bool isEllipsisLine = needsEllipsis && lineIndex == visibleLineCount - 1;
            float ellipsisStartX = isEllipsisLine ? Math.Max(0f, boxSizeCanvas.X - ellipsisWidth) : 0f;

            int start = line.GlyphStart;
            int end = start + line.GlyphCount;
            for (int gi = start; gi < end; gi++)
            {
                ref readonly TextLayoutCache.Glyph g = ref glyphs[gi];

                float localCenterX = g.CenterPx.X + xOffset;
                float localCenterY = g.CenterPx.Y + yOffset;
                float left = localCenterX - g.HalfSizePx.X;
                float right = localCenterX + g.HalfSizePx.X;
                float top = localCenterY - g.HalfSizePx.Y;
                float bottom = localCenterY + g.HalfSizePx.Y;

                if (overflowMode == TextOverflowMode.Hidden)
                {
                    if (left < 0f || right > boxSizeCanvas.X || top < 0f || bottom > boxSizeCanvas.Y)
                    {
                        continue;
                    }
                }

                if (isEllipsisLine && canDrawDots)
                {
                    if (right > ellipsisStartX)
                    {
                        continue;
                    }
                }

                Vector2 center = new Vector2(boxTopLeftCanvas.X + localCenterX, boxTopLeftCanvas.Y + localCenterY);
                var cmd = SdfCommand.Glyph(center, g.HalfSizePx, g.UvRect, Vector4.Zero).WithInternalNoRender();
                draw.Add(cmd);
            }

            if (isEllipsisLine && canDrawDots)
            {
                float cursorX = ellipsisStartX;
                float baselineY = line.BaselineYPx + yOffset;
                for (int di = 0; di < 3; di++)
                {
                    float glyphW = dotGlyph.Width * scaleFont;
                    float glyphH = dotGlyph.Height * scaleFont;
                    float glyphX = cursorX + dotGlyph.OffsetX * scaleFont;
                    float glyphY = baselineY + dotGlyph.OffsetY * scaleFont;
                    Vector2 dotCenterLocal = new Vector2(glyphX + glyphW * 0.5f, glyphY + glyphH * 0.5f);
                    Vector2 dotHalfSize = new Vector2(glyphW * 0.5f, glyphH * 0.5f);
                    Vector2 dotCenterCanvas = boxTopLeftCanvas + dotCenterLocal;
                    var cmd = SdfCommand.Glyph(dotCenterCanvas, dotHalfSize, new Vector4(dotGlyph.U0, dotGlyph.V0, dotGlyph.U1, dotGlyph.V1), Vector4.Zero)
                        .WithInternalNoRender();
                    draw.Add(cmd);
                    cursorX += dotAdvance;
                }
            }
        }

        glyphCount = draw.CommandCount - firstGlyphIndex;
        return glyphCount > 0;
    }

    private bool TryEmitShapeMaskGeometryCommandEcs(CanvasSdfDrawList draw, EntityId shapeEntity, Vector2 canvasOrigin, in WorldTransform parentWorldTransform)
    {
        if (!TryGetShapeWorldTransformEcs(shapeEntity, parentWorldTransform, out ShapeWorldTransform worldTransform))
        {
            return false;
        }

        ShapeKind kind = GetShapeKindEcs(shapeEntity, out RectGeometryComponentHandle rectGeometryHandle, out CircleGeometryComponentHandle circleGeometryHandle, out PathComponentHandle pathHandle);
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
            float rotationRadians = worldTransform.RotationRadians;

            GetBounds(pointsLocal, out float minLocalX, out float minLocalY, out float maxLocalX, out float maxLocalY);
            Vector2 boundsMinLocal = new Vector2(minLocalX, minLocalY);
            Vector2 boundsSizeLocal = new Vector2(maxLocalX - minLocalX, maxLocalY - minLocalY);
            Vector2 pivotLocal = GetPolygonPivotLocalEcs(pathHandle, worldTransform.Anchor, boundsMinLocal, boundsSizeLocal);

            EnsurePolygonScratchCapacity(pointCount + 1);
            for (int i = 0; i < pointCount; i++)
            {
                Vector2 pWorld = TransformPolygonPointToWorld(positionWorld, pivotLocal, rotationRadians, scaleX, scaleY, pointsLocal[i]);
                _polygonCanvasScratch[i] = new Vector2(
                    WorldToCanvasX(pWorld.X, canvasOrigin),
                    WorldToCanvasY(pWorld.Y, canvasOrigin));
            }
            _polygonCanvasScratch[pointCount] = _polygonCanvasScratch[0];

            ReadOnlySpan<Vector2> polyline = _polygonCanvasScratch.AsSpan(0, pointCount + 1);
            int headerIndex = draw.AddPolyline(polyline);
            var cmd = SdfCommand.FilledPolygon(headerIndex, Vector4.Zero).WithInternalNoRender();
            draw.Add(cmd);
            return true;
        }

        float zoom = Zoom;
        float rotationDraw = 0f;
        Vector2 centerCanvas;
        ImRect rectCanvas = GetShapeRectCanvasForDrawEcs(shapeEntity, kind, rectGeometryHandle, circleGeometryHandle, canvasOrigin, worldTransform, out centerCanvas, out rotationDraw);
        Vector2 halfSizeCanvas = new Vector2(rectCanvas.Width * 0.5f, rectCanvas.Height * 0.5f);

        if (kind == ShapeKind.Rect)
        {
            var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, rectGeometryHandle);
            if (!rectGeometry.IsAlive)
            {
                return false;
            }

            Vector2 scaleWorld = worldTransform.ScaleWorld;
            float scaleX = scaleWorld.X;
            float scaleY = scaleWorld.Y;
            float radiusScale = (MathF.Abs(scaleX) + MathF.Abs(scaleY)) * 0.5f;
            float radiusTL = Math.Max(0f, rectGeometry.CornerRadius.X) * radiusScale * zoom;
            float radiusTR = Math.Max(0f, rectGeometry.CornerRadius.Y) * radiusScale * zoom;
            float radiusBR = Math.Max(0f, rectGeometry.CornerRadius.Z) * radiusScale * zoom;
            float radiusBL = Math.Max(0f, rectGeometry.CornerRadius.W) * radiusScale * zoom;
            bool hasCornerRadius = radiusTL > 0f || radiusTR > 0f || radiusBR > 0f || radiusBL > 0f;

            var cmd = hasCornerRadius
                ? SdfCommand.RoundedRectPerCorner(centerCanvas, halfSizeCanvas, radiusTL, radiusTR, radiusBR, radiusBL, Vector4.Zero)
                : SdfCommand.Rect(centerCanvas, halfSizeCanvas, Vector4.Zero);

            cmd = cmd.WithRotation(rotationDraw).WithInternalNoRender();
            draw.Add(cmd);
            return true;
        }

        float radius = Math.Min(rectCanvas.Width, rectCanvas.Height) * 0.5f;
        var circleCmd = SdfCommand.Circle(centerCanvas, radius, Vector4.Zero)
            .WithRotation(rotationDraw)
            .WithInternalNoRender();
        draw.Add(circleCmd);
        return true;
    }

    private bool TryGetBlendStateEcs(EntityId entity, out bool isVisible, out float opacity, out PaintBlendMode blendMode)
    {
        isVisible = true;
        opacity = 1f;
        blendMode = PaintBlendMode.Normal;

        if (!_world.TryGetComponent(entity, BlendComponent.Api.PoolIdConst, out AnyComponentHandle blendAny))
        {
            return true;
        }

        var blendHandle = new BlendComponentHandle(blendAny.Index, blendAny.Generation);
        return TryGetBlendState(blendHandle, out isVisible, out opacity, out blendMode);
    }

    private bool TryGetShapeWorldTransformEcs(EntityId shapeEntity, in WorldTransform parentWorldTransform, out ShapeWorldTransform worldTransform)
    {
        worldTransform = default;
        if (!parentWorldTransform.IsVisible)
        {
            return false;
        }

        if (!_world.TryGetComponent(shapeEntity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny))
        {
            return false;
        }

        var transformHandle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
        var transform = TransformComponent.Api.FromHandle(_propertyWorld, transformHandle);
        if (!transform.IsAlive)
        {
            return false;
        }

        if (!TryGetBlendStateEcs(shapeEntity, out bool isVisible, out float opacity, out PaintBlendMode blendMode))
        {
            return false;
        }

        if (!isVisible)
        {
            return false;
        }

        Vector2 localPosition = transform.Position;
        if (TryGetComputedTransform(shapeEntity, out ComputedTransformComponentHandle computedTransformHandle))
        {
            var computedTransform = ComputedTransformComponent.Api.FromHandle(_propertyWorld, computedTransformHandle);
            if (computedTransform.IsAlive)
            {
                localPosition = computedTransform.Position;
            }
        }

        Vector2 localScale = NormalizeScale(transform.Scale);
        Vector2 scaleWorld = new Vector2(parentWorldTransform.Scale.X * localScale.X, parentWorldTransform.Scale.Y * localScale.Y);
        float rotationRadians = parentWorldTransform.RotationRadians + transform.Rotation * (MathF.PI / 180f);
        Vector2 positionWorld = TransformPoint(parentWorldTransform, localPosition);

        worldTransform = new ShapeWorldTransform(
            positionWorld,
            scaleWorld,
            rotationRadians,
            transform.Anchor,
            opacity,
            blendMode,
            isVisible: true);
        return true;
    }

    private bool TryGetTextWorldTransformEcs(EntityId textEntity, in WorldTransform parentWorldTransform, out ShapeWorldTransform worldTransform)
    {
        worldTransform = default;
        if (!parentWorldTransform.IsVisible)
        {
            return false;
        }

        if (!_world.TryGetComponent(textEntity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny))
        {
            return false;
        }

        var transformHandle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
        var transform = TransformComponent.Api.FromHandle(_propertyWorld, transformHandle);
        if (!transform.IsAlive)
        {
            return false;
        }

        if (!TryGetBlendStateEcs(textEntity, out bool isVisible, out float opacity, out PaintBlendMode blendMode))
        {
            return false;
        }

        if (!isVisible)
        {
            return false;
        }

        Vector2 localPosition = transform.Position;
        if (TryGetComputedTransform(textEntity, out ComputedTransformComponentHandle computedTransformHandle))
        {
            var computedTransform = ComputedTransformComponent.Api.FromHandle(_propertyWorld, computedTransformHandle);
            if (computedTransform.IsAlive)
            {
                localPosition = computedTransform.Position;
            }
        }

        Vector2 localScale = NormalizeScale(transform.Scale);
        Vector2 scaleWorld = new Vector2(parentWorldTransform.Scale.X * localScale.X, parentWorldTransform.Scale.Y * localScale.Y);
        float rotationRadians = parentWorldTransform.RotationRadians + transform.Rotation * (MathF.PI / 180f);
        Vector2 positionWorld = TransformPoint(parentWorldTransform, localPosition);

        worldTransform = new ShapeWorldTransform(
            positionWorld,
            scaleWorld,
            rotationRadians,
            transform.Anchor,
            opacity,
            blendMode,
            isVisible: true);
        return true;
    }

    private bool TryGetGroupWorldTransformEcs(EntityId groupEntity, in WorldTransform parentWorldTransform, out WorldTransform groupWorldTransform, out float opacity, out PaintBlendMode blendMode)
    {
        groupWorldTransform = IdentityWorldTransform;
        opacity = 1f;
        blendMode = PaintBlendMode.Normal;

        if (!TryGetBlendStateEcs(groupEntity, out bool groupIsVisible, out opacity, out blendMode))
        {
            return false;
        }

        if (!_world.TryGetComponent(groupEntity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny))
        {
            groupWorldTransform = new WorldTransform(Vector2.Zero, Vector2.One, 0f, groupIsVisible && parentWorldTransform.IsVisible);
            return true;
        }

        var transformHandle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
        var transform = TransformComponent.Api.FromHandle(_propertyWorld, transformHandle);
        if (!transform.IsAlive)
        {
            return false;
        }

        Vector2 localPosition = transform.Position;
        if (TryGetComputedTransform(groupEntity, out ComputedTransformComponentHandle computedTransformHandle))
        {
            var computedTransform = ComputedTransformComponent.Api.FromHandle(_propertyWorld, computedTransformHandle);
            if (computedTransform.IsAlive)
            {
                localPosition = computedTransform.Position;
            }
        }

        Vector2 localScale = NormalizeScale(transform.Scale);
        float localRotationRadians = transform.Rotation * (MathF.PI / 180f);
        groupWorldTransform = ComposeTransform(parentWorldTransform, localPosition, localScale, localRotationRadians, groupIsVisible);
        return true;
    }

    private bool TryGetBooleanGroupSettingsEcs(EntityId groupEntity, out int operation, out float smoothness)
    {
        operation = 0;
        smoothness = 12f;

        if (!_world.TryGetComponent(groupEntity, BooleanGroupComponent.Api.PoolIdConst, out AnyComponentHandle componentAny))
        {
            return true;
        }

        var handle = new BooleanGroupComponentHandle(componentAny.Index, componentAny.Generation);
        var view = BooleanGroupComponent.Api.FromHandle(_propertyWorld, handle);
        if (!view.IsAlive)
        {
            return false;
        }

        operation = view.Operation;
        smoothness = view.Smoothness;
        return true;
    }

    private void BuildBooleanGroupEntityLayered(CanvasSdfDrawList draw, EntityId groupEntity, Vector2 canvasOrigin, in WorldTransform parentWorldTransform)
    {
        int maxLayerCount = GetMaxPaintLayerCountInBooleanSubtree(groupEntity);
        if (maxLayerCount <= 0)
        {
            maxLayerCount = 1;
        }

        for (int layerIndex = maxLayerCount - 1; layerIndex >= 0; layerIndex--)
        {
            GroupEffectBounds effectBounds = default;
            BuildBooleanGroupEntityForLayer(draw, groupEntity, canvasOrigin, parentWorldTransform, layerIndex, ref effectBounds);
        }
    }

    private int GetMaxPaintLayerCountInBooleanSubtree(EntityId groupEntity)
    {
        int max = 0;

        Span<EntityId> stack = stackalloc EntityId[256];
        int count = 0;
        stack[count++] = groupEntity;

        while (count > 0)
        {
            EntityId current = stack[--count];
            ReadOnlySpan<EntityId> children = _world.GetChildren(current);
            int childStart = 0;
            if (_world.GetNodeType(current) == UiNodeType.BooleanGroup &&
                _world.TryGetComponent(current, MaskGroupComponent.Api.PoolIdConst, out _) &&
                children.Length > 0)
            {
                // Child 0 is the mask source; it does not render as paint.
                childStart = 1;
            }

            for (int childIndex = childStart; childIndex < children.Length; childIndex++)
            {
                EntityId child = children[childIndex];
                UiNodeType type = _world.GetNodeType(child);
                if (type == UiNodeType.BooleanGroup || type == UiNodeType.Shape)
                {
                    if (count < stack.Length)
                    {
                        stack[count++] = child;
                    }
                }

                if (type != UiNodeType.Shape)
                {
                    continue;
                }

                if (!_world.TryGetComponent(child, PaintComponent.Api.PoolIdConst, out AnyComponentHandle paintAny))
                {
                    continue;
                }

                var paintHandle = new PaintComponentHandle(paintAny.Index, paintAny.Generation);
                var paintView = PaintComponent.Api.FromHandle(_propertyWorld, paintHandle);
                if (!paintView.IsAlive)
                {
                    continue;
                }

                int layerCount = paintView.LayerCount;
                if (layerCount > max)
                {
                    max = layerCount;
                }
            }
        }

        return max;
    }

    private void BuildBooleanGroupEntityForLayer(CanvasSdfDrawList draw, EntityId groupEntity, Vector2 canvasOrigin, in WorldTransform parentWorldTransform, int paintLayerIndex, ref GroupEffectBounds effectBounds)
    {
        if (!TryGetGroupWorldTransformEcs(groupEntity, parentWorldTransform, out WorldTransform groupWorldTransform, out float groupOpacity, out PaintBlendMode groupBlendMode))
        {
            return;
        }

        if (!groupWorldTransform.IsVisible)
        {
            return;
        }

        if (!TryGetBooleanGroupSettingsEcs(groupEntity, out int opIndex, out float smoothnessWorld))
        {
            return;
        }

        float zoom = Zoom;
        float groupStyleScale = (MathF.Abs(groupWorldTransform.Scale.X) + MathF.Abs(groupWorldTransform.Scale.Y)) * 0.5f;
        if (groupStyleScale <= 0.0001f)
        {
            groupStyleScale = 1f;
        }

        float smoothnessCanvas = smoothnessWorld * groupStyleScale * zoom;
        var op = GetSdfBooleanOp(opIndex, smoothnessCanvas);
        draw.BeginBooleanGroup(op, smoothness: smoothnessCanvas);

        bool includeDistanceOnlyOperands = op != SdfBooleanOp.Union && op != SdfBooleanOp.SmoothUnion;

        GroupEffectBounds localBounds = default;
        ReadOnlySpan<EntityId> children = _world.GetChildren(groupEntity);
        for (int childIndex = 0; childIndex < children.Length; childIndex++)
        {
            EntityId child = children[childIndex];
            UiNodeType childType = _world.GetNodeType(child);
            if (childType == UiNodeType.BooleanGroup)
            {
                int pushedChildWarps = TryPushEntityWarpStackEcs(draw, child);
                if (_world.TryGetComponent(child, MaskGroupComponent.Api.PoolIdConst, out _))
                {
                    BuildMaskGroupEntityForLayer(draw, child, canvasOrigin, groupWorldTransform, paintLayerIndex, includeDistanceOnlyOperands, ref localBounds);
                }
                else
                {
                    BuildBooleanGroupEntityForLayer(draw, child, canvasOrigin, groupWorldTransform, paintLayerIndex, ref localBounds);
                }
                for (int popIndex = 0; popIndex < pushedChildWarps; popIndex++)
                {
                    draw.PopWarp();
                }
            }
            else if (childType == UiNodeType.Shape)
            {
                int pushedChildWarps = TryPushEntityWarpStackEcs(draw, child);
                EmitBooleanOperandEcs(draw, child, canvasOrigin, groupWorldTransform, paintLayerIndex, includeDistanceOnlyOperands, ref localBounds);
                for (int popIndex = 0; popIndex < pushedChildWarps; popIndex++)
                {
                    draw.PopWarp();
                }
            }
        }

        effectBounds.AccumulateFrom(in localBounds);

        uint groupEndColor = ImStyle.WithAlphaF(0xFFFFFFFF, groupOpacity);
        uint groupBlendModeValue = (uint)groupBlendMode;
        draw.EndBooleanGroup(
            fillColor: groupEndColor,
            strokeColor: 0u,
            strokeWidth: localBounds.MaxStrokeWidthCanvas,
            glowRadius: localBounds.MaxGlowRadiusCanvas,
            blendMode: groupBlendModeValue);
    }

    private void BuildMaskGroupEntityForLayer(
        CanvasSdfDrawList draw,
        EntityId groupEntity,
        Vector2 canvasOrigin,
        in WorldTransform parentWorldTransform,
        int paintLayerIndex,
        bool includeDistanceOnlyOperands,
        ref GroupEffectBounds effectBounds)
    {
        if (!TryGetGroupWorldTransformEcs(groupEntity, parentWorldTransform, out WorldTransform groupWorldTransform, out _, out _))
        {
            return;
        }

        if (!groupWorldTransform.IsVisible)
        {
            return;
        }

        float softEdgePx = 2f;
        bool invert = false;
        if (_world.TryGetComponent(groupEntity, MaskGroupComponent.Api.PoolIdConst, out AnyComponentHandle maskAny))
        {
            var maskHandle = new MaskGroupComponentHandle(maskAny.Index, maskAny.Generation);
            var mask = MaskGroupComponent.Api.FromHandle(_propertyWorld, maskHandle);
            if (mask.IsAlive)
            {
                softEdgePx = Math.Clamp(mask.SoftEdgePx, 0f, 64f);
                invert = mask.Invert;
            }
        }

        ReadOnlySpan<EntityId> children = _world.GetChildren(groupEntity);
        if (children.IsEmpty)
        {
            return;
        }

        EntityId maskSource = children[0];
        UiNodeType maskSourceType = _world.GetNodeType(maskSource);
        if (maskSourceType != UiNodeType.Shape && maskSourceType != UiNodeType.Text)
        {
            for (int i = 0; i < children.Length; i++)
            {
                EntityId child = children[i];
                UiNodeType childType = _world.GetNodeType(child);
                if (childType == UiNodeType.BooleanGroup)
                {
                    int pushedChildWarps = TryPushEntityWarpStackEcs(draw, child);
                    if (_world.TryGetComponent(child, MaskGroupComponent.Api.PoolIdConst, out _))
                    {
                        BuildMaskGroupEntityForLayer(draw, child, canvasOrigin, groupWorldTransform, paintLayerIndex, includeDistanceOnlyOperands, ref effectBounds);
                    }
                    else
                    {
                        BuildBooleanGroupEntityForLayer(draw, child, canvasOrigin, groupWorldTransform, paintLayerIndex, ref effectBounds);
                    }
                    for (int popIndex = 0; popIndex < pushedChildWarps; popIndex++)
                    {
                        draw.PopWarp();
                    }
                }
                else if (childType == UiNodeType.Shape)
                {
                    int pushedChildWarps = TryPushEntityWarpStackEcs(draw, child);
                    EmitBooleanOperandEcs(draw, child, canvasOrigin, groupWorldTransform, paintLayerIndex, includeDistanceOnlyOperands, ref effectBounds);
                    for (int popIndex = 0; popIndex < pushedChildWarps; popIndex++)
                    {
                        draw.PopWarp();
                    }
                }
            }
            return;
        }

        bool pushedMask = false;
        int pushedMaskSourceWarps = TryPushEntityWarpStackEcs(draw, maskSource);
        if (maskSourceType == UiNodeType.Shape)
        {
            pushedMask = TryPushMaskFromSourcePaintEcs(draw, maskSource, canvasOrigin, groupWorldTransform, softEdgePx, invert);
        }
        else
        {
            pushedMask = TryPushMaskFromTextPaintEcs(draw, maskSource, canvasOrigin, groupWorldTransform, softEdgePx, invert);
        }
        for (int popIndex = 0; popIndex < pushedMaskSourceWarps; popIndex++)
        {
            draw.PopWarp();
        }

        if (!pushedMask)
        {
            uint maskCommandIndex = 0u;
            bool hasMaskCommand = false;
            if (maskSourceType == UiNodeType.Shape)
            {
                pushedMaskSourceWarps = TryPushEntityWarpStackEcs(draw, maskSource);
                int shapeMaskCommandIndex = draw.CommandCount;
                if (TryEmitShapeMaskGeometryCommandEcs(draw, maskSource, canvasOrigin, groupWorldTransform))
                {
                    maskCommandIndex = (uint)shapeMaskCommandIndex;
                    hasMaskCommand = true;
                }
                for (int popIndex = 0; popIndex < pushedMaskSourceWarps; popIndex++)
                {
                    draw.PopWarp();
                }
            }
            else
            {
                pushedMaskSourceWarps = TryPushEntityWarpStackEcs(draw, maskSource);
                if (TryEmitTextMaskGeometryCommandEcs(draw, maskSource, canvasOrigin, groupWorldTransform, out int textMaskCommandIndex))
                {
                    maskCommandIndex = (uint)textMaskCommandIndex;
                    hasMaskCommand = true;
                }
                for (int popIndex = 0; popIndex < pushedMaskSourceWarps; popIndex++)
                {
                    draw.PopWarp();
                }
            }

            if (!hasMaskCommand)
            {
                for (int i = 0; i < children.Length; i++)
                {
                    EntityId child = children[i];
                    UiNodeType childType = _world.GetNodeType(child);
                    if (childType == UiNodeType.BooleanGroup)
                    {
                        int pushedChildWarps = TryPushEntityWarpStackEcs(draw, child);
                        if (_world.TryGetComponent(child, MaskGroupComponent.Api.PoolIdConst, out _))
                        {
                            BuildMaskGroupEntityForLayer(draw, child, canvasOrigin, groupWorldTransform, paintLayerIndex, includeDistanceOnlyOperands, ref effectBounds);
                        }
                        else
                        {
                            BuildBooleanGroupEntityForLayer(draw, child, canvasOrigin, groupWorldTransform, paintLayerIndex, ref effectBounds);
                        }
                        for (int popIndex = 0; popIndex < pushedChildWarps; popIndex++)
                        {
                            draw.PopWarp();
                        }
                    }
                    else if (childType == UiNodeType.Shape)
                    {
                        int pushedChildWarps = TryPushEntityWarpStackEcs(draw, child);
                        EmitBooleanOperandEcs(draw, child, canvasOrigin, groupWorldTransform, paintLayerIndex, includeDistanceOnlyOperands, ref effectBounds);
                        for (int popIndex = 0; popIndex < pushedChildWarps; popIndex++)
                        {
                            draw.PopWarp();
                        }
                    }
                }
                return;
            }

            draw.PushMask(SdfMaskShape.CommandRef(maskCommandIndex, softEdgePx, invert));
        }

        for (int i = 1; i < children.Length; i++)
        {
            EntityId child = children[i];
            UiNodeType childType = _world.GetNodeType(child);
            if (childType == UiNodeType.BooleanGroup)
            {
                int pushedChildWarps = TryPushEntityWarpStackEcs(draw, child);
                if (_world.TryGetComponent(child, MaskGroupComponent.Api.PoolIdConst, out _))
                {
                    BuildMaskGroupEntityForLayer(draw, child, canvasOrigin, groupWorldTransform, paintLayerIndex, includeDistanceOnlyOperands, ref effectBounds);
                }
                else
                {
                    BuildBooleanGroupEntityForLayer(draw, child, canvasOrigin, groupWorldTransform, paintLayerIndex, ref effectBounds);
                }
                for (int popIndex = 0; popIndex < pushedChildWarps; popIndex++)
                {
                    draw.PopWarp();
                }
            }
            else if (childType == UiNodeType.Shape)
            {
                int pushedChildWarps = TryPushEntityWarpStackEcs(draw, child);
                EmitBooleanOperandEcs(draw, child, canvasOrigin, groupWorldTransform, paintLayerIndex, includeDistanceOnlyOperands, ref effectBounds);
                for (int popIndex = 0; popIndex < pushedChildWarps; popIndex++)
                {
                    draw.PopWarp();
                }
            }
        }
        draw.PopMask();
    }

    private ShapeKind GetShapeKindEcs(EntityId shapeEntity, out RectGeometryComponentHandle rectGeometryHandle, out CircleGeometryComponentHandle circleGeometryHandle, out PathComponentHandle pathHandle)
    {
        rectGeometryHandle = default;
        circleGeometryHandle = default;
        pathHandle = default;

        if (_world.TryGetComponent(shapeEntity, PathComponent.Api.PoolIdConst, out AnyComponentHandle pathAny))
        {
            pathHandle = new PathComponentHandle(pathAny.Index, pathAny.Generation);
            return ShapeKind.Polygon;
        }

        if (_world.TryGetComponent(shapeEntity, CircleGeometryComponent.Api.PoolIdConst, out AnyComponentHandle circleAny))
        {
            circleGeometryHandle = new CircleGeometryComponentHandle(circleAny.Index, circleAny.Generation);
            return ShapeKind.Circle;
        }

        if (_world.TryGetComponent(shapeEntity, RectGeometryComponent.Api.PoolIdConst, out AnyComponentHandle rectAny))
        {
            rectGeometryHandle = new RectGeometryComponentHandle(rectAny.Index, rectAny.Generation);
            return ShapeKind.Rect;
        }

        return ShapeKind.Rect;
    }

    private bool TryGetPathArraysEcs(PathComponentHandle handle, out ReadOnlySpan<Vector2> positionsLocal, out ReadOnlySpan<Vector2> tangentsInLocal, out ReadOnlySpan<Vector2> tangentsOutLocal, out ReadOnlySpan<int> vertexKind, out int vertexCount)
    {
        positionsLocal = default;
        tangentsInLocal = default;
        tangentsOutLocal = default;
        vertexKind = default;
        vertexCount = 0;

        if (handle.IsNull)
        {
            return false;
        }

        var view = PathComponent.Api.FromHandle(_propertyWorld, handle);
        if (!view.IsAlive)
        {
            return false;
        }

        vertexCount = view.VertexCount;
        if (vertexCount < 0)
        {
            vertexCount = 0;
        }
        else if (vertexCount > PathComponent.MaxVertices)
        {
            vertexCount = PathComponent.MaxVertices;
        }

        ReadOnlySpan<Vector2> posAll = PathComponentProperties.PositionLocalArray(_propertyWorld, handle);
        ReadOnlySpan<Vector2> tinAll = PathComponentProperties.TangentInLocalArray(_propertyWorld, handle);
        ReadOnlySpan<Vector2> toutAll = PathComponentProperties.TangentOutLocalArray(_propertyWorld, handle);
        ReadOnlySpan<int> kindAll = PathComponentProperties.VertexKindArray(_propertyWorld, handle);

        positionsLocal = posAll.Slice(0, vertexCount);
        tangentsInLocal = tinAll.Slice(0, vertexCount);
        tangentsOutLocal = toutAll.Slice(0, vertexCount);
        vertexKind = kindAll.Slice(0, vertexCount);
        return true;
    }

    private bool TryGetPolygonPointsLocalEcs(PathComponentHandle pathHandle, out ReadOnlySpan<Vector2> pointsLocal, out int pointCount)
    {
        pointsLocal = default;
        pointCount = 0;

        if (!TryGetPathArraysEcs(pathHandle, out ReadOnlySpan<Vector2> positionsLocal, out ReadOnlySpan<Vector2> tangentsInLocal, out ReadOnlySpan<Vector2> tangentsOutLocal, out ReadOnlySpan<int> vertexKind, out int vertexCount))
        {
            return false;
        }

        if (vertexCount < 3)
        {
            return false;
        }

        if (!PathHasAnyCurves(tangentsInLocal, tangentsOutLocal, vertexKind))
        {
            pointsLocal = positionsLocal;
            pointCount = vertexCount;
            return true;
        }

        int tessCount = TessellatePathToLocalScratch(positionsLocal, tangentsInLocal, tangentsOutLocal, vertexKind, vertexCount);
        if (tessCount < 3)
        {
            return false;
        }

        pointsLocal = _pathTessellationLocalScratch.AsSpan(0, tessCount);
        pointCount = tessCount;
        return true;
    }

    private Vector2 GetPolygonPivotLocalEcs(PathComponentHandle pathHandle, Vector2 anchor, Vector2 boundsMinLocal, Vector2 boundsSizeLocal)
    {
        if (!pathHandle.IsNull)
        {
            var view = PathComponent.Api.FromHandle(_propertyWorld, pathHandle);
            if (view.IsAlive)
            {
                return view.PivotLocal;
            }
        }

        return new Vector2(
            boundsMinLocal.X + anchor.X * boundsSizeLocal.X,
            boundsMinLocal.Y + anchor.Y * boundsSizeLocal.Y);
    }

    private void EmitBooleanOperandEcs(
        CanvasSdfDrawList draw,
        EntityId shapeEntity,
        Vector2 canvasOrigin,
        in WorldTransform parentWorldTransform,
        int paintLayerIndex,
        bool includeDistanceOnlyOperands,
        ref GroupEffectBounds effectBounds)
    {
        if (!TryGetShapeWorldTransformEcs(shapeEntity, parentWorldTransform, out ShapeWorldTransform worldTransform))
        {
            return;
        }

        ReadOnlySpan<EntityId> children = _world.GetChildren(shapeEntity);

        Vector2 scaleWorld = worldTransform.ScaleWorld;
        float scaleX = scaleWorld.X;
        float scaleY = scaleWorld.Y;
        float styleScale = (MathF.Abs(scaleX) + MathF.Abs(scaleY)) * 0.5f;
        if (styleScale <= 0.0001f)
        {
            styleScale = 1f;
        }

        float zoom = Zoom;
        float shapeOpacity = worldTransform.Opacity;

        bool useGradient = false;
        int gradientKind = PaintFillGradientKindLinear;
        Vector2 gradientDirection = new Vector2(1f, 0f);
        Vector2 gradientCenter = Vector2.Zero;
        float gradientRadiusScale = 1f;
        float gradientAngleOffset = 0f;

        uint startArgb = 0u;
        uint endArgb = 0u;
        int gradientStopCount = 0;
        int gradientStopStartIndex = -1;

        bool hasStroke = false;
        float strokeWidthCanvas = 0f;
        uint strokeArgb = 0u;
        bool strokeTrimEnabledValue = false;
        float strokeTrimStartValue = 0f;
        float strokeTrimLengthValue = 0f;
        float strokeTrimOffsetValue = 0f;
        int strokeTrimCapValue = 0;
        float strokeTrimCapSoftnessValue = 0f;

        bool hasStrokeDash = false;
        SdfStrokeDash strokeDash = default;
        bool hasStrokeTrim = false;
        SdfStrokeTrim strokeTrim = default;

	        float glowRadiusCanvas = 0f;

	        float shadowBlurCanvas = 0f;
	        float shadowOffsetX = 0f;
	        float shadowOffsetY = 0f;
	        SdfFeatherDirection shadowFeatherDirection = SdfFeatherDirection.Both;

        bool hasAnyStyle = false;

        PaintComponentHandle paintHandle = default;
        if (_world.TryGetComponent(shapeEntity, PaintComponent.Api.PoolIdConst, out AnyComponentHandle paintAny))
        {
            paintHandle = new PaintComponentHandle(paintAny.Index, paintAny.Generation);
            var paintView = PaintComponent.Api.FromHandle(_propertyWorld, paintHandle);
            if (paintView.IsAlive && paintView.LayerCount > 0 && (uint)paintLayerIndex < (uint)paintView.LayerCount)
            {
	                ReadOnlySpan<int> layerKind = PaintComponentProperties.LayerKindArray(_propertyWorld, paintHandle);
	                ReadOnlySpan<bool> layerIsVisible = PaintComponentProperties.LayerIsVisibleArray(_propertyWorld, paintHandle);
	                ReadOnlySpan<float> layerOpacityValues = PaintComponentProperties.LayerOpacityArray(_propertyWorld, paintHandle);
	                ReadOnlySpan<Vector2> layerOffsetWorld = PaintComponentProperties.LayerOffsetArray(_propertyWorld, paintHandle);
	                ReadOnlySpan<float> layerBlurWorld = PaintComponentProperties.LayerBlurArray(_propertyWorld, paintHandle);
	                ReadOnlySpan<int> layerBlurDirection = PaintComponentProperties.LayerBlurDirectionArray(_propertyWorld, paintHandle);

                bool isVisible = (uint)paintLayerIndex < (uint)layerIsVisible.Length && layerIsVisible[paintLayerIndex];
                if (isVisible)
                {
                    float layerOpacity = (uint)paintLayerIndex < (uint)layerOpacityValues.Length
                        ? shapeOpacity * Math.Clamp(layerOpacityValues[paintLayerIndex], 0f, 1f)
                        : shapeOpacity;
                    if (layerOpacity > 0.0001f)
                    {
                        int kindValue = (uint)paintLayerIndex < (uint)layerKind.Length ? layerKind[paintLayerIndex] : (int)PaintLayerKind.Fill;
	                        if (kindValue == (int)PaintLayerKind.Fill)
	                        {
	                            float blurWorld = (uint)paintLayerIndex < (uint)layerBlurWorld.Length ? layerBlurWorld[paintLayerIndex] : 0f;
	                            int blurDirectionValue = (uint)paintLayerIndex < (uint)layerBlurDirection.Length ? layerBlurDirection[paintLayerIndex] : 0;
	                            Vector2 offsetWorld = (uint)paintLayerIndex < (uint)layerOffsetWorld.Length ? layerOffsetWorld[paintLayerIndex] : Vector2.Zero;
	                            bool renderShadowOnly = blurWorld > 0.0001f || offsetWorld.X != 0f || offsetWorld.Y != 0f;
	                            if (renderShadowOnly)
	                            {
	                                shadowOffsetX = offsetWorld.X * zoom * styleScale;
	                                shadowOffsetY = offsetWorld.Y * zoom * styleScale;
	                                shadowBlurCanvas = Math.Max(0f, blurWorld) * zoom * styleScale;

	                                if (blurDirectionValue == 1)
	                                {
	                                    shadowFeatherDirection = SdfFeatherDirection.Outside;
	                                }
	                                else if (blurDirectionValue == 2)
	                                {
	                                    shadowFeatherDirection = SdfFeatherDirection.Inside;
	                                }
	                                else
	                                {
	                                    shadowFeatherDirection = SdfFeatherDirection.Both;
	                                }
	                            }

                            ReadOnlySpan<Color32> fillColor = PaintComponentProperties.FillColorArray(_propertyWorld, paintHandle);
                            ReadOnlySpan<bool> fillUseGradient = PaintComponentProperties.FillUseGradientArray(_propertyWorld, paintHandle);
                            ReadOnlySpan<Color32> fillGradientColorA = PaintComponentProperties.FillGradientColorAArray(_propertyWorld, paintHandle);
                            ReadOnlySpan<Color32> fillGradientColorB = PaintComponentProperties.FillGradientColorBArray(_propertyWorld, paintHandle);
                            ReadOnlySpan<int> fillGradientType = PaintComponentProperties.FillGradientTypeArray(_propertyWorld, paintHandle);
                            ReadOnlySpan<Vector2> fillGradientDirection = PaintComponentProperties.FillGradientDirectionArray(_propertyWorld, paintHandle);
                            ReadOnlySpan<Vector2> fillGradientCenter = PaintComponentProperties.FillGradientCenterArray(_propertyWorld, paintHandle);
                            ReadOnlySpan<float> fillGradientRadius = PaintComponentProperties.FillGradientRadiusArray(_propertyWorld, paintHandle);
                            ReadOnlySpan<float> fillGradientAngle = PaintComponentProperties.FillGradientAngleArray(_propertyWorld, paintHandle);

                            useGradient = (uint)paintLayerIndex < (uint)fillUseGradient.Length && fillUseGradient[paintLayerIndex];
                            if (useGradient)
                            {
                                gradientKind = (uint)paintLayerIndex < (uint)fillGradientType.Length
                                    ? fillGradientType[paintLayerIndex]
                                    : PaintFillGradientKindLinear;
                                gradientDirection = (uint)paintLayerIndex < (uint)fillGradientDirection.Length
                                    ? fillGradientDirection[paintLayerIndex]
                                    : new Vector2(1f, 0f);
                                gradientCenter = (uint)paintLayerIndex < (uint)fillGradientCenter.Length
                                    ? fillGradientCenter[paintLayerIndex]
                                    : Vector2.Zero;
                                gradientRadiusScale = (uint)paintLayerIndex < (uint)fillGradientRadius.Length
                                    ? fillGradientRadius[paintLayerIndex]
                                    : 1f;
                                gradientAngleOffset = (uint)paintLayerIndex < (uint)fillGradientAngle.Length
                                    ? fillGradientAngle[paintLayerIndex]
                                    : 0f;
                            }

                            if (useGradient)
                            {
                                gradientStopStartIndex = AddFillGradientStops(draw, paintHandle, paintLayerIndex, layerOpacity, out startArgb, out endArgb, out gradientStopCount);
                                if (gradientStopStartIndex < 0)
                                {
                                    Color32 startColor32 = (uint)paintLayerIndex < (uint)fillGradientColorA.Length
                                        ? ApplyTintAndOpacity(fillGradientColorA[paintLayerIndex], tint: Color32.White, layerOpacity)
                                        : default;
                                    Color32 endColor32 = (uint)paintLayerIndex < (uint)fillGradientColorB.Length
                                        ? ApplyTintAndOpacity(fillGradientColorB[paintLayerIndex], tint: Color32.White, layerOpacity)
                                        : default;
                                    startArgb = startColor32.A == 0 ? 0u : ToArgb(startColor32);
                                    endArgb = endColor32.A == 0 ? 0u : ToArgb(endColor32);
                                    gradientStopCount = 0;
                                }

                                hasAnyStyle = gradientStopStartIndex >= 0 || startArgb != 0u || endArgb != 0u;
                            }
                            else
                            {
                                Color32 solidColor = (uint)paintLayerIndex < (uint)fillColor.Length
                                    ? ApplyTintAndOpacity(fillColor[paintLayerIndex], tint: Color32.White, opacity: layerOpacity)
                                    : default;
                                startArgb = solidColor.A == 0 ? 0u : ToArgb(solidColor);
                                endArgb = 0u;
                                gradientStopCount = 0;
                                hasAnyStyle = startArgb != 0u;
                            }
                        }
                        else if (kindValue == (int)PaintLayerKind.Stroke)
                        {
                            ReadOnlySpan<float> strokeWidth = PaintComponentProperties.StrokeWidthArray(_propertyWorld, paintHandle);
                            ReadOnlySpan<Color32> strokeColor = PaintComponentProperties.StrokeColorArray(_propertyWorld, paintHandle);

                            float widthWorld = (uint)paintLayerIndex < (uint)strokeWidth.Length ? strokeWidth[paintLayerIndex] : 0f;
                            strokeWidthCanvas = Math.Max(0f, widthWorld) * zoom * styleScale;
                            Color32 strokeColor32 = (uint)paintLayerIndex < (uint)strokeColor.Length
                                ? ApplyTintAndOpacity(strokeColor[paintLayerIndex], tint: Color32.White, opacity: layerOpacity)
                                : default;
                            strokeArgb = strokeColor32.A == 0 ? 0u : ToArgb(strokeColor32);
                            hasStroke = strokeWidthCanvas > 0.0001f && strokeArgb != 0u;
                            if (hasStroke)
                            {
                                hasAnyStyle = true;

                                ReadOnlySpan<bool> strokeDashEnabled = PaintComponentProperties.StrokeDashEnabledArray(_propertyWorld, paintHandle);
                                ReadOnlySpan<float> strokeDashLength = PaintComponentProperties.StrokeDashLengthArray(_propertyWorld, paintHandle);
                                ReadOnlySpan<float> strokeDashGapLength = PaintComponentProperties.StrokeDashGapLengthArray(_propertyWorld, paintHandle);
                                ReadOnlySpan<float> strokeDashOffset = PaintComponentProperties.StrokeDashOffsetArray(_propertyWorld, paintHandle);
                                ReadOnlySpan<int> strokeDashCap = PaintComponentProperties.StrokeDashCapArray(_propertyWorld, paintHandle);
                                ReadOnlySpan<float> strokeDashCapSoftness = PaintComponentProperties.StrokeDashCapSoftnessArray(_propertyWorld, paintHandle);

                                bool dashEnabled = (uint)paintLayerIndex < (uint)strokeDashEnabled.Length && strokeDashEnabled[paintLayerIndex];
                                float dashLen = (uint)paintLayerIndex < (uint)strokeDashLength.Length ? strokeDashLength[paintLayerIndex] : 0f;
                                float dashGap = (uint)paintLayerIndex < (uint)strokeDashGapLength.Length ? strokeDashGapLength[paintLayerIndex] : 0f;
                                float dashOff = (uint)paintLayerIndex < (uint)strokeDashOffset.Length ? strokeDashOffset[paintLayerIndex] : 0f;
                                int dashCap = (uint)paintLayerIndex < (uint)strokeDashCap.Length ? strokeDashCap[paintLayerIndex] : 0;
                                float dashSoft = (uint)paintLayerIndex < (uint)strokeDashCapSoftness.Length ? strokeDashCapSoftness[paintLayerIndex] : 0f;

                                hasStrokeDash = TryGetStrokeDash(
                                    dashEnabled,
                                    dashLen,
                                    dashGap,
                                    dashOff,
                                    dashCap,
                                    dashSoft,
                                    zoom * styleScale,
                                    out strokeDash);

                                ReadOnlySpan<bool> strokeTrimEnabled = PaintComponentProperties.StrokeTrimEnabledArray(_propertyWorld, paintHandle);
                                ReadOnlySpan<float> strokeTrimStart = PaintComponentProperties.StrokeTrimStartArray(_propertyWorld, paintHandle);
                                ReadOnlySpan<float> strokeTrimLength = PaintComponentProperties.StrokeTrimLengthArray(_propertyWorld, paintHandle);
                                ReadOnlySpan<float> strokeTrimOffset = PaintComponentProperties.StrokeTrimOffsetArray(_propertyWorld, paintHandle);
                                ReadOnlySpan<int> strokeTrimCap = PaintComponentProperties.StrokeTrimCapArray(_propertyWorld, paintHandle);
                                ReadOnlySpan<float> strokeTrimCapSoftness = PaintComponentProperties.StrokeTrimCapSoftnessArray(_propertyWorld, paintHandle);

                                strokeTrimEnabledValue = (uint)paintLayerIndex < (uint)strokeTrimEnabled.Length && strokeTrimEnabled[paintLayerIndex];
                                strokeTrimStartValue = (uint)paintLayerIndex < (uint)strokeTrimStart.Length ? strokeTrimStart[paintLayerIndex] : 0f;
                                strokeTrimLengthValue = (uint)paintLayerIndex < (uint)strokeTrimLength.Length ? strokeTrimLength[paintLayerIndex] : 0f;
                                strokeTrimOffsetValue = (uint)paintLayerIndex < (uint)strokeTrimOffset.Length ? strokeTrimOffset[paintLayerIndex] : 0f;
                                strokeTrimCapValue = (uint)paintLayerIndex < (uint)strokeTrimCap.Length ? strokeTrimCap[paintLayerIndex] : 0;
                                strokeTrimCapSoftnessValue = (uint)paintLayerIndex < (uint)strokeTrimCapSoftness.Length ? strokeTrimCapSoftness[paintLayerIndex] : 0f;
                            }
                        }
                    }
                }
            }
        }

        bool shouldEmitDistanceOnly = !hasAnyStyle && includeDistanceOnlyOperands;
        bool shouldEmitThisShape = hasAnyStyle || shouldEmitDistanceOnly;

        if (hasAnyStyle)
        {
            effectBounds.Accumulate(strokeWidthCanvas, glowRadiusCanvas);
        }

        ShapeKind kind = GetShapeKindEcs(shapeEntity, out RectGeometryComponentHandle rectGeometryHandle, out CircleGeometryComponentHandle circleGeometryHandle, out PathComponentHandle pathHandle);
        float strokePathLengthCanvas = 0f;

        if (shouldEmitThisShape)
        {
            if (kind == ShapeKind.Polygon)
            {
                if (!TryGetPolygonPointsLocalEcs(pathHandle, out ReadOnlySpan<Vector2> pointsLocal, out int pointCount))
                {
                    goto EmitChildren;
                }

                GetBounds(pointsLocal, out float minLocalX, out float minLocalY, out float maxLocalX, out float maxLocalY);
                Vector2 boundsMinLocal = new Vector2(minLocalX, minLocalY);
                Vector2 boundsSizeLocal = new Vector2(maxLocalX - minLocalX, maxLocalY - minLocalY);

                Vector2 positionWorld = worldTransform.PositionWorld;
                Vector2 anchor = worldTransform.Anchor;
                float rotationRadians = worldTransform.RotationRadians;
                Vector2 pivotLocal = GetPolygonPivotLocalEcs(pathHandle, anchor, boundsMinLocal, boundsSizeLocal);

                EnsurePolygonScratchCapacity(pointCount + 1);
                float minCanvasX = float.MaxValue;
                float minCanvasY = float.MaxValue;
                float maxCanvasX = float.MinValue;
                float maxCanvasY = float.MinValue;

                for (int i = 0; i < pointCount; i++)
                {
                    Vector2 pWorld = TransformPolygonPointToWorld(positionWorld, pivotLocal, rotationRadians, scaleX, scaleY, pointsLocal[i]);
                    float canvasX = WorldToCanvasX(pWorld.X, canvasOrigin);
                    float canvasY = WorldToCanvasY(pWorld.Y, canvasOrigin);
                    _polygonCanvasScratch[i] = new Vector2(canvasX, canvasY);

                    if (canvasX < minCanvasX) minCanvasX = canvasX;
                    if (canvasY < minCanvasY) minCanvasY = canvasY;
                    if (canvasX > maxCanvasX) maxCanvasX = canvasX;
                    if (canvasY > maxCanvasY) maxCanvasY = canvasY;
                }

                _polygonCanvasScratch[pointCount] = _polygonCanvasScratch[0];
                ReadOnlySpan<Vector2> strokePoints = _polygonCanvasScratch.AsSpan(0, pointCount + 1);
                strokePathLengthCanvas = ComputePolylineLength(strokePoints);

                if (hasStroke)
                {
                    hasStrokeTrim = TryGetStrokeTrimPercent(
                        strokeTrimEnabledValue,
                        strokeTrimStartValue,
                        strokeTrimLengthValue,
                        strokeTrimOffsetValue,
                        strokeTrimCapValue,
                        strokeTrimCapSoftnessValue,
                        zoom * styleScale,
                        strokePathLengthCanvas,
                        out strokeTrim);
                }

                int headerIndex = draw.AddPolyline(strokePoints);
                Vector4 fillVec = shouldEmitDistanceOnly ? Vector4.Zero : ImStyle.ToVector4(startArgb);
                var cmd = SdfCommand.FilledPolygon(headerIndex, fillVec);
                if (!shouldEmitDistanceOnly && useGradient)
                {
                    cmd.Position = new Vector2((minCanvasX + maxCanvasX) * 0.5f, (minCanvasY + maxCanvasY) * 0.5f);
                    cmd.Size = new Vector2((maxCanvasX - minCanvasX) * 0.5f, (maxCanvasY - minCanvasY) * 0.5f);
                    cmd = WithPaintFillGradient(
                        cmd,
                        endArgb,
                        gradientKind,
                        gradientDirection,
                        gradientCenter,
                        gradientRadiusScale,
                        gradientAngleOffset,
                        rotationRadians,
                        polygonManualRotation: true);
                    if (gradientStopStartIndex >= 0 && gradientStopCount >= 2)
                    {
                        cmd = cmd.WithGradientStops(gradientStopStartIndex, gradientStopCount);
                    }
                }

                if (!shouldEmitDistanceOnly && strokeWidthCanvas > 0f && strokeArgb != 0u)
                {
                    cmd = cmd.WithStroke(ImStyle.ToVector4(strokeArgb), strokeWidthCanvas);
                    if (hasStrokeTrim)
                    {
                        cmd = cmd.WithStrokeTrim(in strokeTrim);
                    }
                    if (hasStrokeDash)
                    {
                        cmd = cmd.WithStrokeDash(in strokeDash);
                    }
                }

                if (!shouldEmitDistanceOnly && glowRadiusCanvas > 0f)
                {
                    cmd = cmd.WithGlow(glowRadiusCanvas);
                }

                bool pushedLayerOffset = false;
                bool pushedLayerFeather = false;
                if (!shouldEmitDistanceOnly)
                {
                    if (shadowOffsetX != 0f || shadowOffsetY != 0f)
                    {
                        draw.PushModifierOffset(shadowOffsetX, shadowOffsetY);
                        pushedLayerOffset = true;
                    }
	                    if (shadowBlurCanvas > 0.0001f)
	                    {
	                        draw.PushModifierFeather(shadowBlurCanvas, shadowFeatherDirection);
	                        pushedLayerFeather = true;
	                    }
                }

                draw.Add(cmd);

                if (pushedLayerFeather)
                {
                    draw.PopModifier();
                }
                if (pushedLayerOffset)
                {
                    draw.PopModifier();
                }
                goto EmitChildren;
            }

            ImRect rectCanvas = GetShapeRectCanvasForDrawEcs(shapeEntity, kind, rectGeometryHandle, circleGeometryHandle, canvasOrigin, worldTransform, out Vector2 centerCanvas, out float rotationOperand);
            Vector2 halfSize = new Vector2(rectCanvas.Width * 0.5f, rectCanvas.Height * 0.5f);

            if (kind == ShapeKind.Rect)
            {
                var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, rectGeometryHandle);
                float radiusScale = (MathF.Abs(scaleX) + MathF.Abs(scaleY)) * 0.5f;
                float radiusTL = Math.Max(0f, rectGeometry.CornerRadius.X) * radiusScale * zoom;
                float radiusTR = Math.Max(0f, rectGeometry.CornerRadius.Y) * radiusScale * zoom;
                float radiusBR = Math.Max(0f, rectGeometry.CornerRadius.Z) * radiusScale * zoom;
                float radiusBL = Math.Max(0f, rectGeometry.CornerRadius.W) * radiusScale * zoom;
                bool hasCornerRadius = radiusTL > 0f || radiusTR > 0f || radiusBR > 0f || radiusBL > 0f;
                strokePathLengthCanvas = ComputeRoundedRectPerimeter(rectCanvas.Width, rectCanvas.Height, radiusTL, radiusTR, radiusBR, radiusBL);
                if (hasStroke)
                {
                    hasStrokeTrim = TryGetStrokeTrimPercent(
                        strokeTrimEnabledValue,
                        strokeTrimStartValue,
                        strokeTrimLengthValue,
                        strokeTrimOffsetValue,
                        strokeTrimCapValue,
                        strokeTrimCapSoftnessValue,
                        zoom * styleScale,
                        strokePathLengthCanvas,
                        out strokeTrim);
                }

                Vector4 fillVec = shouldEmitDistanceOnly ? Vector4.Zero : ImStyle.ToVector4(startArgb);
                var cmd = hasCornerRadius
                    ? SdfCommand.RoundedRectPerCorner(centerCanvas, halfSize, radiusTL, radiusTR, radiusBR, radiusBL, fillVec)
                    : SdfCommand.Rect(centerCanvas, halfSize, fillVec);

                cmd = cmd.WithRotation(rotationOperand);
                if (!shouldEmitDistanceOnly && useGradient)
                {
                    cmd = WithPaintFillGradient(
                        cmd,
                        endArgb,
                        gradientKind,
                        gradientDirection,
                        gradientCenter,
                        gradientRadiusScale,
                        gradientAngleOffset,
                        polygonRotationRadians: 0f,
                        polygonManualRotation: false);
                    if (gradientStopStartIndex >= 0 && gradientStopCount >= 2)
                    {
                        cmd = cmd.WithGradientStops(gradientStopStartIndex, gradientStopCount);
                    }
                }

                if (!shouldEmitDistanceOnly && strokeWidthCanvas > 0f && strokeArgb != 0u)
                {
                    cmd = cmd.WithStroke(ImStyle.ToVector4(strokeArgb), strokeWidthCanvas);
                    if (hasStrokeTrim)
                    {
                        cmd = cmd.WithStrokeTrim(in strokeTrim);
                    }
                    if (hasStrokeDash)
                    {
                        cmd = cmd.WithStrokeDash(in strokeDash);
                    }
                }

                if (!shouldEmitDistanceOnly && glowRadiusCanvas > 0f)
                {
                    cmd = cmd.WithGlow(glowRadiusCanvas);
                }

                bool pushedLayerOffset = false;
                bool pushedLayerFeather = false;
                if (!shouldEmitDistanceOnly)
                {
                    if (shadowOffsetX != 0f || shadowOffsetY != 0f)
                    {
                        draw.PushModifierOffset(shadowOffsetX, shadowOffsetY);
                        pushedLayerOffset = true;
                    }
	                    if (shadowBlurCanvas > 0.0001f)
	                    {
	                        draw.PushModifierFeather(shadowBlurCanvas, shadowFeatherDirection);
	                        pushedLayerFeather = true;
	                    }
                }

                draw.Add(cmd);

                if (pushedLayerFeather)
                {
                    draw.PopModifier();
                }
	                if (pushedLayerOffset)
	                {
	                    draw.PopModifier();
	                }
	                goto EmitChildren;
	            }

	            {
	            float radius = Math.Min(rectCanvas.Width, rectCanvas.Height) * 0.5f;
	            strokePathLengthCanvas = radius > 0f ? (MathF.PI * 2f * radius) : 0f;
	            if (hasStroke)
	            {
	                hasStrokeTrim = TryGetStrokeTrimPercent(
	                    strokeTrimEnabledValue,
                    strokeTrimStartValue,
                    strokeTrimLengthValue,
                    strokeTrimOffsetValue,
                    strokeTrimCapValue,
                    strokeTrimCapSoftnessValue,
                    zoom * styleScale,
                    strokePathLengthCanvas,
                    out strokeTrim);
            }

            Vector4 circleFillVec = shouldEmitDistanceOnly ? Vector4.Zero : ImStyle.ToVector4(startArgb);
            var circleCmd = SdfCommand.Circle(centerCanvas, radius, circleFillVec)
                .WithRotation(rotationOperand);
            if (!shouldEmitDistanceOnly && useGradient)
            {
                circleCmd = WithPaintFillGradient(
                    circleCmd,
                    endArgb,
                    gradientKind,
                    gradientDirection,
                    gradientCenter,
                    gradientRadiusScale,
                    gradientAngleOffset,
                    polygonRotationRadians: 0f,
                    polygonManualRotation: false);
                if (gradientStopStartIndex >= 0 && gradientStopCount >= 2)
                {
                    circleCmd = circleCmd.WithGradientStops(gradientStopStartIndex, gradientStopCount);
                }
            }
            if (!shouldEmitDistanceOnly && strokeWidthCanvas > 0f && strokeArgb != 0u)
            {
                circleCmd = circleCmd.WithStroke(ImStyle.ToVector4(strokeArgb), strokeWidthCanvas);
                if (hasStrokeTrim)
                {
                    circleCmd = circleCmd.WithStrokeTrim(in strokeTrim);
                }
                if (hasStrokeDash)
                {
                    circleCmd = circleCmd.WithStrokeDash(in strokeDash);
                }
            }
            if (!shouldEmitDistanceOnly && glowRadiusCanvas > 0f)
            {
                circleCmd = circleCmd.WithGlow(glowRadiusCanvas);
            }
            bool pushedLayerOffset = false;
            bool pushedLayerFeather = false;
            if (!shouldEmitDistanceOnly)
            {
                if (shadowOffsetX != 0f || shadowOffsetY != 0f)
                {
                    draw.PushModifierOffset(shadowOffsetX, shadowOffsetY);
                    pushedLayerOffset = true;
                }
	                if (shadowBlurCanvas > 0.0001f)
	                {
	                    draw.PushModifierFeather(shadowBlurCanvas, shadowFeatherDirection);
	                    pushedLayerFeather = true;
	                }
            }

            draw.Add(circleCmd);

            if (pushedLayerFeather)
            {
                draw.PopModifier();
            }
	            if (pushedLayerOffset)
	            {
	                draw.PopModifier();
	            }
	            }
	        }

    EmitChildren:
        var shapeParentWorldTransform = new WorldTransform(
            worldTransform.PositionWorld,
            worldTransform.ScaleWorld,
            worldTransform.RotationRadians,
            worldTransform.IsVisible);

        for (int childIndex = 0; childIndex < children.Length; childIndex++)
        {
            EntityId child = children[childIndex];
            UiNodeType childType = _world.GetNodeType(child);
            if (childType == UiNodeType.BooleanGroup)
            {
                int pushedChildWarps = TryPushEntityWarpStackEcs(draw, child);
                BuildBooleanGroupEntityForLayer(draw, child, canvasOrigin, shapeParentWorldTransform, paintLayerIndex, ref effectBounds);
                for (int popIndex = 0; popIndex < pushedChildWarps; popIndex++)
                {
                    draw.PopWarp();
                }
            }
            else if (childType == UiNodeType.Shape)
            {
                int pushedChildWarps = TryPushEntityWarpStackEcs(draw, child);
                EmitBooleanOperandEcs(draw, child, canvasOrigin, shapeParentWorldTransform, paintLayerIndex, includeDistanceOnlyOperands, ref effectBounds);
                for (int popIndex = 0; popIndex < pushedChildWarps; popIndex++)
                {
                    draw.PopWarp();
                }
            }
        }
    }

    private Color32 GetFillShadowColor32(
        PaintComponentHandle paintHandle,
        int layerIndex,
        float opacity,
        ReadOnlySpan<bool> useGradient,
        ReadOnlySpan<Color32> fillColor,
        ReadOnlySpan<Color32> gradientA,
        ReadOnlySpan<Color32> gradientB,
        ReadOnlySpan<float> gradientMix)
    {
        Color32 color;
        if ((uint)layerIndex < (uint)useGradient.Length && useGradient[layerIndex])
        {
            float t = (uint)layerIndex < (uint)gradientMix.Length ? gradientMix[layerIndex] : 0.5f;
            int stopCount = GetPaintFillGradientStopCount(paintHandle, layerIndex);
            if (stopCount >= 2)
            {
                color = SamplePaintGradientAtT(paintHandle, layerIndex, stopCount, t);
            }
            else
            {
                Color32 a = (uint)layerIndex < (uint)gradientA.Length ? gradientA[layerIndex] : default;
                Color32 b = (uint)layerIndex < (uint)gradientB.Length ? gradientB[layerIndex] : default;
                color = UiColor32.LerpColor(a, b, t);
            }
        }
        else
        {
            color = (uint)layerIndex < (uint)fillColor.Length ? fillColor[layerIndex] : default;
        }

        return ApplyTintAndOpacity(color, tint: Color32.White, opacity);
    }

    private Color32 SamplePaintGradientAtT(PaintComponentHandle paintHandle, int layerIndex, int stopCount, float sampleT)
    {
        float t = Math.Clamp(sampleT, 0f, 1f);

        float prevT = 0f;
        Color32 prevColor = GetPaintFillGradientStopColor(paintHandle, layerIndex, 0);
        bool hasPrev = false;

        float nextT = 1f;
        Color32 nextColor = GetPaintFillGradientStopColor(paintHandle, layerIndex, stopCount - 1);
        bool hasNext = false;

        for (int i = 0; i < stopCount; i++)
        {
            float stopT = GetPaintFillGradientStopT(paintHandle, layerIndex, i);
            Color32 stopColor = GetPaintFillGradientStopColor(paintHandle, layerIndex, i);

            if (stopT <= t)
            {
                if (!hasPrev || stopT >= prevT)
                {
                    prevT = stopT;
                    prevColor = stopColor;
                    hasPrev = true;
                }
            }

            if (stopT >= t)
            {
                if (!hasNext || stopT <= nextT)
                {
                    nextT = stopT;
                    nextColor = stopColor;
                    hasNext = true;
                }
            }
        }

        if (!hasPrev)
        {
            prevT = nextT;
            prevColor = nextColor;
        }
        if (!hasNext)
        {
            nextT = prevT;
            nextColor = prevColor;
        }

        float denom = nextT - prevT;
        float lerpT = denom <= 0.0001f ? 0f : (t - prevT) / denom;
        return UiColor32.LerpColor(prevColor, nextColor, lerpT);
    }

    private ImRect GetShapeRectCanvasForDrawEcs(
        EntityId shapeEntity,
        ShapeKind kind,
        RectGeometryComponentHandle rectGeometryHandle,
        CircleGeometryComponentHandle circleGeometryHandle,
        Vector2 canvasOrigin,
        in ShapeWorldTransform worldTransform,
        out Vector2 centerCanvas,
        out float rotationRadians)
    {
        Vector2 anchor = worldTransform.Anchor;
        rotationRadians = worldTransform.RotationRadians;

        Vector2 rectSizeLocal = Vector2.Zero;
        if (kind == ShapeKind.Rect)
        {
            if (!TryGetComputedSize(shapeEntity, out rectSizeLocal) || rectSizeLocal.X <= 0f || rectSizeLocal.Y <= 0f)
            {
                var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, rectGeometryHandle);
                rectSizeLocal = rectGeometry.IsAlive ? rectGeometry.Size : Vector2.Zero;
            }
        }

        ImRect rectWorld = GetShapeRectWorldEcs(kind, circleGeometryHandle, worldTransform, rectSizeLocal, out float widthWorld, out float heightWorld);

        Vector2 anchorOffset = new Vector2(
            (anchor.X - 0.5f) * widthWorld,
            (anchor.Y - 0.5f) * heightWorld);

        Vector2 anchorPos = worldTransform.PositionWorld;
        Vector2 centerWorld = anchorPos - RotateVector(anchorOffset, rotationRadians);

        float centerCanvasX = WorldToCanvasX(centerWorld.X, canvasOrigin);
        float centerCanvasY = WorldToCanvasY(centerWorld.Y, canvasOrigin);

        float widthCanvas = widthWorld * Zoom;
        float heightCanvas = heightWorld * Zoom;

        centerCanvas = new Vector2(centerCanvasX, centerCanvasY);
        return new ImRect(centerCanvasX - widthCanvas * 0.5f, centerCanvasY - heightCanvas * 0.5f, widthCanvas, heightCanvas);
    }

    private ImRect GetShapeRectWorldEcs(
        ShapeKind kind,
        CircleGeometryComponentHandle circleGeometryHandle,
        in ShapeWorldTransform worldTransform,
        Vector2 rectSizeLocal,
        out float widthWorld,
        out float heightWorld)
    {
        Vector2 positionWorld = worldTransform.PositionWorld;
        Vector2 scale = worldTransform.ScaleWorld;

        float scaleX = scale.X == 0f ? 1f : scale.X;
        float scaleY = scale.Y == 0f ? 1f : scale.Y;

        Vector2 anchor = worldTransform.Anchor;
        float posX = positionWorld.X;
        float posY = positionWorld.Y;

        if (kind == ShapeKind.Rect)
        {
            widthWorld = rectSizeLocal.X * scaleX;
            heightWorld = rectSizeLocal.Y * scaleY;
            float x = posX - anchor.X * widthWorld;
            float y = posY - anchor.Y * heightWorld;
            return new ImRect(x, y, widthWorld, heightWorld);
        }

        var circleGeometry = CircleGeometryComponent.Api.FromHandle(_propertyWorld, circleGeometryHandle);
        float radius = circleGeometry.Radius;
        float scaleAvg = (scaleX + scaleY) * 0.5f;
        float diameter = radius * 2f * scaleAvg;
        widthWorld = diameter;
        heightWorld = diameter;
        float circleX = posX - anchor.X * diameter;
        float circleY = posY - anchor.Y * diameter;
        return new ImRect(circleX, circleY, diameter, diameter);
    }

    private void BuildTextPrimitiveEcs(CanvasSdfDrawList draw, EntityId textEntity, Vector2 canvasOrigin, in WorldTransform parentWorldTransform)
    {
        if (!TryGetTextWorldTransformEcs(textEntity, parentWorldTransform, out ShapeWorldTransform worldTransform))
        {
            return;
        }

        if (!worldTransform.IsVisible)
        {
            return;
        }

        if (!TryGetBlendStateEcs(textEntity, out bool isVisible, out float opacity, out PaintBlendMode textBlendMode))
        {
            return;
        }

        if (!isVisible || opacity <= 0.0001f)
        {
            return;
        }

        if (!_world.TryGetComponent(textEntity, RectGeometryComponent.Api.PoolIdConst, out AnyComponentHandle rectAny))
        {
            return;
        }

        if (!_world.TryGetComponent(textEntity, PaintComponent.Api.PoolIdConst, out AnyComponentHandle paintAny))
        {
            return;
        }

        if (!_world.TryGetComponent(textEntity, TextComponent.Api.PoolIdConst, out AnyComponentHandle textAny))
        {
            return;
        }

        var rectHandle = new RectGeometryComponentHandle(rectAny.Index, rectAny.Generation);
        var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, rectHandle);
        if (!rectGeometry.IsAlive)
        {
            return;
        }

        var paintHandle = new PaintComponentHandle(paintAny.Index, paintAny.Generation);
        var paintView = PaintComponent.Api.FromHandle(_propertyWorld, paintHandle);
        if (!paintView.IsAlive || paintView.LayerCount <= 0)
        {
            return;
        }

        var textHandle = new TextComponentHandle(textAny.Index, textAny.Generation);
        var text = TextComponent.Api.FromHandle(_propertyWorld, textHandle);
        if (!text.IsAlive)
        {
            return;
        }

        int layerCount = paintView.LayerCount;
        if (layerCount > PaintComponent.MaxLayers)
        {
            layerCount = PaintComponent.MaxLayers;
        }

        ReadOnlySpan<int> layerKind = PaintComponentProperties.LayerKindArray(_propertyWorld, paintHandle);
        ReadOnlySpan<bool> layerIsVisible = PaintComponentProperties.LayerIsVisibleArray(_propertyWorld, paintHandle);
        ReadOnlySpan<bool> layerInheritBlendMode = PaintComponentProperties.LayerInheritBlendModeArray(_propertyWorld, paintHandle);
        ReadOnlySpan<int> layerBlendMode = PaintComponentProperties.LayerBlendModeArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> layerOpacityValues = PaintComponentProperties.LayerOpacityArray(_propertyWorld, paintHandle);
        ReadOnlySpan<Vector2> layerOffsetWorld = PaintComponentProperties.LayerOffsetArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> layerBlurWorld = PaintComponentProperties.LayerBlurArray(_propertyWorld, paintHandle);
        ReadOnlySpan<int> layerBlurDirection = PaintComponentProperties.LayerBlurDirectionArray(_propertyWorld, paintHandle);

        ReadOnlySpan<Color32> fillColor = PaintComponentProperties.FillColorArray(_propertyWorld, paintHandle);
        ReadOnlySpan<bool> fillUseGradient = PaintComponentProperties.FillUseGradientArray(_propertyWorld, paintHandle);
        ReadOnlySpan<Color32> fillGradientColorA = PaintComponentProperties.FillGradientColorAArray(_propertyWorld, paintHandle);
        ReadOnlySpan<Color32> fillGradientColorB = PaintComponentProperties.FillGradientColorBArray(_propertyWorld, paintHandle);
        ReadOnlySpan<int> fillGradientType = PaintComponentProperties.FillGradientTypeArray(_propertyWorld, paintHandle);
        ReadOnlySpan<Vector2> fillGradientDirection = PaintComponentProperties.FillGradientDirectionArray(_propertyWorld, paintHandle);
        ReadOnlySpan<Vector2> fillGradientCenter = PaintComponentProperties.FillGradientCenterArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> fillGradientRadius = PaintComponentProperties.FillGradientRadiusArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> fillGradientAngle = PaintComponentProperties.FillGradientAngleArray(_propertyWorld, paintHandle);

        ReadOnlySpan<float> strokeWidth = PaintComponentProperties.StrokeWidthArray(_propertyWorld, paintHandle);
        ReadOnlySpan<Color32> strokeColor = PaintComponentProperties.StrokeColorArray(_propertyWorld, paintHandle);

        float zoom = Zoom;
        Vector2 scaleWorld = worldTransform.ScaleWorld;
        float scaleX = scaleWorld.X == 0f ? 1f : scaleWorld.X;
        float scaleY = scaleWorld.Y == 0f ? 1f : scaleWorld.Y;
        float styleScale = (MathF.Abs(scaleX) + MathF.Abs(scaleY)) * 0.5f;
        Vector2 rectSizeLocal = rectGeometry.Size;
        if (TryGetComputedSize(textEntity, out Vector2 computedSize) && computedSize.X > 0f && computedSize.Y > 0f)
        {
            rectSizeLocal = computedSize;
        }
        if (!TryEmitTextGlyphCommandsEcs(
                draw,
                textAny,
                text,
                rectSizeLocal,
                worldTransform,
                canvasOrigin,
                zoom,
                styleScale,
                out Vector2 centerCanvas,
                out Vector2 halfSizeCanvas,
                out float rotationDraw,
                out bool clip,
                out int firstGlyphIndex,
                out int glyphCount))
        {
            return;
        }

        Vector4 clipRect = clip
            ? new Vector4(centerCanvas.X - halfSizeCanvas.X, centerCanvas.Y - halfSizeCanvas.Y, halfSizeCanvas.X * 2f, halfSizeCanvas.Y * 2f)
            : default;

        uint textBlendModeValue = (uint)Math.Clamp((int)textBlendMode, (int)PaintBlendMode.Normal, (int)PaintBlendMode.Luminosity);

        for (int layerIndex = layerCount - 1; layerIndex >= 0; layerIndex--)
        {
            if ((uint)layerIndex >= (uint)layerIsVisible.Length || !layerIsVisible[layerIndex])
            {
                continue;
            }

            float layerOpacity = opacity * Math.Clamp(layerOpacityValues[layerIndex], 0f, 1f);
            if (layerOpacity <= 0.0001f)
            {
                continue;
            }

            uint blendModeValue = layerInheritBlendMode[layerIndex]
                ? textBlendModeValue
                : (uint)Math.Clamp(layerBlendMode[layerIndex], (int)PaintBlendMode.Normal, (int)PaintBlendMode.Luminosity);

            int kindValue = (uint)layerIndex < (uint)layerKind.Length ? layerKind[layerIndex] : (int)PaintLayerKind.Fill;

            Vector2 layerOffset = (uint)layerIndex < (uint)layerOffsetWorld.Length ? layerOffsetWorld[layerIndex] : Vector2.Zero;
            float layerBlur = (uint)layerIndex < (uint)layerBlurWorld.Length ? layerBlurWorld[layerIndex] : 0f;

            int blurDirectionValue = (uint)layerIndex < (uint)layerBlurDirection.Length ? layerBlurDirection[layerIndex] : 0;
            SdfFeatherDirection layerFeatherDirection;
            if (blurDirectionValue == 1)
            {
                layerFeatherDirection = SdfFeatherDirection.Outside;
            }
            else if (blurDirectionValue == 2)
            {
                layerFeatherDirection = SdfFeatherDirection.Inside;
            }
            else
            {
                layerFeatherDirection = SdfFeatherDirection.Both;
            }

            float layerOffsetX = layerOffset.X * zoom * styleScale;
            float layerOffsetY = layerOffset.Y * zoom * styleScale;
            float layerFeatherRadiusCanvas = Math.Max(0f, layerBlur) * zoom * styleScale;
            bool hasLayerBlur = layerFeatherRadiusCanvas > 0.0001f || layerOffsetX != 0f || layerOffsetY != 0f;

            bool pushedLayerOffset = false;
            bool pushedLayerFeather = false;
            if (hasLayerBlur)
            {
                if (layerOffsetX != 0f || layerOffsetY != 0f)
                {
                    draw.PushModifierOffset(layerOffsetX, layerOffsetY);
                    pushedLayerOffset = true;
                }

                if (layerFeatherRadiusCanvas > 0.0001f)
                {
                    draw.PushModifierFeather(layerFeatherRadiusCanvas, layerFeatherDirection);
                    pushedLayerFeather = true;
                }
            }

            if (kindValue == (int)PaintLayerKind.Fill)
            {
                bool useGradient = (uint)layerIndex < (uint)fillUseGradient.Length && fillUseGradient[layerIndex];
                int gradientKind = useGradient && (uint)layerIndex < (uint)fillGradientType.Length
                    ? fillGradientType[layerIndex]
                    : PaintFillGradientKindLinear;
                Vector2 gradientDirection = (uint)layerIndex < (uint)fillGradientDirection.Length
                    ? fillGradientDirection[layerIndex]
                    : new Vector2(1f, 0f);
                Vector2 gradientCenter = (uint)layerIndex < (uint)fillGradientCenter.Length
                    ? fillGradientCenter[layerIndex]
                    : Vector2.Zero;
                float gradientRadiusScale = (uint)layerIndex < (uint)fillGradientRadius.Length
                    ? fillGradientRadius[layerIndex]
                    : 1f;
                float gradientAngleOffset = (uint)layerIndex < (uint)fillGradientAngle.Length
                    ? fillGradientAngle[layerIndex]
                    : 0f;

                uint fillStartArgb;
                uint fillEndArgb;
                int gradientStopCount;
                int gradientStopStartIndex = -1;

                if (useGradient)
                {
                    gradientStopStartIndex = AddFillGradientStops(draw, paintHandle, layerIndex, layerOpacity, out fillStartArgb, out fillEndArgb, out gradientStopCount);
                    if (gradientStopStartIndex < 0)
                    {
                        Color32 startColor = (uint)layerIndex < (uint)fillGradientColorA.Length
                            ? ApplyTintAndOpacity(fillGradientColorA[layerIndex], tint: Color32.White, opacity: layerOpacity)
                            : default;
                        Color32 endColor = (uint)layerIndex < (uint)fillGradientColorB.Length
                            ? ApplyTintAndOpacity(fillGradientColorB[layerIndex], tint: Color32.White, opacity: layerOpacity)
                            : default;
                        fillStartArgb = startColor.A == 0 ? 0u : ToArgb(startColor);
                        fillEndArgb = endColor.A == 0 ? 0u : ToArgb(endColor);
                        gradientStopCount = 0;
                    }
                }
                else
                {
                    Color32 solidColor = (uint)layerIndex < (uint)fillColor.Length
                        ? ApplyTintAndOpacity(fillColor[layerIndex], tint: Color32.White, opacity: layerOpacity)
                        : default;
                    fillStartArgb = solidColor.A == 0 ? 0u : ToArgb(solidColor);
                    fillEndArgb = 0u;
                    gradientStopCount = 0;
                }

                bool hasFill = useGradient
                    ? (gradientStopStartIndex >= 0 || fillStartArgb != 0u || fillEndArgb != 0u)
                    : fillStartArgb != 0u;
                if (hasFill)
                {
                    var cmd = SdfCommand.TextGroup(centerCanvas, halfSizeCanvas, firstGlyphIndex, glyphCount, ImStyle.ToVector4(fillStartArgb))
                        .WithBlendMode(blendModeValue)
                        .WithRotation(rotationDraw);

                    if (useGradient)
                    {
                        cmd = WithPaintFillGradient(
                            cmd,
                            fillEndArgb,
                            gradientKind,
                            gradientDirection,
                            gradientCenter,
                            gradientRadiusScale,
                            gradientAngleOffset,
                            polygonRotationRadians: 0f,
                            polygonManualRotation: false);
                        if (gradientStopStartIndex >= 0 && gradientStopCount >= 2)
                        {
                            cmd = cmd.WithGradientStops(gradientStopStartIndex, gradientStopCount);
                        }
                    }

                    if (clip)
                    {
                        cmd = cmd.WithClip(clipRect);
                    }

                    draw.Add(cmd);
                }
            }
            else if (kindValue == (int)PaintLayerKind.Stroke)
            {
                float strokeWidthWorld = (uint)layerIndex < (uint)strokeWidth.Length ? strokeWidth[layerIndex] : 0f;
                if (strokeWidthWorld > 0.0001f)
                {
                    float strokeWidthCanvas = strokeWidthWorld * zoom * styleScale;

                    bool useGradient = (uint)layerIndex < (uint)fillUseGradient.Length && fillUseGradient[layerIndex];
                    int gradientKind = useGradient && (uint)layerIndex < (uint)fillGradientType.Length
                        ? fillGradientType[layerIndex]
                        : PaintFillGradientKindLinear;
                    Vector2 gradientDirection = (uint)layerIndex < (uint)fillGradientDirection.Length
                        ? fillGradientDirection[layerIndex]
                        : new Vector2(1f, 0f);
                    Vector2 gradientCenter = (uint)layerIndex < (uint)fillGradientCenter.Length
                        ? fillGradientCenter[layerIndex]
                        : Vector2.Zero;
                    float gradientRadiusScale = (uint)layerIndex < (uint)fillGradientRadius.Length
                        ? fillGradientRadius[layerIndex]
                        : 1f;
                    float gradientAngleOffset = (uint)layerIndex < (uint)fillGradientAngle.Length
                        ? fillGradientAngle[layerIndex]
                        : 0f;

                    uint strokeStartArgb;
                    uint strokeEndArgb;
                    int gradientStopCount;
                    int gradientStopStartIndex = -1;

                    if (useGradient)
                    {
                        gradientStopStartIndex = AddFillGradientStops(draw, paintHandle, layerIndex, layerOpacity, out strokeStartArgb, out strokeEndArgb, out gradientStopCount);
                        if (gradientStopStartIndex < 0)
                        {
                            Color32 startColor = (uint)layerIndex < (uint)fillGradientColorA.Length
                                ? ApplyTintAndOpacity(fillGradientColorA[layerIndex], tint: Color32.White, opacity: layerOpacity)
                                : default;
                            Color32 endColor = (uint)layerIndex < (uint)fillGradientColorB.Length
                                ? ApplyTintAndOpacity(fillGradientColorB[layerIndex], tint: Color32.White, opacity: layerOpacity)
                                : default;
                            strokeStartArgb = startColor.A == 0 ? 0u : ToArgb(startColor);
                            strokeEndArgb = endColor.A == 0 ? 0u : ToArgb(endColor);
                            gradientStopCount = 0;
                        }
                    }
                    else
                    {
                        Color32 strokeColor32 = (uint)layerIndex < (uint)strokeColor.Length
                            ? ApplyTintAndOpacity(strokeColor[layerIndex], tint: Color32.White, opacity: layerOpacity)
                            : default;
                        strokeStartArgb = strokeColor32.A == 0 ? 0u : ToArgb(strokeColor32);
                        strokeEndArgb = 0u;
                        gradientStopCount = 0;
                    }

                    bool hasStroke = useGradient
                        ? (gradientStopStartIndex >= 0 || strokeStartArgb != 0u || strokeEndArgb != 0u)
                        : strokeStartArgb != 0u;
                    if (hasStroke)
                    {
                        var cmd = SdfCommand.TextGroup(centerCanvas, halfSizeCanvas, firstGlyphIndex, glyphCount, Vector4.Zero)
                            .WithBlendMode(blendModeValue)
                            .WithRotation(rotationDraw)
                            .WithStroke(ImStyle.ToVector4(strokeStartArgb), strokeWidthCanvas);

                        if (useGradient)
                        {
                            cmd = WithPaintFillGradient(
                                cmd,
                                strokeEndArgb,
                                gradientKind,
                                gradientDirection,
                                gradientCenter,
                                gradientRadiusScale,
                                gradientAngleOffset,
                                polygonRotationRadians: 0f,
                                polygonManualRotation: false);
                            if (gradientStopStartIndex >= 0 && gradientStopCount >= 2)
                            {
                                cmd = cmd.WithGradientStops(gradientStopStartIndex, gradientStopCount);
                            }
                        }

                        if (clip)
                        {
                            cmd = cmd.WithClip(clipRect);
                        }

                        draw.Add(cmd);
                    }
                }
            }

            if (pushedLayerFeather)
            {
                draw.PopModifier();
            }
            if (pushedLayerOffset)
            {
                draw.PopModifier();
            }
        }
    }

    private void BuildShapePrimitiveEcs(CanvasSdfDrawList draw, EntityId shapeEntity, Vector2 canvasOrigin, in WorldTransform parentWorldTransform)
    {
        if (!TryGetShapeWorldTransformEcs(shapeEntity, parentWorldTransform, out ShapeWorldTransform worldTransform))
        {
            return;
        }

        Vector2 positionWorld = worldTransform.PositionWorld;
        Vector2 scaleWorld = worldTransform.ScaleWorld;
        float scaleX = scaleWorld.X;
        float scaleY = scaleWorld.Y;
        float styleScale = (MathF.Abs(scaleX) + MathF.Abs(scaleY)) * 0.5f;
        if (styleScale <= 0.0001f)
        {
            styleScale = 1f;
        }

        float rotationRadians = worldTransform.RotationRadians;
        float zoom = Zoom;
        float shapeOpacity = worldTransform.Opacity;
        PaintBlendMode shapeBlendMode = worldTransform.BlendMode;
        uint shapeBlendModeValue = (uint)shapeBlendMode;

        if (!_world.TryGetComponent(shapeEntity, PaintComponent.Api.PoolIdConst, out AnyComponentHandle paintAny))
        {
            return;
        }

        var paintHandle = new PaintComponentHandle(paintAny.Index, paintAny.Generation);
        var paintView = PaintComponent.Api.FromHandle(_propertyWorld, paintHandle);
        if (!paintView.IsAlive)
        {
            return;
        }

        int layerCount = paintView.LayerCount;
        if (layerCount <= 0)
        {
            return;
        }

        ReadOnlySpan<int> layerKind = PaintComponentProperties.LayerKindArray(_propertyWorld, paintHandle);
        ReadOnlySpan<bool> layerIsVisible = PaintComponentProperties.LayerIsVisibleArray(_propertyWorld, paintHandle);
        ReadOnlySpan<bool> layerInheritBlendMode = PaintComponentProperties.LayerInheritBlendModeArray(_propertyWorld, paintHandle);
        ReadOnlySpan<int> layerBlendMode = PaintComponentProperties.LayerBlendModeArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> layerOpacityValues = PaintComponentProperties.LayerOpacityArray(_propertyWorld, paintHandle);
        ReadOnlySpan<Vector2> layerOffsetWorld = PaintComponentProperties.LayerOffsetArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> layerBlurWorld = PaintComponentProperties.LayerBlurArray(_propertyWorld, paintHandle);
        ReadOnlySpan<int> layerBlurDirection = PaintComponentProperties.LayerBlurDirectionArray(_propertyWorld, paintHandle);

        ReadOnlySpan<Color32> fillColor = PaintComponentProperties.FillColorArray(_propertyWorld, paintHandle);
        ReadOnlySpan<bool> fillUseGradient = PaintComponentProperties.FillUseGradientArray(_propertyWorld, paintHandle);
        ReadOnlySpan<Color32> fillGradientColorA = PaintComponentProperties.FillGradientColorAArray(_propertyWorld, paintHandle);
        ReadOnlySpan<Color32> fillGradientColorB = PaintComponentProperties.FillGradientColorBArray(_propertyWorld, paintHandle);
        ReadOnlySpan<int> fillGradientType = PaintComponentProperties.FillGradientTypeArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> fillGradientMix = PaintComponentProperties.FillGradientMixArray(_propertyWorld, paintHandle);
        ReadOnlySpan<Vector2> fillGradientDirection = PaintComponentProperties.FillGradientDirectionArray(_propertyWorld, paintHandle);
        ReadOnlySpan<Vector2> fillGradientCenter = PaintComponentProperties.FillGradientCenterArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> fillGradientRadius = PaintComponentProperties.FillGradientRadiusArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> fillGradientAngle = PaintComponentProperties.FillGradientAngleArray(_propertyWorld, paintHandle);

        ReadOnlySpan<float> strokeWidth = PaintComponentProperties.StrokeWidthArray(_propertyWorld, paintHandle);
        ReadOnlySpan<Color32> strokeColor = PaintComponentProperties.StrokeColorArray(_propertyWorld, paintHandle);
        ReadOnlySpan<bool> strokeDashEnabled = PaintComponentProperties.StrokeDashEnabledArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> strokeDashLength = PaintComponentProperties.StrokeDashLengthArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> strokeDashGapLength = PaintComponentProperties.StrokeDashGapLengthArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> strokeDashOffset = PaintComponentProperties.StrokeDashOffsetArray(_propertyWorld, paintHandle);
        ReadOnlySpan<int> strokeDashCap = PaintComponentProperties.StrokeDashCapArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> strokeDashCapSoftness = PaintComponentProperties.StrokeDashCapSoftnessArray(_propertyWorld, paintHandle);
        ReadOnlySpan<bool> strokeTrimEnabled = PaintComponentProperties.StrokeTrimEnabledArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> strokeTrimStart = PaintComponentProperties.StrokeTrimStartArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> strokeTrimLength = PaintComponentProperties.StrokeTrimLengthArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> strokeTrimOffset = PaintComponentProperties.StrokeTrimOffsetArray(_propertyWorld, paintHandle);
        ReadOnlySpan<int> strokeTrimCap = PaintComponentProperties.StrokeTrimCapArray(_propertyWorld, paintHandle);
        ReadOnlySpan<float> strokeTrimCapSoftness = PaintComponentProperties.StrokeTrimCapSoftnessArray(_propertyWorld, paintHandle);

        ShapeKind kind = GetShapeKindEcs(shapeEntity, out RectGeometryComponentHandle rectGeometryHandle, out CircleGeometryComponentHandle circleGeometryHandle, out PathComponentHandle pathHandle);

        int polygonPointCount = 0;
        ReadOnlySpan<Vector2> polygonStrokePoints = default;
        float polygonMinCanvasX = 0f;
        float polygonMinCanvasY = 0f;
        float polygonMaxCanvasX = 0f;
        float polygonMaxCanvasY = 0f;

        ImRect rectCanvas = default;
        Vector2 centerCanvas = default;
        float rotationDraw = 0f;
        Vector2 halfSizeCanvas = default;
        float circleRadiusCanvas = 0f;
        bool hasCornerRadius = false;
        float radiusTL = 0f;
        float radiusTR = 0f;
        float radiusBR = 0f;
        float radiusBL = 0f;
        float strokePathLengthCanvas = 0f;

        if (kind == ShapeKind.Polygon)
        {
            if (!TryGetPolygonPointsLocalEcs(pathHandle, out ReadOnlySpan<Vector2> pointsLocal, out int pointCount))
            {
                return;
            }

            GetBounds(pointsLocal, out float minLocalX, out float minLocalY, out float maxLocalX, out float maxLocalY);
            Vector2 boundsMinLocal = new Vector2(minLocalX, minLocalY);
            Vector2 boundsSizeLocal = new Vector2(maxLocalX - minLocalX, maxLocalY - minLocalY);
            Vector2 anchor = worldTransform.Anchor;
            Vector2 pivotLocal = GetPolygonPivotLocalEcs(pathHandle, anchor, boundsMinLocal, boundsSizeLocal);

            polygonPointCount = pointCount;
            EnsurePolygonScratchCapacity(polygonPointCount + 1);
            polygonMinCanvasX = float.MaxValue;
            polygonMinCanvasY = float.MaxValue;
            polygonMaxCanvasX = float.MinValue;
            polygonMaxCanvasY = float.MinValue;

            for (int i = 0; i < polygonPointCount; i++)
            {
                Vector2 pWorld = TransformPolygonPointToWorld(positionWorld, pivotLocal, rotationRadians, scaleX, scaleY, pointsLocal[i]);
                float canvasX = WorldToCanvasX(pWorld.X, canvasOrigin);
                float canvasY = WorldToCanvasY(pWorld.Y, canvasOrigin);
                _polygonCanvasScratch[i] = new Vector2(canvasX, canvasY);

                if (canvasX < polygonMinCanvasX) polygonMinCanvasX = canvasX;
                if (canvasY < polygonMinCanvasY) polygonMinCanvasY = canvasY;
                if (canvasX > polygonMaxCanvasX) polygonMaxCanvasX = canvasX;
                if (canvasY > polygonMaxCanvasY) polygonMaxCanvasY = canvasY;
            }

            _polygonCanvasScratch[polygonPointCount] = _polygonCanvasScratch[0];
            polygonStrokePoints = _polygonCanvasScratch.AsSpan(0, polygonPointCount + 1);
            strokePathLengthCanvas = ComputePolylineLength(polygonStrokePoints);
        }
        else
        {
            rectCanvas = GetShapeRectCanvasForDrawEcs(shapeEntity, kind, rectGeometryHandle, circleGeometryHandle, canvasOrigin, worldTransform, out centerCanvas, out rotationDraw);
            halfSizeCanvas = new Vector2(rectCanvas.Width * 0.5f, rectCanvas.Height * 0.5f);
            circleRadiusCanvas = Math.Min(rectCanvas.Width, rectCanvas.Height) * 0.5f;

            if (kind == ShapeKind.Rect)
            {
                var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, rectGeometryHandle);
                float radiusScale = (MathF.Abs(scaleX) + MathF.Abs(scaleY)) * 0.5f;
                radiusTL = Math.Max(0f, rectGeometry.CornerRadius.X) * radiusScale * zoom;
                radiusTR = Math.Max(0f, rectGeometry.CornerRadius.Y) * radiusScale * zoom;
                radiusBR = Math.Max(0f, rectGeometry.CornerRadius.Z) * radiusScale * zoom;
                radiusBL = Math.Max(0f, rectGeometry.CornerRadius.W) * radiusScale * zoom;
                hasCornerRadius = radiusTL > 0f || radiusTR > 0f || radiusBR > 0f || radiusBL > 0f;
                strokePathLengthCanvas = ComputeRoundedRectPerimeter(rectCanvas.Width, rectCanvas.Height, radiusTL, radiusTR, radiusBR, radiusBL);
            }
            else
            {
                strokePathLengthCanvas = circleRadiusCanvas > 0f ? (MathF.PI * 2f * circleRadiusCanvas) : 0f;
            }
        }

        // Paint stack order: index 0 is topmost, so draw from bottom -> top.
        for (int layerIndex = layerCount - 1; layerIndex >= 0; layerIndex--)
        {
            if ((uint)layerIndex >= (uint)layerIsVisible.Length || !layerIsVisible[layerIndex])
            {
                continue;
            }

            float layerOpacity = shapeOpacity * Math.Clamp(layerOpacityValues[layerIndex], 0f, 1f);
            if (layerOpacity <= 0.0001f)
            {
                continue;
            }

            uint blendModeValue = layerInheritBlendMode[layerIndex]
                ? shapeBlendModeValue
                : (uint)Math.Clamp(layerBlendMode[layerIndex], (int)PaintBlendMode.Normal, (int)PaintBlendMode.Luminosity);

            int kindValue = (uint)layerIndex < (uint)layerKind.Length ? layerKind[layerIndex] : (int)PaintLayerKind.Fill;
            if (kindValue == (int)PaintLayerKind.Fill)
            {
                bool useGradient = fillUseGradient[layerIndex];
                int gradientKind = useGradient ? fillGradientType[layerIndex] : PaintFillGradientKindLinear;
                Vector2 gradientDirection = fillGradientDirection[layerIndex];
                Vector2 gradientCenter = fillGradientCenter[layerIndex];
                float gradientRadiusScale = fillGradientRadius[layerIndex];
                float gradientAngleOffset = fillGradientAngle[layerIndex];

                uint fillStartArgb;
                uint fillEndArgb;
                int gradientStopCount;
                int gradientStopStartIndex = -1;

                if (useGradient)
                {
                    gradientStopStartIndex = AddFillGradientStops(draw, paintHandle, layerIndex, layerOpacity, out fillStartArgb, out fillEndArgb, out gradientStopCount);
                    if (gradientStopStartIndex < 0)
                    {
                        Color32 startColor = ApplyTintAndOpacity(fillGradientColorA[layerIndex], tint: Color32.White, opacity: layerOpacity);
                        Color32 endColor = ApplyTintAndOpacity(fillGradientColorB[layerIndex], tint: Color32.White, opacity: layerOpacity);
                        fillStartArgb = startColor.A == 0 ? 0u : ToArgb(startColor);
                        fillEndArgb = endColor.A == 0 ? 0u : ToArgb(endColor);
                        gradientStopCount = 0;
                    }
                }
                else
                {
                    Color32 solidColor = ApplyTintAndOpacity(fillColor[layerIndex], tint: Color32.White, opacity: layerOpacity);
                    fillStartArgb = solidColor.A == 0 ? 0u : ToArgb(solidColor);
                    fillEndArgb = 0u;
                    gradientStopCount = 0;
                }

                bool hasFill = useGradient
                    ? (gradientStopStartIndex >= 0 || fillStartArgb != 0u || fillEndArgb != 0u)
                    : fillStartArgb != 0u;
                if (!hasFill)
                {
                    continue;
                }

	                Vector2 layerOffset = layerOffsetWorld[layerIndex];
	                float layerBlur = layerBlurWorld[layerIndex];
	                int blurDirectionValue = (uint)layerIndex < (uint)layerBlurDirection.Length ? layerBlurDirection[layerIndex] : 0;
	                SdfFeatherDirection layerFeatherDirection;
	                if (blurDirectionValue == 1)
	                {
	                    layerFeatherDirection = SdfFeatherDirection.Outside;
	                }
	                else if (blurDirectionValue == 2)
	                {
	                    layerFeatherDirection = SdfFeatherDirection.Inside;
	                }
	                else
	                {
	                    layerFeatherDirection = SdfFeatherDirection.Both;
	                }

	                float shadowOffsetX = layerOffset.X * zoom * styleScale;
	                float shadowOffsetY = layerOffset.Y * zoom * styleScale;
	                float shadowBlurCanvas = Math.Max(0f, layerBlur) * zoom * styleScale;
	                bool hasLayerBlur = shadowBlurCanvas > 0.0001f || shadowOffsetX != 0f || shadowOffsetY != 0f;

                if (kind == ShapeKind.Polygon)
                {
                    int headerIndex = draw.AddPolyline(polygonStrokePoints);
                    var cmd = SdfCommand.FilledPolygon(headerIndex, ImStyle.ToVector4(fillStartArgb))
                        .WithBlendMode(blendModeValue);

                    if (useGradient)
                    {
                        cmd.Position = new Vector2((polygonMinCanvasX + polygonMaxCanvasX) * 0.5f, (polygonMinCanvasY + polygonMaxCanvasY) * 0.5f);
                        cmd.Size = new Vector2((polygonMaxCanvasX - polygonMinCanvasX) * 0.5f, (polygonMaxCanvasY - polygonMinCanvasY) * 0.5f);
                        cmd = WithPaintFillGradient(
                            cmd,
                            fillEndArgb,
                            gradientKind,
                            gradientDirection,
                            gradientCenter,
                            gradientRadiusScale,
                            gradientAngleOffset,
                            rotationRadians,
                            polygonManualRotation: true);
                        if (gradientStopStartIndex >= 0 && gradientStopCount >= 2)
                        {
                            cmd = cmd.WithGradientStops(gradientStopStartIndex, gradientStopCount);
                        }
                    }

                    bool pushedLayerOffset = false;
                    bool pushedLayerFeather = false;
                    if (hasLayerBlur)
                    {
                        if (shadowOffsetX != 0f || shadowOffsetY != 0f)
                        {
                            draw.PushModifierOffset(shadowOffsetX, shadowOffsetY);
                            pushedLayerOffset = true;
                        }
	                        if (shadowBlurCanvas > 0.0001f)
	                        {
	                            draw.PushModifierFeather(shadowBlurCanvas, layerFeatherDirection);
	                            pushedLayerFeather = true;
	                        }
                    }

                    draw.Add(cmd);

                    if (pushedLayerFeather)
                    {
                        draw.PopModifier();
                    }
                    if (pushedLayerOffset)
                    {
                        draw.PopModifier();
                    }
                    continue;
                }

                if (kind == ShapeKind.Rect)
                {
                    var cmd = hasCornerRadius
                        ? SdfCommand.RoundedRectPerCorner(centerCanvas, halfSizeCanvas, radiusTL, radiusTR, radiusBR, radiusBL, ImStyle.ToVector4(fillStartArgb))
                        : SdfCommand.Rect(centerCanvas, halfSizeCanvas, ImStyle.ToVector4(fillStartArgb));

                    cmd = cmd.WithRotation(rotationDraw).WithBlendMode(blendModeValue);

                    if (useGradient)
                    {
                        cmd = WithPaintFillGradient(
                            cmd,
                            fillEndArgb,
                            gradientKind,
                            gradientDirection,
                            gradientCenter,
                            gradientRadiusScale,
                            gradientAngleOffset,
                            polygonRotationRadians: 0f,
                            polygonManualRotation: false);
                        if (gradientStopStartIndex >= 0 && gradientStopCount >= 2)
                        {
                            cmd = cmd.WithGradientStops(gradientStopStartIndex, gradientStopCount);
                        }
                    }

                    bool pushedLayerOffset = false;
                    bool pushedLayerFeather = false;
                    if (hasLayerBlur)
                    {
                        if (shadowOffsetX != 0f || shadowOffsetY != 0f)
                        {
                            draw.PushModifierOffset(shadowOffsetX, shadowOffsetY);
                            pushedLayerOffset = true;
                        }
	                        if (shadowBlurCanvas > 0.0001f)
	                        {
	                            draw.PushModifierFeather(shadowBlurCanvas, layerFeatherDirection);
	                            pushedLayerFeather = true;
	                        }
                    }

                    draw.Add(cmd);

                    if (pushedLayerFeather)
                    {
                        draw.PopModifier();
                    }
                    if (pushedLayerOffset)
                    {
                        draw.PopModifier();
                    }
                    continue;
                }

	                {
	                var circleCmd = SdfCommand.Circle(centerCanvas, circleRadiusCanvas, ImStyle.ToVector4(fillStartArgb))
	                    .WithRotation(rotationDraw)
	                    .WithBlendMode(blendModeValue);

                if (useGradient)
                {
                    circleCmd = WithPaintFillGradient(
                        circleCmd,
                        fillEndArgb,
                        gradientKind,
                        gradientDirection,
                        gradientCenter,
                        gradientRadiusScale,
                        gradientAngleOffset,
                        polygonRotationRadians: 0f,
                        polygonManualRotation: false);
                    if (gradientStopStartIndex >= 0 && gradientStopCount >= 2)
                    {
                        circleCmd = circleCmd.WithGradientStops(gradientStopStartIndex, gradientStopCount);
                    }
                }

                bool pushedLayerOffset = false;
                bool pushedLayerFeather = false;
                if (hasLayerBlur)
                {
                    if (shadowOffsetX != 0f || shadowOffsetY != 0f)
                    {
                        draw.PushModifierOffset(shadowOffsetX, shadowOffsetY);
                        pushedLayerOffset = true;
                    }
	                    if (shadowBlurCanvas > 0.0001f)
	                    {
	                        draw.PushModifierFeather(shadowBlurCanvas, layerFeatherDirection);
	                        pushedLayerFeather = true;
	                    }
                }

                draw.Add(circleCmd);

                if (pushedLayerFeather)
                {
                    draw.PopModifier();
                }
	                if (pushedLayerOffset)
	                {
	                    draw.PopModifier();
	                }
	                continue;
	                }
	            }

            if (kindValue == (int)PaintLayerKind.Stroke)
            {
                float strokeWidthWorld = strokeWidth[layerIndex];
                if (strokeWidthWorld <= 0.0001f)
                {
                    continue;
                }

                float strokeWidthCanvas = strokeWidthWorld * zoom * styleScale;
                bool useGradient = (uint)layerIndex < (uint)fillUseGradient.Length && fillUseGradient[layerIndex];
                int gradientKind = useGradient && (uint)layerIndex < (uint)fillGradientType.Length
                    ? fillGradientType[layerIndex]
                    : PaintFillGradientKindLinear;
                Vector2 gradientDirection = (uint)layerIndex < (uint)fillGradientDirection.Length
                    ? fillGradientDirection[layerIndex]
                    : new Vector2(1f, 0f);
                Vector2 gradientCenter = (uint)layerIndex < (uint)fillGradientCenter.Length
                    ? fillGradientCenter[layerIndex]
                    : Vector2.Zero;
                float gradientRadiusScale = (uint)layerIndex < (uint)fillGradientRadius.Length
                    ? fillGradientRadius[layerIndex]
                    : 1f;
                float gradientAngleOffset = (uint)layerIndex < (uint)fillGradientAngle.Length
                    ? fillGradientAngle[layerIndex]
                    : 0f;

                uint strokeStartArgb;
                uint strokeEndArgb;
                int gradientStopCount;
                int gradientStopStartIndex = -1;

                if (useGradient)
                {
                    gradientStopStartIndex = AddFillGradientStops(draw, paintHandle, layerIndex, layerOpacity, out strokeStartArgb, out strokeEndArgb, out gradientStopCount);
                    if (gradientStopStartIndex < 0)
                    {
                        Color32 startColor = (uint)layerIndex < (uint)fillGradientColorA.Length
                            ? ApplyTintAndOpacity(fillGradientColorA[layerIndex], tint: Color32.White, opacity: layerOpacity)
                            : default;
                        Color32 endColor = (uint)layerIndex < (uint)fillGradientColorB.Length
                            ? ApplyTintAndOpacity(fillGradientColorB[layerIndex], tint: Color32.White, opacity: layerOpacity)
                            : default;
                        strokeStartArgb = startColor.A == 0 ? 0u : ToArgb(startColor);
                        strokeEndArgb = endColor.A == 0 ? 0u : ToArgb(endColor);
                        gradientStopCount = 0;
                    }
                }
                else
                {
                    Color32 strokeColor32 = (uint)layerIndex < (uint)strokeColor.Length
                        ? ApplyTintAndOpacity(strokeColor[layerIndex], tint: Color32.White, opacity: layerOpacity)
                        : default;
                    strokeStartArgb = strokeColor32.A == 0 ? 0u : ToArgb(strokeColor32);
                    strokeEndArgb = 0u;
                    gradientStopCount = 0;
                }

                bool hasStroke = useGradient
                    ? (gradientStopStartIndex >= 0 || strokeStartArgb != 0u || strokeEndArgb != 0u)
                    : strokeStartArgb != 0u;
                if (!hasStroke)
                {
                    continue;
                }

                Vector2 layerOffset = layerOffsetWorld[layerIndex];
                float layerBlur = layerBlurWorld[layerIndex];
                int blurDirectionValue = (uint)layerIndex < (uint)layerBlurDirection.Length ? layerBlurDirection[layerIndex] : 0;
                SdfFeatherDirection layerFeatherDirection;
	                if (blurDirectionValue == 1)
	                {
	                    layerFeatherDirection = SdfFeatherDirection.Outside;
	                }
	                else if (blurDirectionValue == 2)
	                {
	                    layerFeatherDirection = SdfFeatherDirection.Inside;
	                }
	                else
	                {
	                    layerFeatherDirection = SdfFeatherDirection.Both;
	                }

	                float layerOffsetX = layerOffset.X * zoom * styleScale;
	                float layerOffsetY = layerOffset.Y * zoom * styleScale;
	                float layerFeatherRadiusCanvas = Math.Max(0f, layerBlur) * zoom * styleScale;
	                bool hasLayerBlur = layerFeatherRadiusCanvas > 0.0001f || layerOffsetX != 0f || layerOffsetY != 0f;

                bool hasStrokeTrim = TryGetStrokeTrimPercent(
                    strokeTrimEnabled[layerIndex],
                    strokeTrimStart[layerIndex],
                    strokeTrimLength[layerIndex],
                    strokeTrimOffset[layerIndex],
                    strokeTrimCap[layerIndex],
                    strokeTrimCapSoftness[layerIndex],
                    zoom * styleScale,
                    strokePathLengthCanvas,
                    out SdfStrokeTrim strokeTrim);

                bool hasStrokeDash = TryGetStrokeDash(
                    strokeDashEnabled[layerIndex],
                    strokeDashLength[layerIndex],
                    strokeDashGapLength[layerIndex],
                    strokeDashOffset[layerIndex],
                    strokeDashCap[layerIndex],
                    strokeDashCapSoftness[layerIndex],
                    zoom * styleScale,
                    out SdfStrokeDash strokeDash);

                if (kind == ShapeKind.Polygon)
                {
                    int headerIndex = draw.AddPolyline(polygonStrokePoints);
                    var cmd = SdfCommand.Polyline(headerIndex, strokeWidthCanvas, ImStyle.ToVector4(strokeStartArgb))
                        .WithBlendMode(blendModeValue);
                    if (useGradient)
                    {
                        cmd.Position = new Vector2((polygonMinCanvasX + polygonMaxCanvasX) * 0.5f, (polygonMinCanvasY + polygonMaxCanvasY) * 0.5f);
                        cmd.Size = new Vector2((polygonMaxCanvasX - polygonMinCanvasX) * 0.5f, (polygonMaxCanvasY - polygonMinCanvasY) * 0.5f);
                        cmd = WithPaintFillGradient(
                            cmd,
                            strokeEndArgb,
                            gradientKind,
                            gradientDirection,
                            gradientCenter,
                            gradientRadiusScale,
                            gradientAngleOffset,
                            rotationRadians,
                            polygonManualRotation: true);
                        if (gradientStopStartIndex >= 0 && gradientStopCount >= 2)
                        {
                            cmd = cmd.WithGradientStops(gradientStopStartIndex, gradientStopCount);
                        }
                    }

                    if (hasStrokeTrim)
                    {
                        cmd = cmd.WithStrokeTrim(in strokeTrim);
                    }
                    if (hasStrokeDash)
                    {
                        cmd = cmd.WithStrokeDash(in strokeDash);
                    }

                    bool pushedLayerOffset = false;
                    bool pushedLayerFeather = false;
                    if (hasLayerBlur)
                    {
                        if (layerOffsetX != 0f || layerOffsetY != 0f)
                        {
                            draw.PushModifierOffset(layerOffsetX, layerOffsetY);
                            pushedLayerOffset = true;
                        }
	                        if (layerFeatherRadiusCanvas > 0.0001f)
	                        {
	                            draw.PushModifierFeather(layerFeatherRadiusCanvas, layerFeatherDirection);
	                            pushedLayerFeather = true;
	                        }
                    }

                    draw.Add(cmd);

                    if (pushedLayerFeather)
                    {
                        draw.PopModifier();
                    }
                    if (pushedLayerOffset)
                    {
                        draw.PopModifier();
                    }
                    continue;
                }

                if (kind == ShapeKind.Rect)
                {
                    var cmd = hasCornerRadius
                        ? SdfCommand.RoundedRectPerCorner(centerCanvas, halfSizeCanvas, radiusTL, radiusTR, radiusBR, radiusBL, Vector4.Zero)
                        : SdfCommand.Rect(centerCanvas, halfSizeCanvas, Vector4.Zero);

                    cmd = cmd.WithRotation(rotationDraw)
                        .WithBlendMode(blendModeValue)
                        .WithStroke(ImStyle.ToVector4(strokeStartArgb), strokeWidthCanvas);
                    if (useGradient)
                    {
                        cmd = WithPaintFillGradient(
                            cmd,
                            strokeEndArgb,
                            gradientKind,
                            gradientDirection,
                            gradientCenter,
                            gradientRadiusScale,
                            gradientAngleOffset,
                            polygonRotationRadians: 0f,
                            polygonManualRotation: false);
                        if (gradientStopStartIndex >= 0 && gradientStopCount >= 2)
                        {
                            cmd = cmd.WithGradientStops(gradientStopStartIndex, gradientStopCount);
                        }
                    }

                    if (hasStrokeTrim)
                    {
                        cmd = cmd.WithStrokeTrim(in strokeTrim);
                    }
                    if (hasStrokeDash)
                    {
                        cmd = cmd.WithStrokeDash(in strokeDash);
                    }

                    bool pushedLayerOffset = false;
                    bool pushedLayerFeather = false;
                    if (hasLayerBlur)
                    {
                        if (layerOffsetX != 0f || layerOffsetY != 0f)
                        {
                            draw.PushModifierOffset(layerOffsetX, layerOffsetY);
                            pushedLayerOffset = true;
                        }
	                        if (layerFeatherRadiusCanvas > 0.0001f)
	                        {
	                            draw.PushModifierFeather(layerFeatherRadiusCanvas, layerFeatherDirection);
	                            pushedLayerFeather = true;
	                        }
                    }

                    draw.Add(cmd);

                    if (pushedLayerFeather)
                    {
                        draw.PopModifier();
                    }
                    if (pushedLayerOffset)
                    {
                        draw.PopModifier();
                    }
                    continue;
                }

                {
                var circleCmd = SdfCommand.Circle(centerCanvas, circleRadiusCanvas, Vector4.Zero)
                    .WithRotation(rotationDraw)
                    .WithBlendMode(blendModeValue)
                    .WithStroke(ImStyle.ToVector4(strokeStartArgb), strokeWidthCanvas);
                if (useGradient)
                {
                    circleCmd = WithPaintFillGradient(
                        circleCmd,
                        strokeEndArgb,
                        gradientKind,
                        gradientDirection,
                        gradientCenter,
                        gradientRadiusScale,
                        gradientAngleOffset,
                        polygonRotationRadians: 0f,
                        polygonManualRotation: false);
                    if (gradientStopStartIndex >= 0 && gradientStopCount >= 2)
                    {
                        circleCmd = circleCmd.WithGradientStops(gradientStopStartIndex, gradientStopCount);
                    }
                }

                if (hasStrokeTrim)
                {
                    circleCmd = circleCmd.WithStrokeTrim(in strokeTrim);
                }
                if (hasStrokeDash)
                {
                    circleCmd = circleCmd.WithStrokeDash(in strokeDash);
                }

                bool pushedLayerOffset = false;
                bool pushedLayerFeather = false;
                if (hasLayerBlur)
                {
                    if (layerOffsetX != 0f || layerOffsetY != 0f)
                    {
                        draw.PushModifierOffset(layerOffsetX, layerOffsetY);
                        pushedLayerOffset = true;
                    }
	                    if (layerFeatherRadiusCanvas > 0.0001f)
	                    {
	                        draw.PushModifierFeather(layerFeatherRadiusCanvas, layerFeatherDirection);
	                        pushedLayerFeather = true;
	                    }
                }

                draw.Add(circleCmd);

                if (pushedLayerFeather)
                {
                    draw.PopModifier();
                }
	                if (pushedLayerOffset)
	                {
	                    draw.PopModifier();
	                }
	                }
	            }
	        }
	    }

    private int AddFillGradientStops(
        CanvasSdfDrawList draw,
        PaintComponentHandle paintHandle,
        int layerIndex,
        float opacity,
        out uint startArgb,
        out uint endArgb,
        out int stopCount)
    {
        startArgb = 0u;
        endArgb = 0u;
        stopCount = GetPaintFillGradientStopCount(paintHandle, layerIndex);
        if (stopCount <= 0)
        {
            return -1;
        }

        Span<SdfGradientStop> stops = stackalloc SdfGradientStop[MaxStops];
        Span<uint> stopColorsArgb = stackalloc uint[MaxStops];
        int visibleStopCount = 0;

        for (int stopIndex = 0; stopIndex < stopCount; stopIndex++)
        {
            float t = GetPaintFillGradientStopT(paintHandle, layerIndex, stopIndex);
            Color32 stopColor = GetPaintFillGradientStopColor(paintHandle, layerIndex, stopIndex);
            stopColor = ApplyTintAndOpacity(stopColor, tint: Color32.White, opacity);
            uint argb = stopColor.A == 0 ? 0u : ToArgb(stopColor);
            stopColorsArgb[stopIndex] = argb;

            stops[stopIndex] = new SdfGradientStop
            {
                Color = ImStyle.ToVector4(argb),
                Params = new Vector4(Math.Clamp(t, 0f, 1f), 0f, 0f, 0f)
            };

            if (argb != 0u)
            {
                visibleStopCount++;
            }
        }

        if (visibleStopCount == 0)
        {
            return -1;
        }

        startArgb = stopColorsArgb[0];
        endArgb = stopColorsArgb[stopCount - 1];
        return draw.AddGradientStops(stops.Slice(0, stopCount));
    }

    private int GetPaintFillGradientStopCount(PaintComponentHandle paintHandle, int layerIndex)
    {
        ReadOnlySpan<int> stopCountArray = PaintComponentProperties.FillGradientStopCountArray(_propertyWorld, paintHandle);
        int count = ((uint)layerIndex < (uint)stopCountArray.Length) ? stopCountArray[layerIndex] : 0;
        if (count < 2)
        {
            return 0;
        }

        if (count > MaxStops)
        {
            return MaxStops;
        }

        return count;
    }

    private float GetPaintFillGradientStopT(PaintComponentHandle paintHandle, int layerIndex, int stopIndex)
    {
        if ((uint)stopIndex >= MaxStops)
        {
            return 0f;
        }

        Vector4 t0123 = PaintComponentProperties.FillGradientStopT0To3Array(_propertyWorld, paintHandle)[layerIndex];
        if (stopIndex < 4)
        {
            return stopIndex switch
            {
                0 => t0123.X,
                1 => t0123.Y,
                2 => t0123.Z,
                _ => t0123.W
            };
        }

        Vector4 t4567 = PaintComponentProperties.FillGradientStopT4To7Array(_propertyWorld, paintHandle)[layerIndex];
        return (stopIndex - 4) switch
        {
            0 => t4567.X,
            1 => t4567.Y,
            2 => t4567.Z,
            _ => t4567.W
        };
    }

    private Color32 GetPaintFillGradientStopColor(PaintComponentHandle paintHandle, int layerIndex, int stopIndex)
    {
        return stopIndex switch
        {
            0 => PaintComponentProperties.FillGradientStopColor0Array(_propertyWorld, paintHandle)[layerIndex],
            1 => PaintComponentProperties.FillGradientStopColor1Array(_propertyWorld, paintHandle)[layerIndex],
            2 => PaintComponentProperties.FillGradientStopColor2Array(_propertyWorld, paintHandle)[layerIndex],
            3 => PaintComponentProperties.FillGradientStopColor3Array(_propertyWorld, paintHandle)[layerIndex],
            4 => PaintComponentProperties.FillGradientStopColor4Array(_propertyWorld, paintHandle)[layerIndex],
            5 => PaintComponentProperties.FillGradientStopColor5Array(_propertyWorld, paintHandle)[layerIndex],
            6 => PaintComponentProperties.FillGradientStopColor6Array(_propertyWorld, paintHandle)[layerIndex],
            7 => PaintComponentProperties.FillGradientStopColor7Array(_propertyWorld, paintHandle)[layerIndex],
            _ => default
        };
    }
}
