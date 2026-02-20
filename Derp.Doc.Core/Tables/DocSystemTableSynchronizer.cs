using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Derp.Doc.Model;
using Derp.Doc.Plugins;
using Derp.Doc.Storage;

namespace Derp.Doc.Tables;

internal static class DocSystemTableSynchronizer
{
    private const string AssetsTableName = "assets";
    private const string PackagesTableName = "packages";
    private const string ExportsTableName = "exports";
    private const string TexturesTableName = "textures";
    private const string ModelsTableName = "models";
    private const string AudioTableName = "audio";
    private const string UiTableName = "ui";
    private const string MaterialsTableName = "materials";
    private const string AssetDepsTableName = "asset_deps";

    private const string AssetsFileName = "system_assets";
    private const string PackagesFileName = "system_packages";
    private const string ExportsFileName = "system_exports";
    private const string TexturesFileName = "system_textures";
    private const string ModelsFileName = "system_models";
    private const string AudioFileName = "system_audio";
    private const string UiFileName = "system_ui";
    private const string MaterialsFileName = "system_materials";
    private const string AssetDepsFileName = "system_asset_deps";

    private const string AssetsColumnAssetId = "asset_id";
    private const string AssetsColumnName = "name";
    private const string AssetsColumnRelativePath = "relative_path";
    private const string AssetsColumnKind = "kind";
    private const string AssetsColumnExtension = "extension";
    private const string AssetsColumnSizeBytes = "size_bytes";
    private const string AssetsColumnContentHash = "content_hash";
    private const string AssetsColumnMissing = "missing";
    private const string AssetsColumnLastSeenUtc = "last_seen_utc";

    private const string PackagesColumnPackageId = "package_id";
    private const string PackagesColumnName = "name";
    private const string PackagesColumnDefaultLoadFrom = "default_load_from";
    private const string PackagesColumnCdnBaseUri = "cdn_base_uri";
    private const string PackagesColumnOutputPath = "output_path";

    private const string ExportsColumnPackageId = "package_id";
    private const string ExportsColumnAssetId = "asset_id";
    private const string ExportsColumnEnabled = "enabled";
    private const string ExportsColumnAddress = "address";
    private const string ExportsColumnLoadFromOverride = "load_from_override";

    private const string AssetDepsColumnAssetId = "asset_id";
    private const string AssetDepsColumnDependsOnAssetId = "depends_on_asset_id";
    private const string AssetDepsColumnReason = "reason";
    private const string TypedPreviewColumn = "preview";
    private const string ResourcesRelativePrefix = "Resources/";

