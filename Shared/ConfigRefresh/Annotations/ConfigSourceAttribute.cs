#nullable enable
using System;

namespace ConfigRefresh
{
    /// <summary>
    /// Marks a class or struct as having cached config from a GameDocDatabase table.
    /// For systems: specifies the table type and row ID for singleton lookup.
    /// For SimRows: specifies the type table to look up stats from (uses TypeIdField).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class ConfigSourceAttribute : Attribute
    {
        /// <summary>The GameDocDatabase table type (e.g., typeof(SeparationConfigData)).</summary>
        public Type TableType { get; }

        /// <summary>
        /// For singleton tables: the int value to use for lookup (cast enum to int).
        /// For entity tables: leave as default (uses TypeIdField).
        /// </summary>
        public int RowId { get; }

        /// <summary>
        /// Creates a ConfigSource for entity-level lookup (uses TypeIdField).
        /// </summary>
        public ConfigSourceAttribute(Type tableType)
        {
            TableType = tableType;
            RowId = -1; // Sentinel for entity-level
        }

        /// <summary>
        /// Creates a ConfigSource for singleton lookup with explicit row ID.
        /// </summary>
        public ConfigSourceAttribute(Type tableType, int rowId)
        {
            TableType = tableType;
            RowId = rowId;
        }
    }
}
