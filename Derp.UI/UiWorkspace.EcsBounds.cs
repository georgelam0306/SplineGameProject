using System;
using System.Numerics;
using DerpLib.ImGui.Core;
using Property.Runtime;

namespace Derp.UI;

public sealed partial class UiWorkspace
{
    private bool TryGetEntityPrimitiveBoundsWorldEcs(EntityId entity, in WorldTransform parentWorldTransform, out ImRect boundsWorld)
    {
        boundsWorld = ImRect.Zero;

        UiNodeType type = _world.GetNodeType(entity);
        if (type == UiNodeType.BooleanGroup)
        {
            if (!TryGetGroupWorldTransformEcs(entity, parentWorldTransform, out WorldTransform groupWorldTransform, out _, out _))
            {
                return false;
            }

            if (!groupWorldTransform.IsVisible)
            {
                return false;
            }

            if (!TryGetGroupSubtreeBoundsLocalEcs(entity, groupWorldTransform, out ImRect boundsLocal))
            {
                return false;
            }

            Vector2 c0 = TransformPoint(groupWorldTransform, new Vector2(boundsLocal.X, boundsLocal.Y));
            Vector2 c1 = TransformPoint(groupWorldTransform, new Vector2(boundsLocal.Right, boundsLocal.Y));
            Vector2 c2 = TransformPoint(groupWorldTransform, new Vector2(boundsLocal.Right, boundsLocal.Bottom));
            Vector2 c3 = TransformPoint(groupWorldTransform, new Vector2(boundsLocal.X, boundsLocal.Bottom));

            bool has = false;
            float minX = 0f;
            float minY = 0f;
            float maxX = 0f;
            float maxY = 0f;
            AccumulateBoundsPointWorld(c0, ref has, ref minX, ref minY, ref maxX, ref maxY);
            AccumulateBoundsPointWorld(c1, ref has, ref minX, ref minY, ref maxX, ref maxY);
            AccumulateBoundsPointWorld(c2, ref has, ref minX, ref minY, ref maxX, ref maxY);
            AccumulateBoundsPointWorld(c3, ref has, ref minX, ref minY, ref maxX, ref maxY);

            if (!has)
            {
                return false;
            }

            boundsWorld = ImRect.FromMinMax(minX, minY, maxX, maxY);
            return true;
        }

        if (type == UiNodeType.Text)
        {
            if (!TryGetTextWorldTransformEcs(entity, parentWorldTransform, out ShapeWorldTransform textWorldTransform))
            {
                return false;
            }

            if (!textWorldTransform.IsVisible)
            {
                return false;
            }

            if (!_world.TryGetComponent(entity, RectGeometryComponent.Api.PoolIdConst, out AnyComponentHandle rectAny))
            {
                return false;
            }

            var rectHandle = new RectGeometryComponentHandle(rectAny.Index, rectAny.Generation);
            var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, rectHandle);
            if (!rectGeometry.IsAlive)
            {
                return false;
            }

            Vector2 textScaleWorld = textWorldTransform.ScaleWorld;
            float textScaleX = textScaleWorld.X == 0f ? 1f : textScaleWorld.X;
            float textScaleY = textScaleWorld.Y == 0f ? 1f : textScaleWorld.Y;

            Vector2 sizeLocal = rectGeometry.Size;
            if (TryGetComputedSize(entity, out Vector2 computedSize) && computedSize.X > 0f && computedSize.Y > 0f)
            {
                sizeLocal = computedSize;
            }

            float textWidthWorld = sizeLocal.X * MathF.Abs(textScaleX);
            float textHeightWorld = sizeLocal.Y * MathF.Abs(textScaleY);

            Vector2 textAnchorOffset = new Vector2(
                (textWorldTransform.Anchor.X - 0.5f) * textWidthWorld,
                (textWorldTransform.Anchor.Y - 0.5f) * textHeightWorld);

            Vector2 textCenterWorld = textWorldTransform.PositionWorld - RotateVector(textAnchorOffset, textWorldTransform.RotationRadians);
            Vector2 textHalfSizeWorld = new Vector2(textWidthWorld * 0.5f, textHeightWorld * 0.5f);

            Vector2 textAxisX = RotateVector(new Vector2(textHalfSizeWorld.X, 0f), textWorldTransform.RotationRadians);
            Vector2 textAxisY = RotateVector(new Vector2(0f, textHalfSizeWorld.Y), textWorldTransform.RotationRadians);

            Vector2 c0 = textCenterWorld - textAxisX - textAxisY;
            Vector2 c1 = textCenterWorld + textAxisX - textAxisY;
            Vector2 c2 = textCenterWorld + textAxisX + textAxisY;
            Vector2 c3 = textCenterWorld - textAxisX + textAxisY;

            bool has = false;
            float minX = 0f;
            float minY = 0f;
            float maxX = 0f;
            float maxY = 0f;
            AccumulateBoundsPointWorld(c0, ref has, ref minX, ref minY, ref maxX, ref maxY);
            AccumulateBoundsPointWorld(c1, ref has, ref minX, ref minY, ref maxX, ref maxY);
            AccumulateBoundsPointWorld(c2, ref has, ref minX, ref minY, ref maxX, ref maxY);
            AccumulateBoundsPointWorld(c3, ref has, ref minX, ref minY, ref maxX, ref maxY);

            if (!has)
            {
                return false;
            }

            boundsWorld = ImRect.FromMinMax(minX, minY, maxX, maxY);
            return true;
        }

