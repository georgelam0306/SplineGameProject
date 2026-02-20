using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 237)]
public partial struct ClipRectComponent
{
    [Column]
    [Property(Name = "Clip", Group = "Clip", Order = 0)]
    public bool Enabled;
}

