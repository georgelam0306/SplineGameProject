using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Derp.Doc.Tables;

public static class SplineUtils
{
    private const float DuplicateTimeTolerance = 0.000001f;
    private const float DefaultTangentWeight = 1f / 3f;
    private const float MinTangentWeight = 0.001f;
    private const float MaxTangentWeight = 1f;

    public readonly struct SplinePoint
    {
        public readonly float Time;
        public readonly float Value;
        public readonly float TangentIn;
        public readonly float TangentOut;
        public readonly float TangentInWeight;
        public readonly float TangentOutWeight;

        public SplinePoint(
            float time,
            float value,
            float tangentIn,
            float tangentOut,
            float tangentInWeight = DefaultTangentWeight,
            float tangentOutWeight = DefaultTangentWeight)
        {
            Time = time;
            Value = value;
            TangentIn = tangentIn;
            TangentOut = tangentOut;
            TangentInWeight = tangentInWeight;
            TangentOutWeight = tangentOutWeight;
        }
    }

    private static readonly SplinePoint[] LinearDefaultPoints =
    [
        new SplinePoint(0f, 0f, 0f, 1f),
        new SplinePoint(1f, 1f, 1f, 0f),
    ];

    public static readonly string DefaultSplineJson = Serialize(LinearDefaultPoints);

    public static string Serialize(ReadOnlySpan<SplinePoint> points)
    {
        if (points.Length <= 0)
        {
            return "[]";
        }

        SplinePoint[] normalizedPoints = NormalizeToArray(points);
        if (normalizedPoints.Length <= 0)
        {
            return "[]";
        }

        var jsonBuilder = new StringBuilder(normalizedPoints.Length * 84);
        jsonBuilder.Append('[');
        for (int pointIndex = 0; pointIndex < normalizedPoints.Length; pointIndex++)
        {
            if (pointIndex > 0)
            {
                jsonBuilder.Append(',');
            }

            SplinePoint point = normalizedPoints[pointIndex];
            jsonBuilder.Append("{\"t\":");
            AppendFloat(jsonBuilder, point.Time);
            jsonBuilder.Append(",\"v\":");
            AppendFloat(jsonBuilder, point.Value);
            jsonBuilder.Append(",\"ti\":");
            AppendFloat(jsonBuilder, point.TangentIn);
            jsonBuilder.Append(",\"to\":");
            AppendFloat(jsonBuilder, point.TangentOut);
            jsonBuilder.Append(",\"wi\":");
            AppendFloat(jsonBuilder, point.TangentInWeight);
            jsonBuilder.Append(",\"wo\":");
            AppendFloat(jsonBuilder, point.TangentOutWeight);
            jsonBuilder.Append('}');
        }

        jsonBuilder.Append(']');
        return jsonBuilder.ToString();
    }

