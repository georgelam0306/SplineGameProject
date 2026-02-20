namespace Derp.Doc.Model;

public sealed class DocTable
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Table";
    public string? SystemKey { get; set; }
    public bool IsSystemSchemaLocked { get; set; }
    public bool IsSystemDataLocked { get; set; }
    public string? FolderId { get; set; }
    public string? SchemaSourceTableId { get; set; }
    public string? InheritanceSourceTableId { get; set; }
    public List<DocColumn> Columns { get; set; } = new();
    public List<DocTableVariant> Variants { get; set; } = new();
    public List<DocRow> Rows { get; set; } = new();
    public List<DocTableVariantDelta> VariantDeltas { get; set; } = new();

    /// <summary>
    /// File name stem used for on-disk storage (e.g. "tasks" for tasks.schema.json).
    /// </summary>
    public string FileName { get; set; } = "table";

    /// <summary>
    /// When non-null, this table is a derived (computed) view. Rows are materialized
    /// from other tables via the Append/Join pipeline defined here.
    /// </summary>
    public DocDerivedConfig? DerivedConfig { get; set; }

    public bool IsDerived => DerivedConfig != null;

    /// <summary>
    /// Runtime export configuration (Phase 5).
    /// </summary>
    public DocTableExportConfig? ExportConfig { get; set; }

    /// <summary>
    /// Key metadata (Phase 5). Primary key and secondary keys used for validation and query APIs.
    /// </summary>
    public DocTableKeys Keys { get; set; } = new();

    /// <summary>
    /// Saved view configurations (Phase 6). Each view defines filter/sort/column visibility
    /// and a presentation type (Grid, Board, Calendar, Chart).
    /// </summary>
    public List<DocView> Views { get; set; } = new();

    /// <summary>
    /// Table-scoped variables that can be referenced by view property bindings.
    /// </summary>
    public List<DocTableVariable> Variables { get; set; } = new();

    /// <summary>
    /// If this table is a child subtable, the parent table's ID.
    /// </summary>
    public string? ParentTableId { get; set; }

    /// <summary>
    /// If this table is a child subtable, the ID of the hidden column storing the parent row ID.
    /// </summary>
    public string? ParentRowColumnId { get; set; }

    /// <summary>
    /// Optional plugin-owned table type id. When set, this table was created and is managed by a plugin table workflow.
    /// </summary>
    public string? PluginTableTypeId { get; set; }

    /// <summary>
    /// Optional owner column type id that created this plugin table (for example: splinegame.level).
    /// </summary>
    public string? PluginOwnerColumnTypeId { get; set; }

    /// <summary>
    /// When true, schema mutations are blocked for this plugin-owned table.
    /// </summary>
    public bool IsPluginSchemaLocked { get; set; }

    public bool IsSubtable => !string.IsNullOrEmpty(ParentTableId);
    public bool IsSchemaLinked => !string.IsNullOrWhiteSpace(SchemaSourceTableId);
    public bool IsInherited => !string.IsNullOrWhiteSpace(InheritanceSourceTableId);
    public bool IsSystemTable => !string.IsNullOrWhiteSpace(SystemKey);
}
