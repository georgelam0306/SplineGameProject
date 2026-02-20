using System;

namespace GameDocDatabase;

/// <summary>
/// Marks a struct as a GameDocDatabase table.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class GameDocTableAttribute : Attribute
{
    public string TableName { get; }
    public int Version { get; set; } = 1;

    public GameDocTableAttribute(string tableName)
    {
        TableName = tableName;
    }
}

/// <summary>
/// Marks a field as the primary key (unique, required).
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class PrimaryKeyAttribute : Attribute
{
}

/// <summary>
/// Marks a field as a secondary key for indexed lookups.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class SecondaryKeyAttribute : Attribute
{
    public int Index { get; }
    public int KeyOrder { get; set; } = 0;

    public SecondaryKeyAttribute(int index)
    {
        Index = index;
    }
}

/// <summary>
/// Indicates a key allows multiple records with the same value.
/// Returns RangeView instead of single record.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class NonUniqueAttribute : Attribute
{
}

/// <summary>
/// Marks a field as a foreign key reference to another table.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class ForeignKeyAttribute : Attribute
{
    public Type ReferencedTable { get; }

    public ForeignKeyAttribute(Type referencedTable)
    {
        ReferencedTable = referencedTable;
    }
}
