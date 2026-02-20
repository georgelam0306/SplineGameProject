using System.Numerics;
using GpuStruct;

namespace DerpLib.Sdf;

/// <summary>
/// Shape types for SDF rendering.
/// </summary>
public enum SdfShapeType : uint
{
    Circle = 0,
    Rect = 1,
    RoundedRect = 2,
    Line = 3,
    Bezier = 4,
    Polyline = 5,
    FilledPolygon = 6,
    Glyph = 7,
    TextGroup = 8,      // Text group - unions multiple glyphs with shared warp/effects
    RoundedRectPerCorner = 9, // Rounded rect with per-corner radii
    Image = 10, // Textured quad sampling from bindless texture array (WarpParams.z = texture index)
    GroupBegin = 100,   // Boolean group start marker
    GroupEnd = 101,     // Boolean group end marker
    MorphBegin = 102,   // Morph group start marker
    MorphEnd = 103,     // Morph group end marker
    MaskPush = 104,     // Push alpha mask onto stack
    MaskPop = 105,      // Pop alpha mask from stack
}

/// <summary>
/// Warp types for SDF coordinate distortion.
/// Stored in the per-command warp node chain (see SdfWarpNode).
/// </summary>
public enum SdfWarpType : uint
{
    None = 0,
    Wave = 1,      // Sinusoidal wave distortion
    Twist = 2,     // Rotational twist from center
    Bulge = 3,     // Fisheye/pinch effect
    Noise = 4,     // Noise-based distortion (future)
    Lattice = 5,   // 4x4 FFD lattice deformation
    Repeat = 6,    // Repeat/tile local coordinates
}

/// <summary>
/// Boolean operation modes for combining SDF shapes.
/// </summary>
public enum SdfBooleanOp : uint
{
    None = 0,           // No boolean - render independently
    Union = 1,          // min(a, b) - combine shapes
    Intersect = 2,      // max(a, b) - overlap only
    Subtract = 3,       // max(a, -b) - cut out
    SmoothUnion = 4,    // Smooth blend union
    SmoothIntersect = 5,// Smooth blend intersect
    SmoothSubtract = 6, // Smooth blend subtract
    Exclude = 7         // Symmetric difference (XOR)
}

/// <summary>
/// Cap styles for trimmed/dashed strokes.
/// </summary>
public enum SdfStrokeCap : uint
{
    Butt = 0,
    Round = 1,
    Square = 2,
    Soft = 3
}

/// <summary>
/// Represents a warp configuration for the warp stack.
/// </summary>
public readonly struct SdfWarp
{
    public readonly SdfWarpType Type;
    public readonly float Param1;  // Wave: frequency, Twist: strength, Bulge: strength
    public readonly float Param2;  // Wave: amplitude, Twist: unused, Bulge: radius
    public readonly float Param3;  // Wave: phase/time offset

    public SdfWarp(SdfWarpType type, float param1 = 0, float param2 = 0, float param3 = 0)
    {
        Type = type;
        Param1 = param1;
        Param2 = param2;
        Param3 = param3;
    }

    public static SdfWarp None => new(SdfWarpType.None);

    /// <summary>Wave warp: sinusoidal distortion along one axis.</summary>
    /// <param name="frequency">How many waves per unit distance.</param>
    /// <param name="amplitude">How far the wave displaces (in pixels).</param>
    /// <param name="phase">Phase offset (use time for animation).</param>
    public static SdfWarp Wave(float frequency, float amplitude, float phase = 0)
        => new(SdfWarpType.Wave, frequency, amplitude, phase);

    /// <summary>Twist warp: rotates coordinates based on distance from center.</summary>
    /// <param name="strength">Radians of rotation per 100 pixels from center.</param>
    public static SdfWarp Twist(float strength)
        => new(SdfWarpType.Twist, strength);

    /// <summary>Bulge warp: fisheye/pinch effect.</summary>
    /// <param name="strength">Positive = bulge out, negative = pinch in.</param>
    /// <param name="radius">Radius of effect (0 = use shape size).</param>
    public static SdfWarp Bulge(float strength, float radius = 0)
        => new(SdfWarpType.Bulge, strength, radius);

    /// <summary>Lattice warp: 4x4 FFD grid deformation.</summary>
    /// <param name="latticeIndex">Index into the lattice buffer.</param>
    /// <param name="scaleX">Width of the lattice area in pixels.</param>
    /// <param name="scaleY">Height of the lattice area in pixels.</param>
    public static SdfWarp Lattice(int latticeIndex, float scaleX = 200f, float scaleY = 200f)
        => new(SdfWarpType.Lattice, latticeIndex, scaleX, scaleY);

    /// <summary>Repeat warp: tiles local coordinates to repeat the shape.</summary>
    /// <param name="periodX">Repeat period in pixels for X (must be &gt; 0).</param>
    /// <param name="periodY">Repeat period in pixels for Y (0 = use <paramref name="periodX"/>).</param>
    /// <param name="offsetPx">Offset in pixels applied to both axes before repeat.</param>
    public static SdfWarp Repeat(float periodX, float periodY = 0f, float offsetPx = 0f)
        => new(SdfWarpType.Repeat, periodX, periodY, offsetPx);
}

