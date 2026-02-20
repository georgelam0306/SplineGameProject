using System;
using System.Numerics;
using System.Runtime.InteropServices;
using DerpLib.ImGui.Core;

namespace Derp.UI;

public sealed partial class UiWorkspace
{
    internal ShapeKind GetShapeKind(in Shape shape)
    {
        var view = ShapeComponent.Api.FromHandle(_propertyWorld, shape.Component);
        if (!view.IsAlive)
        {
            return ShapeKind.Rect;
        }
        return view.Kind;
    }

	    private bool TryGetPolygonPointsLocal(in Shape shape, out ReadOnlySpan<Vector2> pointsLocal)
	    {
	        if (!shape.Path.IsNull)
	        {
	            if (!TryGetPathData(shape, out ReadOnlySpan<Vector2> positionsLocal, out ReadOnlySpan<Vector2> tangentsInLocal, out ReadOnlySpan<Vector2> tangentsOutLocal, out ReadOnlySpan<int> vertexKind, out int vertexCount))
	            {
	                pointsLocal = default;
	                return false;
	            }

	            if (vertexCount < 3)
	            {
	                pointsLocal = default;
	                return false;
	            }

	            if (!PathHasAnyCurves(tangentsInLocal, tangentsOutLocal, vertexKind))
	            {
	                pointsLocal = positionsLocal.Slice(0, vertexCount);
	                return true;
	            }

	            int tessCount = TessellatePathToLocalScratch(positionsLocal, tangentsInLocal, tangentsOutLocal, vertexKind, vertexCount);
	            if (tessCount < 3)
	            {
	                pointsLocal = default;
	                return false;
	            }

	            pointsLocal = _pathTessellationLocalScratch.AsSpan(0, tessCount);
	            return true;
	        }

	        int pointCount = shape.PointCount;
	        int start = shape.PointStart;
	        if (pointCount < 3 || start < 0 || start + pointCount > _polygonPointsLocal.Count)
	        {
	            pointsLocal = default;
	            return false;
	        }

	        pointsLocal = CollectionsMarshal.AsSpan(_polygonPointsLocal).Slice(start, pointCount);
	        return true;
	    }

	    private bool TryGetPathData(
	        in Shape shape,
	        out ReadOnlySpan<Vector2> positionsLocal,
	        out ReadOnlySpan<Vector2> tangentsInLocal,
	        out ReadOnlySpan<Vector2> tangentsOutLocal,
	        out ReadOnlySpan<int> vertexKind,
	        out int vertexCount)
	    {
	        positionsLocal = default;
	        tangentsInLocal = default;
	        tangentsOutLocal = default;
	        vertexKind = default;
	        vertexCount = 0;

	        if (shape.Path.IsNull)
	        {
	            return false;
	        }

	        var view = PathComponent.Api.FromHandle(_propertyWorld, shape.Path);
	        if (!view.IsAlive)
	        {
	            return false;
	        }

	        vertexCount = view.VertexCount;
	        if (vertexCount <= 0 || vertexCount > PathComponent.MaxVertices)
	        {
	            return false;
	        }

	        positionsLocal = view.PositionLocalSpan().Slice(0, vertexCount);
	        tangentsInLocal = view.TangentInLocalSpan().Slice(0, vertexCount);
	        tangentsOutLocal = view.TangentOutLocalSpan().Slice(0, vertexCount);
	        vertexKind = view.VertexKindSpan().Slice(0, vertexCount);
	        return true;
	    }

	    private static bool PathHasAnyCurves(ReadOnlySpan<Vector2> tangentsInLocal, ReadOnlySpan<Vector2> tangentsOutLocal, ReadOnlySpan<int> vertexKind)
	    {
	        int count = vertexKind.Length;
	        for (int i = 0; i < count; i++)
	        {
	            if (vertexKind[i] != 0)
	            {
	                return true;
	            }

	            Vector2 tin = tangentsInLocal[i];
	            Vector2 tout = tangentsOutLocal[i];
	            if ((tin.X != 0f || tin.Y != 0f) || (tout.X != 0f || tout.Y != 0f))
	            {
	                return true;
	            }
	        }

	        return false;
	    }

