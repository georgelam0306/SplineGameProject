using System.Numerics;
using Core;
using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 204)]
public partial struct FillComponent
{
    [Column]
    [Property(Name = "Color", Group = "Fill", Order = 0)]
    public Color32 Color;

    [Column]
    [Property(Name = "Use Gradient", Group = "Fill", Order = 1)]
    public bool UseGradient;

    [Column]
    [Property(Name = "Color A", Group = "Gradient", Order = 0)]
    public Color32 GradientColorA;

    [Column]
    [Property(Name = "Color B", Group = "Gradient", Order = 1)]
    public Color32 GradientColorB;

    [Column]
    [Property(Name = "Mix", Group = "Gradient", Order = 2, Min = 0f, Max = 1f, Step = 0.01f)]
    public float GradientMix;

    [Column]
    [Property(Name = "Direction", Group = "Gradient", Order = 3)]
    public Vector2 GradientDirection;

    // Multi-stop gradient data (up to 8 stops). StopCount==0 means "use legacy GradientColorA/B at 0/1".
    [Column]
    public byte GradientStopCount;

    [Column]
    [Property(Name = "T 0-3", Group = "Stops", Order = 0, Flags = PropertyFlags.Hidden)]
    public Vector4 GradientStopT0To3;

    [Column]
    [Property(Name = "T 4-7", Group = "Stops", Order = 1, Flags = PropertyFlags.Hidden)]
    public Vector4 GradientStopT4To7;

    [Column]
    [Property(Name = "Color 0", Group = "Stops", Order = 2, Flags = PropertyFlags.Hidden)]
    public Color32 GradientStopColor0;

    [Column]
    [Property(Name = "Color 1", Group = "Stops", Order = 3, Flags = PropertyFlags.Hidden)]
    public Color32 GradientStopColor1;

    [Column]
    [Property(Name = "Color 2", Group = "Stops", Order = 4, Flags = PropertyFlags.Hidden)]
    public Color32 GradientStopColor2;

    [Column]
    [Property(Name = "Color 3", Group = "Stops", Order = 5, Flags = PropertyFlags.Hidden)]
    public Color32 GradientStopColor3;

    [Column]
    [Property(Name = "Color 4", Group = "Stops", Order = 6, Flags = PropertyFlags.Hidden)]
    public Color32 GradientStopColor4;

    [Column]
    [Property(Name = "Color 5", Group = "Stops", Order = 7, Flags = PropertyFlags.Hidden)]
    public Color32 GradientStopColor5;

    [Column]
    [Property(Name = "Color 6", Group = "Stops", Order = 8, Flags = PropertyFlags.Hidden)]
    public Color32 GradientStopColor6;

    [Column]
    [Property(Name = "Color 7", Group = "Stops", Order = 9, Flags = PropertyFlags.Hidden)]
    public Color32 GradientStopColor7;
}
