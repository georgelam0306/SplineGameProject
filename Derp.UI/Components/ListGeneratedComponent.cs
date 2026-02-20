using Pooled;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 222)]
public partial struct ListGeneratedComponent
{
    [Column]
    public uint SourceLayoutStableId;

    [Column]
    public ushort ListVariableId;

    [Column]
    public ushort ItemIndex;
}