/// <summary>
/// A single SDF command for the compute shader.
/// Uses std430 layout via [GpuStruct] generator.
/// </summary>
[GpuStruct]
public partial struct SdfCommand
{
    private const uint FlagTrimEnabled = 1u << 0;
    private const uint FlagDashEnabled = 1u << 1;
    public const uint FlagInternalNoRender = 1u << 17;
    public const uint FlagSecondaryFontAtlas = 1u << 18;
    public const uint FlagExplicitFontAtlasIndex = 1u << 19;
    private const int TrimCapShift = 8;
    private const int DashCapShift = 12;
    private const uint CapMask = 0xFu;
    private const int BlendModeShift = 20;
    private const uint BlendModeMask = 0xFFu;

    /// <summary>Shape type (circle, rect, etc.)</summary>
    public partial uint Type { get; set; }

    /// <summary>Reserved flags for future use.</summary>
    public partial uint Flags { get; set; }

    /// <summary>Center position in screen coordinates.</summary>
    public partial Vector2 Position { get; set; }

    /// <summary>Size (radius for circle, half-extents for rect).</summary>
    public partial Vector2 Size { get; set; }

    /// <summary>
    /// Rotation parameters: x=rotation radians, y=reserved for internal engine use (warp chain head pointer).
    /// </summary>
    public partial Vector2 Rotation { get; set; }

    /// <summary>RGBA fill color (0-1 range).</summary>
    public partial Vector4 Color { get; set; }

    /// <summary>Extra parameters (x=cornerRadius for rounded rect, etc.)</summary>
    public partial Vector4 Params { get; set; }

    /// <summary>RGBA stroke color (0-1 range). Alpha=0 means no stroke.</summary>
    public partial Vector4 StrokeColor { get; set; }

    /// <summary>Effect parameters: x=strokeWidth, y=glowRadius, z=softEdge, w=gradientType.</summary>
    public partial Vector4 Effects { get; set; }

    /// <summary>Second color for gradients. Used when Effects.w > 0.</summary>
    public partial Vector4 GradientColor { get; set; }

    /// <summary>Gradient parameters: x=angle (radians), y=centerX, z=centerY, w=reserved.</summary>
    public partial Vector4 GradientParams { get; set; }

    /// <summary>
    /// Extra per-command parameters.
    /// Currently used by multi-stop gradients: WarpParams.x = stopCount.
    /// WarpParams.y stores the modifier chain head pointer (bitwise, uint-as-float).
    /// WarpParams.z stores explicit glyph atlas index when FlagExplicitFontAtlasIndex is set.
    /// For Image commands, WarpParams.z stores the bindless texture index to sample.
    /// </summary>
    public partial Vector4 WarpParams { get; set; }

    /// <summary>Boolean operation parameters: x=operation (SdfBooleanOp), y=smoothness factor.</summary>
    public partial Vector2 BooleanParams { get; set; }

    /// <summary>
     /// Clip rectangle: x, y = top-left corner; z = width, w = height.
     /// If w &lt; 0, clipping is disabled for this command.
     /// </summary>
    public partial Vector4 ClipRect { get; set; }

    /// <summary>
    /// Stroke trim parameters (pixels):
    /// x=start, y=length, z=offset, w=capSoftness (used for Soft cap).
    /// Enabled via Flags (see FlagTrimEnabled).
    /// </summary>
    public partial Vector4 TrimParams { get; set; }

    /// <summary>
    /// Stroke dash parameters (pixels):
    /// x=dashLength, y=gapLength, z=offset, w=capSoftness (used for Soft cap).
    /// Enabled via Flags (see FlagDashEnabled).
    /// </summary>
    public partial Vector4 DashParams { get; set; }

    /// <summary>Default soft edge width for anti-aliasing.</summary>
    public const float DefaultSoftEdge = 1.5f;

    /// <summary>Default soft edge width for font/text anti-aliasing.</summary>
    public const float DefaultFontSoftEdge = 1.5f;

    /// <summary>Sentinel value for "no clipping".</summary>
    public static readonly Vector4 NoClip = new(-1, -1, -1, -1);

    public static SdfCommand Circle(Vector2 center, float radius, Vector4 color)
    {
        return new SdfCommand
        {
            Type = (uint)SdfShapeType.Circle,
            Flags = 0,
            Position = center,
            Size = new Vector2(radius, radius),
            Color = color,
            Params = Vector4.Zero,
            StrokeColor = Vector4.Zero,
            Effects = new Vector4(0, 0, DefaultSoftEdge, 0),
            GradientColor = Vector4.Zero,
            GradientParams = Vector4.Zero,
            WarpParams = Vector4.Zero,
            ClipRect = NoClip,
            TrimParams = Vector4.Zero,
            DashParams = Vector4.Zero
        };
    }

    public static SdfCommand Rect(Vector2 center, Vector2 halfSize, Vector4 color)
    {
        return new SdfCommand
        {
            Type = (uint)SdfShapeType.Rect,
            Flags = 0,
            Position = center,
            Size = halfSize,
            Color = color,
            Params = Vector4.Zero,
            StrokeColor = Vector4.Zero,
            Effects = new Vector4(0, 0, DefaultSoftEdge, 0),
            GradientColor = Vector4.Zero,
            GradientParams = Vector4.Zero,
            WarpParams = Vector4.Zero,
            ClipRect = NoClip,
            TrimParams = Vector4.Zero,
            DashParams = Vector4.Zero
        };
    }

