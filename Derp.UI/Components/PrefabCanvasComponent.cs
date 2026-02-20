using System.Numerics;
using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 212)]
public partial struct PrefabCanvasComponent
{
    [Column]
    [Property(Name = "Size", Group = "Prefab", Order = 0, Min = 1f, Step = 1f)]
    public Vector2 Size;
}

