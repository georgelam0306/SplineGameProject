using System.Numerics;
using System.Runtime.CompilerServices;
using Core;
using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 213)]
public partial struct PaintComponent
{
    public const int MaxLayers = 32;

    [InlineArray(MaxLayers)]
    public struct BoolBuffer
    {
        private bool _element0;
    }

    [InlineArray(MaxLayers)]
    public struct IntBuffer
    {
        private int _element0;
    }

    [InlineArray(MaxLayers)]
    public struct FloatBuffer
    {
        private float _element0;
    }

    [InlineArray(MaxLayers)]
    public struct Vec2Buffer
    {
        private Vector2 _element0;
    }

    [InlineArray(MaxLayers)]
    public struct Vec4Buffer
    {
        private Vector4 _element0;
    }

    [InlineArray(MaxLayers)]
    public struct Color32Buffer
    {
        private Color32 _element0;
    }

    [Column]
    public byte LayerCount;

    [Column]
    [Property(Name = "Kind", Group = "Paint", Order = 0, Flags = PropertyFlags.Hidden, Min = 0f, Max = 1f, Step = 1f)]
    [Array(MaxLayers)]
    public IntBuffer LayerKind;

    [Column]
    [Property(Name = "Visible", Group = "Paint", Order = 1, Flags = PropertyFlags.Hidden)]
    [Array(MaxLayers)]
    public BoolBuffer LayerIsVisible;

    [Column]
    [Property(Name = "Inherit Blend", Group = "Paint", Order = 2, Flags = PropertyFlags.Hidden)]
    [Array(MaxLayers)]
    public BoolBuffer LayerInheritBlendMode;

    [Column]
    [Property(Name = "Blend Mode", Group = "Paint", Order = 3, Flags = PropertyFlags.Hidden, Min = 0, Max = 15, Step = 1)]
    [Array(MaxLayers)]
    public IntBuffer LayerBlendMode;

    [Column]
    [Property(Name = "Opacity", Group = "Paint", Order = 4, Flags = PropertyFlags.Hidden, Min = 0f, Max = 1f, Step = 0.01f)]
    [Array(MaxLayers)]
    public FloatBuffer LayerOpacity;

    [Column]
    [Property(Name = "Offset", Group = "Paint", Order = 5, Flags = PropertyFlags.Hidden)]
    [Array(MaxLayers)]
    public Vec2Buffer LayerOffset;

    [Column]
    [Property(Name = "Blur", Group = "Paint", Order = 6, Flags = PropertyFlags.Hidden, Min = 0f, Step = 0.5f)]
    [Array(MaxLayers)]
    public FloatBuffer LayerBlur;

    [Column]
    [Property(Name = "Blur Direction", Group = "Paint", Order = 7, Flags = PropertyFlags.Hidden, Min = 0f, Max = 2f, Step = 1f)]
    [Array(MaxLayers)]
    public IntBuffer LayerBlurDirection;

    [Column]
    [Property(Name = "Mask Combine Op", Group = "Mask", Order = 1, Flags = PropertyFlags.Hidden, Min = 0, Max = 5, Step = 1)]
    [Array(MaxLayers)]
    public IntBuffer LayerMaskCombineOp;

    [Column]
    [Property(Name = "Fill Color", Group = "Fill", Order = 0, Flags = PropertyFlags.Hidden)]
    [Array(MaxLayers)]
    public Color32Buffer FillColor;

    [Column]
    [Property(Name = "Use Gradient", Group = "Fill", Order = 1, Flags = PropertyFlags.Hidden)]
    [Array(MaxLayers)]
    public BoolBuffer FillUseGradient;

    [Column]
    [Property(Name = "Color A", Group = "Fill Gradient", Order = 0, Flags = PropertyFlags.Hidden)]
    [Array(MaxLayers)]
    public Color32Buffer FillGradientColorA;

    [Column]
    [Property(Name = "Color B", Group = "Fill Gradient", Order = 1, Flags = PropertyFlags.Hidden)]
    [Array(MaxLayers)]
    public Color32Buffer FillGradientColorB;