    public static SdfCommand RoundedRect(Vector2 center, Vector2 halfSize, float cornerRadius, Vector4 color)
    {
        return new SdfCommand
        {
            Type = (uint)SdfShapeType.RoundedRect,
            Flags = 0,
            Position = center,
            Size = halfSize,
            Color = color,
            Params = new Vector4(cornerRadius, 0, 0, 0),
            StrokeColor = Vector4.Zero,
            Effects = new Vector4(0, 0, DefaultSoftEdge, 0),
            GradientColor = Vector4.Zero,
            GradientParams = Vector4.Zero,
            WarpParams = Vector4.Zero,
            ClipRect = NoClip,
            TrimParams = Vector4.Zero,
            DashParams = Vector4.Zero
        };
    }

    /// <summary>
    /// Creates a rounded rectangle with per-corner radii.
    /// </summary>
    /// <param name="center">Center position.</param>
    /// <param name="halfSize">Half-extents of the rectangle.</param>
    /// <param name="radiusTL">Top-left corner radius.</param>
    /// <param name="radiusTR">Top-right corner radius.</param>
    /// <param name="radiusBR">Bottom-right corner radius.</param>
    /// <param name="radiusBL">Bottom-left corner radius.</param>
    /// <param name="color">Fill color.</param>
    public static SdfCommand RoundedRectPerCorner(Vector2 center, Vector2 halfSize,
        float radiusTL, float radiusTR, float radiusBR, float radiusBL, Vector4 color)
    {
        return new SdfCommand
        {
            Type = (uint)SdfShapeType.RoundedRectPerCorner,
            Flags = 0,
            Position = center,
            Size = halfSize,
            Color = color,
            Params = new Vector4(radiusTL, radiusTR, radiusBR, radiusBL),
            StrokeColor = Vector4.Zero,
            Effects = new Vector4(0, 0, DefaultSoftEdge, 0),
            GradientColor = Vector4.Zero,
            GradientParams = Vector4.Zero,
            WarpParams = Vector4.Zero,
            ClipRect = NoClip,
            TrimParams = Vector4.Zero,
            DashParams = Vector4.Zero
        };
    }

    /// <summary>
    /// Creates a line (capsule) from point A to point B.
    /// </summary>
    /// <param name="a">Start point.</param>
    /// <param name="b">End point.</param>
    /// <param name="thickness">Line thickness (diameter).</param>
    /// <param name="color">Line color.</param>
    public static SdfCommand Line(Vector2 a, Vector2 b, float thickness, Vector4 color)
    {
        return new SdfCommand
        {
            Type = (uint)SdfShapeType.Line,
            Flags = 0,
            Position = a,      // Start point
            Size = b,          // End point (reusing Size field)
            Color = color,
            Params = new Vector4(thickness, 0, 0, 0),
            StrokeColor = Vector4.Zero,
            Effects = new Vector4(0, 0, DefaultSoftEdge, 0),
            GradientColor = Vector4.Zero,
            GradientParams = Vector4.Zero,
            WarpParams = Vector4.Zero,
            ClipRect = NoClip,
            TrimParams = Vector4.Zero,
            DashParams = Vector4.Zero
        };
    }

    /// <summary>
    /// Creates a quadratic bezier curve from P0 to P2 with control point P1.
    /// </summary>
    /// <param name="p0">Start point.</param>
    /// <param name="p1">Control point (defines curve shape).</param>
    /// <param name="p2">End point.</param>
    /// <param name="thickness">Curve thickness.</param>
    /// <param name="color">Curve color.</param>
    public static SdfCommand Bezier(Vector2 p0, Vector2 p1, Vector2 p2, float thickness, Vector4 color)
    {
        return new SdfCommand
        {
            Type = (uint)SdfShapeType.Bezier,
            Flags = 0,
            Position = p0,     // Start point
            Size = p2,         // End point (reusing Size field)
            Color = color,
            Params = new Vector4(thickness, p1.X, p1.Y, 0),  // thickness + control point
            StrokeColor = Vector4.Zero,
            Effects = new Vector4(0, 0, DefaultSoftEdge, 0),
            GradientColor = Vector4.Zero,
            GradientParams = Vector4.Zero,
            WarpParams = Vector4.Zero,
            ClipRect = NoClip,
            TrimParams = Vector4.Zero,
            DashParams = Vector4.Zero
        };
    }

    /// <summary>
    /// Creates a glyph quad that samples from a bound font atlas.
    /// Params stores UV rect: (u0, v0, u1, v1).
    /// </summary>
    public static SdfCommand Glyph(Vector2 center, Vector2 halfSize, Vector4 uvRect, Vector4 color)
    {
        return new SdfCommand
        {
            Type = (uint)SdfShapeType.Glyph,
            Flags = 0,
            Position = center,
            Size = halfSize,
            Color = color,
            Params = uvRect,
            StrokeColor = Vector4.Zero,
            Effects = new Vector4(0, 0, DefaultFontSoftEdge, 0),
            GradientColor = Vector4.Zero,
            GradientParams = Vector4.Zero,
            WarpParams = Vector4.Zero,
            ClipRect = NoClip,
            TrimParams = Vector4.Zero,
            DashParams = Vector4.Zero
        };
    }

