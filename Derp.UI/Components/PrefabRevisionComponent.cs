using Pooled;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 219)]
public partial struct PrefabRevisionComponent
{
    [Column]
    public uint Revision;
}
