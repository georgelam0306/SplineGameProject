using System;
using System.Numerics;
using DerpLib.ImGui.Core;
using Property.Runtime;

namespace Derp.UI;

public sealed partial class UiWorkspace
{
    private EntityId FindTopmostPrefabEntity(Vector2 mouseWorld)
    {
        ReadOnlySpan<EntityId> prefabs = _world.GetChildren(EntityId.Null);
        for (int i = prefabs.Length - 1; i >= 0; i--)
        {
            EntityId prefabEntity = prefabs[i];
            if (_world.GetNodeType(prefabEntity) != UiNodeType.Prefab)
            {
                continue;
            }

            uint stableId = _world.GetStableId(prefabEntity);
            if (GetLayerHidden(stableId) || GetLayerLocked(stableId))
            {
                continue;
            }

            if (!TryGetPrefabRectWorldEcs(prefabEntity, out ImRect rectWorld))
            {
                continue;
            }

            if (rectWorld.Contains(mouseWorld))
            {
                return prefabEntity;
            }
        }

        return EntityId.Null;
    }

    private EntityId FindTopmostGroupEntityInNode(EntityId entity, Vector2 mouseWorld, in WorldTransform parentWorldTransform)
    {
        uint stableId = _world.GetStableId(entity);
        if (GetLayerHidden(stableId) || GetLayerLocked(stableId))
        {
            return EntityId.Null;
        }

        if (!IsPointInsideEntityClipRectIfEnabledEcs(entity, mouseWorld, parentWorldTransform))
        {
            return EntityId.Null;
        }

        UiNodeType type = _world.GetNodeType(entity);
        if (type == UiNodeType.BooleanGroup)
        {
            return FindTopmostGroupEntityInGroup(entity, mouseWorld, parentWorldTransform);
        }

        if (type == UiNodeType.Text)
        {
            return FindTopmostGroupEntityInText(entity, mouseWorld, parentWorldTransform);
        }

        if (type == UiNodeType.PrefabInstance)
        {
            return FindTopmostGroupEntityInPrefabInstance(entity, mouseWorld, parentWorldTransform);
        }

        if (type != UiNodeType.Shape)
        {
            return EntityId.Null;
        }

        return FindTopmostGroupEntityInShape(entity, mouseWorld, parentWorldTransform);
    }

    private EntityId FindTopmostGroupEntityInShape(EntityId shapeEntity, Vector2 mouseWorld, in WorldTransform parentWorldTransform)
    {
        if (!TryGetShapeWorldTransformEcs(shapeEntity, parentWorldTransform, out ShapeWorldTransform worldTransform))
        {
            return EntityId.Null;
        }

        var shapeParentWorldTransform = new WorldTransform(
            worldTransform.PositionWorld,
            worldTransform.ScaleWorld,
            worldTransform.RotationRadians,
            worldTransform.IsVisible);

        ReadOnlySpan<EntityId> children = _world.GetChildren(shapeEntity);
        for (int i = children.Length - 1; i >= 0; i--)
        {
            EntityId child = children[i];
            EntityId nested = FindTopmostGroupEntityInNode(child, mouseWorld, shapeParentWorldTransform);
            if (!nested.IsNull)
            {
                nested = ResolveOpaquePrefabInstanceSelection(nested);
                if (!nested.IsNull)
                {
                    return nested;
                }
            }
        }

        return EntityId.Null;
    }

    private EntityId FindTopmostGroupEntityInText(EntityId textEntity, Vector2 mouseWorld, in WorldTransform parentWorldTransform)
    {
        if (!TryGetTextWorldTransformEcs(textEntity, parentWorldTransform, out ShapeWorldTransform worldTransform))
        {
            return EntityId.Null;
        }

        var textParentWorldTransform = new WorldTransform(
            worldTransform.PositionWorld,
            worldTransform.ScaleWorld,
            worldTransform.RotationRadians,
            worldTransform.IsVisible);

        ReadOnlySpan<EntityId> children = _world.GetChildren(textEntity);
        for (int i = children.Length - 1; i >= 0; i--)
        {
            EntityId child = children[i];
            EntityId nested = FindTopmostGroupEntityInNode(child, mouseWorld, textParentWorldTransform);
            if (!nested.IsNull)
            {
                nested = ResolveOpaquePrefabInstanceSelection(nested);
                if (!nested.IsNull)
                {
                    return nested;
                }
            }
        }

        return EntityId.Null;
    }

