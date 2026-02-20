// SPDX-License-Identifier: MIT
#nullable enable
using System;

namespace Property;

[AttributeUsage(AttributeTargets.Field)]
public sealed class PropertyDrawerAttribute : Attribute
{
    public Type DrawerType { get; }
    public int Options { get; }

    public PropertyDrawerAttribute(Type drawerType, int options = 0)
    {
        DrawerType = drawerType;
        Options = options;
    }
}

