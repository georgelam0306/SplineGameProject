using System.Numerics;
using System.Runtime.InteropServices;
using DerpLib.Sdf;
using DerpLib.ImGui.Core;

namespace DerpLib.ImGui.Rendering;

/// <summary>
/// Types of draw commands.
/// </summary>
public enum ImDrawCommandType : byte
{
    Rect,
    RoundedRect,
    RoundedRectStroke,
    RoundedRectPerCorner,
    RoundedRectPerCornerStroke,
    Circle,
    CircleStroke,
    Line,
    Polyline,
    Glyph,
    Image,
    Graph,
    FilledPolygon,
    SVRect, // Saturation-Value gradient rect for color picker
    GradientStopsRoundedRect, // Linear gradient with stop buffer
    GroupBegin,
    GroupEnd
}

/// <summary>
/// A single draw command with all parameters stored inline (zero allocation).
/// Uses explicit layout to minimize size.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public struct ImDrawCommand
{
    [FieldOffset(0)] public ImDrawCommandType Type;

    // Common parameters (screen-space, pre-scaled)
    [FieldOffset(4)] public float X;
    [FieldOffset(8)] public float Y;
    [FieldOffset(12)] public float Width;
    [FieldOffset(16)] public float Height;
    [FieldOffset(20)] public float R;
    [FieldOffset(24)] public float G;
    [FieldOffset(28)] public float B;
    [FieldOffset(32)] public float A;

    // Additional parameters
    [FieldOffset(36)] public float Radius;      // Corner radius or circle radius
    [FieldOffset(40)] public float StrokeWidth;

    // For lines
    [FieldOffset(44)] public float X2;
    [FieldOffset(48)] public float Y2;
    [FieldOffset(52)] public float Thickness;

    // For glyphs
    [FieldOffset(56)] public float U0;
    [FieldOffset(60)] public float V0;
    [FieldOffset(64)] public float U1;
    [FieldOffset(68)] public float V1;

    // Clip rectangle (ClipW < 0 means no clipping)
    [FieldOffset(72)] public float ClipX;
    [FieldOffset(76)] public float ClipY;
    [FieldOffset(80)] public float ClipW;
    [FieldOffset(84)] public float ClipH;

    // For graph/polygon - index into separate data arrays
    [FieldOffset(88)] public int DataIndex;
    [FieldOffset(92)] public int DataCount;

    // Shadow parameters (ShadowRadius <= 0 means no shadow)
    [FieldOffset(96)] public float ShadowOffsetX;
    [FieldOffset(100)] public float ShadowOffsetY;
    [FieldOffset(104)] public float ShadowRadius;
    [FieldOffset(108)] public float ShadowR;
    [FieldOffset(112)] public float ShadowG;
    [FieldOffset(116)] public float ShadowB;
    [FieldOffset(120)] public float ShadowA;

    // Sort key for z-ordering (higher values render on top)
    [FieldOffset(124)] public int SortKey;

    // Per-corner radii (for RoundedRectPerCorner)
    [FieldOffset(128)] public float RadiusTL;
    [FieldOffset(132)] public float RadiusTR;
    [FieldOffset(136)] public float RadiusBR;
    [FieldOffset(140)] public float RadiusBL;

    // Glow effect radius in pixels (0 = no glow). Applied to fill-only primitives.
    [FieldOffset(144)] public float GlowRadius;

    // Rotation in radians (applied in SDF evaluation).
    [FieldOffset(148)] public float Rotation;
}

/// <summary>
/// Zero-allocation draw command buffer.
/// Commands are stored as structs and executed on Flush().
/// </summary>
public class ImDrawList
{
    internal const int SecondaryFontAtlasSentinel = -2;

    private ImDrawCommand[] _commands;
    private int _count;
    private const int InitialCapacity = 512;

    // Separate storage for variable-length data (graphs, polygons)
    private float[] _floatData;
    private int _floatDataCount;
    private Vector2[] _pointData;
    private int _pointDataCount;
    private SdfGradientStop[] _gradientStopData;
    private int _gradientStopDataCount;

    // Current clip rect (captured when commands are added)
    private Vector4 _currentClipRect = new(-1, -1, -1, -1);

    // Current sort key for z-ordering (set by window begin/end)
    private int _currentSortKey;

    // Reusable index array for stable sorting
    private int[] _sortIndices = new int[InitialCapacity];

    public ImDrawList()
    {
        _commands = new ImDrawCommand[InitialCapacity];
        _floatData = new float[1024];
        _pointData = new Vector2[256];
        _gradientStopData = new SdfGradientStop[256];
        _count = 0;
        _floatDataCount = 0;
        _pointDataCount = 0;
        _gradientStopDataCount = 0;
    }