    [Column]
    [Property(Name = "Type", Group = "Fill Gradient", Order = 2, Flags = PropertyFlags.Hidden, Min = 0, Max = 2, Step = 1)]
    [Array(MaxLayers)]
    public IntBuffer FillGradientType;

    [Column]
    [Property(Name = "Mix", Group = "Fill Gradient", Order = 2, Flags = PropertyFlags.Hidden, Min = 0f, Max = 1f, Step = 0.01f)]
    [Array(MaxLayers)]
    public FloatBuffer FillGradientMix;

    [Column]
    [Property(Name = "Direction", Group = "Fill Gradient", Order = 3, Flags = PropertyFlags.Hidden)]
    [Array(MaxLayers)]
    [PropertyGizmo(typeof(PaintFillGradientGizmo), PropertyGizmoTriggers.Hover | PropertyGizmoTriggers.Active)]
    public Vec2Buffer FillGradientDirection;

    [Column]
    [Property(Name = "Center", Group = "Fill Gradient", Order = 4, Flags = PropertyFlags.Hidden)]
    [Array(MaxLayers)]
    public Vec2Buffer FillGradientCenter;

    [Column]
    [Property(Name = "Radius", Group = "Fill Gradient", Order = 5, Flags = PropertyFlags.Hidden, Min = 0.05f, Max = 8f, Step = 0.01f)]
    [Array(MaxLayers)]
    public FloatBuffer FillGradientRadius;

    [Column]
    [Property(Name = "Angle", Group = "Fill Gradient", Order = 6, Flags = PropertyFlags.Hidden)]
    [Array(MaxLayers)]
    public FloatBuffer FillGradientAngle;

    [Column]
    [Property(Name = "Stops Count", Group = "Fill Stops", Order = 0, Flags = PropertyFlags.Hidden, Min = 0f, Max = 8f, Step = 1f)]
    [Array(MaxLayers)]
    public IntBuffer FillGradientStopCount;

    [Column]
    [Property(Name = "T 0-3", Group = "Fill Stops", Order = 1, Flags = PropertyFlags.Hidden)]
    [Array(MaxLayers)]
    public Vec4Buffer FillGradientStopT0To3;

    [Column]
    [Property(Name = "T 4-7", Group = "Fill Stops", Order = 2, Flags = PropertyFlags.Hidden)]
    [Array(MaxLayers)]
    public Vec4Buffer FillGradientStopT4To7;

    [Column]
    [Property(Name = "Color 0", Group = "Fill Stops", Order = 3, Flags = PropertyFlags.Hidden)]
    [Array(MaxLayers)]
    public Color32Buffer FillGradientStopColor0;

    [Column]
    [Property(Name = "Color 1", Group = "Fill Stops", Order = 4, Flags = PropertyFlags.Hidden)]
    [Array(MaxLayers)]
    public Color32Buffer FillGradientStopColor1;

    [Column]
    [Property(Name = "Color 2", Group = "Fill Stops", Order = 5, Flags = PropertyFlags.Hidden)]
    [Array(MaxLayers)]
    public Color32Buffer FillGradientStopColor2;

    [Column]
    [Property(Name = "Color 3", Group = "Fill Stops", Order = 6, Flags = PropertyFlags.Hidden)]
    [Array(MaxLayers)]
    public Color32Buffer FillGradientStopColor3;

    [Column]
    [Property(Name = "Color 4", Group = "Fill Stops", Order = 7, Flags = PropertyFlags.Hidden)]
    [Array(MaxLayers)]
    public Color32Buffer FillGradientStopColor4;

    [Column]
    [Property(Name = "Color 5", Group = "Fill Stops", Order = 8, Flags = PropertyFlags.Hidden)]
    [Array(MaxLayers)]
    public Color32Buffer FillGradientStopColor5;

    [Column]
    [Property(Name = "Color 6", Group = "Fill Stops", Order = 9, Flags = PropertyFlags.Hidden)]
    [Array(MaxLayers)]
    public Color32Buffer FillGradientStopColor6;

