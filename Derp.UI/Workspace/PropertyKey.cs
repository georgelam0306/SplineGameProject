using Property;
using Property.Runtime;

namespace Derp.UI;

internal readonly struct PropertyKey
{
    public readonly AnyComponentHandle Component;
    public readonly ushort PropertyIndexHint;
    public readonly ulong PropertyId;
    public readonly PropertyKind Kind;

    public PropertyKey(AnyComponentHandle component, ushort propertyIndexHint, ulong propertyId, PropertyKind kind)
    {
        Component = component;
        PropertyIndexHint = propertyIndexHint;
        PropertyId = propertyId;
        Kind = kind;
    }

    public static PropertyKey FromSlot(PropertySlot slot)
    {
        return new PropertyKey(
            slot.Component,
            slot.PropertyIndex,
            slot.PropertyId,
            slot.Kind);
    }
}

