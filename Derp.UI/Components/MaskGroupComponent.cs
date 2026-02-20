using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 214)]
public partial struct MaskGroupComponent
{
    [Column]
    [Property(Name = "Soft Edge", Group = "Mask", Order = -1, Min = 0f, Max = 64f, Step = 0.5f)]
    public float SoftEdgePx;

    [Column]
    [Property(Name = "Invert", Group = "Mask", Order = 0)]
    public bool Invert;
}
