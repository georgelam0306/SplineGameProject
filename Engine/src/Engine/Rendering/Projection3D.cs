using System;
using System.Numerics;

namespace DerpLib.Rendering;

public static class Projection3D
{
    public static bool TryWorldToTarget(Matrix4x4 viewProjection, float targetWidth, float targetHeight, Vector3 world, out Vector2 targetPos)
    {
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
        if (clip.W <= 0f)
        {
            targetPos = default;
            return false;
        }

        float invW = 1f / clip.W;
        float ndcX = clip.X * invW;
        float ndcY = clip.Y * invW;

        if (!float.IsFinite(ndcX) || !float.IsFinite(ndcY))
        {
            targetPos = default;
            return false;
        }

        // NDC is (-1..+1). Y is already flipped for Vulkan by Camera3D.GetProjectionMatrix.
        float x = (ndcX * 0.5f + 0.5f) * targetWidth;
        float y = (ndcY * 0.5f + 0.5f) * targetHeight;
        targetPos = new Vector2(x, y);
        return true;
    }

    public static bool TryProjectAabbToTargetRect(Matrix4x4 viewProjection, float targetWidth, float targetHeight, BoundingBox aabb, out Rectangle rect)
    {
        bool hasAny = false;
        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;

        Vector3 min = aabb.Min;
        Vector3 max = aabb.Max;

        // 8 corners.
        TryIncludeCorner(viewProjection, targetWidth, targetHeight, new Vector3(min.X, min.Y, min.Z), ref hasAny, ref minX, ref minY, ref maxX, ref maxY);
        TryIncludeCorner(viewProjection, targetWidth, targetHeight, new Vector3(max.X, min.Y, min.Z), ref hasAny, ref minX, ref minY, ref maxX, ref maxY);
        TryIncludeCorner(viewProjection, targetWidth, targetHeight, new Vector3(min.X, max.Y, min.Z), ref hasAny, ref minX, ref minY, ref maxX, ref maxY);
        TryIncludeCorner(viewProjection, targetWidth, targetHeight, new Vector3(max.X, max.Y, min.Z), ref hasAny, ref minX, ref minY, ref maxX, ref maxY);
        TryIncludeCorner(viewProjection, targetWidth, targetHeight, new Vector3(min.X, min.Y, max.Z), ref hasAny, ref minX, ref minY, ref maxX, ref maxY);
        TryIncludeCorner(viewProjection, targetWidth, targetHeight, new Vector3(max.X, min.Y, max.Z), ref hasAny, ref minX, ref minY, ref maxX, ref maxY);
        TryIncludeCorner(viewProjection, targetWidth, targetHeight, new Vector3(min.X, max.Y, max.Z), ref hasAny, ref minX, ref minY, ref maxX, ref maxY);
        TryIncludeCorner(viewProjection, targetWidth, targetHeight, new Vector3(max.X, max.Y, max.Z), ref hasAny, ref minX, ref minY, ref maxX, ref maxY);

        if (!hasAny || minX >= maxX || minY >= maxY)
        {
            rect = default;
            return false;
        }

        // Reject if completely offscreen.
        if (maxX < 0f || maxY < 0f || minX > targetWidth || minY > targetHeight)
        {
            rect = default;
            return false;
        }

        // Clamp for stable overlay rectangles when partially offscreen.
        float clampedMinX = Math.Clamp(minX, 0f, targetWidth);
        float clampedMinY = Math.Clamp(minY, 0f, targetHeight);
        float clampedMaxX = Math.Clamp(maxX, 0f, targetWidth);
        float clampedMaxY = Math.Clamp(maxY, 0f, targetHeight);

        if (clampedMinX >= clampedMaxX || clampedMinY >= clampedMaxY)
        {
            rect = default;
            return false;
        }

        rect = new Rectangle(clampedMinX, clampedMinY, clampedMaxX - clampedMinX, clampedMaxY - clampedMinY);
        return true;
    }

    private static bool TryIncludeCorner(
        Matrix4x4 viewProjection,
        float targetWidth,
        float targetHeight,
        Vector3 corner,
        ref bool hasAny,
        ref float minX,
        ref float minY,
        ref float maxX,
        ref float maxY)
    {
        if (!TryWorldToTarget(viewProjection, targetWidth, targetHeight, corner, out Vector2 pos))
        {
            return false;
        }

        hasAny = true;
        minX = MathF.Min(minX, pos.X);
        minY = MathF.Min(minY, pos.Y);
        maxX = MathF.Max(maxX, pos.X);
        maxY = MathF.Max(maxY, pos.Y);
        return true;
    }

    public static bool TryScreenToWorldRay(Matrix4x4 viewProjection, float targetWidth, float targetHeight, Vector2 screen, out Ray3D ray)
    {
        if (!Matrix4x4.Invert(viewProjection, out Matrix4x4 invViewProjection))
        {
            ray = default;
            return false;
        }

        float ndcX = (screen.X / targetWidth) * 2f - 1f;
        float ndcY = (screen.Y / targetHeight) * 2f - 1f;

        // Depth range is assumed 0..1 (Vulkan/D3D style).
        Vector4 nearClip = new Vector4(ndcX, ndcY, 0f, 1f);
        Vector4 farClip = new Vector4(ndcX, ndcY, 1f, 1f);

        Vector4 nearWorld4 = Vector4.Transform(nearClip, invViewProjection);
        Vector4 farWorld4 = Vector4.Transform(farClip, invViewProjection);

        if (nearWorld4.W == 0f || farWorld4.W == 0f)
        {
            ray = default;
            return false;
        }

        Vector3 nearWorld = new Vector3(nearWorld4.X, nearWorld4.Y, nearWorld4.Z) / nearWorld4.W;
        Vector3 farWorld = new Vector3(farWorld4.X, farWorld4.Y, farWorld4.Z) / farWorld4.W;

        Vector3 dir = farWorld - nearWorld;
        float lenSq = dir.LengthSquared();
        if (lenSq <= 0f || !float.IsFinite(lenSq))
        {
            ray = default;
            return false;
        }

        dir = Vector3.Normalize(dir);
        ray = new Ray3D(nearWorld, dir);
        return true;
    }
}
