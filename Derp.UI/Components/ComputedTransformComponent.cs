using System.Numerics;
using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 223)]
public partial struct ComputedTransformComponent
{
    [Column]
    [Property(Name = "Position", Group = "Computed", Order = 0, Flags = PropertyFlags.ReadOnly)]
    public Vector2 Position;
}
