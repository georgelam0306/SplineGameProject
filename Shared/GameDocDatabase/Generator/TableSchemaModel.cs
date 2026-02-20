using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace GameDocDatabase.Generator;

internal sealed class TableSchemaModel
{
    public INamedTypeSymbol Type { get; }
    public string TableName { get; }
    public int Version { get; }
    public string Namespace { get; }
    public string TypeName { get; }
    public List<FieldInfo> Fields { get; } = new();
    public FieldInfo? PrimaryKey { get; set; }
    public List<SecondaryKeyInfo> SecondaryKeys { get; } = new();

    public TableSchemaModel(INamedTypeSymbol type, string tableName, int version)
    {
        Type = type;
        TableName = tableName;
        Version = version;
        Namespace = type.ContainingNamespace?.ToDisplayString() ?? "global";
        TypeName = type.Name;
    }
}

internal sealed class FieldInfo
{
    public string Name { get; }
    public ITypeSymbol Type { get; }
    public string TypeName { get; }
    public int Size { get; }
    public bool IsPrimaryKey { get; set; }
    public bool IsSecondaryKey { get; set; }
    public bool IsNonUnique { get; set; }
    public int SecondaryKeyIndex { get; set; } = -1;
    public INamedTypeSymbol? ForeignKeyTable { get; set; }

    public FieldInfo(string name, ITypeSymbol type, int size)
    {
        Name = name;
        Type = type;
        TypeName = type.ToDisplayString();
        Size = size;
    }
}

internal sealed class SecondaryKeyInfo
{
    public FieldInfo Field { get; }
    public int Index { get; }
    public bool IsNonUnique { get; }

    public SecondaryKeyInfo(FieldInfo field, int index, bool isNonUnique)
    {
        Field = field;
        Index = index;
        IsNonUnique = isNonUnique;
    }
}
