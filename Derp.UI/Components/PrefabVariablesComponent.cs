using System.Runtime.CompilerServices;
using Core;
using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 217)]
public partial struct PrefabVariablesComponent
{
    public const int MaxVariables = 64;

    [InlineArray(MaxVariables)]
    public struct UShortBuffer
    {
        private ushort _element0;
    }

    [InlineArray(MaxVariables)]
    public struct StringHandleBuffer
    {
        private StringHandle _element0;
    }

    [InlineArray(MaxVariables)]
    public struct IntBuffer
    {
        private int _element0;
    }

    [InlineArray(MaxVariables)]
    public struct ValueBuffer
    {
        private PropertyValue _element0;
    }

    [Column]
    public ushort NextVariableId;

    [Column]
    public ushort VariableCount;

    [Column]
    [Array(MaxVariables)]
    public UShortBuffer VariableId;

    [Column]
    [Array(MaxVariables)]
    public StringHandleBuffer Name;

    [Column]
    [Array(MaxVariables)]
    public IntBuffer Kind;

    [Column]
    [Array(MaxVariables)]
    public ValueBuffer DefaultValue;
}
