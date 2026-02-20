// SPDX-License-Identifier: MIT
#nullable enable
using Property;

namespace Property.Runtime
{
    public readonly struct PropertySlot
    {
        public readonly AnyComponentHandle Component;
        public readonly ushort PropertyIndex;
        public readonly ulong PropertyId;
        public readonly PropertyKind Kind;

        public PropertySlot(
            AnyComponentHandle component,
            ushort propertyIndex,
            ulong propertyId,
            PropertyKind kind)
        {
            Component = component;
            PropertyIndex = propertyIndex;
            PropertyId = propertyId;
            Kind = kind;
        }
    }
}