	    private int TessellatePathToLocalScratch(
	        ReadOnlySpan<Vector2> positionsLocal,
	        ReadOnlySpan<Vector2> tangentsInLocal,
	        ReadOnlySpan<Vector2> tangentsOutLocal,
	        ReadOnlySpan<int> vertexKind,
	        int vertexCount)
	    {
	        int approxMaxPoints = vertexCount * 16 + 1;
	        EnsurePathTessellationScratchCapacity(approxMaxPoints);

	        int writeIndex = 0;
	        _pathTessellationLocalScratch[writeIndex++] = positionsLocal[0];

	        float zoom = Zoom;
	        if (zoom <= 0.0001f)
	        {
	            zoom = 1f;
	        }

	        for (int i = 0; i < vertexCount; i++)
	        {
	            int next = i + 1;
	            if (next >= vertexCount)
	            {
	                next = 0;
	            }

	            Vector2 p0 = positionsLocal[i];
	            Vector2 p3 = positionsLocal[next];

	            Vector2 tOut = vertexKind[i] == 0 ? Vector2.Zero : tangentsOutLocal[i];
	            Vector2 tIn = vertexKind[next] == 0 ? Vector2.Zero : tangentsInLocal[next];

	            bool hasCurve = (tOut.X != 0f || tOut.Y != 0f) || (tIn.X != 0f || tIn.Y != 0f);
	            if (!hasCurve)
	            {
	                _pathTessellationLocalScratch[writeIndex++] = p3;
	                continue;
	            }

	            Vector2 p1 = p0 + tOut;
	            Vector2 p2 = p3 + tIn;

	            float approxLen =
	                Vector2.Distance(p0, p1) +
	                Vector2.Distance(p1, p2) +
	                Vector2.Distance(p2, p3);

	            int steps = (int)(approxLen * zoom / 25f);
	            if (steps < 2)
	            {
	                steps = 2;
	            }
	            else if (steps > 16)
	            {
	                steps = 16;
	            }

	            for (int step = 1; step <= steps; step++)
	            {
	                float t = step / (float)steps;
	                _pathTessellationLocalScratch[writeIndex++] = EvalCubicBezier(p0, p1, p2, p3, t);
	            }
	        }

	        return writeIndex;
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

	    private void EnsurePathTessellationScratchCapacity(int needed)
	    {
	        if (_pathTessellationLocalScratch.Length >= needed)
	        {
	            return;
	        }

	        int size = _pathTessellationLocalScratch.Length;
	        if (size < 1)
	        {
	            size = 1;
	        }

	        while (size < needed)
	        {
	            size *= 2;
	        }

	        _pathTessellationLocalScratch = new Vector2[size];
	    }

    private static void GetBounds(ReadOnlySpan<Vector2> points, out float minX, out float minY, out float maxX, out float maxY)
    {
        minX = points[0].X;
        minY = points[0].Y;
        maxX = minX;
        maxY = minY;

        for (int i = 1; i < points.Length; i++)
        {
            Vector2 p = points[i];
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }
    }

    private static Vector2 TransformPolygonPointToWorld(
        Vector2 positionWorld,
        Vector2 pivotLocal,
        float rotationRadians,
        float scaleX,
        float scaleY,
        Vector2 pointLocal)
    {
        Vector2 relative = new Vector2(
            (pointLocal.X - pivotLocal.X) * scaleX,
            (pointLocal.Y - pivotLocal.Y) * scaleY);

        Vector2 rotated = RotateVector(relative, rotationRadians);
        return new Vector2(positionWorld.X + rotated.X, positionWorld.Y + rotated.Y);
    }

    private Vector2 GetPolygonPivotLocal(in Shape shape, Vector2 anchor, Vector2 boundsMinLocal, Vector2 boundsSizeLocal)
    {
        if (!shape.Path.IsNull)
        {
            var view = PathComponent.Api.FromHandle(_propertyWorld, shape.Path);
            if (view.IsAlive)
            {
                return view.PivotLocal;
            }
        }

        return new Vector2(
            boundsMinLocal.X + anchor.X * boundsSizeLocal.X,
            boundsMinLocal.Y + anchor.Y * boundsSizeLocal.Y);
    }

    internal ImRect GetShapeRectWorld(in Shape shape)
    {
        var transform = TransformComponent.Api.FromHandle(_propertyWorld, shape.Transform);
        Vector2 positionWorld = transform.Position;
        Vector2 scale = transform.Scale;

        float scaleX = scale.X;
        float scaleY = scale.Y;
        if (scaleX == 0f)
        {
            scaleX = 1f;
        }
        if (scaleY == 0f)
        {
            scaleY = 1f;
        }

        float posX = positionWorld.X;
        float posY = positionWorld.Y;
        Vector2 anchor = transform.Anchor;

        ShapeKind kind = GetShapeKind(shape);
        if (kind == ShapeKind.Polygon)
        {
            if (!TryGetPolygonPointsLocal(shape, out ReadOnlySpan<Vector2> pointsLocal))
            {
                return new ImRect(posX, posY, 0f, 0f);
            }

            GetBounds(pointsLocal, out float minLocalX, out float minLocalY, out float maxLocalX, out float maxLocalY);
            Vector2 boundsMinLocal = new Vector2(minLocalX, minLocalY);
            Vector2 boundsSizeLocal = new Vector2(maxLocalX - minLocalX, maxLocalY - minLocalY);
            Vector2 pivotLocal = GetPolygonPivotLocal(shape, anchor, boundsMinLocal, boundsSizeLocal);

            float rotationRadians = transform.Rotation * (MathF.PI / 180f);

            Vector2 firstWorld = TransformPolygonPointToWorld(positionWorld, pivotLocal, rotationRadians, scaleX, scaleY, pointsLocal[0]);
            float minXWorld = firstWorld.X;
            float minYWorld = firstWorld.Y;
            float maxXWorld = firstWorld.X;
            float maxYWorld = firstWorld.Y;

            for (int i = 1; i < pointsLocal.Length; i++)
            {
                Vector2 pWorld = TransformPolygonPointToWorld(positionWorld, pivotLocal, rotationRadians, scaleX, scaleY, pointsLocal[i]);
                if (pWorld.X < minXWorld)
                {
                    minXWorld = pWorld.X;
                }
                if (pWorld.Y < minYWorld)
                {
                    minYWorld = pWorld.Y;
                }
                if (pWorld.X > maxXWorld)
                {
                    maxXWorld = pWorld.X;
                }
                if (pWorld.Y > maxYWorld)
                {
                    maxYWorld = pWorld.Y;
                }
            }

            return ImRect.FromMinMax(minXWorld, minYWorld, maxXWorld, maxYWorld);
        }

        if (kind == ShapeKind.Rect)
        {
            var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, shape.RectGeometry);
            float width = rectGeometry.Size.X * scaleX;
            float height = rectGeometry.Size.Y * scaleY;
            float x = posX - anchor.X * width;
            float y = posY - anchor.Y * height;
            return new ImRect(x, y, width, height);
        }

        var circleGeometry = CircleGeometryComponent.Api.FromHandle(_propertyWorld, shape.CircleGeometry);
        float radius = circleGeometry.Radius;
        float scaleAvg = (scaleX + scaleY) * 0.5f;
        float diameter = radius * 2f * scaleAvg;
        float circleX = posX - anchor.X * diameter;
        float circleY = posY - anchor.Y * diameter;
        return new ImRect(circleX, circleY, diameter, diameter);
    }

