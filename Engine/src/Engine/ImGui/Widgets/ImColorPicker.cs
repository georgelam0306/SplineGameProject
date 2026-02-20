using System.Numerics;
using DerpLib.ImGui.Core;
using DerpLib.Sdf;

namespace DerpLib.ImGui.Widgets;

/// <summary>
/// Color picker widget with SV square, hue bar, and optional alpha slider.
/// Uses ARGB uint format (0xAARRGGBB) matching ImStyle colors.
/// </summary>
public static class ImColorPicker
{
    // State tracking
    private static readonly ColorPickerState[] _states = new ColorPickerState[16];
    private static readonly int[] _stateIds = new int[16];
    private static int _stateCount;

    private struct ColorPickerState
    {
        public bool DraggingSV;
        public bool DraggingHue;
        public bool DraggingAlpha;
    }

    private const float HueBarWidth = 20f;
    private const float AlphaBarHeight = 16f;
    private const float PreviewHeight = 24f;
    private const float Spacing = 8f;

    /// <summary>
    /// Draw a color picker at explicit position.
    /// Returns true if color changed.
    /// </summary>
    public static bool DrawAt(string id, float x, float y, float width, ref uint color, bool showAlpha = true)
        => DrawAtInternal(Im.Context.GetId(id), x, y, width, ref color, showAlpha);

    private static bool DrawAtInternal(int widgetId, float x, float y, float width, ref uint color, bool showAlpha)
    {
        // Get or create state
        int stateIdx = FindOrCreateState(widgetId);
        ref var state = ref _states[stateIdx];

        // Convert to HSV
        RgbToHsv(color, out float h, out float s, out float v, out float a);

        // Calculate layout
        float svSize = width - HueBarWidth - Spacing;

        var baseRect = new ImRect(x, y, width, svSize + Spacing + (showAlpha ? AlphaBarHeight + Spacing : 0) + PreviewHeight);

        // Rects
        var svRect = new ImRect(baseRect.X, baseRect.Y, svSize, svSize);
        var hueRect = new ImRect(baseRect.X + svSize + Spacing, baseRect.Y, HueBarWidth, svSize);

        float alphaY = baseRect.Y + svSize + Spacing;
        var alphaRect = showAlpha ? new ImRect(baseRect.X, alphaY, width, AlphaBarHeight) : default;

        float previewY = showAlpha ? alphaY + AlphaBarHeight + Spacing : baseRect.Y + svSize + Spacing;
        var previewRect = new ImRect(baseRect.X, previewY, width, PreviewHeight);

        bool changed = false;

        // === Input handling ===

        // SV Square
        if (svRect.Contains(Im.MousePos) && Im.MousePressed && !state.DraggingHue && !state.DraggingAlpha)
        {
            state.DraggingSV = true;
        }

        // Hue Bar
        if (hueRect.Contains(Im.MousePos) && Im.MousePressed && !state.DraggingSV && !state.DraggingAlpha)
        {
            state.DraggingHue = true;
        }

        // Alpha Bar
        if (showAlpha && alphaRect.Contains(Im.MousePos) && Im.MousePressed && !state.DraggingSV && !state.DraggingHue)
        {
            state.DraggingAlpha = true;
        }

        // Mouse released
        if (Im.Context.Input.MouseReleased)
        {
            state.DraggingSV = false;
            state.DraggingHue = false;
            state.DraggingAlpha = false;
        }

        // Drag handling
        if (state.DraggingSV && Im.MouseDown)
        {
            float newS = Math.Clamp((Im.MousePos.X - svRect.X) / svRect.Width, 0f, 1f);
            float newV = Math.Clamp(1f - (Im.MousePos.Y - svRect.Y) / svRect.Height, 0f, 1f);
            if (Math.Abs(newS - s) > 0.001f || Math.Abs(newV - v) > 0.001f)
            {
                s = newS;
                v = newV;
                changed = true;
            }
        }

        if (state.DraggingHue && Im.MouseDown)
        {
            float newH = Math.Clamp((Im.MousePos.Y - hueRect.Y) / hueRect.Height, 0f, 1f) * 360f;
            if (Math.Abs(newH - h) > 0.5f)
            {
                h = newH;
                changed = true;
            }
        }

        if (state.DraggingAlpha && Im.MouseDown)
        {
            float newA = Math.Clamp((Im.MousePos.X - alphaRect.X) / alphaRect.Width, 0f, 1f);
            if (Math.Abs(newA - a) > 0.001f)
            {
                a = newA;
                changed = true;
            }
        }

        // Update color if changed
        if (changed)
        {
            color = HsvToRgb(h, s, v, a);
        }

        // === Rendering ===

        // SV Square background
        uint hueColor = GetHueColor(h);
        DrawSVSquare(svRect, hueColor);

        // SV marker
        float markerX = svRect.X + s * svRect.Width;
        float markerY = svRect.Y + (1f - v) * svRect.Height;
        DrawMarker(markerX, markerY, 6f);

        // Hue bar
        DrawHueBar(hueRect);

        // Hue marker
        float hueMarkerY = hueRect.Y + (h / 360f) * hueRect.Height;
        DrawHueMarker(hueRect.X, hueMarkerY, hueRect.Width);

        // Alpha bar
        if (showAlpha)
        {
            uint solidColor = WithAlpha(color, 255);
            DrawAlphaBar(alphaRect, solidColor);

            // Alpha marker
            float alphaMarkerX = alphaRect.X + a * alphaRect.Width;
            DrawAlphaMarker(alphaMarkerX, alphaRect.Y, alphaRect.Height);
        }

        // Preview swatch
        DrawPreview(previewRect, color);

        return changed;
    }