    /// <summary>
    /// Creates a text group that wraps multiple glyph commands.
    /// The shader evaluates all glyphs in the range and unions them before applying effects.
    /// Params stores: x=firstGlyphIndex, y=glyphCount, z=reserved, w=reserved.
    /// </summary>
    /// <param name="center">Center of text bounds.</param>
    /// <param name="halfSize">Half extents of text bounds.</param>
    /// <param name="firstGlyphIndex">Index of the first glyph command in the buffer.</param>
    /// <param name="glyphCount">Number of glyph commands in this group.</param>
    /// <param name="color">Fill color for the combined text.</param>
    public static SdfCommand TextGroup(Vector2 center, Vector2 halfSize, int firstGlyphIndex, int glyphCount, Vector4 color)
    {
        return new SdfCommand
        {
            Type = (uint)SdfShapeType.TextGroup,
            Flags = 0,
            Position = center,
            Size = halfSize,
            Color = color,
            Params = new Vector4(firstGlyphIndex, glyphCount, 0, 0),
            StrokeColor = Vector4.Zero,
            Effects = new Vector4(0, 0, DefaultFontSoftEdge, 0),
            GradientColor = Vector4.Zero,
            GradientParams = Vector4.Zero,
            WarpParams = Vector4.Zero,
            ClipRect = NoClip,
            TrimParams = Vector4.Zero,
            DashParams = Vector4.Zero
        };
    }

    /// <summary>
    /// Creates a polyline from a pre-registered header.
    /// Shader reads bounds, startIndex, pointCount from header buffer.
    /// </summary>
    /// <param name="headerIndex">Index into the polyline header buffer.</param>
    /// <param name="thickness">Stroke thickness.</param>
    /// <param name="color">Stroke color.</param>
    public static SdfCommand Polyline(int headerIndex, float thickness, Vector4 color)
    {
        return new SdfCommand
        {
            Type = (uint)SdfShapeType.Polyline,
            Flags = 0,
            Position = Vector2.Zero,  // Not used - data comes from header
            Size = Vector2.Zero,      // Not used
            Color = color,
            Params = new Vector4(thickness, headerIndex, 0, 0),
            StrokeColor = Vector4.Zero,
            Effects = new Vector4(0, 0, DefaultSoftEdge, 0),
            GradientColor = Vector4.Zero,
            GradientParams = Vector4.Zero,
            WarpParams = Vector4.Zero,
            ClipRect = NoClip,
            TrimParams = Vector4.Zero,
            DashParams = Vector4.Zero
        };
    }

    /// <summary>
    /// Creates a filled polygon from a pre-registered header.
    /// Shader reads bounds, startIndex, pointCount from header buffer.
    /// </summary>
    /// <param name="headerIndex">Index into the polyline header buffer.</param>
    /// <param name="color">Fill color.</param>
    public static SdfCommand FilledPolygon(int headerIndex, Vector4 color)
    {
        return new SdfCommand
        {
            Type = (uint)SdfShapeType.FilledPolygon,
            Flags = 0,
            Position = Vector2.Zero,
            Size = Vector2.Zero,
            Color = color,
            Params = new Vector4(0, headerIndex, 0, 0),
            StrokeColor = Vector4.Zero,
            Effects = new Vector4(0, 0, DefaultSoftEdge, 0),
            GradientColor = Vector4.Zero,
            GradientParams = Vector4.Zero,
            WarpParams = Vector4.Zero,
            ClipRect = NoClip,
            TrimParams = Vector4.Zero,
            DashParams = Vector4.Zero
        };
    }

    /// <summary>
    /// Apply effects to this command.
    /// </summary>
    /// <param name="strokeColor">Stroke color (alpha=0 means no stroke).</param>
    /// <param name="strokeWidth">Stroke width in pixels.</param>
    /// <param name="glowRadius">Glow radius in pixels (0 = no glow).</param>
    /// <param name="softEdge">Anti-aliasing edge width (default 1.0).</param>
    public SdfCommand WithEffects(Vector4 strokeColor, float strokeWidth = 0, float glowRadius = 0, float softEdge = DefaultSoftEdge)
    {
        var cmd = this;
        cmd.StrokeColor = strokeColor;
        cmd.Effects = new Vector4(strokeWidth, glowRadius, softEdge, Effects.W);
        return cmd;
    }

    /// <summary>
    /// Apply glow effect to this command.
    /// </summary>
    public SdfCommand WithGlow(float glowRadius, float softEdge = DefaultSoftEdge)
    {
        var cmd = this;
        cmd.Effects = new Vector4(Effects.X, glowRadius, softEdge, Effects.W);
        return cmd;
    }

    /// <summary>
    /// Apply stroke to this command.
    /// </summary>
    public SdfCommand WithStroke(Vector4 strokeColor, float strokeWidth)
    {
        var cmd = this;
        cmd.StrokeColor = strokeColor;
        cmd.Effects = new Vector4(strokeWidth, Effects.Y, Effects.Z, Effects.W);
        return cmd;
    }

    /// <summary>
    /// Apply a linear gradient fill to this command.
    /// </summary>
    /// <param name="endColor">Gradient end color (start color is the fill color).</param>
    /// <param name="angle">Gradient angle in radians (0 = left-to-right, PI/2 = top-to-bottom).</param>
    public SdfCommand WithLinearGradient(Vector4 endColor, float angle = 0f)
    {
        var cmd = this;
        cmd.GradientColor = endColor;
        cmd.GradientParams = new Vector4(angle, 0, 0, GradientParams.W);
        cmd.Effects = new Vector4(Effects.X, Effects.Y, Effects.Z, 1f); // gradientType = 1 (linear)
        return cmd;
    }