    public static SplinePoint[] Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<SplinePoint>();
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<SplinePoint>();
            }

            int estimatedCount = root.GetArrayLength();
            if (estimatedCount <= 0)
            {
                return Array.Empty<SplinePoint>();
            }

            var parsedPoints = new List<SplinePoint>(estimatedCount);
            foreach (JsonElement pointElement in root.EnumerateArray())
            {
                if (pointElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                float time = 0f;
                float value = 0f;
                float tangentIn = 0f;
                float tangentOut = 0f;
                float tangentInWeight = DefaultTangentWeight;
                float tangentOutWeight = DefaultTangentWeight;

                if (pointElement.TryGetProperty("t", out JsonElement timeElement) &&
                    TryReadSingle(timeElement, out float parsedTime))
                {
                    time = parsedTime;
                }

                if (pointElement.TryGetProperty("v", out JsonElement valueElement) &&
                    TryReadSingle(valueElement, out float parsedValue))
                {
                    value = parsedValue;
                }

                if (pointElement.TryGetProperty("ti", out JsonElement tangentInElement) &&
                    TryReadSingle(tangentInElement, out float parsedTangentIn))
                {
                    tangentIn = parsedTangentIn;
                }

                if (pointElement.TryGetProperty("to", out JsonElement tangentOutElement) &&
                    TryReadSingle(tangentOutElement, out float parsedTangentOut))
                {
                    tangentOut = parsedTangentOut;
                }

                if (pointElement.TryGetProperty("wi", out JsonElement tangentInWeightElement) &&
                    TryReadSingle(tangentInWeightElement, out float parsedTangentInWeight))
                {
                    tangentInWeight = parsedTangentInWeight;
                }

                if (pointElement.TryGetProperty("wo", out JsonElement tangentOutWeightElement) &&
                    TryReadSingle(tangentOutWeightElement, out float parsedTangentOutWeight))
                {
                    tangentOutWeight = parsedTangentOutWeight;
                }

                parsedPoints.Add(new SplinePoint(
                    time,
                    value,
                    tangentIn,
                    tangentOut,
                    tangentInWeight,
                    tangentOutWeight));
            }

            if (parsedPoints.Count <= 0)
            {
                return Array.Empty<SplinePoint>();
            }

            return NormalizeToArray(parsedPoints.ToArray());
        }
        catch
        {
            return Array.Empty<SplinePoint>();
        }
    }

    public static float Evaluate(ReadOnlySpan<SplinePoint> points, float t)
    {
        if (points.Length <= 0)
        {
            return 0f;
        }

        if (points.Length == 1)
        {
            return CoerceFinite(points[0].Value);
        }

        float clampedT = Clamp01Finite(t);
        int lastPointIndex = points.Length - 1;

        if (clampedT <= points[0].Time)
        {
            return CoerceFinite(points[0].Value);
        }

        if (clampedT >= points[lastPointIndex].Time)
        {
            return CoerceFinite(points[lastPointIndex].Value);
        }

        int segmentStartIndex = 0;
        for (int pointIndex = 0; pointIndex < lastPointIndex; pointIndex++)
        {
            segmentStartIndex = pointIndex;
            if (points[pointIndex + 1].Time >= clampedT)
            {
                break;
            }
        }

        SplinePoint point0 = points[segmentStartIndex];
        SplinePoint point1 = points[Math.Min(segmentStartIndex + 1, lastPointIndex)];
        return EvaluateBezierSegmentAtTime(in point0, in point1, clampedT);
    }

    private static float EvaluateBezierSegmentAtTime(in SplinePoint point0, in SplinePoint point1, float time)
    {
        GetBezierControlPoints(
            in point0,
            in point1,
            out float x0,
            out float y0,
            out float x1,
            out float y1,
            out float x2,
            out float y2,
            out float x3,
            out float y3);

        if (time <= x0)
        {
            return y0;
        }

        if (time >= x3)
        {
            return y3;
        }

        float u = SolveBezierParameterForX(time, x0, x1, x2, x3);
        return CubicBezierScalar(y0, y1, y2, y3, u);
    }

    private static void GetBezierControlPoints(
        in SplinePoint point0,
        in SplinePoint point1,
        out float x0,
        out float y0,
        out float x1,
        out float y1,
        out float x2,
        out float y2,
        out float x3,
        out float y3)
    {
        x0 = point0.Time;
        y0 = point0.Value;
        x3 = point1.Time;
        y3 = point1.Value;

        float segmentLength = x3 - x0;
        if (!float.IsFinite(segmentLength) || segmentLength <= DuplicateTimeTolerance)
        {
            segmentLength = DuplicateTimeTolerance;
        }

        float outgoingWeight = NormalizeWeight(point0.TangentOutWeight);
        float incomingWeight = NormalizeWeight(point1.TangentInWeight);

        x1 = x0 + (segmentLength * outgoingWeight);
        x2 = x3 - (segmentLength * incomingWeight);
        y1 = y0 + (point0.TangentOut / 3f);
        y2 = y3 - (point1.TangentIn / 3f);
    }

    private static float SolveBezierParameterForX(float targetX, float x0, float x1, float x2, float x3)
    {
        const int coarseSamples = 12;
        float bestU = 0f;
        float bestError = float.MaxValue;

        for (int sampleIndex = 0; sampleIndex <= coarseSamples; sampleIndex++)
        {
            float u = sampleIndex / (float)coarseSamples;
            float x = CubicBezierScalar(x0, x1, x2, x3, u);
            float error = MathF.Abs(x - targetX);
            if (error < bestError)
            {
                bestError = error;
                bestU = u;
            }
        }

        for (int iteration = 0; iteration < 8; iteration++)
        {
            float x = CubicBezierScalar(x0, x1, x2, x3, bestU);
            float derivative = CubicBezierDerivative(x0, x1, x2, x3, bestU);
            if (!float.IsFinite(derivative) || MathF.Abs(derivative) < 0.000001f)
            {
                break;
            }

            float nextU = Math.Clamp(bestU - ((x - targetX) / derivative), 0f, 1f);
            if (MathF.Abs(nextU - bestU) < 0.000001f)
            {
                bestU = nextU;
                break;
            }

            bestU = nextU;
        }

        return bestU;
    }

    private static float CubicBezierScalar(float p0, float p1, float p2, float p3, float u)
    {
        float oneMinusU = 1f - u;
        float oneMinusUSquared = oneMinusU * oneMinusU;
        float uSquared = u * u;
        return (oneMinusUSquared * oneMinusU * p0)
            + (3f * oneMinusUSquared * u * p1)
            + (3f * oneMinusU * uSquared * p2)
            + (uSquared * u * p3);
    }

    private static float CubicBezierDerivative(float p0, float p1, float p2, float p3, float u)
    {
        float oneMinusU = 1f - u;
        float term0 = 3f * oneMinusU * oneMinusU * (p1 - p0);
        float term1 = 6f * oneMinusU * u * (p2 - p1);
        float term2 = 3f * u * u * (p3 - p2);
        return term0 + term1 + term2;
    }

    private static bool TryReadSingle(JsonElement element, out float value)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetSingle(out value);
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return float.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        value = 0f;
        return false;
    }

    private static SplinePoint[] NormalizeToArray(ReadOnlySpan<SplinePoint> points)
    {
        if (points.Length <= 0)
        {
            return Array.Empty<SplinePoint>();
        }

        SplinePoint[] normalizedPoints = points.ToArray();
        for (int pointIndex = 0; pointIndex < normalizedPoints.Length; pointIndex++)
        {
            SplinePoint point = normalizedPoints[pointIndex];
            normalizedPoints[pointIndex] = new SplinePoint(
                Clamp01Finite(point.Time),
                CoerceFinite(point.Value),
                CoerceFinite(point.TangentIn),
                CoerceFinite(point.TangentOut),
                NormalizeWeight(point.TangentInWeight),
                NormalizeWeight(point.TangentOutWeight));
        }

        for (int outerIndex = 1; outerIndex < normalizedPoints.Length; outerIndex++)
        {
            SplinePoint keyPoint = normalizedPoints[outerIndex];
            int innerIndex = outerIndex - 1;
            while (innerIndex >= 0 && normalizedPoints[innerIndex].Time > keyPoint.Time)
            {
                normalizedPoints[innerIndex + 1] = normalizedPoints[innerIndex];
                innerIndex--;
            }

            normalizedPoints[innerIndex + 1] = keyPoint;
        }

        int writeIndex = 0;
        for (int readIndex = 1; readIndex < normalizedPoints.Length; readIndex++)
        {
            if (Math.Abs(normalizedPoints[readIndex].Time - normalizedPoints[writeIndex].Time) <= DuplicateTimeTolerance)
            {
                normalizedPoints[writeIndex] = normalizedPoints[readIndex];
                continue;
            }

            writeIndex++;
            normalizedPoints[writeIndex] = normalizedPoints[readIndex];
        }

        int finalCount = writeIndex + 1;
        if (finalCount == normalizedPoints.Length)
        {
            return normalizedPoints;
        }

        var result = new SplinePoint[finalCount];
        for (int pointIndex = 0; pointIndex < finalCount; pointIndex++)
        {
            result[pointIndex] = normalizedPoints[pointIndex];
        }

        return result;
    }

    private static float NormalizeWeight(float weight)
    {
        if (!float.IsFinite(weight) || weight <= DuplicateTimeTolerance)
        {
            return DefaultTangentWeight;
        }

        return Math.Clamp(weight, MinTangentWeight, MaxTangentWeight);
    }

    private static float Clamp01Finite(float value)
    {
        if (!float.IsFinite(value))
        {
            return 0f;
        }

        return Math.Clamp(value, 0f, 1f);
    }

    private static float CoerceFinite(float value)
    {
        return float.IsFinite(value) ? value : 0f;
    }

    private static void AppendFloat(StringBuilder builder, float value)
    {
        float safeValue = CoerceFinite(value);
        builder.Append(safeValue.ToString("G9", CultureInfo.InvariantCulture));
    }
}