        if (type != UiNodeType.Shape)
        {
            return false;
        }

        if (!TryGetShapeWorldTransformEcs(entity, parentWorldTransform, out ShapeWorldTransform shapeWorldTransform))
        {
            return false;
        }

        if (!shapeWorldTransform.IsVisible)
        {
            return false;
        }

        ShapeKind kind = GetShapeKindEcs(entity, out RectGeometryComponentHandle rectGeometryHandle, out CircleGeometryComponentHandle circleGeometryHandle, out PathComponentHandle pathHandle);
        if (kind == ShapeKind.Polygon && !pathHandle.IsNull)
        {
            if (!TryGetPolygonPointsLocalEcs(pathHandle, out ReadOnlySpan<Vector2> pointsLocal, out int pointCount))
            {
                return false;
            }

            if (pointCount <= 0)
            {
                return false;
            }

            GetBounds(pointsLocal, out float minLocalX, out float minLocalY, out float maxLocalX, out float maxLocalY);
            Vector2 boundsMinLocal = new Vector2(minLocalX, minLocalY);
            Vector2 boundsSizeLocal = new Vector2(maxLocalX - minLocalX, maxLocalY - minLocalY);
            Vector2 pivotLocal = GetPolygonPivotLocalEcs(pathHandle, shapeWorldTransform.Anchor, boundsMinLocal, boundsSizeLocal);

            Vector2 scale = shapeWorldTransform.ScaleWorld;
            float scaleX = scale.X == 0f ? 1f : scale.X;
            float scaleY = scale.Y == 0f ? 1f : scale.Y;

            bool has = false;
            float minX = 0f;
            float minY = 0f;
            float maxX = 0f;
            float maxY = 0f;

            for (int i = 0; i < pointCount; i++)
            {
                Vector2 pWorld = TransformPolygonPointToWorld(shapeWorldTransform.PositionWorld, pivotLocal, shapeWorldTransform.RotationRadians, scaleX, scaleY, pointsLocal[i]);
                AccumulateBoundsPointWorld(pWorld, ref has, ref minX, ref minY, ref maxX, ref maxY);
            }

            if (!has)
            {
                return false;
            }

            boundsWorld = ImRect.FromMinMax(minX, minY, maxX, maxY);
            return true;
        }

        Vector2 scaleWorld = shapeWorldTransform.ScaleWorld;
        float sx = scaleWorld.X == 0f ? 1f : scaleWorld.X;
        float sy = scaleWorld.Y == 0f ? 1f : scaleWorld.Y;

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

