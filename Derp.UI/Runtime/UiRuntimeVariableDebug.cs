using Property;
using Property.Runtime;

namespace Derp.UI;

public readonly struct UiRuntimeVariableDebug
{
    public readonly PropertyKind Kind;
    public readonly PropertyValue Value;

    public UiRuntimeVariableDebug(PropertyKind kind, in PropertyValue value)
    {
        Kind = kind;
        Value = value;
    }
}
