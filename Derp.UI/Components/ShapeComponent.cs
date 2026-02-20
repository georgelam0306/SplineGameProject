using Pooled;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 201)]
public partial struct ShapeComponent
{
    [Column]
    public ShapeKind Kind;
}
