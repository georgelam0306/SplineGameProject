using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DerpLib.Sdf;

/// <summary>
/// Metadata for a polyline segment in the shared point buffer.
/// Contains bounding box for early-out and indices into the point buffer.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct SdfPolylineHeader
{
    /// <summary>Bounding box minimum (for shader early-out).</summary>
    public Vector2 BoundsMin;

    /// <summary>Bounding box maximum (for shader early-out).</summary>
    public Vector2 BoundsMax;

    /// <summary>Start index into the point buffer.</summary>
    public uint StartIndex;

    /// <summary>Number of points in this polyline.</summary>
    public uint PointCount;

    /// <summary>Size of this struct in bytes (24).</summary>
    public const int SizeInBytes = 24; // 2*vec2 + 2*uint = 16 + 8

    public SdfPolylineHeader(uint startIndex, uint pointCount, Vector2 boundsMin, Vector2 boundsMax)
    {
        StartIndex = startIndex;
        PointCount = pointCount;
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
    }
}

/// <summary>
/// Helper for building polyline point data.
/// Computes bounds and provides factory methods.
/// </summary>
public static class SdfPolylineBuilder
{
    /// <summary>
    /// Compute bounding box for a set of points.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (Vector2 min, Vector2 max) ComputeBounds(ReadOnlySpan<Vector2> points)
    {
        if (points.Length == 0)
            return (Vector2.Zero, Vector2.Zero);

        Vector2 min = points[0];
        Vector2 max = points[0];

        for (int i = 1; i < points.Length; i++)
        {
            min = Vector2.Min(min, points[i]);
            max = Vector2.Max(max, points[i]);
        }

        return (min, max);
    }

    /// <summary>
    /// Generate points for a regular polygon.
    /// </summary>
    /// <param name="buffer">Buffer to write points into.</param>
    /// <param name="center">Center of the polygon.</param>
    /// <param name="radius">Distance from center to vertices.</param>
    /// <param name="sides">Number of sides.</param>
    /// <param name="rotation">Rotation in radians.</param>
    /// <param name="closed">If true, duplicates first point at end.</param>
    /// <returns>Number of points written.</returns>
    public static int WritePolygon(Span<Vector2> buffer, Vector2 center, float radius, int sides, float rotation = 0f, bool closed = true)
    {
        int needed = closed ? sides + 1 : sides;
        if (buffer.Length < needed) return 0;

        for (int i = 0; i < sides; i++)
        {
            float angle = rotation + i * MathF.PI * 2f / sides;
            buffer[i] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }

        if (closed)
        {
            buffer[sides] = buffer[0];
            return sides + 1;
        }

        return sides;
    }

    /// <summary>
    /// Generate points for a star shape.
    /// </summary>
    public static int WriteStar(Span<Vector2> buffer, Vector2 center, float outerRadius, float innerRadius, int points, float rotation = 0f)
    {
        int vertices = points * 2;
        int needed = vertices + 1; // +1 to close
        if (buffer.Length < needed) return 0;

        for (int i = 0; i < vertices; i++)
        {
            float angle = rotation + i * MathF.PI / points;
            float r = (i % 2 == 0) ? outerRadius : innerRadius;
            buffer[i] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * r;
        }

        buffer[vertices] = buffer[0]; // Close
        return vertices + 1;
    }

    /// <summary>
    /// Generate points for an arc.
    /// </summary>
    public static int WriteArc(Span<Vector2> buffer, Vector2 center, float radius, float startAngle, float endAngle, int segments)
    {
        if (buffer.Length < segments) return 0;

        for (int i = 0; i < segments; i++)
        {
            float t = i / (float)(segments - 1);
            float angle = startAngle + t * (endAngle - startAngle);
            buffer[i] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }

        return segments;
    }

    /// <summary>
    /// Generate points for a waveform/graph.
    /// </summary>
    public static int WriteWaveform(Span<Vector2> buffer, ReadOnlySpan<float> samples, float startX, float width, float baseY, float amplitude = 1f)
    {
        int count = Math.Min(samples.Length, buffer.Length);
        if (count == 0) return 0;

        float step = count > 1 ? width / (count - 1) : 0;

        for (int i = 0; i < count; i++)
        {
            float x = startX + i * step;
            float y = baseY + samples[i] * amplitude;
            buffer[i] = new Vector2(x, y);
        }

        return count;
    }

    /// <summary>
    /// Generate points for a horizontal graph (useful for profiler).
    /// </summary>
    public static int WriteGraph(Span<Vector2> buffer, ReadOnlySpan<float> values, float x, float y, float width, float height, float minValue = 0f, float maxValue = 1f)
    {
        int count = Math.Min(values.Length, buffer.Length);
        if (count == 0) return 0;

        float step = count > 1 ? width / (count - 1) : 0;
        float range = maxValue - minValue;
        if (range < 0.0001f) range = 1f;

        for (int i = 0; i < count; i++)
        {
            float px = x + i * step;
            float normalized = (values[i] - minValue) / range;
            float py = y + height - normalized * height; // Y-down: higher value = lower Y
            buffer[i] = new Vector2(px, py);
        }

        return count;
    }

    /// <summary>
    /// Generate points for a rectangle.
    /// </summary>
    public static int WriteRectangle(Span<Vector2> buffer, Vector2 center, float halfWidth, float halfHeight)
    {
        if (buffer.Length < 5) return 0;

        buffer[0] = center + new Vector2(-halfWidth, -halfHeight);
        buffer[1] = center + new Vector2(halfWidth, -halfHeight);
        buffer[2] = center + new Vector2(halfWidth, halfHeight);
        buffer[3] = center + new Vector2(-halfWidth, halfHeight);
        buffer[4] = buffer[0]; // Close

        return 5;
    }
}
