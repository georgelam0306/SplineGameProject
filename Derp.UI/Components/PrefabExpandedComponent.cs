using Pooled;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 216)]
public partial struct PrefabExpandedComponent
{
    [Column]
    public uint InstanceRootStableId;

    [Column]
    public uint SourceNodeStableId;
}
