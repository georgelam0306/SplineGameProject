#nullable enable
using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace ConfigRefresh.Generator
{
    /// <summary>
    /// Represents a class/struct with cached config fields.
    /// </summary>
    internal sealed class ConfigSourceModel
    {
        public INamedTypeSymbol Type { get; }
        public INamedTypeSymbol TableType { get; }
        public int RowId { get; } // -1 for entity-level (uses TypeIdField)
        public bool IsEntityLevel => RowId == -1;
        public string? TypeIdFieldName { get; set; }
        public ITypeSymbol? TypeIdFieldType { get; set; }
        public List<CachedFieldInfo> CachedFields { get; } = new List<CachedFieldInfo>();

        public ConfigSourceModel(
            INamedTypeSymbol type,
            INamedTypeSymbol tableType,
            int rowId)
        {
            Type = type;
            TableType = tableType;
            RowId = rowId;
        }
    }

    /// <summary>
    /// Represents a single cached field.
    /// </summary>
    internal sealed class CachedFieldInfo
    {
        public string FieldName { get; }
        public string SourcePropertyName { get; }
        public ITypeSymbol FieldType { get; }

        public CachedFieldInfo(
            string fieldName,
            string sourcePropertyName,
            ITypeSymbol fieldType)
        {
            FieldName = fieldName;
            SourcePropertyName = sourcePropertyName;
            FieldType = fieldType;
        }
    }
}