    /// <summary>
    /// Number of buffered commands.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Clear all buffered commands without executing.
    /// </summary>
    public void Clear()
    {
        _count = 0;
        _floatDataCount = 0;
        _pointDataCount = 0;
        _gradientStopDataCount = 0;
        _currentClipRect = new Vector4(-1, -1, -1, -1);
        _currentSortKey = 0;
    }

    /// <summary>
    /// Set the current clip rect for subsequent commands.
    /// </summary>
    public void SetClipRect(Vector4 clipRect)
    {
        _currentClipRect = clipRect;
    }

    /// <summary>
    /// Get the current clip rect used for subsequent commands.
    /// ClipW &lt; 0 means no clipping.
    /// </summary>
    public Vector4 GetClipRect() => _currentClipRect;

    /// <summary>
    /// Clear the current clip rect.
    /// </summary>
    public void ClearClipRect()
    {
        _currentClipRect = new Vector4(-1, -1, -1, -1);
    }


    /// <summary>
    /// Set the current sort key for subsequent commands.
    /// Higher values render on top.
    /// </summary>
    public void SetSortKey(int sortKey)
    {
        _currentSortKey = sortKey;
    }


    /// <summary>
    /// Get the current sort key.
    /// </summary>
    public int GetSortKey() => _currentSortKey;


    /// <summary>
    /// Increment the sort key (for popups/overlays).
    /// </summary>
    public void PushSortKey() => _currentSortKey++;

    /// <summary>
    /// Decrement the sort key (after popup/overlay).
    /// </summary>
    public void PopSortKey() => _currentSortKey--;

    private void EnsureCapacity()
    {
        if (_count >= _commands.Length)
        {
            var newCommands = new ImDrawCommand[_commands.Length * 2];
            Array.Copy(_commands, newCommands, _commands.Length);
            _commands = newCommands;
        }
    }

    private void EnsureFloatCapacity(int needed)
    {
        if (_floatDataCount + needed > _floatData.Length)
        {
            var newData = new float[_floatData.Length * 2];
            Array.Copy(_floatData, newData, _floatDataCount);
            _floatData = newData;
        }
    }

    private void EnsurePointCapacity(int needed)
    {
        if (_pointDataCount + needed > _pointData.Length)
        {
            var newData = new Vector2[_pointData.Length * 2];
            Array.Copy(_pointData, newData, _pointDataCount);
            _pointData = newData;
        }
    }

    private void EnsureGradientStopCapacity(int needed)
    {
        if (_gradientStopDataCount + needed > _gradientStopData.Length)
        {
            var newData = new SdfGradientStop[_gradientStopData.Length * 2];
            Array.Copy(_gradientStopData, newData, _gradientStopDataCount);
            _gradientStopData = newData;
        }
    }


    private void EnsureSortIndicesCapacity()
    {
        if (_sortIndices.Length < _commands.Length)
        {
            _sortIndices = new int[_commands.Length];
        }
    }

    /// <summary>
    /// Stable sort indices by SortKey using insertion sort.
    /// Preserves original order for commands with equal SortKey.
    /// </summary>
    private void StableSortBySortKey()
    {
        EnsureSortIndicesCapacity();

        // Initialize indices
        for (int i = 0; i < _count; i++)
            _sortIndices[i] = i;

        // Insertion sort - stable and efficient for small arrays
        for (int i = 1; i < _count; i++)
        {
            int key = _sortIndices[i];
            int keyValue = _commands[key].SortKey;
            int j = i - 1;

            // Move elements that are greater than keyValue
            while (j >= 0 && _commands[_sortIndices[j]].SortKey > keyValue)
            {
                _sortIndices[j + 1] = _sortIndices[j];
                j--;
            }
            _sortIndices[j + 1] = key;
        }
    }