    private ImRect GetShapeRectWorld(in Shape shape, in ShapeWorldTransform worldTransform)
    {
        Vector2 positionWorld = worldTransform.PositionWorld;
        Vector2 scale = worldTransform.ScaleWorld;

        float scaleX = scale.X == 0f ? 1f : scale.X;
        float scaleY = scale.Y == 0f ? 1f : scale.Y;

        float posX = positionWorld.X;
        float posY = positionWorld.Y;
        Vector2 anchor = worldTransform.Anchor;

	        ShapeKind kind = GetShapeKind(shape);
	        if (kind == ShapeKind.Polygon)
	        {
	            if (!TryGetPolygonPointsLocal(shape, out ReadOnlySpan<Vector2> pointsLocal))
	            {
	                return new ImRect(posX, posY, 0f, 0f);
	            }

	            GetBounds(pointsLocal, out float minLocalX, out float minLocalY, out float maxLocalX, out float maxLocalY);
	            Vector2 boundsMinLocal = new Vector2(minLocalX, minLocalY);
	            Vector2 boundsSizeLocal = new Vector2(maxLocalX - minLocalX, maxLocalY - minLocalY);
	            Vector2 pivotLocal = GetPolygonPivotLocal(shape, anchor, boundsMinLocal, boundsSizeLocal);

	            float rotationRadians = worldTransform.RotationRadians;

	            Vector2 firstWorld = TransformPolygonPointToWorld(positionWorld, pivotLocal, rotationRadians, scaleX, scaleY, pointsLocal[0]);
	            float minXWorld = firstWorld.X;
	            float minYWorld = firstWorld.Y;
	            float maxXWorld = firstWorld.X;
	            float maxYWorld = firstWorld.Y;

	            for (int i = 1; i < pointsLocal.Length; i++)
	            {
	                Vector2 pWorld = TransformPolygonPointToWorld(positionWorld, pivotLocal, rotationRadians, scaleX, scaleY, pointsLocal[i]);
	                if (pWorld.X < minXWorld) minXWorld = pWorld.X;
	                if (pWorld.Y < minYWorld) minYWorld = pWorld.Y;
	                if (pWorld.X > maxXWorld) maxXWorld = pWorld.X;
	                if (pWorld.Y > maxYWorld) maxYWorld = pWorld.Y;
	            }

	            return ImRect.FromMinMax(minXWorld, minYWorld, maxXWorld, maxYWorld);
	        }

        if (kind == ShapeKind.Rect)
        {
            var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, shape.RectGeometry);
            float width = rectGeometry.Size.X * scaleX;
            float height = rectGeometry.Size.Y * scaleY;
            float x = posX - anchor.X * width;
            float y = posY - anchor.Y * height;
            return new ImRect(x, y, width, height);
        }

