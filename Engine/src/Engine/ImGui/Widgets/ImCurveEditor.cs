using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using DerpLib.ImGui.Core;

namespace DerpLib.ImGui.Widgets;

/// <summary>
/// A control point on a curve with position and tangent handles.
/// </summary>
public struct CurvePoint
{
    public const float DefaultHandleWeight = 1f / 3f;

    /// <summary>Time position (0-1 normalized, X axis).</summary>
    public float Time;

    /// <summary>Value at this point (Y axis).</summary>
    public float Value;

    /// <summary>Incoming tangent slope.</summary>
    public float TangentIn;

    /// <summary>Outgoing tangent slope.</summary>
    public float TangentOut;

    /// <summary>Incoming handle X weight relative to previous segment length.</summary>
    public float TangentInWeight;

    /// <summary>Outgoing handle X weight relative to next segment length.</summary>
    public float TangentOutWeight;

    public CurvePoint(
        float time,
        float value,
        float tangentIn = 0f,
        float tangentOut = 0f,
        float tangentInWeight = DefaultHandleWeight,
        float tangentOutWeight = DefaultHandleWeight)
    {
        Time = time;
        Value = value;
        TangentIn = tangentIn;
        TangentOut = tangentOut;
        TangentInWeight = tangentInWeight;
        TangentOutWeight = tangentOutWeight;
    }

    /// <summary>Create a point with smooth (equal) tangents.</summary>
    public static CurvePoint Smooth(
        float time,
        float value,
        float tangent = 0f,
        float tangentInWeight = DefaultHandleWeight,
        float tangentOutWeight = DefaultHandleWeight)
        => new(time, value, tangent, tangent, tangentInWeight, tangentOutWeight);

    /// <summary>Create a point with broken (independent) tangents.</summary>
    public static CurvePoint Broken(
        float time,
        float value,
        float tangentIn,
        float tangentOut,
        float tangentInWeight = DefaultHandleWeight,
        float tangentOutWeight = DefaultHandleWeight)
        => new(time, value, tangentIn, tangentOut, tangentInWeight, tangentOutWeight);
}

/// <summary>
/// Fixed-capacity inline array buffer for curve points.
/// </summary>
[InlineArray(Curve.MaxPoints)]
public struct CurvePointBuffer
{
    private CurvePoint _element0;
}

/// <summary>
/// A curve with fixed-capacity storage. Blittable, memcpy-safe, zero allocation.
/// Max 16 control points.
/// </summary>
public struct Curve
{
    public const int MaxPoints = 16;

    /// <summary>Fixed storage for curve points.</summary>
    public CurvePointBuffer Points;

    /// <summary>Number of active points in the curve.</summary>
    public int Count;

    /// <summary>Access a point by index.</summary>
    public ref CurvePoint this[int index]
    {
        [UnscopedRef]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)Count)
                throw new IndexOutOfRangeException();
            return ref Points[index];
        }
    }

    /// <summary>Add a point to the curve. Returns false if at capacity.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Add(CurvePoint point)
    {
        if (Count >= MaxPoints)
            return false;
        Points[Count++] = point;
        return true;
    }

    /// <summary>Remove a point at the specified index.</summary>
    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)Count)
            return;
        for (int i = index; i < Count - 1; i++)
            Points[i] = Points[i + 1];
        Count--;
    }

    /// <summary>Clear all points.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear() => Count = 0;

    /// <summary>Get a span of the active points.</summary>
    [UnscopedRef]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<CurvePoint> AsSpan() => ((Span<CurvePoint>)Points)[..Count];

    /// <summary>Create a linear curve (straight line from 0,0 to 1,1).</summary>
    public static Curve Linear()
    {
        var curve = new Curve();
        curve.Add(new CurvePoint(0f, 0f, 0f, 1f));
        curve.Add(new CurvePoint(1f, 1f, 1f, 0f));
        return curve;
    }

    /// <summary>Create an ease-in curve (slow start, fast end).</summary>
    public static Curve EaseIn()
    {
        var curve = new Curve();
        curve.Add(new CurvePoint(0f, 0f, 0f, 0f));
        curve.Add(new CurvePoint(1f, 1f, 2f, 0f));
        return curve;
    }

    /// <summary>Create an ease-out curve (fast start, slow end).</summary>
    public static Curve EaseOut()
    {
        var curve = new Curve();
        curve.Add(new CurvePoint(0f, 0f, 0f, 2f));
        curve.Add(new CurvePoint(1f, 1f, 0f, 0f));
        return curve;
    }

    /// <summary>Create an ease-in-out curve (slow start and end).</summary>
    public static Curve EaseInOut()
    {
        var curve = new Curve();
        curve.Add(new CurvePoint(0f, 0f, 0f, 0f));
        curve.Add(new CurvePoint(1f, 1f, 0f, 0f));
        return curve;
    }

    /// <summary>Create a step curve (instant jump at midpoint).</summary>
    public static Curve Step()
    {
        var curve = new Curve();
        curve.Add(new CurvePoint(0f, 0f, 0f, 0f));
        curve.Add(new CurvePoint(0.49f, 0f, 0f, 0f));
        curve.Add(new CurvePoint(0.51f, 1f, 0f, 0f));
        curve.Add(new CurvePoint(1f, 1f, 0f, 0f));
        return curve;
    }

    /// <summary>Create a constant curve at the specified value.</summary>
    public static Curve Constant(float value)
    {
        var curve = new Curve();
        curve.Add(new CurvePoint(0f, value, 0f, 0f));
        curve.Add(new CurvePoint(1f, value, 0f, 0f));
        return curve;
    }
}

/// <summary>
/// Interactive curve editor widget for editing Bezier/Hermite curves.
/// </summary>
public static class ImCurveEditor
{
    public struct CurveView
    {
        public float ZoomX;
        public float ZoomY;
        public Vector2 PanOffset;
    }

    // State tracking
    private static readonly EditorState[] _states = new EditorState[16];
    private static readonly int[] _stateIds = new int[16];
    private static int _stateCount;

    private struct EditorState
    {
        public int SelectedPoint;
        public int DraggingPoint;
        public int DraggingTangent;
        public float ZoomX;
        public float ZoomY;
        public Vector2 PanOffset;
        public bool IsPanning;
        public Vector2 PanStart;
        public float TimeSinceLastClick; // Accumulates each frame, reset on click
        public Vector2 LastClickPos;
    }