    private void CaptureClipRect(ref ImDrawCommand cmd)
    {
        cmd.ClipX = _currentClipRect.X;
        cmd.ClipY = _currentClipRect.Y;
        cmd.ClipW = _currentClipRect.Z;
        cmd.ClipH = _currentClipRect.W;
        cmd.SortKey = _currentSortKey;
        cmd.GlowRadius = 0f;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Add Commands (all coordinates should be pre-scaled)
    // ═══════════════════════════════════════════════════════════════════════════

    public void AddRect(float x, float y, float width, float height, Vector4 color, float rotation = 0f)
    {
        EnsureCapacity();
        ref var cmd = ref _commands[_count++];
        cmd.Type = ImDrawCommandType.Rect;
        cmd.X = x; cmd.Y = y; cmd.Width = width; cmd.Height = height;
        cmd.R = color.X; cmd.G = color.Y; cmd.B = color.Z; cmd.A = color.W;
        CaptureClipRect(ref cmd);
        cmd.Rotation = rotation;
    }

    public void AddRectWithGlow(float x, float y, float width, float height, Vector4 color, float glowRadius, float rotation = 0f)
    {
        EnsureCapacity();
        ref var cmd = ref _commands[_count++];
        cmd.Type = ImDrawCommandType.Rect;
        cmd.X = x; cmd.Y = y; cmd.Width = width; cmd.Height = height;
        cmd.R = color.X; cmd.G = color.Y; cmd.B = color.Z; cmd.A = color.W;
        CaptureClipRect(ref cmd);
        cmd.GlowRadius = glowRadius;
        cmd.Rotation = rotation;
    }

    /// <summary>
    /// Add an SV (Saturation-Value) gradient rect for color pickers.
    /// </summary>
    /// <param name="hueColor">The hue color at full saturation and value.</param>
    public void AddSVRect(float x, float y, float width, float height, Vector4 hueColor)
    {
        EnsureCapacity();
        ref var cmd = ref _commands[_count++];
        cmd.Type = ImDrawCommandType.SVRect;
        cmd.X = x; cmd.Y = y; cmd.Width = width; cmd.Height = height;
        cmd.R = hueColor.X; cmd.G = hueColor.Y; cmd.B = hueColor.Z; cmd.A = hueColor.W;
        CaptureClipRect(ref cmd);
    }

    public void AddGradientStopsRoundedRect(float x, float y, float width, float height, float radius, ReadOnlySpan<SdfGradientStop> stops, float rotation = 0f)
    {
        if (stops.Length <= 0)
        {
            return;
        }

        EnsureCapacity();
        EnsureGradientStopCapacity(stops.Length);

        ref var cmd = ref _commands[_count++];
        cmd.Type = ImDrawCommandType.GradientStopsRoundedRect;
        cmd.X = x; cmd.Y = y; cmd.Width = width; cmd.Height = height;
        cmd.Radius = radius;

        Vector4 start = stops[0].Color;
        Vector4 end = stops[stops.Length - 1].Color;
        cmd.R = start.X; cmd.G = start.Y; cmd.B = start.Z; cmd.A = start.W;
        cmd.U0 = end.X; cmd.V0 = end.Y; cmd.U1 = end.Z; cmd.V1 = end.W;

        cmd.DataIndex = _gradientStopDataCount;
        cmd.DataCount = stops.Length;
        CaptureClipRect(ref cmd);
        cmd.Rotation = rotation;

        stops.CopyTo(_gradientStopData.AsSpan(_gradientStopDataCount));
        _gradientStopDataCount += stops.Length;
    }

    public void AddRoundedRect(float x, float y, float width, float height, float radius, Vector4 color, float rotation = 0f)
    {
        EnsureCapacity();
        ref var cmd = ref _commands[_count++];
        cmd.Type = ImDrawCommandType.RoundedRect;
        cmd.X = x; cmd.Y = y; cmd.Width = width; cmd.Height = height;
        cmd.Radius = radius;
        cmd.R = color.X; cmd.G = color.Y; cmd.B = color.Z; cmd.A = color.W;
        cmd.ShadowRadius = 0; // No shadow
        CaptureClipRect(ref cmd);
        cmd.Rotation = rotation;
    }

    public void AddRoundedRectStroke(float x, float y, float width, float height,
        float radius, Vector4 color, float strokeWidth, float rotation = 0f)
    {
        EnsureCapacity();
        ref var cmd = ref _commands[_count++];
        cmd.Type = ImDrawCommandType.RoundedRectStroke;
        cmd.X = x; cmd.Y = y; cmd.Width = width; cmd.Height = height;
        cmd.Radius = radius; cmd.StrokeWidth = strokeWidth;
        cmd.R = color.X; cmd.G = color.Y; cmd.B = color.Z; cmd.A = color.W;
        cmd.ShadowRadius = 0; // No shadow
        CaptureClipRect(ref cmd);
        cmd.Rotation = rotation;
    }

    public void AddRoundedRectWithShadow(float x, float y, float width, float height, float radius, Vector4 color,
        float shadowOffsetX, float shadowOffsetY, float shadowRadius, Vector4 shadowColor, float rotation = 0f)
    {
        EnsureCapacity();
        ref var cmd = ref _commands[_count++];
        cmd.Type = ImDrawCommandType.RoundedRect;
        cmd.X = x; cmd.Y = y; cmd.Width = width; cmd.Height = height;
        cmd.Radius = radius;
        cmd.R = color.X; cmd.G = color.Y; cmd.B = color.Z; cmd.A = color.W;
        cmd.ShadowOffsetX = shadowOffsetX;
        cmd.ShadowOffsetY = shadowOffsetY;
        cmd.ShadowRadius = shadowRadius;
        cmd.ShadowR = shadowColor.X; cmd.ShadowG = shadowColor.Y; cmd.ShadowB = shadowColor.Z; cmd.ShadowA = shadowColor.W;
        CaptureClipRect(ref cmd);
        cmd.Rotation = rotation;
    }

    public void AddRoundedRectWithShadowAndGlow(float x, float y, float width, float height, float radius, Vector4 color,
        float shadowOffsetX, float shadowOffsetY, float shadowRadius, Vector4 shadowColor, float glowRadius, float rotation = 0f)
    {
        EnsureCapacity();
        ref var cmd = ref _commands[_count++];
        cmd.Type = ImDrawCommandType.RoundedRect;
        cmd.X = x; cmd.Y = y; cmd.Width = width; cmd.Height = height;
        cmd.Radius = radius;
        cmd.R = color.X; cmd.G = color.Y; cmd.B = color.Z; cmd.A = color.W;
        cmd.ShadowOffsetX = shadowOffsetX;
        cmd.ShadowOffsetY = shadowOffsetY;
        cmd.ShadowRadius = shadowRadius;
        cmd.ShadowR = shadowColor.X; cmd.ShadowG = shadowColor.Y; cmd.ShadowB = shadowColor.Z; cmd.ShadowA = shadowColor.W;
        CaptureClipRect(ref cmd);
        cmd.GlowRadius = glowRadius;
        cmd.Rotation = rotation;
    }

    public void AddRoundedRectPerCorner(float x, float y, float width, float height,
        float radiusTL, float radiusTR, float radiusBR, float radiusBL, Vector4 color, float rotation = 0f)
    {
        EnsureCapacity();
        ref var cmd = ref _commands[_count++];
        cmd.Type = ImDrawCommandType.RoundedRectPerCorner;
        cmd.X = x; cmd.Y = y; cmd.Width = width; cmd.Height = height;
        cmd.RadiusTL = radiusTL; cmd.RadiusTR = radiusTR;
        cmd.RadiusBR = radiusBR; cmd.RadiusBL = radiusBL;
        cmd.R = color.X; cmd.G = color.Y; cmd.B = color.Z; cmd.A = color.W;
        cmd.ShadowRadius = 0; // No shadow
        CaptureClipRect(ref cmd);
        cmd.Rotation = rotation;
    }

    public void AddRoundedRectPerCornerStroke(float x, float y, float width, float height,
        float radiusTL, float radiusTR, float radiusBR, float radiusBL, Vector4 color, float strokeWidth, float rotation = 0f)
    {
        EnsureCapacity();
        ref var cmd = ref _commands[_count++];
        cmd.Type = ImDrawCommandType.RoundedRectPerCornerStroke;
        cmd.X = x; cmd.Y = y; cmd.Width = width; cmd.Height = height;
        cmd.RadiusTL = radiusTL; cmd.RadiusTR = radiusTR;
        cmd.RadiusBR = radiusBR; cmd.RadiusBL = radiusBL;
        cmd.StrokeWidth = strokeWidth;
        cmd.R = color.X; cmd.G = color.Y; cmd.B = color.Z; cmd.A = color.W;
        cmd.ShadowRadius = 0; // No shadow
        CaptureClipRect(ref cmd);
        cmd.Rotation = rotation;
    }

    public void AddCircle(float centerX, float centerY, float radius, Vector4 color, float rotation = 0f)
    {
        EnsureCapacity();
        ref var cmd = ref _commands[_count++];
        cmd.Type = ImDrawCommandType.Circle;
        cmd.X = centerX; cmd.Y = centerY; cmd.Radius = radius;
        cmd.R = color.X; cmd.G = color.Y; cmd.B = color.Z; cmd.A = color.W;
        CaptureClipRect(ref cmd);
        cmd.Rotation = rotation;
    }

    public void AddCircleWithGlow(float centerX, float centerY, float radius, Vector4 color, float glowRadius, float rotation = 0f)
    {
        EnsureCapacity();
        ref var cmd = ref _commands[_count++];
        cmd.Type = ImDrawCommandType.Circle;
        cmd.X = centerX; cmd.Y = centerY; cmd.Radius = radius;
        cmd.R = color.X; cmd.G = color.Y; cmd.B = color.Z; cmd.A = color.W;
        CaptureClipRect(ref cmd);
        cmd.GlowRadius = glowRadius;
        cmd.Rotation = rotation;
    }

    public void AddCircleStroke(float centerX, float centerY, float radius, Vector4 color, float strokeWidth, float rotation = 0f)
    {
        EnsureCapacity();
        ref var cmd = ref _commands[_count++];
        cmd.Type = ImDrawCommandType.CircleStroke;
        cmd.X = centerX; cmd.Y = centerY; cmd.Radius = radius;
        cmd.StrokeWidth = strokeWidth;
        cmd.R = color.X; cmd.G = color.Y; cmd.B = color.Z; cmd.A = color.W;
        CaptureClipRect(ref cmd);
        cmd.Rotation = rotation;
    }

    public void AddLine(float x1, float y1, float x2, float y2, float thickness, Vector4 color)
    {
        EnsureCapacity();
        ref var cmd = ref _commands[_count++];
        cmd.Type = ImDrawCommandType.Line;
        cmd.X = x1; cmd.Y = y1; cmd.X2 = x2; cmd.Y2 = y2;
        cmd.Thickness = thickness;
        cmd.R = color.X; cmd.G = color.Y; cmd.B = color.Z; cmd.A = color.W;
        CaptureClipRect(ref cmd);
    }

    public void AddPolyline(ReadOnlySpan<Vector2> points, float thickness, Vector4 color)
    {
        if (points.Length < 2)
        {
            return;
        }

        EnsureCapacity();
        EnsurePointCapacity(points.Length);

        ref var cmd = ref _commands[_count++];
        cmd.Type = ImDrawCommandType.Polyline;
        cmd.Thickness = thickness;
        cmd.R = color.X;
        cmd.G = color.Y;
        cmd.B = color.Z;
        cmd.A = color.W;
        cmd.DataIndex = _pointDataCount;
        cmd.DataCount = points.Length;
        CaptureClipRect(ref cmd);

        points.CopyTo(_pointData.AsSpan(_pointDataCount));
        _pointDataCount += points.Length;
    }

    public void AddGlyph(float x, float y, float width, float height,
        float u0, float v0, float u1, float v1, Vector4 color)
    {
        AddGlyph(x, y, width, height, u0, v0, u1, v1, color, fontAtlasIndex: 0, rotation: 0f);
    }

    public void AddGlyph(float x, float y, float width, float height,
        float u0, float v0, float u1, float v1, Vector4 color, bool secondaryFontAtlas)
    {
        AddGlyph(
            x,
            y,
            width,
            height,
            u0,
            v0,
            u1,
            v1,
            color,
            secondaryFontAtlas ? SecondaryFontAtlasSentinel : 0,
            rotation: 0f);
    }

    public void AddGlyph(float x, float y, float width, float height,
        float u0, float v0, float u1, float v1, Vector4 color, int fontAtlasIndex, float rotation = 0f)
    {
        EnsureCapacity();
        ref var cmd = ref _commands[_count++];
        cmd.Type = ImDrawCommandType.Glyph;
        cmd.X = x; cmd.Y = y; cmd.Width = width; cmd.Height = height;
        cmd.U0 = u0; cmd.V0 = v0; cmd.U1 = u1; cmd.V1 = v1;
        cmd.R = color.X; cmd.G = color.Y; cmd.B = color.Z; cmd.A = color.W;
        cmd.DataIndex = fontAtlasIndex;
        CaptureClipRect(ref cmd);
        cmd.Rotation = rotation;
    }

    public void AddImage(float x, float y, float width, float height,
        float u0, float v0, float u1, float v1, Vector4 tint, int textureIndex, float rotation = 0f)
    {
        EnsureCapacity();
        ref var cmd = ref _commands[_count++];
        cmd.Type = ImDrawCommandType.Image;
        cmd.X = x;
        cmd.Y = y;
        cmd.Width = width;
        cmd.Height = height;
        cmd.U0 = u0;
        cmd.V0 = v0;
        cmd.U1 = u1;
        cmd.V1 = v1;
        cmd.R = tint.X;
        cmd.G = tint.Y;
        cmd.B = tint.Z;
        cmd.A = tint.W;
        cmd.DataIndex = textureIndex;
        CaptureClipRect(ref cmd);
        cmd.Rotation = rotation;
    }

    public void AddGraph(ReadOnlySpan<float> values, float x, float y, float width, float height,
        float minVal, float maxVal, Vector4 color, float thickness)
    {
        EnsureCapacity();
        EnsureFloatCapacity(values.Length);

        ref var cmd = ref _commands[_count++];
        cmd.Type = ImDrawCommandType.Graph;
        cmd.X = x; cmd.Y = y; cmd.Width = width; cmd.Height = height;
        cmd.Thickness = thickness;
        cmd.R = color.X; cmd.G = color.Y; cmd.B = color.Z; cmd.A = color.W;
        cmd.U0 = minVal; cmd.V0 = maxVal; // Reuse UV fields for min/max
        cmd.DataIndex = _floatDataCount;
        cmd.DataCount = values.Length;
        CaptureClipRect(ref cmd);

        values.CopyTo(_floatData.AsSpan(_floatDataCount));
        _floatDataCount += values.Length;
    }

    public void AddFilledPolygon(ReadOnlySpan<Vector2> points, Vector4 color)
    {
        EnsureCapacity();
        EnsurePointCapacity(points.Length);

        ref var cmd = ref _commands[_count++];
        cmd.Type = ImDrawCommandType.FilledPolygon;
        cmd.R = color.X; cmd.G = color.Y; cmd.B = color.Z; cmd.A = color.W;
        cmd.DataIndex = _pointDataCount;
        cmd.DataCount = points.Length;
        CaptureClipRect(ref cmd);

        points.CopyTo(_pointData.AsSpan(_pointDataCount));
        _pointDataCount += points.Length;
    }

    public void AddGroupBegin(SdfBooleanOp op, float smoothness)
    {
        EnsureCapacity();
        ref var cmd = ref _commands[_count++];
        cmd.Type = ImDrawCommandType.GroupBegin;
        cmd.DataIndex = (int)op;
        cmd.Thickness = smoothness;
        CaptureClipRect(ref cmd);
    }

    public void AddGroupEnd(Vector4 fillColor)
    {
        EnsureCapacity();
        ref var cmd = ref _commands[_count++];
        cmd.Type = ImDrawCommandType.GroupEnd;
        cmd.R = fillColor.X; cmd.G = fillColor.Y; cmd.B = fillColor.Z; cmd.A = fillColor.W;
        CaptureClipRect(ref cmd);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Execute Commands
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Execute all buffered commands to the SDF buffer and clear the list.
    /// </summary>
    public void Flush(SdfBuffer buffer)
    {
        // Stable sort commands by SortKey (preserves order for equal keys)
        StableSortBySortKey();

        for (int i = 0; i < _count; i++)
        {
            int cmdIndex = _sortIndices[i];
            ref var cmd = ref _commands[cmdIndex];

            // Set clip rect for this command
            Vector4 clipRect = new(cmd.ClipX, cmd.ClipY, cmd.ClipW, cmd.ClipH);
            Vector4 color = new(cmd.R, cmd.G, cmd.B, cmd.A);

            switch (cmd.Type)
            {
                case ImDrawCommandType.GroupBegin:
                {
                    buffer.BeginGroup((SdfBooleanOp)cmd.DataIndex, cmd.Thickness);
                    break;
                }

                case ImDrawCommandType.GroupEnd:
                {
                    buffer.EndGroupClipped(color, clipRect);
                    break;
                }

                case ImDrawCommandType.Rect:
                {
                    var sdfCmd = SdfCommand.Rect(
                        new Vector2(cmd.X + cmd.Width / 2, cmd.Y + cmd.Height / 2),
                        new Vector2(cmd.Width / 2, cmd.Height / 2),
                        color);
                    if (cmd.GlowRadius > 0)
                    {
                        sdfCmd = sdfCmd.WithGlow(cmd.GlowRadius);
                    }
                    if (cmd.Rotation != 0f)
                    {
                        sdfCmd = sdfCmd.WithRotation(cmd.Rotation);
                    }
                    if (clipRect.W >= 0) sdfCmd = sdfCmd.WithClip(clipRect);
                    buffer.Add(sdfCmd);
                    break;
                }

                case ImDrawCommandType.SVRect:
                {
                    var sdfCmd = SdfCommand.Rect(
                        new Vector2(cmd.X + cmd.Width / 2, cmd.Y + cmd.Height / 2),
                        new Vector2(cmd.Width / 2, cmd.Height / 2),
                        color).WithSVGradient();
                    if (clipRect.W >= 0) sdfCmd = sdfCmd.WithClip(clipRect);
                    buffer.Add(sdfCmd);
                    break;
                }

                case ImDrawCommandType.GradientStopsRoundedRect:
                {
                    ReadOnlySpan<SdfGradientStop> stops = _gradientStopData.AsSpan(cmd.DataIndex, cmd.DataCount);
                    int stopStart = buffer.AddGradientStops(stops);
                    var endColor = new Vector4(cmd.U0, cmd.V0, cmd.U1, cmd.V1);

                    var sdfCmd = SdfCommand.RoundedRect(
                            new Vector2(cmd.X + cmd.Width / 2, cmd.Y + cmd.Height / 2),
                            new Vector2(cmd.Width / 2, cmd.Height / 2),
                            cmd.Radius,
                            color)
                        .WithLinearGradient(endColor, angle: 0f)
                        .WithGradientStops(stopStart, cmd.DataCount);

                    if (cmd.Rotation != 0f)
                    {
                        sdfCmd = sdfCmd.WithRotation(cmd.Rotation);
                    }
                    if (clipRect.W >= 0) sdfCmd = sdfCmd.WithClip(clipRect);
                    buffer.Add(sdfCmd);
                    break;
                }

                case ImDrawCommandType.RoundedRect:
                {
                    var sdfCmd = SdfCommand.RoundedRect(
                        new Vector2(cmd.X + cmd.Width / 2, cmd.Y + cmd.Height / 2),
                        new Vector2(cmd.Width / 2, cmd.Height / 2),
                        cmd.Radius,
                        color);
                    if (cmd.GlowRadius > 0)
                    {
                        sdfCmd = sdfCmd.WithGlow(cmd.GlowRadius);
                    }
                    if (cmd.Rotation != 0f)
                    {
                        sdfCmd = sdfCmd.WithRotation(cmd.Rotation);
                    }
                    if (clipRect.W >= 0) sdfCmd = sdfCmd.WithClip(clipRect);
                    buffer.Add(sdfCmd);
                    break;
                }

                case ImDrawCommandType.RoundedRectStroke:
                {
                    var sdfCmd = SdfCommand.RoundedRect(
                        new Vector2(cmd.X + cmd.Width / 2, cmd.Y + cmd.Height / 2),
                        new Vector2(cmd.Width / 2, cmd.Height / 2),
                        cmd.Radius,
                        Vector4.Zero)
                        .WithStroke(color, cmd.StrokeWidth);
                    if (cmd.Rotation != 0f)
                    {
                        sdfCmd = sdfCmd.WithRotation(cmd.Rotation);
                    }
                    if (clipRect.W >= 0) sdfCmd = sdfCmd.WithClip(clipRect);
                    buffer.Add(sdfCmd);
                    break;
                }

                case ImDrawCommandType.RoundedRectPerCorner:
                {
                    var sdfCmd = SdfCommand.RoundedRectPerCorner(
                        new Vector2(cmd.X + cmd.Width / 2, cmd.Y + cmd.Height / 2),
                        new Vector2(cmd.Width / 2, cmd.Height / 2),
                        cmd.RadiusTL, cmd.RadiusTR, cmd.RadiusBR, cmd.RadiusBL,
                        color);
                    if (cmd.GlowRadius > 0)
                    {
                        sdfCmd = sdfCmd.WithGlow(cmd.GlowRadius);
                    }
                    if (cmd.Rotation != 0f)
                    {
                        sdfCmd = sdfCmd.WithRotation(cmd.Rotation);
                    }
                    if (clipRect.W >= 0) sdfCmd = sdfCmd.WithClip(clipRect);
                    buffer.Add(sdfCmd);
                    break;
                }

                case ImDrawCommandType.RoundedRectPerCornerStroke:
                {
                    var sdfCmd = SdfCommand.RoundedRectPerCorner(
                        new Vector2(cmd.X + cmd.Width / 2, cmd.Y + cmd.Height / 2),
                        new Vector2(cmd.Width / 2, cmd.Height / 2),
                        cmd.RadiusTL, cmd.RadiusTR, cmd.RadiusBR, cmd.RadiusBL,
                        Vector4.Zero)
                        .WithStroke(color, cmd.StrokeWidth);
                    if (cmd.Rotation != 0f)
                    {
                        sdfCmd = sdfCmd.WithRotation(cmd.Rotation);
                    }
                    if (clipRect.W >= 0) sdfCmd = sdfCmd.WithClip(clipRect);
                    buffer.Add(sdfCmd);
                    break;
                }

                case ImDrawCommandType.Circle:
                {
                    var sdfCmd = SdfCommand.Circle(
                        new Vector2(cmd.X, cmd.Y),
                        cmd.Radius,
                        color);
                    if (cmd.GlowRadius > 0)
                    {
                        sdfCmd = sdfCmd.WithGlow(cmd.GlowRadius);
                    }
                    if (cmd.Rotation != 0f)
                    {
                        sdfCmd = sdfCmd.WithRotation(cmd.Rotation);
                    }
                    if (clipRect.W >= 0) sdfCmd = sdfCmd.WithClip(clipRect);
                    buffer.Add(sdfCmd);
                    break;
                }

                case ImDrawCommandType.CircleStroke:
                {
                    var sdfCmd = SdfCommand.Circle(
                        new Vector2(cmd.X, cmd.Y),
                        cmd.Radius,
                        Vector4.Zero)
                        .WithStroke(color, cmd.StrokeWidth);
                    if (cmd.Rotation != 0f)
                    {
                        sdfCmd = sdfCmd.WithRotation(cmd.Rotation);
                    }
                    if (clipRect.W >= 0) sdfCmd = sdfCmd.WithClip(clipRect);
                    buffer.Add(sdfCmd);
                    break;
                }

                case ImDrawCommandType.Line:
                {
                    var sdfCmd = SdfCommand.Line(
                        new Vector2(cmd.X, cmd.Y),
                        new Vector2(cmd.X2, cmd.Y2),
                        cmd.Thickness,
                        color);
                    if (clipRect.W >= 0) sdfCmd = sdfCmd.WithClip(clipRect);
                    buffer.Add(sdfCmd);
                    break;
                }

                case ImDrawCommandType.Polyline:
                {
                    var points = _pointData.AsSpan(cmd.DataIndex, cmd.DataCount);
                    int headerIndex = buffer.AddPolyline(points);
                    var sdfCmd = SdfCommand.Polyline(headerIndex, cmd.Thickness, color);
                    if (clipRect.W >= 0)
                    {
                        sdfCmd = sdfCmd.WithClip(clipRect);
                    }
                    buffer.Add(sdfCmd);
                    break;
                }

                case ImDrawCommandType.Glyph:
                {
                    var sdfCmd = SdfCommand.Glyph(
                        new Vector2(cmd.X + cmd.Width * 0.5f, cmd.Y + cmd.Height * 0.5f),
                        new Vector2(cmd.Width * 0.5f, cmd.Height * 0.5f),
                        new Vector4(cmd.U0, cmd.V0, cmd.U1, cmd.V1),
                        color);

                    if (cmd.DataIndex == SecondaryFontAtlasSentinel)
                    {
                        sdfCmd.Flags |= SdfCommand.FlagSecondaryFontAtlas;
                    }
                    else
                    {
                        var glyphAtlasIndex = cmd.DataIndex < 0 ? 0u : (uint)cmd.DataIndex;
                        sdfCmd = sdfCmd.WithFontAtlasIndex(glyphAtlasIndex);
                    }

                    if (cmd.Rotation != 0f)
                    {
                        sdfCmd = sdfCmd.WithRotation(cmd.Rotation);
                    }
                    if (clipRect.W >= 0) sdfCmd = sdfCmd.WithClip(clipRect);
                    buffer.Add(sdfCmd);
                    break;
                }

                case ImDrawCommandType.Image:
                {
                    uint textureIndex;
                    if (cmd.DataIndex < 0)
                    {
                        textureIndex = 0u;
                    }
                    else if (cmd.DataIndex > 255)
                    {
                        textureIndex = 255u;
                    }
                    else
                    {
                        textureIndex = (uint)cmd.DataIndex;
                    }
                    var sdfCmd = SdfCommand.Image(
                        new Vector2(cmd.X + cmd.Width * 0.5f, cmd.Y + cmd.Height * 0.5f),
                        new Vector2(cmd.Width * 0.5f, cmd.Height * 0.5f),
                        new Vector4(cmd.U0, cmd.V0, cmd.U1, cmd.V1),
                        color,
                        textureIndex);
                    if (cmd.Rotation != 0f)
                    {
                        sdfCmd = sdfCmd.WithRotation(cmd.Rotation);
                    }
                    if (clipRect.W >= 0) sdfCmd = sdfCmd.WithClip(clipRect);
                    buffer.Add(sdfCmd);
                    break;
                }

                case ImDrawCommandType.Graph:
                {
                    var values = _floatData.AsSpan(cmd.DataIndex, cmd.DataCount);
                    // Push clip if needed
                    if (clipRect.W >= 0) buffer.PushClipRect(clipRect);
                    buffer.AddGraph(values,
                        cmd.X, cmd.Y, cmd.Width, cmd.Height,
                        cmd.U0, cmd.V0, // min, max
                        color.X, color.Y, color.Z, color.W,
                        cmd.Thickness);
                    if (clipRect.W >= 0) buffer.PopClipRect();
                    break;
                }

                case ImDrawCommandType.FilledPolygon:
                {
                    var points = _pointData.AsSpan(cmd.DataIndex, cmd.DataCount);
                    // Push clip if needed
                    if (clipRect.W >= 0) buffer.PushClipRect(clipRect);
                    buffer.AddFilledPolygon(points, color.X, color.Y, color.Z, color.W);
                    if (clipRect.W >= 0) buffer.PopClipRect();
                    break;
                }
            }
        }

        Clear();
    }
}
