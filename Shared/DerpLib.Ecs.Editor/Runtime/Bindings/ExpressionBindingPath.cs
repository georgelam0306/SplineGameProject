using System;
using Property;

namespace DerpLib.Ecs.Editor;

/// <summary>
/// Locates a specific property on a specific entity/component for binding read/write.
/// </summary>
public readonly struct ExpressionBindingPath : IEquatable<ExpressionBindingPath>
{
    public readonly uint EntityStableId;
    public readonly ulong ComponentSchemaId;
    public readonly ulong PropertyId;
    public readonly ushort PropertyIndex;
    public readonly PropertyKind ValueKind;

    public ExpressionBindingPath(uint entityStableId, ulong componentSchemaId, ulong propertyId, ushort propertyIndex, PropertyKind valueKind)
    {
        EntityStableId = entityStableId;
        ComponentSchemaId = componentSchemaId;
        PropertyId = propertyId;
        PropertyIndex = propertyIndex;
        ValueKind = valueKind;
    }

    public bool Equals(ExpressionBindingPath other) =>
        EntityStableId == other.EntityStableId &&
        ComponentSchemaId == other.ComponentSchemaId &&
        PropertyId == other.PropertyId;

    public override bool Equals(object? obj) => obj is ExpressionBindingPath other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(EntityStableId, ComponentSchemaId, PropertyId);
    public static bool operator ==(in ExpressionBindingPath a, in ExpressionBindingPath b) => a.Equals(b);
    public static bool operator !=(in ExpressionBindingPath a, in ExpressionBindingPath b) => !a.Equals(b);
}