    // Reusable buffer for sorting
    private static int[] _sortBuffer = new int[16];

    // Visual settings
    public static float GridLineAlpha = 0.15f;
    public static float SubGridLineAlpha = 0.08f;
    public static float PointRadius = 5f;
    public static float TangentHandleRadius = 4f;
    public static float TangentHandleLength = 40f;
    public static float TangentHandleWeightMin = 0.001f;
    public static float TangentHandleWeightMax = 1f;
    public static float CurveThickness = 2f;
    public static float HitTestRadius = 8f;
    public static int CurveSegments = 64;

    // Colors
    public static uint CurveColor = 0xFF4488FF;
    public static uint PointColor = 0xFFFFFFFF;
    public static uint SelectedPointColor = 0xFF00FF88;
    public static uint TangentLineColor = 0xAAFFFF00;
    public static uint TangentHandleColor = 0xFFFFCC00;
    public static uint GridColor = 0xFFFFFFFF;

    // Preview settings
    public static float PreviewWidth = 60f;
    public static float PreviewHeight = 20f;
    public static uint PreviewBackground = 0xFF1A1A1A;
    public static uint PreviewBorder = 0xFF444444;
    public static uint PreviewCurveColor = 0xFF6699FF;
    public static float PreviewCurveThickness = 1.5f;
    public static int PreviewSegments = 24;

    /// <summary>
    /// Draw a curve editor at the specified position.
    /// </summary>
    public static bool DrawAt(string label, ref Curve curve, float x, float y,
        float width, float height, float minValue = 0f, float maxValue = 1f)
    {
        return DrawAt(Im.Context.GetId(label), ref curve, x, y, width, height, minValue, maxValue);
    }

    /// <summary>
    /// Draw a curve editor with int ID.
    /// </summary>
    public static bool DrawAt(int id, ref Curve curve, float x, float y,
        float width, float height, float minValue = 0f, float maxValue = 1f)
    {
        var rect = new ImRect(x, y, width, height);
        return DrawInternalEx(id, ref curve, rect, minValue, maxValue, drawBackground: true, drawGrid: true, drawHandles: true, handleInput: true);
    }

    public static bool DrawAt(
        int id,
        ref Curve curve,
        float x,
        float y,
        float width,
        float height,
        float minValue,
        float maxValue,
        bool drawBackground,
        bool drawGrid,
        bool drawHandles,
        bool handleInput)
    {
        var rect = new ImRect(x, y, width, height);
        return DrawInternalEx(id, ref curve, rect, minValue, maxValue, drawBackground, drawGrid, drawHandles, handleInput);
    }

    public static bool DrawAt(
        int id,
        ref Curve curve,
        float x,
        float y,
        float width,
        float height,
        float minValue,
        float maxValue,
        bool drawBackground,
        bool drawGrid,
        bool drawHandles,
        bool handleInput,
        ref CurveView view)
    {
        var rect = new ImRect(x, y, width, height);
        return DrawInternalEx(id, ref curve, rect, minValue, maxValue, drawBackground, drawGrid, drawHandles, handleInput, ref view);
    }

