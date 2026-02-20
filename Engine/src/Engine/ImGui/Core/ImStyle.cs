using System.Numerics;

namespace DerpLib.ImGui.Core;

/// <summary>
/// Theme configuration for ImGUI widgets.
/// Colors are packed ARGB (0xAARRGGBB) for zero-allocation comparisons.
/// </summary>
public struct ImStyle
{
    //=== Colors ===

    /// <summary>Primary accent color (buttons, highlights).</summary>
    public uint Primary;

    /// <summary>Secondary accent color.</summary>
    public uint Secondary;

    /// <summary>Background color for panels/windows.</summary>
    public uint Background;

    /// <summary>Surface color for widget backgrounds.</summary>
    public uint Surface;

    /// <summary>Primary text color.</summary>
    public uint TextPrimary;

    /// <summary>Secondary/dimmed text color.</summary>
    public uint TextSecondary;

    /// <summary>Disabled text color.</summary>
    public uint TextDisabled;

    /// <summary>Hover highlight color.</summary>
    public uint Hover;

    /// <summary>Active/pressed color.</summary>
    public uint Active;

    /// <summary>Border color.</summary>
    public uint Border;

    /// <summary>Title bar background color.</summary>
    public uint TitleBar;

    /// <summary>Title bar background when inactive.</summary>
    public uint TitleBarInactive;

    /// <summary>Scrollbar track color.</summary>
    public uint ScrollbarTrack;

    /// <summary>Scrollbar thumb color.</summary>
    public uint ScrollbarThumb;

    /// <summary>Inactive tab color.</summary>
    public uint TabInactive;

    /// <summary>Active tab color.</summary>
    public uint TabActive;

    /// <summary>Dock preview overlay color.</summary>
    public uint DockPreview;

    /// <summary>Shadow color (typically semi-transparent black).</summary>
    public uint ShadowColor;

    /// <summary>Slider track fill color.</summary>
    public uint SliderFill;

    /// <summary>Checkbox/radio check mark color.</summary>
    public uint CheckMark;

    //=== Sizing ===

    /// <summary>Default font size in pixels.</summary>
    public float FontSize;

    /// <summary>Corner radius for rounded rectangles.</summary>
    public float CornerRadius;

    /// <summary>Border width in pixels.</summary>
    public float BorderWidth;

    /// <summary>Inner padding for widgets.</summary>
    public float Padding;

    /// <summary>Spacing between widgets in layout.</summary>
    public float Spacing;

    /// <summary>Title bar height.</summary>
    public float TitleBarHeight;

    /// <summary>Tab bar height.</summary>
    public float TabHeight;

    /// <summary>Scrollbar width.</summary>
    public float ScrollbarWidth;

    /// <summary>Checkbox/radio button size.</summary>
    public float CheckboxSize;

    /// <summary>Slider track height.</summary>
    public float SliderHeight;

    /// <summary>Slider thumb width.</summary>
    public float SliderThumbWidth;

    /// <summary>Minimum button width.</summary>
    public float MinButtonWidth;

    /// <summary>Minimum button height.</summary>
    public float MinButtonHeight;

    /// <summary>Splitter handle size for dock splits.</summary>
    public float SplitterSize;

    //=== Docking ===

    /// <summary>
    /// Fraction of a dock node's size used for edge zones (Left/Right/Top/Bottom).
    /// </summary>
    public float DockZoneEdgeFraction;

    /// <summary>
    /// Pixel gap between dock zones. Areas that fall in the gaps return DockZone.None.
    /// </summary>
    public float DockZoneGap;

    //=== SDF Effects ===

    /// <summary>Glow radius when widget is hovered.</summary>
    public float HoverGlow;

    /// <summary>Glow radius when widget is active.</summary>
    public float ActiveGlow;

    /// <summary>Anti-aliasing soft edge width.</summary>
    public float SoftEdge;

    /// <summary>Shadow blur radius.</summary>
    public float ShadowRadius;

