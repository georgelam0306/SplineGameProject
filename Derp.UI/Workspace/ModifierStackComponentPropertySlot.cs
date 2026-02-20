using System;
using Property;
using Property.Runtime;

namespace Derp.UI;

internal static class ModifierStackComponentPropertySlot
{
    private static bool _initialized;
    private static ulong[] _keyBySlot = Array.Empty<ulong>();
    private static ushort[] _propertyIndexBySlot = Array.Empty<ushort>();
    private static int _mask;

    public static PropertySlot ArrayElement(AnyComponentHandle modifiersComponent, string fieldName, int elementIndex, PropertyKind kind)
    {
        EnsureInitialized(modifiersComponent);

        ulong propertyId = ModifierStackComponentPropertyId.ArrayElement(fieldName, elementIndex);
        ushort propertyIndex = GetPropertyIndex(propertyId, fieldName, elementIndex);
        return new PropertySlot(modifiersComponent, propertyIndex, propertyId, kind);
    }

    private static ushort GetPropertyIndex(ulong propertyId, string fieldName, int elementIndex)
    {
        int slot = (int)propertyId & _mask;
        while (true)
        {
            ulong existing = _keyBySlot[slot];
            if (existing == 0)
            {
                throw new InvalidOperationException($"ModifierStackComponent property not found: {fieldName}[{elementIndex}].");
            }

            if (existing == propertyId)
            {
                return _propertyIndexBySlot[slot];
            }

            slot = (slot + 1) & _mask;
        }
    }

    private static void EnsureInitialized(AnyComponentHandle modifiersComponent)
    {
        if (_initialized)
        {
            return;
        }

        int propertyCount = PropertyDispatcher.GetPropertyCount(modifiersComponent);
        if (propertyCount <= 0)
        {
            _initialized = true;
            return;
        }

        int capacity = NextPowerOfTwo(Math.Max(16, propertyCount * 2));
        _keyBySlot = new ulong[capacity];
        _propertyIndexBySlot = new ushort[capacity];
        _mask = capacity - 1;

        for (ushort propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
        {
            if (!PropertyDispatcher.TryGetInfo(modifiersComponent, propertyIndex, out PropertyInfo info))
            {
                continue;
            }

            ulong key = info.PropertyId;
            if (key == 0)
            {
                continue;
            }

            int slot = (int)key & _mask;
            while (true)
            {
                if (_keyBySlot[slot] == 0)
                {
                    _keyBySlot[slot] = key;
                    _propertyIndexBySlot[slot] = propertyIndex;
                    break;
                }

                slot = (slot + 1) & _mask;
            }
        }

        _initialized = true;
    }

    private static int NextPowerOfTwo(int value)
    {
        int v = value;
        v--;
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        v++;
        return v < 2 ? 2 : v;
    }
}

