using System.Runtime.CompilerServices;
using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 242)]
public partial struct ModifierStackComponent
{
    public const int MaxWarps = 16;

    [InlineArray(MaxWarps)]
    public struct ByteBuffer
    {
        private byte _element0;
    }

    [InlineArray(MaxWarps)]
    public struct FloatBuffer
    {
        private float _element0;
    }

    [Column]
    public ushort Count;

    [Column]
    public ByteBuffer EnabledValue;

    [Column]
    public ByteBuffer TypeValue;

    [Column]
    [Property(Name = "Param1", Group = "Warp", Order = 0, Flags = PropertyFlags.Hidden)]
    [Array(MaxWarps)]
    public FloatBuffer Param1Value;

    [Column]
    [Property(Name = "Param2", Group = "Warp", Order = 1, Flags = PropertyFlags.Hidden)]
    [Array(MaxWarps)]
    public FloatBuffer Param2Value;

    [Column]
    [Property(Name = "Param3", Group = "Warp", Order = 2, Flags = PropertyFlags.Hidden)]
    [Array(MaxWarps)]
    public FloatBuffer Param3Value;
}
