using System.Runtime.CompilerServices;

namespace DerpLib.Sdf;

/// <summary>
/// Utility for walking along curves and marking affected tiles.
/// Uses Bresenham/Wu-style walking for efficient tile coverage detection.
/// Zero-allocation design - takes arrays directly instead of callbacks.
/// </summary>
internal static class CurveTileWalker
{
    /// <summary>
    /// Walks a line from (x1,y1) to (x2,y2) and marks affected tiles.
    /// Uses Bresenham-style walking with thickness expansion.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WalkLine(
        float x1, float y1, float x2, float y2,
        float thickness, float glowRadius, float softEdge,
        int tileSize, int tilesX, int tilesY,
        List<ushort>[] tiles,
        int[] visitedStamp, int visitId,
        int[] tileTouchedStamp, int tileTouchedId,
        int[] touchedTiles, ref int touchedTileCount,
        int cmdIdx)
    {
        float radius = thickness + glowRadius + softEdge;
        int tileRadius = (int)MathF.Ceiling(radius / tileSize);

        float dx = x2 - x1;
        float dy = y2 - y1;
        float length = MathF.Sqrt(dx * dx + dy * dy);

        if (length < 0.001f)
        {
            MarkTileWithRadius((int)x1, (int)y1, tileRadius, tileSize, tilesX, tilesY,
                tiles, visitedStamp, visitId,
                tileTouchedStamp, tileTouchedId, touchedTiles, ref touchedTileCount,
                cmdIdx);
            return;
        }

        float stepSize = tileSize * 0.5f;
        int steps = (int)MathF.Ceiling(length / stepSize);

        float invLength = 1f / length;
        float stepX = dx * invLength * stepSize;
        float stepY = dy * invLength * stepSize;

        float px = x1;
        float py = y1;

        for (int i = 0; i <= steps; i++)
        {
            MarkTileWithRadius((int)px, (int)py, tileRadius, tileSize, tilesX, tilesY,
                tiles, visitedStamp, visitId,
                tileTouchedStamp, tileTouchedId, touchedTiles, ref touchedTileCount,
                cmdIdx);
            px += stepX;
            py += stepY;
        }
    }

    /// <summary>
    /// Walks a quadratic bezier curve and marks affected tiles.
    /// </summary>
    public static void WalkBezier(
        float x0, float y0,
        float x1, float y1,
        float x2, float y2,
        float thickness, float glowRadius, float softEdge,
        int tileSize, int tilesX, int tilesY,
        List<ushort>[] tiles,
        int[] visitedStamp, int visitId,
        int[] tileTouchedStamp, int tileTouchedId,
        int[] touchedTiles, ref int touchedTileCount,
        int cmdIdx)
    {
        float radius = thickness + glowRadius + softEdge;
        int tileRadius = (int)MathF.Ceiling(radius / tileSize);

        float chordLength = Distance(x0, y0, x2, y2);
        float controlDist = Distance(x0, y0, x1, y1) + Distance(x1, y1, x2, y2);
        float approxLength = (chordLength + controlDist) * 0.5f;

        float stepSize = tileSize * 0.5f;
        int steps = Math.Max(4, (int)MathF.Ceiling(approxLength / stepSize));
        float dt = 1f / steps;

        for (int i = 0; i <= steps; i++)
        {
            float t = i * dt;
            var (px, py) = EvalQuadraticBezier(x0, y0, x1, y1, x2, y2, t);
            MarkTileWithRadius((int)px, (int)py, tileRadius, tileSize, tilesX, tilesY,
                tiles, visitedStamp, visitId,
                tileTouchedStamp, tileTouchedId, touchedTiles, ref touchedTileCount,
                cmdIdx);
        }
    }

    /// <summary>
    /// Walks a polyline and marks affected tiles.
    /// </summary>
    public static void WalkPolyline(
        ReadOnlySpan<System.Numerics.Vector2> points,
        float thickness, float glowRadius, float softEdge,
        int tileSize, int tilesX, int tilesY,
        List<ushort>[] tiles,
        int[] visitedStamp, int visitId,
        int[] tileTouchedStamp, int tileTouchedId,
        int[] touchedTiles, ref int touchedTileCount,
        int cmdIdx)
    {
        if (points.Length < 2) return;

        for (int i = 0; i < points.Length - 1; i++)
        {
            WalkLine(
                points[i].X, points[i].Y,
                points[i + 1].X, points[i + 1].Y,
                thickness, glowRadius, softEdge,
                tileSize, tilesX, tilesY,
                tiles, visitedStamp, visitId,
                tileTouchedStamp, tileTouchedId, touchedTiles, ref touchedTileCount,
                cmdIdx);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void MarkTileWithRadius(
        int px, int py, int tileRadius,
        int tileSize, int tilesX, int tilesY,
        List<ushort>[] tiles,
        int[] visitedStamp, int visitId,
        int[] tileTouchedStamp, int tileTouchedId,
        int[] touchedTiles, ref int touchedTileCount,
        int cmdIdx)
    {
        int centerTileX = px / tileSize;
        int centerTileY = py / tileSize;

        for (int dy = -tileRadius; dy <= tileRadius; dy++)
        {
            int ty = centerTileY + dy;
            if (ty < 0 || ty >= tilesY) continue;

            for (int dx = -tileRadius; dx <= tileRadius; dx++)
            {
                int tx = centerTileX + dx;
                if (tx < 0 || tx >= tilesX) continue;

                int tileIdx = ty * tilesX + tx;
                if (visitedStamp[tileIdx] != visitId)
                {
                    visitedStamp[tileIdx] = visitId;
                    if (tileTouchedStamp[tileIdx] != tileTouchedId)
                    {
                        tileTouchedStamp[tileIdx] = tileTouchedId;
                        touchedTiles[touchedTileCount++] = tileIdx;
                    }
                    tiles[tileIdx].Add((ushort)cmdIdx);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Distance(float x1, float y1, float x2, float y2)
    {
        float dx = x2 - x1;
        float dy = y2 - y1;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (float x, float y) EvalQuadraticBezier(
        float x0, float y0, float x1, float y1, float x2, float y2, float t)
    {
        float mt = 1f - t;
        float mt2 = mt * mt;
        float t2 = t * t;
        float tmt2 = 2f * mt * t;

        return (
            mt2 * x0 + tmt2 * x1 + t2 * x2,
            mt2 * y0 + tmt2 * y1 + t2 * y2
        );
    }
}