    /// <summary>
    /// Draw a compact inline preview of the curve. Returns true if clicked.
    /// </summary>
    public static bool DrawPreview(ref Curve curve, float x, float y,
        float width = 0f, float height = 0f, float minValue = 0f, float maxValue = 1f)
    {
        if (width <= 0) width = PreviewWidth;
        if (height <= 0) height = PreviewHeight;

        var rect = new ImRect(x, y, width, height);

        // Draw background
        Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, 3f, PreviewBackground);
        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, 3f, PreviewBorder, 1f);

        // Draw curve (simplified)
        var previewClipRect = new ImRect(rect.X + 1f, rect.Y + 1f, rect.Width - 2f, rect.Height - 2f);
        if (previewClipRect.Width > 0f && previewClipRect.Height > 0f)
        {
            Im.PushClipRect(previewClipRect);
        }

        if (curve.Count >= 2)
        {
            DrawPreviewCurve(ref curve, rect, minValue, maxValue);
        }
        else if (curve.Count == 1)
        {
            float normalizedY = (curve[0].Value - minValue) / (maxValue - minValue);
            float lineY = rect.Y + rect.Height * (1f - normalizedY);
            lineY = Math.Clamp(lineY, rect.Y + 2, rect.Y + rect.Height - 2);
            Im.DrawLine(rect.X + 2, lineY, rect.X + rect.Width - 2, lineY, PreviewCurveThickness, PreviewCurveColor);
        }

        if (previewClipRect.Width > 0f && previewClipRect.Height > 0f)
        {
            Im.PopClipRect();
        }

        // Check for click
        bool hovered = rect.Contains(Im.MousePos);
        bool clicked = hovered && Im.MousePressed;

        // Draw hover highlight
        if (hovered)
        {
            Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, 3f, 0x20FFFFFF);
            Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, 3f, Im.Style.Primary, 1f);
        }

        return clicked;
    }

    /// <summary>
    /// Draw preview with string ID for tooltip tracking.
    /// </summary>
    public static bool DrawPreview(string label, ref Curve curve, float x, float y,
        float width = 0f, float height = 0f, float minValue = 0f, float maxValue = 1f)
    {
        bool clicked = DrawPreview(ref curve, x, y, width, height, minValue, maxValue);

        // Show tooltip on hover
        var rect = new ImRect(x, y, width > 0 ? width : PreviewWidth, height > 0 ? height : PreviewHeight);
        if (rect.Contains(Im.MousePos))
        {
            ImTooltip.DrawImmediate("Click to edit curve");
        }

        return clicked;
    }

    private static void DrawPreviewCurve(ref Curve curve, ImRect rect, float minValue, float maxValue)
    {
        Span<int> sortedIndices = stackalloc int[curve.Count];
        for (int i = 0; i < curve.Count; i++)
            sortedIndices[i] = i;

        // Simple insertion sort
        for (int i = 1; i < curve.Count; i++)
        {
            int key = sortedIndices[i];
            float keyTime = curve[key].Time;
            int j = i - 1;
            while (j >= 0 && curve[sortedIndices[j]].Time > keyTime)
            {
                sortedIndices[j + 1] = sortedIndices[j];
                j--;
            }
            sortedIndices[j + 1] = key;
        }

        float padding = 2f;
        float drawX = rect.X + padding;
        float drawWidth = rect.Width - padding * 2;
        float drawY = rect.Y + padding;
        float drawHeight = rect.Height - padding * 2;

        Span<Vector2> sampledPoints = stackalloc Vector2[PreviewSegments + 1];
        int sampledPointCount = 0;
        for (int i = 0; i <= PreviewSegments; i++)
        {
            float t = i / (float)PreviewSegments;
            float value = EvaluateCurveSpan(ref curve, sortedIndices, t);

            float screenX = drawX + t * drawWidth;
            float normalizedY = (value - minValue) / (maxValue - minValue);
            float screenY = drawY + drawHeight * (1f - normalizedY);
            screenY = Math.Clamp(screenY, rect.Y + 1, rect.Y + rect.Height - 1);
            sampledPoints[sampledPointCount++] = new Vector2(screenX, screenY);
        }

        if (sampledPointCount >= 2)
        {
            Im.DrawPolyline(sampledPoints[..sampledPointCount], PreviewCurveThickness, PreviewCurveColor);
        }
    }

    private static float EvaluateCurveSpan(ref Curve curve, ReadOnlySpan<int> sortedIndices, float t)
    {
        if (curve.Count == 0) return 0;
        if (curve.Count == 1) return curve[0].Value;

        int segStart = 0;
        for (int i = 0; i < sortedIndices.Length - 1; i++)
        {
            if (curve[sortedIndices[i + 1]].Time >= t)
            {
                segStart = i;
                break;
            }
            segStart = i;
        }

        var p0 = curve[sortedIndices[segStart]];
        var p1 = curve[sortedIndices[Math.Min(segStart + 1, sortedIndices.Length - 1)]];

        if (t <= p0.Time) return p0.Value;
        if (t >= p1.Time) return p1.Value;

        return EvaluateBezierSegmentAtTime(in p0, in p1, t);
    }

    private static bool DrawInternal(int id, ref Curve curve, ImRect rect,
        float minValue, float maxValue)
    {
        return DrawInternalEx(id, ref curve, rect, minValue, maxValue, drawBackground: true, drawGrid: true, drawHandles: true, handleInput: true);
    }

    private static bool DrawInternalEx(
        int id,
        ref Curve curve,
        ImRect rect,
        float minValue,
        float maxValue,
        bool drawBackground,
        bool drawGrid,
        bool drawHandles,
        bool handleInput)
    {
        bool changed = false;

        // Get or create state
        int stateIdx = FindOrCreateState(id);
        ref var state = ref _states[stateIdx];
        if (state.ZoomX == 0) state.ZoomX = 1f;
        if (state.ZoomY == 0) state.ZoomY = 1f;

        var view = new CurveView
        {
            ZoomX = state.ZoomX,
            ZoomY = state.ZoomY,
            PanOffset = state.PanOffset
        };

        if (!float.IsFinite(minValue) || !float.IsFinite(maxValue))
        {
            minValue = 0f;
            maxValue = 1f;
        }
        if (maxValue < minValue)
        {
            (minValue, maxValue) = (maxValue, minValue);
        }
        if (maxValue == minValue)
        {
            minValue = MathF.BitDecrement(minValue);
            maxValue = MathF.BitIncrement(maxValue);
        }

        // Draw background
        if (drawBackground)
        {
            Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, Im.Style.Surface);
            Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, Im.Style.Border, 1f);
        }

        var innerRect = new ImRect(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2);

        // Draw grid
        if (drawGrid)
        {
            DrawGrid(innerRect, minValue, maxValue, view.ZoomX, view.ZoomY, view.PanOffset);
        }

        bool hasInnerClipRect = innerRect.Width > 0f && innerRect.Height > 0f;
        if (hasInnerClipRect)
        {
            Im.PushClipRect(innerRect);
        }

        // Draw curve
        if (curve.Count >= 2)
        {
            DrawCurve(ref curve, innerRect, minValue, maxValue, view.ZoomX, view.ZoomY, view.PanOffset);
        }

        // Draw points and handles
        for (int i = 0; i < curve.Count; i++)
        {
            bool isSelected = state.SelectedPoint == i;
            DrawPoint(ref curve, i, innerRect, minValue, maxValue, isSelected,
                drawHandles, view.ZoomX, view.ZoomY, view.PanOffset);
        }

        if (hasInnerClipRect)
        {
            Im.PopClipRect();
        }

        // Handle input
        if (handleInput)
        {
            changed = HandleInput(id, ref curve, innerRect, minValue, maxValue, drawHandles, ref state, ref view);
        }

        state.ZoomX = view.ZoomX;
        state.ZoomY = view.ZoomY;
        state.PanOffset = view.PanOffset;

        return changed;
    }

    private static bool DrawInternalEx(
        int id,
        ref Curve curve,
        ImRect rect,
        float minValue,
        float maxValue,
        bool drawBackground,
        bool drawGrid,
        bool drawHandles,
        bool handleInput,
        ref CurveView view)
    {
        bool changed = false;

        // Get or create state
        int stateIdx = FindOrCreateState(id);
        ref var state = ref _states[stateIdx];

        if (view.ZoomX == 0f) view.ZoomX = 1f;
        if (view.ZoomY == 0f) view.ZoomY = 1f;

        if (!float.IsFinite(minValue) || !float.IsFinite(maxValue))
        {
            minValue = 0f;
            maxValue = 1f;
        }
        if (maxValue < minValue)
        {
            (minValue, maxValue) = (maxValue, minValue);
        }
        if (maxValue == minValue)
        {
            minValue = MathF.BitDecrement(minValue);
            maxValue = MathF.BitIncrement(maxValue);
        }

        // Draw background
        if (drawBackground)
        {
            Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, Im.Style.Surface);
            Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, Im.Style.Border, 1f);
        }

        var innerRect = new ImRect(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2);

        // Draw grid
        if (drawGrid)
        {
            DrawGrid(innerRect, minValue, maxValue, view.ZoomX, view.ZoomY, view.PanOffset);
        }

        bool hasInnerClipRect = innerRect.Width > 0f && innerRect.Height > 0f;
        if (hasInnerClipRect)
        {
            Im.PushClipRect(innerRect);
        }

        // Draw curve
        if (curve.Count >= 2)
        {
            DrawCurve(ref curve, innerRect, minValue, maxValue, view.ZoomX, view.ZoomY, view.PanOffset);
        }

        // Draw points and handles
        for (int i = 0; i < curve.Count; i++)
        {
            bool isSelected = state.SelectedPoint == i;
            DrawPoint(ref curve, i, innerRect, minValue, maxValue, isSelected,
                drawHandles, view.ZoomX, view.ZoomY, view.PanOffset);
        }

        if (hasInnerClipRect)
        {
            Im.PopClipRect();
        }

        // Handle input
        if (handleInput)
        {
            changed = HandleInput(id, ref curve, innerRect, minValue, maxValue, drawHandles, ref state, ref view);
        }

        return changed;
    }

    private static void DrawGrid(ImRect rect, float minValue, float maxValue,
        float zoomX, float zoomY, Vector2 pan)
    {
        float majorStepX = 0.25f;
        float rangeY = maxValue - minValue;
        if (!float.IsFinite(rangeY) || rangeY <= 0f)
        {
            return;
        }
        float majorStepY = rangeY * 0.25f;
        if (!float.IsFinite(majorStepY) || majorStepY <= 0f)
        {
            return;
        }

        uint majorColor = (GridColor & 0x00FFFFFF) | ((uint)(GridLineAlpha * 255) << 24);

        // Vertical lines (time axis)
        for (float t = 0f; t <= 1f; t += majorStepX)
        {
            float screenX = TimeToScreenX(t, rect, zoomX, pan.X);
            if (screenX >= rect.X && screenX <= rect.X + rect.Width)
            {
                Im.DrawLine(screenX, rect.Y, screenX, rect.Y + rect.Height, 1, majorColor);
            }
        }

        // Horizontal lines (value axis)
        for (float v = minValue; v <= maxValue; v += majorStepY)
        {
            float screenY = ValueToScreenY(v, rect, minValue, maxValue, zoomY, pan.Y);
            if (screenY >= rect.Y && screenY <= rect.Y + rect.Height)
            {
                Im.DrawLine(rect.X, screenY, rect.X + rect.Width, screenY, 1, majorColor);
            }
        }

        // Draw axis labels
        DrawAxisLabels(rect, minValue, maxValue);
    }

    private static void DrawAxisLabels(ImRect rect, float minValue, float maxValue)
    {
        uint textColor = Im.Style.TextSecondary;
        float fontSize = Im.Style.FontSize * 0.7f;

        // Time axis labels (0, 0.5, 1)
        Im.Text("0".AsSpan(), rect.X + 2, rect.Y + rect.Height - fontSize - 2, fontSize, textColor);
        Im.Text("0.5".AsSpan(), rect.X + rect.Width * 0.5f - 8, rect.Y + rect.Height - fontSize - 2, fontSize, textColor);
        Im.Text("1".AsSpan(), rect.X + rect.Width - 12, rect.Y + rect.Height - fontSize - 2, fontSize, textColor);

        // Value axis labels
        string minLabel = minValue == 0f ? "0" : minValue == 1f ? "1" : "min";
        string maxLabel = maxValue == 1f ? "1" : maxValue == 0f ? "0" : "max";
        Im.Text(minLabel.AsSpan(), rect.X + 2, rect.Y + rect.Height - fontSize * 2 - 4, fontSize, textColor);
        Im.Text(maxLabel.AsSpan(), rect.X + 2, rect.Y + 2, fontSize, textColor);
    }

    private static void DrawCurve(ref Curve curve, ImRect rect,
        float minValue, float maxValue, float zoomX, float zoomY, Vector2 pan)
    {
        if (curve.Count < 2) return;

        // Sort points by time
        EnsureSortBuffer(curve.Count);
        var sortedIndices = _sortBuffer.AsSpan(0, curve.Count);
        SortPointsByTime(ref curve, sortedIndices);

        // Max sampled points: first point + up to 32 subdivisions per segment.
        Span<Vector2> sampledPoints = stackalloc Vector2[(Curve.MaxPoints - 1) * 32 + 1];
        int sampledPointCount = 0;

        // Draw each segment
        for (int seg = 0; seg < sortedIndices.Length - 1; seg++)
        {
            var p0 = curve[sortedIndices[seg]];
            var p1 = curve[sortedIndices[seg + 1]];
            GetBezierControlPoints(in p0, in p1, out float bx0, out float by0, out float bx1, out float by1, out float bx2, out float by2, out float bx3, out float by3);

            float x0 = TimeToScreenX(bx0, rect, zoomX, pan.X);
            float y0 = ValueToScreenY(by0, rect, minValue, maxValue, zoomY, pan.Y);
            float x1 = TimeToScreenX(bx1, rect, zoomX, pan.X);
            float y1 = ValueToScreenY(by1, rect, minValue, maxValue, zoomY, pan.Y);
            float x2 = TimeToScreenX(bx2, rect, zoomX, pan.X);
            float y2 = ValueToScreenY(by2, rect, minValue, maxValue, zoomY, pan.Y);
            float x3 = TimeToScreenX(bx3, rect, zoomX, pan.X);
            float y3 = ValueToScreenY(by3, rect, minValue, maxValue, zoomY, pan.Y);

            float controlPolylineLength =
                Vector2.Distance(new Vector2(x0, y0), new Vector2(x1, y1)) +
                Vector2.Distance(new Vector2(x1, y1), new Vector2(x2, y2)) +
                Vector2.Distance(new Vector2(x2, y2), new Vector2(x3, y3));
            int subdivisions = Math.Clamp((int)MathF.Ceiling(controlPolylineLength / 5f), 4, 48);
            if (sampledPointCount == 0)
            {
                sampledPoints[sampledPointCount++] = new Vector2(x0, y0);
            }

            for (int i = 1; i <= subdivisions; i++)
            {
                float u = i / (float)subdivisions;
                float time = CubicBezierScalar(bx0, bx1, bx2, bx3, u);
                float value = CubicBezierScalar(by0, by1, by2, by3, u);

                float currX = TimeToScreenX(time, rect, zoomX, pan.X);
                float currY = ValueToScreenY(value, rect, minValue, maxValue, zoomY, pan.Y);
                sampledPoints[sampledPointCount++] = new Vector2(currX, currY);
            }
        }

        if (sampledPointCount >= 2)
        {
            Im.DrawPolyline(sampledPoints[..sampledPointCount], CurveThickness, CurveColor);
        }
    }

    private static void DrawPoint(ref Curve curve, int index, ImRect rect,
        float minValue, float maxValue, bool isSelected, bool drawHandles, float zoomX, float zoomY, Vector2 pan)
    {
        var point = curve[index];
        float screenX = TimeToScreenX(point.Time, rect, zoomX, pan.X);
        float screenY = ValueToScreenY(point.Value, rect, minValue, maxValue, zoomY, pan.Y);

        // Draw tangent handles if selected
        if (isSelected && drawHandles)
        {
            GetTangentHandlePositions(ref curve, index, rect, minValue, maxValue, zoomX, zoomY, pan, out var incomingHandle, out var outgoingHandle);
            float tanInX = incomingHandle.X;
            float tanInY = incomingHandle.Y;
            Im.DrawLine(screenX, screenY, tanInX, tanInY, 1, TangentLineColor);
            Im.DrawCircle(tanInX, tanInY, TangentHandleRadius, TangentHandleColor);

            float tanOutX = outgoingHandle.X;
            float tanOutY = outgoingHandle.Y;
            Im.DrawLine(screenX, screenY, tanOutX, tanOutY, 1, TangentLineColor);
            Im.DrawCircle(tanOutX, tanOutY, TangentHandleRadius, TangentHandleColor);
        }

        // Draw point
        uint color = isSelected ? SelectedPointColor : PointColor;
        Im.DrawCircle(screenX, screenY, PointRadius, color);
        Im.DrawCircle(screenX, screenY, PointRadius - 1, Im.Style.Surface);
        Im.DrawCircle(screenX, screenY, PointRadius - 2, color);
    }

    private static bool HandleInput(
        int id,
        ref Curve curve,
        ImRect rect,
        float minValue,
        float maxValue,
        bool allowTangents,
        ref EditorState state,
        ref CurveView view)
    {
        bool changed = false;
        var mousePos = Im.MousePos;
        bool mousePressed = Im.MousePressed;
        bool mouseDown = Im.MouseDown;
        bool mouseReleased = !mouseDown && state.DraggingPoint >= 0;

        bool inBounds = rect.Contains(mousePos);
        var input = Im.Context.Input;

        if (!allowTangents)
        {
            state.DraggingTangent = -1;
        }

        if (inBounds && input.ScrollDelta != 0f && state.DraggingPoint < 0 && state.DraggingTangent < 0 && !input.MouseMiddleDown)
        {
            const float zoomMin = 0.10f;
            const float zoomMax = 20.0f;

            float scroll = input.ScrollDelta;
            float factor = MathF.Pow(1.15f, scroll);

            float relX = rect.Width <= 0f ? 0f : (mousePos.X - rect.X) / rect.Width;
            float relY = rect.Height <= 0f ? 0f : (rect.Y + rect.Height - mousePos.Y) / rect.Height;

            if (input.KeyCtrl)
            {
                float oldZoom = view.ZoomX;
                float newZoom = Math.Clamp(oldZoom * factor, zoomMin, zoomMax);
                if (newZoom != oldZoom && oldZoom > 0f && rect.Width > 0f)
                {
                    float time = (relX - view.PanOffset.X) / oldZoom;
                    view.ZoomX = newZoom;
                    view.PanOffset.X = relX - time * newZoom;
                }
            }
            else
            {
                float oldZoom = view.ZoomY;
                float newZoom = Math.Clamp(oldZoom * factor, zoomMin, zoomMax);
                if (newZoom != oldZoom && oldZoom > 0f && rect.Height > 0f)
                {
                    float normalized = (relY - view.PanOffset.Y) / oldZoom;
                    view.ZoomY = newZoom;
                    view.PanOffset.Y = relY - normalized * newZoom;
                }
            }

            view.PanOffset.X = Math.Clamp(view.PanOffset.X, -10f, 10f);
            view.PanOffset.Y = Math.Clamp(view.PanOffset.Y, -10f, 10f);
        }

        if (inBounds && input.MouseMiddlePressed && state.DraggingPoint < 0 && state.DraggingTangent < 0)
        {
            state.IsPanning = true;
            state.PanStart = mousePos;
            state.PanOffset = view.PanOffset;
        }

        if (state.IsPanning)
        {
            if (!input.MouseMiddleDown)
            {
                state.IsPanning = false;
            }
            else
            {
                Vector2 delta = mousePos - state.PanStart;
                if (rect.Height > 0f)
                {
                    view.PanOffset.Y = state.PanOffset.Y - delta.Y / rect.Height;
                }
                if (input.KeyCtrl && rect.Width > 0f)
                {
                    view.PanOffset.X = state.PanOffset.X + delta.X / rect.Width;
                }
            }
        }

        // Expanded bounds for point selection (allows clicking slightly outside for edge points)
        var expandedRect = new ImRect(
            rect.X - HitTestRadius, rect.Y - HitTestRadius,
            rect.Width + HitTestRadius * 2, rect.Height + HitTestRadius * 2);
        bool inExpandedBounds = expandedRect.Contains(mousePos);
        bool hitSomething = false; // Track if we hit a point or tangent THIS frame

        // Point/tangent selection uses expanded bounds (for corner points)
        if (mousePressed && inExpandedBounds)
        {
            int hitPoint = HitTestPoints(ref curve, rect, mousePos, minValue, maxValue,
                view.ZoomX, view.ZoomY, view.PanOffset);

            if (hitPoint >= 0)
            {
                hitSomething = true;
                state.SelectedPoint = hitPoint;
                state.DraggingPoint = hitPoint;

                // Check tangent handles
                if (allowTangents && state.SelectedPoint >= 0)
                {
                        int hitTangent = HitTestTangents(ref curve, hitPoint, rect, mousePos,
                            minValue, maxValue, view.ZoomX, view.ZoomY, view.PanOffset);
                    if (hitTangent >= 0)
                    {
                        state.DraggingTangent = hitTangent;
                        state.DraggingPoint = -1;
                    }
                }
            }
            else
            {
                // Only check tangent hits if mouse is near the selected point
                if (state.SelectedPoint >= 0 && state.SelectedPoint < curve.Count)
                {
                    var selPoint = curve[state.SelectedPoint];
                    float selScreenX = TimeToScreenX(selPoint.Time, rect, view.ZoomX, view.PanOffset.X);
                    float selScreenY = ValueToScreenY(selPoint.Value, rect, minValue, maxValue, view.ZoomY, view.PanOffset.Y);
                    float distToSelected = Vector2.Distance(mousePos, new Vector2(selScreenX, selScreenY));
                    GetTangentHandlePositions(ref curve, state.SelectedPoint, rect, minValue, maxValue, view.ZoomX, view.ZoomY, view.PanOffset, out var selectedIncomingHandle, out var selectedOutgoingHandle);
                    float tangentReach = MathF.Max(
                        Vector2.Distance(new Vector2(selScreenX, selScreenY), selectedIncomingHandle),
                        Vector2.Distance(new Vector2(selScreenX, selScreenY), selectedOutgoingHandle));

                    // Only check tangents if within reasonable distance of selected point
                    if (allowTangents && distToSelected < tangentReach + HitTestRadius * 2)
                    {
                        int hitTangent = HitTestTangents(ref curve, state.SelectedPoint, rect, mousePos,
                            minValue, maxValue, view.ZoomX, view.ZoomY, view.PanOffset);
                        if (hitTangent >= 0)
                        {
                            hitSomething = true;
                            state.DraggingTangent = hitTangent;
                        }
                        else if (inBounds)
                        {
                            // Only deselect if clicking inside the actual bounds
                            state.SelectedPoint = -1;
                        }
                    }
                    else if (inBounds)
                    {
                        state.SelectedPoint = -1;
                    }
                }
                else if (inBounds)
                {
                    state.SelectedPoint = -1;
                }
            }
        }

        if (mouseDown)
        {
            if (state.DraggingPoint >= 0 && state.DraggingPoint < curve.Count)
            {
                float newTime = ScreenXToTime(mousePos.X, rect, view.ZoomX, view.PanOffset.X);
                float newValue = ScreenYToValue(mousePos.Y, rect, minValue, maxValue, view.ZoomY, view.PanOffset.Y);

                newTime = Math.Clamp(newTime, 0f, 1f);
                newValue = Math.Clamp(newValue, minValue, maxValue);

                ref var point = ref curve[state.DraggingPoint];
                float oldTime = point.Time;
                float minTime = 0f;
                float maxTime = 1f;

                for (int i = 0; i < curve.Count; i++)
                {
                    if (i == state.DraggingPoint)
                    {
                        continue;
                    }

                    float t = curve[i].Time;
                    if (t < oldTime && t > minTime)
                    {
                        minTime = t;
                    }
                    else if (t > oldTime && t < maxTime)
                    {
                        maxTime = t;
                    }
                }

                const float epsilon = 0.0005f;
                minTime = Math.Clamp(minTime + epsilon, 0f, 1f);
                maxTime = Math.Clamp(maxTime - epsilon, 0f, 1f);
                if (maxTime < minTime)
                {
                    minTime = oldTime;
                    maxTime = oldTime;
                }

                newTime = Math.Clamp(newTime, minTime, maxTime);
                if (Math.Abs(point.Time - newTime) > 0.0001f || Math.Abs(point.Value - newValue) > 0.0001f)
                {
                    point.Time = newTime;
                    point.Value = newValue;
                    changed = true;
                }
            }
            else if (allowTangents && state.DraggingTangent >= 0 && state.SelectedPoint >= 0 && state.SelectedPoint < curve.Count)
            {
                ref var point = ref curve[state.SelectedPoint];
                float prevSegLen = GetPrevSegmentLength(ref curve, state.SelectedPoint);
                float nextSegLen = GetNextSegmentLength(ref curve, state.SelectedPoint);

                // Hold Shift for broken tangents (independent control)
                bool brokenTangents = Im.Context.Input.KeyShift;

                if (state.DraggingTangent == 0) // In tangent
                {
                    float tangentTimeDelta = ScreenXToTime(mousePos.X, rect, view.ZoomX, view.PanOffset.X) - point.Time;
                    if (tangentTimeDelta >= -0.000001f)
                    {
                        tangentTimeDelta = -0.000001f;
                    }

                    float incomingWeight = NormalizeHandleWeight(MathF.Abs(tangentTimeDelta) / prevSegLen);
                    float handleValue = ScreenYToValue(mousePos.Y, rect, minValue, maxValue, view.ZoomY, view.PanOffset.Y);
                    float incomingControlDeltaValue = handleValue - point.Value;
                    float newTangentIn = -3f * incomingControlDeltaValue;
                    float newTangentOut = point.TangentOut;

                    if (!brokenTangents)
                    {
                        float globalSlope = incomingControlDeltaValue / tangentTimeDelta;
                        float outgoingTimeDelta = nextSegLen * NormalizeHandleWeight(point.TangentOutWeight);
                        float mirroredOutgoingControlDeltaValue = globalSlope * outgoingTimeDelta;
                        newTangentOut = 3f * mirroredOutgoingControlDeltaValue;
                    }

                    if (Math.Abs(point.TangentIn - newTangentIn) > 0.001f ||
                        Math.Abs(point.TangentInWeight - incomingWeight) > 0.0001f ||
                        (!brokenTangents && Math.Abs(point.TangentOut - newTangentOut) > 0.001f))
                    {
                        point.TangentIn = newTangentIn;
                        point.TangentInWeight = incomingWeight;
                        if (!brokenTangents)
                        {
                            point.TangentOut = newTangentOut;
                        }
                        changed = true;
                    }
                }
                else // Out tangent
                {
                    float tangentTimeDelta = ScreenXToTime(mousePos.X, rect, view.ZoomX, view.PanOffset.X) - point.Time;
                    if (tangentTimeDelta <= 0.000001f)
                    {
                        tangentTimeDelta = 0.000001f;
                    }

                    float outgoingWeight = NormalizeHandleWeight(MathF.Abs(tangentTimeDelta) / nextSegLen);
                    float handleValue = ScreenYToValue(mousePos.Y, rect, minValue, maxValue, view.ZoomY, view.PanOffset.Y);
                    float outgoingControlDeltaValue = handleValue - point.Value;
                    float newTangentOut = 3f * outgoingControlDeltaValue;
                    float newTangentIn = point.TangentIn;

                    if (!brokenTangents)
                    {
                        float globalSlope = outgoingControlDeltaValue / tangentTimeDelta;
                        float incomingTimeDelta = -prevSegLen * NormalizeHandleWeight(point.TangentInWeight);
                        float mirroredIncomingControlDeltaValue = globalSlope * incomingTimeDelta;
                        newTangentIn = -3f * mirroredIncomingControlDeltaValue;
                    }

                    if (Math.Abs(point.TangentOut - newTangentOut) > 0.001f ||
                        Math.Abs(point.TangentOutWeight - outgoingWeight) > 0.0001f ||
                        (!brokenTangents && Math.Abs(point.TangentIn - newTangentIn) > 0.001f))
                    {
                        point.TangentOut = newTangentOut;
                        point.TangentOutWeight = outgoingWeight;
                        if (!brokenTangents)
                        {
                            point.TangentIn = newTangentIn;
                        }
                        changed = true;
                    }
                }
            }
        }

        if (mouseReleased || !mouseDown)
        {
            state.DraggingPoint = -1;
            state.DraggingTangent = -1;
        }

        // Track time since last click (for double-click detection)
        state.TimeSinceLastClick += Im.Context.DeltaTime;

        // Double-click to add point (only on empty space, not on existing points/tangents)
        if (mousePressed && inBounds)
        {
            if (hitSomething)
            {
                // Clicked on point/tangent - reset double-click tracking
                state.TimeSinceLastClick = 999f; // Large value so next click isn't a double-click
            }
            else
            {
                // Clicked on empty space - check for double-click
                float clickDist = Vector2.Distance(mousePos, state.LastClickPos);

                if (state.TimeSinceLastClick < 0.3f && clickDist < 10f)
                {
                    // Double-click detected - add new point
                    float newTime = ScreenXToTime(mousePos.X, rect, view.ZoomX, view.PanOffset.X);
                    float newValue = ScreenYToValue(mousePos.Y, rect, minValue, maxValue, view.ZoomY, view.PanOffset.Y);

                    newTime = Math.Clamp(newTime, 0f, 1f);
                    newValue = Math.Clamp(newValue, minValue, maxValue);

                    if (curve.Add(new CurvePoint(newTime, newValue, 0f, 0f)))
                    {
                        state.SelectedPoint = curve.Count - 1;
                        changed = true;
                    }

                    // Reset to prevent triple-click adding more points
                    state.TimeSinceLastClick = 999f;
                }
                else
                {
                    // First click - reset timer and record position
                    state.TimeSinceLastClick = 0;
                    state.LastClickPos = mousePos;
                }
            }
        }

        // Right-click to delete
        if (Im.Context.Input.MouseRightPressed && inBounds && state.SelectedPoint >= 0 && curve.Count > 2)
        {
            curve.RemoveAt(state.SelectedPoint);
            state.SelectedPoint = -1;
            changed = true;
        }

        return changed;
    }

    private static int HitTestPoints(ref Curve curve, ImRect rect, Vector2 mousePos,
        float minValue, float maxValue, float zoomX, float zoomY, Vector2 pan)
    {
        float hitRadiusSq = HitTestRadius * HitTestRadius;

        for (int i = 0; i < curve.Count; i++)
        {
            var point = curve[i];
            float screenX = TimeToScreenX(point.Time, rect, zoomX, pan.X);
            float screenY = ValueToScreenY(point.Value, rect, minValue, maxValue, zoomY, pan.Y);

            float dx = mousePos.X - screenX;
            float dy = mousePos.Y - screenY;
            if (dx * dx + dy * dy <= hitRadiusSq)
                return i;
        }

        return -1;
    }

    private static int HitTestTangents(ref Curve curve, int pointIndex, ImRect rect, Vector2 mousePos,
        float minValue, float maxValue, float zoomX, float zoomY, Vector2 pan)
    {
        float hitRadiusSq = HitTestRadius * HitTestRadius;
        GetTangentHandlePositions(ref curve, pointIndex, rect, minValue, maxValue, zoomX, zoomY, pan, out var incomingHandle, out var outgoingHandle);

        float dx = mousePos.X - incomingHandle.X;
        float dy = mousePos.Y - incomingHandle.Y;
        if (dx * dx + dy * dy <= hitRadiusSq)
        {
            return 0;
        }

        dx = mousePos.X - outgoingHandle.X;
        dy = mousePos.Y - outgoingHandle.Y;
        if (dx * dx + dy * dy <= hitRadiusSq)
        {
            return 1;
        }

        return -1;
    }

    private static float NormalizeHandleWeight(float weight)
    {
        if (!float.IsFinite(weight) || weight <= 0.000001f)
        {
            return CurvePoint.DefaultHandleWeight;
        }

        return Math.Clamp(weight, TangentHandleWeightMin, TangentHandleWeightMax);
    }

    private static float GetPrevSegmentLength(ref Curve curve, int pointIndex)
    {
        float time = curve[pointIndex].Time;
        float prevTime = 0f;
        bool hasPrev = false;

        for (int i = 0; i < curve.Count; i++)
        {
            if (i == pointIndex)
            {
                continue;
            }

            float t = curve[i].Time;
            if (t < time)
            {
                if (!hasPrev || t > prevTime)
                {
                    prevTime = t;
                    hasPrev = true;
                }
            }
        }

        float len = hasPrev ? time - prevTime : 1f;
        if (!float.IsFinite(len) || len <= 0.000001f)
        {
            len = 0.000001f;
        }
        return len;
    }

    private static float GetNextSegmentLength(ref Curve curve, int pointIndex)
    {
        float time = curve[pointIndex].Time;
        float nextTime = 1f;
        bool hasNext = false;

        for (int i = 0; i < curve.Count; i++)
        {
            if (i == pointIndex)
            {
                continue;
            }

            float t = curve[i].Time;
            if (t > time)
            {
                if (!hasNext || t < nextTime)
                {
                    nextTime = t;
                    hasNext = true;
                }
            }
        }

        float len = hasNext ? nextTime - time : 1f;
        if (!float.IsFinite(len) || len <= 0.000001f)
        {
            len = 0.000001f;
        }
        return len;
    }

    private static float TimeToScreenX(float time, ImRect rect, float zoom, float pan)
    {
        return rect.X + (time * zoom + pan) * rect.Width;
    }

    private static float ValueToScreenY(float value, ImRect rect, float minValue, float maxValue, float zoom, float pan)
    {
        float normalized = (value - minValue) / (maxValue - minValue);
        return rect.Y + rect.Height - (normalized * zoom + pan) * rect.Height;
    }

    private static float ScreenXToTime(float screenX, ImRect rect, float zoom, float pan)
    {
        return ((screenX - rect.X) / rect.Width - pan) / zoom;
    }

    private static float ScreenYToValue(float screenY, ImRect rect, float minValue, float maxValue, float zoom, float pan)
    {
        float normalized = (rect.Y + rect.Height - screenY) / rect.Height;
        normalized = (normalized - pan) / zoom;
        return minValue + normalized * (maxValue - minValue);
    }

    private static void GetTangentHandlePositions(
        ref Curve curve,
        int pointIndex,
        ImRect rect,
        float minValue,
        float maxValue,
        float zoomX,
        float zoomY,
        Vector2 pan,
        out Vector2 incomingHandle,
        out Vector2 outgoingHandle)
    {
        ref CurvePoint point = ref curve[pointIndex];
        float prevSegLen = GetPrevSegmentLength(ref curve, pointIndex);
        float nextSegLen = GetNextSegmentLength(ref curve, pointIndex);
        float incomingWeight = NormalizeHandleWeight(point.TangentInWeight);
        float outgoingWeight = NormalizeHandleWeight(point.TangentOutWeight);

        float incomingTime = point.Time - (prevSegLen * incomingWeight);
        float outgoingTime = point.Time + (nextSegLen * outgoingWeight);
        float incomingValue = point.Value - (point.TangentIn / 3f);
        float outgoingValue = point.Value + (point.TangentOut / 3f);

        incomingHandle = new Vector2(
            TimeToScreenX(incomingTime, rect, zoomX, pan.X),
            ValueToScreenY(incomingValue, rect, minValue, maxValue, zoomY, pan.Y));

        outgoingHandle = new Vector2(
            TimeToScreenX(outgoingTime, rect, zoomX, pan.X),
            ValueToScreenY(outgoingValue, rect, minValue, maxValue, zoomY, pan.Y));
    }

    private static void GetBezierControlPoints(
        in CurvePoint point0,
        in CurvePoint point1,
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

        float segmentLen = x3 - x0;
        if (!float.IsFinite(segmentLen) || segmentLen <= 0.000001f)
        {
            segmentLen = 0.000001f;
        }

        float outWeight = NormalizeHandleWeight(point0.TangentOutWeight);
        float inWeight = NormalizeHandleWeight(point1.TangentInWeight);

        x1 = x0 + segmentLen * outWeight;
        x2 = x3 - segmentLen * inWeight;
        y1 = y0 + (point0.TangentOut / 3f);
        y2 = y3 - (point1.TangentIn / 3f);
    }

    private static float EvaluateBezierSegmentAtTime(in CurvePoint point0, in CurvePoint point1, float time)
    {
        GetBezierControlPoints(in point0, in point1, out float x0, out float y0, out float x1, out float y1, out float x2, out float y2, out float x3, out float y3);

        float clampedTime = time;
        if (clampedTime <= x0)
        {
            return y0;
        }
        if (clampedTime >= x3)
        {
            return y3;
        }

        float u = SolveBezierParameterForX(clampedTime, x0, x1, x2, x3);
        return CubicBezierScalar(y0, y1, y2, y3, u);
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

    private static void EnsureSortBuffer(int size)
    {
        if (_sortBuffer.Length < size)
            _sortBuffer = new int[size * 2];
    }

    private static void SortPointsByTime(ref Curve curve, Span<int> indices)
    {
        for (int i = 0; i < indices.Length; i++)
            indices[i] = i;

        for (int i = 1; i < indices.Length; i++)
        {
            int key = indices[i];
            float keyTime = curve[key].Time;
            int j = i - 1;

            while (j >= 0 && curve[indices[j]].Time > keyTime)
            {
                indices[j + 1] = indices[j];
                j--;
            }
            indices[j + 1] = key;
        }
    }

    /// <summary>
    /// Evaluate the curve at a given time (0-1).
    /// </summary>
    public static float Evaluate(ref Curve curve, float t)
    {
        if (curve.Count == 0) return 0;
        if (curve.Count == 1) return curve[0].Value;

        EnsureSortBuffer(curve.Count);
        var sortedIndices = _sortBuffer.AsSpan(0, curve.Count);
        SortPointsByTime(ref curve, sortedIndices);

        // Find segment
        int segStart = 0;
        for (int i = 0; i < sortedIndices.Length - 1; i++)
        {
            if (curve[sortedIndices[i + 1]].Time >= t)
            {
                segStart = i;
                break;
            }
            segStart = i;
        }

        var p0 = curve[sortedIndices[segStart]];
        var p1 = curve[sortedIndices[Math.Min(segStart + 1, sortedIndices.Length - 1)]];

        if (t <= p0.Time) return p0.Value;
        if (t >= p1.Time) return p1.Value;

        return EvaluateBezierSegmentAtTime(in p0, in p1, t);
    }

    private static int FindOrCreateState(int id)
    {
        for (int i = 0; i < _stateCount; i++)
        {
            if (_stateIds[i] == id) return i;
        }

        if (_stateCount >= 16) return 0;

        int idx = _stateCount++;
        _stateIds[idx] = id;
        _states[idx] = new EditorState { SelectedPoint = -1, DraggingPoint = -1, DraggingTangent = -1 };
        return idx;
    }
}