        var circleGeometry = CircleGeometryComponent.Api.FromHandle(_propertyWorld, shape.CircleGeometry);
        float radius = circleGeometry.Radius;
        float scaleAvg = (scaleX + scaleY) * 0.5f;
        float diameter = radius * 2f * scaleAvg;
        float circleX = posX - anchor.X * diameter;
        float circleY = posY - anchor.Y * diameter;
        return new ImRect(circleX, circleY, diameter, diameter);
    }

    private void SetShapeRectWorld(ref Shape shape, ImRect rectWorld)
    {
        ShapeKind kind = GetShapeKind(shape);
        if (kind == ShapeKind.Polygon)
        {
            var transform = TransformComponent.Api.FromHandle(_propertyWorld, shape.Transform);
            ImRect currentBounds = GetShapeRectWorld(shape);
            float dx = rectWorld.X - currentBounds.X;
            float dy = rectWorld.Y - currentBounds.Y;
            if (dx != 0f || dy != 0f)
            {
                Vector2 position = transform.Position;
                transform.Position = new Vector2(position.X + dx, position.Y + dy);
            }
            return;
        }

        var nonPolygonTransform = TransformComponent.Api.FromHandle(_propertyWorld, shape.Transform);
        Vector2 anchor = nonPolygonTransform.Anchor;
        float anchorX = rectWorld.X + anchor.X * rectWorld.Width;
        float anchorY = rectWorld.Y + anchor.Y * rectWorld.Height;
        nonPolygonTransform.Position = new Vector2(anchorX, anchorY);

        Vector2 scale = nonPolygonTransform.Scale;
        float scaleX = scale.X;
        float scaleY = scale.Y;
        if (scaleX == 0f)
        {
            scaleX = 1f;
        }
        if (scaleY == 0f)
        {
            scaleY = 1f;
        }

        if (kind == ShapeKind.Rect)
        {
            var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, shape.RectGeometry);
            float width = rectWorld.Width / scaleX;
            float height = rectWorld.Height / scaleY;
            rectGeometry.Size = new Vector2(width, height);
        }
        else
        {
            var circleGeometry = CircleGeometryComponent.Api.FromHandle(_propertyWorld, shape.CircleGeometry);
            float width = rectWorld.Width / scaleX;
            float height = rectWorld.Height / scaleY;
            float size = Math.Max(width, height);
            circleGeometry.Radius = size * 0.5f;
        }
    }

    private ImRect GetTextRectWorld(in Text text)
    {
        var transform = TransformComponent.Api.FromHandle(_propertyWorld, text.Transform);
        if (!transform.IsAlive)
        {
            return ImRect.Zero;
        }

        var rectGeometry = RectGeometryComponent.Api.FromHandle(_propertyWorld, text.RectGeometry);
        if (!rectGeometry.IsAlive)
        {
            return ImRect.Zero;
        }

        Vector2 positionWorld = transform.Position;
        Vector2 scale = transform.Scale;

        float scaleX = scale.X == 0f ? 1f : scale.X;
        float scaleY = scale.Y == 0f ? 1f : scale.Y;

        Vector2 anchor = transform.Anchor;
        float width = rectGeometry.Size.X * scaleX;
        float height = rectGeometry.Size.Y * scaleY;

        float x = positionWorld.X - anchor.X * width;
        float y = positionWorld.Y - anchor.Y * height;
        return new ImRect(x, y, width, height);
    }

    private void SetTextRectWorld(ref Text text, ImRect rectWorld)
    {
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

        Vector2 anchor = transform.Anchor;
        float anchorX = rectWorld.X + anchor.X * rectWorld.Width;
        float anchorY = rectWorld.Y + anchor.Y * rectWorld.Height;
        transform.Position = new Vector2(anchorX, anchorY);

        Vector2 scale = transform.Scale;
        float scaleX = scale.X == 0f ? 1f : scale.X;
        float scaleY = scale.Y == 0f ? 1f : scale.Y;

        rectGeometry.Size = new Vector2(rectWorld.Width / scaleX, rectWorld.Height / scaleY);
    }

    private ImRect GetShapeRectCanvasForDraw(in Shape shape, Vector2 canvasOrigin, out Vector2 centerCanvas, out float rotationRadians)
    {
        var transform = TransformComponent.Api.FromHandle(_propertyWorld, shape.Transform);
        Vector2 anchor = transform.Anchor;
        rotationRadians = transform.Rotation * (MathF.PI / 180f);

        ImRect rectWorld = GetShapeRectWorld(shape);
        float widthWorld = rectWorld.Width;
        float heightWorld = rectWorld.Height;

        Vector2 anchorOffset = new Vector2(
            (anchor.X - 0.5f) * widthWorld,
            (anchor.Y - 0.5f) * heightWorld);

        Vector2 anchorPos = transform.Position;
        Vector2 centerWorld = anchorPos - RotateVector(anchorOffset, rotationRadians);

        float centerCanvasX = WorldToCanvasX(centerWorld.X, canvasOrigin);
        float centerCanvasY = WorldToCanvasY(centerWorld.Y, canvasOrigin);

        float widthCanvas = widthWorld * Zoom;
        float heightCanvas = heightWorld * Zoom;

        centerCanvas = new Vector2(centerCanvasX, centerCanvasY);
        return new ImRect(centerCanvasX - widthCanvas * 0.5f, centerCanvasY - heightCanvas * 0.5f, widthCanvas, heightCanvas);
    }

    private ImRect GetShapeRectCanvasForDraw(in Shape shape, Vector2 canvasOrigin, in ShapeWorldTransform worldTransform, out Vector2 centerCanvas, out float rotationRadians)
    {
        Vector2 anchor = worldTransform.Anchor;
        rotationRadians = worldTransform.RotationRadians;

        ImRect rectWorld = GetShapeRectWorld(shape, worldTransform);
        float widthWorld = rectWorld.Width;
        float heightWorld = rectWorld.Height;

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

}
