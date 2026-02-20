#nullable enable
using System;

namespace ConfigRefresh
{
    /// <summary>
    /// Marks a private field as caching a value from the ConfigSource table.
    /// Used for system-level config (singletons).
    ///
    /// The field name is mapped to a property name using convention:
    /// _fieldName -> FieldName (strip underscore, capitalize first letter).
    /// Use PropertyName to override the convention.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class CachedConfigAttribute : Attribute
    {
        /// <summary>
        /// Override the property name in the source table.
        /// If null, uses convention: _fieldName -> FieldName.
        /// </summary>
        public string? PropertyName { get; }

        public CachedConfigAttribute()
        {
            PropertyName = null;
        }

        public CachedConfigAttribute(string propertyName)
        {
            PropertyName = propertyName;
        }
    }
}
