#nullable enable
using System;

namespace ConfigRefresh
{
    /// <summary>
    /// Marks a field in a SimRow as caching a stat from the type table.
    /// Used for entity-level stats that are looked up by TypeId.
    ///
    /// The field name is mapped to a property name using convention.
    /// Use PropertyName to override the convention.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class CachedStatAttribute : Attribute
    {
        /// <summary>
        /// Override the property name in the type table.
        /// If null, uses the field name directly.
        /// Example: MaxHealth field with PropertyName="Health" maps to TypeData.Health.
        /// </summary>
        public string? PropertyName { get; }

        public CachedStatAttribute()
        {
            PropertyName = null;
        }

        public CachedStatAttribute(string propertyName)
        {
            PropertyName = propertyName;
        }
    }
}
