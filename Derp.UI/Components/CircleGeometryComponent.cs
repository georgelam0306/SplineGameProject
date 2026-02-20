using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 203)]
public partial struct CircleGeometryComponent
{
    [Column]
    [Property(Name = "Radius", Group = "Ellipse Path", Order = 0, Min = 0f, Step = 0.5f)]
    public float Radius;
}