    private static readonly string[] AssetKindOptions = ["Texture", "Model", "Audio", "Material", "Ui", "Other"];
    private static readonly string[] PackageLoadFromOptions = ["Disk", "CDN"];
    private static readonly string[] ExportLoadFromOverrideOptions = ["Default", "Disk", "CDN"];
    private static readonly string[] TextureExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".tga"];
    private static readonly string[] ModelExtensions = [".obj", ".glb", ".gltf", ".fbx", ".dae", ".3ds", ".mesh"];
    private static readonly string[] AudioExtensions = [".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac", ".webm"];
    private static readonly string[] MaterialExtensions = [".mat", ".material"];
    private static readonly string[] UiExtensions = [".bdui"];

    private readonly struct SystemColumnSpec
    {
        public SystemColumnSpec(
            string id,
            string name,
            DocColumnKind kind,
            float width = 150f,
            string[]? options = null,
            bool hidden = false,
            string? exportType = null,
            string? relationTableId = null,
            DocRelationTargetMode relationTargetMode = DocRelationTargetMode.ExternalTable,
            int relationTableVariantId = 0,
            string? relationDisplayColumnId = null,
            string? tableRefBaseTableId = null,
            double? defaultNumberValue = null)
        {
            Id = id;
            Name = name;
            Kind = kind;
            Width = width;
            Options = options;
            Hidden = hidden;
            ExportType = exportType;
            RelationTableId = relationTableId;
            RelationTargetMode = relationTargetMode;
            RelationTableVariantId = relationTableVariantId;
            RelationDisplayColumnId = relationDisplayColumnId;
            TableRefBaseTableId = tableRefBaseTableId;
            DefaultNumberValue = defaultNumberValue;
        }

        public string Id { get; }
        public string Name { get; }
        public DocColumnKind Kind { get; }
        public float Width { get; }
        public string[]? Options { get; }
        public bool Hidden { get; }
        public string? ExportType { get; }
        public string? RelationTableId { get; }
        public DocRelationTargetMode RelationTargetMode { get; }
        public int RelationTableVariantId { get; }
        public string? RelationDisplayColumnId { get; }
        public string? TableRefBaseTableId { get; }
        public double? DefaultNumberValue { get; }
    }

    private readonly struct AssetScanEntry
    {
        public AssetScanEntry(
            string relativePath,
            string extension,
            long sizeBytes,
            string contentHash,
            string kind)
        {
            RelativePath = relativePath;
            Extension = extension;
            SizeBytes = sizeBytes;
            ContentHash = contentHash;
            Kind = kind;
        }

        public string RelativePath { get; }
        public string Extension { get; }
        public long SizeBytes { get; }
        public string ContentHash { get; }
        public string Kind { get; }
    }

    public static void Synchronize(DocProject project, string dbRoot)
    {
        if (project == null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        if (string.IsNullOrWhiteSpace(dbRoot))
        {
            throw new ArgumentException("dbRoot is required.", nameof(dbRoot));
        }

        DemoteUnknownSystemTableKeys(project);

        DocTable assetsTable = EnsureAssetsTable(project);
        DocTable packagesTable = EnsurePackagesTable(project);
        DocTable exportsTable = EnsureExportsTable(project, packagesTable, assetsTable);
        DocTable texturesTable = EnsureTexturesTable(project, assetsTable);
        DocTable modelsTable = EnsureModelsTable(project, assetsTable);
        DocTable audioTable = EnsureAudioTable(project, assetsTable);
        DocTable uiTable = EnsureUiTable(project, assetsTable);
        DocTable materialsTable = EnsureMaterialsTable(project, assetsTable);
        DocTable assetDepsTable = EnsureAssetDepsTable(project);
        DocTable splineGameEntityBaseTable = EnsureSplineGameEntityBaseTable(project);
        _ = EnsureSplineGameEntityToolsTable(project, splineGameEntityBaseTable);

        string? assetsRoot = ResolveAssetsRoot(dbRoot);
        string? resourcesRoot = ResolveResourcesRoot(dbRoot);
        ReconcileAssetsRows(assetsTable, assetsRoot, resourcesRoot);
        ReconcilePackagesRows(packagesTable);
        ReconcileExportsRows(exportsTable, packagesTable, assetsTable);
        ReconcileAssetDependencyRows(assetDepsTable);
        MaterializeDerivedSystemTableRows(project, texturesTable);
        MaterializeDerivedSystemTableRows(project, modelsTable);
        MaterializeDerivedSystemTableRows(project, audioTable);
        MaterializeDerivedSystemTableRows(project, uiTable);
        MaterializeDerivedSystemTableRows(project, materialsTable);
    }

    public static void NormalizeEditableTables(DocProject project)
    {
        if (project == null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        DocTable? assetsTable = FindSystemTableByKey(project, DocSystemTableKeys.Assets);
        DocTable? packagesTable = FindSystemTableByKey(project, DocSystemTableKeys.Packages);
        DocTable? exportsTable = FindSystemTableByKey(project, DocSystemTableKeys.Exports);

        if (packagesTable != null)
        {
            ReconcilePackagesRows(packagesTable);
        }

        if (exportsTable != null && assetsTable != null && packagesTable != null)
        {
            ReconcileExportsRows(exportsTable, packagesTable, assetsTable);
        }
    }

    private static void DemoteUnknownSystemTableKeys(DocProject project)
    {
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            DocTable table = project.Tables[tableIndex];
            if (string.IsNullOrWhiteSpace(table.SystemKey))
            {
                continue;
            }

            if (DocSystemTableKeys.IsKnown(table.SystemKey))
            {
                continue;
            }

            table.SystemKey = null;
            table.IsSystemSchemaLocked = false;
            table.IsSystemDataLocked = false;
        }
    }

    private static DocTable EnsureAssetsTable(DocProject project)
    {
        DocTable table = GetOrCreateSystemTable(
            project,
            DocSystemTableKeys.Assets,
            AssetsTableName,
            AssetsFileName,
            dataLocked: true);

        ReconcileSystemTableColumns(
            table,
            [
                new SystemColumnSpec(AssetsColumnAssetId, "AssetId", DocColumnKind.Number, 100f, exportType: "int"),
                new SystemColumnSpec(AssetsColumnName, "Name", DocColumnKind.Text, 220f),
                new SystemColumnSpec(AssetsColumnRelativePath, "RelativePath", DocColumnKind.Text, 320f),
                new SystemColumnSpec(AssetsColumnKind, "Kind", DocColumnKind.Select, 130f, AssetKindOptions),
                new SystemColumnSpec(AssetsColumnExtension, "Extension", DocColumnKind.Text, 90f),
                new SystemColumnSpec(AssetsColumnSizeBytes, "SizeBytes", DocColumnKind.Number, 120f),
                new SystemColumnSpec(AssetsColumnContentHash, "ContentHash", DocColumnKind.Text, 280f),
                new SystemColumnSpec(AssetsColumnMissing, "Missing", DocColumnKind.Checkbox, 90f),
                new SystemColumnSpec(AssetsColumnLastSeenUtc, "LastSeenUtc", DocColumnKind.Text, 200f),
            ]);

        table.DerivedConfig = null;
        table.SchemaSourceTableId = null;
        table.InheritanceSourceTableId = null;
        table.ExportConfig = null;
        table.Keys.PrimaryKeyColumnId = AssetsColumnAssetId;
        table.Keys.SecondaryKeys.Clear();
        table.Variants.Clear();
        table.VariantDeltas.Clear();
        return table;
    }

    private static DocTable EnsurePackagesTable(DocProject project)
    {
        DocTable table = GetOrCreateSystemTable(
            project,
            DocSystemTableKeys.Packages,
            PackagesTableName,
            PackagesFileName,
            dataLocked: false);

        ReconcileSystemTableColumns(
            table,
            [
                new SystemColumnSpec(PackagesColumnPackageId, "PackageId", DocColumnKind.Number, 100f, exportType: "int"),
                new SystemColumnSpec(PackagesColumnName, "Name", DocColumnKind.Text, 220f),
                new SystemColumnSpec(PackagesColumnDefaultLoadFrom, "DefaultLoadFrom", DocColumnKind.Select, 140f, PackageLoadFromOptions),
                new SystemColumnSpec(PackagesColumnCdnBaseUri, "CdnBaseUri", DocColumnKind.Text, 260f),
                new SystemColumnSpec(PackagesColumnOutputPath, "OutputPath", DocColumnKind.Text, 240f),
            ]);

        table.DerivedConfig = null;
        table.SchemaSourceTableId = null;
        table.InheritanceSourceTableId = null;
        table.ExportConfig = null;
        table.Keys.PrimaryKeyColumnId = PackagesColumnPackageId;
        table.Keys.SecondaryKeys.Clear();
        return table;
    }

    private static DocTable EnsureExportsTable(DocProject project, DocTable packagesTable, DocTable assetsTable)
    {
        DocTable table = GetOrCreateSystemTable(
            project,
            DocSystemTableKeys.Exports,
            ExportsTableName,
            ExportsFileName,
            dataLocked: false);

        ReconcileSystemTableColumns(
            table,
            [
                new SystemColumnSpec(
                    ExportsColumnPackageId,
                    "PackageId",
                    DocColumnKind.Relation,
                    180f,
                    relationTableId: packagesTable.Id,
                    relationDisplayColumnId: PackagesColumnName),
                new SystemColumnSpec(
                    ExportsColumnAssetId,
                    "AssetId",
                    DocColumnKind.Relation,
                    220f,
                    relationTableId: assetsTable.Id,
                    relationDisplayColumnId: AssetsColumnName),
                new SystemColumnSpec(ExportsColumnEnabled, "Enabled", DocColumnKind.Checkbox, 90f),
                new SystemColumnSpec(ExportsColumnAddress, "Address", DocColumnKind.Text, 260f),
                new SystemColumnSpec(ExportsColumnLoadFromOverride, "LoadFromOverride", DocColumnKind.Select, 160f, ExportLoadFromOverrideOptions),
            ]);

        table.DerivedConfig = null;
        table.SchemaSourceTableId = null;
        table.InheritanceSourceTableId = null;
        table.ExportConfig = null;
        table.Keys.PrimaryKeyColumnId = "";
        table.Keys.SecondaryKeys.Clear();
        return table;
    }

    private static DocTable EnsureTexturesTable(DocProject project, DocTable assetsTable)
    {
        return EnsureTypedDerivedTable(
            project,
            assetsTable,
            DocSystemTableKeys.Textures,
            TexturesTableName,
            TexturesFileName,
            "Texture",
            DocColumnKind.TextureAsset);
    }

    private static DocTable EnsureModelsTable(DocProject project, DocTable assetsTable)
    {
        return EnsureTypedDerivedTable(
            project,
            assetsTable,
            DocSystemTableKeys.Models,
            ModelsTableName,
            ModelsFileName,
            "Model",
            DocColumnKind.MeshAsset);
    }

    private static DocTable EnsureAudioTable(DocProject project, DocTable assetsTable)
    {
        return EnsureTypedDerivedTable(
            project,
            assetsTable,
            DocSystemTableKeys.Audio,
            AudioTableName,
            AudioFileName,
            "Audio",
            DocColumnKind.AudioAsset);
    }

    private static DocTable EnsureMaterialsTable(DocProject project, DocTable assetsTable)
    {
        return EnsureTypedDerivedTable(
            project,
            assetsTable,
            DocSystemTableKeys.Materials,
            MaterialsTableName,
            MaterialsFileName,
            "Material",
            previewColumnKind: null);
    }

    private static DocTable EnsureUiTable(DocProject project, DocTable assetsTable)
    {
        return EnsureTypedDerivedTable(
            project,
            assetsTable,
            DocSystemTableKeys.Ui,
            UiTableName,
            UiFileName,
            "Ui",
            DocColumnKind.UiAsset);
    }

    private static DocTable EnsureAssetDepsTable(DocProject project)
    {
        DocTable table = GetOrCreateSystemTable(
            project,
            DocSystemTableKeys.AssetDependencies,
            AssetDepsTableName,
            AssetDepsFileName,
            dataLocked: true);

        ReconcileSystemTableColumns(
            table,
            [
                new SystemColumnSpec(AssetDepsColumnAssetId, "AssetId", DocColumnKind.Number, 100f, exportType: "int"),
                new SystemColumnSpec(AssetDepsColumnDependsOnAssetId, "DependsOnAssetId", DocColumnKind.Number, 150f, exportType: "int"),
                new SystemColumnSpec(AssetDepsColumnReason, "Reason", DocColumnKind.Text, 220f),
            ]);

        table.DerivedConfig = null;
        table.SchemaSourceTableId = null;
        table.InheritanceSourceTableId = null;
        table.ExportConfig = null;
        table.Keys.PrimaryKeyColumnId = "";
        table.Keys.SecondaryKeys.Clear();
        table.Variants.Clear();
        table.VariantDeltas.Clear();
        return table;
    }

    private static DocTable EnsureSplineGameEntityBaseTable(DocProject project)
    {
        DocTable table = GetOrCreateSystemTable(
            project,
            DocSystemTableKeys.SplineGameEntityBase,
            SplineGameLevelIds.SystemEntityBaseTableName,
            SplineGameLevelIds.SystemEntityBaseFileName,
            dataLocked: false);

        ReconcileSystemTableColumns(
            table,
            [
                new SystemColumnSpec(SplineGameLevelIds.EntityDefinitionIdColumnId, "Id", DocColumnKind.Id, 180f),
                new SystemColumnSpec(SplineGameLevelIds.EntityDefinitionNameColumnId, "Name", DocColumnKind.Text, 220f),
                new SystemColumnSpec(SplineGameLevelIds.EntityDefinitionUiAssetColumnId, "UiAsset", DocColumnKind.UiAsset, 210f),
                new SystemColumnSpec(
                    SplineGameLevelIds.EntityDefinitionScaleColumnId,
                    "Scale",
                    DocColumnKind.Number,
                    90f,
                    defaultNumberValue: 0.1d),
            ]);

        table.DerivedConfig = null;
        table.SchemaSourceTableId = null;
        table.InheritanceSourceTableId = null;
        table.ExportConfig = null;
        table.Keys.PrimaryKeyColumnId = SplineGameLevelIds.EntityDefinitionIdColumnId;
        table.Keys.SecondaryKeys.Clear();
        table.Variants.Clear();
        table.VariantDeltas.Clear();
        return table;
    }

    private static DocTable EnsureSplineGameEntityToolsTable(
        DocProject project,
        DocTable entityBaseTable)
    {
        DocTable table = GetOrCreateSystemTable(
            project,
            DocSystemTableKeys.SplineGameEntityTools,
            SplineGameLevelIds.SystemEntityToolsTableName,
            SplineGameLevelIds.SystemEntityToolsFileName,
            dataLocked: false);

        ReconcileSystemTableColumns(
            table,
            [
                new SystemColumnSpec(SplineGameLevelIds.EntityToolIdColumnId, "Id", DocColumnKind.Id, 180f),
                new SystemColumnSpec(SplineGameLevelIds.EntityToolNameColumnId, "Name", DocColumnKind.Text, 220f),
                new SystemColumnSpec(
                    SplineGameLevelIds.EntityToolTableRefColumnId,
                    "EntitiesTable",
                    DocColumnKind.TableRef,
                    220f,
                    tableRefBaseTableId: entityBaseTable.Id),
            ]);

        table.DerivedConfig = null;
        table.SchemaSourceTableId = null;
        table.InheritanceSourceTableId = null;
        table.ExportConfig = null;
        table.Keys.PrimaryKeyColumnId = SplineGameLevelIds.EntityToolIdColumnId;
        table.Keys.SecondaryKeys.Clear();
        table.Variants.Clear();
        table.VariantDeltas.Clear();
        return table;
    }

    private static DocTable EnsureTypedDerivedTable(
        DocProject project,
        DocTable assetsTable,
        string systemKey,
        string tableName,
        string fileName,
        string kind,
        DocColumnKind? previewColumnKind)
    {
        DocTable table = GetOrCreateSystemTable(project, systemKey, tableName, fileName, dataLocked: true);
        ReconcileSystemTableColumns(table, BuildTypedAssetsProjectionColumns(previewColumnKind));
        table.SchemaSourceTableId = null;
        table.InheritanceSourceTableId = null;
        table.ExportConfig = null;
        table.Keys.PrimaryKeyColumnId = "";
        table.Keys.SecondaryKeys.Clear();
        table.Variants.Clear();
        table.VariantDeltas.Clear();

        table.DerivedConfig = BuildTypedAssetsDerivedConfig(assetsTable.Id, table.Columns, kind);
        return table;
    }

    private static List<SystemColumnSpec> BuildTypedAssetsProjectionColumns(DocColumnKind? previewColumnKind)
    {
        var columns = new List<SystemColumnSpec>(10)
        {
            new SystemColumnSpec(AssetsColumnAssetId, "AssetId", DocColumnKind.Number, 100f, exportType: "int"),
            new SystemColumnSpec(AssetsColumnName, "Name", DocColumnKind.Text, 220f),
        };

        if (previewColumnKind.HasValue)
        {
            columns.Add(new SystemColumnSpec(TypedPreviewColumn, "Preview", previewColumnKind.Value, 180f));
        }

        columns.Add(new SystemColumnSpec(AssetsColumnRelativePath, "RelativePath", DocColumnKind.Text, 320f));
        columns.Add(new SystemColumnSpec(AssetsColumnKind, "Kind", DocColumnKind.Select, 130f, AssetKindOptions));
        columns.Add(new SystemColumnSpec(AssetsColumnExtension, "Extension", DocColumnKind.Text, 90f));
        columns.Add(new SystemColumnSpec(AssetsColumnSizeBytes, "SizeBytes", DocColumnKind.Number, 120f));
        columns.Add(new SystemColumnSpec(AssetsColumnContentHash, "ContentHash", DocColumnKind.Text, 280f));
        columns.Add(new SystemColumnSpec(AssetsColumnMissing, "Missing", DocColumnKind.Checkbox, 90f));
        columns.Add(new SystemColumnSpec(AssetsColumnLastSeenUtc, "LastSeenUtc", DocColumnKind.Text, 200f));
        return columns;
    }

    private static DocDerivedConfig BuildTypedAssetsDerivedConfig(
        string assetsTableId,
        List<DocColumn> derivedColumns,
        string kind)
    {
        var derivedConfig = new DocDerivedConfig
        {
            BaseTableId = assetsTableId,
            FilterExpression = "thisRow.Kind == \"" + kind + "\"",
        };

        for (int columnIndex = 0; columnIndex < derivedColumns.Count; columnIndex++)
        {
            DocColumn column = derivedColumns[columnIndex];
            string sourceColumnId = string.Equals(column.Id, TypedPreviewColumn, StringComparison.Ordinal)
                ? AssetsColumnRelativePath
                : column.Id;
            string renameAlias = string.Equals(sourceColumnId, column.Id, StringComparison.Ordinal)
                ? ""
                : column.Name;
            derivedConfig.Projections.Add(new DerivedProjection
            {
                SourceTableId = assetsTableId,
                SourceColumnId = sourceColumnId,
                OutputColumnId = column.Id,
                RenameAlias = renameAlias,
            });
        }

        return derivedConfig;
    }

    private static DocTable GetOrCreateSystemTable(
        DocProject project,
        string systemKey,
        string tableName,
        string fileName,
        bool dataLocked)
    {
        DocTable? matchByKey = null;
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            DocTable candidateTable = project.Tables[tableIndex];
            if (!string.Equals(candidateTable.SystemKey, systemKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (matchByKey == null)
            {
                matchByKey = candidateTable;
                continue;
            }

            candidateTable.SystemKey = null;
            candidateTable.IsSystemSchemaLocked = false;
            candidateTable.IsSystemDataLocked = false;
        }

        DocTable table = matchByKey ?? FindLegacySystemTable(project, tableName, fileName) ?? CreateSystemTable(project);
        table.SystemKey = systemKey;
        table.IsSystemSchemaLocked = true;
        table.IsSystemDataLocked = dataLocked;
        table.Name = tableName;
        table.FileName = fileName;
        table.FolderId = null;
        table.SchemaSourceTableId = null;
        table.InheritanceSourceTableId = null;
        table.ParentTableId = null;
        table.ParentRowColumnId = null;
        return table;
    }

    private static DocTable? FindLegacySystemTable(DocProject project, string tableName, string fileName)
    {
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            DocTable candidateTable = project.Tables[tableIndex];
            if (!string.IsNullOrWhiteSpace(candidateTable.SystemKey))
            {
                continue;
            }

            if (candidateTable.IsSubtable)
            {
                continue;
            }

            if (string.Equals(candidateTable.FileName, fileName, StringComparison.OrdinalIgnoreCase))
            {
                return candidateTable;
            }

            if (string.Equals(candidateTable.Name, tableName, StringComparison.OrdinalIgnoreCase))
            {
                return candidateTable;
            }
        }

        return null;
    }

    private static DocTable? FindSystemTableByKey(DocProject project, string systemKey)
    {
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            DocTable table = project.Tables[tableIndex];
            if (string.Equals(table.SystemKey, systemKey, StringComparison.Ordinal))
            {
                return table;
            }
        }

        return null;
    }

    private static DocTable CreateSystemTable(DocProject project)
    {
        var table = new DocTable();
        project.Tables.Add(table);
        return table;
    }

    private static void ReconcileSystemTableColumns(DocTable table, IReadOnlyList<SystemColumnSpec> desiredColumns)
    {
        var existingById = new Dictionary<string, DocColumn>(table.Columns.Count, StringComparer.Ordinal);
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            DocColumn existingColumn = table.Columns[columnIndex];
            existingById[existingColumn.Id] = existingColumn;
        }

        var reconciledColumns = new List<DocColumn>(desiredColumns.Count);
        var defaultCellValueByColumnId = new Dictionary<string, DocCellValue>(desiredColumns.Count, StringComparer.Ordinal);
        var validColumnIds = new HashSet<string>(desiredColumns.Count, StringComparer.Ordinal);
        for (int specIndex = 0; specIndex < desiredColumns.Count; specIndex++)
        {
            SystemColumnSpec spec = desiredColumns[specIndex];
            validColumnIds.Add(spec.Id);

            if (!existingById.TryGetValue(spec.Id, out DocColumn? column))
            {
                column = new DocColumn { Id = spec.Id };
            }

            column.Name = spec.Name;
            column.Kind = spec.Kind;
            column.ColumnTypeId = DocColumnTypeIdMapper.FromKind(spec.Kind);
            column.Width = spec.Width;
            column.Options = spec.Options != null ? new List<string>(spec.Options) : null;
            column.FormulaExpression = "";
            column.RelationTableId = spec.Kind == DocColumnKind.Relation ? spec.RelationTableId : null;
            column.RelationTargetMode = spec.Kind == DocColumnKind.Relation ? spec.RelationTargetMode : DocRelationTargetMode.ExternalTable;
            column.RelationTableVariantId = spec.Kind == DocColumnKind.Relation ? spec.RelationTableVariantId : 0;
            column.RelationDisplayColumnId = spec.Kind == DocColumnKind.Relation ? spec.RelationDisplayColumnId : null;
            column.TableRefBaseTableId = spec.Kind == DocColumnKind.TableRef ? spec.TableRefBaseTableId : null;
            column.RowRefTableRefColumnId = null;
            column.IsHidden = spec.Hidden;
            column.IsProjected = false;
            column.ExportType = spec.ExportType;
            column.NumberMin = null;
            column.NumberMax = null;
            column.ExportEnumName = null;
            column.ExportIgnore = false;
            column.SubtableId = null;
            column.SubtableDisplayRendererId = null;
            column.SubtableDisplayCellWidth = null;
            column.SubtableDisplayCellHeight = null;
            column.SubtableDisplayPreviewQuality = null;
            column.FormulaEvalScopes = DocFormulaEvalScope.None;
            column.ModelPreviewSettings = null;
            column.PluginSettingsJson = null;
            reconciledColumns.Add(column);

            if (spec.DefaultNumberValue.HasValue && spec.Kind == DocColumnKind.Number)
            {
                defaultCellValueByColumnId[spec.Id] = DocCellValue.Number(spec.DefaultNumberValue.Value);
            }
        }

        table.Columns.Clear();
        table.Columns.AddRange(reconciledColumns);

        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            TrimRowCells(table.Rows[rowIndex], validColumnIds);
            EnsureRowHasDefaultCells(table.Rows[rowIndex], table.Columns, defaultCellValueByColumnId);
        }

        for (int variantDeltaIndex = 0; variantDeltaIndex < table.VariantDeltas.Count; variantDeltaIndex++)
        {
            DocTableVariantDelta variantDelta = table.VariantDeltas[variantDeltaIndex];
            for (int addedRowIndex = 0; addedRowIndex < variantDelta.AddedRows.Count; addedRowIndex++)
            {
                TrimRowCells(variantDelta.AddedRows[addedRowIndex], validColumnIds);
                EnsureRowHasDefaultCells(variantDelta.AddedRows[addedRowIndex], table.Columns, defaultCellValueByColumnId);
            }

            for (int cellOverrideIndex = variantDelta.CellOverrides.Count - 1; cellOverrideIndex >= 0; cellOverrideIndex--)
            {
                DocTableCellOverride cellOverride = variantDelta.CellOverrides[cellOverrideIndex];
                if (!validColumnIds.Contains(cellOverride.ColumnId))
                {
                    variantDelta.CellOverrides.RemoveAt(cellOverrideIndex);
                }
            }
        }
    }

    private static void TrimRowCells(DocRow row, HashSet<string> validColumnIds)
    {
        if (row.Cells.Count <= 0)
        {
            return;
        }

        var invalidColumnIds = new List<string>();
        foreach (var cellEntry in row.Cells)
        {
            if (!validColumnIds.Contains(cellEntry.Key))
            {
                invalidColumnIds.Add(cellEntry.Key);
            }
        }

        for (int invalidIndex = 0; invalidIndex < invalidColumnIds.Count; invalidIndex++)
        {
            row.Cells.Remove(invalidColumnIds[invalidIndex]);
        }
    }

    private static void EnsureRowHasDefaultCells(
        DocRow row,
        List<DocColumn> columns,
        Dictionary<string, DocCellValue>? defaultCellValueByColumnId = null)
    {
        for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            DocColumn column = columns[columnIndex];
            if (!row.Cells.ContainsKey(column.Id))
            {
                if (defaultCellValueByColumnId != null &&
                    defaultCellValueByColumnId.TryGetValue(column.Id, out DocCellValue configuredDefault))
                {
                    row.Cells[column.Id] = configuredDefault;
                }
                else
                {
                    row.Cells[column.Id] = DocCellValue.Default(column);
                }
            }
        }
    }

    private static void ReconcileAssetsRows(DocTable assetsTable, string? assetsRoot, string? resourcesRoot)
    {
        var columnById = BuildColumnById(assetsTable);
        var rows = assetsTable.Rows;
        EnsureUniqueAssetIds(rows, columnById);
        RemoveUnsupportedAssetRows(rows);

        var discoveredAssets = ScanAssets(assetsRoot, resourcesRoot);
        var rowByPath = new Dictionary<string, DocRow>(StringComparer.OrdinalIgnoreCase);
        var rowsByHash = new Dictionary<string, Queue<DocRow>>(StringComparer.Ordinal);
        var rowUsed = new HashSet<string>(StringComparer.Ordinal);

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            DocRow row = rows[rowIndex];
            string relativePath = GetStringCellValue(row, AssetsColumnRelativePath);
            if (!string.IsNullOrWhiteSpace(relativePath) && !rowByPath.ContainsKey(relativePath))
            {
                rowByPath[relativePath] = row;
            }

            string contentHash = GetStringCellValue(row, AssetsColumnContentHash);
            if (string.IsNullOrWhiteSpace(contentHash))
            {
                continue;
            }

            if (!rowsByHash.TryGetValue(contentHash, out Queue<DocRow>? queue))
            {
                queue = new Queue<DocRow>();
                rowsByHash[contentHash] = queue;
            }

            queue.Enqueue(row);
        }

        int nextAssetId = GetNextAssetId(rows, columnById);
        string lastSeenUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        for (int assetIndex = 0; assetIndex < discoveredAssets.Count; assetIndex++)
        {
            AssetScanEntry discoveredAsset = discoveredAssets[assetIndex];
            DocRow row = ResolveAssetRow(
                rows,
                discoveredAsset,
                rowByPath,
                rowsByHash,
                rowUsed,
                ref nextAssetId,
                columnById);

            rowUsed.Add(row.Id);
            SetStringCellValue(row, AssetsColumnRelativePath, discoveredAsset.RelativePath);
            SetStringCellValue(row, AssetsColumnExtension, discoveredAsset.Extension);
            SetStringCellValue(row, AssetsColumnKind, discoveredAsset.Kind);
            SetNumberCellValue(row, AssetsColumnSizeBytes, discoveredAsset.SizeBytes);
            SetStringCellValue(row, AssetsColumnContentHash, discoveredAsset.ContentHash);
            SetBoolCellValue(row, AssetsColumnMissing, false);
            SetStringCellValue(row, AssetsColumnLastSeenUtc, lastSeenUtc);
        }

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            DocRow row = rows[rowIndex];
            if (rowUsed.Contains(row.Id))
            {
                continue;
            }

            SetBoolCellValue(row, AssetsColumnMissing, true);
        }

        NormalizeAssetNames(rows);
        rows.Sort(CompareAssetsRows);
    }

    private static void RemoveUnsupportedAssetRows(List<DocRow> rows)
    {
        for (int rowIndex = rows.Count - 1; rowIndex >= 0; rowIndex--)
        {
            DocRow row = rows[rowIndex];
            string extension = GetStringCellValue(row, AssetsColumnExtension);
            if (string.IsNullOrWhiteSpace(extension))
            {
                string relativePath = GetStringCellValue(row, AssetsColumnRelativePath);
                extension = Path.GetExtension(relativePath);
            }

            string normalizedExtension = string.IsNullOrWhiteSpace(extension)
                ? ""
                : extension.ToLowerInvariant();

            if (!IsSupportedAssetExtension(normalizedExtension))
            {
                rows.RemoveAt(rowIndex);
            }
        }
    }

    private static Dictionary<string, DocColumn> BuildColumnById(DocTable table)
    {
        var columnById = new Dictionary<string, DocColumn>(table.Columns.Count, StringComparer.Ordinal);
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            DocColumn column = table.Columns[columnIndex];
            columnById[column.Id] = column;
        }

        return columnById;
    }

    private static void EnsureUniqueAssetIds(List<DocRow> rows, Dictionary<string, DocColumn> columnById)
    {
        var usedIds = new HashSet<int>();
        int maxId = 0;
        var rowsNeedingId = new List<DocRow>();

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            DocRow row = rows[rowIndex];
            int assetId = (int)Math.Round(GetNumberCellValue(row, AssetsColumnAssetId));
            if (assetId > 0 && !usedIds.Contains(assetId))
            {
                usedIds.Add(assetId);
                if (assetId > maxId)
                {
                    maxId = assetId;
                }
            }
            else
            {
                rowsNeedingId.Add(row);
            }

            EnsureRowHasDefaultCells(row, BuildColumnList(columnById));
        }

        for (int rowIndex = 0; rowIndex < rowsNeedingId.Count; rowIndex++)
        {
            maxId++;
            SetNumberCellValue(rowsNeedingId[rowIndex], AssetsColumnAssetId, maxId);
        }
    }

    private static List<DocColumn> BuildColumnList(Dictionary<string, DocColumn> columnById)
    {
        var columns = new List<DocColumn>(columnById.Count);
        foreach (var entry in columnById)
        {
            columns.Add(entry.Value);
        }

        return columns;
    }

    private static int GetNextAssetId(List<DocRow> rows, Dictionary<string, DocColumn> columnById)
    {
        int nextId = 1;
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            DocRow row = rows[rowIndex];
            int current = (int)Math.Round(GetNumberCellValue(row, AssetsColumnAssetId));
            if (current >= nextId)
            {
                nextId = current + 1;
            }

            EnsureRowHasDefaultCells(row, BuildColumnList(columnById));
        }

        return nextId;
    }

    private static DocRow ResolveAssetRow(
        List<DocRow> rows,
        AssetScanEntry discoveredAsset,
        Dictionary<string, DocRow> rowByPath,
        Dictionary<string, Queue<DocRow>> rowsByHash,
        HashSet<string> rowUsed,
        ref int nextAssetId,
        Dictionary<string, DocColumn> columnById)
    {
        if (rowByPath.TryGetValue(discoveredAsset.RelativePath, out DocRow? matchedByPath) &&
            !rowUsed.Contains(matchedByPath.Id))
        {
            return matchedByPath;
        }

        if (!string.IsNullOrWhiteSpace(discoveredAsset.ContentHash) &&
            rowsByHash.TryGetValue(discoveredAsset.ContentHash, out Queue<DocRow>? matchedRows))
        {
            while (matchedRows.Count > 0)
            {
                DocRow candidateRow = matchedRows.Dequeue();
                if (!rowUsed.Contains(candidateRow.Id))
                {
                    return candidateRow;
                }
            }
        }

        var newRow = new DocRow();
        EnsureRowHasDefaultCells(newRow, BuildColumnList(columnById));
        SetNumberCellValue(newRow, AssetsColumnAssetId, nextAssetId);
        nextAssetId++;
        rows.Add(newRow);
        return newRow;
    }

    private static List<AssetScanEntry> ScanAssets(string? assetsRoot, string? resourcesRoot)
    {
        var entries = new List<AssetScanEntry>();
        ScanAssetsFromRoot(entries, assetsRoot, relativePrefix: "", includeOnlyUiAssets: false);
        ScanAssetsFromRoot(entries, resourcesRoot, ResourcesRelativePrefix, includeOnlyUiAssets: true);
        entries.Sort(static (left, right) =>
            string.Compare(left.RelativePath, right.RelativePath, StringComparison.OrdinalIgnoreCase));
        return entries;
    }

    private static void ScanAssetsFromRoot(
        List<AssetScanEntry> destinationEntries,
        string? rootPath,
        string relativePrefix,
        bool includeOnlyUiAssets)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return;
        }

        string fullRootPath = Path.GetFullPath(rootPath);
        if (!Directory.Exists(fullRootPath))
        {
            return;
        }

        string[] filePaths;
        try
        {
            filePaths = Directory.GetFiles(fullRootPath, "*", SearchOption.AllDirectories);
        }
        catch
        {
            return;
        }

        for (int fileIndex = 0; fileIndex < filePaths.Length; fileIndex++)
        {
            string fullPath = filePaths[fileIndex];
            string extension = Path.GetExtension(fullPath);
            string normalizedExtension = string.IsNullOrWhiteSpace(extension)
                ? ""
                : extension.ToLowerInvariant();

            if (includeOnlyUiAssets)
            {
                if (!ContainsExtension(UiExtensions, normalizedExtension))
                {
                    continue;
                }
            }
            else if (!IsSupportedAssetExtension(normalizedExtension))
            {
                continue;
            }

            string kind = ResolveAssetKind(normalizedExtension);
            if (string.Equals(kind, "Other", StringComparison.Ordinal))
            {
                continue;
            }

            long sizeBytes;
            try
            {
                sizeBytes = new FileInfo(fullPath).Length;
            }
            catch
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(fullRootPath, fullPath).Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(relativePrefix))
            {
                relativePath = relativePrefix + relativePath;
            }

            string contentHash = ComputeContentHash(fullPath);
            destinationEntries.Add(new AssetScanEntry(relativePath, normalizedExtension, sizeBytes, contentHash, kind));
        }
    }

    private static bool IsSupportedAssetExtension(string extension)
    {
        return !string.Equals(ResolveAssetKind(extension), "Other", StringComparison.Ordinal);
    }

    private static string ResolveAssetKind(string extension)
    {
        if (ContainsExtension(TextureExtensions, extension))
        {
            return "Texture";
        }

        if (ContainsExtension(ModelExtensions, extension))
        {
            return "Model";
        }

        if (ContainsExtension(AudioExtensions, extension))
        {
            return "Audio";
        }

        if (ContainsExtension(MaterialExtensions, extension))
        {
            return "Material";
        }

        if (ContainsExtension(UiExtensions, extension))
        {
            return "Ui";
        }

        return "Other";
    }

    private static bool ContainsExtension(string[] extensions, string extension)
    {
        for (int extensionIndex = 0; extensionIndex < extensions.Length; extensionIndex++)
        {
            if (string.Equals(extensions[extensionIndex], extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ComputeContentHash(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(stream);
            return Convert.ToHexString(hash);
        }
        catch
        {
            return "";
        }
    }

    private static void NormalizeAssetNames(List<DocRow> rows)
    {
        var orderedRows = new List<DocRow>(rows);
        orderedRows.Sort(static (left, right) =>
        {
            string leftPath = GetStringCellValue(left, AssetsColumnRelativePath);
            string rightPath = GetStringCellValue(right, AssetsColumnRelativePath);
            int byPath = string.Compare(leftPath, rightPath, StringComparison.OrdinalIgnoreCase);
            if (byPath != 0)
            {
                return byPath;
            }

            int leftAssetId = (int)Math.Round(GetNumberCellValue(left, AssetsColumnAssetId));
            int rightAssetId = (int)Math.Round(GetNumberCellValue(right, AssetsColumnAssetId));
            return leftAssetId.CompareTo(rightAssetId);
        });

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int rowIndex = 0; rowIndex < orderedRows.Count; rowIndex++)
        {
            DocRow row = orderedRows[rowIndex];
            string relativePath = GetStringCellValue(row, AssetsColumnRelativePath);
            string baseName = DeriveAssetName(relativePath);
            string candidateName = baseName;
            int suffix = 2;
            while (usedNames.Contains(candidateName))
            {
                candidateName = baseName + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            usedNames.Add(candidateName);
            SetStringCellValue(row, AssetsColumnName, candidateName);
        }
    }

    private static string DeriveAssetName(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return "asset";
        }

        var builder = new StringBuilder(relativePath.Length + 8);
        bool previousUnderscore = false;
        for (int charIndex = 0; charIndex < relativePath.Length; charIndex++)
        {
            char value = relativePath[charIndex];
            bool keepCharacter = char.IsLetterOrDigit(value) || value == '_';
            if (keepCharacter)
            {
                builder.Append(value);
                previousUnderscore = false;
                continue;
            }

            if (!previousUnderscore)
            {
                builder.Append('_');
                previousUnderscore = true;
            }
        }

        string name = builder.ToString().Trim('_');
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "asset";
        }

        if (!char.IsLetter(name[0]) && name[0] != '_')
        {
            name = "_" + name;
        }

        return name;
    }

    private static int CompareAssetsRows(DocRow left, DocRow right)
    {
        int leftAssetId = (int)Math.Round(GetNumberCellValue(left, AssetsColumnAssetId));
        int rightAssetId = (int)Math.Round(GetNumberCellValue(right, AssetsColumnAssetId));
        int byAssetId = leftAssetId.CompareTo(rightAssetId);
        if (byAssetId != 0)
        {
            return byAssetId;
        }

        string leftPath = GetStringCellValue(left, AssetsColumnRelativePath);
        string rightPath = GetStringCellValue(right, AssetsColumnRelativePath);
        return string.Compare(leftPath, rightPath, StringComparison.OrdinalIgnoreCase);
    }

    private static void ReconcilePackagesRows(DocTable packagesTable)
    {
        var usedPackageIds = new HashSet<int>();
        int maxPackageId = 0;
        var rowsNeedingId = new List<DocRow>();
        for (int rowIndex = 0; rowIndex < packagesTable.Rows.Count; rowIndex++)
        {
            DocRow row = packagesTable.Rows[rowIndex];
            int packageId = (int)Math.Round(GetNumberCellValue(row, PackagesColumnPackageId));
            if (packageId > 0 && !usedPackageIds.Contains(packageId))
            {
                usedPackageIds.Add(packageId);
                maxPackageId = Math.Max(maxPackageId, packageId);
            }
            else
            {
                rowsNeedingId.Add(row);
            }

            string defaultLoadFrom = GetStringCellValue(row, PackagesColumnDefaultLoadFrom);
            if (string.IsNullOrWhiteSpace(defaultLoadFrom))
            {
                SetStringCellValue(row, PackagesColumnDefaultLoadFrom, "Disk");
            }
        }

        for (int rowIndex = 0; rowIndex < rowsNeedingId.Count; rowIndex++)
        {
            maxPackageId++;
            SetNumberCellValue(rowsNeedingId[rowIndex], PackagesColumnPackageId, maxPackageId);
        }
    }

    private static void ReconcileExportsRows(DocTable exportsTable, DocTable packagesTable, DocTable assetsTable)
    {
        Dictionary<int, DocRow> packageRowById = BuildRowByNumericId(packagesTable, PackagesColumnPackageId);
        Dictionary<int, DocRow> assetRowById = BuildRowByNumericId(assetsTable, AssetsColumnAssetId);
        var seenPairs = new HashSet<string>(StringComparer.Ordinal);

        for (int rowIndex = exportsTable.Rows.Count - 1; rowIndex >= 0; rowIndex--)
        {
            DocRow row = exportsTable.Rows[rowIndex];

            NormalizeRelationCellFromLegacyNumericId(row, ExportsColumnPackageId, packageRowById);
            NormalizeRelationCellFromLegacyNumericId(row, ExportsColumnAssetId, assetRowById);

            string packageRowId = GetStringCellValue(row, ExportsColumnPackageId);
            string assetRowId = GetStringCellValue(row, ExportsColumnAssetId);
            if (!string.IsNullOrWhiteSpace(packageRowId) && !string.IsNullOrWhiteSpace(assetRowId))
            {
                string pairKey = packageRowId + "|" + assetRowId;
                if (seenPairs.Contains(pairKey))
                {
                    exportsTable.Rows.RemoveAt(rowIndex);
                    continue;
                }

                seenPairs.Add(pairKey);
            }

            if (!row.Cells.ContainsKey(ExportsColumnEnabled))
            {
                SetBoolCellValue(row, ExportsColumnEnabled, true);
            }

            string loadFromOverride = GetStringCellValue(row, ExportsColumnLoadFromOverride);
            if (string.IsNullOrWhiteSpace(loadFromOverride))
            {
                SetStringCellValue(row, ExportsColumnLoadFromOverride, "Default");
            }
        }
    }

    private static Dictionary<int, DocRow> BuildRowByNumericId(DocTable table, string numericIdColumnId)
    {
        var rowByNumericId = new Dictionary<int, DocRow>();
        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            DocRow row = table.Rows[rowIndex];
            int numericId = TryReadLegacyNumericId(row, numericIdColumnId);
            if (numericId <= 0 || rowByNumericId.ContainsKey(numericId))
            {
                continue;
            }

            rowByNumericId[numericId] = row;
        }

        return rowByNumericId;
    }

    private static void NormalizeRelationCellFromLegacyNumericId(
        DocRow row,
        string relationColumnId,
        Dictionary<int, DocRow> rowByNumericId)
    {
        string relationRowId = GetStringCellValue(row, relationColumnId);
        if (!string.IsNullOrWhiteSpace(relationRowId))
        {
            return;
        }

        int legacyNumericId = TryReadLegacyNumericId(row, relationColumnId);
        if (legacyNumericId <= 0 || !rowByNumericId.TryGetValue(legacyNumericId, out DocRow? targetRow))
        {
            return;
        }

        SetStringCellValue(row, relationColumnId, targetRow.Id);
    }

    private static int TryReadLegacyNumericId(DocRow row, string columnId)
    {
        if (!row.Cells.TryGetValue(columnId, out DocCellValue value))
        {
            return 0;
        }

        int numericFromNumber = (int)Math.Round(value.NumberValue);
        if (numericFromNumber > 0)
        {
            return numericFromNumber;
        }

        string rawString = value.StringValue ?? "";
        if (int.TryParse(rawString, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numericFromText) &&
            numericFromText > 0)
        {
            return numericFromText;
        }

        return 0;
    }

    private static void ReconcileAssetDependencyRows(DocTable assetDepsTable)
    {
        assetDepsTable.Rows.Clear();
    }

    private static void MaterializeDerivedSystemTableRows(DocProject project, DocTable table)
    {
        if (table.DerivedConfig == null)
        {
            return;
        }

        var formulaContext = new ProjectFormulaContext(project);
        DerivedMaterializeResult materializeResult = DerivedResolver.Materialize(table, formulaContext);
        table.Rows.Clear();
        for (int rowIndex = 0; rowIndex < materializeResult.Rows.Count; rowIndex++)
        {
            table.Rows.Add(materializeResult.Rows[rowIndex]);
        }
    }

    private static string? ResolveAssetsRoot(string dbRoot)
    {
        if (DocProjectPaths.TryGetGameRootFromDbRoot(dbRoot, out string gameRoot))
        {
            return Path.Combine(gameRoot, "Assets");
        }

        string localAssetsRoot = Path.Combine(dbRoot, "Assets");
        if (Directory.Exists(localAssetsRoot))
        {
            return localAssetsRoot;
        }

        string? parentDir = Directory.GetParent(dbRoot)?.FullName;
        if (!string.IsNullOrWhiteSpace(parentDir))
        {
            string siblingAssetsRoot = Path.Combine(parentDir, "Assets");
            if (Directory.Exists(siblingAssetsRoot))
            {
                return siblingAssetsRoot;
            }
        }

        return null;
    }

    private static string? ResolveResourcesRoot(string dbRoot)
    {
        if (DocProjectPaths.TryGetGameRootFromDbRoot(dbRoot, out string gameRoot))
        {
            return Path.Combine(gameRoot, "Resources");
        }

        string localResourcesRoot = Path.Combine(dbRoot, "Resources");
        if (Directory.Exists(localResourcesRoot))
        {
            return localResourcesRoot;
        }

        string? parentDir = Directory.GetParent(dbRoot)?.FullName;
        if (!string.IsNullOrWhiteSpace(parentDir))
        {
            string siblingResourcesRoot = Path.Combine(parentDir, "Resources");
            if (Directory.Exists(siblingResourcesRoot))
            {
                return siblingResourcesRoot;
            }
        }

        return null;
    }

    private static string GetStringCellValue(DocRow row, string columnId)
    {
        if (row.Cells.TryGetValue(columnId, out DocCellValue value))
        {
            return value.StringValue ?? "";
        }

        return "";
    }

    private static void SetStringCellValue(DocRow row, string columnId, string value)
    {
        row.Cells[columnId] = DocCellValue.Text(value);
    }

    private static double GetNumberCellValue(DocRow row, string columnId)
    {
        if (row.Cells.TryGetValue(columnId, out DocCellValue value))
        {
            return value.NumberValue;
        }

        return 0;
    }

    private static void SetNumberCellValue(DocRow row, string columnId, double value)
    {
        row.Cells[columnId] = DocCellValue.Number(value);
    }

    private static bool GetBoolCellValue(DocRow row, string columnId)
    {
        if (row.Cells.TryGetValue(columnId, out DocCellValue value))
        {
            return value.BoolValue;
        }

        return false;
    }

    private static void SetBoolCellValue(DocRow row, string columnId, bool value)
    {
        row.Cells[columnId] = DocCellValue.Bool(value);
    }
}
