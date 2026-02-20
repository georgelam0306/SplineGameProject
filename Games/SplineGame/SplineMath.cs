using FixedMath;

namespace SplineGame;

public static class SplineMath
{
    private const float MinimumTangentMagnitude = 0.0001f;

    public static Fixed64 Wrap01(Fixed64 value)
    {
        while (value < Fixed64.Zero)
        {
            value += Fixed64.OneValue;
        }

        while (value >= Fixed64.OneValue)
        {
            value -= Fixed64.OneValue;
        }

        return value;
    }

    public static void SamplePositionAndTangent(
        ReadOnlySpan<SplineCompiledLevel.Point> points,
        float paramT,
        out float x,
        out float y,
        out float tangentX,
        out float tangentY)
    {
        if (points.Length <= 0)
        {
            x = 0f;
            y = 0f;
            tangentX = 1f;
            tangentY = 0f;
            return;
        }

        if (points.Length == 1)
        {
            x = points[0].X;
            y = points[0].Y;
            tangentX = 1f;
            tangentY = 0f;
            return;
        }

        float wrappedT = paramT - MathF.Floor(paramT);
        if (wrappedT < 0f)
        {
            wrappedT += 1f;
        }

        int segmentCount = points.Length;
        float segmentT = wrappedT * segmentCount;
        int segmentIndex = (int)segmentT;
        if (segmentIndex >= segmentCount)
        {
            segmentIndex = segmentCount - 1;
        }

        int nextSegmentIndex = segmentIndex + 1;
        if (nextSegmentIndex >= segmentCount)
        {
            nextSegmentIndex = 0;
        }

        float localT = segmentT - segmentIndex;
        ref readonly SplineCompiledLevel.Point startPoint = ref points[segmentIndex];
        ref readonly SplineCompiledLevel.Point endPoint = ref points[nextSegmentIndex];

        float p0x = startPoint.X;
        float p0y = startPoint.Y;
        float p1x = startPoint.X + startPoint.TangentOutX;
        float p1y = startPoint.Y + startPoint.TangentOutY;
        float p2x = endPoint.X + endPoint.TangentInX;
        float p2y = endPoint.Y + endPoint.TangentInY;
        float p3x = endPoint.X;
        float p3y = endPoint.Y;

        x = Cubic(p0x, p1x, p2x, p3x, localT);
        y = Cubic(p0y, p1y, p2y, p3y, localT);
        tangentX = CubicDerivative(p0x, p1x, p2x, p3x, localT);
        tangentY = CubicDerivative(p0y, p1y, p2y, p3y, localT);

        float tangentMagnitude = MathF.Sqrt((tangentX * tangentX) + (tangentY * tangentY));
        if (tangentMagnitude > MinimumTangentMagnitude)
        {
            tangentX /= tangentMagnitude;
            tangentY /= tangentMagnitude;
            return;
        }

        tangentX = 1f;
        tangentY = 0f;
    }

    private static float Cubic(float p0, float p1, float p2, float p3, float t)
    {
        float inverseT = 1f - t;
        float inverseTSquared = inverseT * inverseT;
        float inverseTCubed = inverseTSquared * inverseT;
        float tSquared = t * t;
        float tCubed = tSquared * t;
        return (inverseTCubed * p0) + (3f * inverseTSquared * t * p1) + (3f * inverseT * tSquared * p2) + (tCubed * p3);
    }

    private static float CubicDerivative(float p0, float p1, float p2, float p3, float t)
    {
        float inverseT = 1f - t;
        float a = 3f * inverseT * inverseT * (p1 - p0);
        float b = 6f * inverseT * t * (p2 - p1);
        float c = 3f * t * t * (p3 - p2);
        return a + b + c;
    }
}