    [Column]
    [Property(Name = "Color 7", Group = "Fill Stops", Order = 10, Flags = PropertyFlags.Hidden)]
    [Array(MaxLayers)]
    public Color32Buffer FillGradientStopColor7;

    [Column]
    [Property(Name = "Stroke Width", Group = "Stroke", Order = 0, Flags = PropertyFlags.Hidden, Min = 0f, Step = 0.5f)]
    [Array(MaxLayers)]
    public FloatBuffer StrokeWidth;

    [Column]
    [Property(Name = "Stroke Color", Group = "Stroke", Order = 1, Flags = PropertyFlags.Hidden)]
    [Array(MaxLayers)]
    public Color32Buffer StrokeColor;

    [Column]
    [Property(Name = "Dash Enabled", Group = "Stroke Dash", Order = 0, Flags = PropertyFlags.Hidden)]
    [Array(MaxLayers)]
    public BoolBuffer StrokeDashEnabled;

    [Column]
    [Property(Name = "Dash Length", Group = "Stroke Dash", Order = 1, Flags = PropertyFlags.Hidden, Min = 0f, Step = 1f)]
    [Array(MaxLayers)]
    public FloatBuffer StrokeDashLength;

    [Column]
    [Property(Name = "Dash Gap Length", Group = "Stroke Dash", Order = 2, Flags = PropertyFlags.Hidden, Min = 0f, Step = 1f)]
    [Array(MaxLayers)]
    public FloatBuffer StrokeDashGapLength;

    [Column]
    [Property(Name = "Dash Offset", Group = "Stroke Dash", Order = 3, Flags = PropertyFlags.Hidden, Step = 1f)]
    [Array(MaxLayers)]
    public FloatBuffer StrokeDashOffset;

    [Column]
    [Property(Name = "Dash Cap", Group = "Stroke Dash", Order = 4, Flags = PropertyFlags.Hidden, Kind = PropertyKind.Int, Min = 0f, Max = 3f, Step = 1f)]
    [Array(MaxLayers)]
    public IntBuffer StrokeDashCap;

    [Column]
    [Property(Name = "Dash Cap Softness", Group = "Stroke Dash", Order = 5, Flags = PropertyFlags.Hidden, Min = 0f, Step = 1f)]
    [Array(MaxLayers)]
    public FloatBuffer StrokeDashCapSoftness;

    [Column]
    [Property(Name = "Trim Enabled", Group = "Stroke Trim", Order = 0, Flags = PropertyFlags.Hidden)]
    [Array(MaxLayers)]
    public BoolBuffer StrokeTrimEnabled;

    [Column]
    [Property(Name = "Trim Start", Group = "Stroke Trim", Order = 1, Flags = PropertyFlags.Hidden, Min = 0f, Step = 1f)]
    [Array(MaxLayers)]
    public FloatBuffer StrokeTrimStart;

    [Column]
    [Property(Name = "Trim Length", Group = "Stroke Trim", Order = 2, Flags = PropertyFlags.Hidden, Min = 0f, Step = 1f)]
    [Array(MaxLayers)]
    public FloatBuffer StrokeTrimLength;

    [Column]
    [Property(Name = "Trim Offset", Group = "Stroke Trim", Order = 3, Flags = PropertyFlags.Hidden, Step = 1f)]
    [Array(MaxLayers)]
    public FloatBuffer StrokeTrimOffset;

    [Column]
    [Property(Name = "Trim Cap", Group = "Stroke Trim", Order = 4, Flags = PropertyFlags.Hidden, Kind = PropertyKind.Int, Min = 0f, Max = 3f, Step = 1f)]
    [Array(MaxLayers)]
    public IntBuffer StrokeTrimCap;

    [Column]
    [Property(Name = "Trim Cap Softness", Group = "Stroke Trim", Order = 5, Flags = PropertyFlags.Hidden, Min = 0f, Step = 1f)]
    [Array(MaxLayers)]
    public FloatBuffer StrokeTrimCapSoftness;
}
