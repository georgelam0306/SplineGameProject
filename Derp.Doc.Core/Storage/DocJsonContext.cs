using System.Text.Json;
using System.Text.Json.Serialization;

namespace Derp.Doc.Storage;

/// <summary>
/// DTO for project.json — project manifest with table references.
/// </summary>
internal sealed class ProjectDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("folders")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<FolderDto>? Folders { get; set; }

    [JsonPropertyName("tables")]
    public List<TableRefDto> Tables { get; set; } = new();

    [JsonPropertyName("documents")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DocumentRefDto>? Documents { get; set; }

    [JsonPropertyName("ui")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProjectUiDto? Ui { get; set; }

    [JsonPropertyName("pluginSettings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? PluginSettings { get; set; }

    // Legacy: variants were previously stored at the project level. They now live per-table
    // in {table}.schema.json. We still deserialize this for migration.
    [JsonPropertyName("variants")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ProjectVariantDto>? Variants { get; set; }
}

internal sealed class ProjectVariantDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

internal sealed class TableRefDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("folderId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FolderId { get; set; }
}

/// <summary>
/// DTO for schema.json — table schema with column definitions.
/// </summary>
internal sealed class TableSchemaDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("columns")]
    public List<ColumnDto> Columns { get; set; } = new();

    [JsonPropertyName("variants")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TableVariantDto>? Variants { get; set; }

    [JsonPropertyName("derived")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DerivedConfigDto? Derived { get; set; }

    [JsonPropertyName("export")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExportConfigDto? Export { get; set; }

    [JsonPropertyName("keys")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TableKeysDto? Keys { get; set; }

    [JsonPropertyName("parentTableId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentTableId { get; set; }

    [JsonPropertyName("parentRowColumnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentRowColumnId { get; set; }

    [JsonPropertyName("pluginTableTypeId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PluginTableTypeId { get; set; }

    [JsonPropertyName("pluginOwnerColumnTypeId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PluginOwnerColumnTypeId { get; set; }

    [JsonPropertyName("pluginSchemaLocked")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PluginSchemaLocked { get; set; }

    [JsonPropertyName("variables")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TableVariableDto>? Variables { get; set; }

    [JsonPropertyName("schemaSourceTableId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SchemaSourceTableId { get; set; }

    [JsonPropertyName("inheritanceSourceTableId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InheritanceSourceTableId { get; set; }

    [JsonPropertyName("systemKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SystemKey { get; set; }

    [JsonPropertyName("systemSchemaLocked")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SystemSchemaLocked { get; set; }

    [JsonPropertyName("systemDataLocked")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SystemDataLocked { get; set; }
}

internal sealed class TableVariantDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

internal sealed class TableVariableDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "Text";

    [JsonPropertyName("typeId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TypeId { get; set; }

    [JsonPropertyName("expression")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Expression { get; set; }
}

internal sealed class ColumnDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "Text";

    [JsonPropertyName("typeId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TypeId { get; set; }

    [JsonPropertyName("pluginSettings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PluginSettings { get; set; }

    [JsonPropertyName("width")]
    public float Width { get; set; } = 150f;

    [JsonPropertyName("options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Options { get; set; }

    [JsonPropertyName("formula")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Formula { get; set; }

    [JsonPropertyName("relationTableId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RelationTableId { get; set; }

    [JsonPropertyName("tableRefBaseTableId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TableRefBaseTableId { get; set; }

    [JsonPropertyName("rowRefTableRefColumnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RowRefTableRefColumnId { get; set; }

    [JsonPropertyName("relationTargetMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RelationTargetMode { get; set; }

    [JsonPropertyName("relationTableVariantId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int RelationTableVariantId { get; set; }

    [JsonPropertyName("relationDisplayColumnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RelationDisplayColumnId { get; set; }

    [JsonPropertyName("hidden")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Hidden { get; set; }

    [JsonPropertyName("projected")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Projected { get; set; }

    [JsonPropertyName("inherited")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Inherited { get; set; }

    [JsonPropertyName("exportType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExportType { get; set; }

    [JsonPropertyName("numberMin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? NumberMin { get; set; }

    [JsonPropertyName("numberMax")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? NumberMax { get; set; }

    [JsonPropertyName("exportEnumName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExportEnumName { get; set; }

    [JsonPropertyName("exportIgnore")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ExportIgnore { get; set; }

    [JsonPropertyName("subtableId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SubtableId { get; set; }

    [JsonPropertyName("subtableDisplayRendererId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SubtableDisplayRendererId { get; set; }

    [JsonPropertyName("subtableDisplayCellWidth")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? SubtableDisplayCellWidth { get; set; }

    [JsonPropertyName("subtableDisplayCellHeight")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? SubtableDisplayCellHeight { get; set; }

    [JsonPropertyName("subtableDisplayPreviewQuality")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SubtableDisplayPreviewQuality { get; set; }

    [JsonPropertyName("formulaEvalScopes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FormulaEvalScopes { get; set; }

    [JsonPropertyName("modelPreview")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ModelPreviewDto? ModelPreview { get; set; }

    // Backward compatibility for older schemas. Serialized output uses formulaEvalScopes.
    [JsonPropertyName("livePreviewPriority")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyLivePreviewPriority { get; set; }
}

internal sealed class ModelPreviewDto
{
    [JsonPropertyName("orbitYawDegrees")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? OrbitYawDegrees { get; set; }

    [JsonPropertyName("orbitPitchDegrees")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? OrbitPitchDegrees { get; set; }

    [JsonPropertyName("panX")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? PanX { get; set; }

    [JsonPropertyName("panY")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? PanY { get; set; }

    [JsonPropertyName("zoom")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Zoom { get; set; }

    [JsonPropertyName("textureRelativePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TextureRelativePath { get; set; }
}

internal sealed class AssetCellDto
{
    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Value { get; set; }

    [JsonPropertyName("modelPreview")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ModelPreviewDto? ModelPreview { get; set; }
}

internal sealed class Vec2CellDto
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}

internal sealed class Vec3CellDto
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }
}

internal sealed class Vec4CellDto
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }

    [JsonPropertyName("w")]
    public double W { get; set; }
}

internal sealed class ColorCellDto
{
    [JsonPropertyName("r")]
    public double R { get; set; }

    [JsonPropertyName("g")]
    public double G { get; set; }

    [JsonPropertyName("b")]
    public double B { get; set; }

    [JsonPropertyName("a")]
    public double A { get; set; }
}

internal sealed class FormulaOverrideCellDto
{
    [JsonPropertyName("f")]
    public string Formula { get; set; } = "";

    [JsonPropertyName("v")]
    public JsonElement Value { get; set; }
}

internal sealed class ExportConfigDto
{
    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Enabled { get; set; }

    [JsonPropertyName("namespace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Namespace { get; set; }

    [JsonPropertyName("structName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StructName { get; set; }
}

internal sealed class TableKeysDto
{
    [JsonPropertyName("primaryKeyColumnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PrimaryKeyColumnId { get; set; }

    [JsonPropertyName("secondaryKeys")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SecondaryKeyDto>? SecondaryKeys { get; set; }
}

internal sealed class SecondaryKeyDto
{
    [JsonPropertyName("columnId")]
    public string ColumnId { get; set; } = "";

    [JsonPropertyName("unique")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Unique { get; set; }
}

/// <summary>
/// DTO for a single row in rows.jsonl — one JSON object per line.
/// </summary>
internal sealed class RowDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("cells")]
    public Dictionary<string, JsonElement> Cells { get; set; } = new();
}

internal sealed class TableVariantDeltaOperationDto
{
    [JsonPropertyName("variantId")]
    public int VariantId { get; set; }

    [JsonPropertyName("op")]
    public string Op { get; set; } = "";

    [JsonPropertyName("rowId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RowId { get; set; }

    [JsonPropertyName("columnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ColumnId { get; set; }

    [JsonPropertyName("cells")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonElement>? Cells { get; set; }

    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Value { get; set; }
}

/// <summary>
/// DTO for document references in project.json.
/// </summary>
internal sealed class DocumentRefDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("folderId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FolderId { get; set; }
}

internal sealed class FolderDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "Tables";

    [JsonPropertyName("parentFolderId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentFolderId { get; set; }
}

internal sealed class ProjectUiDto
{
    [JsonPropertyName("tableFolderExpandedById")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, bool>? TableFolderExpandedById { get; set; }

    [JsonPropertyName("documentFolderExpandedById")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, bool>? DocumentFolderExpandedById { get; set; }
}

/// <summary>
/// DTO for {fileName}.meta.json — document metadata.
/// </summary>
internal sealed class DocumentMetaDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";
}

/// <summary>
/// DTO for a single block in blocks.jsonl.
/// </summary>
internal sealed class BlockDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("order")]
    public string Order { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "Paragraph";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("spans")]
    public List<RichSpanDto> Spans { get; set; } = new();

    [JsonPropertyName("indent")]
    public int Indent { get; set; }

    [JsonPropertyName("checked")]
    public bool Checked { get; set; }

    [JsonPropertyName("lang")]
    public string Lang { get; set; } = "";

    [JsonPropertyName("tableId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TableId { get; set; }

    [JsonPropertyName("tableVariantId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int TableVariantId { get; set; }

    [JsonPropertyName("viewId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ViewId { get; set; }

    [JsonPropertyName("embeddedWidth")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public float EmbeddedWidth { get; set; }

    [JsonPropertyName("embeddedHeight")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public float EmbeddedHeight { get; set; }

    [JsonPropertyName("tableVariableOverrides")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<BlockTableVariableOverrideDto>? TableVariableOverrides { get; set; }
}

internal sealed class BlockTableVariableOverrideDto
{
    [JsonPropertyName("variableId")]
    public string VariableId { get; set; } = "";

    [JsonPropertyName("expression")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Expression { get; set; }
}

// --- Derived table DTOs ---

internal sealed class DerivedConfigDto
{
    [JsonPropertyName("baseTableId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BaseTableId { get; set; }

    [JsonPropertyName("steps")]
    public List<DerivedStepDto> Steps { get; set; } = new();

    [JsonPropertyName("projections")]
    public List<DerivedProjectionDto> Projections { get; set; } = new();

    [JsonPropertyName("suppressedProjections")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DerivedSuppressedProjectionDto>? SuppressedProjections { get; set; }

    [JsonPropertyName("filterExpression")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FilterExpression { get; set; }
}

internal sealed class DerivedStepDto
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "Append";

    [JsonPropertyName("sourceTableId")]
    public string SourceTableId { get; set; } = "";

    [JsonPropertyName("joinKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JoinKind { get; set; }

    [JsonPropertyName("keyMappings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DerivedKeyMappingDto>? KeyMappings { get; set; }
}

internal sealed class DerivedKeyMappingDto
{
    [JsonPropertyName("baseColumnId")]
    public string BaseColumnId { get; set; } = "";

    [JsonPropertyName("sourceColumnId")]
    public string SourceColumnId { get; set; } = "";
}

internal sealed class DerivedProjectionDto
{
    [JsonPropertyName("sourceTableId")]
    public string SourceTableId { get; set; } = "";

    [JsonPropertyName("sourceColumnId")]
    public string SourceColumnId { get; set; } = "";

    [JsonPropertyName("outputColumnId")]
    public string OutputColumnId { get; set; } = "";

    [JsonPropertyName("renameAlias")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RenameAlias { get; set; }
}

internal sealed class DerivedSuppressedProjectionDto
{
    [JsonPropertyName("sourceTableId")]
    public string SourceTableId { get; set; } = "";

    [JsonPropertyName("sourceColumnId")]
    public string SourceColumnId { get; set; } = "";

    [JsonPropertyName("outputColumnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputColumnId { get; set; }
}

/// <summary>
/// DTO for a row in {fileName}.own-data.jsonl — local cell data keyed by derived row ID.
/// </summary>
internal sealed class OwnDataRowDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("cells")]
    public Dictionary<string, JsonElement> Cells { get; set; } = new();
}

/// <summary>
/// DTO for a rich text span in blocks.jsonl.
/// </summary>
internal sealed class RichSpanDto
{
    [JsonPropertyName("s")]
    public int S { get; set; }

    [JsonPropertyName("l")]
    public int L { get; set; }

    [JsonPropertyName("st")]
    public int St { get; set; }
}

// --- View DTOs (Phase 6) ---

internal sealed class ViewsFileDto
{
    [JsonPropertyName("views")]
    public List<ViewDto> Views { get; set; } = new();
}

internal sealed class ViewDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "Grid";

    [JsonPropertyName("visibleColumnIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? VisibleColumnIds { get; set; }

    [JsonPropertyName("sorts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ViewSortDto>? Sorts { get; set; }

    [JsonPropertyName("filters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ViewFilterDto>? Filters { get; set; }

    [JsonPropertyName("groupByColumnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GroupByColumnId { get; set; }

    [JsonPropertyName("groupByColumnBinding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ViewBindingDto? GroupByColumnBinding { get; set; }

    [JsonPropertyName("calendarDateColumnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CalendarDateColumnId { get; set; }

    [JsonPropertyName("calendarDateColumnBinding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ViewBindingDto? CalendarDateColumnBinding { get; set; }

    [JsonPropertyName("chartKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChartKind { get; set; }

    [JsonPropertyName("chartKindBinding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ViewBindingDto? ChartKindBinding { get; set; }

    [JsonPropertyName("chartCategoryColumnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChartCategoryColumnId { get; set; }

    [JsonPropertyName("chartCategoryColumnBinding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ViewBindingDto? ChartCategoryColumnBinding { get; set; }

    [JsonPropertyName("chartValueColumnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChartValueColumnId { get; set; }

    [JsonPropertyName("chartValueColumnBinding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ViewBindingDto? ChartValueColumnBinding { get; set; }

    [JsonPropertyName("customRendererId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CustomRendererId { get; set; }
}

internal sealed class ViewSortDto
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("columnId")]
    public string ColumnId { get; set; } = "";

    [JsonPropertyName("descending")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Descending { get; set; }

    [JsonPropertyName("columnIdBinding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ViewBindingDto? ColumnIdBinding { get; set; }

    [JsonPropertyName("descendingBinding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ViewBindingDto? DescendingBinding { get; set; }
}

internal sealed class ViewFilterDto
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("columnId")]
    public string ColumnId { get; set; } = "";

    [JsonPropertyName("op")]
    public string Op { get; set; } = "Equals";

    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Value { get; set; }

    [JsonPropertyName("columnIdBinding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ViewBindingDto? ColumnIdBinding { get; set; }

    [JsonPropertyName("opBinding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ViewBindingDto? OpBinding { get; set; }

    [JsonPropertyName("valueBinding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ViewBindingDto? ValueBinding { get; set; }
}

internal sealed class ViewBindingDto
{
    [JsonPropertyName("variableName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VariableName { get; set; }

    [JsonPropertyName("formula")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Formula { get; set; }
}

[JsonSerializable(typeof(ProjectDto))]
[JsonSerializable(typeof(TableSchemaDto))]
[JsonSerializable(typeof(RowDto))]
[JsonSerializable(typeof(TableVariantDeltaOperationDto))]
[JsonSerializable(typeof(AssetCellDto))]
[JsonSerializable(typeof(Vec2CellDto))]
[JsonSerializable(typeof(Vec3CellDto))]
[JsonSerializable(typeof(Vec4CellDto))]
[JsonSerializable(typeof(ColorCellDto))]
[JsonSerializable(typeof(FormulaOverrideCellDto))]
[JsonSerializable(typeof(DocumentMetaDto))]
[JsonSerializable(typeof(DerivedConfigDto))]
[JsonSerializable(typeof(ViewsFileDto))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class DocJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Compact JSON context for JSONL rows and blocks (no indenting).
/// </summary>
[JsonSerializable(typeof(RowDto))]
[JsonSerializable(typeof(BlockDto))]
[JsonSerializable(typeof(OwnDataRowDto))]
[JsonSerializable(typeof(TableVariantDeltaOperationDto))]
[JsonSerializable(typeof(AssetCellDto))]
[JsonSerializable(typeof(Vec2CellDto))]
[JsonSerializable(typeof(Vec3CellDto))]
[JsonSerializable(typeof(Vec4CellDto))]
[JsonSerializable(typeof(ColorCellDto))]
[JsonSerializable(typeof(FormulaOverrideCellDto))]
[JsonSourceGenerationOptions(WriteIndented = false)]
internal partial class DocJsonCompactContext : JsonSerializerContext
{
}