            widthWorld = sizeLocal.X * MathF.Abs(sx);
            heightWorld = sizeLocal.Y * MathF.Abs(sy);
        }
        else
        {
            var circleGeometry = CircleGeometryComponent.Api.FromHandle(_propertyWorld, circleGeometryHandle);
            float radius = circleGeometry.Radius;
            float scaleAvg = (MathF.Abs(sx) + MathF.Abs(sy)) * 0.5f;
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

        Vector2 s0 = centerWorld - axisX - axisY;
        Vector2 s1 = centerWorld + axisX - axisY;
        Vector2 s2 = centerWorld + axisX + axisY;
        Vector2 s3 = centerWorld - axisX + axisY;

        bool hasRect = false;
        float rMinX = 0f;
        float rMinY = 0f;
        float rMaxX = 0f;
        float rMaxY = 0f;
        AccumulateBoundsPointWorld(s0, ref hasRect, ref rMinX, ref rMinY, ref rMaxX, ref rMaxY);
        AccumulateBoundsPointWorld(s1, ref hasRect, ref rMinX, ref rMinY, ref rMaxX, ref rMaxY);
        AccumulateBoundsPointWorld(s2, ref hasRect, ref rMinX, ref rMinY, ref rMaxX, ref rMaxY);
        AccumulateBoundsPointWorld(s3, ref hasRect, ref rMinX, ref rMinY, ref rMaxX, ref rMaxY);

        if (!hasRect)
        {
            return false;
        }

        boundsWorld = ImRect.FromMinMax(rMinX, rMinY, rMaxX, rMaxY);
        return true;
    }

    internal bool TryComputeSelectionBoundsWorldEcs(EntityId commonParent, ReadOnlySpan<EntityId> entities, out ImRect boundsWorld)
    {
        boundsWorld = ImRect.Zero;

        if (entities.Length <= 0)
        {
            return false;
        }

        WorldTransform parentWorldTransform = IdentityWorldTransform;
        if (!commonParent.IsNull)
        {
            if (!TryGetEntityWorldTransformEcs(commonParent, out parentWorldTransform))
            {
                return false;
            }
        }

        bool hasBounds = false;
        float minX = 0f;
        float minY = 0f;
        float maxX = 0f;
        float maxY = 0f;

        for (int i = 0; i < entities.Length; i++)
        {
            AccumulateSubtreeBoundsWorldEcs(entities[i], parentWorldTransform, ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
        }

        if (!hasBounds)
        {
            return false;
        }

        boundsWorld = ImRect.FromMinMax(minX, minY, maxX, maxY);
        return true;
    }

    private void AccumulateSubtreeBoundsWorldEcs(
        EntityId entity,
        in WorldTransform parentWorldTransform,
        ref bool hasBounds,
        ref float minX,
        ref float minY,
        ref float maxX,
        ref float maxY)
    {
        UiNodeType type = _world.GetNodeType(entity);
        if (type == UiNodeType.None)
        {
            return;
        }

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
                AccumulateSubtreeBoundsWorldEcs(groupChildren[i], groupWorldTransform, ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
            }

            return;
        }

        if (type == UiNodeType.Text)
        {
            if (!TryGetTextWorldTransformEcs(entity, parentWorldTransform, out ShapeWorldTransform textWorldTransform))
            {
                return;
            }

            if (!textWorldTransform.IsVisible)
            {
                return;
            }

            if (!_world.TryGetComponent(entity, RectGeometryComponent.Api.PoolIdConst, out AnyComponentHandle rectAny))
            {
                return;
            }

            var rectHandle = new RectGeometryComponentHandle(rectAny.Index, rectAny.Generation);
            var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, rectHandle);
            if (!rectGeometry.IsAlive)
            {
                return;
            }

            Vector2 scale = textWorldTransform.ScaleWorld;
            float scaleX = scale.X == 0f ? 1f : scale.X;
            float scaleY = scale.Y == 0f ? 1f : scale.Y;

            Vector2 sizeLocal = rectGeometry.Size;
            if (TryGetComputedSize(entity, out Vector2 computedSize) && computedSize.X > 0f && computedSize.Y > 0f)
            {
                sizeLocal = computedSize;
            }

            float widthWorld = sizeLocal.X * MathF.Abs(scaleX);
            float heightWorld = sizeLocal.Y * MathF.Abs(scaleY);

            Vector2 anchorOffset = new Vector2(
                (textWorldTransform.Anchor.X - 0.5f) * widthWorld,
                (textWorldTransform.Anchor.Y - 0.5f) * heightWorld);

            Vector2 centerWorld = textWorldTransform.PositionWorld - RotateVector(anchorOffset, textWorldTransform.RotationRadians);
            Vector2 halfSizeWorld = new Vector2(widthWorld * 0.5f, heightWorld * 0.5f);

            Vector2 axisX = RotateVector(new Vector2(halfSizeWorld.X, 0f), textWorldTransform.RotationRadians);
            Vector2 axisY = RotateVector(new Vector2(0f, halfSizeWorld.Y), textWorldTransform.RotationRadians);

            Vector2 c0 = centerWorld - axisX - axisY;
            Vector2 c1 = centerWorld + axisX - axisY;
            Vector2 c2 = centerWorld + axisX + axisY;
            Vector2 c3 = centerWorld - axisX + axisY;

            AccumulateBoundsPointWorld(c0, ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
            AccumulateBoundsPointWorld(c1, ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
            AccumulateBoundsPointWorld(c2, ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
            AccumulateBoundsPointWorld(c3, ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
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

        ShapeKind kind = GetShapeKindEcs(entity, out RectGeometryComponentHandle rectGeometryHandle, out CircleGeometryComponentHandle circleGeometryHandle, out PathComponentHandle pathHandle);
        if (kind == ShapeKind.Polygon && !pathHandle.IsNull)
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
                    AccumulateBoundsPointWorld(pWorld, ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
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

            AccumulateBoundsPointWorld(c0, ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
            AccumulateBoundsPointWorld(c1, ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
            AccumulateBoundsPointWorld(c2, ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
            AccumulateBoundsPointWorld(c3, ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
        }

        var shapeParentWorldTransform = new WorldTransform(
            shapeWorldTransform.PositionWorld,
            shapeWorldTransform.ScaleWorld,
            shapeWorldTransform.RotationRadians,
            shapeWorldTransform.IsVisible);

        ReadOnlySpan<EntityId> children = _world.GetChildren(entity);
        for (int i = 0; i < children.Length; i++)
        {
            EntityId child = children[i];
            AccumulateSubtreeBoundsWorldEcs(child, shapeParentWorldTransform, ref hasBounds, ref minX, ref minY, ref maxX, ref maxY);
        }
    }

    private static void AccumulateBoundsPointWorld(Vector2 p, ref bool hasBounds, ref float minX, ref float minY, ref float maxX, ref float maxY)
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