    private EntityId FindTopmostGroupEntityInGroup(EntityId groupEntity, Vector2 mouseWorld, in WorldTransform parentWorldTransform)
    {
        if (!TryGetGroupWorldTransformEcs(groupEntity, parentWorldTransform, out WorldTransform groupWorldTransform, out _, out _))
        {
            return EntityId.Null;
        }

        ReadOnlySpan<EntityId> children = _world.GetChildren(groupEntity);
        for (int i = children.Length - 1; i >= 0; i--)
        {
            EntityId child = children[i];
            EntityId nested = FindTopmostGroupEntityInNode(child, mouseWorld, groupWorldTransform);
            if (!nested.IsNull)
            {
                nested = ResolveOpaquePrefabInstanceSelection(nested);
                if (!nested.IsNull)
                {
                    return nested;
                }
            }
        }

        if (IsPointInsideGroupBoundsEcs(groupEntity, mouseWorld, parentWorldTransform))
        {
            return ResolveOpaquePrefabInstanceSelection(groupEntity);
        }

        return EntityId.Null;
    }

    private EntityId FindTopmostGroupEntityInPrefabInstance(EntityId instanceEntity, Vector2 mouseWorld, in WorldTransform parentWorldTransform)
    {
        if (!TryGetGroupWorldTransformEcs(instanceEntity, parentWorldTransform, out WorldTransform instanceWorldTransform, out _, out _))
        {
            return EntityId.Null;
        }

        ReadOnlySpan<EntityId> children = _world.GetChildren(instanceEntity);
        for (int i = children.Length - 1; i >= 0; i--)
        {
            EntityId child = children[i];
            EntityId nested = FindTopmostGroupEntityInNode(child, mouseWorld, instanceWorldTransform);
            if (!nested.IsNull)
            {
                nested = ResolveOpaquePrefabInstanceSelection(nested);
                if (!nested.IsNull)
                {
                    return nested;
                }
            }
        }

        if (IsPointInsidePrefabInstanceBoundsEcs(instanceEntity, mouseWorld, parentWorldTransform))
        {
            return ResolveOpaquePrefabInstanceSelection(instanceEntity);
        }

        return EntityId.Null;
    }

    private EntityId FindTopmostGroupEntityInPrefab(EntityId prefabEntity, Vector2 mouseWorld)
    {
        WorldTransform parentWorldTransform = IdentityWorldTransform;
        if (!TryGetEntityWorldTransformEcs(prefabEntity, out parentWorldTransform))
        {
            parentWorldTransform = IdentityWorldTransform;
        }

        if (IsClipRectEnabledEcs(prefabEntity, defaultValue: true))
        {
            if (!TryGetPrefabRectWorldEcs(prefabEntity, out ImRect prefabRectWorld) || !prefabRectWorld.Contains(mouseWorld))
            {
                return EntityId.Null;
            }
        }

        ReadOnlySpan<EntityId> children = _world.GetChildren(prefabEntity);
        for (int i = children.Length - 1; i >= 0; i--)
        {
            EntityId child = children[i];
            EntityId groupEntity = FindTopmostGroupEntityInNode(child, mouseWorld, parentWorldTransform);
            if (!groupEntity.IsNull)
            {
                return groupEntity;
            }
        }

        return EntityId.Null;
    }

    private EntityId FindTopmostShapeEntityInNode(
        EntityId entity,
        Vector2 mouseWorld,
        bool includeGroupedShapes,
        in WorldTransform parentWorldTransform,
        bool resolveOpaquePrefabInstanceSelection = true)
    {
        uint stableId = _world.GetStableId(entity);
        if (GetLayerHidden(stableId) || GetLayerLocked(stableId))
        {
            return EntityId.Null;
        }

        if (!IsPointInsideEntityClipRectIfEnabledEcs(entity, mouseWorld, parentWorldTransform))
        {
            return EntityId.Null;
        }

        UiNodeType type = _world.GetNodeType(entity);
        if (type == UiNodeType.BooleanGroup)
        {
            if (!includeGroupedShapes)
            {
                return EntityId.Null;
            }

            return FindTopmostShapeEntityInGroup(entity, mouseWorld, parentWorldTransform, resolveOpaquePrefabInstanceSelection);
        }

        if (type == UiNodeType.Text)
        {
            return FindTopmostShapeEntityInText(entity, mouseWorld, includeGroupedShapes, parentWorldTransform, resolveOpaquePrefabInstanceSelection);
        }

        if (type == UiNodeType.PrefabInstance)
        {
            return FindTopmostShapeEntityInPrefabInstance(entity, mouseWorld, includeGroupedShapes, parentWorldTransform, resolveOpaquePrefabInstanceSelection);
        }

        if (type != UiNodeType.Shape)
        {
            return EntityId.Null;
        }

        return FindTopmostShapeEntityInShape(entity, mouseWorld, includeGroupedShapes, parentWorldTransform, resolveOpaquePrefabInstanceSelection);
    }