    /// <summary>
    /// Apply a radial gradient fill to this command.
    /// </summary>
    /// <param name="endColor">Gradient outer color (start color is the fill color at center).</param>
    /// <param name="centerX">Gradient center X offset from shape center (0 = centered).</param>
    /// <param name="centerY">Gradient center Y offset from shape center (0 = centered).</param>
    /// <param name="radiusScale">Scale factor applied to the computed shape radius (1 = fill reaches the edge).</param>
    public SdfCommand WithRadialGradient(Vector4 endColor, float centerX = 0f, float centerY = 0f, float radiusScale = 1f)
    {
        var cmd = this;
        cmd.GradientColor = endColor;
        cmd.GradientParams = new Vector4(radiusScale, centerX, centerY, GradientParams.W);
        cmd.Effects = new Vector4(Effects.X, Effects.Y, Effects.Z, 2f); // gradientType = 2 (radial)
        return cmd;
    }

    /// <summary>
    /// Apply an angular (conic) gradient fill to this command.
    /// </summary>
    /// <param name="endColor">Gradient end color (start color is the fill color).</param>
    /// <param name="centerX">Gradient center X offset from shape center (0 = centered).</param>
    /// <param name="centerY">Gradient center Y offset from shape center (0 = centered).</param>
    /// <param name="angleOffset">Angle offset in radians (0 = seam on +X axis).</param>
    public SdfCommand WithAngularGradient(Vector4 endColor, float centerX = 0f, float centerY = 0f, float angleOffset = 0f)
    {
        var cmd = this;
        cmd.GradientColor = endColor;
        cmd.GradientParams = new Vector4(angleOffset, centerX, centerY, GradientParams.W);
        cmd.Effects = new Vector4(Effects.X, Effects.Y, Effects.Z, 4f); // gradientType = 4 (angular)
        return cmd;
    }

    /// <summary>
    /// Reference a range of gradient stops in the dedicated gradient stop buffer.
    /// stopStart is stored in GradientParams.w, stopCount is stored in WarpParams.x.
    /// </summary>
    public SdfCommand WithGradientStops(int stopStart, int stopCount)
    {
        if (stopStart < 0)
        {
            stopStart = 0;
        }
        if (stopCount < 0)
        {
            stopCount = 0;
        }

        var cmd = this;
        cmd.GradientParams = new Vector4(GradientParams.X, GradientParams.Y, GradientParams.Z, stopStart);
        cmd.WarpParams = new Vector4(stopCount, WarpParams.Y, WarpParams.Z, WarpParams.W);
        return cmd;
    }

    /// <summary>
    /// Select an explicit font atlas index for glyph sampling.
    /// Used by ImGui text so mixed font variants can render in a single draw list.
    /// </summary>
    public SdfCommand WithFontAtlasIndex(uint atlasIndex)
    {
        var cmd = this;
        cmd.Flags |= FlagExplicitFontAtlasIndex;
        cmd.WarpParams = new Vector4(WarpParams.X, WarpParams.Y, atlasIndex, WarpParams.W);
        return cmd;
    }

    /// <summary>
    /// Apply an SV (Saturation-Value) gradient for color pickers.
    /// The fill color should be the hue at full saturation and value.
    /// </summary>
    public SdfCommand WithSVGradient()
    {
        var cmd = this;
        cmd.Effects = new Vector4(Effects.X, Effects.Y, Effects.Z, 3f); // gradientType = 3 (SV)
        return cmd;
    }

    /// <summary>
    /// Apply a clip rectangle to this command.
    /// Pixels outside this rect will not be rendered.
    /// </summary>
    /// <param name="x">Left edge of clip rect.</param>
    /// <param name="y">Top edge of clip rect.</param>
    /// <param name="width">Width of clip rect.</param>
    /// <param name="height">Height of clip rect.</param>
    public SdfCommand WithClip(float x, float y, float width, float height)
    {
        var cmd = this;
        cmd.ClipRect = new Vector4(x, y, width, height);
        return cmd;
    }

    /// <summary>
    /// Creates a textured image quad that samples RGBA from a bindless texture.
    /// Params stores UV rect: (u0, v0, u1, v1), WarpParams.z stores the texture index.
    /// </summary>
    public static SdfCommand Image(Vector2 center, Vector2 halfSize, Vector4 uvRect, Vector4 tint, uint textureIndex)
    {
        return new SdfCommand
        {
            Type = (uint)SdfShapeType.Image,
            Flags = 0,
            Position = center,
            Size = halfSize,
            Color = tint,
            Params = uvRect,
            StrokeColor = Vector4.Zero,
            Effects = new Vector4(0, 0, DefaultSoftEdge, 0),
            GradientColor = Vector4.Zero,
            GradientParams = Vector4.Zero,
            WarpParams = new Vector4(0, 0, textureIndex, 0),
            BooleanParams = Vector2.Zero,
            ClipRect = NoClip,
            TrimParams = Vector4.Zero,
            DashParams = Vector4.Zero
        };
    }

    /// <summary>
    /// Apply a clip rectangle to this command.
    /// </summary>
    public SdfCommand WithClip(Vector4 clipRect)
    {
        var cmd = this;
        cmd.ClipRect = clipRect;
        return cmd;
    }

