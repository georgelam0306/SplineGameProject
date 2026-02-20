using DerpLib.ImGui.Core;

namespace DerpLib.ImGui.Widgets;

/// <summary>
/// Dual-handle range slider for selecting a min/max range.
/// </summary>
public static class ImRangeSlider
{
    // State tracking (fixed-size arrays)
    private static readonly RangeSliderState[] _states = new RangeSliderState[32];
    private static readonly int[] _stateIds = new int[32];
    private static int _stateCount;

    private struct RangeSliderState
    {
        public bool DraggingMin;
        public bool DraggingMax;
        public bool DraggingRange;
        public float DragOffset;
    }

    /// <summary>
    /// Draw a range slider at explicit position.
    /// Returns true if either value changed.
    /// </summary>
    public static bool DrawAt(string id, float x, float y, float width, ref float minValue, ref float maxValue,
        float rangeMin, float rangeMax, bool showLabels = true)
        => DrawAtInternal(Im.Context.GetId(id), x, y, width, ref minValue, ref maxValue, rangeMin, rangeMax, showLabels);

    private static bool DrawAtInternal(int widgetId, float x, float y, float width, ref float minValue, ref float maxValue,
        float rangeMin, float rangeMax, bool showLabels)
    {
        float height = Im.Style.MinButtonHeight;
        float thumbWidth = Im.Style.SliderThumbWidth;
        float trackHeight = Im.Style.SliderHeight;

        var rect = new ImRect(x, y, width, height);
        float trackY = rect.Y + (height - trackHeight) * 0.5f;

        var trackRect = new ImRect(rect.X, trackY, rect.Width, trackHeight);

        // Get or create state
        int stateIdx = FindOrCreateState(widgetId);
        ref var state = ref _states[stateIdx];

        // Calculate thumb positions
        float range = rangeMax - rangeMin;
        float minNorm = (minValue - rangeMin) / range;
        float maxNorm = (maxValue - rangeMin) / range;

        float usableWidth = rect.Width - thumbWidth;
        float minThumbX = rect.X + minNorm * usableWidth;
        float maxThumbX = rect.X + maxNorm * usableWidth;

        var minThumbRect = new ImRect(minThumbX, rect.Y, thumbWidth, height);
        var maxThumbRect = new ImRect(maxThumbX, rect.Y, thumbWidth, height);
        var rangeRect = new ImRect(minThumbX + thumbWidth, trackY, maxThumbX - minThumbX - thumbWidth, trackHeight);

        bool minHovered = minThumbRect.Contains(Im.MousePos);
        bool maxHovered = maxThumbRect.Contains(Im.MousePos);
        bool rangeHovered = rangeRect.Width > 0 && rangeRect.Contains(Im.MousePos) && !minHovered && !maxHovered;
        bool changed = false;

        // Handle input
        if (Im.MousePressed)
        {
            if (minHovered && !state.DraggingMax)
            {
                state.DraggingMin = true;
            }
            else if (maxHovered && !state.DraggingMin)
            {
                state.DraggingMax = true;
            }
            else if (rangeHovered)
            {
                state.DraggingRange = true;
                state.DragOffset = Im.MousePos.X - (minThumbX + thumbWidth * 0.5f);
            }
        }

        if (Im.Context.Input.MouseReleased)
        {
            state.DraggingMin = false;
            state.DraggingMax = false;
            state.DraggingRange = false;
        }

        if (state.DraggingMin)
        {
            float newNorm = (Im.MousePos.X - rect.X - thumbWidth * 0.5f) / usableWidth;
            newNorm = Math.Clamp(newNorm, 0f, maxNorm - 0.01f);
            float newValue = rangeMin + newNorm * range;
            if (Math.Abs(newValue - minValue) > 0.0001f)
            {
                minValue = newValue;
                changed = true;
            }
        }

        if (state.DraggingMax)
        {
            float newNorm = (Im.MousePos.X - rect.X - thumbWidth * 0.5f) / usableWidth;
            newNorm = Math.Clamp(newNorm, minNorm + 0.01f, 1f);
            float newValue = rangeMin + newNorm * range;
            if (Math.Abs(newValue - maxValue) > 0.0001f)
            {
                maxValue = newValue;
                changed = true;
            }
        }

        if (state.DraggingRange)
        {
            float currentRange = maxValue - minValue;
            float targetMinX = Im.MousePos.X - state.DragOffset - thumbWidth * 0.5f;
            float newMinNorm = (targetMinX - rect.X) / usableWidth;

            float rangeNorm = currentRange / range;
            newMinNorm = Math.Clamp(newMinNorm, 0f, 1f - rangeNorm);

            float newMin = rangeMin + newMinNorm * range;
            float newMax = newMin + currentRange;

            if (Math.Abs(newMin - minValue) > 0.0001f)
            {
                minValue = newMin;
                maxValue = newMax;
                changed = true;
            }
        }

        // Draw track background
        Im.DrawRoundedRect(trackRect.X, trackRect.Y, trackRect.Width, trackRect.Height,
            trackHeight * 0.5f, Im.Style.Surface);
        Im.DrawRoundedRectStroke(trackRect.X, trackRect.Y, trackRect.Width, trackRect.Height,
            trackHeight * 0.5f, Im.Style.Border, 1f);

        // Recalculate positions after potential changes
        minNorm = (minValue - rangeMin) / range;
        maxNorm = (maxValue - rangeMin) / range;
        minThumbX = rect.X + minNorm * usableWidth;
        maxThumbX = rect.X + maxNorm * usableWidth;

        // Draw filled range
        float filledX = minThumbX + thumbWidth * 0.5f;
        float filledWidth = maxThumbX - minThumbX;
        if (filledWidth > 0)
        {
            Im.DrawRoundedRect(filledX, trackY, filledWidth, trackHeight, trackHeight * 0.5f, Im.Style.Primary);
        }

        // Draw min thumb
        bool minActive = state.DraggingMin || minHovered;
        uint minThumbColor = minActive ? Im.Style.Primary : Im.Style.Surface;
        Im.DrawRoundedRect(minThumbX, rect.Y + 2, thumbWidth, height - 4, Im.Style.CornerRadius, minThumbColor);
        Im.DrawRoundedRectStroke(minThumbX, rect.Y + 2, thumbWidth, height - 4, Im.Style.CornerRadius, Im.Style.Border, 1f);

        // Draw max thumb
        bool maxActive = state.DraggingMax || maxHovered;
        uint maxThumbColor = maxActive ? Im.Style.Primary : Im.Style.Surface;
        Im.DrawRoundedRect(maxThumbX, rect.Y + 2, thumbWidth, height - 4, Im.Style.CornerRadius, maxThumbColor);
        Im.DrawRoundedRectStroke(maxThumbX, rect.Y + 2, thumbWidth, height - 4, Im.Style.CornerRadius, Im.Style.Border, 1f);

        // Draw labels
        if (showLabels)
        {
            Span<char> minLabelBuf = stackalloc char[16];
            minValue.TryFormat(minLabelBuf, out int minLabelLen, "F1");
            var minLabelSpan = minLabelBuf.Slice(0, minLabelLen);

            Span<char> maxLabelBuf = stackalloc char[16];
            maxValue.TryFormat(maxLabelBuf, out int maxLabelLen, "F1");
            var maxLabelSpan = maxLabelBuf.Slice(0, maxLabelLen);

            float smallFontSize = Im.Style.FontSize * 0.8f;
            float minLabelWidth = ImTextMetrics.MeasureWidth(Im.Context.Font, minLabelSpan, smallFontSize);
            float maxLabelWidth = ImTextMetrics.MeasureWidth(Im.Context.Font, maxLabelSpan, smallFontSize);

            float labelY = rect.Y + height + 2;
            Im.Text(minLabelSpan, minThumbX + thumbWidth * 0.5f - minLabelWidth * 0.5f, labelY, smallFontSize, Im.Style.TextSecondary);
            Im.Text(maxLabelSpan, maxThumbX + thumbWidth * 0.5f - maxLabelWidth * 0.5f, labelY, smallFontSize, Im.Style.TextSecondary);
        }

        return changed;
    }

    /// <summary>
    /// Draw a range slider with label.
    /// </summary>
    public static bool DrawAt(string label, string id, float x, float y, float labelWidth, float sliderWidth,
        ref float minValue, ref float maxValue, float rangeMin, float rangeMax, bool showLabels = true)
    {
        var labelRect = new ImRect(x, y, labelWidth, Im.Style.MinButtonHeight);
        float textY = labelRect.Y + (Im.Style.MinButtonHeight - Im.Style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), labelRect.X, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        // Draw slider
        return DrawAtInternal(Im.Context.GetId(id), x + labelWidth, y, sliderWidth, ref minValue, ref maxValue, rangeMin, rangeMax, showLabels);
    }

    private static int FindOrCreateState(int id)
    {
        for (int i = 0; i < _stateCount; i++)
        {
            if (_stateIds[i] == id) return i;
        }

        if (_stateCount >= 32) return 0;

        int idx = _stateCount++;
        _stateIds[idx] = id;
        _states[idx] = default;
        return idx;
    }
}