    private bool IsPointInsideEntityClipRectIfEnabledEcs(EntityId entity, Vector2 pointWorld, in WorldTransform parentWorldTransform)
    {
        if (!_world.TryGetComponent(entity, ClipRectComponent.Api.PoolIdConst, out AnyComponentHandle clipAny) || !clipAny.IsValid)
        {
            return true;
        }

        var clipHandle = new ClipRectComponentHandle(clipAny.Index, clipAny.Generation);
        var clip = ClipRectComponent.Api.FromHandle(_propertyWorld, clipHandle);
        if (!clip.IsAlive || !clip.Enabled)
        {
            return true;
        }

        UiNodeType type = _world.GetNodeType(entity);
        if (type == UiNodeType.Shape)
        {
            if (!TryGetShapeWorldTransformEcs(entity, parentWorldTransform, out ShapeWorldTransform shapeWorldTransform))
            {
                return true;
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
                        return true;
                    }

                    sizeLocal = rectGeometry.Size;
                }
                else if (kind == ShapeKind.Circle)
                {
                    var circleGeometry = CircleGeometryComponent.Api.FromHandle(_propertyWorld, circleGeometryHandle);
                    if (!circleGeometry.IsAlive || circleGeometry.Radius <= 0f)
                    {
                        return true;
                    }

                    float d = circleGeometry.Radius * 2f;
                    sizeLocal = new Vector2(d, d);
                }
                else
                {
                    return true;
                }
            }