    private static void DrawSVSquare(ImRect rect, uint hueColor)
    {
        // Draw SV gradient using shader-based rendering
        Im.DrawSVRect(rect.X, rect.Y, rect.Width, rect.Height, hueColor);

        // Border
        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, 0, Im.Style.Border, 1f);
    }

    private static void DrawHueBar(ImRect rect)
    {
        // Draw 6 segments for hue spectrum
        float segmentHeight = rect.Height / 6f;

        // Simplified: draw solid color blocks
        uint[] hueColors = { 0xFFFF0000, 0xFFFFFF00, 0xFF00FF00, 0xFF00FFFF, 0xFF0000FF, 0xFFFF00FF };
        for (int i = 0; i < 6; i++)
        {
            Im.DrawRect(rect.X, rect.Y + i * segmentHeight, rect.Width, segmentHeight, hueColors[i]);
        }

        // Border
        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, 0, Im.Style.Border, 1f);
    }

    private static void DrawAlphaBar(ImRect rect, uint solidColor)
    {
        // Checkerboard background
        DrawCheckerboard(rect, 8f);

        // Gradient from transparent to solid (0 alpha -> full alpha)
        uint transparent = solidColor & 0x00FFFFFF;
        Span<SdfGradientStop> stops = stackalloc SdfGradientStop[2];
        stops[0] = new SdfGradientStop
        {
            Color = ImStyle.ToVector4(transparent),
            Params = new Vector4(0f, 0f, 0f, 0f)
        };
        stops[1] = new SdfGradientStop
        {
            Color = ImStyle.ToVector4(solidColor),
            Params = new Vector4(1f, 0f, 0f, 0f)
        };
        Im.DrawRoundedRectGradientStops(rect.X, rect.Y, rect.Width, rect.Height, radius: 0f, stops);

        // Border
        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, 0, Im.Style.Border, 1f);
    }

    private static void DrawPreview(ImRect rect, uint color)
    {
        // Checkerboard background
        DrawCheckerboard(rect, 8f);

        // Color overlay
        Im.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, color);

        // Border
        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, 0, Im.Style.Border, 1f);

        // Hex text
        Span<char> hexBuf = stackalloc char[10];
        FormatHex(color, hexBuf, out int hexLen);
        var hexSpan = hexBuf.Slice(0, hexLen);

        float smallFontSize = Im.Style.FontSize * 0.8f;
        float textWidth = ImTextMetrics.MeasureWidth(Im.Context.Font, hexSpan, smallFontSize);
        float textX = rect.X + (rect.Width - textWidth) * 0.5f;
        float textY = rect.Y + (rect.Height - smallFontSize) * 0.5f;

        uint textColor = GetContrastingColor(color);
        Im.Text(hexSpan, textX, textY, smallFontSize, textColor);
    }

    private static void DrawCheckerboard(ImRect rect, float cellSize)
    {
        uint light = 0xFFCCCCCC;
        uint dark = 0xFF999999;

        int cols = (int)MathF.Ceiling(rect.Width / cellSize);
        int rows = (int)MathF.Ceiling(rect.Height / cellSize);

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                bool isLight = (row + col) % 2 == 0;
                float cellX = rect.X + col * cellSize;
                float cellY = rect.Y + row * cellSize;
                float cellW = MathF.Min(cellSize, rect.X + rect.Width - cellX);
                float cellH = MathF.Min(cellSize, rect.Y + rect.Height - cellY);
                Im.DrawRect(cellX, cellY, cellW, cellH, isLight ? light : dark);
            }
        }
    }

    private static void DrawMarker(float cx, float cy, float radius)
    {
        Im.DrawCircle(cx, cy, radius + 1, 0xFFFFFFFF);
        Im.DrawCircle(cx, cy, radius - 1, 0xFF000000);
    }

    private static void DrawHueMarker(float x, float y, float width)
    {
        float markerHeight = 4f;
        Im.DrawRect(x, y - markerHeight * 0.5f, width, markerHeight, 0xFFFFFFFF);
        Im.DrawRoundedRectStroke(x, y - markerHeight * 0.5f, width, markerHeight, 0, 0xFF000000, 1f);
    }

    private static void DrawAlphaMarker(float x, float y, float height)
    {
        float markerWidth = 4f;
        Im.DrawRect(x - markerWidth * 0.5f, y, markerWidth, height, 0xFFFFFFFF);
        Im.DrawRoundedRectStroke(x - markerWidth * 0.5f, y, markerWidth, height, 0, 0xFF000000, 1f);
    }

    // Color conversion utilities

    private static void RgbToHsv(uint color, out float h, out float s, out float v, out float a)
    {
        float r = ((color >> 16) & 0xFF) / 255f;
        float g = ((color >> 8) & 0xFF) / 255f;
        float b = (color & 0xFF) / 255f;
        a = ((color >> 24) & 0xFF) / 255f;

        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        float delta = max - min;

        v = max;
        s = max > 0 ? delta / max : 0;

        if (delta == 0)
        {
            h = 0;
        }
        else if (max == r)
        {
            h = 60f * (((g - b) / delta) % 6);
        }
        else if (max == g)
        {
            h = 60f * (((b - r) / delta) + 2);
        }
        else
        {
            h = 60f * (((r - g) / delta) + 4);
        }

        if (h < 0) h += 360f;
    }

    private static uint HsvToRgb(float h, float s, float v, float a)
    {
        float c = v * s;
        float x = c * (1 - Math.Abs((h / 60f) % 2 - 1));
        float m = v - c;

        float r, g, b;

        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        byte rb = (byte)((r + m) * 255);
        byte gb = (byte)((g + m) * 255);
        byte bb = (byte)((b + m) * 255);
        byte ab = (byte)(a * 255);

        return (uint)(ab << 24 | rb << 16 | gb << 8 | bb);
    }

    private static uint GetHueColor(float h)
    {
        return HsvToRgb(h, 1f, 1f, 1f);
    }

    private static uint WithAlpha(uint color, byte alpha)
    {
        return (color & 0x00FFFFFF) | ((uint)alpha << 24);
    }

    private static void FormatHex(uint color, Span<char> buffer, out int length)
    {
        buffer[0] = '#';
        byte r = (byte)((color >> 16) & 0xFF);
        byte g = (byte)((color >> 8) & 0xFF);
        byte b = (byte)(color & 0xFF);
        byte a = (byte)((color >> 24) & 0xFF);

        ToHex(r, buffer.Slice(1, 2));
        ToHex(g, buffer.Slice(3, 2));
        ToHex(b, buffer.Slice(5, 2));
        ToHex(a, buffer.Slice(7, 2));
        length = 9;
    }

    private static void ToHex(byte value, Span<char> buffer)
    {
        const string hexChars = "0123456789ABCDEF";
        buffer[0] = hexChars[value >> 4];
        buffer[1] = hexChars[value & 0xF];
    }

    private static uint GetContrastingColor(uint color)
    {
        float r = ((color >> 16) & 0xFF) / 255f;
        float g = ((color >> 8) & 0xFF) / 255f;
        float b = (color & 0xFF) / 255f;
        float luminance = 0.299f * r + 0.587f * g + 0.114f * b;
        return luminance > 0.5f ? 0xFF000000 : 0xFFFFFFFF;
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
        _states[idx] = default;
        return idx;
    }
}
