using Core;
using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 205)]
public partial struct StrokeComponent
{
    [Column]
    [Property(Name = "Width", Group = "Stroke", Order = 0, Min = 0f, Step = 0.5f)]
    public float Width;

    [Column]
    [Property(Name = "Color", Group = "Stroke", Order = 1)]
    public Color32 Color;

    [Column]
    [Property(Name = "Enabled", Group = "Dash", Order = 0)]
    public bool DashEnabled;

    [Column]
    [Property(Name = "Dash Length", Group = "Dash", Order = 1, Min = 0f, Step = 1f)]
    public float DashLength;

    [Column]
    [Property(Name = "Gap Length", Group = "Dash", Order = 2, Min = 0f, Step = 1f)]
    public float DashGapLength;

    [Column]
    [Property(Name = "Offset", Group = "Dash", Order = 3, Step = 1f)]
    public float DashOffset;

    [Column]
    [Property(Name = "Cap", Group = "Dash", Order = 4, Kind = PropertyKind.Int, Min = 0f, Max = 3f, Step = 1f)]
    public int DashCap;

    [Column]
    [Property(Name = "Cap Softness", Group = "Dash", Order = 5, Min = 0f, Step = 1f)]
    public float DashCapSoftness;

    [Column]
    [Property(Name = "Enabled", Group = "Trim", Order = 0)]
    public bool TrimEnabled;

    [Column]
    [Property(Name = "Start", Group = "Trim", Order = 1, Min = 0f, Step = 1f)]
    public float TrimStart;

    [Column]
    [Property(Name = "Length", Group = "Trim", Order = 2, Min = 0f, Step = 1f)]
    public float TrimLength;

    [Column]
    [Property(Name = "Offset", Group = "Trim", Order = 3, Step = 1f)]
    public float TrimOffset;

    [Column]
    [Property(Name = "Cap", Group = "Trim", Order = 4, Kind = PropertyKind.Int, Min = 0f, Max = 3f, Step = 1f)]
    public int TrimCap;

    [Column]
    [Property(Name = "Cap Softness", Group = "Trim", Order = 5, Min = 0f, Step = 1f)]
    public float TrimCapSoftness;
}