            var worldTransform = new WorldTransform(
                shapeWorldTransform.PositionWorld,
                shapeWorldTransform.ScaleWorld,
                shapeWorldTransform.RotationRadians,
                shapeWorldTransform.IsVisible);
            ImRect boundsLocal = RectTransformMath.GetRectFromPivot(Vector2.Zero, shapeWorldTransform.Anchor, sizeLocal);
            Vector2 localCenter = new Vector2(boundsLocal.X + boundsLocal.Width * 0.5f, boundsLocal.Y + boundsLocal.Height * 0.5f);
            Vector2 centerWorld = TransformPoint(worldTransform, localCenter);
            Vector2 halfSizeWorld = new Vector2(
                MathF.Abs(worldTransform.Scale.X * boundsLocal.Width) * 0.5f,
                MathF.Abs(worldTransform.Scale.Y * boundsLocal.Height) * 0.5f);
            return IsPointInsideOrientedRect(centerWorld, halfSizeWorld, worldTransform.RotationRadians, pointWorld);
        }

        if (type == UiNodeType.Text)
        {
            if (!TryGetTextWorldTransformEcs(entity, parentWorldTransform, out ShapeWorldTransform textWorldTransform))
            {
                return true;
            }

            Vector2 sizeLocal = Vector2.Zero;
            if (!TryGetComputedSize(entity, out sizeLocal) || sizeLocal.X <= 0f || sizeLocal.Y <= 0f)
            {
                if (_world.TryGetComponent(entity, RectGeometryComponent.Api.PoolIdConst, out AnyComponentHandle rectAny) && rectAny.IsValid)
                {
                    var rectHandle = new RectGeometryComponentHandle(rectAny.Index, rectAny.Generation);
                    var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, rectHandle);
                    if (rectGeometry.IsAlive)
                    {
                        sizeLocal = rectGeometry.Size;
                    }
                }
            }

            if (sizeLocal.X <= 0f || sizeLocal.Y <= 0f)
            {
                return true;
            }

            var worldTransform = new WorldTransform(
                textWorldTransform.PositionWorld,
                textWorldTransform.ScaleWorld,
                textWorldTransform.RotationRadians,
                textWorldTransform.IsVisible);
            ImRect boundsLocal = RectTransformMath.GetRectFromPivot(Vector2.Zero, textWorldTransform.Anchor, sizeLocal);
            Vector2 localCenter = new Vector2(boundsLocal.X + boundsLocal.Width * 0.5f, boundsLocal.Y + boundsLocal.Height * 0.5f);
            Vector2 centerWorld = TransformPoint(worldTransform, localCenter);
            Vector2 halfSizeWorld = new Vector2(
                MathF.Abs(worldTransform.Scale.X * boundsLocal.Width) * 0.5f,
                MathF.Abs(worldTransform.Scale.Y * boundsLocal.Height) * 0.5f);
            return IsPointInsideOrientedRect(centerWorld, halfSizeWorld, worldTransform.RotationRadians, pointWorld);
        }

        if (type == UiNodeType.BooleanGroup || type == UiNodeType.PrefabInstance)
        {
            if (!TryGetComputedSize(entity, out Vector2 sizeLocal) || sizeLocal.X <= 0f || sizeLocal.Y <= 0f)
            {
                return true;
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
                return true;
            }

            ImRect boundsLocal = RectTransformMath.GetRectFromPivot(Vector2.Zero, anchor01, sizeLocal);
            Vector2 localCenter = new Vector2(boundsLocal.X + boundsLocal.Width * 0.5f, boundsLocal.Y + boundsLocal.Height * 0.5f);
            Vector2 centerWorld = TransformPoint(worldTransform, localCenter);
            Vector2 halfSizeWorld = new Vector2(
                MathF.Abs(worldTransform.Scale.X * boundsLocal.Width) * 0.5f,
                MathF.Abs(worldTransform.Scale.Y * boundsLocal.Height) * 0.5f);
            return IsPointInsideOrientedRect(centerWorld, halfSizeWorld, worldTransform.RotationRadians, pointWorld);
        }

        return true;
    }

    private EntityId FindTopmostShapeEntityInShape(
        EntityId shapeEntity,
        Vector2 mouseWorld,
        bool includeGroupedShapes,
        in WorldTransform parentWorldTransform,
        bool resolveOpaquePrefabInstanceSelection = true)
    {
        if (!TryGetShapeWorldTransformEcs(shapeEntity, parentWorldTransform, out ShapeWorldTransform worldTransform))
        {
            return EntityId.Null;
        }

        var shapeParentWorldTransform = new WorldTransform(
            worldTransform.PositionWorld,
            worldTransform.ScaleWorld,
            worldTransform.RotationRadians,
            worldTransform.IsVisible);

        ReadOnlySpan<EntityId> children = _world.GetChildren(shapeEntity);
        for (int i = children.Length - 1; i >= 0; i--)
        {
            EntityId child = children[i];
            EntityId nested = FindTopmostShapeEntityInNode(child, mouseWorld, includeGroupedShapes, shapeParentWorldTransform, resolveOpaquePrefabInstanceSelection);
            if (!nested.IsNull)
            {
                if (resolveOpaquePrefabInstanceSelection)
                {
                    nested = ResolveOpaquePrefabInstanceSelection(nested);
                    if (!nested.IsNull)
                    {
                        return nested;
                    }
                }
                else
                {
                    return nested;
                }
            }
        }

        if (IsPointInsideShapeEcs(shapeEntity, mouseWorld, parentWorldTransform))
        {
            return resolveOpaquePrefabInstanceSelection ? ResolveOpaquePrefabInstanceSelection(shapeEntity) : shapeEntity;
        }

        return EntityId.Null;
    }

    private EntityId FindTopmostShapeEntityInText(
        EntityId textEntity,
        Vector2 mouseWorld,
        bool includeGroupedShapes,
        in WorldTransform parentWorldTransform,
        bool resolveOpaquePrefabInstanceSelection = true)
    {
        if (!TryGetTextWorldTransformEcs(textEntity, parentWorldTransform, out ShapeWorldTransform worldTransform))
        {
            return EntityId.Null;
        }

        var textParentWorldTransform = new WorldTransform(
            worldTransform.PositionWorld,
            worldTransform.ScaleWorld,
            worldTransform.RotationRadians,
            worldTransform.IsVisible);

        ReadOnlySpan<EntityId> children = _world.GetChildren(textEntity);
        for (int i = children.Length - 1; i >= 0; i--)
        {
            EntityId child = children[i];
            EntityId nested = FindTopmostShapeEntityInNode(child, mouseWorld, includeGroupedShapes, textParentWorldTransform, resolveOpaquePrefabInstanceSelection);
            if (!nested.IsNull)
            {
                if (resolveOpaquePrefabInstanceSelection)
                {
                    nested = ResolveOpaquePrefabInstanceSelection(nested);
                    if (!nested.IsNull)
                    {
                        return nested;
                    }
                }
                else
                {
                    return nested;
                }
            }
        }

        if (IsPointInsideTextEcs(textEntity, mouseWorld, parentWorldTransform))
        {
            return resolveOpaquePrefabInstanceSelection ? ResolveOpaquePrefabInstanceSelection(textEntity) : textEntity;
        }

        return EntityId.Null;
    }

    private EntityId FindTopmostShapeEntityInGroup(
        EntityId groupEntity,
        Vector2 mouseWorld,
        in WorldTransform parentWorldTransform,
        bool resolveOpaquePrefabInstanceSelection = true)
    {
        if (!TryGetGroupWorldTransformEcs(groupEntity, parentWorldTransform, out WorldTransform groupWorldTransform, out _, out _))
        {
            return EntityId.Null;
        }

        ReadOnlySpan<EntityId> children = _world.GetChildren(groupEntity);
        for (int i = children.Length - 1; i >= 0; i--)
        {
            EntityId child = children[i];
            EntityId nested = FindTopmostShapeEntityInNode(child, mouseWorld, includeGroupedShapes: true, groupWorldTransform, resolveOpaquePrefabInstanceSelection);
            if (!nested.IsNull)
            {
                if (resolveOpaquePrefabInstanceSelection)
                {
                    nested = ResolveOpaquePrefabInstanceSelection(nested);
                    if (!nested.IsNull)
                    {
                        return nested;
                    }
                }
                else
                {
                    return nested;
                }
            }
        }

        return EntityId.Null;
    }

    private EntityId FindTopmostShapeEntityInPrefabInstance(
        EntityId instanceEntity,
        Vector2 mouseWorld,
        bool includeGroupedShapes,
        in WorldTransform parentWorldTransform,
        bool resolveOpaquePrefabInstanceSelection = true)
    {
        if (!TryGetGroupWorldTransformEcs(instanceEntity, parentWorldTransform, out WorldTransform instanceWorldTransform, out _, out _))
        {
            return EntityId.Null;
        }

        ReadOnlySpan<EntityId> children = _world.GetChildren(instanceEntity);
        for (int i = children.Length - 1; i >= 0; i--)
        {
            EntityId child = children[i];
            EntityId nested = FindTopmostShapeEntityInNode(child, mouseWorld, includeGroupedShapes, instanceWorldTransform, resolveOpaquePrefabInstanceSelection);
            if (!nested.IsNull)
            {
                if (resolveOpaquePrefabInstanceSelection)
                {
                    nested = ResolveOpaquePrefabInstanceSelection(nested);
                    if (!nested.IsNull)
                    {
                        return nested;
                    }
                }
                else
                {
                    return nested;
                }
            }
        }

        if (IsPointInsidePrefabInstanceBoundsEcs(instanceEntity, mouseWorld, parentWorldTransform))
        {
            return resolveOpaquePrefabInstanceSelection ? ResolveOpaquePrefabInstanceSelection(instanceEntity) : instanceEntity;
        }

        return EntityId.Null;
    }

    private bool IsPointInsidePrefabInstanceBoundsEcs(EntityId instanceEntity, Vector2 mouseWorld, in WorldTransform parentWorldTransform)
    {
        if (!TryGetComputedSize(instanceEntity, out Vector2 sizeLocal) || sizeLocal.X <= 0f || sizeLocal.Y <= 0f)
        {
            return false;
        }

        Vector2 anchor01 = Vector2.Zero;
        if (_world.TryGetComponent(instanceEntity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny))
        {
            var transformHandle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
            var transform = TransformComponent.Api.FromHandle(_propertyWorld, transformHandle);
            if (transform.IsAlive)
            {
                anchor01 = transform.Anchor;
            }
        }

        if (!TryGetGroupWorldTransformEcs(instanceEntity, parentWorldTransform, out WorldTransform instanceWorldTransform, out _, out _))
        {
            return false;
        }

        ImRect boundsLocal = RectTransformMath.GetRectFromPivot(Vector2.Zero, anchor01, sizeLocal);
        Vector2 localCenter = new Vector2(boundsLocal.X + boundsLocal.Width * 0.5f, boundsLocal.Y + boundsLocal.Height * 0.5f);

        Vector2 centerWorld = TransformPoint(instanceWorldTransform, localCenter);
        float halfWidth = MathF.Abs(instanceWorldTransform.Scale.X * boundsLocal.Width) * 0.5f;
        float halfHeight = MathF.Abs(instanceWorldTransform.Scale.Y * boundsLocal.Height) * 0.5f;
        return IsPointInsideOrientedRect(centerWorld, new Vector2(halfWidth, halfHeight), instanceWorldTransform.RotationRadians, mouseWorld);
    }

    private EntityId FindTopmostShapeEntityInPrefab(
        EntityId prefabEntity,
        Vector2 mouseWorld,
        bool includeGroupedShapes,
        bool resolveOpaquePrefabInstanceSelection = true)
    {
        WorldTransform parentWorldTransform = IdentityWorldTransform;
        if (!TryGetEntityWorldTransformEcs(prefabEntity, out parentWorldTransform))
        {
            parentWorldTransform = IdentityWorldTransform;
        }

        if (IsClipRectEnabledEcs(prefabEntity, defaultValue: true))
        {
            if (!TryGetPrefabRectWorldEcs(prefabEntity, out ImRect prefabRectWorld) || !prefabRectWorld.Contains(mouseWorld))
            {
                return EntityId.Null;
            }
        }

        ReadOnlySpan<EntityId> children = _world.GetChildren(prefabEntity);
        for (int i = children.Length - 1; i >= 0; i--)
        {
            EntityId child = children[i];
            EntityId shapeEntity = FindTopmostShapeEntityInNode(child, mouseWorld, includeGroupedShapes, parentWorldTransform, resolveOpaquePrefabInstanceSelection);
            if (!shapeEntity.IsNull)
            {
                return shapeEntity;
            }
        }

        return EntityId.Null;
    }

    private bool IsPointInsideShapeEcs(EntityId shapeEntity, Vector2 pointWorld, in WorldTransform parentWorldTransform)
    {
        if (!TryGetShapeWorldTransformEcs(shapeEntity, parentWorldTransform, out ShapeWorldTransform worldTransform))
        {
            return false;
        }

        ShapeKind kind = GetShapeKindEcs(shapeEntity, out RectGeometryComponentHandle rectGeometryHandle, out CircleGeometryComponentHandle circleGeometryHandle, out PathComponentHandle pathHandle);
        if (kind == ShapeKind.Circle)
        {
            var circleGeometry = CircleGeometryComponent.Api.FromHandle(_propertyWorld, circleGeometryHandle);
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
            var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, rectGeometryHandle);
            Vector2 scale = worldTransform.ScaleWorld;
            float scaleX = scale.X == 0f ? 1f : scale.X;
            float scaleY = scale.Y == 0f ? 1f : scale.Y;

            Vector2 sizeLocal = rectGeometry.IsAlive ? rectGeometry.Size : Vector2.Zero;
            if (TryGetComputedSize(shapeEntity, out Vector2 computedSize) && computedSize.X > 0f && computedSize.Y > 0f)
            {
                sizeLocal = computedSize;
            }

            float widthWorld = sizeLocal.X * MathF.Abs(scaleX);
            float heightWorld = sizeLocal.Y * MathF.Abs(scaleY);

            Vector2 anchorOffset = new Vector2(
                (worldTransform.Anchor.X - 0.5f) * widthWorld,
                (worldTransform.Anchor.Y - 0.5f) * heightWorld);
            Vector2 centerWorld = worldTransform.PositionWorld - RotateVector(anchorOffset, worldTransform.RotationRadians);
            Vector2 halfSizeWorld = new Vector2(widthWorld * 0.5f, heightWorld * 0.5f);
            return IsPointInsideOrientedRect(centerWorld, halfSizeWorld, worldTransform.RotationRadians, pointWorld);
        }

        if (!TryGetPolygonPointsLocalEcs(pathHandle, out ReadOnlySpan<Vector2> pointsLocal, out int pointCount))
        {
            return false;
        }

        GetBounds(pointsLocal, out float minLocalX, out float minLocalY, out float maxLocalX, out float maxLocalY);
        Vector2 boundsMinLocal = new Vector2(minLocalX, minLocalY);
        Vector2 boundsSizeLocal = new Vector2(maxLocalX - minLocalX, maxLocalY - minLocalY);

        Vector2 positionWorld = worldTransform.PositionWorld;
        Vector2 anchor = worldTransform.Anchor;
        float rotationRadians = worldTransform.RotationRadians;
        Vector2 pivotLocal = GetPolygonPivotLocalEcs(pathHandle, anchor, boundsMinLocal, boundsSizeLocal);

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

    private bool IsPointInsideTextEcs(EntityId textEntity, Vector2 pointWorld, in WorldTransform parentWorldTransform)
    {
        if (!TryGetTextWorldTransformEcs(textEntity, parentWorldTransform, out ShapeWorldTransform worldTransform))
        {
            return false;
        }

        if (!_world.TryGetComponent(textEntity, RectGeometryComponent.Api.PoolIdConst, out AnyComponentHandle rectAny))
        {
            return false;
        }

        var rectHandle = new RectGeometryComponentHandle(rectAny.Index, rectAny.Generation);
        var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, rectHandle);
        if (!rectGeometry.IsAlive)
        {
            return false;
        }

        Vector2 scale = worldTransform.ScaleWorld;
        float scaleX = scale.X == 0f ? 1f : scale.X;
        float scaleY = scale.Y == 0f ? 1f : scale.Y;

        Vector2 sizeLocal = rectGeometry.Size;
        if (TryGetComputedSize(textEntity, out Vector2 computedSize) && computedSize.X > 0f && computedSize.Y > 0f)
        {
            sizeLocal = computedSize;
        }

        float widthWorld = sizeLocal.X * MathF.Abs(scaleX);
        float heightWorld = sizeLocal.Y * MathF.Abs(scaleY);

        Vector2 anchorOffset = new Vector2(
            (worldTransform.Anchor.X - 0.5f) * widthWorld,
            (worldTransform.Anchor.Y - 0.5f) * heightWorld);
        Vector2 centerWorld = worldTransform.PositionWorld - RotateVector(anchorOffset, worldTransform.RotationRadians);
        Vector2 halfSizeWorld = new Vector2(widthWorld * 0.5f, heightWorld * 0.5f);
        return IsPointInsideOrientedRect(centerWorld, halfSizeWorld, worldTransform.RotationRadians, pointWorld);
    }

    private bool IsPointInsideGroupBoundsEcs(EntityId groupEntity, Vector2 mouseWorld, in WorldTransform parentWorldTransform)
    {
        if (!TryGetGroupWorldTransformEcs(groupEntity, parentWorldTransform, out WorldTransform groupWorldTransform, out _, out _))
        {
            return false;
        }

        if (!groupWorldTransform.IsVisible)
        {
            return false;
        }

        float minX = 0f;
        float minY = 0f;
        float maxX = 0f;
        float maxY = 0f;
        bool hasBounds = false;

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

        Vector2 mouseLocal = InverseTransformPoint(groupWorldTransform, mouseWorld);
        return mouseLocal.X >= minX && mouseLocal.X <= maxX && mouseLocal.Y >= minY && mouseLocal.Y <= maxY;
    }

    private void AccumulateGroupSubtreeBoundsLocalEcs(
        EntityId entity,
        in WorldTransform parentWorldTransform,
        in WorldTransform rootGroupWorldTransform,
        ref bool hasBounds,
        ref float minX,
        ref float minY,
        ref float maxX,
        ref float maxY)
    {
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
                EntityId child = groupChildren[i];
                AccumulateGroupSubtreeBoundsLocalEcs(child, groupWorldTransform, rootGroupWorldTransform, ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
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

        var shapeParentWorldTransform = new WorldTransform(
            shapeWorldTransform.PositionWorld,
            shapeWorldTransform.ScaleWorld,
            shapeWorldTransform.RotationRadians,
            shapeWorldTransform.IsVisible);

        ShapeKind kind = GetShapeKindEcs(entity, out RectGeometryComponentHandle rectGeometryHandle, out CircleGeometryComponentHandle circleGeometryHandle, out PathComponentHandle pathHandle);
        if (kind == ShapeKind.Polygon)
        {
            if (TryGetPolygonPointsLocalEcs(pathHandle, out ReadOnlySpan<Vector2> pointsLocal, out int pointCount))
            {
                GetBounds(pointsLocal, out float minLocalX, out float minLocalY, out float maxLocalX, out float maxLocalY);
                Vector2 boundsMinLocal = new Vector2(minLocalX, minLocalY);
                Vector2 boundsSizeLocal = new Vector2(maxLocalX - minLocalX, maxLocalY - minLocalY);
                Vector2 pivotLocal = GetPolygonPivotLocalEcs(pathHandle, shapeWorldTransform.Anchor, boundsMinLocal, boundsSizeLocal);

                Vector2 scale = shapeWorldTransform.ScaleWorld;
                float scaleX = scale.X == 0f ? 1f : scale.X;
                float scaleY = scale.Y == 0f ? 1f : scale.Y;

                for (int i = 0; i < pointCount; i++)
                {
                    Vector2 pWorld = TransformPolygonPointToWorld(shapeWorldTransform.PositionWorld, pivotLocal, shapeWorldTransform.RotationRadians, scaleX, scaleY, pointsLocal[i]);
                    Vector2 pLocal = InverseTransformPoint(rootGroupWorldTransform, pWorld);
                    AccumulateBoundsPoint(pLocal, ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
                }
            }
        }
        else
        {
            Vector2 scale = shapeWorldTransform.ScaleWorld;
            float scaleX = scale.X == 0f ? 1f : scale.X;
            float scaleY = scale.Y == 0f ? 1f : scale.Y;

            float widthWorld;
            float heightWorld;

            if (kind == ShapeKind.Rect)
            {
                var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, rectGeometryHandle);
                Vector2 sizeLocal = rectGeometry.IsAlive ? rectGeometry.Size : Vector2.Zero;
                if (TryGetComputedSize(entity, out Vector2 computedSize) && computedSize.X > 0f && computedSize.Y > 0f)
                {
                    sizeLocal = computedSize;
                }

                widthWorld = sizeLocal.X * MathF.Abs(scaleX);
                heightWorld = sizeLocal.Y * MathF.Abs(scaleY);
            }
            else
            {
                var circleGeometry = CircleGeometryComponent.Api.FromHandle(_propertyWorld, circleGeometryHandle);
                float radius = circleGeometry.Radius;
                float scaleAvg = (MathF.Abs(scaleX) + MathF.Abs(scaleY)) * 0.5f;
                float diameter = radius * 2f * scaleAvg;
                widthWorld = diameter;
                heightWorld = diameter;
            }

            Vector2 anchorOffset = new Vector2(
                (shapeWorldTransform.Anchor.X - 0.5f) * widthWorld,
                (shapeWorldTransform.Anchor.Y - 0.5f) * heightWorld);
            Vector2 centerWorld = shapeWorldTransform.PositionWorld - RotateVector(anchorOffset, shapeWorldTransform.RotationRadians);
            Vector2 halfSizeWorld = new Vector2(widthWorld * 0.5f, heightWorld * 0.5f);

            Vector2 axisX = RotateVector(new Vector2(halfSizeWorld.X, 0f), shapeWorldTransform.RotationRadians);
            Vector2 axisY = RotateVector(new Vector2(0f, halfSizeWorld.Y), shapeWorldTransform.RotationRadians);

            Vector2 c0 = centerWorld - axisX - axisY;
            Vector2 c1 = centerWorld + axisX - axisY;
            Vector2 c2 = centerWorld + axisX + axisY;
            Vector2 c3 = centerWorld - axisX + axisY;

            AccumulateBoundsPoint(InverseTransformPoint(rootGroupWorldTransform, c0), ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
            AccumulateBoundsPoint(InverseTransformPoint(rootGroupWorldTransform, c1), ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
            AccumulateBoundsPoint(InverseTransformPoint(rootGroupWorldTransform, c2), ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
            AccumulateBoundsPoint(InverseTransformPoint(rootGroupWorldTransform, c3), ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
        }

        ReadOnlySpan<EntityId> shapeChildren = _world.GetChildren(entity);
        for (int i = 0; i < shapeChildren.Length; i++)
        {
            EntityId child = shapeChildren[i];
            AccumulateGroupSubtreeBoundsLocalEcs(child, shapeParentWorldTransform, rootGroupWorldTransform, ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
        }
    }

    private static void AccumulateBoundsPoint(Vector2 p, ref bool hasBounds, ref float minX, ref float minY, ref float maxX, ref float maxY)
    {
        if (!hasBounds)
        {
            hasBounds = true;
            minX = p.X;
            maxX = p.X;
            minY = p.Y;
            maxY = p.Y;
            return;
        }

        if (p.X < minX) minX = p.X;
        if (p.Y < minY) minY = p.Y;
        if (p.X > maxX) maxX = p.X;
        if (p.Y > maxY) maxY = p.Y;
    }
}
