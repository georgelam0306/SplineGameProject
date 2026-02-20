#nullable enable
using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace SimTable.Generator
{
    /// <summary>
    /// Model for a [MultiTableQuery] interface or [TableUnion] struct.
    /// Represents a unified query over multiple SimTable types.
    /// </summary>
    internal sealed class MultiTableQueryModel
    {
        /// <summary>
        /// The interface or struct symbol that defines the query.
        /// </summary>
        public INamedTypeSymbol Type { get; }

        /// <summary>
        /// The name of the query (interface name without 'I' prefix, or struct name).
        /// </summary>
        public string QueryName { get; }

        /// <summary>
        /// Properties defined on the interface (for field projection).
        /// </summary>
        public List<QueryPropertyInfo> Properties { get; }

        /// <summary>
        /// SimTable types that participate in this query.
        /// For MultiTableQuery: auto-discovered by matching fields.
        /// For TableUnion: explicitly listed in attribute.
        /// </summary>
        public List<ParticipatingTableInfo> ParticipatingTables { get; }

        /// <summary>
        /// True if this is an explicit TableUnion (vs auto-discovered MultiTableQuery).
        /// </summary>
        public bool IsExplicitUnion { get; }

        /// <summary>
        /// Explicit type names to include (fully qualified). If empty, auto-discover all matching types.
        /// </summary>
        public List<string> ExplicitTypeNames { get; }

        public MultiTableQueryModel(INamedTypeSymbol type, bool isExplicitUnion)
        {
            Type = type;
            IsExplicitUnion = isExplicitUnion;
            Properties = new List<QueryPropertyInfo>();
            ParticipatingTables = new List<ParticipatingTableInfo>();
            ExplicitTypeNames = new List<string>();

            // Extract query name (remove 'I' prefix for interfaces)
            var name = type.Name;
            if (!isExplicitUnion && name.Length > 1 && name.StartsWith("I") && char.IsUpper(name[1]))
            {
                QueryName = name.Substring(1);
            }
            else
            {
                QueryName = name;
            }
        }
    }

    /// <summary>
    /// A property on a query interface that maps to a field on participating tables.
    /// </summary>
    internal sealed class QueryPropertyInfo
    {
        public string Name { get; }
        public ITypeSymbol Type { get; }
        public bool IsRefReturn { get; }

        public QueryPropertyInfo(string name, ITypeSymbol type, bool isRefReturn)
        {
            Name = name;
            Type = type;
            IsRefReturn = isRefReturn;
        }
    }

    /// <summary>
    /// A SimTable that participates in a query.
    /// </summary>
    internal sealed class ParticipatingTableInfo
    {
        /// <summary>
        /// The table's SchemaModel.
        /// </summary>
        public SchemaModel Schema { get; }

        /// <summary>
        /// Index used in the generated switch statement (0, 1, 2, ...).
        /// </summary>
        public int SwitchIndex { get; set; }

        /// <summary>
        /// The table's constant table ID (from TableIdConst).
        /// </summary>
        public int TableId { get; set; }

        public ParticipatingTableInfo(SchemaModel schema)
        {
            Schema = schema;
        }
    }
}