    /// <summary>
    /// Apply a rotation (in radians) to this command.
    /// </summary>
    public SdfCommand WithRotation(float radians)
    {
        var cmd = this;
        cmd.Rotation = new Vector2(radians, Rotation.Y);
        return cmd;
    }

    /// <summary>
    /// Apply a boolean operation to this command.
    /// Commands with the same boolean op will be combined before rendering.
    /// </summary>
    /// <param name="op">Boolean operation (Union, Intersect, Subtract, or smooth variants).</param>
    /// <param name="smoothness">Smoothness factor for SmoothUnion/Intersect/Subtract (default 10).</param>
    public SdfCommand WithBoolean(SdfBooleanOp op, float smoothness = 10f)
    {
        var cmd = this;
        cmd.BooleanParams = new Vector2((float)op, smoothness);
        return cmd;
    }

    /// <summary>
    /// Creates a group begin marker. All commands until GroupEnd are combined using the specified boolean op.
    /// </summary>
    /// <param name="op">Boolean operation for the group.</param>
    /// <param name="smoothness">Smoothness factor for smooth boolean operations.</param>
    internal static SdfCommand GroupBegin(SdfBooleanOp op, float smoothness = 10f)
    {
        return new SdfCommand
        {
            Type = (uint)SdfShapeType.GroupBegin,
            Flags = 0,
            Position = Vector2.Zero,
            Size = Vector2.Zero,
            Color = Vector4.Zero,
            Params = Vector4.Zero,
            StrokeColor = Vector4.Zero,
            Effects = new Vector4(0, 0, DefaultSoftEdge, 0),
            GradientColor = Vector4.Zero,
            GradientParams = Vector4.Zero,
            WarpParams = Vector4.Zero,
            BooleanParams = new Vector2((float)op, smoothness),
            ClipRect = NoClip,
            TrimParams = Vector4.Zero,
            DashParams = Vector4.Zero
        };
    }

    /// <summary>
    /// Creates a group end marker. Uses the styling from this command for the final combined shape.
    /// </summary>
    internal static SdfCommand GroupEnd(Vector4 color, Vector4 strokeColor = default, float strokeWidth = 0f,
        float glowRadius = 0f, float softEdge = DefaultSoftEdge)
    {
        return new SdfCommand
        {
            Type = (uint)SdfShapeType.GroupEnd,
            Flags = 0,
            Position = Vector2.Zero,
            Size = Vector2.Zero,
            Color = color,
            Params = Vector4.Zero,
            StrokeColor = strokeColor,
            Effects = new Vector4(strokeWidth, glowRadius, softEdge, 0),
            GradientColor = Vector4.Zero,
            GradientParams = Vector4.Zero,
            WarpParams = Vector4.Zero,
            BooleanParams = Vector2.Zero,
            ClipRect = NoClip,
            TrimParams = Vector4.Zero,
            DashParams = Vector4.Zero
        };
    }

    /// <summary>
    /// Creates a morph begin marker. The next two shapes will be blended using the morph factor.
    /// </summary>
    /// <param name="morphFactor">Blend factor: 0 = first shape, 1 = second shape.</param>
    internal static SdfCommand MorphBegin(float morphFactor)
    {
        return new SdfCommand
        {
            Type = (uint)SdfShapeType.MorphBegin,
            Flags = 0,
            Position = Vector2.Zero,
            Size = Vector2.Zero,
            Color = Vector4.Zero,
            Params = new Vector4(morphFactor, 0, 0, 0),  // X = morphFactor
            StrokeColor = Vector4.Zero,
            Effects = new Vector4(0, 0, DefaultSoftEdge, 0),
            GradientColor = Vector4.Zero,
            GradientParams = Vector4.Zero,
            WarpParams = Vector4.Zero,
            BooleanParams = Vector2.Zero,
            ClipRect = NoClip,
            TrimParams = Vector4.Zero,
            DashParams = Vector4.Zero
        };
    }

    /// <summary>
    /// Creates a morph end marker. Uses the styling from this command for the blended shape.
    /// </summary>
    internal static SdfCommand MorphEnd(Vector4 color, Vector4 strokeColor = default, float strokeWidth = 0f,
        float glowRadius = 0f, float softEdge = DefaultSoftEdge)
    {
        return new SdfCommand
        {
            Type = (uint)SdfShapeType.MorphEnd,
            Flags = 0,
            Position = Vector2.Zero,
            Size = Vector2.Zero,
            Color = color,
            Params = Vector4.Zero,
            StrokeColor = strokeColor,
            Effects = new Vector4(strokeWidth, glowRadius, softEdge, 0),
            GradientColor = Vector4.Zero,
            GradientParams = Vector4.Zero,
            WarpParams = Vector4.Zero,
            BooleanParams = Vector2.Zero,
            ClipRect = NoClip,
            TrimParams = Vector4.Zero,
            DashParams = Vector4.Zero
        };
    }

