// SPDX-License-Identifier: MIT
#nullable enable
using System;

namespace Property;

[AttributeUsage(AttributeTargets.Field)]
public sealed class PropertyGizmoAttribute : Attribute
{
    public Type GizmoType { get; }
    public PropertyGizmoTriggers Triggers { get; }

    public PropertyGizmoAttribute(Type gizmoType, PropertyGizmoTriggers triggers = PropertyGizmoTriggers.Active)
    {
        GizmoType = gizmoType;
        Triggers = triggers;
    }
}

