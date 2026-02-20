using System.Numerics;
using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 224)]
public partial struct ComputedSizeComponent
{
    [Column]
    [Property(Name = "Size", Group = "Computed", Order = 1, Flags = PropertyFlags.ReadOnly, Min = 0f, Step = 1f)]
    public Vector2 Size;
}
