using System.Runtime.CompilerServices;
using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 215)]
public partial struct PrefabInstanceComponent
{
    public const int MaxVariables = 64;

    [InlineArray(MaxVariables)]
    public struct UShortBuffer
    {
        private ushort _element0;
    }

    [InlineArray(MaxVariables)]
    public struct ValueBuffer
    {
        private PropertyValue _element0;
    }

    [Column]
    public uint SourcePrefabStableId;

    [Column]
    public uint SourcePrefabRevisionAtBuild;

    [Column]
    public uint SourcePrefabBindingsRevisionAtBuild;

    [Column]
    public ushort ValueCount;

    // Bit i indicates VariableId[i] is overridden locally on the instance.
    // When a bit is 0, the effective value comes from the source prefab's default value.
    [Column]
    public ulong OverrideMask;

    // 0 = size comes from source prefab by default (and may be overwritten by parent layout)
    // 1 = user-resized override; do not sync size from source prefab
    [Column]
    public byte CanvasSizeIsOverridden;

    [Column]
    [Array(MaxVariables)]
    public UShortBuffer VariableId;

    [Column]
    [Array(MaxVariables)]
    public ValueBuffer Value;
}