    /// <summary>Shadow X offset.</summary>
    public float ShadowOffsetX;

    /// <summary>Shadow Y offset.</summary>
    public float ShadowOffsetY;

    //=== Timing ===

    /// <summary>Caret blink rate in seconds.</summary>
    public float CaretBlinkRate;

    /// <summary>Double-click time threshold in seconds.</summary>
    public float DoubleClickTime;

    //=== Presets ===

    /// <summary>
    /// Default dark theme.
    /// </summary>
    public static ImStyle Default => new()
    {
        // Colors - Neutral dark token ladder (higher contrast, original hue direction)
        Primary = 0xFF3391FF,         // Accent blue
        Secondary = 0xFF2D7ACC,       // Secondary accent
        Background = 0xFF1F1F1F,      // App background (darkest)
        Surface = 0xFF2A2A2A,         // Panel surface
        TextPrimary = 0xFFE8E8E8,     // Primary text
        TextSecondary = 0xFFB8B8B8,   // Secondary text
        TextDisabled = 0xFF7A7A7A,    // Disabled text
        Hover = 0xFF383838,           // Hover surface
        Active = 0xFF0078D4,          // Active accent
        Border = 0xFF4A4A4A,          // Borders/dividers
        TitleBar = 0xFF282828,        // Focused title bar
        TitleBarInactive = 0xFF232323,// Unfocused title bar
        ScrollbarTrack = 0xFF1A1A1A,  // Scrollbar track
        ScrollbarThumb = 0xFF555555,  // Scrollbar thumb
        TabInactive = 0xFF2D2D2D,     // Inactive tab
        TabActive = 0xFF1F1F1F,       // Active tab (dark, matches app background)
        DockPreview = 0x403391FF,     // Semi-transparent blue
        ShadowColor = 0x80000000,     // Semi-transparent black
        SliderFill = 0xFF3391FF,      // Slider fill
        CheckMark = 0xFFFFFFFF,       // White check mark

        // Sizing
        FontSize = 14f,
        CornerRadius = 4f,
        BorderWidth = 1f,
        Padding = 8f,
        Spacing = 4f,
        TitleBarHeight = 28f,
        TabHeight = 24f,
        ScrollbarWidth = 12f,
        CheckboxSize = 18f,
        SliderHeight = 6f,
        SliderThumbWidth = 14f,
        MinButtonWidth = 60f,
        MinButtonHeight = 28f,
        SplitterSize = 4f,

        // Docking
        DockZoneEdgeFraction = 0.25f,
        DockZoneGap = 16f,

        // SDF Effects
        HoverGlow = 4f,
        ActiveGlow = 6f,
        SoftEdge = 1f,
        ShadowRadius = 8f,
        ShadowOffsetX = 2f,
        ShadowOffsetY = 2f,

        // Timing
        CaretBlinkRate = 0.53f,
        DoubleClickTime = 0.3f,
    };

