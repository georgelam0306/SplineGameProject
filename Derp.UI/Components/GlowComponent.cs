using Core;
using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 206)]
public partial struct GlowComponent
{
    [Column]
    [Property(Name = "Radius", Group = "Glow", Order = 0, Min = 0f, Step = 0.5f)]
    public float Radius;

    [Column]
    [Property(Name = "Color", Group = "Glow", Order = 1)]
    public Color32 Color;
}
