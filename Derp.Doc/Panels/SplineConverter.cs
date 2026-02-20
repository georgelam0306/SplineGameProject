using Derp.Doc.Tables;
using DerpLib.ImGui.Widgets;

namespace Derp.Doc.Panels;

internal static class SplineConverter
{
    public static Curve JsonToCurve(string? json)
    {
        SplineUtils.SplinePoint[] points = SplineUtils.Deserialize(json);
        return PointsToCurve(points);
    }

    public static string CurveToJson(ref Curve curve)
    {
        if (curve.Count <= 0)
        {
            return SplineUtils.DefaultSplineJson;
        }

        Span<SplineUtils.SplinePoint> points = stackalloc SplineUtils.SplinePoint[Curve.MaxPoints];
        int pointCount = Math.Min(curve.Count, Curve.MaxPoints);
        for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
        {
            ref CurvePoint sourcePoint = ref curve[pointIndex];
            points[pointIndex] = new SplineUtils.SplinePoint(
                sourcePoint.Time,
                sourcePoint.Value,
                sourcePoint.TangentIn,
                sourcePoint.TangentOut,
                sourcePoint.TangentInWeight,
                sourcePoint.TangentOutWeight);
        }

        return SplineUtils.Serialize(points[..pointCount]);
    }

    private static Curve PointsToCurve(ReadOnlySpan<SplineUtils.SplinePoint> points)
    {
        if (points.Length <= 0)
        {
            return Curve.Linear();
        }

        var curve = new Curve();
        int maxPointCount = Math.Min(points.Length, Curve.MaxPoints);
        for (int pointIndex = 0; pointIndex < maxPointCount; pointIndex++)
        {
            SplineUtils.SplinePoint point = points[pointIndex];
            if (!curve.Add(new CurvePoint(
                point.Time,
                point.Value,
                point.TangentIn,
                point.TangentOut,
                point.TangentInWeight,
                point.TangentOutWeight)))
            {
                break;
            }
        }

        if (curve.Count <= 0)
        {
            return Curve.Linear();
        }

        return curve;
    }
}
