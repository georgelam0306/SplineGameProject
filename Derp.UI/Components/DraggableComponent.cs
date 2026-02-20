using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 241)]
public partial struct DraggableComponent
{
    [Column]
    [Property(Name = "Enabled", Group = "Runtime Drag", Order = 0)]
    public bool Enabled;
}

