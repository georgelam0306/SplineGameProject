using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 208)]
public partial struct BooleanGroupComponent
{
    [Column]
    [Property(Name = "Operation", Group = "Boolean", Order = -1, Min = 0f, Max = 3f, Step = 1f)]
    public int Operation;

    [Column]
    [Property(Name = "Smoothness", Group = "Boolean", Order = 0, Min = 0f, Max = 50f, Step = 0.1f)]
    public float Smoothness;
}