    /// <summary>
    /// Creates a mask push command. Internal - use SdfMaskShape factory methods.
    /// </summary>
    /// <param name="maskShapeType">Mask shape type (0=circle, 1=rect, 2=roundedRect).</param>
    /// <param name="center">Center position of mask.</param>
    /// <param name="size">Size (radius for circle, half-extents for rect).</param>
    /// <param name="softEdge">Soft edge width for alpha falloff.</param>
    /// <param name="invert">If true, mask is inverted (visible outside shape).</param>
    /// <param name="cornerRadius">Corner radius for rounded rect masks.</param>
    /// <param name="unionCount">Number of additional mask shapes to union (0 = single mask).</param>
    internal static SdfCommand MaskPush(uint maskShapeType, Vector2 center, Vector2 size,
        float softEdge, bool invert, float cornerRadius, int unionCount)
    {
        return new SdfCommand
        {
            Type = (uint)SdfShapeType.MaskPush,
            Flags = 0,
            Position = center,
            Size = size,
            Color = Vector4.Zero,
            // X = mask shape type, Y = soft edge, Z = invert, W = cornerRadius/unionCount
            Params = new Vector4(maskShapeType, softEdge, invert ? 1f : 0f, unionCount > 0 ? unionCount : cornerRadius),
            StrokeColor = Vector4.Zero,
            Effects = Vector4.Zero,
            GradientColor = Vector4.Zero,
            GradientParams = Vector4.Zero,
            WarpParams = Vector4.Zero,
            BooleanParams = Vector2.Zero,
            ClipRect = NoClip,
            TrimParams = Vector4.Zero,
            DashParams = Vector4.Zero
        };
    }

    /// <summary>
    /// Creates a mask pop command.
    /// </summary>
    internal static SdfCommand MaskPop()
    {
        return new SdfCommand
        {
            Type = (uint)SdfShapeType.MaskPop,
            Flags = 0,
            Position = Vector2.Zero,
            Size = Vector2.Zero,
            Color = Vector4.Zero,
            Params = Vector4.Zero,
            StrokeColor = Vector4.Zero,
            Effects = Vector4.Zero,
            GradientColor = Vector4.Zero,
            GradientParams = Vector4.Zero,
            WarpParams = Vector4.Zero,
            BooleanParams = Vector2.Zero,
            ClipRect = NoClip,
            TrimParams = Vector4.Zero,
            DashParams = Vector4.Zero
        };
    }

    public SdfCommand WithStrokeTrim(float startPx, float lengthPx, float offsetPx = 0f, SdfStrokeCap cap = SdfStrokeCap.Butt, float capSoftnessPx = 12f)
    {
        var cmd = this;
        if (lengthPx <= 0f)
        {
            cmd.Flags &= ~FlagTrimEnabled;
            cmd.TrimParams = Vector4.Zero;
            cmd.Flags &= ~(CapMask << TrimCapShift);
            return cmd;
        }

        cmd.Flags |= FlagTrimEnabled;
        cmd.TrimParams = new Vector4(startPx, lengthPx, offsetPx, MathF.Max(capSoftnessPx, 0f));
        cmd.Flags = (cmd.Flags & ~(CapMask << TrimCapShift)) | (((uint)cap & CapMask) << TrimCapShift);
        return cmd;
    }

    public SdfCommand WithStrokeTrim(in SdfStrokeTrim trim)
    {
        return WithStrokeTrim(trim.StartPx, trim.LengthPx, trim.OffsetPx, trim.Cap, trim.CapSoftnessPx);
    }

    public SdfCommand WithStrokeDash(float dashLengthPx, float gapLengthPx, float offsetPx = 0f, SdfStrokeCap cap = SdfStrokeCap.Butt, float capSoftnessPx = 12f)
    {
        var cmd = this;
        if (dashLengthPx <= 0f)
        {
            cmd.Flags &= ~FlagDashEnabled;
            cmd.DashParams = Vector4.Zero;
            cmd.Flags &= ~(CapMask << DashCapShift);
            return cmd;
        }

        cmd.Flags |= FlagDashEnabled;
        cmd.DashParams = new Vector4(dashLengthPx, MathF.Max(gapLengthPx, 0f), offsetPx, MathF.Max(capSoftnessPx, 0f));
        cmd.Flags = (cmd.Flags & ~(CapMask << DashCapShift)) | (((uint)cap & CapMask) << DashCapShift);
        return cmd;
    }

    public SdfCommand WithStrokeDash(in SdfStrokeDash dash)
    {
        return WithStrokeDash(dash.DashLengthPx, dash.GapLengthPx, dash.OffsetPx, dash.Cap, dash.CapSoftnessPx);
    }

    public SdfCommand WithBlendMode(uint blendMode)
    {
        var cmd = this;
        cmd.Flags = (cmd.Flags & ~(BlendModeMask << BlendModeShift)) | ((blendMode & BlendModeMask) << BlendModeShift);
        return cmd;
    }

    public bool HasStrokeTrim => (Flags & FlagTrimEnabled) != 0;
    public bool HasStrokeDash => (Flags & FlagDashEnabled) != 0;

    public SdfStrokeCap StrokeTrimCap => (SdfStrokeCap)((Flags >> TrimCapShift) & CapMask);
    public SdfStrokeCap StrokeDashCap => (SdfStrokeCap)((Flags >> DashCapShift) & CapMask);

    public uint BlendMode => (Flags >> BlendModeShift) & BlendModeMask;

    public SdfCommand WithMaskUnionCount(int unionCount)
    {
        var cmd = this;
        if ((SdfShapeType)cmd.Type != SdfShapeType.MaskPush)
        {
            return cmd;
        }

        uint maskType = (uint)cmd.Params.X;
        if (maskType == (uint)SdfMaskShapeType.RoundedRect)
        {
            return cmd;
        }

        cmd.Params = new Vector4(cmd.Params.X, cmd.Params.Y, cmd.Params.Z, MathF.Max(0, unionCount));
        return cmd;
    }

