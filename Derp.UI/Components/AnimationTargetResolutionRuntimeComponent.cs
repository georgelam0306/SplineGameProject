using System.Runtime.CompilerServices;
using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 238)]
public partial struct AnimationTargetResolutionRuntimeComponent
{
    public const int MaxTargets = AnimationLibraryComponent.MaxTargets;

    [InlineArray(MaxTargets)]
    public struct UIntBufferTargets
    {
        private uint _element0;
    }

    [Column]
    public uint SourcePrefabStableId;

    [Column]
    public uint AnimationLibraryRevision;

    [Column]
    public ushort TargetCount;

    [Column]
    [Array(MaxTargets)]
    public UIntBufferTargets ResolvedTargetEntityStableId;
}
