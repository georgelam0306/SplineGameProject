#nullable enable
using System;

namespace ConfigRefresh
{
    /// <summary>
    /// Marks the field that holds the type ID for entity lookups.
    /// Used by the generator to find the correct row in the type table.
    /// Example: [TypeIdField] public ZombieTypeId TypeId;
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TypeIdFieldAttribute : Attribute
    {
    }
}