    public SdfCommand WithMaskMeta(bool usePaint, SdfMaskCombineOp combineOp)
    {
        var cmd = this;
        if ((SdfShapeType)cmd.Type != SdfShapeType.MaskPush)
        {
            return cmd;
        }

        cmd.Color = new Vector4(usePaint ? 1f : 0f, 0f, (float)combineOp, 0f);
        return cmd;
    }

    public SdfCommand WithInternalNoRender()
    {
        var cmd = this;
        cmd.Flags |= FlagInternalNoRender;
        return cmd;
    }
}

/// <summary>
/// Mask shape types for alpha masking.
/// </summary>
public enum SdfMaskShapeType : uint
{
    Circle = 0,
    Rect = 1,
    RoundedRect = 2,
    CommandRef = 3,
}

public enum SdfMaskCombineOp : byte
{
    Union = 0,
    Intersect = 1,
    Subtract = 2,
    Exclude = 3,
    Add = 4,
    Multiply = 5,
}

/// <summary>
/// Factory for creating alpha mask shapes.
/// Use with SdfBuffer.PushMask() to apply alpha-based masking to subsequent shapes.
/// </summary>
public static class SdfMaskShape
{
    /// <summary>
    /// Creates a circular mask.
    /// </summary>
    /// <param name="center">Center of the circle.</param>
    /// <param name="radius">Radius of the circle.</param>
    /// <param name="softEdge">Soft edge width for alpha falloff (default 2px).</param>
    /// <param name="invert">If true, area outside circle is visible.</param>
    public static SdfCommand Circle(Vector2 center, float radius, float softEdge = 2f, bool invert = false)
    {
        return SdfCommand.MaskPush((uint)SdfMaskShapeType.Circle, center, new Vector2(radius, radius), softEdge, invert, 0f, 0);
    }

    /// <summary>
    /// Creates a rectangular mask.
    /// </summary>
    /// <param name="center">Center of the rectangle.</param>
    /// <param name="halfSize">Half-extents of the rectangle.</param>
    /// <param name="softEdge">Soft edge width for alpha falloff (default 2px).</param>
    /// <param name="invert">If true, area outside rectangle is visible.</param>
    public static SdfCommand Rect(Vector2 center, Vector2 halfSize, float softEdge = 2f, bool invert = false)
    {
        return SdfCommand.MaskPush((uint)SdfMaskShapeType.Rect, center, halfSize, softEdge, invert, 0f, 0);
    }

    /// <summary>
    /// Creates a rounded rectangle mask.
    /// </summary>
    /// <param name="center">Center of the rectangle.</param>
    /// <param name="halfSize">Half-extents of the rectangle.</param>
    /// <param name="cornerRadius">Corner radius.</param>
    /// <param name="softEdge">Soft edge width for alpha falloff (default 2px).</param>
    /// <param name="invert">If true, area outside rectangle is visible.</param>
    public static SdfCommand RoundedRect(Vector2 center, Vector2 halfSize, float cornerRadius, float softEdge = 2f, bool invert = false)
    {
        return SdfCommand.MaskPush((uint)SdfMaskShapeType.RoundedRect, center, halfSize, softEdge, invert, cornerRadius, 0);
    }

    /// <summary>
    /// Creates a mask that evaluates the SDF of an existing command in the command buffer.
    /// Useful for masking with arbitrary shapes (including rotated shapes and paths).
    /// </summary>
    /// <param name="commandIndex">Index of the referenced SDF command.</param>
    /// <param name="softEdge">Soft edge width for alpha falloff (default 2px).</param>
    /// <param name="invert">If true, area outside mask shape is visible.</param>
    public static SdfCommand CommandRef(uint commandIndex, float softEdge = 2f, bool invert = false)
    {
        // For CommandRef masks, we encode the referenced command index in Position.X.
        return SdfCommand.MaskPush((uint)SdfMaskShapeType.CommandRef, new Vector2(commandIndex, 0f), Vector2.Zero, softEdge, invert, 0f, 0);
    }

    public static SdfCommand CommandRefPaint(
        uint commandIndex,
        float softEdge = 2f,
        bool invert = false,
        SdfMaskCombineOp combineOp = SdfMaskCombineOp.Union)
    {
        return CommandRef(commandIndex, softEdge, invert)
            .WithMaskMeta(usePaint: true, combineOp);
    }

    /// <summary>
    /// Creates a union of multiple mask shapes.
    /// The resulting mask is visible where ANY of the input shapes are visible.
    /// </summary>
    /// <param name="masks">Mask shapes to union (2-8 masks).</param>
    public static SdfCommand[] Union(params SdfCommand[] masks)
    {
        if (masks.Length < 2)
            throw new ArgumentException("Union requires at least 2 masks");
        if (masks.Length > 8)
            throw new ArgumentException("Union supports at most 8 masks");

        // Tag the first mask with the union count
        var result = new SdfCommand[masks.Length];
        result[0] = masks[0];
        result[0].Params = new Vector4(
            result[0].Params.X,  // mask shape type
            result[0].Params.Y,  // soft edge
            result[0].Params.Z,  // invert
            masks.Length - 1);   // union count (additional shapes after this one)

        for (int i = 1; i < masks.Length; i++)
        {
            result[i] = masks[i];
        }

        return result;
    }
}