    /// <summary>
    /// Light theme variant.
    /// </summary>
    public static ImStyle Light => new()
    {
        // Colors - Light theme
        Primary = 0xFF0078D4,         // Blue
        Secondary = 0xFF005A9E,       // Darker blue
        Background = 0xFFF3F3F3,      // Light gray
        Surface = 0xFFFFFFFF,         // White
        TextPrimary = 0xFF1E1E1E,     // Near black
        TextSecondary = 0xFF666666,   // Gray
        TextDisabled = 0xFFAAAAAA,    // Light gray
        Hover = 0xFFE5E5E5,           // Hover highlight
        Active = 0xFF0078D4,          // Active blue
        Border = 0xFFD1D1D1,          // Light border
        TitleBar = 0xFFE5E5E5,        // Title bar
        TitleBarInactive = 0xFFEBEBEB,// Inactive title bar
        ScrollbarTrack = 0xFFF3F3F3,  // Scrollbar track
        ScrollbarThumb = 0xFFBBBBBB,  // Scrollbar thumb
        TabInactive = 0xFFE5E5E5,     // Inactive tab
        TabActive = 0xFFF3F3F3,       // Active tab
        DockPreview = 0x400078D4,     // Semi-transparent blue
        ShadowColor = 0x40000000,     // Lighter shadow
        SliderFill = 0xFF0078D4,      // Slider fill
        CheckMark = 0xFF0078D4,       // Blue check mark

        // Sizing (same as dark)
        FontSize = 14f,
        CornerRadius = 4f,
        BorderWidth = 1f,
        Padding = 8f,
        Spacing = 4f,
        TitleBarHeight = 28f,
        TabHeight = 24f,
        ScrollbarWidth = 12f,
        CheckboxSize = 18f,
        SliderHeight = 6f,
        SliderThumbWidth = 14f,
        MinButtonWidth = 60f,
        MinButtonHeight = 28f,
        SplitterSize = 4f,

        // Docking
        DockZoneEdgeFraction = 0.25f,
        DockZoneGap = 16f,

        // SDF Effects
        HoverGlow = 4f,
        ActiveGlow = 6f,
        SoftEdge = 1f,
        ShadowRadius = 8f,
        ShadowOffsetX = 2f,
        ShadowOffsetY = 2f,

        // Timing
        CaretBlinkRate = 0.53f,
        DoubleClickTime = 0.3f,
    };

    //=== Color Helpers ===

    /// <summary>
    /// Create color from RGBA components (0-255).
    /// </summary>
    public static uint Rgba(byte r, byte g, byte b, byte a = 255)
        => (uint)((a << 24) | (r << 16) | (g << 8) | b);

    /// <summary>
    /// Create color from RGBA components (0-1).
    /// </summary>
    public static uint RgbaF(float r, float g, float b, float a = 1f)
        => Rgba(
            (byte)(Math.Clamp(r, 0f, 1f) * 255),
            (byte)(Math.Clamp(g, 0f, 1f) * 255),
            (byte)(Math.Clamp(b, 0f, 1f) * 255),
            (byte)(Math.Clamp(a, 0f, 1f) * 255));

    /// <summary>
    /// Extract alpha component (0-255).
    /// </summary>
    public static byte GetAlpha(uint color) => (byte)(color >> 24);

    /// <summary>
    /// Extract red component (0-255).
    /// </summary>
    public static byte GetRed(uint color) => (byte)(color >> 16);

    /// <summary>
    /// Extract green component (0-255).
    /// </summary>
    public static byte GetGreen(uint color) => (byte)(color >> 8);

    /// <summary>
    /// Extract blue component (0-255).
    /// </summary>
    public static byte GetBlue(uint color) => (byte)color;

    /// <summary>
    /// Convert packed color to Vector4 (RGBA, 0-1 range).
    /// </summary>
    public static Vector4 ToVector4(uint color)
    {
        return new Vector4(
            GetRed(color) / 255f,
            GetGreen(color) / 255f,
            GetBlue(color) / 255f,
            GetAlpha(color) / 255f);
    }

    /// <summary>
    /// Modify alpha of a color.
    /// </summary>
    public static uint WithAlpha(uint color, byte alpha)
        => (color & 0x00FFFFFF) | ((uint)alpha << 24);

    /// <summary>
    /// Modify alpha of a color (0-1 range).
    /// </summary>
    public static uint WithAlphaF(uint color, float alpha)
        => WithAlpha(color, (byte)(Math.Clamp(alpha, 0f, 1f) * 255));

    /// <summary>
    /// Lerp between two colors.
    /// </summary>
    public static uint Lerp(uint a, uint b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        float invT = 1f - t;

        return Rgba(
            (byte)(GetRed(a) * invT + GetRed(b) * t),
            (byte)(GetGreen(a) * invT + GetGreen(b) * t),
            (byte)(GetBlue(a) * invT + GetBlue(b) * t),
            (byte)(GetAlpha(a) * invT + GetAlpha(b) * t));
    }
}
